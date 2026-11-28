#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.DailyMissions.Editor;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.DailyMissions.Tests
{
    public sealed class DailyReturnMissionTests
    {
        private static readonly DateTimeOffset BeforeUtcRollover =
            new DateTimeOffset(2026, 11, 28, 8, 59, 59, TimeSpan.FromHours(9));
        private static readonly DateTimeOffset AfterUtcRollover = BeforeUtcRollover.AddSeconds(1);
        private const string Player = "verified-player-a";
        private string directory = null!;
        private string path = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN112-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "progress.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (!Directory.Exists(directory))
                return;
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [Test]
        public void LateDeliveryUsesOriginalUtcDayAndReconnectionRemembersBothDays()
        {
            var old = Settle(RunEndReason.Extracted);
            var next = Settle(RunEndReason.Extracted);

            Assert.That(Mission().Record(Player, next.Save, next.Run, AfterUtcRollover).Status,
                Is.EqualTo(DailyReturnStatus.Completed));
            DailyReturnResult late = Mission().Record(Player, old.Save, old.Run, BeforeUtcRollover);
            Assert.That(late.Status, Is.EqualTo(DailyReturnStatus.Completed));
            Assert.That(late.UtcDay, Is.EqualTo(BeforeUtcRollover.UtcDateTime.Date));
            Assert.That(Mission().Read(Player, BeforeUtcRollover), Is.EqualTo(old.Run));
            Assert.That(Mission().Read(Player, AfterUtcRollover), Is.EqualTo(next.Run));
            Assert.That(Mission().Read("verified-player-b", AfterUtcRollover), Is.Null);
        }

        [Test]
        public void ACompletedDayCreditsOnceAndAnAlreadySeenRunCannotMoveToAnotherDay()
        {
            var initial = Settle(RunEndReason.Extracted);
            var laterSameDay = Settle(RunEndReason.Extracted);
            DailyReturnMission first = Mission();
            Assert.That(first.Record(Player, initial.Save, initial.Run, BeforeUtcRollover).Status,
                Is.EqualTo(DailyReturnStatus.Completed));
            DailyReturnResult second = first.Record(Player, laterSameDay.Save, laterSameDay.Run, BeforeUtcRollover);
            Assert.That(second.Status, Is.EqualTo(DailyReturnStatus.AlreadyCompleted));
            Assert.That(second.CreditRun, Is.EqualTo(initial.Run));

            byte[] committed = File.ReadAllBytes(path);
            for (int retry = 0; retry < 10; retry++)
            {
                DailyReturnResult reopened = Mission().Record(Player, laterSameDay.Save, laterSameDay.Run, AfterUtcRollover);
                Assert.That(reopened.Status, Is.EqualTo(DailyReturnStatus.RunAlreadyRecorded));
                Assert.That(reopened.UtcDay, Is.EqualTo(BeforeUtcRollover.UtcDateTime.Date));
                Assert.That(reopened.CreditRun, Is.EqualTo(initial.Run));
            }
            Assert.That(Mission().Read(Player, AfterUtcRollover), Is.Null);
            CollectionAssert.AreEqual(committed, File.ReadAllBytes(path));
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.TimeExpired)]
        public void FailedSettlementPreservesExistingProgressAndNeverCompletesAnotherDay(RunEndReason reason)
        {
            var success = Settle(RunEndReason.Extracted);
            var failed = Settle(reason);
            Mission().Record(Player, success.Save, success.Run, BeforeUtcRollover);
            byte[] committed = File.ReadAllBytes(path);

            Assert.That(Mission().Record(Player, failed.Save, failed.Run, AfterUtcRollover).Status,
                Is.EqualTo(DailyReturnStatus.Unsuccessful));

            CollectionAssert.AreEqual(committed, File.ReadAllBytes(path));
            Assert.That(Mission().Read(Player, BeforeUtcRollover), Is.EqualTo(success.Run));
            Assert.That(Mission().Read(Player, AfterUtcRollover), Is.Null);
        }

        [Test]
        public void MissingAndUnparseableCommittedReceiptCannotChangeProgress()
        {
            var success = Settle(RunEndReason.Extracted);
            var unparseable = new SaveSnapshot(10, 50, Array.Empty<WeaponInstance>(), Array.Empty<CatalogSaveRecord>(),
                new[] { new SettlementSaveRecord(success.Run, "RunSettlement:" + success.Run, "unknown") });

            Assert.That(Mission().Record(Player, success.Save, Guid.NewGuid(), BeforeUtcRollover).Status,
                Is.EqualTo(DailyReturnStatus.Unconfirmed));
            Assert.That(Mission().Record(Player, unparseable, success.Run, BeforeUtcRollover).Status,
                Is.EqualTo(DailyReturnStatus.Unconfirmed));
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void CompetingWritersCreditOneReturnAndKeepBothEventsForReconnect()
        {
            var first = Settle(RunEndReason.Extracted);
            var second = Settle(RunEndReason.Extracted);
            Task<DailyReturnResult> a = Task.Run(() => Mission().Record(Player, first.Save, first.Run, BeforeUtcRollover));
            Task<DailyReturnResult> b = Task.Run(() => Mission().Record(Player, second.Save, second.Run, BeforeUtcRollover));

            Assert.That(Task.WaitAll(new Task[] { a, b }, TimeSpan.FromSeconds(15)), Is.True);
            Assert.That(new[] { a.Result.Status, b.Result.Status }, Is.EquivalentTo(new[]
                { DailyReturnStatus.Completed, DailyReturnStatus.AlreadyCompleted }));
            Assert.That(a.Result.CreditRun, Is.EqualTo(b.Result.CreditRun));
            Assert.That(Mission().Read(Player, BeforeUtcRollover), Is.EqualTo(a.Result.CreditRun));
            Assert.That(Mission().Record(Player, first.Save, first.Run, BeforeUtcRollover).Status,
                Is.EqualTo(DailyReturnStatus.RunAlreadyRecorded));
            Assert.That(Mission().Record(Player, second.Save, second.Run, BeforeUtcRollover).Status,
                Is.EqualTo(DailyReturnStatus.RunAlreadyRecorded));
        }

        [Test]
        public void InvalidDirectStoreInputsCannotCorruptAnExistingProgressFile()
        {
            var completed = Settle(RunEndReason.Extracted);
            Mission().Record(Player, completed.Save, completed.Run, BeforeUtcRollover);
            byte[] original = File.ReadAllBytes(path);
            var store = new LocalDailyMissionProgressStore(path);
            DateTime day = BeforeUtcRollover.UtcDateTime.Date;

            Assert.That(() => store.RecordDailyReturn(" ", Guid.NewGuid(), day), Throws.ArgumentException);
            Assert.That(() => store.RecordDailyReturn(Player, Guid.Empty, day), Throws.ArgumentException);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.That(Mission().Read(Player, BeforeUtcRollover), Is.EqualTo(completed.Run));
        }

        private DailyReturnMission Mission() => new DailyReturnMission(new LocalDailyMissionProgressStore(path));

        private (SaveSnapshot Save, Guid Run) Settle(RunEndReason reason)
        {
            RunId id = RunId.New();
            TimeSpan clock = TimeSpan.Zero;
            var run = new RunState(() => clock);
            var owned = WeaponInstance.Create(new WeaponDefinitionId("old_sword"), WeaponProvenance.Personal,
                WeaponLocation.Equipped(0));
            var town = new InventoryService(new[] { owned });
            var sortie = town.TryStartSortie(id.Value, () => run.TryStartRun(id, 1, TimeSpan.FromSeconds(10)));
            Assert.That(sortie.Status, Is.EqualTo(SortieStartStatus.Started));
            RunResult? result;
            if (reason == RunEndReason.Extracted)
            {
                ExtractionAttempt attempt = run.TryBeginExtraction(id, true)!;
                clock = attempt.CompletesAt;
                result = run.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, clock) });
            }
            else if (reason == RunEndReason.Death)
                result = run.ProcessSignals(new[] { RunSignal.PlayerDied(id, clock) });
            else
            {
                clock = run.CurrentRun!.Deadline;
                result = run.ProcessSignals(Array.Empty<RunSignal>());
            }
            Assert.That(result?.Reason, Is.EqualTo(reason));

            var store = new AtomicSnapshotStore(Path.Combine(directory, id.Value.ToString("N") + ".json"));
            Assert.That(store.Commit(0, new SaveSnapshot(1, 50, town.Snapshot(), Array.Empty<CatalogSaveRecord>(),
                Array.Empty<SettlementSaveRecord>())).Status, Is.EqualTo(SnapshotCommitStatus.Committed));
            var settlement = new RunSettlementCoordinator(store).TrySettle(result!, sortie.Manifest!, new[] { owned },
                new RunCoinAccumulator(id));
            Assert.That(settlement.Status, Is.EqualTo(RunSettlementStatus.Committed));
            return (settlement.Snapshot!, id.Value);
        }
    }
}
