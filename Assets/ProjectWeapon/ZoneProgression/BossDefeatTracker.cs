#nullable enable

using System;
using ProjectWeapon.Run;

namespace ProjectWeapon.Dungeon
{
    public sealed class BossDefeatEvidence
    {
        internal BossDefeatEvidence(RunSession session, string bossId, TimeSpan defeatedAt)
        {
            RunId = session.Id;
            ZoneId = session.ZoneId;
            BossId = bossId;
            DefeatedAt = defeatedAt;
        }

        public RunId RunId { get; }
        public int ZoneId { get; }
        public string BossId { get; }
        public TimeSpan DefeatedAt { get; }
    }

    /// <summary>Receives confirmed enemy deaths from the encounter that designated this run's boss.</summary>
    public sealed class BossDefeatTracker
    {
        private readonly RunState run;
        private readonly RunSession session;
        private readonly string bossId;

        public BossDefeatTracker(RunState run, string designatedBossId)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            if (run.CurrentRun is not { } active || run.Result != null)
                throw new ArgumentException("A boss can only be designated for an active run.", nameof(run));
            if (string.IsNullOrWhiteSpace(designatedBossId))
                throw new ArgumentException("The designated boss needs a stable target ID.", nameof(designatedBossId));

            session = active;
            bossId = designatedBossId;
        }

        public BossDefeatEvidence? Evidence { get; private set; }

        public bool TryRecordEnemyDeath(RunId runId, string enemyId, TimeSpan defeatedAt)
        {
            if (Evidence != null || runId != session.Id || run.CurrentRun?.Id != session.Id || run.Result != null
                || !StringComparer.Ordinal.Equals(bossId, enemyId)
                || defeatedAt < session.StartedAt || defeatedAt >= session.Deadline)
                return false;

            Evidence = new BossDefeatEvidence(session, bossId, defeatedAt);
            return true;
        }
    }
}
