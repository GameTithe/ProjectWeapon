#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Catalog
{
    public enum CatalogStage
    {
        Unrecognized = -1,
        Undiscovered = 0,
        Discovered = 1,
        Recovered = 2,
        Mastered = 3
    }

    /// <summary>Tracks persistent, per-definition progress on the owning gameplay thread.</summary>
    public sealed class CatalogProgress
    {
        private readonly Dictionary<WeaponDefinitionId, CatalogSaveRecord> records =
            new Dictionary<WeaponDefinitionId, CatalogSaveRecord>();

        public CatalogProgress(IEnumerable<CatalogSaveRecord> savedRecords)
        {
            if (savedRecords == null)
                throw new ArgumentNullException(nameof(savedRecords));

            foreach (CatalogSaveRecord record in savedRecords)
            {
                if (record == null || !record.DefinitionId.IsValid || record.State == null
                    || !records.TryAdd(record.DefinitionId, record))
                    throw new ArgumentException("Catalog records require distinct definitions and a state.", nameof(savedRecords));
            }
        }

        public int RecoveredCount => records.Values.Count(record => ReadStage(record.State) >= CatalogStage.Recovered);

        public CatalogStage GetStage(WeaponDefinitionId id)
            => records.TryGetValue(id, out CatalogSaveRecord record)
                ? ReadStage(record.State)
                : CatalogStage.Undiscovered;

        /// <summary>Call after the pickup is accepted into a run slot.</summary>
        public bool RecordPickup(WeaponInstance pickedUp)
            => IsHeldPersonalWeapon(pickedUp) && Advance(pickedUp.DefinitionId, CatalogStage.Discovered, "discovered");

        /// <summary>Call with the receipt from a committed draw. A draw opens only discovery.</summary>
        public bool RecordDraw(CoinWeaponDrawSaveRecord receipt)
        {
            if (receipt == null)
                throw new ArgumentNullException(nameof(receipt));

            IReadOnlyList<CoinWeaponDrawAward> awards = receipt.Awards;
            if (receipt.RequestId == Guid.Empty || receipt.Cost <= 0 || receipt.BalanceAfterPurchase < 0
                || awards.Any(award => !award.InstanceId.IsValid || !award.DefinitionId.IsValid
                    || award.Grade < 1 || award.Grade > 5))
                return false;

            bool changed = false;
            foreach (CoinWeaponDrawAward award in awards)
                changed |= Advance(award.DefinitionId, CatalogStage.Discovered, "discovered");

            return changed;
        }

        /// <summary>Call with a weapon held at the instant a run ended successfully.</summary>
        public bool RecordRecovery(RunResult result, WeaponInstance heldWeapon)
            => result != null && result.Succeeded && IsHeldPersonalWeapon(heldWeapon)
                && Advance(heldWeapon.DefinitionId, CatalogStage.Recovered, "recovered");

        /// <summary>Applies the result of a mastery check performed by the owning activity.</summary>
        public bool RecordVerifiedMastery(WeaponDefinitionId id)
            => GetStage(id) == CatalogStage.Recovered && Advance(id, CatalogStage.Mastered, "mastered");

        public IReadOnlyList<CatalogSaveRecord> ToSaveRecords()
            => records.Values.OrderBy(record => record.DefinitionId.Value, StringComparer.Ordinal).ToArray();

        private bool Advance(WeaponDefinitionId id, CatalogStage newStage, string wireState)
        {
            CatalogStage current = GetStage(id);
            if (current == CatalogStage.Unrecognized || current >= newStage)
                return false;

            records[id] = new CatalogSaveRecord(id, wireState);
            return true;
        }

        private static bool IsHeldPersonalWeapon(WeaponInstance? weapon)
            => weapon is { IsValid: true, IsLoaned: false }
                && (weapon.Location.Kind == WeaponLocationKind.Equipped || weapon.Location.Kind == WeaponLocationKind.Carried);

        private static CatalogStage ReadStage(string state) => state switch
        {
            "discovered" => CatalogStage.Discovered,
            "recovered" => CatalogStage.Recovered,
            "mastered" => CatalogStage.Mastered,
            _ => CatalogStage.Unrecognized
        };
    }
}
