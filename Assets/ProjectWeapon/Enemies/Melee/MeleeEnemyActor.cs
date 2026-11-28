#nullable enable

using System;
using ProjectWeapon.Combat;
using UnityEngine;

namespace ProjectWeapon.Enemies
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class MeleeEnemyActor : MonoBehaviour
    {
        private Rigidbody2D? body;
        private MeleeEnemy? brain;
        private TimeSpan? movementResumeAt;
        private double lastHitVisualAt = double.NegativeInfinity;

        public MeleeEnemy Brain => brain ?? throw new InvalidOperationException("Initialize the enemy before using it.");

        public void Initialize(string targetId, MeleeEnemySettings settings)
        {
            if (brain != null)
                throw new InvalidOperationException("The enemy has already been initialized.");

            brain = new MeleeEnemy(targetId, settings);
            body = GetComponent<Rigidbody2D>();
        }

        /// <summary>Call once per encounter physics step with its current clock and player snapshot.</summary>
        public void Advance(TimeSpan now, TargetCandidate player)
        {
            if (!isActiveAndEnabled)
                return;

            MeleeEnemy state = Brain;
            var position = body!.position;
            state.Advance(now, new WorldPoint2D(position.x, position.y), player);
            if (movementResumeAt is TimeSpan resumeAt)
            {
                if (now < resumeAt)
                {
                    body.linearVelocity = Vector2.zero;
                    return;
                }

                movementResumeAt = null;
            }

            WorldPoint2D velocity = state.MovementVelocity;
            body.linearVelocity = new Vector2(velocity.X, velocity.Y);
        }

        public bool ApplyDamage(DamageRequest request, TimeSpan now)
        {
            if (!Brain.ApplyDamage(request, now))
                return false;

            lastHitVisualAt = Time.unscaledTimeAsDouble;
            if (Brain.Phase == MeleeEnemyPhase.Dead)
                body!.linearVelocity = Vector2.zero;
            return true;
        }

        /// <summary>Apply a confirmed hit and gate the physical movement output until the latest stop expires.</summary>
        public bool ApplyMovementFreezeHit(MovementFreezeHit hit)
        {
            if (hit.ResumeAt <= hit.EffectiveAt || !ApplyDamage(hit.Damage, hit.EffectiveAt))
                return false;

            if (Brain.Phase != MeleeEnemyPhase.Dead)
            {
                if (!movementResumeAt.HasValue || hit.ResumeAt > movementResumeAt.Value)
                    movementResumeAt = hit.ResumeAt;
                body!.linearVelocity = Vector2.zero;
            }

            return true;
        }

        private void OnDisable()
        {
            if (body != null)
                body.linearVelocity = Vector2.zero;
        }

        // Debug shapes give the prototype scene a way to inspect state without art assets.
        private void OnDrawGizmosSelected()
        {
            if (brain == null)
                return;

            Gizmos.color = brain.Phase == MeleeEnemyPhase.Dead ? Color.gray
                : Time.unscaledTimeAsDouble - lastHitVisualAt < 0.15d ? Color.red
                : Color.green;
            Gizmos.DrawWireSphere(transform.position, 0.2f);
            if (brain.ActiveAttack is not { } attack)
                return;

            Gizmos.color = new Color(1f, 0.55f, 0.1f);
            AreaDamageRequest area = attack.Area;
            Vector3 origin = new Vector3(area.Origin.X, area.Origin.Y, transform.position.z);
            float start = Mathf.Atan2(area.Direction.Y, area.Direction.X) - area.AngleDegrees * Mathf.Deg2Rad / 2f;
            float step = area.AngleDegrees * Mathf.Deg2Rad / 32f;
            Vector3 previous = GetArcPoint(origin, area.Radius, start);
            Gizmos.DrawLine(origin, previous);
            for (int segment = 1; segment <= 32; segment++)
            {
                Vector3 point = GetArcPoint(origin, area.Radius, start + step * segment);
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
            Gizmos.DrawLine(previous, origin);
        }

        private static Vector3 GetArcPoint(Vector3 origin, float radius, float radians)
            => origin + new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * radius;
    }
}
