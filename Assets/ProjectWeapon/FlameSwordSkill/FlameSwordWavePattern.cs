#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.FlameSwordSkill
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Skills/Flame Sword Wave")]
    public sealed class FlameSwordWavePattern : ManualSkillPattern
    {
        private static readonly WeaponDefinitionId FlameSword = new WeaponDefinitionId("flame_sword");

        [SerializeField, Range(0f, 360f)] private float arcDegrees = 65f;
        [SerializeField, Min(0f)] private float damage = 38f;
        [SerializeField, Min(1)] private int windupMilliseconds = 220;

        internal bool TryCreateWave(ManualSkillActivationRequest activation, out FlameSwordWave wave)
        {
            wave = default;
            WeaponDefinition definition = activation.Weapon.Definition;
            WorldPoint2D direction = activation.Aim.Vector;
            if (activation.Pattern != this || !definition || definition.Skill != this
                || !definition.Id.Equals(FlameSword) || definition.Grade != 2 || definition.Kind != WeaponKind.Sword
                || AimKind != ManualSkillAimKind.Direction || activation.Aim.Kind != ManualSkillAimKind.Direction
                || activation.GestureId == Guid.Empty || !activation.Weapon.InstanceId.IsValid
                || string.IsNullOrWhiteSpace(activation.AttackerId) || !activation.Origin.IsFinite
                || !direction.IsFinite || (direction.X == 0 && direction.Y == 0)
                || !float.IsFinite(MaxAimDistance) || MaxAimDistance <= 0
                || !float.IsFinite(arcDegrees) || arcDegrees <= 0 || arcDegrees > 360
                || !float.IsFinite(damage) || damage <= 0 || windupMilliseconds <= 0)
                return false;

            TimeSpan windup = TimeSpan.FromMilliseconds(windupMilliseconds);
            if (activation.StartedAt > TimeSpan.MaxValue - windup)
                return false;

            AreaDamageRequest area = AreaDamageRequest.Sector(activation.GestureId, activation.AttackerId,
                activation.Origin, MaxAimDistance, damage, direction, arcDegrees);
            wave = new FlameSwordWave(activation, activation.StartedAt + windup, area);
            return true;
        }
    }
}
