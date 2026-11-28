#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.WeaponSwitch;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;

namespace ProjectWeapon.ManualSkillUI
{
    /// <summary>Owns a direction gesture on the skill button without capturing other pointers.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class DirectionalSkillDrag : MonoBehaviour, IPointerDownHandler, IInitializePotentialDragHandler,
        IDragHandler, IPointerUpHandler, ICancelHandler
    {
        [SerializeField, Min(0.1f)] private float minimumCanvasDistance = 16f;

        private ManualSkillExecutor? executor;
        private EquippedWeaponSelection selection = null!;
        private RectTransform canvasSpace = null!;
        private RectTransform aimArea = null!;
        private DirectionalSkillArrow arrow = null!;
        private Func<TimeSpan> now = null!;
        private Func<WorldPoint2D> origin = null!;
        private Gesture? active;
        private bool inputAvailable = true;

        public bool IsAiming => active.HasValue;
        public event Action<Guid, ManualSkillActivationStatus>? Finished;

        /// <summary>Binds the right-hand button to its allowed release region and a non-intercepting graphic.</summary>
        public void Bind(ManualSkillExecutor skillExecutor, EquippedWeaponSelection equippedSelection,
            RectTransform allowedReleaseArea, DirectionalSkillArrow indicator, Func<TimeSpan> readTime,
            Func<WorldPoint2D> readOrigin)
        {
            if (skillExecutor == null)
                throw new ArgumentNullException(nameof(skillExecutor));
            if (equippedSelection == null)
                throw new ArgumentNullException(nameof(equippedSelection));
            if (!allowedReleaseArea)
                throw new ArgumentNullException(nameof(allowedReleaseArea));
            if (!indicator)
                throw new ArgumentNullException(nameof(indicator));
            if (readTime == null)
                throw new ArgumentNullException(nameof(readTime));
            if (readOrigin == null)
                throw new ArgumentNullException(nameof(readOrigin));
            Canvas? owner = GetComponentInParent<Canvas>()?.rootCanvas;
            if (!owner || allowedReleaseArea.GetComponentInParent<Canvas>()?.rootCanvas != owner
                || indicator.GetComponentInParent<Canvas>()?.rootCanvas != owner)
                throw new ArgumentException("The button, aim region and indicator must share a root Canvas.");

            CancelAim();
            executor = skillExecutor;
            selection = equippedSelection;
            canvasSpace = (RectTransform)owner!.transform;
            aimArea = allowedReleaseArea;
            arrow = indicator;
            now = readTime;
            origin = readOrigin;
            arrow.Hide();
        }

        public void SetInputAvailable(bool available)
        {
            inputAvailable = available;
            if (!inputAvailable)
                CancelAim();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || !inputAvailable || executor == null || active.HasValue || !aimArea.gameObject.activeInHierarchy
                || eventData.button != PointerEventData.InputButton.Left
                || !float.IsFinite(minimumCanvasDistance) || minimumCanvasDistance <= 0f
                || selection.ReadActiveWeapon() is not { } weapon
                || weapon.Definition.Skill is not ManualSkillPattern { AimKind: ManualSkillAimKind.Direction })
                return;

            Camera? camera = eventData.pressEventCamera;
            if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, eventData.position, camera)
                || !RectTransformUtility.RectangleContainsScreenPoint(aimArea, eventData.position, camera)
                || !TryCanvasPoint(eventData.position, camera, out Vector2 press))
                return;

            active = new Gesture(eventData, weapon.InstanceId, press, camera);
            arrow.Hide();
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (executor != null && eventData.button == PointerEventData.InputButton.Left)
                eventData.eligibleForClick = false;
            if (active is { } gesture && gesture.Owns(eventData))
                eventData.useDragThreshold = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (active is not { } gesture || !gesture.Owns(eventData))
                return;
            if (!aimArea.gameObject.activeInHierarchy || !TryCanvasPoint(eventData.position, gesture.Camera, out Vector2 point))
            {
                CancelAim();
                return;
            }
            Vector2 delta = point - gesture.Press;
            arrow.Show(canvasSpace, gesture.Press, delta, CanRelease(delta, eventData.position, gesture.Camera));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (active is not { } gesture || !gesture.Owns(eventData))
                return;
            if (gesture.TouchWasCanceled || !aimArea.gameObject.activeInHierarchy
                || !TryCanvasPoint(eventData.position, gesture.Camera, out Vector2 point))
            {
                CancelAim();
                return;
            }

            Vector2 delta = point - gesture.Press;
            if (!CanRelease(delta, eventData.position, gesture.Camera))
            {
                CancelAim();
                return;
            }

            ClearAim();
            ManualSkillActivationStatus status = executor!.TryActivate(gesture.Key, gesture.Weapon, now(), origin(),
                ManualSkillAim.Along(new WorldPoint2D(delta.x, delta.y)));
            Finished?.Invoke(gesture.Key, status);
        }

        public void OnCancel(BaseEventData eventData)
        {
            if (eventData is not PointerEventData pointer || (active is { } gesture && gesture.Owns(pointer)))
                CancelAim();
        }

        public void CancelAim()
        {
            if (active is not { } gesture)
                return;
            ClearAim();
            ManualSkillActivationStatus status = executor!.Cancel(gesture.Key);
            Finished?.Invoke(gesture.Key, status);
        }

        private void OnEnable() => InputSystem.onDeviceChange += HandleDeviceChange;

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= HandleDeviceChange;
            CancelAim();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                CancelAim();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                CancelAim();
        }

        private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (active is { } gesture && gesture.Device == device
                && (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected
                    || change == InputDeviceChange.Disabled || change == InputDeviceChange.HardReset
                    || change == InputDeviceChange.SoftReset))
                CancelAim();
        }

        private bool CanRelease(Vector2 delta, Vector2 screen, Camera? camera) =>
            delta.sqrMagnitude >= minimumCanvasDistance * minimumCanvasDistance
            && RectTransformUtility.RectangleContainsScreenPoint(aimArea, screen, camera);

        private bool TryCanvasPoint(Vector2 screen, Camera? camera, out Vector2 point) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasSpace, screen, camera, out point)
            && float.IsFinite(point.x) && float.IsFinite(point.y);

        private void ClearAim()
        {
            active = null;
            if (arrow)
                arrow.Hide();
        }

        private readonly struct Gesture
        {
            private readonly int pointerId;
            private readonly int touchId;
            private readonly TouchControl? touch;

            internal Gesture(PointerEventData start, WeaponInstanceId weapon, Vector2 press, Camera? camera)
            {
                var input = start as ExtendedPointerEventData;
                pointerId = start.pointerId;
                touchId = input?.touchId ?? 0;
                touch = input?.control?.parent as TouchControl;
                if (touch == null && input?.device is Touchscreen screen && screen.primaryTouch.touchId.ReadValue() == touchId)
                    touch = screen.primaryTouch;
                Device = input?.device;
                Key = Guid.NewGuid();
                Weapon = weapon;
                Press = press;
                Camera = camera;
            }

            internal Guid Key { get; }
            internal WeaponInstanceId Weapon { get; }
            internal Vector2 Press { get; }
            internal Camera? Camera { get; }
            internal InputDevice? Device { get; }
            internal bool TouchWasCanceled => touch != null && (touch.touchId.ReadValue() != touchId
                || touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled);

            internal bool Owns(PointerEventData pointer) => pointer.pointerId == pointerId
                && pointer.button == PointerEventData.InputButton.Left;
        }
    }
}
