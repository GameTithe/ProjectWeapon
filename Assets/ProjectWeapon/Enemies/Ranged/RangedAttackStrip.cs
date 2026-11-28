#nullable enable

using System;
using ProjectWeapon.Combat;

namespace ProjectWeapon.Enemies
{
    /// <summary>An immutable forward rectangle shared by the warning and impact.</summary>
    public readonly struct RangedAttackStrip
    {
        private readonly double directionX;
        private readonly double directionY;

        internal RangedAttackStrip(WorldPoint2D origin, WorldPoint2D aim, float length, float width)
        {
            Origin = origin;
            Length = length;
            Width = width;
            double dx = (double)aim.X - origin.X;
            double dy = (double)aim.Y - origin.Y;
            double magnitude = Math.Sqrt(dx * dx + dy * dy);
            directionX = magnitude == 0d ? 0d : dx / magnitude;
            directionY = magnitude == 0d ? -1d : dy / magnitude;
        }

        public WorldPoint2D Origin { get; }
        public WorldPoint2D Direction => new WorldPoint2D((float)directionX, (float)directionY);
        public float Length { get; }
        public float Width { get; }

        /// <summary>Tests a world point against inclusive front, back and side boundaries.</summary>
        public bool Contains(WorldPoint2D point)
        {
            if (Length <= 0f || Width <= 0f || !point.IsFinite)
                return false;

            double dx = (double)point.X - Origin.X;
            double dy = (double)point.Y - Origin.Y;
            double depth = dx * directionX + dy * directionY;
            double side = dy * directionX - dx * directionY;
            return depth >= 0d && depth <= Length && Math.Abs(side) <= Width / 2d;
        }

        public WorldPoint2D GetCorner(bool farEnd, bool leftSide)
        {
            double depth = farEnd ? Length : 0d;
            double side = leftSide ? Width / 2d : -Width / 2d;
            return new WorldPoint2D(
                (float)(Origin.X + directionX * depth - directionY * side),
                (float)(Origin.Y + directionY * depth + directionX * side));
        }
    }
}
