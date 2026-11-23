#nullable enable

using System;
using System.Collections.Generic;

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
        private readonly object sync = new object();
        private readonly Dictionary<WeaponInstanceId, WeaponInstance> instances;

        public InventoryService(IReadOnlyList<WeaponInstance> initialInstances)
        {
            if (initialInstances == null)
                throw new ArgumentNullException(nameof(initialInstances));

            IReadOnlyList<WeaponInstanceIssue> issues = WeaponInstanceValidator.Validate(initialInstances);
            if (issues.Count != 0)
                throw new ArgumentException(
                    $"Invalid inventory entry at index {issues[0].Index}: {issues[0].Kind}.",
                    nameof(initialInstances));

            instances = new Dictionary<WeaponInstanceId, WeaponInstance>(initialInstances.Count);
            var occupiedSlots = new HashSet<(WeaponLocationKind Kind, int Index)>();
            foreach (WeaponInstance instance in initialInstances)
            {
                WeaponLocation location = instance.Location;
                if (location.SlotIndex is int index && !occupiedSlots.Add((location.Kind, index)))
                    throw new ArgumentException("Multiple weapons occupy the same slot.", nameof(initialInstances));

                instances.Add(instance.Id, instance);
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

                instances[id] = WeaponInstance.Rehydrate(instance.Id, instance.DefinitionId, instance.Provenance, destination);
                return InventoryMoveResult.Moved;
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

        private static bool IsMoveLocation(WeaponLocation location) =>
            location.IsValid && (location.Kind == WeaponLocationKind.Warehouse || location.SlotIndex.HasValue);

        private static bool SamePosition(WeaponLocation first, WeaponLocation second) =>
            first.Kind == second.Kind && first.SlotIndex == second.SlotIndex;
    }
}
