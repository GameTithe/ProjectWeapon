#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public readonly struct DaggerStrike
    {
        internal DaggerStrike(int index, TimeSpan scheduledAt, TimeSpan effectiveAt, DamageRequest? hit)
        {
            Index = index;
            ScheduledAt = scheduledAt;
            EffectiveAt = effectiveAt;
            Hit = hit;
        }

        public int Index { get; }
        public TimeSpan ScheduledAt { get; }
        public TimeSpan EffectiveAt { get; }
        public DamageRequest? Hit { get; }
    }

    /// <summary>One scheduled basic attack with multiple opportunities to hit its original target.</summary>
    public sealed class DaggerFlurry
    {
        private readonly string attackerId;
        private readonly float range;
        private readonly float damage;
        private readonly int strikeCount;
        private readonly TimeSpan strikeInterval;
        private int nextIndex;
        private TimeSpan observedThrough;

        internal DaggerFlurry(BasicAttackRequest attack, string attackerId, float range, float damage,
            int strikeCount, TimeSpan strikeInterval)
        {
            RunId = attack.RunId;
            StartedAt = attack.StartedAt;
            observedThrough = StartedAt;
            TargetId = attack.Target.TargetId!;
            this.attackerId = attackerId;
            this.range = range;
            this.damage = damage;
            this.strikeCount = strikeCount;
            this.strikeInterval = strikeInterval;
        }

        public RunId RunId { get; }
        public string TargetId { get; }
        public TimeSpan StartedAt { get; }
        public bool IsFinished => nextIndex == strikeCount;
        public TimeSpan? NextHitAt => IsFinished ? null : StartedAt + TimeSpan.FromTicks(strikeInterval.Ticks * nextIndex);

        /// <summary>Consumes a due strike using the current observation time. A miss consumes its slot.</summary>
        public bool TryResolveNext(RunState run, TimeSpan now, WorldPoint2D origin, TargetCandidate target,
            out DaggerStrike strike)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));

            strike = default;
            if (NextHitAt is not { } hitAt)
                return false;

            RunSession? session = run.CurrentRun;
            if (session == null || session.Id != RunId || run.Phase != RunPhase.Exploring || now >= session.Deadline)
            {
                nextIndex = strikeCount;
                return false;
            }

            if (now < observedThrough)
                return false;
            observedThrough = now;
            if (now < hitAt)
                return false;

            DamageRequest? hit = null;
            if (origin.IsFinite && target.IsEligible
                && StringComparer.Ordinal.Equals(TargetId, target.TargetId)
                && origin.SquaredDistanceTo(target.Position) <= (double)range * range)
            {
                var contact = AreaDamageRequest.Circle(Guid.NewGuid(), attackerId, target.Position, 0f, damage);
                var targets = AreaDamageSelector.FindTargets(contact, new[] { target });
                if (targets.Count != 0)
                    hit = targets[0];
            }

            strike = new DaggerStrike(nextIndex, hitAt, now, hit);
            nextIndex++;
            return true;
        }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Dagger Flurry")]
    public sealed class DaggerFlurryPattern : BasicAttackPattern
    {
        [SerializeField, Min(2)] private int strikeCount = 2;
        [SerializeField, Min(1)] private int strikeIntervalMilliseconds = 120;
        [SerializeField, Min(0f)] private float strikeDamage = 5f;

        public int StrikeCount => strikeCount;
        public TimeSpan StrikeInterval => TimeSpan.FromMilliseconds(strikeIntervalMilliseconds);
        public float StrikeDamage => strikeDamage;

        public bool TryCreateFlurry(BasicAttackRequest attack, string attackerId, out DaggerFlurry? flurry)
        {
            flurry = null;
            if (attack.Pattern != this || !IsValid || !float.IsFinite(strikeDamage) || strikeDamage < 0f
                || strikeCount < 2 || strikeIntervalMilliseconds <= 0 || string.IsNullOrWhiteSpace(attackerId)
                || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || attack.Origin.SquaredDistanceTo(attack.Target.Position) > (double)Range * Range)
                return false;

            long durationMilliseconds = (long)(strikeCount - 1) * strikeIntervalMilliseconds;
            if (durationMilliseconds >= Cooldown.TotalMilliseconds
                || attack.StartedAt > TimeSpan.MaxValue - TimeSpan.FromMilliseconds(durationMilliseconds))
                return false;

            flurry = new DaggerFlurry(attack, attackerId, Range, strikeDamage, strikeCount, StrikeInterval);
            return true;
        }
    }
}
