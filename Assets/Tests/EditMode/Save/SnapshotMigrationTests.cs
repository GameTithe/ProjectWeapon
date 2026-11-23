#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.Save.Tests
{
    public sealed class SnapshotMigrationTests
    {
        private const string FixtureId = "ProjectWeapon.KAN82.TestFixture";
        private const long Revision = 71;
        private const long Coins = 3_000_000_001L;
        private string directory = string.Empty;
        private string path = string.Empty;

        [SetUp]
        public void CreateFixtureDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon.Save.Migration.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "profile.json");
        }

        [TearDown]
        public void DeleteFixtureDirectory()
        {
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);

            Directory.Delete(directory);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UpgradePreservesRevisionAndDataWhetherTheFixtureHasReceipts(bool includesReceipts)
        {
            SaveSnapshot expected = Snapshot(Revision, includesReceipts);
            FixturePayload fixture = MakeFixture(expected, includesReceipts);
            string payload = JsonUtility.ToJson(fixture);
            if (!includesReceipts)
                Assert.That(payload, Does.Not.Contain("\"settlements\""));
            WriteEnvelope(payload);

            var store = new AtomicSnapshotStore(path);
            Assert.That(() => store.Load(), Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("Unsupported snapshot format version -1"));

            var migration = store.UpgradeLegacySnapshot();
            Assert.That(migration.Upgraded, Is.True);
            AssertSnapshot(migration.Snapshot, expected);
            AssertSnapshot(new AtomicSnapshotStore(path).Load(), expected);
            Assert.That(File.ReadAllText(path), Does.Contain("\"formatVersion\":1"));

            byte[] migratedBytes = File.ReadAllBytes(path);
            var repeated = store.UpgradeLegacySnapshot();
            Assert.That(repeated.Upgraded, Is.False);
            AssertSnapshot(repeated.Snapshot, expected);
            CollectionAssert.AreEqual(migratedBytes, File.ReadAllBytes(path));

            Assert.That(store.Commit(Revision, Snapshot(Revision + 1, includesReceipts)).Status,
                Is.EqualTo(SnapshotCommitStatus.Committed));
        }

        [Test]
        public void MigrationLeavesAnExistingCurrentFormatByteForByteUnchanged()
        {
            var store = new AtomicSnapshotStore(path);
            SaveSnapshot expected = Snapshot(1, includesReceipts: true);
            store.Commit(0, expected);
            byte[] original = File.ReadAllBytes(path);

            var result = new AtomicSnapshotStore(path).UpgradeLegacySnapshot();

            Assert.That(result.Upgraded, Is.False);
            AssertSnapshot(result.Snapshot, expected);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        }

        [Test]
        public void MigrationDoesNotCreateAnActiveSnapshotWhenNoneExists()
        {
            var result = new AtomicSnapshotStore(path).UpgradeLegacySnapshot();

            Assert.That(result.Upgraded, Is.False);
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(File.Exists(path), Is.False);
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(97)]
        public void UnsupportedVersionsStayUnchangedThroughUpgradeAndCommit(int version)
        {
            SaveSnapshot expected = Snapshot(Revision, includesReceipts: true);
            WriteEnvelope(JsonUtility.ToJson(MakeFixture(expected, true)), version);
            byte[] original = File.ReadAllBytes(path);
            var store = new AtomicSnapshotStore(path);

            Assert.That(() => store.UpgradeLegacySnapshot(), Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains($"Unsupported snapshot format version {version}"));
            Assert.That(() => store.Commit(Revision, Snapshot(Revision + 1, false)),
                Throws.TypeOf<InvalidDataException>());

            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        [TestCase("marker", "requires the KAN-82 fixture marker")]
        [TestCase("coin", "coin balance is not a valid integer")]
        [TestCase("checksum", "checksum does not match")]
        public void InvalidFixtureRemainsUnchanged(string corruption, string diagnostic)
        {
            FixturePayload fixture = MakeFixture(Snapshot(Revision, true), true);
            if (corruption == "marker")
                fixture.fixtureId = "unknown";
            if (corruption == "coin")
                fixture.coins = "1.5";
            WriteEnvelope(JsonUtility.ToJson(fixture), validChecksum: corruption != "checksum");
            byte[] original = File.ReadAllBytes(path);

            Assert.That(() => new AtomicSnapshotStore(path).UpgradeLegacySnapshot(),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains(diagnostic));

            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AtomicUpgradeRetryAfterAnIoFailurePreservesData(bool failAfterReplace)
        {
            SaveSnapshot expected = Snapshot(Revision, true);
            WriteEnvelope(JsonUtility.ToJson(MakeFixture(expected, true)));
            byte[] original = File.ReadAllBytes(path);
            var failing = new AtomicSnapshotStore(path, new FailingReplacement(failAfterReplace));
            var normal = new AtomicSnapshotStore(path);

            Assert.That(() => failing.UpgradeLegacySnapshot(), Throws.TypeOf<IOException>());
            if (failAfterReplace)
                AssertSnapshot(normal.Load(), expected);
            else
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path));

            var retry = normal.UpgradeLegacySnapshot();
            Assert.That(retry.Upgraded, Is.EqualTo(!failAfterReplace));
            AssertSnapshot(retry.Snapshot, expected);
            AssertSnapshot(normal.Load(), expected);
        }

        [Test]
        public void ConcurrentMigrationCallsInstallOnlyOnce()
        {
            SaveSnapshot expected = Snapshot(Revision, true);
            WriteEnvelope(JsonUtility.ToJson(MakeFixture(expected, true)));
            using var start = new ManualResetEventSlim();
            AtomicSnapshotStore FirstStore() => new AtomicSnapshotStore(path);
            AtomicSnapshotStore SecondStore() => new AtomicSnapshotStore(Path.Combine(directory, ".", "profile.json"));
            var first = Task.Run(() =>
            {
                start.Wait();
                return FirstStore().UpgradeLegacySnapshot();
            });
            var second = Task.Run(() =>
            {
                start.Wait();
                return SecondStore().UpgradeLegacySnapshot();
            });
            start.Set();

            var results = Task.WhenAll(first, second).GetAwaiter().GetResult();
            Assert.That(results.Select(item => item.Upgraded), Is.EquivalentTo(new[] { true, false }));
            AssertSnapshot(results[0].Snapshot, expected);
            AssertSnapshot(results[1].Snapshot, expected);
        }

        private static SaveSnapshot Snapshot(long revision, bool includesReceipts)
        {
            var definition = new WeaponDefinitionId("old_sword");
            WeaponInstance weapon = WeaponInstance.Create(definition, WeaponProvenance.Personal, WeaponLocation.Carried(2));
            var catalog = new CatalogSaveRecord(definition, "회수 정보 보존");
            var runId = Guid.NewGuid();
            var settlement = new SettlementSaveRecord(runId, $"RunSettlement:{runId:D}", "정산 성공");
            return new SaveSnapshot(revision, Coins, new[] { weapon }, new[] { catalog },
                includesReceipts ? new[] { settlement } : Array.Empty<SettlementSaveRecord>());
        }

        private static FixturePayload MakeFixture(SaveSnapshot snapshot, bool includesReceipts)
        {
            FixturePayload fixture = includesReceipts
                ? new FixturePayloadWithReceipts { settlements = snapshot.Settlements.ToArray() }
                : new FixturePayload();
            fixture.revision = snapshot.Revision;
            fixture.coins = snapshot.ConfirmedCoins.ToString(CultureInfo.InvariantCulture);
            fixture.weapons = snapshot.Weapons.ToArray();
            fixture.catalog = snapshot.Catalog.ToArray();
            return fixture;
        }

        private void WriteEnvelope(string payload, int version = -1, bool validChecksum = true)
        {
            using SHA256 sha = SHA256.Create();
            var fixture = new FixtureEnvelope
            {
                formatVersion = version,
                payload = payload,
                checksum = validChecksum ? Convert.ToBase64String(sha.ComputeHash(SnapshotCodec.Utf8.GetBytes(payload)))
                    : "invalid-checksum"
            };
            File.WriteAllText(path, JsonUtility.ToJson(fixture), SnapshotCodec.Utf8);
        }

        private static void AssertSnapshot(SaveSnapshot? actual, SaveSnapshot expected)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.Revision, Is.EqualTo(expected.Revision));
            Assert.That(actual.ConfirmedCoins, Is.EqualTo(expected.ConfirmedCoins));
            Assert.That(actual.Weapons.Count, Is.EqualTo(1));
            Assert.That(actual.Weapons[0].Id, Is.EqualTo(expected.Weapons[0].Id));
            Assert.That(actual.Weapons[0].DefinitionId, Is.EqualTo(expected.Weapons[0].DefinitionId));
            Assert.That(actual.Weapons[0].Provenance, Is.EqualTo(expected.Weapons[0].Provenance));
            Assert.That(actual.Weapons[0].Location.Kind, Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(actual.Weapons[0].Location.SlotIndex, Is.EqualTo(2));
            Assert.That(actual.Catalog.Count, Is.EqualTo(1));
            Assert.That(actual.Catalog[0].DefinitionId, Is.EqualTo(expected.Catalog[0].DefinitionId));
            Assert.That(actual.Catalog[0].State, Is.EqualTo(expected.Catalog[0].State));
            Assert.That(actual.Settlements.Count, Is.EqualTo(expected.Settlements.Count));
            if (expected.Settlements.Count != 0)
            {
                SettlementSaveRecord previous = expected.Settlements[0];
                Assert.That(actual.TryGetSettlement(previous.TransactionId, out SettlementSaveRecord? receipt), Is.True);
                Assert.That(receipt!.RunId, Is.EqualTo(previous.RunId));
                Assert.That(receipt.Result, Is.EqualTo(previous.Result));
            }
        }

        [Serializable]
        private sealed class FixtureEnvelope
        {
            public int formatVersion;
            public string payload = string.Empty;
            public string checksum = string.Empty;
        }

        [Serializable]
        private class FixturePayload
        {
            public string fixtureId = FixtureId;
            public long revision;
            public string coins = string.Empty;
            public WeaponInstance[] weapons = Array.Empty<WeaponInstance>();
            public CatalogSaveRecord[] catalog = Array.Empty<CatalogSaveRecord>();
        }

        [Serializable]
        private sealed class FixturePayloadWithReceipts : FixturePayload
        {
            public SettlementSaveRecord[] settlements = Array.Empty<SettlementSaveRecord>();
        }

        private sealed class FailingReplacement : SnapshotFileOperations
        {
            private readonly bool failAfterReplace;

            internal FailingReplacement(bool failAfterReplace)
            {
                this.failAfterReplace = failAfterReplace;
            }

            internal override void Activate(string tempPath, string activePath, bool hasActiveSnapshot)
            {
                if (!failAfterReplace)
                    throw new IOException("Injected failure before replacing the legacy snapshot.");

                base.Activate(tempPath, activePath, hasActiveSnapshot);
                throw new IOException("Injected response failure after replacing the legacy snapshot.");
            }
        }
    }
}
