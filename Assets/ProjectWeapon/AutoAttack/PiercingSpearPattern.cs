#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public sealed class PiercingSpearThrust
    {
        internal PiercingSpearThrust(RunId runId, TimeSpan effectiveAt, WorldPoint2D origin, WorldPoint2D tip,
            IReadOnlyList<DamageRequest> hits)
        {
            RunId = runId;
            EffectiveAt = effectiveAt;
            Origin = origin;
            Tip = tip;
            Hits = hits;
        }

        public RunId RunId { get; }
        public TimeSpan EffectiveAt { get; }
        public WorldPoint2D Origin { get; }
        public WorldPoint2D Tip { get; }
        public IReadOnlyList<DamageRequest> Hits { get; }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Piercing Spear")]
    public sealed class PiercingSpearPattern : BasicAttackPattern
    {
        [SerializeField, Min(0f)] private float damage = 14f;
        [SerializeField, Min(0f)] private float halfWidth = 0.22f;

        public float Damage => damage;
        public float HalfWidth => halfWidth;

        public bool TryCreateThrust(BasicAttackRequest attack, Guid applicationId, string attackerId,
            IReadOnlyList<TargetCandidate> candidates, out PiercingSpearThrust? thrust)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            thrust = null;
            double rangeSquared = (double)Range * Range;
            double distanceSquared = attack.Origin.SquaredDistanceTo(attack.Target.Position);
            if (attack.Pattern != this || !IsValid || !float.IsFinite(damage) || damage < 0f ||
                !float.IsFinite(halfWidth) || halfWidth < 0f || applicationId == Guid.Empty ||
                string.IsNullOrWhiteSpace(attackerId) || !attack.Origin.IsFinite || !attack.Target.IsEligible ||
                StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId) || distanceSquared > rangeSquared)
                return false;

            double length = Math.Sqrt(distanceSquared);
            double directionX = length > 0 ? ((double)attack.Target.Position.X - attack.Origin.X) / length : 0;
            double directionY = length > 0 ? ((double)attack.Target.Position.Y - attack.Origin.Y) / length : 0;
            var tip = new WorldPoint2D((float)(attack.Origin.X + directionX * Range), (float)(attack.Origin.Y + directionY * Range));
            if (!tip.IsFinite)
                return false;

            TargetCandidate[] inside = candidates.Where(candidate => candidate.IsEligible && InThrust(candidate.Position))
                .OrderBy(candidate => attack.Origin.SquaredDistanceTo(candidate.Position))
                .ThenBy(candidate => candidate.TargetId, StringComparer.Ordinal)
                .ToArray();
            var area = AreaDamageRequest.Circle(applicationId, attackerId, attack.Origin, Range, damage);
            DamageRequest[] hits = AreaDamageSelector.FindTargets(area, inside).ToArray();
            thrust = new PiercingSpearThrust(attack.RunId, attack.StartedAt, attack.Origin, tip, Array.AsReadOnly(hits));
            return true;

            bool InThrust(WorldPoint2D position)
            {
                double x = (double)position.X - attack.Origin.X;
                double y = (double)position.Y - attack.Origin.Y;
                if (length == 0)
                    return x == 0 && y == 0;
                double along = directionX * x + directionY * y;
                return along >= 0 && Math.Abs(directionX * y - directionY * x) <= halfWidth;
            }
        }
    }
}
