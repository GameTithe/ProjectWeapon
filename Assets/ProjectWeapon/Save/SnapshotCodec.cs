#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ProjectWeapon.Inventory;
using UnityEngine;

namespace ProjectWeapon.Save
{
    internal static class SnapshotCodec
    {
        private const int FormatVersion = 1;
        private const int SyntheticTestVersion = -1;
        private const string SyntheticFixtureId = "ProjectWeapon.KAN82.TestFixture";
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static (byte[] bytes, string checksum) Encode(SaveSnapshot snapshot)
        {
            snapshot.Validate();
            string payload = JsonUtility.ToJson(snapshot);
            string checksum = Checksum(payload);
            var envelope = new Envelope
            {
                formatVersion = FormatVersion,
                payload = payload,
                checksum = checksum
            };
            return (Utf8.GetBytes(JsonUtility.ToJson(envelope)), checksum);
        }

        internal static (SaveSnapshot snapshot, string checksum) Decode(string json)
        {
            Envelope envelope = Parse<Envelope>(json);
            if (envelope.formatVersion != FormatVersion)
                throw UnsupportedVersion(envelope.formatVersion);

            return DecodeCurrent(envelope);
        }

        internal static (SaveSnapshot? snapshot, int formatVersion) DecodeForRecovery(string json)
        {
            Envelope envelope = Parse<Envelope>(json);
            if (envelope.formatVersion == FormatVersion)
                return (DecodeCurrent(envelope).snapshot, envelope.formatVersion);

            ValidateChecksum(envelope);
            if (envelope.formatVersion == 0)
                throw new InvalidDataException("The snapshot format version is missing.");

            return (null, envelope.formatVersion);
        }

        internal static (SaveSnapshot snapshot, bool requiresUpgrade) DecodeUpgradeInput(string json)
        {
            Envelope envelope = Parse<Envelope>(json);
            if (envelope.formatVersion == FormatVersion)
                return (DecodeCurrent(envelope).snapshot, false);

            if (envelope.formatVersion != SyntheticTestVersion)
                throw UnsupportedVersion(envelope.formatVersion);

            ValidateChecksum(envelope);
            var fixture = Parse<SyntheticTestSnapshot>(envelope.payload);
            if (!StringComparer.Ordinal.Equals(fixture.fixtureId, SyntheticFixtureId))
                throw new InvalidDataException("The previous test format requires the KAN-82 fixture marker.");

            if (!long.TryParse(fixture.coins, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long coins))
                throw new InvalidDataException("The synthetic snapshot coin balance is not a valid integer.");

            if (fixture.weapons == null || fixture.catalog == null)
                throw new InvalidDataException("The synthetic snapshot is missing a required collection.");

            var snapshot = new SaveSnapshot(fixture.revision, coins, fixture.weapons, fixture.catalog,
                fixture.settlements ?? Array.Empty<SettlementSaveRecord>());
            return (snapshot, true);
        }

        private static (SaveSnapshot snapshot, string checksum) DecodeCurrent(Envelope envelope)
        {
            ValidateChecksum(envelope);
            SaveSnapshot snapshot = Parse<SaveSnapshot>(envelope.payload);
            snapshot.Validate();
            return (snapshot, envelope.checksum);
        }

        private static void ValidateChecksum(Envelope envelope)
        {
            if (string.IsNullOrEmpty(envelope.payload) ||
                !StringComparer.Ordinal.Equals(envelope.checksum, Checksum(envelope.payload)))
                throw new InvalidDataException("The snapshot payload is incomplete or its checksum does not match.");
        }

        private static InvalidDataException UnsupportedVersion(int version) =>
            new InvalidDataException($"Unsupported snapshot format version {version}.");

        private static T Parse<T>(string value) where T : class
        {
            if (!value.TrimEnd().EndsWith("}", StringComparison.Ordinal))
                throw new InvalidDataException("The snapshot JSON object is incomplete.");

            try
            {
                return JsonUtility.FromJson<T>(value) ??
                    throw new InvalidDataException("The snapshot JSON is missing an object.");
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException("The snapshot JSON is invalid.", error);
            }
        }

        private static string Checksum(string payload)
        {
            using SHA256 sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Utf8.GetBytes(payload)));
        }

        [Serializable]
        private sealed class Envelope
        {
            public int formatVersion;
            public string payload = string.Empty;
            public string checksum = string.Empty;
        }

        [Serializable]
        private sealed class SyntheticTestSnapshot
        {
            public string fixtureId = string.Empty;
            public long revision = 0;
            public string coins = string.Empty;
            public WeaponInstance[]? weapons = null;
            public CatalogSaveRecord[]? catalog = null;
            public SettlementSaveRecord[]? settlements = null;
        }
    }
}
