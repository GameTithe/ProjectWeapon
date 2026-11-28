#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEngine;

namespace ProjectWeapon.AutoAttack
{
    /// <summary>Owns the two pending parts of one accepted attack.</summary>
    public sealed class DoomScytheSequence
    {
        private readonly RunId runId;
        private TimeSpan observedThrough;
        private readonly (TimeSpan DueAt, AreaDamageRequest Area)[] stages;
        private int nextStage;

        internal DoomScytheSequence(RunId runId, TimeSpan startedAt,
            (TimeSpan DueAt, AreaDamageRequest Area) delayedSlash, (TimeSpan DueAt, AreaDamageRequest Area) shadowBlast)
        {
            this.runId = runId;
            observedThrough = startedAt;
            stages = new[] { delayedSlash, shadowBlast };
        }

        public bool IsFinished => nextStage == stages.Length;
        public TimeSpan? NextHitAt => IsFinished ? null : stages[nextStage].DueAt;

        /// <summary>Resolve at most one due part with this frame's live targets. Apply its result before sampling again.</summary>
        public bool TryResolveNext(RunState run, TimeSpan now, IReadOnlyList<TargetCandidate> candidates,
            [NotNullWhen(true)] out CircularStrike? strike)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            strike = null;
            if (IsFinished)
                return false;
            if (run.CurrentRun is not { } session || session.Id != runId || run.Result != null || now >= session.Deadline)
            {
                nextStage = stages.Length;
                return false;
            }

            if (now < observedThrough)
                return false;

            observedThrough = now;
            var stage = stages[nextStage];
            if (now < stage.DueAt)
                return false;

            strike = new CircularStrike(runId, stage.Area.ApplicationId, now,
                AreaDamageSelector.FindTargets(stage.Area, candidates));
            nextStage++;
            return true;
        }
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Basic Attacks/Doom Scythe")]
    public sealed class DoomScythePattern : BasicAttackPattern
    {
        [SerializeField, Min(1)] private int secondSlashAtMilliseconds = 260;
        [SerializeField, Min(1)] private int shadowBlastAtMilliseconds = 700;
        [SerializeField, Min(0f)] private float slashDamage = 19f;
        [SerializeField, Min(0f)] private float blastRadius = 4.2f;
        [SerializeField, Min(0f)] private float blastDamage = 28f;

        public TimeSpan SecondSlashDelay => TimeSpan.FromMilliseconds(secondSlashAtMilliseconds);
        public TimeSpan ShadowBlastDelay => TimeSpan.FromMilliseconds(shadowBlastAtMilliseconds);
        public float BlastRadius => blastRadius;

        /// <summary>Create the immediate slash and freeze the origin and tuning for both remaining stages.</summary>
        public bool TryStart(BasicAttackRequest attack, string attackerId, IReadOnlyList<TargetCandidate> candidates,
            [NotNullWhen(true)] out CircularStrike? immediate, [NotNullWhen(true)] out DoomScytheSequence? sequence)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            immediate = null;
            sequence = null;
            TimeSpan secondDelay = SecondSlashDelay;
            TimeSpan blastDelay = ShadowBlastDelay;
            if (attack.Pattern != this || !IsValid || Range <= 0f || secondDelay <= TimeSpan.Zero
                || blastDelay <= secondDelay || !float.IsFinite(slashDamage) || slashDamage < 0f
                || !float.IsFinite(blastRadius) || blastRadius <= Range || !float.IsFinite(blastDamage) || blastDamage < 0f
                || string.IsNullOrWhiteSpace(attackerId) || !attack.RunId.IsValid || attack.StartedAt < TimeSpan.Zero
                || attack.StartedAt > TimeSpan.MaxValue - blastDelay || !attack.Origin.IsFinite || !attack.Target.IsEligible
                || StringComparer.Ordinal.Equals(attackerId, attack.Target.TargetId)
                || attack.Origin.SquaredDistanceTo(attack.Target.Position) > (double)Range * Range)
                return false;

            AreaDamageRequest first = AreaDamageRequest.Circle(Guid.NewGuid(), attackerId, attack.Origin, Range, slashDamage);
            AreaDamageRequest second = AreaDamageRequest.Circle(Guid.NewGuid(), attackerId, attack.Origin, Range, slashDamage);
            AreaDamageRequest blast = AreaDamageRequest.Circle(Guid.NewGuid(), attackerId, attack.Origin, blastRadius, blastDamage);
            immediate = new CircularStrike(attack.RunId, first.ApplicationId, attack.StartedAt,
                AreaDamageSelector.FindTargets(first, candidates));
            sequence = new DoomScytheSequence(attack.RunId, attack.StartedAt,
                (attack.StartedAt + secondDelay, second), (attack.StartedAt + blastDelay, blast));
            return true;
        }
    }
}
