using System;
using UnityEngine;

namespace ProjectWeapon.Movement
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Movement Settings")]
    public sealed class MovementSettings : ScriptableObject
    {
        [SerializeField, Min(0f)] private float speed = 4f;
        [SerializeField, Range(0f, 0.99f)] private float touchDeadzone = 0.15f;
        [SerializeField, Min(0f)] private float dodgeDistance = 2f;
        [SerializeField, Min(1)] private int dodgeDurationMilliseconds = 200;
        [SerializeField, Min(0)] private int dodgeInvulnerabilityMilliseconds = 120;
        [SerializeField, Min(1)] private int dodgeCooldownMilliseconds = 1250;

        public float Speed => float.IsFinite(speed) ? Mathf.Max(0f, speed) : 0f;
        public float DodgeDistance => dodgeDistance;
        public TimeSpan DodgeDuration => TimeSpan.FromMilliseconds(dodgeDurationMilliseconds);
        public TimeSpan DodgeInvulnerability => TimeSpan.FromMilliseconds(dodgeInvulnerabilityMilliseconds);
        public TimeSpan DodgeCooldown => TimeSpan.FromMilliseconds(dodgeCooldownMilliseconds);

        public Vector2 ApplyTouchDeadzone(Vector2 direction)
        {
            float deadzone = float.IsFinite(touchDeadzone) ? Mathf.Clamp(touchDeadzone, 0f, 0.99f) : 0f;
            float magnitude = direction.magnitude;
            if (magnitude <= deadzone)
            {
                return Vector2.zero;
            }

            return direction / magnitude * Mathf.InverseLerp(deadzone, 1f, magnitude);
        }
    }
}
