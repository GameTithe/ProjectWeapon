#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.ForestBoss
{
    public sealed class ForestBossSettings : ScriptableObject
    {
        [SerializeField, Min(1)] private int maxHealth = 205;
        [SerializeField, Min(1)] private int damage = 34;
        [SerializeField, Min(0.01f)] private float radius = 1.3f;
        [SerializeField, Min(0.01f)] private float detectionRange = 5.25f;
        [SerializeField, Min(1)] private int windupMilliseconds = 1000;
        [SerializeField, Min(1)] private int recoveryMilliseconds = 1650;

        public int MaxHealth => maxHealth;
        public int Damage => damage;
        public float Radius => radius;
        public float DetectionRange => detectionRange;
        public TimeSpan Windup => TimeSpan.FromMilliseconds(windupMilliseconds);
        public TimeSpan Recovery => TimeSpan.FromMilliseconds(recoveryMilliseconds);

        internal bool IsValid => maxHealth > 0 && damage > 0 && float.IsFinite(radius) && radius > 0
            && float.IsFinite(detectionRange) && detectionRange > 0 && windupMilliseconds > 0 && recoveryMilliseconds > 0;
    }
}
