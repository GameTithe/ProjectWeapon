#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Settlement.Tests
{
    public sealed class SuccessfulSortieSettlementTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Bow = new WeaponDefinitionId("hunting_bow");

        [Test]
        public void RecoversHeldInstancesAndDistinctDefinitionsWhileLeavingDiscardedEquipmentBehind()
        {
            WeaponInstance abandoned = Make(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            WeaponInstance retained = Make(Bow, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            WeaponInstance moved = At(retained, WeaponLocation.Carried(1));
            WeaponInstance found1 = Make(Bow, WeaponProvenance.ExplorationLoot, WeaponLocation.Carried(0));
            WeaponInstance found2 = Make(Bow, WeaponProvenance.ExplorationLoot, WeaponLocation.Equipped(0));
            var fixture = Start(new InventoryService(new[] { abandoned, retained }), RunId.New(), success: true);
            var wallet = new RunCoinAccumulator(fixture.Run.RunId);
            wallet.AddReward(37);
            RunCoinSettlementProposal coins = wallet.ProposeSettlement(new WalletBalance(100), fixture.Run);

            for (int retry = 0; retry < 10; retry++)
            {
                SuccessfulSortieResult result = SuccessfulSortieSettlement.Calculate(fixture.Run, fixture.Manifest,
                    new[] { moved, found1, found2 }, coins);
                Assert.That(result.RecoveredWeapons.Select(item => item.Id),
                    Is.EqualTo(new[] { retained.Id, found1.Id, found2.Id }));
                Assert.That(result.RecoveredWeapons.All(item => item.Location.Kind == WeaponLocationKind.Warehouse), Is.True);
                Assert.That(result.RecoveredWeapons[1].Provenance, Is.EqualTo(WeaponProvenance.ExplorationLoot));
                Assert.That(result.RecoveredDefinitions, Is.EqualTo(new[] { Bow }));
                Assert.That(result.AbandonedPackedWeapons.Single().Id, Is.EqualTo(abandoned.Id));
                Assert.That(result.ReturnedLoans, Is.Empty);
                Assert.That(result.Coins.ResultingBalance.ConfirmedCoins, Is.EqualTo(137));
                Assert.That(result.Coins.AwardedCoins, Is.EqualTo(37));
            }

            Assert.That(retained.Location.SlotIndex, Is.EqualTo(1));
            Assert.That(found1.Location.Kind, Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(wallet.IsClosed, Is.False);
            Assert.That(wallet.PendingCoins, Is.EqualTo(37));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LoanIsReturnedEvenIfDroppedAndDoesNotCountAsARecovery(bool held)
        {
            RunId id = RunId.New();
            var town = new InventoryService(Array.Empty<WeaponInstance>());
            WeaponInstance loan = town.PrepareStarterLoan(id.Value).Loan!;
            var fixture = Start(town, id, success: true);
            var coins = new RunCoinAccumulator(id).ProposeSettlement(new WalletBalance(0), fixture.Run);

            SuccessfulSortieResult result = SuccessfulSortieSettlement.Calculate(fixture.Run, fixture.Manifest,
                held ? new[] { loan } : Array.Empty<WeaponInstance>(), coins);

            Assert.That(result.ReturnedLoans.Single(), Is.SameAs(loan));
            Assert.That(result.RecoveredWeapons, Is.Empty);
            Assert.That(result.RecoveredDefinitions, Is.Empty);
            Assert.That(result.AbandonedPackedWeapons, Is.Empty);
        }

        [Test]
        public void WrongRunFailureAndConflictingIdentityAreRejected()
        {
            WeaponInstance owned = Make(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            var success = Start(new InventoryService(new[] { owned }), RunId.New(), success: true);
            var failure = Start(new InventoryService(new[] { owned }), RunId.New(), success: false);
            var wallet = new RunCoinAccumulator(success.Run.RunId);
            var coins = wallet.ProposeSettlement(new WalletBalance(0), success.Run);
            var failureCoins = new RunCoinAccumulator(failure.Run.RunId).ProposeSettlement(new WalletBalance(0), failure.Run);
            WeaponInstance mismatched = WeaponInstance.Rehydrate(owned.Id, Bow, owned.Provenance, owned.Location);
            WeaponInstance foreign = Make(Bow, WeaponProvenance.Personal, WeaponLocation.Carried(0));

            Assert.Throws<ArgumentException>(() => SuccessfulSortieSettlement.Calculate(failure.Run, failure.Manifest,
                new[] { owned }, failureCoins));
            Assert.Throws<ArgumentException>(() => SuccessfulSortieSettlement.Calculate(success.Run, failure.Manifest,
                new[] { owned }, coins));
            Assert.Throws<ArgumentException>(() => SuccessfulSortieSettlement.Calculate(success.Run, success.Manifest,
                new[] { owned }, failureCoins));
            Assert.Throws<ArgumentException>(() => SuccessfulSortieSettlement.Calculate(success.Run, success.Manifest,
                new[] { mismatched }, coins));
            Assert.Throws<ArgumentException>(() => SuccessfulSortieSettlement.Calculate(success.Run, success.Manifest,
                new[] { foreign }, coins));
            Assert.Throws<ArgumentException>(() => SuccessfulSortieSettlement.Calculate(success.Run, success.Manifest,
                new[] { owned, owned }, coins));
        }

        private static WeaponInstance Make(WeaponDefinitionId definition, WeaponProvenance provenance, WeaponLocation location)
            => WeaponInstance.Create(definition, provenance, location);

        private static WeaponInstance At(WeaponInstance instance, WeaponLocation location)
            => WeaponInstance.Rehydrate(instance.Id, instance.DefinitionId, instance.Provenance, location);

        private static (RunResult Run, SortieManifest Manifest) Start(InventoryService town, RunId id, bool success)
        {
            TimeSpan now = TimeSpan.Zero;
            var state = new RunState(() => now);
            var manifest = town.TryStartSortie(id.Value,
                () => state.TryStartRun(id, 1, TimeSpan.FromSeconds(10))).Manifest!;
            if (success)
            {
                var attempt = state.TryBeginExtraction(id, isInRange: true)!;
                now = attempt.CompletesAt;
                state.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, now) });
            }
            else
            {
                now = TimeSpan.FromSeconds(1);
                state.ProcessSignals(new[] { RunSignal.PlayerDied(id, now) });
            }

            return (state.Result!, manifest);
        }
    }
}
