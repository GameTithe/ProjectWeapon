#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.Art.Wanderer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class WandererLocomotionView : MonoBehaviour
    {
        [SerializeField] private WandererLocomotionSet? poses;

        private SpriteRenderer? spriteRenderer;
        private double elapsed;

        public WandererFacing Facing { get; private set; } = WandererFacing.South;
        public WandererMotion Motion { get; private set; } = WandererMotion.Idle;

        private void Awake() => spriteRenderer = GetComponent<SpriteRenderer>();
        private void OnEnable() => RenderCurrentFrame();

        public void Configure(WandererLocomotionSet frames)
        {
            poses = frames != null ? frames : throw new ArgumentNullException(nameof(frames));
            spriteRenderer ??= GetComponent<SpriteRenderer>();
            elapsed = 0;
            RenderCurrentFrame();
        }

        /// <summary>Pass the frame's accepted move and active dodge state after sampling player movement.</summary>
        public void Present(Vector2 move, bool dodging, Vector2 dodgeDirection, float deltaSeconds)
        {
            if (!isActiveAndEnabled)
                return;

            WandererMotion nextMotion = dodging ? WandererMotion.Dodge
                : HasDirection(move) ? WandererMotion.Walk : WandererMotion.Idle;
            Vector2 direction = dodging ? dodgeDirection : move;
            WandererFacing nextFacing = HasDirection(direction) ? ReadFacing(direction) : Facing;
            if (nextMotion != Motion || nextFacing != Facing)
            {
                Motion = nextMotion;
                Facing = nextFacing;
                elapsed = 0;
            }
            else if (float.IsFinite(deltaSeconds) && deltaSeconds > 0)
            {
                elapsed += deltaSeconds;
            }

            RenderCurrentFrame();
        }

        private WandererFacing ReadFacing(Vector2 direction)
        {
            float x = Mathf.Abs(direction.x);
            float y = Mathf.Abs(direction.y);
            bool horizontal = x > y || (x == y && Facing is WandererFacing.East or WandererFacing.West);
            if (horizontal)
                return direction.x > 0 ? WandererFacing.East : WandererFacing.West;

            return direction.y > 0 ? WandererFacing.North : WandererFacing.South;
        }

        private static bool HasDirection(Vector2 value)
            => float.IsFinite(value.x) && float.IsFinite(value.y) && (value.x != 0 || value.y != 0);

        private void RenderCurrentFrame()
        {
            if (poses == null || spriteRenderer == null)
                return;

            int count = WandererLocomotionSet.FramesPerDirection(Motion);
            double framesPerSecond = Motion switch
            {
                WandererMotion.Walk => 12,
                WandererMotion.Dodge => 20,
                _ => 2
            };
            double advancing = Math.Floor(elapsed * framesPerSecond);
            int frame = Motion == WandererMotion.Dodge
                ? (int)Math.Min(count - 1, advancing)
                : (int)(advancing % count);
            spriteRenderer.sprite = poses.GetFrame(Motion, Facing, frame);
        }

        private void OnDisable()
        {
            Motion = WandererMotion.Idle;
            elapsed = 0;
            RenderCurrentFrame();
        }
    }
}
