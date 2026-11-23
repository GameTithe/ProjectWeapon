#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;

namespace ProjectWeapon.Extraction.Tests
{
    public sealed class ExtractionChannelTests
    {
        private static readonly RunId FirstRunId = new RunId(new Guid("51b161e1-54db-4343-be78-a9747913fbe2"));
        private static readonly RunId SecondRunId = new RunId(new Guid("f00d7187-1016-4709-b346-edd9a458b221"));
        private static readonly RunSignal[] EmptyFrame = Array.Empty<RunSignal>();
        private RunState run = null!;
        private ExtractionAccess access = null!;
        private ExtractionChannel channel = null!;
        private TimeSpan now;
        private int clockReads;

        [SetUp]
        public void SetUp()
        {
            now = TimeSpan.Zero;
            clockReads = 0;
            run = new RunState(() =>
            {
                clockReads++;
                return now;
            });
            access = new ExtractionAccess(run, 1, new WorldPoint2D(4, -2), 1);
            channel = new ExtractionChannel(run);
        }

        [Test]
        public void ProgressIsClampedAndCompletionAtThreeSecondsFinalizesOnce()
        {
            ExtractionAttempt attempt = StartAttempt(FirstRunId);
            var updates = new List<ExtractionChannelProgress?>();
            channel.ProgressChanged += updates.Add;
            var finalized = new List<RunResult>();
            run.RunResultFinalized += finalized.Add;
            var frame = new List<RunSignal>();

            Assert.That(channel.IsAutomaticAttackSuppressed, Is.True);
            Assert.That(channel.ProcessFrame(frame), Is.Null);
            Assert.That(updates[0]!.Value.Fraction, Is.Zero);

            now = Ms(1500);
            Assert.That(channel.ProcessFrame(frame), Is.Null);
            Assert.That(channel.Progress!.Value.Fraction, Is.EqualTo(0.5f));
            Assert.That(channel.Progress.Value.RunId, Is.EqualTo(FirstRunId));
            Assert.That(channel.Progress.Value.AttemptId, Is.EqualTo(attempt.Id));
            Assert.That(channel.ProcessFrame(frame), Is.Null);
            Assert.That(updates.Count, Is.EqualTo(2));

            now = Ms(2999);
            Assert.That(channel.ProcessFrame(frame), Is.Null);
            Assert.That(channel.Progress.Value.Fraction, Is.LessThan(1));
            Assert.That(run.Result, Is.Null);
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.True);

            now = Ms(3000);
            RunResult? result = channel.ProcessFrame(frame);
            Assert.That(result!.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(result.EffectiveAt, Is.EqualTo(attempt.CompletesAt));
            Assert.That(channel.Progress!.Value.Fraction, Is.EqualTo(1));
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.False);
            Assert.That(frame, Is.Empty);

            now = Ms(3001);
            Assert.That(channel.ProcessFrame(EmptyFrame), Is.SameAs(result));
            Assert.That(finalized.Count, Is.EqualTo(1));
            Assert.That(updates.Count, Is.EqualTo(4));
        }

        [Test]
        public void SkippedFrameUsesScheduledTimeAndSamplesTheAuthoritativeClockOnce()
        {
            Assert.That(run.TryStartRun(FirstRunId, 1, Ms(10000)), Is.True);
            now = Ms(6999);
            ExtractionAttempt attempt = access.TryStart(FirstRunId, new WorldPoint2D(4, -2))!;
            Assert.That(channel.ProcessFrame(EmptyFrame), Is.Null);
            now = Ms(10001);
            int before = clockReads;

            RunResult? result = channel.ProcessFrame(EmptyFrame);

            Assert.That(clockReads - before, Is.EqualTo(1));
            Assert.That(result!.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(result.EffectiveAt, Is.EqualTo(attempt.CompletesAt));
            Assert.That(result.EffectiveAt, Is.EqualTo(Ms(9999)));
        }

        [Test]
        public void DeadlineAtExactCompletionWinsThroughTheExistingRunRule()
        {
            StartAttempt(FirstRunId, limitMs: 3000);
            now = Ms(3000);

            RunResult? result = channel.ProcessFrame(EmptyFrame);

            Assert.That(result!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(channel.Progress, Is.Null);
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.False);
        }

        [TestCase(2999, RunEndReason.Death)]
        [TestCase(3000, RunEndReason.Death)]
        [TestCase(3001, RunEndReason.Extracted)]
        public void SameFrameDeathAndCompletionUseRunStateOrdering(int diedAtMs, RunEndReason expected)
        {
            StartAttempt(FirstRunId);
            now = Ms(3001);
            var signals = new List<RunSignal> { RunSignal.PlayerDied(FirstRunId, Ms(diedAtMs)) };

            RunResult? result = channel.ProcessFrame(signals);

            Assert.That(result!.Reason, Is.EqualTo(expected));
            Assert.That(signals.Count, Is.EqualTo(1));
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.False);
        }

        [Test]
        public void ExistingCancelSignalClearsProgressAndGateAndNewAttemptStartsFresh()
        {
            ExtractionAttempt attempt = StartAttempt(FirstRunId);
            channel.ProcessFrame(EmptyFrame);
            var changes = new List<ExtractionChannelProgress?>();
            channel.ProgressChanged += changes.Add;
            now = Ms(3000);

            Assert.That(channel.ProcessFrame(new[] { RunSignal.CancelExtraction(FirstRunId, attempt.Id, now) }), Is.Null);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.False);
            Assert.That(channel.Progress, Is.Null);
            Assert.That(changes, Is.EqualTo(new ExtractionChannelProgress?[] { null }));

            ExtractionAttempt next = access.TryStart(FirstRunId, new WorldPoint2D(4, -2))!;
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.True);
            Assert.That(next.Id, Is.Not.EqualTo(attempt.Id));
            Assert.That(channel.ProcessFrame(EmptyFrame), Is.Null);
            Assert.That(channel.Progress!.Value.Fraction, Is.Zero);
            Assert.That(channel.Progress.Value.AttemptId, Is.EqualTo(next.Id));
            now = Ms(6000);
            Assert.That(channel.ProcessFrame(EmptyFrame)!.Reason, Is.EqualTo(RunEndReason.Extracted));
        }

        [Test]
        public void AFrameEnumerationFailureDoesNotConsumeCompletionForAnEmptyRetry()
        {
            StartAttempt(FirstRunId);
            now = Ms(3000);

            Assert.Throws<InvalidOperationException>(() => channel.ProcessFrame(ThrowingFrame()));
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.True);
            Assert.That(run.Result, Is.Null);
            Assert.That(channel.ProcessFrame(EmptyFrame)!.Reason, Is.EqualTo(RunEndReason.Extracted));
        }

        [Test]
        public void NewRunDoesNotInheritSuppressionOrDispatchMarkerWhenAttemptIdRepeats()
        {
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.False);
            Assert.That(channel.ProcessFrame(EmptyFrame), Is.Null);
            ExtractionAttempt first = StartAttempt(FirstRunId);
            now = Ms(3000);
            Assert.That(channel.ProcessFrame(EmptyFrame)!.Succeeded, Is.True);
            Assert.That(run.TryReturnToTown(FirstRunId), Is.True);
            Assert.That(channel.ProcessFrame(EmptyFrame), Is.Null);
            Assert.That(channel.Progress, Is.Null);
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.False);

            ExtractionAttempt second = StartAttempt(SecondRunId);
            Assert.That(second.Id, Is.EqualTo(first.Id));
            now = Ms(6000);
            RunResult? result = channel.ProcessFrame(EmptyFrame);
            Assert.That(result!.RunId, Is.EqualTo(SecondRunId));
            Assert.That(result.Succeeded, Is.True);
        }

        [Test]
        public void ResultSubscriberStartingANewRunDoesNotPublishOldProgressIntoIt()
        {
            StartAttempt(FirstRunId);
            channel.ProcessFrame(EmptyFrame);
            run.RunResultFinalized += result =>
            {
                Assert.That(run.TryReturnToTown(result.RunId), Is.True);
                Assert.That(run.TryStartRun(SecondRunId, 1, Ms(10000)), Is.True);
            };
            now = Ms(3000);

            Assert.That(channel.ProcessFrame(EmptyFrame)!.RunId, Is.EqualTo(FirstRunId));
            Assert.That(run.CurrentRun!.Id, Is.EqualTo(SecondRunId));
            Assert.That(channel.Progress, Is.Null);
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.False);
        }

        private ExtractionAttempt StartAttempt(RunId id, int limitMs = 10000)
        {
            Assert.That(run.TryStartRun(id, 1, Ms(limitMs)), Is.True);
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.False);
            return access.TryStart(id, new WorldPoint2D(4, -2))!;
        }

        private static IEnumerable<RunSignal> ThrowingFrame()
        {
            yield return default;
            throw new InvalidOperationException("Fixture frame enumeration failed before arbitration.");
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
