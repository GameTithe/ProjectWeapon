#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Accounts.Unity;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;

namespace ProjectWeapon.Accounts.Tests
{
    public sealed class UnityAccountAdapterTests
    {
        [Test]
        public async Task UnlinkedProjectExitsThroughOfficialAdapterBeforeBrowserOrSdkInitialization()
        {
            if (!string.IsNullOrWhiteSpace(Application.cloudProjectId))
                Assert.Ignore("This fixture only exercises the unlinked-project path.");

            var provider = new UnityPlayerAccountProvider();
            ProviderSignInResult result = await provider.SignInAsync(CancellationToken.None);
            Assert.That(result.Status, Is.EqualTo(AccountSignInStatus.Unavailable));
            Assert.That(result.PlayerId, Is.Null);
        }

        [Test]
        public void CorruptOrEmptyGuestPreferenceIsReplacedAndDoesNotRegenerateOnReload()
        {
            string key = "ProjectWeapon.Tests.Guest." + Guid.NewGuid().ToString("N");
            try
            {
                PlayerPrefs.SetString(key, "malformed");
                GuestProfileId first = LocalGuestProfile.GetOrCreate(key);
                Assert.That(first.IsValid, Is.True);
                Assert.That(LocalGuestProfile.GetOrCreate(key), Is.EqualTo(first));
                Assert.That(PlayerPrefs.GetString(key), Is.EqualTo(first.Value.ToString("N")));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void ActualUnityLoggerRedactsAuthenticationTagsOnly()
        {
            var sink = new CapturingLogHandler();
            var logger = new Logger(new AuthenticationLogFilter(sink));
            logger.Log("[PlayerAccounts]", "synthetic-authorization-url");
            logger.Log(LogType.Exception, "[Authentication]", new InvalidOperationException("synthetic-token-marker"));
            logger.Log("Game", "Unrelated [Authentication] documentation.");
            logger.LogException(new InvalidOperationException("Unrelated exception."));

            Assert.That(sink.Messages, Has.Count.EqualTo(2));
            Assert.That(sink.Messages[0], Does.Contain("Unrelated [Authentication] documentation."));
            Assert.That(sink.Messages[1], Is.EqualTo("Unrelated exception."));
        }

        [Test]
        public async Task AdapterWaitsForSdkSignedInEventAfterBrowserLaunchReturns()
        {
            var browser = new ControlledBrowser();
            Task<AccountSignInStatus> pending = UnityPlayerAccountProvider.WaitForBrowser(browser, CancellationToken.None);
            Assert.That(browser.Launches, Is.EqualTo(1));
            Assert.That(pending.IsCompleted, Is.False);
            browser.RaiseSignedIn();
            Assert.That(await pending, Is.EqualTo(AccountSignInStatus.SignedIn));
            Assert.That(browser.HasListeners, Is.False);
        }

        [Test]
        public async Task CancelledSdkBrowserIsSignedOutAndDoesNotUseLateCallbacks()
        {
            var browser = new ControlledBrowser();
            using var cancellation = new CancellationTokenSource();
            Task<AccountSignInStatus> pending = UnityPlayerAccountProvider.WaitForBrowser(browser, cancellation.Token);
            cancellation.Cancel();
            Assert.That(await pending, Is.EqualTo(AccountSignInStatus.Cancelled));
            Assert.That(browser.SignOuts, Is.EqualTo(1));
            Assert.That(browser.HasListeners, Is.False);
            browser.RaiseSignedIn();
            Assert.That(await pending, Is.EqualTo(AccountSignInStatus.Cancelled));
        }

        [Test]
        public async Task BrowserConfigurationProblemBecomesASanitizedUnavailableStatus()
        {
            var browser = new ControlledBrowser();
            Task<AccountSignInStatus> pending = UnityPlayerAccountProvider.WaitForBrowser(browser, CancellationToken.None);
            browser.RaiseFailed(new RequestFailedException(PlayerAccountsErrorCodes.MissingClientId, "synthetic-marker"));
            Assert.That(await pending, Is.EqualTo(AccountSignInStatus.Unavailable));
            Assert.That(browser.HasListeners, Is.False);
        }

        private sealed class ControlledBrowser : IPlayerAccountService
        {
            public event Action? SignedIn;
            public event Action? SignedOut;
            public event Action<RequestFailedException>? SignInFailed;
            public string AccessToken => string.Empty;
            public string IdToken => string.Empty;
            public string AccountPortalUrl => string.Empty;
            public IdToken IdTokenClaims => null!;
            public bool IsSignedIn => false;
            public int Launches { get; private set; }
            public int SignOuts { get; private set; }
            public bool HasListeners => SignedIn != null || SignInFailed != null;
            public Task StartSignInAsync(bool isSigningUp = false)
            {
                Launches++;
                return Task.CompletedTask;
            }
            public Task RefreshTokenAsync() => Task.CompletedTask;
            public void SignOut()
            {
                SignOuts++;
                SignedOut?.Invoke();
            }
            public void RaiseSignedIn() => SignedIn?.Invoke();
            public void RaiseFailed(RequestFailedException error) => SignInFailed?.Invoke(error);
        }

        private sealed class CapturingLogHandler : ILogHandler
        {
            public readonly List<string> Messages = new List<string>();

            public void LogFormat(LogType logType, UnityEngine.Object? context, string format, params object[] args)
                => Messages.Add(string.Format(format, args));

            public void LogException(Exception exception, UnityEngine.Object? context) => Messages.Add(exception.Message);
        }
    }
}
