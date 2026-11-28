#nullable enable

using System.Collections.Generic;
using ProjectWeapon.Combat;
using UnityEngine;

namespace ProjectWeapon.Art.EnemyCues
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class EnemyCueView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer? impact;
        [SerializeField, Min(0f)] private float impactDuration = 0.12f;
        [SerializeField] private Vector2 impactOffset = new Vector2(0.22f, 0.28f);

        private LineRenderer? outline;
        private double hideImpactAt;

        private void Awake()
        {
            outline = GetComponent<LineRenderer>();
            ClearTelegraph();
            HideImpact();
        }

        /// <summary>Uses the locked attack geometry supplied by the melee enemy.</summary>
        public bool ShowSector(AreaDamageRequest attack)
        {
            if (attack.Shape != DamageAreaShape.Sector || !attack.Origin.IsFinite || !attack.Direction.IsFinite
                || (attack.Direction.X == 0 && attack.Direction.Y == 0) || !float.IsFinite(attack.Radius)
                || attack.Radius <= 0 || !float.IsFinite(attack.AngleDegrees)
                || attack.AngleDegrees <= 0 || attack.AngleDegrees > 360)
            {
                ClearTelegraph();
                return false;
            }

            int arcSegments = Mathf.CeilToInt(attack.AngleDegrees / 2.5f);
            bool completeCircle = attack.AngleDegrees == 360f;
            int arcStart = completeCircle ? 0 : 1;
            int arcVertices = completeCircle ? arcSegments : arcSegments + 1;
            var points = new Vector3[arcStart + arcVertices];
            if (!completeCircle)
                points[0] = Position(attack.Origin);

            float center = Mathf.Atan2(attack.Direction.Y, attack.Direction.X);
            float arc = attack.AngleDegrees * Mathf.Deg2Rad;
            for (int segment = 0; segment < arcVertices; segment++)
            {
                float angle = center - arc / 2f + arc * segment / arcSegments;
                points[segment + arcStart] = new Vector3(
                    attack.Origin.X + attack.Radius * Mathf.Cos(angle),
                    attack.Origin.Y + attack.Radius * Mathf.Sin(angle), transform.position.z);
            }

            return SetOutline(points);
        }

        /// <summary>Uses the four locked corners from the ranged enemy's attack strip.</summary>
        public bool ShowStrip(IReadOnlyList<WorldPoint2D> corners)
        {
            if (corners == null || corners.Count != 4)
            {
                ClearTelegraph();
                return false;
            }

            var points = new Vector3[4];
            for (int index = 0; index < points.Length; index++)
            {
                if (!corners[index].IsFinite)
                {
                    ClearTelegraph();
                    return false;
                }

                points[index] = Position(corners[index]);
            }

            return SetOutline(points);
        }

        public void ClearTelegraph()
        {
            if (outline != null)
                outline.enabled = false;
        }

        public void FlashHit(WorldPoint2D at)
        {
            if (impact == null || !isActiveAndEnabled || !at.IsFinite
                || !float.IsFinite(impactDuration) || impactDuration <= 0)
                return;

            impact.transform.position = Position(at) + new Vector3(impactOffset.x, impactOffset.y);
            impact.enabled = true;
            hideImpactAt = Time.unscaledTimeAsDouble + impactDuration;
        }

        private bool SetOutline(Vector3[] points)
        {
            if (outline == null || !isActiveAndEnabled)
                return false;

            outline.useWorldSpace = true;
            outline.loop = true;
            outline.positionCount = points.Length;
            outline.SetPositions(points);
            outline.enabled = true;
            return true;
        }

        private Vector3 Position(WorldPoint2D point) => new Vector3(point.X, point.Y, transform.position.z);

        private void Update()
        {
            if (impact != null && impact.enabled && Time.unscaledTimeAsDouble >= hideImpactAt)
                HideImpact();
        }

        private void OnDisable()
        {
            ClearTelegraph();
            HideImpact();
        }

        private void HideImpact()
        {
            if (impact != null)
                impact.enabled = false;
        }
    }
}
