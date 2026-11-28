#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Accounts;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace ProjectWeapon.LevelPlayTest
{
    /// <summary>Starts the native SDK only for an explicit development test app and authenticated account.</summary>
    public static class LevelPlayTestRuntime
    {
        private static string? appKey;
        private static string? signedUserId;
        private static bool initializing;
        private static bool ready;
        private static LevelPlayTestRewardedAdapter? adapter;
        private static readonly HashSet<string> UsedReservationTokens = new HashSet<string>(StringComparer.Ordinal);

        public static bool TryInitialize(AccountSessionBoundary accounts, OnlineAccountSession session, string testAppKey)
        {
            if (!IsMobileDevelopmentBuild() || accounts == null || session == null || !accounts.IsCurrent(session)
                || string.IsNullOrWhiteSpace(testAppKey))
                return false;
            if (appKey != null && (!StringComparer.Ordinal.Equals(appKey, testAppKey)
                || !StringComparer.Ordinal.Equals(signedUserId, session.PlayerId)))
                return false;
            if (ready || initializing)
                return true;

            appKey = testAppKey;
            signedUserId = session.PlayerId;
            initializing = true;
            LevelPlay.OnInitSuccess += OnInitialized;
            LevelPlay.OnInitFailed += OnInitializationFailed;
            LevelPlay.Init(testAppKey, session.PlayerId);
            return true;
        }

        public static LevelPlayTestRewardedAdapter? CreateAdapter(AccountSessionBoundary accounts,
            OnlineAccountSession session, string testAdUnitId, ILevelPlayTestAuthority authority,
            string? placement = null)
        {
            if (!IsMobileDevelopmentBuild() || !ready || accounts == null || session == null || !accounts.IsCurrent(session)
                || !StringComparer.Ordinal.Equals(session.PlayerId, signedUserId)
                || string.IsNullOrWhiteSpace(testAdUnitId) || authority == null || adapter != null)
                return null;

            LevelPlayTestRewardedAdapter? created = null;
            void StopWhenRevoked(AccountMode _)
            {
                if (!accounts.IsCurrent(session))
                    created?.Dispose();
            }

            created = new LevelPlayTestRewardedAdapter(session, () => accounts.IsCurrent(session),
                () => new LevelPlayRewardedAd(testAdUnitId), LevelPlay.SetDynamicUserId, authority, placement,
                () => Time.realtimeSinceStartupAsDouble, UsedReservationTokens.Add, () =>
                {
                    accounts.ModeChanged -= StopWhenRevoked;
                    adapter = null;
                });
            adapter = created;
            accounts.ModeChanged += StopWhenRevoked;
            return created;
        }

        private static bool IsMobileDevelopmentBuild() => !Application.isEditor && Debug.isDebugBuild
            && Application.platform is RuntimePlatform.Android or RuntimePlatform.IPhonePlayer;

        private static void OnInitialized(LevelPlayConfiguration _) => FinishInitialization(true);
        private static void OnInitializationFailed(LevelPlayInitError _) => FinishInitialization(false);

        private static void FinishInitialization(bool succeeded)
        {
            LevelPlay.OnInitSuccess -= OnInitialized;
            LevelPlay.OnInitFailed -= OnInitializationFailed;
            initializing = false;
            ready = succeeded;
        }
    }
}
