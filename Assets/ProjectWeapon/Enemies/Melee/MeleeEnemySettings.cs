#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.Enemies
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Enemies/Melee Settings")]
    public sealed class MeleeEnemySettings : ScriptableObject
    {
        [SerializeField, Min(1f)] private float maxHealth = 24f;
        [SerializeField, Min(0f)] private float moveSpeed = 2.2f;
        [SerializeField, Min(0f)] private float approachDistance = 1.25f;
        [SerializeField, Min(0f)] private float attackRadius = 1.4f;
        [SerializeField, UnityEngine.Range(0f, 360f)] private float attackAngle = 90f;
        [SerializeField, Min(1)] private int windupMilliseconds = 450;
        [SerializeField, Min(1)] private int recoveryMilliseconds = 850;
        [SerializeField, Min(1f)] private float attackDamage = 8f;

        public float MaxHealth => maxHealth;
        public float MoveSpeed => moveSpeed;
        public float ApproachDistance => approachDistance;
        public float AttackRadius => attackRadius;
        public float AttackAngle => attackAngle;
        public TimeSpan Windup => TimeSpan.FromMilliseconds(windupMilliseconds);
        public TimeSpan Recovery => TimeSpan.FromMilliseconds(recoveryMilliseconds);
        public float AttackDamage => attackDamage;

        internal bool IsValid =>
            float.IsFinite(maxHealth) && maxHealth > 0f
            && float.IsFinite(moveSpeed) && moveSpeed > 0f
            && float.IsFinite(approachDistance) && approachDistance >= 0f
            && float.IsFinite(attackRadius) && attackRadius >= approachDistance
            && float.IsFinite(attackAngle) && attackAngle > 0f && attackAngle <= 360f
            && windupMilliseconds > 0 && recoveryMilliseconds > 0
            && float.IsFinite(attackDamage) && attackDamage > 0f;
    }
}
