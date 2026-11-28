#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectWeapon.PrisonGuard
{
    [DisallowMultipleComponent]
    public sealed class PrisonGuardActor : MonoBehaviour
    {
        public const int ZoneId = 2;
        public const string BossId = "prison_guard";
        private static readonly TimeSpan HitFlash = TimeSpan.FromMilliseconds(120);
        private static readonly Color HurtTint = new Color(1f, .64f, .56f, 1f);
        private static readonly Color DefeatedTint = new Color(.42f, .39f, .44f, .85f);

        [SerializeField] private PrisonGuardSettings settings = null!;
        [SerializeField] private SpriteRenderer body = null!;
        [SerializeField] private LineRenderer warning = null!;

        private readonly List<RaycastHit2D> wallHits = new List<RaycastHit2D>();
        private readonly HashSet<Guid> appliedDamage = new HashSet<Guid>();
        private RunState? run;
        private RunId runId;
        private string playerId = null!;
        private Rigidbody2D player = null!;
        private PlayerHealth playerHealth = null!;
        private Collider2D walls = null!;
        private Color bodyTint;
        private TimeSpan flashUntil;
        private TimeSpan processedAt;
        private TimeSpan readyAt;

        public float CurrentHealth { get; private set; }
        public PrisonGuardSlam? ActiveAttack { get; private set; }
        public PrisonGuardDefeat? Defeat { get; private set; }
        public event Action<PrisonGuardDefeat>? Defeated;

        public void Bind(RunState run, string playerId, Rigidbody2D player, PlayerHealth playerHealth, Collider2D walls)
        {
            if (this.run != null)
                throw new InvalidOperationException("Use one prison guard instance per run.");
            if (run?.CurrentRun is not { ZoneId: ZoneId } session || run.Result != null)
                throw new ArgumentException("An active prison run is required.", nameof(run));
            if (string.IsNullOrWhiteSpace(playerId) || StringComparer.Ordinal.Equals(playerId, BossId))
                throw new ArgumentException("Use the stable player target identifier.", nameof(playerId));
            if (!player || !walls || walls.isTrigger || player.gameObject.scene != gameObject.scene
                || walls.gameObject.scene != gameObject.scene)
                throw new ArgumentException("Player, solid walls and guard must share the same physics scene.");
            if (!settings || !settings.IsValid || !body || !warning || warning.positionCount != 4)
                throw new InvalidOperationException("The guard prefab is missing valid settings or renderers.");

            this.playerHealth = playerHealth ?? throw new ArgumentNullException(nameof(playerHealth));
            this.run = run;
            runId = session.Id;
            this.playerId = playerId;
            this.player = player;
            this.walls = walls;
            bodyTint = body.color;
            CurrentHealth = settings.MaxHealth;
            processedAt = session.StartedAt;
            run.RunResultFinalized += OnRunFinalized;
            CancelAttack();
        }

        /// <summary>Collects one resolved strike for the room's effective-time damage arbitration.</summary>
        public PrisonGuardImpact? Advance(TimeSpan now)
        {
            if (!CanAct(now) || Defeat.HasValue)
            {
                CancelAttack();
                return null;
            }
            if (now < processedAt)
                return null;

            processedAt = now;
            if (now >= flashUntil)
                body.color = bodyTint;
            if (ActiveAttack is { } attack)
                return now >= attack.HitsAt ? Resolve(attack) : null;

            if (now < readyAt || playerHealth.CurrentHealth == 0
                || now > TimeSpan.MaxValue - settings.Windup - settings.Recovery)
                return null;

            WorldPoint2D origin = Point(transform.position);
            WorldPoint2D target = Point(player.position);
            double distance = origin.SquaredDistanceTo(target);
            if (!origin.IsFinite || !target.IsFinite || distance == 0d
                || distance > (double)settings.DetectionRange * settings.DetectionRange || !HasLineOfSight(origin, target))
                return null;

            ActiveAttack = new PrisonGuardSlam(Guid.NewGuid(), origin, target, now, settings);
            ShowWarning(ActiveAttack.Value);
            return null;
        }

        public bool ApplyDamage(DamageRequest hit, TimeSpan effectiveAt)
        {
            if (!CanAct(effectiveAt) || Defeat.HasValue || effectiveAt < processedAt || hit.ApplicationId == Guid.Empty
                || !StringComparer.Ordinal.Equals(hit.TargetId, BossId) || string.IsNullOrWhiteSpace(hit.AttackerId)
                || StringComparer.Ordinal.Equals(hit.AttackerId, BossId)
                || !float.IsFinite(hit.Amount) || hit.Amount <= 0f || !appliedDamage.Add(hit.ApplicationId))
                return false;

            processedAt = effectiveAt;
            CurrentHealth = Math.Max(0f, CurrentHealth - hit.Amount);
            if (CurrentHealth == 0f)
            {
                CancelAttack();
                body.color = DefeatedTint;
                var result = new PrisonGuardDefeat(runId, effectiveAt);
                Defeat = result;
                Defeated?.Invoke(result);
            }
            else
            {
                body.color = HurtTint;
                flashUntil = effectiveAt <= TimeSpan.MaxValue - HitFlash ? effectiveAt + HitFlash : TimeSpan.MaxValue;
            }
            return true;
        }

        private PrisonGuardImpact Resolve(PrisonGuardSlam attack)
        {
            CancelAttack();
            readyAt = attack.ReadyAt;
            WorldPoint2D point = Point(player.position);
            if (playerHealth.CurrentHealth == 0 || !attack.Contains(point) || !HasLineOfSight(attack.Origin, point))
                return new PrisonGuardImpact(attack, Array.Empty<DamageRequest>());

            var contact = AreaDamageRequest.Circle(attack.DamageId, BossId, point, radius: 0f, amount: attack.Damage);
            var target = new TargetCandidate(playerId, point, isAlive: true, isAttackable: true);
            return new PrisonGuardImpact(attack, AreaDamageSelector.FindTargets(contact, new[] { target }));
        }

        private bool CanAct(TimeSpan now) => isActiveAndEnabled && player && walls && run is { Result: null }
            && run.CurrentRun is { } session && session.Id == runId && session.ZoneId == ZoneId
            && now >= session.StartedAt && now < session.Deadline;

        private bool HasLineOfSight(WorldPoint2D origin, WorldPoint2D target)
        {
            var start = new Vector2(origin.X, origin.Y);
            var end = new Vector2(target.X, target.Y);
            if (start == end)
                return true;

            var filter = new ContactFilter2D { useTriggers = false };
            gameObject.scene.GetPhysicsScene2D().Linecast(start, end, filter, wallHits);
            foreach (RaycastHit2D hit in wallHits)
                if (hit.collider == walls)
                    return false;

            return true;
        }

        private void OnRunFinalized(RunResult result)
        {
            if (result.RunId == runId)
                CancelAttack();
        }

        private void OnDisable() => CancelAttack();

        private void OnDestroy()
        {
            if (run != null)
                run.RunResultFinalized -= OnRunFinalized;
        }

        private void CancelAttack()
        {
            ActiveAttack = null;
            if (warning)
                warning.enabled = false;
        }

        private void ShowWarning(PrisonGuardSlam area)
        {
            warning.SetPosition(0, Vector(area.GetCorner(false, true)));
            warning.SetPosition(1, Vector(area.GetCorner(true, true)));
            warning.SetPosition(2, Vector(area.GetCorner(true, false)));
            warning.SetPosition(3, Vector(area.GetCorner(false, false)));
            warning.enabled = true;
        }

        private static WorldPoint2D Point(Vector2 value) => new WorldPoint2D(value.x, value.y);
        private static Vector3 Vector(WorldPoint2D value) => new Vector3(value.X, value.Y, 0f);
    }
}
