#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.Enemies.Boss
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Enemies/Boss Shockwave")]
    public sealed class BossShockwaveSettings : ScriptableObject
    {
        [SerializeField, Min(1f)] private float maxHealth = 90f;
        [SerializeField, Min(0.01f)] private float aggroRadius = 2f;
        [SerializeField, Min(0.01f)] private float attackRadius = 1.6f;
        [SerializeField, Min(0f)] private float damage = 24f;
        [SerializeField, Min(1)] private int windupMilliseconds = 900;
        [SerializeField, Min(1)] private int recoveryMilliseconds = 1500;

        public float MaxHealth => maxHealth;
        public float AggroRadius => aggroRadius;
        public float AttackRadius => attackRadius;
        public float Damage => damage;
        public TimeSpan Windup => TimeSpan.FromMilliseconds(windupMilliseconds);
        public TimeSpan Recovery => TimeSpan.FromMilliseconds(recoveryMilliseconds);

        public bool IsValid => float.IsFinite(MaxHealth) && MaxHealth > 0
            && float.IsFinite(AggroRadius) && float.IsFinite(AttackRadius)
            && AggroRadius >= AttackRadius && AttackRadius > 0
            && float.IsFinite(Damage) && Damage > 0
            && windupMilliseconds > 0 && recoveryMilliseconds > 0;
    }
}
