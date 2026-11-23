#nullable enable

using System;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;

namespace ProjectWeapon.Extraction.Tests
{
    public sealed class ExtractionAccessTests
    {
        private const int ZoneId = 1;
        private static readonly RunId CurrentId = new RunId(new Guid("d8226384-2bb2-49a7-b9e0-ceec6719ba62"));
        private static readonly RunId OtherId = new RunId(new Guid("99dd2f37-8789-4c6c-bca0-a44dc3399610"));
        private static readonly WorldPoint2D Center = new WorldPoint2D(10, -3);

        private RunState run = null!;
        private ExtractionAccess access = null!;
        private TimeSpan now;

        [SetUp]
        public void CreateRun()
        {
            now = TimeSpan.Zero;
            run = new RunState(() => now);
            access = new ExtractionAccess(run, ZoneId, Center, 5);
        }

        [Test]
        public void AvailabilityTracksEnterEdgeExitAndReentryWithoutStartingARunAttempt()
        {
            StartRunning();
            Assert.That(access.CanStart(CurrentId, new WorldPoint2D(16, -3)), Is.False);
            Assert.That(access.CanStart(CurrentId, new WorldPoint2D(12, -2)), Is.True);
            Assert.That(access.CanStart(CurrentId, new WorldPoint2D(13, 1)), Is.True);
            Assert.That(access.CanStart(CurrentId, new WorldPoint2D(13, 1.001f)), Is.False);
            Assert.That(access.CanStart(CurrentId, Center), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(run.ActiveExtraction, Is.Null);
        }

        [Test]
        public void ButtonRechecksCurrentPositionAndAdmitsOnlyOneAttemptAtTheBoundary()
        {
            StartRunning();
            Assert.That(access.CanStart(CurrentId, Center), Is.True);
            Assert.That(access.TryStart(CurrentId, new WorldPoint2D(16, -3)), Is.Null);
            Assert.That(run.ActiveExtraction, Is.Null);

            var boundary = new WorldPoint2D(13, 1);
            ExtractionAttempt? attempt = access.TryStart(CurrentId, boundary);
            Assert.That(attempt, Is.Not.Null);
            Assert.That(run.ActiveExtraction, Is.SameAs(attempt));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Extracting));
            Assert.That(access.CanStart(CurrentId, boundary), Is.False);
            Assert.That(access.TryStart(CurrentId, boundary), Is.Null);
            Assert.That(run.ActiveExtraction, Is.SameAs(attempt));
        }

        [Test]
        public void WrongRunAndWrongZoneCannotAuthorizeStart()
        {
            StartRunning();
            Assert.That(access.CanStart(OtherId, Center), Is.False);
            Assert.That(access.TryStart(OtherId, Center), Is.Null);

            var anotherZone = new ExtractionAccess(run, ZoneId + 1, Center, 5);
            Assert.That(anotherZone.CanStart(CurrentId, Center), Is.False);
            Assert.That(anotherZone.TryStart(CurrentId, Center), Is.Null);
            Assert.That(run.ActiveExtraction, Is.Null);
        }

        [Test]
        public void TownAndTerminalPhasesDoNotAllowStart()
        {
            Assert.That(access.CanStart(CurrentId, Center), Is.False);
            Assert.That(access.TryStart(CurrentId, Center), Is.Null);
            StartRunning();
            now = TimeSpan.FromMilliseconds(100);
            run.ProcessSignals(new[] { RunSignal.PlayerDied(CurrentId, now) });
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Failed));

            Assert.That(access.CanStart(CurrentId, Center), Is.False);
            Assert.That(access.TryStart(CurrentId, Center), Is.Null);
        }

        [Test]
        public void StartAvailabilityUsesRunDeadlineEvenBeforeTimeoutSignalsAreProcessed()
        {
            StartRunning();
            now = TimeSpan.FromMilliseconds(9999);
            Assert.That(access.CanStart(CurrentId, Center), Is.True);
            Assert.That(run.ActiveExtraction, Is.Null);

            now = TimeSpan.FromSeconds(10);
            Assert.That(access.CanStart(CurrentId, Center), Is.False);
            Assert.That(access.TryStart(CurrentId, Center), Is.Null);
            Assert.That(run.Result, Is.Null);
        }

        [Test]
        public void NearDeadlineStillAllowsStartWhenExistingRunStateAllowsIt()
        {
            StartRunning();
            now = TimeSpan.FromMilliseconds(9999);
            Assert.That(access.TryStart(CurrentId, Center), Is.Not.Null);
            Assert.That(run.ActiveExtraction!.CompletesAt, Is.EqualTo(TimeSpan.FromMilliseconds(12999)));
        }

        [Test]
        public void PriorAcceptedCancelLetsThePlayerStartAgainIfWithinRadius()
        {
            StartRunning();
            ExtractionAttempt first = access.TryStart(CurrentId, Center)!;
            now = TimeSpan.FromMilliseconds(450);
            run.ProcessSignals(new[] { RunSignal.CancelExtraction(CurrentId, first.Id, now) });

            Assert.That(access.CanStart(CurrentId, Center), Is.True);
            ExtractionAttempt next = access.TryStart(CurrentId, Center)!;
            Assert.That(next.Id, Is.Not.EqualTo(first.Id));
        }

        [TestCase(float.NaN, 0)]
        [TestCase(0, float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity, 0)]
        public void MalformedPlayerPositionIsUnavailable(float x, float y)
        {
            StartRunning();
            var position = new WorldPoint2D(x, y);
            Assert.That(access.CanStart(CurrentId, position), Is.False);
            Assert.That(access.TryStart(CurrentId, position), Is.Null);
            Assert.That(run.ActiveExtraction, Is.Null);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidConfiguredRadiusIsRejected(float radius)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionAccess(run, ZoneId, Center, radius));
        }

        [Test]
        public void InvalidConfiguredZoneOrCenterIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionAccess(run, 0, Center, 5));
            Assert.Throws<ArgumentException>(() => new ExtractionAccess(run, ZoneId, new WorldPoint2D(float.NaN, 0), 5));
        }

        private void StartRunning() => Assert.That(run.TryStartRun(CurrentId, ZoneId, TimeSpan.FromSeconds(10)), Is.True);
    }
}
