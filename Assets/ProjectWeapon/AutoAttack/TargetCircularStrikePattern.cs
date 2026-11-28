#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Target Circular Strike")]
    public sealed class TargetCircularStrikePattern : BasicAttackPattern
    {
        [SerializeField, Min(0f)] private float radius = 1.25f;
        [SerializeField, Min(0f)] private float damage = 18f;

        public float Radius => radius;
        public float Damage => damage;

        /// <summary>Builds an immediate strike at the target position locked by the accepted attack.</summary>
        public bool TryCreateStrike(BasicAttackRequest attack, Guid applicationId, string attackerId,
            IReadOnlyList<TargetCandidate> candidates, out CircularStrike? strike)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            strike = null;
            if (attack.Pattern != this || !IsValid || !float.IsFinite(radius) || radius < 0f
                || !float.IsFinite(damage) || damage < 0f || applicationId == Guid.Empty
                || string.IsNullOrWhiteSpace(attackerId) || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || attack.Origin.SquaredDistanceTo(attack.Target.Position) > (double)Range * Range)
                return false;

            AreaDamageRequest area = AreaDamageRequest.Circle(applicationId, attackerId, attack.Target.Position, radius, damage);
            IReadOnlyList<DamageRequest> hits = AreaDamageSelector.FindTargets(area, candidates);
            foreach (DamageRequest hit in hits)
            {
                if (!StringComparer.Ordinal.Equals(hit.TargetId, attack.Target.TargetId))
                    continue;

                strike = new CircularStrike(attack.RunId, applicationId, attack.StartedAt, hits);
                return true;
            }

            return false;
        }
    }
}
