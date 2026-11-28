#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectWeapon.Save;

namespace ProjectWeapon.AccountSync
{
    /// <summary>Supplied by the trusted host from its authenticated request context.</summary>
    public interface IAccountSaveIdentity
    {
        string? VerifiedPlayerId { get; }
    }

    public sealed class ProtectedAccountSave
    {
        public ProtectedAccountSave(AccountSaveSnapshot snapshot, string writeLock)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(writeLock))
                throw new ArgumentException("An existing Protected value must supply a write lock.", nameof(writeLock));
            WriteLock = writeLock;
        }

        public AccountSaveSnapshot Snapshot { get; }
        public string WriteLock { get; }
    }

    public interface IProtectedAccountSaveStore
    {
        Task<ProtectedAccountSave?> ReadAsync(string playerId, CancellationToken cancellation);

        /// <summary>Atomically replaces the whole value with the loaded lock. False means a lock conflict.</summary>
        Task<bool> TryReplaceAsync(string playerId, ProtectedAccountSave current, AccountSaveSnapshot next,
            CancellationToken cancellation);
    }

    public sealed class ResolvedAccountSaveOperation
    {
        public ResolvedAccountSaveOperation(SaveSnapshot progress, string resultId)
        {
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            ResultId = resultId;
        }

        public SaveSnapshot Progress { get; }
        public string ResultId { get; }
    }

    /// <summary>
    /// Server-side ownership and gameplay validation with no durable side effects.
    /// Use SaveSnapshot.Next and retain the loaded record objects when appending transaction receipts.
    /// </summary>
    public interface IAccountSaveOperations
    {
        ResolvedAccountSaveOperation? Resolve(string playerId, SaveSnapshot current, AccountSaveOperation operation);
    }

    public sealed class AccountSaveAuthority
    {
        private readonly IProtectedAccountSaveStore store;
        private readonly IAccountSaveOperations operations;

        public AccountSaveAuthority(IProtectedAccountSaveStore store, IAccountSaveOperations operations)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        }

        public async Task<AccountSaveResult> ReadAsync(IAccountSaveIdentity identity, CancellationToken cancellation)
        {
            string? playerId = identity.VerifiedPlayerId;
            if (string.IsNullOrWhiteSpace(playerId))
                return new AccountSaveResult(AccountSaveStatus.Unavailable);

            ProtectedAccountSave? loaded = await store.ReadAsync(playerId, cancellation);
            return loaded == null
                ? new AccountSaveResult(AccountSaveStatus.Unavailable)
                : new AccountSaveResult(AccountSaveStatus.Read, loaded.Snapshot);
        }

        public async Task<AccountSaveResult> ApplyAsync(IAccountSaveIdentity identity, long expectedRevision,
            AccountSaveOperation operation, CancellationToken cancellation)
        {
            string? playerId = identity.VerifiedPlayerId;
            if (string.IsNullOrWhiteSpace(playerId))
                return new AccountSaveResult(AccountSaveStatus.Unavailable);
            if (operation.Id == Guid.Empty || expectedRevision <= 0)
                return new AccountSaveResult(AccountSaveStatus.Rejected);

            ProtectedAccountSave? current = await store.ReadAsync(playerId, cancellation);
            if (current == null)
                return new AccountSaveResult(AccountSaveStatus.Unavailable);
            AccountSaveSnapshot snapshot = current.Snapshot;
            AccountSaveResult? replay = Replay(snapshot, operation);
            if (replay != null)
                return replay;
            if (snapshot.Revision != expectedRevision)
                return new AccountSaveResult(AccountSaveStatus.VersionConflict, snapshot);

            cancellation.ThrowIfCancellationRequested();
            ResolvedAccountSaveOperation? change = operations.Resolve(playerId, snapshot.Progress, operation);
            if (change == null)
                return new AccountSaveResult(AccountSaveStatus.Rejected, snapshot);
            if (change.Progress.Revision != checked(snapshot.Revision + 1)
                || !snapshot.Progress.Settlements.All(change.Progress.Settlements.Contains)
                || !snapshot.Progress.CoinWeaponDraws.All(change.Progress.CoinWeaponDraws.Contains)
                || !snapshot.Progress.WeaponSales.All(change.Progress.WeaponSales.Contains)
                || !snapshot.Progress.CatalogMilestones.All(change.Progress.CatalogMilestones.Contains))
                throw new InvalidOperationException("The trusted operation must advance one revision and retain prior receipts.");

            var receipt = new AccountSaveReceipt(operation, change.Progress.Revision, change.ResultId);
            var next = new AccountSaveSnapshot(change.Progress, snapshot.Receipts.Values.Append(receipt));
            cancellation.ThrowIfCancellationRequested();
            if (await store.TryReplaceAsync(playerId, current, next, cancellation))
                return new AccountSaveResult(AccountSaveStatus.Applied, next, receipt);

            ProtectedAccountSave? latest = await store.ReadAsync(playerId, cancellation);
            if (latest == null || latest.Snapshot.Revision <= snapshot.Revision)
                return new AccountSaveResult(AccountSaveStatus.Unavailable);
            return Replay(latest.Snapshot, operation)
                ?? new AccountSaveResult(AccountSaveStatus.VersionConflict, latest.Snapshot);
        }

        private static AccountSaveResult? Replay(AccountSaveSnapshot snapshot, AccountSaveOperation operation)
        {
            if (!snapshot.Receipts.TryGetValue(operation.Id, out AccountSaveReceipt receipt))
                return null;
            return receipt.Operation.Matches(operation)
                ? new AccountSaveResult(AccountSaveStatus.AlreadyApplied, snapshot, receipt)
                : new AccountSaveResult(AccountSaveStatus.OperationMismatch, snapshot);
        }
    }
}
