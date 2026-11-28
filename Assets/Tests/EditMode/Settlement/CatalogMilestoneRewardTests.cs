#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ProjectWeapon.CoinWeaponDraw;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Wallet;
using ProjectWeapon.WeaponDraw;
using ProjectWeapon.WeaponSale;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;

namespace ProjectWeapon.Settlement.Tests
{
    public sealed class CatalogMilestoneRewardTests
    {
        private string path = null!;

        [SetUp]
        public void ProfilePath()
        {
            path = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN111-" + Guid.NewGuid().ToString("N"),
                "snapshot.json");
        }

        [TearDown]
        public void RemoveProfile()
        {
            string directory = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(directory))
                return;
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [TestCase(5, 500)]
        [TestCase(10, 1000)]
        [TestCase(15, 2000)]
        public void EachBoundaryPaysOnceForDistinctCommittedRecoveries(int threshold, int amount)
        {
            var store = new AtomicSnapshotStore(path);
            Seed(store, threshold - 1, threshold);
            TestSortie beforeBoundary = Start(store, Loot("recovered_0"), Loot("recovered_0"));
            byte[] ready = File.ReadAllBytes(path);
            Assert.That(beforeBoundary.Settle(store).Status, Is.EqualTo(RunSettlementStatus.Committed));
            SaveSnapshot stillBelow = store.Load()!;
            Assert.That(stillBelow.CatalogMilestones.Count, Is.EqualTo(threshold / 5 - 1));
            Assert.That(stillBelow.ConfirmedCoins, Is.EqualTo(203));
            CollectionAssert.AreNotEqual(ready, File.ReadAllBytes(path));

            TestSortie reaching = Start(store, Loot("new_recovery"), Loot("new_recovery"));
            RunSettlementCommit committed = reaching.Settle(store);
            Assert.That(committed.Status, Is.EqualTo(RunSettlementStatus.Committed));
            SaveSnapshot reached = new AtomicSnapshotStore(path).Load()!;
            Assert.That(reached.ConfirmedCoins, Is.EqualTo(206 + amount));
            Assert.That(reached.Catalog.Count(item => item.DefinitionId.Value == "new_recovery"), Is.EqualTo(1));
            CatalogMilestoneSaveRecord reward = reached.CatalogMilestones.Last();
            Assert.That(reward.RecoveredThreshold, Is.EqualTo(threshold));
            Assert.That(reward.AwardedCoins, Is.EqualTo(amount));
            Assert.That(reward.SourceRunId, Is.EqualTo(reaching.Result.RunId.Value));
            Assert.That(reward.TransactionId, Is.EqualTo("CatalogMilestone:" + threshold));
            Assert.That(RunSettlementDetails.Read(committed.Receipt!)!.AwardedCoins, Is.EqualTo(3),
                "The exploration wallet shows only coins earned in this run.");

            byte[] finalBytes = File.ReadAllBytes(path);
            for (int retry = 0; retry < 10; retry++)
                Assert.That(reaching.Settle(new AtomicSnapshotStore(path)).Status,
                    Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
            CollectionAssert.AreEqual(finalBytes, File.ReadAllBytes(path));
        }

        [Test]
        public void LegacyProfileQualifiesForAllUnpaidRewardsInTheSameSuccessfulCommit()
        {
            var store = new AtomicSnapshotStore(path);
            WriteLegacyProfileWithoutMilestones(15);
            Assert.That(store.Load()!.CatalogMilestones, Is.Empty);
            TestSortie reaching = Start(store, Loot("recovered_0"));
            SnapshotCommitResult WriteThenLoseReply(long revision, SaveSnapshot next)
            {
                store.Commit(revision, next);
                throw new IOException("The response was interrupted.");
            }
            var interrupted = new RunSettlementCoordinator(store, WriteThenLoseReply);
            Assert.That(() => reaching.Settle(interrupted), Throws.TypeOf<IOException>());

            var reopened = new AtomicSnapshotStore(path);
            RunSettlementCommit replay = reaching.Settle(reopened);
            Assert.That(replay.Status, Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
            SaveSnapshot saved = reopened.Load()!;
            CollectionAssert.AreEqual(new[] { 5, 10, 15 }, saved.CatalogMilestones.Select(reward => reward.RecoveredThreshold));
            CollectionAssert.AreEqual(new long[] { 500, 1000, 2000 }, saved.CatalogMilestones.Select(reward => reward.AwardedCoins));
            Assert.That(saved.ConfirmedCoins, Is.EqualTo(3703));

            TestSortie next = Start(reopened, Loot("recovered_0"));
            Assert.That(next.Settle(reopened).Status, Is.EqualTo(RunSettlementStatus.Committed));
            SaveSnapshot afterAnotherSuccess = reopened.Load()!;
            Assert.That(afterAnotherSuccess.CatalogMilestones, Has.Count.EqualTo(3));
            Assert.That(afterAnotherSuccess.ConfirmedCoins, Is.EqualTo(3706));
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.TimeExpired)]
        public void AFailedRunNeitherGrantsLegacyProgressNorCountsHeldLoot(RunEndReason reason)
        {
            var store = new AtomicSnapshotStore(path);
            Seed(store, 14, firstUnpaid: 5);
            TestSortie failed = Start(store, reason, Loot("failed_drop"));
            Assert.That(failed.Settle(store).Status, Is.EqualTo(RunSettlementStatus.Committed));
            SaveSnapshot saved = store.Load()!;
            Assert.That(saved.CatalogMilestones, Is.Empty);
            Assert.That(saved.ConfirmedCoins, Is.EqualTo(200));
            Assert.That(saved.Catalog.Any(record => record.DefinitionId.Value == "failed_drop"), Is.False);
            Assert.That(saved.Weapons, Is.Empty);
        }

        [Test]
        public void AWriteFailureNeverLeavesCoinsOrAMilestoneWithoutTheSettlement()
        {
            var store = new AtomicSnapshotStore(path);
            Seed(store, 4, firstUnpaid: 5);
            TestSortie reaching = Start(store, Loot("fifth"));
            byte[] reservation = File.ReadAllBytes(path);
            var interrupted = new RunSettlementCoordinator(store, (_, _) => throw new IOException("No write."));
            Assert.That(() => reaching.Settle(interrupted), Throws.TypeOf<IOException>());
            CollectionAssert.AreEqual(reservation, File.ReadAllBytes(path));

            Assert.That(reaching.Settle(new AtomicSnapshotStore(path)).Status, Is.EqualTo(RunSettlementStatus.Committed));
            SaveSnapshot saved = store.Load()!;
            Assert.That(saved.ConfirmedCoins, Is.EqualTo(703));
            Assert.That(saved.CatalogMilestones.Single().SourceRunId, Is.EqualTo(saved.Settlements.Single().RunId));
        }

        [Test]
        public void CurrentDrawWriterPreservesACommittedMilestoneAndItsSourceReceipt()
        {
            var store = new AtomicSnapshotStore(path);
            Seed(store, 4, firstUnpaid: 5);
            TestSortie reaching = Start(store, Loot("fifth"));
            Assert.That(reaching.Settle(store).Status, Is.EqualTo(RunSettlementStatus.Committed));
            SaveSnapshot paid = store.Load()!;

            int[] sizes = { 5, 5, 5, 3, 3 };
            var pools = new List<IReadOnlyList<WeaponDefinitionId>>();
            var grades = new Dictionary<WeaponDefinitionId, int>();
            for (int grade = 1; grade <= sizes.Length; grade++)
            {
                var pool = new WeaponDefinitionId[sizes[grade - 1]];
                for (int index = 0; index < pool.Length; index++)
                {
                    pool[index] = new WeaponDefinitionId($"fixture_{grade}_{index}");
                    grades.Add(pool[index], grade);
                }
                pools.Add(pool);
            }
            var draw = new WeightedWeaponDraw(WeightedWeaponDraw.DraftGradeWeightsBasisPoints, pools, grades);
            Guid purchase = Guid.NewGuid();
            var result = new CoinWeaponDrawService(store, draw).Purchase(purchase, _ => 0);

            Assert.That(result.Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
            SaveSnapshot reopened = new AtomicSnapshotStore(path).Load()!;
            Assert.That(reopened.ConfirmedCoins, Is.EqualTo(paid.ConfirmedCoins - 500));
            Assert.That(reopened.CatalogMilestones.Single().SourceRunId, Is.EqualTo(paid.Settlements.Single().RunId));
            Assert.That(reopened.CatalogMilestones.Single().TransactionId, Is.EqualTo("CatalogMilestone:5"));
            Assert.That(reopened.CoinWeaponDraws.Single().RequestId, Is.EqualTo(purchase));
        }

        [Test]
        public void SellingARecoveredDuplicateKeepsTheMilestoneAndPaidBalance()
        {
            var store = new AtomicSnapshotStore(path);
            Seed(store, 4, firstUnpaid: 5);
            WeaponInstance sold = Loot("wooden_spear");
            TestSortie reaching = Start(store, sold, Loot("wooden_spear"));
            Assert.That(reaching.Settle(store).Status, Is.EqualTo(RunSettlementStatus.Committed));
            SaveSnapshot paid = store.Load()!;
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(
                "Assets/ProjectWeapon/Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset");
            var prices = AssetDatabase.LoadAssetAtPath<WeaponSalePrices>(
                "Assets/ProjectWeapon/WeaponSale/PrototypeWeaponSalePrices.asset");
            var sale = new WeaponSaleService(store, new RunState(() => TimeSpan.Zero), catalog, prices);
            Guid request = Guid.NewGuid();

            Assert.That(sale.TrySell(request, sold.Id).Status, Is.EqualTo(WeaponSaleStatus.Sold));
            SaveSnapshot reopened = new AtomicSnapshotStore(path).Load()!;
            Assert.That(reopened.ConfirmedCoins, Is.EqualTo(paid.ConfirmedCoins + 30));
            Assert.That(reopened.CatalogMilestones.Single().SourceRunId, Is.EqualTo(paid.Settlements.Single().RunId));
            Assert.That(reopened.WeaponSales.Single().RequestId, Is.EqualTo(request));

            TestSortie later = Start(store, Loot("wooden_spear"));
            Assert.That(later.Settle(store).Status, Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(sale.TrySell(request, sold.Id).Status, Is.EqualTo(WeaponSaleStatus.AlreadySold));
            SaveSnapshot final = store.Load()!;
            Assert.That(final.CatalogMilestones, Has.Count.EqualTo(1));
            Assert.That(final.WeaponSales, Has.Count.EqualTo(1));
            Assert.That(final.ConfirmedCoins, Is.EqualTo(reopened.ConfirmedCoins + 3));
        }

        private void WriteLegacyProfileWithoutMilestones(int recovered)
        {
            var legacy = new LegacyPayload
            {
                revision = 1,
                confirmedCoins = 200,
                catalog = Enumerable.Range(0, recovered)
                    .Select(index => new CatalogSaveRecord(new WeaponDefinitionId("recovered_" + index), "recovered"))
                    .ToArray()
            };
            string payload = JsonUtility.ToJson(legacy);
            Assert.That(payload, Does.Not.Contain("catalogMilestones"));
            using var sha = SHA256.Create();
            var envelope = new LegacyEnvelope
            {
                payload = payload,
                checksum = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)))
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonUtility.ToJson(envelope), new UTF8Encoding(false));
        }

        [Serializable]
        private sealed class LegacyEnvelope
        {
            public int formatVersion = 1;
            public string payload = string.Empty;
            public string checksum = string.Empty;
        }

        [Serializable]
        private sealed class LegacyPayload
        {
            public long revision;
            public long confirmedCoins;
            public WeaponInstance[] weapons = Array.Empty<WeaponInstance>();
            public CatalogSaveRecord[] catalog = Array.Empty<CatalogSaveRecord>();
            public SettlementSaveRecord[] settlements = Array.Empty<SettlementSaveRecord>();
        }

        private static void Seed(AtomicSnapshotStore store, int recovered, int firstUnpaid)
        {
            CatalogSaveRecord[] progress = Enumerable.Range(0, recovered)
                .Select(index => new CatalogSaveRecord(new WeaponDefinitionId("recovered_" + index),
                    index == 0 ? "mastered" : "recovered"))
                .Concat(new[] { new CatalogSaveRecord(new WeaponDefinitionId("silhouette"), "discovered"),
                    new CatalogSaveRecord(new WeaponDefinitionId("legacy"), "opaque-status") })
                .ToArray();
            var previous = Enumerable.Range(1, firstUnpaid / 5 - 1)
                .Select(tier => new CatalogMilestoneSaveRecord(5 * tier, 500L << (tier - 1), Guid.NewGuid())).ToArray();
            var oldReceipts = previous.Select(reward => new SettlementSaveRecord(reward.SourceRunId,
                "RunSettlement:" + reward.SourceRunId, "Extracted"));
            var initial = new SaveSnapshot(1, 200, Array.Empty<WeaponInstance>(), progress, oldReceipts,
                catalogMilestones: previous);
            Assert.That(store.Commit(0, initial).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
        }

        private static WeaponInstance Loot(string id)
            => WeaponInstance.Create(new WeaponDefinitionId(id), WeaponProvenance.ExplorationLoot,
                WeaponLocation.Warehouse());

        private static TestSortie Start(AtomicSnapshotStore store, params WeaponInstance[] loot)
            => Start(store, RunEndReason.Extracted, loot);

        private static TestSortie Start(AtomicSnapshotStore store, RunEndReason reason, params WeaponInstance[] loot)
        {
            SaveSnapshot current = store.Load()!;
            var town = new InventoryService(current.Weapons);
            RunId id = RunId.New();
            var assigned = town.PrepareStarterLoan(id.Value);
            Assert.That(assigned.Status, Is.EqualTo(StarterLoanStatus.Issued));
            TimeSpan clock = TimeSpan.Zero;
            var run = new RunState(() => clock);
            SortieStartResult started = town.TryStartSortie(id.Value, () => run.TryStartRun(id, 1, TimeSpan.FromSeconds(10)));
            Assert.That(started.Status, Is.EqualTo(SortieStartStatus.Started));
            SortieManifest manifest = started.Manifest!;
            var field = new InventoryService(manifest.Equipped);
            foreach (WeaponInstance item in loot)
                Assert.That(field.TryPickup(item).Status, Is.EqualTo(InventoryPickupStatus.PickedUp));

            Assert.That(store.Commit(current.Revision, current.Next(current.ConfirmedCoins, weapons: town.Snapshot())).Status,
                Is.EqualTo(SnapshotCommitStatus.Committed));
            RunResult? ended;
            if (reason == RunEndReason.Extracted)
            {
                ExtractionAttempt attempt = run.TryBeginExtraction(id, true)!;
                clock = attempt.CompletesAt;
                ended = run.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, clock) });
            }
            else if (reason == RunEndReason.Death)
                ended = run.ProcessSignals(new[] { RunSignal.PlayerDied(id, clock) });
            else
            {
                clock = run.CurrentRun!.Deadline;
                ended = run.ProcessSignals(Array.Empty<RunSignal>());
            }
            Assert.That(ended?.Reason, Is.EqualTo(reason));
            return new TestSortie(ended!, manifest, field, id);
        }

        private sealed class TestSortie
        {
            private readonly SortieManifest manifest;
            private readonly InventoryService field;
            private readonly RunCoinAccumulator wallet;

            internal TestSortie(RunResult result, SortieManifest manifest, InventoryService field, RunId id)
            {
                Result = result;
                this.manifest = manifest;
                this.field = field;
                wallet = new RunCoinAccumulator(id);
                wallet.AddReward(3);
            }

            internal RunResult Result { get; }
            internal RunSettlementCommit Settle(AtomicSnapshotStore store) => Settle(new RunSettlementCoordinator(store));
            internal RunSettlementCommit Settle(RunSettlementCoordinator coordinator)
                => coordinator.TrySettle(Result, manifest, field.Snapshot(), wallet);
        }
    }
}
