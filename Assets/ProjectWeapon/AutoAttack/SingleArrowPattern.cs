#nullable enable

using System;
using ProjectWeapon.Combat;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Single Arrow")]
    public sealed class SingleArrowPattern : BasicAttackPattern
    {
        [SerializeField, Min(0f)] private float speed = 10f;
        [SerializeField, Min(1)] private int lifetimeMilliseconds = 550;
        [SerializeField, Min(0f)] private float damage = 8f;

        /// <summary>Starts one arrow from an inactive projectile rental.</summary>
        public bool TryLaunch(BasicAttackRequest attack, Guid applicationId, string attackerId,
            ProjectileLifecycle projectile)
        {
            if (projectile == null)
                throw new ArgumentNullException(nameof(projectile));

            if (attack.Pattern != this || !IsValid || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || !float.IsFinite(speed) || speed <= 0f)
                return false;

            double distanceSquared = attack.Origin.SquaredDistanceTo(attack.Target.Position);
            if (distanceSquared > (double)Range * Range)
                return false;

            WorldPoint2D velocity = default;
            if (distanceSquared > 0)
            {
                double scale = speed / Math.Sqrt(distanceSquared);
                velocity = new WorldPoint2D(
                    (float)(((double)attack.Target.Position.X - attack.Origin.X) * scale),
                    (float)(((double)attack.Target.Position.Y - attack.Origin.Y) * scale));
            }

            var launch = new ProjectileLaunch(applicationId, attackerId, attack.Origin, velocity,
                TimeSpan.FromMilliseconds(lifetimeMilliseconds), damage, ProjectileHitBehavior.StopOnTarget);
            return projectile.TryLaunch(launch);
        }
    }
}
