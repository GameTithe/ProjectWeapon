#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProjectWeapon.Save;

namespace ProjectWeapon.AccountSync
{
    public enum AccountSaveStatus
    {
        Unavailable,
        OfflineReadOnly,
        Read,
        Applied,
        AlreadyApplied,
        VersionConflict,
        OperationMismatch,
        Rejected,
        StaleSession
    }

    public enum AccountSaveOperationKind
    {
        Sortie = 1,
        Settlement,
        CoinDraw,
        WeaponSale,
        Reward
    }

    /// <summary>The operation ID survives retries and version changes. The subject is looked up by the authority.</summary>
    public readonly struct AccountSaveOperation
    {
        public AccountSaveOperation(Guid id, AccountSaveOperationKind kind, string subject)
        {
            if (id == Guid.Empty || !Enum.IsDefined(typeof(AccountSaveOperationKind), kind)
                || string.IsNullOrWhiteSpace(subject) || subject != subject.Trim())
                throw new ArgumentException("A stable operation ID, kind and subject are required.");
            Id = id;
            Kind = kind;
            Subject = subject;
        }

        public Guid Id { get; }
        public AccountSaveOperationKind Kind { get; }
        public string Subject { get; }
        internal bool Matches(AccountSaveOperation other) =>
            Id == other.Id && Kind == other.Kind && StringComparer.Ordinal.Equals(Subject, other.Subject);
    }

    public sealed class AccountSaveReceipt
    {
        public AccountSaveReceipt(AccountSaveOperation operation, long appliedRevision, string resultId)
        {
            if (operation.Id == Guid.Empty || appliedRevision <= 0 || string.IsNullOrWhiteSpace(resultId))
                throw new ArgumentException("A receipt requires a completed operation, revision and result ID.");
            Operation = operation;
            AppliedRevision = appliedRevision;
            ResultId = resultId;
        }

        public AccountSaveOperation Operation { get; }
        public long AppliedRevision { get; }
        public string ResultId { get; }
    }

    /// <summary>One Protected value contains the full saved progression and its operation receipts.</summary>
    public sealed class AccountSaveSnapshot
    {
        private readonly ReadOnlyDictionary<Guid, AccountSaveReceipt> receipts;

        public AccountSaveSnapshot(SaveSnapshot progress, IEnumerable<AccountSaveReceipt> receipts)
        {
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            var byId = new Dictionary<Guid, AccountSaveReceipt>();
            foreach (AccountSaveReceipt receipt in receipts ?? throw new ArgumentNullException(nameof(receipts)))
            {
                if (receipt.AppliedRevision > progress.Revision || !byId.TryAdd(receipt.Operation.Id, receipt))
                    throw new ArgumentException("The account receipt does not belong to this save revision.", nameof(receipts));
            }
            this.receipts = new ReadOnlyDictionary<Guid, AccountSaveReceipt>(byId);
        }

        public SaveSnapshot Progress { get; }
        public long Revision => Progress.Revision;
        public IReadOnlyDictionary<Guid, AccountSaveReceipt> Receipts => receipts;
    }

    public sealed class AccountSaveResult
    {
        public AccountSaveResult(AccountSaveStatus status, AccountSaveSnapshot? snapshot = null,
            AccountSaveReceipt? receipt = null)
        {
            Status = status;
            Snapshot = snapshot;
            Receipt = receipt;
        }

        public AccountSaveStatus Status { get; }
        public AccountSaveSnapshot? Snapshot { get; }
        public AccountSaveReceipt? Receipt { get; }
    }
}
