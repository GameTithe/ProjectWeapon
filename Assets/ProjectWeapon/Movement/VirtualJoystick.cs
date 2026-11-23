using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectWeapon.Movement
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IDragHandler, ICancelHandler
    {
        [SerializeField] private RectTransform thumb;

        private int? owner;
        public bool IsHeld => owner.HasValue;
        public Vector2 Direction { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || IsHeld || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            owner = eventData.pointerId;
            UpdatePosition(eventData);
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (owner == eventData.pointerId)
            {
                eventData.useDragThreshold = false;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (owner == eventData.pointerId)
            {
                UpdatePosition(eventData);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (owner == eventData.pointerId && eventData.button == PointerEventData.InputButton.Left)
            {
                Release();
            }
        }

        public void OnCancel(BaseEventData eventData) => Release();

        public void Release()
        {
            owner = null;
            Direction = Vector2.zero;
            if (thumb != null)
            {
                thumb.anchoredPosition = Vector2.zero;
            }
        }

        private void OnDisable() => Release();

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                Release();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Release();
            }
        }

        private void UpdatePosition(PointerEventData eventData)
        {
            var surface = (RectTransform)transform;
            Rect bounds = surface.rect;
            float radius = Mathf.Min(bounds.width, bounds.height) * 0.5f;
            if (radius <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    surface, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                return;
            }

            Direction = Vector2.ClampMagnitude((localPoint - bounds.center) / radius, 1f);
            if (thumb != null)
            {
                thumb.anchoredPosition = Direction * radius;
            }
        }
    }
}
