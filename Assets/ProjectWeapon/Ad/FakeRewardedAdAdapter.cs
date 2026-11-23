#nullable enable

using System;
using System.Collections.Generic;

namespace ProjectWeapon.Ad
{
    /// <summary>In-memory test adapter that receives terminal callbacks directly from the caller.</summary>
    public sealed class FakeRewardedAdAdapter : IRewardedAdAdapter
    {
        private readonly object sync = new object();
        private readonly Dictionary<Guid, RewardedAdOutcome> requests = new Dictionary<Guid, RewardedAdOutcome>();
        private readonly HashSet<string> usedCompletionIds = new HashSet<string>(StringComparer.Ordinal);

        public event Action<RewardedAdResult>? OutcomeReported;

        public bool TryBegin(Guid requestId)
        {
            if (requestId == Guid.Empty)
                return false;

            lock (sync)
                return requests.TryAdd(requestId, RewardedAdOutcome.None);
        }

        public bool TryInjectCallback(Guid requestId, RewardedAdOutcome outcome, string? verifiedCompletionId = null)
        {
            if (!RewardedAdResult.TryCreate(requestId, outcome, verifiedCompletionId, out RewardedAdResult result))
                return false;

            lock (sync)
            {
                if (!requests.TryGetValue(requestId, out RewardedAdOutcome current) || current != RewardedAdOutcome.None)
                    return false;

                if (result.VerifiedCompletion is AdCompletionVerified evidence && !usedCompletionIds.Add(evidence.CompletionId))
                    return false;

                requests[requestId] = outcome;
            }

            OutcomeReported?.Invoke(result);
            return true;
        }
    }
}
