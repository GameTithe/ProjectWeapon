#nullable enable

using System;
using ProjectWeapon.ManualSkill;
using UnityEngine;

namespace ProjectWeapon.HuntingBowSkill
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Skills/Hunting Bow Triple Arrow")]
    public sealed class HuntingBowTripleArrowPattern : ManualSkillPattern
    {
        [SerializeField, Min(0f)] private float range = 6f;
        [SerializeField, Min(0f)] private float speed = 10f;
        [SerializeField, Min(0f)] private float damage = 11f;
        [SerializeField, Range(0f, 90f)] private float spreadDegrees = 15f;

        public float Range => range;
        public float Speed => speed;
        public float Damage => damage;
        public float SpreadDegrees => spreadDegrees;

        internal bool TryGetLifetime(out TimeSpan lifetime)
        {
            lifetime = default;
            if (!float.IsFinite(range) || range <= 0f || !float.IsFinite(speed) || speed <= 0f
                || !float.IsFinite(damage) || damage < 0f || !float.IsFinite(spreadDegrees)
                || spreadDegrees < 0f || spreadDegrees > 90f)
                return false;

            double ticks = (double)range / speed * TimeSpan.TicksPerSecond;
            if (ticks < 1d || ticks >= long.MaxValue)
                return false;
            lifetime = TimeSpan.FromTicks((long)ticks);
            return true;
        }
    }
}
