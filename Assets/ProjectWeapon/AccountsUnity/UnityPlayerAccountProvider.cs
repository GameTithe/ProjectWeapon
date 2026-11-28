#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;

namespace ProjectWeapon.Accounts.Unity
{
    /// <summary>Single gameplay-thread owner of the Unity Authentication and Player Accounts SDK sign-in.</summary>
    public sealed class UnityPlayerAccountProvider : IAccountProvider
    {
        private IAuthenticationService? authentication;
        private IPlayerAccountService? playerAccounts;
        private string? verifiedId;

        public event Action? SessionInvalidated;

        public async Task<ProviderSignInResult> SignInAsync(CancellationToken cancellation)
        {
            if (cancellation.IsCancellationRequested)
                return ProviderSignInResult.Cancelled;
            if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
                return ProviderSignInResult.Unavailable;

            AuthenticationLogFilter.Install();
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (cancellation.IsCancellationRequested)
                return ProviderSignInResult.Cancelled;

            if (authentication == null)
            {
                authentication = AuthenticationService.Instance;
                playerAccounts = PlayerAccountService.Instance;
                SignOut();
            }

            IPlayerAccountService browser = playerAccounts!;
            AccountSignInStatus browserStatus = await WaitForBrowser(browser, cancellation);
            if (cancellation.IsCancellationRequested || browserStatus == AccountSignInStatus.Cancelled)
                return ProviderSignInResult.Cancelled;
            if (browserStatus == AccountSignInStatus.Unavailable)
                return ProviderSignInResult.Unavailable;
            if (browserStatus != AccountSignInStatus.SignedIn || !browser.IsSignedIn || string.IsNullOrEmpty(browser.AccessToken))
                return default;

            // Only the official SDK receives this token. Keep the exchange serialized until it actually finishes.
            await authentication.SignInWithUnityAsync(browser.AccessToken);
            if (cancellation.IsCancellationRequested)
                return ProviderSignInResult.Cancelled;
            if (!authentication.IsAuthorized || !browser.IsSignedIn || string.IsNullOrWhiteSpace(authentication.PlayerId))
                return default;

            verifiedId = authentication.PlayerId;
            authentication.Expired += Invalidate;
            authentication.SignedOut += Invalidate;
            authentication.PlayerIdChanged += OnPlayerIdChanged;
            browser.SignedOut += Invalidate;
            return ProviderSignInResult.Verified(verifiedId);
        }

        public bool IsAuthorized(string playerId)
            => verifiedId != null && StringComparer.Ordinal.Equals(verifiedId, playerId)
                && authentication?.IsAuthorized == true && StringComparer.Ordinal.Equals(authentication.PlayerId, playerId)
                && playerAccounts?.IsSignedIn == true;

        public void SignOut()
        {
            DetachSession();
            try
            {
                authentication?.SignOut(clearCredentials: true);
            }
            finally
            {
                playerAccounts?.SignOut();
            }
        }

        internal static async Task<AccountSignInStatus> WaitForBrowser(IPlayerAccountService browser, CancellationToken cancellation)
        {
            var result = new TaskCompletionSource<AccountSignInStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            void SignedIn() => result.TrySetResult(AccountSignInStatus.SignedIn);
            void Failed(RequestFailedException failure) => result.TrySetResult(ClassifyBrowserError(failure));
            browser.SignedIn += SignedIn;
            browser.SignInFailed += Failed;
            try
            {
                // The boundary cancels on the Unity thread. SDK sign-out also retires its outstanding browser callback.
                using var stop = cancellation.Register(() =>
                {
                    try
                    {
                        browser.SignOut();
                    }
                    finally
                    {
                        result.TrySetResult(AccountSignInStatus.Cancelled);
                    }
                });

                if (cancellation.IsCancellationRequested)
                    return AccountSignInStatus.Cancelled;

                await browser.StartSignInAsync();
                return await result.Task;
            }
            catch (RequestFailedException failure)
            {
                return ClassifyBrowserError(failure);
            }
            finally
            {
                browser.SignedIn -= SignedIn;
                browser.SignInFailed -= Failed;
            }
        }

        private static AccountSignInStatus ClassifyBrowserError(RequestFailedException failure)
            => failure.ErrorCode == PlayerAccountsErrorCodes.MissingClientId
                ? AccountSignInStatus.Unavailable : AccountSignInStatus.Failed;

        private void OnPlayerIdChanged(string playerId)
        {
            if (!StringComparer.Ordinal.Equals(verifiedId, playerId))
                Invalidate();
        }

        private void Invalidate()
        {
            if (verifiedId == null)
                return;

            DetachSession();
            SessionInvalidated?.Invoke();
        }

        private void DetachSession()
        {
            if (verifiedId == null)
                return;

            verifiedId = null;
            authentication!.Expired -= Invalidate;
            authentication.SignedOut -= Invalidate;
            authentication.PlayerIdChanged -= OnPlayerIdChanged;
            playerAccounts!.SignedOut -= Invalidate;
        }
    }
}
