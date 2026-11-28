#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Ad;
using ProjectWeapon.Inventory;
using ProjectWeapon.WeaponDraw;
using ProjectWeapon.Weapons;
using UnityEditor.Compilation;

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
            draw = DrawFixture();
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

        private static WeightedWeaponDraw DrawFixture()
        {
            var grades = new Dictionary<WeaponDefinitionId, int>();
            IReadOnlyList<WeaponDefinitionId>[] pools = new IReadOnlyList<WeaponDefinitionId>[5];
            for (int grade = 1; grade <= pools.Length; grade++)
            {
                var definitions = new WeaponDefinitionId[grade <= 3 ? 5 : 3];
                for (int index = 0; index < definitions.Length; index++)
                {
                    var id = new WeaponDefinitionId($"grade{grade}-{index}");
                    grades.Add(id, grade);
                    definitions[index] = id;
                }
                pools[grade - 1] = definitions;
            }
            return new WeightedWeaponDraw(WeightedWeaponDraw.DraftGradeWeightsBasisPoints, pools, grades);
        }

        private sealed class ProofVerifierDouble : IRewardedAdProofVerifier
        {
            private readonly Dictionary<string, VerifiedAdRewardCompletion> verified = new Dictionary<string, VerifiedAdRewardCompletion>();

            internal void Add(string signedCallback, VerifiedAdRewardCompletion completion) => verified.Add(signedCallback, completion);

            public bool TryVerifyLevelPlayCompletion(string signedCallback, out VerifiedAdRewardCompletion completion)
                => verified.TryGetValue(signedCallback, out completion);
        }

        private sealed class CommitDouble : IAdRewardAuthorityStore
        {
            private readonly IAdRewardAuthorityStore store;
            private readonly Func<long, AdRewardGrant, bool> commit;

            internal CommitDouble(IAdRewardAuthorityStore store, Func<long, AdRewardGrant, bool> commit)
            {
                this.store = store;
                this.commit = commit;
            }

            public AdRewardAuthoritySnapshot Read() => store.Read();
            public bool TryCommit(long expectedRevision, AdRewardGrant grant) => commit(expectedRevision, grant);
        }
    }
}
