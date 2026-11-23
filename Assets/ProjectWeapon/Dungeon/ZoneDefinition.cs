using System;
using UnityEngine;

namespace ProjectWeapon.Dungeon
{
    [Serializable]
    public sealed class ZoneDefinition
    {
        public const int GradeCount = 5;
        public const int ProbabilityTotalBasisPoints = 10_000;

        [SerializeField] private int zoneId;
        [SerializeField] private string theme;
        [SerializeField] private int recommendedGrade;
        [SerializeField] private int minimumRewardGrade;
        [SerializeField] private int maximumRewardGrade;
        [SerializeField] private int timeLimitSeconds;
        [SerializeField] private int[] rewardProbabilityBasisPoints;

        public int ZoneId => zoneId;
        public string Theme => theme;
        public int RecommendedGrade => recommendedGrade;
        public int MinimumRewardGrade => minimumRewardGrade;
        public int MaximumRewardGrade => maximumRewardGrade;
        public int TimeLimitSeconds => timeLimitSeconds;

        public int GetRewardProbabilityBasisPoints(int grade)
        {
            if (grade < 1 || grade > GradeCount)
                throw new ArgumentOutOfRangeException(nameof(grade), grade, "Reward grade must be between 1 and 5.");

            return rewardProbabilityBasisPoints[grade - 1];
        }

        internal void Validate()
        {
            if (zoneId <= 0)
                throw Invalid("zone ID must be positive");
            if (string.IsNullOrWhiteSpace(theme))
                throw Invalid("theme must be present");
            if (recommendedGrade < 1 || recommendedGrade > GradeCount)
                throw Invalid("recommended grade must be between 1 and 5");
            if (minimumRewardGrade < 1 || maximumRewardGrade > GradeCount || minimumRewardGrade > maximumRewardGrade)
                throw Invalid("reward grade range must be ordered and within 1 to 5");
            if (timeLimitSeconds <= 0)
                throw Invalid("time limit must be positive");
            if (rewardProbabilityBasisPoints == null || rewardProbabilityBasisPoints.Length != GradeCount)
                throw Invalid("reward probabilities must contain exactly five grades");

            int total = 0;
            for (int grade = 1; grade <= GradeCount; grade++)
            {
                int probability = rewardProbabilityBasisPoints[grade - 1];
                if (probability < 0 || probability > ProbabilityTotalBasisPoints)
                    throw Invalid($"grade {grade} probability must be between 0 and 10,000 basis points");

                if ((grade < minimumRewardGrade || grade > maximumRewardGrade) && probability != 0)
                    throw Invalid($"grade {grade} probability must be zero outside the reward grade range");

                total += probability;
            }

            if (total != ProbabilityTotalBasisPoints)
                throw Invalid($"reward probabilities must total 10,000 basis points (got {total})");
        }

        private InvalidOperationException Invalid(string issue) =>
            new InvalidOperationException($"Zone {zoneId}: {issue}.");
    }
}
