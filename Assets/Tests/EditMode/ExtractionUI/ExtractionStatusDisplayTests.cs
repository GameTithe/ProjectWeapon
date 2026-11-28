#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Extraction;
using ProjectWeapon.Player;
using ProjectWeapon.Run;

namespace ProjectWeapon.ExtractionUI.Tests
{
    public sealed class ExtractionStatusDisplayTests
    {
        private static readonly WorldPoint2D Center = new WorldPoint2D(1, 1);
        private TimeSpan now;
        private RunId id;
        private RunState run = null!;
        private ExtractionAccess access = null!;
        private ExtractionChannel channel = null!;

        [SetUp]
        public void Prepare()
        {
            now = TimeSpan.Zero;
            id = RunId.New();
            run = new RunState(() => now);
            access = new ExtractionAccess(run, 1, Center, 2);
            channel = new ExtractionChannel(run);
        }

        [Test]
        public void ProgressStartsAtZeroAndOnlyTheAcceptedThreeSecondResultDisplaysCompletion()
        {
            StartRun(10000);
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Hidden));
            Begin();
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Channeling));
            Assert.That(Display().Percentage, Is.Zero);

            Frame(1500);
            Assert.That(Display().Fraction, Is.EqualTo(.5f));
            Assert.That(Display().Percentage, Is.EqualTo(50));
            Frame(2999);
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Channeling));
            Assert.That(Display().Percentage, Is.EqualTo(99));

            Frame(3000);
            Assert.That(run.Result?.EffectiveAt, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Succeeded));
            Assert.That(Display().Percentage, Is.EqualTo(100));
            Assert.That(Display().Fraction, Is.EqualTo(1));
            Assert.That(run.TryReturnToTown(id), Is.True);
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Hidden));
        }

        [Test]
        public void ActualAcceptedThresholdCancelsWithMovementAndRetryClearsThePreviousAttempt()
        {
            StartRun(10000);
            ExtractionAttempt attempt = Begin();
            now = TimeSpan.FromMilliseconds(1500);
            Assert.That(channel.ObserveMovement(.19f, now), Is.Null);
            channel.ProcessFrame(Array.Empty<RunSignal>());
            Assert.That(Display().Percentage, Is.EqualTo(50));

            RunSignal movement = channel.ObserveMovement(.20f, now)!.Value;
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Channeling));
            channel.ProcessFrame(new[] { movement });
            Assert.That(run.LastExtractionCancellation?.AttemptId, Is.EqualTo(attempt.Id));
            Assert.That(run.LastExtractionCancellation?.EffectiveAt, Is.EqualTo(now));
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Cancelled));
            Assert.That(Display().Title, Does.StartWith("이동으로"));
            Assert.That(Display().Fraction, Is.Zero);

            Begin();
            channel.ProcessFrame(new[] { movement });
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Channeling));
            Assert.That(Display().Percentage, Is.Zero);
            Assert.That(run.LastExtractionCancellation, Is.Null);
        }

        [Test]
        public void ConcreteDamageCancelsAtCompletionButZeroDamageDoesNot()
        {
            StartRun(10000);
            Begin();
            var health = new PlayerHealth(10);
            var signals = new List<RunSignal>();
            now = TimeSpan.FromMilliseconds(1250);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 0, now, signals), Is.False);
            channel.ProcessFrame(signals);
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Channeling));

            now = TimeSpan.FromSeconds(3);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, id, 1, now, signals), Is.True);
            channel.ProcessFrame(signals);
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Cancelled));
            Assert.That(Display().Title, Does.StartWith("피격으로"));
            Assert.That(run.Result, Is.Null);
        }

        [TestCase(true, ExtractionCancelReason.Movement)]
        [TestCase(false, ExtractionCancelReason.Damage)]
        public void TwoCancelObservationsAtSameTimeKeepTheExistingFirstAcceptedOrder(bool moveFirst,
            ExtractionCancelReason expected)
        {
            StartRun(10000);
            Begin();
            now = TimeSpan.FromSeconds(1);
            var signals = new List<RunSignal>();
            var dodge = channel.ObserveAcceptedDodge(id, now)!.Value;
            if (moveFirst)
                signals.Add(dodge);
            Assert.That(PlayerRunDamage.ApplyHit(new PlayerHealth(10), run, id, 1, now, signals), Is.True);
            if (!moveFirst)
                signals.Add(dodge);

            channel.ProcessFrame(signals);
            Assert.That(run.LastExtractionCancellation?.Reason, Is.EqualTo(expected));
        }

        [Test]
        public void SameTimeDeathAndExpiredDeadlineDoNotShowCancellationOrCompletion()
        {
            StartRun(3000);
            Begin();
            now = TimeSpan.FromSeconds(3);
            channel.ProcessFrame(new[] { channel.ObserveMovement(.2f, now)!.Value });
            Assert.That(run.Result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(run.LastExtractionCancellation, Is.Null);
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Hidden));

            Assert.That(run.TryReturnToTown(id), Is.True);
            id = RunId.New();
            StartRun(10000);
            Begin();
            var signals = new List<RunSignal>();
            now = TimeSpan.FromSeconds(4);
            Assert.That(PlayerRunDamage.ApplyHit(new PlayerHealth(1), run, id, 1, now, signals), Is.True);
            channel.ProcessFrame(signals);
            Assert.That(run.Result?.Reason, Is.EqualTo(RunEndReason.Death));
            Assert.That(run.LastExtractionCancellation, Is.Null);
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Hidden));
        }

        [Test]
        public void ExistingCancellationCallStillClearsTheBarWithANeutralReason()
        {
            StartRun(10000);
            ExtractionAttempt attempt = Begin();
            var legacyCancel = RunSignal.CancelExtraction(id, attempt.Id, now);
            channel.ProcessFrame(new[] { legacyCancel });
            Assert.That(Display().Kind, Is.EqualTo(ExtractionStatusKind.Cancelled));
            Assert.That(Display().Title, Is.EqualTo("귀환이 취소되었습니다."));
        }

        private ExtractionStatusDisplay Display() => ExtractionStatusDisplay.Read(run, channel);

        private void StartRun(int durationMs)
            => Assert.That(run.TryStartRun(id, 1, TimeSpan.FromMilliseconds(durationMs)), Is.True);

        private ExtractionAttempt Begin()
        {
            ExtractionAttempt? attempt = access.TryStart(id, Center, 0);
            Assert.That(attempt, Is.Not.Null);
            return attempt!;
        }

        private void Frame(int timeMs)
        {
            now = TimeSpan.FromMilliseconds(timeMs);
            channel.ProcessFrame(Array.Empty<RunSignal>());
        }
    }
}
