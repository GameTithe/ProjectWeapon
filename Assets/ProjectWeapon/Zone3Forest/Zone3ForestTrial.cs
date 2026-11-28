#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.AutoAttack;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Enemies;
using ProjectWeapon.Extraction;
using ProjectWeapon.ForestBoss;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Movement;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ProjectWeapon.Zone3Forest
{
    /// <summary>The authored forest encounter and its local, unsaved validation run.</summary>
    [DisallowMultipleComponent]
    public sealed class Zone3ForestTrial : MonoBehaviour
    {
        private const int ZoneId = 3;
        private const string PlayerId = "forest-player";
        private const string GuardId = "forest-guard";
        private const string SentryId = "forest-sentry";
        private const float GuardDetectionRadius = 2.4f;
        private const float ChestInteractRadius = .8f;
        private const int ArcSegments = 16;

        [SerializeField] private ZoneCatalog zones = null!;
        [SerializeField] private PlayerMovement movement = null!;
        [SerializeField] private LootPickupTrigger pickup = null!;
        [SerializeField] private Collider2D walls = null!;
        [SerializeField] private CircleCollider2D returnArea = null!;
        [SerializeField] private SpriteRenderer returnMarker = null!;
        [SerializeField] private WeaponDefinition validationSword = null!;
        [SerializeField] private FixedLootChest chest = null!;
        [SerializeField] private MeleeEnemyActor guard = null!;
        [SerializeField] private MeleeEnemySettings guardSettings = null!;
        [SerializeField] private RangedEnemyActor sentry = null!;
        [SerializeField] private RangedEnemySettings sentrySettings = null!;
        [SerializeField] private LineRenderer guardWarning = null!;
        [SerializeField] private LineRenderer sentryWarning = null!;
        [SerializeField] private ForestBossActor boss = null!;

        private readonly InventoryService bag = new InventoryService(Array.Empty<WeaponInstance>());
        private readonly PlayerHealth health = new PlayerHealth(100);
        private readonly List<RaycastHit2D> wallHits = new List<RaycastHit2D>();
        private readonly TargetCandidate[] enemies = new TargetCandidate[3];
        private readonly List<(DamageRequest Hit, TimeSpan At)> incoming = new List<(DamageRequest, TimeSpan)>();
        private readonly List<RunSignal> signals = new List<RunSignal>();
        private RunState run = null!;
        private RunId runId;
        private ZoneRunCountdown countdown = null!;
        private ExtractionAccess extraction = null!;
        private AutoAttackScheduler attacks = null!;
        private Rigidbody2D playerBody = null!;
        private Rigidbody2D guardBody = null!;
        private Func<TimeSpan> readClock = null!;
        private InputAction? interact;

        public ZoneDefinition Zone => zones.GetZone(ZoneId);
        public RunState Run => run;
        public MeleeEnemyActor Guard => guard;
        public RangedEnemyActor Sentry => sentry;
        public ForestBossActor Boss => boss;
        public ForestBossDefeat? BossDefeat { get; private set; }
        public event Action<ForestBossDefeat>? BossDefeated;
        public FixedLootChest Chest => chest;
        public int PlayerHealth => health.CurrentHealth;
        public IReadOnlyList<WeaponInstance> Carried => bag.Snapshot();

        public void ConfigureClock(Func<TimeSpan> clock)
        {
            if (run != null)
                throw new InvalidOperationException("Configure the clock before starting the forest trial.");
            readClock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        private void Start()
        {
            double startedAt = Time.timeAsDouble;
            readClock ??= () => TimeSpan.FromSeconds(Time.timeAsDouble - startedAt);
            run = new RunState(readClock);
            runId = RunId.New();
            countdown = new ZoneRunCountdown(zones, run);
            if (!countdown.TryStartRun(runId, ZoneId))
                throw new InvalidOperationException("The forest trial could not start.");

            playerBody = movement.GetComponent<Rigidbody2D>();
            guardBody = guard.GetComponent<Rigidbody2D>();
            Vector2 center = returnArea.transform.TransformPoint(returnArea.offset);
            extraction = new ExtractionAccess(run, ZoneId, Point(center), returnArea.radius);
            interact = InputSystem.actions?.FindAction("Player/Interact");
            attacks = new AutoAttackScheduler(run, TrySwing, initiallyEnabled: true, () => movement.IsDodging);

            guard.Initialize(GuardId, guardSettings);
            sentry.Initialize(SentryId, sentrySettings);
            boss.Bind(run, PlayerId, playerBody, health, walls);
            boss.Defeated += PublishBossDefeat;
            guard.Brain.AttackResolved += (attack, hits) => RecordHits(hits, attack.HitsAt);
            sentry.Brain.AttackResolved += (attack, hits) => RecordHits(hits, attack.HitsAt);
            guard.Brain.Died += _ =>
            {
                guard.GetComponent<Collider2D>().enabled = false;
                guard.GetComponent<SpriteRenderer>().color = Color.gray;
            };
            sentry.Brain.Died += _ => sentry.GetComponent<SpriteRenderer>().color = Color.gray;

            chest.BindRun(run);
            pickup.Initialize(new LootPickupCoordinator(bag), CanInteract);
            pickup.OutcomeReported += (drop, _, result) =>
            {
                if (drop && result.Status == InventoryPickupStatus.PickedUp)
                    drop!.gameObject.SetActive(false);
            };
        }

        private void OnDestroy()
        {
            if (boss)
                boss.Defeated -= PublishBossDefeat;
        }

        private void PublishBossDefeat(ForestBossDefeat result)
        {
            if (BossDefeat.HasValue || result.RunId != runId || result.ZoneId != ZoneId
                || result.BossId != ForestBossActor.BossId)
                return;

            BossDefeat = result;
            BossDefeated?.Invoke(result);
        }

        private void Update()
        {
            if (run.Result != null)
                return;

            TimeSpan now = readClock();
            Vector2 position = playerBody.position;
            float move = movement.SampleDirection().magnitude;
            bool chestRequested = interact?.WasPressedThisFrame() == true
                && extraction.TryStart(runId, Point(position), move) == null;
            bool dodged = Keyboard.current?.spaceKey.wasPressedThisFrame == true && movement.TryDodge(run, now);

            incoming.Clear();
            signals.Clear();
            ObserveEnemyAttacks(now, position);
            if (dodged && countdown.Extraction.ObserveAcceptedDodge(runId, now) is RunSignal dodgeCancel)
                signals.Add(dodgeCancel);
            if (countdown.Extraction.ObserveMovement(move, now) is RunSignal moveCancel)
                signals.Add(moveCancel);
            countdown.ProcessFrame(signals);

            if (run.Result != null)
            {
                movement.enabled = false;
                guardBody.linearVelocity = Vector2.zero;
            }
            else
            {
                if (chestRequested)
                    TryOpenChest();
                if (attacks.IsEffective)
                    TryAttack(now, position);
            }
            ShowWarnings();
            returnMarker.color = run.Phase switch
            {
                RunPhase.Succeeded => new Color32(154, 223, 165, 255),
                RunPhase.Failed => new Color32(164, 133, 130, 255),
                RunPhase.Extracting => new Color32(198, 237, 255, 255),
                _ => Color.white
            };
        }

        public bool TryOpenChest()
        {
            if (!CanInteract())
                return false;

            Vector2 origin = playerBody.position;
            Vector2 position = chest.transform.position;
            return (position - origin).sqrMagnitude <= ChestInteractRadius * ChestInteractRadius
                && HasClearPath(origin, position) && chest.TryOpen(out _);
        }

        private bool CanInteract() => run?.Phase == RunPhase.Exploring
            && run.CurrentRun is { } current && readClock() < current.Deadline;

        private void ObserveEnemyAttacks(TimeSpan now, Vector2 player)
        {
            bool alive = health.CurrentHealth > 0;
            Vector2 guardPosition = guardBody.position;
            bool guardSeesPlayer = (player - guardPosition).sqrMagnitude <= GuardDetectionRadius * GuardDetectionRadius
                && HasClearPath(guardPosition, player);
            if (now < run.CurrentRun!.Deadline || guard.Brain.ActiveAttack.HasValue)
                guard.Advance(now, new TargetCandidate(PlayerId, Point(player), alive, guardSeesPlayer));
            if (now < run.CurrentRun.Deadline || sentry.Brain.ActiveAttack.HasValue)
                sentry.Advance(now, new TargetCandidate(PlayerId, Point(player), alive, HasClearPath(sentry.transform.position, player)));
            if (boss.Advance(now) is { } roots)
                RecordHits(roots.Hits, roots.Attack.HitsAt);

            incoming.Sort((left, right) => left.At.CompareTo(right.At));
            foreach ((DamageRequest hit, TimeSpan at) in incoming)
                PlayerRunDamage.ApplyHit(health, run, runId, Mathf.RoundToInt(hit.Amount), at, signals, movement.Dodge);
        }

        private void TryAttack(TimeSpan now, Vector2 player)
        {
            Vector2 guardPosition = guardBody.position;
            Vector2 sentryPosition = sentry.transform.position;
            enemies[0] = new TargetCandidate(GuardId, Point(guardPosition), guard.Brain.CurrentHealth > 0,
                HasClearPath(player, guardPosition));
            enemies[1] = new TargetCandidate(SentryId, Point(sentryPosition), sentry.Brain.CurrentHealth > 0,
                HasClearPath(player, sentryPosition));
            Vector2 bossPosition = boss.transform.position;
            enemies[2] = new TargetCandidate(ForestBossActor.BossId, Point(bossPosition), boss.CurrentHealth > 0,
                HasClearPath(player, bossPosition));
            attacks.TryAttack(now, Point(player), validationSword, enemies);
        }

        private bool TrySwing(BasicAttackRequest attack)
        {
            if (attack.Pattern is not MeleeSlashPattern sword
                || !sword.TryCreateSwing(attack, Guid.NewGuid(), PlayerId, enemies, out MeleeSlashSwing? swing))
                return false;

            foreach (DamageRequest hit in swing!.Hits)
            {
                if (StringComparer.Ordinal.Equals(hit.TargetId, GuardId))
                    guard.ApplyDamage(hit, attack.StartedAt);
                else if (StringComparer.Ordinal.Equals(hit.TargetId, SentryId))
                    sentry.ApplyDamage(hit, attack.StartedAt);
                else if (StringComparer.Ordinal.Equals(hit.TargetId, ForestBossActor.BossId))
                    boss.ApplyDamage(hit, attack.StartedAt);
            }
            return true;
        }

        private void RecordHits(IReadOnlyList<DamageRequest> hits, TimeSpan at)
        {
            foreach (DamageRequest hit in hits)
                incoming.Add((hit, at));
        }

        private bool HasClearPath(Vector2 from, Vector2 to)
        {
            Vector2 direction = to - from;
            if (direction.sqrMagnitude == 0)
                return true;
            var filter = new ContactFilter2D { useTriggers = false };
            gameObject.scene.GetPhysicsScene2D().Raycast(from, direction, direction.magnitude, filter, wallHits);
            return !wallHits.Exists(hit => hit.collider == walls);
        }

        private void ShowWarnings()
        {
            guardWarning.enabled = run.Result == null && guard.Brain.ActiveAttack.HasValue;
            if (guardWarning.enabled && guard.Brain.ActiveAttack is { } melee)
            {
                AreaDamageRequest area = melee.Area;
                guardWarning.SetPosition(0, Vector(area.Origin));
                float direction = Mathf.Atan2(area.Direction.Y, area.Direction.X);
                float halfAngle = area.AngleDegrees * Mathf.Deg2Rad * .5f;
                for (int index = 0; index <= ArcSegments; index++)
                {
                    float angle = direction - halfAngle + 2f * halfAngle * index / ArcSegments;
                    var reach = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * area.Radius;
                    guardWarning.SetPosition(index + 1, Vector(area.Origin) + reach);
                }
            }

            sentryWarning.enabled = run.Result == null && sentry.Brain.ActiveAttack.HasValue;
            if (sentryWarning.enabled && sentry.Brain.ActiveAttack is { } shot)
            {
                RangedAttackStrip strip = shot.Area;
                sentryWarning.SetPosition(0, Vector(strip.GetCorner(false, true)));
                sentryWarning.SetPosition(1, Vector(strip.GetCorner(true, true)));
                sentryWarning.SetPosition(2, Vector(strip.GetCorner(true, false)));
                sentryWarning.SetPosition(3, Vector(strip.GetCorner(false, false)));
            }
        }

        private static WorldPoint2D Point(Vector2 point) => new WorldPoint2D(point.x, point.y);
        private static Vector3 Vector(WorldPoint2D point) => new Vector3(point.X, point.Y);
    }
}
