#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public sealed class StormSwordImpact
    {
        internal StormSwordImpact(WorldPoint2D position, DamageRequest directHit, CircularStrike lightning)
        {
            Position = position;
            DirectHit = directHit;
            Lightning = lightning;
        }

        public WorldPoint2D Position { get; }
        public DamageRequest DirectHit { get; }
        public CircularStrike Lightning { get; }
        public TimeSpan EffectiveAt => Lightning.EffectiveAt;
    }

    public sealed class StormSwordFlight
    {
        private readonly RunState run;
        private readonly ProjectileLifecycle projectile;
        private readonly string attackerId;
        private readonly float lightningDamage;

        internal StormSwordFlight(RunState run, BasicAttackRequest attack, ProjectileLifecycle projectile,
            string attackerId, float lightningRadius, float lightningDamage)
        {
            this.run = run;
            this.projectile = projectile;
            this.attackerId = attackerId;
            this.lightningDamage = lightningDamage;
            RunId = attack.RunId;
            StartedAt = attack.StartedAt;
            ApplicationId = projectile.ApplicationId;
            Lifetime = projectile.Lifetime;
            Velocity = projectile.Velocity;
            LightningRadius = lightningRadius;
        }

        public RunId RunId { get; }
        public TimeSpan StartedAt { get; }
        public Guid ApplicationId { get; }
        public TimeSpan Lifetime { get; }
        public WorldPoint2D Velocity { get; }
        public float LightningRadius { get; }
        public WorldPoint2D Position => projectile.Position;
        public ProjectilePhase Phase => projectile.Phase;

        public bool Advance(Guid applicationId, TimeSpan now)
            => IsLiveAt(applicationId, now) && projectile.Advance(applicationId, now - StartedAt);

        /// <summary>Consumes the first eligible enemy contact and emits separate direct and circular damage.</summary>
        public StormSwordImpact? ContactTarget(Guid applicationId, TimeSpan now, WorldPoint2D position,
            TargetCandidate target, IReadOnlyList<TargetCandidate> nearby)
        {
            if (nearby == null)
                throw new ArgumentNullException(nameof(nearby));
            if (!position.IsFinite || !IsLiveAt(applicationId, now)
                || projectile.ContactTarget(applicationId, now - StartedAt, target) is not { } directHit)
                return null;

            var candidates = new List<TargetCandidate>(nearby) { target };
            var area = AreaDamageRequest.Circle(Guid.NewGuid(), attackerId, position, LightningRadius, lightningDamage);
            var lightning = new CircularStrike(RunId, area.ApplicationId, now, AreaDamageSelector.FindTargets(area, candidates));
            return new StormSwordImpact(position, directHit, lightning);
        }

        /// <summary>A confirmed solid contact only stops the flight.</summary>
        public bool ContactWorld(Guid applicationId, TimeSpan now)
            => IsLiveAt(applicationId, now) && projectile.ContactWorld(applicationId, now - StartedAt);

        public void Cancel()
        {
            if (projectile.Phase == ProjectilePhase.Active)
                projectile.ResetForPool();
        }

        private bool IsLiveAt(Guid applicationId, TimeSpan now)
        {
            if (applicationId != ApplicationId || Phase != ProjectilePhase.Active || now < StartedAt)
                return false;
            if (run.CurrentRun is { } active && active.Id == RunId && run.Result == null && now < active.Deadline)
                return true;

            Cancel();
            return false;
        }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Storm Sword")]
    public sealed class StormSwordPattern : BasicAttackPattern
    {
        [SerializeField, Min(0f)] private float speed = 12f;
        [SerializeField, Min(1)] private int lifetimeMilliseconds = 625;
        [SerializeField, Min(0f)] private float directDamage = 13f;
        [SerializeField, Min(0f)] private float lightningRadius = 1.5f;
        [SerializeField, Min(0f)] private float lightningDamage = 16f;

        public float Speed => speed;
        public TimeSpan Lifetime => TimeSpan.FromMilliseconds(lifetimeMilliseconds);
        public float DirectDamage => directDamage;
        public float LightningRadius => lightningRadius;
        public float LightningDamage => lightningDamage;

        /// <summary>Creates the single sword wave for a scheduler dispatch using the aim at cast time.</summary>
        public bool TryLaunch(BasicAttackRequest attack, RunState run, string attackerId,
            [NotNullWhen(true)] out StormSwordFlight? flight)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));

            flight = null;
            if (run.CurrentRun is not { } active || active.Id != attack.RunId || run.Phase != RunPhase.Exploring
                || attack.StartedAt < active.StartedAt || attack.StartedAt >= active.Deadline
                || attack.Pattern != this || !IsValid || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || !float.IsFinite(speed) || speed <= 0f || lifetimeMilliseconds <= 0
                || !float.IsFinite(lightningRadius) || lightningRadius < 0f
                || !float.IsFinite(lightningDamage) || lightningDamage < 0f)
                return false;

            double distanceSquared = attack.Origin.SquaredDistanceTo(attack.Target.Position);
            if (distanceSquared > (double)Range * Range)
                return false;

            WorldPoint2D velocity = default;
            if (distanceSquared > 0)
            {
                double scale = speed / Math.Sqrt(distanceSquared);
                velocity = new WorldPoint2D(
                    (float)(((double)attack.Target.Position.X - attack.Origin.X) * scale),
                    (float)(((double)attack.Target.Position.Y - attack.Origin.Y) * scale));
            }

            var projectile = new ProjectileLifecycle();
            var launch = new ProjectileLaunch(Guid.NewGuid(), attackerId, attack.Origin, velocity, Lifetime,
                directDamage, ProjectileHitBehavior.StopOnTarget);
            if (!projectile.TryLaunch(launch))
                return false;

            flight = new StormSwordFlight(run, attack, projectile, attackerId, lightningRadius, lightningDamage);
            return true;
        }
    }
}
