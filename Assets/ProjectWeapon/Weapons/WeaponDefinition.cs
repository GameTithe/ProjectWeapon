using System;
using UnityEngine;

namespace ProjectWeapon.Weapons
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Weapon Definition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [SerializeField] private string definitionId;
        [SerializeField] private string displayName;
        [SerializeField, Range(1, 5)] private int grade = 1;
        [SerializeField] private ScriptableObject basicAttack;
        [SerializeField] private ScriptableObject skill;
        [SerializeField] private GameObject attackVfx;

        public WeaponDefinitionId Id => new WeaponDefinitionId(definitionId);
        public string DisplayName => displayName;
        public int Grade => grade;
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
