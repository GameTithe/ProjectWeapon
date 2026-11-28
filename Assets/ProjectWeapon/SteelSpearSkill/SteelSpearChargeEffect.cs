#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;

namespace ProjectWeapon.SteelSpearSkill
{
    public readonly struct SteelSpearCharge
    {
        internal SteelSpearCharge(ManualSkillActivationRequest activation, WorldPoint2D position, TimeSpan duration,
            float distance, float reach, float arc, int damage)
        {
            RunId = activation.RunId;
            WeaponInstanceId = activation.Weapon.InstanceId;
            StartedAt = activation.StartedAt;
            ThrustAt = StartedAt + duration;
            Origin = position;
            Direction = activation.Aim.Vector;
            Distance = distance;
            preview = AreaDamageRequest.Sector(activation.GestureId, activation.AttackerId, position,
                reach, damage, Direction, arc);
        }

        private readonly AreaDamageRequest preview;

        public RunId RunId { get; }
        public WeaponInstanceId WeaponInstanceId { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan ThrustAt { get; }
        public WorldPoint2D Origin { get; }
        public WorldPoint2D Direction { get; }
        public float Distance { get; }

        // Preview estimators and the resolved hit can use the same wedge at their current stop position.
        public AreaDamageRequest AreaAt(WorldPoint2D stop) => AreaDamageRequest.Sector(preview.ApplicationId,
            preview.AttackerId!, stop, preview.Radius, preview.Amount, Direction, preview.AngleDegrees);
    }

    public readonly struct SteelSpearChargeImpact
    {
        internal SteelSpearChargeImpact(SteelSpearCharge charge, TimeSpan observedAt, AreaDamageRequest area, DamageRequest? hit)
        {
            Charge = charge;
            ObservedAt = observedAt;
            Area = area;
            Hit = hit;
        }

        public SteelSpearCharge Charge { get; }
        public TimeSpan ObservedAt { get; }
        public AreaDamageRequest Area { get; }
        public DamageRequest? Hit { get; }
    }

    /// <summary>Single owned charge followed by one thrust. The room suspends ordinary movement while the charge is live.</summary>
    public sealed class SteelSpearChargeEffect : IManualSkillEffect
    {
        private readonly RunState run;
        private readonly SteelSpearChargeMotion motion;
        private readonly Func<WorldPoint2D, IReadOnlyList<TargetCandidate>> readCandidates;
        private readonly Action<SteelSpearChargeImpact> applyImpact;
        private SteelSpearCharge? pending;
        private float attemptedDistance;
        private bool stoppedAtWall;

        public SteelSpearChargeEffect(RunState run, SteelSpearChargeMotion motion,
            Func<WorldPoint2D, IReadOnlyList<TargetCandidate>> readCandidates, Action<SteelSpearChargeImpact> applyImpact)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            this.motion = motion ?? throw new ArgumentNullException(nameof(motion));
            this.readCandidates = readCandidates ?? throw new ArgumentNullException(nameof(readCandidates));
            this.applyImpact = applyImpact ?? throw new ArgumentNullException(nameof(applyImpact));
        }

        public SteelSpearCharge? PendingCharge => pending is { } charge && IsLive(charge) ? charge : null;

        public bool TryStart(ManualSkillActivationRequest activation)
        {
            if (PendingCharge.HasValue || run.Phase != RunPhase.Exploring
                || run.CurrentRun is not { } session || activation.RunId != session.Id
                || activation.StartedAt < session.StartedAt || activation.StartedAt >= session.Deadline
                || !motion.IsAvailable || activation.Pattern is not SteelSpearChargePattern pattern
                || !pattern.TryCreateCharge(activation, motion.Position, out SteelSpearCharge charge)
                || charge.ThrustAt >= session.Deadline)
                return false;

            pending = charge;
            attemptedDistance = 0;
            stoppedAtWall = false;
            return true;
        }

        /// <summary>Advances swept movement during a live room frame and returns true once the thrust resolves.</summary>
        public bool Advance(TimeSpan observedAt)
        {
            if (pending is not { } charge)
                return false;
            if (!IsLive(charge) || observedAt >= run.CurrentRun!.Deadline || !motion.IsAvailable)
            {
                pending = null;
                return false;
            }
            if (observedAt < charge.StartedAt)
                return false;

            if (!stoppedAtWall)
            {
                double fraction = Math.Min(1d, (observedAt - charge.StartedAt).Ticks
                    / (double)(charge.ThrustAt - charge.StartedAt).Ticks);
                float total = (float)(charge.Distance * fraction);
                if (total > attemptedDistance)
                {
                    stoppedAtWall = motion.MoveForward(charge.Direction, total - attemptedDistance);
                    attemptedDistance = total;
                }
            }

            if (observedAt < charge.ThrustAt)
                return false;

            pending = null;
            WorldPoint2D stop = motion.Position;
            IReadOnlyList<TargetCandidate> candidates = readCandidates(stop);
            if (!IsLive(charge) || !motion.IsAvailable)
                return false;

            AreaDamageRequest area = charge.AreaAt(stop);
            applyImpact(new SteelSpearChargeImpact(charge, observedAt, area, ChooseHit(area, candidates)));
            return true;
        }

        private bool IsLive(SteelSpearCharge charge) => run.Phase == RunPhase.Exploring && run.Result == null
            && run.CurrentRun is { } session && session.Id == charge.RunId && charge.ThrustAt < session.Deadline;

        private DamageRequest? ChooseHit(AreaDamageRequest area, IReadOnlyList<TargetCandidate> candidates)
        {
            var visible = new List<TargetCandidate>(candidates.Count);
            foreach (TargetCandidate candidate in candidates)
            {
                if (candidate.IsEligible && IsForward(area, candidate.Position)
                    && area.Origin.SquaredDistanceTo(candidate.Position) <= (double)area.Radius * area.Radius
                    && motion.HasClearPath(area.Origin, candidate.Position))
                    visible.Add(candidate);
            }

            visible.Sort((left, right) =>
            {
                int distance = area.Origin.SquaredDistanceTo(left.Position).CompareTo(area.Origin.SquaredDistanceTo(right.Position));
                return distance != 0 ? distance : StringComparer.Ordinal.Compare(left.TargetId, right.TargetId);
            });
            IReadOnlyList<DamageRequest> hits = AreaDamageSelector.FindTargets(area, visible);
            return hits.Count > 0 ? hits[0] : null;
        }

        private static bool IsForward(AreaDamageRequest area, WorldPoint2D point) =>
            ((double)point.X - area.Origin.X) * area.Direction.X
            + ((double)point.Y - area.Origin.Y) * area.Direction.Y > 0d;
    }
}
