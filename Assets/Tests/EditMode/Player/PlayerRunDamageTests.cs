#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Run;

namespace ProjectWeapon.Player.Tests
{
    public sealed class PlayerRunDamageTests
    {
        [Test]
        public void DodgeInvulnerabilityUsesTheHitTimeAndStopsAtTheExclusiveBoundary()
        {
            var id = RunId.New();
            var run = new RunState(() => TimeSpan.Zero);
            var dodge = new PlayerDodge();
            var health = new PlayerHealth(10);
            var signals = new List<RunSignal>();
            int damageEvents = 0;
            health.DamageApplied += _ => damageEvents++;
            Assert.That(run.TryStartRun(id, 1, TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(dodge.TryStart(run, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(1250)), Is.True);

            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 1, TimeSpan.FromMilliseconds(99), signals, dodge), Is.True);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 10, TimeSpan.FromMilliseconds(100), signals, dodge), Is.False);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 10, TimeSpan.FromMilliseconds(219), signals, dodge), Is.False);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 1, TimeSpan.FromMilliseconds(220), signals, dodge), Is.True);
            Assert.That(dodge.IsInvulnerable(RunId.New(), TimeSpan.FromMilliseconds(100)), Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(8));
            Assert.That(damageEvents, Is.EqualTo(2));
            Assert.That(signals, Is.Empty);
        }

        [Test]
        public void InvalidWrongRunAndExploringHitsDoNotQueueCancellation()
        {
            var id = RunId.New();
            var run = new RunState(() => TimeSpan.FromSeconds(2));
            Assert.That(run.TryStartRun(id, 1, TimeSpan.FromSeconds(10)), Is.True);
            var health = new PlayerHealth(10);
            var signals = new List<RunSignal>();

            Assert.That(PlayerRunDamage.ApplyHit(health, run, RunId.New(), 9, TimeSpan.FromSeconds(2), signals), Is.False);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 6, TimeSpan.FromSeconds(1), signals), Is.False);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 0, TimeSpan.FromSeconds(2), signals), Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(10));
            Assert.That(signals, Is.Empty);

            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 1, TimeSpan.FromSeconds(2), signals), Is.True);
            Assert.That(health.CurrentHealth, Is.EqualTo(9));
            Assert.That(signals, Is.Empty);
        }

        [TestCase(10)]
        [TestCase(11)]
        public void DamageAtOrAfterDeadlineDoesNotChangeHealthOrPublishEvents(int hitSeconds)
        {
            TimeSpan now = TimeSpan.Zero;
            var run = new RunState(() => now);
            var id = RunId.New();
            var health = new PlayerHealth(10);
            var signals = new List<RunSignal>();
            int healthEvents = 0;
            health.DamageApplied += _ => healthEvents++;
            health.Died += _ => healthEvents++;
            Assert.That(run.TryStartRun(id, 1, TimeSpan.FromSeconds(10)), Is.True);
            now = TimeSpan.FromSeconds(7);
            Assert.That(run.TryBeginExtraction(id, isInRange: true), Is.Not.Null);
            now = TimeSpan.FromSeconds(hitSeconds);

            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 10, now, signals), Is.False);

            Assert.That(health.CurrentHealth, Is.EqualTo(10));
            Assert.That(healthEvents, Is.Zero);
            Assert.That(signals, Is.Empty);
            Assert.That(run.ProcessSignals(signals)?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NonlethalDamageAtCompletionCancelsTheActiveAttempt(bool completeFirst)
        {
            TimeSpan now = TimeSpan.Zero;
            var run = new RunState(() => now);
            var id = RunId.New();
            var health = new PlayerHealth(10);
            var signals = new List<RunSignal>();
            Assert.That(run.TryStartRun(id, 1, TimeSpan.FromSeconds(10)), Is.True);
            ExtractionAttempt attempt = run.TryBeginExtraction(id, isInRange: true)!;
            now = attempt.CompletesAt;
            RunSignal completion = RunSignal.CompleteExtraction(id, attempt.Id, now);
            if (completeFirst)
                signals.Add(completion);

            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 1, now, signals), Is.True);
            if (!completeFirst)
                signals.Add(completion);

            Assert.That(signals.Count, Is.EqualTo(2));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Extracting));
            Assert.That(run.ProcessSignals(signals), Is.Null);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(run.ActiveExtraction, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeathAtCompletionResolvesThroughRunStateOnlyOnce(bool completeFirst)
        {
            TimeSpan now = TimeSpan.Zero;
            var run = new RunState(() => now);
            var id = RunId.New();
            var health = new PlayerHealth(10);
            var signals = new List<RunSignal>();
            int results = 0;
            run.RunResultFinalized += _ => results++;
            Assert.That(run.TryStartRun(id, 1, TimeSpan.FromSeconds(10)), Is.True);
            ExtractionAttempt attempt = run.TryBeginExtraction(id, isInRange: true)!;
            now = attempt.CompletesAt;
            RunSignal completion = RunSignal.CompleteExtraction(id, attempt.Id, now);
            if (completeFirst)
                signals.Add(completion);

            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 20, now, signals), Is.True);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 1, now, signals), Is.False);
            if (!completeFirst)
                signals.Add(completion);

            Assert.That(signals.Count, Is.EqualTo(3));
            Assert.That(results, Is.Zero);
            RunResult? result = run.ProcessSignals(signals);
            Assert.That(result?.Reason, Is.EqualTo(RunEndReason.Death));
            Assert.That(result?.EffectiveAt, Is.EqualTo(now));
            Assert.That(run.ProcessSignals(signals), Is.SameAs(result));
            Assert.That(results, Is.EqualTo(1));
        }

        [Test]
        public void DelayedCancellationRefersToTheAttemptSeenAtImpact()
        {
            TimeSpan now = TimeSpan.Zero;
            var run = new RunState(() => now);
            var id = RunId.New();
            var health = new PlayerHealth(10);
            var signals = new List<RunSignal>();
            Assert.That(run.TryStartRun(id, 1, TimeSpan.FromSeconds(10)), Is.True);
            ExtractionAttempt first = run.TryBeginExtraction(id, isInRange: true)!;
            now = TimeSpan.FromSeconds(1);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 1, now, signals), Is.True);

            run.ProcessSignals(new[] { RunSignal.CancelExtraction(id, first.Id, now) });
            ExtractionAttempt second = run.TryBeginExtraction(id, isInRange: true)!;
            run.ProcessSignals(signals);

            Assert.That(run.ActiveExtraction, Is.SameAs(second));
        }
    }
}
