#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.Enemies
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Enemies/Ranged Settings")]
    public sealed class RangedEnemySettings : ScriptableObject
    {
        [SerializeField, Min(1f)] private float maxHealth = 18f;
        [SerializeField, Min(0.01f)] private float detectionRange = 4f;
        [SerializeField, Min(0.01f)] private float attackLength = 4f;
        [SerializeField, Min(0.01f)] private float attackWidth = 0.7f;
        [SerializeField, Min(1)] private int windupMilliseconds = 750;
        [SerializeField, Min(1)] private int recoveryMilliseconds = 1250;
        [SerializeField, Min(1f)] private float attackDamage = 6f;

        public float MaxHealth => maxHealth;
        public float DetectionRange => detectionRange;
        public float AttackLength => attackLength;
        public float AttackWidth => attackWidth;
        public TimeSpan Windup => TimeSpan.FromMilliseconds(windupMilliseconds);
        public TimeSpan Recovery => TimeSpan.FromMilliseconds(recoveryMilliseconds);
        public float AttackDamage => attackDamage;

        internal bool IsValid =>
            float.IsFinite(maxHealth) && maxHealth > 0f
            && float.IsFinite(detectionRange) && detectionRange > 0f
            && float.IsFinite(attackLength) && attackLength >= detectionRange
            && float.IsFinite(attackWidth) && attackWidth > 0f
            && windupMilliseconds > 0 && recoveryMilliseconds > 0
            && float.IsFinite(attackDamage) && attackDamage > 0f;
    }
}
