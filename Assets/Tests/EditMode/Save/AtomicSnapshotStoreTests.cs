#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Save.Tests
{
    public sealed class AtomicSnapshotStoreTests
    {
        private string directory = string.Empty;
        private string path = string.Empty;

        [SetUp]
        public void CreateTempDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon.Save.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "profile.json");
        }

        [TearDown]
        public void DeleteTempDirectory()
        {
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);

            Directory.Delete(directory);
        }

        [Test]
        public void CreatesAndReplacesTheCompleteSnapshot()
        {
            var store = new AtomicSnapshotStore(path);
            Assert.That(store.Load(), Is.Null);

            SaveSnapshot first = CreateSnapshot(1, "낡은 검");
            Assert.That(store.Commit(0, first).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            AssertSnapshot(new AtomicSnapshotStore(path).Load(), first);

            SaveSnapshot second = CreateSnapshot(2, "new");
            Assert.That(store.Commit(1, second).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            AssertSnapshot(new AtomicSnapshotStore(path).Load(), second);
        }

        [TestCase(FailureStage.PartialWrite)]
        [TestCase(FailureStage.BeforeInstall)]
        public void FailureBeforeInstallPreservesTheCompletePreviousSnapshot(FailureStage stage)
        {
            var store = new AtomicSnapshotStore(path);
            SaveSnapshot first = CreateSnapshot(1, "previous");
            SaveSnapshot second = CreateSnapshot(2, "attempt");
            store.Commit(0, first);

            var failing = new AtomicSnapshotStore(path, new FailingFiles(stage));
            Assert.That(() => failing.Commit(1, second), Throws.TypeOf<IOException>());
            AssertSnapshot(new AtomicSnapshotStore(path).Load(), first);
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);

            Assert.That(store.Commit(1, second).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            AssertSnapshot(store.Load(), second);
        }

        [TestCase(FailureStage.PartialWrite)]
        [TestCase(FailureStage.BeforeInstall)]
        public void FailureOfTheFirstCommitDoesNotExposeAnActiveSnapshot(FailureStage stage)
        {
            var failing = new AtomicSnapshotStore(path, new FailingFiles(stage));

            Assert.That(() => failing.Commit(0, CreateSnapshot(1, "first")), Throws.TypeOf<IOException>());
            Assert.That(new AtomicSnapshotStore(path).Load(), Is.Null);
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnknownPostInstallResultCanBeReadAndRetriedWithoutReapplying(bool replace)
        {
            var store = new AtomicSnapshotStore(path);
            long expected = replace ? 1 : 0;
            if (replace)
                store.Commit(0, CreateSnapshot(1, "previous"));

            SaveSnapshot next = CreateSnapshot(expected + 1, "committed");
            var failing = new AtomicSnapshotStore(path, new FailingFiles(FailureStage.AfterInstall));
            Assert.That(() => failing.Commit(expected, next), Throws.TypeOf<IOException>());
            AssertSnapshot(new AtomicSnapshotStore(path).Load(), next);

            SnapshotCommitResult retry = failing.Commit(expected, next);
            Assert.That(retry.Status, Is.EqualTo(SnapshotCommitStatus.AlreadyCommitted));
            AssertSnapshot(retry.ActiveSnapshot, next);
            string id = next.Settlements[0].TransactionId;
            Assert.That(retry.ActiveSnapshot!.TryGetSettlement(id, out SettlementSaveRecord? record), Is.True);
            Assert.That(record!.Result, Is.EqualTo("committed"));
        }

        [Test]
        public void StaleRevisionReturnsCurrentSnapshotWithoutOverwritingIt()
        {
            var store = new AtomicSnapshotStore(path);
            SaveSnapshot firstWriter = CreateSnapshot(1, "winner");
            SaveSnapshot staleWriter = CreateSnapshot(1, "stale");
            store.Commit(0, firstWriter);

            SnapshotCommitResult result = store.Commit(0, staleWriter);
            Assert.That(result.Status, Is.EqualTo(SnapshotCommitStatus.RevisionConflict));
            AssertSnapshot(result.ActiveSnapshot, firstWriter);
            AssertSnapshot(store.Load(), firstWriter);
        }

        [Test]
        public void ConcurrentStoreInstancesAtTheSameNormalizedPathCommitExactlyOneCandidate()
        {
            var store1 = new AtomicSnapshotStore(path);
            var store2 = new AtomicSnapshotStore(Path.Combine(directory, ".", Path.GetFileName(path)));
            store1.Commit(0, CreateSnapshot(1, "base"));
            SaveSnapshot candidate1 = CreateSnapshot(2, "one");
            SaveSnapshot candidate2 = CreateSnapshot(2, "two");
            using var start = new ManualResetEventSlim();
            Task<SnapshotCommitResult> first = Task.Run(() =>
            {
                start.Wait();
                return store1.Commit(1, candidate1);
            });
            Task<SnapshotCommitResult> second = Task.Run(() =>
            {
                start.Wait();
                return store2.Commit(1, candidate2);
            });
            start.Set();

            SnapshotCommitResult[] results = Task.WhenAll(first, second).GetAwaiter().GetResult();
            Assert.That(results.Select(result => result.Status), Is.EquivalentTo(
                new[] { SnapshotCommitStatus.Committed, SnapshotCommitStatus.RevisionConflict }));
            SaveSnapshot winner = results[0].Status == SnapshotCommitStatus.Committed ? candidate1 : candidate2;
            AssertSnapshot(results[0].ActiveSnapshot, winner);
            AssertSnapshot(results[1].ActiveSnapshot, winner);
            AssertSnapshot(store1.Load(), winner);
        }

        [Test]
        public void IndependentWriterLockProtectsTheActiveSnapshot()
        {
            var store = new AtomicSnapshotStore(path);
            SaveSnapshot first = CreateSnapshot(1, "previous");
            SaveSnapshot second = CreateSnapshot(2, "next");
            store.Commit(0, first);

            using (new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(() => store.Commit(1, second), Throws.TypeOf<IOException>());
                AssertSnapshot(store.Load(), first);
            }

            Assert.That(store.Commit(1, second).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            AssertSnapshot(store.Load(), second);
        }

        [Test]
        public void AValidOrTornOrphanedTempFileNeverBecomesActive()
        {
            var store = new AtomicSnapshotStore(path);
            File.WriteAllText(path + ".torn.tmp", "{");
            File.WriteAllBytes(path + ".complete.tmp", SnapshotCodec.Encode(CreateSnapshot(2, "staging")).bytes);
            Assert.That(store.Load(), Is.Null);

            SaveSnapshot committed = CreateSnapshot(1, "active");
            store.Commit(0, committed);
            AssertSnapshot(store.Load(), committed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RejectsTheActiveFileWhenItIsTruncatedOrItsPayloadIsModified(bool truncated)
        {
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, CreateSnapshot(1, "payload-token"));
            string valid = File.ReadAllText(path);
            string damaged = truncated ? valid.Substring(0, valid.Length / 2) :
                valid.Replace("payload-token", "tampered-state");
            Assert.That(damaged, Is.Not.EqualTo(valid));
            File.WriteAllText(path, damaged);

            Assert.That(() => store.Load(), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => store.Commit(1, CreateSnapshot(2, "later")), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void RejectsTheActiveFileWhenItsFormatVersionIsUnsupported()
        {
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, CreateSnapshot(1, "previous"));
            string valid = File.ReadAllText(path);
            string unsupported = valid.Replace("\"formatVersion\":1", "\"formatVersion\":2");
            Assert.That(unsupported, Is.Not.EqualTo(valid));
            File.WriteAllText(path, unsupported);

            Assert.That(() => store.Load(), Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("Unsupported snapshot format version 2"));
        }

        [Test]
        public void InvalidOrSkippedRevisionsCannotBeWritten()
        {
            var store = new AtomicSnapshotStore(path);
            Assert.That(() => store.Commit(-1, CreateSnapshot(1, "next")), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => store.Commit(0, CreateSnapshot(2, "next")), Throws.TypeOf<ArgumentException>());
            Assert.That(store.Load(), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CannotCreateSnapshotsWithDuplicateSettlementKeys(bool sameRun)
        {
            SaveSnapshot sample = CreateSnapshot(1, "test");
            SettlementSaveRecord first = sample.Settlements[0];
            var duplicate = new SettlementSaveRecord(sameRun ? first.RunId : Guid.NewGuid(),
                sameRun ? "different-transaction" : first.TransactionId, "different-result");

            Assert.That(() => new SaveSnapshot(1, 0, sample.Weapons, sample.Catalog, new[] { first, duplicate }),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void RoundTripsReservedLoansAndCarriedLootWithoutChangingProvenance()
        {
            var definition = new WeaponDefinitionId("old_sword");
            Guid runId = Guid.NewGuid();
            WeaponInstance loan = WeaponInstance.Create(definition, WeaponProvenance.Loan, WeaponLocation.RunReserved(runId));
            WeaponInstance loot = WeaponInstance.Create(definition, WeaponProvenance.ExplorationLoot, WeaponLocation.Carried(1));
            var snapshot = new SaveSnapshot(1, 0, new[] { loan, loot }, Array.Empty<CatalogSaveRecord>(),
                Array.Empty<SettlementSaveRecord>());
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, snapshot);

            var saved = store.Load()!;
            Assert.That(saved.Weapons.Select(weapon => weapon.Id), Is.EqualTo(new[] { loan.Id, loot.Id }));
            Assert.That(saved.Weapons[0].IsLoaned, Is.True);
            Assert.That(saved.Weapons[0].Location.ReservedRunId, Is.EqualTo(runId));
            Assert.That(saved.Weapons[1].Provenance, Is.EqualTo(WeaponProvenance.ExplorationLoot));
            Assert.That(saved.Weapons[1].Location.SlotIndex, Is.EqualTo(1));
        }

        [Test]
        public void RejectsDuplicateWeaponAndCatalogRecords()
        {
            SaveSnapshot sample = CreateSnapshot(1, "test");
            Assert.That(() => new SaveSnapshot(1, 0, new[] { sample.Weapons[0], sample.Weapons[0] }, sample.Catalog,
                sample.Settlements), Throws.TypeOf<InvalidDataException>().With.Message.Contains("DuplicateInstanceId"));
            Assert.That(() => new SaveSnapshot(1, 0, sample.Weapons, new[] { sample.Catalog[0], sample.Catalog[0] },
                sample.Settlements), Throws.TypeOf<InvalidDataException>().With.Message.Contains("catalog record"));
        }

        private static SaveSnapshot CreateSnapshot(long revision, string label)
        {
            var definitionId = new WeaponDefinitionId("old_sword");
            Guid runId = Guid.NewGuid();
            WeaponInstance weapon = WeaponInstance.Create(definitionId, WeaponProvenance.Personal,
                WeaponLocation.Equipped(0));
            var catalog = new CatalogSaveRecord(definitionId, $"catalog-{label}");
            var receipt = new SettlementSaveRecord(runId, $"RunSettlement:{runId:D}", label);
            return new SaveSnapshot(revision, 3_000_000_000L + revision, new[] { weapon }, new[] { catalog }, new[] { receipt });
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
            Assert.That(actual.Weapons[0].Location.Kind, Is.EqualTo(expected.Weapons[0].Location.Kind));
            Assert.That(actual.Weapons[0].Location.SlotIndex, Is.EqualTo(expected.Weapons[0].Location.SlotIndex));
            Assert.That(actual.Catalog.Count, Is.EqualTo(1));
            Assert.That(actual.Catalog[0].DefinitionId, Is.EqualTo(expected.Catalog[0].DefinitionId));
            Assert.That(actual.Catalog[0].State, Is.EqualTo(expected.Catalog[0].State));
            Assert.That(actual.Settlements.Count, Is.EqualTo(1));
            Assert.That(actual.Settlements[0].RunId, Is.EqualTo(expected.Settlements[0].RunId));
            Assert.That(actual.Settlements[0].TransactionId, Is.EqualTo(expected.Settlements[0].TransactionId));
            Assert.That(actual.Settlements[0].Result, Is.EqualTo(expected.Settlements[0].Result));
        }

        public enum FailureStage
        {
            PartialWrite,
            BeforeInstall,
            AfterInstall
        }

        private sealed class FailingFiles : SnapshotFileOperations
        {
            private readonly FailureStage stage;

            internal FailingFiles(FailureStage stage)
            {
                this.stage = stage;
            }

            internal override void WriteAndFlush(string path, byte[] bytes)
            {
                if (stage == FailureStage.PartialWrite)
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    stream.Write(bytes, 0, bytes.Length / 2);
                    throw new IOException("Injected partial write.");
                }

                base.WriteAndFlush(path, bytes);
            }

            internal override void Activate(string tempPath, string activePath, bool hasActiveSnapshot)
            {
                if (stage == FailureStage.BeforeInstall)
                    throw new IOException("Injected replacement failure.");

                base.Activate(tempPath, activePath, hasActiveSnapshot);

                if (stage == FailureStage.AfterInstall)
                    throw new IOException("Injected response loss after the atomic install.");
            }
        }
    }
}
