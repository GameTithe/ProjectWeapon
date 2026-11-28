#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public readonly struct FrostSpearStrike
    {
        internal FrostSpearStrike(RunId runId, TimeSpan effectiveAt, WorldPoint2D origin, WorldPoint2D end,
            IReadOnlyList<MovementFreezeHit> hits)
        {
            RunId = runId;
            EffectiveAt = effectiveAt;
            Origin = origin;
            End = end;
            Hits = hits;
        }

        public RunId RunId { get; }
        public TimeSpan EffectiveAt { get; }
        public WorldPoint2D Origin { get; }
        public WorldPoint2D End { get; }
        public IReadOnlyList<MovementFreezeHit> Hits { get; }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Frost Spear")]
    public sealed class FrostSpearPattern : BasicAttackPattern
    {
        [SerializeField, Min(0.01f)] private float damage = 14f;
        [SerializeField, Min(1)] private int freezeMilliseconds = 900;

        /// <summary>Resolve one straight, instantaneous sweep against the encounter's current colliders.</summary>
        public bool TryCreateStrike(BasicAttackRequest attack, Guid applicationId, string attackerId,
            PhysicsScene2D physics, LayerMask solidLayers, Func<Collider2D, TargetCandidate?> resolveTarget,
            out FrostSpearStrike strike)
        {
            if (resolveTarget == null)
                throw new ArgumentNullException(nameof(resolveTarget));

            strike = default;
            double distanceSquared = attack.Origin.SquaredDistanceTo(attack.Target.Position);
            TimeSpan freezeDuration = TimeSpan.FromMilliseconds(freezeMilliseconds);
            if (attack.Pattern != this || !IsValid || !physics.IsValid() || !attack.Origin.IsFinite
                || !attack.Target.IsEligible || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || distanceSquared > (double)Range * Range || !float.IsFinite(damage) || damage <= 0f
                || freezeDuration <= TimeSpan.Zero || attack.StartedAt > TimeSpan.MaxValue - freezeDuration)
                return false;

            WorldPoint2D end = attack.Origin;
            if (distanceSquared > 0)
            {
                double scale = Range / Math.Sqrt(distanceSquared);
                end = new WorldPoint2D(
                    (float)(attack.Origin.X + ((double)attack.Target.Position.X - attack.Origin.X) * scale),
                    (float)(attack.Origin.Y + ((double)attack.Target.Position.Y - attack.Origin.Y) * scale));
            }

            if (!end.IsFinite)
                return false;

            var contacts = new ProjectileLifecycle();
            // Every contact in this instantaneous sweep occurs at elapsed zero. Use the shared
            // piercing contact lifecycle to retain eligibility, deduplication and solid termination.
            if (!contacts.TryLaunch(new ProjectileLaunch(applicationId, attackerId, attack.Origin, default,
                    TimeSpan.FromTicks(1), damage, ProjectileHitBehavior.ContinueThroughTargets)))
                return false;

            var hits = new List<MovementFreezeHit>();
            TimeSpan resumeAt = attack.StartedAt + freezeDuration;
            var origin = new Vector2(attack.Origin.X, attack.Origin.Y);

            void AcceptTarget(Collider2D collider)
            {
                if (resolveTarget(collider) is TargetCandidate target
                    && contacts.ContactTarget(applicationId, TimeSpan.Zero, target) is DamageRequest hit)
                    hits.Add(new MovementFreezeHit(hit, attack.StartedAt, resumeAt));
            }

            if (distanceSquared == 0)
            {
                var overlaps = new List<Collider2D>();
                physics.OverlapPoint(origin, ContactFilter2D.noFilter, overlaps);
                if (!overlaps.Exists(collider => IsSolid(collider, solidLayers)))
                    foreach (Collider2D collider in overlaps)
                        AcceptTarget(collider);
            }
            else
            {
                var sweep = new List<RaycastHit2D>();
                physics.Linecast(origin, new Vector2(end.X, end.Y), ContactFilter2D.noFilter, sweep);
                sweep.Sort((left, right) =>
                {
                    int distanceOrder = left.fraction.CompareTo(right.fraction);
                    return distanceOrder != 0 ? distanceOrder
                        : IsSolid(right.collider, solidLayers).CompareTo(IsSolid(left.collider, solidLayers));
                });
                foreach (RaycastHit2D contact in sweep)
                {
                    if (IsSolid(contact.collider, solidLayers))
                    {
                        contacts.ContactWorld(applicationId, TimeSpan.Zero);
                        end = new WorldPoint2D(contact.point.x, contact.point.y);
                        break;
                    }
                    AcceptTarget(contact.collider);
                }
            }

            strike = new FrostSpearStrike(attack.RunId, attack.StartedAt, attack.Origin, end, hits.AsReadOnly());
            return true;
        }

        private static bool IsSolid(Collider2D collider, LayerMask layers)
            => !collider.isTrigger && (layers.value & (1 << collider.gameObject.layer)) != 0;
    }
}
