#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;

namespace ProjectWeapon.Enemies
{
    public enum MeleeEnemyPhase
    {
        Seeking,
        Telegraphing,
        Recovering,
        Dead
    }

    public readonly struct MeleeEnemyAttack
    {
        internal MeleeEnemyAttack(AreaDamageRequest area, TimeSpan startedAt, TimeSpan hitsAt)
        {
            Area = area;
            StartedAt = startedAt;
            HitsAt = hitsAt;
        }

        public AreaDamageRequest Area { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan HitsAt { get; }
    }

    /// <summary>Melee enemy state driven by the owning encounter's clock and physics snapshots.</summary>
    public sealed class MeleeEnemy
    {
        private readonly string id;
        private readonly MeleeEnemySettings settings;
        private readonly HashSet<Guid> appliedDamage = new HashSet<Guid>();
        private WorldPoint2D facing = new WorldPoint2D(0f, -1f);
        private TimeSpan lastActionAt;
        private TimeSpan recoveryEndsAt;

        public MeleeEnemy(string id, MeleeEnemySettings settings)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("An enemy target ID is required.", nameof(id));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!settings.IsValid)
                throw new ArgumentException("Invalid melee enemy settings.", nameof(settings));

            this.id = id;
            this.settings = settings;
            CurrentHealth = settings.MaxHealth;
        }

        public MeleeEnemyPhase Phase { get; private set; }
        public float CurrentHealth { get; private set; }
        public WorldPoint2D MovementVelocity { get; private set; }
        public MeleeEnemyAttack? ActiveAttack { get; private set; }

        public event Action<MeleeEnemyAttack>? AttackStarted;
        public event Action<MeleeEnemyAttack, IReadOnlyList<DamageRequest>>? AttackResolved;
        public event Action<DamageRequest, TimeSpan>? DamageApplied;
        public event Action<TimeSpan>? Died;

        /// <summary>Apply incoming damage for a timestamp before advancing to that timestamp.</summary>
        public void Advance(TimeSpan now, WorldPoint2D origin, TargetCandidate player)
        {
            if (now < lastActionAt || Phase == MeleeEnemyPhase.Dead)
                return;

            lastActionAt = now;
            MovementVelocity = default;
            if (ActiveAttack is { } attack)
            {
                if (now >= attack.HitsAt)
                    ResolveAttack(attack, player);
                return;
            }

            if (Phase == MeleeEnemyPhase.Recovering && now < recoveryEndsAt)
                return;

            Phase = MeleeEnemyPhase.Seeking;
            if (!origin.IsFinite || !player.IsEligible || StringComparer.Ordinal.Equals(id, player.TargetId))
                return;

            double distanceSquared = origin.SquaredDistanceTo(player.Position);
            if (distanceSquared > 0d)
                facing = DirectionTo(origin, player.Position, Math.Sqrt(distanceSquared));

            if (distanceSquared > (double)settings.ApproachDistance * settings.ApproachDistance)
            {
                MovementVelocity = new WorldPoint2D(facing.X * settings.MoveSpeed, facing.Y * settings.MoveSpeed);
                return;
            }

            var area = AreaDamageRequest.Sector(Guid.NewGuid(), id, origin, settings.AttackRadius,
                settings.AttackDamage, facing, settings.AttackAngle);
            var next = new MeleeEnemyAttack(area, now, now + settings.Windup);
            ActiveAttack = next;
            Phase = MeleeEnemyPhase.Telegraphing;
            AttackStarted?.Invoke(next);
        }

        public bool ApplyDamage(DamageRequest request, TimeSpan now)
        {
            if (now < lastActionAt || Phase == MeleeEnemyPhase.Dead || request.ApplicationId == Guid.Empty
                || !StringComparer.Ordinal.Equals(request.TargetId, id) || string.IsNullOrWhiteSpace(request.AttackerId)
                || StringComparer.Ordinal.Equals(id, request.AttackerId)
                || !float.IsFinite(request.Amount) || request.Amount <= 0f || !appliedDamage.Add(request.ApplicationId))
                return false;

            lastActionAt = now;
            CurrentHealth = Math.Max(0f, CurrentHealth - request.Amount);
            bool died = CurrentHealth == 0f;
            if (died)
            {
                ActiveAttack = null;
                MovementVelocity = default;
                Phase = MeleeEnemyPhase.Dead;
            }

            DamageApplied?.Invoke(request, now);
            if (died)
                Died?.Invoke(now);
            return true;
        }

        private void ResolveAttack(MeleeEnemyAttack attack, TargetCandidate player)
        {
            ActiveAttack = null;
            recoveryEndsAt = attack.HitsAt + settings.Recovery;
            Phase = MeleeEnemyPhase.Recovering;
            var listener = AttackResolved;
            if (listener != null)
                listener(attack, AreaDamageSelector.FindTargets(attack.Area, new[] { player }));
        }

        private static WorldPoint2D DirectionTo(WorldPoint2D origin, WorldPoint2D target, double distance)
            => new WorldPoint2D((float)(((double)target.X - origin.X) / distance),
                (float)(((double)target.Y - origin.Y) / distance));
    }
}
