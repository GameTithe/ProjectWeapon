#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.Inventory
{
    [Serializable]
    public struct WeaponInstanceId : IEquatable<WeaponInstanceId>
    {
        [SerializeField] private string? value;

        private WeaponInstanceId(Guid value)
        {
            this.value = value.ToString("D");
        }

        public bool IsValid => TryRead(value, out _);

        public static WeaponInstanceId New() => new WeaponInstanceId(Guid.NewGuid());

        public static bool TryParse(string? value, out WeaponInstanceId id)
        {
            if (TryRead(value, out Guid parsed))
            {
                id = new WeaponInstanceId(parsed);
                return true;
            }

            id = default;
            return false;
        }

        public bool Equals(WeaponInstanceId other) =>
            StringComparer.OrdinalIgnoreCase.Equals(value, other.value);

        public override bool Equals(object? obj) =>
            obj is WeaponInstanceId other && Equals(other);

        public override int GetHashCode() =>
            value == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value);

        public override string ToString() => TryRead(value, out Guid parsed) ? parsed.ToString("D") : string.Empty;

        private static bool TryRead(string? input, out Guid parsed)
        {
            if (input?.Length == 36 && Guid.TryParseExact(input, "D", out parsed) && parsed != Guid.Empty)
                return true;

            parsed = Guid.Empty;
            return false;
        }

        public static bool operator ==(WeaponInstanceId left, WeaponInstanceId right) => left.Equals(right);
        public static bool operator !=(WeaponInstanceId left, WeaponInstanceId right) => !left.Equals(right);
    }
}
