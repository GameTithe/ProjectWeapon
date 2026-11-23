#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.WeaponDraw.Tests
{
    public sealed class WeightedWeaponDrawTests
    {
        [TestCase(0, 1)]
        [TestCase(4999, 1)]
        [TestCase(5000, 2)]
        [TestCase(7999, 2)]
        [TestCase(8000, 3)]
        [TestCase(9499, 3)]
        [TestCase(9500, 4)]
        [TestCase(9899, 4)]
        [TestCase(9900, 5)]
        [TestCase(9999, 5)]
        public void DeterminesGradeAtEachHalfOpenBoundary(int roll, int expectedGrade)
        {
            var fixture = new DrawFixture();
            WeaponDrawResult result = Draw(fixture.Create(), roll, 0, out int[] bounds);
            Assert.That(result.Grade, Is.EqualTo(expectedGrade));
            Assert.That(result.DefinitionId, Is.EqualTo(fixture.Pools[expectedGrade - 1][0]));
            Assert.That(bounds, Is.EqualTo(new[] { 10000, fixture.Pools[expectedGrade - 1].Length }));
        }

        [Test]
        public void FullGradeRollSpacePreservesTheExactDraftWeights()
        {
            var fixture = new DrawFixture();
            WeightedWeaponDraw draw = fixture.Create();
            var counts = new int[5];
            for (int roll = 0; roll < 10000; roll++)
            {
                WeaponDrawResult result = draw.Draw(exclusive => exclusive == 10000 ? roll : 0);
                counts[result.Grade - 1]++;
            }

            Assert.That(counts, Is.EqualTo(new[] { 5000, 3000, 1500, 400, 100 }));
        }

        [TestCase(0, 5)]
        [TestCase(5000, 5)]
        [TestCase(8000, 5)]
        [TestCase(9500, 3)]
        [TestCase(9900, 3)]
        public void EachIntegerWithinSelectedPoolChoosesOneDifferentWeapon(int gradeRoll, int poolSize)
        {
            var fixture = new DrawFixture();
            WeightedWeaponDraw draw = fixture.Create();
            var selected = new HashSet<WeaponDefinitionId>();
            int grade = 0;

            for (int weaponRoll = 0; weaponRoll < poolSize; weaponRoll++)
            {
                WeaponDrawResult result = Draw(draw, gradeRoll, weaponRoll, out int[] bounds);
                Assert.That(bounds, Is.EqualTo(new[] { 10000, poolSize }));
                Assert.That(selected.Add(result.DefinitionId), Is.True);
                grade = result.Grade;
            }

            Assert.That(selected, Is.EquivalentTo(fixture.Pools[grade - 1]));
        }

        [Test]
        public void OrderOfInputIdsDoesNotChangeADeterministicSelection()
        {
            var fixture = new DrawFixture();
            var draw = fixture.Create();
            Array.Reverse(fixture.Pools[3]);
            var reversed = fixture.Create();

            for (int index = 0; index < 3; index++)
                Assert.That(Draw(reversed, 9500, index, out _).DefinitionId,
                    Is.EqualTo(Draw(draw, 9500, index, out _).DefinitionId));
        }

        [Test]
        public void ValidatedTableKeepsItsOwnWeightAndPoolSnapshots()
        {
            var fixture = new DrawFixture();
            WeaponDefinitionId original = fixture.Pools[0][0];
            var draw = fixture.Create();

            fixture.Weights[0] = 0;
            fixture.Pools[0][0] = default;
            fixture.Catalog.Clear();

            Assert.That(Draw(draw, 0, 0, out _).DefinitionId, Is.EqualTo(original));
        }

        [Test]
        public void WeightInputsCanDisableGradesWithoutShiftingTheRemainingGradeIds()
        {
            var fixture = new DrawFixture();
            int[] weights = { 0, 9999, 0, 0, 1 };
            var draw = new WeightedWeaponDraw(weights, fixture.Pools, fixture.Catalog);

            Assert.That(Draw(draw, 0, 0, out _).Grade, Is.EqualTo(2));
            Assert.That(Draw(draw, 9998, 0, out _).Grade, Is.EqualTo(2));
            Assert.That(Draw(draw, 9999, 0, out _).Grade, Is.EqualTo(5));
        }

        [TestCase(-1)]
        [TestCase(10000)]
        public void InvalidGradeRollFailsWithoutRequestingASecondRoll(int gradeRoll)
        {
            int calls = 0;
            var draw = new DrawFixture().Create();
            Assert.Throws<InvalidOperationException>(() => draw.Draw(_ =>
            {
                calls++;
                return gradeRoll;
            }));
            Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void InvalidWeaponRollFailsAfterExactlyTwoRequests(int weaponRoll)
        {
            var draw = new DrawFixture().Create();
            int calls = 0;
            Assert.Throws<InvalidOperationException>(() => draw.Draw(bound =>
            {
                calls++;
                if (calls == 1)
                {
                    Assert.That(bound, Is.EqualTo(10000));
                    return 9900;
                }

                Assert.That(bound, Is.EqualTo(3));
                return weaponRoll;
            }));
            Assert.That(calls, Is.EqualTo(2));
        }

        [TestCase(4999)]
        [TestCase(5001)]
        [TestCase(-1)]
        [TestCase(10001)]
        public void RejectsWrongWeightSumsAndOutOfRangeValues(int gradeOneWeight)
        {
            var fixture = new DrawFixture();
            fixture.Weights[0] = gradeOneWeight;
            Assert.Throws<ArgumentException>(() => fixture.Create());
        }

        [Test]
        public void RejectsMissingWeightsAndGradeGroups()
        {
            var fixture = new DrawFixture();
            Assert.Throws<ArgumentException>(() => new WeightedWeaponDraw(Array.Empty<int>(), fixture.Pools, fixture.Catalog));
            Assert.Throws<ArgumentException>(() => new WeightedWeaponDraw(fixture.Weights,
                Array.Empty<IReadOnlyList<WeaponDefinitionId>>(), fixture.Catalog));
            fixture.Pools[0] = null!;
            Assert.Throws<ArgumentException>(() => fixture.Create());
            fixture.Pools[0] = new[] { new WeaponDefinitionId("only-one") };
            Assert.Throws<ArgumentException>(() => fixture.Create());
        }

        [Test]
        public void RejectsInvalidIdMissingCatalogReferenceAndWrongCatalogGrade()
        {
            var fixture = new DrawFixture();
            WeaponDefinitionId original = fixture.Pools[0][0];
            fixture.Pools[0][0] = default;
            Assert.Throws<ArgumentException>(() => fixture.Create());

            fixture.Pools[0][0] = new WeaponDefinitionId("unknown");
            Assert.That(Assert.Throws<ArgumentException>(() => fixture.Create())!.Message, Does.Contain("missing from the catalog"));

            fixture.Pools[0][0] = original;
            fixture.Catalog[original] = 5;
            Assert.That(Assert.Throws<ArgumentException>(() => fixture.Create())!.Message, Does.Contain("belongs to grade 5"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RejectsDuplicateDefinitionIdsWithinOrAcrossGrades(bool acrossGrades)
        {
            var fixture = new DrawFixture();
            if (acrossGrades)
                fixture.Pools[1][0] = fixture.Pools[0][0];
            else
                fixture.Pools[0][1] = fixture.Pools[0][0];

            Assert.That(Assert.Throws<ArgumentException>(() => fixture.Create())!.Message, Does.Contain("appears more than once"));
        }

        [Test]
        public void RejectsMissingSources()
        {
            var fixture = new DrawFixture();
            Assert.Throws<ArgumentNullException>(() => new WeightedWeaponDraw(null!, fixture.Pools, fixture.Catalog));
            Assert.Throws<ArgumentNullException>(() => new WeightedWeaponDraw(fixture.Weights, null!, fixture.Catalog));
            Assert.Throws<ArgumentNullException>(() => new WeightedWeaponDraw(fixture.Weights, fixture.Pools, null!));
            Assert.Throws<ArgumentNullException>(() => fixture.Create().Draw(null!));
        }

        private static WeaponDrawResult Draw(WeightedWeaponDraw draw, int gradeRoll, int weaponRoll, out int[] bounds)
        {
            var observations = new List<int>();
            WeaponDrawResult result = draw.Draw(bound =>
            {
                observations.Add(bound);
                return observations.Count == 1 ? gradeRoll : weaponRoll;
            });
            bounds = observations.ToArray();
            return result;
        }

        private sealed class DrawFixture
        {
            private static readonly int[] PoolSizes = { 5, 5, 5, 3, 3 };

            public DrawFixture()
            {
                Pools = new WeaponDefinitionId[PoolSizes.Length][];
                for (int gradeIndex = 0; gradeIndex < PoolSizes.Length; gradeIndex++)
                {
                    int grade = gradeIndex + 1;
                    Pools[gradeIndex] = Enumerable.Range(0, PoolSizes[gradeIndex])
                        .Select(index => new WeaponDefinitionId($"test-{grade}-{index}")).ToArray();

                    foreach (WeaponDefinitionId id in Pools[gradeIndex])
                        Catalog.Add(id, grade);
                }
            }

            public int[] Weights { get; } = WeightedWeaponDraw.DraftGradeWeightsBasisPoints.ToArray();
            public WeaponDefinitionId[][] Pools { get; }
            public Dictionary<WeaponDefinitionId, int> Catalog { get; } = new Dictionary<WeaponDefinitionId, int>();

            public WeightedWeaponDraw Create() => new WeightedWeaponDraw(Weights, Pools, Catalog);
        }
    }
}
