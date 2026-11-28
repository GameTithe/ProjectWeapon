#nullable enable

using System;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;

namespace ProjectWeapon.DailyMissions
{
    public enum DailyReturnStatus
    {
        Unconfirmed,
        Unsuccessful,
        Completed,
        AlreadyCompleted,
        RunAlreadyRecorded
    }

    public readonly struct DailyReturnResult
    {
        public DailyReturnResult(DailyReturnStatus status, DateTime? utcDay, Guid? creditRun)
        {
            Status = status;
            UtcDay = utcDay;
            CreditRun = creditRun;
        }

        public DailyReturnStatus Status { get; }
        public DateTime? UtcDay { get; }
        public Guid? CreditRun { get; }
    }

    public interface IDailyMissionProgressStore
    {
        /// <summary>Atomically records the event and the first completion for this player and UTC day.</summary>
        DailyReturnResult RecordDailyReturn(string playerId, Guid runId, DateTime utcDay);

        Guid? ReadDailyReturn(string playerId, DateTime utcDay);
    }

    /// <summary>Consumes the online authority's committed runs. Does not award coins.</summary>
    public sealed class DailyReturnMission
    {
        public const string MissionId = "daily_safe_return";

        private readonly IDailyMissionProgressStore store;

        public DailyReturnMission(IDailyMissionProgressStore store)
            => this.store = store ?? throw new ArgumentNullException(nameof(store));

        /// <param name="playerId">The online account ID from the verified settlement context.</param>
        /// <param name="snapshot">The authority's current committed account snapshot.</param>
        /// <param name="runId">The run already validated by the settlement authority.</param>
        /// <param name="effectiveAt">The server's recorded outcome time for that run, including on a retry.</param>
        public DailyReturnResult Record(string playerId, SaveSnapshot snapshot, Guid runId, DateTimeOffset effectiveAt)
        {
            ValidatePlayer(playerId);
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (runId == Guid.Empty || effectiveAt == default)
                return default;

            string transaction = "RunSettlement:" + runId.ToString("D");
            if (!snapshot.TryGetSettlement(transaction, out SettlementSaveRecord? receipt)
                || receipt?.RunId != runId || RunSettlementDetails.Read(receipt) is not { } details)
                return default;
            DateTime day = effectiveAt.UtcDateTime.Date;
            if (details.Reason != RunEndReason.Extracted)
                return new DailyReturnResult(DailyReturnStatus.Unsuccessful, day, null);

            return store.RecordDailyReturn(playerId, runId, day);
        }

        public Guid? Read(string playerId, DateTimeOffset utcClock)
        {
            ValidatePlayer(playerId);
            if (utcClock == default)
                throw new ArgumentOutOfRangeException(nameof(utcClock));
            return store.ReadDailyReturn(playerId, utcClock.UtcDateTime.Date);
        }

        private static void ValidatePlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId) || !StringComparer.Ordinal.Equals(playerId, playerId.Trim()))
                throw new ArgumentException("An authorized account ID is required.", nameof(playerId));
        }
    }
}
