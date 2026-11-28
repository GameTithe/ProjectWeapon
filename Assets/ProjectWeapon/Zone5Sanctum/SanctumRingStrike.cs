#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;

namespace ProjectWeapon.Zone5Sanctum
{
    public readonly struct SanctumRingStrike
    {
        // Unity positions round independently to floats; absorb that error at the drawn boundary.
        private const float BoundaryTolerance = .000002f;

        internal SanctumRingStrike(Guid id, WorldPoint2D center, TimeSpan startedAt, SanctumBossSettings settings)
        {
            DamageId = id;
            Center = center;
            InnerRadius = settings.InnerRadius;
            OuterRadius = settings.OuterRadius;
            Damage = settings.Damage;
            HitsAt = startedAt + settings.Windup;
            ReadyAt = HitsAt + settings.Recovery;
        }

        public Guid DamageId { get; }
        public WorldPoint2D Center { get; }
        public float InnerRadius { get; }
        public float OuterRadius { get; }
        public int Damage { get; }
        public TimeSpan HitsAt { get; }
        public TimeSpan ReadyAt { get; }

        public bool Contains(WorldPoint2D point)
        {
            double distance = Center.SquaredDistanceTo(point);
            double inner = Math.Max(0d, InnerRadius - BoundaryTolerance);
            double outer = OuterRadius + BoundaryTolerance;
            return point.IsFinite && distance >= inner * inner && distance <= outer * outer;
        }

        internal IReadOnlyList<DamageRequest> ReadHits(TargetCandidate player)
        {
            if (!Contains(player.Position))
                return Array.Empty<DamageRequest>();

            var area = AreaDamageRequest.Circle(DamageId, SanctumBossActor.BossId, Center,
                OuterRadius + BoundaryTolerance, Damage);
            return AreaDamageSelector.FindTargets(area, new[] { player });
        }
    }

    public readonly struct SanctumRingImpact
    {
        internal SanctumRingImpact(SanctumRingStrike attack, IReadOnlyList<DamageRequest> hits)
        {
            Attack = attack;
            Hits = hits;
        }

        public SanctumRingStrike Attack { get; }
        public IReadOnlyList<DamageRequest> Hits { get; }
    }

    public readonly struct SanctumBossDefeat
    {
        internal SanctumBossDefeat(RunId runId, TimeSpan defeatedAt)
        {
            RunId = runId;
            DefeatedAt = defeatedAt;
        }

        public RunId RunId { get; }
        public int ZoneId => SanctumBossActor.ZoneId;
        public string BossId => SanctumBossActor.BossId;
        public TimeSpan DefeatedAt { get; }
    }
}
