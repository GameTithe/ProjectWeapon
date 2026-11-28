#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.Art.Wanderer
{
    public enum WandererFacing { South, East, North, West }
    public enum WandererMotion { Idle, Walk, Dodge }

    public sealed class WandererLocomotionSet : ScriptableObject
    {
        [SerializeField] private Sprite[] idle = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] walk = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] dodge = Array.Empty<Sprite>();

        public static int FramesPerDirection(WandererMotion motion) => motion switch
        {
            WandererMotion.Idle => 2,
            WandererMotion.Walk => 6,
            WandererMotion.Dodge => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(motion))
        };

        public Sprite? GetFrame(WandererMotion motion, WandererFacing facing, int frame)
        {
            int count = FramesPerDirection(motion);
            if ((uint)frame >= count || (uint)facing >= 4)
                throw new ArgumentOutOfRangeException(frame < 0 || frame >= count ? nameof(frame) : nameof(facing));

            Sprite[] frames = motion switch
            {
                WandererMotion.Walk => walk,
                WandererMotion.Dodge => dodge,
                _ => idle
            };
            int index = (int)facing * count + frame;
            return index < frames.Length ? frames[index] : null;
        }
    }
}
