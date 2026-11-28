#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;

namespace ProjectWeapon.ManualSkill
{
    public enum ManualSkillActivationStatus
    {
        Unavailable,
        InvalidInput,
        DuplicateGesture,
        InvalidTarget,
        EffectRejected,
        Canceled,
        Started
    }

    /// <summary>Accepts one completion or cancellation per input gesture on the owning gameplay thread.</summary>
    public sealed class ManualSkillExecutor
    {
        private readonly RunState run;
        private readonly RunId runId;
        private readonly EquippedWeaponSelection selection;
        private readonly IManualSkillEffect effect;
        private readonly Func<bool> isDodging;
        private readonly Func<string, TargetCandidate?> readEnemy;
        private readonly string attackerId;
        private readonly HashSet<Guid> handledGestures = new HashSet<Guid>();
        private TimeSpan sampledThrough;

        public ManualSkillExecutor(RunState run, EquippedWeaponSelection selection, IManualSkillEffect effect,
            Func<bool> isDodging, Func<string, TargetCandidate?> readEnemy, string attackerId)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            RunSession session = run.CurrentRun ?? throw new InvalidOperationException("Start the run before binding skills.");
            runId = session.Id;
            sampledThrough = session.StartedAt;
            this.selection = selection ?? throw new ArgumentNullException(nameof(selection));
            this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
            this.isDodging = isDodging ?? throw new ArgumentNullException(nameof(isDodging));
            this.readEnemy = readEnemy ?? throw new ArgumentNullException(nameof(readEnemy));
            if (string.IsNullOrWhiteSpace(attackerId))
                throw new ArgumentException("A skill needs a valid attacker ID.", nameof(attackerId));
            this.attackerId = attackerId;
        }

        public ManualSkillActivationStatus Cancel(Guid gestureId)
        {
            if (gestureId == Guid.Empty)
                return ManualSkillActivationStatus.InvalidInput;
            if (run.CurrentRun?.Id != runId)
                return ManualSkillActivationStatus.Unavailable;
            return handledGestures.Add(gestureId) ? ManualSkillActivationStatus.Canceled
                : ManualSkillActivationStatus.DuplicateGesture;
        }

        public ManualSkillActivationStatus TryActivate(Guid gestureId, WeaponInstanceId expectedWeapon,
            TimeSpan observedAt, WorldPoint2D origin, ManualSkillAim aim)
        {
            if (gestureId == Guid.Empty)
                return ManualSkillActivationStatus.InvalidInput;
            if (run.CurrentRun is not { } session || session.Id != runId)
                return ManualSkillActivationStatus.Unavailable;
            if (!handledGestures.Add(gestureId))
                return ManualSkillActivationStatus.DuplicateGesture;
            if (!expectedWeapon.IsValid || !origin.IsFinite)
                return ManualSkillActivationStatus.InvalidInput;
            if (run.Phase != RunPhase.Exploring || observedAt < sampledThrough || observedAt >= session.Deadline)
                return ManualSkillActivationStatus.Unavailable;

            sampledThrough = observedAt;
            if (isDodging())
                return ManualSkillActivationStatus.Unavailable;

            ManualSkillActivationStatus status = ManualSkillActivationStatus.Unavailable;
            selection.TryStartSkill(runId, observedAt, weapon =>
            {
                if (weapon.InstanceId != expectedWeapon || weapon.Definition.Skill is not ManualSkillPattern pattern
                    || !pattern.TryGetCooldowns(observedAt, out var readyAt))
                    return null;

                if (!TryResolveAim(pattern, origin, aim, out ManualSkillAim resolved, out TargetCandidate? target))
                {
                    status = ManualSkillActivationStatus.InvalidTarget;
                    return null;
                }

                var request = new ManualSkillActivationRequest(gestureId, runId, observedAt, attackerId,
                    weapon, pattern, origin, resolved, target);
                if (!effect.TryStart(request))
                {
                    status = ManualSkillActivationStatus.EffectRejected;
                    return null;
                }

                status = ManualSkillActivationStatus.Started;
                return readyAt;
            });
            return status;
        }

        private bool TryResolveAim(ManualSkillPattern pattern, WorldPoint2D origin, ManualSkillAim aim,
            out ManualSkillAim resolved, out TargetCandidate? target)
        {
            resolved = aim;
            target = null;
            if (aim.Kind != pattern.AimKind)
                return false;

            double rangeSquared = (double)pattern.MaxAimDistance * pattern.MaxAimDistance;
            switch (aim.Kind)
            {
                case ManualSkillAimKind.Self:
                    return true;
                case ManualSkillAimKind.Direction:
                    if (!aim.Vector.IsFinite)
                        return false;
                    double length = Math.Sqrt(aim.Vector.SquaredDistanceTo(default));
                    if (length == 0)
                        return false;
                    resolved = ManualSkillAim.Along(new WorldPoint2D(
                        (float)(aim.Vector.X / length), (float)(aim.Vector.Y / length)));
                    return true;
                case ManualSkillAimKind.Ground:
                    return aim.Vector.IsFinite && origin.SquaredDistanceTo(aim.Vector) <= rangeSquared;
                case ManualSkillAimKind.Enemy:
                    if (string.IsNullOrWhiteSpace(aim.TargetId) || StringComparer.Ordinal.Equals(attackerId, aim.TargetId)
                        || readEnemy(aim.TargetId!) is not TargetCandidate candidate || !candidate.IsEligible
                        || !StringComparer.Ordinal.Equals(aim.TargetId, candidate.TargetId)
                        || origin.SquaredDistanceTo(candidate.Position) > rangeSquared)
                        return false;
                    target = candidate;
                    return true;
                default:
                    return false;
            }
        }
    }
}
