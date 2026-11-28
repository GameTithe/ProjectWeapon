#nullable enable

using System;
using System.Globalization;
using UnityEngine;

namespace ProjectWeapon.Save
{
    /// <summary>A permanent wallet receipt for one recovered-catalog threshold.</summary>
    [Serializable]
    public sealed class CatalogMilestoneSaveRecord
    {
        [SerializeField] private int recoveredThreshold;
        [SerializeField] private long awardedCoins;
        [SerializeField] private string sourceRunId;

        public CatalogMilestoneSaveRecord(int recoveredThreshold, long awardedCoins, Guid sourceRunId)
        {
            if (recoveredThreshold <= 0)
                throw new ArgumentOutOfRangeException(nameof(recoveredThreshold));
            if (awardedCoins <= 0)
                throw new ArgumentOutOfRangeException(nameof(awardedCoins));
            if (sourceRunId == Guid.Empty)
                throw new ArgumentException("A milestone must reference its committed run.", nameof(sourceRunId));

            this.recoveredThreshold = recoveredThreshold;
            this.awardedCoins = awardedCoins;
            this.sourceRunId = sourceRunId.ToString("D");
        }

        public int RecoveredThreshold => recoveredThreshold;
        public long AwardedCoins => awardedCoins;
        public Guid SourceRunId => Guid.ParseExact(sourceRunId, "D");
        public string TransactionId => "CatalogMilestone:" + recoveredThreshold.ToString(CultureInfo.InvariantCulture);

        internal bool IsValid => recoveredThreshold > 0 && awardedCoins > 0
            && Guid.TryParseExact(sourceRunId, "D", out Guid id) && id != Guid.Empty;
    }
}
