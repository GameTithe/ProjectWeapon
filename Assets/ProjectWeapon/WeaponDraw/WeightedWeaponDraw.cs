#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Balance;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.WeaponDraw
{
    public readonly struct WeaponDrawResult
    {
        internal WeaponDrawResult(int grade, WeaponDefinitionId definitionId)
        {
            Grade = grade;
            DefinitionId = definitionId;
        }

        public int Grade { get; }
        public WeaponDefinitionId DefinitionId { get; }
    }

    public sealed class WeightedWeaponDraw
    {
        private static readonly int[] RequiredPoolSizes = { 5, 5, 5, 3, 3 };
        private readonly int[] upperGradeBounds;
        private readonly WeaponDefinitionId[][] pools;

        public static IReadOnlyList<int> DraftGradeWeightsBasisPoints { get; } =
            Array.AsReadOnly(new[] { 5000, 3000, 1500, 400, 100 });

        /// <summary>Validates and copies the five grade groups in ascending grade order.</summary>
        public WeightedWeaponDraw(
            IReadOnlyList<int> weightsBasisPointsByGrade,
            IReadOnlyList<IReadOnlyList<WeaponDefinitionId>> poolsByGrade,
            IReadOnlyDictionary<WeaponDefinitionId, int> definitionGrades)
        {
            if (weightsBasisPointsByGrade == null)
                throw new ArgumentNullException(nameof(weightsBasisPointsByGrade));
            if (poolsByGrade == null)
                throw new ArgumentNullException(nameof(poolsByGrade));
            if (definitionGrades == null)
                throw new ArgumentNullException(nameof(definitionGrades));

            upperGradeBounds = BuildGradeBounds(weightsBasisPointsByGrade);
            pools = CopyPools(poolsByGrade, definitionGrades);
        }

        /// <summary>Reads a uniform roll in each requested range [0, upperBoundExclusive).</summary>
        public WeaponDrawResult Draw(Func<int, int> nextExclusive)
        {
            if (nextExclusive == null)
                throw new ArgumentNullException(nameof(nextExclusive));

            int gradeRoll = ReadRoll(nextExclusive, BalanceDistribution.RequiredTotalBasisPoints);
            int gradeIndex = 0;
            while (gradeRoll >= upperGradeBounds[gradeIndex])
                gradeIndex++;

            WeaponDefinitionId[] pool = pools[gradeIndex];
            int weaponIndex = ReadRoll(nextExclusive, pool.Length);
            return new WeaponDrawResult(gradeIndex + 1, pool[weaponIndex]);
        }

        private static int[] BuildGradeBounds(IReadOnlyList<int> weights)
        {
            if (weights.Count != RequiredPoolSizes.Length)
                throw new ArgumentException("Provide one weight for each of grades 1 through 5.", nameof(weights));

            var bounds = new int[RequiredPoolSizes.Length];
            int total = 0;
            for (int index = 0; index < bounds.Length; index++)
            {
                int weight = weights[index];
                if (weight < 0 || weight > BalanceDistribution.RequiredTotalBasisPoints)
                    throw new ArgumentException($"Grade {index + 1} weight must be between 0 and 10,000 basis points.",
                        nameof(weights));

                total += weight;
                bounds[index] = total;
            }

            if (total != BalanceDistribution.RequiredTotalBasisPoints)
                throw new ArgumentException($"Grade weights must total 10,000 basis points (got {total}).", nameof(weights));

            return bounds;
        }

        private static WeaponDefinitionId[][] CopyPools(
            IReadOnlyList<IReadOnlyList<WeaponDefinitionId>> source,
            IReadOnlyDictionary<WeaponDefinitionId, int> definitionGrades)
        {
            if (source.Count != RequiredPoolSizes.Length)
                throw new ArgumentException("Provide one pool for each of grades 1 through 5.", nameof(source));

            var seen = new HashSet<WeaponDefinitionId>();
            var result = new WeaponDefinitionId[RequiredPoolSizes.Length][];
            for (int gradeIndex = 0; gradeIndex < result.Length; gradeIndex++)
            {
                int grade = gradeIndex + 1;
                IReadOnlyList<WeaponDefinitionId> group = source[gradeIndex];
                if (group == null || group.Count != RequiredPoolSizes[gradeIndex])
                    throw new ArgumentException($"Grade {grade} must contain {RequiredPoolSizes[gradeIndex]} weapons.",
                        nameof(source));

                var copy = new WeaponDefinitionId[group.Count];
                for (int weaponIndex = 0; weaponIndex < copy.Length; weaponIndex++)
                {
                    WeaponDefinitionId id = group[weaponIndex];
                    if (!id.IsValid)
                        throw new ArgumentException($"Grade {grade} contains an invalid weapon definition ID.", nameof(source));

                    if (!seen.Add(id))
                        throw new ArgumentException($"Weapon definition ID '{id}' appears more than once.", nameof(source));

                    if (!definitionGrades.TryGetValue(id, out int catalogGrade))
                        throw new ArgumentException($"Weapon definition '{id}' is missing from the catalog.", nameof(source));

                    if (catalogGrade != grade)
                        throw new ArgumentException($"Weapon definition '{id}' belongs to grade {catalogGrade}, not grade {grade}.",
                            nameof(source));

                    copy[weaponIndex] = id;
                }

                Array.Sort(copy, (left, right) => StringComparer.Ordinal.Compare(left.Value, right.Value));
                result[gradeIndex] = copy;
            }

            return result;
        }

        private static int ReadRoll(Func<int, int> nextExclusive, int upperBoundExclusive)
        {
            int roll = nextExclusive(upperBoundExclusive);
            if ((uint)roll >= (uint)upperBoundExclusive)
                throw new InvalidOperationException($"Random source returned {roll} outside [0, {upperBoundExclusive}).");

            return roll;
        }
    }
}
