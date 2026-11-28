#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using ProjectWeapon.Weapons;

[assembly: InternalsVisibleTo("ProjectWeapon.Save.Tests")]

namespace ProjectWeapon.Save
{
    public enum SnapshotCommitStatus
    {
        Committed,
        AlreadyCommitted,
        RevisionConflict
    }

    public sealed class SnapshotCommitResult
    {
        internal SnapshotCommitResult(SnapshotCommitStatus status, SaveSnapshot? activeSnapshot)
        {
            Status = status;
            ActiveSnapshot = activeSnapshot;
        }

        public SnapshotCommitStatus Status { get; }
        public SaveSnapshot? ActiveSnapshot { get; }
    }

    public sealed class AtomicSnapshotStore
    {
        internal const string BackupSuffix = ".bak";

        private static readonly ConcurrentDictionary<string, object> PathLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private readonly string activePath;
        private readonly string directoryPath;
        private readonly object pathLock;
        private readonly SnapshotFileOperations files;

        public AtomicSnapshotStore(string path) : this(path, new SnapshotFileOperations())
        {
        }

        internal AtomicSnapshotStore(string path, SnapshotFileOperations files)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A snapshot file path is required.", nameof(path));

            activePath = Path.GetFullPath(path);
            directoryPath = Path.GetDirectoryName(activePath)
                ?? throw new ArgumentException("A snapshot file must be inside a directory.", nameof(path));
            if (string.IsNullOrEmpty(Path.GetFileName(activePath)))
                throw new ArgumentException("A snapshot file name is required.", nameof(path));

            pathLock = PathLocks.GetOrAdd(activePath, _ => new object());
            this.files = files ?? throw new ArgumentNullException(nameof(files));
        }

        public SaveSnapshot? Load()
        {
            lock (pathLock)
                return ReadActive()?.snapshot;
        }

        /// <summary>Reads a valid snapshot or backup without changing either file.</summary>
        public SnapshotLoadResult LoadWithRecovery(Func<WeaponDefinitionId, bool> definitionExists)
        {
            if (definitionExists == null)
                throw new ArgumentNullException(nameof(definitionExists));

            lock (pathLock)
            {
                var active = ReadForRecovery(activePath, definitionExists);
                if (active.status == SnapshotLoadStatus.Healthy || active.status == SnapshotLoadStatus.UnsupportedVersion)
                    return new SnapshotLoadResult(active.status, active.snapshot, active.issue);

                var backup = ReadForRecovery(activePath + BackupSuffix, definitionExists);
                if (backup.status == SnapshotLoadStatus.Healthy)
                    return new SnapshotLoadResult(SnapshotLoadStatus.RecoveredFromBackup, backup.snapshot, active.issue);

                if (active.status == SnapshotLoadStatus.Missing && backup.status == SnapshotLoadStatus.Missing)
                    return new SnapshotLoadResult(SnapshotLoadStatus.Missing, null);

                SnapshotLoadStatus status = active.status == SnapshotLoadStatus.Missing
                    && backup.status == SnapshotLoadStatus.UnsupportedVersion
                    ? SnapshotLoadStatus.UnsupportedVersion
                    : SnapshotLoadStatus.Corrupt;
                return new SnapshotLoadResult(status, null, active.issue, backup.issue);
            }
        }

        public (SaveSnapshot? Snapshot, bool Upgraded) UpgradeLegacySnapshot()
        {
            lock (pathLock)
            {
                using FileStream writerLock = OpenWriterLock();
                string? original = ReadActiveJson();
                if (original == null)
                    return (null, false);

                var current = SnapshotCodec.DecodeUpgradeInput(original);
                if (current.requiresUpgrade)
                    Install(SnapshotCodec.Encode(current.snapshot).bytes, hasActiveSnapshot: true);

                return (current.snapshot, current.requiresUpgrade);
            }
        }

        public SnapshotCommitResult Commit(long expectedRevision, SaveSnapshot next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));
            if (expectedRevision < 0 || expectedRevision == long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            if (next.Revision != expectedRevision + 1)
                throw new ArgumentException("The next revision must follow the expected revision.", nameof(next));

            var encoded = SnapshotCodec.Encode(next);
            lock (pathLock)
            {
                using FileStream writerLock = OpenWriterLock();

                var active = ReadActive();
                if (active.HasValue && active.Value.snapshot.Revision == next.Revision &&
                    StringComparer.Ordinal.Equals(active.Value.checksum, encoded.checksum))
                    return new SnapshotCommitResult(SnapshotCommitStatus.AlreadyCommitted, active.Value.snapshot);

                if ((active?.snapshot.Revision ?? 0) != expectedRevision)
                    return new SnapshotCommitResult(SnapshotCommitStatus.RevisionConflict, active?.snapshot);

                Install(encoded.bytes, active.HasValue);

                return new SnapshotCommitResult(SnapshotCommitStatus.Committed, next);
            }
        }

        private FileStream OpenWriterLock()
        {
            Directory.CreateDirectory(directoryPath);
            return new FileStream(activePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private void Install(byte[] bytes, bool hasActiveSnapshot)
        {
            string tempPath = activePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                files.WriteAndFlush(tempPath, bytes);
                files.Activate(tempPath, activePath, hasActiveSnapshot);
            }
            finally
            {
                // A temp file is never considered by Load. Cleanup errors do not determine the commit result.
                TryDeleteTemp(tempPath);
            }
        }

        private (SaveSnapshot snapshot, string checksum)? ReadActive()
        {
            string? json = ReadActiveJson();
            return json == null ? null : SnapshotCodec.Decode(json);
        }

        private static (SnapshotLoadStatus status, SaveSnapshot? snapshot, string? issue) ReadForRecovery(
            string path, Func<WeaponDefinitionId, bool> definitionExists)
        {
            SaveSnapshot? snapshot;
            try
            {
                string? json = ReadSnapshotJson(path);
                if (json == null)
                    return (SnapshotLoadStatus.Missing, null, "The snapshot file is missing.");

                var decoded = SnapshotCodec.DecodeForRecovery(json);
                snapshot = decoded.snapshot;
                if (snapshot == null)
                    return (SnapshotLoadStatus.UnsupportedVersion, null,
                        $"Unsupported snapshot format version {decoded.formatVersion}.");
            }
            catch (InvalidDataException error)
            {
                return (SnapshotLoadStatus.Corrupt, null, error.Message);
            }

            string? issue = FindMissingDefinition(snapshot, definitionExists);
            return issue == null
                ? (SnapshotLoadStatus.Healthy, snapshot, null)
                : (SnapshotLoadStatus.Corrupt, null, issue);
        }

        private static string? FindMissingDefinition(SaveSnapshot snapshot,
            Func<WeaponDefinitionId, bool> definitionExists)
        {
            foreach (var weapon in snapshot.Weapons)
            {
                if (!definitionExists(weapon.DefinitionId))
                    return $"Weapon '{weapon.Id}' references missing definition '{weapon.DefinitionId}'.";
            }

            foreach (var record in snapshot.Catalog)
            {
                if (!definitionExists(record.DefinitionId))
                    return $"Catalog record references missing definition '{record.DefinitionId}'.";
            }

            foreach (var purchase in snapshot.CoinWeaponDraws)
            {
                foreach (var award in purchase.Awards)
                {
                    if (!definitionExists(award.DefinitionId))
                        return $"Coin draw '{purchase.RequestId}' award '{award.InstanceId}' references missing definition '{award.DefinitionId}'.";
                }
            }

            foreach (WeaponSaleSaveRecord sale in snapshot.WeaponSales)
            {
                if (!definitionExists(sale.DefinitionId))
                    return $"Weapon sale '{sale.RequestId}' references missing definition '{sale.DefinitionId}'.";
            }

            return null;
        }

        private string? ReadActiveJson() => ReadSnapshotJson(activePath);

        private static string? ReadSnapshotJson(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                using var reader = new StreamReader(stream, SnapshotCodec.Utf8, detectEncodingFromByteOrderMarks: false);
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (DecoderFallbackException error)
            {
                throw new InvalidDataException("The snapshot contains invalid UTF-8.", error);
            }
        }

        private static void TryDeleteTemp(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal class SnapshotFileOperations
    {
        internal virtual void WriteAndFlush(string path, byte[] bytes)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        internal virtual void Activate(string tempPath, string activePath, bool hasActiveSnapshot)
        {
            if (hasActiveSnapshot)
                File.Replace(tempPath, activePath, destinationBackupFileName: activePath + AtomicSnapshotStore.BackupSuffix);
            else
                File.Move(tempPath, activePath);
        }
    }
}
