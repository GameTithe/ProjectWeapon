using ProjectWeapon.MovementInput;
using UnityEngine;

namespace ProjectWeapon.Movement
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private MovementSettings settings;
        [SerializeField] private MoveInputAdapter desktopInput;
        [SerializeField] private VirtualJoystick joystick;

        private Rigidbody2D body;

        public Vector2 Direction { get; private set; }

        public void BindJoystick(VirtualJoystick control)
        {
            if (joystick == control)
            {
                return;
            }

            if (joystick != null)
            {
                joystick.Release();
            }

            joystick = control;
        }

        private void Awake() => body = GetComponent<Rigidbody2D>();

        private void FixedUpdate()
        {
            if (settings == null)
            {
                Stop();
                return;
            }

            Direction = joystick != null && joystick.IsHeld
                ? settings.ApplyTouchDeadzone(joystick.Direction)
                : desktopInput != null ? desktopInput.Direction : Vector2.zero;
            body.linearVelocity = Direction * settings.Speed;
        }

        private void OnDisable() => Stop();

        private void Stop()
        {
            Direction = Vector2.zero;
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }
        }
    }
}
