#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ProjectWeapon.Accounts;
using ProjectWeapon.Ad;
using Unity.Services.LevelPlay;

[assembly: InternalsVisibleTo("ProjectWeapon.LevelPlayTest.Tests")]

namespace ProjectWeapon.LevelPlayTest
{
    public enum LevelPlayTestReceiptStatus { Pending, Rejected, Confirmed }

    public readonly struct LevelPlayTestReceipt
    {
        public LevelPlayTestReceipt(Guid requestId, string reservationToken,
            LevelPlayTestReceiptStatus status, string? completionId = null)
        {
            RequestId = requestId;
            ReservationToken = reservationToken;
            Status = status;
            CompletionId = completionId;
        }

        public Guid RequestId { get; }
        public string ReservationToken { get; }
        public LevelPlayTestReceiptStatus Status { get; }
        public string? CompletionId { get; }
    }

    public readonly struct LevelPlayTestPresentation
    {
        public LevelPlayTestPresentation(Guid requestId, string reservationToken, TimeSpan remaining)
        {
            RequestId = requestId;
            ReservationToken = reservationToken;
            Remaining = remaining;
        }

        public Guid RequestId { get; }
        public string ReservationToken { get; }
        public TimeSpan Remaining { get; }
    }

    /// <summary>The authenticated test host authorizes presentation and reads its durable receipt.</summary>
    public interface ILevelPlayTestAuthority
    {
        Task<LevelPlayTestPresentation> AuthorizeAsync(OnlineAccountSession session, Guid requestId);
        Task<LevelPlayTestReceipt> ReadAsync(OnlineAccountSession session, Guid requestId, string reservationToken);
    }

    /// <summary>Owns one mobile test presentation on the Unity thread. Only the authority can confirm a reward.</summary>
    public sealed class LevelPlayTestRewardedAdapter : IRewardedAdAdapter, IDisposable
    {
        private enum Phase { Loading, Ready, Showing }
        private const double LoadTimeoutSeconds = 45;
        private const double ShowTimeoutSeconds = 240;

        private readonly OnlineAccountSession session;
        private readonly Func<bool> isCurrent;
        private readonly Func<ILevelPlayRewardedAd> createAd;
        private readonly Func<string, bool> setReservationToken;
        private readonly ILevelPlayTestAuthority authority;
        private readonly Func<double> monotonicSeconds;
        private readonly Func<string, bool> consumeToken;
        private readonly Action? onDispose;
        private readonly HashSet<Guid> started = new HashSet<Guid>();
        private readonly Dictionary<Guid, Attempt> pending = new Dictionary<Guid, Attempt>();
        private readonly string? placement;
        private Attempt? active;
        private bool disposed;

        internal LevelPlayTestRewardedAdapter(OnlineAccountSession session, Func<bool> isCurrent,
            Func<ILevelPlayRewardedAd> createAd, Func<string, bool> setReservationToken,
            ILevelPlayTestAuthority authority, string? placement, Func<double> monotonicSeconds,
            Func<string, bool> consumeToken, Action? onDispose = null)
        {
            this.session = session;
            this.isCurrent = isCurrent;
            this.createAd = createAd;
            this.setReservationToken = setReservationToken;
            this.authority = authority;
            this.monotonicSeconds = monotonicSeconds;
            this.consumeToken = consumeToken;
            this.onDispose = onDispose;
            this.placement = placement;
        }

        public event Action<RewardedAdResult>? OutcomeReported;
        public event Action<Guid>? Loaded;
        public event Action<Guid>? AwaitingVerification;

        public bool TryBegin(Guid requestId)
        {
            if (disposed || !isCurrent() || requestId == Guid.Empty || active != null || !started.Add(requestId))
                return false;

            var attempt = new Attempt(requestId, createAd()) { PhaseBeganAt = monotonicSeconds() };
            active = attempt;
            attempt.OnLoaded = _ =>
            {
                if (!IsActive(attempt, Phase.Loading))
                    return;
                attempt.Phase = Phase.Ready;
                Loaded?.Invoke(requestId);
            };
            attempt.OnLoadFailed = _ =>
            {
                if (IsActive(attempt, Phase.Loading))
                    Finish(attempt, RewardedAdOutcome.LoadFailed);
            };
            attempt.OnDisplayFailed = (_, _) =>
            {
                if (IsActive(attempt, Phase.Showing))
                    Finish(attempt, RewardedAdOutcome.Error);
            };
            attempt.OnClosed = _ =>
            {
                if (!IsActive(attempt, Phase.Showing))
                    return;
                BeginReceiptWait(attempt);
            };

            attempt.Ad.OnAdLoaded += attempt.OnLoaded;
            attempt.Ad.OnAdLoadFailed += attempt.OnLoadFailed;
            attempt.Ad.OnAdDisplayFailed += attempt.OnDisplayFailed;
            attempt.Ad.OnAdClosed += attempt.OnClosed;
            attempt.Ad.LoadAd();
            return true;
        }

        /// <summary>Shows a loaded ad only after the current account receives a fresh presentation authorization.</summary>
        public async Task<bool> TryShowAsync(Guid requestId)
        {
            if (active is not { } attempt || attempt.Id != requestId || !IsActive(attempt, Phase.Ready)
                || attempt.Authorizing)
                return false;

            attempt.Authorizing = true;
            double startedAt = monotonicSeconds();
            try
            {
                LevelPlayTestPresentation permit = await authority.AuthorizeAsync(session, requestId);
                double elapsed = monotonicSeconds() - startedAt;
                if (!IsActive(attempt, Phase.Ready) || permit.RequestId != requestId
                    || !IsReservationToken(permit.ReservationToken) || !double.IsFinite(elapsed) || elapsed < 0
                    || elapsed >= permit.Remaining.TotalSeconds || !consumeToken(permit.ReservationToken))
                    return false;

                if (!attempt.Ad.IsAdReady() || !setReservationToken(permit.ReservationToken))
                {
                    Finish(attempt, RewardedAdOutcome.Error);
                    return false;
                }

                attempt.ReservationToken = permit.ReservationToken;
                attempt.Phase = Phase.Showing;
                attempt.PhaseBeganAt = monotonicSeconds();
                attempt.Ad.ShowAd(placement);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                attempt.Authorizing = false;
            }
        }

        public bool TryCancel(Guid requestId)
        {
            if (active is not { } attempt || attempt.Id != requestId
                || (!IsActive(attempt, Phase.Loading) && !IsActive(attempt, Phase.Ready)))
                return false;
            Finish(attempt, RewardedAdOutcome.Cancelled);
            return true;
        }

        /// <summary>Advances recovery on the Unity thread if the SDK supplies no callback.</summary>
        public bool RecoverTimedOutPresentation()
        {
            if (active is not { } attempt || !IsActive(attempt, attempt.Phase))
                return false;

            double elapsed = monotonicSeconds() - attempt.PhaseBeganAt;
            if (!double.IsFinite(elapsed))
                return false;
            if (attempt.Phase == Phase.Loading && elapsed >= LoadTimeoutSeconds)
                return Finish(attempt, RewardedAdOutcome.LoadFailed);
            if (attempt.Phase != Phase.Showing || elapsed < ShowTimeoutSeconds)
                return false;

            BeginReceiptWait(attempt);
            return true;
        }

        /// <summary>Queries the current session's host when the native presentation is over.</summary>
        public async Task<bool> CheckForReceiptAsync(Guid requestId)
        {
            if (!pending.TryGetValue(requestId, out Attempt attempt)
                || !IsAwaiting(attempt) || attempt.Checking)
                return false;

            attempt.Checking = true;
            try
            {
                LevelPlayTestReceipt receipt = await authority.ReadAsync(session, requestId, attempt.ReservationToken);
                if (!IsAwaiting(attempt) || receipt.RequestId != requestId
                    || !StringComparer.Ordinal.Equals(receipt.ReservationToken, attempt.ReservationToken))
                    return false;

                return receipt.Status switch
                {
                    LevelPlayTestReceiptStatus.Confirmed => Finish(attempt, RewardedAdOutcome.ConfirmedCompletion,
                        receipt.CompletionId),
                    LevelPlayTestReceiptStatus.Rejected => Finish(attempt, RewardedAdOutcome.ClosedWithoutConfirmation),
                    _ => false
                };
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                attempt.Checking = false;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            pending.Clear();
            try
            {
                if (active is { } attempt)
                    Drop(attempt);
            }
            finally
            {
                onDispose?.Invoke();
            }
        }

        private bool IsActive(Attempt attempt, Phase phase)
            => !disposed && ReferenceEquals(attempt, active) && attempt.Phase == phase && isCurrent();

        private void BeginReceiptWait(Attempt attempt)
        {
            pending.Add(attempt.Id, attempt);
            Drop(attempt);
            AwaitingVerification?.Invoke(attempt.Id);
        }

        private bool IsAwaiting(Attempt attempt) => !disposed && isCurrent()
            && pending.TryGetValue(attempt.Id, out Attempt current) && ReferenceEquals(attempt, current);

        private bool Finish(Attempt attempt, RewardedAdOutcome outcome, string? completionId = null)
        {
            if (!RewardedAdResult.TryCreate(attempt.Id, outcome, completionId, out RewardedAdResult result))
                return false;
            if (ReferenceEquals(active, attempt))
                Drop(attempt);
            else
                pending.Remove(attempt.Id);
            OutcomeReported?.Invoke(result);
            return true;
        }

        private void Drop(Attempt attempt)
        {
            active = null;
            attempt.Ad.OnAdLoaded -= attempt.OnLoaded;
            attempt.Ad.OnAdLoadFailed -= attempt.OnLoadFailed;
            attempt.Ad.OnAdDisplayFailed -= attempt.OnDisplayFailed;
            attempt.Ad.OnAdClosed -= attempt.OnClosed;
            attempt.Ad.Dispose();
        }

        internal static bool IsReservationToken(string? value)
        {
            if (value is null || value.Length == 0 || value.Length > 64)
                return false;
            foreach (char c in value)
                if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z'))
                    return false;
            return true;
        }

        private sealed class Attempt
        {
            public Attempt(Guid id, ILevelPlayRewardedAd ad)
            {
                Id = id;
                Ad = ad;
            }

            public Guid Id { get; }
            public ILevelPlayRewardedAd Ad { get; }
            public Phase Phase { get; set; }
            public double PhaseBeganAt { get; set; }
            public string ReservationToken { get; set; } = string.Empty;
            public bool Checking { get; set; }
            public bool Authorizing { get; set; }
            public Action<LevelPlayAdInfo> OnLoaded = null!;
            public Action<LevelPlayAdError> OnLoadFailed = null!;
            public Action<LevelPlayAdInfo, LevelPlayAdError> OnDisplayFailed = null!;
            public Action<LevelPlayAdInfo> OnClosed = null!;
        }
    }
}
