using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProjectWeapon.Weapons.Editor
{
    internal static class WeaponDefinitionValidationMenu
    {
        [MenuItem("Tools/ProjectWeapon/Validate Weapon Definitions")]
        private static void ValidateWeaponDefinitions()
        {
            var paths = AssetDatabase.FindAssets("t:WeaponDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var errors = WeaponDefinitionValidator.Validate(
                paths.Select(AssetDatabase.LoadAssetAtPath<WeaponDefinition>));

            if (errors.Count == 0)
            {
                Debug.Log($"Validated {paths.Length} weapon definition asset(s).");
                return;
            }

            foreach (var error in errors)
            {
                Debug.LogError(error);
            }
        }
    }
}
