#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectWeapon.Zone5Sanctum
{
    [DisallowMultipleComponent]
    public sealed class SanctumBossActor : MonoBehaviour
    {
        public const int ZoneId = 5;
        public const string BossId = "sanctum_ring_warden";
        private const int RingSegments = 64;
        private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(120);
        private static readonly Color HurtTint = new Color(1f, 0.62f, 0.88f);
        private static readonly Color DefeatedTint = new Color(0.46f, 0.41f, 0.48f, 0.86f);

        [SerializeField] private SanctumBossSettings settings = null!;
        [SerializeField] private SpriteRenderer body = null!;
        [SerializeField] private LineRenderer innerWarning = null!;
        [SerializeField] private LineRenderer outerWarning = null!;

        private readonly List<RaycastHit2D> wallHits = new List<RaycastHit2D>();
        private readonly HashSet<Guid> appliedDamage = new HashSet<Guid>();
        private RunState? run;
        private RunId runId;
        private Rigidbody2D player = null!;
        private PlayerHealth health = null!;
        private string playerId = null!;
        private Collider2D walls = null!;
        private Color originalTint;
        private TimeSpan flashUntil;
        private TimeSpan processedAt;
        private TimeSpan readyAt;

        public float CurrentHealth { get; private set; }
        public SanctumRingStrike? ActiveAttack { get; private set; }
        public SanctumBossDefeat? Defeat { get; private set; }
        public event Action<SanctumBossDefeat>? Defeated;

        public void Bind(RunState run, string playerId, Rigidbody2D player, PlayerHealth health, Collider2D walls)
        {
            if (this.run != null)
                throw new InvalidOperationException("Use one sanctum boss instance per run.");
            if (run?.CurrentRun is not { ZoneId: ZoneId } session || run.Result != null)
                throw new ArgumentException("An active sanctum run is required.", nameof(run));
            if (string.IsNullOrWhiteSpace(playerId) || playerId == BossId)
                throw new ArgumentException("The player needs a distinct stable target ID.", nameof(playerId));
            if (!player || !walls || walls.isTrigger || player.gameObject.scene != gameObject.scene
                || walls.gameObject.scene != gameObject.scene)
                throw new ArgumentException("Player, solid walls and boss must share the same physics scene.");
            if (!settings || !settings.IsValid || !body || !IsRing(innerWarning) || !IsRing(outerWarning))
                throw new InvalidOperationException("The sanctum boss prefab needs valid settings, a sprite and two world-space rings.");

            this.health = health ?? throw new ArgumentNullException(nameof(health));
            this.run = run;
            this.playerId = playerId;
            this.player = player;
            this.walls = walls;
            runId = session.Id;
            processedAt = session.StartedAt;
            readyAt = session.StartedAt;
            originalTint = body.color;
            CurrentHealth = settings.MaxHealth;
            run.RunResultFinalized += OnRunFinalized;
            ClearWarning();
        }

        /// <summary>Returns at most one strike, at its original effective time for the room to arbitrate.</summary>
        public SanctumRingImpact? Advance(TimeSpan now)
        {
            if (LiveSession() is not { } session || Defeat.HasValue)
            {
                ClearWarning();
                return null;
            }
            if (now < processedAt)
                return null;

            processedAt = now;
            if (now >= flashUntil)
                body.color = originalTint;
            if (ActiveAttack is { } scheduled)
            {
                if (now >= scheduled.HitsAt && scheduled.HitsAt < session.Deadline)
                    return Resolve(scheduled);
                if (now >= session.Deadline)
                    ClearWarning();
                return null;
            }

            if (now >= session.Deadline || now < readyAt || health.CurrentHealth == 0
                || now > TimeSpan.MaxValue - settings.Windup - settings.Recovery)
                return null;

            WorldPoint2D origin = Point(transform.position);
            WorldPoint2D target = Point(player.position);
            double detect = settings.DetectionRange;
            if (!origin.IsFinite || !target.IsFinite || origin.SquaredDistanceTo(target) > detect * detect
                || !HasLineOfSight(origin, target))
                return null;

            var attack = new SanctumRingStrike(Guid.NewGuid(), origin, now, settings);
            ActiveAttack = attack;
            ShowWarning(attack);
            return null;
        }

        public bool ApplyDamage(DamageRequest hit, TimeSpan effectiveAt)
        {
            if (LiveSession() is not { } session || effectiveAt >= session.Deadline || Defeat.HasValue
                || effectiveAt < processedAt || hit.ApplicationId == Guid.Empty || hit.TargetId != BossId
                || string.IsNullOrWhiteSpace(hit.AttackerId) || hit.AttackerId == BossId
                || !float.IsFinite(hit.Amount) || hit.Amount <= 0f || !appliedDamage.Add(hit.ApplicationId))
                return false;

            processedAt = effectiveAt;
            CurrentHealth = Math.Max(0f, CurrentHealth - hit.Amount);
            if (CurrentHealth == 0f)
            {
                ClearWarning();
                body.color = DefeatedTint;
                var defeat = new SanctumBossDefeat(runId, effectiveAt);
                Defeat = defeat;
                Defeated?.Invoke(defeat);
            }
            else
            {
                body.color = HurtTint;
                flashUntil = effectiveAt <= TimeSpan.MaxValue - FlashDuration
                    ? effectiveAt + FlashDuration
                    : TimeSpan.MaxValue;
            }
            return true;
        }

        private SanctumRingImpact Resolve(SanctumRingStrike strike)
        {
            ClearWarning();
            readyAt = strike.ReadyAt;
            WorldPoint2D observed = Point(player.position);
            var target = new TargetCandidate(playerId, observed, health.CurrentHealth > 0,
                observed.IsFinite && HasLineOfSight(strike.Center, observed));
            return new SanctumRingImpact(strike, strike.ReadHits(target));
        }

        private RunSession? LiveSession() => isActiveAndEnabled && player && walls && run is { Result: null }
            && run.CurrentRun is { ZoneId: ZoneId } session && session.Id == runId ? session : null;

        private bool HasLineOfSight(WorldPoint2D origin, WorldPoint2D target)
        {
            var start = new Vector2(origin.X, origin.Y);
            var end = new Vector2(target.X, target.Y);
            if (start.Equals(end))
                return true;

            var filter = new ContactFilter2D { useTriggers = false };
            gameObject.scene.GetPhysicsScene2D().Linecast(start, end, filter, wallHits);
            foreach (RaycastHit2D hit in wallHits)
                if (hit.collider == walls)
                    return false;
            return true;
        }

        public TargetCandidate ReadTarget(Vector2 observer)
        {
            WorldPoint2D target = Point(transform.position);
            return new TargetCandidate(BossId, target, CurrentHealth > 0, HasLineOfSight(Point(observer), target));
        }

        private static bool IsRing(LineRenderer renderer) => renderer && renderer.useWorldSpace && renderer.loop;

        private void ShowWarning(SanctumRingStrike strike)
        {
            DrawRing(innerWarning, strike.Center, strike.InnerRadius);
            DrawRing(outerWarning, strike.Center, strike.OuterRadius);
        }

        private static void DrawRing(LineRenderer renderer, WorldPoint2D center, float radius)
        {
            renderer.positionCount = RingSegments;
            for (int point = 0; point < RingSegments; point++)
            {
                float angle = point * (2f * Mathf.PI / RingSegments);
                renderer.SetPosition(point, new Vector3(center.X + radius * Mathf.Cos(angle),
                    center.Y + radius * Mathf.Sin(angle)));
            }
            renderer.enabled = true;
        }

        private void ClearWarning()
        {
            ActiveAttack = null;
            if (innerWarning)
                innerWarning.enabled = false;
            if (outerWarning)
                outerWarning.enabled = false;
        }

        private void OnRunFinalized(RunResult result)
        {
            if (result.RunId == runId)
                ClearWarning();
        }

        private void OnDisable() => ClearWarning();

        private void OnDestroy()
        {
            if (run != null)
                run.RunResultFinalized -= OnRunFinalized;
        }

        private static WorldPoint2D Point(Vector2 value) => new WorldPoint2D(value.x, value.y);
    }
}
