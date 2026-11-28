#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.RepeaterBowSkill
{
    public readonly struct RepeaterBowArrowSnapshot
    {
        internal RepeaterBowArrowSnapshot(ProjectileLifecycle projectile, TimeSpan launchedAt)
        {
            ApplicationId = projectile.ApplicationId;
            Position = projectile.Position;
            Velocity = projectile.Velocity;
            Lifetime = projectile.Lifetime;
            LaunchedAt = launchedAt;
        }

        public Guid ApplicationId { get; }
        public WorldPoint2D Position { get; }
        public WorldPoint2D Velocity { get; }
        public TimeSpan Lifetime { get; }
        public TimeSpan LaunchedAt { get; }
    }

    public readonly struct RepeaterBowArrowImpact
    {
        internal RepeaterBowArrowImpact(RunId runId, WeaponInstanceId weaponInstanceId, TimeSpan contactAt, DamageRequest damage)
        {
            RunId = runId;
            WeaponInstanceId = weaponInstanceId;
            ContactAt = contactAt;
            Damage = damage;
        }

        public RunId RunId { get; }
        public WeaponInstanceId WeaponInstanceId { get; }
        public TimeSpan ContactAt { get; }
        public DamageRequest Damage { get; }
    }

    /// <summary>Drives independent stop-on-hit projectiles in the bound run's Physics2D scene.</summary>
    public sealed class RepeaterBowVolleyEffect : IManualSkillEffect
    {
        private readonly RunState run;
        private readonly RunId runId;
        private readonly TimeSpan deadline;
        private readonly PhysicsScene2D physicsScene;
        private readonly int targetLayers;
        private readonly int blockingLayers;
        private readonly ContactFilter2D filter;
        private readonly Func<Collider2D, TargetCandidate?> readTarget;
        private readonly List<Flight> flights = new List<Flight>();
        private readonly List<RaycastHit2D> contacts = new List<RaycastHit2D>();
        private readonly List<(RepeaterBowArrowImpact Impact, int Order)> frameImpacts
            = new List<(RepeaterBowArrowImpact, int)>();
        private TimeSpan observedThrough;

        public RepeaterBowVolleyEffect(RunState run, PhysicsScene2D physicsScene,
            LayerMask targetLayers, LayerMask blockingLayers, Func<Collider2D, TargetCandidate?> readTarget)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            RunSession session = run.CurrentRun ?? throw new InvalidOperationException("Start the expedition before binding the volley.");
            if (!physicsScene.IsValid())
                throw new ArgumentException("An owning 2D physics scene is required.", nameof(physicsScene));

            runId = session.Id;
            deadline = session.Deadline;
            observedThrough = session.StartedAt;
            this.physicsScene = physicsScene;
            this.targetLayers = targetLayers;
            this.blockingLayers = blockingLayers;
            this.readTarget = readTarget ?? throw new ArgumentNullException(nameof(readTarget));
            filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(targetLayers.value | blockingLayers.value);
        }

        public IReadOnlyList<RepeaterBowArrowSnapshot> GetActiveArrows()
        {
            if (observedThrough >= deadline || !IsRunOpen())
                return Array.Empty<RepeaterBowArrowSnapshot>();

            var snapshots = new RepeaterBowArrowSnapshot[flights.Count];
            for (int i = 0; i < snapshots.Length; i++)
                snapshots[i] = new RepeaterBowArrowSnapshot(flights[i].Projectile, flights[i].LaunchedAt);
            return snapshots;
        }

        public bool TryStart(ManualSkillActivationRequest request)
        {
            if (request.RunId != runId || request.StartedAt < observedThrough || request.StartedAt >= deadline || !IsRunOpen()
                || run.Phase != RunPhase.Exploring || request.Pattern is not RepeaterBowVolleyPattern pattern
                || !pattern.TryGetLifetime(request, out TimeSpan lifetime))
                return false;

            WorldPoint2D aim = request.Aim.Vector;
            if (!aim.IsFinite || (aim.X == 0 && aim.Y == 0))
                return false;

            double aimRadians = Math.Atan2(aim.Y, aim.X);
            double stepRadians = pattern.SpreadStepDegrees * (Math.PI / 180d);
            var volley = new Flight[RepeaterBowVolleyPattern.ArrowCount];
            for (int i = 0; i < volley.Length; i++)
            {
                double angle = aimRadians + (i - volley.Length / 2) * stepRadians;
                var velocity = new WorldPoint2D((float)(Math.Cos(angle) * pattern.Speed),
                    (float)(Math.Sin(angle) * pattern.Speed));
                var launch = new ProjectileLaunch(Guid.NewGuid(), request.AttackerId, request.Origin,
                    velocity, lifetime, pattern.Damage, ProjectileHitBehavior.StopOnTarget);
                var projectile = new ProjectileLifecycle();
                if (!projectile.TryLaunch(launch))
                    return false;

                volley[i] = new Flight(projectile, request.StartedAt, request.Weapon.InstanceId, request.AttackerId);
            }
            flights.AddRange(volley);
            observedThrough = request.StartedAt;
            return true;
        }

        /// <summary>Call with the current run time. Append successful contacts in effective time order.</summary>
        public void Advance(TimeSpan now, List<RepeaterBowArrowImpact> impacts)
        {
            if (impacts == null)
                throw new ArgumentNullException(nameof(impacts));
            if (!IsRunOpen())
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

            bool reachesDeadline = now >= deadline;
            TimeSpan sweepThrough = reachesDeadline ? deadline : now;
            frameImpacts.Clear();
            for (int i = 0; i < flights.Count; i++)
            {
                Flight flight = flights[i];
                if (AdvanceFlight(flight, sweepThrough, reachesDeadline) is DamageRequest damage)
                {
                    var impact = new RepeaterBowArrowImpact(runId, flight.WeaponInstanceId,
                        flight.LaunchedAt + flight.Projectile.Elapsed, damage);
                    frameImpacts.Add((impact, i));
                }
            }
            if (reachesDeadline)
                flights.Clear();
            else
                flights.RemoveAll(flight => flight.Projectile.Phase != ProjectilePhase.Active);
            frameImpacts.Sort((left, right) =>
            {
                int time = left.Impact.ContactAt.CompareTo(right.Impact.ContactAt);
                return time != 0 ? time : left.Order.CompareTo(right.Order);
            });
            foreach (var entry in frameImpacts)
                impacts.Add(entry.Impact);
        }

        private DamageRequest? AdvanceFlight(Flight flight, TimeSpan sweepThrough, bool reachesDeadline)
        {
            ProjectileLifecycle projectile = flight.Projectile;
            TimeSpan elapsed = sweepThrough - flight.LaunchedAt;
            TimeSpan stepEnd = elapsed < projectile.Lifetime ? elapsed : projectile.Lifetime;
            TimeSpan step = stepEnd - projectile.Elapsed;
            if (step <= TimeSpan.Zero)
                return null;

            WorldPoint2D position = projectile.Position;
            WorldPoint2D velocity = projectile.Velocity;
            double speed = Math.Sqrt(velocity.SquaredDistanceTo(default));
            float distance = (float)(speed * step.TotalSeconds);
            if (distance > 0)
            {
                var direction = new Vector2((float)(velocity.X / speed), (float)(velocity.Y / speed));
                contacts.Clear();
                physicsScene.Raycast(new Vector2(position.X, position.Y), direction, distance, filter, contacts);
                bool excludesEndpoint = reachesDeadline || stepEnd == projectile.Lifetime;
                if (FindFirstContact(flight.AttackerId, distance, excludesEndpoint) is { } contact)
                {
                    long hitTicks = (long)(step.Ticks * Math.Min(1d, contact.Distance / (double)distance));
                    TimeSpan hitAt = projectile.Elapsed + TimeSpan.FromTicks(hitTicks);
                    if (contact.Target is { } target)
                        return projectile.ContactTarget(projectile.ApplicationId, hitAt, target);

                    projectile.ContactWorld(projectile.ApplicationId, hitAt);
                    return null;
                }
            }
            projectile.Advance(projectile.ApplicationId, elapsed);
            return null;
        }

        private (float Distance, TargetCandidate? Target)? FindFirstContact(string attackerId, float distance, bool excludesEndpoint)
        {
            float nearestDistance = float.PositiveInfinity;
            TargetCandidate? nearest = null;
            foreach (RaycastHit2D hit in contacts)
            {
                if (hit.distance > nearestDistance || (excludesEndpoint && hit.distance >= distance))
                    break;

                Collider2D collider = hit.collider;
                if (!collider)
                    continue;
                int layer = 1 << collider.gameObject.layer;
                if ((blockingLayers & layer) != 0 && !collider.isTrigger)
                    return (hit.distance, null);
                if ((targetLayers & layer) == 0 || readTarget(collider) is not { IsEligible: true } target
                    || StringComparer.Ordinal.Equals(attackerId, target.TargetId))
                    continue;

                if (hit.distance < nearestDistance || StringComparer.Ordinal.Compare(target.TargetId, nearest?.TargetId) < 0)
                {
                    nearestDistance = hit.distance;
                    nearest = target;
                }
            }
            return nearest.HasValue ? (nearestDistance, nearest) : null;
        }

        private bool IsRunOpen() => run.CurrentRun is { } session && session.Id == runId
            && run.Result == null && physicsScene.IsValid();

        private readonly struct Flight
        {
            internal Flight(ProjectileLifecycle projectile, TimeSpan launchedAt, WeaponInstanceId weaponInstanceId, string attackerId)
            {
                Projectile = projectile;
                LaunchedAt = launchedAt;
                WeaponInstanceId = weaponInstanceId;
                AttackerId = attackerId;
            }

            internal ProjectileLifecycle Projectile { get; }
            internal TimeSpan LaunchedAt { get; }
            internal WeaponInstanceId WeaponInstanceId { get; }
            internal string AttackerId { get; }
        }
    }
}
