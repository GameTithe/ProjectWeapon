#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ProjectWeapon.AdRewards;

namespace ProjectWeapon.LevelPlayTestAuthority
{
    public readonly struct LevelPlayTestCallbackBinding
    {
        internal LevelPlayTestCallbackBinding(string token, string eventId, string userId, string timestamp)
        {
            ReservationToken = token;
            ProviderEventId = eventId;
            SignedUserId = userId;
            SignedTimestamp = timestamp;
        }

        public string ReservationToken { get; }
        public string ProviderEventId { get; }
        public string SignedUserId { get; }
        public string SignedTimestamp { get; }
    }

    /// <summary>Verifies the dedicated test app's normalized LevelPlay callback query on the trusted host.</summary>
    public sealed class LevelPlayTestProofVerifier : IRewardedAdProofVerifier
    {
        private readonly string authorityScope;
        private readonly string appKey;
        private readonly string privateKey;
        private readonly string reward;
        // Because dynamicUserId is unsigned, the host must match the full callback to the exact presentation.
        private readonly Func<LevelPlayTestCallbackBinding, bool> matchesPresentation;

        public LevelPlayTestProofVerifier(string authorityScope, string appKey, string privateKey, int reward,
            Func<LevelPlayTestCallbackBinding, bool> matchesPresentation)
        {
            if (string.IsNullOrWhiteSpace(authorityScope) || string.IsNullOrWhiteSpace(appKey)
                || string.IsNullOrWhiteSpace(privateKey) || reward <= 0)
                throw new ArgumentException("A dedicated test application, authority scope, callback key and reward are required.");

            this.authorityScope = authorityScope;
            this.appKey = appKey;
            this.privateKey = privateKey;
            this.reward = reward.ToString(CultureInfo.InvariantCulture);
            this.matchesPresentation = matchesPresentation ?? throw new ArgumentNullException(nameof(matchesPresentation));
        }

        public bool TryVerifyLevelPlayCompletion(string signedCallback, out VerifiedAdRewardCompletion completion)
        {
            completion = default;
            if (!TryReadQuery(signedCallback, out Dictionary<string, string> values)
                || !values.TryGetValue("appKey", out string app) || !StringComparer.Ordinal.Equals(appKey, app)
                || !values.TryGetValue("timestamp", out string timestamp) || !IsDigits(timestamp)
                || !values.TryGetValue("eventId", out string eventId) || !IsIdentifier(eventId, 128)
                || !values.TryGetValue("userId", out string userId) || !IsIdentifier(userId, 128)
                || !values.TryGetValue("rewards", out string amount) || !StringComparer.Ordinal.Equals(reward, amount)
                || !values.TryGetValue("dynamicUserId", out string token) || !IsIdentifier(token, 64, alphanumericOnly: true)
                || !values.TryGetValue("signature", out string signature) || !TryReadDigest(signature, out byte[] provided))
                return false;

            // LevelPlay defines this digest for its S2S callbacks. The dedicated test private key stays on the host.
            using var md5 = MD5.Create();
            byte[] expected = md5.ComputeHash(Encoding.UTF8.GetBytes(timestamp + eventId + userId + amount + privateKey));
            if (!CryptographicOperations.FixedTimeEquals(expected, provided)
                || !matchesPresentation(new LevelPlayTestCallbackBinding(token, eventId, userId, timestamp)))
                return false;

            completion = new VerifiedAdRewardCompletion(authorityScope, userId, token, eventId);
            return true;
        }

        private static bool TryReadQuery(string input, out Dictionary<string, string> values)
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(input) || input.Length > 4096)
                return false;

            ReadOnlySpan<char> query = input.AsSpan();
            if (query[0] == '?')
                query = query.Slice(1);
            foreach (string pair in query.ToString().Split('&'))
            {
                int separator = pair.IndexOf('=');
                if (separator <= 0)
                    return false;
                string name = pair.Substring(0, separator);
                string value;
                try
                {
                    value = Uri.UnescapeDataString(pair.Substring(separator + 1).Replace('+', ' '));
                }
                catch (UriFormatException)
                {
                    return false;
                }

                if (!values.TryAdd(name, value))
                    return false;
            }
            return true;
        }

        private static bool IsDigits(string value)
        {
            if (value.Length == 0 || value.Length > 20)
                return false;
            foreach (char c in value)
                if (c is < '0' or > '9')
                    return false;
            return true;
        }

        private static bool IsIdentifier(string value, int maxLength, bool alphanumericOnly = false)
        {
            if (value.Length == 0 || value.Length > maxLength)
                return false;
            foreach (char c in value)
            {
                if (c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z')
                    continue;
                if (alphanumericOnly || c is not ('-' or '_'))
                    return false;
            }
            return true;
        }

        private static bool TryReadDigest(string signature, out byte[] bytes)
        {
            bytes = signature.Length == 32 ? new byte[16] : Array.Empty<byte>();
            if (bytes.Length == 0)
                return false;

            for (int index = 0; index < bytes.Length; index++)
            {
                if (!byte.TryParse(signature.AsSpan(index * 2, 2), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out bytes[index]))
                    return false;
            }
            return true;
        }
    }
}
