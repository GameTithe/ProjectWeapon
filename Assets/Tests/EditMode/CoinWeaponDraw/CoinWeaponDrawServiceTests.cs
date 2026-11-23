#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Save;
using ProjectWeapon.WeaponDraw;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.CoinWeaponDraw.Tests
{
    public sealed class CoinWeaponDrawServiceTests
    {
        private string directory = string.Empty;
        private string path = string.Empty;
        private AtomicSnapshotStore store = null!;
        private readonly Guid id = Guid.Parse("6953c11b-bf4e-4aac-bb42-ad0939f5a02f");

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon.CoinDraw.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "profile.json");
            store = new AtomicSnapshotStore(path);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [Test]
        public void Rejects499WithoutDrawingOrWriting()
        {
            Seed(499);
            byte[] original = File.ReadAllBytes(path);

            var result = Service().Purchase(id, NoDraw);

            Assert.That(result.Status, Is.EqualTo(CoinWeaponDrawStatus.InsufficientFunds));
            Assert.That(result.Receipt, Is.Null);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(store.Load()!.ConfirmedCoins, Is.EqualTo(499));
        }

        [Test]
        public void MissingProfileCannotCreateAFreeWeapon()
        {
            Assert.That(Service().Purchase(id, NoDraw).Status, Is.EqualTo(CoinWeaponDrawStatus.InsufficientFunds));
            Assert.That(store.Load(), Is.Null);
        }

        [Test]
        public void Exact500AtomicDrawRetainsCatalogAndDistinctRunReceipt()
        {
            Seed(500);
            CoinWeaponDrawResult result = Service().Purchase(id, _ => 0);
            Assert.That(result.Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
            SaveSnapshot saved = store.Load()!;

            Assert.That(saved.Revision, Is.EqualTo(2));
            Assert.That(saved.ConfirmedCoins, Is.Zero);
            Assert.That(saved.Weapons.Count, Is.EqualTo(1));
            Assert.That(saved.Weapons[0].Location.Kind, Is.EqualTo(WeaponLocationKind.Warehouse));
            Assert.That(saved.Weapons[0].Provenance, Is.EqualTo(WeaponProvenance.Personal));
            Assert.That(saved.Catalog.Count, Is.EqualTo(1));
            Assert.That(saved.Catalog[0].DefinitionId.Value, Is.EqualTo("historical"));
            Assert.That(saved.Catalog[0].State, Is.EqualTo("existing recovery"));
            Assert.That(saved.Settlements.Count, Is.EqualTo(1));
            Assert.That(saved.Settlements[0].RunId, Is.EqualTo(id));
            Assert.That(saved.CoinWeaponDraws.Count, Is.EqualTo(1));
            Assert.That(saved.TryGetCoinWeaponDraw(id, out CoinWeaponDrawSaveRecord? receipt), Is.True);
            AssertReceipt(receipt!, result.Receipt!);
            Assert.That(receipt!.Cost, Is.EqualTo(500));
            Assert.That(receipt.DefinitionId.Value, Is.EqualTo("g1-w0"));
            Assert.That(receipt.Grade, Is.EqualTo(1));
            Assert.That(receipt.BalanceAfterPurchase, Is.Zero);
            Assert.That(receipt.InstanceId, Is.EqualTo(saved.Weapons[0].Id));
        }

        [Test]
        public void CommittedDuplicateReplaysAfterRestartWithoutDrawOrNewRevision()
        {
            Seed(800);
            var first = Service().Purchase(id, _ => 0);
            byte[] original = File.ReadAllBytes(path);
            var another = new CoinWeaponDrawService(new AtomicSnapshotStore(path), NewDraw());

            CoinWeaponDrawResult again = another.Purchase(id, NoDraw);

            Assert.That(again.Status, Is.EqualTo(CoinWeaponDrawStatus.AlreadyPurchased));
            AssertReceipt(again.Receipt!, first.Receipt!);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(store.Load()!.Weapons.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConflictingPriceForSameRequestDoesNotChangeTheSavedOutcome()
        {
            Seed(800);
            Service().Purchase(id, _ => 0);
            byte[] original = File.ReadAllBytes(path);
            var alternatePrice = new CoinWeaponDrawService(store, NewDraw(), 400);

            var result = alternatePrice.Purchase(id, NoDraw);

            Assert.That(result.Status, Is.EqualTo(CoinWeaponDrawStatus.RequestConflict));
            Assert.That(result.Receipt, Is.Null);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }

        [Test]
        public void LaterLossOfTheIssuedWeaponDoesNotAllowAnotherGrant()
        {
            Seed(900);
            CoinWeaponDrawResult prior = Service().Purchase(id, _ => 0);
            SaveSnapshot active = store.Load()!;
            store.Commit(active.Revision, new SaveSnapshot(active.Revision + 1, 1_000, Array.Empty<WeaponInstance>(),
                active.Catalog, active.Settlements, active.CoinWeaponDraws));

            var duplicate = Service().Purchase(id, NoDraw);

            Assert.That(duplicate.Status, Is.EqualTo(CoinWeaponDrawStatus.AlreadyPurchased));
            AssertReceipt(duplicate.Receipt!, prior.Receipt!);
            Assert.That(duplicate.ActiveSnapshot!.Weapons, Is.Empty);
            Assert.That(duplicate.ActiveSnapshot.ConfirmedCoins, Is.EqualTo(1_000));
            Assert.That(duplicate.Receipt!.BalanceAfterPurchase, Is.EqualTo(400));
        }

        [Test]
        [Platform(Include = "Win")]
        public void FailureDuringAtomicReplaceKeepsBothTheCoinsAndInventoryBeforeRetry()
        {
            Seed(500);
            byte[] original = File.ReadAllBytes(path);
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.That(() => Service().Purchase(id, _ => 0), Throws.InstanceOf<IOException>());

            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(store.Load()!.ConfirmedCoins, Is.EqualTo(500));
            Assert.That(store.Load()!.Weapons, Is.Empty);
            Assert.That(store.Load()!.CoinWeaponDraws, Is.Empty);
            Assert.That(Service().Purchase(id, _ => 0).Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
            Assert.That(store.Load()!.ConfirmedCoins, Is.Zero);
            Assert.That(store.Load()!.Weapons.Count, Is.EqualTo(1));
        }

        [Test]
        public void FailedCommitBeforeWriteDoesNotDebitOrMint()
        {
            Seed(500);
            byte[] original = File.ReadAllBytes(path);
            var faulted = new CoinWeaponDrawService(store, NewDraw(), 500, (revision, candidate) =>
            {
                Assert.That(candidate.ConfirmedCoins, Is.Zero);
                Assert.That(candidate.Weapons.Count, Is.EqualTo(1));
                throw new IOException("Injected interruption before writing the candidate.");
            });

            Assert.That(() => faulted.Purchase(id, _ => 0), Throws.TypeOf<IOException>());
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(Service().Purchase(id, _ => 0).Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
            Assert.That(store.Load()!.Weapons.Count, Is.EqualTo(1));
        }

        [Test]
        public void LostResponseAfterRealReplaceReplaysTheOnlyPersistedAward()
        {
            Seed(500);
            var faulted = new CoinWeaponDrawService(store, NewDraw(), 500, (revision, candidate) =>
            {
                Assert.That(store.Commit(revision, candidate).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
                throw new IOException("Injected response loss after the disk commit.");
            });

            Assert.That(() => faulted.Purchase(id, _ => 0), Throws.TypeOf<IOException>());
            SaveSnapshot persisted = store.Load()!;
            byte[] original = File.ReadAllBytes(path);
            var retry = Service().Purchase(id, NoDraw);

            Assert.That(retry.Status, Is.EqualTo(CoinWeaponDrawStatus.AlreadyPurchased));
            Assert.That(persisted.ConfirmedCoins, Is.Zero);
            Assert.That(persisted.Weapons.Count, Is.EqualTo(1));
            AssertReceipt(retry.Receipt!, persisted.CoinWeaponDraws[0]);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }

        [Test]
        public void LostRevisionToSameRequestReturnsWinnerWithoutPublishingTheLosingCandidate()
        {
            Seed(500);
            CoinWeaponDrawResult? winner = null;
            var losing = new CoinWeaponDrawService(store, NewDraw(), 500, (revision, candidate) =>
            {
                winner = Service().Purchase(id, exclusive => exclusive == 10_000 ? 5_000 : 0);
                Assert.That(winner.Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
                return store.Commit(revision, candidate);
            });

            CoinWeaponDrawResult replay = losing.Purchase(id, _ => 0);

            Assert.That(replay.Status, Is.EqualTo(CoinWeaponDrawStatus.AlreadyPurchased));
            Assert.That(replay.Receipt!.DefinitionId.Value, Is.EqualTo("g2-w0"));
            AssertReceipt(replay.Receipt!, winner!.Receipt!);
            Assert.That(store.Load()!.Weapons.Count, Is.EqualTo(1));
            Assert.That(store.Load()!.ConfirmedCoins, Is.Zero);
        }

        [Test]
        public void UnrelatedRevisionConflictDoesNotSpendAgainstTheStaleBalance()
        {
            Seed(500);
            var service = new CoinWeaponDrawService(store, NewDraw(), 500, (revision, candidate) =>
            {
                SaveSnapshot current = store.Load()!;
                store.Commit(current.Revision, new SaveSnapshot(current.Revision + 1, 499, current.Weapons,
                    current.Catalog, current.Settlements, current.CoinWeaponDraws));
                return store.Commit(revision, candidate);
            });

            var result = service.Purchase(id, _ => 0);

            Assert.That(result.Status, Is.EqualTo(CoinWeaponDrawStatus.RevisionConflict));
            Assert.That(result.ActiveSnapshot!.ConfirmedCoins, Is.EqualTo(499));
            Assert.That(store.Load()!.Weapons, Is.Empty);
            Assert.That(Service().Purchase(id, NoDraw).Status, Is.EqualTo(CoinWeaponDrawStatus.InsufficientFunds));
        }

        [Test]
        public void OldSchemaOneSnapshotWithoutDrawReceiptsRemainsReadable()
        {
            string payload = "{\"revision\":1,\"confirmedCoins\":500,\"weapons\":[],\"catalog\":[],\"settlements\":[]}";
            WriteSchemaOnePayload(payload);

            Assert.That(store.Load()!.CoinWeaponDraws, Is.Empty);
            Assert.That(Service().Purchase(id, _ => 0).Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
            Assert.That(store.Load()!.CoinWeaponDraws.Count, Is.EqualTo(1));
        }

        [Test]
        public void TenDrawRejects4999WithoutSamplingOrWriting()
        {
            Seed(4_999);
            byte[] original = File.ReadAllBytes(path);

            var result = Service().PurchaseTen(id, NoDraw);

            Assert.That(result.Status, Is.EqualTo(CoinWeaponDrawStatus.InsufficientFunds));
            Assert.That(result.Receipt, Is.Null);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }

        [Test]
        public void TenDrawCommits5000AndAllTenDistinctInstancesWithOneReceipt()
        {
            Seed(5_000);
            int samples = 0;
            CoinWeaponDrawResult purchase = Service().PurchaseTen(id, _ =>
            {
                samples++;
                return 0;
            });

            Assert.That(purchase.Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
            Assert.That(samples, Is.EqualTo(20));
            Assert.That(purchase.Receipt!.Cost, Is.EqualTo(CoinWeaponDrawService.PrototypeTenDrawCost));
            SaveSnapshot actual = store.Load()!;
            Assert.That(actual.Revision, Is.EqualTo(2));
            Assert.That(actual.ConfirmedCoins, Is.Zero);
            Assert.That(actual.Weapons.Count, Is.EqualTo(10));
            Assert.That(actual.Weapons.Select(item => item.Id).Distinct().Count(), Is.EqualTo(10));
            Assert.That(actual.Weapons.All(item => item.Provenance == WeaponProvenance.Personal
                && item.Location.Kind == WeaponLocationKind.Warehouse), Is.True);
            Assert.That(actual.CoinWeaponDraws.Count, Is.EqualTo(1));
            Assert.That(actual.Catalog[0].State, Is.EqualTo("existing recovery"));
            Assert.That(actual.Settlements[0].RunId, Is.EqualTo(id));
            IReadOnlyList<CoinWeaponDrawAward> awards = actual.CoinWeaponDraws[0].Awards;
            Assert.That(awards.Select(award => award.InstanceId), Is.EqualTo(actual.Weapons.Select(item => item.Id)));
            Assert.That(awards.All(award => award.Grade == 1 && award.DefinitionId.Value == "g1-w0"), Is.True);
        }

        [Test]
        public void ConfiguredUnitPriceDoesNotAddABatchDiscount()
        {
            Seed(4_500);
            var service = new CoinWeaponDrawService(store, NewDraw(), 450);

            CoinWeaponDrawResult result = service.PurchaseTen(id, _ => 0);

            Assert.That(result.Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
            Assert.That(result.Receipt!.Cost, Is.EqualTo(4_500));
            Assert.That(result.ActiveSnapshot!.ConfirmedCoins, Is.Zero);
        }

        [Test]
        public void MidDrawFailureDoesNotSaveAnyPrefixAndCanBeRetried()
        {
            Seed(5_000);
            byte[] original = File.ReadAllBytes(path);
            int samples = 0;

            Assert.That(() => Service().PurchaseTen(id, bound =>
            {
                samples++;
                return samples == 11 ? bound : 0;
            }), Throws.InvalidOperationException);

            Assert.That(samples, Is.EqualTo(11));
            Assert.That(store.Load()!.Weapons, Is.Empty);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(Service().PurchaseTen(id, _ => 0).Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
            Assert.That(store.Load()!.Weapons.Count, Is.EqualTo(10));
            Assert.That(store.Load()!.ConfirmedCoins, Is.Zero);
        }

        [Test]
        public void FailedWriteOfTheBatchLeavesOriginalCoinsAndWeapons()
        {
            Seed(5_000);
            byte[] original = File.ReadAllBytes(path);
            var failing = new CoinWeaponDrawService(store, NewDraw(), 500, (_, candidate) =>
            {
                Assert.That(candidate.Weapons.Count, Is.EqualTo(10));
                Assert.That(candidate.ConfirmedCoins, Is.Zero);
                throw new IOException("Injected failure before installing complete batch.");
            });

            Assert.That(() => failing.PurchaseTen(id, _ => 0), Throws.TypeOf<IOException>());

            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(store.Load()!.Weapons, Is.Empty);
            Assert.That(store.Load()!.CoinWeaponDraws, Is.Empty);
        }

        [Test]
        public void TenDrawRetryAfterLostCommitResponseReplaysAllTenInOriginalOrder()
        {
            Seed(5_000);
            var failing = new CoinWeaponDrawService(store, NewDraw(), 500, (revision, candidate) =>
            {
                Assert.That(store.Commit(revision, candidate).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
                throw new IOException("Injected loss after commit.");
            });
            int calls = 0;
            Assert.That(() => failing.PurchaseTen(id, _ => calls++ % 2 == 0 ? 0 : (calls / 2) % 5),
                Throws.TypeOf<IOException>());

            SaveSnapshot persisted = store.Load()!;
            byte[] committed = File.ReadAllBytes(path);
            var reopened = new CoinWeaponDrawService(new AtomicSnapshotStore(path), NewDraw());
            CoinWeaponDrawResult retry = reopened.PurchaseTen(id, NoDraw);

            Assert.That(retry.Status, Is.EqualTo(CoinWeaponDrawStatus.AlreadyPurchased));
            Assert.That(retry.Receipt!.AwardCount, Is.EqualTo(10));
            Assert.That(retry.Receipt.Awards.Select(award => award.DefinitionId), Is.EqualTo(
                persisted.CoinWeaponDraws[0].Awards.Select(award => award.DefinitionId)));
            Assert.That(retry.Receipt.Awards.Select(award => award.InstanceId), Is.EqualTo(
                persisted.Weapons.Select(weapon => weapon.Id)));
            CollectionAssert.AreEqual(committed, File.ReadAllBytes(path));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SameIdCannotSwitchBetweenSingleAndTenAtTheSameTotalPrice(bool tenFirst)
        {
            Seed(10_000);
            var single = new CoinWeaponDrawService(store, NewDraw(), 5_000);
            var ten = Service();
            CoinWeaponDrawResult purchased = tenFirst ? ten.PurchaseTen(id, _ => 0) : single.Purchase(id, _ => 0);
            byte[] original = File.ReadAllBytes(path);

            CoinWeaponDrawResult conflicting = tenFirst ? single.Purchase(id, NoDraw) : ten.PurchaseTen(id, NoDraw);

            Assert.That(purchased.Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));
            Assert.That(conflicting.Status, Is.EqualTo(CoinWeaponDrawStatus.RequestConflict));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }

        [Test]
        public void OriginalSingleReceiptWithoutAdditionalAwardsStillReplays()
        {
            WeaponInstance originalWeapon = WeaponInstance.Create(new WeaponDefinitionId("g1-w0"),
                WeaponProvenance.Personal, WeaponLocation.Warehouse());
            string receipt = $"{{\"requestId\":\"{id:D}\",\"cost\":500,\"balanceAfterPurchase\":500," +
                $"\"instanceId\":{JsonUtility.ToJson(originalWeapon.Id)},\"definitionId\":\"g1-w0\",\"grade\":1}}";
            string payload = "{\"revision\":1,\"confirmedCoins\":500,\"weapons\":[],\"catalog\":[]," +
                "\"settlements\":[],\"coinWeaponDraws\":[" + receipt + "]}";
            WriteSchemaOnePayload(payload);
            byte[] originalBytes = File.ReadAllBytes(path);

            CoinWeaponDrawResult replay = Service().Purchase(id, NoDraw);

            Assert.That(replay.Status, Is.EqualTo(CoinWeaponDrawStatus.AlreadyPurchased));
            Assert.That(replay.Receipt!.AwardCount, Is.EqualTo(1));
            Assert.That(replay.Receipt.InstanceId, Is.EqualTo(originalWeapon.Id));
            Assert.That(replay.Receipt.Awards[0].InstanceId, Is.EqualTo(originalWeapon.Id));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(path));
        }

        [Test]
        public void AReceiptCannotRepeatAnInstanceWithinTheBatch()
        {
            WeaponInstance item = WeaponInstance.Create(new WeaponDefinitionId("g1-w0"),
                WeaponProvenance.Personal, WeaponLocation.Warehouse());
            var award = new CoinWeaponDrawAward(item.Id, item.DefinitionId, 1);
            var receipt = new CoinWeaponDrawSaveRecord(id, 5_000, 0, new[] { award, award });

            Assert.That(() => new SaveSnapshot(1, 0, new[] { item }, Array.Empty<CatalogSaveRecord>(),
                Array.Empty<SettlementSaveRecord>(), new[] { receipt }), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void LoadRejectsTheSameHistoricalInstanceInTwoSeparateTenDrawReceipts()
        {
            var definition = new WeaponDefinitionId("g1-w0");
            CoinWeaponDrawAward NewAward() => new CoinWeaponDrawAward(
                WeaponInstance.Create(definition, WeaponProvenance.Personal, WeaponLocation.Warehouse()).Id,
                definition, 1);
            var firstAwards = Enumerable.Range(0, 10).Select(_ => NewAward()).ToArray();
            var secondAwards = Enumerable.Range(0, 10).Select(_ => NewAward()).ToArray();
            secondAwards[9] = firstAwards[9];
            var first = new CoinWeaponDrawSaveRecord(id, 5_000, 5_000, firstAwards);
            var second = new CoinWeaponDrawSaveRecord(Guid.NewGuid(), 5_000, 0, secondAwards);

            string payload = "{\"revision\":3,\"confirmedCoins\":0,\"weapons\":[],\"catalog\":[]," +
                "\"settlements\":[],\"coinWeaponDraws\":[" + JsonUtility.ToJson(first) + "," + JsonUtility.ToJson(second) + "]}";
            WriteSchemaOnePayload(payload);

            Assert.That(() => store.Load(), Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("same instance ID more than once"));
        }

        private void WriteSchemaOnePayload(string payload)
        {
            using SHA256 sha = SHA256.Create();
            var envelope = new FixtureEnvelope
            {
                payload = payload,
                checksum = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)))
            };
            File.WriteAllText(path, JsonUtility.ToJson(envelope), new UTF8Encoding(false));
        }

        private void Seed(long coins)
        {
            var catalog = new CatalogSaveRecord(new WeaponDefinitionId("historical"), "existing recovery");
            var runReceipt = new SettlementSaveRecord(id, $"RunSettlement:{id:D}", "already settled");
            store.Commit(0, new SaveSnapshot(1, coins, Array.Empty<WeaponInstance>(), new[] { catalog },
                new[] { runReceipt }));
        }

        private CoinWeaponDrawService Service() => new CoinWeaponDrawService(store, NewDraw());

        private static int NoDraw(int _) => throw new AssertionException("A rejected or committed duplicate must not draw.");

        private static WeightedWeaponDraw NewDraw()
        {
            int[] sizes = { 5, 5, 5, 3, 3 };
            var pools = new IReadOnlyList<WeaponDefinitionId>[sizes.Length];
            var grades = new Dictionary<WeaponDefinitionId, int>();
            for (int group = 0; group < sizes.Length; group++)
            {
                int grade = group + 1;
                var ids = new WeaponDefinitionId[sizes[group]];
                for (int index = 0; index < ids.Length; index++)
                {
                    ids[index] = new WeaponDefinitionId($"g{grade}-w{index}");
                    grades.Add(ids[index], grade);
                }
                pools[group] = ids;
            }

            return new WeightedWeaponDraw(WeightedWeaponDraw.DraftGradeWeightsBasisPoints, pools, grades);
        }

        private static void AssertReceipt(CoinWeaponDrawSaveRecord actual, CoinWeaponDrawSaveRecord expected)
        {
            Assert.That(actual.RequestId, Is.EqualTo(expected.RequestId));
            Assert.That(actual.Cost, Is.EqualTo(expected.Cost));
            Assert.That(actual.BalanceAfterPurchase, Is.EqualTo(expected.BalanceAfterPurchase));
            Assert.That(actual.InstanceId, Is.EqualTo(expected.InstanceId));
            Assert.That(actual.DefinitionId, Is.EqualTo(expected.DefinitionId));
            Assert.That(actual.Grade, Is.EqualTo(expected.Grade));
        }

        [Serializable]
        private sealed class FixtureEnvelope
        {
            public int formatVersion = 1;
            public string payload = string.Empty;
            public string checksum = string.Empty;
        }
    }
}
