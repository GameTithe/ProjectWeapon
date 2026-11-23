using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Weapons.Tests
{
    public sealed class WeaponCatalogTests
    {
        private const string Folder = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyFixtures()
        {
            foreach (Object item in created)
                Object.DestroyImmediate(item);

            created.Clear();
        }

        [Test]
        public void StarterCatalogRegistersExactlyFiveWeaponsAndReportsUnknownIds()
        {
            IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> lookup = LoadStarterLookup();

            Assert.That(lookup.Count, Is.EqualTo(5));
            Assert.That(lookup.TryGetValue(new WeaponDefinitionId("unknown"), out _), Is.False);
            Assert.That(lookup.TryGetValue(new WeaponDefinitionId("OLD_SWORD"), out _), Is.False);
        }

        [TestCase("OldSword", "old_sword", "낡은 검", WeaponKind.Sword, "근접 베기", "전방 넓게 베기")]
        [TestCase("WoodenSpear", "wooden_spear", "나무 창", WeaponKind.Spear, "직선 찌르기", "연속 찌르기")]
        [TestCase("HuntingBow", "hunting_bow", "사냥 활", WeaponKind.Bow, "단일 화살", "화살 3발")]
        [TestCase("Mace", "mace", "철퇴", WeaponKind.Mace, "근접 원형 타격", "주변 충격파")]
        [TestCase("Dagger", "dagger", "단검", WeaponKind.Dagger, "빠른 연속 타격", "순간 연속 베기")]
        public void StoredAndAcquisitionIdsResolveTheSameSerializedWeapon(
            string fileName, string id, string displayName, WeaponKind kind, string basicPattern, string skillPattern)
        {
            IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> lookup = LoadStarterLookup();
            string serialized = JsonUtility.ToJson(new StoredReference { definitionId = id });
            var restoredId = new WeaponDefinitionId(JsonUtility.FromJson<StoredReference>(serialized).definitionId);
            var acquisitionId = new WeaponDefinitionId(id);
            WeaponDefinition saved = lookup[restoredId];
            WeaponDefinition acquired = lookup[acquisitionId];

            Assert.That(saved, Is.SameAs(acquired));
            Assert.That(acquired, Is.SameAs(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Folder + fileName + ".asset")));
            Assert.That(acquired.DisplayName, Is.EqualTo(displayName));
            Assert.That(acquired.Grade, Is.EqualTo(1));
            Assert.That(acquired.Kind, Is.EqualTo(kind));
            Assert.That(acquired.PrototypeBasicAttackPattern, Is.EqualTo(basicPattern));
            Assert.That(acquired.PrototypeSkillPattern, Is.EqualTo(skillPattern));
        }

        [Test]
        public void MetadataValidationAllowsIncompleteActionsAndStrictValidationReportsThem()
        {
            WeaponDefinition row = CreateDefinition("prototype", 1, WeaponKind.Sword);

            Assert.That(WeaponDefinitionValidator.ValidateCatalogMetadata(new[] { row }), Is.Empty);
            Assert.That(CreateCatalog(row).CreateLookup()[row.Id], Is.SameAs(row));
            Assert.That(WeaponDefinitionValidator.Validate(new[] { row }), Has.Count.EqualTo(3));
            Assert.That(WeaponDefinitionValidator.Validate(new[] { row }), Has.Some.Contains("Basic attack is missing."));
        }

        [Test]
        public void LookupRejectsDuplicateDefinitionIds()
        {
            WeaponDefinition first = CreateDefinition("shared", 1, WeaponKind.Sword);
            WeaponDefinition second = CreateDefinition("shared", 1, WeaponKind.Spear);
            WeaponCatalog catalog = CreateCatalog(first, second);

            Assert.That(() => catalog.CreateLookup(),
                Throws.InvalidOperationException.With.Message.Contains("Duplicate definition ID 'shared'"));
        }

        [Test]
        public void LookupRejectsMissingCatalogEntries()
        {
            WeaponCatalog catalog = CreateCatalog(null, CreateDefinition("known", 1, WeaponKind.Sword));

            Assert.That(() => catalog.CreateLookup(),
                Throws.InvalidOperationException.With.Message.Contains("Weapon definition is missing."));
        }

        [TestCase("", 1, WeaponKind.Sword, "Definition ID is missing")]
        [TestCase("known", 0, WeaponKind.Sword, "Grade must be between 1 and 5.")]
        [TestCase("known", 1, (WeaponKind)99, "Weapon kind is unsupported.")]
        public void MetadataValidationRejectsInvalidIdentityGradeAndKind(
            string id, int grade, WeaponKind kind, string diagnostic)
        {
            WeaponDefinition row = CreateDefinition(id, grade, kind);

            Assert.That(WeaponDefinitionValidator.ValidateCatalogMetadata(new[] { row }), Has.Some.Contains(diagnostic));
        }

        private static IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> LoadStarterLookup()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Folder + "PrototypeStarterWeaponCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            return catalog.CreateLookup();
        }

        private WeaponCatalog CreateCatalog(params WeaponDefinition[] definitions)
        {
            var catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
            created.Add(catalog);
            using var serialized = new SerializedObject(catalog);
            SerializedProperty entries = serialized.FindProperty("definitions");
            entries.arraySize = definitions.Length;
            for (int index = 0; index < definitions.Length; index++)
                entries.GetArrayElementAtIndex(index).objectReferenceValue = definitions[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return catalog;
        }

        private WeaponDefinition CreateDefinition(string id, int grade, WeaponKind kind)
        {
            var definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            created.Add(definition);
            using var serialized = new SerializedObject(definition);
            serialized.FindProperty("definitionId").stringValue = id;
            serialized.FindProperty("displayName").stringValue = "Test weapon";
            serialized.FindProperty("grade").intValue = grade;
            serialized.FindProperty("kind").intValue = (int)kind;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        [Serializable]
        private sealed class StoredReference
        {
            public string definitionId;
        }
    }
}
