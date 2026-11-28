#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Settlement
{
    public sealed class SuccessfulSortieResult
    {
        internal SuccessfulSortieResult(RunResult run, RunCoinSettlementProposal coins,
            WeaponInstance[] recovered, WeaponInstance[] abandoned, WeaponInstance[] loans)
        {
            Run = run;
            Coins = coins;
            RecoveredWeapons = Array.AsReadOnly(recovered);
            AbandonedPackedWeapons = Array.AsReadOnly(abandoned);
            ReturnedLoans = Array.AsReadOnly(loans);
            RecoveredDefinitions = Array.AsReadOnly(recovered.Select(item => item.DefinitionId).Distinct().ToArray());
        }

        public RunResult Run { get; }
        public RunCoinSettlementProposal Coins { get; }
        public IReadOnlyList<WeaponInstance> RecoveredWeapons { get; }
        public IReadOnlyList<WeaponDefinitionId> RecoveredDefinitions { get; }
        public IReadOnlyList<WeaponInstance> AbandonedPackedWeapons { get; }
        public IReadOnlyList<WeaponInstance> ReturnedLoans { get; }
    }

    /// <summary>Returns proposed recoveries from the inventory held at the extraction cutoff.</summary>
    public static class SuccessfulSortieSettlement
    {
        public static SuccessfulSortieResult Calculate(RunResult run, SortieManifest manifest,
            IReadOnlyList<WeaponInstance> heldAtExtraction, RunCoinSettlementProposal coins)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (heldAtExtraction == null)
                throw new ArgumentNullException(nameof(heldAtExtraction));
            if (!run.Succeeded)
                throw new ArgumentException("A successful settlement requires a finalized extraction.", nameof(run));
            if (manifest.RunId != run.RunId.Value)
                throw new ArgumentException("The sortie manifest belongs to another run.", nameof(manifest));
            if (coins.RunId != run.RunId || coins.Reason != run.Reason)
                throw new ArgumentException("The coin proposal belongs to another run or outcome.", nameof(coins));
            if (WeaponInstanceValidator.Validate(heldAtExtraction).Count != 0)
                throw new ArgumentException("The field inventory contains invalid or duplicate instances.", nameof(heldAtExtraction));

            var packed = manifest.Equipped.ToDictionary(item => item.Id);
            var heldIds = new HashSet<WeaponInstanceId>();
            var recovery = new List<WeaponInstance>();
            foreach (WeaponInstance held in heldAtExtraction)
            {
                if (held.Location.Kind != WeaponLocationKind.Equipped && held.Location.Kind != WeaponLocationKind.Carried)
                    throw new ArgumentException("Only equipped or carried instances can be recovered.", nameof(heldAtExtraction));
                if (packed.TryGetValue(held.Id, out WeaponInstance packedItem))
                {
                    if (!held.DefinitionId.Equals(packedItem.DefinitionId) || held.Provenance != packedItem.Provenance)
                        throw new ArgumentException("A held instance conflicts with its sortie manifest.", nameof(heldAtExtraction));
                }
                else if (held.Provenance != WeaponProvenance.ExplorationLoot)
                {
                    throw new ArgumentException("An instance absent from the manifest must be exploration loot.", nameof(heldAtExtraction));
                }

                heldIds.Add(held.Id);
                if (!held.IsLoaned)
                    recovery.Add(WeaponInstance.Rehydrate(held.Id, held.DefinitionId, held.Provenance, WeaponLocation.Warehouse()));
            }

            WeaponInstance[] abandoned = manifest.Equipped.Where(item => !item.IsLoaned && !heldIds.Contains(item.Id)).ToArray();
            WeaponInstance[] loans = manifest.Equipped.Where(item => item.IsLoaned).ToArray();
            return new SuccessfulSortieResult(run, coins, recovery.ToArray(), abandoned, loans);
        }
    }
}
