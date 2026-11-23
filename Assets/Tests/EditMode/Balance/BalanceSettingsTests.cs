using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Balance.Tests
{
    public sealed class BalanceSettingsTests
    {
        private BalanceSettings settings;

        [SetUp]
        public void CreateSettings()
        {
            settings = ScriptableObject.CreateInstance<BalanceSettings>();
            settings.name = "Balance test fixture";
        }

        [TearDown]
        public void DestroySettings()
        {
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void NewScaffoldContainsAVersionAndNoDomainData()
        {
            Assert.That(settings.SchemaVersion, Is.EqualTo(BalanceSettings.CurrentSchemaVersion));
            Assert.That(settings.Revision, Is.EqualTo(1));
            Assert.That(settings.GetRanges(), Is.Empty);
            Assert.That(settings.GetDistributions(), Is.Empty);
        }

        [TestCase("schemaVersion", 0, "schema version 0")]
        [TestCase("schemaVersion", 2, "schema version 2")]
        [TestCase("revision", 0, "revision must be positive")]
        [TestCase("revision", -1, "revision must be positive")]
        public void RejectsUnsupportedSchemasAndRevisions(string field, int value, string diagnostic)
        {
            Edit(item => item.FindProperty(field).intValue = value);

            Assert.That(() => settings.GetRanges(), Throws.InvalidOperationException.With.Message.Contains(diagnostic));
        }

        [Test]
        public void ExposesRevisionAndRecordedApprovalWithValidRanges()
        {
            Edit(item =>
            {
                item.FindProperty("revision").intValue = 2;
                SerializedProperty ranges = item.FindProperty("ranges");
                ranges.arraySize = 4;
                WriteRange(ranges.GetArrayElementAtIndex(0), "time.test", BalanceRangeKind.TimeLimitSeconds, 1, 2);
                WriteRange(ranges.GetArrayElementAtIndex(1), "dps.test", BalanceRangeKind.DamagePerSecond, 0, 1.5);
                WriteRange(ranges.GetArrayElementAtIndex(2), "coins.test", BalanceRangeKind.CoinSupply, 0, 0);
                WriteRange(ranges.GetArrayElementAtIndex(3), "capacity.test", BalanceRangeKind.BagCapacity, 0, 2);
                ranges.GetArrayElementAtIndex(3).FindPropertyRelative("approval").intValue = (int)BalanceApproval.Approved;
            });

            var ranges = settings.GetRanges();
            Assert.That(settings.Revision, Is.EqualTo(2));
            Assert.That(ranges.Count, Is.EqualTo(4));
            Assert.That(ranges[0].Approval, Is.EqualTo(BalanceApproval.Draft));
            Assert.That(ranges[1].Maximum, Is.EqualTo(1.5));
            Assert.That(ranges[3].Key, Is.EqualTo("capacity.test"));
            Assert.That(ranges[3].Kind, Is.EqualTo(BalanceRangeKind.BagCapacity));
            Assert.That(ranges[3].Approval, Is.EqualTo(BalanceApproval.Approved));
        }

        [TestCase(BalanceRangeKind.TimeLimitSeconds, 0, 1, "positive whole seconds")]
        [TestCase(BalanceRangeKind.TimeLimitSeconds, 1, 1.5, "positive whole seconds")]
        [TestCase(BalanceRangeKind.DamagePerSecond, 2, 1, "finite and ordered")]
        [TestCase(BalanceRangeKind.DamagePerSecond, -1, 1, "DPS cannot be negative")]
        [TestCase(BalanceRangeKind.DamagePerSecond, 0, double.PositiveInfinity, "finite and ordered")]
        [TestCase(BalanceRangeKind.DamagePerSecond, double.NaN, 1, "finite and ordered")]
        [TestCase(BalanceRangeKind.CoinSupply, -1, 0, "nonnegative whole numbers")]
        [TestCase(BalanceRangeKind.CoinSupply, 1, 1.5, "nonnegative whole numbers")]
        [TestCase(BalanceRangeKind.BagCapacity, 0.5, 1, "nonnegative whole numbers")]
        [TestCase((BalanceRangeKind)99, 1, 1, "range kind is unsupported")]
        public void RejectsInvalidRanges(BalanceRangeKind kind, double minimum, double maximum, string diagnostic)
        {
            Edit(item => WriteRange(FirstRange(item), "test", kind, minimum, maximum));

            Assert.That(() => settings.GetRanges(), Throws.InvalidOperationException.With.Message.Contains(diagnostic));
        }

        [TestCase("")]
        [TestCase(" key")]
        public void RejectsInvalidSettingKeys(string key)
        {
            Edit(item => WriteRange(FirstRange(item), key, BalanceRangeKind.BagCapacity, 0, 1));

            Assert.That(() => settings.GetRanges(), Throws.InvalidOperationException.With.Message.Contains("requires a key"));
        }

        [Test]
        public void RejectsDuplicateKeysAcrossRangesAndDistributions()
        {
            Edit(item =>
            {
                WriteRange(FirstRange(item), "duplicate", BalanceRangeKind.BagCapacity, 0, 1);
                WriteDistribution(FirstDistribution(item), "duplicate", 10_000, 0);
            });

            Assert.That(() => settings.GetDistributions(),
                Throws.InvalidOperationException.With.Message.Contains("duplicate setting key 'duplicate'"));
        }

        [Test]
        public void RejectsUnknownApprovalStatus()
        {
            Edit(item =>
            {
                SerializedProperty range = FirstRange(item);
                WriteRange(range, "test", BalanceRangeKind.BagCapacity, 0, 1);
                range.FindPropertyRelative("approval").intValue = 99;
            });

            Assert.That(() => settings.GetRanges(),
                Throws.InvalidOperationException.With.Message.Contains("unsupported approval status"));
        }

        [Test]
        public void AcceptsProbabilityBoundaryWeightsAndUsesDraftByDefault()
        {
            Edit(item => WriteDistribution(FirstDistribution(item), "test", 0, 10_000));

            BalanceDistribution distribution = settings.GetDistributions()[0];
            Assert.That(distribution.Approval, Is.EqualTo(BalanceApproval.Draft));
            Assert.That(distribution.Outcomes[0].BasisPoints, Is.Zero);
            Assert.That(distribution.Outcomes[1].Id, Is.EqualTo("second"));
            Assert.That(distribution.Outcomes[1].BasisPoints, Is.EqualTo(10_000));
        }

        [TestCase(-1, 10_000, "between 0 and 10,000")]
        [TestCase(0, 10_001, "between 0 and 10,000")]
        [TestCase(5_000, 4_999, "got 9999")]
        [TestCase(5_000, 5_001, "got 10001")]
        public void RejectsInvalidWeightRangesAndTotals(int first, int second, string diagnostic)
        {
            Edit(item => WriteDistribution(FirstDistribution(item), "test", first, second));

            Assert.That(() => settings.GetDistributions(),
                Throws.InvalidOperationException.With.Message.Contains(diagnostic));
        }

        [Test]
        public void RejectsEmptyDistributions()
        {
            Edit(item => FirstDistribution(item).FindPropertyRelative("key").stringValue = "test");

            Assert.That(() => settings.GetDistributions(),
                Throws.InvalidOperationException.With.Message.Contains("at least one outcome"));
        }

        [TestCase("", "requires an ID")]
        [TestCase(" second", "requires an ID")]
        [TestCase("first", "duplicate outcome ID")]
        public void RejectsInvalidAndDuplicateOutcomes(string id, string diagnostic)
        {
            Edit(item =>
            {
                SerializedProperty distribution = FirstDistribution(item);
                WriteDistribution(distribution, "test", 5_000, 5_000);
                distribution.FindPropertyRelative("outcomes").GetArrayElementAtIndex(1)
                    .FindPropertyRelative("id").stringValue = id;
            });

            Assert.That(() => settings.GetDistributions(),
                Throws.InvalidOperationException.With.Message.Contains(diagnostic));
        }

        private void Edit(Action<SerializedObject> edit)
        {
            using var serialized = new SerializedObject(settings);
            edit(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty FirstRange(SerializedObject item)
        {
            SerializedProperty ranges = item.FindProperty("ranges");
            ranges.arraySize = 1;
            return ranges.GetArrayElementAtIndex(0);
        }

        private static SerializedProperty FirstDistribution(SerializedObject item)
        {
            SerializedProperty distributions = item.FindProperty("distributions");
            distributions.arraySize = 1;
            return distributions.GetArrayElementAtIndex(0);
        }

        private static void WriteRange(SerializedProperty property, string key,
            BalanceRangeKind kind, double minimum, double maximum)
        {
            property.FindPropertyRelative("key").stringValue = key;
            property.FindPropertyRelative("kind").intValue = (int)kind;
            property.FindPropertyRelative("minimum").doubleValue = minimum;
            property.FindPropertyRelative("maximum").doubleValue = maximum;
        }

        private static void WriteDistribution(SerializedProperty property, string key, int first, int second)
        {
            property.FindPropertyRelative("key").stringValue = key;
            SerializedProperty outcomes = property.FindPropertyRelative("outcomes");
            outcomes.arraySize = 2;
            WriteOutcome(outcomes.GetArrayElementAtIndex(0), "first", first);
            WriteOutcome(outcomes.GetArrayElementAtIndex(1), "second", second);
        }

        private static void WriteOutcome(SerializedProperty property, string id, int basisPoints)
        {
            property.FindPropertyRelative("id").stringValue = id;
            property.FindPropertyRelative("basisPoints").intValue = basisPoints;
        }
    }
}
