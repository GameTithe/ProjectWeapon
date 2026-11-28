#nullable enable

using System;
using ProjectWeapon.Combat;

namespace ProjectWeapon.AutoAttack
{
    /// <summary>Curved path for one locked target, with collision and expiry owned by ProjectileLifecycle.</summary>
    public sealed class HomingLightArrow
    {
        private readonly ProjectileLifecycle lifecycle = new ProjectileLifecycle();
        private bool tracking;
        private double? heading;
        private double speed;
        private double turnRadiansPerSecond;
        private double x;
        private double y;

        public ProjectilePhase Phase => lifecycle.Phase;
        public Guid ApplicationId => lifecycle.ApplicationId;
        public TimeSpan Elapsed => lifecycle.Elapsed;
        public TimeSpan Lifetime => lifecycle.Lifetime;
        public string? LockedTargetId { get; private set; }
        public bool IsTracking => Phase == ProjectilePhase.Active && tracking;
        public WorldPoint2D Position => new WorldPoint2D((float)x, (float)y);
        public WorldPoint2D Velocity => heading is double angle
            ? new WorldPoint2D((float)(Math.Cos(angle) * speed), (float)(Math.Sin(angle) * speed))
            : default;

        internal bool TryLaunch(Guid applicationId, string attackerId, WorldPoint2D origin,
            TargetCandidate target, float launchSpeed, float turnDegreesPerSecond, TimeSpan lifetime, float damage)
        {
            // Bound every possible heading so a later course change cannot move outside finite world coordinates.
            double travel = launchSpeed * lifetime.TotalSeconds;
            if (Math.Abs((double)origin.X) + travel > float.MaxValue
                || Math.Abs((double)origin.Y) + travel > float.MaxValue)
                return false;

            double offsetX = (double)target.Position.X - origin.X;
            double offsetY = (double)target.Position.Y - origin.Y;
            double? initialHeading = offsetX == 0 && offsetY == 0 ? null : Math.Atan2(offsetY, offsetX);
            var launch = new ProjectileLaunch(applicationId, attackerId, origin, default, lifetime, damage,
                ProjectileHitBehavior.StopOnTarget);
            if (!lifecycle.TryLaunch(launch))
                return false;

            LockedTargetId = target.TargetId;
            tracking = true;
            heading = initialHeading;
            speed = launchSpeed;
            turnRadiansPerSecond = turnDegreesPerSecond * (Math.PI / 180d);
            x = origin.X;
            y = origin.Y;
            return true;
        }

        /// <summary>Advance a fixed step using the current snapshot for the locked ID, or null when it is missing.</summary>
        public bool Advance(Guid applicationId, TimeSpan elapsed, TargetCandidate? currentTarget)
        {
            TimeSpan previous = Elapsed;
            if (!lifecycle.Advance(applicationId, elapsed))
                return false;

            if (tracking && (!currentTarget.HasValue || !IsLockedAndEligible(currentTarget.Value)))
                tracking = false;

            double stepSeconds = (Elapsed - previous).TotalSeconds;
            if (stepSeconds == 0)
                return true;

            if (tracking && currentTarget is TargetCandidate target)
                SteerToward(target.Position, stepSeconds);

            if (heading is double angle)
            {
                double stepDistance = speed * stepSeconds;
                x += Math.Cos(angle) * stepDistance;
                y += Math.Sin(angle) * stepDistance;
            }

            return true;
        }

        /// <summary>Submit a confirmed contact at the current step time; only the locked live target can be hit.</summary>
        public DamageRequest? ContactTarget(Guid applicationId, TimeSpan elapsed, TargetCandidate target)
        {
            if (!IsTracking || applicationId != ApplicationId || elapsed != Elapsed
                || !StringComparer.Ordinal.Equals(target.TargetId, LockedTargetId))
                return null;

            if (!target.IsEligible)
            {
                tracking = false;
                return null;
            }

            return lifecycle.ContactTarget(applicationId, elapsed, target);
        }

        /// <summary>Submit a confirmed solid contact at the current step time.</summary>
        public bool ContactWorld(Guid applicationId, TimeSpan elapsed)
            => elapsed == Elapsed && lifecycle.ContactWorld(applicationId, elapsed);

        public void ResetForPool()
        {
            lifecycle.ResetForPool();
            LockedTargetId = null;
            tracking = false;
            heading = null;
            speed = 0;
            turnRadiansPerSecond = 0;
            x = 0;
            y = 0;
        }

        private bool IsLockedAndEligible(TargetCandidate snapshot)
            => snapshot.IsEligible && StringComparer.Ordinal.Equals(snapshot.TargetId, LockedTargetId);

        private void SteerToward(WorldPoint2D targetPosition, double stepSeconds)
        {
            double offsetX = targetPosition.X - x;
            double offsetY = targetPosition.Y - y;
            if (offsetX == 0 && offsetY == 0)
                return;

            double desired = Math.Atan2(offsetY, offsetX);
            if (heading is not double current)
            {
                heading = desired;
                return;
            }

            double difference = desired - current;
            double shortestTurn = Math.Atan2(Math.Sin(difference), Math.Cos(difference));
            double maxTurn = turnRadiansPerSecond * stepSeconds;
            double next = current + Math.Max(-maxTurn, Math.Min(maxTurn, shortestTurn));
            heading = Math.Atan2(Math.Sin(next), Math.Cos(next));
        }
    }
}
