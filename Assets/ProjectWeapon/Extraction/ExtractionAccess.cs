#nullable enable

using System;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;

namespace ProjectWeapon.Extraction
{
    /// <summary>Checks a configured extraction point against the current player's world position.</summary>
    public sealed class ExtractionAccess
    {
        public const float MovementCancelThreshold = 0.20f;

        private readonly RunState run;
        private readonly int zoneId;
        private readonly WorldPoint2D center;
        private readonly double radiusSquared;

        public ExtractionAccess(RunState run, int zoneId, WorldPoint2D center, float radius)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            if (zoneId <= 0)
                throw new ArgumentOutOfRangeException(nameof(zoneId));
            if (!center.IsFinite)
                throw new ArgumentException("The extraction center must be finite.", nameof(center));
            if (!float.IsFinite(radius) || radius <= 0)
                throw new ArgumentOutOfRangeException(nameof(radius));

            this.zoneId = zoneId;
            this.center = center;
            radiusSquared = (double)radius * radius;
        }

        public bool CanStart(RunId runId, WorldPoint2D playerPosition)
            => run.CanBeginExtraction(runId, IsInRange(playerPosition));

        public bool CanStart(RunId runId, WorldPoint2D playerPosition, float acceptedMoveMagnitude)
            => !HasCancelMovement(acceptedMoveMagnitude) && CanStart(runId, playerPosition);

        /// <summary>Call once for a button press, with the player's current position.</summary>
        public ExtractionAttempt? TryStart(RunId runId, WorldPoint2D playerPosition)
            => run.TryBeginExtraction(runId, IsInRange(playerPosition));

        public ExtractionAttempt? TryStart(RunId runId, WorldPoint2D playerPosition, float acceptedMoveMagnitude)
            => HasCancelMovement(acceptedMoveMagnitude) ? null : TryStart(runId, playerPosition);

        internal static bool HasCancelMovement(float acceptedMoveMagnitude)
            => float.IsFinite(acceptedMoveMagnitude) && acceptedMoveMagnitude >= MovementCancelThreshold;

        private bool IsInRange(WorldPoint2D playerPosition)
            => run.CurrentRun?.ZoneId == zoneId && playerPosition.IsFinite
                && center.SquaredDistanceTo(playerPosition) <= radiusSquared;
    }
}
