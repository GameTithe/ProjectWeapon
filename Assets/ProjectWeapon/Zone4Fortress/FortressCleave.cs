#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;

namespace ProjectWeapon.Zone4Fortress
{
    public readonly struct FortressCleave
    {
        internal FortressCleave(AreaDamageRequest area, TimeSpan now, FortressBossSettings settings)
        {
            Area = area;
            StartedAt = now;
            HitsAt = now + settings.Windup;
            ReadyAt = HitsAt + settings.Recovery;
        }

        public AreaDamageRequest Area { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan HitsAt { get; }
        public TimeSpan ReadyAt { get; }

        internal IReadOnlyList<DamageRequest> FindHits(TargetCandidate target)
        {
            // Round-off from the world-space outline must not turn a drawn boundary into a miss.
            var edge = AreaDamageRequest.Sector(Area.ApplicationId, FortressBossActor.BossId, Area.Origin,
                Area.Radius + .000002f, Area.Amount, Area.Direction, Math.Min(360f, Area.AngleDegrees + .0002f));
            return AreaDamageSelector.FindTargets(edge, new[] { target });
        }
    }

    public readonly struct FortressCleaveImpact
    {
        internal FortressCleaveImpact(FortressCleave attack, IReadOnlyList<DamageRequest> hits)
        {
            Attack = attack;
            Hits = hits;
        }

        public FortressCleave Attack { get; }
        public IReadOnlyList<DamageRequest> Hits { get; }
    }

    public readonly struct FortressBossDefeat
    {
        internal FortressBossDefeat(RunId runId, TimeSpan defeatedAt)
        {
            RunId = runId;
            DefeatedAt = defeatedAt;
        }

        public RunId RunId { get; }
        public int ZoneId => FortressBossActor.ZoneId;
        public string BossId => FortressBossActor.BossId;
        public TimeSpan DefeatedAt { get; }
    }
}
