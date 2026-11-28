#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ProjectWeapon.DpsQA
{
    public readonly struct DpsWeaponResult
    {
        internal DpsWeaponResult(string id, string source, int grade, float range, TimeSpan cooldown, int basicHits,
            int skillHits, double basicDps, double combinedDps)
        {
            Id = id;
            Source = source;
            Grade = grade;
            Range = range;
            Cooldown = cooldown;
            BasicHitsPerMode = basicHits;
            SkillHits = skillHits;
            BasicDps = basicDps;
            CombinedDps = combinedDps;
        }

        public string Id { get; }
        public string Source { get; }
        public int Grade { get; }
        public float Range { get; }
        public TimeSpan Cooldown { get; }
        public int BasicHitsPerMode { get; }
        public int SkillHits { get; }
        public double BasicDps { get; }
        public double CombinedDps { get; }
    }

    public readonly struct DpsGradeResult
    {
        internal DpsGradeResult(int grade, IReadOnlyList<DpsWeaponResult> weapons)
        {
            Grade = grade;
            SampleCount = weapons.Count;
            BasicDps = weapons.Average(weapon => weapon.BasicDps);
            CombinedDps = weapons.Average(weapon => weapon.CombinedDps);
        }

        public int Grade { get; }
        public int SampleCount { get; }
        public double BasicDps { get; }
        public double CombinedDps { get; }
    }

    public sealed class DpsQaReport
    {
        internal DpsQaReport(string conditions, DpsWeaponResult[] weapons)
        {
            Conditions = conditions;
            Weapons = Array.AsReadOnly(weapons);
            Grades = Array.AsReadOnly(weapons.GroupBy(weapon => weapon.Grade).OrderBy(group => group.Key)
                .Select(group => new DpsGradeResult(group.Key, group.ToArray())).ToArray());
            MissingGrades = Array.AsReadOnly(Enumerable.Range(1, 5)
                .Where(grade => Grades.All(result => result.Grade != grade)).ToArray());

            var violations = new List<string>();
            for (int index = 1; index < Grades.Count; index++)
            {
                DpsGradeResult low = Grades[index - 1];
                DpsGradeResult high = Grades[index];
                if (high.BasicDps <= low.BasicDps)
                    violations.Add($"Basic: grade {high.Grade} <= grade {low.Grade}");
                if (high.CombinedDps <= low.CombinedDps)
                    violations.Add($"With skill: grade {high.Grade} <= grade {low.Grade}");
            }
            Violations = violations.AsReadOnly();
        }

        public string Conditions { get; }
        public IReadOnlyList<DpsWeaponResult> Weapons { get; }
        public IReadOnlyList<DpsGradeResult> Grades { get; }
        public IReadOnlyList<int> MissingGrades { get; }
        public IReadOnlyList<string> Violations { get; }
        public bool Passes => MissingGrades.Count == 0 && Violations.Count == 0;

        public string Summary()
        {
            var text = new StringBuilder();
            text.AppendLine("QA DPS | baseline attacks + calibration fixtures");
            text.AppendLine(Conditions);
            text.AppendLine();
            text.AppendLine("Grade     Cases     Basic DPS     With fixed skill");
            foreach (DpsGradeResult grade in Grades)
                text.AppendLine(FormattableString.Invariant(
                    $"   {grade.Grade}           {grade.SampleCount}          {grade.BasicDps,6:F2}              {grade.CombinedDps,6:F2}"));
            text.AppendLine();
            if (MissingGrades.Count > 0)
                text.AppendLine("Missing grades: " + string.Join(", ", MissingGrades));
            foreach (string violation in Violations)
                text.AppendLine(violation);
            text.AppendLine(Passes ? "Fixture order: PASS" : "Fixture order: CHECK");
            return text.ToString();
        }

        public string DetailedLog()
        {
            var text = new StringBuilder(Summary());
            foreach (DpsWeaponResult weapon in Weapons)
            {
                text.AppendLine(FormattableString.Invariant(
                    $"{weapon.Id} [{weapon.Source}] g{weapon.Grade} range={weapon.Range} cooldown={weapon.Cooldown.TotalMilliseconds}ms"));
                text.AppendLine(FormattableString.Invariant(
                    $"  basic={weapon.BasicDps:F4} ({weapon.BasicHitsPerMode} hits/mode), withSkill={weapon.CombinedDps:F4} ({weapon.SkillHits} skill hits)"));
            }
            return text.ToString();
        }
    }
}
