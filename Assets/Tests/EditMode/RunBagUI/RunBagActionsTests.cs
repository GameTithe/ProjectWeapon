#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.RunBagUI.Tests
{
    public sealed class RunBagActionsTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private static readonly WeaponLocation Equipped = WeaponLocation.Equipped(0);
        private static readonly WeaponLocation Carry = WeaponLocation.Carried(0);
        private TimeSpan now;
        private RunId id;
        private RunState run = null!;
        private InventoryService inventory = null!;
        private LootPickupCoordinator loot = null!;
        private RunBagActions bag = null!;
        private bool canEquip;
        private bool canDrop;
        private WorldLoot? placed;

        [SetUp]
        public void Prepare()
        {
            now = TimeSpan.Zero;
            id = RunId.New();
            canEquip = canDrop = true;
            placed = null;
            run = new RunState(() => now);
            Assert.That(run.TryStartRun(id, 1, TimeSpan.FromSeconds(5)), Is.True);
        }

        [TearDown]
        public void CleanWorld()
        {
            foreach (GameObject item in created)
                UnityEngine.Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void RequiresTheRenderedDestinationAndExchangesBothSlotsWithoutDuplicatingItems()
        {
            WeaponInstance worn = Item("old_sword", Equipped);
            WeaponInstance carried = Item("dagger", Carry);
            CreateBag(worn, carried);

            Assert.That(inventory.Move(carried.Id, Carry, Equipped), Is.EqualTo(InventoryMoveResult.DestinationOccupied));
            Assert.That(bag.Move(carried.Id, Carry, Equipped, null), Is.EqualTo(InventoryMoveResult.DestinationMismatch));
            Assert.That(Location(carried), Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(Location(worn), Is.EqualTo(WeaponLocationKind.Equipped));

            Assert.That(bag.Move(carried.Id, Carry, Equipped, worn.Id), Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(Location(worn), Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(Location(carried), Is.EqualTo(WeaponLocationKind.Equipped));
            Assert.That(inventory.Snapshot().Select(item => item.Id), Is.EquivalentTo(new[] { worn.Id, carried.Id }));
            Assert.That(bag.Move(carried.Id, Carry, Equipped, worn.Id), Is.EqualTo(InventoryMoveResult.SourceMismatch));
        }

        [Test]
        public void CarryChangeInvalidatesAnExistingOverflowOffer()
        {
            WeaponInstance carried = Item("dagger", Carry);
            WeaponInstance second = Item("mace", WeaponLocation.Carried(1));
            WeaponInstance third = Item("hunting_bow", WeaponLocation.Carried(2));
            CreateBag(carried, second, third);

            InventoryPickupResult overflow = inventory.TryPickup(Item("wooden_spear", WeaponLocation.Warehouse()));
            Assert.That(overflow.Status, Is.EqualTo(InventoryPickupStatus.ChoiceRequired));
            Assert.That(bag.Move(carried.Id, Carry, Equipped, null), Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(inventory.ResolvePickup(overflow.Choice, second.Id).Status, Is.EqualTo(InventoryPickupStatus.StaleChoice));
            Assert.That(Location(carried), Is.EqualTo(WeaponLocationKind.Equipped));
        }

        [Test]
        public void LoanCanDropAndBePickedUpAgainAfterADeclinedPlacementWithoutChangingItsIdentity()
        {
            CreateBag();
            WeaponInstance loan = inventory.PrepareStarterLoan(id.Value).Loan!;
            Assert.That(bag.Drop(loan.Id, Equipped), Is.EqualTo(InventoryMoveResult.Moved));
            WorldLoot firstDrop = placed!;
            Assert.That(firstDrop.isActiveAndEnabled, Is.True);
            Assert.That(firstDrop.Item, Is.SameAs(loan));
            Assert.That(inventory.Snapshot(), Is.Empty);
            Assert.That(Observe(firstDrop, out InventoryPickupResult firstPickup), Is.True);
            Assert.That(firstPickup.Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            firstDrop.gameObject.SetActive(false);

            canDrop = false;
            Assert.That(bag.Drop(loan.Id, Carry), Is.EqualTo(InventoryMoveResult.DestinationRejected));
            Assert.That(Location(loan), Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(Observe(firstDrop, out _), Is.False);
            Assert.That(inventory.Move(loan.Id, Carry, WeaponLocation.Warehouse()),
                Is.EqualTo(InventoryMoveResult.ProvenanceRestricted));

            canDrop = true;
            Assert.That(bag.Drop(loan.Id, Carry), Is.EqualTo(InventoryMoveResult.Moved));
            WorldLoot secondDrop = placed!;
            Assert.That(secondDrop.gameObject.activeSelf, Is.True);
            Assert.That(secondDrop.Item!.Id, Is.EqualTo(loan.Id));
            Assert.That(secondDrop.Item.IsLoaned, Is.True);
            Assert.That(bag.Drop(loan.Id, Carry), Is.EqualTo(InventoryMoveResult.MissingInstance));
            Assert.That(Observe(secondDrop, out InventoryPickupResult secondPickup), Is.True);
            Assert.That(secondPickup.Status, Is.EqualTo(InventoryPickupStatus.PickedUp));
            Assert.That(Observe(secondDrop, out _), Is.False);
            Assert.That(inventory.Snapshot().Single().Id, Is.EqualTo(loan.Id));
        }

        [Test]
        public void ThrowingOrReentrantDropDestinationCannotChangeTheSlotOrStrandTheGuard()
        {
            WeaponInstance carried = Item("dagger", Carry);
            CreateBag(carried);

            Assert.Throws<InvalidOperationException>(() => loot.TryDropRunSlot(carried.Id, Carry, _ =>
            {
                Assert.That(inventory.Move(carried.Id, Carry, Equipped), Is.EqualTo(InventoryMoveResult.InvalidRequest));
                throw new InvalidOperationException();
            }));
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(carried));
            Assert.That(bag.Drop(carried.Id, Carry), Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(placed!.Item, Is.SameAs(carried));
            Assert.That(inventory.Snapshot(), Is.Empty);
        }

        [Test]
        public void EquipmentBusyOnlyBlocksOperationsThatTouchEquippedSlots()
        {
            WeaponInstance carried = Item("mace", Carry);
            WeaponInstance worn = Item("old_sword", Equipped);
            CreateBag(carried, worn);
            canEquip = false;

            Assert.That(bag.Move(carried.Id, Carry, Equipped, worn.Id), Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(bag.Drop(worn.Id, Equipped), Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(placed, Is.Null);
            Assert.That(bag.Move(carried.Id, Carry, WeaponLocation.Carried(1), null),
                Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(inventory.Snapshot().Single(item => item.Id == carried.Id).Location.SlotIndex, Is.EqualTo(1));
        }

        [Test]
        public void ActiveExtractionIsReadOnlyAndExpiryInvalidatesAllRequestsEvenAfterANewRun()
        {
            WeaponInstance carried = Item("dagger", Carry);
            CreateBag(carried);
            now = TimeSpan.FromSeconds(2);
            Assert.That(run.TryBeginExtraction(id, true), Is.Not.Null);
            Assert.That(bag.CanOpen, Is.True);
            Assert.That(bag.CanChangeSlots, Is.False);
            Assert.That(bag.Drop(carried.Id, Carry), Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(bag.Move(carried.Id, Carry, Equipped, null), Is.EqualTo(InventoryMoveResult.InvalidRequest));

            now = TimeSpan.FromSeconds(5);
            Assert.That(run.ProcessSignals(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(bag.CanOpen, Is.False);
            Assert.That(bag.Drop(carried.Id, Carry), Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(run.TryReturnToTown(id), Is.True);
            Assert.That(run.TryStartRun(RunId.New(), 1, TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(bag.CanOpen, Is.False);
            Assert.That(bag.Move(carried.Id, Carry, Equipped, null), Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(carried));
            Assert.That(placed, Is.Null);
        }

        private void CreateBag(params WeaponInstance[] items)
        {
            inventory = new InventoryService(items);
            loot = new LootPickupCoordinator(inventory);
            bag = new RunBagActions(run, id, inventory, loot, () => canEquip, Stage);
        }

        private WorldLoot? Stage(WeaponInstance _)
        {
            if (!canDrop)
                return null;
            var item = new GameObject("Staged WorldLoot");
            item.SetActive(false);
            item.transform.position = Vector3.right * 3;
            created.Add(item);
            placed = item.AddComponent<WorldLoot>();
            return placed;
        }

        private bool Observe(WorldLoot item, out InventoryPickupResult result)
            => loot.TryObserve(item.Item, item.transform.position, item.transform.position, 1, out result);

        private WeaponLocationKind Location(WeaponInstance original)
            => inventory.Snapshot().Single(item => item.Id == original.Id).Location.Kind;

        private static WeaponInstance Item(string definition, WeaponLocation location)
            => WeaponInstance.Create(new WeaponDefinitionId(definition), WeaponProvenance.Personal, location);
    }
}
