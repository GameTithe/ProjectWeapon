using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace ProjectWeapon.Weapons
{
    [CreateAssetMenu(menuName = "ProjectWeapon/Weapon Catalog")]
    public sealed class WeaponCatalog : ScriptableObject
    {
        [SerializeField] private WeaponDefinition[] definitions = Array.Empty<WeaponDefinition>();

        public IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> CreateLookup()
        {
            IReadOnlyList<string> errors = WeaponDefinitionValidator.ValidateCatalogMetadata(definitions);
            if (errors.Count != 0)
                throw new InvalidOperationException($"Weapon catalog '{name}' has invalid metadata: {string.Join(" ", errors)}");

            var lookup = new Dictionary<WeaponDefinitionId, WeaponDefinition>(definitions.Length);
            foreach (WeaponDefinition definition in definitions)
                lookup.Add(definition.Id, definition);

            return new ReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition>(lookup);
        }
    }
}
