#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;

namespace ProjectWeapon.PrisonGuard
{
    /// <summary>The immutable world-space rectangle from the accepted windup.</summary>
    public readonly struct PrisonGuardSlam
    {
        private readonly double directionX;
        private readonly double directionY;

        internal PrisonGuardSlam(Guid damageId, WorldPoint2D origin, WorldPoint2D aim, TimeSpan now,
            PrisonGuardSettings settings)
        {
            DamageId = damageId;
            Origin = origin;
            Length = settings.AttackLength;
            Width = settings.AttackWidth;
            Damage = settings.AttackDamage;
            StartsAt = now;
            HitsAt = now + settings.Windup;
            ReadyAt = HitsAt + settings.Recovery;
            double x = (double)aim.X - origin.X;
            double y = (double)aim.Y - origin.Y;
            double distance = Math.Sqrt(x * x + y * y);
            directionX = x / distance;
            directionY = y / distance;
        }

        public Guid DamageId { get; }
        public WorldPoint2D Origin { get; }
        public WorldPoint2D Direction => new WorldPoint2D((float)directionX, (float)directionY);
        public float Length { get; }
        public float Width { get; }
        public float Damage { get; }
        public TimeSpan StartsAt { get; }
        public TimeSpan HitsAt { get; }
        internal TimeSpan ReadyAt { get; }

        public bool Contains(WorldPoint2D point)
        {
            double x = (double)point.X - Origin.X;
            double y = (double)point.Y - Origin.Y;
            double forward = x * directionX + y * directionY;
            double side = y * directionX - x * directionY;
            return point.IsFinite && Length > 0f && forward >= 0d && forward <= Length && Math.Abs(side) <= Width / 2d;
        }

        public WorldPoint2D GetCorner(bool far, bool left)
        {
            double depth = far ? Length : 0d;
            double side = left ? Width / 2d : -Width / 2d;
            return new WorldPoint2D((float)(Origin.X + depth * directionX - side * directionY),
                (float)(Origin.Y + depth * directionY + side * directionX));
        }
    }

    public readonly struct PrisonGuardImpact
    {
        internal PrisonGuardImpact(PrisonGuardSlam attack, IReadOnlyList<DamageRequest> hits)
        {
            Attack = attack;
            Hits = hits;
        }

        public PrisonGuardSlam Attack { get; }
        public IReadOnlyList<DamageRequest> Hits { get; }
    }

    public readonly struct PrisonGuardDefeat
    {
        internal PrisonGuardDefeat(RunId runId, TimeSpan effectiveAt)
        {
            RunId = runId;
            EffectiveAt = effectiveAt;
        }

        public RunId RunId { get; }
        public int ZoneId => PrisonGuardActor.ZoneId;
        public string BossId => PrisonGuardActor.BossId;
        public TimeSpan EffectiveAt { get; }
    }
}
