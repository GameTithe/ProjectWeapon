using System;
using System.Collections.Generic;

namespace ProjectWeapon.Weapons
{
    public static class WeaponDefinitionValidator
    {
        public static IReadOnlyList<string> Validate(IEnumerable<WeaponDefinition> definitions) =>
            Validate(definitions, validateActionReferences: true);

        public static IReadOnlyList<string> ValidateCatalogMetadata(IEnumerable<WeaponDefinition> definitions) =>
            Validate(definitions, validateActionReferences: false);

        private static IReadOnlyList<string> Validate(
            IEnumerable<WeaponDefinition> definitions, bool validateActionReferences)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var errors = new List<string>();
            var ids = new HashSet<WeaponDefinitionId>();
            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    errors.Add("Weapon definition is missing.");
                    continue;
                }

                var label = definition.name;
                if (!definition.Id.IsValid)
                {
                    errors.Add($"{label}: Definition ID is missing or has surrounding whitespace.");
                }
                else if (!ids.Add(definition.Id))
                {
                    errors.Add($"{label}: Duplicate definition ID '{definition.Id}'.");
                }

                if (string.IsNullOrWhiteSpace(definition.DisplayName))
                {
                    errors.Add($"{label}: Display name is missing.");
                }

                if (definition.Grade < 1 || definition.Grade > 5)
                {
                    errors.Add($"{label}: Grade must be between 1 and 5.");
                }

                if (!Enum.IsDefined(typeof(WeaponKind), definition.Kind))
                {
                    errors.Add($"{label}: Weapon kind is unsupported.");
                }

                if (!validateActionReferences)
                    continue;

                if (definition.BasicAttack == null)
                {
                    errors.Add($"{label}: Basic attack is missing.");
                }

                if (definition.Skill == null)
                {
                    errors.Add($"{label}: Skill is missing.");
                }

                if (definition.AttackVfx == null)
                {
                    errors.Add($"{label}: Attack VFX is missing.");
                }
            }

            return errors;
        }
    }
}
