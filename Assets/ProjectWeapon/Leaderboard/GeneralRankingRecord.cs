#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using ProjectWeapon.Run;

namespace ProjectWeapon.Leaderboard
{
    public sealed class GeneralRankingRecord
    {
        /// <summary>Rehydrates a previously accepted ranking record from its stored fields.</summary>
        public GeneralRankingRecord(int zoneId, bool bossDefeated, long remainingMilliseconds, long exportValue,
            DateTimeOffset achievedAt, string submissionId)
        {
            if (zoneId <= 0)
                throw new ArgumentOutOfRangeException(nameof(zoneId));
            if (remainingMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingMilliseconds));
            if (exportValue < 0)
                throw new ArgumentOutOfRangeException(nameof(exportValue));
            if (string.IsNullOrWhiteSpace(submissionId))
                throw new ArgumentException("A verified submission ID is required.", nameof(submissionId));

            ZoneId = zoneId;
            BossDefeated = bossDefeated;
            RemainingMilliseconds = remainingMilliseconds;
            ExportValue = exportValue;
            AchievedAtUtc = achievedAt.ToUniversalTime();
            SubmissionId = submissionId;
        }

        public int ZoneId { get; }
        public bool BossDefeated { get; }
        public long RemainingMilliseconds { get; }
        public long ExportValue { get; }
        public DateTimeOffset AchievedAtUtc { get; }
        public string SubmissionId { get; }

        /// <summary>Creates a candidate from a caller-validated successful submission.</summary>
        public static bool TryCreate(RunResult run, bool bossDefeated, long exportValue,
            DateTimeOffset achievedAt, string submissionId, [NotNullWhen(true)] out GeneralRankingRecord? record)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (!run.Succeeded)
            {
                record = null;
                return false;
            }
            long remainingMilliseconds = run.Remaining.Ticks / TimeSpan.TicksPerMillisecond;
            record = new GeneralRankingRecord(run.ZoneId, bossDefeated, remainingMilliseconds, exportValue,
                achievedAt, submissionId);
            return true;
        }
    }

    public static class GeneralRanking
    {
        /// <summary>Negative puts left first. Zero means both records share one rank.</summary>
        public static int CompareRank(GeneralRankingRecord left, GeneralRankingRecord right)
        {
            if (left == null)
                throw new ArgumentNullException(nameof(left));
            if (right == null)
                throw new ArgumentNullException(nameof(right));

            int comparison = right.ZoneId.CompareTo(left.ZoneId);
            if (comparison != 0)
                return comparison;
            comparison = right.BossDefeated.CompareTo(left.BossDefeated);
            if (comparison != 0)
                return comparison;
            comparison = right.RemainingMilliseconds.CompareTo(left.RemainingMilliseconds);
            if (comparison != 0)
                return comparison;
            return right.ExportValue.CompareTo(left.ExportValue);
        }

        /// <summary>Orders records within a shared rank without changing rank equality.</summary>
        public static int CompareDisplayOrder(GeneralRankingRecord left, GeneralRankingRecord right)
        {
            int comparison = CompareRank(left, right);
            if (comparison != 0)
                return comparison;
            comparison = left.AchievedAtUtc.CompareTo(right.AchievedAtUtc);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left.SubmissionId, right.SubmissionId);
        }
    }
}
