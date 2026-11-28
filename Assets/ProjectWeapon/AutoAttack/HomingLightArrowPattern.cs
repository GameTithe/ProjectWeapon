#nullable enable

using System;
using ProjectWeapon.Combat;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Homing Light Arrow")]
    public sealed class HomingLightArrowPattern : BasicAttackPattern
    {
        [SerializeField, Min(0.01f)] private float speed = 10f;
        [SerializeField, Min(0f)] private float turnDegreesPerSecond = 540f;
        [SerializeField, Min(1)] private int lifetimeMilliseconds = 1200;
        [SerializeField, Min(0f)] private float damage = 22f;

        /// <summary>Starts one arrow from an inactive rental, locking the scheduler's selected target.</summary>
        public bool TryLaunch(BasicAttackRequest attack, Guid applicationId, string attackerId,
            HomingLightArrow projectile)
        {
            if (projectile == null)
                throw new ArgumentNullException(nameof(projectile));

            if (attack.Pattern != this || !IsValid || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attack.Target.TargetId, attackerId)
                || attack.Origin.SquaredDistanceTo(attack.Target.Position) > (double)Range * Range
                || !float.IsFinite(speed) || speed <= 0f
                || !float.IsFinite(turnDegreesPerSecond) || turnDegreesPerSecond < 0f)
                return false;

            return projectile.TryLaunch(applicationId, attackerId, attack.Origin, attack.Target, speed,
                turnDegreesPerSecond, TimeSpan.FromMilliseconds(lifetimeMilliseconds), damage);
        }
    }
}
