#nullable enable

using UnityEngine;

namespace ProjectWeapon.ManualSkillUI
{
    /// <summary>Draws the current drag direction in the host Canvas without taking raycasts.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class DirectionalSkillArrow : UnityEngine.UI.MaskableGraphic
    {
        private const float MaxLength = 68f;
        [SerializeField] private Color validColor = new Color32(255, 216, 149, 255);
        [SerializeField] private Color canceledColor = new Color32(152, 163, 183, 255);
        private Vector2 start;
        private Vector2 end;
        private bool valid;

        public bool IsVisible { get; private set; }
        public Vector2 Direction { get; private set; }

        protected override void OnEnable()
        {
            base.OnEnable();
            raycastTarget = false;
        }

        internal void Show(RectTransform canvasSpace, Vector2 press, Vector2 delta, bool canRelease)
        {
            Direction = delta.normalized;
            IsVisible = delta.sqrMagnitude > 0f;
            start = rectTransform.InverseTransformPoint(canvasSpace.TransformPoint(press));
            end = rectTransform.InverseTransformPoint(canvasSpace.TransformPoint(press + Vector2.ClampMagnitude(delta, MaxLength)));
            valid = canRelease;
            SetVerticesDirty();
        }

        internal void Hide()
        {
            if (!IsVisible)
                return;

            Direction = Vector2.zero;
            IsVisible = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper helper)
        {
            helper.Clear();
            if (!IsVisible)
                return;

            Vector2 offset = end - start;
            float length = offset.magnitude;
            if (length < 0.01f)
                return;

            Vector2 along = offset / length;
            Vector2 normal = new Vector2(-along.y, along.x);
            float headLength = Mathf.Min(11f, length * 0.7f);
            float headHalfWidth = Mathf.Min(6f, length * 0.4f);
            float shaftHalfWidth = Mathf.Min(1.75f, headHalfWidth);
            Vector2 neck = end - along * headLength;
            Color32 tint = valid ? validColor : canceledColor;
            helper.AddVert(start - normal * shaftHalfWidth, tint, Vector2.zero);
            helper.AddVert(start + normal * shaftHalfWidth, tint, Vector2.zero);
            helper.AddVert(neck + normal * shaftHalfWidth, tint, Vector2.zero);
            helper.AddVert(neck - normal * shaftHalfWidth, tint, Vector2.zero);
            helper.AddVert(neck - normal * headHalfWidth, tint, Vector2.zero);
            helper.AddVert(neck + normal * headHalfWidth, tint, Vector2.zero);
            helper.AddVert(end, tint, Vector2.zero);
            helper.AddTriangle(0, 1, 2);
            helper.AddTriangle(0, 2, 3);
            helper.AddTriangle(4, 5, 6);
        }
    }
}
