#nullable enable

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace ProjectWeapon.AdRewards.Tests
{
    public sealed class AdRewardReservationServiceTests
    {
        private const string Scope = "reward-production-a";
        private const string Player = "verified-player";
        private const string Other = "other-verified-player";
        private static readonly DateTime BeforeMidnight = new DateTime(2026, 11, 28, 23, 59, 0, DateTimeKind.Utc);
        private static readonly Identity Online = new Identity(Player);
        private readonly object gate = new object();
        private AdRewardFileStore store = null!;
        private PresentationVerifier presentation = null!;
        private ProofVerifierDouble completion = null!;
        private string directory = null!;
        private string path = null!;
        private DateTime now;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ProjectWeapon-KAN105-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "authority.bin");
            store = OpenStore();
            store.Seed(new AdRewardAuthoritySnapshot(Scope, 0, Array.Empty<AdRewardReservation>(), Array.Empty<AdRewardGrant>()));
            presentation = new PresentationVerifier();
            completion = new ProofVerifierDouble();
            now = BeforeMidnight;
        }

        [TearDown]
        public void Clean()
        {
            File.Delete(path);
            File.Delete(path + ".staging");
            Directory.Delete(directory);
        }

        [Test]
        public void TwoSlotsCountBothGrantsAndHoldsWhileAThirdIsRejected()
        {
            var service = Reservations();
            Guid firstRequest = Guid.NewGuid();
            Assert.That(service.TryReserve(Online, firstRequest, false).Status, Is.EqualTo(AdRewardReservationStatus.AdNotReady));
            Assert.That(store.Read().Revision, Is.Zero);
            AdRewardReservation first = Reserve(service, firstRequest);
            Assert.That(first.CreatedAtUtc, Is.EqualTo(BeforeMidnight));
            Assert.That(first.AcceptUntilUtc, Is.EqualTo(BeforeMidnight.AddDays(9)));
            Assert.That(Finish(first, "proof1").Status, Is.EqualTo(AdRewardClaimStatus.Granted));

            AdRewardReservation second = Reserve(service);
            VerifyEnd(second, AdRewardPresentationEnd.AwaitingProof);
            Assert.That(service.RecordPresentationEnd("end").Status, Is.EqualTo(AdRewardReservationStatus.AwaitingProof));
            var restartedService = Reservations(OpenStore());
            var filled = restartedService.ReadAvailability(Online)!.Value;
            Assert.That(filled.UtcDay, Is.EqualTo(BeforeMidnight.Date));
            Assert.That(filled.NextResetUtc, Is.EqualTo(BeforeMidnight.Date.AddDays(1)));
            Assert.That(filled.ConfirmedUses, Is.EqualTo(1));
            Assert.That(filled.HeldSlots, Is.EqualTo(1));
            Assert.That(filled.RemainingSlots, Is.Zero);
            Assert.That(filled.ViewingInProgress, Is.False);
            Assert.That(restartedService.TryReserve(Online, Guid.NewGuid(), true).Status,
                Is.EqualTo(AdRewardReservationStatus.DailyLimitReached));
            Assert.That(Finish(second, "proof2").Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            Assert.That(restartedService.TryReserve(Online, Guid.NewGuid(), true).Status,
                Is.EqualTo(AdRewardReservationStatus.DailyLimitReached));

            var exhausted = restartedService.ReadAvailability(Online)!.Value;
            Assert.That(exhausted.ConfirmedUses, Is.EqualTo(2));
            Assert.That(exhausted.HeldSlots, Is.Zero);
            Assert.That(restartedService.ReadAvailability(new Identity(Other))!.Value.RemainingSlots, Is.EqualTo(2));
            Assert.That(OpenStore().Read().Grants.Select(grant => grant.Weapon.Id).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void ViewingSpansMidnightButPendingProofAndTheCallbackUseOnlyTheReservationDay()
        {
            var service = Reservations();
            AdRewardReservation yesterday = Reserve(service);
            now = BeforeMidnight.Date.AddDays(1);
            var midnight = service.ReadAvailability(Online)!.Value;
            Assert.That(midnight.ConfirmedUses, Is.Zero);
            Assert.That(midnight.HeldSlots, Is.Zero);
            Assert.That(midnight.ViewingInProgress, Is.True);
            Assert.That(service.TryReserve(Online, Guid.NewGuid(), true).Status,
                Is.EqualTo(AdRewardReservationStatus.ViewingInProgress));

            VerifyEnd(yesterday, AdRewardPresentationEnd.AwaitingProof);
            Assert.That(service.RecordPresentationEnd("end").Status, Is.EqualTo(AdRewardReservationStatus.AwaitingProof));
            AdRewardReservation today = Reserve(service);
            Assert.That(today.CreatedAtUtc, Is.EqualTo(now));
            var replay = service.TryReserve(new Identity(Player), yesterday.RequestId, true);
            Assert.That(replay.Status, Is.EqualTo(AdRewardReservationStatus.AlreadyReserved));
            Assert.That(replay.Reservation!.Token, Is.EqualTo(yesterday.Token));
            Assert.That(Finish(yesterday, "late-proof").Status, Is.EqualTo(AdRewardClaimStatus.Granted));

            var todayQuota = service.ReadAvailability(Online)!.Value;
            Assert.That(todayQuota.ConfirmedUses, Is.Zero);
            Assert.That(todayQuota.HeldSlots, Is.EqualTo(1));
            Assert.That(todayQuota.ViewingInProgress, Is.True);
            Assert.That(OpenStore().Read().ConfirmedUses(Player, BeforeMidnight.Date), Is.EqualTo(1));
        }

        [Test]
        public void OnlyVerifiedNoExposureCanReleaseAnUnpaidSlot()
        {
            var service = Reservations();
            AdRewardReservation first = Reserve(service);
            long before = store.Read().Revision;
            Assert.That(service.RecordPresentationEnd("app-cancelled").Status,
                Is.EqualTo(AdRewardReservationStatus.UnverifiedPresentation));
            VerifyEnd(first, AdRewardPresentationEnd.ConfirmedNoExposure, account: Other);
            Assert.That(service.RecordPresentationEnd("end").Status, Is.EqualTo(AdRewardReservationStatus.MissingReservation));
            VerifyEnd(first, AdRewardPresentationEnd.ConfirmedNoExposure, scope: "wrong-environment");
            Assert.That(service.RecordPresentationEnd("end").Status, Is.EqualTo(AdRewardReservationStatus.UnverifiedPresentation));
            Assert.That(store.Read().Revision, Is.EqualTo(before));

            VerifyEnd(first, AdRewardPresentationEnd.AwaitingProof);
            Assert.That(service.RecordPresentationEnd("end").Status, Is.EqualTo(AdRewardReservationStatus.AwaitingProof));
            Assert.That(service.ReadAvailability(Online)!.Value.HeldSlots, Is.EqualTo(1));
            VerifyEnd(first, AdRewardPresentationEnd.ConfirmedNoExposure);
            Assert.That(service.RecordPresentationEnd("end").Status, Is.EqualTo(AdRewardReservationStatus.Released));
            Assert.That(service.RecordPresentationEnd("end").Status, Is.EqualTo(AdRewardReservationStatus.Released));
            Assert.That(service.ReadAvailability(Online)!.Value.RemainingSlots, Is.EqualTo(2));
            Assert.That(Finish(first, "proof-after-release", UnexpectedDraw).Status, Is.EqualTo(AdRewardClaimStatus.ReservationUnavailable));
            Assert.That(OpenStore().Read().Revision, Is.EqualTo(before + 2));
            Assert.That(OpenStore().Read().Grants, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TwoDevicesReadTheSameRevisionAndOnlyOneCanStart(bool sameRequest)
        {
            var secondDevice = Reservations(OpenStore());
            Guid firstRequest = Guid.NewGuid();
            Guid secondRequest = sameRequest ? firstRequest : Guid.NewGuid();
            AdRewardReservationResult winner = default;
            var race = new InterleavingStore(store, () =>
                winner = secondDevice.TryReserve(new Identity(Player), secondRequest, true));
            var loser = Reservations(race).TryReserve(Online, firstRequest, true);
            Assert.That(winner.Status, Is.EqualTo(AdRewardReservationStatus.Reserved));
            Assert.That(loser.Status, Is.EqualTo(sameRequest
                ? AdRewardReservationStatus.AlreadyReserved
                : AdRewardReservationStatus.ViewingInProgress));
            Assert.That(loser.Reservation?.Token, Is.EqualTo(sameRequest ? winner.Reservation!.Token : null));
            Assert.That(OpenStore().Read().Revision, Is.EqualTo(1));
            Assert.That(OpenStore().Read().Reservations.Count, Is.EqualTo(1));
        }

        [Test]
        public void LocalTimeOrUnverifiedIdentityCannotAuthorizeReservationAndExpiryIsExact()
        {
            var service = Reservations();
            Assert.That(service.TryReserve(new Identity(null), Guid.NewGuid(), true).Status,
                Is.EqualTo(AdRewardReservationStatus.Unauthorized));
            Assert.That(service.ReadAvailability(new Identity(Player, "wrong-environment")), Is.Null);
            now = DateTime.SpecifyKind(BeforeMidnight.AddYears(5), DateTimeKind.Local);
            Assert.That(service.TryReserve(Online, Guid.NewGuid(), true).Status, Is.EqualTo(AdRewardReservationStatus.RetryRequired));
            Assert.That(service.ReadAvailability(Online), Is.Null);
            Assert.That(store.Read().Revision, Is.Zero);

            now = BeforeMidnight;
            AdRewardReservation first = Reserve(service);
            now = first.AcceptUntilUtc;
            Assert.That(service.ReadAvailability(Online)!.Value.ViewingInProgress, Is.False);
            Assert.That(Finish(first, "expired-proof", UnexpectedDraw).Status, Is.EqualTo(AdRewardClaimStatus.ExpiredReservation));
            Assert.That(Reserve(service).CreatedAtUtc, Is.EqualTo(now));
        }

        [Test]
        public void LegacyThirdCallbackDoesNotDrawAndHistoricReceiptStillReplaysAfterExpiry()
        {
            AdRewardReservation Legacy(string token) => new AdRewardReservation(token, Player, BeforeMidnight,
                BeforeMidnight.AddDays(9), true);
            AdRewardReservation first = Legacy("legacy-a");
            AdRewardReservation second = Legacy("legacy-b");
            AdRewardReservation third = Legacy("legacy-c");
            store.Seed(new AdRewardAuthoritySnapshot(Scope, 0, new[] { first, second, third }, Array.Empty<AdRewardGrant>()));
            now = BeforeMidnight.AddMinutes(5);

            var original = Finish(first, "first");
            Assert.That(original.Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            Assert.That(Finish(second, "second").Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            byte[] before = File.ReadAllBytes(path);
            Assert.That(Finish(third, "third", UnexpectedDraw).Status, Is.EqualTo(AdRewardClaimStatus.DailyLimitReached));
            now = first.AcceptUntilUtc.AddHours(1);
            var replay = Complete(UnexpectedDraw).ProcessCallback(AdRewardCompletionService.LevelPlayProviderId, "first");
            Assert.That(replay.Status, Is.EqualTo(AdRewardClaimStatus.AlreadyGranted));
            Assert.That(replay.Receipt!.Weapon.Id, Is.EqualTo(original.Receipt!.Weapon.Id));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        [Test]
        public void NoExposureCannotUndoAGrantThatWonTheSameRevision()
        {
            AdRewardReservation first = Reserve(Reservations());
            VerifyEnd(first, AdRewardPresentationEnd.ConfirmedNoExposure);
            AdRewardClaimResult granted = default;
            var racing = new InterleavingStore(store, () => granted = Finish(first, "won-before-release"));

            Assert.That(Reservations(racing).RecordPresentationEnd("end").Status,
                Is.EqualTo(AdRewardReservationStatus.AlreadyGranted));
            Assert.That(granted.Status, Is.EqualTo(AdRewardClaimStatus.Granted));
            Assert.That(OpenStore().Read().Revision, Is.EqualTo(2));
            Assert.That(OpenStore().Read().ConfirmedUses(Player, BeforeMidnight.Date), Is.EqualTo(1));
        }

        private AdRewardFileStore OpenStore() => new AdRewardFileStore(path, gate);
        private AdRewardReservationService Reservations(IAdRewardReservationStore? other = null)
            => new AdRewardReservationService(Scope, other ?? store, presentation, () => now);
        private AdRewardCompletionService Complete(Func<int, int>? roll = null)
            => new AdRewardCompletionService(Scope, completion, OpenStore(), AdRewardDrawFixture.Create(), roll ?? (_ => 0), () => now);
        private static int UnexpectedDraw(int _) => throw new InvalidOperationException("An ineligible callback cannot draw a weapon.");

        private static AdRewardReservation Reserve(AdRewardReservationService service, Guid? request = null)
        {
            var result = service.TryReserve(Online, request ?? Guid.NewGuid(), true);
            Assert.That(result.Status, Is.EqualTo(AdRewardReservationStatus.Reserved));
            return result.Reservation!;
        }

        private AdRewardClaimResult Finish(AdRewardReservation reservation, string proof, Func<int, int>? roll = null)
        {
            completion.Add(proof, new VerifiedAdRewardCompletion(Scope, reservation.AccountId, reservation.Token, proof + "-event"));
            return Complete(roll).ProcessCallback(AdRewardCompletionService.LevelPlayProviderId, proof);
        }

        private void VerifyEnd(AdRewardReservation reservation, AdRewardPresentationEnd end, string? account = null, string? scope = null)
            => presentation.Verified = new VerifiedAdRewardPresentationEnd(scope ?? Scope,
                account ?? reservation.AccountId, reservation.Token, end);

        private sealed class Identity : IAdRewardReservationIdentity
        {
            public Identity(string? playerId, string authorityScope = Scope)
            {
                VerifiedPlayerId = playerId;
                AuthorityScope = authorityScope;
            }
            public string? VerifiedPlayerId { get; }
            public string AuthorityScope { get; }
        }

        private sealed class PresentationVerifier : IAdRewardPresentationEndVerifier
        {
            public VerifiedAdRewardPresentationEnd Verified { get; set; }
            public bool TryVerify(string evidence, out VerifiedAdRewardPresentationEnd presentation)
            {
                presentation = Verified;
                return evidence == "end";
            }
        }

        private sealed class InterleavingStore : IAdRewardReservationStore
        {
            private readonly IAdRewardReservationStore store;
            private Action? beforeWrite;

            public InterleavingStore(IAdRewardReservationStore store, Action beforeWrite)
            {
                this.store = store;
                this.beforeWrite = beforeWrite;
            }

            public AdRewardAuthoritySnapshot Read() => store.Read();
            public bool TryCommit(long expectedRevision, AdRewardGrant grant) => store.TryCommit(expectedRevision, grant);
            public bool TryWriteReservation(long expectedRevision, AdRewardReservation reservation)
            {
                Action? interleaving = beforeWrite;
                beforeWrite = null;
                interleaving?.Invoke();
                return store.TryWriteReservation(expectedRevision, reservation);
            }
        }
    }
}
