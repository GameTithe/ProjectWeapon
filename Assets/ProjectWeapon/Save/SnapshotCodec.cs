#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ProjectWeapon.Save
{
    internal static class SnapshotCodec
    {
        private const int FormatVersion = 1;
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
                throw new InvalidDataException($"Unsupported snapshot format version {envelope.formatVersion}.");

            if (string.IsNullOrEmpty(envelope.payload) ||
                !StringComparer.Ordinal.Equals(envelope.checksum, Checksum(envelope.payload)))
                throw new InvalidDataException("The snapshot payload is incomplete or its checksum does not match.");

            SaveSnapshot snapshot = Parse<SaveSnapshot>(envelope.payload);
            snapshot.Validate();
            return (snapshot, envelope.checksum);
        }

        private static T Parse<T>(string value) where T : class
        {
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
    }
}
