#nullable enable

using System;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.Inventory
{
    public enum WeaponProvenance
    {
        Unspecified = 0,
        Personal = 1,
        ExplorationLoot = 2,
        Loan = 3
    }

    [Serializable]
    public sealed class WeaponInstance
    {
        [SerializeField] private WeaponInstanceId id;
        [SerializeField] private string definitionId;
        [SerializeField] private WeaponProvenance provenance;
        [SerializeField] private WeaponLocation location;

        private WeaponInstance(WeaponInstanceId id, WeaponDefinitionId definitionId,
            WeaponProvenance provenance, WeaponLocation location)
        {
            this.id = id;
            this.definitionId = definitionId.Value;
            this.provenance = provenance;
            this.location = location;
        }

        public WeaponInstanceId Id => id;
        public WeaponDefinitionId DefinitionId => new WeaponDefinitionId(definitionId);
        public WeaponProvenance Provenance => provenance;
        public WeaponLocation Location => location;
        public bool IsLoaned => provenance == WeaponProvenance.Loan;

        public bool IsValid => id.IsValid && DefinitionId.IsValid && IsKnownProvenance(provenance)
            && IsAllowedLocation(provenance, location);

        public static WeaponInstance Create(WeaponDefinitionId definitionId, WeaponProvenance provenance,
            WeaponLocation location) => Rehydrate(WeaponInstanceId.New(), definitionId, provenance, location);

        public static WeaponInstance Rehydrate(WeaponInstanceId id, WeaponDefinitionId definitionId,
            WeaponProvenance provenance, WeaponLocation location)
        {
            if (!id.IsValid)
                throw new ArgumentException("The weapon instance ID is invalid.", nameof(id));

            if (!definitionId.IsValid)
                throw new ArgumentException("The weapon definition ID is invalid.", nameof(definitionId));

            if (!IsKnownProvenance(provenance))
                throw new ArgumentOutOfRangeException(nameof(provenance));

            if (!IsAllowedLocation(provenance, location))
                throw new ArgumentException("The weapon location is invalid for the given provenance.", nameof(location));

            return new WeaponInstance(id, definitionId, provenance, location);
        }

        private static bool IsKnownProvenance(WeaponProvenance value)
            => value == WeaponProvenance.Personal || value == WeaponProvenance.ExplorationLoot
                || value == WeaponProvenance.Loan;

        private static bool IsAllowedLocation(WeaponProvenance value, WeaponLocation position)
            => position.IsValid && (value != WeaponProvenance.Loan || position.Kind != WeaponLocationKind.Warehouse);
    }
}
