#nullable enable

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Catalog;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Settlement.Tests
{
    public sealed class OldSwordMasterySettlementTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Spare = new WeaponDefinitionId("wooden_spear");
        private string path = null!;
        private AtomicSnapshotStore store = null!;

        [SetUp]
        public void SetUp()
        {
            path = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN113-" + Guid.NewGuid().ToString("N"),
                "snapshot.json");
            store = new AtomicSnapshotStore(path);
        }

        [TearDown]
        public void TearDown()
        {
            string directory = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(directory))
                return;
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [Test]
        public void ThreeFinalBlowsAndTheSameHeldInstanceMasterTheRecoveredDefinitionInTheSettlement()
        {
            WeaponInstance sword = SwordInSlot(0);
            var warehouse = WeaponInstance.Create(Spare, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            Attempt sortie = Begin("recovered", sword, warehouse);
            AssertThreeBlows(sortie, sword);
            Assert.That(ReadStage(store.Load()!), Is.EqualTo(CatalogStage.Recovered));
            Assert.That(store.Load()!.Settlements, Is.Empty);
            RunResult result = sortie.Finish(RunEndReason.Extracted);
            Assert.That(sortie.Tracker.ObserveFinalBlow(sortie.Id, sword.Id, "after-result", Ms(4)), Is.False);

            RunSettlementCommit committed = Settle(sortie, result, sword);
            Assert.That(committed.Status, Is.EqualTo(RunSettlementStatus.Committed));
            SaveSnapshot durable = new AtomicSnapshotStore(path).Load()!;
            Assert.That(ReadStage(durable), Is.EqualTo(CatalogStage.Mastered));
            Assert.That(durable.Revision, Is.EqualTo(2));
            Assert.That(durable.ConfirmedCoins, Is.EqualTo(70));
            CollectionAssert.AreEquivalent(new[] { sword.Id, warehouse.Id }, durable.Weapons.Select(item => item.Id));
            Assert.That(durable.Weapons.All(item => item.Location.Kind == WeaponLocationKind.Warehouse), Is.True);
            RunSettlementDetails details = RunSettlementDetails.Read(durable.Settlements.Single())!;
            Assert.That(details.HasItemizedDetails, Is.True);
            Assert.That(details.NewCatalogRecoveries, Is.Empty);
            Assert.That(details.ReturnedWeapons.Single().InstanceId, Is.EqualTo(sword.Id.ToString()));
        }

        [Test]
        public void DistinctInstancesOfTheSameDefinitionAndDuplicateEnemyIdsCannotBeCombined()
        {
            WeaponInstance first = SwordInSlot(0);
            WeaponInstance second = SwordInSlot(1);
            Attempt sortie = Begin("recovered", first, second);
            Assert.That(sortie.Record(first, "first"), Is.True);
            Assert.That(sortie.Record(first, "second"), Is.True);
            Assert.That(sortie.Record(second, "third"), Is.True);
            Assert.That(sortie.Record(first, "third"), Is.False);
            Assert.That(sortie.Record(second, "first"), Is.False);
            Assert.That(sortie.Tracker.ObserveFinalBlow(RunId.New(), first.Id, "wrong-run", Ms(10)), Is.False);

            RunSettlementCommit value = Settle(sortie, sortie.Finish(RunEndReason.Extracted), first, second);
            Assert.That(value.Status, Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(ReadStage(store.Load()!), Is.EqualTo(CatalogStage.Recovered));
        }

        [Test]
        public void AbandoningTheQualifyingSwordCannotBeReplacedWithAnotherHeldSword()
        {
            WeaponInstance first = SwordInSlot(0);
            WeaponInstance second = SwordInSlot(1);
            Attempt sortie = Begin("recovered", first, second);
            AssertThreeBlows(sortie, first);

            RunSettlementCommit value = Settle(sortie, sortie.Finish(RunEndReason.Extracted), second);
            Assert.That(value.Status, Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(ReadStage(store.Load()!), Is.EqualTo(CatalogStage.Recovered));
            Assert.That(store.Load()!.Weapons.Single().Id, Is.EqualTo(second.Id));
        }

        [Test]
        public void FirstRecoveryAndStarterLoanCannotMasterAnOldSword()
        {
            WeaponInstance owned = SwordInSlot(0);
            Attempt unrecovered = Begin("discovered", owned);
            Assert.That(unrecovered.Record(owned, "one"), Is.False);
            Assert.That(unrecovered.Record(owned, "two"), Is.False);
            Assert.That(unrecovered.Record(owned, "three"), Is.False);
            Assert.That(Settle(unrecovered, unrecovered.Finish(RunEndReason.Extracted), owned).Status,
                Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(ReadStage(store.Load()!), Is.EqualTo(CatalogStage.Recovered));

            WeaponInstance onlyInWarehouse = store.Load()!.Weapons.Single();
            Attempt loanSortie = Begin("recovered", new[] { onlyInWarehouse }, prepareLoan: true);
            WeaponInstance loan = loanSortie.Manifest.Equipped.Single();
            Assert.That(loan.IsLoaned, Is.True);
            Assert.That(loanSortie.Record(loan, "one"), Is.False);
            Assert.That(loanSortie.Record(loan, "two"), Is.False);
            Assert.That(loanSortie.Record(loan, "three"), Is.False);
            Assert.That(Settle(loanSortie, loanSortie.Finish(RunEndReason.Extracted), loan).Status,
                Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(ReadStage(store.Load()!), Is.EqualTo(CatalogStage.Recovered));
            Assert.That(store.Load()!.Weapons.Single().Id, Is.EqualTo(owned.Id));
        }

        [Test]
        public void PartialProgressDoesNotCarryOverToAnotherRunWithTheSameWeapon()
        {
            WeaponInstance sword = SwordInSlot(0);
            Attempt first = Begin("recovered", sword);
            Assert.That(first.Record(sword, "enemy-one"), Is.True);
            Assert.That(first.Record(sword, "enemy-two"), Is.True);
            Assert.That(Settle(first, first.Finish(RunEndReason.Extracted), sword).Status,
                Is.EqualTo(RunSettlementStatus.Committed));

            var town = new InventoryService(store.Load()!.Weapons);
            Assert.That(town.Move(sword.Id, WeaponLocation.Warehouse(), WeaponLocation.Equipped(0)),
                Is.EqualTo(InventoryMoveResult.Moved));
            WeaponInstance reequipped = town.Snapshot().Single();
            Attempt next = Begin("recovered", reequipped);
            Assert.That(next.Id, Is.Not.EqualTo(first.Id));
            Assert.That(next.Record(reequipped, "enemy-one"), Is.True);
            RunResult completed = next.Finish(RunEndReason.Extracted);

            // An old evidence source is also ineligible for a different finalized run.
            Assert.That(first.Tracker.GetVerifiedMasteries(completed, new[] { reequipped }), Is.Empty);
            Assert.That(Settle(next, completed, reequipped).Status, Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(ReadStage(store.Load()!), Is.EqualTo(CatalogStage.Recovered));
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.TimeExpired)]
        public void AFailedRunWithThreeFinalBlowsLeavesRecoveryAndRecordsNoMastery(RunEndReason reason)
        {
            WeaponInstance sword = SwordInSlot(0);
            Attempt sortie = Begin("recovered", sword);
            AssertThreeBlows(sortie, sword);

            RunSettlementCommit committed = Settle(sortie, sortie.Finish(reason), sword);
            Assert.That(committed.Status, Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(ReadStage(store.Load()!), Is.EqualTo(CatalogStage.Recovered));
            Assert.That(store.Load()!.Weapons, Is.Empty);
            Assert.That(RunSettlementDetails.Read(committed.Receipt!)!.Reason, Is.EqualTo(reason));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AcceptedFinalBlowAtTheCutoffCountsButAProjectedLaterOneDoesNot(bool afterCutoff)
        {
            WeaponInstance sword = SwordInSlot(0);
            Attempt sortie = Begin("recovered", sword);
            Assert.That(sortie.Record(sword, "a"), Is.True);
            Assert.That(sortie.Record(sword, "b"), Is.True);
            ExtractionAttempt channel = sortie.BeginExtraction();
            TimeSpan impact = channel.CompletesAt + (afterCutoff ? TimeSpan.FromTicks(1) : TimeSpan.Zero);
            Assert.That(sortie.Tracker.ObserveFinalBlow(sortie.Id, sword.Id, "c", impact), Is.True);

            Assert.That(Settle(sortie, sortie.Finish(RunEndReason.Extracted), sword).Status,
                Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(ReadStage(store.Load()!), Is.EqualTo(afterCutoff ? CatalogStage.Recovered : CatalogStage.Mastered));
        }

        [Test]
        public void WriteFailureAndLostResponseKeepMasteryAndReceiptInOneAtomicRevision()
        {
            WeaponInstance sword = SwordInSlot(0);
            Attempt sortie = Begin("recovered", sword);
            AssertThreeBlows(sortie, sword);
            RunResult outcome = sortie.Finish(RunEndReason.Extracted);
            var noWrite = new RunSettlementCoordinator(store, (_, _) => throw new IOException("write failed"));
            Assert.Throws<IOException>(() => noWrite.TrySettle(outcome, sortie.Manifest, new[] { sword }, sortie.Wallet,
                sortie.Tracker));
            SaveSnapshot before = store.Load()!;
            Assert.That(ReadStage(before), Is.EqualTo(CatalogStage.Recovered));
            Assert.That(before.Revision, Is.EqualTo(1));
            Assert.That(before.Settlements, Is.Empty);

            var ambiguous = new RunSettlementCoordinator(store, (revision, next) =>
            {
                Assert.That(store.Commit(revision, next).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
                throw new IOException("response lost");
            });
            Assert.Throws<IOException>(() => ambiguous.TrySettle(outcome, sortie.Manifest, new[] { sword }, sortie.Wallet,
                sortie.Tracker));

            byte[] written = File.ReadAllBytes(path);
            for (int retry = 0; retry < 10; retry++)
            {
                var reopened = new RunSettlementCoordinator(new AtomicSnapshotStore(path));
                RunSettlementCommit replay = reopened.TrySettle(outcome, sortie.Manifest, new[] { sword }, sortie.Wallet,
                    sortie.Tracker);
                Assert.That(replay.Status, Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
                Assert.That(replay.Receipt!.RunId, Is.EqualTo(sortie.Id.Value));
                Assert.That(ReadStage(replay.Snapshot!), Is.EqualTo(CatalogStage.Mastered));
                Assert.That(replay.Snapshot!.Revision, Is.EqualTo(2));
            }
            CollectionAssert.AreEqual(written, File.ReadAllBytes(path));
        }

        [Test]
        public void ExistingSettlementOverloadKeepsItsReceiptFinalOnLaterMasteryReplay()
        {
            WeaponInstance sword = SwordInSlot(0);
            Attempt sortie = Begin("recovered", sword);
            AssertThreeBlows(sortie, sword);
            RunResult outcome = sortie.Finish(RunEndReason.Extracted);
            var legacy = new RunSettlementCoordinator(store);
            RunSettlementCommit first = legacy.TrySettle(outcome, sortie.Manifest, new[] { sword }, sortie.Wallet);
            Assert.That(first.Status, Is.EqualTo(RunSettlementStatus.Committed));

            RunSettlementCommit replay = Settle(sortie, outcome, sword);
            Assert.That(replay.Status, Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
            Assert.That(replay.Receipt!.Result, Is.EqualTo(first.Receipt!.Result));
            Assert.That(store.Load()!.Revision, Is.EqualTo(2));
            Assert.That(ReadStage(store.Load()!), Is.EqualTo(CatalogStage.Recovered));
        }

        private Attempt Begin(string catalogState, params WeaponInstance[] weapons)
            => Begin(catalogState, weapons, prepareLoan: false);

        private Attempt Begin(string catalogState, WeaponInstance[] weapons, bool prepareLoan)
        {
            var attempt = new Attempt();
            var town = new InventoryService(weapons);
            if (prepareLoan)
                Assert.That(town.PrepareStarterLoan(attempt.Id.Value).Status, Is.EqualTo(StarterLoanStatus.Issued));
            var started = town.TryStartSortie(attempt.Id.Value,
                () => attempt.Run.TryStartRun(attempt.Id, 1, TimeSpan.FromSeconds(20)));
            Assert.That(started.Status, Is.EqualTo(SortieStartStatus.Started));

            SaveSnapshot? previous = store.Load();
            var reserved = new SaveSnapshot((previous?.Revision ?? 0) + 1, previous?.ConfirmedCoins ?? 70,
                town.Snapshot(), previous?.Catalog ?? new[] { new CatalogSaveRecord(Sword, catalogState) },
                previous?.Settlements ?? Array.Empty<SettlementSaveRecord>());
            Assert.That(store.Commit(previous?.Revision ?? 0, reserved).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            attempt.Manifest = started.Manifest!;
            attempt.Tracker = new OldSwordMasteryTracker(attempt.Run, attempt.Manifest, reserved);
            return attempt;
        }

        private RunSettlementCommit Settle(Attempt attempt, RunResult outcome, params WeaponInstance[] held)
            => new RunSettlementCoordinator(store).TrySettle(outcome, attempt.Manifest, held, attempt.Wallet, attempt.Tracker);

        private static CatalogStage ReadStage(SaveSnapshot snapshot) => new CatalogProgress(snapshot.Catalog).GetStage(Sword);

        private static WeaponInstance SwordInSlot(int slot)
            => WeaponInstance.Create(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(slot));

        private static void AssertThreeBlows(Attempt sortie, WeaponInstance sword)
        {
            Assert.That(sortie.Record(sword, "one"), Is.True);
            Assert.That(sortie.Record(sword, "two"), Is.True);
            Assert.That(sortie.Record(sword, "three"), Is.True);
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

        private sealed class Attempt
        {
            private TimeSpan clock;

            public Attempt()
            {
                Run = new RunState(() => clock);
                Wallet = new RunCoinAccumulator(Id);
            }

            public RunId Id { get; } = RunId.New();
            public RunState Run { get; }
            public RunCoinAccumulator Wallet { get; }
            public SortieManifest Manifest { get; set; } = null!;
            public OldSwordMasteryTracker Tracker { get; set; } = null!;

            public bool Record(WeaponInstance weapon, string enemy)
            {
                clock += Ms(10);
                return Tracker.ObserveFinalBlow(Id, weapon.Id, enemy, clock);
            }

            public ExtractionAttempt BeginExtraction()
            {
                clock += Ms(20);
                return Run.TryBeginExtraction(Id, isInRange: true)!;
            }

            public RunResult Finish(RunEndReason reason)
            {
                switch (reason)
                {
                    case RunEndReason.Extracted:
                        ExtractionAttempt active = Run.ActiveExtraction ?? BeginExtraction();
                        clock = active.CompletesAt;
                        return Run.ProcessSignals(new[] { RunSignal.CompleteExtraction(Id, active.Id, clock) })!;
                    case RunEndReason.Death:
                        clock += Ms(100);
                        return Run.ProcessSignals(new[] { RunSignal.PlayerDied(Id, clock) })!;
                    default:
                        clock = Run.CurrentRun!.Deadline;
                        return Run.ProcessSignals(Array.Empty<RunSignal>())!;
                }
            }
        }
    }
}
