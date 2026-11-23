using UnityEngine;

namespace ProjectWeapon.ExtractionPresentation
{
    [DisallowMultipleComponent]
    public sealed class ExtractionFeedbackView : MonoBehaviour
    {
        private const int CircleSegments = 64;

        [SerializeField] private LineRenderer areaOutline;
        [SerializeField] private LineRenderer progressArc;
        [SerializeField] private LineRenderer completionMark;

        private static readonly Color IdleColor = new Color(0.62f, 0.62f, 0.62f);

        private float displayedRadius;
        private ExtractionFeedbackSnapshot displayed;

        // The three authored lines share this object's origin and occupy the local XY plane.
        public void Render(ExtractionFeedbackSnapshot feedback, float radius)
        {
            if (!areaOutline || !progressArc || !completionMark)
                return;

            if (!float.IsFinite(radius) || radius <= 0)
            {
                areaOutline.enabled = false;
                progressArc.enabled = false;
                completionMark.enabled = false;
                displayedRadius = 0;
                return;
            }

            bool radiusChanged = displayedRadius != radius;
            if (!radiusChanged && displayed.State == feedback.State && displayed.Progress == feedback.Progress)
                return;

            bool idle = feedback.State == ExtractionFeedbackState.Idle;
            bool showProgress = feedback.State == ExtractionFeedbackState.Channeling && feedback.Progress > 0;
            bool showCompletion = feedback.State == ExtractionFeedbackState.Succeeded;

            SetLine(areaOutline, radius * (idle ? 0.025f : 0.045f), idle ? IdleColor : Color.white);
            if (radiusChanged)
                DrawArc(areaOutline, radius, 1, true);
            areaOutline.enabled = true;

            progressArc.enabled = showProgress;
            if (showProgress)
            {
                SetLine(progressArc, radius * 0.07f, Color.white);
                DrawArc(progressArc, radius * 1.10f, feedback.Progress, false);
            }

            completionMark.enabled = showCompletion;
            if (showCompletion)
            {
                SetLine(completionMark, radius * 0.065f, Color.white);
                completionMark.loop = false;
                completionMark.positionCount = 3;
                completionMark.SetPosition(0, new Vector3(-0.18f, 1.28f) * radius);
                completionMark.SetPosition(1, new Vector3(-0.045f, 1.12f) * radius);
                completionMark.SetPosition(2, new Vector3(0.20f, 1.43f) * radius);
            }

            displayedRadius = radius;
            displayed = feedback;
        }

        private static void SetLine(LineRenderer line, float width, Color color)
        {
            line.useWorldSpace = false;
            line.alignment = LineAlignment.TransformZ;
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = color;
        }

        private static void DrawArc(LineRenderer line, float radius, float fraction, bool closed)
        {
            int count = closed ? CircleSegments : Mathf.CeilToInt(CircleSegments * fraction) + 1;
            line.loop = closed;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.Min((float)i / CircleSegments, fraction) * (2 * Mathf.PI);
                line.SetPosition(i, new Vector3(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius));
            }
        }
    }
}
