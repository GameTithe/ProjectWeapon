#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.DpsQA
{
    [CreateAssetMenu(menuName = "ProjectWeapon/QA/DPS Settings")]
    public sealed class DpsQaSettings : ScriptableObject
    {
        [SerializeField] private WeaponCatalog weapons = null!;
        [SerializeField] private WeaponDefinition[] baselineWeapons = Array.Empty<WeaponDefinition>();
        [SerializeField] private int seed = 40;
        [SerializeField, Min(1)] private int trials = 3;
        [SerializeField, Min(1)] private int windowMilliseconds = 12000;
        [SerializeField, Min(1)] private int stepMilliseconds = 50;
        [SerializeField, Min(1)] private int skillPeriodMilliseconds = 2000;
        [SerializeField, Min(1)] private int skillOffsetSlots = 8;
        [SerializeField, Min(0f)] private float skillDamage = 8f;

        public WeaponCatalog Weapons => weapons;
        public IReadOnlyList<WeaponDefinition> BaselineWeapons => baselineWeapons;
        public int Seed => seed;
        public int Trials => trials;
        public int WindowMilliseconds => windowMilliseconds;
        public int StepMilliseconds => stepMilliseconds;
        public int SkillPeriodMilliseconds => skillPeriodMilliseconds;
        public int SkillOffsetSlots => skillOffsetSlots;
        public float SkillDamage => skillDamage;

        internal void Validate()
        {
            if (!weapons || baselineWeapons == null || trials <= 0 || windowMilliseconds <= 0 || stepMilliseconds <= 0
                || windowMilliseconds % stepMilliseconds != 0 || skillPeriodMilliseconds <= 0
                || skillPeriodMilliseconds % stepMilliseconds != 0 || skillOffsetSlots <= 0
                || skillOffsetSlots > skillPeriodMilliseconds / stepMilliseconds || !float.IsFinite(skillDamage)
                || skillDamage < 0f)
                throw new InvalidOperationException("Invalid QA timing, tuning or weapon selection.");
        }
    }
}
