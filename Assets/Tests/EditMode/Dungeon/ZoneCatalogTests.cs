using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Dungeon.Tests
{
    public sealed class ZoneCatalogTests
    {
        private const string CatalogPath = "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset";

        private ZoneCatalog prototype;
        private ZoneCatalog editedCatalog;

        [SetUp]
        public void LoadCatalog()
        {
            prototype = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(CatalogPath);
            Assert.That(prototype, Is.Not.Null);
        }

        [TearDown]
        public void DestroyEditedCatalog()
        {
            if (editedCatalog != null)
                Object.DestroyImmediate(editedCatalog);
        }

        [Test]
        public void PrototypeCatalogContainsAllZones()
        {
            IReadOnlyList<ZoneDefinition> zones = prototype.GetZones();
            Assert.That(zones.Count, Is.EqualTo(5));
            Assert.That(zones[0].ZoneId, Is.EqualTo(1));
            Assert.That(zones[4].ZoneId, Is.EqualTo(5));
        }

        [TestCase(1, "버려진 폐성", 1, 1, 2, 480, 8500, 1500, 0, 0, 0)]
        [TestCase(2, "지하 감옥", 2, 1, 3, 600, 4500, 4500, 1000, 0, 0)]
        [TestCase(3, "저주받은 숲", 3, 2, 4, 720, 0, 4500, 4500, 1000, 0)]
        [TestCase(4, "마왕의 요새", 4, 3, 5, 720, 0, 0, 6000, 3500, 500)]
        [TestCase(5, "심연의 성역", 5, 4, 5, 900, 0, 0, 0, 8500, 1500)]
        public void PrototypeZoneMatchesDesign(
            int id, string theme, int recommended, int min, int max, int seconds,
            int grade1, int grade2, int grade3, int grade4, int grade5)
        {
            ZoneDefinition zone = prototype.GetZone(id);
            var expectedProbabilities = new[] { grade1, grade2, grade3, grade4, grade5 };

            Assert.That(zone.ZoneId, Is.EqualTo(id));
            Assert.That(zone.Theme, Is.EqualTo(theme));
            Assert.That(zone.RecommendedGrade, Is.EqualTo(recommended));
            Assert.That(zone.MinimumRewardGrade, Is.EqualTo(min));
            Assert.That(zone.MaximumRewardGrade, Is.EqualTo(max));
            Assert.That(zone.TimeLimitSeconds, Is.EqualTo(seconds));
            for (int grade = 1; grade <= ZoneDefinition.GradeCount; grade++)
                Assert.That(zone.GetRewardProbabilityBasisPoints(grade), Is.EqualTo(expectedProbabilities[grade - 1]));
        }

        [TestCase("zoneId", 0, "zone ID")]
        [TestCase("recommendedGrade", 0, "recommended grade")]
        [TestCase("recommendedGrade", 6, "recommended grade")]
        [TestCase("minimumRewardGrade", 0, "reward grade range")]
        [TestCase("minimumRewardGrade", 3, "reward grade range")]
        [TestCase("maximumRewardGrade", 6, "reward grade range")]
        [TestCase("timeLimitSeconds", 0, "time limit")]
        [TestCase("timeLimitSeconds", -1, "time limit")]
        public void CatalogRejectsInvalidNumbers(string field, int value, string diagnostic)
        {
            EditZone(zone => zone.FindPropertyRelative(field).intValue = value);
            Assert.That(() => editedCatalog.GetZone(1), Throws.InvalidOperationException.With.Message.Contains(diagnostic));
        }

        [Test]
        public void CatalogRejectsBlankThemes()
        {
            EditZone(zone => zone.FindPropertyRelative("theme").stringValue = " ");
            Assert.That(() => editedCatalog.GetZones(), Throws.InvalidOperationException.With.Message.Contains("theme"));
        }

        [Test]
        public void CatalogRejectsWrongProbabilityCount()
        {
            EditZone(zone => zone.FindPropertyRelative("rewardProbabilityBasisPoints").arraySize = 4);
            Assert.That(() => editedCatalog.GetZones(),
                Throws.InvalidOperationException.With.Message.Contains("exactly five grades"));
        }

        [TestCase(1, -1, "between 0 and 10,000")]
        [TestCase(1, 10001, "between 0 and 10,000")]
        [TestCase(3, 1, "outside the reward grade range")]
        [TestCase(1, 8499, "got 9999")]
        [TestCase(1, 8501, "got 10001")]
        public void CatalogRejectsInvalidProbabilities(int grade, int value, string diagnostic)
        {
            EditZone(zone => SetProbability(zone, grade, value));
            Assert.That(() => editedCatalog.GetZones(), Throws.InvalidOperationException.With.Message.Contains(diagnostic));
        }

        [Test]
        public void CatalogAcceptsSingleRewardGradeAndIndependentRecommendation()
        {
            EditZone(zone =>
            {
                zone.FindPropertyRelative("recommendedGrade").intValue = 5;
                zone.FindPropertyRelative("maximumRewardGrade").intValue = 1;
                zone.FindPropertyRelative("timeLimitSeconds").intValue = 1;
                SetProbability(zone, 1, 10_000);
                SetProbability(zone, 2, 0);
            });

            ZoneDefinition configuredZone = editedCatalog.GetZone(1);
            Assert.That(configuredZone.RecommendedGrade, Is.EqualTo(5));
            Assert.That(configuredZone.GetRewardProbabilityBasisPoints(1), Is.EqualTo(10_000));
            Assert.That(configuredZone.GetRewardProbabilityBasisPoints(2), Is.Zero);
            Assert.That(configuredZone.TimeLimitSeconds, Is.EqualTo(1));
        }

        [Test]
        public void CatalogRejectsAnEmptyCatalog()
        {
            editedCatalog = ScriptableObject.CreateInstance<ZoneCatalog>();
            Assert.That(() => editedCatalog.GetZones(),
                Throws.InvalidOperationException.With.Message.Contains("must contain at least one zone"));
        }

        [Test]
        public void CatalogRejectsDuplicateZoneIds()
        {
            EditZone(zone => zone.FindPropertyRelative("zoneId").intValue = 2);
            Assert.That(() => editedCatalog.GetZones(),
                Throws.InvalidOperationException.With.Message.Contains("duplicate zone ID 2"));
        }

        [Test]
        public void LookupRejectsInvalidAndMissingIds()
        {
            Assert.That(() => prototype.GetZone(0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => prototype.GetZone(6), Throws.TypeOf<KeyNotFoundException>());
        }

        [TestCase(0)]
        [TestCase(6)]
        public void RewardLookupRejectsGradesOutsideTheFiveTiers(int grade)
        {
            Assert.That(() => prototype.GetZone(1).GetRewardProbabilityBasisPoints(grade),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private void EditZone(Action<SerializedProperty> edit)
        {
            editedCatalog = Object.Instantiate(prototype);
            using var serialized = new SerializedObject(editedCatalog);
            edit(serialized.FindProperty("zones").GetArrayElementAtIndex(0));
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetProbability(SerializedProperty zone, int grade, int value)
        {
            zone.FindPropertyRelative("rewardProbabilityBasisPoints").GetArrayElementAtIndex(grade - 1).intValue = value;
        }
    }
}
