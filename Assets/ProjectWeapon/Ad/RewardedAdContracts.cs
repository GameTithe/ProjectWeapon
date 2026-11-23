#nullable enable

using System;

namespace ProjectWeapon.Ad
{
    public enum RewardedAdOutcome
    {
        None = 0,
        LoadFailed,
        Cancelled,
        ClosedWithoutConfirmation,
        Error,
        ConfirmedCompletion
    }

    public readonly struct AdCompletionVerified
    {
        internal AdCompletionVerified(Guid requestId, string completionId)
        {
            RequestId = requestId;
            CompletionId = completionId;
        }

        public Guid RequestId { get; }
        public string CompletionId { get; }
    }

    public readonly struct RewardedAdResult
    {
        private RewardedAdResult(Guid requestId, RewardedAdOutcome outcome, AdCompletionVerified? verifiedCompletion)
        {
            RequestId = requestId;
            Outcome = outcome;
            VerifiedCompletion = verifiedCompletion;
        }

        public Guid RequestId { get; }
        public RewardedAdOutcome Outcome { get; }
        public AdCompletionVerified? VerifiedCompletion { get; }

        /// <summary>Builds a terminal notification once the adapter has checked the supplied evidence.</summary>
        public static bool TryCreate(
            Guid requestId, RewardedAdOutcome outcome, string? verifiedCompletionId, out RewardedAdResult result)
        {
            result = default;
            if (requestId == Guid.Empty)
                return false;

            switch (outcome)
            {
                case RewardedAdOutcome.ConfirmedCompletion when !string.IsNullOrWhiteSpace(verifiedCompletionId):
                    result = new RewardedAdResult(requestId, outcome,
                        new AdCompletionVerified(requestId, verifiedCompletionId!));
                    return true;
                case RewardedAdOutcome.LoadFailed:
                case RewardedAdOutcome.Cancelled:
                case RewardedAdOutcome.ClosedWithoutConfirmation:
                case RewardedAdOutcome.Error:
                    if (verifiedCompletionId != null)
                        return false;

                    result = new RewardedAdResult(requestId, outcome, null);
                    return true;
                default:
                    return false;
            }
        }
    }

    public interface IRewardedAdAdapter
    {
        event Action<RewardedAdResult>? OutcomeReported;

        bool TryBegin(Guid requestId);
    }
}
