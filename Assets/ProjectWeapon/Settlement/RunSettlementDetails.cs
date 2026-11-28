#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.Wallet;
using UnityEngine;

namespace ProjectWeapon.Settlement
{
    [Serializable]
    public sealed class RunSettlementDetails
    {
        [SerializeField] private int version;
        [SerializeField] private string outcome;
        [SerializeField] private long awardedCoins;
        [SerializeField] private long forfeitedCoins;
        [SerializeField] private string[] recovered;
        [SerializeField] private string[] lost;
        [SerializeField] private string[] closedLoans;

        internal RunSettlementDetails(RunCoinSettlementProposal coins, IEnumerable<WeaponInstance> recovered,
            IEnumerable<WeaponInstance> lost, IEnumerable<WeaponInstance> loans)
        {
            version = 1;
            outcome = coins.Reason.ToString();
            awardedCoins = coins.AwardedCoins;
            forfeitedCoins = coins.ForfeitedCoins;
            this.recovered = ReadIds(recovered);
            this.lost = ReadIds(lost);
            closedLoans = ReadIds(loans);
        }

        public RunEndReason Reason => Enum.Parse<RunEndReason>(outcome);
        public long AwardedCoins => awardedCoins;
        public long ForfeitedCoins => forfeitedCoins;
        public IReadOnlyList<string> RecoveredInstanceIds => Array.AsReadOnly(recovered);
        public IReadOnlyList<string> LostInstanceIds => Array.AsReadOnly(lost);
        public IReadOnlyList<string> ClosedLoanIds => Array.AsReadOnly(closedLoans);

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

            if (details == null || details.version != 1 || !ParseReason(details.outcome, out RunEndReason reason) ||
                details.awardedCoins < 0 || details.forfeitedCoins < 0 ||
                details.recovered == null || details.lost == null || details.closedLoans == null)
                return null;

            if (reason == RunEndReason.Extracted && details.forfeitedCoins != 0)
                return null;
            if (reason != RunEndReason.Extracted && (details.awardedCoins != 0 || details.recovered.Length != 0))
                return null;

            return details;
        }

        internal static bool ParseReason(string value, out RunEndReason reason)
            => Enum.TryParse(value, out reason) && Enum.IsDefined(typeof(RunEndReason), reason)
                && StringComparer.Ordinal.Equals(value, reason.ToString());

        private static string[] ReadIds(IEnumerable<WeaponInstance> items)
            => items.Select(item => item.Id.ToString()).ToArray();
    }
}
