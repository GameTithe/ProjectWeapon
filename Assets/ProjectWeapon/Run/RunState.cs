#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectWeapon.Run
{
    /// <summary>Resolves a single local expedition on its owning gameplay thread.</summary>
    public sealed class RunState
    {
        public static readonly TimeSpan ExtractionDuration = TimeSpan.FromSeconds(3);

        private readonly Func<TimeSpan> readClock;
        private readonly HashSet<RunId> usedRunIds = new HashSet<RunId>();
        private TimeSpan lastObservedTime;
        private TimeSpan processedThrough;
        private int lastAttemptId;

        public RunState(Func<TimeSpan> readClock)
        {
            this.readClock = readClock ?? throw new ArgumentNullException(nameof(readClock));
        }

        public RunPhase Phase { get; private set; } = RunPhase.Town;
        public RunSession? CurrentRun { get; private set; }
        public ExtractionAttempt? ActiveExtraction { get; private set; }
        public RunResult? Result { get; private set; }

        public event Action<RunResult>? RunResultFinalized;

        // The caller supplies the ID reserved for this sortie and validates access to the zone.
        public bool TryStartRun(RunId id, int zoneId, TimeSpan timeLimit)
        {
            if (Phase != RunPhase.Town || !id.IsValid || usedRunIds.Contains(id)
                || zoneId <= 0 || timeLimit <= TimeSpan.Zero)
                return false;

            TimeSpan now = ReadTime();
            if (timeLimit > TimeSpan.MaxValue - now)
                return false;

            usedRunIds.Add(id);
            CurrentRun = new RunSession(id, zoneId, now, now + timeLimit);
            processedThrough = now;
            lastAttemptId = 0;
            Phase = RunPhase.Exploring;
            return true;
        }

        // The adapter verifies that the player can interact with the extraction point.
        public ExtractionAttempt? TryBeginExtraction(RunId id, bool isInRange)
        {
            RunSession? session = CurrentRun;
            if (Phase != RunPhase.Exploring || session == null || session.Id != id || !isInRange)
                return null;

            TimeSpan now = ReadTime();
            if (now >= session.Deadline || now > TimeSpan.MaxValue - ExtractionDuration)
                return null;

            var attempt = new ExtractionAttempt(checked(++lastAttemptId), now, now + ExtractionDuration);
            ActiveExtraction = attempt;
            Phase = RunPhase.Extracting;
            return attempt;
        }

        /// <summary>
        /// Resolves one frame's observations together. A late callback carries its effective
        /// time, such as the scheduled completion time, so callback order does not choose the result.
        /// </summary>
        public RunResult? ProcessSignals(IEnumerable<RunSignal> signals)
        {
            if (signals == null)
                throw new ArgumentNullException(nameof(signals));

            RunSession? session = CurrentRun;
            if (session == null || Result != null)
                return Result;

            TimeSpan now = ReadTime();
            IEnumerable<RunSignal> frame = signals;
            if (now >= session.Deadline)
                frame = frame.Append(RunSignal.DeadlineReached(session.Id, session.Deadline));

            var ordered = frame
                .Where(signal => signal.RunId == session.Id
                    && signal.EffectiveAt >= processedThrough
                    && signal.EffectiveAt <= now
                    && signal.EffectiveAt <= session.Deadline)
                .OrderBy(signal => signal.EffectiveAt)
                .ThenBy(signal => signal.Kind)
                .ToArray();
            processedThrough = now;

            foreach (RunSignal signal in ordered)
            {
                // Scheduled expiry wins at zero. For earlier same-time events, death and
                // cancellation precede extraction completion in the prototype ordering.
                switch (signal.Kind)
                {
                    case RunSignalKind.DeadlineReached when signal.EffectiveAt == session.Deadline:
                        return FinalizeResult(session, RunEndReason.TimeExpired, session.Deadline);
                    case RunSignalKind.PlayerDied when signal.EffectiveAt < session.Deadline:
                        return FinalizeResult(session, RunEndReason.Death, signal.EffectiveAt);
                    case RunSignalKind.ExtractionCancelled when MatchesActiveExtraction(signal):
                        ActiveExtraction = null;
                        Phase = RunPhase.Exploring;
                        break;
                    case RunSignalKind.ExtractionCompleted when MatchesActiveExtraction(signal)
                        && signal.EffectiveAt == ActiveExtraction!.CompletesAt
                        && signal.EffectiveAt < session.Deadline:
                        return FinalizeResult(session, RunEndReason.Extracted, signal.EffectiveAt);
                }
            }

            return null;
        }

        // The caller decides when settlement/UI allows the completed run to leave the results screen.
        public bool TryReturnToTown(RunId id)
        {
            if (Result == null || Result.RunId != id)
                return false;

            CurrentRun = null;
            Result = null;
            Phase = RunPhase.Town;
            return true;
        }

        private bool MatchesActiveExtraction(RunSignal signal)
        {
            ExtractionAttempt? attempt = ActiveExtraction;
            return attempt != null && signal.AttemptId == attempt.Id && signal.EffectiveAt >= attempt.StartedAt;
        }

        private RunResult FinalizeResult(RunSession session, RunEndReason reason, TimeSpan effectiveAt)
        {
            var result = new RunResult(session, reason, effectiveAt);
            ActiveExtraction = null;
            Result = result;
            Phase = result.Succeeded ? RunPhase.Succeeded : RunPhase.Failed;
            RunResultFinalized?.Invoke(result);
            return result;
        }

        private TimeSpan ReadTime()
        {
            TimeSpan now = readClock();
            if (now < lastObservedTime)
                throw new InvalidOperationException("The run clock must be non-negative and monotonic.");

            lastObservedTime = now;
            return now;
        }
    }
}
