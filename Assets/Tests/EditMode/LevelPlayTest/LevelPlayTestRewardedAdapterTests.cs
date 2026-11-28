#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Accounts;
using ProjectWeapon.Ad;
using Unity.Services.LevelPlay;

namespace ProjectWeapon.LevelPlayTest.Tests
{
    public sealed class LevelPlayTestRewardedAdapterTests
    {
        private const string Token = "ReservationTest123";
        private readonly List<RewardedAdResult> outcomes = new List<RewardedAdResult>();
        private readonly List<FakeAd> ads = new List<FakeAd>();
        private readonly ReceiptSource receipts = new ReceiptSource();
        private readonly HashSet<string> usedTokens = new HashSet<string>();
        private double clock;
        private AccountSessionBoundary accounts = null!;
        private OnlineAccountSession session = null!;
        private LevelPlayTestRewardedAdapter adapter = null!;
        private string? configuredToken;

        [SetUp]
        public async Task SetUp()
        {
            outcomes.Clear();
            ads.Clear();
            configuredToken = null;
            clock = 0;
            usedTokens.Clear();
            receipts.PermitOverride = null;
            accounts = new AccountSessionBoundary(new GuestProfileId(Guid.NewGuid()), new Provider());
            Assert.That(await accounts.SignInAsync(), Is.EqualTo(AccountSignInStatus.SignedIn));
            Assert.That(accounts.TryGetOnlineSession(out OnlineAccountSession? signedIn), Is.True);
            session = signedIn!;
            adapter = new LevelPlayTestRewardedAdapter(session, () => accounts.IsCurrent(session),
                () =>
                {
                    var ad = new FakeAd();
                    ads.Add(ad);
                    return ad;
                },
                token =>
                {
                    configuredToken = token;
                    return true;
                }, receipts, "test-only-placement", () => clock, usedTokens.Add);
            adapter.OutcomeReported += outcomes.Add;
        }

        [TearDown]
        public async Task TearDown()
        {
            adapter.Dispose();
            await accounts.CloseAsync();
        }

        [Test]
        public async Task LoadedPresentationAcceptsOnlyTheMatchingDurableAuthorityReceipt()
        {
            Guid id = Guid.NewGuid();
            int loaded = 0;
            int closed = 0;
            adapter.Loaded += request => { Assert.That(request, Is.EqualTo(id)); loaded++; };
            adapter.AwaitingVerification += request => { Assert.That(request, Is.EqualTo(id)); closed++; };
            Assert.That(adapter.TryBegin(Guid.Empty), Is.False);
            Assert.That(adapter.TryBegin(id), Is.True);
            Assert.That(adapter.TryBegin(id), Is.False);
            Assert.That(adapter.TryBegin(Guid.NewGuid()), Is.False);
            Assert.That(await adapter.TryShowAsync(id), Is.False);
            FakeAd ad = ads[0];
            ad.Loaded();
            ad.Loaded();
            Assert.That(loaded, Is.EqualTo(1));
            Assert.That(configuredToken, Is.Null);
            receipts.PermitOverride = request => Task.FromResult(
                new LevelPlayTestPresentation(request, "not-a-valid-token", TimeSpan.FromMinutes(5)));
            Assert.That(await adapter.TryShowAsync(id), Is.False);
            receipts.PermitOverride = null;
            Assert.That(await adapter.TryShowAsync(id), Is.True);
            Assert.That(ad.ShownPlacement, Is.EqualTo("test-only-placement"));
            Assert.That(configuredToken, Is.EqualTo(Token));

            ad.Rewarded();
            Assert.That(outcomes, Is.Empty);
            Assert.That(await adapter.CheckForReceiptAsync(id), Is.False);
            ad.Closed();
            ad.Rewarded();
            ad.Closed();
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(outcomes, Is.Empty);

            receipts.Next = new LevelPlayTestReceipt(id, "OtherReservation", LevelPlayTestReceiptStatus.Confirmed, "receipt");
            Assert.That(await adapter.CheckForReceiptAsync(id), Is.False);
            receipts.Next = new LevelPlayTestReceipt(Guid.NewGuid(), Token, LevelPlayTestReceiptStatus.Confirmed, "receipt");
            Assert.That(await adapter.CheckForReceiptAsync(id), Is.False);
            receipts.Next = new LevelPlayTestReceipt(id, Token, LevelPlayTestReceiptStatus.Confirmed);
            Assert.That(await adapter.CheckForReceiptAsync(id), Is.False);
            receipts.Next = new LevelPlayTestReceipt(id, Token, LevelPlayTestReceiptStatus.Pending);
            Assert.That(await adapter.CheckForReceiptAsync(id), Is.False);
            receipts.Next = new LevelPlayTestReceipt(id, Token, LevelPlayTestReceiptStatus.Confirmed, "receipt");
            Assert.That(await adapter.CheckForReceiptAsync(id), Is.True);
            Assert.That(outcomes.Count, Is.EqualTo(1));
            Assert.That(outcomes[0].Outcome, Is.EqualTo(RewardedAdOutcome.ConfirmedCompletion));
            Assert.That(outcomes[0].VerifiedCompletion!.Value.CompletionId, Is.EqualTo("receipt"));
            Assert.That(receipts.CurrentSession, Is.SameAs(session));
            Assert.That(ad.Disposed, Is.True);
            Assert.That(await adapter.CheckForReceiptAsync(id), Is.False);
            Assert.That(adapter.TryBegin(id), Is.False);
        }

        [Test]
        public async Task LoadFailureCancelBeforePresentationAndDisplayFailureAreTerminalWithoutProof()
        {
            Guid load = Guid.NewGuid();
            Assert.That(adapter.TryBegin(load), Is.True);
            ads[0].LoadFailed();
            ads[0].Loaded();

            Guid cancelled = Guid.NewGuid();
            Assert.That(adapter.TryBegin(cancelled), Is.True);
            Assert.That(adapter.TryCancel(cancelled), Is.True);
            Assert.That(adapter.TryCancel(cancelled), Is.False);

            Guid display = Guid.NewGuid();
            await Present(display);
            Assert.That(adapter.TryCancel(display), Is.False);
            ads[2].DisplayFailed();
            ads[2].Closed();
            Assert.That(outcomes.ConvertAll(item => item.Outcome), Is.EqualTo(new[] {
                RewardedAdOutcome.LoadFailed, RewardedAdOutcome.Cancelled, RewardedAdOutcome.Error }));
            Assert.That(outcomes.TrueForAll(item => item.VerifiedCompletion == null), Is.True);
            Assert.That(ads.TrueForAll(ad => ad.Disposed), Is.True);
        }

        [Test]
        public async Task RejectedProofProducesClosedWithoutConfirmation()
        {
            Guid id = Guid.NewGuid();
            await Present(id);
            ads[0].Rewarded();
            ads[0].Closed();
            receipts.Next = new LevelPlayTestReceipt(id, Token, LevelPlayTestReceiptStatus.Rejected);
            Assert.That(await adapter.CheckForReceiptAsync(id), Is.True);
            Assert.That(outcomes.Count, Is.EqualTo(1));
            Assert.That(outcomes[0].Outcome, Is.EqualTo(RewardedAdOutcome.ClosedWithoutConfirmation));
            Assert.That(outcomes[0].VerifiedCompletion, Is.Null);
        }

        [Test]
        public async Task DelayedProofFromARevokedSessionDoesNotReachAReloggedAccount()
        {
            Guid id = Guid.NewGuid();
            await Present(id);
            ads[0].Closed();
            var delayed = new TaskCompletionSource<LevelPlayTestReceipt>();
            receipts.Delayed = delayed.Task;
            Task<bool> checking = adapter.CheckForReceiptAsync(id);
            Assert.That(await adapter.CheckForReceiptAsync(id), Is.False);
            await accounts.UseGuestAsync();
            Assert.That(await accounts.SignInAsync(), Is.EqualTo(AccountSignInStatus.SignedIn));
            delayed.SetResult(new LevelPlayTestReceipt(id, Token, LevelPlayTestReceiptStatus.Confirmed, "old"));
            Assert.That(await checking, Is.False);
            Assert.That(adapter.TryBegin(Guid.NewGuid()), Is.False);
            Assert.That(outcomes, Is.Empty);
        }

        [Test]
        public async Task PendingReceiptDoesNotBlockAnotherPresentationAndCannotBeMisattributed()
        {
            Guid first = Guid.NewGuid();
            await Present(first);
            ads[0].Closed();
            Assert.That(outcomes, Is.Empty);

            Guid second = Guid.NewGuid();
            Assert.That(adapter.TryBegin(second), Is.True);
            ads[1].Loaded();
            Assert.That(await adapter.TryShowAsync(second), Is.False, "A reservation token is consumed once.");
            const string secondToken = "AnotherReservation";
            receipts.PermitOverride = id => Task.FromResult(
                new LevelPlayTestPresentation(id, secondToken, TimeSpan.FromSeconds(30)));
            Assert.That(await adapter.TryShowAsync(second), Is.True);
            receipts.Next = new LevelPlayTestReceipt(first, Token, LevelPlayTestReceiptStatus.Confirmed, "receipt-one");
            Assert.That(await adapter.CheckForReceiptAsync(first), Is.True);
            Assert.That(outcomes[0].RequestId, Is.EqualTo(first));
            Assert.That(ads[1].Disposed, Is.False);
            ads[1].Closed();
            receipts.Next = new LevelPlayTestReceipt(second, secondToken, LevelPlayTestReceiptStatus.Rejected);
            Assert.That(await adapter.CheckForReceiptAsync(second), Is.True);
            Assert.That(outcomes[1].RequestId, Is.EqualTo(second));
            Assert.That(outcomes[1].VerifiedCompletion, Is.Null);
        }

        [Test]
        public async Task ExpiredDelayedAuthorizationCannotDisplayTheAd()
        {
            Guid id = Guid.NewGuid();
            Assert.That(adapter.TryBegin(id), Is.True);
            ads[0].Loaded();
            var deferred = new TaskCompletionSource<LevelPlayTestPresentation>();
            receipts.PermitOverride = _ => deferred.Task;
            Task<bool> authorization = adapter.TryShowAsync(id);
            Assert.That(await adapter.TryShowAsync(id), Is.False);
            clock = 3;
            deferred.SetResult(new LevelPlayTestPresentation(id, Token, TimeSpan.FromSeconds(3)));
            Assert.That(await authorization, Is.False);
            Assert.That(configuredToken, Is.Null);
            Assert.That(ads[0].ShownPlacement, Is.Null);
            Assert.That(outcomes, Is.Empty);
        }

        [Test]
        public async Task TimedOutNativeOperationsRecoverWithoutCreatingRewardEvidence()
        {
            Guid load = Guid.NewGuid();
            Assert.That(adapter.TryBegin(load), Is.True);
            clock = 44.99;
            Assert.That(adapter.RecoverTimedOutPresentation(), Is.False);
            clock = 45;
            Assert.That(adapter.RecoverTimedOutPresentation(), Is.True);
            Assert.That(outcomes[0].Outcome, Is.EqualTo(RewardedAdOutcome.LoadFailed));
            Assert.That(outcomes[0].VerifiedCompletion, Is.Null);

            Guid showing = Guid.NewGuid();
            await Present(showing);
            clock += 239.9;
            Assert.That(adapter.RecoverTimedOutPresentation(), Is.False);
            clock = 285;
            Assert.That(adapter.RecoverTimedOutPresentation(), Is.True);
            Assert.That(ads[1].Disposed, Is.True);
            Assert.That(outcomes, Has.Count.EqualTo(1));
            receipts.Next = new LevelPlayTestReceipt(showing, Token, LevelPlayTestReceiptStatus.Confirmed, "late");
            Assert.That(await adapter.CheckForReceiptAsync(showing), Is.True);
            Assert.That(outcomes[1].VerifiedCompletion!.Value.CompletionId, Is.EqualTo("late"));
        }

        [TestCase("")]
        [TestCase("예약")]
        [TestCase("ABC-123")]
        [TestCase("ABC 123")]
        public void RejectsInvalidProviderReservationTokens(string value)
            => Assert.That(LevelPlayTestRewardedAdapter.IsReservationToken(value), Is.False);

        [Test]
        public void TokenLengthAndCaseLimitsMatchTheNativeSDK()
        {
            Assert.That(LevelPlayTestRewardedAdapter.IsReservationToken("aB9"), Is.True);
            Assert.That(LevelPlayTestRewardedAdapter.IsReservationToken(new string('x', 64)), Is.True);
            Assert.That(LevelPlayTestRewardedAdapter.IsReservationToken(new string('x', 65)), Is.False);
        }

        private async Task Present(Guid id)
        {
            Assert.That(adapter.TryBegin(id), Is.True);
            ads[^1].Loaded();
            Assert.That(await adapter.TryShowAsync(id), Is.True);
        }

        private sealed class ReceiptSource : ILevelPlayTestAuthority
        {
            public LevelPlayTestReceipt Next { get; set; }
            public Func<Guid, Task<LevelPlayTestPresentation>>? PermitOverride { get; set; }
            public Task<LevelPlayTestPresentation> AuthorizeAsync(OnlineAccountSession active, Guid id)
                => PermitOverride?.Invoke(id)
                    ?? Task.FromResult(new LevelPlayTestPresentation(id, Token, TimeSpan.FromMinutes(5)));
            public Task<LevelPlayTestReceipt>? Delayed { get; set; }
            public OnlineAccountSession? CurrentSession { get; private set; }
            public Task<LevelPlayTestReceipt> ReadAsync(OnlineAccountSession active, Guid id, string token)
            {
                CurrentSession = active;
                Assert.That(token, Is.Not.Empty);
                Task<LevelPlayTestReceipt> response = Delayed ?? Task.FromResult(Next);
                Delayed = null;
                return response;
            }
        }

        private sealed class Provider : IAccountProvider
        {
            public event Action? SessionInvalidated { add { } remove { } }
            public bool IsAuthorized(string playerId) => playerId == "test-player";
            public Task<ProviderSignInResult> SignInAsync(CancellationToken _) =>
                Task.FromResult(ProviderSignInResult.Verified("test-player"));
            public void SignOut() { }
        }

        private sealed class FakeAd : ILevelPlayRewardedAd
        {
            public event Action<LevelPlayAdInfo>? OnAdLoaded;
            public event Action<LevelPlayAdError>? OnAdLoadFailed;
            public event Action<LevelPlayAdInfo, LevelPlayAdError>? OnAdDisplayFailed;
            public event Action<LevelPlayAdInfo, LevelPlayReward>? OnAdRewarded;
            public event Action<LevelPlayAdInfo>? OnAdClosed;
            public event Action<LevelPlayAdInfo> OnAdDisplayed { add { } remove { } }
            public event Action<LevelPlayAdInfo> OnAdClicked { add { } remove { } }
            public event Action<LevelPlayAdInfo> OnAdInfoChanged { add { } remove { } }
            public event Action<LevelPlayImpressionData> OnAdImpressionDataReady { add { } remove { } }

            public string AdUnitId => "test-ad-unit";
            public bool Disposed { get; private set; }
            public string? ShownPlacement { get; private set; }
            public string GetAdId() => "test-ad";
            public void LoadAd() { }
            public void ShowAd(string? placementName = null) => ShownPlacement = placementName;
            public bool IsAdReady() => !Disposed;
            public LevelPlayReward GetReward(string placement) => default!;
            public void DestroyAd() => Dispose();
            public void Dispose() => Disposed = true;
            public void Loaded() => OnAdLoaded?.Invoke(default!);
            public void LoadFailed() => OnAdLoadFailed?.Invoke(default!);
            public void Rewarded() => OnAdRewarded?.Invoke(default!, default!);
            public void Closed() => OnAdClosed?.Invoke(default!);
            public void DisplayFailed() => OnAdDisplayFailed?.Invoke(default!, default!);
        }
    }
}
