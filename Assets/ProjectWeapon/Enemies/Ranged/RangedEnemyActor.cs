#nullable enable

using System;
using ProjectWeapon.Combat;
using UnityEngine;

namespace ProjectWeapon.Enemies
{
    [DisallowMultipleComponent]
    public sealed class RangedEnemyActor : MonoBehaviour
    {
        private RangedEnemy? brain;
        private double lastHitVisualAt = double.NegativeInfinity;

        public RangedEnemy Brain => brain ?? throw new InvalidOperationException("Initialize the enemy before using it.");

        public void Initialize(string targetId, RangedEnemySettings settings)
        {
            if (brain != null)
                throw new InvalidOperationException("The enemy has already been initialized.");

            brain = new RangedEnemy(targetId, settings);
        }

        public void Advance(TimeSpan now, TargetCandidate player)
        {
            if (!isActiveAndEnabled)
                return;

            Vector3 position = transform.position;
            Brain.Advance(now, new WorldPoint2D(position.x, position.y), player);
        }

        public bool ApplyDamage(DamageRequest request, TimeSpan now)
        {
            if (!Brain.ApplyDamage(request, now))
                return false;

            lastHitVisualAt = Time.unscaledTimeAsDouble;
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (brain == null)
                return;

            Gizmos.color = brain.Phase == RangedEnemyPhase.Dead ? Color.gray
                : Time.unscaledTimeAsDouble - lastHitVisualAt < 0.15d ? Color.red
                : Color.green;
            Gizmos.DrawWireCube(transform.position, new Vector3(0.42f, 0.42f, 0f));
            if (brain.ActiveAttack is not { } attack)
                return;

            Gizmos.color = new Color(1f, 0.55f, 0.1f);
            float z = transform.position.z;
            Vector3 nearLeft = AtZ(attack.Area.GetCorner(farEnd: false, leftSide: true), z);
            Vector3 farLeft = AtZ(attack.Area.GetCorner(farEnd: true, leftSide: true), z);
            Vector3 farRight = AtZ(attack.Area.GetCorner(farEnd: true, leftSide: false), z);
            Vector3 nearRight = AtZ(attack.Area.GetCorner(farEnd: false, leftSide: false), z);
            Gizmos.DrawLine(nearLeft, farLeft);
            Gizmos.DrawLine(farLeft, farRight);
            Gizmos.DrawLine(farRight, nearRight);
            Gizmos.DrawLine(nearRight, nearLeft);
        }

        private static Vector3 AtZ(WorldPoint2D point, float z) => new Vector3(point.X, point.Y, z);
    }
}
