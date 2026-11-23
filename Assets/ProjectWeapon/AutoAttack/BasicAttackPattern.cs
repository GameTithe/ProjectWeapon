#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    /// <summary>Shared tuning for a concrete basic attack data asset.</summary>
    public abstract class BasicAttackPattern : ScriptableObject
    {
        [SerializeField, Min(0f)] private float range = 1.5f;
        [SerializeField, Min(1)] private int cooldownMilliseconds = 1000;

        public float Range => range;
        public TimeSpan Cooldown => TimeSpan.FromMilliseconds(cooldownMilliseconds);

        internal bool IsValid => float.IsFinite(range) && range >= 0f && cooldownMilliseconds > 0;
    }
}
