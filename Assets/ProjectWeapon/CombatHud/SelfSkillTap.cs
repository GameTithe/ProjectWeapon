#nullable enable

using System;
using ProjectWeapon.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;

namespace ProjectWeapon.CombatHud
{
    [RequireComponent(typeof(RectTransform))]
    internal sealed class SelfSkillTap : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, ICancelHandler
    {
        private Func<WeaponInstanceId?>? begin;
        private Action<Guid, WeaponInstanceId>? activate;
        private Action<Guid>? cancel;
        private Gesture? owner;

        internal void Bind(Func<WeaponInstanceId?> capture, Action<Guid, WeaponInstanceId> complete, Action<Guid> abandon)
        {
            Cancel();
            begin = capture;
            activate = complete;
            cancel = abandon;
        }

        public void OnPointerDown(PointerEventData pointer)
        {
            if (owner.HasValue || pointer.button != PointerEventData.InputButton.Left || begin?.Invoke() is not { } id)
                return;

            owner = new Gesture(pointer, id);
        }

        public void OnPointerUp(PointerEventData pointer)
        {
            if (owner is not { } gesture || pointer.pointerId != gesture.PointerId
                || pointer.button != PointerEventData.InputButton.Left)
                return;

            owner = null;
            if (gesture.WasCanceled || !RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)transform, pointer.position, gesture.Camera))
                cancel!(gesture.Key);
            else
                activate!(gesture.Key, gesture.Weapon);
        }

        public void OnCancel(BaseEventData data)
        {
            if (data is not PointerEventData pointer || owner?.PointerId == pointer.pointerId)
                Cancel();
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= HandleDeviceChange;
            Cancel();
        }
        private void OnEnable() => InputSystem.onDeviceChange += HandleDeviceChange;


        private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (owner is { } gesture && gesture.Device == device
                && (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected
                    || change == InputDeviceChange.Disabled || change == InputDeviceChange.HardReset
                    || change == InputDeviceChange.SoftReset))
                Cancel();
        }

        internal void Cancel()
        {
            if (owner is not { } gesture)
                return;

            owner = null;
            cancel!(gesture.Key);
        }

        private readonly struct Gesture
        {
            private readonly int touchId;
            private readonly TouchControl? touch;

            internal Gesture(PointerEventData pointer, WeaponInstanceId weapon)
            {
                var input = pointer as ExtendedPointerEventData;
                PointerId = pointer.pointerId;
                Camera = pointer.pressEventCamera;
                touchId = input?.touchId ?? 0;
                touch = input?.control?.parent as TouchControl;
                if (touch == null && input?.device is Touchscreen screen && screen.primaryTouch.touchId.ReadValue() == touchId)
                    touch = screen.primaryTouch;

                Device = input?.device;
                Key = Guid.NewGuid();
                Weapon = weapon;
            }

            internal int PointerId { get; }
            internal Camera? Camera { get; }
            internal InputDevice? Device { get; }
            internal Guid Key { get; }
            internal WeaponInstanceId Weapon { get; }
            internal bool WasCanceled => touch != null && (touch.touchId.ReadValue() != touchId
                || touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled);
        }
    }
}
