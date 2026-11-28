#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public enum TwinSwordHand
    {
        Left,
        Right
    }

    public readonly struct TwinSwordStrike
    {
        internal TwinSwordStrike(TwinSwordHand hand, TimeSpan effectiveAt, DamageRequest? hit)
        {
            Hand = hand;
            EffectiveAt = effectiveAt;
            Hit = hit;
        }

        public TwinSwordHand Hand { get; }
        public TimeSpan EffectiveAt { get; }
        public DamageRequest? Hit { get; }
    }

    public sealed class TwinSwordSequence
    {
        private readonly string attackerId;
        private readonly float range;
        private readonly float leftDamage;
        private readonly float rightDamage;
        private readonly TimeSpan rightStrikeAt;
        private TimeSpan lastResolvedAt;
        private int nextStrike;

        internal TwinSwordSequence(BasicAttackRequest attack, string attackerId, float range,
            float leftDamage, float rightDamage, TimeSpan rightStrikeDelay)
        {
            RunId = attack.RunId;
            StartedAt = attack.StartedAt;
            TargetId = attack.Target.TargetId!;
            rightStrikeAt = StartedAt + rightStrikeDelay;
            lastResolvedAt = StartedAt;
            this.attackerId = attackerId;
            this.range = range;
            this.leftDamage = leftDamage;
            this.rightDamage = rightDamage;
        }

        public RunId RunId { get; }
        public TimeSpan StartedAt { get; }
        public string TargetId { get; }
        public bool IsFinished => nextStrike == 2;
        public TimeSpan? NextStrikeAt => nextStrike switch
        {
            0 => StartedAt,
            1 => rightStrikeAt,
            _ => null
        };

        /// <summary>Resolves one due strike using the current positions. A miss still consumes that strike.</summary>
        public bool TryResolveNext(RunState run, TimeSpan now, WorldPoint2D origin, TargetCandidate target,
            out TwinSwordStrike strike)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            strike = default;
            if (NextStrikeAt is not { } scheduledAt || now < lastResolvedAt)
                return false;

            RunSession? session = run.CurrentRun;
            if (session == null || session.Id != RunId || run.Phase != RunPhase.Exploring || now >= session.Deadline)
            {
                nextStrike = 2;
                return false;
            }
            if (now < scheduledAt)
                return false;

            TwinSwordHand hand = nextStrike == 0 ? TwinSwordHand.Left : TwinSwordHand.Right;
            DamageRequest? hit = null;
            if (StringComparer.Ordinal.Equals(TargetId, target.TargetId))
            {
                float damage = hand == TwinSwordHand.Left ? leftDamage : rightDamage;
                var area = AreaDamageRequest.Circle(Guid.NewGuid(), attackerId, origin, range, damage);
                var hits = AreaDamageSelector.FindTargets(area, new[] { target });
                if (hits.Count != 0)
                    hit = hits[0];
            }

            strike = new TwinSwordStrike(hand, now, hit);
            lastResolvedAt = now;
            nextStrike++;
            return true;
        }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Twin Sword Sequence")]
    public sealed class TwinSwordSequencePattern : BasicAttackPattern
    {
        [SerializeField, Min(0f)] private float leftDamage = 7f;
        [SerializeField, Min(0f)] private float rightDamage = 9f;
        [SerializeField, Min(1)] private int strikeIntervalMilliseconds = 150;

        public float LeftDamage => leftDamage;
        public float RightDamage => rightDamage;
        public TimeSpan StrikeInterval => TimeSpan.FromMilliseconds(strikeIntervalMilliseconds);

        public bool TryCreateSequence(BasicAttackRequest attack, string attackerId,
            [NotNullWhen(true)] out TwinSwordSequence? sequence)
        {
            sequence = null;
            if (attack.Pattern != this || !IsValid || strikeIntervalMilliseconds <= 0 || StrikeInterval >= Cooldown
                || !float.IsFinite(leftDamage) || leftDamage < 0f || !float.IsFinite(rightDamage) || rightDamage < 0f
                || string.IsNullOrWhiteSpace(attackerId) || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || attack.Origin.SquaredDistanceTo(attack.Target.Position) > (double)Range * Range
                || attack.StartedAt > TimeSpan.MaxValue - StrikeInterval)
                return false;

            sequence = new TwinSwordSequence(attack, attackerId, Range, leftDamage, rightDamage, StrikeInterval);
            return true;
        }
    }
}
