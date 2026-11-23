#nullable enable

namespace ProjectWeapon.Combat
{
    public readonly struct WorldPoint2D
    {
        public WorldPoint2D(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }
        public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y);

        public double SquaredDistanceTo(WorldPoint2D other)
        {
            double dx = (double)X - other.X;
            double dy = (double)Y - other.Y;
            return dx * dx + dy * dy;
        }
    }

    /// <summary>World-space snapshot supplied by the Unity target adapter.</summary>
    public readonly struct TargetCandidate
    {
        public TargetCandidate(string targetId, WorldPoint2D position, bool isAlive, bool isAttackable)
        {
            TargetId = targetId;
            Position = position;
            IsAlive = isAlive;
            IsAttackable = isAttackable;
        }

        public string? TargetId { get; }
        public WorldPoint2D Position { get; }
        public bool IsAlive { get; }
        public bool IsAttackable { get; }
        public bool IsEligible => IsAlive && IsAttackable && Position.IsFinite && !string.IsNullOrWhiteSpace(TargetId);
    }
}
