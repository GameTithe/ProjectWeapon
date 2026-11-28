#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;

namespace ProjectWeapon.ManualSkill
{
    public readonly struct ManualSkillActivationRequest
    {
        internal ManualSkillActivationRequest(Guid gestureId, RunId runId, TimeSpan startedAt, string attackerId,
            SelectedWeapon weapon, ManualSkillPattern pattern, WorldPoint2D origin, ManualSkillAim aim,
            TargetCandidate? target)
        {
            GestureId = gestureId;
            RunId = runId;
            StartedAt = startedAt;
            AttackerId = attackerId;
            Weapon = weapon;
            Pattern = pattern;
            Origin = origin;
            Aim = aim;
            Target = target;
        }

        public Guid GestureId { get; }
        public RunId RunId { get; }
        public TimeSpan StartedAt { get; }
        public string AttackerId { get; }
        public SelectedWeapon Weapon { get; }
        public ManualSkillPattern Pattern { get; }
        public WorldPoint2D Origin { get; }
        public ManualSkillAim Aim { get; }
        public TargetCandidate? Target { get; }
    }

    /// <summary>Check effect-specific rules before starting. Return false only when nothing started or changed.</summary>
    public interface IManualSkillEffect
    {
        bool TryStart(ManualSkillActivationRequest request);
    }
}
