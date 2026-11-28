#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Inventory;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;
using ProjectWeapon.WeaponSwitch;
using UnityEditor;

namespace ProjectWeapon.OwnershipFlow.Tests
{
    public sealed class SortieOwnershipFlowTests
    {
        private string directory = null!;
        private string path = null!;

        [SetUp]
        public void CreateProfile()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN35-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "snapshot.json");
        }

        [TearDown]
        public void RemoveProfile()
        {
            if (!Directory.Exists(directory))
                return;
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.TimeExpired)]
        public void RepeatedEquipmentMovementAndFailuresPreserveOnlyTheExactUnbroughtItems(RunEndReason reason)
        {
            var activeSword = Own("old_sword");
            var activeMace = Own("mace");
            var secondSword = Own("old_sword");
            var secondMace = Own("mace");
            var safeDuplicateSword = Own("old_sword");
            var safeSpear = Own("wooden_spear");
            var safeCatalog = new CatalogSaveRecord(safeSpear.DefinitionId, "recovered");
            var store = new AtomicSnapshotStore(path);
            var initial = new SaveSnapshot(1, 180,
                new[] { activeSword, activeMace, secondSword, secondMace, safeDuplicateSword, safeSpear },
                new[] { safeCatalog }, Array.Empty<SettlementSaveRecord>());
            Assert.That(store.Commit(0, initial).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            var zones = AssetDatabase.LoadAssetAtPath<ZoneCatalog>("Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(
                "Assets/ProjectWeapon/Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset");
            Assert.That(zones, Is.Not.Null);
            Assert.That(catalog, Is.Not.Null);

            ExecuteFailedSortie(store, zones, catalog, activeSword, activeMace, reason);
            SaveSnapshot afterFirst = new AtomicSnapshotStore(path).Load()!;
            CollectionAssert.AreEquivalent(new[] { secondSword.Id, secondMace.Id, safeDuplicateSword.Id, safeSpear.Id },
                afterFirst.Weapons.Select(item => item.Id));
            ExecuteFailedSortie(new AtomicSnapshotStore(path), zones, catalog, secondSword, secondMace, reason);

            SaveSnapshot final = new AtomicSnapshotStore(path).Load()!;
            CollectionAssert.AreEquivalent(new[] { safeDuplicateSword.Id, safeSpear.Id }, final.Weapons.Select(item => item.Id));
            foreach (WeaponInstance safe in final.Weapons)
                Assert.That(safe.Location.Kind, Is.EqualTo(WeaponLocationKind.Warehouse));
            Assert.That(final.Revision, Is.EqualTo(5));
            Assert.That(final.ConfirmedCoins, Is.EqualTo(initial.ConfirmedCoins));
            Assert.That(final.Catalog.Single().DefinitionId, Is.EqualTo(safeCatalog.DefinitionId));
            Assert.That(final.Catalog.Single().State, Is.EqualTo("recovered"));
            Assert.That(final.Settlements.Select(item => item.RunId).Distinct().Count(), Is.EqualTo(2));
        }

        private void ExecuteFailedSortie(AtomicSnapshotStore store, ZoneCatalog zones, WeaponCatalog catalog,
            WeaponInstance sword, WeaponInstance mace, RunEndReason reason)
        {
            SaveSnapshot before = store.Load()!;
            var town = new InventoryService(before.Weapons);
            for (int movement = 0; movement < 3; movement++)
            {
                MoveBothToEquipment(town, sword.Id, mace.Id);
                Assert.That(town.Move(sword.Id, WeaponLocation.Equipped(0), WeaponLocation.Warehouse()),
                    Is.EqualTo(InventoryMoveResult.Moved));
                Assert.That(town.Move(mace.Id, WeaponLocation.Equipped(1), WeaponLocation.Warehouse()),
                    Is.EqualTo(InventoryMoveResult.Moved));
                AssertExactIds(town.Snapshot(), before.Weapons.Select(item => item.Id));
                Assert.That(town.Snapshot().All(item => item.Location.Kind == WeaponLocationKind.Warehouse), Is.True);
            }
            MoveBothToEquipment(town, sword.Id, mace.Id);

            TimeSpan clock = TimeSpan.Zero;
            var run = new RunState(() => clock);
            var countdown = new ZoneRunCountdown(zones, run);
            RunId id = RunId.New();
            var starter = new SortieStarter(town, countdown);
            SortieStartResult start = starter.TryStart(id, 1);
            Assert.That(start.Status, Is.EqualTo(SortieStartStatus.Started));
            SortieManifest manifest = start.Manifest!;
            var field = new InventoryService(manifest.Equipped);
            var locks = new WeaponActionLocks();
            var selected = new EquippedWeaponSelection(run, field, catalog, () => false, locks);

            Assert.That(selected.ReadActiveWeapon()?.InstanceId, Is.EqualTo(sword.Id));
            Assert.That(selected.TrySwitch(1, clock), Is.True);
            Assert.That(selected.TryGetReadyWeapon(CombatWeaponAction.BasicAttack, clock, out SelectedWeapon ready),
                Is.True);
            Assert.That(ready.InstanceId, Is.EqualTo(mace.Id));
            Assert.That(selected.TrySwitch(0, clock), Is.True);
            AssertExactIds(field.Snapshot(), new[] { sword.Id, mace.Id });
            Assert.That(town.Move(sword.Id, WeaponLocation.RunReserved(id.Value), WeaponLocation.Warehouse()),
                Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(starter.TryStart(RunId.New(), 1).Status, Is.EqualTo(SortieStartStatus.InventoryAlreadyReserved));
            foreach (WeaponInstance item in field.Snapshot())
            {
                WeaponInstance reservation = town.Snapshot().Single(candidate => candidate.Id == item.Id);
                Assert.That(reservation.Location.Kind, Is.EqualTo(WeaponLocationKind.RunReserved));
                Assert.That(reservation.Location.ReservedRunId, Is.EqualTo(id.Value));
                Assert.That(item.Location.Kind, Is.EqualTo(WeaponLocationKind.Equipped));
            }
            var other = before.Weapons.Where(item => item.Id != sword.Id && item.Id != mace.Id).ToArray();
            AssertExactIds(town.Snapshot().Where(item => item.Location.Kind == WeaponLocationKind.Warehouse),
                other.Select(item => item.Id));
            Assert.That(town.Snapshot().Where(item => item.Location.Kind == WeaponLocationKind.Warehouse)
                .Select(item => item.Id).Intersect(field.Snapshot().Select(item => item.Id)), Is.Empty);

            var inFlight = new SaveSnapshot(before.Revision + 1, before.ConfirmedCoins, town.Snapshot(),
                before.Catalog, before.Settlements, before.CoinWeaponDraws);
            Assert.That(store.Commit(before.Revision, inFlight).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            var reopened = new AtomicSnapshotStore(path);
            SaveSnapshot persisted = reopened.Load()!;
            foreach (WeaponInstance item in field.Snapshot())
            {
                WeaponInstance reservation = persisted.Weapons.Single(candidate => candidate.Id == item.Id);
                Assert.That(reservation.Location.Kind, Is.EqualTo(WeaponLocationKind.RunReserved));
                Assert.That(reservation.Location.ReservedRunId, Is.EqualTo(id.Value));
            }
            AssertExactIds(persisted.Weapons.Where(item => item.Location.Kind == WeaponLocationKind.Warehouse),
                other.Select(item => item.Id));

            var hitSignals = new List<RunSignal>();
            if (reason == RunEndReason.Death)
            {
                clock = TimeSpan.FromSeconds(1);
                Assert.That(PlayerRunDamage.ApplyHit(new PlayerHealth(100), run, id, 100, clock, hitSignals), Is.True);
            }
            else
                clock = run.CurrentRun!.Deadline;
            RunResult terminal = countdown.ProcessFrame(hitSignals)!;
            Assert.That(terminal.Reason, Is.EqualTo(reason));
            var wallet = new RunCoinAccumulator(id);
            wallet.AddReward(35);
            RunSettlementCommit first = new RunSettlementCoordinator(store).TrySettle(terminal, manifest, field.Snapshot(), wallet);
            Assert.That(first.Status, Is.EqualTo(RunSettlementStatus.Committed));
            RunSettlementCommit again = new RunSettlementCoordinator(new AtomicSnapshotStore(path))
                .TrySettle(terminal, manifest, field.Snapshot(), wallet);
            Assert.That(again.Status, Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
            Assert.That(again.Receipt!.Result, Is.EqualTo(first.Receipt!.Result));
            SaveSnapshot settled = store.Load()!;
            AssertExactIds(settled.Weapons, other.Select(item => item.Id));
            Assert.That(settled.Weapons.All(item => item.Location.Kind == WeaponLocationKind.Warehouse), Is.True);
            foreach (WeaponInstance item in settled.Weapons)
            {
                WeaponInstance original = other.Single(candidate => candidate.Id == item.Id);
                Assert.That(item.DefinitionId, Is.EqualTo(original.DefinitionId));
                Assert.That(item.Provenance, Is.EqualTo(original.Provenance));
            }
            Assert.That(settled.ConfirmedCoins, Is.EqualTo(before.ConfirmedCoins));
            RunSettlementDetails details = RunSettlementDetails.Read(again.Receipt)!;
            CollectionAssert.AreEquivalent(new[] { sword.Id.ToString(), mace.Id.ToString() }, details.LostInstanceIds);
            Assert.That(details.ForfeitedCoins, Is.EqualTo(35));
        }

        private static void MoveBothToEquipment(InventoryService inventory, WeaponInstanceId sword, WeaponInstanceId mace)
        {
            Assert.That(inventory.Move(sword, WeaponLocation.Warehouse(), WeaponLocation.Equipped(0)),
                Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(inventory.Move(mace, WeaponLocation.Warehouse(), WeaponLocation.Equipped(1)),
                Is.EqualTo(InventoryMoveResult.Moved));
        }

        private static void AssertExactIds(IEnumerable<WeaponInstance> items, IEnumerable<WeaponInstanceId> expected)
            => CollectionAssert.AreEquivalent(expected, items.Select(item => item.Id));

        private static WeaponInstance Own(string id)
            => WeaponInstance.Create(new WeaponDefinitionId(id), WeaponProvenance.Personal, WeaponLocation.Warehouse());
    }
}
