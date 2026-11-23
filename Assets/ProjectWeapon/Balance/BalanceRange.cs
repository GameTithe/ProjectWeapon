using System;
using UnityEngine;

namespace ProjectWeapon.Balance
{
    [Serializable]
    public sealed class BalanceRange
    {
        [SerializeField] private string key = string.Empty;
        [SerializeField] private BalanceApproval approval = BalanceApproval.Draft;
        [SerializeField] private BalanceRangeKind kind = BalanceRangeKind.TimeLimitSeconds;
        [SerializeField] private double minimum = 0;
        [SerializeField] private double maximum = 0;

        public string Key => key;
        public BalanceApproval Approval => approval;
        public BalanceRangeKind Kind => kind;
        public double Minimum => minimum;
        public double Maximum => maximum;

        internal void Validate()
        {
            if (double.IsNaN(minimum) || double.IsInfinity(minimum)
                || double.IsNaN(maximum) || double.IsInfinity(maximum) || minimum > maximum)
                throw Invalid("range endpoints must be finite and ordered");

            switch (kind)
            {
                case BalanceRangeKind.TimeLimitSeconds:
                    if (minimum <= 0 || !IsWholeNumber(minimum) || !IsWholeNumber(maximum))
                        throw Invalid("time limits must be positive whole seconds");
                    break;

                case BalanceRangeKind.DamagePerSecond:
                    if (minimum < 0)
                        throw Invalid("DPS cannot be negative");
                    break;

                case BalanceRangeKind.CoinSupply:
                case BalanceRangeKind.BagCapacity:
                    if (minimum < 0 || !IsWholeNumber(minimum) || !IsWholeNumber(maximum))
                        throw Invalid("coin supply and bag capacity must be nonnegative whole numbers");
                    break;

                default:
                    throw Invalid("range kind is unsupported");
            }
        }

        private static bool IsWholeNumber(double number) => Math.Truncate(number) == number;

        private InvalidOperationException Invalid(string issue) =>
            new InvalidOperationException($"Balance range '{key}': {issue}.");
    }
}
