#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ProjectWeapon.Catalog;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Wallet;

[assembly: InternalsVisibleTo("ProjectWeapon.Settlement.Tests")]

namespace ProjectWeapon.Settlement
{
    public enum RunSettlementStatus
    {
        Committed,
        AlreadyCommitted,
        ConflictingOutcome,
        RetryRequired,
        MissingSave
    }

    public sealed class RunSettlementCommit
    {
        internal RunSettlementCommit(RunSettlementStatus status, SaveSnapshot? snapshot, SettlementSaveRecord? receipt = null)
        {
            Status = status;
            Snapshot = snapshot;
            Receipt = receipt;
        }

        public RunSettlementStatus Status { get; }
        public SaveSnapshot? Snapshot { get; }
        public SettlementSaveRecord? Receipt { get; }
    }

    public sealed class RunSettlementCoordinator
    {
        private readonly AtomicSnapshotStore store;
        private readonly Func<long, SaveSnapshot, SnapshotCommitResult> commit;

        public RunSettlementCoordinator(AtomicSnapshotStore store)
            : this(store, (store ?? throw new ArgumentNullException(nameof(store))).Commit)
        {
        }

        internal RunSettlementCoordinator(AtomicSnapshotStore store, Func<long, SaveSnapshot, SnapshotCommitResult> commit)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
        }

        public RunSettlementCommit TrySettle(RunResult run, SortieManifest manifest,
            IReadOnlyList<WeaponInstance> heldAtEnd, RunCoinAccumulator wallet, bool requireExactReservations = false)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (heldAtEnd == null)
                throw new ArgumentNullException(nameof(heldAtEnd));
            if (wallet == null)
                throw new ArgumentNullException(nameof(wallet));
            if (manifest.RunId != run.RunId.Value || wallet.RunId != run.RunId)
                throw new ArgumentException("The manifest and wallet must belong to the finalized run.");

            SaveSnapshot? current = store.Load();
            if (current == null)
                return new RunSettlementCommit(RunSettlementStatus.MissingSave, null);
            if (ReadExisting(current, run) is RunSettlementCommit existing)
                return existing;
            if (requireExactReservations && !manifest.MatchesReservations(current.Weapons))
                return new RunSettlementCommit(RunSettlementStatus.RetryRequired, current);

            RunCoinSettlementProposal coins = wallet.ProposeSettlement(new WalletBalance(current.ConfirmedCoins), run);
            SaveSnapshot candidate = BuildCandidate(current, run, manifest, heldAtEnd.ToArray(), coins);
            SnapshotCommitResult write = commit(current.Revision, candidate);
            if (write.Status == SnapshotCommitStatus.Committed)
                return new RunSettlementCommit(RunSettlementStatus.Committed, candidate, candidate.Settlements.Last());

            SaveSnapshot? active = write.ActiveSnapshot;
            return (active != null ? ReadExisting(active, run) : null)
                ?? new RunSettlementCommit(RunSettlementStatus.RetryRequired, active);
        }

        private static SaveSnapshot BuildCandidate(SaveSnapshot current, RunResult run, SortieManifest manifest,
            IReadOnlyList<WeaponInstance> heldAtEnd, RunCoinSettlementProposal coins)
        {
            IReadOnlyList<WeaponInstance> nextWeapons;
            IReadOnlyList<CatalogSaveRecord> nextCatalog;
            RunSettlementDetails details;
            long nextBalance = coins.ResultingBalance.ConfirmedCoins;
            IReadOnlyList<CatalogMilestoneSaveRecord> milestones = current.CatalogMilestones;
            int warehouseCount = current.Weapons.Count(item => item.Location.Kind == WeaponLocationKind.Warehouse);
            if (run.Succeeded)
            {
                SuccessfulSortieResult recovered = SuccessfulSortieSettlement.Calculate(run, manifest, heldAtEnd, coins);
                nextWeapons = ApplySuccessfulRecovery(current, manifest, recovered);
                var catalog = new CatalogProgress(current.Catalog);
                var catalogRecoveries = new List<string>();
                foreach (WeaponInstance weapon in heldAtEnd)
                {
                    if (catalog.RecordRecovery(run, weapon))
                        catalogRecoveries.Add(weapon.DefinitionId.Value);
                }
                nextCatalog = catalog.ToSaveRecords();
                IReadOnlyList<CatalogMilestoneSaveRecord> unpaid = CatalogMilestoneRewards.Unpaid(run, catalog, milestones);
                if (unpaid.Count > 0)
                {
                    nextBalance = checked(nextBalance + unpaid.Sum(item => item.AwardedCoins));
                    milestones = milestones.Concat(unpaid).ToArray();
                }
                details = new RunSettlementDetails(coins, manifest, recovered.RecoveredWeapons,
                    recovered.AbandonedPackedWeapons, recovered.ReturnedLoans, catalogRecoveries, warehouseCount);
            }
            else
            {
                FailedSortieResult failed = FailedSortieSettlement.Calculate(run, manifest, current, heldAtEnd, coins);
                nextWeapons = failed.PreservedWeapons;
                nextCatalog = failed.PreservedCatalog;
                details = new RunSettlementDetails(coins, manifest, Array.Empty<WeaponInstance>(), failed.LostWeapons,
                    failed.ExpiredLoans, Array.Empty<string>(), warehouseCount);
            }

            var receipt = new SettlementSaveRecord(run.RunId.Value, "RunSettlement:" + run.RunId, details.Encode());
            return current.Next(nextBalance, weapons: nextWeapons, catalog: nextCatalog,
                settlements: current.Settlements.Append(receipt), catalogMilestones: milestones);
        }

        private static IReadOnlyList<WeaponInstance> ApplySuccessfulRecovery(SaveSnapshot current, SortieManifest manifest,
            SuccessfulSortieResult outcome)
        {
            var packed = manifest.Equipped.ToDictionary(item => item.Id);
            var saved = current.Weapons.ToDictionary(item => item.Id);
            foreach (WeaponInstance startItem in manifest.Equipped)
            {
                if (!saved.TryGetValue(startItem.Id, out WeaponInstance savedItem))
                {
                    if (!startItem.IsLoaned)
                        throw new ArgumentException("An owned manifest instance is missing from the save.", nameof(current));
                    continue;
                }

                bool matchingLocation = savedItem.Location.ReservedRunId == manifest.RunId ||
                    (savedItem.Location.Kind == WeaponLocationKind.Equipped && savedItem.Location.SlotIndex == startItem.Location.SlotIndex);
                if (!matchingLocation || savedItem.Provenance != startItem.Provenance ||
                    !savedItem.DefinitionId.Equals(startItem.DefinitionId))
                    throw new ArgumentException("A saved instance conflicts with its sortie manifest.", nameof(current));
            }

            var next = new List<WeaponInstance>();
            foreach (WeaponInstance savedItem in current.Weapons)
            {
                if (packed.ContainsKey(savedItem.Id))
                    continue;
                if (savedItem.Location.ReservedRunId == manifest.RunId)
                    throw new ArgumentException("A reservation is missing from the sortie manifest.", nameof(current));
                next.Add(savedItem);
            }

            foreach (WeaponInstance item in outcome.RecoveredWeapons)
            {
                if (!packed.ContainsKey(item.Id) && saved.ContainsKey(item.Id))
                    throw new ArgumentException("A recovered item collides with a saved weapon ID.", nameof(outcome));
                next.Add(item);
            }
            return next;
        }

        private static RunSettlementCommit? ReadExisting(SaveSnapshot current, RunResult request)
        {
            SettlementSaveRecord? receipt = current.Settlements.FirstOrDefault(item => item.RunId == request.RunId.Value);
            if (receipt == null)
                return null;

            RunSettlementDetails? details = RunSettlementDetails.Read(receipt);
            var status = RunSettlementStatus.AlreadyCommitted;
            if (details != null)
            {
                if (details.Reason != request.Reason)
                    status = RunSettlementStatus.ConflictingOutcome;
            }
            else if (RunSettlementDetails.ParseReason(receipt.Result, out RunEndReason legacyReason)
                && legacyReason != request.Reason)
            {
                status = RunSettlementStatus.ConflictingOutcome;
            }

            return new RunSettlementCommit(status, current, receipt);
        }
    }
}
