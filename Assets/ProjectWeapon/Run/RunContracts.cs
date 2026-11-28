#nullable enable

using System;

namespace ProjectWeapon.Run
{
    public readonly struct RunId : IEquatable<RunId>
    {
        public RunId(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("A run ID must be non-empty.", nameof(value));

            Value = value;
        }

        public Guid Value { get; }
        public bool IsValid => Value != Guid.Empty;

        public static RunId New() => new RunId(Guid.NewGuid());
        public bool Equals(RunId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is RunId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString("D");

        public static bool operator ==(RunId left, RunId right) => left.Equals(right);
        public static bool operator !=(RunId left, RunId right) => !left.Equals(right);
    }

    public enum RunPhase
    {
        Town,
        Exploring,
        Extracting,
        Succeeded,
        Failed
    }

    public enum RunEndReason
    {
        Extracted,
        Death,
        TimeExpired
    }

    public enum ExtractionCancelReason
    {
        Unspecified,
        Movement,
        Damage
    }

    public readonly struct ExtractionCancellation
    {
        internal ExtractionCancellation(RunId runId, int attemptId, ExtractionCancelReason reason, TimeSpan effectiveAt)
        {
            RunId = runId;
            AttemptId = attemptId;
            Reason = reason;
            EffectiveAt = effectiveAt;
        }

        public RunId RunId { get; }
        public int AttemptId { get; }
        public ExtractionCancelReason Reason { get; }
        public TimeSpan EffectiveAt { get; }
    }

    public sealed class RunSession
    {
        internal RunSession(RunId id, int zoneId, TimeSpan startedAt, TimeSpan deadline)
        {
            Id = id;
            ZoneId = zoneId;
            StartedAt = startedAt;
            Deadline = deadline;
        }

        public RunId Id { get; }
        public int ZoneId { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan Deadline { get; }
    }

    public sealed class ExtractionAttempt
    {
        internal ExtractionAttempt(int id, TimeSpan startedAt, TimeSpan completesAt)
        {
            Id = id;
            StartedAt = startedAt;
            CompletesAt = completesAt;
        }

        public int Id { get; }
        public TimeSpan StartedAt { get; }
        public TimeSpan CompletesAt { get; }
    }

    public sealed class RunResult
    {
        internal RunResult(RunSession session, RunEndReason reason, TimeSpan effectiveAt)
        {
            RunId = session.Id;
            ZoneId = session.ZoneId;
            Reason = reason;
            EffectiveAt = effectiveAt;
            Remaining = session.Deadline - effectiveAt;
        }

        public RunId RunId { get; }
        public int ZoneId { get; }
        public RunEndReason Reason { get; }
        public bool Succeeded => Reason == RunEndReason.Extracted;
        public TimeSpan EffectiveAt { get; }
        public TimeSpan Remaining { get; }
    }

    internal enum RunSignalKind
    {
        None,
        DeadlineReached,
        PlayerDied,
        ExtractionCancelled,
        ExtractionCompleted
    }

    public readonly struct RunSignal
    {
        private RunSignal(RunId runId, RunSignalKind kind, TimeSpan effectiveAt, int attemptId = 0,
            ExtractionCancelReason cancelReason = ExtractionCancelReason.Unspecified)
        {
            RunId = runId;
            Kind = kind;
            EffectiveAt = effectiveAt;
            AttemptId = attemptId;
            CancelReason = cancelReason;
        }

        public RunId RunId { get; }
        public TimeSpan EffectiveAt { get; }
        internal RunSignalKind Kind { get; }
        internal int AttemptId { get; }
        internal ExtractionCancelReason CancelReason { get; }

        public static RunSignal DeadlineReached(RunId runId, TimeSpan deadline)
            => new RunSignal(runId, RunSignalKind.DeadlineReached, deadline);

        public static RunSignal PlayerDied(RunId runId, TimeSpan effectiveAt)
            => new RunSignal(runId, RunSignalKind.PlayerDied, effectiveAt);

        public static RunSignal CancelExtraction(RunId runId, int attemptId, TimeSpan effectiveAt)
            => CancelExtraction(runId, attemptId, effectiveAt, ExtractionCancelReason.Unspecified);

        public static RunSignal CancelExtraction(RunId runId, int attemptId, TimeSpan effectiveAt,
            ExtractionCancelReason reason)
            => new RunSignal(runId, RunSignalKind.ExtractionCancelled, effectiveAt, attemptId, reason);

        public static RunSignal CompleteExtraction(RunId runId, int attemptId, TimeSpan completesAt)
            => new RunSignal(runId, RunSignalKind.ExtractionCompleted, completesAt, attemptId);
    }
}
