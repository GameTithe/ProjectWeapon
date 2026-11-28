#nullable enable

using System;

namespace ProjectWeapon.Run
{
    public readonly struct RunCheckpoint
    {
        public RunCheckpoint(RunId runId, int zoneId, TimeSpan startedAt, TimeSpan deadline, TimeSpan observedAt,
            int lastAttemptId, RunEndReason? reason = null, TimeSpan? effectiveAt = null)
        {
            RunId = runId;
            ZoneId = zoneId;
            StartedAt = startedAt;
            Deadline = deadline;
            ObservedAt = observedAt;
            LastAttemptId = lastAttemptId;
            Reason = reason;
            EffectiveAt = effectiveAt;
        }

        public RunId RunId { get; }
        public int ZoneId { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan Deadline { get; }
        public TimeSpan ObservedAt { get; }
        public int LastAttemptId { get; }
        public RunEndReason? Reason { get; }
        public TimeSpan? EffectiveAt { get; }

        internal bool IsValid
        {
            get
            {
                if (!RunId.IsValid || ZoneId <= 0 || StartedAt < TimeSpan.Zero || Deadline <= StartedAt
                    || ObservedAt < StartedAt || LastAttemptId < 0 || LastAttemptId == int.MaxValue)
                    return false;
                if (Reason is not { } reason)
                    return !EffectiveAt.HasValue;
                if (EffectiveAt is not { } resultAt || resultAt < StartedAt || resultAt > ObservedAt)
                    return false;

                return reason switch
                {
                    RunEndReason.Extracted => LastAttemptId > 0 && resultAt < Deadline
                        && resultAt - StartedAt >= RunState.ExtractionDuration,
                    RunEndReason.Death => resultAt < Deadline,
                    RunEndReason.TimeExpired => resultAt == Deadline,
                    _ => false
                };
            }
        }
    }
}
