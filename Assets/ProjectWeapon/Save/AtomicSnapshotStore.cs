#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

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
                Directory.CreateDirectory(directoryPath);
                using var writerLock = new FileStream(activePath + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);

                var active = ReadActive();
                if (active.HasValue && active.Value.snapshot.Revision == next.Revision &&
                    StringComparer.Ordinal.Equals(active.Value.checksum, encoded.checksum))
                    return new SnapshotCommitResult(SnapshotCommitStatus.AlreadyCommitted, active.Value.snapshot);

                if ((active?.snapshot.Revision ?? 0) != expectedRevision)
                    return new SnapshotCommitResult(SnapshotCommitStatus.RevisionConflict, active?.snapshot);

                string tempPath = activePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    files.WriteAndFlush(tempPath, encoded.bytes);
                    files.Activate(tempPath, activePath, active.HasValue);
                }
                finally
                {
                    // A temp file is never considered by Load. Cleanup errors do not determine the commit result.
                    TryDeleteTemp(tempPath);
                }

                return new SnapshotCommitResult(SnapshotCommitStatus.Committed, next);
            }
        }

        private (SaveSnapshot snapshot, string checksum)? ReadActive()
        {
            try
            {
                using var stream = new FileStream(activePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                using var reader = new StreamReader(stream, SnapshotCodec.Utf8, detectEncodingFromByteOrderMarks: false);
                return SnapshotCodec.Decode(reader.ReadToEnd());
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
                throw new InvalidDataException("The active snapshot contains invalid UTF-8.", error);
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
                File.Replace(tempPath, activePath, destinationBackupFileName: null);
            else
                File.Move(tempPath, activePath);
        }
    }
}
