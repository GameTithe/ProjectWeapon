#nullable enable

using System;
using System.Collections.Generic;

namespace ProjectWeapon.Combat
{
    public static class TargetSelector
    {
        public static TargetCandidate? FindNearest(
            WorldPoint2D origin, float range, IReadOnlyList<TargetCandidate> candidates)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            if (!origin.IsFinite || !float.IsFinite(range) || range < 0)
                return null;

            TargetCandidate? nearest = null;
            double nearestDistanceSquared = (double)range * range;
            for (int i = 0; i < candidates.Count; i++)
            {
                TargetCandidate candidate = candidates[i];
                if (!candidate.IsEligible)
                    continue;

                double distanceSquared = origin.SquaredDistanceTo(candidate.Position);
                if (distanceSquared > nearestDistanceSquared)
                    continue;

                if (nearest.HasValue && distanceSquared == nearestDistanceSquared
                    && StringComparer.Ordinal.Compare(candidate.TargetId, nearest.Value.TargetId) >= 0)
                    continue;

                nearest = candidate;
                nearestDistanceSquared = distanceSquared;
            }

            return nearest;
        }
    }
}
