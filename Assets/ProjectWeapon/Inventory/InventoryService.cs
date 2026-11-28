#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Inventory
{
    public enum InventoryMoveResult
    {
        InvalidRequest = 0,
        Moved,
        AlreadyAtDestination,
        MissingInstance,
        SourceMismatch,
        DestinationOccupied,
        ProvenanceRestricted
    }

    /// <summary>Owns the current position of each weapon instance.</summary>
    public sealed class InventoryService
    {
        private static readonly WeaponDefinitionId StarterLoanDefinition = new WeaponDefinitionId("old_sword");
        private readonly object sync = new object();
        private readonly HashSet<WeaponInstanceId> issuedStarterLoans = new HashSet<WeaponInstanceId>();
        private readonly Dictionary<WeaponInstanceId, WeaponInstance> instances;
        private long carryRevision;

        public InventoryService(IReadOnlyList<WeaponInstance> initialInstances)
            : this(initialInstances, InventorySlotCapacity.Prototype)
        {
        }

        public InventoryService(IReadOnlyList<WeaponInstance> initialInstances, InventorySlotCapacity capacity)
        {
            if (initialInstances == null)
                throw new ArgumentNullException(nameof(initialInstances));

            IReadOnlyList<WeaponInstanceIssue> issues = WeaponInstanceValidator.Validate(initialInstances);
            if (issues.Count != 0)
                throw new ArgumentException(
                    $"Invalid inventory entry at index {issues[0].Index}: {issues[0].Kind}.",
                    nameof(initialInstances));

            Capacity = capacity;
            instances = new Dictionary<WeaponInstanceId, WeaponInstance>(initialInstances.Count);
            var occupiedSlots = new HashSet<(WeaponLocationKind Kind, int Index)>();
            foreach (WeaponInstance instance in initialInstances)
            {
                WeaponLocation location = instance.Location;
                if (!FitsCapacity(location))
                    throw new ArgumentException("A weapon occupies a slot beyond the configured capacity.", nameof(initialInstances));

                if (location.SlotIndex is int index && !occupiedSlots.Add((location.Kind, index)))
                    throw new ArgumentException("Multiple weapons occupy the same slot.", nameof(initialInstances));

                instances.Add(instance.Id, instance);
            }
        }

        public InventorySlotCapacity Capacity { get; }

        /// <summary>Issues the approved fallback for a prepared sortie, at most once per run on this inventory.</summary>
        public StarterLoanResult PrepareStarterLoan(Guid runId)
        {
            if (runId == Guid.Empty)
                return new StarterLoanResult(StarterLoanStatus.InvalidRun);

            WeaponInstanceId loanId = WeaponInstanceId.ForStarterLoan(runId);
            lock (sync)
            {
                if (instances.TryGetValue(loanId, out WeaponInstance existing))
                {
                    if (!existing.IsLoaned || !existing.DefinitionId.Equals(StarterLoanDefinition))
                        return new StarterLoanResult(StarterLoanStatus.InstanceIdConflict);

                    issuedStarterLoans.Add(loanId);
                    return new StarterLoanResult(StarterLoanStatus.AlreadyIssued, existing);
                }

                if (issuedStarterLoans.Contains(loanId))
                    return new StarterLoanResult(StarterLoanStatus.AlreadyIssued);

                bool hasOwnedWeapon = false;
                foreach (WeaponInstance instance in instances.Values)
                {
                    WeaponLocationKind location = instance.Location.Kind;
                    if (location == WeaponLocationKind.Carried || (location == WeaponLocationKind.Equipped && instance.IsLoaned))
                        return new StarterLoanResult(StarterLoanStatus.InventoryNotPrepared);

                    if (location == WeaponLocationKind.Equipped
                        || (!instance.IsLoaned && instance.Location.ReservedRunId == runId))
                        hasOwnedWeapon = true;
                }

                if (hasOwnedWeapon)
                    return new StarterLoanResult(StarterLoanStatus.OwnedWeaponEquipped);
                if (Capacity.EquippedSlots == 0)
                    return new StarterLoanResult(StarterLoanStatus.NoEquipmentSlot);

                WeaponInstance loan = WeaponInstance.Rehydrate(loanId, StarterLoanDefinition,
                    WeaponProvenance.Loan, WeaponLocation.Equipped(0));
                instances.Add(loanId, loan);
                issuedStarterLoans.Add(loanId);
                return new StarterLoanResult(StarterLoanStatus.Issued, loan);
            }
        }

        /// <summary>Rejects non-owned loans before applying any sale-specific rules.</summary>
        public InventorySaleProvenance CheckSaleProvenance(WeaponInstanceId id)
        {
            if (!id.IsValid)
                return InventorySaleProvenance.InvalidInstance;

            lock (sync)
            {
                if (!instances.TryGetValue(id, out WeaponInstance instance))
                    return InventorySaleProvenance.MissingInstance;

                return instance.IsLoaned
                    ? InventorySaleProvenance.ProvenanceRestricted
                    : InventorySaleProvenance.RequiresSaleRules;
            }
        }

        /// <summary>Replaces the location only if the expected source still matches.</summary>
        public InventoryMoveResult Move(WeaponInstanceId id, WeaponLocation expectedSource, WeaponLocation destination)
        {
            if (!id.IsValid || !IsMoveLocation(expectedSource) || !IsMoveLocation(destination))
                return InventoryMoveResult.InvalidRequest;

            lock (sync)
            {
                if (!instances.TryGetValue(id, out WeaponInstance instance))
                    return InventoryMoveResult.MissingInstance;

                if (!SamePosition(instance.Location, expectedSource))
                    return InventoryMoveResult.SourceMismatch;

                if (SamePosition(expectedSource, destination))
                    return InventoryMoveResult.AlreadyAtDestination;

                if (instance.IsLoaned && destination.Kind == WeaponLocationKind.Warehouse)
                    return InventoryMoveResult.ProvenanceRestricted;

                if (destination.SlotIndex.HasValue && IsOccupied(destination))
                    return InventoryMoveResult.DestinationOccupied;

                instances[id] = AtLocation(instance, destination);
                if (expectedSource.Kind == WeaponLocationKind.Carried || destination.Kind == WeaponLocationKind.Carried)
                    carryRevision++;

                return InventoryMoveResult.Moved;
            }
        }

        /// <summary>Accepts an external weapon into the first unoccupied carry slot, or offers a choice.</summary>
        public InventoryPickupResult TryPickup(WeaponInstance? loot)
        {
            if (loot == null || !loot.IsValid || loot.Location.Kind == WeaponLocationKind.RunReserved)
                return new InventoryPickupResult(InventoryPickupStatus.InvalidRequest);

            lock (sync)
            {
                if (instances.ContainsKey(loot.Id))
                    return new InventoryPickupResult(InventoryPickupStatus.DuplicateInstance);

                if (Capacity.CarriedSlots == 0)
                    return new InventoryPickupResult(InventoryPickupStatus.NoCarryCapacity);

                for (int index = 0; index < Capacity.CarriedSlots; index++)
                {
                    WeaponLocation destination = WeaponLocation.Carried(index);
                    if (IsOccupied(destination))
                        continue;

                    WeaponInstance added = AtLocation(loot, destination);
                    instances.Add(added.Id, added);
                    carryRevision++;
                    return new InventoryPickupResult(InventoryPickupStatus.PickedUp, pickedUp: added);
                }

                var carried = new List<WeaponInstance>();
                foreach (WeaponInstance instance in instances.Values)
                {
                    if (instance.Location.Kind == WeaponLocationKind.Carried)
                        carried.Add(instance);
                }

                return new InventoryPickupResult(InventoryPickupStatus.ChoiceRequired,
                    choice: new InventoryPickupChoice(this, carryRevision, loot, carried.AsReadOnly()));
            }
        }

        /// <summary>Resolves a full-bag choice. An absent selection abandons the offer.</summary>
        public InventoryPickupResult ResolvePickup(InventoryPickupChoice? choice, WeaponInstanceId? selectedId)
        {
            if (choice == null || !ReferenceEquals(choice.Owner, this))
                return new InventoryPickupResult(InventoryPickupStatus.InvalidRequest);

            lock (sync)
            {
                if (choice.Resolved || choice.CarryRevision != carryRevision)
                    return new InventoryPickupResult(InventoryPickupStatus.StaleChoice);

                if (!selectedId.HasValue)
                {
                    choice.Resolved = true;
                    return new InventoryPickupResult(InventoryPickupStatus.Cancelled);
                }

                if (!selectedId.Value.IsValid || !instances.TryGetValue(selectedId.Value, out WeaponInstance displaced)
                    || displaced.Location.Kind != WeaponLocationKind.Carried)
                    return new InventoryPickupResult(InventoryPickupStatus.InvalidSelection);

                if (instances.ContainsKey(choice.Loot.Id))
                    return new InventoryPickupResult(InventoryPickupStatus.DuplicateInstance);

                WeaponInstance added = AtLocation(choice.Loot, displaced.Location);
                instances.Add(added.Id, added);
                instances.Remove(displaced.Id);
                choice.Resolved = true;
                carryRevision++;
                return new InventoryPickupResult(InventoryPickupStatus.PickedUp, pickedUp: added, displaced: displaced);
            }
        }

        /// <summary>Copies the current records for a reader without exposing the service's collection.</summary>
        public IReadOnlyList<WeaponInstance> Snapshot()
        {
            lock (sync)
            {
                var snapshot = new WeaponInstance[instances.Count];
                instances.Values.CopyTo(snapshot, 0);
                return snapshot;
            }
        }

        private bool IsOccupied(WeaponLocation destination)
        {
            foreach (WeaponInstance instance in instances.Values)
            {
                if (SamePosition(instance.Location, destination))
                    return true;
            }

            return false;
        }

        private bool FitsCapacity(WeaponLocation location) => location.Kind switch
        {
            WeaponLocationKind.Equipped => location.SlotIndex < Capacity.EquippedSlots,
            WeaponLocationKind.Carried => location.SlotIndex < Capacity.CarriedSlots,
            _ => true
        };

        private bool IsMoveLocation(WeaponLocation location) =>
            location.IsValid && (location.Kind == WeaponLocationKind.Warehouse || location.SlotIndex.HasValue)
                && FitsCapacity(location);

        private static bool SamePosition(WeaponLocation first, WeaponLocation second) =>
            first.Kind == second.Kind && first.SlotIndex == second.SlotIndex;

        private static WeaponInstance AtLocation(WeaponInstance instance, WeaponLocation destination) =>
            WeaponInstance.Rehydrate(instance.Id, instance.DefinitionId, instance.Provenance, destination);
    }
}
