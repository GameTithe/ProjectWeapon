#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectWeapon.DpsQA.Tests
{
    public sealed class DpsQaMeasurementsTests
    {
        private const string Folder = "Assets/Scenes/DpsQA/";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(0, 0);
        private static readonly WorldPoint2D Dummy = new WorldPoint2D(1, 0);

        [UnityTest]
        public IEnumerator ActualSceneStartsAndDisplaysMeasuredGradeAverages()
        {
            const string scenePath = Folder + "DpsQA.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            try
            {
                yield return null;
                Assert.That(scene.isLoaded, Is.True);
                var host = scene.GetRootGameObjects().Single(root => root.name == "DPS QA").GetComponent<DpsQaScene>();
                Assert.That(host.Report, Is.Not.Null);
                DpsQaReport report = host.Report!;
                Assert.That(report.Passes, Is.True);
                Assert.That(report.Conditions, Does.Contain("Seed 40").And.Contain("1.00 units"));
                var canvas = scene.GetRootGameObjects().Single(root => root.name == "DPS QA Canvas");
                Assert.That(canvas.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "DPS Report").text,
                    Does.Contain("Fixture order: PASS").And.Contain("With fixed skill"));
            }
            finally
            {
                if (scene.isLoaded)
                    SceneManager.UnloadSceneAsync(scene);
            }
            yield return null;
        }

        [Test]
        public void SameSeedAndTuningReplayAcrossAllMeasuredWeapons()
        {
            DpsQaSettings settings = Settings();
            DpsQaReport first = DpsQaRunner.Measure(settings, Origin, Dummy);
            DpsQaReport repeat = DpsQaRunner.Measure(settings, Origin, Dummy);
            Assert.That(repeat.DetailedLog(), Is.EqualTo(first.DetailedLog()));
            Assert.That(first.Conditions, Does.Contain("first offsets 350, 100, 50ms"));
            CollectionAssert.AreEqual(new[] { 5, 1, 1, 1, 1 }, first.Grades.Select(grade => grade.SampleCount));
            Assert.That(first.Weapons.Count(weapon => weapon.Source.StartsWith("baseline:", StringComparison.Ordinal)),
                Is.EqualTo(5));
            Assert.That(first.Weapons.All(weapon => weapon.BasicHitsPerMode > 1 && weapon.SkillHits == 18), Is.True);
            Assert.That(first.Weapons.All(weapon => Math.Abs(weapon.CombinedDps - weapon.BasicDps - 4) < 0.00001),
                Is.True);
            Assert.That(first.Grades[0].BasicDps, Is.EqualTo(10.8).Within(0.00001));
            Assert.That(first.Weapons.Single(weapon => weapon.Source == "baseline:hunting_bow").BasicHitsPerMode,
                Is.EqualTo(33));
            DpsWeaponResult dagger = first.Weapons.Single(weapon => weapon.Source == "baseline:dagger");
            Assert.That(dagger.BasicHitsPerMode, Is.EqualTo(114));
            Assert.That(dagger.BasicDps, Is.EqualTo(570d / 36d).Within(0.00001));
            Assert.That(first.Grades[4].BasicDps, Is.EqualTo(32));
            var alternative = Object.Instantiate(settings);
            try
            {
                var data = new SerializedObject(alternative);
                data.FindProperty("seed").intValue = 41;
                data.ApplyModifiedPropertiesWithoutUndo();
                DpsQaReport changed = DpsQaRunner.Measure(alternative, Origin, Dummy);
                Assert.That(changed.Conditions, Does.Contain("first offsets 200, 150, 300ms"));
            }
            finally
            {
                Object.DestroyImmediate(alternative);
            }
        }

        [Test]
        public void BasicArrowCountsItsContactOnlyInsideTheMeasurementWindow()
        {
            var settings = Object.Instantiate(Settings());
            try
            {
                var data = new SerializedObject(settings);
                data.FindProperty("trials").intValue = 1;
                data.FindProperty("windowMilliseconds").intValue = 100;
                data.ApplyModifiedPropertiesWithoutUndo();
                DpsQaReport beforeContact = DpsQaRunner.Measure(settings, Origin, Dummy);
                Assert.That(beforeContact.Weapons.Single(weapon => weapon.Source == "baseline:hunting_bow")
                    .BasicHitsPerMode, Is.Zero);

                data.FindProperty("windowMilliseconds").intValue = 150;
                data.ApplyModifiedPropertiesWithoutUndo();
                DpsQaReport afterContact = DpsQaRunner.Measure(settings, Origin, Dummy);
                Assert.That(afterContact.Weapons.Single(weapon => weapon.Source == "baseline:hunting_bow")
                    .BasicHitsPerMode, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(120, 1)]
        [TestCase(130, 2)]
        public void DaggerIncludesTheImmediateStrikeAndOnlyCountsFollowUpInsideTheWindow(int window, int expectedHits)
        {
            var settings = Object.Instantiate(Settings());
            try
            {
                var actual = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(
                    "Assets/ProjectWeapon/Weapons/PrototypeStarter/Dagger.asset");
                WeaponDefinition fixture = settings.Weapons.CreateLookup()[new WeaponDefinitionId("qa_dagger")];
                Assert.That(settings.BaselineWeapons, Does.Contain(actual));
                Assert.That(fixture.BasicAttack, Is.SameAs(actual.BasicAttack));

                var data = new SerializedObject(settings);
                data.FindProperty("trials").intValue = 1;
                data.FindProperty("windowMilliseconds").intValue = window;
                data.FindProperty("stepMilliseconds").intValue = 10;
                data.ApplyModifiedPropertiesWithoutUndo();

                DpsWeaponResult dagger = DpsQaRunner.Measure(settings, Origin, Dummy).Weapons
                    .Single(weapon => weapon.Source == "baseline:dagger");
                Assert.That(dagger.BasicHitsPerMode, Is.EqualTo(expectedHits));
                Assert.That(dagger.BasicDps, Is.EqualTo(expectedHits * 5d / (window / 1000d)).Within(0.00001));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(1)]
        [TestCase(4)]
        public void AWeakerOrEqualHigherTierTriggersBothComparisons(int sourceGrade)
        {
            DpsQaSettings original = Settings();
            WeaponDefinition[] originalWeapons = original.Weapons.CreateLookup().Values.ToArray();
            var measured = Object.Instantiate(originalWeapons.Single(weapon => weapon.Grade == 5));
            var settings = Object.Instantiate(original);
            var catalog = Object.Instantiate(original.Weapons);
            try
            {
                var fields = new SerializedObject(measured);
                fields.FindProperty("basicAttack").objectReferenceValue =
                    originalWeapons.First(weapon => weapon.Grade == sourceGrade).BasicAttack;
                fields.ApplyModifiedPropertiesWithoutUndo();
                ReplaceCatalog(settings, catalog, originalWeapons.Select(weapon => weapon.Grade == 5 ? measured : weapon).ToArray());
                DpsQaReport report = DpsQaRunner.Measure(settings, Origin, Dummy);
                Assert.That(report.Passes, Is.False);
                Assert.That(report.MissingGrades, Is.Empty);
                Assert.That(report.Violations, Is.EquivalentTo(new[] { "Basic: grade 5 <= grade 4", "With skill: grade 5 <= grade 4" }));
                if (sourceGrade == 4)
                    Assert.That(report.Grades[4].BasicDps, Is.EqualTo(report.Grades[3].BasicDps));
                else
                    Assert.That(report.Grades[4].BasicDps, Is.LessThan(report.Grades[3].BasicDps));
            }
            finally
            {
                Object.DestroyImmediate(measured);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void MissingGradesAndInvalidTimingCannotBeReportedAsAnOrderingPass()
        {
            DpsQaSettings original = Settings();
            var settings = Object.Instantiate(original);
            var catalog = Object.Instantiate(original.Weapons);
            try
            {
                ReplaceCatalog(settings, catalog,
                    original.Weapons.CreateLookup().Values.Where(weapon => weapon.Grade != 3).ToArray());
                DpsQaReport incomplete = DpsQaRunner.Measure(settings, Origin, Dummy);
                Assert.That(incomplete.Passes, Is.False);
                Assert.That(incomplete.MissingGrades, Is.EqualTo(new[] { 3 }));

                var data = new SerializedObject(settings);
                data.FindProperty("stepMilliseconds").intValue = 51;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.Throws<InvalidOperationException>(() => DpsQaRunner.Measure(settings, Origin, Dummy));
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(catalog);
            }
        }

        private static DpsQaSettings Settings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<DpsQaSettings>(Folder + "DpsCalibration.asset");
            Assert.That(settings, Is.Not.Null);
            return settings;
        }

        private static void ReplaceCatalog(DpsQaSettings settings, WeaponCatalog catalog, WeaponDefinition[] cases)
        {
            var catalogFields = new SerializedObject(catalog);
            var entries = catalogFields.FindProperty("definitions");
            entries.arraySize = cases.Length;
            for (int index = 0; index < cases.Length; index++)
                entries.GetArrayElementAtIndex(index).objectReferenceValue = cases[index];
            catalogFields.ApplyModifiedPropertiesWithoutUndo();
            var settingsFields = new SerializedObject(settings);
            settingsFields.FindProperty("weapons").objectReferenceValue = catalog;
            settingsFields.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
