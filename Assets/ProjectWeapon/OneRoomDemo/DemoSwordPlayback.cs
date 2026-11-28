#nullable enable

using System;
using ProjectWeapon.Art.Wanderer;
using ProjectWeapon.AutoAttack;
using UnityEngine;

namespace ProjectWeapon.OneRoomDemo
{
    public sealed class DemoSwordPlayback : MonoBehaviour
    {
        private const int FrameMilliseconds = 65;
        private static readonly Vector2[,] Hands =
        {
            { new Vector2(8, 25), new Vector2(9, 17), new Vector2(26, 26), new Vector2(23, 31) },
            { new Vector2(14, 24), new Vector2(18, 17), new Vector2(28, 22), new Vector2(26, 28) },
            { new Vector2(20, 28), new Vector2(23, 17), new Vector2(28, 14), new Vector2(11, 22) },
            { new Vector2(17, 24), new Vector2(13, 17), new Vector2(3, 22), new Vector2(5, 28) }
        };
        private static readonly float[,] Angle =
        {
            { -85, -110, -102, -90 },
            { -52, -10, -32, -54 },
            { 42, 66, 48, 70 },
            { 126, 106, 146, 130 }
        };

        [SerializeField] private SpriteRenderer actor = null!;
        [SerializeField] private SpriteRenderer weapon = null!;
        [SerializeField] private Sprite[] attacks = Array.Empty<Sprite>();

        private BasicAttackRequest? pending;
        private WandererFacing facing;
        private bool struck;

        public bool IsPlaying => pending.HasValue;

        public bool TryBegin(BasicAttackRequest request, WandererFacing fallback)
        {
            if (IsPlaying || request.Pattern is not MeleeSlashPattern || attacks.Length != 16)
                return false;

            pending = request;
            struck = false;
            float x = request.Target.Position.X - request.Origin.X;
            float y = request.Target.Position.Y - request.Origin.Y;
            facing = x == 0 && y == 0 ? fallback : Mathf.Abs(x) > Mathf.Abs(y)
                ? (x > 0 ? WandererFacing.East : WandererFacing.West)
                : (y > 0 ? WandererFacing.North : WandererFacing.South);
            ShowAttackFrame(0);
            return true;
        }

        public bool Advance(TimeSpan at, Action<BasicAttackRequest> hit)
        {
            if (pending is not BasicAttackRequest request)
                return false;

            double elapsed = Math.Max(0, (at - request.StartedAt).TotalMilliseconds);
            if (!struck && elapsed >= 2 * FrameMilliseconds)
            {
                struck = true;
                ShowAttackFrame(2);
                hit(request);
                return true;
            }

            if (elapsed >= 4 * FrameMilliseconds)
            {
                pending = null;
                return false;
            }

            ShowAttackFrame((int)(elapsed / FrameMilliseconds));
            return true;
        }

        public void ShowRest(WandererFacing direction)
        {
            int row = (int)direction;
            Attach(Hands[row, 0], Angle[row, 0], direction == WandererFacing.North);
        }

        private void ShowAttackFrame(int frame)
        {
            int row = (int)facing;
            actor.sprite = attacks[row * 4 + frame];
            Attach(Hands[row, frame], Angle[row, frame], facing == WandererFacing.North);
        }

        private void Attach(Vector2 hand, float angle, bool behind)
        {
            weapon.transform.localPosition = new Vector3((hand.x - 16) / 32f, (42 - hand.y) / 32f, 0);
            weapon.transform.localRotation = Quaternion.Euler(0, 0, angle);
            weapon.sortingOrder = actor.sortingOrder + (behind ? -1 : 1);
        }
    }
}
