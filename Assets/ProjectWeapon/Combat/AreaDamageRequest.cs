#nullable enable

using System;

namespace ProjectWeapon.Combat
{
    public enum DamageAreaShape
    {
        Unspecified,
        Circle,
        Sector
    }

    public readonly struct AreaDamageRequest
    {
        private AreaDamageRequest(Guid applicationId, string attackerId, WorldPoint2D origin,
            DamageAreaShape shape, float radius, float amount, WorldPoint2D direction, float angleDegrees)
        {
            ApplicationId = applicationId;
            AttackerId = attackerId;
            Origin = origin;
            Shape = shape;
            Radius = radius;
            Amount = amount;
            Direction = direction;
            AngleDegrees = angleDegrees;
        }

        public Guid ApplicationId { get; }
        public string? AttackerId { get; }
        public WorldPoint2D Origin { get; }
        public DamageAreaShape Shape { get; }
        public float Radius { get; }
        public float Amount { get; }
        public WorldPoint2D Direction { get; }
        public float AngleDegrees { get; }

        public static AreaDamageRequest Circle(Guid applicationId, string attackerId, WorldPoint2D origin,
            float radius, float amount)
            => new AreaDamageRequest(applicationId, attackerId, origin, DamageAreaShape.Circle,
                radius, amount, default, 0);

        public static AreaDamageRequest Sector(Guid applicationId, string attackerId, WorldPoint2D origin,
            float radius, float amount, WorldPoint2D direction, float angleDegrees)
            => new AreaDamageRequest(applicationId, attackerId, origin, DamageAreaShape.Sector,
                radius, amount, direction, angleDegrees);

        internal bool IsValid => ApplicationId != Guid.Empty && !string.IsNullOrWhiteSpace(AttackerId)
            && Origin.IsFinite && float.IsFinite(Radius) && Radius >= 0 && float.IsFinite(Amount) && Amount >= 0
            && HasValidShape();

        private bool HasValidShape()
        {
            if (Shape == DamageAreaShape.Circle)
                return true;

            return Shape == DamageAreaShape.Sector && Direction.IsFinite
                && (Direction.X != 0 || Direction.Y != 0)
                && float.IsFinite(AngleDegrees) && AngleDegrees >= 0 && AngleDegrees <= 360;
        }
    }

    public readonly struct DamageRequest
    {
        internal DamageRequest(Guid applicationId, string attackerId, string targetId, float amount)
        {
            ApplicationId = applicationId;
            AttackerId = attackerId;
            TargetId = targetId;
            Amount = amount;
        }

        public Guid ApplicationId { get; }
        public string AttackerId { get; }
        public string TargetId { get; }
        public float Amount { get; }
    }
}
