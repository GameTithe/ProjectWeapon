#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Run;

namespace ProjectWeapon.RunBagUI
{
    /// <summary>Delegates bag requests to the active run inventory and its loot coordinator.</summary>
    public sealed class RunBagActions
    {
        private readonly RunState run;
        private readonly RunId runId;
        private readonly InventoryService inventory;
        private readonly LootPickupCoordinator loot;
        private readonly Func<bool> canChangeEquipment;
        private readonly Func<WeaponInstance, WorldLoot?> stageDrop;

        public RunBagActions(RunState run, RunId runId, InventoryService inventory, LootPickupCoordinator loot,
            Func<bool> canChangeEquipment, Func<WeaponInstance, WorldLoot?> stageDrop)
        {
            if (!runId.IsValid)
                throw new ArgumentException("A valid run ID is required.", nameof(runId));
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            this.runId = runId;
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.loot = loot ?? throw new ArgumentNullException(nameof(loot));
            this.canChangeEquipment = canChangeEquipment ?? throw new ArgumentNullException(nameof(canChangeEquipment));
            this.stageDrop = stageDrop ?? throw new ArgumentNullException(nameof(stageDrop));
        }

        public InventorySlotCapacity Capacity => inventory.Capacity;
        public bool CanOpen => run.CurrentRun?.Id == runId && run.Phase is RunPhase.Exploring or RunPhase.Extracting;
        public bool CanChangeSlots => CanOpen && run.Phase == RunPhase.Exploring;
        public bool CanChangeEquippedSlots => CanChangeSlots && canChangeEquipment();
        public IReadOnlyList<WeaponInstance> Snapshot() => inventory.Snapshot();

        public InventoryMoveResult Move(WeaponInstanceId id, WeaponLocation source, WeaponLocation target,
            WeaponInstanceId? targetId)
            => CanChangeSlots && (!TouchesEquipment(source, target) || CanChangeEquippedSlots)
                ? inventory.MoveRunSlot(id, source, target, targetId) : InventoryMoveResult.InvalidRequest;

        public InventoryMoveResult Drop(WeaponInstanceId id, WeaponLocation source)
            => CanChangeSlots && (source.Kind != WeaponLocationKind.Equipped || CanChangeEquippedSlots)
                ? loot.TryDropRunSlot(id, source, stageDrop) : InventoryMoveResult.InvalidRequest;

        private static bool TouchesEquipment(WeaponLocation source, WeaponLocation destination)
            => source.Kind == WeaponLocationKind.Equipped || destination.Kind == WeaponLocationKind.Equipped;
    }
}
