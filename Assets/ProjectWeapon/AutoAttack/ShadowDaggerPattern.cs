#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public readonly struct ShadowStrikeResolution
    {
        internal ShadowStrikeResolution(TimeSpan effectiveAt, DamageRequest? hit)
        {
            EffectiveAt = effectiveAt;
            Hit = hit;
        }

        public TimeSpan EffectiveAt { get; }
        public DamageRequest? Hit { get; }
    }

    public sealed class ShadowDaggerStrike
    {
        private readonly string attackerId;
        private readonly float range;
        private readonly float damage;

        internal ShadowDaggerStrike(BasicAttackRequest attack, string attackerId, float range, float damage, TimeSpan delay)
        {
            RunId = attack.RunId;
            TargetId = attack.Target.TargetId!;
            Origin = attack.Origin;
            StartedAt = attack.StartedAt;
            HitsAt = attack.StartedAt + delay;
            this.attackerId = attackerId;
            this.range = range;
            this.damage = damage;
        }

        public RunId RunId { get; }
        public string TargetId { get; }
        public WorldPoint2D Origin { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan HitsAt { get; }
        public bool IsFinished { get; private set; }

        /// <summary>Consumes the single due strike. A missing or invalid target resolves as a miss.</summary>
        public bool TryResolve(RunState run, TimeSpan now, TargetCandidate target, out ShadowStrikeResolution resolution)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            resolution = default;
            if (IsFinished || now < StartedAt)
                return false;

            RunSession? session = run.CurrentRun;
            if (session == null || session.Id != RunId || run.Result != null || now >= session.Deadline)
            {
                IsFinished = true;
                return false;
            }
            if (now < HitsAt)
                return false;

            DamageRequest? hit = null;
            if (StringComparer.Ordinal.Equals(TargetId, target.TargetId))
            {
                var area = AreaDamageRequest.Circle(Guid.NewGuid(), attackerId, Origin, range, damage);
                var hits = AreaDamageSelector.FindTargets(area, new[] { target });
                if (hits.Count != 0)
                    hit = hits[0];
            }

            resolution = new ShadowStrikeResolution(now, hit);
            IsFinished = true;
            return true;
        }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Shadow Dagger")]
    public sealed class ShadowDaggerPattern : BasicAttackPattern
    {
        [SerializeField, Min(1)] private int delayMilliseconds = 230;
        [SerializeField, Min(0f)] private float damage = 15f;

        public TimeSpan Delay => TimeSpan.FromMilliseconds(delayMilliseconds);
        public float Damage => damage;

        public bool TrySchedule(BasicAttackRequest attack, string attackerId,
            [NotNullWhen(true)] out ShadowDaggerStrike? strike)
        {
            strike = null;
            if (attack.Pattern != this || !IsValid || delayMilliseconds <= 0 || Delay >= Cooldown
                || !float.IsFinite(damage) || damage < 0f || string.IsNullOrWhiteSpace(attackerId)
                || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || attack.Origin.SquaredDistanceTo(attack.Target.Position) > (double)Range * Range
                || attack.StartedAt > TimeSpan.MaxValue - Delay)
                return false;

            strike = new ShadowDaggerStrike(attack, attackerId, Range, damage, Delay);
            return true;
        }
    }
}
