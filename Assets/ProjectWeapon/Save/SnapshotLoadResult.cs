#nullable enable

namespace ProjectWeapon.Save
{
    public enum SnapshotLoadStatus
    {
        Healthy,
        Missing,
        RecoveredFromBackup,
        Corrupt,
        UnsupportedVersion
    }

    public sealed class SnapshotLoadResult
    {
        internal SnapshotLoadResult(SnapshotLoadStatus status, SaveSnapshot? snapshot,
            string? activeIssue = null, string? backupIssue = null)
        {
            Status = status;
            Snapshot = snapshot;
            ActiveIssue = activeIssue;
            BackupIssue = backupIssue;
        }

        public SnapshotLoadStatus Status { get; }
        public SaveSnapshot? Snapshot { get; }
        public string? ActiveIssue { get; }
        public string? BackupIssue { get; }
    }
}
