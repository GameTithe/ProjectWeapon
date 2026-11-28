#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;
using UnityEngine;

namespace ProjectWeapon.Loot
{
    /// <summary>Shares pickup attempts among sensors for one world session on the gameplay thread.</summary>
    public sealed class LootPickupCoordinator
    {
        private readonly InventoryService inventory;
        private readonly Dictionary<WeaponInstanceId, InventoryPickupResult> attempts =
            new Dictionary<WeaponInstanceId, InventoryPickupResult>();

        public LootPickupCoordinator(InventoryService inventory)
        {
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public bool TryObserve(WeaponInstance? loot, Vector2 center, Vector2 lootPosition, float radius,
            out InventoryPickupResult result)
        {
            result = default;
            if (loot == null || !loot.Id.IsValid || attempts.ContainsKey(loot.Id)
                || !IsWithinRange(center, lootPosition, radius))
                return false;

            result = inventory.TryPickup(loot);
            attempts.Add(loot.Id, result);
            return true;
        }

        public bool TryResolveChoice(InventoryPickupChoice? choice, WeaponInstanceId? selectedId,
            out InventoryPickupResult result)
        {
            result = default;
            if (choice == null || !attempts.TryGetValue(choice.Loot.Id, out InventoryPickupResult previous)
                || !ReferenceEquals(previous.Choice, choice))
                return false;

            result = inventory.ResolvePickup(choice, selectedId);
            if (result.Status != InventoryPickupStatus.InvalidSelection)
                attempts[choice.Loot.Id] = result;

            return true;
        }

        public bool AllowRetry(WeaponInstanceId id)
        {
            if (!attempts.TryGetValue(id, out InventoryPickupResult previous)
                || (previous.Status != InventoryPickupStatus.Cancelled && previous.Status != InventoryPickupStatus.StaleChoice))
                return false;

            return attempts.Remove(id);
        }

        /// <summary>Places an owned item back into the world using its original instance ID.</summary>
        /// <remarks>The factory must return a fresh, inactive WorldLoot at a legal drop position,
        /// or null if it cannot stage a drop. This method activates it after the inventory accepts the removal.</remarks>
        public InventoryMoveResult TryDropRunSlot(WeaponInstanceId id, WeaponLocation source,
            Func<WeaponInstance, WorldLoot?> stageDrop)
        {
            if (stageDrop == null)
                throw new ArgumentNullException(nameof(stageDrop));

            WorldLoot? staged = null;
            InventoryMoveResult result = inventory.TryDropRunSlot(id, source, item =>
            {
                staged = stageDrop(item);
                if (!staged || staged!.gameObject.activeSelf || !staged.enabled || staged.Item != null
                    || (staged.transform.parent && !staged.transform.parent.gameObject.activeInHierarchy))
                    return false;

                staged.Initialize(item);
                return true;
            });

            if (result == InventoryMoveResult.Moved)
            {
                attempts.Remove(id);
                staged!.gameObject.SetActive(true);
            }

            return result;
        }

        public static bool IsWithinRange(Vector2 center, Vector2 item, float radius)
        {
            if (!float.IsFinite(center.x) || !float.IsFinite(center.y) || !float.IsFinite(item.x)
                || !float.IsFinite(item.y) || !float.IsFinite(radius) || radius < 0)
                return false;

            double x = (double)item.x - center.x;
            double y = (double)item.y - center.y;
            return x * x + y * y <= (double)radius * radius;
        }
    }
}
