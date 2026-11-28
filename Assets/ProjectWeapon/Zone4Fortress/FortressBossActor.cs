#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Art.EnemyCues;
using ProjectWeapon.Combat;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectWeapon.Zone4Fortress
{
    [DisallowMultipleComponent]
    public sealed class FortressBossActor : MonoBehaviour
    {
        public const int ZoneId = 4;
        public const string BossId = "fortress_cleave_boss";
        private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(120);
        private static readonly Color HurtTint = new Color(1f, .63f, .49f);
        private static readonly Color DefeatedTint = new Color(.49f, .41f, .39f);

        [SerializeField] private FortressBossSettings settings = null!;
        [SerializeField] private SpriteRenderer body = null!;
        [SerializeField] private EnemyCueView cuePrefab = null!;

        private readonly List<RaycastHit2D> wallHits = new List<RaycastHit2D>();
        private readonly HashSet<Guid> appliedDamage = new HashSet<Guid>();
        private RunState? run;
        private RunId runId;
        private Rigidbody2D player = null!;
        private PlayerHealth health = null!;
        private Collider2D walls = null!;
        private EnemyCueView cue = null!;
        private string playerId = null!;
        private Color originalTint;
        private Vector2 facing = Vector2.down;
        private TimeSpan processedAt;
        private TimeSpan readyAt;
        private TimeSpan flashUntil;

        public float CurrentHealth { get; private set; }
        public FortressCleave? ActiveAttack { get; private set; }
        public FortressBossDefeat? Defeat { get; private set; }
        public event Action<FortressBossDefeat>? Defeated;

        public void Bind(RunState run, string playerId, Rigidbody2D player, PlayerHealth health, Collider2D walls)
        {
            if (this.run != null)
                throw new InvalidOperationException("Use one fortress boss per run.");
            if (run?.CurrentRun is not { ZoneId: ZoneId } session || run.Result != null)
                throw new ArgumentException("An active fortress run is required.", nameof(run));
            if (string.IsNullOrWhiteSpace(playerId) || playerId == BossId)
                throw new ArgumentException("The player needs a distinct stable target ID.", nameof(playerId));
            if (!player || !walls || walls.isTrigger || player.gameObject.scene != gameObject.scene
                || walls.gameObject.scene != gameObject.scene)
                throw new ArgumentException("Player, walls and boss must share the same physics scene.");
            if (!settings || !settings.IsValid || !body || !cuePrefab)
                throw new InvalidOperationException("The boss requires settings, a sprite and the enemy cue prefab.");

            this.health = health ?? throw new ArgumentNullException(nameof(health));
            this.run = run;
            this.playerId = playerId;
            this.player = player;
            this.walls = walls;
            cue = Instantiate(cuePrefab, transform);
            runId = session.Id;
            processedAt = readyAt = session.StartedAt;
            originalTint = body.color;
            CurrentHealth = settings.MaxHealth;
            run.RunResultFinalized += OnRunFinalized;
        }

        public FortressCleaveImpact? Advance(TimeSpan now)
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
                if (now >= scheduled.HitsAt)
                {
                    ClearWarning();
                    readyAt = scheduled.ReadyAt;
                    if (scheduled.HitsAt < session.Deadline)
                    {
                        WorldPoint2D position = Point(player.position);
                        var target = new TargetCandidate(playerId, position, health.CurrentHealth > 0,
                            HasLineOfSight(scheduled.Area.Origin, position));
                        return new FortressCleaveImpact(scheduled, scheduled.FindHits(target));
                    }
                }
                return null;
            }

            if (now < readyAt || now >= session.Deadline - settings.Windup || health.CurrentHealth == 0
                || now > TimeSpan.MaxValue - settings.Windup - settings.Recovery)
                return null;

            Vector2 origin = transform.position;
            Vector2 targetPosition = player.position;
            Vector2 direction = targetPosition - origin;
            if (direction.sqrMagnitude > settings.DetectionRange * settings.DetectionRange
                || !HasLineOfSight(Point(origin), Point(targetPosition)))
                return null;
            if (direction != Vector2.zero)
                facing = direction.normalized;

            var area = AreaDamageRequest.Sector(Guid.NewGuid(), BossId, Point(origin),
                settings.Radius, settings.Damage, Point(facing), settings.Angle);
            ActiveAttack = new FortressCleave(area, now, settings);
            cue.ShowSector(area);
            return null;
        }

        public TargetCandidate ReadTarget(Vector2 from)
        {
            WorldPoint2D position = Point(transform.position);
            return new TargetCandidate(BossId, position, CurrentHealth > 0f, HasLineOfSight(Point(from), position));
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
            cue.FlashHit(Point(transform.position));
            if (CurrentHealth == 0f)
            {
                ClearWarning();
                body.color = DefeatedTint;
                var defeat = new FortressBossDefeat(runId, effectiveAt);
                Defeat = defeat;
                Defeated?.Invoke(defeat);
            }
            else
            {
                body.color = HurtTint;
                flashUntil = effectiveAt <= TimeSpan.MaxValue - FlashDuration ? effectiveAt + FlashDuration : TimeSpan.MaxValue;
            }
            return true;
        }

        private RunSession? LiveSession() => isActiveAndEnabled && player && walls && run is { Result: null }
            && run.CurrentRun is { ZoneId: ZoneId } session && session.Id == runId ? session : null;

        private bool HasLineOfSight(WorldPoint2D origin, WorldPoint2D target)
        {
            if (!origin.IsFinite || !target.IsFinite)
                return false;
            if (origin.Equals(target))
                return true;

            var filter = new ContactFilter2D { useTriggers = false };
            gameObject.scene.GetPhysicsScene2D().Linecast(new Vector2(origin.X, origin.Y),
                new Vector2(target.X, target.Y), filter, wallHits);
            return !wallHits.Exists(hit => hit.collider == walls);
        }

        private void ClearWarning()
        {
            ActiveAttack = null;
            if (cue)
                cue.ClearTelegraph();
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

        private static WorldPoint2D Point(Vector2 vector) => new WorldPoint2D(vector.x, vector.y);
    }
}
