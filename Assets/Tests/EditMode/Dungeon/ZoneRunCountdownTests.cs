#nullable enable

using System;
using NUnit.Framework;
using ProjectWeapon.Run;
using UnityEditor;

namespace ProjectWeapon.Dungeon.Tests
{
    public sealed class ZoneRunCountdownTests
    {
        private const string CatalogPath = "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset";
        private static readonly RunSignal[] EmptyFrame = Array.Empty<RunSignal>();
        private ZoneRunCountdown countdown = null!;
        private RunState run = null!;
        private RunId runId;
        private TimeSpan now;
        private int clockReads;

        [SetUp]
        public void SetUp()
        {
            ZoneCatalog catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null);

            runId = RunId.New();
            now = TimeSpan.FromSeconds(25);
            clockReads = 0;
            run = new RunState(() =>
            {
                clockReads++;
                return now;
            });
            countdown = new ZoneRunCountdown(catalog, run);
        }

        [TestCase(1, 480)]
        [TestCase(5, 900)]
        public void StartsFromSerializedZoneLimitAndClampsDisplay(int zoneId, int limitSeconds)
        {
            Assert.That(countdown.Sample(now), Is.Null);
            Assert.That(countdown.TryStartRun(runId, zoneId), Is.True);

            TimeSpan limit = TimeSpan.FromSeconds(limitSeconds);
            Assert.That(run.CurrentRun!.Deadline, Is.EqualTo(now + limit));
            ZoneCountdownSample initial = countdown.Sample(TimeSpan.Zero)!.Value;
            Assert.That(initial.RunId, Is.EqualTo(runId));
            Assert.That(initial.ZoneId, Is.EqualTo(zoneId));
            Assert.That(initial.Remaining, Is.EqualTo(limit));

            TimeSpan elapsed = TimeSpan.FromTicks(1234567);
            now += elapsed;
            Assert.That(countdown.ProcessFrame(EmptyFrame), Is.Null);
            Assert.That(countdown.Sample(now)!.Value.Remaining, Is.EqualTo(limit - elapsed));
            Assert.That(countdown.Sample(run.CurrentRun.Deadline + TimeSpan.FromTicks(1))!.Value.Remaining, Is.EqualTo(TimeSpan.Zero));
            Assert.That(run.Result, Is.Null);
        }

        [Test]
        public void EmptyFrameAtZeroExpiresOnceAndTownClearsTheCountdown()
        {
            Assert.That(countdown.TryStartRun(runId, 2), Is.True);
            int resultCount = 0;
            run.RunResultFinalized += _ => resultCount++;
            now = run.CurrentRun!.Deadline;

            RunResult? result = countdown.ProcessFrame(EmptyFrame);

            Assert.That(result!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(result.EffectiveAt, Is.EqualTo(now));
            Assert.That(countdown.Sample(now)!.Value.Remaining, Is.EqualTo(TimeSpan.Zero));

            now += TimeSpan.FromSeconds(2);
            Assert.That(countdown.ProcessFrame(new[] { RunSignal.PlayerDied(runId, result.EffectiveAt) }), Is.SameAs(result));
            Assert.That(countdown.Sample(now)!.Value.Remaining, Is.EqualTo(TimeSpan.Zero));
            Assert.That(resultCount, Is.EqualTo(1));
            Assert.That(run.TryReturnToTown(runId), Is.True);
            Assert.That(countdown.Sample(now), Is.Null);

            RunId nextId = RunId.New();
            Assert.That(countdown.TryStartRun(nextId, 5), Is.True);
            Assert.That(countdown.Sample(now)!.Value.RunId, Is.EqualTo(nextId));
            Assert.That(countdown.Sample(now)!.Value.Remaining, Is.EqualTo(TimeSpan.FromSeconds(900)));
        }

        [TestCase(-1, RunEndReason.Extracted)]
        [TestCase(0, RunEndReason.TimeExpired)]
        [TestCase(1, RunEndReason.TimeExpired)]
        public void SkippedFrameResolvesCompletionAgainstZeroWithOneClockRead(int deltaTicks, RunEndReason expected)
        {
            ExtractionAttempt attempt = StartAttempt(deltaTicks);
            TimeSpan deadline = run.CurrentRun!.Deadline;
            now = deadline + TimeSpan.FromSeconds(1);
            int readsBefore = clockReads;
            int resultCount = 0;
            run.RunResultFinalized += _ => resultCount++;

            RunResult? result = countdown.ProcessFrame(EmptyFrame);

            Assert.That(clockReads - readsBefore, Is.EqualTo(1));
            Assert.That(result!.Reason, Is.EqualTo(expected));
            Assert.That(countdown.Extraction.IsAutomaticAttackSuppressed, Is.False);
            TimeSpan effectiveAt = expected == RunEndReason.Extracted ? attempt.CompletesAt : deadline;
            Assert.That(result.EffectiveAt, Is.EqualTo(effectiveAt));
            Assert.That(countdown.Sample(now)!.Value.Remaining, Is.EqualTo(deadline - effectiveAt));

            now += TimeSpan.FromSeconds(1);
            Assert.That(countdown.ProcessFrame(EmptyFrame), Is.SameAs(result));
            Assert.That(countdown.Sample(now)!.Value.Remaining, Is.EqualTo(deadline - effectiveAt));
            Assert.That(resultCount, Is.EqualTo(1));
        }

        [TestCase(false, false, RunEndReason.Death)]
        [TestCase(false, true, RunEndReason.Death)]
        [TestCase(true, false, RunEndReason.TimeExpired)]
        [TestCase(true, true, RunEndReason.TimeExpired)]
        public void GroupsCompletionWithDeathOrCancellationRegardlessOfInputOrder(
            bool cancel, bool reverse, RunEndReason expected)
        {
            ExtractionAttempt attempt = StartAttempt(-1);
            TimeSpan completionAt = attempt.CompletesAt;
            TimeSpan deadline = run.CurrentRun!.Deadline;
            now = deadline + TimeSpan.FromTicks(1);
            var frame = new[]
            {
                RunSignal.CompleteExtraction(runId, attempt.Id, completionAt),
                cancel
                    ? RunSignal.CancelExtraction(runId, attempt.Id, completionAt)
                    : RunSignal.PlayerDied(runId, completionAt)
            };
            if (reverse)
                Array.Reverse(frame);
            int readsBefore = clockReads;

            RunResult? result = countdown.ProcessFrame(frame);

            Assert.That(clockReads - readsBefore, Is.EqualTo(1));
            Assert.That(result!.Reason, Is.EqualTo(expected));
            Assert.That(countdown.Extraction.IsAutomaticAttackSuppressed, Is.False);
            Assert.That(result.EffectiveAt, Is.EqualTo(cancel ? deadline : completionAt));
        }

        private ExtractionAttempt StartAttempt(int completionOffsetTicks)
        {
            Assert.That(countdown.TryStartRun(runId, 1), Is.True);
            now = run.CurrentRun!.Deadline - RunState.ExtractionDuration + TimeSpan.FromTicks(completionOffsetTicks);
            ExtractionAttempt? attempt = run.TryBeginExtraction(runId, true);
            Assert.That(attempt, Is.Not.Null);
            Assert.That(countdown.ProcessFrame(EmptyFrame), Is.Null);
            return attempt!;
        }
    }
}
