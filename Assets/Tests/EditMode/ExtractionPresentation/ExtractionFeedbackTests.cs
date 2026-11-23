using System;
using NUnit.Framework;

namespace ProjectWeapon.ExtractionPresentation.Tests
{
    public sealed class ExtractionFeedbackTests
    {
        private static readonly Guid PrimaryRun = new Guid("35379e9c-8036-48d1-b403-b2f216d81dca");
        private static readonly Guid NextRun = new Guid("8104712b-004b-4604-9719-3336f2f74597");

        [Test]
        public void AvailabilityComesFromTheActiveRun()
        {
            var feedback = new ExtractionFeedback();
            feedback.OnRunStarted(PrimaryRun);
            feedback.SetAvailability(NextRun, true);

            Assert.That(feedback.Sample(TimeSpan.Zero).State, Is.EqualTo(ExtractionFeedbackState.Idle));

            feedback.SetAvailability(PrimaryRun, true);
            Assert.That(feedback.Sample(TimeSpan.Zero).State, Is.EqualTo(ExtractionFeedbackState.Available));

            feedback.SetAvailability(PrimaryRun, false);
            Assert.That(feedback.Sample(TimeSpan.Zero).State, Is.EqualTo(ExtractionFeedbackState.Idle));
        }

        [TestCase(9.0, 0f)]
        [TestCase(10.0, 0f)]
        [TestCase(11.5, 0.5f)]
        [TestCase(13.0, 1f)]
        [TestCase(14.0, 1f)]
        public void ProgressUsesTheEventWindowWithoutSynthesizingSuccess(double now, float expected)
        {
            var feedback = new ExtractionFeedback();
            feedback.OnRunStarted(PrimaryRun);
            feedback.OnExtractionStarted(PrimaryRun, 1, Seconds(10), Seconds(13));

            ExtractionFeedbackSnapshot snapshot = feedback.Sample(Seconds(now));
            Assert.That(snapshot.State, Is.EqualTo(ExtractionFeedbackState.Channeling));
            Assert.That(snapshot.Progress, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void CancellationClearsProgressAndReplayCannotRestartPreviousAttempt()
        {
            var feedback = new ExtractionFeedback();
            feedback.OnRunStarted(PrimaryRun);
            feedback.SetAvailability(PrimaryRun, true);
            feedback.OnExtractionStarted(PrimaryRun, 1, Seconds(10), Seconds(13));
            feedback.OnExtractionCancelled(PrimaryRun, 1);
            feedback.OnExtractionStarted(PrimaryRun, 1, Seconds(10), Seconds(13));

            ExtractionFeedbackSnapshot cancelled = feedback.Sample(Seconds(11));
            Assert.That(cancelled.State, Is.EqualTo(ExtractionFeedbackState.Available));
            Assert.That(cancelled.Progress, Is.Zero);

            feedback.OnExtractionStarted(PrimaryRun, 2, Seconds(12), Seconds(15));
            feedback.OnExtractionCancelled(PrimaryRun, 1);

            ExtractionFeedbackSnapshot next = feedback.Sample(Seconds(13.5));
            Assert.That(next.State, Is.EqualTo(ExtractionFeedbackState.Channeling));
            Assert.That(next.Progress, Is.EqualTo(0.5f));
        }

        [Test]
        public void FirstResultIsKeptAndNewRunClearsTheCompletionCue()
        {
            var feedback = new ExtractionFeedback();
            feedback.OnRunStarted(PrimaryRun);
            feedback.OnExtractionStarted(PrimaryRun, 1, Seconds(10), Seconds(13));

            Assert.That(feedback.OnRunResultFinalized(PrimaryRun, true), Is.True);
            Assert.That(feedback.OnRunResultFinalized(PrimaryRun, true), Is.False);
            Assert.That(feedback.OnRunResultFinalized(PrimaryRun, false), Is.False);
            feedback.OnRunStarted(PrimaryRun);
            Assert.That(feedback.Sample(Seconds(13)).State, Is.EqualTo(ExtractionFeedbackState.Succeeded));

            feedback.OnRunStarted(NextRun);
            Assert.That(feedback.OnRunResultFinalized(PrimaryRun, true), Is.False);
            Assert.That(feedback.Sample(Seconds(14)).State, Is.EqualTo(ExtractionFeedbackState.Idle));

            Assert.That(feedback.OnRunResultFinalized(NextRun, false), Is.True);
            feedback.SetAvailability(NextRun, true);
            Assert.That(feedback.Sample(Seconds(14)).State, Is.EqualTo(ExtractionFeedbackState.Idle));
        }

        private static TimeSpan Seconds(double time) => TimeSpan.FromSeconds(time);
    }
}
