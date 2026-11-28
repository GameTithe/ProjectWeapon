#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Catalog;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Wallet;
using ProjectWeapon.WeaponDraw;
using ProjectWeapon.Weapons;
using UnityEditor;

namespace ProjectWeapon.CoinWeaponDraw.Tests
{
    public sealed class CatalogRecoveryFlowTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Spear = new WeaponDefinitionId("wooden_spear");
        private static readonly WeaponDefinitionId Replacement = new WeaponDefinitionId("hunting_bow");
        private string directory = null!;
        private string path = null!;
        private AtomicSnapshotStore store = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN103-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "snapshot.json");
            store = new AtomicSnapshotStore(path);
            Assert.That(store.Commit(0, new SaveSnapshot(1, 500, Array.Empty<WeaponInstance>(),
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>())).Status,
                Is.EqualTo(SnapshotCommitStatus.Committed));
        }

        [TearDown]
        public void TearDown()
        {
            if (!Directory.Exists(directory))
                return;
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [Test]
        public void DrawStaysUnrecoveredUntilTheSameInstanceReturnsAndReceiptReplayKeepsTheCount()
        {
            Guid purchaseId = Guid.NewGuid();
            var purchase = new CoinWeaponDrawService(store, DrawTable()).Purchase(purchaseId, _ => 0);
            Assert.That(purchase.Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
            WeaponInstanceId awardedId = purchase.Receipt!.InstanceId;
            Assert.That(purchase.Receipt.DefinitionId, Is.EqualTo(Sword));
            SaveSnapshot afterDraw = store.Load()!;
            Assert.That(afterDraw.Weapons.Single().Id, Is.EqualTo(awardedId));
            Assert.That(Stage(afterDraw, Sword), Is.LessThan(CatalogStage.Recovered));

            Sortie sortie = StartSortie(awardedId);
            Assert.That(sortie.Manifest.Equipped.Single().Id, Is.EqualTo(awardedId));
            Assert.That(Stage(store.Load()!, Sword), Is.LessThan(CatalogStage.Recovered));
            var coordinator = new RunSettlementCoordinator(store);
            RunResult result = sortie.Finish(RunEndReason.Extracted);
            var wallet = new RunCoinAccumulator(result.RunId);
            Assert.That(result.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(sortie.Field.Snapshot().Single().DefinitionId, Is.EqualTo(Sword));
            Assert.That(sortie.Field.Snapshot().Single().Provenance, Is.EqualTo(WeaponProvenance.Personal));

            RunSettlementCommit first = coordinator.TrySettle(result, sortie.Manifest, sortie.Field.Snapshot(), wallet);
            Assert.That(first.Status, Is.EqualTo(RunSettlementStatus.Committed));
            SaveSnapshot recovered = store.Load()!;
            Assert.That(Stage(recovered, Sword), Is.EqualTo(CatalogStage.Recovered));
            Assert.That(recovered.Weapons.Single().Id, Is.EqualTo(awardedId));
            Assert.That(recovered.CoinWeaponDraws.Single().RequestId, Is.EqualTo(purchaseId));
            Assert.That(RunSettlementDetails.Read(recovered.Settlements.Single())!.NewCatalogRecoveries.Single(),
                Is.EqualTo(Sword.Value));

            for (int retry = 0; retry < 10; retry++)
            {
                var reopened = new RunSettlementCoordinator(new AtomicSnapshotStore(path));
                Assert.That(reopened.TrySettle(result, sortie.Manifest, sortie.Field.Snapshot(), wallet).Status,
                    Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
            }
            SaveSnapshot replayed = store.Load()!;
            Assert.That(replayed.Revision, Is.EqualTo(recovered.Revision));
            Assert.That(replayed.Catalog.Single().DefinitionId, Is.EqualTo(Sword));
        }

        [Test]
        public void SuccessfulReturnRecoversOnlyTheLootStillHeldAfterFullBagReplacement()
        {
            Sortie sortie = StartSortie();
            var displaced = Loot(Spear);
            var incoming = Loot(Replacement);
            Assert.That(sortie.Field.TryPickup(displaced).Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            var offer = sortie.Field.TryPickup(incoming);
            Assert.That(offer.Status, Is.EqualTo(InventoryPickupStatus.ChoiceRequired));
            Assert.That(sortie.Field.ResolvePickup(offer.Choice, displaced.Id).Status,
                Is.EqualTo(InventoryPickupStatus.PickedUp));

            SaveSnapshot saved = Settle(sortie, RunEndReason.Extracted);

            Assert.That(saved.Weapons.Single().Id, Is.EqualTo(incoming.Id));
            Assert.That(Stage(saved, Spear), Is.Not.EqualTo(CatalogStage.Recovered));
            Assert.That(Stage(saved, Replacement), Is.EqualTo(CatalogStage.Recovered));
            Assert.That(Stage(saved, Sword), Is.LessThan(CatalogStage.Recovered));
            Assert.That(RunSettlementDetails.Read(saved.Settlements.Single())!.NewCatalogRecoveries.Single(),
                Is.EqualTo(Replacement.Value));
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.TimeExpired)]
        public void FailedRunDoesNotRecoverHeldLoot(RunEndReason reason)
        {
            Sortie sortie = StartSortie();
            Assert.That(sortie.Field.TryPickup(Loot(Spear)).Status, Is.EqualTo(InventoryPickupStatus.PickedUp));

            SaveSnapshot saved = Settle(sortie, reason);

            Assert.That(saved.Weapons, Is.Empty);
            Assert.That(Stage(saved, Spear), Is.Not.EqualTo(CatalogStage.Recovered));
            Assert.That(RunSettlementDetails.Read(saved.Settlements.Single())!.NewCatalogRecoveries, Is.Empty);
        }

        [Test]
        public void AWeaponLostOnALaterRunDoesNotRemoveThePreviousRecovery()
        {
            Sortie first = StartSortie();
            var loot = Loot(Spear);
            Assert.That(first.Field.TryPickup(loot).Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            Assert.That(Stage(Settle(first, RunEndReason.Extracted), Spear), Is.EqualTo(CatalogStage.Recovered));

            Sortie second = StartSortie(loot.Id);
            Assert.That(second.Manifest.Equipped.Single().Id, Is.EqualTo(loot.Id));
            SaveSnapshot afterLoss = Settle(second, RunEndReason.Death);

            Assert.That(afterLoss.Weapons, Is.Empty);
            Assert.That(afterLoss.Settlements, Has.Count.EqualTo(2));
            Assert.That(afterLoss.Catalog.Single().DefinitionId, Is.EqualTo(Spear));
            Assert.That(Stage(afterLoss, Spear), Is.EqualTo(CatalogStage.Recovered));
        }

        private Sortie StartSortie(WeaponInstanceId? equip = null)
        {
            SaveSnapshot previous = store.Load()!;
            var town = new InventoryService(previous.Weapons);
            if (equip.HasValue)
                Assert.That(town.Move(equip.Value, WeaponLocation.Warehouse(), WeaponLocation.Equipped(0)),
                    Is.EqualTo(InventoryMoveResult.Moved));

            ZoneCatalog zones = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(
                "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset");
            Assert.That(zones, Is.Not.Null);
            var sortie = new Sortie(zones, town);
            Assert.That(store.Commit(previous.Revision, new SaveSnapshot(previous.Revision + 1,
                previous.ConfirmedCoins, town.Snapshot(), previous.Catalog, previous.Settlements,
                previous.CoinWeaponDraws)).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            return sortie;
        }

        private SaveSnapshot Settle(Sortie sortie, RunEndReason reason)
        {
            RunResult result = sortie.Finish(reason);
            Assert.That(result.Reason, Is.EqualTo(reason));
            RunSettlementCommit settlement = new RunSettlementCoordinator(store).TrySettle(result,
                sortie.Manifest, sortie.Field.Snapshot(), new RunCoinAccumulator(result.RunId));
            Assert.That(settlement.Status, Is.EqualTo(RunSettlementStatus.Committed));
            return store.Load()!;
        }

        private static CatalogStage Stage(SaveSnapshot snapshot, WeaponDefinitionId definition)
            => new CatalogProgress(snapshot.Catalog).GetStage(definition);

        private static WeaponInstance Loot(WeaponDefinitionId definition)
            => WeaponInstance.Create(definition, WeaponProvenance.ExplorationLoot, WeaponLocation.Warehouse());

        private static WeightedWeaponDraw DrawTable()
        {
            int[] sizes = { 5, 5, 5, 3, 3 };
            var pools = new List<IReadOnlyList<WeaponDefinitionId>>();
            var grades = new Dictionary<WeaponDefinitionId, int>();
            for (int grade = 1; grade <= sizes.Length; grade++)
            {
                var pool = new List<WeaponDefinitionId>();
                for (int index = 0; index < sizes[grade - 1]; index++)
                {
                    var id = grade == 1 && index == 0 ? Sword : new WeaponDefinitionId($"qa_fixture_{grade}_{index}");
                    pool.Add(id);
                    grades.Add(id, grade);
                }
                pools.Add(pool);
            }
            return new WeightedWeaponDraw(WeightedWeaponDraw.DraftGradeWeightsBasisPoints, pools, grades);
        }

        private sealed class Sortie
        {
            private readonly RunState state;
            private readonly ZoneRunCountdown countdown;
            private readonly RunId id = RunId.New();
            private TimeSpan clock;

            internal Sortie(ZoneCatalog zones, InventoryService town)
            {
                state = new RunState(() => clock);
                countdown = new ZoneRunCountdown(zones, state);
                if (!town.Snapshot().Any(item => item.Location.Kind == WeaponLocationKind.Equipped))
                    Assert.That(town.PrepareStarterLoan(id.Value).Status, Is.EqualTo(StarterLoanStatus.Issued));
                SortieStartResult started = new SortieStarter(town, countdown).TryStart(id, 1);
                Assert.That(started.Status, Is.EqualTo(SortieStartStatus.Started));
                Manifest = started.Manifest!;
                Field = new InventoryService(Manifest.Equipped, new InventorySlotCapacity(2, 1));
            }

            internal SortieManifest Manifest { get; }
            internal InventoryService Field { get; }

            internal RunResult Finish(RunEndReason reason)
            {
                if (reason == RunEndReason.Death)
                    return countdown.ProcessFrame(new[] { RunSignal.PlayerDied(id, clock) })!;
                if (reason == RunEndReason.Extracted)
                {
                    Assert.That(state.TryBeginExtraction(id, isInRange: true), Is.Not.Null);
                    clock += RunState.ExtractionDuration;
                }
                else
                    clock = state.CurrentRun!.Deadline;
                return countdown.ProcessFrame(Array.Empty<RunSignal>())!;
            }
        }
    }
}
