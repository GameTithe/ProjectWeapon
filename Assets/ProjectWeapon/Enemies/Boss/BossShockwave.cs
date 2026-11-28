#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;

namespace ProjectWeapon.Enemies.Boss
{
    public readonly struct ShockwaveCast
    {
        internal ShockwaveCast(AreaDamageRequest area, TimeSpan hitsAt)
        {
            Area = area;
            HitsAt = hitsAt;
        }

        public AreaDamageRequest Area { get; }
        public TimeSpan HitsAt { get; }
    }

    public sealed class BossShockwave
    {
        private readonly RunState run;
        private readonly RunSession session;
        private readonly BossShockwaveSettings settings;
        private readonly HashSet<Guid> appliedHits = new HashSet<Guid>();
        private TimeSpan lastObservedAt;
        private TimeSpan readyAt;

        public BossShockwave(RunState run, string targetId, BossShockwaveSettings settings)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            if (run.CurrentRun is not { } active || run.Result != null)
                throw new ArgumentException("The boss must belong to an active expedition.", nameof(run));
            if (string.IsNullOrWhiteSpace(targetId))
                throw new ArgumentException("A designated boss needs a stable target ID.", nameof(targetId));
            if (settings == null || !settings.IsValid)
                throw new ArgumentException("Boss shockwave settings are invalid.", nameof(settings));

            session = active;
            this.settings = settings;
            TargetId = targetId;
            Health = settings.MaxHealth;
            lastObservedAt = active.StartedAt;
            readyAt = active.StartedAt;
        }

        public string TargetId { get; }
        public float Health { get; private set; }
        public bool IsDead => Health <= 0;
        public ShockwaveCast? ActiveCast { get; private set; }

        public event Action<ShockwaveCast, IReadOnlyList<DamageRequest>, TimeSpan>? ShockwaveResolved;
        public event Action<RunId, string, TimeSpan>? Defeated;

        public void Advance(TimeSpan now, WorldPoint2D origin, TargetCandidate player)
        {
            if (run.CurrentRun?.Id != session.Id || run.Result != null || now >= session.Deadline)
            {
                ActiveCast = null;
                return;
            }
            if (IsDead || now < lastObservedAt)
                return;

            lastObservedAt = now;
            if (ActiveCast is { } cast)
            {
                if (now < cast.HitsAt)
                    return;

                ActiveCast = null;
                readyAt = now <= TimeSpan.MaxValue - settings.Recovery ? now + settings.Recovery : TimeSpan.MaxValue;
                ShockwaveResolved?.Invoke(cast, AreaDamageSelector.FindTargets(cast.Area, new[] { player }), now);
                return;
            }
            if (now < readyAt || !origin.IsFinite || !player.IsEligible
                || StringComparer.Ordinal.Equals(TargetId, player.TargetId)
                || origin.SquaredDistanceTo(player.Position) > (double)settings.AggroRadius * settings.AggroRadius
                || session.Deadline - now <= settings.Windup)
                return;

            var area = AreaDamageRequest.Circle(Guid.NewGuid(), TargetId, origin, settings.AttackRadius, settings.Damage);
            ActiveCast = new ShockwaveCast(area, now + settings.Windup);
        }

        public bool ApplyDamage(DamageRequest hit, TimeSpan now)
        {
            if (IsDead || now < lastObservedAt || now >= session.Deadline || run.CurrentRun?.Id != session.Id
                || run.Result != null || !StringComparer.Ordinal.Equals(hit.TargetId, TargetId)
                || StringComparer.Ordinal.Equals(hit.AttackerId, TargetId) || !float.IsFinite(hit.Amount)
                || hit.Amount <= 0 || hit.ApplicationId == Guid.Empty || !appliedHits.Add(hit.ApplicationId))
                return false;

            lastObservedAt = now;
            Health = Math.Max(0f, Health - hit.Amount);
            if (IsDead)
            {
                ActiveCast = null;
                Defeated?.Invoke(session.Id, TargetId, now);
            }
            return true;
        }
    }
}
