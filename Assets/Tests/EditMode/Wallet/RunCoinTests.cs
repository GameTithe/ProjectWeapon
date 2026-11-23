#nullable enable

using System;
using NUnit.Framework;
using ProjectWeapon.Balance;
using ProjectWeapon.Run;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Wallet.Tests
{
    public sealed class RunCoinAccumulatorTests
    {
        [Test]
        public void RewardsStayPendingForTheirOwnRun()
        {
            var wallet = new WalletBalance(110);
            var run = new RunCoinAccumulator(RunId.New());
            var otherRun = new RunCoinAccumulator(RunId.New());
            run.AddReward(2);
            run.AddReward(15);

            Assert.That(wallet.ConfirmedCoins, Is.EqualTo(110));
            Assert.That(run.PendingCoins, Is.EqualTo(17));
            Assert.That(otherRun.PendingCoins, Is.Zero);
        }

        [Test]
        public void SuccessfulProposalAndRetryDoNotChangeTheWalletBeforeCommit()
        {
            var activeWallet = new WalletBalance(110);
            var coins = new RunCoinAccumulator(RunId.New());
            coins.AddReward(23);
            RunResult result = FinalizeRun(coins.RunId, RunEndReason.Extracted);

            RunCoinSettlementProposal first = coins.ProposeSettlement(activeWallet, result);
            RunCoinSettlementProposal retry = coins.ProposeSettlement(activeWallet, result);

            Assert.That(first.RunId, Is.EqualTo(coins.RunId));
            Assert.That(first.PreviousBalance.ConfirmedCoins, Is.EqualTo(110));
            Assert.That(first.ResultingBalance.ConfirmedCoins, Is.EqualTo(133));
            Assert.That(first.CollectedCoins, Is.EqualTo(23));
            Assert.That(first.AwardedCoins, Is.EqualTo(23));
            Assert.That(first.ForfeitedCoins, Is.Zero);
            Assert.That(retry.ResultingBalance.ConfirmedCoins, Is.EqualTo(133));
            Assert.That(activeWallet.ConfirmedCoins, Is.EqualTo(110));
            Assert.That(coins.PendingCoins, Is.EqualTo(23));
            Assert.That(coins.IsClosed, Is.False);

            coins.AcknowledgeCommitted(first);
            Assert.That(coins.PendingCoins, Is.Zero);
            Assert.That(coins.IsClosed, Is.True);
            Assert.That(() => coins.AddReward(1), Throws.InvalidOperationException);
            Assert.That(() => coins.ProposeSettlement(activeWallet, result), Throws.InvalidOperationException);
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.TimeExpired)]
        public void FailureForfeitsOnlyTheRunCoins(RunEndReason reason)
        {
            var wallet = new WalletBalance(15);
            var run = new RunCoinAccumulator(RunId.New());
            run.AddReward(80);

            RunCoinSettlementProposal candidate = run.ProposeSettlement(wallet, FinalizeRun(run.RunId, reason));

            Assert.That(candidate.Reason, Is.EqualTo(reason));
            Assert.That(candidate.ResultingBalance.ConfirmedCoins, Is.EqualTo(15));
            Assert.That(candidate.AwardedCoins, Is.Zero);
            Assert.That(candidate.ForfeitedCoins, Is.EqualTo(80));
            Assert.That(run.PendingCoins, Is.EqualTo(80));

            run.AcknowledgeCommitted(candidate);

            Assert.That(run.PendingCoins, Is.Zero);
            Assert.That(wallet.ConfirmedCoins, Is.EqualTo(15));
        }

        [Test]
        public void RejectsInvalidBalancesAndCrossRunResults()
        {
            var coins = new RunCoinAccumulator(RunId.New());
            var wallet = new WalletBalance(7);
            coins.AddReward(2);

            Assert.That(() => new RunCoinAccumulator(default), Throws.TypeOf<ArgumentException>());
            Assert.That(() => new WalletBalance(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => coins.AddReward(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => coins.ProposeSettlement(wallet, FinalizeRun(RunId.New(), RunEndReason.Death)),
                Throws.TypeOf<ArgumentException>());
            Assert.That(coins.PendingCoins, Is.EqualTo(2));
            Assert.That(wallet.ConfirmedCoins, Is.EqualTo(7));
        }

        [Test]
        public void OverflowLeavesPendingAndConfirmedValuesUnchanged()
        {
            var coins = new RunCoinAccumulator(RunId.New());
            var wallet = new WalletBalance(1);
            coins.AddReward(long.MaxValue);
            RunResult success = FinalizeRun(coins.RunId, RunEndReason.Extracted);

            Assert.That(() => coins.AddReward(1), Throws.TypeOf<OverflowException>());
            Assert.That(() => coins.ProposeSettlement(wallet, success), Throws.TypeOf<OverflowException>());
            Assert.That(coins.PendingCoins, Is.EqualTo(long.MaxValue));
            Assert.That(wallet.ConfirmedCoins, Is.EqualTo(1));
        }

        [Test]
        public void AcknowledgementCannotClearAnotherRunOrDifferentPendingAmount()
        {
            var run = new RunCoinAccumulator(RunId.New());
            var otherRun = new RunCoinAccumulator(RunId.New());
            var wallet = new WalletBalance(3);
            run.AddReward(4);
            RunCoinSettlementProposal proposal =
                run.ProposeSettlement(wallet, FinalizeRun(run.RunId, RunEndReason.Death));

            Assert.That(() => otherRun.AcknowledgeCommitted(proposal), Throws.TypeOf<ArgumentException>());
            run.AddReward(1);
            Assert.That(() => run.AcknowledgeCommitted(proposal), Throws.TypeOf<ArgumentException>());
            Assert.That(run.IsClosed, Is.False);
            Assert.That(run.PendingCoins, Is.EqualTo(5));
            Assert.That(otherRun.IsClosed, Is.False);
        }

        private static RunResult FinalizeRun(RunId id, RunEndReason reason)
        {
            TimeSpan clock = TimeSpan.Zero;
            var state = new RunState(() => clock);
            TimeSpan limit = TimeSpan.FromSeconds(10);
            Assert.That(state.TryStartRun(id, 1, limit), Is.True);

            if (reason == RunEndReason.Death)
            {
                clock = TimeSpan.FromSeconds(1);
                return state.ProcessSignals(new[] { RunSignal.PlayerDied(id, clock) })!;
            }

            if (reason == RunEndReason.TimeExpired)
            {
                clock = limit;
                return state.ProcessSignals(Array.Empty<RunSignal>())!;
            }

            ExtractionAttempt attempt = state.TryBeginExtraction(id, true)!;
            clock = attempt.CompletesAt;
            return state.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, clock) })!;
        }
    }

    public sealed class RunCoinRewardTableTests
    {
        private const string AssetPath = "Assets/ProjectWeapon/Wallet/PrototypeRunCoinRewards.asset";
        private BalanceSettings? editedSettings;

        [TearDown]
        public void DestroyEditedSettings()
        {
            if (editedSettings != null)
                Object.DestroyImmediate(editedSettings);
        }

        [TestCase(RunCoinRewardSource.RegularMonster, 2, 5)]
        [TestCase(RunCoinRewardSource.EliteMonster, 15, 30)]
        [TestCase(RunCoinRewardSource.Chest, 20, 50)]
        [TestCase(RunCoinRewardSource.Boss, 100, 200)]
        public void DraftSettingsProvideInclusiveBounds(RunCoinRewardSource source, int min, int max)
        {
            var rewards = new RunCoinRewardTable(LoadConfig());
            RunCoinRewardRange range = rewards.GetRange(source);

            Assert.That(range.Approval, Is.EqualTo(BalanceApproval.Draft));
            Assert.That(range.Minimum, Is.EqualTo(min));
            Assert.That(range.Maximum, Is.EqualTo(max));
            Assert.That(rewards.Roll(source, (minimum, maximum) => minimum), Is.EqualTo(min));
            Assert.That(rewards.Roll(source, (minimum, maximum) => maximum), Is.EqualTo(max));
            Assert.That(() => rewards.Roll(source, (minimum, maximum) => minimum - 1),
                Throws.InvalidOperationException);
            Assert.That(() => rewards.Roll(source, (minimum, maximum) => maximum + 1),
                Throws.InvalidOperationException);
        }

        [Test]
        public void RejectsUnknownSource()
        {
            var rewards = new RunCoinRewardTable(LoadConfig());
            Assert.That(() => rewards.GetRange((RunCoinRewardSource)99), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void RequiresAllFourRecognizedKeys()
        {
            editedSettings = ScriptableObject.CreateInstance<BalanceSettings>();

            Assert.That(() => new RunCoinRewardTable(editedSettings),
                Throws.InvalidOperationException.With.Message.Contains("Missing run coin reward range"));
        }

        [TestCase("kind", (double)BalanceRangeKind.DamagePerSecond, "32-bit whole coin amounts")]
        [TestCase("maximum", 1.0, "finite and ordered")]
        [TestCase("maximum", 3.5, "nonnegative whole numbers")]
        [TestCase("maximum", (double)int.MaxValue + 1.0, "32-bit whole coin amounts")]
        public void RejectsWrongTypeOrInvalidRange(string field, double value, string diagnostic)
        {
            editedSettings = Object.Instantiate(LoadConfig());
            using var serialized = new SerializedObject(editedSettings);
            SerializedProperty property = serialized.FindProperty("ranges").GetArrayElementAtIndex(0)
                .FindPropertyRelative(field);
            if (field == "kind")
                property.intValue = (int)value;
            else
                property.doubleValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(() => new RunCoinRewardTable(editedSettings),
                Throws.InvalidOperationException.With.Message.Contains(diagnostic));
        }

        private static BalanceSettings LoadConfig()
        {
            var settings = AssetDatabase.LoadAssetAtPath<BalanceSettings>(AssetPath);
            Assert.That(settings, Is.Not.Null);
            return settings;
        }
    }
}
