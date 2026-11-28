#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using ProjectWeapon.CoinWeaponDraw;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.WeaponDraw;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.WeaponSale.Tests
{
    public sealed class WeaponSaleServiceTests
    {
        private static readonly WeaponLocation Warehouse = WeaponLocation.Warehouse();
        private static readonly WeaponDefinitionId OldSword = DefinitionId(1);
        private readonly List<WeaponDefinition> definitions = new List<WeaponDefinition>();
        private string directory = null!;
        private string path = null!;
        private AtomicSnapshotStore store = null!;
        private RunState run = null!;
        private WeaponCatalog catalog = null!;
        private WeaponSalePrices prices = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), $"ProjectWeapon-WeaponSale-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "inventory.json");
            store = new AtomicSnapshotStore(path);
            run = new RunState(() => TimeSpan.Zero);
            catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
            prices = AssetDatabase.LoadAssetAtPath<WeaponSalePrices>(
                "Assets/ProjectWeapon/WeaponSale/PrototypeWeaponSalePrices.asset");
            Assert.That(prices, Is.Not.Null);

            for (int grade = 1; grade <= 5; grade++)
            {
                var definition = ScriptableObject.CreateInstance<WeaponDefinition>();
                using var fields = new SerializedObject(definition);
                fields.FindProperty("definitionId").stringValue = DefinitionId(grade).Value;
                fields.FindProperty("displayName").stringValue = $"Sale fixture grade {grade}";
                fields.FindProperty("grade").intValue = grade;
                fields.FindProperty("kind").intValue = (int)WeaponKind.Sword;
                fields.ApplyModifiedPropertiesWithoutUndo();
                definitions.Add(definition);
            }

            using var catalogFields = new SerializedObject(catalog);
            SerializedProperty entries = catalogFields.FindProperty("definitions");
            entries.arraySize = definitions.Count;
            for (int index = 0; index < definitions.Count; index++)
                entries.GetArrayElementAtIndex(index).objectReferenceValue = definitions[index];
            catalogFields.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(catalog);
            foreach (WeaponDefinition definition in definitions)
                Object.DestroyImmediate(definition);
            definitions.Clear();

            foreach (string file in Directory.GetFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [TestCase(1, 30)]
        [TestCase(2, 75)]
        [TestCase(3, 150)]
        [TestCase(4, 300)]
        [TestCase(5, 450)]
        public void SellingOneDuplicateUsesTheGradePriceAndPreservesOtherRecords(int grade, int price)
        {
            WeaponInstance offered = Item(DefinitionId(grade), Warehouse, WeaponProvenance.ExplorationLoot);
            WeaponInstance retained = Item(DefinitionId(grade), WeaponLocation.Equipped(1));
            Seed(300, offered, retained);
            Guid request = Guid.NewGuid();

            WeaponSaleResult sold = Service().TrySell(request, offered.Id);

            Assert.That(sold.Status, Is.EqualTo(WeaponSaleStatus.Sold));
            SaveSnapshot active = store.Load()!;
            Assert.That(active.Revision, Is.EqualTo(2));
            Assert.That(active.ConfirmedCoins, Is.EqualTo(300 + price));
            Assert.That(active.Weapons.Select(item => item.Id), Is.EqualTo(new[] { retained.Id }));
            Assert.That(active.Catalog.Single().State, Is.EqualTo("recovered"));
            Assert.That(active.Settlements.Single().Result, Is.EqualTo("existing outcome"));
            Assert.That(active.CoinWeaponDraws.Count, Is.EqualTo(1));
            WeaponSaleSaveRecord receipt = active.WeaponSales.Single();
            Assert.That(receipt.RequestId, Is.EqualTo(request));
            Assert.That(receipt.InstanceId, Is.EqualTo(offered.Id));
            Assert.That(receipt.DefinitionId, Is.EqualTo(offered.DefinitionId));
            Assert.That(receipt.Grade, Is.EqualTo(grade));
            Assert.That(receipt.Price, Is.EqualTo(price));
            Assert.That(receipt.BalanceAfterSale, Is.EqualTo(300 + price));
        }

        [Test]
        public void SameRequestReplaysAndReusingItsIdForAnotherInstanceConflicts()
        {
            WeaponInstance offered = Item(OldSword, Warehouse);
            WeaponInstance retained = Item(OldSword, Warehouse);
            Seed(300, offered, retained);
            Guid request = Guid.NewGuid();
            Assert.That(Service().TrySell(request, offered.Id).Status, Is.EqualTo(WeaponSaleStatus.Sold));
            Assert.That(run.TryStartRun(RunId.New(), 1, TimeSpan.FromMinutes(1)), Is.True);

            var reloaded = Service();
            WeaponSaleResult replay = reloaded.TrySell(request, offered.Id);
            Assert.That(replay.Status, Is.EqualTo(WeaponSaleStatus.AlreadySold));
            Assert.That(replay.Receipt!.Price, Is.EqualTo(30));
            Assert.That(reloaded.TrySell(request, retained.Id).Status, Is.EqualTo(WeaponSaleStatus.RequestConflict));
            Assert.That(store.Load()!.ConfirmedCoins, Is.EqualTo(330));
            Assert.That(store.Load()!.WeaponSales.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConcurrentReplayOfTheSameRequestAccountsForOneSale()
        {
            WeaponInstance offered = Item(OldSword, Warehouse);
            Seed(300, offered, Item(OldSword, Warehouse));
            Guid request = Guid.NewGuid();
            var racing = Service((revision, next) =>
            {
                Assert.That(Service().TrySell(request, offered.Id).Status, Is.EqualTo(WeaponSaleStatus.Sold));
                return store.Commit(revision, next);
            });

            WeaponSaleResult replay = racing.TrySell(request, offered.Id);

            Assert.That(replay.Status, Is.EqualTo(WeaponSaleStatus.AlreadySold));
            Assert.That(replay.Receipt!.InstanceId, Is.EqualTo(offered.Id));
            Assert.That(replay.ActiveSnapshot!.ConfirmedCoins, Is.EqualTo(330));
            Assert.That(replay.ActiveSnapshot.Revision, Is.EqualTo(2));
            Assert.That(replay.ActiveSnapshot.WeaponSales.Count, Is.EqualTo(1));
        }

        [Test]
        public void MissingLoansUnsellableLocationsAndLastOwnedCopyAreRejected()
        {
            WeaponInstance onlyOwned = Item(OldSword, Warehouse);
            WeaponInstance loan = Item(OldSword, WeaponLocation.Equipped(0), WeaponProvenance.Loan);
            WeaponInstance equipped = Item(DefinitionId(2), WeaponLocation.Equipped(1));
            WeaponInstance carried = Item(OldSword, WeaponLocation.Carried(0));
            Seed(300, onlyOwned, loan, equipped, carried);

            var service = Service();
            Assert.That(service.TrySell(Guid.Empty, onlyOwned.Id).Status, Is.EqualTo(WeaponSaleStatus.InvalidRequest));
            Assert.That(service.TrySell(Guid.NewGuid(), WeaponInstanceId.New()).Status,
                Is.EqualTo(WeaponSaleStatus.MissingInstance));
            Assert.That(service.TrySell(Guid.NewGuid(), loan.Id).Status, Is.EqualTo(WeaponSaleStatus.ProvenanceRestricted));
            Assert.That(service.TrySell(Guid.NewGuid(), equipped.Id).Status, Is.EqualTo(WeaponSaleStatus.InvalidLocation));
            Assert.That(service.TrySell(Guid.NewGuid(), carried.Id).Status, Is.EqualTo(WeaponSaleStatus.InvalidLocation));
            Assert.That(service.TrySell(Guid.NewGuid(), onlyOwned.Id).Status, Is.EqualTo(WeaponSaleStatus.NoOwnedDuplicate));
            Assert.That(store.Load()!.Revision, Is.EqualTo(1));
            Assert.That(store.Load()!.WeaponSales.Count, Is.Zero);
        }

        [Test]
        public void AnActiveOrUnsettledRunBlocksSales()
        {
            WeaponInstance offered = Item(OldSword, Warehouse);
            WeaponInstance duplicate = Item(OldSword, Warehouse);
            SaveSnapshot initial = Seed(300, offered, duplicate);
            Guid activeRunId = Guid.NewGuid();
            Assert.That(run.TryStartRun(new RunId(activeRunId), 1, TimeSpan.FromMinutes(1)), Is.True);

            Assert.That(Service().TrySell(Guid.NewGuid(), offered.Id).Status, Is.EqualTo(WeaponSaleStatus.NotInTown));
            run = new RunState(() => TimeSpan.Zero);
            WeaponInstance reserved = Item(DefinitionId(2), WeaponLocation.RunReserved(activeRunId));
            store.Commit(1, new SaveSnapshot(2, initial.ConfirmedCoins, initial.Weapons.Append(reserved),
                initial.Catalog, initial.Settlements, initial.CoinWeaponDraws, initial.WeaponSales));

            Assert.That(Service().TrySell(Guid.NewGuid(), offered.Id).Status, Is.EqualTo(WeaponSaleStatus.NotInTown));
            Assert.That(store.Load()!.ConfirmedCoins, Is.EqualTo(300));
        }

        [Test]
        public void PreInstallFailureLeavesNoPartialWriteAndTheSameRequestCanRetry()
        {
            WeaponInstance offered = Item(OldSword, Warehouse);
            WeaponInstance duplicate = Item(OldSword, Warehouse);
            Seed(300, offered, duplicate);
            Guid request = Guid.NewGuid();
            var interrupted = Service((_, _) => throw new IOException("Injected pre-install failure"));

            Assert.Throws<IOException>(() => interrupted.TrySell(request, offered.Id));
            SaveSnapshot before = store.Load()!;
            Assert.That(before.Revision, Is.EqualTo(1));
            Assert.That(before.ConfirmedCoins, Is.EqualTo(300));
            Assert.That(before.Weapons.Select(item => item.Id), Is.EquivalentTo(new[] { offered.Id, duplicate.Id }));
            Assert.That(before.WeaponSales.Count, Is.Zero);
            Assert.That(Service().TrySell(request, offered.Id).Status, Is.EqualTo(WeaponSaleStatus.Sold));
        }

        [Test]
        public void PostInstallResponseLossReturnsTheCommittedReceiptWithoutAnotherCredit()
        {
            WeaponInstance offered = Item(OldSword, Warehouse);
            Seed(300, offered, Item(OldSword, Warehouse));
            Guid request = Guid.NewGuid();
            var interrupted = Service((revision, next) =>
            {
                Assert.That(store.Commit(revision, next).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
                throw new IOException("Injected lost response");
            });

            Assert.Throws<IOException>(() => interrupted.TrySell(request, offered.Id));
            WeaponSaleResult replay = Service().TrySell(request, offered.Id);

            Assert.That(replay.Status, Is.EqualTo(WeaponSaleStatus.AlreadySold));
            Assert.That(replay.ActiveSnapshot!.Revision, Is.EqualTo(2));
            Assert.That(replay.ActiveSnapshot.ConfirmedCoins, Is.EqualTo(330));
            Assert.That(replay.ActiveSnapshot.WeaponSales.Count, Is.EqualTo(1));
            Assert.That(replay.ActiveSnapshot.Weapons.Count, Is.EqualTo(1));
        }

        [Test]
        public void CompetingSaleWinsRevisionAndRetryCannotSellTheLastOwnedCopy()
        {
            WeaponInstance offered = Item(OldSword, Warehouse);
            WeaponInstance competitor = Item(OldSword, Warehouse);
            Seed(300, offered, competitor);
            Guid request = Guid.NewGuid();
            var outer = Service((revision, next) =>
            {
                Assert.That(Service().TrySell(Guid.NewGuid(), competitor.Id).Status, Is.EqualTo(WeaponSaleStatus.Sold));
                return store.Commit(revision, next);
            });

            WeaponSaleResult conflict = outer.TrySell(request, offered.Id);
            Assert.That(conflict.Status, Is.EqualTo(WeaponSaleStatus.RevisionConflict));
            Assert.That(conflict.ActiveSnapshot!.ConfirmedCoins, Is.EqualTo(330));
            Assert.That(conflict.ActiveSnapshot.Weapons.Single().Id, Is.EqualTo(offered.Id));
            Assert.That(conflict.ActiveSnapshot.WeaponSales.Single().InstanceId, Is.EqualTo(competitor.Id));
            Assert.That(Service().TrySell(request, offered.Id).Status, Is.EqualTo(WeaponSaleStatus.NoOwnedDuplicate));
            Assert.That(store.Load()!.ConfirmedCoins, Is.EqualTo(330));
        }

        [Test]
        public void LegacySchemaOneLoadsThenALaterCoinDrawPreservesTheSaleReceipt()
        {
            WeaponInstance offered = Item(OldSword, Warehouse);
            WeaponInstance retained = Item(OldSword, Warehouse);
            string weapons = JsonUtility.ToJson(offered) + "," + JsonUtility.ToJson(retained);
            string payload = "{\"revision\":1,\"confirmedCoins\":1000,\"weapons\":[" + weapons
                + "],\"catalog\":[],\"settlements\":[]}";
            WriteLegacySnapshot(payload);
            Assert.That(store.Load()!.WeaponSales.Count, Is.Zero);

            Guid request = Guid.NewGuid();
            Assert.That(Service().TrySell(request, offered.Id).Status, Is.EqualTo(WeaponSaleStatus.Sold));
            var draw = new CoinWeaponDrawService(store, CreateDraw());
            Assert.That(draw.Purchase(Guid.NewGuid(), _ => 0).Status, Is.EqualTo(CoinWeaponDrawStatus.Purchased));

            SaveSnapshot final = store.Load()!;
            Assert.That(final.ConfirmedCoins, Is.EqualTo(530));
            Assert.That(final.WeaponSales.Single().RequestId, Is.EqualTo(request));
            Assert.That(final.Weapons.Any(item => item.Id == offered.Id), Is.False);
            Assert.That(Service().TrySell(request, offered.Id).Status, Is.EqualTo(WeaponSaleStatus.AlreadySold));
        }

        [Test]
        public void OverflowOrUnknownDefinitionDoesNotCreateASale()
        {
            WeaponDefinitionId unknown = new WeaponDefinitionId("unknown-weapon");
            WeaponInstance offered = Item(OldSword, Warehouse);
            WeaponInstance unknownItem = Item(unknown, Warehouse);
            Seed(long.MaxValue, offered, Item(OldSword, Warehouse), unknownItem, Item(unknown, Warehouse));

            Assert.That(Service().TrySell(Guid.NewGuid(), offered.Id).Status, Is.EqualTo(WeaponSaleStatus.StorageLimit));
            Assert.That(Service().TrySell(Guid.NewGuid(), unknownItem.Id).Status, Is.EqualTo(WeaponSaleStatus.InvalidDefinition));
            Assert.That(store.Load()!.ConfirmedCoins, Is.EqualTo(long.MaxValue));
            Assert.That(store.Load()!.WeaponSales.Count, Is.Zero);
        }

        [Test]
        public void StorageRejectsDuplicateReceiptsAndReappearingSoldInstances()
        {
            WeaponInstance sold = Item(OldSword, Warehouse);
            var receipt = new WeaponSaleSaveRecord(Guid.NewGuid(), sold.Id, OldSword, 1, 30, 30);
            var repeated = new WeaponSaleSaveRecord(Guid.NewGuid(), sold.Id, OldSword, 1, 30, 60);

            Assert.Throws<InvalidDataException>(() => new SaveSnapshot(2, 60, Array.Empty<WeaponInstance>(),
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>(), weaponSales: new[] { receipt, repeated }));
            Assert.Throws<InvalidDataException>(() => new SaveSnapshot(2, 30, new[] { sold },
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>(), weaponSales: new[] { receipt }));
        }

        private WeaponSaleService Service(Func<long, SaveSnapshot, SnapshotCommitResult>? commit = null)
            => commit == null ? new WeaponSaleService(store, run, catalog, prices)
                : new WeaponSaleService(store, run, catalog, prices, commit);

        private SaveSnapshot Seed(long balance, params WeaponInstance[] items)
        {
            Guid transaction = Guid.NewGuid();
            var catalog = new[] { new CatalogSaveRecord(OldSword, "recovered") };
            var pastRun = new[] { new SettlementSaveRecord(transaction, $"settlement:{transaction:D}", "existing outcome") };
            var draw = new[] { new CoinWeaponDrawSaveRecord(Guid.NewGuid(), 500, balance, WeaponInstanceId.New(), OldSword, 1) };
            var initial = new SaveSnapshot(1, balance, items, catalog, pastRun, draw);
            Assert.That(store.Commit(0, initial).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            return initial;
        }

        private void WriteLegacySnapshot(string payload)
        {
            using SHA256 sha = SHA256.Create();
            var envelope = new FixtureEnvelope
            {
                payload = payload,
                checksum = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)))
            };
            File.WriteAllText(path, JsonUtility.ToJson(envelope), new UTF8Encoding(false));
        }

        private static WeightedWeaponDraw CreateDraw()
        {
            int[] sizes = { 5, 5, 5, 3, 3 };
            var pools = new IReadOnlyList<WeaponDefinitionId>[sizes.Length];
            var grades = new Dictionary<WeaponDefinitionId, int>();
            for (int grade = 1; grade <= sizes.Length; grade++)
            {
                var ids = new WeaponDefinitionId[sizes[grade - 1]];
                for (int index = 0; index < ids.Length; index++)
                {
                    ids[index] = new WeaponDefinitionId($"draw-grade-{grade}-weapon-{index}");
                    grades.Add(ids[index], grade);
                }
                pools[grade - 1] = ids;
            }
            return new WeightedWeaponDraw(WeightedWeaponDraw.DraftGradeWeightsBasisPoints, pools, grades);
        }

        private static WeaponDefinitionId DefinitionId(int grade) => new WeaponDefinitionId($"sale-weapon-{grade}");

        private static WeaponInstance Item(WeaponDefinitionId definition, WeaponLocation location,
            WeaponProvenance provenance = WeaponProvenance.Personal) => WeaponInstance.Create(definition, provenance, location);

        [Serializable]
        private sealed class FixtureEnvelope
        {
            public int formatVersion = 1;
            public string payload = string.Empty;
            public string checksum = string.Empty;
        }
    }
}
