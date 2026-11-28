#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectWeapon.SteelSpearSkill
{
    /// <summary>Sweeps the bound player's normal collider in the same physics scene as the room walls.</summary>
    public sealed class SteelSpearChargeMotion
    {
        private const float WallClearance = .01f;
        private readonly Rigidbody2D body;
        private readonly Collider2D playerShape;
        private readonly Collider2D walls;
        private readonly List<RaycastHit2D> wallHits = new List<RaycastHit2D>();
        private readonly ContactFilter2D solid = new ContactFilter2D { useTriggers = false };

        public SteelSpearChargeMotion(Rigidbody2D body, Collider2D playerShape, Collider2D walls)
        {
            if (!body || !playerShape || !walls || walls == playerShape || playerShape.attachedRigidbody != body
                || walls.attachedRigidbody == body || playerShape.isTrigger || walls.isTrigger
                || walls.gameObject.scene != body.gameObject.scene)
                throw new ArgumentException("The player and solid walls must use the same 2D physics scene.");

            this.body = body;
            this.playerShape = playerShape;
            this.walls = walls;
        }

        internal bool IsAvailable => body && body.simulated && body.bodyType == RigidbodyType2D.Dynamic
            && playerShape && playerShape.isActiveAndEnabled && !playerShape.isTrigger
            && walls && walls.isActiveAndEnabled && !walls.isTrigger && walls.gameObject.scene == body.gameObject.scene;

        internal WorldPoint2D Position => new WorldPoint2D(body.position.x, body.position.y);

        internal bool MoveForward(WorldPoint2D direction, float distance)
        {
            var forward = new Vector2(direction.X, direction.Y);
            float allowed = distance;
            playerShape.Cast(forward, solid, wallHits, distance);
            foreach (RaycastHit2D hit in wallHits)
            {
                if (hit.collider == walls)
                    allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - WallClearance));
            }

            body.linearVelocity = Vector2.zero;
            body.position += forward * allowed;
            return allowed < distance;
        }

        internal bool HasClearPath(WorldPoint2D origin, WorldPoint2D target)
        {
            var start = new Vector2(origin.X, origin.Y);
            var end = new Vector2(target.X, target.Y);
            if (start.Equals(end))
                return true;

            body.gameObject.scene.GetPhysicsScene2D().Linecast(start, end, solid, wallHits);
            return !wallHits.Exists(hit => hit.collider == walls);
        }
    }
}
