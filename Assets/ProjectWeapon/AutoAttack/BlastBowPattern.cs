#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public sealed class BlastBowExplosion
    {
        internal BlastBowExplosion(RunId runId, Guid applicationId, TimeSpan effectiveAt, WorldPoint2D center,
            float radius, IReadOnlyList<DamageRequest> hits)
        {
            RunId = runId;
            ApplicationId = applicationId;
            EffectiveAt = effectiveAt;
            Center = center;
            Radius = radius;
            Hits = hits;
        }

        public RunId RunId { get; }
        public Guid ApplicationId { get; }
        public TimeSpan EffectiveAt { get; }
        public WorldPoint2D Center { get; }
        public float Radius { get; }
        public IReadOnlyList<DamageRequest> Hits { get; }
    }

    public sealed class BlastBowFlight
    {
        private readonly RunState run;
        private readonly ProjectileLifecycle projectile;
        private readonly string attackerId;
        private readonly float blastRadius;
        private readonly float blastDamage;

        internal BlastBowFlight(RunState run, BasicAttackRequest attack, ProjectileLifecycle projectile,
            string attackerId, float blastRadius, float blastDamage)
        {
            this.run = run;
            this.projectile = projectile;
            this.attackerId = attackerId;
            this.blastRadius = blastRadius;
            this.blastDamage = blastDamage;
            RunId = attack.RunId;
            StartedAt = attack.StartedAt;
            ApplicationId = projectile.ApplicationId;
            Lifetime = projectile.Lifetime;
        }

        public RunId RunId { get; }
        public TimeSpan StartedAt { get; }
        public Guid ApplicationId { get; }
        public TimeSpan Lifetime { get; }
        public WorldPoint2D Position => projectile.Position;
        public ProjectilePhase Phase => projectile.Phase;

        public bool Advance(Guid applicationId, TimeSpan now)
            => IsLiveAt(now) && projectile.Advance(applicationId, now - StartedAt);

        /// <summary>Accepts a confirmed contact and exposes only circular damage.</summary>
        public BlastBowExplosion? ContactTarget(Guid applicationId, TimeSpan now, WorldPoint2D impact,
            TargetCandidate target, IReadOnlyList<TargetCandidate> blastCandidates)
        {
            if (blastCandidates == null)
                throw new ArgumentNullException(nameof(blastCandidates));
            if (!impact.IsFinite || !IsLiveAt(now)
                || !projectile.ContactTarget(applicationId, now - StartedAt, target).HasValue)
                return null;

            var candidates = new List<TargetCandidate>(blastCandidates) { target };
            return Explode(now, impact, candidates);
        }

        /// <summary>Accepts a confirmed solid-world contact at the reported collision point.</summary>
        public BlastBowExplosion? ContactWorld(Guid applicationId, TimeSpan now, WorldPoint2D impact,
            IReadOnlyList<TargetCandidate> blastCandidates)
        {
            if (blastCandidates == null)
                throw new ArgumentNullException(nameof(blastCandidates));
            if (!impact.IsFinite || !IsLiveAt(now) || !projectile.ContactWorld(applicationId, now - StartedAt))
                return null;

            return Explode(now, impact, blastCandidates);
        }

        public void Cancel()
        {
            if (projectile.Phase == ProjectilePhase.Active)
                projectile.ResetForPool();
        }

        private bool IsLiveAt(TimeSpan now)
        {
            if (projectile.Phase != ProjectilePhase.Active || now < StartedAt)
                return false;
            RunSession? session = run.CurrentRun;
            if (session != null && session.Id == RunId && run.Result == null && now < session.Deadline)
                return true;

            Cancel();
            return false;
        }

        private BlastBowExplosion Explode(TimeSpan now, WorldPoint2D impact, IReadOnlyList<TargetCandidate> candidates)
        {
            var area = AreaDamageRequest.Circle(ApplicationId, attackerId, impact, blastRadius, blastDamage);
            var hits = AreaDamageSelector.FindTargets(area, candidates);
            return new BlastBowExplosion(RunId, ApplicationId, now, impact, blastRadius,
                new List<DamageRequest>(hits).AsReadOnly());
        }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Blast Bow")]
    public sealed class BlastBowPattern : BasicAttackPattern
    {
        [SerializeField, Min(0f)] private float speed = 12f;
        [SerializeField, Min(1)] private int lifetimeMilliseconds = 650;
        [SerializeField, Min(0f)] private float blastRadius = 1.25f;
        [SerializeField, Min(0f)] private float blastDamage = 20f;

        public float Speed => speed;
        public TimeSpan Lifetime => TimeSpan.FromMilliseconds(lifetimeMilliseconds);
        public float BlastRadius => blastRadius;
        public float BlastDamage => blastDamage;

        public bool TryLaunch(BasicAttackRequest attack, RunState run, string attackerId,
            [NotNullWhen(true)] out BlastBowFlight? flight)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            flight = null;
            RunSession? session = run.CurrentRun;
            if (session == null || session.Id != attack.RunId || run.Phase != RunPhase.Exploring
                || attack.StartedAt < session.StartedAt || attack.StartedAt >= session.Deadline
                || attack.Pattern != this || !IsValid || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || !float.IsFinite(speed) || speed <= 0f || lifetimeMilliseconds <= 0
                || !float.IsFinite(blastRadius) || blastRadius < 0f || !float.IsFinite(blastDamage) || blastDamage < 0f)
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
            var launch = new ProjectileLaunch(Guid.NewGuid(), attackerId, attack.Origin, velocity,
                Lifetime, amount: 0f, hitBehavior: ProjectileHitBehavior.StopOnTarget);
            if (!projectile.TryLaunch(launch))
                return false;

            flight = new BlastBowFlight(run, attack, projectile, attackerId, blastRadius, blastDamage);
            return true;
        }
    }
}
