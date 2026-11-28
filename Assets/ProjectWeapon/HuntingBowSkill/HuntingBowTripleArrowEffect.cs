#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.HuntingBowSkill
{
    public readonly struct HuntingBowArrowSnapshot
    {
        internal HuntingBowArrowSnapshot(ProjectileLifecycle projectile)
        {
            ApplicationId = projectile.ApplicationId;
            Position = projectile.Position;
            Velocity = projectile.Velocity;
            Lifetime = projectile.Lifetime;
            Elapsed = projectile.Elapsed;
        }

        public Guid ApplicationId { get; }
        public WorldPoint2D Position { get; }
        public WorldPoint2D Velocity { get; }
        public TimeSpan Lifetime { get; }
        public TimeSpan Elapsed { get; }
    }

    public readonly struct HuntingBowArrowImpact
    {
        internal HuntingBowArrowImpact(RunId runId, WeaponInstanceId weaponId, TimeSpan contactAt, DamageRequest damage)
        {
            RunId = runId;
            WeaponId = weaponId;
            ContactAt = contactAt;
            Damage = damage;
        }

        public RunId RunId { get; }
        public WeaponInstanceId WeaponId { get; }
        public TimeSpan ContactAt { get; }
        public DamageRequest Damage { get; }
    }

    /// <summary>Starts three independent KAN-72 projectiles and sweeps them in the owning run's Physics2D scene.</summary>
    public sealed class HuntingBowTripleArrowEffect : IManualSkillEffect
    {
        private readonly RunState run;
        private readonly RunId runId;
        private readonly PhysicsScene2D physicsScene;
        private readonly ContactFilter2D filter;
        private readonly int blockingLayers;
        private readonly int targetLayers;
        private readonly Func<Collider2D, TargetCandidate?> readTarget;
        private readonly List<Flight> flights = new List<Flight>();
        private readonly List<RaycastHit2D> contacts = new List<RaycastHit2D>();
        private readonly List<(int Order, HuntingBowArrowImpact Impact)> stepImpacts =
            new List<(int Order, HuntingBowArrowImpact Impact)>();
        private TimeSpan observedThrough;

        public HuntingBowTripleArrowEffect(RunState run, PhysicsScene2D physicsScene, LayerMask targetLayers,
            LayerMask blockingLayers, Func<Collider2D, TargetCandidate?> readTarget)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            RunSession session = run.CurrentRun ?? throw new InvalidOperationException("Start the run before binding arrows.");
            if (!physicsScene.IsValid())
                throw new ArgumentException("Arrows need the owning 2D physics scene.", nameof(physicsScene));

            runId = session.Id;
            observedThrough = session.StartedAt;
            this.physicsScene = physicsScene;
            this.blockingLayers = blockingLayers;
            this.targetLayers = targetLayers;
            this.readTarget = readTarget ?? throw new ArgumentNullException(nameof(readTarget));
            filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(targetLayers.value | blockingLayers.value);
        }

        public IReadOnlyList<HuntingBowArrowSnapshot> GetActiveArrows()
        {
            if (!IsLiveRun(observedThrough))
                return Array.Empty<HuntingBowArrowSnapshot>();

            var snapshot = new HuntingBowArrowSnapshot[flights.Count];
            for (int i = 0; i < flights.Count; i++)
                snapshot[i] = new HuntingBowArrowSnapshot(flights[i].Projectile);
            return snapshot;
        }

        public bool TryStart(ManualSkillActivationRequest request)
        {
            if (request.RunId != runId || request.StartedAt < observedThrough || !IsLiveRun(request.StartedAt)
                || run.Phase != RunPhase.Exploring || request.Pattern is not HuntingBowTripleArrowPattern pattern
                || !pattern.TryGetLifetime(out TimeSpan lifetime) || request.Aim.Kind != ManualSkillAimKind.Direction)
                return false;

            WorldPoint2D aim = request.Aim.Vector;
            if (!aim.IsFinite || aim.SquaredDistanceTo(default) == 0d)
                return false;

            var volley = new Flight[3];
            double aimRadians = Math.Atan2(aim.Y, aim.X);
            double spreadRadians = pattern.SpreadDegrees * (Math.PI / 180d);
            for (int i = 0; i < volley.Length; i++)
            {
                double angle = aimRadians + (i - 1) * spreadRadians;
                var velocity = new WorldPoint2D((float)(Math.Cos(angle) * pattern.Speed),
                    (float)(Math.Sin(angle) * pattern.Speed));
                var launch = new ProjectileLaunch(Guid.NewGuid(), request.AttackerId, request.Origin, velocity,
                    lifetime, pattern.Damage, ProjectileHitBehavior.StopOnTarget);
                var projectile = new ProjectileLifecycle();
                if (!projectile.TryLaunch(launch))
                    return false;
                volley[i] = new Flight(projectile, request.StartedAt, request.AttackerId, request.Weapon.InstanceId);
            }

            flights.AddRange(volley);
            observedThrough = request.StartedAt;
            return true;
        }

        /// <summary>Append this step's impacts in contact time order. Use the same run clock as the executor.</summary>
        public void Advance(TimeSpan now, List<HuntingBowArrowImpact> impacts)
        {
            if (impacts == null)
                throw new ArgumentNullException(nameof(impacts));
            if (!IsLiveRun(now))
            {
                flights.Clear();
                if (now > observedThrough)
                    observedThrough = now;
                return;
            }
            if (now < observedThrough)
                return;

            observedThrough = now;
            if (flights.Count == 0)
                return;

            stepImpacts.Clear();
            for (int i = 0; i < flights.Count; i++)
            {
                Flight flight = flights[i];
                if (AdvanceFlight(flight, now) is DamageRequest hit)
                {
                    TimeSpan contactAt = flight.StartedAt + flight.Projectile.Elapsed;
                    stepImpacts.Add((i, new HuntingBowArrowImpact(runId, flight.WeaponId, contactAt, hit)));
                }
            }
            flights.RemoveAll(flight => flight.Projectile.Phase != ProjectilePhase.Active);
            stepImpacts.Sort((left, right) =>
            {
                int order = left.Impact.ContactAt.CompareTo(right.Impact.ContactAt);
                return order != 0 ? order : left.Order.CompareTo(right.Order);
            });
            foreach (var impact in stepImpacts)
                impacts.Add(impact.Impact);
        }

        private bool IsLiveRun(TimeSpan now)
            => run.CurrentRun is { } session && session.Id == runId && run.Result == null
                && now < session.Deadline && physicsScene.IsValid();

        private DamageRequest? AdvanceFlight(Flight flight, TimeSpan now)
        {
            ProjectileLifecycle projectile = flight.Projectile;
            TimeSpan elapsed = now - flight.StartedAt;
            TimeSpan segmentEnd = elapsed < projectile.Lifetime ? elapsed : projectile.Lifetime;
            TimeSpan segment = segmentEnd - projectile.Elapsed;
            if (segment <= TimeSpan.Zero)
                return null;

            var origin = new Vector2(projectile.Position.X, projectile.Position.Y);
            WorldPoint2D velocity = projectile.Velocity;
            double speed = Math.Sqrt(velocity.SquaredDistanceTo(default));
            float distance = (float)(speed * segment.TotalSeconds);
            if (distance > 0f)
            {
                var direction = new Vector2((float)(velocity.X / speed), (float)(velocity.Y / speed));
                physicsScene.Raycast(origin, direction, distance, filter, contacts);
                if (TryFindContact(flight, distance, segmentEnd == projectile.Lifetime, out var contact))
                {
                    long hitTicks = (long)(segment.Ticks * Math.Min(1d, (double)contact.Distance / distance));
                    TimeSpan hitAt = projectile.Elapsed + TimeSpan.FromTicks(hitTicks);
                    if (contact.Target is TargetCandidate target)
                        return projectile.ContactTarget(projectile.ApplicationId, hitAt, target);
                    projectile.ContactWorld(projectile.ApplicationId, hitAt);
                    return null;
                }
            }

            projectile.Advance(projectile.ApplicationId, elapsed);
            return null;
        }

        private bool TryFindContact(Flight flight, float length, bool reachesLifetime,
            out (float Distance, TargetCandidate? Target) contact)
        {
            contact = (float.PositiveInfinity, null);
            foreach (RaycastHit2D hit in contacts)
            {
                if (hit.distance > contact.Distance || (reachesLifetime && hit.distance >= length))
                    break;

                Collider2D collider = hit.collider;
                if (collider == null)
                    continue;
                if (!collider.isTrigger && (blockingLayers & (1 << collider.gameObject.layer)) != 0)
                {
                    contact = (hit.distance, null);
                    return true;
                }

                if ((targetLayers & (1 << collider.gameObject.layer)) == 0
                    || readTarget(collider) is not { IsEligible: true } candidate
                    || StringComparer.Ordinal.Equals(candidate.TargetId, flight.AttackerId))
                    continue;

                if (hit.distance < contact.Distance
                    || StringComparer.Ordinal.Compare(candidate.TargetId, contact.Target?.TargetId) < 0)
                    contact = (hit.distance, candidate);
            }
            return contact.Target.HasValue;
        }

        private sealed class Flight
        {
            public Flight(ProjectileLifecycle projectile, TimeSpan startedAt, string attackerId, WeaponInstanceId weaponId)
            {
                Projectile = projectile;
                StartedAt = startedAt;
                AttackerId = attackerId;
                WeaponId = weaponId;
            }

            public ProjectileLifecycle Projectile { get; }
            public TimeSpan StartedAt { get; }
            public string AttackerId { get; }
            public WeaponInstanceId WeaponId { get; }
        }
    }
}
