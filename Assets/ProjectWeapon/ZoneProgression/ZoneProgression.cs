#nullable enable

using System;
using System.Linq;
using ProjectWeapon.Run;
using ProjectWeapon.Settlement;
using ProjectWeapon.Save;

namespace ProjectWeapon.Dungeon
{
    /// <summary>A monotonic unlock state on the owning gameplay thread.</summary>
    public sealed class ZoneProgression
    {
        private readonly int[] zones;
        private int highestIndex;

        public ZoneProgression(ZoneCatalog catalog, int? highestUnlockedZoneId = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            zones = catalog.GetZones().Select(zone => zone.ZoneId).OrderBy(id => id).ToArray();
            if (highestUnlockedZoneId.HasValue)
            {
                highestIndex = Array.BinarySearch(zones, highestUnlockedZoneId.Value);
                if (highestIndex < 0)
                    throw new ArgumentOutOfRangeException(nameof(highestUnlockedZoneId));
            }
        }

        /// <summary>Replays only explicit adjacent unlocks from durable successful settlement receipts.</summary>
        public static ZoneProgression Restore(ZoneCatalog catalog, SaveSnapshot? snapshot)
        {
            var progression = new ZoneProgression(catalog);
            if (snapshot == null)
                return progression;

            foreach (SettlementSaveRecord receipt in snapshot.Settlements)
            {
                if (progression.highestIndex + 1 >= progression.zones.Length)
                    break;
                if (RunSettlementDetails.Read(receipt)?.UnlockedZoneId == progression.zones[progression.highestIndex + 1])
                    progression.highestIndex++;
            }
            return progression;
        }

        /// <summary>The highest unlocked zone ID accepted by this read model.</summary>
        public int HighestUnlockedZoneId => zones[highestIndex];

        public bool IsUnlocked(int zoneId)
        {
            int index = Array.BinarySearch(zones, zoneId);
            return index >= 0 && index <= highestIndex;
        }

        /// <summary>Returns the newly unlocked zone, or null when the committed outcome has no change.</summary>
        public int? TryUnlockNext(RunResult run, RunSettlementCommit settlement, BossDefeatEvidence? boss)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (settlement == null)
                throw new ArgumentNullException(nameof(settlement));
            if (!ProposeNextUnlock(run, boss).HasValue || !IsCommittedSuccess(run, settlement))
                return null;

            highestIndex++;
            return HighestUnlockedZoneId;
        }

        /// <summary>Builds a proposal from confirmed same-run boss evidence for the pending save transaction.</summary>
        public int? ProposeNextUnlock(RunResult run, BossDefeatEvidence? boss)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (!run.Succeeded || boss == null || boss.RunId != run.RunId || boss.ZoneId != run.ZoneId
                || boss.DefeatedAt > run.EffectiveAt || run.ZoneId != HighestUnlockedZoneId
                || highestIndex + 1 >= zones.Length)
                return null;
            return zones[highestIndex + 1];
        }

        private static bool IsCommittedSuccess(RunResult run, RunSettlementCommit settlement)
        {
            if ((settlement.Status != RunSettlementStatus.Committed && settlement.Status != RunSettlementStatus.AlreadyCommitted)
                || settlement.Receipt is not { } receipt || receipt.RunId != run.RunId.Value
                || settlement.Snapshot is not { } snapshot
                || !snapshot.TryGetSettlement(receipt.TransactionId, out var stored) || stored == null
                || stored.RunId != receipt.RunId || !StringComparer.Ordinal.Equals(stored.Result, receipt.Result))
                return false;

            RunSettlementDetails? details = RunSettlementDetails.Read(stored);
            return details != null
                ? details.Reason == RunEndReason.Extracted
                : StringComparer.Ordinal.Equals(stored.Result, nameof(RunEndReason.Extracted));
        }
    }
}
