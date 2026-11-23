#nullable enable

using System;
using NUnit.Framework;

namespace ProjectWeapon.Run.Tests
{
    public sealed class RunStateTests
    {
        private static readonly RunId FirstRun = new RunId(new Guid("3a008a3b-ced3-4bbb-968a-d99efb9b8716"));
        private static readonly RunId NextRun = new RunId(new Guid("a60dfe9a-9c55-44af-8578-d21470efc422"));
        private readonly TestClock clock = new TestClock();
        private RunState state = null!;

        [SetUp]
        public void SetUp()
        {
            clock.Now = Ms(0);
            state = new RunState(() => clock.Now);
        }

        [Test]
        public void StartRequiresValidRunIdZoneAndPositiveTimeLimit()
        {
            Assert.That(state.Phase, Is.EqualTo(RunPhase.Town));
            Assert.That(state.TryStartRun(default, 1, Ms(10000)), Is.False);
            Assert.That(state.TryStartRun(FirstRun, 0, Ms(10000)), Is.False);
            Assert.That(state.TryStartRun(FirstRun, 1, TimeSpan.Zero), Is.False);
            Assert.That(state.ProcessSignals(Array.Empty<RunSignal>()), Is.Null);

            clock.Now = Ms(1500);
            StartRun();
            Assert.That(state.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(state.CurrentRun!.Id, Is.EqualTo(FirstRun));
            Assert.That(state.CurrentRun.ZoneId, Is.EqualTo(1));
            Assert.That(state.CurrentRun.StartedAt, Is.EqualTo(Ms(1500)));
            Assert.That(state.CurrentRun.Deadline, Is.EqualTo(Ms(11500)));
            Assert.That(state.TryStartRun(NextRun, 1, Ms(10000)), Is.False);
            Assert.That(state.TryReturnToTown(FirstRun), Is.False);
            Assert.That(state.TryBeginExtraction(FirstRun, false), Is.Null);
            Assert.That(state.TryBeginExtraction(NextRun, true), Is.Null);
        }

        [Test]
        public void CompletionRequiresExactScheduledTimeAndFinalizesOnce()
        {
            StartRun();
            ExtractionAttempt attempt = StartExtractionAt(1000);
            Assert.That(attempt.CompletesAt, Is.EqualTo(Ms(4000)));
            Assert.That(state.Phase, Is.EqualTo(RunPhase.Extracting));
            Assert.That(state.TryBeginExtraction(FirstRun, true), Is.Null);

            clock.Now = Ms(3999);
            Assert.That(state.ProcessSignals(new[] {
                RunSignal.CompleteExtraction(FirstRun, attempt.Id, clock.Now),
                Completed(attempt)
            }), Is.Null);

            var eventCount = 0;
            state.RunResultFinalized += result =>
            {
                eventCount++;
                Assert.That(state.Phase, Is.EqualTo(RunPhase.Succeeded));
                Assert.That(state.Result, Is.SameAs(result));
                Assert.That(state.ProcessSignals(Array.Empty<RunSignal>()), Is.SameAs(result));
            };
            clock.Now = Ms(4000);
            RunResult? outcome = state.ProcessSignals(new[] { Completed(attempt), Completed(attempt) });

            Assert.That(outcome!.RunId, Is.EqualTo(FirstRun));
            Assert.That(outcome.ZoneId, Is.EqualTo(1));
            Assert.That(outcome.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(outcome.Remaining, Is.EqualTo(Ms(6000)));
            Assert.That(state.ActiveExtraction, Is.Null);
            clock.Now = Ms(10000);
            Assert.That(state.ProcessSignals(new[] { RunSignal.PlayerDied(FirstRun, Ms(4001)) }), Is.SameAs(outcome));
            Assert.That(eventCount, Is.EqualTo(1));
        }

        [Test]
        public void CancellationReturnsToExplorationAndOldAttemptCannotCompleteNewAttempt()
        {
            StartRun();
            ExtractionAttempt first = StartExtractionAt(500);
            clock.Now = Ms(2000);
            Assert.That(state.ProcessSignals(new[] { RunSignal.CancelExtraction(FirstRun, first.Id, clock.Now) }), Is.Null);
            Assert.That(state.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(state.ActiveExtraction, Is.Null);

            ExtractionAttempt current = StartExtractionAt(2200);
            Assert.That(current.Id, Is.Not.EqualTo(first.Id));
            clock.Now = Ms(5200);
            Assert.That(state.ProcessSignals(new[] {
                RunSignal.CompleteExtraction(FirstRun, first.Id, current.CompletesAt),
                RunSignal.CancelExtraction(FirstRun, first.Id, current.CompletesAt)
            }), Is.Null);
            Assert.That(state.ActiveExtraction, Is.SameAs(current));
            Assert.That(state.ProcessSignals(new[] { Completed(current) })!.Succeeded, Is.True);
        }

        [TestCase(6999, false, RunEndReason.Extracted, 9999)]
        [TestCase(6999, true, RunEndReason.Extracted, 9999)]
        [TestCase(7000, false, RunEndReason.TimeExpired, 10000)]
        [TestCase(7000, true, RunEndReason.TimeExpired, 10000)]
        [TestCase(7001, false, RunEndReason.TimeExpired, 10000)]
        [TestCase(7001, true, RunEndReason.TimeExpired, 10000)]
        public void TimeoutBoundaryUsesEffectiveTimeWhenFrameSkipsDeadline(int startMs, bool reverse, RunEndReason reason, int effectiveMs)
        {
            StartRun();
            ExtractionAttempt attempt = StartExtractionAt(startMs);
            clock.Now = Ms(10002);
            RunSignal[] observations = InOrder(Completed(attempt), RunSignal.DeadlineReached(FirstRun, Ms(10000)), reverse);
            RunResult? result = state.ProcessSignals(observations);
            Assert.That(result!.Reason, Is.EqualTo(reason));
            Assert.That(result.EffectiveAt, Is.EqualTo(Ms(effectiveMs)));
            Assert.That(result.Remaining, Is.EqualTo(Ms(10000 - effectiveMs)));
        }

        [TestCase(3799, false, RunEndReason.Death)]
        [TestCase(3799, true, RunEndReason.Death)]
        [TestCase(3800, false, RunEndReason.Death)]
        [TestCase(3800, true, RunEndReason.Death)]
        [TestCase(3801, false, RunEndReason.Extracted)]
        [TestCase(3801, true, RunEndReason.Extracted)]
        public void DeathAndExtractionUseEffectiveTimeAndPublishOneResult(int deathMs, bool reverse, RunEndReason reason)
        {
            StartRun();
            ExtractionAttempt attempt = StartExtractionAt(800);
            clock.Now = Ms(4800);
            var finalized = 0;
            state.RunResultFinalized += _ => finalized++;
            RunSignal[] observations = InOrder(Completed(attempt), RunSignal.PlayerDied(FirstRun, Ms(deathMs)), reverse);
            RunResult? result = state.ProcessSignals(observations);
            Assert.That(result!.Reason, Is.EqualTo(reason));
            Assert.That(state.Phase, Is.EqualTo(result.Succeeded ? RunPhase.Succeeded : RunPhase.Failed));
            Assert.That(state.ProcessSignals(observations), Is.SameAs(result));
            Assert.That(finalized, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SameTimeCancellationInvalidatesCompletionRegardlessOfOrdering(bool reverse)
        {
            StartRun();
            ExtractionAttempt attempt = StartExtractionAt(1000);
            clock.Now = attempt.CompletesAt;
            RunSignal[] observations = InOrder(Completed(attempt), RunSignal.CancelExtraction(FirstRun, attempt.Id, clock.Now), reverse);
            Assert.That(state.ProcessSignals(observations), Is.Null);
            Assert.That(state.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(state.ActiveExtraction, Is.Null);
        }

        [Test]
        public void OldRunAndOutOfWindowSignalsCannotAffectNewRun()
        {
            StartRun();
            clock.Now = Ms(1000);
            Assert.That(state.ProcessSignals(new[] { RunSignal.PlayerDied(FirstRun, clock.Now) })!.Reason,
                Is.EqualTo(RunEndReason.Death));

            Assert.That(state.TryReturnToTown(NextRun), Is.False);
            Assert.That(state.TryReturnToTown(FirstRun), Is.True);
            Assert.That(state.Phase, Is.EqualTo(RunPhase.Town));
            Assert.That(state.TryStartRun(FirstRun, 1, Ms(10000)), Is.False);
            Assert.That(state.TryStartRun(NextRun, 1, Ms(10000)), Is.True);

            clock.Now = Ms(2000);
            Assert.That(state.ProcessSignals(new[] {
                RunSignal.PlayerDied(FirstRun, clock.Now),
                RunSignal.PlayerDied(NextRun, Ms(999)),
                RunSignal.PlayerDied(NextRun, Ms(2001)),
                RunSignal.DeadlineReached(NextRun, clock.Now)
            }), Is.Null);
            clock.Now = Ms(3000);
            Assert.That(state.ProcessSignals(new[] { RunSignal.PlayerDied(NextRun, Ms(1999)) }), Is.Null);
            Assert.That(state.Phase, Is.EqualTo(RunPhase.Exploring));
        }

        [Test]
        public void DeadlineIsEnforcedWhenAdapterSendsNoSignals()
        {
            StartRun();
            clock.Now = Ms(9999);
            Assert.That(state.ProcessSignals(Array.Empty<RunSignal>()), Is.Null);
            clock.Now = Ms(10000);
            Assert.That(state.TryBeginExtraction(FirstRun, true), Is.Null);
            RunResult? result = state.ProcessSignals(Array.Empty<RunSignal>());
            Assert.That(result!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(result.Remaining, Is.EqualTo(TimeSpan.Zero));
            Assert.That(state.Phase, Is.EqualTo(RunPhase.Failed));
        }

        [Test]
        public void DeadlineWinsWhenDeathAndCompletionOccurAtZero()
        {
            StartRun();
            ExtractionAttempt attempt = StartExtractionAt(7000);
            clock.Now = Ms(10000);
            RunSignal resultAtZero = Completed(attempt);
            RunSignal deathAtZero = RunSignal.PlayerDied(FirstRun, clock.Now);
            RunSignal timeoutAtZero = RunSignal.DeadlineReached(FirstRun, clock.Now);
            int count = 0;
            state.RunResultFinalized += _ => count++;

            RunResult? result = state.ProcessSignals(new[] { resultAtZero, deathAtZero, timeoutAtZero });
            Assert.That(result!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void CompletionMustBeSuppliedBeforeFrameCloses()
        {
            StartRun();
            ExtractionAttempt attempt = StartExtractionAt(500);
            clock.Now = Ms(3501);
            Assert.That(state.ProcessSignals(Array.Empty<RunSignal>()), Is.Null);

            // The old effective timestamp is outside the next frame's observation window.
            Assert.That(state.ProcessSignals(new[] { Completed(attempt) }), Is.Null);
            Assert.That(state.Phase, Is.EqualTo(RunPhase.Extracting));

            clock.Now = Ms(10001);
            Assert.That(state.ProcessSignals(Array.Empty<RunSignal>())!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
        }

        [Test]
        public void ClockCannotGoBackwards()
        {
            StartRun();
            clock.Now = Ms(500);
            Assert.That(state.ProcessSignals(Array.Empty<RunSignal>()), Is.Null);
            clock.Now = Ms(499);
            Assert.Throws<InvalidOperationException>(() => state.ProcessSignals(Array.Empty<RunSignal>()));
        }

        private void StartRun() => Assert.That(state.TryStartRun(FirstRun, 1, Ms(10000)), Is.True);

        private ExtractionAttempt StartExtractionAt(int milliseconds)
        {
            clock.Now = Ms(milliseconds);
            ExtractionAttempt? attempt = state.TryBeginExtraction(FirstRun, true);
            Assert.That(attempt, Is.Not.Null);
            return attempt!;
        }

        private static RunSignal Completed(ExtractionAttempt attempt)
            => RunSignal.CompleteExtraction(FirstRun, attempt.Id, attempt.CompletesAt);

        private static RunSignal[] InOrder(RunSignal first, RunSignal second, bool reverse)
            => reverse ? new[] { second, first } : new[] { first, second };

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

        private sealed class TestClock
        {
            public TimeSpan Now { get; set; }
        }
    }
}
