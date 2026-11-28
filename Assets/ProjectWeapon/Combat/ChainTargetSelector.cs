#nullable enable

using System;
using System.Collections.Generic;

namespace ProjectWeapon.Combat
{
    public static class ChainTargetSelector
    {
        /// <summary>
        /// Selects additional targets of a single chain from the positions in the supplied snapshot.
        /// The original hit provides the first origin even if that hit killed its target.
        /// </summary>
        public static IReadOnlyList<TargetCandidate> FindAdditionalTargets(TargetCandidate initialHit,
            int maximumAdditionalHits, float maximumHopDistance, IReadOnlyList<TargetCandidate> candidates)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            if (maximumAdditionalHits <= 0 || !float.IsFinite(maximumHopDistance) || maximumHopDistance < 0
                || string.IsNullOrWhiteSpace(initialHit.TargetId) || !initialHit.Position.IsFinite)
                return Array.Empty<TargetCandidate>();

            var remaining = new List<TargetCandidate>(candidates.Count);
            foreach (TargetCandidate candidate in candidates)
            {
                if (!StringComparer.Ordinal.Equals(initialHit.TargetId, candidate.TargetId))
                    remaining.Add(candidate);
            }

            var selected = new List<TargetCandidate>(Math.Min(maximumAdditionalHits, remaining.Count));
            WorldPoint2D origin = initialHit.Position;
            while (selected.Count < maximumAdditionalHits && remaining.Count > 0)
            {
                TargetCandidate? next = TargetSelector.FindNearest(origin, maximumHopDistance, remaining);
                if (!next.HasValue)
                    break;

                TargetCandidate hit = next.Value;
                selected.Add(hit);
                origin = hit.Position;
                remaining.RemoveAll(candidate => StringComparer.Ordinal.Equals(hit.TargetId, candidate.TargetId));
            }

            return selected.AsReadOnly();
        }
    }
}
