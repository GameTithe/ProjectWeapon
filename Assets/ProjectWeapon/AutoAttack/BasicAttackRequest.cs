#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.AutoAttack
{
    public readonly struct BasicAttackRequest
    {
        internal BasicAttackRequest(
            RunId runId, WeaponDefinitionId weaponId, BasicAttackPattern pattern,
            WorldPoint2D origin, TargetCandidate target, TimeSpan startedAt)
        {
            RunId = runId;
            WeaponId = weaponId;
            Pattern = pattern;
            Origin = origin;
            Target = target;
            StartedAt = startedAt;
        }

        public RunId RunId { get; }
        public WeaponDefinitionId WeaponId { get; }
        public BasicAttackPattern Pattern { get; }
        public WorldPoint2D Origin { get; }
        public TargetCandidate Target { get; }
        public TimeSpan StartedAt { get; }
    }
}
