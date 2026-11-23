using System;

namespace ProjectWeapon.ExtractionPresentation
{
    public enum ExtractionFeedbackState
    {
        Idle,
        Available,
        Channeling,
        Succeeded
    }

    public readonly struct ExtractionFeedbackSnapshot
    {
        internal ExtractionFeedbackSnapshot(ExtractionFeedbackState state, float progress)
        {
            State = state;
            Progress = progress;
        }

        public ExtractionFeedbackState State { get; }
        public float Progress { get; }
    }

    /// <summary>Projects accepted run signals onto presentation state without authorizing actions.</summary>
    public sealed class ExtractionFeedback
    {
        private Guid runId;
        private bool available;
        private bool finalized;
        private bool succeeded;
        private int latestAttemptId;
        private bool channeling;
        private TimeSpan startedAt;
        private TimeSpan completesAt;

        public void OnRunStarted(Guid id)
        {
            if (id == Guid.Empty || id == runId)
                return;

            runId = id;
            available = false;
            finalized = false;
            succeeded = false;
            latestAttemptId = 0;
            channeling = false;
        }

        public void SetAvailability(Guid id, bool canStart)
        {
            if (MatchesLiveRun(id))
                available = canStart;
        }

        public void OnExtractionStarted(Guid id, int attemptId, TimeSpan start, TimeSpan end)
        {
            if (!MatchesLiveRun(id) || attemptId <= latestAttemptId || start < TimeSpan.Zero || end <= start)
                return;

            latestAttemptId = attemptId;
            startedAt = start;
            completesAt = end;
            channeling = true;
        }

        public void OnExtractionCancelled(Guid id, int attemptId)
        {
            if (MatchesLiveRun(id) && channeling && attemptId == latestAttemptId)
                channeling = false;
        }

        /// <returns>Whether this notification was the first accepted result for the displayed run.</returns>
        public bool OnRunResultFinalized(Guid id, bool wasSuccessful)
        {
            if (!MatchesLiveRun(id))
                return false;

            finalized = true;
            succeeded = wasSuccessful;
            channeling = false;
            available = false;
            return true;
        }

        public ExtractionFeedbackSnapshot Sample(TimeSpan now)
        {
            if (succeeded)
                return new ExtractionFeedbackSnapshot(ExtractionFeedbackState.Succeeded, 0);

            if (channeling)
            {
                float progress = now <= startedAt ? 0
                    : now >= completesAt ? 1
                    : (float)((now - startedAt).Ticks / (double)(completesAt - startedAt).Ticks);
                return new ExtractionFeedbackSnapshot(ExtractionFeedbackState.Channeling, progress);
            }

            var state = available ? ExtractionFeedbackState.Available : ExtractionFeedbackState.Idle;
            return new ExtractionFeedbackSnapshot(state, 0);
        }

        private bool MatchesLiveRun(Guid id) => !finalized && id != Guid.Empty && id == runId;
    }
}
