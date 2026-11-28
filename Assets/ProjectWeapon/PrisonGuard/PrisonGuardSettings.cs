#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.PrisonGuard
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Enemies/Prison Guard Settings")]
    public sealed class PrisonGuardSettings : ScriptableObject
    {
        [SerializeField, Min(1f)] private float maxHealth = 160f;
        [SerializeField, Min(.01f)] private float detectionRange = 4.5f;
        [SerializeField, Min(.01f)] private float attackLength = 3f;
        [SerializeField, Min(.01f)] private float attackWidth = 1f;
        [SerializeField, Min(1)] private int windupMilliseconds = 900;
        [SerializeField, Min(1)] private int recoveryMilliseconds = 1800;
        [SerializeField, Min(1f)] private float attackDamage = 30f;

        public float MaxHealth => maxHealth;
        public float DetectionRange => detectionRange;
        public float AttackLength => attackLength;
        public float AttackWidth => attackWidth;
        public TimeSpan Windup => TimeSpan.FromMilliseconds(windupMilliseconds);
        public TimeSpan Recovery => TimeSpan.FromMilliseconds(recoveryMilliseconds);
        public float AttackDamage => attackDamage;

        internal bool IsValid => IsPositive(maxHealth) && IsPositive(detectionRange) && IsPositive(attackLength)
            && IsPositive(attackWidth) && IsPositive(attackDamage) && windupMilliseconds > 0 && recoveryMilliseconds > 0;

        private static bool IsPositive(float value) => float.IsFinite(value) && value > 0f;
    }
}
