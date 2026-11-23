using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectWeapon.Balance
{
    public enum BalanceApproval
    {
        Draft = 0,
        Approved = 1
    }

    public enum BalanceRangeKind
    {
        TimeLimitSeconds = 0,
        DamagePerSecond = 1,
        CoinSupply = 2,
        BagCapacity = 3
    }

    [CreateAssetMenu(menuName = "ProjectWeapon/Balance Settings")]
    public sealed class BalanceSettings : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private int revision = 1;
        [SerializeField] private BalanceRange[] ranges = Array.Empty<BalanceRange>();
        [SerializeField] private BalanceDistribution[] distributions = Array.Empty<BalanceDistribution>();

        public int SchemaVersion => schemaVersion;
        public int Revision => revision;

        public IReadOnlyList<BalanceRange> GetRanges()
        {
            Validate();
            return Array.AsReadOnly(ranges);
        }

        public IReadOnlyList<BalanceDistribution> GetDistributions()
        {
            Validate();
            return Array.AsReadOnly(distributions);
        }

        public void Validate()
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw Invalid($"schema version {schemaVersion} is unsupported (expected {CurrentSchemaVersion})");
            if (revision <= 0)
                throw Invalid("revision must be positive");
            if (ranges == null || distributions == null)
                throw Invalid("range and distribution lists must be present");

            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < ranges.Length; index++)
            {
                BalanceRange range = ranges[index];
                if (range == null)
                    throw Invalid($"range {index} is missing");

                ValidateKeyAndApproval(range.Key, range.Approval, keys, $"range {index}");
                range.Validate();
            }

            for (int index = 0; index < distributions.Length; index++)
            {
                BalanceDistribution distribution = distributions[index];
                if (distribution == null)
                    throw Invalid($"distribution {index} is missing");

                ValidateKeyAndApproval(distribution.Key, distribution.Approval, keys, $"distribution {index}");
                distribution.Validate();
            }
        }

        private void ValidateKeyAndApproval(
            string key, BalanceApproval approval, HashSet<string> keys, string location)
        {
            if (string.IsNullOrWhiteSpace(key) || key != key.Trim())
                throw Invalid($"{location} requires a key without surrounding whitespace");
            if (!keys.Add(key))
                throw Invalid($"duplicate setting key '{key}'");
            if (approval != BalanceApproval.Draft && approval != BalanceApproval.Approved)
                throw Invalid($"setting '{key}' has an unsupported approval status");
        }

        private InvalidOperationException Invalid(string issue) =>
            new InvalidOperationException($"Balance settings '{name}': {issue}.");
    }
}
