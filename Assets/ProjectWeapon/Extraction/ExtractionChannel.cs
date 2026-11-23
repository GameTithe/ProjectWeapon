#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Run;

namespace ProjectWeapon.Extraction
{
    public readonly struct ExtractionChannelProgress
    {
        internal ExtractionChannelProgress(RunId runId, int attemptId, float fraction)
        {
            RunId = runId;
            AttemptId = attemptId;
            Fraction = fraction;
        }

        public RunId RunId { get; }
        public int AttemptId { get; }
        public float Fraction { get; }
    }

    /// <summary>Drives the active extraction attempt through the owning run's frame boundary.</summary>
    public sealed class ExtractionChannel
    {
        private readonly RunState run;
        private RunId requestedRunId;
        private int requestedAttemptId;

        public ExtractionChannel(RunState run)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
        }

        public bool IsAutomaticAttackSuppressed => run.Phase == RunPhase.Extracting;
        public ExtractionChannelProgress? Progress { get; private set; }

        public event Action<ExtractionChannelProgress?>? ProgressChanged;

        /// <summary>Resolves the complete frame, including scheduled completion when due.</summary>
        public RunResult? ProcessFrame(IEnumerable<RunSignal> frameSignals)
        {
            RunId runId = run.CurrentRun?.Id ?? default;
            ExtractionAttempt? attempt = run.ActiveExtraction;
            TimeSpan frameAt = TimeSpan.Zero;
            bool requested = false;

            RunResult? result = run.ProcessSignals(frameSignals, now =>
            {
                frameAt = now;
                if (attempt == null || now < attempt.CompletesAt
                    || (requestedRunId == runId && requestedAttemptId == attempt.Id))
                    return null;

                requested = true;
                return RunSignal.CompleteExtraction(runId, attempt.Id, attempt.CompletesAt);
            });

            if (requested)
            {
                requestedRunId = runId;
                requestedAttemptId = attempt!.Id;
            }

            if (attempt != null && ReferenceEquals(attempt, run.ActiveExtraction))
            {
                double elapsed = (frameAt - attempt.StartedAt).Ticks;
                double duration = (attempt.CompletesAt - attempt.StartedAt).Ticks;
                float fraction = (float)Math.Max(0, Math.Min(1, elapsed / duration));
                Publish(new ExtractionChannelProgress(runId, attempt.Id, fraction));
            }
            else if (result != null && result.RunId == run.CurrentRun?.Id && result.Succeeded)
            {
                if (attempt != null)
                    Publish(new ExtractionChannelProgress(runId, attempt.Id, 1));
            }
            else
            {
                Publish(null);
            }

            return result;
        }

        private void Publish(ExtractionChannelProgress? sample)
        {
            ExtractionChannelProgress? previous = Progress;
            if (!sample.HasValue && !previous.HasValue)
                return;
            if (sample.HasValue && previous.HasValue && sample.Value.RunId == previous.Value.RunId
                && sample.Value.AttemptId == previous.Value.AttemptId && sample.Value.Fraction == previous.Value.Fraction)
                return;

            Progress = sample;
            ProgressChanged?.Invoke(sample);
        }
    }
}
