#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public sealed class CircularStrike
    {
        internal CircularStrike(RunId runId, Guid applicationId, TimeSpan effectiveAt,
            IReadOnlyList<DamageRequest> hits)
        {
            RunId = runId;
            ApplicationId = applicationId;
            EffectiveAt = effectiveAt;
            Hits = hits;
        }

        public RunId RunId { get; }
        public Guid ApplicationId { get; }
        public TimeSpan EffectiveAt { get; }
        public IReadOnlyList<DamageRequest> Hits { get; }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Circular Strike")]
    public sealed class CircularStrikePattern : BasicAttackPattern
    {
        [SerializeField, Min(0f)] private float damage = 12f;

        public float Damage => damage;

        /// <summary>Builds one immediate damage batch centered on the attacker.</summary>
        public bool TryCreateStrike(BasicAttackRequest attack, Guid applicationId, string attackerId,
            IReadOnlyList<TargetCandidate> candidates, out CircularStrike? strike)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            strike = null;
            if (attack.Pattern != this || !IsValid || !float.IsFinite(damage) || damage < 0f
                || applicationId == Guid.Empty || string.IsNullOrWhiteSpace(attackerId)
                || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || attack.Origin.SquaredDistanceTo(attack.Target.Position) > (double)Range * Range)
                return false;

            AreaDamageRequest area = AreaDamageRequest.Circle(applicationId, attackerId, attack.Origin, Range, damage);
            strike = new CircularStrike(attack.RunId, applicationId, attack.StartedAt,
                AreaDamageSelector.FindTargets(area, candidates));
            return true;
        }
    }
}
