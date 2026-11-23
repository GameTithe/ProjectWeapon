#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Inventory.Tests
{
    public sealed class InventoryPickupTests
    {
        private static readonly WeaponLocation Warehouse = WeaponLocation.Warehouse();
        private static readonly WeaponLocation FirstCarry = WeaponLocation.Carried(0);

        [Test]
        public void PrototypeCapacityRestrictsMovesAndPickupFillsTheLowestFreeCarrySlot()
        {
            WeaponInstance existing = Item(WeaponLocation.Carried(1));
            WeaponInstance owned = Item(Warehouse);
            WeaponInstance loot = Item(Warehouse, WeaponProvenance.ExplorationLoot);
            var inventory = new InventoryService(new[] { existing, owned });

            Assert.That(inventory.Capacity.EquippedSlots, Is.EqualTo(2));
            Assert.That(inventory.Capacity.CarriedSlots, Is.EqualTo(3));
            Assert.That(inventory.Move(owned.Id, Warehouse, WeaponLocation.Equipped(2)),
                Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(inventory.Move(owned.Id, Warehouse, WeaponLocation.Equipped(1)),
                Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(inventory.Move(owned.Id, WeaponLocation.Equipped(1), WeaponLocation.Carried(3)),
                Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(inventory.Move(owned.Id, WeaponLocation.Equipped(1), WeaponLocation.Carried(2)),
                Is.EqualTo(InventoryMoveResult.Moved));

            InventoryPickupResult result = inventory.TryPickup(loot);
            Assert.That(result.Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            Assert.That(result.PickedUp?.Id, Is.EqualTo(loot.Id));
            Assert.That(result.PickedUp?.Location.Kind, Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(result.PickedUp?.Location.SlotIndex, Is.Zero);
            Assert.That(result.PickedUp?.Provenance, Is.EqualTo(loot.Provenance));
            Assert.That(result.Displaced, Is.Null);
            Assert.That(inventory.TryPickup(loot).Status, Is.EqualTo(InventoryPickupStatus.DuplicateInstance));
        }

        [Test]
        public void FullBagChoiceAndCancellationLeaveInventoryUnchanged()
        {
            WeaponInstance existing = Item(FirstCarry);
            WeaponInstance loot = Item(Warehouse, WeaponProvenance.ExplorationLoot);
            var inventory = new InventoryService(new[] { existing }, new InventorySlotCapacity(1, 1));

            InventoryPickupResult result = inventory.TryPickup(loot);
            Assert.That(result.Status, Is.EqualTo(InventoryPickupStatus.ChoiceRequired));
            InventoryPickupChoice choice = result.Choice!;
            Assert.That(choice.Loot, Is.SameAs(loot));
            Assert.That(choice.Candidates.Single(), Is.SameAs(existing));
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(existing));

            Assert.That(inventory.ResolvePickup(choice, null).Status, Is.EqualTo(InventoryPickupStatus.Cancelled));
            Assert.That(inventory.ResolvePickup(choice, existing.Id).Status, Is.EqualTo(InventoryPickupStatus.StaleChoice));
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(existing));
            Assert.That(inventory.TryPickup(loot).Status, Is.EqualTo(InventoryPickupStatus.ChoiceRequired));
        }

        [Test]
        public void ResolvingChoiceReturnsOnlySelectedItemAndRejectsWrongOrDuplicateInputs()
        {
            WeaponInstance first = Item(FirstCarry);
            WeaponInstance selected = Item(WeaponLocation.Carried(1), WeaponProvenance.Loan);
            WeaponInstance equipped = Item(WeaponLocation.Equipped(0));
            WeaponInstance loot = Item(Warehouse, WeaponProvenance.ExplorationLoot);
            var inventory = new InventoryService(new[] { first, selected, equipped }, new InventorySlotCapacity(1, 2));
            InventoryPickupChoice choice = inventory.TryPickup(loot).Choice!;

            Assert.That(inventory.ResolvePickup(choice, equipped.Id).Status, Is.EqualTo(InventoryPickupStatus.InvalidSelection));
            Assert.That(inventory.ResolvePickup(choice, loot.Id).Status, Is.EqualTo(InventoryPickupStatus.InvalidSelection));
            Assert.That(inventory.Snapshot(), Is.EquivalentTo(new[] { first, selected, equipped }));

            InventoryPickupResult result = inventory.ResolvePickup(choice, selected.Id);
            Assert.That(result.Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            Assert.That(result.PickedUp?.DefinitionId, Is.EqualTo(loot.DefinitionId));
            Assert.That(result.PickedUp?.Location.SlotIndex, Is.EqualTo(1));
            Assert.That(result.Displaced, Is.SameAs(selected));
            Assert.That(result.Displaced?.IsLoaned, Is.True);
            Assert.That(inventory.Snapshot(), Is.EquivalentTo(new[] { first, equipped, result.PickedUp }));
            Assert.That(inventory.ResolvePickup(choice, selected.Id).Status, Is.EqualTo(InventoryPickupStatus.StaleChoice));
        }

        [Test]
        public void MovingAnOfferedItemAwayAndBackInvalidatesOldChoices()
        {
            WeaponInstance existing = Item(FirstCarry);
            WeaponInstance loot = Item(Warehouse);
            var inventory = new InventoryService(new[] { existing }, new InventorySlotCapacity(1, 1));
            InventoryPickupChoice stale = inventory.TryPickup(loot).Choice!;
            Assert.That(inventory.Move(existing.Id, FirstCarry, Warehouse), Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(inventory.Move(existing.Id, Warehouse, FirstCarry), Is.EqualTo(InventoryMoveResult.Moved));

            Assert.That(inventory.ResolvePickup(stale, existing.Id).Status, Is.EqualTo(InventoryPickupStatus.StaleChoice));
            Assert.That(inventory.Snapshot().Single().Id, Is.EqualTo(existing.Id));

            InventoryPickupChoice fresh = inventory.TryPickup(loot).Choice!;
            Assert.That(inventory.ResolvePickup(fresh, existing.Id).Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
        }

        [Test]
        public void InvalidCapacityExternalLootAndForeignChoiceDoNotChangeInventory()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new InventorySlotCapacity(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InventorySlotCapacity(0, -1));
            Assert.Throws<ArgumentException>(() =>
                new InventoryService(new[] { Item(WeaponLocation.Carried(1)) }, new InventorySlotCapacity(1, 1)));

            WeaponInstance existing = Item(FirstCarry);
            WeaponInstance loot = Item(Warehouse);
            var none = new InventoryService(Array.Empty<WeaponInstance>(), new InventorySlotCapacity(0, 0));
            var inventory = new InventoryService(new[] { existing }, new InventorySlotCapacity(0, 1));

            Assert.That(none.TryPickup(loot).Status, Is.EqualTo(InventoryPickupStatus.NoCarryCapacity));
            Assert.That(none.Snapshot(), Is.Empty);
            Assert.That(inventory.TryPickup(null).Status, Is.EqualTo(InventoryPickupStatus.InvalidRequest));
            Assert.That(inventory.TryPickup(Item(WeaponLocation.RunReserved(Guid.NewGuid()))).Status,
                Is.EqualTo(InventoryPickupStatus.InvalidRequest));
            Assert.That(none.ResolvePickup(inventory.TryPickup(loot).Choice, existing.Id).Status,
                Is.EqualTo(InventoryPickupStatus.InvalidRequest));
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(existing));
        }

        [Test]
        public void ConcurrentPickupRetriesAcceptAnInstanceOnlyOnce()
        {
            WeaponInstance loot = Item(Warehouse);
            var inventory = new InventoryService(Array.Empty<WeaponInstance>(), new InventorySlotCapacity(1, 2));
            InventoryPickupStatus[] results = Race(() => inventory.TryPickup(loot).Status, () => inventory.TryPickup(loot).Status);

            Assert.That(results, Is.EquivalentTo(new[] { InventoryPickupStatus.PickedUp, InventoryPickupStatus.DuplicateInstance }));
            Assert.That(inventory.Snapshot().Single().Id, Is.EqualTo(loot.Id));
        }

        [Test]
        public void ConcurrentChoicesCanDisplaceAnInstanceOnlyOnce()
        {
            WeaponInstance existing = Item(FirstCarry);
            var inventory = new InventoryService(new[] { existing }, new InventorySlotCapacity(1, 1));
            InventoryPickupChoice first = inventory.TryPickup(Item(Warehouse)).Choice!;
            InventoryPickupChoice second = inventory.TryPickup(Item(Warehouse)).Choice!;

            InventoryPickupStatus[] results = Race(() => inventory.ResolvePickup(first, existing.Id).Status,
                () => inventory.ResolvePickup(second, existing.Id).Status);

            Assert.That(results, Is.EquivalentTo(new[] { InventoryPickupStatus.PickedUp, InventoryPickupStatus.StaleChoice }));
            Assert.That(inventory.Snapshot().Single().Id, Is.EqualTo(
                results[0] == InventoryPickupStatus.PickedUp ? first.Loot.Id : second.Loot.Id));
        }

        private static WeaponInstance Item(WeaponLocation location, WeaponProvenance provenance = WeaponProvenance.Personal) =>
            WeaponInstance.Create(new WeaponDefinitionId("old-sword"), provenance, location);

        private static InventoryPickupStatus[] Race(Func<InventoryPickupStatus> first, Func<InventoryPickupStatus> second)
        {
            var timeout = TimeSpan.FromSeconds(10);
            using var barrier = new Barrier(3);
            Task<InventoryPickupStatus> Start(Func<InventoryPickupStatus> operation) => Task.Run(() =>
            {
                if (!barrier.SignalAndWait(timeout))
                    throw new TimeoutException("Pickups did not start together.");

                return operation();
            });

            Task<InventoryPickupStatus> left = Start(first);
            Task<InventoryPickupStatus> right = Start(second);
            Assert.That(barrier.SignalAndWait(timeout), Is.True);
            Assert.That(Task.WaitAll(new Task[] { left, right }, timeout), Is.True);
            return new[] { left.Result, right.Result };
        }
    }
}
