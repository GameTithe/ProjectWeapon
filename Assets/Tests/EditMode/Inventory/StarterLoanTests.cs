#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Weapons;
using UnityEditor;

namespace ProjectWeapon.Inventory.Tests
{
    public sealed class StarterLoanTests
    {
        private static readonly Guid RunId = new Guid("08307402-0bb5-4a80-8dc1-17360a88c4f8");
        private static readonly WeaponLocation FirstSlot = WeaponLocation.Equipped(0);
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Bow = new WeaponDefinitionId("hunting_bow");

        [Test]
        public void NoOwnedEquipmentGrantsOneCatalogSwordInFirstSlotWithAllCarrySlotsEmpty()
        {
            WeaponInstance storedSword = WeaponInstance.Create(Sword, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            var inventory = new InventoryService(new[] { storedSword });
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(
                "Assets/ProjectWeapon/Weapons/PrototypeStarter/PrototypeStarterWeaponCatalog.asset");

            StarterLoanResult prepared = inventory.PrepareStarterLoan(RunId);

            Assert.That(prepared.Status, Is.EqualTo(StarterLoanStatus.Issued));
            WeaponInstance loan = prepared.Loan!;
            Assert.That(loan.Id, Is.Not.EqualTo(storedSword.Id));
            Assert.That(loan.DefinitionId, Is.EqualTo(Sword));
            Assert.That(catalog.CreateLookup()[loan.DefinitionId].BasicAttack, Is.Not.Null);
            Assert.That(loan.IsLoaned, Is.True);
            Assert.That(loan.Location.Kind, Is.EqualTo(WeaponLocationKind.Equipped));
            Assert.That(loan.Location.SlotIndex, Is.Zero);
            Assert.That(inventory.Snapshot(), Is.EquivalentTo(new[] { storedSword, loan }));
        }

        [TestCase(WeaponProvenance.Personal)]
        [TestCase(WeaponProvenance.ExplorationLoot)]
        public void OwnedWeaponInEitherEquipSlotUsesOriginalEquipment(WeaponProvenance provenance)
        {
            WeaponInstance equipped = WeaponInstance.Create(Bow, provenance, WeaponLocation.Equipped(1));
            var inventory = new InventoryService(new[] { equipped });

            StarterLoanResult prepared = inventory.PrepareStarterLoan(RunId);

            Assert.That(prepared.Status, Is.EqualTo(StarterLoanStatus.OwnedWeaponEquipped));
            Assert.That(prepared.Loan, Is.Null);
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(equipped));
        }

        [Test]
        public void OwnedReservationForTheSameRunStillCountsAsBroughtEquipment()
        {
            WeaponInstance reserved = WeaponInstance.Create(Bow, WeaponProvenance.Personal, WeaponLocation.RunReserved(RunId));
            var inventory = new InventoryService(new[] { reserved });

            Assert.That(inventory.PrepareStarterLoan(RunId).Status, Is.EqualTo(StarterLoanStatus.OwnedWeaponEquipped));
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(reserved));
        }

        [Test]
        public void RepeatingTheRequestAfterMovingTheLoanOrRebuildingTheInventoryKeepsTheSameInstance()
        {
            var inventory = new InventoryService(Array.Empty<WeaponInstance>());
            WeaponInstance loan = inventory.PrepareStarterLoan(RunId).Loan!;
            WeaponLocation carried = WeaponLocation.Carried(2);
            Assert.That(inventory.Move(loan.Id, FirstSlot, carried), Is.EqualTo(InventoryMoveResult.Moved));

            StarterLoanResult replay = inventory.PrepareStarterLoan(RunId);
            Assert.That(replay.Status, Is.EqualTo(StarterLoanStatus.AlreadyIssued));
            Assert.That(replay.Loan!.Id, Is.EqualTo(loan.Id));
            Assert.That(replay.Loan.Location.SlotIndex, Is.EqualTo(2));
            Assert.That(replay.Loan.Location.Kind, Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(inventory.Snapshot().Count, Is.EqualTo(1));

            var restored = new InventoryService(inventory.Snapshot());
            StarterLoanResult restoredReplay = restored.PrepareStarterLoan(RunId);

            Assert.That(restoredReplay.Status, Is.EqualTo(StarterLoanStatus.AlreadyIssued));
            Assert.That(restoredReplay.Loan!.Id, Is.EqualTo(loan.Id));
            Assert.That(restored.Snapshot().Count, Is.EqualTo(1));
        }

        [Test]
        public void ReplayingARequestAfterTheLoanLeavesTheInventoryDoesNotIssueAgain()
        {
            var inventory = new InventoryService(Array.Empty<WeaponInstance>(), new InventorySlotCapacity(2, 1));
            WeaponInstance loan = inventory.PrepareStarterLoan(RunId).Loan!;
            Assert.That(inventory.Move(loan.Id, FirstSlot, WeaponLocation.Carried(0)), Is.EqualTo(InventoryMoveResult.Moved));
            WeaponInstance found = WeaponInstance.Create(Bow, WeaponProvenance.ExplorationLoot, WeaponLocation.Carried(0));
            InventoryPickupResult overflow = inventory.TryPickup(found);
            Assert.That(overflow.Status, Is.EqualTo(InventoryPickupStatus.ChoiceRequired));
            Assert.That(inventory.ResolvePickup(overflow.Choice, loan.Id).Status, Is.EqualTo(InventoryPickupStatus.PickedUp));

            StarterLoanResult replay = inventory.PrepareStarterLoan(RunId);

            Assert.That(replay.Status, Is.EqualTo(StarterLoanStatus.AlreadyIssued));
            Assert.That(replay.Loan, Is.Null);
            Assert.That(inventory.Snapshot().Single().Id, Is.EqualTo(found.Id));
        }

        [Test]
        public void ConcurrentRequestsForTheSameRunReturnOneGrantAndOneReplay()
        {
            var inventory = new InventoryService(Array.Empty<WeaponInstance>());
            using var start = new Barrier(3);
            var timeout = TimeSpan.FromSeconds(10);
            Task<StarterLoanResult> Request() => Task.Run(() =>
            {
                if (!start.SignalAndWait(timeout))
                    throw new TimeoutException("The concurrent loan requests did not start together.");

                return inventory.PrepareStarterLoan(RunId);
            });

            Task<StarterLoanResult> first = Request();
            Task<StarterLoanResult> second = Request();
            Assert.That(start.SignalAndWait(timeout), Is.True);
            Assert.That(Task.WaitAll(new Task[] { first, second }, timeout), Is.True);

            Assert.That(new[] { first.Result.Status, second.Result.Status },
                Is.EquivalentTo(new[] { StarterLoanStatus.Issued, StarterLoanStatus.AlreadyIssued }));
            Assert.That(first.Result.Loan!.Id, Is.EqualTo(second.Result.Loan!.Id));
            Assert.That(inventory.Snapshot().Count, Is.EqualTo(1));
        }

        [Test]
        public void SaleOrWarehouseMoveForTheLoanIsRejectedAndDoesNotChangeEitherItem()
        {
            WeaponInstance owned = WeaponInstance.Create(Bow, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            var inventory = new InventoryService(new[] { owned });
            WeaponInstance loan = inventory.PrepareStarterLoan(RunId).Loan!;

            Assert.That(inventory.CheckSaleProvenance(loan.Id), Is.EqualTo(InventorySaleProvenance.ProvenanceRestricted));
            Assert.That(inventory.Move(loan.Id, FirstSlot, WeaponLocation.Warehouse()),
                Is.EqualTo(InventoryMoveResult.ProvenanceRestricted));
            Assert.That(inventory.CheckSaleProvenance(owned.Id), Is.EqualTo(InventorySaleProvenance.RequiresSaleRules));
            Assert.That(inventory.CheckSaleProvenance(default), Is.EqualTo(InventorySaleProvenance.InvalidInstance));
            Assert.That(inventory.CheckSaleProvenance(WeaponInstanceId.New()),
                Is.EqualTo(InventorySaleProvenance.MissingInstance));
            Assert.That(inventory.Snapshot(), Is.EquivalentTo(new[] { owned, loan }));
        }

        [Test]
        public void OtherSortieCannotIssueAnotherLoanOverAnExistingOne()
        {
            var inventory = new InventoryService(Array.Empty<WeaponInstance>());
            WeaponInstance first = inventory.PrepareStarterLoan(RunId).Loan!;
            Guid otherRun = Guid.NewGuid();

            Assert.That(inventory.PrepareStarterLoan(otherRun).Status, Is.EqualTo(StarterLoanStatus.InventoryNotPrepared));
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(first));

            var empty = new InventoryService(Array.Empty<WeaponInstance>());
            Assert.That(empty.PrepareStarterLoan(otherRun).Loan!.Id, Is.Not.EqualTo(first.Id));
        }

        [Test]
        public void InvalidRunZeroEquipCapacityAndNonEmptyCarryInventoryRemainUnchanged()
        {
            var noEquipment = new InventoryService(Array.Empty<WeaponInstance>(), new InventorySlotCapacity(0, 3));
            Assert.That(noEquipment.PrepareStarterLoan(Guid.Empty).Status, Is.EqualTo(StarterLoanStatus.InvalidRun));
            Assert.That(noEquipment.PrepareStarterLoan(RunId).Status, Is.EqualTo(StarterLoanStatus.NoEquipmentSlot));
            Assert.That(noEquipment.Snapshot(), Is.Empty);

            WeaponInstance alreadyCarried = WeaponInstance.Create(Bow, WeaponProvenance.Personal, WeaponLocation.Carried(0));
            var unprepared = new InventoryService(new[] { alreadyCarried });
            Assert.That(unprepared.PrepareStarterLoan(RunId).Status, Is.EqualTo(StarterLoanStatus.InventoryNotPrepared));
            Assert.That(unprepared.Snapshot().Single(), Is.SameAs(alreadyCarried));
        }

        [Test]
        public void ConflictingOwnedInstanceIdIsPreservedRatherThanReassigned()
        {
            var initial = new InventoryService(Array.Empty<WeaponInstance>());
            WeaponInstance loan = initial.PrepareStarterLoan(RunId).Loan!;
            WeaponInstance owned = WeaponInstance.Rehydrate(loan.Id, Bow, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            var inventory = new InventoryService(new[] { owned });

            Assert.That(inventory.PrepareStarterLoan(RunId).Status, Is.EqualTo(StarterLoanStatus.InstanceIdConflict));
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(owned));
        }
    }
}
