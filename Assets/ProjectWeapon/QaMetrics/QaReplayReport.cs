#nullable enable

using System;

namespace ProjectWeapon.QaMetrics
{
    public sealed class QaReplayWindow
    {
        public QaReplayWindow(Guid scope, int contractVersion, TimeSpan start, TimeSpan end, TimeSpan observedUntil,
            TimeSpan? postLootWindow = null, int? highGrade = null, TimeSpan? redeployWindow = null)
        {
            if (scope == Guid.Empty)
                throw new ArgumentException("A synthetic scope ID is required.", nameof(scope));
            if (contractVersion != QaMetricEvent.ContractVersion)
                throw new ArgumentOutOfRangeException(nameof(contractVersion));
            if (start < TimeSpan.Zero || end <= start || observedUntil < end)
                throw new ArgumentException("The report and observation windows must be ordered.");
            if (postLootWindow < TimeSpan.Zero || redeployWindow < TimeSpan.Zero || highGrade is < 1 or > 5)
                throw new ArgumentException("Invalid QA fixture parameter.");

            Scope = scope;
            Start = start;
            End = end;
            ObservedUntil = observedUntil;
            PostLootWindow = postLootWindow;
            HighGrade = highGrade;
            RedeployWindow = redeployWindow;
        }

        public Guid Scope { get; }
        public TimeSpan Start { get; }
        public TimeSpan End { get; }
        public TimeSpan ObservedUntil { get; }
        public TimeSpan? PostLootWindow { get; }
        public int? HighGrade { get; }
        public TimeSpan? RedeployWindow { get; }

        internal bool Includes(TimeSpan time) => time >= Start && time < End;
        internal bool HasObserved(TimeSpan since, TimeSpan horizon) => ObservedUntil - since > horizon;
    }

    public readonly struct QaRatio
    {
        internal QaRatio(int numerator, int denominator)
        {
            Numerator = numerator;
            Denominator = denominator;
        }

        public int Numerator { get; }
        public int Denominator { get; }
        public decimal? Value => Denominator == 0 ? null : (decimal)Numerator / Denominator;
    }

    public readonly struct QaCatalogSpeed
    {
        internal QaCatalogSpeed(int definitions, decimal playedTicks)
        {
            Definitions = definitions;
            PlayedSeconds = playedTicks / TimeSpan.TicksPerSecond;
        }

        public int Definitions { get; }
        public decimal PlayedSeconds { get; }
        public decimal? PerHour => PlayedSeconds == 0 ? null : Definitions * 3600m / PlayedSeconds;
    }

    public sealed class QaReplayReport
    {
        public int AcceptedEvents { get; internal set; }
        public int DuplicateDeliveries { get; internal set; }
        public int ConflictingKeys { get; internal set; }
        public QaRatio ExtractionSuccess { get; internal set; }
        public int PendingSettlements { get; internal set; }
        public QaRatio? PostLootExtraction { get; internal set; }
        public QaRatio? PostLootCancellation { get; internal set; }
        public QaRatio? HighGradeSorties { get; internal set; }
        public QaRatio? RedeployAfterLoss { get; internal set; }
        public decimal CoinsEarned { get; internal set; }
        public decimal CoinsSpent { get; internal set; }
        public decimal CoinNet => CoinsEarned - CoinsSpent;
        public QaRatio AdCompletion { get; internal set; }
        public int PendingAdAttempts { get; internal set; }
        public int AdCancelledBeforeDisplay { get; internal set; }
        public int AdFillFailures { get; internal set; }
        public int InconsistentAdAttempts { get; internal set; }
        public QaCatalogSpeed CatalogRecovery { get; internal set; }
    }
}
