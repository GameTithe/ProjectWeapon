#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Catalog;
using ProjectWeapon.Run;
using ProjectWeapon.Save;

namespace ProjectWeapon.Settlement
{
    internal static class CatalogMilestoneRewards
    {
        private static readonly (int Threshold, long Coins)[] Tiers =
        {
            (5, 500),
            (10, 1000),
            (15, 2000)
        };

        internal static IReadOnlyList<CatalogMilestoneSaveRecord> Unpaid(
            RunResult run, CatalogProgress catalog, IReadOnlyList<CatalogMilestoneSaveRecord> paid)
        {
            if (!run.Succeeded)
                return Array.Empty<CatalogMilestoneSaveRecord>();

            int recovered = catalog.RecoveredCount;
            var rewards = new List<CatalogMilestoneSaveRecord>();
            foreach ((int threshold, long coins) in Tiers)
            {
                if (recovered < threshold)
                    break;
                if (!paid.Any(existing => existing.RecoveredThreshold == threshold))
                    rewards.Add(new CatalogMilestoneSaveRecord(threshold, coins, run.RunId.Value));
            }
            return rewards;
        }
    }
}
