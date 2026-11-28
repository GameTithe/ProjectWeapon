#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Accounts.Unity;
using UnityEngine;

namespace ProjectWeapon.Accounts.Tests
{
    public sealed class AccountSessionTests
    {
        private FakeProvider provider = null!;
        private AccountSessionBoundary accounts = null!;
        private GuestProfileId guest;

        [SetUp]
        public void Prepare()
        {
            guest = new GuestProfileId(Guid.NewGuid());
            provider = new FakeProvider();
            accounts = new AccountSessionBoundary(guest, provider);
        }

        [TearDown]
        public async Task Close()
        {
            provider.UnblockOutstanding();
            await accounts.CloseAsync();
        }

        [Test]
        public async Task GuestCannotStartAnOnlineRequestAndWrongProviderClaimCannotBecomeAnAccount()
        {
            Assert.That(accounts.Mode, Is.EqualTo(AccountMode.LocalGuest));
            Assert.That(accounts.GuestProfile, Is.EqualTo(guest));
            Assert.That(accounts.TryGetOnlineSession(out _), Is.False);

            bool requested = false;
            OnlineRequestStatus guestRequest = await accounts.RunOnlineAsync<int>(_ =>
            {
                requested = true;
                return Task.FromResult(44);
            }, _ => Assert.Fail("Guest response applied."));
            Assert.That(guestRequest, Is.EqualTo(OnlineRequestStatus.Unavailable));
            Assert.That(requested, Is.False);

            Task<AccountSignInStatus> login = accounts.SignInAsync();
            provider.Complete(0, ProviderSignInResult.Verified("unverified-claim"), authorizedId: null);
            Assert.That(await login, Is.EqualTo(AccountSignInStatus.Failed));
            Assert.That(accounts.TryGetOnlineSession(out _), Is.False);
        }

        [Test]
        public async Task VerifiedPlayerIdAndLeaseReachOnlyTheRequestForTheCurrentAccount()
        {
            OnlineAccountSession first = await SignIn("Player-A", 0);
            OnlineRequestStatus status = await accounts.RunOnlineAsync(session =>
            {
                Assert.That(session, Is.SameAs(first));
                Assert.That(session.PlayerId, Is.EqualTo("Player-A"));
                Assert.That(session.Cancellation.IsCancellationRequested, Is.False);
                return Task.FromResult(42);
            }, value => Assert.That(value, Is.EqualTo(42)));
            Assert.That(status, Is.EqualTo(OnlineRequestStatus.Applied));

            var response = Pending<int>();
            Task<OnlineRequestStatus> oldRequest = accounts.RunOnlineAsync(_ => response.Task,
                _ => Assert.Fail("A response was applied to B."));
            Task<AccountSignInStatus> switchAccount = accounts.SignInAsync();
            Assert.That(first.Cancellation.IsCancellationRequested, Is.True);
            Assert.That(accounts.TryGetOnlineSession(out _), Is.False);
            provider.Complete(1, ProviderSignInResult.Verified("Player-B"), "Player-B");
            Assert.That(await switchAccount, Is.EqualTo(AccountSignInStatus.SignedIn));
            Assert.That(accounts.TryGetOnlineSession(out OnlineAccountSession? second), Is.True);
            Assert.That(second!.PlayerId, Is.EqualTo("Player-B"));
            response.SetResult(8);
            Assert.That(await oldRequest, Is.EqualTo(OnlineRequestStatus.Stale));
            Assert.That(accounts.IsCurrent(first), Is.False);
            Assert.That(accounts.IsCurrent(second), Is.True);
        }

        [Test]
        public async Task LogoutRevokesCredentialsImmediatelyAndLateSdkExchangeCannotReplaceANewerAttempt()
        {
            var changes = new List<AccountMode>();
            accounts.ModeChanged += changes.Add;
            Task<AccountSignInStatus> first = accounts.SignInAsync();
            Assert.That(await accounts.SignInAsync(), Is.EqualTo(AccountSignInStatus.InProgress));
            Assert.That(provider.Attempts, Has.Count.EqualTo(1));

            Task<bool> guestAction = accounts.UseGuestAsync();
            Assert.That(accounts.Mode, Is.EqualTo(AccountMode.LocalGuest));
            Assert.That(accounts.TryGetOnlineSession(out _), Is.False);
            Assert.That(provider.Attempts[0].Cancellation.IsCancellationRequested, Is.True);
            Task<AccountSignInStatus> next = accounts.SignInAsync();
            Assert.That(provider.Attempts, Has.Count.EqualTo(1));

            // The fake SDK deliberately ignores cancellation and changes its identity when the old request returns.
            provider.Complete(0, ProviderSignInResult.Verified("Player-A"), "Player-A");
            Assert.That(await first, Is.EqualTo(AccountSignInStatus.Superseded));
            Assert.That(await guestAction, Is.False);
            await provider.Started(1);
            Assert.That(provider.CurrentId, Is.Null);
            provider.Complete(1, ProviderSignInResult.Verified("Player-B"), "Player-B");
            Assert.That(await next, Is.EqualTo(AccountSignInStatus.SignedIn));
            Assert.That(accounts.TryGetOnlineSession(out OnlineAccountSession? session), Is.True);
            Assert.That(session!.PlayerId, Is.EqualTo("Player-B"));
            Assert.That(changes, Is.EqualTo(new[]
                { AccountMode.SigningIn, AccountMode.LocalGuest, AccountMode.SigningIn, AccountMode.Online }));
            Assert.That(await accounts.UseGuestAsync(), Is.True);
            Assert.That(provider.CurrentId, Is.Null);
        }

        [Test]
        public async Task ExpiryRejectsOldCallbacksEvenWhenTheSameAccountSignsInAgain()
        {
            OnlineAccountSession first = await SignIn("Same-Account", 0);
            var response = Pending<int>();
            using var registered = first.Cancellation.Register(() => { });
            Task<OnlineRequestStatus> request = accounts.RunOnlineAsync(_ => response.Task,
                _ => Assert.Fail("An expired request was applied."));

            provider.Invalidate();
            Assert.That(accounts.Mode, Is.EqualTo(AccountMode.Expired));
            Assert.That(first.Cancellation.IsCancellationRequested, Is.True);
            Assert.That(accounts.TryGetOnlineSession(out _), Is.False);
            OnlineAccountSession fresh = await SignIn("Same-Account", 1);
            Assert.That(fresh, Is.Not.SameAs(first));
            response.SetResult(10);
            Assert.That(await request, Is.EqualTo(OnlineRequestStatus.Stale));
            Assert.That(accounts.IsCurrent(fresh), Is.True);
        }

        [Test]
        public async Task CurrentPermissionIsCheckedEvenIfAnSdkInvalidationEventHasNotArrived()
        {
            OnlineAccountSession session = await SignIn("Player-A", 0);
            provider.CurrentId = "Someone-Else";
            Assert.That(accounts.IsCurrent(session), Is.False);
            Assert.That(session.Cancellation.IsCancellationRequested, Is.True);
            Assert.That(accounts.Mode, Is.EqualTo(AccountMode.Expired));
        }

        [Test]
        public async Task RepeatedCloseWaitsForTheSdkAndRejectsFurtherSignIn()
        {
            Task<AccountSignInStatus> login = accounts.SignInAsync();
            Task<bool> firstClose = accounts.CloseAsync();
            Task<bool> secondClose = accounts.CloseAsync();
            Assert.That(accounts.Mode, Is.EqualTo(AccountMode.Closed));
            Assert.That(firstClose.IsCompleted || secondClose.IsCompleted, Is.False);
            provider.Complete(0, ProviderSignInResult.Verified("Retired-Account"), "Retired-Account");
            Assert.That(await login, Is.EqualTo(AccountSignInStatus.Superseded));
            Assert.That(await firstClose, Is.True);
            Assert.That(await secondClose, Is.True);
            Assert.That(provider.CurrentId, Is.Null);
            Assert.That(await accounts.SignInAsync(), Is.EqualTo(AccountSignInStatus.Unavailable));
        }

        [Test]
        public async Task FailureAndCancelledBrowserLeaveTheInstallationGuestUsableForRetry()
        {
            Task<AccountSignInStatus> faulted = accounts.SignInAsync();
            provider.Attempts[0].Result.SetException(new InvalidOperationException("synthetic-credential-marker"));
            Assert.That(await faulted, Is.EqualTo(AccountSignInStatus.Failed));
            Assert.That(accounts.Mode, Is.EqualTo(AccountMode.LocalGuest));
            Assert.That(accounts.GuestProfile, Is.EqualTo(guest));
            Task<AccountSignInStatus> cancelled = accounts.SignInAsync();
            provider.Complete(1, ProviderSignInResult.Cancelled, null);
            Assert.That(await cancelled, Is.EqualTo(AccountSignInStatus.Cancelled));
            Assert.That(accounts.TryGetOnlineSession(out _), Is.False);
            await SignIn("Player-A", 2);
        }

        [Test]
        public async Task ThrowingModeObserverDoesNotStrandSignInOrPreventGuestAndProviderTeardown()
        {
            accounts.ModeChanged += _ => throw new InvalidOperationException("observer failed");
            var observed = new List<AccountMode>();
            accounts.ModeChanged += observed.Add;

            Task<AccountSignInStatus> previous = accounts.SignInAsync();
            Assert.That(provider.Attempts, Has.Count.EqualTo(1));
            Assert.That(accounts.Mode, Is.EqualTo(AccountMode.SigningIn));
            Task<bool> guestFallback = accounts.UseGuestAsync();
            Assert.That(provider.Attempts[0].Cancellation.IsCancellationRequested, Is.True);
            provider.Complete(0, ProviderSignInResult.Verified("retired"), "retired");
            Assert.That(await previous, Is.EqualTo(AccountSignInStatus.Superseded));
            Assert.That(await guestFallback, Is.True);
            Assert.That(provider.CurrentId, Is.Null);

            OnlineAccountSession current = await SignIn("returning", 1);
            Assert.That(await accounts.CloseAsync(), Is.True);
            Assert.That(current.Cancellation.IsCancellationRequested, Is.True);
            Assert.That(accounts.TryGetOnlineSession(out _), Is.False);
            Assert.That(provider.CurrentId, Is.Null);
            Assert.That(await accounts.CloseAsync(), Is.True);
            Assert.That(await accounts.SignInAsync(), Is.EqualTo(AccountSignInStatus.Unavailable));
            Assert.That(observed, Is.EqualTo(new[]
            {
                AccountMode.SigningIn, AccountMode.LocalGuest, AccountMode.SigningIn, AccountMode.Online, AccountMode.Closed
            }));
        }

        private async Task<OnlineAccountSession> SignIn(string player, int index)
        {
            Task<AccountSignInStatus> login = accounts.SignInAsync();
            await provider.Started(index);
            provider.Complete(index, ProviderSignInResult.Verified(player), player);
            Assert.That(await login, Is.EqualTo(AccountSignInStatus.SignedIn));
            Assert.That(accounts.TryGetOnlineSession(out OnlineAccountSession? session), Is.True);
            return session!;
        }

        private static TaskCompletionSource<T> Pending<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class FakeProvider : IAccountProvider
        {
            private readonly Dictionary<int, TaskCompletionSource<bool>> starts = new Dictionary<int, TaskCompletionSource<bool>>();
            public readonly List<Attempt> Attempts = new List<Attempt>();
            public string? CurrentId { get; set; }

            public event Action? SessionInvalidated;

            public Task<ProviderSignInResult> SignInAsync(CancellationToken cancellation)
            {
                var attempt = new Attempt(cancellation);
                int index = Attempts.Count;
                Attempts.Add(attempt);
                if (starts.TryGetValue(index, out TaskCompletionSource<bool> waiting))
                    waiting.TrySetResult(true);
                return attempt.Result.Task;
            }

            public bool IsAuthorized(string playerId) => CurrentId == playerId;
            public void SignOut() => CurrentId = null;

            public void Invalidate()
            {
                CurrentId = null;
                SessionInvalidated?.Invoke();
            }

            public void Complete(int index, ProviderSignInResult result, string? authorizedId)
            {
                CurrentId = authorizedId;
                Attempts[index].Result.SetResult(result);
            }

            public Task Started(int index)
            {
                if (index < Attempts.Count)
                    return Task.CompletedTask;
                if (!starts.TryGetValue(index, out TaskCompletionSource<bool> waiting))
                {
                    waiting = Pending<bool>();
                    starts.Add(index, waiting);
                }
                return waiting.Task;
            }

            public void UnblockOutstanding()
            {
                foreach (Attempt item in Attempts)
                    item.Result.TrySetResult(default);
            }
        }

        private sealed class Attempt
        {
            public Attempt(CancellationToken cancellation) => Cancellation = cancellation;
            public TaskCompletionSource<ProviderSignInResult> Result { get; } = Pending<ProviderSignInResult>();
            public CancellationToken Cancellation { get; }
        }
    }
}
