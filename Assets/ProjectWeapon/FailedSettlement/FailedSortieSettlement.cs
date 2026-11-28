#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Wallet;

namespace ProjectWeapon.Settlement
{
    public sealed class FailedSortieResult
    {
        internal FailedSortieResult(RunResult run, SaveSnapshot current, RunCoinSettlementProposal coins,
            WeaponInstance[] preservedWeapons, WeaponInstance[] lostWeapons, WeaponInstance[] expiredLoans)
        {
            Run = run;
            BaseRevision = current.Revision;
            Coins = coins;
            PreservedWeapons = Array.AsReadOnly(preservedWeapons);
            LostWeapons = Array.AsReadOnly(lostWeapons);
            ExpiredLoans = Array.AsReadOnly(expiredLoans);
            PreservedCatalog = current.Catalog;
        }

        public RunResult Run { get; }
        public long BaseRevision { get; }
        public RunCoinSettlementProposal Coins { get; }
        public IReadOnlyList<WeaponInstance> PreservedWeapons { get; }
        public IReadOnlyList<WeaponInstance> LostWeapons { get; }
        public IReadOnlyList<WeaponInstance> ExpiredLoans { get; }
        public IReadOnlyList<CatalogSaveRecord> PreservedCatalog { get; }
    }

    /// <summary>Calculates the effects of a finalized failure without writing any game state.</summary>
    public static class FailedSortieSettlement
    {
        public static FailedSortieResult Calculate(RunResult run, SortieManifest manifest, SaveSnapshot current,
            IReadOnlyList<WeaponInstance> heldAtEnd, RunCoinSettlementProposal coins)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (current == null)
                throw new ArgumentNullException(nameof(current));
            if (heldAtEnd == null)
                throw new ArgumentNullException(nameof(heldAtEnd));
            if (run.Reason != RunEndReason.Death && run.Reason != RunEndReason.TimeExpired)
                throw new ArgumentException("A failure result requires death or expiry.", nameof(run));
            if (run.RunId.Value != manifest.RunId)
                throw new ArgumentException("The sortie manifest belongs to another run.", nameof(manifest));
            if (coins.RunId != run.RunId || coins.Reason != run.Reason ||
                coins.PreviousBalance.ConfirmedCoins != current.ConfirmedCoins)
                throw new ArgumentException("The coin proposal does not match the run and save balance.", nameof(coins));
            if (WeaponInstanceValidator.Validate(heldAtEnd).Count != 0)
                throw new ArgumentException("The field inventory contains invalid or duplicate instances.", nameof(heldAtEnd));

            var reserved = manifest.Equipped.ToDictionary(weapon => weapon.Id);
            var persistent = current.Weapons.ToDictionary(weapon => weapon.Id);
            foreach (WeaponInstance packed in manifest.Equipped)
            {
                if (!persistent.TryGetValue(packed.Id, out WeaponInstance saved))
                {
                    if (!packed.IsLoaned)
                        throw new ArgumentException("An owned manifest instance is missing from the save.", nameof(current));
                    continue;
                }

                bool matchingLocation = saved.Location.ReservedRunId == manifest.RunId ||
                    (saved.Location.Kind == WeaponLocationKind.Equipped && saved.Location.SlotIndex == packed.Location.SlotIndex);
                if (!matchingLocation || !HasSameIdentity(packed, saved))
                    throw new ArgumentException("A saved instance conflicts with its sortie manifest.", nameof(current));
            }

            if (current.Weapons.Any(weapon => weapon.Location.ReservedRunId == manifest.RunId &&
                                               !reserved.ContainsKey(weapon.Id)))
                throw new ArgumentException("A reservation is missing from the sortie manifest.", nameof(current));

            var losses = new List<WeaponInstance>(manifest.Equipped.Where(weapon => !weapon.IsLoaned));
            foreach (WeaponInstance held in heldAtEnd)
            {
                if (held.Location.Kind != WeaponLocationKind.Equipped && held.Location.Kind != WeaponLocationKind.Carried)
                    throw new ArgumentException("Only equipped or carried instances belong to the field inventory.", nameof(heldAtEnd));

                if (reserved.TryGetValue(held.Id, out WeaponInstance packed))
                {
                    if (!HasSameIdentity(packed, held))
                        throw new ArgumentException("A held instance conflicts with its sortie manifest.", nameof(heldAtEnd));
                    continue;
                }

                if (held.Provenance != WeaponProvenance.ExplorationLoot || persistent.ContainsKey(held.Id))
                    throw new ArgumentException("A new field instance must be exploration loot with a distinct ID.", nameof(heldAtEnd));

                losses.Add(held);
            }

            WeaponInstance[] preserved = current.Weapons.Where(weapon => !reserved.ContainsKey(weapon.Id)).ToArray();
            WeaponInstance[] loans = manifest.Equipped.Where(weapon => weapon.IsLoaned).ToArray();
            return new FailedSortieResult(run, current, coins, preserved, losses.ToArray(), loans);
        }

        private static bool HasSameIdentity(WeaponInstance first, WeaponInstance second)
            => first.Provenance == second.Provenance && first.DefinitionId.Equals(second.DefinitionId);
    }
}
