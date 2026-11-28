#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;

namespace ProjectWeapon.DaggerSkill
{
    public readonly struct DaggerDoubleSlashStrike
    {
        internal DaggerDoubleSlashStrike(ManualSkillActivationRequest activation, int strikeIndex, TimeSpan effectiveAt,
            AreaDamageRequest area, IReadOnlyList<DamageRequest> hits)
        {
            RunId = activation.RunId;
            WeaponInstanceId = activation.Weapon.InstanceId;
            GestureId = activation.GestureId;
            StrikeIndex = strikeIndex;
            EffectiveAt = effectiveAt;
            Area = area;
            Hits = hits;
        }

        public RunId RunId { get; }
        public WeaponInstanceId WeaponInstanceId { get; }
        public Guid GestureId { get; }
        public int StrikeIndex { get; }
        public TimeSpan EffectiveAt { get; }
        public AreaDamageRequest Area { get; }
        public IReadOnlyList<DamageRequest> Hits { get; }
    }

    /// <summary>Applies one strike immediately and one from the owning run's combat frame.</summary>
    public sealed class DaggerDoubleSlashEffect : IManualSkillEffect
    {
        private readonly RunState run;
        private readonly RunId runId;
        private readonly Func<IReadOnlyList<TargetCandidate>> readCandidates;
        private readonly Action<DaggerDoubleSlashStrike> applyStrike;
        private readonly List<FollowUp> pending = new List<FollowUp>();

        public DaggerDoubleSlashEffect(RunState run, Func<IReadOnlyList<TargetCandidate>> readCandidates,
            Action<DaggerDoubleSlashStrike> applyStrike)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            runId = run.CurrentRun?.Id ?? throw new InvalidOperationException("Start the run before binding this skill.");
            this.readCandidates = readCandidates ?? throw new ArgumentNullException(nameof(readCandidates));
            this.applyStrike = applyStrike ?? throw new ArgumentNullException(nameof(applyStrike));
        }

        public bool TryStart(ManualSkillActivationRequest request)
        {
            if (request.RunId != runId || !IsLive(request.StartedAt)
                || request.Pattern is not DaggerDoubleSlashPattern pattern || !pattern.CanStart(request))
                return false;

            TimeSpan hitsAt = request.StartedAt + pattern.FollowUpDelay;
            AreaDamageRequest first = AreaDamageRequest.Sector(request.GestureId, request.AttackerId, request.Origin,
                pattern.MaxAimDistance, pattern.Damage, request.Aim.Vector, pattern.ArcDegrees);
            AreaDamageRequest second = AreaDamageRequest.Sector(Guid.NewGuid(), request.AttackerId, request.Origin,
                pattern.MaxAimDistance, pattern.Damage, request.Aim.Vector, pattern.ArcDegrees);
            Apply(request, 0, request.StartedAt, first);

            if (IsLive(hitsAt))
            {
                int index = pending.FindIndex(strike => strike.HitsAt > hitsAt);
                pending.Insert(index < 0 ? pending.Count : index, new FollowUp(request, hitsAt, second));
            }
            return true;
        }

        /// <summary>Consumes due strikes in time order, before later timestamped damage is applied.</summary>
        public void Advance(TimeSpan now)
        {
            while (pending.Count != 0)
            {
                if (!IsLive(now))
                {
                    pending.Clear();
                    return;
                }

                FollowUp strike = pending[0];
                if (now < strike.HitsAt)
                    return;

                pending.RemoveAt(0);
                Apply(strike.Cast, 1, strike.HitsAt, strike.Area);
            }
        }

        private bool IsLive(TimeSpan time) => run.CurrentRun is { } session && session.Id == runId
            && run.Result == null && time >= session.StartedAt && time < session.Deadline;

        private void Apply(ManualSkillActivationRequest request, int index, TimeSpan time, AreaDamageRequest area)
            => applyStrike(new DaggerDoubleSlashStrike(request, index, time, area,
                AreaDamageSelector.FindTargets(area, readCandidates())));

        private readonly struct FollowUp
        {
            internal FollowUp(ManualSkillActivationRequest cast, TimeSpan hitsAt, AreaDamageRequest area)
            {
                Cast = cast;
                HitsAt = hitsAt;
                Area = area;
            }

            internal ManualSkillActivationRequest Cast { get; }
            internal TimeSpan HitsAt { get; }
            internal AreaDamageRequest Area { get; }
        }
    }
}
