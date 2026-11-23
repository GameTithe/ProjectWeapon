#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Balance;

namespace ProjectWeapon.Wallet
{
    public enum RunCoinRewardSource
    {
        RegularMonster,
        EliteMonster,
        Chest,
        Boss
    }

    public readonly struct RunCoinRewardRange
    {
        internal RunCoinRewardRange(BalanceRange range)
        {
            Minimum = (int)range.Minimum;
            Maximum = (int)range.Maximum;
            Approval = range.Approval;
        }

        public int Minimum { get; }
        public int Maximum { get; }
        public BalanceApproval Approval { get; }
    }

    public sealed class RunCoinRewardTable
    {
        private readonly Dictionary<RunCoinRewardSource, RunCoinRewardRange> ranges;

        public RunCoinRewardTable(BalanceSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            IReadOnlyList<BalanceRange> values = settings.GetRanges();
            ranges = new Dictionary<RunCoinRewardSource, RunCoinRewardRange>
            {
                [RunCoinRewardSource.RegularMonster] = ReadRange(values, "run.coins.regular"),
                [RunCoinRewardSource.EliteMonster] = ReadRange(values, "run.coins.elite"),
                [RunCoinRewardSource.Chest] = ReadRange(values, "run.coins.chest"),
                [RunCoinRewardSource.Boss] = ReadRange(values, "run.coins.boss")
            };
        }

        public RunCoinRewardRange GetRange(RunCoinRewardSource source)
        {
            if (!ranges.TryGetValue(source, out RunCoinRewardRange range))
                throw new ArgumentOutOfRangeException(nameof(source));

            return range;
        }

        public int Roll(RunCoinRewardSource source, Func<int, int, int> sampleInclusive)
        {
            if (sampleInclusive == null)
                throw new ArgumentNullException(nameof(sampleInclusive));

            RunCoinRewardRange range = GetRange(source);
            int amount = sampleInclusive(range.Minimum, range.Maximum);
            if (amount < range.Minimum || amount > range.Maximum)
                throw new InvalidOperationException("The reward sampler returned an amount outside the inclusive range.");

            return amount;
        }

        private static RunCoinRewardRange ReadRange(IReadOnlyList<BalanceRange> values, string key)
        {
            BalanceRange? range = values.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Key, key));
            if (range == null)
                throw new InvalidOperationException($"Missing run coin reward range '{key}'.");
            if (range.Kind != BalanceRangeKind.CoinSupply || range.Maximum > int.MaxValue)
                throw new InvalidOperationException($"Run coin reward range '{key}' must use 32-bit whole coin amounts.");

            return new RunCoinRewardRange(range);
        }
    }
}
