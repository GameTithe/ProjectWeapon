#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.Zone5Sanctum
{
    public sealed class SanctumBossSettings : ScriptableObject
    {
        [SerializeField, Min(1)] private int maxHealth = 300;
        [SerializeField, Min(1)] private int damage = 42;
        [SerializeField, Min(.01f)] private float innerRadius = 1.35f;
        [SerializeField, Min(.01f)] private float outerRadius = 3.1f;
        [SerializeField, Min(.01f)] private float detectionRange = 5.2f;
        [SerializeField, Min(1)] private int windupMilliseconds = 1050;
        [SerializeField, Min(1)] private int recoveryMilliseconds = 1900;

        public int MaxHealth => maxHealth;
        public int Damage => damage;
        public float InnerRadius => innerRadius;
        public float OuterRadius => outerRadius;
        public float DetectionRange => detectionRange;
        public TimeSpan Windup => TimeSpan.FromMilliseconds(windupMilliseconds);
        public TimeSpan Recovery => TimeSpan.FromMilliseconds(recoveryMilliseconds);

        internal bool IsValid => maxHealth > 0 && damage > 0
            && float.IsFinite(innerRadius) && float.IsFinite(outerRadius) && innerRadius > 0f && outerRadius > innerRadius
            && float.IsFinite(detectionRange) && detectionRange > 0f && windupMilliseconds > 0 && recoveryMilliseconds > 0;
    }
}
