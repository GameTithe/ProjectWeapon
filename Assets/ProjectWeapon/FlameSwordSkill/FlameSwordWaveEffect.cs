#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;

namespace ProjectWeapon.FlameSwordSkill
{
    /// <summary>The fixed telegraph footprint and scheduled hit for the accepted weapon instance.</summary>
    public readonly struct FlameSwordWave
    {
        internal FlameSwordWave(ManualSkillActivationRequest activation, TimeSpan scheduledAt, AreaDamageRequest area)
        {
            RunId = activation.RunId;
            WeaponInstanceId = activation.Weapon.InstanceId;
            ScheduledAt = scheduledAt;
            Area = area;
        }

        public RunId RunId { get; }
        public WeaponInstanceId WeaponInstanceId { get; }
        public TimeSpan ScheduledAt { get; }
        public AreaDamageRequest Area { get; }
    }

    public readonly struct FlameSwordWaveImpact
    {
        internal FlameSwordWaveImpact(FlameSwordWave wave, TimeSpan observedAt, IReadOnlyList<DamageRequest> hits)
        {
            Wave = wave;
            ObservedAt = observedAt;
            Hits = hits;
        }

        public FlameSwordWave Wave { get; }
        public TimeSpan ObservedAt { get; }
        public IReadOnlyList<DamageRequest> Hits { get; }
    }

    /// <summary>Owns one delayed wave on the combat host's gameplay thread.</summary>
    public sealed class FlameSwordWaveEffect : IManualSkillEffect
    {
        private readonly RunState run;
        private readonly Func<WorldPoint2D, IReadOnlyList<TargetCandidate>> readCandidates;
        private readonly Action<FlameSwordWaveImpact> applyImpact;

        /// <param name="run">The authoritative expedition.</param>
        /// <param name="readCandidates">Samples current targets and clear paths from the supplied cast origin.</param>
        /// <param name="applyImpact">Consumes the single impact. Use ObservedAt for monotonic live enemy actors.</param>
        public FlameSwordWaveEffect(RunState run, Func<WorldPoint2D, IReadOnlyList<TargetCandidate>> readCandidates,
            Action<FlameSwordWaveImpact> applyImpact)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            this.readCandidates = readCandidates ?? throw new ArgumentNullException(nameof(readCandidates));
            this.applyImpact = applyImpact ?? throw new ArgumentNullException(nameof(applyImpact));
        }

        public FlameSwordWave? PendingWave { get; private set; }

        public bool TryStart(ManualSkillActivationRequest activation)
        {
            if (PendingWave.HasValue || run.Phase != RunPhase.Exploring
                || run.CurrentRun is not { } session || session.Id != activation.RunId
                || activation.StartedAt < session.StartedAt || activation.StartedAt >= session.Deadline
                || activation.Pattern is not FlameSwordWavePattern pattern || !pattern.TryCreateWave(activation, out var wave)
                || wave.ScheduledAt >= session.Deadline)
                return false;

            PendingWave = wave;
            return true;
        }

        /// <summary>Advance before finalizing the run frame to admit a late observation of a legal scheduled hit.</summary>
        public bool Advance(TimeSpan observedAt)
        {
            if (PendingWave is not { } wave)
                return false;
            if (!IsLive(wave))
            {
                PendingWave = null;
                return false;
            }
            if (observedAt < wave.ScheduledAt)
                return false;

            PendingWave = null;
            IReadOnlyList<TargetCandidate> candidates = readCandidates(wave.Area.Origin);
            if (!IsLive(wave))
                return false;
            applyImpact(new FlameSwordWaveImpact(wave, observedAt, AreaDamageSelector.FindTargets(wave.Area, candidates)));
            return true;
        }

        private bool IsLive(FlameSwordWave wave) => run.Result == null && run.CurrentRun is { } session
            && session.Id == wave.RunId && wave.ScheduledAt < session.Deadline;
    }
}
