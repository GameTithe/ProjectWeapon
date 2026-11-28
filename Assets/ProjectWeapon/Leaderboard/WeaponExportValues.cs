#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;

namespace ProjectWeapon.Leaderboard
{
    public sealed class WeaponExportValues
    {
        private readonly int[] valueByGrade;

        public WeaponExportValues(int grade1, int grade2, int grade3, int grade4, int grade5)
        {
            valueByGrade = new[] { grade1, grade2, grade3, grade4, grade5 };
            if (Array.Exists(valueByGrade, value => value < 0))
                throw new ArgumentException("Weapon export values cannot be negative.");
        }

        public static WeaponExportValues Prototype { get; } = new WeaponExportValues(10, 25, 60, 140, 300);

        /// <summary>Projects export value from the confirmed recovery of a single successful run.</summary>
        public long Calculate(RunResult run, SortieManifest manifest, IReadOnlyList<WeaponInstance> recovered,
            Func<WeaponDefinitionId, int> resolveGrade)
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            if (!run.Succeeded)
                throw new ArgumentException("Only successful extractions have an export value.", nameof(run));
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (manifest.RunId != run.RunId.Value)
                throw new ArgumentException("The sortie manifest belongs to another run.", nameof(manifest));
            if (recovered == null)
                throw new ArgumentNullException(nameof(recovered));
            if (resolveGrade == null)
                throw new ArgumentNullException(nameof(resolveGrade));

            var packedIds = new HashSet<WeaponInstanceId>();
            foreach (WeaponInstance item in manifest.Equipped)
                packedIds.Add(item.Id);

            var counted = new Dictionary<WeaponInstanceId, WeaponDefinitionId>();
            long total = 0;
            foreach (WeaponInstance item in recovered)
            {
                if (item == null || !item.IsValid)
                    throw new ArgumentException("A recovery contains an invalid weapon instance.", nameof(recovered));
                if (item.Provenance != WeaponProvenance.ExplorationLoot || packedIds.Contains(item.Id))
                    continue;
                if (item.Location.Kind != WeaponLocationKind.Warehouse)
                    throw new ArgumentException("A recovered weapon must have been settled to the warehouse.", nameof(recovered));

                WeaponDefinitionId definition = item.DefinitionId;
                if (counted.TryGetValue(item.Id, out WeaponDefinitionId previous))
                {
                    if (!previous.Equals(definition))
                        throw new ArgumentException("One recovered instance cannot have two definitions.", nameof(recovered));
                    continue;
                }
                counted.Add(item.Id, definition);
                int grade = resolveGrade(definition);
                if (grade < 1 || grade > valueByGrade.Length)
                    throw new ArgumentOutOfRangeException(nameof(resolveGrade), grade, "The weapon grade is unsupported.");
                total += valueByGrade[grade - 1];
            }
            return total;
        }
    }
}
