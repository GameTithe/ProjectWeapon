#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    /// <summary>One basic attack with independently timed arrow launches.</summary>
    public sealed class RepeaterBowVolley
    {
        private readonly WorldPoint2D origin;
        private readonly WorldPoint2D velocity;
        private readonly string attackerId;
        private readonly TimeSpan lifetime;
        private readonly TimeSpan interval;
        private readonly float damage;
        private readonly int shotCount;
        private TimeSpan lastLaunchAt;
        private int nextShot;

        internal RepeaterBowVolley(BasicAttackRequest attack, WorldPoint2D velocity, string attackerId,
            TimeSpan lifetime, TimeSpan interval, float damage, int shotCount)
        {
            RunId = attack.RunId;
            TargetId = attack.Target.TargetId!;
            StartedAt = attack.StartedAt;
            lastLaunchAt = StartedAt;
            origin = attack.Origin;
            this.velocity = velocity;
            this.attackerId = attackerId;
            this.lifetime = lifetime;
            this.interval = interval;
            this.damage = damage;
            this.shotCount = shotCount;
        }

        public RunId RunId { get; }
        public string TargetId { get; }
        public TimeSpan StartedAt { get; }
        public bool IsFinished => nextShot == shotCount;
        public TimeSpan? NextShotAt => IsFinished ? null : StartedAt + TimeSpan.FromTicks(interval.Ticks * nextShot);

        /// <summary>Launches one due arrow from a ready rental. Measure contacts from this call's time.</summary>
        public bool TryLaunchNext(RunState run, TimeSpan now, ProjectileLifecycle projectile)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (projectile == null)
                throw new ArgumentNullException(nameof(projectile));
            if (NextShotAt is not { } scheduledAt || now < lastLaunchAt)
                return false;

            RunSession? session = run.CurrentRun;
            if (session == null || session.Id != RunId || run.Phase != RunPhase.Exploring || now >= session.Deadline)
            {
                nextShot = shotCount;
                return false;
            }

            if (now < scheduledAt)
                return false;

            var launch = new ProjectileLaunch(Guid.NewGuid(), attackerId, origin, velocity, lifetime,
                damage, ProjectileHitBehavior.StopOnTarget);
            if (!projectile.TryLaunch(launch))
                return false;

            lastLaunchAt = now;
            nextShot++;
            return true;
        }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Repeater Bow")]
    public sealed class RepeaterBowPattern : BasicAttackPattern
    {
        [SerializeField, Min(2)] private int shotCount = 3;
        [SerializeField, Min(1)] private int shotIntervalMilliseconds = 120;
        [SerializeField, Min(0f)] private float projectileSpeed = 10f;
        [SerializeField, Min(1)] private int projectileLifetimeMilliseconds = 600;
        [SerializeField, Min(0f)] private float arrowDamage = 6f;

        public int ShotCount => shotCount;
        public TimeSpan ShotInterval => TimeSpan.FromMilliseconds(shotIntervalMilliseconds);
        public float ProjectileSpeed => projectileSpeed;
        public TimeSpan ProjectileLifetime => TimeSpan.FromMilliseconds(projectileLifetimeMilliseconds);
        public float ArrowDamage => arrowDamage;

        public bool TryCreateVolley(BasicAttackRequest attack, string attackerId,
            [NotNullWhen(true)] out RepeaterBowVolley? volley)
        {
            volley = null;
            if (attack.Pattern != this || !IsValid || shotCount < 2 || shotIntervalMilliseconds <= 0
                || !float.IsFinite(projectileSpeed) || projectileSpeed <= 0f || projectileLifetimeMilliseconds <= 0
                || !float.IsFinite(arrowDamage) || arrowDamage < 0f || string.IsNullOrWhiteSpace(attackerId)
                || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId))
                return false;

            long durationMilliseconds = (long)(shotCount - 1) * shotIntervalMilliseconds;
            if (durationMilliseconds >= Cooldown.TotalMilliseconds
                || attack.StartedAt > TimeSpan.MaxValue - TimeSpan.FromMilliseconds(durationMilliseconds))
                return false;

            double distanceSquared = attack.Origin.SquaredDistanceTo(attack.Target.Position);
            if (distanceSquared > (double)Range * Range)
                return false;

            WorldPoint2D velocity = default;
            if (distanceSquared > 0)
            {
                double scale = projectileSpeed / Math.Sqrt(distanceSquared);
                velocity = new WorldPoint2D(
                    (float)(((double)attack.Target.Position.X - attack.Origin.X) * scale),
                    (float)(((double)attack.Target.Position.Y - attack.Origin.Y) * scale));
            }

            TimeSpan lifetime = ProjectileLifetime;
            var end = new WorldPoint2D(
                (float)(attack.Origin.X + velocity.X * lifetime.TotalSeconds),
                (float)(attack.Origin.Y + velocity.Y * lifetime.TotalSeconds));
            if (!end.IsFinite)
                return false;

            volley = new RepeaterBowVolley(attack, velocity, attackerId, lifetime, ShotInterval, arrowDamage, shotCount);
            return true;
        }
    }
}
