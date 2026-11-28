#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.SteelSpearSkill
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Skills/Steel Spear Charge")]
    public sealed class SteelSpearChargePattern : ManualSkillPattern
    {
        private static readonly WeaponDefinitionId SteelSpear = new WeaponDefinitionId("steel_spear");

        [SerializeField, Min(.01f)] private float dashDistance = 2.4f;
        [SerializeField, Min(1)] private int dashMilliseconds = 180;
        [SerializeField, Range(0f, 360f)] private float thrustArcDegrees = 45f;
        [SerializeField, Min(1)] private int thrustDamage = 44;

        internal bool TryCreateCharge(ManualSkillActivationRequest activation, WorldPoint2D position,
            out SteelSpearCharge charge)
        {
            charge = default;
            WeaponDefinition definition = activation.Weapon.Definition;
            WorldPoint2D direction = activation.Aim.Vector;
            if (activation.Pattern != this || !definition || definition.Skill != this || !SteelSpear.Equals(definition.Id)
                || definition.Grade != 2 || definition.Kind != WeaponKind.Spear
                || AimKind != ManualSkillAimKind.Direction || activation.Aim.Kind != ManualSkillAimKind.Direction
                || activation.GestureId == Guid.Empty || !activation.Weapon.InstanceId.IsValid
                || string.IsNullOrWhiteSpace(activation.AttackerId) || !position.IsFinite
                || !direction.IsFinite || (direction.X == 0 && direction.Y == 0)
                || !float.IsFinite(dashDistance) || dashDistance <= 0 || dashMilliseconds <= 0
                || !float.IsFinite(MaxAimDistance) || MaxAimDistance <= 0 || thrustDamage <= 0
                || !float.IsFinite(thrustArcDegrees) || thrustArcDegrees <= 0 || thrustArcDegrees > 180f)
                return false;

            TimeSpan duration = TimeSpan.FromMilliseconds(dashMilliseconds);
            if (activation.StartedAt > TimeSpan.MaxValue - duration)
                return false;

            // Direction has already been normalized by the manual skill executor.
            charge = new SteelSpearCharge(activation, position, duration, dashDistance, MaxAimDistance,
                thrustArcDegrees, thrustDamage);
            return true;
        }
    }
}
