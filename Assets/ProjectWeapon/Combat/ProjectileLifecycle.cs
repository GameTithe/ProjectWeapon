#nullable enable

using System;
using System.Collections.Generic;

namespace ProjectWeapon.Combat
{
    public enum ProjectilePhase
    {
        Inactive,
        Active,
        HitTarget,
        Blocked,
        Expired
    }

    /// <summary>Per-rental state driven by an adapter's clock and confirmed contacts.</summary>
    public sealed class ProjectileLifecycle
    {
        private readonly HashSet<string> hitTargets = new HashSet<string>(StringComparer.Ordinal);
        private ProjectileLaunch launch;

        public ProjectilePhase Phase { get; private set; }
        public Guid ApplicationId => launch.ApplicationId;
        public TimeSpan Lifetime => launch.Lifetime;
        public WorldPoint2D Velocity => launch.Velocity;
        public WorldPoint2D Position { get; private set; }
        public TimeSpan Elapsed { get; private set; }

        public bool TryLaunch(ProjectileLaunch request)
        {
            if (Phase != ProjectilePhase.Inactive || !request.IsValid)
                return false;

            launch = request;
            Position = request.Origin;
            Phase = ProjectilePhase.Active;
            return true;
        }

        /// <summary>Advance to an absolute time since launch. Exact lifetime is expired.</summary>
        public bool Advance(Guid applicationId, TimeSpan elapsed)
        {
            if (Phase != ProjectilePhase.Active || applicationId != ApplicationId || elapsed < Elapsed)
                return false;

            if (elapsed >= Lifetime)
            {
                Elapsed = Lifetime;
                Phase = ProjectilePhase.Expired;
            }
            else
            {
                Elapsed = elapsed;
            }

            Position = launch.PositionAt(Elapsed);
            return true;
        }

        /// <summary>Accept a collider adapter's target contact and emit at most one request per target.</summary>
        public DamageRequest? ContactTarget(Guid applicationId, TimeSpan elapsed, TargetCandidate target)
        {
            if (!Advance(applicationId, elapsed) || Phase != ProjectilePhase.Active || !target.IsEligible
                || StringComparer.Ordinal.Equals(target.TargetId, launch.AttackerId) || !hitTargets.Add(target.TargetId!))
                return null;

            if (launch.HitBehavior == ProjectileHitBehavior.StopOnTarget)
                Phase = ProjectilePhase.HitTarget;

            return new DamageRequest(applicationId, launch.AttackerId!, target.TargetId!, launch.Amount);
        }

        /// <summary>Terminate on a confirmed solid contact. Filtering belongs to the collider adapter.</summary>
        public bool ContactWorld(Guid applicationId, TimeSpan elapsed)
        {
            if (!Advance(applicationId, elapsed) || Phase != ProjectilePhase.Active)
                return false;

            Phase = ProjectilePhase.Blocked;
            return true;
        }

        public void ResetForPool()
        {
            launch = default;
            Position = default;
            Elapsed = TimeSpan.Zero;
            hitTargets.Clear();
            Phase = ProjectilePhase.Inactive;
        }
    }
}
