#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Extraction;
using ProjectWeapon.Run;

namespace ProjectWeapon.Dungeon
{
    public readonly struct ZoneCountdownSample
    {
        internal ZoneCountdownSample(RunId runId, int zoneId, TimeSpan remaining)
        {
            RunId = runId;
            ZoneId = zoneId;
            Remaining = remaining;
        }

        public RunId RunId { get; }
        public int ZoneId { get; }
        public TimeSpan Remaining { get; }
    }

    /// <summary>Starts runs with their configured limits and drives one expedition frame.</summary>
    public sealed class ZoneRunCountdown
    {
        private readonly ZoneCatalog catalog;
        private readonly RunState run;

        public ZoneRunCountdown(ZoneCatalog catalog, RunState run)
        {
            this.catalog = catalog != null ? catalog : throw new ArgumentNullException(nameof(catalog));
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            Extraction = new ExtractionChannel(run);
        }

        public ExtractionChannel Extraction { get; }

        public bool TryStartRun(RunId runId, int zoneId)
        {
            ZoneDefinition zone = catalog.GetZone(zoneId);
            return run.TryStartRun(runId, zone.ZoneId, TimeSpan.FromSeconds(zone.TimeLimitSeconds));
        }

        /// <summary>Passes all observations for the frame, including an empty frame, to the run.</summary>
        public RunResult? ProcessFrame(IEnumerable<RunSignal> frameSignals)
            => Extraction.ProcessFrame(frameSignals);

        /// <summary>Reads the display value using the same monotonic timeline as the run.</summary>
        public ZoneCountdownSample? Sample(TimeSpan observedAt)
        {
            RunSession? session = run.CurrentRun;
            if (session == null)
                return null;

            if (run.Result is RunResult result)
                return new ZoneCountdownSample(result.RunId, result.ZoneId, result.Remaining);

            TimeSpan sampleAt = observedAt < session.StartedAt ? session.StartedAt : observedAt;
            TimeSpan remaining = sampleAt >= session.Deadline ? TimeSpan.Zero : session.Deadline - sampleAt;
            return new ZoneCountdownSample(session.Id, session.ZoneId, remaining);
        }
    }
}
