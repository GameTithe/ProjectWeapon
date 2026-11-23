#nullable enable

using System;
using System.Collections.Generic;

namespace ProjectWeapon.Inventory
{
    public enum WeaponInstanceIssueKind
    {
        MissingInstance,
        InvalidInstanceData,
        DuplicateInstanceId
    }

    public readonly struct WeaponInstanceIssue
    {
        internal WeaponInstanceIssue(WeaponInstanceIssueKind kind, int index, WeaponInstanceId instanceId)
        {
            Kind = kind;
            Index = index;
            InstanceId = instanceId;
        }

        public WeaponInstanceIssueKind Kind { get; }
        public int Index { get; }
        public WeaponInstanceId InstanceId { get; }
    }

    public static class WeaponInstanceValidator
    {
        public static IReadOnlyList<WeaponInstanceIssue> Validate(IReadOnlyList<WeaponInstance?> instances)
        {
            if (instances == null)
                throw new ArgumentNullException(nameof(instances));

            var issues = new List<WeaponInstanceIssue>();
            var seen = new HashSet<WeaponInstanceId>();
            for (int index = 0; index < instances.Count; index++)
            {
                WeaponInstance? instance = instances[index];
                if (instance == null)
                {
                    issues.Add(new WeaponInstanceIssue(WeaponInstanceIssueKind.MissingInstance, index, default));
                    continue;
                }

                if (!instance.IsValid)
                    issues.Add(new WeaponInstanceIssue(WeaponInstanceIssueKind.InvalidInstanceData, index, instance.Id));

                if (instance.Id.IsValid && !seen.Add(instance.Id))
                    issues.Add(new WeaponInstanceIssue(WeaponInstanceIssueKind.DuplicateInstanceId, index, instance.Id));
            }

            return issues;
        }
    }
}
