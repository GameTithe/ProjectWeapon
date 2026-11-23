#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProjectWeapon.Combat.Tests
{
    public sealed class AreaDamageSelectorTests
    {
        private const string Attacker = "attacker";
        private static readonly Guid FirstApplication = new Guid("07222522-6fc5-4edf-b26a-37b671926ca0");
        private static readonly Guid NextApplication = new Guid("1acd480f-dde0-4961-a775-31eabe64792b");

        [Test]
        public void CircleIncludesBoundaryAndFiltersAttackerAndInvalidTargets()
        {
            var origin = new WorldPoint2D(10, -10);
            AreaDamageRequest attack = AreaDamageRequest.Circle(FirstApplication, Attacker, origin, radius: 5, amount: 12.5f);

            IReadOnlyList<DamageRequest> hits = AreaDamageSelector.FindTargets(attack, new[]
            {
                Candidate("boundary", 13, -6),
                Candidate("outside", 13, -5.99f),
                Candidate(Attacker, 10, -10),
                Candidate("dead", 10, -10, isAlive: false),
                Candidate("friendly", 10, -10, isAttackable: false),
                Candidate("invalid-x", float.NaN, -10),
                Candidate("inside", 9, -11),
                default
            });

            Assert.That(hits.Select(hit => hit.TargetId), Is.EqualTo(new[] { "boundary", "inside" }));
            Assert.That(hits.All(hit => hit.ApplicationId == FirstApplication && hit.AttackerId == Attacker
                && hit.Amount == 12.5f), Is.True);
        }

        [Test]
        public void SectorIncludesBothAngularEdgesAndTheRadialEdge()
        {
            var origin = new WorldPoint2D(4, -6);
            AreaDamageRequest attack = AreaDamageRequest.Sector(FirstApplication, Attacker, origin,
                radius: 5, amount: 1, direction: new WorldPoint2D(10, 0), angleDegrees: 90);

            IReadOnlyList<DamageRequest> hits = AreaDamageSelector.FindTargets(attack, new[]
            {
                Candidate("upper-edge", 5, -5),
                Candidate("above-edge", 5, -4.999f),
                Candidate("lower-edge", 5, -7),
                Candidate("below-edge", 5, -7.001f),
                Candidate("behind", 3, -6),
                Candidate("nearer-but-outside-angle", 4, -5.9f),
                Candidate("radial-edge", 9, -6),
                Candidate("beyond-radius", 9.001f, -6)
            });

            Assert.That(hits.Select(hit => hit.TargetId), Is.EqualTo(new[] { "upper-edge", "lower-edge", "radial-edge" }));
        }

        [TestCase(0, false, false)]
        [TestCase(180, true, false)]
        [TestCase(360, true, true)]
        public void SectorInterpretsAngleAsFullWidthAndAlwaysIncludesApex(
            float angleDegrees, bool includesSide, bool includesBack)
        {
            AreaDamageRequest attack = AreaDamageRequest.Sector(FirstApplication, Attacker, default,
                radius: 5, amount: 1, direction: new WorldPoint2D(-10, -10), angleDegrees: angleDegrees);

            string[] hits = AreaDamageSelector.FindTargets(attack, new[]
            {
                Candidate("front", -1, -1),
                Candidate("side", -1, 1),
                Candidate("back", 1, 1),
                Candidate("apex", 0, 0)
            }).Select(hit => hit.TargetId).ToArray();

            Assert.That(hits, Does.Contain("front"));
            Assert.That(hits, Does.Contain("apex"));
            Assert.That(hits.Contains("side"), Is.EqualTo(includesSide));
            Assert.That(hits.Contains("back"), Is.EqualTo(includesBack));
        }

        [Test]
        public void NonUnitDirectionAndVerySmallDirectionAreEquivalent()
        {
            var targets = new[] { Candidate("edge", 1, 1), Candidate("behind", -1, -1) };
            var unit = AreaDamageRequest.Sector(FirstApplication, Attacker, default, 5, 1, new WorldPoint2D(1, 0), 90);
            var small = AreaDamageRequest.Sector(FirstApplication, Attacker, default, 5, 1, new WorldPoint2D(float.Epsilon, 0), 90);
            Assert.That(AreaDamageSelector.FindTargets(unit, targets).Select(hit => hit.TargetId), Is.EqualTo(new[] { "edge" }));
            Assert.That(AreaDamageSelector.FindTargets(small, targets).Select(hit => hit.TargetId), Is.EqualTo(new[] { "edge" }));
        }

        [Test]
        public void DuplicateCollidersProduceOneRequestPerApplicationAndTarget()
        {
            TargetCandidate duplicate = Candidate("enemy", 1, 0);
            var candidates = new[] { duplicate, duplicate, Candidate("Enemy", 0, 1), duplicate };
            var first = AreaDamageRequest.Circle(FirstApplication, Attacker, default, 2, 5);
            var next = AreaDamageRequest.Circle(NextApplication, Attacker, default, 2, 5);

            IReadOnlyList<DamageRequest> firstHits = AreaDamageSelector.FindTargets(first, candidates);
            IReadOnlyList<DamageRequest> nextHits = AreaDamageSelector.FindTargets(next, candidates);
            Assert.That(firstHits.Select(hit => hit.TargetId), Is.EqualTo(new[] { "enemy", "Enemy" }));
            Assert.That(nextHits.Select(hit => hit.TargetId), Is.EqualTo(new[] { "enemy", "Enemy" }));
            Assert.That(firstHits[0].ApplicationId, Is.EqualTo(FirstApplication));
            Assert.That(nextHits[0].ApplicationId, Is.EqualTo(NextApplication));
        }

        [Test]
        public void ZeroRadiusAndDamageKeepApexAndDoNotApplyDamage()
        {
            AreaDamageRequest attack = AreaDamageRequest.Circle(FirstApplication, Attacker, default, 0, 0);
            IReadOnlyList<DamageRequest> hits = AreaDamageSelector.FindTargets(attack, new[]
            {
                Candidate("apex", 0, 0),
                Candidate("far", 0.01f, 0)
            });

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0].TargetId, Is.EqualTo("apex"));
            Assert.That(hits[0].Amount, Is.Zero);
        }

        [TestCaseSource(nameof(InvalidAttacks))]
        public void MalformedAttackReturnsNoDamageRequests(AreaDamageRequest attack)
        {
            Assert.That(AreaDamageSelector.FindTargets(attack, new[] { Candidate("enemy", 0, 0) }), Is.Empty);
        }

        [Test]
        public void EmptyAndNullCandidateListsHaveDefinedBehavior()
        {
            AreaDamageRequest attack = AreaDamageRequest.Circle(FirstApplication, Attacker, default, 2, 1);
            Assert.That(AreaDamageSelector.FindTargets(attack, Array.Empty<TargetCandidate>()), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => AreaDamageSelector.FindTargets(attack, null!));
        }

        private static IEnumerable<TestCaseData> InvalidAttacks()
        {
            var direction = new WorldPoint2D(1, 0);
            yield return Invalid(default, "UnspecifiedShape");
            yield return Invalid(AreaDamageRequest.Circle(Guid.Empty, Attacker, default, 2, 1), "MissingApplication");
            yield return Invalid(AreaDamageRequest.Circle(FirstApplication, " ", default, 2, 1), "MissingAttacker");
            yield return Invalid(AreaDamageRequest.Circle(FirstApplication, Attacker, new WorldPoint2D(float.NaN, 0), 2, 1), "NonFiniteOrigin");
            yield return Invalid(AreaDamageRequest.Circle(FirstApplication, Attacker, default, -1, 1), "NegativeRadius");
            yield return Invalid(AreaDamageRequest.Circle(FirstApplication, Attacker, default, float.PositiveInfinity, 1), "NonFiniteRadius");
            yield return Invalid(AreaDamageRequest.Circle(FirstApplication, Attacker, default, 2, -1), "NegativeAmount");
            yield return Invalid(AreaDamageRequest.Circle(FirstApplication, Attacker, default, 2, float.NaN), "NonFiniteAmount");
            yield return Invalid(AreaDamageRequest.Sector(FirstApplication, Attacker, default, 2, 1, default, 90), "ZeroDirection");
            yield return Invalid(AreaDamageRequest.Sector(FirstApplication, Attacker, default, 2, 1, new WorldPoint2D(0, float.NaN), 90), "NonFiniteDirection");
            yield return Invalid(AreaDamageRequest.Sector(FirstApplication, Attacker, default, 2, 1, direction, -1), "NegativeAngle");
            yield return Invalid(AreaDamageRequest.Sector(FirstApplication, Attacker, default, 2, 1, direction, 361), "OversizedAngle");
            yield return Invalid(AreaDamageRequest.Sector(FirstApplication, Attacker, default, 2, 1, direction, float.NaN), "NonFiniteAngle");
        }

        private static TestCaseData Invalid(AreaDamageRequest attack, string reason)
            => new TestCaseData(attack).SetName($"MalformedAttackReturnsNoDamageRequests({reason})");

        private static TargetCandidate Candidate(
            string targetId, float x, float y, bool isAlive = true, bool isAttackable = true)
            => new TargetCandidate(targetId, new WorldPoint2D(x, y), isAlive, isAttackable);
    }
}
