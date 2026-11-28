#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Dungeon.Tests
{
    public sealed class FixedLootChestTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Spear = new WeaponDefinitionId("wooden_spear");
        private readonly List<GameObject> created = new List<GameObject>();
        private string? saveDirectory;
        private TimeSpan clock;

        [SetUp]
        public void ResetClock() => clock = TimeSpan.Zero;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject instance in created)
                Object.DestroyImmediate(instance);
            created.Clear();

            if (saveDirectory != null && Directory.Exists(saveDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(saveDirectory))
                    File.Delete(file);
                Directory.Delete(saveDirectory);
            }
            saveDirectory = null;
        }

        [TestCase(RunEndReason.Extracted)]
        [TestCase(RunEndReason.Death)]
        public void OneWorldDropOnlyReachesPermanentSaveAfterSuccessfulReturn(RunEndReason reason)
        {
            var session = Start();
            FixedLootChest first = Chest(session.Run);
            FixedLootChest second = Chest(session.Run);
            Sprite closed = first.GetComponent<SpriteRenderer>().sprite;
            Assert.That(first.TryOpen(out WorldLoot? drop), Is.True);
            Assert.That(second.TryOpen(out WorldLoot? differentDrop), Is.True);
            created.Add(drop!.gameObject);
            created.Add(differentDrop!.gameObject);
            Assert.That(drop.Item!.Id, Is.Not.EqualTo(differentDrop.Item!.Id));
            Assert.That(first.TryOpen(out WorldLoot? replay), Is.False);
            Assert.That(replay, Is.Null);
            Assert.That(first.IsOpen && drop.gameObject.activeInHierarchy && drop.GetComponent<CircleCollider2D>().isTrigger, Is.True);
            Assert.That(first.GetComponent<SpriteRenderer>().sprite, Is.Not.SameAs(closed));
            Assert.That(closed.pixelsPerUnit, Is.EqualTo(32));
            Assert.That(closed.texture.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(drop.Item.DefinitionId, Is.EqualTo(Spear));
            Assert.That(drop.Item.Provenance, Is.EqualTo(WeaponProvenance.ExplorationLoot));

            var resolve = new LootPickupCoordinator(session.Field);
            Vector2 position = drop.transform.position;
            Assert.That(resolve.TryObserve(drop.Item, position, position, .8f, out InventoryPickupResult pickup), Is.True);
            Assert.That(pickup.Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            Assert.That(resolve.TryObserve(drop.Item, position, position, .8f, out _), Is.False);
            Assert.That(session.Field.Snapshot().Single(item => item.Id == drop.Item.Id).Location.Kind,
                Is.EqualTo(WeaponLocationKind.Carried));

            saveDirectory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN192-" + Guid.NewGuid().ToString("N"));
            var store = new AtomicSnapshotStore(Path.Combine(saveDirectory, "checkpoint.json"));
            var revision = new SaveSnapshot(1, 0, session.Town.Snapshot(), Array.Empty<CatalogSaveRecord>(),
                Array.Empty<SettlementSaveRecord>());
            Assert.That(store.Commit(0, revision).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            Assert.That(store.Load()!.Weapons.Any(item => item.Id == drop.Item.Id), Is.False);

            RunResult outcome = Finish(session.Run, reason);
            RunSettlementCommit result = new RunSettlementCoordinator(store).TrySettle(outcome, session.Manifest,
                session.Field.Snapshot(), new RunCoinAccumulator(outcome.RunId));
            Assert.That(result.Status, Is.EqualTo(RunSettlementStatus.Committed));
            WeaponInstance? written = result.Snapshot!.Weapons.SingleOrDefault(item => item.Id == drop.Item.Id);
            if (reason == RunEndReason.Extracted)
                Assert.That(written!.Location.Kind, Is.EqualTo(WeaponLocationKind.Warehouse));
            else
                Assert.That(written, Is.Null);

            var details = RunSettlementDetails.Read(result.Receipt!)!;
            Assert.That(details.NewWeapons.Any(item => item.InstanceId == drop.Item.Id.ToString()),
                Is.EqualTo(reason == RunEndReason.Extracted));
            Assert.That(first.TryOpen(out _), Is.False);
        }

        [Test]
        public void FullBagUsesExistingChoiceAndUntouchedChestCannotOpenAfterDeath()
        {
            var session = Start();
            for (int i = 0; i < InventorySlotCapacity.Prototype.CarriedSlots; i++)
            {
                var occupied = WeaponInstance.Create(Sword, WeaponProvenance.ExplorationLoot, WeaponLocation.Warehouse());
                Assert.That(session.Field.TryPickup(occupied).Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            }

            FixedLootChest opened = Chest(session.Run);
            FixedLootChest terminal = Chest(session.Run);
            Assert.That(opened.TryOpen(out WorldLoot? drop), Is.True);
            created.Add(drop!.gameObject);
            var coordinator = new LootPickupCoordinator(session.Field);
            Vector2 at = drop.transform.position;
            Assert.That(coordinator.TryObserve(drop.Item, at, at, .8f, out InventoryPickupResult result), Is.True);
            Assert.That(result.Status, Is.EqualTo(InventoryPickupStatus.ChoiceRequired));
            Assert.That(session.Field.Snapshot().Any(item => item.Id == drop.Item!.Id), Is.False);

            WeaponInstance victim = session.Field.Snapshot().First(item => item.Location.Kind == WeaponLocationKind.Carried);
            Assert.That(coordinator.TryResolveChoice(result.Choice, victim.Id, out InventoryPickupResult replaced), Is.True);
            Assert.That(replaced.Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            Assert.That(replaced.Displaced!.Id, Is.EqualTo(victim.Id));
            Assert.That(session.Field.Snapshot().Any(item => item.Id == drop.Item!.Id), Is.True);

            Finish(session.Run, RunEndReason.Death);
            Assert.That(terminal.TryOpen(out _), Is.False);
            Assert.That(terminal.IsOpen, Is.False);
        }

        private FixedLootChest Chest(RunState run)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectWeapon/Chest/AbandonedCastleTestChest.prefab");
            Assert.That(prefab, Is.Not.Null);
            GameObject chest = Object.Instantiate(prefab);
            created.Add(chest);
            var behaviour = chest.GetComponent<FixedLootChest>();
            behaviour.BindRun(run);
            return behaviour;
        }

        private (RunState Run, SortieManifest Manifest, InventoryService Town, InventoryService Field) Start()
        {
            var item = WeaponInstance.Create(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            var town = new InventoryService(new[] { item });
            var run = new RunState(() => clock);
            RunId id = RunId.New();
            SortieStartResult start = town.TryStartSortie(id.Value,
                () => run.TryStartRun(id, 1, TimeSpan.FromSeconds(15)));
            Assert.That(start.Status, Is.EqualTo(SortieStartStatus.Started));
            return (run, start.Manifest!, town, new InventoryService(start.Manifest!.Equipped));
        }

        private RunResult Finish(RunState run, RunEndReason reason)
        {
            RunId id = run.CurrentRun!.Id;
            if (reason == RunEndReason.Extracted)
            {
                ExtractionAttempt attempt = run.TryBeginExtraction(id, isInRange: true)!;
                clock = attempt.CompletesAt;
                return run.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, clock) })!;
            }

            clock = TimeSpan.FromSeconds(1);
            return run.ProcessSignals(new[] { RunSignal.PlayerDied(id, clock) })!;
        }
    }
}
#endif
