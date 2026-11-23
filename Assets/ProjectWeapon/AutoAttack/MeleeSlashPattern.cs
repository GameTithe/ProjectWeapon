#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public sealed class MeleeSlashSwing
    {
        internal MeleeSlashSwing(RunId runId, Guid applicationId, TimeSpan effectiveAt,
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

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Melee Slash")]
    public sealed class MeleeSlashPattern : BasicAttackPattern
    {
        [SerializeField, UnityEngine.Range(0f, 360f)] private float arcDegrees = 90f;
        [SerializeField, Min(0f)] private float damage = 10f;

        public float ArcDegrees => arcDegrees;
        public float Damage => damage;

        /// <summary>Builds one immediate damage batch aimed at the scheduler's selected target.</summary>
        public bool TryCreateSwing(BasicAttackRequest attack, Guid applicationId, string attackerId,
            IReadOnlyList<TargetCandidate> candidates, out MeleeSlashSwing? swing)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            swing = null;
            if (attack.Pattern != this || !IsValid || !float.IsFinite(arcDegrees) || arcDegrees < 0f || arcDegrees > 360f
                || !float.IsFinite(damage) || damage < 0f || applicationId == Guid.Empty
                || string.IsNullOrWhiteSpace(attackerId) || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || attack.Origin.SquaredDistanceTo(attack.Target.Position) > (double)Range * Range)
                return false;

            var direction = new WorldPoint2D(
                attack.Target.Position.X - attack.Origin.X,
                attack.Target.Position.Y - attack.Origin.Y);
            AreaDamageRequest area = direction.X == 0f && direction.Y == 0f
                ? AreaDamageRequest.Circle(applicationId, attackerId, attack.Origin, radius: 0f, amount: damage)
                : AreaDamageRequest.Sector(applicationId, attackerId, attack.Origin, Range, damage, direction, arcDegrees);
            swing = new MeleeSlashSwing(attack.RunId, applicationId, attack.StartedAt,
                AreaDamageSelector.FindTargets(area, candidates));
            return true;
        }
    }
}
