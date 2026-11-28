#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Ad;
using ProjectWeapon.Inventory;
using ProjectWeapon.WeaponDraw;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace ProjectWeapon.AdRewards.Tests
{
    public sealed class AdRewardCompletionServiceTests
    {
        private const string Scope = "test-project-development";
        private const string Account = "verified-player-a";
        private const string OtherAccount = "verified-player-b";
        private const string Token = "opaque-reservation-a";
        private const string OtherToken = "opaque-reservation-b";
        private const string Event = "provider-event-a";
        private const string OtherEvent = "provider-event-b";
        private const string Proof = "test-signed-proof";
        private const string Provider = AdRewardCompletionService.LevelPlayProviderId;
        private static readonly DateTime ReservedAt = new DateTime(2026, 11, 28, 23, 59, 0, DateTimeKind.Utc);
        private static readonly DateTime AcceptUntil = ReservedAt.AddHours(6);
        private readonly object fileLock = new object();
        private string directory = null!;
        private string path = null!;
        private ProofVerifierDouble verifier = null!;
        private WeightedWeaponDraw draw = null!;
        private AdRewardFileStore file = null!;
        private DateTime now;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN97-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "authority.bin");
            file = OpenRestartedStore();
            verifier = new ProofVerifierDouble();
            verifier.Add(Proof, Completion());
            draw = AdRewardDrawFixture.Create();
            now = ReservedAt.AddMinutes(2);
            Seed();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string entry in Directory.EnumerateFiles(directory))
                File.Delete(entry);
            Directory.Delete(directory);
        }

        [Test]
        public void AppPlayerAssemblyCannotIssueRewardsFromTheFakeCompletion()
        {
            Assert.That(CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Any(assembly => assembly.name == "ProjectWeapon.AdRewardsAuthority"), Is.False);
            var fake = new FakeRewardedAdAdapter();
            Guid request = Guid.NewGuid();
            Assert.That(fake.TryBegin(request), Is.True);
            fake.OutcomeReported += notification =>
            {
                AdRewardClaimResult claim = Service().ProcessCallback(Provider, notification.VerifiedCompletion!.Value.CompletionId);
                Assert.That(claim.Status, Is.EqualTo(AdRewardClaimStatus.UnverifiedCompletion));
            };
            Assert.That(fake.TryInjectCallback(request, RewardedAdOutcome.ConfirmedCompletion, Event), Is.True);
            Assert.That(file.Read().Grants, Is.Empty);
        }

        [Test]
        public void VerifiedCallbackCommitsOnePersonalWeaponAndOneUseForTheReservationDay()
        {
            var rolls = new Queue<int>(new[] { 5000, 0 });
            AdRewardClaimResult result = Service(random: _ => rolls.Dequeue()).ProcessCallback(Provider, Proof);

            Assert.That(result.Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            AdRewardAuthoritySnapshot saved = OpenRestartedStore().Read();
            AdRewardGrant receipt = saved.Grants.Single();
            Assert.That(saved.Revision, Is.EqualTo(1));
            Assert.That(receipt.Weapon.Id, Is.EqualTo(result.Receipt!.Weapon.Id));
            Assert.That(receipt.Weapon.DefinitionId.Value, Is.EqualTo("grade2-0"));
            Assert.That(receipt.Weapon.Provenance, Is.EqualTo(WeaponProvenance.Personal));
            Assert.That(receipt.Weapon.Location.Kind, Is.EqualTo(WeaponLocationKind.Warehouse));
            Assert.That(receipt.Completion.ProviderEventId, Is.EqualTo(Event));
            Assert.That(receipt.Completion.AccountId, Is.EqualTo(Account));
            Assert.That(saved.ConfirmedUses(Account, ReservedAt.Date), Is.EqualTo(1));
            Assert.That(saved.ConfirmedUses(Account, now.Date), Is.Zero);
            Assert.That(saved.ConfirmedUses(OtherAccount, ReservedAt.Date), Is.Zero);
            Assert.That(rolls, Is.Empty);
        }

        [TestCase("invalid-provider", AdRewardClaimStatus.InvalidProvider)]
        [TestCase("missing-proof", AdRewardClaimStatus.UnverifiedCompletion)]
        [TestCase("unverified-proof", AdRewardClaimStatus.UnverifiedCompletion)]
        [TestCase("invalid-binding", AdRewardClaimStatus.UnverifiedCompletion)]
        [TestCase("different-scope", AdRewardClaimStatus.ReservationMismatch)]
        [TestCase("different-account", AdRewardClaimStatus.ReservationMismatch)]
        [TestCase("missing-reservation", AdRewardClaimStatus.ReservationMismatch)]
        [TestCase("released-reservation", AdRewardClaimStatus.ReservationUnavailable)]
        [TestCase("exact-expiry", AdRewardClaimStatus.ExpiredReservation)]
        [TestCase("local-device-clock", AdRewardClaimStatus.RetryRequired)]
        [TestCase("clock-before-reservation", AdRewardClaimStatus.RetryRequired)]
        public void RejectedCallbacksNeverDrawOrModifyAnyRecord(string scenario, AdRewardClaimStatus expected)
        {
            verifier.Add("invalid-binding", default);
            verifier.Add("different-scope", Completion(scope: "different-app"));
            verifier.Add("different-account", Completion(account: OtherAccount));
            verifier.Add("missing-reservation", Completion(token: "unknown-token"));
            if (scenario == "released-reservation")
                Seed(acceptsCompletion: false);
            if (scenario == "exact-expiry")
                now = AcceptUntil;
            if (scenario == "local-device-clock")
                now = DateTime.SpecifyKind(now, DateTimeKind.Local);
            if (scenario == "clock-before-reservation")
                now = ReservedAt.AddSeconds(-1);

            string callback = scenario switch
            {
                "missing-proof" => string.Empty,
                "unverified-proof" or "invalid-binding" or "different-scope" or "different-account"
                    or "missing-reservation" => scenario,
                _ => Proof
            };
            byte[] before = File.ReadAllBytes(path);

            AdRewardClaimResult claim = Service(random: UnexpectedDraw).ProcessCallback(
                scenario == "invalid-provider" ? "test_adapter" : Provider, callback);
            Assert.That(claim.Status, Is.EqualTo(expected));
            Assert.That(claim.Receipt, Is.Null);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void CallbackRetriesAfterDurableCommitAndLostResponseUseOneStableReceiptEvenPastExpiry()
        {
            AdRewardClaimResult interrupted = default;
            var lostResponse = new CommitDouble(file, (revision, grant) =>
            {
                Assert.That(file.TryCommit(revision, grant), Is.True);
                throw new IOException("Connection lost after committing the reward.");
            });
            Assert.Throws<IOException>(() => interrupted = Service(lostResponse).ProcessCallback(Provider, Proof));
            Assert.That(interrupted.Receipt, Is.Null);
            AdRewardGrant saved = OpenRestartedStore().Read().Grants.Single();
            byte[] before = File.ReadAllBytes(path);
            now = AcceptUntil.AddDays(1);

            for (int retry = 0; retry < 10; retry++)
            {
                AdRewardClaimResult result = Service(OpenRestartedStore(), UnexpectedDraw).ProcessCallback(Provider, Proof);
                Assert.That(result.Status, Is.EqualTo(AdRewardClaimStatus.AlreadyGranted));
                Assert.That(result.Receipt!.Weapon.Id, Is.EqualTo(saved.Weapon.Id));
                Assert.That(result.Receipt.Completion.ProviderEventId, Is.EqualTo(saved.Completion.ProviderEventId));
            }
            Assert.That(OpenRestartedStore().Read().ConfirmedUses(Account, ReservedAt.Date), Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void WindowsChildKilledAfterCommitReplaysOneDurableWeaponUseAndReceipt()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor
                || Environment.GetEnvironmentVariable("PROJECTWEAPON_KAN97_CHILD") != "1")
                Assert.Ignore("Set PROJECTWEAPON_KAN97_CHILD=1 in the Windows Editor process to verify the real writer termination.");

            string dotnet = Path.Combine(EditorApplication.applicationContentsPath, "DotNetSdk", "dotnet.exe");
            string buildDirectory = Path.Combine(directory, "child-build");
            try
            {
                BuildChildWriter(dotnet, buildDirectory);
                byte[] initial = File.ReadAllBytes(path);
                Assert.That(OpenRestartedStore().Read().Grants, Is.Empty);
                var start = ChildProcess(dotnet, buildDirectory, @"bin\Release\net8.0\AdRewardCrashWriter.dll");
                start.EnvironmentVariables["PROJECTWEAPON_KAN97_LEDGER"] = path;
                start.EnvironmentVariables["PROJECTWEAPON_KAN97_TOKEN"] = Token;
                start.EnvironmentVariables["PROJECTWEAPON_KAN97_PROOF"] = Proof;
                start.EnvironmentVariables["PROJECTWEAPON_KAN97_EVENT"] = Event;
                using Process child = Process.Start(start) ?? throw new InvalidOperationException("The reward writer did not start.");
                Task<string?> marker = child.StandardOutput.ReadLineAsync();
                Task<string> errors = child.StandardError.ReadToEndAsync();
                WeaponInstanceId childWeapon;
                byte[] atBarrier;
                try
                {
                    Assert.That(marker.Wait(TimeSpan.FromSeconds(12)), Is.True, "The child did not reach its durable checkpoint.");
                    Assert.That(marker.Result, Does.StartWith("READY "),
                        () => errors.IsCompleted ? errors.Result : "The child exited before its durable checkpoint.");
                    Assert.That(WeaponInstanceId.TryParse(marker.Result!.Substring("READY ".Length), out childWeapon), Is.True);
                    atBarrier = File.ReadAllBytes(path);
                    Assert.That(atBarrier, Is.Not.EqualTo(initial));
                    Assert.That(child.HasExited, Is.False);
                    child.Kill();
                    Assert.That(child.WaitForExit(10000), Is.True, "The isolated reward writer did not terminate.");
                }
                finally
                {
                    if (!child.HasExited)
                    {
                        child.Kill();
                        child.WaitForExit(10000);
                    }
                }

                Assert.That(child.ExitCode, Is.Not.Zero);
                var reopened = OpenRestartedStore().Read();
                AdRewardGrant grant = reopened.Grants.Single();
                Assert.That(reopened.Revision, Is.EqualTo(1));
                Assert.That(grant.Weapon.Id, Is.EqualTo(childWeapon));
                Assert.That(grant.Weapon.DefinitionId.Value, Is.EqualTo("grade1-0"));
                Assert.That(grant.Weapon.Provenance, Is.EqualTo(WeaponProvenance.Personal));
                Assert.That(grant.Weapon.Location.Kind, Is.EqualTo(WeaponLocationKind.Warehouse));
                Assert.That(grant.Completion.ProviderEventId, Is.EqualTo(Event));
                Assert.That(grant.Completion.ReservationToken, Is.EqualTo(Token));
                Assert.That(reopened.ConfirmedUses(Account, ReservedAt.Date), Is.EqualTo(1));
                Assert.That(reopened.ConfirmedUses(Account, now.Date), Is.Zero);
                Assert.That(reopened.ConfirmedUses(OtherAccount, ReservedAt.Date), Is.Zero);

                now = AcceptUntil.AddDays(1);
                for (int retry = 0; retry < 10; retry++)
                {
                    AdRewardClaimResult reply = Service(OpenRestartedStore(), UnexpectedDraw).ProcessCallback(Provider, Proof);
                    Assert.That(reply.Status, Is.EqualTo(AdRewardClaimStatus.AlreadyGranted));
                    Assert.That(reply.Receipt?.Completion.ProviderEventId, Is.EqualTo(Event));
                    Assert.That(reply.Receipt?.Weapon.Id, Is.EqualTo(childWeapon));
                }
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(atBarrier));
            }
            finally
            {
                if (Directory.Exists(buildDirectory))
                    Directory.Delete(buildDirectory, recursive: true);
            }
        }

        private static void BuildChildWriter(string dotnet, string directory)
        {
            Assert.That(File.Exists(dotnet), Is.True, "The pinned Windows Editor must provide its bundled .NET SDK.");
            string source = Path.Combine(Application.dataPath, "Tests", "EditMode", "AdRewardsAuthority");
            string references = Path.Combine(directory, "references");
            Directory.CreateDirectory(references);
            foreach (string file in new[] { "AdRewardCrashWriter.cs", "AdRewardTestFixtures.cs", "AdRewardFileStore.cs" })
                File.Copy(Path.Combine(source, file), Path.Combine(directory, file));
            File.Copy(Path.Combine(source, "AdRewardCrashWriter.csproj.txt"), Path.Combine(directory, "AdRewardCrashWriter.csproj"));

            string assemblies = Path.GetDirectoryName(typeof(AdRewardCompletionService).Assembly.Location)!;
            foreach (string assembly in new[] { "AdRewardsAuthority", "Balance", "Inventory", "Weapons", "WeaponDraw" })
            {
                string file = "ProjectWeapon." + assembly + ".dll";
                File.Copy(Path.Combine(assemblies, file), Path.Combine(references, file));
            }

            var start = ChildProcess(dotnet, directory, "build AdRewardCrashWriter.csproj -c Release --nologo -v quiet");
            using Process build = Process.Start(start) ?? throw new InvalidOperationException("The child writer build did not start.");
            Task<string> output = build.StandardOutput.ReadToEndAsync();
            Task<string> errors = build.StandardError.ReadToEndAsync();
            if (!build.WaitForExit(45000))
            {
                build.Kill();
                build.WaitForExit(10000);
                Assert.Fail("Building the isolated .NET child writer timed out.");
            }
            Assert.That(build.ExitCode, Is.Zero, () => output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
        }

        private static ProcessStartInfo ChildProcess(string dotnet, string directory, string arguments)
        {
            var start = new ProcessStartInfo(dotnet, arguments)
            {
                WorkingDirectory = directory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
            return start;
        }

        [Test]
        public void NonConfirmationsBeforeAndAfterVerifiedCallbackCannotUndoOrDuplicateIt()
        {
            AdRewardCompletionService service = Service();
            foreach (string ordering in new[] { "load-error", "dismissed", "late-closed" })
                Assert.That(service.ProcessCallback(Provider, ordering).Status, Is.EqualTo(AdRewardClaimStatus.UnverifiedCompletion));

            Assert.That(service.ProcessCallback(Provider, Proof).Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            foreach (string ordering in new[] { "late-closed", "dismissed", "load-error" })
                Assert.That(service.ProcessCallback(Provider, ordering).Status, Is.EqualTo(AdRewardClaimStatus.UnverifiedCompletion));

            Assert.That(service.ProcessCallback(Provider, Proof).Status, Is.EqualTo(AdRewardClaimStatus.AlreadyGranted));
            Assert.That(file.Read().Grants, Has.Count.EqualTo(1));
        }

        [Test]
        public void ValidCompletionsArrivingInReverseOrderStillUseTheirOwnReservationDays()
        {
            DateTime newerReservationAt = ReservedAt.AddMinutes(1);
            file.Seed(new AdRewardAuthoritySnapshot(Scope, 0, new[]
            {
                new AdRewardReservation(Token, Account, ReservedAt, AcceptUntil, true),
                new AdRewardReservation(OtherToken, Account, newerReservationAt, AcceptUntil, true)
            }, Array.Empty<AdRewardGrant>()));
            verifier.Add("newer-completion", Completion(token: OtherToken, eventId: OtherEvent));
            AdRewardCompletionService service = Service();

            Assert.That(service.ProcessCallback(Provider, "newer-completion").Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            Assert.That(service.ProcessCallback(Provider, Proof).Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            Assert.That(Service(random: UnexpectedDraw).ProcessCallback(Provider, Proof).Status,
                Is.EqualTo(AdRewardClaimStatus.AlreadyGranted));

            AdRewardAuthoritySnapshot persisted = OpenRestartedStore().Read();
            Assert.That(persisted.Grants.Select(grant => grant.ReservationDayUtc),
                Is.EqualTo(new[] { newerReservationAt.Date, ReservedAt.Date }));
            Assert.That(persisted.ConfirmedUses(Account, newerReservationAt.Date), Is.EqualTo(1));
            Assert.That(persisted.ConfirmedUses(Account, ReservedAt.Date), Is.EqualTo(1));
        }

        [Test]
        public void VerifiedTokenAndEventCannotBeReassignedAcrossEventsReservationsOrAccounts()
        {
            verifier.Add("same-token-new-event", Completion(eventId: OtherEvent));
            verifier.Add("same-event-other-token", Completion(account: OtherAccount, token: OtherToken));
            verifier.Add("another-legitimate-reward", Completion(account: OtherAccount, token: OtherToken, eventId: OtherEvent));
            var service = Service();
            Assert.That(service.ProcessCallback(Provider, Proof).Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            foreach (string request in new[] { "same-token-new-event", "same-event-other-token" })
            {
                AdRewardClaimResult outcome = Service(random: UnexpectedDraw).ProcessCallback(Provider, request);
                Assert.That(outcome.Status, Is.EqualTo(AdRewardClaimStatus.ConflictingCompletion));
                Assert.That(outcome.Receipt, Is.Null);
            }
            Assert.That(service.ProcessCallback(Provider, "another-legitimate-reward").Status,
                Is.EqualTo(AdRewardClaimStatus.Granted));
            AdRewardAuthoritySnapshot persisted = file.Read();
            Assert.That(persisted.Grants.Select(grant => grant.Weapon.Id).Distinct().Count(), Is.EqualTo(2));
            Assert.That(persisted.ConfirmedUses(Account, ReservedAt.Date), Is.EqualTo(1));
            Assert.That(persisted.ConfirmedUses(OtherAccount, ReservedAt.Date), Is.EqualTo(1));
        }

        [Test]
        public void CompetingCallbackProcessesReturnSameGrantAfterRevisionConflict()
        {
            using var barrier = new Barrier(2);
            var competing = new CommitDouble(file, (revision, grant) =>
            {
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("Both callback processes should attempt the same revision.");
                return OpenRestartedStore().TryCommit(revision, grant);
            });
            Task<AdRewardClaimResult> first = Task.Run(() => Service(competing).ProcessCallback(Provider, Proof));
            Task<AdRewardClaimResult> second = Task.Run(() => Service(competing).ProcessCallback(Provider, Proof));
            Assert.That(Task.WaitAll(new Task[] { first, second }, TimeSpan.FromSeconds(25)), Is.True);
            Assert.That(new[] { first.Result.Status, second.Result.Status },
                Is.EquivalentTo(new[] { AdRewardClaimStatus.Granted, AdRewardClaimStatus.AlreadyGranted }));
            Assert.That(first.Result.Receipt!.Weapon.Id, Is.EqualTo(second.Result.Receipt!.Weapon.Id));
            Assert.That(OpenRestartedStore().Read().Grants.Single().Weapon.Id, Is.EqualTo(first.Result.Receipt.Weapon.Id));
        }

        [Test]
        public void FailedOrStaleWriteDoesNotChangeAnyPartAndVerifiedRetryCompletes()
        {
            byte[] before = File.ReadAllBytes(path);
            var failedWrite = new AdRewardFileStore(path, fileLock, () => throw new IOException("Authority file activation interrupted."));
            var staleWrite = new CommitDouble(file, (_, _) => false);

            Assert.Throws<IOException>(() => Service(failedWrite).ProcessCallback(Provider, Proof));
            Assert.That(Service(staleWrite).ProcessCallback(Provider, Proof).Status, Is.EqualTo(AdRewardClaimStatus.RetryRequired));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(File.Exists(path + ".staging"), Is.False);

            Assert.That(Service(OpenRestartedStore()).ProcessCallback(Provider, Proof).Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            Assert.That(file.Read().Grants.Count, Is.EqualTo(1));
        }

        [Test]
        public void RevisionConflictFromReleasedReservationNeverSpendsTheOldReservation()
        {
            var releasedDuringCommit = new CommitDouble(file, (revision, grant) =>
            {
                file.Seed(new AdRewardAuthoritySnapshot(Scope, revision + 1,
                    new[] { new AdRewardReservation(Token, Account, ReservedAt, AcceptUntil, false) },
                    Array.Empty<AdRewardGrant>()));
                return file.TryCommit(revision, grant);
            });

            Assert.That(Service(releasedDuringCommit).ProcessCallback(Provider, Proof).Status,
                Is.EqualTo(AdRewardClaimStatus.RetryRequired));
            Assert.That(Service(random: UnexpectedDraw).ProcessCallback(Provider, Proof).Status,
                Is.EqualTo(AdRewardClaimStatus.ReservationUnavailable));
            Assert.That(file.Read().Grants, Is.Empty);
        }

        private void Seed(bool acceptsCompletion = true) => file.Seed(new AdRewardAuthoritySnapshot(Scope, 0,
            new[] {
                new AdRewardReservation(Token, Account, ReservedAt, AcceptUntil, acceptsCompletion),
                new AdRewardReservation(OtherToken, OtherAccount, ReservedAt, AcceptUntil, true)
            }, Array.Empty<AdRewardGrant>()));

        private AdRewardFileStore OpenRestartedStore() => new AdRewardFileStore(path, fileLock);

        private AdRewardCompletionService Service(IAdRewardAuthorityStore? store = null, Func<int, int>? random = null)
            => new AdRewardCompletionService(Scope, verifier, store ?? file, draw, random ?? (_ => 0), () => now);

        private static int UnexpectedDraw(int _) => throw new InvalidOperationException("A replay or invalid request must not draw.");

        private static VerifiedAdRewardCompletion Completion(string scope = Scope, string account = Account,
            string token = Token, string eventId = Event) => new VerifiedAdRewardCompletion(scope, account, token, eventId);

    }
}
