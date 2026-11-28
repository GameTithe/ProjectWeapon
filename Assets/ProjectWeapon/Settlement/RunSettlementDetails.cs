#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Wallet;
using ProjectWeapon.Weapons;
using UnityEngine;

namespace ProjectWeapon.Settlement
{
    [Serializable]
    public sealed class SettledWeapon
    {
        [SerializeField] private string instanceId;
        [SerializeField] private string definitionId;

        internal SettledWeapon(WeaponInstance weapon)
        {
            instanceId = weapon.Id.ToString();
            definitionId = weapon.DefinitionId.Value;
        }

        public string InstanceId => instanceId;
        public WeaponDefinitionId DefinitionId => new WeaponDefinitionId(definitionId);
    }

    [Serializable]
    public sealed class RunSettlementDetails
    {
        [SerializeField] private int version;
        [SerializeField] private string outcome;
        [SerializeField] private long awardedCoins;
        [SerializeField] private long forfeitedCoins;
        // Version 1 receipts can still be read without inventing historical detail.
        [SerializeField] private string[]? recovered;
        [SerializeField] private string[]? lost;
        [SerializeField] private string[]? closedLoans;

        [SerializeField] private SettledWeapon[]? returnedWeapons;
        [SerializeField] private SettledWeapon[]? newWeapons;
        [SerializeField] private SettledWeapon[]? lostWeapons;
        [SerializeField] private SettledWeapon[]? loans;
        [SerializeField] private string[]? newCatalogRecoveries;
        [SerializeField] private long priorConfirmedCoins;
        [SerializeField] private int preservedWarehouseCount;
        [SerializeField] private int unlockedZoneId;

        internal RunSettlementDetails(RunCoinSettlementProposal coins, SortieManifest manifest,
            IEnumerable<WeaponInstance> recoveredItems, IEnumerable<WeaponInstance> lostItems,
            IEnumerable<WeaponInstance> expiredLoans, IReadOnlyList<string> catalogRecoveries, int warehouseCount,
            int? unlockedZoneId = null)
        {
            version = 2;
            outcome = coins.Reason.ToString();
            awardedCoins = coins.AwardedCoins;
            forfeitedCoins = coins.ForfeitedCoins;
            var packed = new HashSet<WeaponInstanceId>(manifest.Equipped.Select(item => item.Id));
            var returned = new List<SettledWeapon>();
            var acquired = new List<SettledWeapon>();
            foreach (WeaponInstance item in recoveredItems)
            {
                var line = new SettledWeapon(item);
                if (packed.Contains(item.Id))
                    returned.Add(line);
                else
                    acquired.Add(line);
            }
            returnedWeapons = returned.ToArray();
            newWeapons = acquired.ToArray();
            lostWeapons = CreateLines(lostItems);
            loans = CreateLines(expiredLoans);
            newCatalogRecoveries = catalogRecoveries.ToArray();
            priorConfirmedCoins = coins.PreviousBalance.ConfirmedCoins;
            preservedWarehouseCount = warehouseCount;
            this.unlockedZoneId = unlockedZoneId ?? 0;
        }

        public RunEndReason Reason => Enum.Parse<RunEndReason>(outcome);
        public long AwardedCoins => awardedCoins;
        public long ForfeitedCoins => forfeitedCoins;
        public bool HasItemizedDetails => version >= 2;
        public long? PriorConfirmedCoins => HasItemizedDetails ? priorConfirmedCoins : null;
        public int? PreservedWarehouseCount => HasItemizedDetails ? preservedWarehouseCount : null;
        public int? UnlockedZoneId => unlockedZoneId == 0 ? null : unlockedZoneId;
        public IReadOnlyList<SettledWeapon> ReturnedWeapons => Lines(returnedWeapons);
        public IReadOnlyList<SettledWeapon> NewWeapons => Lines(newWeapons);
        public IReadOnlyList<SettledWeapon> LostWeapons => Lines(lostWeapons);
        public IReadOnlyList<SettledWeapon> ClosedLoans => Lines(loans);
        public IReadOnlyList<string> NewCatalogRecoveries => Ids(newCatalogRecoveries);

        public IReadOnlyList<string> RecoveredInstanceIds => HasItemizedDetails
            ? ReturnedWeapons.Concat(NewWeapons).Select(item => item.InstanceId).ToArray()
            : Ids(recovered);
        public IReadOnlyList<string> LostInstanceIds => HasItemizedDetails ? ReadIds(LostWeapons) : Ids(lost);
        public IReadOnlyList<string> ClosedLoanIds => HasItemizedDetails ? ReadIds(ClosedLoans) : Ids(closedLoans);

        internal string Encode() => JsonUtility.ToJson(this);

        public static RunSettlementDetails? Read(SettlementSaveRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (!record.Result.StartsWith("{", StringComparison.Ordinal))
                return null;

            RunSettlementDetails? details;
            try
            {
                details = JsonUtility.FromJson<RunSettlementDetails>(record.Result);
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (details == null || !ParseReason(details.outcome, out RunEndReason reason)
                || details.awardedCoins < 0 || details.forfeitedCoins < 0)
                return null;

            bool fieldsValid = details.version switch
            {
                1 => details.recovered != null && details.lost != null && details.closedLoans != null,
                2 => details.returnedWeapons != null && details.newWeapons != null && details.lostWeapons != null &&
                    details.loans != null && details.newCatalogRecoveries != null && details.priorConfirmedCoins >= 0 &&
                    details.preservedWarehouseCount >= 0,
                _ => false
            };
            if (!fieldsValid || details.unlockedZoneId < 0
                || (details.unlockedZoneId != 0 && (!details.HasItemizedDetails || reason != RunEndReason.Extracted)))
                return null;

            if (reason == RunEndReason.Extracted && details.forfeitedCoins != 0)
                return null;
            if (reason != RunEndReason.Extracted && (details.awardedCoins != 0 || details.RecoveredInstanceIds.Count != 0))
                return null;

            return details;
        }

        internal static bool ParseReason(string value, out RunEndReason reason)
            => Enum.TryParse(value, out reason) && Enum.IsDefined(typeof(RunEndReason), reason)
                && StringComparer.Ordinal.Equals(value, reason.ToString());

        private static SettledWeapon[] CreateLines(IEnumerable<WeaponInstance> items)
            => items.Select(item => new SettledWeapon(item)).ToArray();

        private static IReadOnlyList<SettledWeapon> Lines(SettledWeapon[]? rows)
            => rows != null ? Array.AsReadOnly(rows) : Array.Empty<SettledWeapon>();

        private static IReadOnlyList<string> Ids(string[]? rows)
            => rows != null ? Array.AsReadOnly(rows) : Array.Empty<string>();

        private static string[] ReadIds(IEnumerable<SettledWeapon> items)
            => items.Select(item => item.InstanceId).ToArray();
    }
}
