#nullable enable

using System;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.DaggerSkill
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Skills/Dagger Double Slash")]
    public sealed class DaggerDoubleSlashPattern : ManualSkillPattern
    {
        private static readonly WeaponDefinitionId Dagger = new WeaponDefinitionId("dagger");

        [SerializeField, Min(1)] private int followUpMilliseconds = 110;
        [SerializeField, UnityEngine.Range(0f, 360f)] private float arcDegrees = 90f;
        [SerializeField, Min(0f)] private float damage = 17f;

        public TimeSpan FollowUpDelay => TimeSpan.FromMilliseconds(followUpMilliseconds);
        public float ArcDegrees => arcDegrees;
        public float Damage => damage;

        internal bool CanStart(ManualSkillActivationRequest request)
            => request.Pattern == this && request.Weapon.Definition && request.Weapon.Definition.Skill == this
                && request.Weapon.Definition.Id.Equals(Dagger) && AimKind == ManualSkillAimKind.Direction
                && request.Aim.Kind == ManualSkillAimKind.Direction && followUpMilliseconds > 0
                && request.StartedAt <= TimeSpan.MaxValue - FollowUpDelay
                && float.IsFinite(MaxAimDistance) && MaxAimDistance > 0f
                && float.IsFinite(arcDegrees) && arcDegrees > 0f && arcDegrees <= 360f
                && float.IsFinite(damage) && damage > 0f;
    }
}
