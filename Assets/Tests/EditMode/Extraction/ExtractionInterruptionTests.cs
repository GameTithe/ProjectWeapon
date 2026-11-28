#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.AutoAttack;
using ProjectWeapon.Combat;
using ProjectWeapon.Player;
using ProjectWeapon.Run;

namespace ProjectWeapon.Extraction.Tests
{
    public sealed class ExtractionInterruptionTests
    {
        private static readonly RunId TestRun = new RunId(new Guid("df3e1638-cc36-4599-ad4c-ab86acec4730"));
        private static readonly WorldPoint2D Center = new WorldPoint2D(3f, -2f);
        private TimeSpan now;
        private RunState run = null!;
        private ExtractionAccess access = null!;
        private ExtractionChannel channel = null!;

        [SetUp]
        public void SetUp()
        {
            now = TimeSpan.Zero;
            run = new RunState(() => now);
            access = new ExtractionAccess(run, 1, Center, 2f);
            channel = new ExtractionChannel(run);
            Assert.That(run.TryStartRun(TestRun, 1, Ms(10000)), Is.True);
        }

        [Test]
        public void MovementAtThresholdBlocksStartWhileSmallInputAllowsIt()
        {
            Assert.That(access.CanStart(TestRun, Center, 0.20f), Is.False);
            Assert.That(access.TryStart(TestRun, Center, 0.20f), Is.Null);
            Assert.That(access.CanStart(TestRun, Center, 0.19f), Is.True);
            Assert.That(access.TryStart(TestRun, Center, 0.19f), Is.Not.Null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MoveInputCancelsEvenWithoutDisplacementAndRestoresPreviousAttackToggle(bool attacksEnabled)
        {
            var scheduler = new AutoAttackScheduler(run, _ => true, attacksEnabled);
            Assert.That(access.TryStart(TestRun, Center, 0f), Is.Not.Null);
            Assert.That(scheduler.IsEffective, Is.False);
            Assert.That(scheduler.TrySetEnabled(!attacksEnabled), Is.False);

            now = Ms(1200);
            Assert.That(channel.ObserveMovement(0.19f, now), Is.Null);
            Assert.That(channel.ObserveMovement(float.NaN, now), Is.Null);
            Assert.That(channel.ProcessFrame(Array.Empty<RunSignal>()), Is.Null);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Extracting));

            RunSignal? moveSignal = channel.ObserveMovement(0.20f, now);
            Assert.That(moveSignal.HasValue, Is.True);
            Assert.That(channel.ProcessFrame(new[] { moveSignal!.Value }), Is.Null);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(scheduler.IsEnabled, Is.EqualTo(attacksEnabled));
            Assert.That(scheduler.IsEffective, Is.EqualTo(attacksEnabled));
            Assert.That(channel.Progress, Is.Null);
        }

        [Test]
        public void AcceptedDodgeForCurrentRunCancelsButStaleRunOrTimestampDoesNot()
        {
            now = Ms(100);
            Assert.That(access.TryStart(TestRun, Center, 0f), Is.Not.Null);
            now = Ms(150);
            var previousRun = new RunId(new Guid("735285f4-6cd4-421c-ad73-3772c809420c"));

            Assert.That(channel.ObserveAcceptedDodge(previousRun, now), Is.Null);
            Assert.That(channel.ObserveAcceptedDodge(TestRun, Ms(99)), Is.Null);
            RunSignal? dodge = channel.ObserveAcceptedDodge(TestRun, now);
            Assert.That(dodge.HasValue, Is.True);
            channel.ProcessFrame(new[] { dodge!.Value });
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(channel.ObserveAcceptedDodge(TestRun, now), Is.Null);
        }

        [Test]
        public void SameTimeNonFatalDamageCancelsBeforeCompletionAndNoDamageAllowsNewAttempt()
        {
            var health = new PlayerHealth(25);
            var signals = new List<RunSignal>();
            Assert.That(access.TryStart(TestRun, Center, 0f), Is.Not.Null);

            now = Ms(3000);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, TestRun, 1, now, signals), Is.True);
            Assert.That(channel.ProcessFrame(signals), Is.Null);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(health.CurrentHealth, Is.EqualTo(24));

            now = Ms(3100);
            Assert.That(access.TryStart(TestRun, Center, 0f), Is.Not.Null);
            now = Ms(6100);
            signals.Clear();
            Assert.That(PlayerRunDamage.ApplyHit(health, run, TestRun, 0, now, signals), Is.False);
            RunResult? result = channel.ProcessFrame(signals);
            Assert.That(result?.Reason, Is.EqualTo(RunEndReason.Extracted));
        }

        [Test]
        public void SameTimeMoveSignalCannotOverrideDeadlineResult()
        {
            now = Ms(7000);
            Assert.That(access.TryStart(TestRun, Center, 0f), Is.Not.Null);

            now = Ms(10000);
            RunSignal? signal = channel.ObserveMovement(0.5f, now);
            Assert.That(signal.HasValue, Is.True);
            RunResult? result = channel.ProcessFrame(new[] { signal!.Value });
            Assert.That(result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
