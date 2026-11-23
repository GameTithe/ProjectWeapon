using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectWeapon.Balance
{
    [Serializable]
    public sealed class BalanceDistribution
    {
        public const int RequiredTotalBasisPoints = 10_000;

        [SerializeField] private string key = string.Empty;
        [SerializeField] private BalanceApproval approval = BalanceApproval.Draft;
        [SerializeField] private WeightedOutcome[] outcomes = Array.Empty<WeightedOutcome>();

        public string Key => key;
        public BalanceApproval Approval => approval;
        public IReadOnlyList<WeightedOutcome> Outcomes => Array.AsReadOnly(outcomes);

        internal void Validate()
        {
            if (outcomes == null || outcomes.Length == 0)
                throw Invalid("at least one outcome is required");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            for (int index = 0; index < outcomes.Length; index++)
            {
                WeightedOutcome outcome = outcomes[index];
                if (outcome == null || string.IsNullOrWhiteSpace(outcome.Id) || outcome.Id != outcome.Id.Trim())
                    throw Invalid($"outcome {index} requires an ID without surrounding whitespace");
                if (!ids.Add(outcome.Id))
                    throw Invalid($"duplicate outcome ID '{outcome.Id}'");
                if (outcome.BasisPoints < 0 || outcome.BasisPoints > RequiredTotalBasisPoints)
                    throw Invalid($"outcome '{outcome.Id}' must be between 0 and 10,000 basis points");

                total += outcome.BasisPoints;
            }

            if (total != RequiredTotalBasisPoints)
                throw Invalid($"weights must total 10,000 basis points (got {total})");
        }

        private InvalidOperationException Invalid(string issue) =>
            new InvalidOperationException($"Balance distribution '{key}': {issue}.");
    }

    [Serializable]
    public sealed class WeightedOutcome
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private int basisPoints = 0;

        public string Id => id;
        public int BasisPoints => basisPoints;
    }
}
