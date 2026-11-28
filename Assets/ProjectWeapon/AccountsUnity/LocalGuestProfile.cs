#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.Accounts.Unity
{
    public static class LocalGuestProfile
    {
        private const string PreferenceKey = "ProjectWeapon.LocalGuestProfile.v1";

        /// <summary>Reads or creates the installation-only ID used to select the local guest save.</summary>
        public static GuestProfileId GetOrCreate() => GetOrCreate(PreferenceKey);

        internal static GuestProfileId GetOrCreate(string key)
        {
            if (Guid.TryParseExact(PlayerPrefs.GetString(key, string.Empty), "N", out Guid value) && value != Guid.Empty)
                return new GuestProfileId(value);

            value = Guid.NewGuid();
            PlayerPrefs.SetString(key, value.ToString("N"));
            PlayerPrefs.Save();
            return new GuestProfileId(value);
        }
    }
}
