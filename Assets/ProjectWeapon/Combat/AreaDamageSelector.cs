#nullable enable

using System;
using System.Collections.Generic;

namespace ProjectWeapon.Combat
{
    public static class AreaDamageSelector
    {
        public static IReadOnlyList<DamageRequest> FindTargets(
            AreaDamageRequest attack, IReadOnlyList<TargetCandidate> candidates)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            if (!attack.IsValid || candidates.Count == 0)
                return Array.Empty<DamageRequest>();

            double radiusSquared = (double)attack.Radius * attack.Radius;
            double halfAngleRadians = attack.AngleDegrees * (Math.PI / 360.0);
            var hits = new List<DamageRequest>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < candidates.Count; i++)
            {
                TargetCandidate candidate = candidates[i];
                if (!candidate.IsEligible || StringComparer.Ordinal.Equals(attack.AttackerId, candidate.TargetId))
                    continue;

                if (attack.Origin.SquaredDistanceTo(candidate.Position) > radiusSquared)
                    continue;

                if (attack.Shape == DamageAreaShape.Sector && !IsInsideAngle(attack, candidate.Position, halfAngleRadians))
                    continue;

                if (seen.Add(candidate.TargetId!))
                    hits.Add(new DamageRequest(attack.ApplicationId, attack.AttackerId!, candidate.TargetId!, attack.Amount));
            }

            return hits;
        }

        private static bool IsInsideAngle(AreaDamageRequest attack, WorldPoint2D target, double halfAngleRadians)
        {
            double offsetX = (double)target.X - attack.Origin.X;
            double offsetY = (double)target.Y - attack.Origin.Y;
            if (offsetX == 0 && offsetY == 0)
                return true;

            double directionX = attack.Direction.X;
            double directionY = attack.Direction.Y;
            double cross = directionX * offsetY - directionY * offsetX;
            double dot = directionX * offsetX + directionY * offsetY;
            // Both products scale together, so the angle is the same as with a unit direction.
            return Math.Atan2(Math.Abs(cross), dot) <= halfAngleRadians;
        }
    }
}
