#nullable enable

using System;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.RepeaterBowSkill
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Skills/Repeater Bow Multi Arrow")]
    public sealed class RepeaterBowVolleyPattern : ManualSkillPattern
    {
        private static readonly WeaponDefinitionId RepeaterBowId = new WeaponDefinitionId("repeater_bow");
        public const int ArrowCount = 5;

        [SerializeField, Min(0f)] private float speed = 13f;
        [SerializeField, Min(0f)] private float damage = 8f;
        [SerializeField, Range(0f, 45f)] private float spreadStepDegrees = 10f;

        public float Speed => speed;
        public float Damage => damage;
        public float SpreadStepDegrees => spreadStepDegrees;

        internal bool TryGetLifetime(ManualSkillActivationRequest request, out TimeSpan lifetime)
        {
            lifetime = default;
            WeaponDefinition definition = request.Weapon.Definition;
            if (request.Pattern != this || !definition || definition.Skill != this || !definition.Id.Equals(RepeaterBowId)
                || definition.Grade != 2 || definition.Kind != WeaponKind.Bow || !request.Weapon.InstanceId.IsValid
                || AimKind != ManualSkillAimKind.Direction || request.Aim.Kind != ManualSkillAimKind.Direction
                || !float.IsFinite(MaxAimDistance) || MaxAimDistance <= 0
                || !float.IsFinite(speed) || speed <= 0 || !float.IsFinite(damage) || damage <= 0
                || !float.IsFinite(spreadStepDegrees) || spreadStepDegrees <= 0 || spreadStepDegrees > 45)
                return false;

            double ticks = (double)MaxAimDistance / speed * TimeSpan.TicksPerSecond;
            if (ticks < 1 || ticks >= long.MaxValue)
                return false;

            lifetime = TimeSpan.FromTicks((long)ticks);
            return true;
        }
    }
}
