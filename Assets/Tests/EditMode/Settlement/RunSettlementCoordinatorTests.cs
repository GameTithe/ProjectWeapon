#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Catalog;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Settlement.Tests
{
    public sealed class RunSettlementCoordinatorTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Spear = new WeaponDefinitionId("wooden_spear");
        private static readonly WeaponDefinitionId Bow = new WeaponDefinitionId("hunting_bow");
        private string path = null!;
        private AtomicSnapshotStore store = null!;

        [SetUp]
        public void SetUp()
        {
            path = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN96-" + Guid.NewGuid().ToString("N"), "snapshot.json");
            store = new AtomicSnapshotStore(path);
        }

        [TearDown]
        public void TearDown()
        {
            string folder = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(folder))
                return;
            foreach (string file in Directory.EnumerateFiles(folder))
                File.Delete(file);
            Directory.Delete(folder);
        }

        [Test]
        public void SuccessfulResultCommitsWeaponsCoinsCatalogAndReceiptOnceAfterRestartAndTenRetries()
        {
            WeaponInstance carriedIn = Weapon(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            WeaponInstance abandoned = Weapon(Bow, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            WeaponInstance townWeapon = Weapon(Sword, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            WeaponInstance loot = Weapon(Spear, WeaponProvenance.ExplorationLoot, WeaponLocation.Carried(0));
            var session = Finish(RunId.New(), RunEndReason.Extracted, carriedIn, abandoned, townWeapon);
            Save(new SaveSnapshot(1, 100, session.Town.Snapshot(),
                new[] { new CatalogSaveRecord(Bow, "opaque-old-code") }, Array.Empty<SettlementSaveRecord>()));
            var wallet = Wallet(session.Run.RunId, 35);
            var assets = new[] { carriedIn, loot };

            RunSettlementCommit first = new RunSettlementCoordinator(store).TrySettle(session.Run, session.Manifest, assets, wallet);
            Assert.That(first.Status, Is.EqualTo(RunSettlementStatus.Committed));
            Assert.That(first.Receipt!.TransactionId, Is.EqualTo("RunSettlement:" + session.Run.RunId));

            for (int retry = 0; retry < 10; retry++)
            {
                var freshStore = new AtomicSnapshotStore(path);
                RunSettlementCommit replay = new RunSettlementCoordinator(freshStore)
                    .TrySettle(session.Run, session.Manifest, assets, wallet);
                Assert.That(replay.Status, Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
                Assert.That(replay.Receipt!.Result, Is.EqualTo(first.Receipt.Result));
            }

            SaveSnapshot final = store.Load()!;
            Assert.That(final.Revision, Is.EqualTo(2));
            Assert.That(final.ConfirmedCoins, Is.EqualTo(135));
            Assert.That(final.Weapons.Select(item => item.Id), Is.EquivalentTo(new[] { townWeapon.Id, carriedIn.Id, loot.Id }));
            Assert.That(final.Weapons.All(item => item.Location.Kind == WeaponLocationKind.Warehouse), Is.True);
            var progress = new CatalogProgress(final.Catalog);
            Assert.That(progress.GetStage(Sword), Is.EqualTo(CatalogStage.Recovered));
            Assert.That(progress.GetStage(Spear), Is.EqualTo(CatalogStage.Recovered));
            Assert.That(final.Catalog.Single(item => item.DefinitionId.Equals(Bow)).State, Is.EqualTo("opaque-old-code"));
            RunSettlementDetails details = RunSettlementDetails.Read(final.Settlements.Single())!;
            Assert.That(details.AwardedCoins, Is.EqualTo(35));
            Assert.That(details.RecoveredInstanceIds, Is.EquivalentTo(new[] { carriedIn.Id.ToString(), loot.Id.ToString() }));
            Assert.That(details.LostInstanceIds.Single(), Is.EqualTo(abandoned.Id.ToString()));
            Assert.That(details.HasItemizedDetails, Is.True);
            Assert.That(details.PriorConfirmedCoins, Is.EqualTo(100));
            Assert.That(details.PreservedWarehouseCount, Is.EqualTo(1));
            Assert.That(details.ReturnedWeapons.Single().DefinitionId, Is.EqualTo(Sword));
            Assert.That(details.NewWeapons.Single().DefinitionId, Is.EqualTo(Spear));
            Assert.That(details.LostWeapons.Single().DefinitionId, Is.EqualTo(Bow));
            Assert.That(details.NewCatalogRecoveries, Is.EquivalentTo(new[] { Sword.Value, Spear.Value }));
            Assert.That(wallet.PendingCoins, Is.EqualTo(35));
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.TimeExpired)]
        public void FailureRecordsLossAndPreservesConfirmedBalanceAndCatalog(RunEndReason reason)
        {
            WeaponInstance packed = Weapon(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            WeaponInstance kept = Weapon(Sword, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            WeaponInstance held = Weapon(Spear, WeaponProvenance.ExplorationLoot, WeaponLocation.Carried(1));
            var session = Finish(RunId.New(), reason, packed, kept);
            Save(new SaveSnapshot(1, 55, session.Town.Snapshot(),
                new[] { new CatalogSaveRecord(Sword, "recovered") }, Array.Empty<SettlementSaveRecord>()));

            RunSettlementCommit result = new RunSettlementCoordinator(store).TrySettle(session.Run, session.Manifest,
                new[] { held }, Wallet(session.Run.RunId, 26));

            Assert.That(result.Status, Is.EqualTo(RunSettlementStatus.Committed));
            SaveSnapshot active = store.Load()!;
            Assert.That(active.Weapons.Single().Id, Is.EqualTo(kept.Id));
            Assert.That(active.ConfirmedCoins, Is.EqualTo(55));
            Assert.That(active.Catalog.Single().State, Is.EqualTo("recovered"));
            RunSettlementDetails details = RunSettlementDetails.Read(active.Settlements.Single())!;
            Assert.That(details.Reason, Is.EqualTo(reason));
            Assert.That(details.ForfeitedCoins, Is.EqualTo(26));
            Assert.That(details.LostInstanceIds, Is.EquivalentTo(new[] { packed.Id.ToString(), held.Id.ToString() }));
        }

        [Test]
        public void CompetingOutcomesUseTheFirstSuccessfulAtomicWrite()
        {
            RunId runId = RunId.New();
            WeaponInstance packed = Weapon(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            var success = Finish(runId, RunEndReason.Extracted, packed);
            var failure = Finish(runId, RunEndReason.Death, packed);
            Save(new SaveSnapshot(1, 55, success.Town.Snapshot(), Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>()));
            using var start = new Barrier(2);

            SnapshotCommitResult CommitAtBarrier(long revision, SaveSnapshot proposal)
            {
                if (!start.SignalAndWait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("Both records must be prepared before writing.");
                return store.Commit(revision, proposal);
            }

            var first = new RunSettlementCoordinator(store, CommitAtBarrier);
            var second = new RunSettlementCoordinator(store, CommitAtBarrier);
            Task<RunSettlementCommit> a = Task.Run(() =>
                first.TrySettle(success.Run, success.Manifest, new[] { packed }, Wallet(runId, 20)));
            Task<RunSettlementCommit> b = Task.Run(() =>
                second.TrySettle(failure.Run, failure.Manifest, new[] { packed }, Wallet(runId, 20)));

            Assert.That(Task.WaitAll(new Task[] { a, b }, TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(new[] { a.Result.Status, b.Result.Status }, Is.EquivalentTo(new[]
                { RunSettlementStatus.Committed, RunSettlementStatus.ConflictingOutcome }));
            Assert.That(a.Result.Receipt!.Result, Is.EqualTo(b.Result.Receipt!.Result));
            SaveSnapshot active = store.Load()!;
            bool recovered = a.Result.Status == RunSettlementStatus.Committed;
            Assert.That(active.Revision, Is.EqualTo(2));
            Assert.That(active.ConfirmedCoins, Is.EqualTo(recovered ? 75 : 55));
            Assert.That(active.Weapons, Has.Count.EqualTo(recovered ? 1 : 0));
            Assert.That(active.Catalog, Has.Count.EqualTo(recovered ? 1 : 0));
            Assert.That(active.Settlements, Has.Count.EqualTo(1));
        }

        [Test]
        public void WriteFailureAndLostResponseHandleAllChangesAsOneCommit()
        {
            var throwing = new RunSettlementCoordinator(store, (_, _) => throw new IOException("Persistence unavailable."));
            WeaponInstance loan = Weapon(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            var session = Finish(RunId.New(), RunEndReason.Extracted, loan);
            Save(new SaveSnapshot(1, 100, session.Town.Snapshot(), Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>()));
            var wallet = Wallet(session.Run.RunId, 7);
            Assert.Throws<IOException>(() => throwing.TrySettle(session.Run, session.Manifest, new[] { loan }, wallet));
            SaveSnapshot before = store.Load()!;
            Assert.That(before.Revision, Is.EqualTo(1));
            Assert.That(before.ConfirmedCoins, Is.EqualTo(100));
            Assert.That(before.Settlements, Is.Empty);
            Assert.That(before.Catalog, Is.Empty);
            Assert.That(before.Weapons.Single().Location.ReservedRunId, Is.EqualTo(session.Run.RunId.Value));

            var lostResponse = new RunSettlementCoordinator(store, (revision, next) =>
            {
                store.Commit(revision, next);
                throw new IOException("Caller never received confirmation.");
            });
            Assert.Throws<IOException>(() => lostResponse.TrySettle(session.Run, session.Manifest, new[] { loan }, wallet));

            RunSettlementCommit retry = new RunSettlementCoordinator(store)
                .TrySettle(session.Run, session.Manifest, new[] { loan }, wallet);
            Assert.That(retry.Status, Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
            SaveSnapshot after = retry.Snapshot!;
            Assert.That(after.Revision, Is.EqualTo(2));
            Assert.That(after.ConfirmedCoins, Is.EqualTo(107));
            Assert.That(after.Weapons.Single().Location.Kind, Is.EqualTo(WeaponLocationKind.Warehouse));
            Assert.That(after.Catalog.Single().State, Is.EqualTo("recovered"));
            Assert.That(after.Settlements, Has.Count.EqualTo(1));
        }

        [Test]
        public void LegacyReceiptForSameRunPreventsAnotherCommitEvenWithDifferentTransactionKey()
        {
            WeaponInstance packed = Weapon(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            RunId runId = RunId.New();
            var session = Finish(runId, RunEndReason.Extracted, packed);
            var existing = new SettlementSaveRecord(runId.Value, "old-run-key", "Death");
            Save(new SaveSnapshot(1, 60, Array.Empty<WeaponInstance>(), Array.Empty<CatalogSaveRecord>(), new[] { existing }));

            RunSettlementCommit result = new RunSettlementCoordinator(store)
                .TrySettle(session.Run, session.Manifest, new[] { packed }, Wallet(runId, 43));

            Assert.That(result.Status, Is.EqualTo(RunSettlementStatus.ConflictingOutcome));
            Assert.That(result.Receipt!.TransactionId, Is.EqualTo("old-run-key"));
            Assert.That(store.Load()!.Revision, Is.EqualTo(1));
        }

        [Test]
        public void UnexpectedCoinRevisionConflictCanBeRecalculatedAgainstTheNewBalance()
        {
            WeaponInstance owned = Weapon(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            var session = Finish(RunId.New(), RunEndReason.Extracted, owned);
            Save(new SaveSnapshot(1, 100, session.Town.Snapshot(), Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>()));
            var competing = new RunSettlementCoordinator(store, (revision, proposal) =>
            {
                store.Commit(revision, new SaveSnapshot(revision + 1, 50, session.Town.Snapshot(),
                    Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>()));
                return store.Commit(revision, proposal);
            });
            var wallet = Wallet(session.Run.RunId, 20);

            Assert.That(competing.TrySettle(session.Run, session.Manifest, new[] { owned }, wallet).Status,
                Is.EqualTo(RunSettlementStatus.RetryRequired));
            RunSettlementCommit result = new RunSettlementCoordinator(store).TrySettle(session.Run, session.Manifest, new[] { owned }, wallet);
            Assert.That(result.Snapshot!.ConfirmedCoins, Is.EqualTo(70));
            Assert.That(result.Snapshot.Revision, Is.EqualTo(3));
            Assert.That(result.Snapshot.Settlements, Has.Count.EqualTo(1));
        }

        [Test]
        public void VersionOneReceiptRetainsKnownFactsWithoutInventingItemizedRecords()
        {
            string payload = @"{""version"":1,""outcome"":""Extracted"",""awardedCoins"":9,""forfeitedCoins"":0,""recovered"":[""known-id""],""lost"":[],""closedLoans"":[]}";
            var receipt = new SettlementSaveRecord(Guid.NewGuid(), "historical-transaction", payload);

            RunSettlementDetails details = RunSettlementDetails.Read(receipt)!;

            Assert.That(details, Is.Not.Null);
            Assert.That(details.HasItemizedDetails, Is.False);
            Assert.That(details.PriorConfirmedCoins, Is.Null);
            Assert.That(details.PreservedWarehouseCount, Is.Null);
            Assert.That(details.AwardedCoins, Is.EqualTo(9));
            Assert.That(details.RecoveredInstanceIds, Is.EqualTo(new[] { "known-id" }));
            Assert.That(details.ReturnedWeapons, Is.Empty);
        }

        private void Save(SaveSnapshot snapshot) => Assert.That(store.Commit(0, snapshot).Status, Is.EqualTo(SnapshotCommitStatus.Committed));

        private static RunCoinAccumulator Wallet(RunId id, int pending)
        {
            var wallet = new RunCoinAccumulator(id);
            wallet.AddReward(pending);
            return wallet;
        }

        private static WeaponInstance Weapon(WeaponDefinitionId definition, WeaponProvenance provenance, WeaponLocation location)
            => WeaponInstance.Create(definition, provenance, location);

        private static (RunResult Run, SortieManifest Manifest, InventoryService Town) Finish(
            RunId id, RunEndReason reason, params WeaponInstance[] weapons)
        {
            TimeSpan now = TimeSpan.Zero;
            TimeSpan limit = TimeSpan.FromSeconds(10);
            var state = new RunState(() => now);
            var town = new InventoryService(weapons);
            SortieManifest manifest = town.TryStartSortie(id.Value, () => state.TryStartRun(id, 1, limit)).Manifest!;
            if (reason == RunEndReason.Extracted)
            {
                var channel = state.TryBeginExtraction(id, isInRange: true)!;
                now = channel.CompletesAt;
                state.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, channel.Id, now) });
            }
            else if (reason == RunEndReason.Death)
            {
                now = TimeSpan.FromSeconds(1);
                state.ProcessSignals(new[] { RunSignal.PlayerDied(id, now) });
            }
            else
            {
                now = limit;
                state.ProcessSignals(Array.Empty<RunSignal>());
            }

            return (state.Result!, manifest, town);
        }
    }
}
