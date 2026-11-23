#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.Loot.Tests
{
    public sealed class LootPickupCoordinatorTests
    {
        [Test]
        public void IncludesThePivotBoundaryAndSuppressesReentryAfterSuccess()
        {
            WeaponInstance loot = Item(WeaponLocation.Warehouse());
            var inventory = new InventoryService(Array.Empty<WeaponInstance>());
            var pickup = new LootPickupCoordinator(inventory);

            Assert.That(pickup.TryObserve(loot, Vector2.zero, new Vector2(3, 4.001f), 5, out _), Is.False);
            Assert.That(inventory.Snapshot(), Is.Empty);
            Assert.That(pickup.TryObserve(loot, Vector2.zero, new Vector2(3, 4), 5, out InventoryPickupResult result),
                Is.True);
            Assert.That(result.Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            Assert.That(result.PickedUp?.Id, Is.EqualTo(loot.Id));
            Assert.That(pickup.TryObserve(loot, Vector2.zero, Vector2.one, 5, out _), Is.False);
            Assert.That(pickup.AllowRetry(loot.Id), Is.False);
            Assert.That(inventory.Snapshot().Single().Id, Is.EqualTo(loot.Id));
        }

        [TestCase(float.NaN, 0, 2)]
        [TestCase(1, float.PositiveInfinity, 2)]
        [TestCase(0, 0, -1)]
        [TestCase(0, 0, float.PositiveInfinity)]
        public void InvalidDistanceInputsAreRejected(float x, float y, float radius)
        {
            Assert.That(LootPickupCoordinator.IsWithinRange(Vector2.zero, new Vector2(x, y), radius), Is.False);
        }

        [Test]
        public void CancellationPreservesBothItemsAndRetryRequiresAnExplicitReset()
        {
            WeaponInstance existing = Item(WeaponLocation.Carried(0));
            WeaponInstance loot = Item(WeaponLocation.Warehouse());
            var inventory = new InventoryService(new[] { existing }, new InventorySlotCapacity(0, 1));
            var pickup = new LootPickupCoordinator(inventory);
            Assert.That(pickup.TryObserve(loot, Vector2.zero, Vector2.one, 2, out InventoryPickupResult result),
                Is.True);
            Assert.That(result.Status, Is.EqualTo(InventoryPickupStatus.ChoiceRequired));
            Assert.That(pickup.TryObserve(loot, Vector2.zero, Vector2.one, 2, out _), Is.False);
            Assert.That(pickup.AllowRetry(loot.Id), Is.False);

            Assert.That(pickup.TryResolveChoice(result.Choice, null, out InventoryPickupResult cancelled), Is.True);
            Assert.That(cancelled.Status, Is.EqualTo(InventoryPickupStatus.Cancelled));
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(existing));
            Assert.That(pickup.TryObserve(loot, Vector2.zero, Vector2.one, 2, out _), Is.False);
            Assert.That(pickup.TryResolveChoice(result.Choice, existing.Id, out _), Is.False);

            Assert.That(pickup.AllowRetry(loot.Id), Is.True);
            Assert.That(pickup.TryObserve(loot, Vector2.zero, Vector2.one, 2, out InventoryPickupResult reopened),
                Is.True);
            Assert.That(pickup.TryResolveChoice(reopened.Choice, loot.Id, out InventoryPickupResult wrong), Is.True);
            Assert.That(wrong.Status, Is.EqualTo(InventoryPickupStatus.InvalidSelection));
            Assert.That(pickup.TryResolveChoice(reopened.Choice, existing.Id, out InventoryPickupResult picked), Is.True);
            Assert.That(picked.Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            Assert.That(picked.Displaced, Is.SameAs(existing));
            Assert.That(pickup.TryResolveChoice(reopened.Choice, existing.Id, out _), Is.False);
            Assert.That(inventory.Snapshot().Single().Id, Is.EqualTo(loot.Id));
        }

        [Test]
        public void BagChangeMakesTheOutstandingOfferStaleUntilAnExplicitRetry()
        {
            WeaponInstance existing = Item(WeaponLocation.Carried(0));
            WeaponInstance loot = Item(WeaponLocation.Warehouse());
            var inventory = new InventoryService(new[] { existing }, new InventorySlotCapacity(0, 1));
            var pickup = new LootPickupCoordinator(inventory);
            Assert.That(pickup.TryObserve(loot, Vector2.zero, Vector2.zero, 1, out InventoryPickupResult pending),
                Is.True);
            Assert.That(inventory.Move(existing.Id, existing.Location, WeaponLocation.Warehouse()),
                Is.EqualTo(InventoryMoveResult.Moved));

            Assert.That(pickup.TryResolveChoice(pending.Choice, existing.Id, out InventoryPickupResult stale), Is.True);
            Assert.That(stale.Status, Is.EqualTo(InventoryPickupStatus.StaleChoice));
            Assert.That(inventory.Snapshot().Count, Is.EqualTo(1));
            Assert.That(pickup.TryObserve(loot, Vector2.zero, Vector2.zero, 1, out _), Is.False);
            Assert.That(pickup.AllowRetry(loot.Id), Is.True);
            Assert.That(pickup.TryObserve(loot, Vector2.zero, Vector2.zero, 1, out InventoryPickupResult retried),
                Is.True);
            Assert.That(retried.Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            Assert.That(retried.Displaced, Is.Null);
        }

        [Test]
        public void InventoryRejectsAnAlreadyHeldIdAndTheSensorDoesNotRepeatTheRejection()
        {
            WeaponInstance existing = Item(WeaponLocation.Carried(0));
            var inventory = new InventoryService(new[] { existing });
            var pickup = new LootPickupCoordinator(inventory);

            Assert.That(pickup.TryObserve(existing, Vector2.zero, Vector2.zero, 1, out InventoryPickupResult result),
                Is.True);
            Assert.That(result.Status, Is.EqualTo(InventoryPickupStatus.DuplicateInstance));
            Assert.That(pickup.TryObserve(existing, Vector2.zero, Vector2.zero, 1, out _), Is.False);
            Assert.That(pickup.AllowRetry(existing.Id), Is.False);
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(existing));
        }

        private static WeaponInstance Item(WeaponLocation location) =>
            WeaponInstance.Create(new WeaponDefinitionId("old-sword"), WeaponProvenance.ExplorationLoot, location);
    }
}
