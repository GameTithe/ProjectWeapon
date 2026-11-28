#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public readonly struct SpearThrust
    {
        internal SpearThrust(RunId runId, TimeSpan effectiveAt, WorldPoint2D origin, WorldPoint2D tip,
            DamageRequest hit)
        {
            RunId = runId;
            EffectiveAt = effectiveAt;
            Origin = origin;
            Tip = tip;
            Hit = hit;
        }

        public RunId RunId { get; }
        public TimeSpan EffectiveAt { get; }
        public WorldPoint2D Origin { get; }
        public WorldPoint2D Tip { get; }
        public DamageRequest Hit { get; }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Spear Thrust")]
    public sealed class SpearThrustPattern : BasicAttackPattern
    {
        [SerializeField, Min(0f)] private float damage = 10f;

        public float Damage => damage;

        /// <summary>Thrusts straight toward the target chosen by the scheduler and hits it immediately.</summary>
        public bool TryCreateThrust(BasicAttackRequest attack, Guid applicationId, string attackerId,
            out SpearThrust thrust)
        {
            thrust = default;
            if (attack.Pattern != this || !IsValid || !float.IsFinite(damage) || damage < 0f
                || applicationId == Guid.Empty || string.IsNullOrWhiteSpace(attackerId)
                || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || attack.Origin.SquaredDistanceTo(attack.Target.Position) > (double)Range * Range)
                return false;

            var contact = AreaDamageRequest.Circle(applicationId, attackerId, attack.Target.Position, radius: 0f,
                amount: damage);
            var hits = AreaDamageSelector.FindTargets(contact, new[] { attack.Target });
            if (hits.Count == 0)
                return false;

            thrust = new SpearThrust(attack.RunId, attack.StartedAt, attack.Origin, attack.Target.Position, hits[0]);
            return true;
        }
    }
}
