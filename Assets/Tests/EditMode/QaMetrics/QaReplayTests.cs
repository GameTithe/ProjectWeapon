#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;
using UnityEditor;

namespace ProjectWeapon.QaMetrics.Tests
{
    public sealed class QaReplayTests
    {
        private string directory = null!;
        private string path = null!;
        private Guid scope;

        [SetUp]
        public void Prepare()
        {
            scope = Guid.NewGuid();
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN50-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "events.ndjson");
        }

        [TearDown]
        public void Cleanup()
        {
            if (!Directory.Exists(directory))
                return;
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [Test]
        public void ActualFileReloadReproducesAllSevenMetrics()
        {
            RunId a = RunId.New(), b = RunId.New(), c = RunId.New();
            RunId pending = RunId.New(), d = RunId.New(), immature = RunId.New();
            var pendingAd = Guid.NewGuid();
            var interrupted = Guid.NewGuid();
            Guid cancelled = Guid.NewGuid(), fillFailed = Guid.NewGuid();
            var award = QaMetricEvent.WalletCommitted(scope, a.Value, S(21), 20, QaWalletTransaction.Settlement);
            var ledger = new List<QaMetricEvent>
            {
                Start(a, 10, 3), Loot(a, 12), Extract(a, 1, true, 15), Extract(a, 1, false, 16),
                End(a, 20, RunEndReason.Extracted), Settle(a, 21, RunEndReason.Extracted), award,
                QaMetricEvent.CatalogCommitted(scope, a.Value, S(21), new[] { "alpha", "prior" }),
                QaMetricEvent.CatalogCommitted(scope, Guid.NewGuid(), S(9), new[] { "prior" }),
                Start(b, 22, 1), Loot(b, 24), Extract(b, 1, true, 30),
                End(b, 35, RunEndReason.Death), Settle(b, 36, RunEndReason.Death, 1),
                Start(c, 37, 4), Loot(c, 40), Extract(c, 1, true, 45), End(c, 50, RunEndReason.Extracted),
                Settle(c, 51, RunEndReason.Extracted),
                QaMetricEvent.CatalogCommitted(scope, c.Value, S(51), new[] { "beta" }),
                QaMetricEvent.WalletCommitted(scope, Guid.NewGuid(), S(52), -12, QaWalletTransaction.CoinDraw),
                Start(pending, 60, 2), End(pending, 70, RunEndReason.Death),
                Start(d, 76, 0), Loot(d, 80), End(d, 90, RunEndReason.TimeExpired), Settle(d, 91, RunEndReason.TimeExpired, 1),
                Start(immature, 99, 1), Loot(immature, 99.5),
                QaMetricEvent.RunStarted(Guid.NewGuid(), RunId.New(), S(92), 5),
                QaMetricEvent.AdAttemptStarted(scope, pendingAd, S(56)),
                QaMetricEvent.AdAttemptStarted(scope, interrupted, S(42)),
                QaMetricEvent.AdAttemptEnded(scope, interrupted, S(43), QaAdEnd.Interrupted),
                QaMetricEvent.AdAttemptEnded(scope, cancelled, S(55), QaAdEnd.CancelledBeforeDisplay),
                QaMetricEvent.AdAttemptEnded(scope, fillFailed, S(56), QaAdEnd.FillFailed),
                QaMetricEvent.WalletCommitted(scope, Guid.NewGuid(), S(100), 99, QaWalletTransaction.OtherConfirmed)
            };
            for (int i = 0; i < 3; i++)
            {
                var ad = Guid.NewGuid();
                ledger.Add(QaMetricEvent.AdAttemptStarted(scope, ad, S(45 + 3 * i)));
                ledger.Add(QaMetricEvent.AdCompletionVerified(scope, ad, S(46 + 3 * i)));
                ledger.Add(QaMetricEvent.AdRewardCommitted(scope, Guid.NewGuid(), ad, S(47 + 3 * i)));
            }

            QaEventLog.Append(path, ledger.Reverse<QaMetricEvent>());
            QaEventLog.Append(path, new[] { award });
            QaReplayReport report = QaEventLog.Replay(path, Window(10, 100, 100))!;
            Expect(report.ExtractionSuccess, 2, 4, .5m);
            Assert.That(report.PendingSettlements, Is.EqualTo(1));
            Expect(report.PostLootExtraction!.Value, 2, 4, .5m);
            Expect(report.PostLootCancellation!.Value, 1, 2, .5m);
            Expect(report.HighGradeSorties!.Value, 2, 6, 1m / 3);
            Expect(report.RedeployAfterLoss!.Value, 1, 2, .5m);
            Assert.That(report.CoinsEarned, Is.EqualTo(20));
            Assert.That(report.CoinsSpent, Is.EqualTo(12));
            Assert.That(report.CoinNet, Is.EqualTo(8));
            Expect(report.AdCompletion, 3, 4, .75m);
            Assert.That(report.PendingAdAttempts, Is.EqualTo(1));
            Assert.That(report.AdCancelledBeforeDisplay, Is.EqualTo(1));
            Assert.That(report.AdFillFailures, Is.EqualTo(1));
            Assert.That(report.CatalogRecovery.Definitions, Is.EqualTo(2));
            Assert.That(report.CatalogRecovery.PlayedSeconds, Is.EqualTo(60));
            Assert.That(report.CatalogRecovery.PerHour, Is.EqualTo(120));
            Assert.That(report.DuplicateDeliveries, Is.EqualTo(1));
            Assert.That(report.ConflictingKeys, Is.Zero);
        }

        [Test]
        public void NamespaceAndConflictingPayloadsAreIndependentOfFileOrder()
        {
            RunId a = RunId.New();
            Guid lootChange = Guid.NewGuid();
            QaMetricEvent duplicate = QaMetricEvent.WalletCommitted(scope, a.Value, S(3), 22, QaWalletTransaction.Settlement);
            var ledger = new[]
            {
                Start(a, 1, 3), End(a, 2, RunEndReason.Extracted), Settle(a, 3, RunEndReason.Extracted),
                duplicate, QaMetricEvent.WalletCommitted(scope, a.Value, S(3), 23, QaWalletTransaction.Settlement), duplicate,
                QaMetricEvent.CatalogCommitted(scope, a.Value, S(3), new[] { "a" }),
                QaMetricEvent.RunLootChanged(scope, a, lootChange, Guid.NewGuid(), S(1.5)),
                QaMetricEvent.RunLootChanged(scope, a, lootChange, Guid.NewGuid(), S(1.5))
            };
            QaEventLog.Append(path, ledger);
            string reversePath = Path.Combine(directory, "reverse.ndjson");
            QaEventLog.Append(reversePath, ledger.Reverse());
            foreach (string source in new[] { path, reversePath })
            {
                QaReplayReport report = QaEventLog.Replay(source, Window(0, 10, 10))!;
                Assert.That(report.ConflictingKeys, Is.EqualTo(2));
                Assert.That(report.DuplicateDeliveries, Is.EqualTo(1));
                Assert.That(report.CoinsEarned, Is.Zero);
                Assert.That(report.CatalogRecovery.Definitions, Is.EqualTo(1));
                Expect(report.ExtractionSuccess, 1, 1, 1m);
                Expect(report.PostLootExtraction!.Value, 0, 0, null);
            }
        }

        [Test]
        public void DelayedDeliveryAndExclusiveObservationCutoffDoNotInventAnEarlyOutcome()
        {
            RunId run = RunId.New(), loss = RunId.New(), next = RunId.New();
            QaEventLog.Append(path, new[]
            {
                Start(run, 0, 1), Loot(run, 1), Extract(run, 1, true, 6),
                Start(loss, 0, 1), End(loss, 1, RunEndReason.Death), Settle(loss, 1, RunEndReason.Death, 1),
                Start(next, 6, 1)
            });
            QaReplayReport immature = QaEventLog.Replay(path, Window(0, 5, 6))!;
            Expect(immature.PostLootExtraction!.Value, 0, 0, null);
            Expect(immature.RedeployAfterLoss!.Value, 0, 0, null);

            QaReplayReport observed = QaEventLog.Replay(path, Window(0, 5, 7))!;
            Expect(observed.PostLootExtraction!.Value, 1, 1, 1m);
            Expect(observed.RedeployAfterLoss!.Value, 1, 1, 1m);
            Assert.That(observed.HighGradeSorties!.Value.Denominator, Is.EqualTo(2));

            QaEventLog.Append(path, new[] { End(run, 4, RunEndReason.Death) });
            QaReplayReport delayed = QaEventLog.Replay(path, Window(0, 5, 7))!;
            Expect(delayed.PostLootExtraction!.Value, 0, 1, 0m);
            Assert.That(delayed.PendingSettlements, Is.EqualTo(1));
        }

        [Test]
        public void MissingFileEmptyPeriodAndUnsignedSettingsRemainDistinct()
        {
            var noPolicy = new QaReplayWindow(scope, QaMetricEvent.ContractVersion, S(0), S(100), S(100));
            Assert.That(QaEventLog.Replay(path, noPolicy), Is.Null);

            QaEventLog.Append(path, Array.Empty<QaMetricEvent>());
            QaReplayReport report = QaEventLog.Replay(path, noPolicy)!;
            Expect(report.ExtractionSuccess, 0, 0, null);
            Expect(report.AdCompletion, 0, 0, null);
            Assert.That(report.CatalogRecovery.PerHour, Is.Null);
            Assert.That(report.CoinsEarned, Is.Zero);
            Assert.That(report.CoinsSpent, Is.Zero);
            Assert.That(report.PostLootExtraction, Is.Null);
            Assert.That(report.HighGradeSorties, Is.Null);
            Assert.That(report.RedeployAfterLoss, Is.Null);

            QaReplayReport configured = QaEventLog.Replay(path, Window(0, 100, 100))!;
            Expect(configured.HighGradeSorties!.Value, 0, 0, null);
            Expect(configured.RedeployAfterLoss!.Value, 0, 0, null);
        }

        [Test]
        public void OrphanOrPrematureSettlementCannotInventACompletedRun()
        {
            RunId known = RunId.New();
            QaEventLog.Append(path, new[]
            {
                Start(known, 1, 0), End(known, 5, RunEndReason.Death),
                Settle(known, 4, RunEndReason.Extracted), Settle(RunId.New(), 6, RunEndReason.Extracted)
            });
            QaReplayReport report = QaEventLog.Replay(path, Window(0, 10, 10))!;
            Expect(report.ExtractionSuccess, 0, 0, null);
            Assert.That(report.PendingSettlements, Is.EqualTo(1));
        }

        [Test]
        public void LoanAndExcludedTrialCannotInflateTheHighGradeMetric()
        {
            var inventory = new InventoryService(Array.Empty<WeaponInstance>());
            var id = RunId.New();
            var run = new RunState(() => S(0));
            inventory.PrepareStarterLoan(id.Value);
            SortieManifest manifest = inventory.TryStartSortie(id.Value, () => run.TryStartRun(id, 1, S(10))).Manifest!;
            var capture = new QaRunCapture(scope, run.CurrentRun!, manifest,
                new Dictionary<WeaponDefinitionId, WeaponDefinition>());
            QaEventLog.Append(path, new[] { capture.Started, QaMetricEvent.RunStarted(Guid.NewGuid(), RunId.New(), S(1), 5) });
            var options = new QaReplayWindow(scope, QaMetricEvent.ContractVersion, S(0), S(10), S(10), highGrade: 1);
            Expect(QaEventLog.Replay(path, options)!.HighGradeSorties!.Value, 0, 1, 0m);
        }

        [Test]
        public void VerifiedWithoutDurableCommitOrConflictingRewardNeverCountsAsCompleted()
        {
            Guid ad = Guid.NewGuid(), contradicts = Guid.NewGuid(), other = Guid.NewGuid(), transaction = Guid.NewGuid();
            var ledger = new[]
            {
                QaMetricEvent.AdAttemptStarted(scope, ad, S(1)),
                QaMetricEvent.AdCompletionVerified(scope, ad, S(2)),
                QaMetricEvent.AdRewardCommitted(scope, transaction, ad, S(3)),
                QaMetricEvent.AdRewardCommitted(scope, transaction, other, S(3)),
                QaMetricEvent.AdAttemptStarted(scope, contradicts, S(1)),
                QaMetricEvent.AdCompletionVerified(scope, contradicts, S(2)),
                QaMetricEvent.AdRewardCommitted(scope, Guid.NewGuid(), contradicts, S(3)),
                QaMetricEvent.AdAttemptEnded(scope, contradicts, S(3), QaAdEnd.VerificationFailed)
            };
            QaEventLog.Append(path, ledger);
            QaReplayReport report = QaEventLog.Replay(path, Window(0, 10, 10))!;
            Expect(report.AdCompletion, 0, 0, null);
            Assert.That(report.PendingAdAttempts, Is.EqualTo(1));
            Assert.That(report.InconsistentAdAttempts, Is.EqualTo(1));
            Assert.That(report.ConflictingKeys, Is.EqualTo(1));
        }

        [Test]
        public void SyntheticRunAdapterReadsRealReceiptAndNeverRecordsItsPendingWallet()
        {
            var owned = WeaponInstance.Create(new WeaponDefinitionId("old_sword"), WeaponProvenance.Personal,
                WeaponLocation.Equipped(0));
            var spear = WeaponInstance.Create(new WeaponDefinitionId("wooden_spear"), WeaponProvenance.ExplorationLoot,
                WeaponLocation.Warehouse());
            var inventory = new InventoryService(new[] { owned });
            var id = RunId.New();
            TimeSpan now = S(0);
            var run = new RunState(() => now);
            SortieManifest manifest = inventory.TryStartSortie(id.Value, () => run.TryStartRun(id, 1, S(10))).Manifest!;
            var defs = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(
                "Assets/ProjectWeapon/Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset").CreateLookup();
            var capture = new QaRunCapture(scope, run.CurrentRun!, manifest, defs);
            var field = new InventoryService(manifest.Equipped);
            var pickup = field.TryPickup(spear);
            var acquired = capture.LootChanged(pickup, Guid.NewGuid(), S(1))!;
            Assert.That(capture.LootChanged(pickup, Guid.NewGuid(), S(2)), Is.Null);
            var wallet = new RunCoinAccumulator(id);
            wallet.AddReward(25);
            now = S(2);
            ExtractionAttempt attempt = run.TryBeginExtraction(id, true)!;
            var extraction = capture.ExtractionStarted(attempt);
            now = attempt.CompletesAt;
            RunResult result = run.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, now) })!;

            QaEventLog.Append(path, new[] { capture.Started, acquired, extraction, capture.Ended(result) });
            QaReplayReport pending = QaEventLog.Replay(path, Window(0, 10, 10))!;
            Assert.That(pending.CoinsEarned, Is.Zero);
            Assert.That(pending.PendingSettlements, Is.EqualTo(1));
            Assert.That(pending.CatalogRecovery.Definitions, Is.Zero);

            var store = new AtomicSnapshotStore(Path.Combine(directory, "save.json"));
            var coordinator = new RunSettlementCoordinator(store);
            Assert.That(capture.SettlementCommitted(coordinator.TrySettle(result, manifest, field.Snapshot(), wallet), S(5)),
                Is.Empty);
            store.Commit(0, new SaveSnapshot(1, 40, inventory.Snapshot(),
                Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>()));
            var committed = coordinator.TrySettle(result, manifest, field.Snapshot(), wallet);
            QaEventLog.Append(path, capture.SettlementCommitted(committed, S(5)));
            var replayed = coordinator.TrySettle(result, manifest, field.Snapshot(), wallet);
            QaEventLog.Append(path, capture.SettlementCommitted(replayed, S(5)));
            QaReplayReport report = QaEventLog.Replay(path, Window(0, 10, 10))!;
            Assert.That(report.CoinsEarned, Is.EqualTo(25));
            Assert.That(report.PendingSettlements, Is.Zero);
            Assert.That(report.CatalogRecovery.Definitions, Is.EqualTo(2));
            Assert.That(report.DuplicateDeliveries, Is.EqualTo(3));
            Expect(report.ExtractionSuccess, 1, 1, 1m);
        }

        [Test]
        public void UnsupportedOrMalformedSourcesFailWithoutEchoingTheirContent()
        {
            QaEventLog.Append(path, Array.Empty<QaMetricEvent>());
            File.AppendAllText(path, "{invalid-raw-record}");
            var error = Assert.Throws<FormatException>(() => QaEventLog.Replay(path, Window(0, 10, 10)));
            Assert.That(error!.Message, Is.EqualTo("Invalid synthetic QA event at line 1."));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QaReplayWindow(scope, 0, S(0), S(10), S(10)));
        }

        private QaReplayWindow Window(double start, double end, double observed)
            => new QaReplayWindow(scope, QaMetricEvent.ContractVersion, S(start), S(end), S(observed), S(5), 3, S(5));

        private QaMetricEvent Start(RunId run, double seconds, int grade)
            => QaMetricEvent.RunStarted(scope, run, S(seconds), grade);

        private QaMetricEvent End(RunId run, double seconds, RunEndReason reason)
            => QaMetricEvent.RunEnded(scope, run, S(seconds), reason);

        private QaMetricEvent Settle(RunId run, double seconds, RunEndReason reason, int lost = 0)
            => QaMetricEvent.SettlementCommitted(scope, run, S(seconds), reason, lost);

        private QaMetricEvent Loot(RunId run, double seconds)
            => QaMetricEvent.RunLootChanged(scope, run, Guid.NewGuid(), Guid.NewGuid(), S(seconds));

        private QaMetricEvent Extract(RunId run, int attempt, bool begin, double seconds)
            => QaMetricEvent.ExtractionChanged(scope, run, attempt, begin, S(seconds));

        private static TimeSpan S(double value) => TimeSpan.FromSeconds(value);

        private static void Expect(QaRatio ratio, int numerator, int denominator, decimal? value)
        {
            Assert.That(ratio.Numerator, Is.EqualTo(numerator));
            Assert.That(ratio.Denominator, Is.EqualTo(denominator));
            Assert.That(ratio.Value, Is.EqualTo(value));
        }
    }
}
