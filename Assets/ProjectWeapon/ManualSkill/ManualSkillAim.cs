#nullable enable

using ProjectWeapon.Combat;

namespace ProjectWeapon.ManualSkill
{
    public enum ManualSkillAimKind
    {
        Unspecified,
        Self,
        Direction,
        Enemy,
        Ground
    }

    /// <summary>A sampled aim from the input adapter; enemy positions are resolved when the skill starts.</summary>
    public readonly struct ManualSkillAim
    {
        private ManualSkillAim(ManualSkillAimKind kind, WorldPoint2D vector, string? targetId)
        {
            Kind = kind;
            Vector = vector;
            TargetId = targetId;
        }

        public ManualSkillAimKind Kind { get; }
        public WorldPoint2D Vector { get; }
        public string? TargetId { get; }

        public static ManualSkillAim AtSelf() => new ManualSkillAim(ManualSkillAimKind.Self, default, null);
        public static ManualSkillAim Along(WorldPoint2D direction)
            => new ManualSkillAim(ManualSkillAimKind.Direction, direction, null);
        public static ManualSkillAim AtEnemy(string targetId)
            => new ManualSkillAim(ManualSkillAimKind.Enemy, default, targetId);
        public static ManualSkillAim AtGround(WorldPoint2D position)
            => new ManualSkillAim(ManualSkillAimKind.Ground, position, null);
    }
}
