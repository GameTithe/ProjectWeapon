#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;

namespace ProjectWeapon.Enemies
{
    public enum RangedEnemyPhase
    {
        Waiting,
        Telegraphing,
        Recovering,
        Dead
    }

    public readonly struct RangedEnemyAttack
    {
        internal RangedEnemyAttack(Guid applicationId, RangedAttackStrip area, TimeSpan startedAt, TimeSpan hitsAt)
        {
            ApplicationId = applicationId;
            Area = area;
            StartedAt = startedAt;
            HitsAt = hitsAt;
        }

        public Guid ApplicationId { get; }
        public RangedAttackStrip Area { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan HitsAt { get; }
    }

    /// <summary>Stationary ranged behavior driven by the encounter's clock and target snapshots.</summary>
    public sealed class RangedEnemy
    {
        private readonly string id;
        private readonly RangedEnemySettings settings;
        private readonly HashSet<Guid> appliedDamage = new HashSet<Guid>();
        private TimeSpan lastActionAt;
        private TimeSpan recoveryEndsAt;

        public RangedEnemy(string id, RangedEnemySettings settings)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("An enemy target ID is required.", nameof(id));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!settings.IsValid)
                throw new ArgumentException("Invalid ranged enemy settings.", nameof(settings));

            this.id = id;
            this.settings = settings;
            CurrentHealth = settings.MaxHealth;
        }

        public RangedEnemyPhase Phase { get; private set; }
        public float CurrentHealth { get; private set; }
        public RangedEnemyAttack? ActiveAttack { get; private set; }

        public event Action<RangedEnemyAttack>? AttackStarted;
        public event Action<RangedEnemyAttack, IReadOnlyList<DamageRequest>>? AttackResolved;
        public event Action<DamageRequest, TimeSpan>? DamageApplied;
        public event Action<TimeSpan>? Died;

        /// <summary>Apply incoming damage before advancing when it shares the impact timestamp.</summary>
        public void Advance(TimeSpan now, WorldPoint2D origin, TargetCandidate player)
        {
            if (now < lastActionAt || Phase == RangedEnemyPhase.Dead)
                return;

            lastActionAt = now;
            if (ActiveAttack is { } attack)
            {
                if (now >= attack.HitsAt)
                    ResolveAttack(attack, player);
                return;
            }

            if (Phase == RangedEnemyPhase.Recovering && now < recoveryEndsAt)
                return;

            Phase = RangedEnemyPhase.Waiting;
            if (!origin.IsFinite || !player.IsEligible || StringComparer.Ordinal.Equals(id, player.TargetId)
                || origin.SquaredDistanceTo(player.Position) > (double)settings.DetectionRange * settings.DetectionRange
                || now > TimeSpan.MaxValue - settings.Windup - settings.Recovery)
                return;

            var area = new RangedAttackStrip(origin, player.Position, settings.AttackLength, settings.AttackWidth);
            var next = new RangedEnemyAttack(Guid.NewGuid(), area, now, now + settings.Windup);
            ActiveAttack = next;
            Phase = RangedEnemyPhase.Telegraphing;
            AttackStarted?.Invoke(next);
        }

        public bool ApplyDamage(DamageRequest request, TimeSpan now)
        {
            if (now < lastActionAt || Phase == RangedEnemyPhase.Dead || request.ApplicationId == Guid.Empty
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
                Phase = RangedEnemyPhase.Dead;
            }

            DamageApplied?.Invoke(request, now);
            if (died)
                Died?.Invoke(now);
            return true;
        }

        private void ResolveAttack(RangedEnemyAttack attack, TargetCandidate player)
        {
            ActiveAttack = null;
            recoveryEndsAt = attack.HitsAt + settings.Recovery;
            Phase = RangedEnemyPhase.Recovering;

            var listener = AttackResolved;
            if (listener == null)
                return;

            IReadOnlyList<DamageRequest> hits = Array.Empty<DamageRequest>();
            if (player.IsEligible && attack.Area.Contains(player.Position))
            {
                var contact = AreaDamageRequest.Circle(
                    attack.ApplicationId, id, player.Position, 0f, settings.AttackDamage);
                hits = AreaDamageSelector.FindTargets(contact, new[] { player });
            }

            listener(attack, hits);
        }
    }
}
