#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ProjectWeapon.DailyMissions.Editor
{
    /// <summary>A local verification store for the authority contract. This assembly is Editor-only.</summary>
    public sealed class LocalDailyMissionProgressStore : IDailyMissionProgressStore
    {
        private readonly string path;
        private readonly string mutexName;

        public LocalDailyMissionProgressStore(string path)
        {
            this.path = Path.GetFullPath(path);
            string identity = Path.DirectorySeparatorChar == '\\' ? this.path.ToUpperInvariant() : this.path;
            using SHA256 sha = SHA256.Create();
            mutexName = "ProjectWeapon-DailyMission-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity)));
        }

        public DailyReturnResult RecordDailyReturn(string playerId, Guid runId, DateTime utcDay)
        {
            string day = FormatDay(utcDay);
            ValidatePlayer(playerId);
            if (runId == Guid.Empty)
                throw new ArgumentException("A run ID is required.", nameof(runId));
            string run = runId.ToString("D");
            return WithLock(() =>
            {
                ProgressDocument state = Load();
                ProgressEntry? firstRecord = state.entries.FirstOrDefault(item => item.player == playerId && item.run == run);
                if (firstRecord != null)
                {
                    ProgressEntry earlierCredit = state.entries.Single(item => item.player == playerId
                        && item.day == firstRecord.day && item.credit);
                    return new DailyReturnResult(DailyReturnStatus.RunAlreadyRecorded, ParseDay(firstRecord.day),
                        Guid.Parse(earlierCredit.run));
                }

                ProgressEntry? credit = state.entries.FirstOrDefault(item => item.player == playerId && item.day == day && item.credit);
                state.entries.Add(new ProgressEntry { player = playerId, day = day, run = run, credit = credit == null });
                Save(state);
                return credit == null ? new DailyReturnResult(DailyReturnStatus.Completed, utcDay, runId)
                    : new DailyReturnResult(DailyReturnStatus.AlreadyCompleted, utcDay, Guid.Parse(credit.run));
            });
        }

        public Guid? ReadDailyReturn(string playerId, DateTime utcDay)
        {
            string day = FormatDay(utcDay);
            ValidatePlayer(playerId);
            return WithLock<Guid?>(() =>
            {
                ProgressEntry? credit = Load().entries.FirstOrDefault(item =>
                    item.player == playerId && item.day == day && item.credit);
                return credit != null ? Guid.Parse(credit.run) : null;
            });
        }

        private T WithLock<T>(Func<T> action)
        {
            using var mutex = new Mutex(false, mutexName);
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                    throw new IOException("The local mission progress store is busy.");
            }
            catch (AbandonedMutexException)
            {
                // The next read sees either the previous file or the completed atomic replacement.
            }

            try
            {
                return action();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        private ProgressDocument Load()
        {
            if (!File.Exists(path))
                return new ProgressDocument { version = 1 };

            ProgressDocument state;
            try
            {
                state = JsonUtility.FromJson<ProgressDocument>(File.ReadAllText(path));
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException("Invalid mission progress document.", error);
            }
            if (state == null || state.version != 1 || state.entries == null)
                throw new InvalidDataException("Invalid mission progress version or entries.");

            var runs = new HashSet<(string, Guid)>();
            var days = new HashSet<(string, string)>();
            var credits = new HashSet<(string, string)>();
            foreach (ProgressEntry item in state.entries)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.player) || item.player != item.player.Trim()
                    || !Guid.TryParseExact(item.run, "D", out Guid recordedRun) || recordedRun == Guid.Empty
                    || item.run != recordedRun.ToString("D") || !TryParseDay(item.day, out _)
                    || !runs.Add((item.player, recordedRun)) || (item.credit && !credits.Add((item.player, item.day))))
                    throw new InvalidDataException("Invalid or duplicate daily return data.");
                days.Add((item.player, item.day));
            }
            if (!days.SetEquals(credits))
                throw new InvalidDataException("A mission day is missing its completion event.");
            return state;
        }

        private void Save(ProgressDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] content = Encoding.UTF8.GetBytes(JsonUtility.ToJson(document));
                using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(content, 0, content.Length);
                    file.Flush(flushToDisk: true);
                }
                if (File.Exists(path))
                    File.Replace(staging, path, null);
                else
                    File.Move(staging, path);
            }
            finally
            {
                if (File.Exists(staging))
                    File.Delete(staging);
            }
        }

        private static void ValidatePlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId) || playerId != playerId.Trim())
                throw new ArgumentException("An authorized account ID is required.", nameof(playerId));
        }

        private static string FormatDay(DateTime day)
        {
            if (day.Kind != DateTimeKind.Utc || day.TimeOfDay != TimeSpan.Zero)
                throw new ArgumentException("The progress key must be a UTC date.", nameof(day));
            return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static DateTime ParseDay(string day)
            => TryParseDay(day, out DateTime parsed) ? parsed : throw new InvalidDataException("Invalid UTC day.");

        private static bool TryParseDay(string day, out DateTime parsed)
            => DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed);

        [Serializable]
        private sealed class ProgressDocument
        {
            public int version;
            public List<ProgressEntry> entries = new List<ProgressEntry>();
        }

        [Serializable]
        private sealed class ProgressEntry
        {
            public string player = string.Empty;
            public string day = string.Empty;
            public string run = string.Empty;
            public bool credit;
        }
    }
}
