#nullable enable

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Catalog.Tests
{
    public sealed class CatalogProgressTests
    {
        private static readonly WeaponDefinitionId Spear = new WeaponDefinitionId("wooden_spear");
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Legacy = new WeaponDefinitionId("legacy_sword");
        private static readonly RunId TestRun = new RunId(new Guid("40dfde5c-69e2-4fcd-b0dc-5bc911de63c0"));

        [Test]
        public void ConfirmedPickupOpensDiscoveryButLoanAndWarehouseItemDoNot()
        {
            var catalog = EmptyCatalog();
            WeaponInstance held = WeaponInstance.Create(Spear, WeaponProvenance.ExplorationLoot, WeaponLocation.Carried(0));
            WeaponInstance loan = WeaponInstance.Create(Sword, WeaponProvenance.Loan, WeaponLocation.Equipped(0));
            WeaponInstance warehouse = WeaponInstance.Create(Sword, WeaponProvenance.Personal, WeaponLocation.Warehouse());

            Assert.That(catalog.GetStage(Spear), Is.EqualTo(CatalogStage.Undiscovered));
            Assert.That(catalog.RecordPickup(loan), Is.False);
            Assert.That(catalog.RecordPickup(warehouse), Is.False);
            Assert.That(catalog.RecordPickup(held), Is.True);
            Assert.That(catalog.RecordPickup(held), Is.False);
            Assert.That(catalog.RecordRecovery(FinishRun(success: false), held), Is.False);
            Assert.That(catalog.GetStage(Spear), Is.EqualTo(CatalogStage.Discovered));
            Assert.That(catalog.GetStage(Sword), Is.EqualTo(CatalogStage.Undiscovered));
            Assert.That(catalog.RecoveredCount, Is.Zero);
        }

        [Test]
        public void ACommittedDrawDiscoversEachDefinitionOnceAndNeverRecovers()
        {
            var catalog = EmptyCatalog();
            var receipt = new CoinWeaponDrawSaveRecord(Guid.NewGuid(), 5000, 500, new[]
            {
                new CoinWeaponDrawAward(WeaponInstanceId.New(), Spear, 1),
                new CoinWeaponDrawAward(WeaponInstanceId.New(), Sword, 1),
                new CoinWeaponDrawAward(WeaponInstanceId.New(), Spear, 1)
            });

            Assert.That(catalog.RecordDraw(receipt), Is.True);
            Assert.That(catalog.RecordDraw(receipt), Is.False);
            Assert.That(catalog.GetStage(Spear), Is.EqualTo(CatalogStage.Discovered));
            Assert.That(catalog.GetStage(Sword), Is.EqualTo(CatalogStage.Discovered));
            Assert.That(catalog.RecoveredCount, Is.Zero);
            Assert.That(catalog.ToSaveRecords().Count, Is.EqualTo(2));
        }

        [Test]
        public void SuccessRequiresAHeldNonLoanAndDeduplicatesTheSameDefinition()
        {
            var catalog = EmptyCatalog();
            RunResult success = FinishRun(success: true);
            WeaponInstance first = WeaponInstance.Create(Spear, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            WeaponInstance second = WeaponInstance.Create(Spear, WeaponProvenance.ExplorationLoot, WeaponLocation.Carried(0));
            WeaponInstance reserved = WeaponInstance.Create(Sword, WeaponProvenance.Personal, WeaponLocation.RunReserved(TestRun.Value));
            WeaponInstance loan = WeaponInstance.Create(Sword, WeaponProvenance.Loan, WeaponLocation.Equipped(1));

            Assert.That(catalog.RecordRecovery(success, reserved), Is.False);
            Assert.That(catalog.RecordRecovery(success, loan), Is.False);
            Assert.That(catalog.RecordRecovery(success, first), Is.True);
            Assert.That(catalog.RecordRecovery(success, second), Is.False);
            Assert.That(catalog.GetStage(Spear), Is.EqualTo(CatalogStage.Recovered));
            Assert.That(catalog.GetStage(Sword), Is.EqualTo(CatalogStage.Undiscovered));
            Assert.That(catalog.RecoveredCount, Is.EqualTo(1));
        }

        [Test]
        public void SaveRoundTripPreservesMasteryAfterItemLossAndKeepsOpaqueLegacyRecords()
        {
            var catalog = new CatalogProgress(new[] { new CatalogSaveRecord(Legacy, "회수 정보 보존") });
            WeaponInstance weapon = WeaponInstance.Create(Spear, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            Assert.That(catalog.RecordVerifiedMastery(Spear), Is.False);
            Assert.That(catalog.RecordRecovery(FinishRun(success: true), weapon), Is.True);
            Assert.That(catalog.RecordVerifiedMastery(Spear), Is.True);
            Assert.That(catalog.RecordVerifiedMastery(Spear), Is.False);

            string directory = Path.Combine(Path.GetTempPath(), $"ProjectWeapon-Catalog-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                var store = new AtomicSnapshotStore(Path.Combine(directory, "save.json"));
                var savedWithoutWeapon = new SaveSnapshot(1, 500, Array.Empty<WeaponInstance>(),
                    catalog.ToSaveRecords(), Array.Empty<SettlementSaveRecord>());
                Assert.That(store.Commit(0, savedWithoutWeapon).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
                Assert.That(store.Commit(0, savedWithoutWeapon).Status, Is.EqualTo(SnapshotCommitStatus.AlreadyCommitted));

                SaveSnapshot loaded = store.Load()!;
                Assert.That(loaded.Weapons, Is.Empty);
                var restored = new CatalogProgress(loaded.Catalog);
                Assert.That(restored.GetStage(Spear), Is.EqualTo(CatalogStage.Mastered));
                Assert.That(restored.GetStage(Legacy), Is.EqualTo(CatalogStage.Unrecognized));
                Assert.That(restored.RecoveredCount, Is.EqualTo(1));
                Assert.That(restored.RecordPickup(weapon), Is.False);
                Assert.That(restored.ToSaveRecords().Single(record => record.DefinitionId.Equals(Legacy)).State,
                    Is.EqualTo("회수 정보 보존"));
            }
            finally
            {
                foreach (string file in Directory.GetFiles(directory))
                    File.Delete(file);
                Directory.Delete(directory);
            }
        }

        private static CatalogProgress EmptyCatalog() => new CatalogProgress(Array.Empty<CatalogSaveRecord>());

        private static RunResult FinishRun(bool success)
        {
            TimeSpan now = TimeSpan.Zero;
            var run = new RunState(() => now);
            Assert.That(run.TryStartRun(TestRun, 1, TimeSpan.FromSeconds(10)), Is.True);

            if (!success)
                return run.ProcessSignals(new[] { RunSignal.PlayerDied(TestRun, now) })!;

            ExtractionAttempt attempt = run.TryBeginExtraction(TestRun, isInRange: true)!;
            now = attempt.CompletesAt;
            return run.ProcessSignals(new[] { RunSignal.CompleteExtraction(TestRun, attempt.Id, now) })!;
        }
    }
}
