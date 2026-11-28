using System;
using ProjectWeapon.MovementInput;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
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
        private Vector2 lastMoveDirection = Vector2.down;
        private Vector2 activeDodgeDirection;
        private RunState dodgeRun;
        private RunId dodgeRunId;
        private TimeSpan dodgeRemaining;
        private float dodgeSpeed;
        private CollisionDetectionMode2D previousCollisionMode;

        public Vector2 Direction { get; private set; }
        public bool IsDodging => dodgeRun != null && dodgeRun.Result == null && dodgeRun.CurrentRun?.Id == dodgeRunId;
        public Vector2 DodgeDirection => IsDodging ? activeDodgeDirection : Vector2.zero;
        public PlayerDodge Dodge { get; } = new PlayerDodge();
        public event Action<RunId, TimeSpan> DodgeStarted;

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

        /// <summary>Reads the current eligible movement input before it moves the physics body.</summary>
        public Vector2 SampleDirection()
        {
            if (settings == null)
                return Vector2.zero;

            return joystick != null && joystick.IsHeld
                ? settings.ApplyTouchDeadzone(joystick.Direction)
                : desktopInput != null ? desktopInput.Direction : Vector2.zero;
        }

        /// <summary>Accepts one button press using the frame's run clock and notifies observers before motion.</summary>
        public bool TryDodge(RunState run, TimeSpan effectiveAt)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));

            if (!isActiveAndEnabled || settings == null || !body.simulated || body.bodyType != RigidbodyType2D.Dynamic
                || IsDodging || settings.DodgeDuration <= TimeSpan.Zero)
                return false;

            float speed = settings.DodgeDistance / (float)settings.DodgeDuration.TotalSeconds;
            if (!float.IsFinite(speed) || speed <= 0f
                || !Dodge.TryStart(run, effectiveAt, settings.DodgeDuration, settings.DodgeInvulnerability, settings.DodgeCooldown))
                return false;

            EndDodge();
            RememberDirection(SampleDirection());
            activeDodgeDirection = lastMoveDirection;
            dodgeRun = run;
            dodgeRunId = run.CurrentRun.Id;
            dodgeRemaining = settings.DodgeDuration;
            dodgeSpeed = speed;
            previousCollisionMode = body.collisionDetectionMode;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            DodgeStarted?.Invoke(dodgeRunId, effectiveAt);
            return true;
        }

        private void FixedUpdate()
        {
            if (settings == null)
            {
                Stop();
                return;
            }

            Direction = SampleDirection();
            RememberDirection(Direction);
            if (IsDodging && dodgeRemaining > TimeSpan.Zero)
            {
                TimeSpan timestep = TimeSpan.FromSeconds(Time.fixedDeltaTime);
                TimeSpan travelTime = dodgeRemaining < timestep ? dodgeRemaining : timestep;
                float portion = (float)travelTime.Ticks / timestep.Ticks;
                body.linearVelocity = activeDodgeDirection * (dodgeSpeed * portion);
                dodgeRemaining -= travelTime;
                return;
            }

            EndDodge();
            body.linearVelocity = Direction * settings.Speed;
        }

        private void OnDisable() => Stop();

        private void RememberDirection(Vector2 direction)
        {
            if (float.IsFinite(direction.x) && float.IsFinite(direction.y) && direction != Vector2.zero)
                lastMoveDirection = direction.normalized;
        }

        private void EndDodge()
        {
            if (dodgeRun == null)
                return;

            dodgeRun = null;
            dodgeRemaining = TimeSpan.Zero;
            if (body != null)
                body.collisionDetectionMode = previousCollisionMode;
        }

        private void Stop()
        {
            EndDodge();
            Direction = Vector2.zero;
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }
        }
    }
}
