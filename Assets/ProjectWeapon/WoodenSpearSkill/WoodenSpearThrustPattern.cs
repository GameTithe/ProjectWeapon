#nullable enable

using System;
using ProjectWeapon.ManualSkill;
using UnityEngine;

namespace ProjectWeapon.WoodenSpearSkill
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Skills/Wooden Spear Repeated Thrust")]
    public sealed class WoodenSpearThrustPattern : ManualSkillPattern
    {
        [SerializeField, Min(1)] private int intervalMilliseconds = 150;
        [SerializeField, Min(0.01f)] private float halfWidth = 0.25f;
        [SerializeField, Min(0.01f)] private float damage = 11f;

        public float Range => MaxAimDistance;
        public float HalfWidth => halfWidth;
        public float Damage => damage;
        public TimeSpan Interval => TimeSpan.FromMilliseconds(intervalMilliseconds);

        internal bool CanStartAt(TimeSpan startedAt)
            => AimKind == ManualSkillAimKind.Direction && float.IsFinite(Range) && Range > 0f
                && float.IsFinite(halfWidth) && halfWidth > 0f && float.IsFinite(damage) && damage > 0f
                && intervalMilliseconds > 0 && startedAt <= TimeSpan.MaxValue - Interval - Interval;
    }
}
