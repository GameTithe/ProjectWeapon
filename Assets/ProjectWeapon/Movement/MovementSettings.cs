using UnityEngine;

namespace ProjectWeapon.Movement
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Movement Settings")]
    public sealed class MovementSettings : ScriptableObject
    {
        [SerializeField, Min(0f)] private float speed = 4f;
        [SerializeField, Range(0f, 0.99f)] private float touchDeadzone = 0.15f;

        public float Speed => float.IsFinite(speed) ? Mathf.Max(0f, speed) : 0f;

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
