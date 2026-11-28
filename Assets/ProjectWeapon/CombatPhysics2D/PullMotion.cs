#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace ProjectWeapon.Combat
{
    public readonly struct PullSettings
    {
        public PullSettings(float range, float speed, float duration, LayerMask solidLayers, float wallClearance = 0.01f)
        {
            Range = range;
            Speed = speed;
            Duration = duration;
            SolidLayers = solidLayers;
            WallClearance = wallClearance;
        }

        public float Range { get; }
        public float Speed { get; }
        public float Duration { get; }
        public LayerMask SolidLayers { get; }
        public float WallClearance { get; }
        internal bool IsValid => float.IsFinite(Range) && Range >= 0f && float.IsFinite(Speed) && Speed > 0f
            && float.IsFinite(Duration) && Duration > 0f && float.IsFinite(WallClearance) && WallClearance >= 0f;
    }

    public enum PullStatus
    {
        Active,
        ReachedCenter,
        DurationElapsed,
        Blocked,
        Cancelled
    }

    public readonly struct PullStep
    {
        internal PullStep(Vector2 displacement, PullStatus status)
        {
            Displacement = displacement;
            Status = status;
        }

        public Vector2 Displacement { get; }
        public PullStatus Status { get; }
    }

    /// <summary>Produces fixed-step displacement for a target's existing 2D movement owner.</summary>
    public sealed class PullMotion
    {
        private readonly Rigidbody2D target;
        private readonly Vector2 center;
        private readonly PullSettings settings;
        private readonly ContactFilter2D filter;
        private readonly List<Collider2D> shapes = new List<Collider2D>();
        private readonly List<RaycastHit2D> contacts = new List<RaycastHit2D>();
        private double remaining;

        private PullMotion(Rigidbody2D target, Vector2 center, PullSettings settings)
        {
            this.target = target;
            this.center = center;
            this.settings = settings;
            remaining = settings.Duration;
            var query = new ContactFilter2D { useTriggers = false };
            query.SetLayerMask(settings.SolidLayers);
            filter = query;
        }

        public PullStatus Status { get; private set; }

        public static bool TryStart(Rigidbody2D target, Vector2 center, PullSettings settings, bool isEligible,
            [NotNullWhen(true)] out PullMotion? motion)
        {
            motion = null;
            if (!isEligible || !settings.IsValid || !CanMove(target) || !IsFinite(center))
                return false;

            float distance = (center - target.position).magnitude;
            if (!float.IsFinite(distance) || distance > settings.Range)
                return false;

            var candidate = new PullMotion(target, center, settings);
            if (!candidate.RefreshShapes())
                return false;
            motion = candidate;
            return true;
        }

        public PullStep Advance(float fixedSeconds, bool isStillEligible)
        {
            if (!float.IsFinite(fixedSeconds) || fixedSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(fixedSeconds));

            if (Status != PullStatus.Active)
                return new PullStep(Vector2.zero, Status);
            if (!isStillEligible || !CanMove(target) || !RefreshShapes())
                return Complete(PullStatus.Cancelled);

            Vector2 offset = center - target.position;
            float distance = offset.magnitude;
            if (!float.IsFinite(distance))
                return Complete(PullStatus.Cancelled);
            if (distance == 0f)
                return Complete(PullStatus.ReachedCenter);

            float travel = (float)Math.Min(distance, settings.Speed * Math.Min(remaining, fixedSeconds));
            if (travel <= 0f)
                return Complete(PullStatus.DurationElapsed);
            Vector2 direction = offset / distance;
            float queryDistance = (float)Math.Min(float.MaxValue, (double)travel + settings.WallClearance);

            float nearestWall = float.PositiveInfinity;
            foreach (Collider2D shape in shapes)
            {
                if (shape.Cast(direction, filter, contacts, queryDistance) > 0)
                    nearestWall = Mathf.Min(nearestWall, contacts[0].distance);
            }
            if (float.IsFinite(nearestWall))
            {
                float safeDistance = Mathf.Clamp(nearestWall - settings.WallClearance, 0f, travel);
                return Complete(PullStatus.Blocked, direction * safeDistance);
            }

            if (travel >= distance)
                return Complete(PullStatus.ReachedCenter, offset);

            remaining -= fixedSeconds;
            return remaining <= 0
                ? Complete(PullStatus.DurationElapsed, direction * travel)
                : new PullStep(direction * travel, Status);
        }

        private bool RefreshShapes()
        {
            shapes.Clear();
            target.GetAttachedColliders(shapes, findTriggers: false);
            shapes.RemoveAll(shape => !shape || !shape.enabled || !shape.gameObject.activeInHierarchy);
            return shapes.Count != 0;
        }

        private PullStep Complete(PullStatus status, Vector2 displacement = default)
        {
            Status = status;
            return new PullStep(displacement, status);
        }

        private static bool CanMove(Rigidbody2D body)
            => body && body.gameObject.activeInHierarchy && body.simulated
                && body.bodyType != RigidbodyType2D.Static && body.attachedColliderCount > 0;

        private static bool IsFinite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);
    }
}
