#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.ManualSkill
{
    public enum ManualSkillFailurePolicy
    {
        PreserveResourcesAndCooldowns = 0
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Skills/Activation Pattern")]
    public class ManualSkillPattern : ScriptableObject
    {
        [SerializeField] private ManualSkillAimKind aimKind = ManualSkillAimKind.Self;
        [SerializeField, Min(0f)] private float maxAimDistance = 4f;
        [SerializeField, Min(0)] private int sharedCooldownMilliseconds = 800;
        [SerializeField, Min(0)] private int weaponCooldownMilliseconds = 1500;
        [SerializeField] private ManualSkillFailurePolicy failurePolicy = ManualSkillFailurePolicy.PreserveResourcesAndCooldowns;

        public ManualSkillAimKind AimKind => aimKind;
        public float MaxAimDistance => maxAimDistance;
        public ManualSkillFailurePolicy FailurePolicy => failurePolicy;

        internal bool TryGetCooldowns(TimeSpan startedAt, out (TimeSpan Shared, TimeSpan Weapon) readyAt)
        {
            readyAt = default;
            if (aimKind < ManualSkillAimKind.Self || aimKind > ManualSkillAimKind.Ground
                || !float.IsFinite(maxAimDistance) || maxAimDistance < 0
                || sharedCooldownMilliseconds < 0 || weaponCooldownMilliseconds < 0
                || failurePolicy != ManualSkillFailurePolicy.PreserveResourcesAndCooldowns)
                return false;

            TimeSpan shared = TimeSpan.FromMilliseconds(sharedCooldownMilliseconds);
            TimeSpan weapon = TimeSpan.FromMilliseconds(weaponCooldownMilliseconds);
            if (startedAt > TimeSpan.MaxValue - shared || startedAt > TimeSpan.MaxValue - weapon)
                return false;

            readyAt = (startedAt + shared, startedAt + weapon);
            return true;
        }
    }
}
