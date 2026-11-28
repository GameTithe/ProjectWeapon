#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using ProjectWeapon.Extraction;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.RunRecovery.Tests
{
    public sealed class RunLifecycleRecoveryTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private readonly List<GameObject> relayObjects = new List<GameObject>();
        private string path = null!;
        private AtomicSnapshotStore store = null!;
        private TimeSpan now;
        private bool clockVerified;
        private RunSignal[] frame = null!;

        [SetUp]
        public void SetUp()
        {
            path = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN189-" + Guid.NewGuid().ToString("N"), "snapshot.json");
            store = new AtomicSnapshotStore(path);
            now = Seconds(1);
            clockVerified = true;
            frame = Array.Empty<RunSignal>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject relay in relayObjects)
                UnityEngine.Object.DestroyImmediate(relay);
            relayObjects.Clear();

            string folder = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(folder))
                return;
            foreach (string file in Directory.EnumerateFiles(folder))
                File.Delete(file);
            Directory.Delete(folder);
        }

        [Test]
        public void PauseFocusAndNetworkEdgesCoalesceAndStartANewFullChannelOnLiveResume()
        {
            Save();
            RunId id = RunId.New();
            RunState run = Start(id);
            var relay = CreateRelay(run);
            var changes = new List<RunRecoveryStatus>();
            relay.RecoveryChanged += result => changes.Add(result.Status);
            now = Seconds(4);
            ExtractionAttempt old = run.TryBeginExtraction(id, true)!;

            now = Seconds(5);
            relay.SendMessage("OnApplicationPause", true);
            relay.SendMessage("OnApplicationFocus", false);
            relay.SetConnectionAvailable(false);
            Assert.That(run.ActiveExtraction, Is.Null);
            Assert.That(relay.IsGameplayBlocked, Is.True);

            now = Seconds(8);
            relay.SendMessage("OnApplicationPause", false);
            relay.SendMessage("OnApplicationFocus", true);
            Assert.That(relay.IsGameplayBlocked, Is.True);
            relay.SetConnectionAvailable(true);
            Assert.That(changes, Is.EqualTo(new[] { RunRecoveryStatus.Suspended, RunRecoveryStatus.Resumed }));
            Assert.That(relay.IsGameplayBlocked, Is.False);
            Assert.That(run.CurrentRun!.StartedAt, Is.EqualTo(Seconds(1)));
            Assert.That(run.CurrentRun.Deadline, Is.EqualTo(Seconds(11)));

            ExtractionAttempt restarted = run.TryBeginExtraction(id, true)!;
            Assert.That(restarted.Id, Is.GreaterThan(old.Id));
            Assert.That(restarted.CompletesAt, Is.EqualTo(Seconds(11)));
            Assert.That(run.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, old.Id, old.CompletesAt) }), Is.Null);
        }

        [TestCase(11)]
        [TestCase(18)]
        public void LiveReconnectExpiresAtOriginalDeadlineAndFinalizesOnce(int resumeAt)
        {
            Save();
            RunId id = RunId.New();
            RunState run = Start(id);
            var relay = CreateRelay(run);
            int finalized = 0;
            run.RunResultFinalized += _ => finalized++;

            now = Seconds(6);
            relay.SetConnectionAvailable(false);
            now = Seconds(resumeAt);
            relay.SetConnectionAvailable(true);
            RunRecoveryResult replay = relay.Reconnect(id);
            Assert.That(replay.Status, Is.EqualTo(RunRecoveryStatus.Finalized));
            Assert.That(replay.Result!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(replay.Result.EffectiveAt, Is.EqualTo(Seconds(11)));
            Assert.That(finalized, Is.EqualTo(1));
            Assert.That(relay.IsGameplayBlocked, Is.True);
            Assert.That(store.Load()!.Settlements, Is.Empty);
        }

        [TestCase(6, false, RunEndReason.Extracted)]
        [TestCase(5, true, RunEndReason.Death)]
        [TestCase(5, false, null)]
        public void InterruptionUsesExistingEffectiveTimeAndTieOrder(int interruptionAt, bool lethal, RunEndReason? expected)
        {
            Save();
            RunId id = RunId.New();
            RunState run = Start(id);
            var relay = CreateRelay(run);
            now = Seconds(2);
            Assert.That(run.TryBeginExtraction(id, true), Is.Not.Null);
            if (lethal)
                frame = new[] { RunSignal.PlayerDied(id, Seconds(5)) };

            now = Seconds(interruptionAt);
            // Run the quit callback without terminating the Unity test runner.
            typeof(RunLifecycleRelay).GetMethod("OnApplicationQuit", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(relay, null);

            Assert.That(run.Result?.Reason, Is.EqualTo(expected));
            if (expected.HasValue)
                Assert.That(run.Result!.EffectiveAt, Is.EqualTo(Seconds(5)));
            Assert.That(run.ActiveExtraction, Is.Null);
            Assert.That(relay.IsGameplayBlocked, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnverifiedOrReversedClockLeavesRunAndAccountPending(bool regressed)
        {
            RunId id = RunId.New();
            WeaponInstance carried = EquippedSword();
            RunState run = new RunState(() => now);
            var town = new InventoryService(new[] { carried });
            town.TryStartSortie(id.Value, () => run.TryStartRun(id, 1, Seconds(10)));
            Save(town.Snapshot());
            byte[] before = File.ReadAllBytes(path);
            var relay = CreateRelay(run);
            now = Seconds(5);
            Assert.That(run.TryBeginExtraction(id, true), Is.Not.Null);

            now = Seconds(regressed ? 4 : 6);
            clockVerified = regressed;
            relay.SetConnectionAvailable(false);
            now = Seconds(18);
            clockVerified = true;
            relay.SetConnectionAvailable(true);

            Assert.That(relay.Reconnect(id).Status, Is.EqualTo(RunRecoveryStatus.RecoveryPending));
            Assert.That(relay.IsGameplayBlocked, Is.True);
            Assert.That(run.Result, Is.Null);
            Assert.That(store.Load()!.Weapons[0].Location.ReservedRunId, Is.EqualTo(id.Value));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void ReconnectWithoutObservedInterruptionDoesNotCreditUnfinishedChannel()
        {
            Save();
            RunId id = RunId.New();
            RunState run = Start(id);
            var relay = CreateRelay(run);
            now = Seconds(3);
            Assert.That(run.TryBeginExtraction(id, true), Is.Not.Null);
            now = Seconds(8);

            Assert.That(relay.Reconnect(id).Status, Is.EqualTo(RunRecoveryStatus.RecoveryPending));
            Assert.That(run.Result, Is.Null);
            Assert.That(relay.IsGameplayBlocked, Is.True);
        }

        [TestCase(8, RunRecoveryStatus.Resumed)]
        [TestCase(11, RunRecoveryStatus.Finalized)]
        [TestCase(18, RunRecoveryStatus.Finalized)]
        public void ColdReconnectUsesOriginalTimelineWithoutRestoringThePreviousAttempt(int resumeAt, RunRecoveryStatus status)
        {
            Save();
            RunId id = RunId.New();
            RunState source = Start(id);
            now = Seconds(4);
            ExtractionAttempt old = source.TryBeginExtraction(id, true)!;
            RunCheckpoint timeline = source.GetRecoveryCheckpoint()!.Value;
            now = Seconds(resumeAt);
            RunState fresh = new RunState(() => now);
            RunLifecycleRelay relay = CreateRelay(fresh);

            RunRecoveryResult recovered = relay.Reconnect(id, new VerifiedRunRecoveryCheckpoint(timeline, 1, now));

            Assert.That(recovered.Status, Is.EqualTo(status));
            Assert.That(fresh.CurrentRun!.Id, Is.EqualTo(id));
            Assert.That(fresh.CurrentRun.ZoneId, Is.EqualTo(1));
            Assert.That(fresh.CurrentRun.StartedAt, Is.EqualTo(Seconds(1)));
            Assert.That(fresh.CurrentRun.Deadline, Is.EqualTo(Seconds(11)));
            Assert.That(fresh.ActiveExtraction, Is.Null);
            if (status == RunRecoveryStatus.Resumed)
            {
                ExtractionAttempt restarted = fresh.TryBeginExtraction(id, true)!;
                Assert.That(restarted.Id, Is.GreaterThan(old.Id));
                Assert.That(restarted.CompletesAt, Is.EqualTo(now + Seconds(3)));
            }
            else
            {
                Assert.That(recovered.Result!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
                Assert.That(recovered.Result.EffectiveAt, Is.EqualTo(Seconds(11)));
            }
        }

        [Test]
        public void VerifiedColdCheckpointPreservesEarlierFinalizedResultWithoutReplay()
        {
            Save();
            RunId id = RunId.New();
            RunState source = Start(id);
            now = Seconds(2);
            var attempt = source.TryBeginExtraction(id, true)!;
            now = attempt.CompletesAt;
            source.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, now) });
            RunCheckpoint timeline = source.GetRecoveryCheckpoint()!.Value;
            now = Seconds(20);
            var fresh = new RunState(() => now);
            var relay = CreateRelay(fresh);
            int finalized = 0;
            fresh.RunResultFinalized += _ => finalized++;

            RunRecoveryResult restored = relay.Reconnect(id, new VerifiedRunRecoveryCheckpoint(timeline, 1, now));
            Assert.That(restored.Status, Is.EqualTo(RunRecoveryStatus.Finalized));
            Assert.That(restored.Result!.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(restored.Result.EffectiveAt, Is.EqualTo(attempt.CompletesAt));
            Assert.That(relay.Reconnect(id).Result, Is.SameAs(restored.Result));
            Assert.That(finalized, Is.EqualTo(1));
        }

        [TestCase("missing-checkpoint")]
        [TestCase("invalid-run")]
        [TestCase("wrong-run")]
        [TestCase("wrong-revision")]
        [TestCase("clock-before-checkpoint")]
        [TestCase("clock-unverified")]
        [TestCase("invalid-terminal-time")]
        public void UnverifiableColdInputNeverCreatesResultOrChangesAccount(string fault)
        {
            Save();
            RunId id = RunId.New();
            RunCheckpoint timeline = Start(id).GetRecoveryCheckpoint()!.Value;
            now = Seconds(19);
            var fresh = new RunState(() => now);
            var relay = CreateRelay(fresh);
            clockVerified = fault != "clock-unverified";
            if (fault == "invalid-terminal-time")
                timeline = new RunCheckpoint(id, 1, Seconds(1), Seconds(11), Seconds(12), 1,
                    RunEndReason.Extracted, Seconds(11));

            VerifiedRunRecoveryCheckpoint? checkpoint = fault == "missing-checkpoint" ? null
                : new VerifiedRunRecoveryCheckpoint(timeline, fault == "wrong-revision" ? 2 : 1,
                    fault == "clock-before-checkpoint" ? TimeSpan.Zero : now);
            byte[] before = File.ReadAllBytes(path);

            RunId requestedRun = fault switch
            {
                "invalid-run" => default,
                "wrong-run" => RunId.New(),
                _ => id
            };
            RunRecoveryResult result = relay.Reconnect(requestedRun, checkpoint);
            Assert.That(result.Status, Is.EqualTo(RunRecoveryStatus.RecoveryPending));
            Assert.That(fresh.CurrentRun, Is.Null);
            Assert.That(fresh.Result, Is.Null);
            Assert.That(relay.IsGameplayBlocked, Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void ReconnectUsesRealCommittedReceiptWithoutRecomputingRewardsOrRewritingSave()
        {
            RunId id = RunId.New();
            WeaponInstance carried = EquippedSword();
            var owner = new RunState(() => now);
            var town = new InventoryService(new[] { carried });
            SortieManifest manifest = town.TryStartSortie(id.Value, () => owner.TryStartRun(id, 1, Seconds(10))).Manifest!;
            Save(town.Snapshot());
            ExtractionAttempt attempt = owner.TryBeginExtraction(id, true)!;
            now = attempt.CompletesAt;
            new ExtractionChannel(owner).ProcessFrame(Array.Empty<RunSignal>());
            var coins = new RunCoinAccumulator(id);
            coins.AddReward(9);
            RunSettlementCommit real = new RunSettlementCoordinator(store).TrySettle(owner.Result!, manifest, new[] { carried }, coins);
            Assert.That(real.Status, Is.EqualTo(RunSettlementStatus.Committed));
            byte[] before = File.ReadAllBytes(path);
            clockVerified = false;
            var fresh = new RunState(() => now);
            var relay = CreateRelay(fresh);

            for (int retry = 0; retry < 10; retry++)
            {
                RunRecoveryResult recovered = relay.Reconnect(id);
                Assert.That(recovered.Status, Is.EqualTo(RunRecoveryStatus.Settled));
                Assert.That(recovered.Receipt!.TransactionId, Is.EqualTo(real.Receipt!.TransactionId));
                Assert.That(recovered.Receipt.Result, Is.EqualTo(real.Receipt.Result));
                Assert.That(recovered.Save!.Snapshot!.ConfirmedCoins, Is.EqualTo(59));
                Assert.That(recovered.Save.Snapshot.Revision, Is.EqualTo(2));
            }
            Assert.That(fresh.CurrentRun, Is.Null);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [TestCase("Death", RunRecoveryStatus.Settled)]
        [TestCase("opaque-v0-outcome", RunRecoveryStatus.RecoveryPending)]
        public void LegacyReceiptIsPreservedAndAnUnknownOutcomeDoesNotSettle(string outcome, RunRecoveryStatus status)
        {
            RunId id = RunId.New();
            Save(receipt: new SettlementSaveRecord(id.Value, "legacy-transaction", outcome));
            var relay = CreateRelay(new RunState(() => now));
            byte[] before = File.ReadAllBytes(path);

            RunRecoveryResult recovered = relay.Reconnect(id);
            Assert.That(recovered.Status, Is.EqualTo(status));
            Assert.That(recovered.Receipt!.Result, Is.EqualTo(outcome));
            Assert.That(recovered.Receipt.TransactionId, Is.EqualTo("legacy-transaction"));
            Assert.That(relay.IsGameplayBlocked, Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void MissingOrBackupOnlyAccountCannotSettleOrRestore()
        {
            RunId id = RunId.New();
            RunCheckpoint timeline = Start(id).GetRecoveryCheckpoint()!.Value;
            var relay = CreateRelay(new RunState(() => now));
            var checkpoint = new VerifiedRunRecoveryCheckpoint(timeline, 1, now);
            Assert.That(relay.Reconnect(id, checkpoint).Save!.Status, Is.EqualTo(SnapshotLoadStatus.Missing));
            Assert.That(File.Exists(path), Is.False);

            Save(receipt: new SettlementSaveRecord(id.Value, "backup-only", "Death"));
            Assert.That(store.Commit(1, new SaveSnapshot(2, 50, Array.Empty<WeaponInstance>(),
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>())).Status,
                Is.EqualTo(SnapshotCommitStatus.Committed));
            File.WriteAllText(path, "{");
            byte[] backup = File.ReadAllBytes(path + ".bak");

            RunRecoveryResult recovered = relay.Reconnect(id, checkpoint);
            Assert.That(recovered.Status, Is.EqualTo(RunRecoveryStatus.RecoveryPending));
            Assert.That(recovered.Save!.Status, Is.EqualTo(SnapshotLoadStatus.RecoveredFromBackup));
            Assert.That(recovered.Receipt, Is.Null);
            Assert.That(File.ReadAllText(path), Is.EqualTo("{"));
            Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(backup));
        }

        private RunState Start(RunId id)
        {
            var run = new RunState(() => now);
            Assert.That(run.TryStartRun(id, 1, Seconds(10)), Is.True);
            return run;
        }

        private static WeaponInstance EquippedSword()
            => WeaponInstance.Create(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));

        private RunLifecycleRelay CreateRelay(RunState run)
        {
            var gameObject = new GameObject("Run Lifecycle Test");
            relayObjects.Add(gameObject);
            var relay = gameObject.AddComponent<RunLifecycleRelay>();
            relay.Initialize(new RunRecoveryCoordinator(run, new ExtractionChannel(run), store, id => id.Equals(Sword)),
                () => frame, () => clockVerified);
            return relay;
        }

        private void Save(IEnumerable<WeaponInstance>? weapons = null, SettlementSaveRecord? receipt = null)
        {
            var snapshot = new SaveSnapshot(1, 50, weapons ?? Array.Empty<WeaponInstance>(), Array.Empty<CatalogSaveRecord>(),
                receipt == null ? Array.Empty<SettlementSaveRecord>() : new[] { receipt });
            Assert.That(store.Commit(0, snapshot).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
        }

        private static TimeSpan Seconds(int value) => TimeSpan.FromSeconds(value);
    }
}
