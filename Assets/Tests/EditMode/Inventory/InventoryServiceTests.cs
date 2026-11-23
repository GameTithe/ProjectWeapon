#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Inventory.Tests
{
    public sealed class InventoryServiceTests
    {
        private static readonly WeaponLocation Warehouse = WeaponLocation.Warehouse();
        private static readonly WeaponLocation Equipped = WeaponLocation.Equipped(0);
        private static readonly WeaponLocation Carried = WeaponLocation.Carried(0);

        [Test]
        public void MovesAmongAllThreeAreasWhilePreservingIdentityAndProvenance()
        {
            WeaponInstance existing = MakeWeapon(Warehouse, WeaponProvenance.ExplorationLoot);
            var inventory = new InventoryService(new[] { existing });
            IReadOnlyList<WeaponInstance> before = inventory.Snapshot();

            Assert.That(inventory.Move(existing.Id, Warehouse, Equipped), Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(inventory.Move(existing.Id, Equipped, Carried), Is.EqualTo(InventoryMoveResult.Moved));
            AssertPosition(Find(inventory, existing.Id), Carried);
            Assert.That(inventory.Move(existing.Id, Carried, Warehouse), Is.EqualTo(InventoryMoveResult.Moved));

            WeaponInstance after = inventory.Snapshot().Single();
            Assert.That(after.Id, Is.EqualTo(existing.Id));
            Assert.That(after.DefinitionId, Is.EqualTo(existing.DefinitionId));
            Assert.That(after.Provenance, Is.EqualTo(existing.Provenance));
            AssertPosition(after, Warehouse);
            AssertPosition(before.Single(), Warehouse);
        }

        [Test]
        public void RejectedOccupiedMovePreservesBothItemsAndCanRetryWhenSlotFrees()
        {
            WeaponInstance candidate = MakeWeapon(Warehouse);
            WeaponInstance occupant = MakeWeapon(Equipped);
            var inventory = new InventoryService(new[] { candidate, occupant });

            Assert.That(inventory.Move(candidate.Id, Warehouse, Equipped),
                Is.EqualTo(InventoryMoveResult.DestinationOccupied));
            Assert.That(Find(inventory, candidate.Id), Is.SameAs(candidate));
            Assert.That(Find(inventory, occupant.Id), Is.SameAs(occupant));

            Assert.That(inventory.Move(occupant.Id, Equipped, Carried), Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(inventory.Move(candidate.Id, Warehouse, Equipped), Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(inventory.Move(candidate.Id, Warehouse, Equipped),
                Is.EqualTo(InventoryMoveResult.SourceMismatch));
            Assert.That(inventory.Move(candidate.Id, Equipped, Equipped),
                Is.EqualTo(InventoryMoveResult.AlreadyAtDestination));
            Assert.That(inventory.Snapshot().Count, Is.EqualTo(2));
            AssertPosition(Find(inventory, candidate.Id), Equipped);
            AssertPosition(Find(inventory, occupant.Id), Carried);
        }

        [Test]
        public void MissingInvalidAndStaleSourceRequestsLeaveOriginalUnchanged()
        {
            WeaponInstance existing = MakeWeapon(Warehouse);
            var inventory = new InventoryService(new[] { existing });

            Assert.That(inventory.Move(WeaponInstanceId.New(), Warehouse, Equipped),
                Is.EqualTo(InventoryMoveResult.MissingInstance));
            Assert.That(inventory.Move(default, Warehouse, Equipped),
                Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(inventory.Move(existing.Id, default, Equipped),
                Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(inventory.Move(existing.Id, Warehouse, default),
                Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(inventory.Move(existing.Id, Carried, Equipped),
                Is.EqualTo(InventoryMoveResult.SourceMismatch));
            Assert.That(inventory.Snapshot().Single(), Is.SameAs(existing));
        }

        [Test]
        public void ExistingLoansAndRunReservationsKeepTheirRestrictions()
        {
            WeaponLocation reservation = WeaponLocation.RunReserved(Guid.NewGuid());
            WeaponInstance loan = MakeWeapon(Equipped, WeaponProvenance.Loan);
            WeaponInstance reserved = MakeWeapon(reservation);
            var inventory = new InventoryService(new[] { loan, reserved });

            Assert.That(inventory.Move(loan.Id, Equipped, Warehouse),
                Is.EqualTo(InventoryMoveResult.ProvenanceRestricted));
            Assert.That(inventory.Move(loan.Id, Equipped, reservation),
                Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(inventory.Move(reserved.Id, reservation, Carried),
                Is.EqualTo(InventoryMoveResult.InvalidRequest));
            Assert.That(inventory.Move(reserved.Id, Warehouse, Carried),
                Is.EqualTo(InventoryMoveResult.SourceMismatch));
            Assert.That(Find(inventory, loan.Id), Is.SameAs(loan));
            Assert.That(Find(inventory, reserved.Id), Is.SameAs(reserved));

            Assert.That(inventory.Move(loan.Id, Equipped, Carried), Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(Find(inventory, loan.Id).IsLoaned, Is.True);
        }

        [Test]
        public void InitialSnapshotRejectsDuplicateIdentityAndSlotOccupancy()
        {
            WeaponInstance first = MakeWeapon(Equipped);
            WeaponInstance duplicate = WeaponInstance.Rehydrate(first.Id, first.DefinitionId, first.Provenance, Warehouse);

            Assert.Throws<ArgumentException>(() => new InventoryService(new[] { first, duplicate }));
            Assert.Throws<ArgumentException>(() => new InventoryService(new[] { first, MakeWeapon(Equipped) }));
            Assert.Throws<ArgumentException>(() => new InventoryService(new[] { first, null! }));
            Assert.DoesNotThrow(() => new InventoryService(new[] { MakeWeapon(Warehouse), MakeWeapon(Warehouse) }));
        }

        [Test]
        public void ConcurrentMovesOfOneInstanceAllowOnlyOneToClaimItsSource()
        {
            WeaponInstance existing = MakeWeapon(Warehouse);
            var inventory = new InventoryService(new[] { existing });

            (InventoryMoveResult first, InventoryMoveResult second) = RunTogether(
                () => inventory.Move(existing.Id, Warehouse, Equipped),
                () => inventory.Move(existing.Id, Warehouse, Carried));

            Assert.That(new[] { first, second },
                Is.EquivalentTo(new[] { InventoryMoveResult.Moved, InventoryMoveResult.SourceMismatch }));
            WeaponInstance survivor = inventory.Snapshot().Single();
            Assert.That(survivor.Id, Is.EqualTo(existing.Id));
            AssertPosition(survivor, first == InventoryMoveResult.Moved ? Equipped : Carried);
        }

        [Test]
        public void ConcurrentClaimsForOneSlotKeepTheLosingInstanceInPlace()
        {
            WeaponInstance left = MakeWeapon(Warehouse);
            WeaponInstance right = MakeWeapon(Warehouse);
            var inventory = new InventoryService(new[] { left, right });

            (InventoryMoveResult first, InventoryMoveResult second) = RunTogether(
                () => inventory.Move(left.Id, Warehouse, Equipped),
                () => inventory.Move(right.Id, Warehouse, Equipped));

            Assert.That(new[] { first, second },
                Is.EquivalentTo(new[] { InventoryMoveResult.Moved, InventoryMoveResult.DestinationOccupied }));
            Assert.That(inventory.Snapshot().Count, Is.EqualTo(2));
            bool leftWon = first == InventoryMoveResult.Moved;
            AssertPosition(Find(inventory, left.Id), leftWon ? Equipped : Warehouse);
            AssertPosition(Find(inventory, right.Id), leftWon ? Warehouse : Equipped);
            Assert.That(Find(inventory, leftWon ? right.Id : left.Id), Is.SameAs(leftWon ? right : left));
        }

        private static WeaponInstance MakeWeapon(WeaponLocation location,
            WeaponProvenance provenance = WeaponProvenance.Personal) =>
            WeaponInstance.Create(new WeaponDefinitionId("old-sword"), provenance, location);

        private static WeaponInstance Find(InventoryService inventory, WeaponInstanceId id) =>
            inventory.Snapshot().Single(instance => instance.Id == id);

        private static void AssertPosition(WeaponInstance instance, WeaponLocation location)
        {
            Assert.That(instance.Location.Kind, Is.EqualTo(location.Kind));
            Assert.That(instance.Location.SlotIndex, Is.EqualTo(location.SlotIndex));
        }

        private static (InventoryMoveResult First, InventoryMoveResult Second) RunTogether(
            Func<InventoryMoveResult> first, Func<InventoryMoveResult> second)
        {
            var timeout = TimeSpan.FromSeconds(10);
            using var start = new Barrier(3);

            Task<InventoryMoveResult> Start(Func<InventoryMoveResult> operation) => Task.Run(() =>
            {
                if (!start.SignalAndWait(timeout))
                    throw new TimeoutException("The concurrent moves did not start together.");

                return operation();
            });

            Task<InventoryMoveResult> firstTask = Start(first);
            Task<InventoryMoveResult> secondTask = Start(second);
            Assert.That(start.SignalAndWait(timeout), Is.True);
            Assert.That(Task.WaitAll(new Task[] { firstTask, secondTask }, timeout), Is.True);
            return (firstTask.Result, secondTask.Result);
        }
    }
}
