#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Settlement.Tests
{
    public sealed class FailedSortieSettlementTests
    {
        private static readonly WeaponDefinitionId Sword = new WeaponDefinitionId("old_sword");
        private static readonly WeaponDefinitionId Spear = new WeaponDefinitionId("wooden_spear");

        [TestCase(RunEndReason.Death, true)]
        [TestCase(RunEndReason.TimeExpired, false)]
        public void FailureLosesPackedInstancesEvenIfDroppedAndKeepsUnpackedAssets(RunEndReason reason, bool saveContainsReservations)
        {
            WeaponInstance packedSword = Make(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            WeaponInstance packedDraw = Make(Spear, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            WeaponInstance sameDefinitionInTown = Make(Sword, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            var fixture = Start(reason, packedSword, packedDraw, sameDefinitionInTown);
            var catalog = new CatalogSaveRecord(Sword, "existing recovery");
            var current = new SaveSnapshot(3, 112, saveContainsReservations ? fixture.Town.Snapshot() :
                new[] { packedSword, packedDraw, sameDefinitionInTown }, new[] { catalog }, Array.Empty<SettlementSaveRecord>());
            WeaponInstance stillHeld = WeaponInstance.Rehydrate(packedDraw.Id, packedDraw.DefinitionId,
                packedDraw.Provenance, WeaponLocation.Carried(0));
            WeaponInstance found = Make(Sword, WeaponProvenance.ExplorationLoot, WeaponLocation.Equipped(0));
            var coins = new RunCoinAccumulator(fixture.Run.RunId);
            coins.AddReward(21);
            RunCoinSettlementProposal proposal = coins.ProposeSettlement(new WalletBalance(112), fixture.Run);

            for (int retry = 0; retry < 10; retry++)
            {
                FailedSortieResult result = FailedSortieSettlement.Calculate(fixture.Run, fixture.Manifest,
                    current, new[] { stillHeld, found }, proposal);
                Assert.That(result.Run.Reason, Is.EqualTo(reason));
                Assert.That(result.BaseRevision, Is.EqualTo(3));
                Assert.That(result.LostWeapons.Select(weapon => weapon.Id),
                    Is.EqualTo(new[] { packedSword.Id, packedDraw.Id, found.Id }));
                Assert.That(result.ExpiredLoans, Is.Empty);
                Assert.That(result.PreservedWeapons.Single(), Is.SameAs(sameDefinitionInTown));
                Assert.That(result.PreservedCatalog.Single(), Is.SameAs(catalog));
                Assert.That(result.Coins.ForfeitedCoins, Is.EqualTo(21));
                Assert.That(result.Coins.ResultingBalance.ConfirmedCoins, Is.EqualTo(112));
            }

            Assert.That(current.Revision, Is.EqualTo(3));
            Assert.That(current.Weapons, Has.Count.EqualTo(3));
            Assert.That(coins.PendingCoins, Is.EqualTo(21));
            Assert.That(coins.IsClosed, Is.False);
        }

        [Test]
        public void IssuedLoanExpiresEvenIfDroppedAndHasNeverBeenSaved()
        {
            var runId = RunId.New();
            var town = new InventoryService(Array.Empty<WeaponInstance>());
            WeaponInstance loan = town.PrepareStarterLoan(runId.Value).Loan!;
            var fixture = Start(RunEndReason.Death, town, runId);
            var current = EmptySave();
            var coins = new RunCoinAccumulator(runId).ProposeSettlement(new WalletBalance(0), fixture.Run);

            FailedSortieResult result = FailedSortieSettlement.Calculate(fixture.Run, fixture.Manifest,
                current, Array.Empty<WeaponInstance>(), coins);

            Assert.That(result.LostWeapons, Is.Empty);
            Assert.That(result.ExpiredLoans.Single(), Is.SameAs(loan));
            Assert.That(result.PreservedWeapons, Is.Empty);
        }

        [Test]
        public void SuccessCrossRunAndOutdatedWalletAreRejected()
        {
            WeaponInstance owned = Make(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            var success = Start(RunEndReason.Extracted, owned);
            var failed = Start(RunEndReason.Death, owned);
            var current = new SaveSnapshot(1, 10, failed.Town.Snapshot(), Array.Empty<CatalogSaveRecord>(),
                Array.Empty<SettlementSaveRecord>());
            var failureCoins = new RunCoinAccumulator(failed.Run.RunId);
            var successCoins = new RunCoinAccumulator(success.Run.RunId);
            var held = new[] { owned };

            Assert.Throws<ArgumentException>(() => FailedSortieSettlement.Calculate(success.Run, success.Manifest,
                current, held, successCoins.ProposeSettlement(new WalletBalance(10), success.Run)));
            Assert.Throws<ArgumentException>(() => FailedSortieSettlement.Calculate(failed.Run, success.Manifest,
                current, held, failureCoins.ProposeSettlement(new WalletBalance(10), failed.Run)));
            Assert.Throws<ArgumentException>(() => FailedSortieSettlement.Calculate(failed.Run, failed.Manifest,
                current, held, failureCoins.ProposeSettlement(new WalletBalance(9), failed.Run)));
        }

        [Test]
        public void MalformedEvidenceCannotDiscardAProtectedOrMissingInstance()
        {
            WeaponInstance owned = Make(Sword, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            WeaponInstance stashed = Make(Sword, WeaponProvenance.Personal, WeaponLocation.Warehouse());
            var fixture = Start(RunEndReason.Death, owned, stashed);
            var current = new SaveSnapshot(1, 0, fixture.Town.Snapshot(), Array.Empty<CatalogSaveRecord>(),
                Array.Empty<SettlementSaveRecord>());
            var wallet = new RunCoinAccumulator(fixture.Run.RunId).ProposeSettlement(new WalletBalance(0), fixture.Run);
            WeaponInstance collision = WeaponInstance.Rehydrate(stashed.Id, Sword, WeaponProvenance.ExplorationLoot,
                WeaponLocation.Carried(0));

            Assert.Throws<ArgumentException>(() => FailedSortieSettlement.Calculate(fixture.Run, fixture.Manifest,
                current, new[] { collision }, wallet));
            Assert.Throws<ArgumentException>(() => FailedSortieSettlement.Calculate(fixture.Run, fixture.Manifest,
                current, new[] { owned, owned }, wallet));
            Assert.Throws<ArgumentException>(() => FailedSortieSettlement.Calculate(fixture.Run, fixture.Manifest,
                EmptySave(), new[] { owned }, wallet));
        }

        private static SaveSnapshot EmptySave()
            => new SaveSnapshot(1, 0, Array.Empty<WeaponInstance>(), Array.Empty<CatalogSaveRecord>(), Array.Empty<SettlementSaveRecord>());

        private static WeaponInstance Make(WeaponDefinitionId definition, WeaponProvenance provenance, WeaponLocation location)
            => WeaponInstance.Create(definition, provenance, location);

        private static (RunResult Run, SortieManifest Manifest, InventoryService Town) Start(
            RunEndReason reason, params WeaponInstance[] starting)
            => Start(reason, new InventoryService(starting), RunId.New());

        private static (RunResult Run, SortieManifest Manifest, InventoryService Town) Start(
            RunEndReason reason, InventoryService town, RunId runId)
        {
            TimeSpan now = TimeSpan.Zero;
            var run = new RunState(() => now);
            TimeSpan deadline = TimeSpan.FromSeconds(10);
            SortieManifest manifest = town.TryStartSortie(runId.Value, () => run.TryStartRun(runId, 1, deadline)).Manifest!;
            switch (reason)
            {
                case RunEndReason.Extracted:
                    ExtractionAttempt extract = run.TryBeginExtraction(runId, isInRange: true)!;
                    now = extract.CompletesAt;
                    run.ProcessSignals(new[] { RunSignal.CompleteExtraction(runId, extract.Id, now) });
                    break;
                case RunEndReason.TimeExpired:
                    now = deadline;
                    run.ProcessSignals(Array.Empty<RunSignal>());
                    break;
                default:
                    now = TimeSpan.FromSeconds(1);
                    run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, now) });
                    break;
            }

            return (run.Result!, manifest, town);
        }
    }
}
