#nullable enable

using System;
using System.Threading.Tasks;
using ProjectWeapon.Accounts;

namespace ProjectWeapon.AccountSync
{
    /// <summary>The transport binds credentials to the captured session. The remote request body carries no account ID.</summary>
    public interface IAccountSaveEndpoint
    {
        Task<AccountSaveResult> ReadAsync(OnlineAccountSession session);
        Task<AccountSaveResult> ApplyAsync(OnlineAccountSession session, long expectedRevision, AccountSaveOperation operation);
    }

    public sealed class AccountSaveClient
    {
        private readonly AccountSessionBoundary accounts;
        private readonly IAccountSaveEndpoint endpoint;
        private readonly Func<bool> connected;
        private OnlineAccountSession? cachedSession;
        private AccountSaveSnapshot? cached;

        public AccountSaveClient(AccountSessionBoundary accounts, IAccountSaveEndpoint endpoint, Func<bool> connected)
        {
            this.accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            this.connected = connected ?? throw new ArgumentNullException(nameof(connected));
        }

        public Task<AccountSaveResult> ReadAsync() => RequestAsync(endpoint.ReadAsync);

        public Task<AccountSaveResult> ApplyAsync(long expectedRevision, AccountSaveOperation operation)
            => RequestAsync(session => endpoint.ApplyAsync(session, expectedRevision, operation));

        private async Task<AccountSaveResult> RequestAsync(Func<OnlineAccountSession, Task<AccountSaveResult>> request)
        {
            if (!accounts.TryGetOnlineSession(out OnlineAccountSession? session))
            {
                cachedSession = null;
                cached = null;
                return new AccountSaveResult(AccountSaveStatus.Unavailable);
            }
            if (!ReferenceEquals(cachedSession, session))
            {
                cachedSession = session;
                cached = null;
            }
            if (!connected())
                return new AccountSaveResult(AccountSaveStatus.OfflineReadOnly, cached);

            AccountSaveResult? fetched = null;
            OnlineRequestStatus status = await accounts.RunOnlineAsync(active =>
                ReferenceEquals(active, session) ? request(active)
                    : Task.FromResult(new AccountSaveResult(AccountSaveStatus.StaleSession)), result => fetched = result);
            if (!accounts.IsCurrent(session) || status == OnlineRequestStatus.Stale)
                return new AccountSaveResult(AccountSaveStatus.StaleSession);
            if (status != OnlineRequestStatus.Applied || fetched == null)
                return connected()
                    ? new AccountSaveResult(AccountSaveStatus.Unavailable)
                    : new AccountSaveResult(AccountSaveStatus.OfflineReadOnly, cached);
            if (fetched.Status == AccountSaveStatus.Unavailable && !connected())
                return new AccountSaveResult(AccountSaveStatus.OfflineReadOnly, cached);

            if (fetched.Snapshot is { } observed)
            {
                if (cached != null && observed.Revision < cached.Revision)
                    return new AccountSaveResult(fetched.Status, cached, fetched.Receipt);
                cached = observed;
            }

            return fetched;
        }
    }
}
