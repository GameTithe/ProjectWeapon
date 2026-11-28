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
using UnityEditor;

namespace ProjectWeapon.Integration.Tests
{
    public sealed class ExtractionSettlementRaceTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Spear = new WeaponDefinitionId("wooden_spear");
        private static readonly WeaponDefinitionId Dagger = new WeaponDefinitionId("dagger");
        private string saveDirectory = null!;

        [SetUp]
        public void CreateSavePath()
            => saveDirectory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN36-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void RemoveSave()
        {
            if (!Directory.Exists(saveDirectory))
                return;
            foreach (string file in Directory.EnumerateFiles(saveDirectory))
                File.Delete(file);
            Directory.Delete(saveDirectory);
        }

        [TestCase(-1, 0, 0f, RunEndReason.Extracted)]
        [TestCase(0, 0, 0f, RunEndReason.TimeExpired)]
        [TestCase(-1, 100, 0f, RunEndReason.Death)]
        [TestCase(0, 100, 0f, RunEndReason.TimeExpired)]
        [TestCase(-2, 25, 0f, RunEndReason.TimeExpired)]
        [TestCase(-1, 0, .20f, RunEndReason.TimeExpired)]
        [TestCase(-1, 0, .19f, RunEndReason.Extracted)]
        public void OneLastFrameAndRepeatedSettlementStillPreserveTheAcceptedOutcome(int completionOffset,
            int damage, float movement, RunEndReason expected)
        {
            var zones = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(
                "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset");
            Assert.That(zones, Is.Not.Null);
            TimeSpan clock = TimeSpan.Zero;
            var run = new RunState(() => clock);
            var dungeon = new ZoneRunCountdown(zones, run);
            var observedResults = new List<RunResult>();
            run.RunResultFinalized += observedResults.Add;
            RunId id = RunId.New();
            WeaponInstance armed = Item(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            WeaponInstance safe = Item(Sword, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            WeaponInstance loot = Item(Spear, WeaponProvenance.ExplorationLoot, WeaponLocation.Warehouse());
            var town = new InventoryService(new[] { armed, safe });
            SortieStartResult sortie = new SortieStarter(town, dungeon).TryStart(id, 1);
            Assert.That(sortie.Status, Is.EqualTo(SortieStartStatus.Started));
            var field = new InventoryService(sortie.Manifest!.Equipped);
            Assert.That(field.TryPickup(loot).Status, Is.EqualTo(InventoryPickupStatus.PickedUp));

            var store = new AtomicSnapshotStore(Path.Combine(saveDirectory, "snapshot.json"));
            var previousCatalog = new CatalogSaveRecord(Dagger, "recovered");
            Assert.That(store.Commit(0, new SaveSnapshot(1, 150, town.Snapshot(), new[] { previousCatalog },
                Array.Empty<SettlementSaveRecord>())).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            var wallet = new RunCoinAccumulator(id);
            wallet.AddReward(20);

            TimeSpan deadline = run.CurrentRun!.Deadline;
            TimeSpan completion = deadline + TimeSpan.FromMilliseconds(completionOffset);
            clock = completion - RunState.ExtractionDuration;
            ExtractionAttempt? attempt = run.TryBeginExtraction(id, isInRange: true);
            Assert.That(attempt, Is.Not.Null);
            Assert.That(attempt!.CompletesAt, Is.EqualTo(completion));

            // Observe both scheduled completion and expiry across the same frame boundary.
            clock = deadline + TimeSpan.FromMilliseconds(1);
            var frame = new List<RunSignal>();
            var player = new PlayerHealth(100);
            if (damage > 0)
                Assert.That(PlayerRunDamage.ApplyHit(player, run, id, damage, completion, frame),
                    Is.EqualTo(completion < deadline));
            if (dungeon.Extraction.ObserveMovement(movement, completion) is RunSignal cancelled)
                frame.Add(cancelled);

            RunResult result = dungeon.ProcessFrame(frame)!;
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Reason, Is.EqualTo(expected));
            Assert.That(result.EffectiveAt, Is.EqualTo(expected == RunEndReason.TimeExpired ? deadline : completion));
            Assert.That(dungeon.ProcessFrame(frame), Is.SameAs(result));
            Assert.That(observedResults.Single(), Is.SameAs(result));

            var settlement = new RunSettlementCoordinator(store);
            IReadOnlyList<WeaponInstance> held = field.Snapshot();
            Assert.That(settlement.TrySettle(result, sortie.Manifest, held, wallet).Status, Is.EqualTo(RunSettlementStatus.Committed));
            for (int retry = 0; retry < 10; retry++)
            {
                var reopened = new AtomicSnapshotStore(Path.Combine(saveDirectory, "snapshot.json"));
                Assert.That(new RunSettlementCoordinator(reopened).TrySettle(result, sortie.Manifest, held, wallet).Status,
                    Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
            }

            SaveSnapshot saved = store.Load()!;
            var details = RunSettlementDetails.Read(saved.Settlements.Single())!;
            Assert.That(saved.Revision, Is.EqualTo(2));
            Assert.That(saved.Catalog.Single(item => item.DefinitionId.Equals(Dagger)).State, Is.EqualTo("recovered"));
            Assert.That(details.PreservedWarehouseCount, Is.EqualTo(1));
            Assert.That(details.PriorConfirmedCoins, Is.EqualTo(150));
            if (expected == RunEndReason.Extracted)
            {
                Assert.That(saved.ConfirmedCoins, Is.EqualTo(170));
                Assert.That(saved.Weapons.Select(item => item.Id), Is.EquivalentTo(new[] { armed.Id, safe.Id, loot.Id }));
                Assert.That(details.AwardedCoins, Is.EqualTo(20));
                Assert.That(details.NewWeapons.Single().DefinitionId, Is.EqualTo(Spear));
                Assert.That(dungeon.Extraction.Progress!.Value.Fraction, Is.EqualTo(1));
            }
            else
            {
                Assert.That(saved.ConfirmedCoins, Is.EqualTo(150));
                Assert.That(saved.Catalog.Single().DefinitionId, Is.EqualTo(Dagger));
                Assert.That(saved.Weapons.Single().Id, Is.EqualTo(safe.Id));
                Assert.That(details.ForfeitedCoins, Is.EqualTo(20));
                Assert.That(details.LostInstanceIds, Is.EquivalentTo(new[] { armed.Id.ToString(), loot.Id.ToString() }));
                Assert.That(dungeon.Extraction.Progress, Is.Null);
            }
        }

        private static WeaponInstance Item(WeaponDefinitionId definition, WeaponProvenance origin, WeaponLocation position)
            => WeaponInstance.Create(definition, origin, position);
    }
}
