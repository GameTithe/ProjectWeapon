#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.Zone4Fortress
{
    public sealed class FortressBossSettings : ScriptableObject
    {
        [SerializeField, Min(1)] private int maxHealth = 250;
        [SerializeField, Min(1)] private int damage = 38;
        [SerializeField, Min(.01f)] private float radius = 2.6f;
        [SerializeField, Range(1, 359)] private float angle = 120f;
        [SerializeField, Min(.01f)] private float detectionRange = 4.7f;
        [SerializeField, Min(1)] private int windupMilliseconds = 900;
        [SerializeField, Min(1)] private int recoveryMilliseconds = 1650;

        public int MaxHealth => maxHealth;
        public int Damage => damage;
        public float Radius => radius;
        public float Angle => angle;
        public float DetectionRange => detectionRange;
        public TimeSpan Windup => TimeSpan.FromMilliseconds(windupMilliseconds);
        public TimeSpan Recovery => TimeSpan.FromMilliseconds(recoveryMilliseconds);

        internal bool IsValid => maxHealth > 0 && damage > 0 && float.IsFinite(radius) && radius > 0f
            && float.IsFinite(detectionRange) && detectionRange > 0f
            && float.IsFinite(angle) && angle > 0f && angle < 360f
            && windupMilliseconds > 0 && recoveryMilliseconds > 0;
    }
}
