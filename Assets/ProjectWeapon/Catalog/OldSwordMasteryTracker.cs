#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Catalog
{
    /// <summary>Read-only mastery evidence evaluated only for a validated successful settlement.</summary>
    public interface IRunMasteryObjective
    {
        IReadOnlyList<WeaponDefinitionId> GetVerifiedMasteries(RunResult result,
            IReadOnlyList<WeaponInstance> recoveredWeapons);
    }

    /// <summary>Tracks KAN-49's first mastery objective for one sortie on the owning gameplay thread.</summary>
    public sealed class OldSwordMasteryTracker : IRunMasteryObjective
    {
        public const int DefaultRequiredFinalBlows = 3;
        private static readonly WeaponDefinitionId OldSword = new WeaponDefinitionId("old_sword");

        private readonly RunState run;
        private readonly RunSession session;
        private readonly int requiredFinalBlows;
        private readonly HashSet<WeaponInstanceId> eligibleWeapons = new HashSet<WeaponInstanceId>();
        private readonly Dictionary<string, (WeaponInstanceId Weapon, TimeSpan EffectiveAt)> finalBlows =
            new Dictionary<string, (WeaponInstanceId, TimeSpan)>(StringComparer.Ordinal);

        /// <summary>Bind when the sortie begins, using its original manifest and committed catalog.</summary>
        public OldSwordMasteryTracker(RunState run, SortieManifest manifest, SaveSnapshot atSortieStart,
            int requiredFinalBlows = DefaultRequiredFinalBlows)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (atSortieStart == null)
                throw new ArgumentNullException(nameof(atSortieStart));
            if (requiredFinalBlows <= 0)
                throw new ArgumentOutOfRangeException(nameof(requiredFinalBlows));

            if (run.Phase != RunPhase.Exploring || run.CurrentRun is not { } current || current.Id.Value != manifest.RunId)
                throw new ArgumentException("The mastery tracker must start with the active sortie manifest.", nameof(manifest));

            session = current;
            this.requiredFinalBlows = requiredFinalBlows;
            var catalog = new CatalogProgress(atSortieStart.Catalog);
            if (catalog.GetStage(OldSword) != CatalogStage.Recovered)
                return;

            foreach (WeaponInstance weapon in manifest.Equipped)
            {
                if (weapon.IsValid && !weapon.IsLoaned && weapon.DefinitionId.Equals(OldSword))
                    eligibleWeapons.Add(weapon.Id);
            }
        }

        /// <summary>Observe one confirmed death, attributed to the weapon captured by the lethal attack.</summary>
        /// <remarks>An enemy's first valid death observation is consumed even when its weapon is ineligible.</remarks>
        public bool ObserveFinalBlow(RunId runId, WeaponInstanceId weapon, string enemyId, TimeSpan effectiveAt)
        {
            if (eligibleWeapons.Count == 0 || runId != session.Id || run.CurrentRun?.Id != session.Id
                || run.Result != null || !weapon.IsValid || string.IsNullOrWhiteSpace(enemyId)
                || !StringComparer.Ordinal.Equals(enemyId, enemyId.Trim())
                || effectiveAt < session.StartedAt || effectiveAt >= session.Deadline)
                return false;

            return finalBlows.TryAdd(enemyId, (weapon, effectiveAt));
        }

        public IReadOnlyList<WeaponDefinitionId> GetVerifiedMasteries(RunResult result,
            IReadOnlyList<WeaponInstance> recoveredWeapons)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (recoveredWeapons == null)
                throw new ArgumentNullException(nameof(recoveredWeapons));
            if (!result.Succeeded || result.RunId != session.Id || !ReferenceEquals(result, run.Result))
                return Array.Empty<WeaponDefinitionId>();

            foreach (WeaponInstance recovered in recoveredWeapons)
            {
                if (recovered.IsLoaned || !recovered.DefinitionId.Equals(OldSword) || !eligibleWeapons.Contains(recovered.Id))
                    continue;

                int count = 0;
                foreach ((WeaponInstanceId weapon, TimeSpan effectiveAt) in finalBlows.Values)
                {
                    if (weapon == recovered.Id && effectiveAt <= result.EffectiveAt && ++count == requiredFinalBlows)
                        return new[] { OldSword };
                }
            }

            return Array.Empty<WeaponDefinitionId>();
        }
    }
}
