#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Extraction;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.RunRecovery;
using ProjectWeapon.Save;
using ProjectWeapon.Settlement;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;

namespace ProjectWeapon.SaveCrashQA.Tests
{
    public sealed class ForcedExitSaveRecoveryTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Spear = new WeaponDefinitionId("wooden_spear");
        private static readonly WeaponDefinitionId Bow = new WeaponDefinitionId("hunting_bow");
        private static readonly WeaponDefinitionId Mace = new WeaponDefinitionId("mace");
        private static readonly WeaponDefinitionId Dagger = new WeaponDefinitionId("dagger");
        private static readonly WeaponDefinitionId[] KnownDefinitions = { Sword, Spear, Bow, Mace, Dagger };
        private string directory = null!;
        private string path = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon.KAN37." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "profile.json");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string file in Directory.EnumerateFiles(directory))
                File.Delete(file);
            Directory.Delete(directory);
        }

        [TestCase(CrashStage.BeforeWrite, RunEndReason.Extracted)]
        [TestCase(CrashStage.PartialWrite, RunEndReason.Extracted)]
        [TestCase(CrashStage.BeforeReplace, RunEndReason.Extracted)]
        [TestCase(CrashStage.AfterReplace, RunEndReason.Extracted)]
        [TestCase(CrashStage.BeforeWrite, RunEndReason.Death)]
        [TestCase(CrashStage.PartialWrite, RunEndReason.Death)]
        [TestCase(CrashStage.BeforeReplace, RunEndReason.Death)]
        [TestCase(CrashStage.AfterReplace, RunEndReason.Death)]
        public void InjectedWriteInterruptionReopensAndReplaysOneSettlement(CrashStage stage, RunEndReason reason)
        {
            var fixture = new SettlementFixture(reason);
            Seed(fixture, path);
            byte[] prior = File.ReadAllBytes(path);
            byte[] expected = WriteExpected(fixture);
            var operations = new InterruptedOperations(stage);
            var interrupted = new AtomicSnapshotStore(path, operations);

            Assert.That(() => fixture.Settle(interrupted), Throws.TypeOf<IOException>()
                .With.Message.EqualTo("Injected interruption at " + stage + "."));
            Assert.That(operations.Reached, Is.True);
            if (stage == CrashStage.PartialWrite)
                Assert.That(operations.StagedBytes, Is.EqualTo(expected.Take(expected.Length / 2).ToArray()));
            if (stage == CrashStage.BeforeReplace)
                Assert.That(operations.StagedBytes, Is.EqualTo(expected));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty, "A caught exception allows store cleanup.");

            AssertReopenAndRetry(fixture, stage == CrashStage.AfterReplace, prior, expected);
        }

        [TestCase(CrashStage.BeforeWrite)]
        [TestCase(CrashStage.PartialWrite)]
        [TestCase(CrashStage.BeforeReplace)]
        [TestCase(CrashStage.AfterReplace)]
        public void TerminatedWindowsChildLeavesOnlyTheExpectedDurableRevision(CrashStage stage)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor
                || Environment.GetEnvironmentVariable("PROJECTWEAPON_KAN37_CHILD") != "1")
                Assert.Ignore("Set PROJECTWEAPON_KAN37_CHILD=1 in the Windows Editor process to run forced exit cases.");

            var fixture = new SettlementFixture(RunEndReason.Extracted);
            Seed(fixture, path);
            byte[] prior = File.ReadAllBytes(path);
            byte[] expected = WriteExpected(fixture);
            TerminateChildAt(stage);

            string orphan = path + ".crash.tmp";
            if (stage == CrashStage.PartialWrite || stage == CrashStage.BeforeReplace)
            {
                byte[] staged = stage == CrashStage.PartialWrite ? expected.Take(expected.Length / 2).ToArray() : expected;
                Assert.That(File.ReadAllBytes(orphan), Is.EqualTo(staged));
            }
            else
            {
                Assert.That(File.Exists(orphan), Is.False);
            }

            AssertReopenAndRetry(fixture, stage == CrashStage.AfterReplace, prior, expected);
        }

        [TestCase("future", SnapshotLoadStatus.UnsupportedVersion, "Unsupported snapshot format version 9000")]
        [TestCase("active", SnapshotLoadStatus.RecoveredFromBackup, "checksum does not match")]
        [TestCase("both", SnapshotLoadStatus.Corrupt, "invalid UTF-8")]
        public void ReconnectSurfacesVersionAndCorruptionDiagnosticsWithoutRewritingEitherFile(string damage,
            SnapshotLoadStatus expectedStatus, string diagnostic)
        {
            var fixture = new SettlementFixture(RunEndReason.Extracted);
            Seed(fixture, path);
            byte[] before = File.ReadAllBytes(path);
            Assert.That(fixture.Settle(new AtomicSnapshotStore(path)).Status, Is.EqualTo(RunSettlementStatus.Committed));
            string original = File.ReadAllText(path, SnapshotCodec.Utf8);
            string backup = path + AtomicSnapshotStore.BackupSuffix;
            if (damage == "future")
            {
                string unsupported = original.Replace("\"formatVersion\":1", "\"formatVersion\":9000");
                Assert.That(unsupported, Is.Not.EqualTo(original));
                File.WriteAllText(path, unsupported, SnapshotCodec.Utf8);
            }
            else if (damage == "active")
            {
                string mismatched = original.Replace("hunting_bow", "tampered_bow");
                Assert.That(mismatched, Is.Not.EqualTo(original));
                File.WriteAllText(path, mismatched, SnapshotCodec.Utf8);
            }
            else
            {
                File.WriteAllBytes(path, new byte[] { 0xC0, 0xAF });
                File.WriteAllText(backup, "{", SnapshotCodec.Utf8);
            }

            byte[] damagedActive = File.ReadAllBytes(path);
            byte[] backupBytes = File.ReadAllBytes(backup);
            RunRecoveryResult reconnect = Reconnect(fixture, out RunState cold);
            Assert.That(reconnect.Status, Is.EqualTo(RunRecoveryStatus.RecoveryPending));
            Assert.That(reconnect.Save?.Status, Is.EqualTo(expectedStatus));
            Assert.That(reconnect.Save?.ActiveIssue, Does.Contain(diagnostic));
            Assert.That(reconnect.Receipt, Is.Null);
            Assert.That(cold.CurrentRun, Is.Null);
            if (expectedStatus == SnapshotLoadStatus.RecoveredFromBackup)
                Assert.That(SnapshotCodec.Encode(reconnect.Save!.Snapshot!).bytes, Is.EqualTo(before));
            else
                Assert.That(reconnect.Save?.Snapshot, Is.Null);
            if (damage == "both")
                Assert.That(reconnect.Save?.BackupIssue, Does.Contain("JSON object is incomplete"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(damagedActive));
            Assert.That(File.ReadAllBytes(backup), Is.EqualTo(backupBytes));
        }

        private void AssertReopenAndRetry(SettlementFixture fixture, bool replaced, byte[] prior, byte[] expected)
        {
            string backup = path + AtomicSnapshotStore.BackupSuffix;
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(replaced ? expected : prior));
            if (replaced)
                Assert.That(File.ReadAllBytes(backup), Is.EqualTo(prior));
            else
                Assert.That(File.Exists(backup), Is.False);

            RunRecoveryResult reconnect = Reconnect(fixture, out RunState cold);
            Assert.That(reconnect.Save?.Status, Is.EqualTo(SnapshotLoadStatus.Healthy));
            Assert.That(reconnect.Status, Is.EqualTo(replaced ? RunRecoveryStatus.Settled : RunRecoveryStatus.RecoveryPending));
            Assert.That(cold.CurrentRun, Is.Null, "An unverified cold world never resumes.");
            if (replaced)
            {
                Assert.That(reconnect.Receipt?.RunId, Is.EqualTo(fixture.Run.RunId.Value));
                Assert.That(reconnect.Save!.Snapshot!.Revision, Is.EqualTo(2));
            }
            else
            {
                SaveSnapshot saved = reconnect.Save!.Snapshot!;
                Assert.That(reconnect.Receipt, Is.Null);
                Assert.That(saved.Revision, Is.EqualTo(1));
                Assert.That(saved.ConfirmedCoins, Is.EqualTo(100));
                Assert.That(fixture.Manifest.MatchesReservations(saved.Weapons), Is.True);
                Assert.That(saved.Weapons.All(item => item.Id != fixture.Loot.Id), Is.True);
                Assert.That(saved.Settlements.Select(receipt => receipt.RunId), Is.EqualTo(new[] { fixture.PriorRunId }));
                Assert.That(saved.CatalogMilestones, Is.Empty);
            }

            RunSettlementCommit first = fixture.Settle(new AtomicSnapshotStore(path));
            Assert.That(first.Status, Is.EqualTo(replaced ? RunSettlementStatus.AlreadyCommitted : RunSettlementStatus.Committed));
            Assert.That(first.Receipt, Is.Not.Null);
            for (int replay = 0; replay < 3; replay++)
            {
                RunSettlementCommit result = fixture.Settle(new AtomicSnapshotStore(path));
                Assert.That(result.Status, Is.EqualTo(RunSettlementStatus.AlreadyCommitted));
                Assert.That(result.Receipt!.TransactionId, Is.EqualTo(first.Receipt!.TransactionId));
                Assert.That(result.Receipt.Result, Is.EqualTo(first.Receipt.Result));
            }

            SaveSnapshot final = new AtomicSnapshotStore(path).Load()!;
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(expected));
            Assert.That(File.ReadAllBytes(backup), Is.EqualTo(prior));
            Assert.That(final.Revision, Is.EqualTo(2));
            Assert.That(final.Weapons.Select(item => item.Id), Is.EquivalentTo(fixture.Run.Succeeded
                ? new[] { fixture.InTown.Id, fixture.Packed.Id, fixture.Loot.Id }
                : new[] { fixture.InTown.Id }));
            Assert.That(final.Weapons.All(item => item.Location.Kind == WeaponLocationKind.Warehouse), Is.True);
            Assert.That(final.Settlements.Select(receipt => receipt.RunId),
                Is.EquivalentTo(new[] { fixture.PriorRunId, fixture.Run.RunId.Value }));
            Assert.That(final.CoinWeaponDraws.Single().RequestId, Is.EqualTo(fixture.DrawId));
            Assert.That(final.WeaponSales.Single().RequestId, Is.EqualTo(fixture.SaleId));
            Assert.That(final.Catalog.Count, Is.EqualTo(fixture.Run.Succeeded ? 5 : 3));
            RunSettlementDetails details = RunSettlementDetails.Read(first.Receipt!)!;
            Assert.That(details.Reason, Is.EqualTo(fixture.Run.Reason));
            Assert.That(details.AwardedCoins, Is.EqualTo(fixture.Run.Succeeded ? 35 : 0));
            Assert.That(details.ForfeitedCoins, Is.EqualTo(fixture.Run.Succeeded ? 0 : 35));
            if (fixture.Run.Succeeded)
            {
                Assert.That(final.ConfirmedCoins, Is.EqualTo(635));
                Assert.That(final.CatalogMilestones.Single().AwardedCoins, Is.EqualTo(500));
                Assert.That(final.CatalogMilestones.Single().SourceRunId, Is.EqualTo(fixture.Run.RunId.Value));
                Assert.That(details.NewWeapons.Single().InstanceId, Is.EqualTo(fixture.Loot.Id.ToString()));
                Assert.That(details.ReturnedWeapons.Single().InstanceId, Is.EqualTo(fixture.Packed.Id.ToString()));
            }
            else
            {
                Assert.That(final.ConfirmedCoins, Is.EqualTo(100));
                Assert.That(final.CatalogMilestones, Is.Empty);
                Assert.That(details.LostWeapons.Select(item => item.InstanceId),
                    Is.EquivalentTo(new[] { fixture.Packed.Id.ToString(), fixture.Abandoned.Id.ToString(), fixture.Loot.Id.ToString() }));
            }

            RunRecoveryResult settled = Reconnect(fixture, out _);
            Assert.That(settled.Status, Is.EqualTo(RunRecoveryStatus.Settled));
            Assert.That(settled.Receipt?.Result, Is.EqualTo(first.Receipt!.Result));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(expected));
        }

        private byte[] WriteExpected(SettlementFixture fixture)
        {
            string oraclePath = Path.Combine(directory, "expected.json");
            Seed(fixture, oraclePath);
            RunSettlementCommit normal = fixture.Settle(new AtomicSnapshotStore(oraclePath));
            Assert.That(normal.Status, Is.EqualTo(RunSettlementStatus.Committed));
            return File.ReadAllBytes(oraclePath);
        }

        private static void Seed(SettlementFixture fixture, string destination)
            => Assert.That(new AtomicSnapshotStore(destination).Commit(0, fixture.Initial).Status,
                Is.EqualTo(SnapshotCommitStatus.Committed));

        private RunRecoveryResult Reconnect(SettlementFixture fixture, out RunState cold)
        {
            cold = new RunState(() => TimeSpan.Zero);
            var reopened = new AtomicSnapshotStore(path);
            var recovery = new RunRecoveryCoordinator(cold, new ExtractionChannel(cold), reopened, KnownDefinitions.Contains,
                (snapshot, _) => fixture.Manifest.MatchesReservations(snapshot.Weapons));
            return recovery.Reconnect(fixture.Run.RunId, hasVerifiedContinuity: false);
        }

        private void TerminateChildAt(CrashStage stage)
        {
            string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            string scriptPath = Path.Combine(Application.dataPath, "Tests", "EditMode", "SaveCrashQA", "ChildCrashingWriter.ps1");
            string script = File.ReadAllText(scriptPath);
            string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var start = new ProcessStartInfo(powershell, "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encodedScript)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.EnvironmentVariables["PROJECTWEAPON_CRASH_DIRECTORY"] = directory;
            start.EnvironmentVariables["PROJECTWEAPON_CRASH_STAGE"] = stage.ToString();
            start.EnvironmentVariables["PROJECTWEAPON_CRASH_SAVE_ASSEMBLY"] = typeof(AtomicSnapshotStore).Assembly.Location;
            start.EnvironmentVariables["PROJECTWEAPON_CRASH_NETSTANDARD"] = Path.Combine(EditorApplication.applicationContentsPath,
                "MonoBleedingEdge", "lib", "mono", "unityjit-win32", "Facades", "netstandard.dll");

            using Process child = Process.Start(start) ?? throw new InvalidOperationException("The child writer did not start.");
            Task<string?> marker = child.StandardOutput.ReadLineAsync();
            Task<string> errors = child.StandardError.ReadToEndAsync();
            try
            {
                Assert.That(marker.Wait(TimeSpan.FromSeconds(12)), Is.True, "The child did not reach its checkpoint in time.");
                Assert.That(marker.Result, Is.EqualTo("Ready " + stage),
                    () => errors.IsCompleted ? errors.Result : "The child exited before its checkpoint.");
                Assert.That(child.HasExited, Is.False);
                child.Kill();
                Assert.That(child.WaitForExit(10000), Is.True);
                Assert.That(child.ExitCode, Is.Not.Zero);
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill();
                    child.WaitForExit(10000);
                }
            }
        }

        public enum CrashStage
        {
            BeforeWrite,
            PartialWrite,
            BeforeReplace,
            AfterReplace
        }

        private sealed class InterruptedOperations : SnapshotFileOperations
        {
            private readonly CrashStage stage;

            public InterruptedOperations(CrashStage stage) => this.stage = stage;
            public bool Reached { get; private set; }
            public byte[]? StagedBytes { get; private set; }

            internal override void WriteAndFlush(string tempPath, byte[] bytes)
            {
                if (stage == CrashStage.BeforeWrite)
                    Interrupt();
                if (stage == CrashStage.PartialWrite)
                {
                    using var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                    stream.Write(bytes, 0, bytes.Length / 2);
                    stream.Flush(flushToDisk: true);
                    stream.Position = 0;
                    using var reader = new BinaryReader(stream);
                    StagedBytes = reader.ReadBytes((int)stream.Length);
                    Interrupt();
                }

                base.WriteAndFlush(tempPath, bytes);
            }

            internal override void Activate(string tempPath, string activePath, bool hasActiveSnapshot)
            {
                if (stage == CrashStage.BeforeReplace)
                {
                    StagedBytes = File.ReadAllBytes(tempPath);
                    Interrupt();
                }

                base.Activate(tempPath, activePath, hasActiveSnapshot);
                if (stage == CrashStage.AfterReplace)
                    Interrupt();
            }

            private void Interrupt()
            {
                Reached = true;
                throw new IOException("Injected interruption at " + stage + ".");
            }
        }

        private sealed class SettlementFixture
        {
            public SettlementFixture(RunEndReason reason)
            {
                TimeSpan now = TimeSpan.Zero;
                var state = new RunState(() => now);
                RunId id = RunId.New();
                var inventory = new InventoryService(new[] { Packed, Abandoned, InTown });
                Manifest = inventory.TryStartSortie(id.Value, () => state.TryStartRun(id, 1, TimeSpan.FromSeconds(10))).Manifest!;
                Assert.That(Manifest, Is.Not.Null);
                if (reason == RunEndReason.Extracted)
                {
                    ExtractionAttempt attempt = state.TryBeginExtraction(id, isInRange: true)!;
                    now = attempt.CompletesAt;
                    state.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, now) });
                }
                else
                {
                    now = TimeSpan.FromSeconds(1);
                    state.ProcessSignals(new[] { RunSignal.PlayerDied(id, now) });
                }

                Run = state.Result!;
                Assert.That(Run.Reason, Is.EqualTo(reason));
                var prior = new SettlementSaveRecord(PriorRunId, "RunSettlement:" + PriorRunId.ToString("D"), "Death");
                var draw = new CoinWeaponDrawSaveRecord(DrawId, 10, 92, InTown.Id, Mace, 1);
                var sold = WeaponInstance.Create(Dagger, WeaponProvenance.Personal, WeaponLocation.Warehouse());
                var sale = new WeaponSaleSaveRecord(SaleId, sold.Id, Dagger, 1, 8, 100);
                var known = new[] { Bow, Mace, Dagger }.Select(definition => new CatalogSaveRecord(definition, "recovered"));
                Initial = new SaveSnapshot(1, 100, inventory.Snapshot(), known, new[] { prior }, new[] { draw }, new[] { sale });
            }

            public RunResult Run { get; }
            public SortieManifest Manifest { get; }
            public SaveSnapshot Initial { get; }
            public Guid PriorRunId { get; } = Guid.NewGuid();
            public Guid DrawId { get; } = Guid.NewGuid();
            public Guid SaleId { get; } = Guid.NewGuid();

            public WeaponInstance Packed { get; }
                = WeaponInstance.Create(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            public WeaponInstance Abandoned { get; }
                = WeaponInstance.Create(Bow, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            public WeaponInstance InTown { get; }
                = WeaponInstance.Create(Mace, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            public WeaponInstance Loot { get; }
                = WeaponInstance.Create(Spear, WeaponProvenance.ExplorationLoot, WeaponLocation.Carried(0));

            public RunSettlementCommit Settle(AtomicSnapshotStore store)
            {
                var wallet = new RunCoinAccumulator(Run.RunId);
                wallet.AddReward(35);
                return new RunSettlementCoordinator(store).TrySettle(Run, Manifest, new[] { Packed, Loot }, wallet,
                    requireExactReservations: true);
            }
        }
    }
}
