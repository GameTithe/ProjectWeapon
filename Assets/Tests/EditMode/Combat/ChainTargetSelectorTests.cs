#nullable enable

using System;
using System.Linq;
using NUnit.Framework;

namespace ProjectWeapon.Combat.Tests
{
    public sealed class ChainTargetSelectorTests
    {
        [Test]
        public void EachHopMeasuresFromPreviousHitAndStopsAtGap()
        {
            TargetCandidate initial = Candidate("first", 0, 0);
            var candidates = new[]
            {
                Candidate("across-gap", 4.01f, 0),
                Candidate("third", 3, 0),
                Candidate("second", 2, 0),
                Candidate("nearest", 1, 0)
            };

            Assert.That(SelectIds(initial, 4, 1, candidates), Is.EqualTo(new[] { "nearest", "second", "third" }));
            Assert.That(SelectIds(initial, 2, 1, candidates), Is.EqualTo(new[] { "nearest", "second" }));
        }

        [Test]
        public void DeadInitialHitStillAnchorsChainAndItsColliderIdsAreExcluded()
        {
            TargetCandidate initial = Candidate("first", 0, 0, isAlive: false);
            var candidates = new[]
            {
                Candidate("first", 0, 0),
                Candidate("next", 1, 0),
                Candidate("first", 1, 0)
            };

            Assert.That(SelectIds(initial, 3, 1, candidates), Is.EqualTo(new[] { "next" }));
        }

        [Test]
        public void SelectedIdAndItsOtherCollidersAreExcludedForThisChainOnly()
        {
            TargetCandidate initial = Candidate("first", 0, 0);
            var candidates = new[]
            {
                Candidate("other", 3, 0),
                Candidate("duplicate", 2, 0),
                Candidate("duplicate", 1, 0)
            };

            // From the selected collider at x=1, the other ID is out of range.
            // The duplicate collider at x=2 must not act as an intermediate hop.
            Assert.That(SelectIds(initial, 5, 1, candidates), Is.EqualTo(new[] { "duplicate" }));
            Assert.That(SelectIds(initial, 5, 1, candidates), Is.EqualTo(new[] { "duplicate" }));
            Assert.That(candidates.Length, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DistanceTieUsesOrdinalIdRegardlessOfCandidateOrder(bool reverse)
        {
            TargetCandidate initial = Candidate("first", 0, 0);
            var candidates = new[]
            {
                Candidate("z", 1, 0),
                Candidate("Z", -1, 0),
                Candidate("0-farther", 0, 2)
            };
            if (reverse)
                Array.Reverse(candidates);

            Assert.That(SelectIds(initial, 3, 1, candidates), Is.EqualTo(new[] { "Z" }));
        }

        [Test]
        public void BoundaryIsInclusiveAndNearbyIneligibleTargetsDoNotEnterChain()
        {
            TargetCandidate initial = Candidate("first", 0, 0);
            var candidates = new[]
            {
                default,
                Candidate(" ", 0, 0),
                Candidate("dead", 0, 0, isAlive: false),
                Candidate("nonattackable", 0, 1, isAttackable: false),
                Candidate("nonfinite", float.NaN, 0),
                Candidate("boundary", 3, 4)
            };

            Assert.That(SelectIds(initial, 2, 5, candidates), Is.EqualTo(new[] { "boundary" }));
            Assert.That(SelectIds(initial, 2, 4.999f, candidates), Is.Empty);
        }

        [Test]
        public void ZeroDistanceAllowsOnlyCoincidentValidTargets()
        {
            TargetCandidate initial = Candidate("first", 2, 1);
            var candidates = new[] { Candidate("coincident", 2, 1), Candidate("offset", 2.01f, 1) };

            Assert.That(SelectIds(initial, int.MaxValue, 0, candidates), Is.EqualTo(new[] { "coincident" }));
            Assert.That(SelectIds(initial, 0, 10, candidates), Is.Empty);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void MalformedDistanceYieldsNoAdditionalHits(float distance)
        {
            Assert.That(SelectIds(Candidate("first", 0, 0), 1, distance, Candidate("other", 0, 0)), Is.Empty);
        }

        [Test]
        public void InvalidAnchorOrEmptyCandidatesYieldNoAdditionalHits()
        {
            TargetCandidate other = Candidate("other", 0, 0);
            Assert.That(SelectIds(default, 1, 1, other), Is.Empty);
            Assert.That(SelectIds(Candidate("first", 0, float.PositiveInfinity), 1, 1, other), Is.Empty);
            Assert.That(SelectIds(Candidate("first", 0, 0), 1, 1), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => ChainTargetSelector.FindAdditionalTargets(default, 1, 1, null!));
        }

        private static string?[] SelectIds(TargetCandidate initial, int cap, float distance, params TargetCandidate[] candidates)
            => ChainTargetSelector.FindAdditionalTargets(initial, cap, distance, candidates).Select(hit => hit.TargetId).ToArray();

        private static TargetCandidate Candidate(string id, float x, float y, bool isAlive = true, bool isAttackable = true)
            => new TargetCandidate(id, new WorldPoint2D(x, y), isAlive, isAttackable);
    }
}
