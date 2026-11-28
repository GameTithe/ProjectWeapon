#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;

namespace ProjectWeapon.WoodenSpearSkill
{
    public readonly struct WoodenSpearStrike
    {
        internal WoodenSpearStrike(ManualSkillActivationRequest request, int number, Guid applicationId,
            TimeSpan scheduledAt, TimeSpan effectiveAt, DamageRequest? hit)
        {
            RunId = request.RunId;
            WeaponId = request.Weapon.InstanceId;
            Origin = request.Origin;
            Direction = request.Aim.Vector;
            Number = number;
            ApplicationId = applicationId;
            ScheduledAt = scheduledAt;
            EffectiveAt = effectiveAt;
            Hit = hit;
        }

        public RunId RunId { get; }
        public WeaponInstanceId WeaponId { get; }
        public WorldPoint2D Origin { get; }
        public WorldPoint2D Direction { get; }
        public int Number { get; }
        public Guid ApplicationId { get; }
        public TimeSpan ScheduledAt { get; }
        public TimeSpan EffectiveAt { get; }
        public DamageRequest? Hit { get; }
    }

    /// <summary>Owns one accepted three-thrust sequence. Drive it with the expedition clock and live target snapshots.</summary>
    public sealed class WoodenSpearThrustEffect : IManualSkillEffect
    {
        private readonly RunState run;
        private Sequence? pending;

        public WoodenSpearThrustEffect(RunState run)
            => this.run = run ?? throw new ArgumentNullException(nameof(run));

        public bool IsBusy => pending is { } sequence && IsLiveRun(sequence.Request.RunId);
        public TimeSpan? NextThrustAt => IsBusy ? pending!.DueAt : null;
        public WorldPoint2D? PendingOrigin => IsBusy ? pending!.Request.Origin : null;

        public bool TryStart(ManualSkillActivationRequest request)
        {
            if (IsBusy || run.Phase != RunPhase.Exploring || !IsLiveRun(request.RunId)
                || run.CurrentRun is not { } session || request.StartedAt < session.StartedAt
                || request.StartedAt >= session.Deadline || request.Pattern is not WoodenSpearThrustPattern pattern
                || !pattern.CanStart(request) || request.Aim.Kind != ManualSkillAimKind.Direction
                || !request.Origin.IsFinite || !request.Aim.Vector.IsFinite
                || request.Aim.Vector.SquaredDistanceTo(default) == 0d)
                return false;

            pending = new Sequence(request, pattern);
            return true;
        }

        /// <summary>Consume no more than one due thrust. Apply it before sampling targets for the next one.</summary>
        public bool TryResolveNext(TimeSpan now, IReadOnlyList<TargetCandidate> candidates, out WoodenSpearStrike strike)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            strike = default;
            if (pending is not { } sequence)
                return false;

            if (!IsLiveRun(sequence.Request.RunId) || now >= run.CurrentRun!.Deadline)
            {
                pending = null;
                return false;
            }
            if (now < sequence.ObservedThrough)
                return false;

            sequence.ObservedThrough = now;
            TimeSpan dueAt = sequence.DueAt;
            if (now < dueAt)
                return false;

            Guid id = Guid.NewGuid();
            DamageRequest? hit = sequence.ResolveHit(id, candidates);
            strike = new WoodenSpearStrike(sequence.Request, ++sequence.ResolvedCount, id, dueAt, now, hit);
            if (sequence.ResolvedCount == 3)
                pending = null;
            return true;
        }

        private bool IsLiveRun(RunId runId)
            => run.CurrentRun?.Id == runId && run.Result == null;

        private sealed class Sequence
        {
            private readonly float range;
            private readonly float halfWidth;
            private readonly float damage;
            private readonly TimeSpan interval;

            public Sequence(ManualSkillActivationRequest request, WoodenSpearThrustPattern pattern)
            {
                Request = request;
                range = pattern.Range;
                halfWidth = pattern.HalfWidth;
                damage = pattern.Damage;
                interval = pattern.Interval;
                ObservedThrough = request.StartedAt;
            }

            public ManualSkillActivationRequest Request { get; }
            public TimeSpan ObservedThrough { get; set; }
            public int ResolvedCount { get; set; }
            public TimeSpan DueAt => Request.StartedAt + TimeSpan.FromTicks(interval.Ticks * ResolvedCount);

            public DamageRequest? ResolveHit(Guid applicationId, IReadOnlyList<TargetCandidate> candidates)
            {
                var forward = new List<TargetCandidate>();
                foreach (TargetCandidate candidate in candidates)
                {
                    if (!candidate.IsEligible || StringComparer.Ordinal.Equals(candidate.TargetId, Request.AttackerId))
                        continue;

                    double dx = (double)candidate.Position.X - Request.Origin.X;
                    double dy = (double)candidate.Position.Y - Request.Origin.Y;
                    WorldPoint2D direction = Request.Aim.Vector;
                    double depth = dx * direction.X + dy * direction.Y;
                    double side = dy * direction.X - dx * direction.Y;
                    if (depth >= 0d && Math.Abs(side) <= halfWidth)
                        forward.Add(candidate);
                }
                if (TargetSelector.FindNearest(Request.Origin, range, forward) is not TargetCandidate target)
                    return null;

                var area = AreaDamageRequest.Circle(applicationId, Request.AttackerId, target.Position, 0f, damage);
                return AreaDamageSelector.FindTargets(area, new[] { target })[0];
            }
        }
    }
}
