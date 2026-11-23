#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ProjectWeapon.Ad.Tests
{
    public sealed class FakeRewardedAdAdapterTests
    {
        private static readonly Guid FirstRequest = new Guid("ef55c560-0e73-4709-b180-c49a7ffc1214");
        private static readonly Guid NextRequest = new Guid("9e13d01a-bf62-4cb0-a44f-ac9035e746b6");

        [TestCase(RewardedAdOutcome.LoadFailed)]
        [TestCase(RewardedAdOutcome.Cancelled)]
        [TestCase(RewardedAdOutcome.ClosedWithoutConfirmation)]
        [TestCase(RewardedAdOutcome.Error)]
        public void FailureReportsStatusWithoutVerifiedCompletion(RewardedAdOutcome outcome)
        {
            var fake = new FakeRewardedAdAdapter();
            IRewardedAdAdapter adapter = fake;
            var reports = new List<RewardedAdResult>();
            adapter.OutcomeReported += reports.Add;
            Assert.That(adapter.TryBegin(FirstRequest), Is.True);

            Assert.That(fake.TryInjectCallback(FirstRequest, outcome), Is.True);
            Assert.That(fake.TryInjectCallback(FirstRequest, RewardedAdOutcome.ConfirmedCompletion, "late-confirmation"),
                Is.False);
            Assert.That(fake.TryInjectCallback(FirstRequest, outcome), Is.False);
            Assert.That(adapter.TryBegin(FirstRequest), Is.False);

            Assert.That(reports.Count, Is.EqualTo(1));
            Assert.That(reports[0].RequestId, Is.EqualTo(FirstRequest));
            Assert.That(reports[0].Outcome, Is.EqualTo(outcome));
            Assert.That(reports[0].VerifiedCompletion.HasValue, Is.False);
        }

        [Test]
        public void RequiredAndMismatchedRequestIdsDoNotConsumeThePendingRequest()
        {
            var fake = new FakeRewardedAdAdapter();
            var reports = new List<RewardedAdResult>();
            fake.OutcomeReported += reports.Add;

            Assert.That(fake.TryBegin(Guid.Empty), Is.False);
            Assert.That(fake.TryBegin(FirstRequest), Is.True);
            Assert.That(fake.TryBegin(FirstRequest), Is.False);
            Assert.That(fake.TryInjectCallback(Guid.Empty, RewardedAdOutcome.ConfirmedCompletion, "proof"), Is.False);
            Assert.That(fake.TryInjectCallback(NextRequest, RewardedAdOutcome.ConfirmedCompletion, "proof"), Is.False);
            Assert.That(fake.TryInjectCallback(NextRequest, RewardedAdOutcome.Cancelled), Is.False);
            Assert.That(reports, Is.Empty);

            Assert.That(fake.TryInjectCallback(FirstRequest, RewardedAdOutcome.ConfirmedCompletion, "proof"), Is.True);
            Assert.That(reports.Count, Is.EqualTo(1));
            Assert.That(reports[0].VerifiedCompletion?.RequestId, Is.EqualTo(FirstRequest));
            Assert.That(reports[0].VerifiedCompletion?.CompletionId, Is.EqualTo("proof"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void UnverifiedCompletionDoesNotEmitAndCanBeFollowedByAnExplicitClosure(string? completionId)
        {
            var fake = new FakeRewardedAdAdapter();
            var reports = new List<RewardedAdResult>();
            fake.OutcomeReported += reports.Add;
            fake.TryBegin(FirstRequest);

            Assert.That(fake.TryInjectCallback(FirstRequest, RewardedAdOutcome.ConfirmedCompletion, completionId),
                Is.False);
            Assert.That(fake.TryInjectCallback(FirstRequest, RewardedAdOutcome.Error, "conflicting-proof"), Is.False);
            Assert.That(fake.TryInjectCallback(FirstRequest, RewardedAdOutcome.None), Is.False);
            Assert.That(fake.TryInjectCallback(FirstRequest, (RewardedAdOutcome)999), Is.False);
            Assert.That(reports, Is.Empty);

            Assert.That(fake.TryInjectCallback(FirstRequest, RewardedAdOutcome.ClosedWithoutConfirmation), Is.True);
            Assert.That(reports.Count, Is.EqualTo(1));
            Assert.That(reports[0].VerifiedCompletion.HasValue, Is.False);
        }

        [Test]
        public void DuplicateRequestAndCompletionIdsCannotEmitAnotherEligibility()
        {
            var fake = new FakeRewardedAdAdapter();
            var reports = new List<RewardedAdResult>();
            fake.OutcomeReported += reports.Add;
            Assert.That(fake.TryBegin(FirstRequest), Is.True);
            Assert.That(fake.TryBegin(NextRequest), Is.True);

            Assert.That(fake.TryInjectCallback(FirstRequest, RewardedAdOutcome.ConfirmedCompletion, "first-proof"),
                Is.True);
            Assert.That(fake.TryInjectCallback(FirstRequest, RewardedAdOutcome.ConfirmedCompletion, "new-proof"),
                Is.False);
            Assert.That(fake.TryInjectCallback(FirstRequest, RewardedAdOutcome.Cancelled), Is.False);
            Assert.That(fake.TryInjectCallback(NextRequest, RewardedAdOutcome.ConfirmedCompletion, "first-proof"),
                Is.False);
            Assert.That(reports.Count, Is.EqualTo(1));

            Assert.That(fake.TryInjectCallback(NextRequest, RewardedAdOutcome.ConfirmedCompletion, "new-proof"), Is.True);
            Assert.That(reports.Count, Is.EqualTo(2));
            Assert.That(reports[1].Outcome, Is.EqualTo(RewardedAdOutcome.ConfirmedCompletion));
            Assert.That(reports[1].VerifiedCompletion?.RequestId, Is.EqualTo(NextRequest));
            Assert.That(reports[1].VerifiedCompletion?.CompletionId, Is.EqualTo("new-proof"));
        }

        [Test]
        public void ConcurrentDuplicateCallbacksEmitOneVerifiedCompletion()
        {
            var fake = new FakeRewardedAdAdapter();
            int verifiedReports = 0;
            fake.OutcomeReported += result =>
            {
                if (result.VerifiedCompletion.HasValue)
                    Interlocked.Increment(ref verifiedReports);
            };
            Assert.That(fake.TryBegin(FirstRequest), Is.True);
            using var start = new Barrier(3);
            var timeout = TimeSpan.FromSeconds(10);

            bool Report()
            {
                if (!start.SignalAndWait(timeout))
                    throw new TimeoutException("The duplicate callback tests did not start together.");

                return fake.TryInjectCallback(FirstRequest, RewardedAdOutcome.ConfirmedCompletion, "proof");
            }

            Task<bool> first = Task.Run(Report);
            Task<bool> second = Task.Run(Report);
            Assert.That(start.SignalAndWait(timeout), Is.True);
            Assert.That(Task.WaitAll(new Task[] { first, second }, timeout), Is.True);
            Assert.That(new[] { first.Result, second.Result }, Is.EquivalentTo(new[] { true, false }));
            Assert.That(verifiedReports, Is.EqualTo(1));
        }
    }
}
