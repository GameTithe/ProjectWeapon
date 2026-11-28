#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectWeapon.Accounts
{
    /// <summary>Owns the guest and verified account selection on the gameplay thread.</summary>
    public sealed class AccountSessionBoundary
    {
        private readonly IAccountProvider provider;
        private readonly SemaphoreSlim providerGate = new SemaphoreSlim(1);
        private OnlineAccountSession? online;
        private CancellationTokenSource? pending;
        private long revision;

        public AccountSessionBoundary(GuestProfileId guestProfile, IAccountProvider provider)
        {
            if (!guestProfile.IsValid)
                throw new ArgumentException("A local guest profile ID is required.", nameof(guestProfile));
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            GuestProfile = guestProfile;
            provider.SessionInvalidated += OnProviderInvalidated;
        }

        public GuestProfileId GuestProfile { get; }
        public AccountMode Mode { get; private set; } = AccountMode.LocalGuest;
        /// <summary>Notifies observers of committed mode changes. A faulty observer cannot interrupt the transition.</summary>
        public event Action<AccountMode>? ModeChanged;

        /// <summary>Start this flow from the settled town. Calls while sign-in is pending do not reopen it.</summary>
        public async Task<AccountSignInStatus> SignInAsync()
        {
            if (Mode == AccountMode.Closed)
                return AccountSignInStatus.Unavailable;
            if (Mode == AccountMode.SigningIn)
                return AccountSignInStatus.InProgress;

            using var attempt = new CancellationTokenSource();
            long started = BeginChange(AccountMode.SigningIn, attempt);
            try
            {
                await providerGate.WaitAsync(attempt.Token);
                try
                {
                    if (started != revision)
                        return AccountSignInStatus.Superseded;

                    ProviderSignInResult result = await RequestProvider(attempt.Token);
                    if (started != revision)
                    {
                        // The SDK may finish a token exchange after cancellation. Clear it before the next operation.
                        ClearProvider();
                        return AccountSignInStatus.Superseded;
                    }

                    if (result.Status == AccountSignInStatus.SignedIn && result.PlayerId is string id && IsAuthorized(id))
                    {
                        online = new OnlineAccountSession(id);
                        SetMode(AccountMode.Online);
                        return started == revision ? AccountSignInStatus.SignedIn : AccountSignInStatus.Superseded;
                    }

                    ClearProvider();
                    SetMode(AccountMode.LocalGuest);
                    return result.Status == AccountSignInStatus.SignedIn ? AccountSignInStatus.Failed : result.Status;
                }
                finally
                {
                    providerGate.Release();
                }
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                return AccountSignInStatus.Superseded;
            }
            finally
            {
                if (ReferenceEquals(pending, attempt))
                    pending = null;
            }
        }

        /// <summary>Revokes account access synchronously and then clears the provider's cached credentials.</summary>
        public Task<bool> UseGuestAsync()
            => Mode == AccountMode.Closed ? Task.FromResult(false) : ClearAfter(BeginChange(AccountMode.LocalGuest));

        /// <summary>Releases provider event ownership and clears the current session.</summary>
        public Task<bool> CloseAsync()
        {
            if (Mode == AccountMode.Closed)
                return ClearAfter(revision);
            provider.SessionInvalidated -= OnProviderInvalidated;
            return ClearAfter(BeginChange(AccountMode.Closed));
        }

        public bool TryGetOnlineSession([NotNullWhen(true)] out OnlineAccountSession? session)
        {
            session = online;
            if (session != null && IsCurrent(session))
                return true;

            session = null;
            return false;
        }

        public bool IsCurrent(OnlineAccountSession? session)
        {
            if (Mode != AccountMode.Online || session == null || !ReferenceEquals(online, session))
                return false;
            if (IsAuthorized(session.PlayerId))
                return true;

            OnProviderInvalidated();
            return false;
        }

        /// <summary>Runs an authenticated game request and applies its response only while that exact session is current.</summary>
        public async Task<OnlineRequestStatus> RunOnlineAsync<T>(Func<OnlineAccountSession, Task<T>> request, Action<T> apply)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));
            if (!TryGetOnlineSession(out OnlineAccountSession? session))
                return OnlineRequestStatus.Unavailable;

            T result;
            try
            {
                result = await request(session);
            }
            catch (Exception)
            {
                return IsCurrent(session) ? OnlineRequestStatus.Failed : OnlineRequestStatus.Stale;
            }

            if (!IsCurrent(session))
                return OnlineRequestStatus.Stale;
            apply(result);
            return OnlineRequestStatus.Applied;
        }

        private async Task<ProviderSignInResult> RequestProvider(CancellationToken token)
        {
            try
            {
                provider.SignOut();
                return await provider.SignInAsync(token);
            }
            catch (Exception)
            {
                // Provider errors can carry credential-bearing context. Only the outcome crosses the boundary.
                return default;
            }
        }

        private async Task<bool> ClearAfter(long expected)
        {
            await providerGate.WaitAsync();
            try
            {
                return expected == revision && ClearProvider();
            }
            finally
            {
                providerGate.Release();
            }
        }

        private bool ClearProvider()
        {
            try
            {
                provider.SignOut();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsAuthorized(string playerId)
        {
            try
            {
                return provider.IsAuthorized(playerId);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void OnProviderInvalidated()
        {
            if (Mode == AccountMode.Online)
                BeginChange(AccountMode.Expired);
        }

        private long BeginChange(AccountMode mode, CancellationTokenSource? next = null)
        {
            long changed = ++revision;
            OnlineAccountSession? previous = online;
            online = null;
            CancellationTokenSource? previousAttempt = pending;
            pending = next;
            AccountMode old = Mode;
            Mode = mode;
            previous?.Revoke();
            if (previousAttempt != null)
                Cancel(previousAttempt);
            if (old != mode)
                PublishMode(mode, changed);
            return changed;
        }

        private void SetMode(AccountMode mode)
        {
            Mode = mode;
            PublishMode(mode, revision);
        }

        private void PublishMode(AccountMode mode, long expectedRevision)
        {
            if (ModeChanged is not { } observers)
                return;

            foreach (Action<AccountMode> observer in observers.GetInvocationList())
            {
                if (revision != expectedRevision || Mode != mode)
                    return;

                try
                {
                    observer(mode);
                }
                catch (Exception)
                {
                    // Observer failures are independent of session and provider lifetime.
                }
            }
        }

        internal static void Cancel(CancellationTokenSource source)
        {
            try
            {
                source.Cancel();
            }
            catch (AggregateException)
            {
                // A consumer's cancellation handler cannot restore a retired session.
            }
        }
    }
}
