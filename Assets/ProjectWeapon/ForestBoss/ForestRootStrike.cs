#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;

namespace ProjectWeapon.ForestBoss
{
    public readonly struct ForestRootStrike
    {
        // Renderer and target coordinates round to floats in world space. Absorb that precision at the perimeter.
        private const float PerimeterTolerance = 0.000002f;

        internal ForestRootStrike(Guid id, WorldPoint2D origin, WorldPoint2D center, TimeSpan at, ForestBossSettings settings)
        {
            DamageId = id;
            Origin = origin;
            Center = center;
            Radius = settings.Radius;
            Damage = settings.Damage;
            StartedAt = at;
            HitsAt = at + settings.Windup;
            ReadyAt = HitsAt + settings.Recovery;
        }

        public Guid DamageId { get; }
        public WorldPoint2D Origin { get; }
        public WorldPoint2D Center { get; }
        public float Radius { get; }
        public int Damage { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan HitsAt { get; }
        public TimeSpan ReadyAt { get; }

        internal AreaDamageRequest Area =>
            AreaDamageRequest.Circle(DamageId, ForestBossActor.BossId, Center, Radius + PerimeterTolerance, Damage);
    }

    public readonly struct ForestRootImpact
    {
        internal ForestRootImpact(ForestRootStrike attack, IReadOnlyList<DamageRequest> hits)
        {
            Attack = attack;
            Hits = hits;
        }

        public ForestRootStrike Attack { get; }
        public IReadOnlyList<DamageRequest> Hits { get; }
    }

    public readonly struct ForestBossDefeat
    {
        internal ForestBossDefeat(RunId runId, TimeSpan defeatedAt)
        {
            RunId = runId;
            DefeatedAt = defeatedAt;
        }

        public RunId RunId { get; }
        public int ZoneId => ForestBossActor.ZoneId;
        public string BossId => ForestBossActor.BossId;
        public TimeSpan DefeatedAt { get; }
    }
}
