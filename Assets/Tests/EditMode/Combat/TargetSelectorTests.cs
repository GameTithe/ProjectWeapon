#nullable enable

using System;
using NUnit.Framework;

namespace ProjectWeapon.Combat.Tests
{
    public sealed class TargetSelectorTests
    {
        [Test]
        public void ChoosesNearestAliveAndAttackableTargetFromWorldOrigin()
        {
            var origin = new WorldPoint2D(10, -20);
            TargetCandidate? result = TargetSelector.FindNearest(origin, 5, new[]
            {
                Candidate("farther", 10, -16),
                Candidate("dead", 10, -20, isAlive: false),
                Candidate("outside", 10, -14),
                Candidate("friendly", 10, -20, isAttackable: false),
                Candidate("nearest", 12, -21),
                Candidate("outside-opposite", 4, -20)
            });

            Assert.That(result?.TargetId, Is.EqualTo("nearest"));
        }

        [Test]
        public void IncludesExactRangeBoundaryAndExcludesTargetsBeyondIt()
        {
            var origin = new WorldPoint2D(0, 0);
            TargetCandidate boundary = Candidate("boundary", 3, -4);
            TargetCandidate outside = Candidate("outside", 3, -4.01f);
            Assert.That(TargetSelector.FindNearest(origin, 5, new[] { outside, boundary })?.TargetId,
                Is.EqualTo("boundary"));
            Assert.That(TargetSelector.FindNearest(origin, 5, new[] { outside }), Is.Null);
        }

        [Test]
        public void ZeroRangeAllowsOnlyACoincidentTarget()
        {
            var origin = new WorldPoint2D(4, -2);
            TargetCandidate outside = Candidate("outside", 4, -2.01f);
            TargetCandidate coincident = Candidate("coincident", 4, -2);
            Assert.That(TargetSelector.FindNearest(origin, 0, new[] { outside, coincident })?.TargetId,
                Is.EqualTo("coincident"));
            Assert.That(TargetSelector.FindNearest(origin, 0, new[] { outside }), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EqualDistanceUsesOrdinalIdRegardlessOfCandidateOrder(bool reverse)
        {
            var origin = new WorldPoint2D(0, 0);
            TargetCandidate alpha = Candidate("A-9", -1, 0);
            TargetCandidate beta = Candidate("a-1", 0, 1);
            TargetCandidate farther = Candidate("0-farther", 0, 2);
            TargetCandidate[] candidates = reverse
                ? new[] { beta, farther, alpha }
                : new[] { alpha, farther, beta };

            Assert.That(TargetSelector.FindNearest(origin, 2, candidates)?.TargetId, Is.EqualTo("A-9"));
        }

        [Test]
        public void NormalizedDuplicateCandidatesDoNotChangeTheSelection()
        {
            var origin = new WorldPoint2D(0, 0);
            TargetCandidate first = Candidate("target-1", 1, 0);
            TargetCandidate second = Candidate("target-2", 0, 1);
            TargetCandidate? result = TargetSelector.FindNearest(origin, 2, new[] { first, second, first });
            Assert.That(result?.TargetId, Is.EqualTo("target-1"));
        }

        [Test]
        public void EmptyAndEntirelyInvalidSearchesReturnNoTarget()
        {
            var origin = new WorldPoint2D(0, 0);
            Assert.That(TargetSelector.FindNearest(origin, 5, Array.Empty<TargetCandidate>()), Is.Null);
            Assert.That(TargetSelector.FindNearest(origin, 5, new[]
            {
                default,
                Candidate(" ", 0, 0),
                Candidate("bad-x", float.NaN, 0),
                Candidate("bad-y", 0, float.NegativeInfinity),
                Candidate("dead", 0, 1, isAlive: false),
                Candidate("invalid", 1, 0, isAttackable: false)
            }), Is.Null);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void MalformedRangeReturnsNoTarget(float range)
        {
            var origin = new WorldPoint2D(0, 0);
            Assert.That(TargetSelector.FindNearest(origin, range, new[] { Candidate("target", 0, 0) }), Is.Null);
        }

        [Test]
        public void NonFiniteOriginAndNullCandidatesAreHandled()
        {
            var badOrigin = new WorldPoint2D(float.PositiveInfinity, 0);
            Assert.That(TargetSelector.FindNearest(badOrigin, 1, new[] { Candidate("target", 0, 0) }), Is.Null);
            Assert.Throws<ArgumentNullException>(() => TargetSelector.FindNearest(default, 1, null!));
        }

        [Test]
        public void LargeFiniteRangePreservesDistanceAndDoesNotIncludeInfinity()
        {
            var origin = new WorldPoint2D(-float.MaxValue, 0);
            TargetCandidate far = Candidate("0-outside", float.MaxValue, 0);
            TargetCandidate atBoundary = Candidate("1-boundary", 0, 0);
            Assert.That(TargetSelector.FindNearest(origin, float.MaxValue, new[] { far, atBoundary })?.TargetId,
                Is.EqualTo("1-boundary"));
        }

        private static TargetCandidate Candidate(string targetId, float x, float y, bool isAlive = true, bool isAttackable = true)
            => new TargetCandidate(targetId, new WorldPoint2D(x, y), isAlive, isAttackable);
    }
}
