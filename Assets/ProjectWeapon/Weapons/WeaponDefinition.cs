using System;
using UnityEngine;

namespace ProjectWeapon.Weapons
{
    public enum WeaponKind
    {
        Unspecified = 0,
        Sword = 1,
        Spear = 2,
        Bow = 3,
        Mace = 4,
        Dagger = 5
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Weapon Definition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [SerializeField] private string definitionId;
        [SerializeField] private string displayName;
        [SerializeField, Range(1, 5)] private int grade = 1;
        [SerializeField] private WeaponKind kind = WeaponKind.Unspecified;
        [SerializeField] private string prototypeBasicAttackPattern = string.Empty;
        [SerializeField] private string prototypeSkillPattern = string.Empty;
        [SerializeField] private ScriptableObject basicAttack;
        [SerializeField] private ScriptableObject skill;
        [SerializeField] private GameObject attackVfx;

        public WeaponDefinitionId Id => new WeaponDefinitionId(definitionId);
        public string DisplayName => displayName;
        public int Grade => grade;
        public WeaponKind Kind => kind;
        public string PrototypeBasicAttackPattern => prototypeBasicAttackPattern;
        public string PrototypeSkillPattern => prototypeSkillPattern;
        public ScriptableObject BasicAttack => basicAttack;
        public ScriptableObject Skill => skill;
        public GameObject AttackVfx => attackVfx;
    }

    public readonly struct WeaponDefinitionId : IEquatable<WeaponDefinitionId>
    {
        public WeaponDefinitionId(string value)
        {
            Value = value;
        }

        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value) && Value == Value.Trim();

        public bool Equals(WeaponDefinitionId other) =>
            StringComparer.Ordinal.Equals(Value, other.Value);

        public override bool Equals(object obj) =>
            obj is WeaponDefinitionId other && Equals(other);

        public override int GetHashCode() =>
            Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;
    }
}
