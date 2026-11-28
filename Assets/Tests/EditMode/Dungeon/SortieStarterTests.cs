#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;

namespace ProjectWeapon.Dungeon.Tests
{
    public sealed class SortieStarterTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private ZoneRunCountdown countdown = null!;
        private RunState run = null!;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(
                "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            run = new RunState(() => TimeSpan.Zero);
            countdown = new ZoneRunCountdown(catalog, run);
        }

        [Test]
        public void StartAllocatesIdAndFreezesTheTwoEquippedInstancesInSlotOrder()
        {
            WeaponInstance second = Personal(WeaponLocation.Equipped(1));
            WeaponInstance first = Personal(WeaponLocation.Equipped(0));
            WeaponInstance warehouse = Personal(WeaponLocation.Warehouse());
            var town = new InventoryService(new[] { second, warehouse, first });

            SortieStartResult started = new SortieStarter(town, countdown).TryStart(1);
            Assert.That(started.Status, Is.EqualTo(SortieStartStatus.Started));
            SortieManifest manifest = started.Manifest!;
            Assert.That(manifest.RunId, Is.EqualTo(run.CurrentRun!.Id.Value));
            Assert.That(run.CurrentRun.Deadline, Is.EqualTo(TimeSpan.FromMinutes(8)));
            Assert.That(manifest.Equipped.Select(weapon => weapon.Id), Is.EqualTo(new[] { first.Id, second.Id }));
            Assert.That(town.Snapshot().Single(weapon => weapon.Id == warehouse.Id).Location.Kind,
                Is.EqualTo(WeaponLocationKind.Warehouse));
            Assert.That(town.Snapshot().Where(weapon => weapon.Id != warehouse.Id)
                .All(weapon => weapon.Location.ReservedRunId == manifest.RunId), Is.True);

            var loadout = new InventoryService(manifest.Equipped);
            Assert.That(loadout.Move(first.Id, WeaponLocation.Equipped(0), WeaponLocation.Carried(0)),
                Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(manifest.Equipped[0].Location.SlotIndex, Is.EqualTo(0));
            Assert.That(manifest.Equipped[0].Location.Kind, Is.EqualTo(WeaponLocationKind.Equipped));
        }

        [Test]
        public void LoanProvenanceIsPreservedWhenPreparedAloneInTheFirstSlot()
        {
            WeaponInstance loan = WeaponInstance.Create(Sword, WeaponProvenance.Loan, WeaponLocation.Equipped(0));
            var town = new InventoryService(new[] { loan });

            SortieStartResult started = new SortieStarter(town, countdown).TryStart(1);
            Assert.That(started.Status, Is.EqualTo(SortieStartStatus.Started));
            Assert.That(started.Manifest!.Equipped.Single().IsLoaned, Is.True);
            Assert.That(town.Snapshot().Single().IsLoaned, Is.True);
        }

        [Test]
        public void DeclinedOrThrowingRunStartLeavesTheTownSnapshotUnchanged()
        {
            WeaponInstance first = Personal(WeaponLocation.Equipped(0));
            var town = new InventoryService(new[] { first });
            var starter = new SortieStarter(town, countdown);
            Assert.That(run.TryStartRun(RunId.New(), 1, TimeSpan.FromMinutes(1)), Is.True);

            SortieStartResult rejected = starter.TryStart(1);
            Assert.That(rejected.Status, Is.EqualTo(SortieStartStatus.RunRejected));
            Assert.That(rejected.Manifest, Is.Null);
            Assert.That(town.Snapshot().Single(), Is.SameAs(first));
            Assert.Throws<InvalidOperationException>(() =>
                town.TryStartSortie(Guid.NewGuid(), () => throw new InvalidOperationException()));
            Assert.That(town.Snapshot().Single(), Is.SameAs(first));

            Assert.That(town.TryStartSortie(Guid.NewGuid(), () => true).Status, Is.EqualTo(SortieStartStatus.Started));
        }

        [Test]
        public void CarrySlotsMustBeEmptyBeforeAPlayerCanStart()
        {
            WeaponInstance first = Personal(WeaponLocation.Equipped(0));
            WeaponInstance carried = Personal(WeaponLocation.Carried(0));
            var town = new InventoryService(new[] { first, carried });

            Assert.That(new SortieStarter(town, countdown).TryStart(1).Status, Is.EqualTo(SortieStartStatus.LoadoutNotReady));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Town));
            Assert.That(town.Snapshot().Single(weapon => weapon.Id == carried.Id), Is.SameAs(carried));
        }

        [Test]
        public void CompetingSortieIdsCannotReserveTheSamePhysicalItemTwice()
        {
            var town = new InventoryService(new[] { Personal(WeaponLocation.Equipped(0)) });
            Guid first = Guid.NewGuid();
            Guid second = Guid.NewGuid();
            SortieStartResult firstResult = default;
            SortieStartResult secondResult = default;
            Parallel.Invoke(
                () => firstResult = town.TryStartSortie(first, () => true),
                () => secondResult = town.TryStartSortie(second, () => true));

            Assert.That(new[] { firstResult.Status, secondResult.Status },
                Is.EquivalentTo(new[] { SortieStartStatus.Started, SortieStartStatus.InventoryAlreadyReserved }));
            Guid? reservedFor = town.Snapshot().Single().Location.ReservedRunId;
            Assert.That(reservedFor, Is.EqualTo((firstResult.Manifest ?? secondResult.Manifest)!.RunId));
        }

        private static WeaponInstance Personal(WeaponLocation location)
            => WeaponInstance.Create(Sword, WeaponProvenance.Personal, location);
    }
}
