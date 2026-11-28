#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    public sealed class AbyssScytheFollowUp
    {
        private readonly AreaDamageRequest area;

        internal AbyssScytheFollowUp(RunId runId, TimeSpan startedAt, TimeSpan hitsAt, AreaDamageRequest area)
        {
            RunId = runId;
            StartedAt = startedAt;
            HitsAt = hitsAt;
            this.area = area;
        }

        public RunId RunId { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan HitsAt { get; }
        public WorldPoint2D Origin => area.Origin;
        public float Radius => area.Radius;
        public bool IsFinished { get; private set; }

        /// <summary>Resolves once using the current target snapshot and the original cast circle.</summary>
        public bool TryResolve(RunState run, TimeSpan now, IReadOnlyList<TargetCandidate> candidates,
            [NotNullWhen(true)] out CircularStrike? strike)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            strike = null;
            if (IsFinished || now < StartedAt)
                return false;
            if (run.CurrentRun is not { } session || session.Id != RunId || run.Result != null || now >= session.Deadline)
            {
                IsFinished = true;
                return false;
            }
            if (now < HitsAt)
                return false;

            strike = new CircularStrike(RunId, area.ApplicationId, now, AreaDamageSelector.FindTargets(area, candidates));
            IsFinished = true;
            return true;
        }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Abyss Scythe")]
    public sealed class AbyssScythePattern : BasicAttackPattern
    {
        [SerializeField, Min(1)] private int strikeIntervalMilliseconds = 280;
        [SerializeField, Min(0f)] private float damage = 14f;

        public TimeSpan StrikeInterval => TimeSpan.FromMilliseconds(strikeIntervalMilliseconds);
        public float Damage => damage;

        /// <summary>Call from the scheduler dispatch to build the first strike and the fixed follow-up together.</summary>
        public bool TryStart(BasicAttackRequest attack, string attackerId, IReadOnlyList<TargetCandidate> candidates,
            [NotNullWhen(true)] out CircularStrike? immediate, [NotNullWhen(true)] out AbyssScytheFollowUp? followUp)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            immediate = null;
            followUp = null;
            TimeSpan interval = StrikeInterval;
            if (attack.Pattern != this || !IsValid || interval <= TimeSpan.Zero || interval >= Cooldown
                || !float.IsFinite(damage) || damage < 0f || string.IsNullOrWhiteSpace(attackerId)
                || !attack.RunId.IsValid || attack.StartedAt < TimeSpan.Zero || attack.StartedAt > TimeSpan.MaxValue - interval
                || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || attack.Origin.SquaredDistanceTo(attack.Target.Position) > (double)Range * Range)
                return false;

            var firstArea = AreaDamageRequest.Circle(Guid.NewGuid(), attackerId, attack.Origin, Range, damage);
            immediate = new CircularStrike(attack.RunId, firstArea.ApplicationId, attack.StartedAt,
                AreaDamageSelector.FindTargets(firstArea, candidates));
            var nextArea = AreaDamageRequest.Circle(Guid.NewGuid(), attackerId, attack.Origin, Range, damage);
            followUp = new AbyssScytheFollowUp(attack.RunId, attack.StartedAt, attack.StartedAt + interval, nextArea);
            return true;
        }
    }
}
