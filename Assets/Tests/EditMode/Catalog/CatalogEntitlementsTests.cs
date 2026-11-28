#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Save;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Catalog.Tests
{
    public sealed class CatalogEntitlementsTests
    {
        private static readonly string[] ExpectedIds =
        {
            "catalog_recovery_18_appearance",
            "catalog_recovery_21_title",
            "catalog_recovery_21_armory_decoration"
        };

        [TestCase(0, 0)]
        [TestCase(17, 0)]
        [TestCase(18, 1)]
        [TestCase(20, 1)]
        [TestCase(21, 3)]
        [TestCase(22, 3)]
        public void ThresholdsDeriveTheExpectedUniqueRecordsInFixedOrder(int recovered, int expected)
        {
            SaveSnapshot source = Save(1, CompletedRecords(recovered));
            IReadOnlyList<CatalogEntitlement> unlocked = CatalogEntitlements.Read(source);

            Assert.That(unlocked.Select(item => item.Id), Is.EqualTo(ExpectedIds.Take(expected)));
            Assert.That(unlocked.Select(item => item.Id).Distinct().Count(), Is.EqualTo(expected));
            Assert.That(unlocked.Select(item => item.Kind), Is.EqualTo(new[]
                { CatalogEntitlementKind.Appearance, CatalogEntitlementKind.Title, CatalogEntitlementKind.ArmoryDecoration }
                .Take(expected)));
            Assert.That(unlocked.Select(item => item.RequiredRecoveredCount), Is.EqualTo(new[] { 18, 21, 21 }.Take(expected)));
            Assert.That(source.Weapons, Is.Empty);
            Assert.That(source.Settlements, Is.Empty);
        }

        [Test]
        public void MasteredCountsOnceAndDiscoveryOrUnrecognizedStatesCannotCrossAThreshold()
        {
            var extra = new[]
            {
                new CatalogSaveRecord(Id(18), "discovered"),
                new CatalogSaveRecord(Id(19), "future-v9"),
                new CatalogSaveRecord(Id(20), "Recovered"),
                new CatalogSaveRecord(Id(21), "mastered ")
            };
            Assert.That(CatalogEntitlements.Read(Save(1, CompletedRecords(17).Concat(extra))), Is.Empty);

            var nearlyComplete = CompletedRecords(20).Append(new CatalogSaveRecord(Id(21), "discovered"));
            Assert.That(CatalogEntitlements.Read(Save(1, nearlyComplete)).Select(item => item.Id),
                Is.EqualTo(ExpectedIds.Take(1)));

            var promoted = CompletedRecords(20).Append(new CatalogSaveRecord(Id(21), "mastered"));
            Assert.That(CatalogEntitlements.Read(Save(1, promoted)).Select(item => item.Id), Is.EqualTo(ExpectedIds));
        }

        [Test]
        public void ReorderedInputReturnsFixedRecordsAndCannotMutateAnUnlockedTier()
        {
            CatalogSaveRecord[] records = CompletedRecords(21).ToArray();
            IReadOnlyList<CatalogEntitlement> first = CatalogEntitlements.Read(Save(1, records));
            IReadOnlyList<CatalogEntitlement> replay = CatalogEntitlements.Read(Save(1, records.Reverse()));

            Assert.That(replay, Is.EqualTo(first));
            var list = (IList<CatalogEntitlement>)first;
            Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
            Assert.That(first.Select(item => item.Id), Is.EqualTo(ExpectedIds));
        }

        [Test]
        public void ReopeningExistingSaveAfterInventoryLossKeepsTheSameEntitlementsWithoutWriting()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN114-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "snapshot.json");
            var store = new AtomicSnapshotStore(path);
            var preserved = WeaponInstance.Create(Id(1), WeaponProvenance.Personal, WeaponLocation.Warehouse());
            var laterLost = WeaponInstance.Create(Id(2), WeaponProvenance.Personal, WeaponLocation.Warehouse());
            var olderReceipt = new SettlementSaveRecord(Guid.NewGuid(), "already-committed", "Death");
            CatalogSaveRecord[] records = CompletedRecords(21).Append(new CatalogSaveRecord(Id(22), "opaque-old-state")).ToArray();
            var known = new HashSet<WeaponDefinitionId>(records.Select(item => item.DefinitionId));
            try
            {
                var original = new SaveSnapshot(1, 500, new[] { preserved, laterLost }, records, new[] { olderReceipt });
                Assert.That(store.Commit(0, original).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
                IReadOnlyList<CatalogEntitlement> originalEntitlements = CatalogEntitlements.Read(LoadHealthy(path, known));
                Assert.That(originalEntitlements.Select(item => item.Id), Is.EqualTo(ExpectedIds));

                var afterLoss = new SaveSnapshot(2, 500, new[] { preserved }, records, new[] { olderReceipt });
                Assert.That(store.Commit(1, afterLoss).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
                byte[] before = File.ReadAllBytes(path);
                for (int reconnect = 0; reconnect < 10; reconnect++)
                {
                    SaveSnapshot loaded = LoadHealthy(path, known);
                    Assert.That(CatalogEntitlements.Read(loaded), Is.EqualTo(originalEntitlements));
                    Assert.That(loaded.Weapons.Single().Id, Is.EqualTo(preserved.Id));
                    Assert.That(loaded.Settlements.Single().TransactionId, Is.EqualTo(olderReceipt.TransactionId));
                    Assert.That(loaded.ConfirmedCoins, Is.EqualTo(500));
                    Assert.That(loaded.Revision, Is.EqualTo(2));
                    Assert.That(loaded.Catalog.Single(item => item.DefinitionId.Equals(Id(22))).State,
                        Is.EqualTo("opaque-old-state"));
                }
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.EnumerateFiles(directory))
                        File.Delete(file);
                    Directory.Delete(directory);
                }
            }
        }

        private static SaveSnapshot LoadHealthy(string path, ISet<WeaponDefinitionId> known)
        {
            SnapshotLoadResult loaded = new AtomicSnapshotStore(path).LoadWithRecovery(known.Contains);
            Assert.That(loaded.Status, Is.EqualTo(SnapshotLoadStatus.Healthy));
            return loaded.Snapshot!;
        }

        private static SaveSnapshot Save(long revision, IEnumerable<CatalogSaveRecord> records) =>
            new SaveSnapshot(revision, 0, Array.Empty<WeaponInstance>(), records, Array.Empty<SettlementSaveRecord>());

        private static IEnumerable<CatalogSaveRecord> CompletedRecords(int count) =>
            Enumerable.Range(1, count).Select(index =>
                new CatalogSaveRecord(Id(index), index % 2 == 0 ? "mastered" : "recovered"));

        private static WeaponDefinitionId Id(int index) => new WeaponDefinitionId($"catalog-weapon-{index:D2}");
    }
}
