#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.Save.Tests
{
    public sealed class SnapshotRecoveryTests
    {
        private static readonly WeaponDefinitionId Known = new WeaponDefinitionId("verified-sword");
        private static readonly WeaponDefinitionId Missing = new WeaponDefinitionId("missing-sword");
        private string directory = string.Empty;
        private string path = string.Empty;

        [SetUp]
        public void CreateDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon.Save.Recovery.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "profile.json");
        }

        [TearDown]
        public void DeleteDirectory()
        {
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [Test]
        public void EmptyProfileAndHealthyActiveHaveDifferentStatuses()
        {
            var store = new AtomicSnapshotStore(path);
            File.WriteAllBytes(path + ".open.tmp", SnapshotCodec.Encode(Sample(2)).bytes);

            SnapshotLoadResult empty = store.LoadWithRecovery(IsKnown);
            Assert.That(empty.Status, Is.EqualTo(SnapshotLoadStatus.Missing));
            Assert.That(empty.Snapshot, Is.Null);
            Assert.That(File.Exists(path), Is.False);

            SaveSnapshot active = Sample(1);
            store.Commit(0, active);
            File.WriteAllText(path + ".bak", "unusable backup");
            SnapshotLoadResult result = store.LoadWithRecovery(IsKnown);

            Assert.That(result.Status, Is.EqualTo(SnapshotLoadStatus.Healthy));
            Assert.That(result.Snapshot!.Weapons[0].Id, Is.EqualTo(active.Weapons[0].Id));
            Assert.That(result.ActiveIssue, Is.Null);
            Assert.That(result.BackupIssue, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TruncatedActiveReturnsLastHealthyBackupWithoutChangingFiles(bool cutLastBraceOnly)
        {
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, Sample(1));
            SaveSnapshot expectedBackup = Sample(2);
            store.Commit(1, expectedBackup);
            store.Commit(2, Sample(3));

            byte[] backupBytes = File.ReadAllBytes(path + ".bak");
            byte[] latestBytes = File.ReadAllBytes(path);
            Array.Resize(ref latestBytes, cutLastBraceOnly ? latestBytes.Length - 1 : latestBytes.Length / 2);
            File.WriteAllBytes(path, latestBytes);

            SnapshotLoadResult recovery = store.LoadWithRecovery(IsKnown);
            Assert.That(recovery.Status, Is.EqualTo(SnapshotLoadStatus.RecoveredFromBackup));
            Assert.That(recovery.ActiveIssue, Does.Contain("JSON"));
            Assert.That(recovery.Snapshot!.Revision, Is.EqualTo(2));
            Assert.That(recovery.Snapshot.ConfirmedCoins, Is.EqualTo(expectedBackup.ConfirmedCoins));
            Assert.That(recovery.Snapshot.Weapons[0].Id, Is.EqualTo(expectedBackup.Weapons[0].Id));
            Assert.That(recovery.Snapshot.Catalog[0].State, Is.EqualTo(expectedBackup.Catalog[0].State));
            Assert.That(recovery.Snapshot.Settlements[0].TransactionId, Is.EqualTo(expectedBackup.Settlements[0].TransactionId));

            SnapshotLoadResult retry = new AtomicSnapshotStore(path).LoadWithRecovery(IsKnown);
            Assert.That(retry.Status, Is.EqualTo(SnapshotLoadStatus.RecoveredFromBackup));
            Assert.That(retry.Snapshot!.Revision, Is.EqualTo(recovery.Snapshot.Revision));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(latestBytes));
            Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(backupBytes));
            Assert.That(() => store.Load(), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void DuplicateInstanceIdsWithValidChecksumStillRecoverThePreviousRevision()
        {
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, Sample(1));
            SaveSnapshot sample = Sample(2);
            store.Commit(1, sample);

            string payload = JsonUtility.ToJson(sample).Replace(sample.Weapons[1].Id.ToString(), sample.Weapons[0].Id.ToString());
            WriteEnvelope(path, payload);

            SnapshotLoadResult result = store.LoadWithRecovery(IsKnown);
            Assert.That(result.Status, Is.EqualTo(SnapshotLoadStatus.RecoveredFromBackup));
            Assert.That(result.Snapshot!.Revision, Is.EqualTo(1));
            Assert.That(result.ActiveIssue, Does.Contain("DuplicateInstanceId"));
        }

        [TestCase("weapon")]
        [TestCase("catalog")]
        [TestCase("receipt")]
        public void MissingDefinitionInActiveSnapshotIsDetectedEvenWithAValidChecksum(string owner)
        {
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, Sample(1));
            SaveSnapshot active = Sample(2, owner);
            store.Commit(1, active);
            byte[] bytes = File.ReadAllBytes(path);

            SnapshotLoadResult result = store.LoadWithRecovery(IsKnown);

            Assert.That(result.Status, Is.EqualTo(SnapshotLoadStatus.RecoveredFromBackup));
            Assert.That(result.Snapshot!.Revision, Is.EqualTo(1));
            Assert.That(result.ActiveIssue, Does.Contain("missing definition 'missing-sword'"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        }

        [Test]
        public void InvalidUtf8IsReportedAndCorruptBackupIsRejected()
        {
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, Sample(1, "catalog"));
            store.Commit(1, Sample(2));
            byte[] unreadable = { 0xc3, 0x28 };
            File.WriteAllBytes(path, unreadable);
            byte[] backup = File.ReadAllBytes(path + ".bak");

            SnapshotLoadResult result = store.LoadWithRecovery(IsKnown);

            Assert.That(result.Status, Is.EqualTo(SnapshotLoadStatus.Corrupt));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.ActiveIssue, Does.Contain("invalid UTF-8"));
            Assert.That(result.BackupIssue, Does.Contain("missing definition"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(unreadable));
            Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(backup));
        }

        [Test]
        public void CorruptFirstRevisionWithNoBackupNeverBecomesAnEmptyNewProfile()
        {
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, Sample(1));
            File.WriteAllText(path, "{");
            SnapshotLoadResult result = store.LoadWithRecovery(IsKnown);

            Assert.That(result.Status, Is.EqualTo(SnapshotLoadStatus.Corrupt));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.BackupIssue, Does.Contain("missing"));
            Assert.That(File.ReadAllText(path), Is.EqualTo("{"));
        }

        [Test]
        public void MissingActiveCanReturnBackupWithoutCreatingAnActiveFile()
        {
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, Sample(1));
            store.Commit(1, Sample(2));
            File.Delete(path);

            SnapshotLoadResult result = store.LoadWithRecovery(IsKnown);

            Assert.That(result.Status, Is.EqualTo(SnapshotLoadStatus.RecoveredFromBackup));
            Assert.That(result.Snapshot!.Revision, Is.EqualTo(1));
            Assert.That(result.ActiveIssue, Does.Contain("missing"));
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void ValidFutureVersionIsNeverSilentlyReplacedByAnOlderBackup()
        {
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, Sample(1));
            store.Commit(1, Sample(2));
            string original = File.ReadAllText(path);
            string future = original.Replace("\"formatVersion\":1", "\"formatVersion\":2");
            Assert.That(future, Is.Not.EqualTo(original));
            File.WriteAllText(path, future);

            SnapshotLoadResult result = store.LoadWithRecovery(IsKnown);

            Assert.That(result.Status, Is.EqualTo(SnapshotLoadStatus.UnsupportedVersion));
            Assert.That(result.ActiveIssue, Does.Contain("version 2"));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(File.ReadAllText(path), Is.EqualTo(future));
            Assert.That(File.Exists(path + ".bak"), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LaterDrawAwardReferenceIsCheckedEvenWhenWeaponIsNoLongerInInventory(bool missingDefinition)
        {
            var store = new AtomicSnapshotStore(path);
            store.Commit(0, Sample(1));

            SaveSnapshot sample = Sample(2);
            var awards = new CoinWeaponDrawAward[10];
            awards[0] = new CoinWeaponDrawAward(sample.Weapons[0].Id, Known, 1);
            for (int index = 1; index < awards.Length; index++)
                awards[index] = new CoinWeaponDrawAward(WeaponInstanceId.New(), Known, 1);

            WeaponInstanceId lastAward = awards[awards.Length - 1].InstanceId;
            if (missingDefinition)
                awards[awards.Length - 1] = new CoinWeaponDrawAward(lastAward, Missing, 1);

            Assert.That(lastAward, Is.Not.EqualTo(sample.Weapons[0].Id));
            Assert.That(lastAward, Is.Not.EqualTo(sample.Weapons[1].Id));

            var purchase = new CoinWeaponDrawSaveRecord(Guid.NewGuid(), 10, sample.ConfirmedCoins, awards);
            var active = new SaveSnapshot(2, sample.ConfirmedCoins, sample.Weapons, sample.Catalog,
                sample.Settlements, new[] { purchase });
            store.Commit(1, active);
            byte[] original = File.ReadAllBytes(path);

            SnapshotLoadResult result = store.LoadWithRecovery(IsKnown);

            if (missingDefinition)
            {
                Assert.That(result.Status, Is.EqualTo(SnapshotLoadStatus.RecoveredFromBackup));
                Assert.That(result.Snapshot!.Revision, Is.EqualTo(1));
                Assert.That(result.ActiveIssue, Does.Contain(lastAward.ToString()).And.Contain("missing-sword"));
            }
            else
            {
                Assert.That(result.Status, Is.EqualTo(SnapshotLoadStatus.Healthy));
                Assert.That(result.Snapshot!.CoinWeaponDraws[0].AwardCount, Is.EqualTo(10));
                Assert.That(result.Snapshot.CoinWeaponDraws[0].Awards[9].InstanceId, Is.EqualTo(lastAward));
            }

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
        }

        private static bool IsKnown(WeaponDefinitionId id) => id.Equals(Known);

        private static SaveSnapshot Sample(long revision, string? missingReference = null)
        {
            var first = WeaponInstance.Create(missingReference == "weapon" ? Missing : Known,
                WeaponProvenance.Personal, WeaponLocation.Warehouse());
            var second = WeaponInstance.Create(Known, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            var catalog = new[]
            {
                new CatalogSaveRecord(missingReference == "catalog" ? Missing : Known, "found")
            };
            var settlements = new[]
            {
                new SettlementSaveRecord(Guid.NewGuid(), $"tx-{revision}", "complete")
            };
            var receipts = new[]
            {
                new CoinWeaponDrawSaveRecord(Guid.NewGuid(), 10, revision * 50,
                    first.Id, missingReference == "receipt" ? Missing : Known, 1)
            };
            return new SaveSnapshot(revision, revision * 50, new[] { first, second }, catalog, settlements, receipts);
        }

        private static void WriteEnvelope(string path, string payload)
        {
            using SHA256 sha = SHA256.Create();
            var envelope = new RecoveryEnvelope
            {
                formatVersion = 1,
                payload = payload,
                checksum = Convert.ToBase64String(sha.ComputeHash(SnapshotCodec.Utf8.GetBytes(payload)))
            };
            File.WriteAllText(path, JsonUtility.ToJson(envelope), SnapshotCodec.Utf8);
        }

        [Serializable]
        private sealed class RecoveryEnvelope
        {
            public int formatVersion;
            public string payload = string.Empty;
            public string checksum = string.Empty;
        }
    }
}
