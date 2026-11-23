#nullable enable

using System;
using ProjectWeapon.Run;

namespace ProjectWeapon.Wallet
{
    public readonly struct WalletBalance
    {
        public WalletBalance(long confirmedCoins)
        {
            if (confirmedCoins < 0)
                throw new ArgumentOutOfRangeException(nameof(confirmedCoins));

            ConfirmedCoins = confirmedCoins;
        }

        public long ConfirmedCoins { get; }
    }

    public readonly struct RunCoinSettlementProposal
    {
        internal RunCoinSettlementProposal(RunResult result, WalletBalance current, long collectedCoins)
        {
            RunId = result.RunId;
            Reason = result.Reason;
            PreviousBalance = current;
            CollectedCoins = collectedCoins;
            AwardedCoins = result.Succeeded ? collectedCoins : 0;
            ResultingBalance = new WalletBalance(checked(current.ConfirmedCoins + AwardedCoins));
        }

        public RunId RunId { get; }
        public RunEndReason Reason { get; }
        public WalletBalance PreviousBalance { get; }
        public WalletBalance ResultingBalance { get; }
        public long CollectedCoins { get; }
        public long AwardedCoins { get; }
        public long ForfeitedCoins => CollectedCoins - AwardedCoins;
    }

    public sealed class RunCoinAccumulator
    {
        public RunCoinAccumulator(RunId runId)
        {
            if (!runId.IsValid)
                throw new ArgumentException("A coin accumulator requires a valid run ID.", nameof(runId));

            RunId = runId;
        }

        public RunId RunId { get; }
        public long PendingCoins { get; private set; }
        public bool IsClosed { get; private set; }

        public void AddReward(long amount)
        {
            EnsureOpen();
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            PendingCoins = checked(PendingCoins + amount);
        }

        public RunCoinSettlementProposal ProposeSettlement(WalletBalance confirmed, RunResult result)
        {
            EnsureOpen();
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (result.RunId != RunId)
                throw new ArgumentException("The finalized result belongs to a different run.", nameof(result));

            return new RunCoinSettlementProposal(result, confirmed, PendingCoins);
        }

        // The coordinator calls this only after the matching proposal is durably committed.
        public void AcknowledgeCommitted(RunCoinSettlementProposal proposal)
        {
            EnsureOpen();
            if (proposal.RunId != RunId || proposal.CollectedCoins != PendingCoins)
                throw new ArgumentException("The committed proposal does not match the pending coins.", nameof(proposal));

            PendingCoins = 0;
            IsClosed = true;
        }

        private void EnsureOpen()
        {
            if (IsClosed)
                throw new InvalidOperationException("The run coin accumulator is closed.");
        }
    }
}
