#nullable enable

using System;

namespace ProjectWeapon.Combat
{
    public enum ProjectileHitBehavior
    {
        StopOnTarget,
        ContinueThroughTargets
    }

    public readonly struct ProjectileLaunch
    {
        public ProjectileLaunch(Guid applicationId, string attackerId, WorldPoint2D origin,
            WorldPoint2D velocity, TimeSpan lifetime, float amount, ProjectileHitBehavior hitBehavior)
        {
            ApplicationId = applicationId;
            AttackerId = attackerId;
            Origin = origin;
            Velocity = velocity;
            Lifetime = lifetime;
            Amount = amount;
            HitBehavior = hitBehavior;
        }

        public Guid ApplicationId { get; }
        public string? AttackerId { get; }
        public WorldPoint2D Origin { get; }
        public WorldPoint2D Velocity { get; }
        public TimeSpan Lifetime { get; }
        public float Amount { get; }
        public ProjectileHitBehavior HitBehavior { get; }

        internal bool IsValid => ApplicationId != Guid.Empty && !string.IsNullOrWhiteSpace(AttackerId)
            && Origin.IsFinite && Velocity.IsFinite && Lifetime > TimeSpan.Zero
            && float.IsFinite(Amount) && Amount >= 0
            && (HitBehavior == ProjectileHitBehavior.StopOnTarget || HitBehavior == ProjectileHitBehavior.ContinueThroughTargets)
            && PositionAt(Lifetime).IsFinite;

        internal WorldPoint2D PositionAt(TimeSpan elapsed)
        {
            double seconds = elapsed.TotalSeconds;
            return new WorldPoint2D(
                (float)(Origin.X + Velocity.X * seconds),
                (float)(Origin.Y + Velocity.Y * seconds));
        }
    }
}
