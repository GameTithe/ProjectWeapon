#nullable enable

using System;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;

namespace ProjectWeapon.Dungeon
{
    /// <summary>Starts the zone and reserves the town loadout as one synchronous operation.</summary>
    public sealed class SortieStarter
    {
        private readonly InventoryService townInventory;
        private readonly ZoneRunCountdown countdown;

        public SortieStarter(InventoryService townInventory, ZoneRunCountdown countdown)
        {
            this.townInventory = townInventory ?? throw new ArgumentNullException(nameof(townInventory));
            this.countdown = countdown ?? throw new ArgumentNullException(nameof(countdown));
        }

        public SortieStartResult TryStart(int zoneId) => TryStart(RunId.New(), zoneId);

        public SortieStartResult TryStart(RunId runId, int zoneId)
            => townInventory.TryStartSortie(runId.Value, () => countdown.TryStartRun(runId, zoneId));
    }
}
