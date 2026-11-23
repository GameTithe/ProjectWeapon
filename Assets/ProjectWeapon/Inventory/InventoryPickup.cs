#nullable enable

using System;
using System.Collections.Generic;

namespace ProjectWeapon.Inventory
{
    public readonly struct InventorySlotCapacity
    {
        public static readonly InventorySlotCapacity Prototype = new InventorySlotCapacity(2, 3);

        public InventorySlotCapacity(int equippedSlots, int carriedSlots)
        {
            if (equippedSlots < 0)
                throw new ArgumentOutOfRangeException(nameof(equippedSlots));

            if (carriedSlots < 0)
                throw new ArgumentOutOfRangeException(nameof(carriedSlots));

            EquippedSlots = equippedSlots;
            CarriedSlots = carriedSlots;
        }

        public int EquippedSlots { get; }
        public int CarriedSlots { get; }
    }

    public enum InventoryPickupStatus
    {
        InvalidRequest = 0,
        PickedUp,
        ChoiceRequired,
        Cancelled,
        DuplicateInstance,
        NoCarryCapacity,
        InvalidSelection,
        StaleChoice
    }

    public sealed class InventoryPickupChoice
    {
        internal InventoryPickupChoice(InventoryService owner, long carryRevision, WeaponInstance loot,
            IReadOnlyList<WeaponInstance> candidates)
        {
            Owner = owner;
            CarryRevision = carryRevision;
            Loot = loot;
            Candidates = candidates;
        }

        public WeaponInstance Loot { get; }
        public IReadOnlyList<WeaponInstance> Candidates { get; }

        internal InventoryService Owner { get; }
        internal long CarryRevision { get; }
        internal bool Resolved { get; set; }
    }

    public readonly struct InventoryPickupResult
    {
        internal InventoryPickupResult(InventoryPickupStatus status, InventoryPickupChoice? choice = null,
            WeaponInstance? pickedUp = null, WeaponInstance? displaced = null)
        {
            Status = status;
            Choice = choice;
            PickedUp = pickedUp;
            Displaced = displaced;
        }

        public InventoryPickupStatus Status { get; }
        public InventoryPickupChoice? Choice { get; }
        public WeaponInstance? PickedUp { get; }

        /// <summary>The unchanged item record removed from the bag, with its former slot.</summary>
        public WeaponInstance? Displaced { get; }
    }
}
