#nullable enable

using System;
using NUnit.Framework;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.Inventory.Tests
{
    public sealed class WeaponInstanceTests
    {
        private const string PersonalId = "a2479882-cdad-4423-8f7f-f2f394767412";
        private static readonly Guid RunGuid = new Guid("6381b71b-9b12-4069-b514-390eb2f0ca37");
        private static readonly WeaponDefinitionId Definition = new WeaponDefinitionId("training-sword");

        [Test]
        public void SameDefinitionProducesDistinctValidInstances()
        {
            WeaponInstance first = WeaponInstance.Create(Definition, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            WeaponInstance second = WeaponInstance.Create(Definition, WeaponProvenance.Personal, WeaponLocation.Warehouse());

            Assert.That(first.DefinitionId, Is.EqualTo(second.DefinitionId));
            Assert.That(first.Id, Is.Not.EqualTo(second.Id));
            Assert.That(first.Id.IsValid && second.Id.IsValid, Is.True);
            Assert.That(WeaponInstanceValidator.Validate(new[] { first, second }), Is.Empty);
        }

        [Test]
        public void UnityJsonRoundTripPreservesIdDefinitionProvenanceAndReservation()
        {
            Assert.That(WeaponInstanceId.TryParse(PersonalId, out WeaponInstanceId assetId), Is.True);
            WeaponInstance original = WeaponInstance.Rehydrate(assetId, Definition, WeaponProvenance.Loan,
                WeaponLocation.RunReserved(RunGuid));

            WeaponInstance copy = JsonUtility.FromJson<WeaponInstance>(JsonUtility.ToJson(original));
            Assert.That(copy.Id, Is.EqualTo(original.Id));
            Assert.That(copy.DefinitionId, Is.EqualTo(Definition));
            Assert.That(copy.Provenance, Is.EqualTo(WeaponProvenance.Loan));
            Assert.That(copy.IsLoaned, Is.True);
            Assert.That(copy.Location.Kind, Is.EqualTo(WeaponLocationKind.RunReserved));
            Assert.That(copy.Location.ReservedRunId, Is.EqualTo(RunGuid));
            Assert.That(copy.Location.SlotIndex, Is.Null);
            Assert.That(copy.IsValid, Is.True);
        }

        [Test]
        public void UnityJsonRoundTripPreservesSlotIndexAndSingleLocation()
        {
            WeaponInstance original = WeaponInstance.Create(Definition, WeaponProvenance.ExplorationLoot,
                WeaponLocation.Carried(2));

            WeaponInstance copy = JsonUtility.FromJson<WeaponInstance>(JsonUtility.ToJson(original));
            Assert.That(copy.Id, Is.EqualTo(original.Id));
            Assert.That(copy.Location.Kind, Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(copy.Location.SlotIndex, Is.EqualTo(2));
            Assert.That(copy.Location.ReservedRunId, Is.Null);
            Assert.That(copy.IsLoaned, Is.False);
        }

        [Test]
        public void CollectionFlagsSameInstanceIdEvenWhenLocationsDifferAfterReload()
        {
            Assert.That(WeaponInstanceId.TryParse(PersonalId, out WeaponInstanceId assetId), Is.True);
            WeaponInstance source = WeaponInstance.Rehydrate(assetId, Definition, WeaponProvenance.Personal,
                WeaponLocation.Warehouse());
            WeaponInstance duplicate = WeaponInstance.Rehydrate(assetId, Definition, WeaponProvenance.Personal,
                WeaponLocation.Equipped(1));
            string duplicateJson = JsonUtility.ToJson(duplicate).Replace(PersonalId, PersonalId.ToUpperInvariant());

            var result = WeaponInstanceValidator.Validate(new[] { source, JsonUtility.FromJson<WeaponInstance>(duplicateJson) });

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Kind, Is.EqualTo(WeaponInstanceIssueKind.DuplicateInstanceId));
            Assert.That(result[0].Index, Is.EqualTo(1));
            Assert.That(result[0].InstanceId, Is.EqualTo(assetId));
            Assert.That(result[0].InstanceId.ToString(), Is.EqualTo(PersonalId));
        }

        [Test]
        public void ValidatorIdentifiesMissingAndMalformedSerializedItems()
        {
            WeaponInstance malformed = JsonUtility.FromJson<WeaponInstance>("{}");
            var result = WeaponInstanceValidator.Validate(new WeaponInstance?[] { null, malformed });

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Kind, Is.EqualTo(WeaponInstanceIssueKind.MissingInstance));
            Assert.That(result[1].Kind, Is.EqualTo(WeaponInstanceIssueKind.InvalidInstanceData));
            Assert.That(result[1].Index, Is.EqualTo(1));
        }

        [Test]
        public void ValidatorRejectsMixedLocationPayloadFromSerialization()
        {
            const string mixedLocationJson = "{\"kind\":1,\"slotIndex\":2,\"runId\":\"6381b71b-9b12-4069-b514-390eb2f0ca37\"}";
            var badLocation = JsonUtility.FromJson<WeaponLocation>(mixedLocationJson);
            Assert.That(badLocation.IsValid, Is.False);
            Assert.Throws<ArgumentException>(() => WeaponInstance.Create(Definition, WeaponProvenance.Personal, badLocation));
        }

        [Test]
        public void RuntimeRejectsInvalidMetadataAndLocationInputs()
        {
            Assert.Throws<ArgumentException>(() => WeaponInstance.Rehydrate(default, Definition, WeaponProvenance.Personal,
                WeaponLocation.Warehouse()));
            Assert.Throws<ArgumentException>(() => WeaponInstance.Create(default, WeaponProvenance.Personal,
                WeaponLocation.Warehouse()));
            Assert.Throws<ArgumentOutOfRangeException>(() => WeaponInstance.Create(Definition, WeaponProvenance.Unspecified,
                WeaponLocation.Warehouse()));
            Assert.Throws<ArgumentException>(() => WeaponInstance.Create(Definition, WeaponProvenance.Loan,
                WeaponLocation.Warehouse()));
            Assert.Throws<ArgumentOutOfRangeException>(() => WeaponLocation.Carried(-1));
            Assert.Throws<ArgumentException>(() => WeaponLocation.RunReserved(Guid.Empty));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not-a-guid")]
        [TestCase("00000000-0000-0000-0000-000000000000")]
        [TestCase("a2479882cdad44238f7ff2f394767412")]
        [TestCase(" a2479882-cdad-4423-8f7f-f2f394767412 ")]
        public void UnusableStoredIdsAreRejected(string? value)
        {
            Assert.That(WeaponInstanceId.TryParse(value, out WeaponInstanceId id), Is.False);
            Assert.That(id.IsValid, Is.False);
        }

        [Test]
        public void RehydrationNormalizesGuidCaseAndPreservesLogicalIdentity()
        {
            Assert.That(WeaponInstanceId.TryParse(PersonalId.ToUpperInvariant(), out WeaponInstanceId id), Is.True);

            Assert.That(id.ToString(), Is.EqualTo(PersonalId));
            Assert.That(WeaponInstanceId.TryParse(id.ToString(), out WeaponInstanceId parsedAgain), Is.True);
            Assert.That(parsedAgain, Is.EqualTo(id));
        }
    }
}
