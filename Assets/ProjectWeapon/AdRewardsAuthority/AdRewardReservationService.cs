#nullable enable

using System;
using System.Linq;

namespace ProjectWeapon.AdRewards
{
    /// <summary>Only the authenticated host supplies the PlayerID and service scope.</summary>
    public interface IAdRewardReservationIdentity
    {
        string? VerifiedPlayerId { get; }
        string AuthorityScope { get; }
    }

    public enum AdRewardPresentationEnd
    {
        AwaitingProof = 1,
        ConfirmedNoExposure
    }

    public readonly struct VerifiedAdRewardPresentationEnd
    {
        public VerifiedAdRewardPresentationEnd(string authorityScope, string accountId, string reservationToken,
            AdRewardPresentationEnd end)
        {
            AuthorityScope = authorityScope;
            AccountId = accountId;
            ReservationToken = reservationToken;
            End = end;
        }

        public string AuthorityScope { get; }
        public string AccountId { get; }
        public string ReservationToken { get; }
        public AdRewardPresentationEnd End { get; }
    }

    /// <summary>Verifies a trusted host disposition bound to this exact presentation. An app close is no proof of non-exposure.</summary>
    public interface IAdRewardPresentationEndVerifier
    {
        bool TryVerify(string evidence, out VerifiedAdRewardPresentationEnd presentation);
    }

    public enum AdRewardReservationStatus
    {
        Unauthorized,
        InvalidRequest,
        AdNotReady,
        RetryRequired,
        Reserved,
        AlreadyReserved,
        ViewingInProgress,
        DailyLimitReached,
        UnverifiedPresentation,
        MissingReservation,
        AlreadyGranted,
        ExpiredReservation,
        AwaitingProof,
        Released
    }

    public readonly struct AdRewardReservationResult
    {
        internal AdRewardReservationResult(AdRewardReservationStatus status, AdRewardReservation? reservation = null)
        {
            Status = status;
            Reservation = reservation;
        }

        public AdRewardReservationStatus Status { get; }
        public AdRewardReservation? Reservation { get; }

        /// <summary>Checks whether a fresh show can finish within the lease using trusted UTC and a provider-verified maximum.</summary>
        public bool AuthorizesPresentationAt(DateTime authorityUtc, TimeSpan verifiedMaximumDisplayDuration) =>
            Status == AdRewardReservationStatus.Reserved && Reservation is { } reservation
            && authorityUtc.Kind == DateTimeKind.Utc && authorityUtc >= reservation.CreatedAtUtc
            && verifiedMaximumDisplayDuration > TimeSpan.Zero
            && verifiedMaximumDisplayDuration <= reservation.ViewUntilUtc - authorityUtc;
    }

    public readonly struct AdRewardDailyAvailability
    {
        internal AdRewardDailyAvailability(DateTime utcDay, int confirmedUses, int heldSlots, bool viewing)
        {
            UtcDay = utcDay;
            ConfirmedUses = confirmedUses;
            HeldSlots = heldSlots;
            ViewingInProgress = viewing;
        }

        public DateTime UtcDay { get; }
        public DateTime NextResetUtc => UtcDay.AddDays(1);
        public int ConfirmedUses { get; }
        public int HeldSlots { get; }
        public int RemainingSlots => Math.Max(0, AdRewardReservationService.DailyLimit - ConfirmedUses - HeldSlots);
        public bool ViewingInProgress { get; }
    }

    /// <summary>Server-side UTC admission, reconciliation and quota reads, sharing a revision with reward grants.</summary>
    public sealed class AdRewardReservationService
    {
        public const int DailyLimit = 2;
        public static readonly TimeSpan DefaultProofWindow = TimeSpan.FromDays(9);
        public static readonly TimeSpan DefaultViewLease = TimeSpan.FromMinutes(5);
        private const int MaxAttempts = 4;

        private readonly string scope;
        private readonly IAdRewardReservationStore store;
        private readonly IAdRewardPresentationEndVerifier verifier;
        private readonly Func<DateTime> utcNow;
        private readonly TimeSpan proofWindow;
        private readonly TimeSpan viewLease;

        public AdRewardReservationService(string scope, IAdRewardReservationStore store,
            IAdRewardPresentationEndVerifier verifier, Func<DateTime> utcNow, TimeSpan? proofWindow = null,
            TimeSpan? viewLease = null)
        {
            if (string.IsNullOrWhiteSpace(scope))
                throw new ArgumentException("An authority scope is required.", nameof(scope));

            this.scope = scope;
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.proofWindow = proofWindow ?? DefaultProofWindow;
            if (this.proofWindow <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(proofWindow));
            this.viewLease = viewLease ?? DefaultViewLease;
            if (this.viewLease <= TimeSpan.Zero || this.viewLease > this.proofWindow)
                throw new ArgumentOutOfRangeException(nameof(viewLease));
        }

        public AdRewardDailyAvailability? ReadAvailability(IAdRewardReservationIdentity identity)
        {
            string? playerId = GetAccount(identity);
            return playerId != null && TryGetUtc(out DateTime now)
                ? Count(ReadScope(), playerId, now)
                : null;
        }

        /// <summary>Only a Reserved result authorizes starting a new presentation. A replay returns the original token.</summary>
        public AdRewardReservationResult TryReserve(IAdRewardReservationIdentity identity, Guid requestId, bool adReady)
        {
            string? playerId = GetAccount(identity);
            if (playerId == null)
                return new AdRewardReservationResult(AdRewardReservationStatus.Unauthorized);
            if (requestId == Guid.Empty)
                return new AdRewardReservationResult(AdRewardReservationStatus.InvalidRequest);

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                AdRewardAuthoritySnapshot snapshot = ReadScope();
                AdRewardReservation? original = snapshot.Reservations.FirstOrDefault(item =>
                    item.AccountId == playerId && item.RequestId == requestId);
                if (original != null)
                    return new AdRewardReservationResult(AdRewardReservationStatus.AlreadyReserved, original);
                if (!adReady)
                    return new AdRewardReservationResult(AdRewardReservationStatus.AdNotReady);
                if (!TryGetUtc(out DateTime now) || proofWindow.Ticks > DateTime.MaxValue.Ticks - now.Ticks)
                    break;

                AdRewardDailyAvailability quota = Count(snapshot, playerId, now);
                if (quota.RemainingSlots == 0)
                    return new AdRewardReservationResult(AdRewardReservationStatus.DailyLimitReached);
                if (quota.ViewingInProgress)
                    return new AdRewardReservationResult(AdRewardReservationStatus.ViewingInProgress);

                string token = Guid.NewGuid().ToString("N");
                if (snapshot.Reservations.Any(item => item.Token == token) ||
                    snapshot.Grants.Any(item => item.Completion.ReservationToken == token))
                    continue;
                var reservation = new AdRewardReservation(token, playerId, now, now.Add(proofWindow),
                    now.Add(viewLease), requestId, AdRewardReservationState.Viewing);
                if (store.TryWriteReservation(snapshot.Revision, reservation))
                    return new AdRewardReservationResult(AdRewardReservationStatus.Reserved, reservation);
            }

            return new AdRewardReservationResult(AdRewardReservationStatus.RetryRequired);
        }

        public AdRewardReservationResult RecordPresentationEnd(string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence)
                || !verifier.TryVerify(evidence, out VerifiedAdRewardPresentationEnd presentation)
                || presentation.AuthorityScope != scope || string.IsNullOrWhiteSpace(presentation.AccountId)
                || string.IsNullOrWhiteSpace(presentation.ReservationToken)
                || !Enum.IsDefined(typeof(AdRewardPresentationEnd), presentation.End))
                return new AdRewardReservationResult(AdRewardReservationStatus.UnverifiedPresentation);

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                AdRewardAuthoritySnapshot snapshot = ReadScope();
                AdRewardReservation? current = snapshot.Reservations.FirstOrDefault(item =>
                    item.Token == presentation.ReservationToken && item.AccountId == presentation.AccountId);
                if (current == null)
                    return new AdRewardReservationResult(AdRewardReservationStatus.MissingReservation);
                if (snapshot.Grants.Any(item => item.Completion.ReservationToken == current.Token))
                    return new AdRewardReservationResult(AdRewardReservationStatus.AlreadyGranted, current);
                if (current.State == AdRewardReservationState.Released)
                    return new AdRewardReservationResult(AdRewardReservationStatus.Released, current);
                if (!TryGetUtc(out DateTime now) || now < current.CreatedAtUtc)
                    break;
                if (now >= current.AcceptUntilUtc)
                    return new AdRewardReservationResult(AdRewardReservationStatus.ExpiredReservation, current);

                bool releasing = presentation.End == AdRewardPresentationEnd.ConfirmedNoExposure;
                var status = releasing ? AdRewardReservationStatus.Released : AdRewardReservationStatus.AwaitingProof;
                var state = releasing ? AdRewardReservationState.Released : AdRewardReservationState.AwaitingProof;
                if (current.State == state)
                    return new AdRewardReservationResult(status, current);
                AdRewardReservation updated = current.WithState(state);
                if (store.TryWriteReservation(snapshot.Revision, updated))
                    return new AdRewardReservationResult(status, updated);
            }

            return new AdRewardReservationResult(AdRewardReservationStatus.RetryRequired);
        }

        private string? GetAccount(IAdRewardReservationIdentity identity)
        {
            string? playerId = identity.VerifiedPlayerId;
            return identity.AuthorityScope == scope && !string.IsNullOrWhiteSpace(playerId) ? playerId : null;
        }

        private bool TryGetUtc(out DateTime now)
        {
            now = utcNow();
            return now.Kind == DateTimeKind.Utc && now.Date < DateTime.MaxValue.Date;
        }

        private AdRewardAuthoritySnapshot ReadScope()
        {
            AdRewardAuthoritySnapshot snapshot = store.Read();
            if (snapshot.AuthorityScope != scope)
                throw new InvalidOperationException("The store belongs to a different authority scope.");
            return snapshot;
        }

        private static AdRewardDailyAvailability Count(AdRewardAuthoritySnapshot snapshot, string playerId, DateTime now)
        {
            var grantedTokens = snapshot.Grants.Where(grant => grant.Completion.AccountId == playerId)
                .Select(grant => grant.Completion.ReservationToken).ToHashSet(StringComparer.Ordinal);
            int held = 0;
            bool viewing = false;
            foreach (AdRewardReservation reservation in snapshot.Reservations)
            {
                if (reservation.AccountId != playerId || !reservation.AcceptsCompletion || now >= reservation.AcceptUntilUtc
                    || grantedTokens.Contains(reservation.Token))
                    continue;
                if (reservation.CreatedAtUtc.Date == now.Date)
                    held++;
                // A backward clock step must not clear an already issued presentation, regardless of its pinned day.
                viewing |= reservation.State == AdRewardReservationState.Viewing && now < reservation.ViewUntilUtc;
            }
            return new AdRewardDailyAvailability(now.Date, snapshot.ConfirmedUses(playerId, now.Date), held, viewing);
        }
    }
}
