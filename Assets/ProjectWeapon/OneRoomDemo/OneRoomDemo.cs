#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Art.Wanderer;
using ProjectWeapon.AutoAttack;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Enemies;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.LootUI;
using ProjectWeapon.Movement;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.OneRoomDemo
{
    /// <summary>Composes the one-room play scene with a fresh, in-memory inventory.</summary>
    public sealed class OneRoomDemo : MonoBehaviour
    {
        private const string PlayerId = "demo-player";
        private const string EnemyId = "demo-guard";
        private const int MaxPlayerHealth = 100;
        private const float WakeDistance = 3.5f;

        [SerializeField] private PlayerMovement movement = null!;
        [SerializeField] private VirtualJoystick joystick = null!;
        [SerializeField] private WandererLocomotionView locomotion = null!;
        [SerializeField] private DemoSwordPlayback sword = null!;
        [SerializeField] private SpriteRenderer playerVisual = null!;
        [SerializeField] private Rigidbody2D enemyBody = null!;
        [SerializeField] private SpriteRenderer enemyVisual = null!;
        [SerializeField] private LineRenderer warning = null!;
        [SerializeField] private LootPickupTrigger pickup = null!;
        [SerializeField] private LootOverflowPanel overflow = null!;
        [SerializeField] private WeaponCatalog weapons = null!;
        [SerializeField] private ZoneCatalog zones = null!;
        [SerializeField] private MeleeEnemySettings enemySettings = null!;
        [SerializeField] private TMP_FontAsset font = null!;
        [SerializeField] private TMP_Text healthLine = null!;
        [SerializeField] private TMP_Text bagLine = null!;
        [SerializeField] private Sprite lootSprite = null!;
        [SerializeField] private Material worldMaterial = null!;
        [SerializeField] private Transform[] lootStarts = Array.Empty<Transform>();
        [SerializeField] private Transform returnedLoot = null!;

        private readonly List<RunSignal> frame = new List<RunSignal>();
        private readonly TargetCandidate[] targets = new TargetCandidate[1];
        private readonly PlayerHealth health = new PlayerHealth(MaxPlayerHealth);
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> definitions = null!;
        private Rigidbody2D playerBody = null!;
        private WeaponDefinition equipped = null!;
        private RunState run = null!;
        private ZoneRunCountdown countdown = null!;
        private AutoAttackScheduler scheduler = null!;
        private MeleeEnemy enemy = null!;
        private TimeSpan clock;
        private double initialTime;
        private double enemyFlashUntil;
        private double playerFlashUntil;
        private bool pausedForChoice;

        public InventoryService Inventory { get; private set; } = null!;
        public int PlayerHealth => health.CurrentHealth;
        public float EnemyHealth => enemy.CurrentHealth;
        public MeleeEnemyPhase EnemyPhase => enemy.Phase;
        public bool IsChoiceOpen => overflow.IsOpen;

        private void Start()
        {
            initialTime = Time.timeAsDouble;
            playerBody = movement.GetComponent<Rigidbody2D>();
            definitions = weapons.CreateLookup();
            equipped = definitions[new WeaponDefinitionId("old_sword")];
            var carried = WeaponInstance.Create(equipped.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            Inventory = new InventoryService(new[] { carried });
            pickup.Initialize(new LootPickupCoordinator(Inventory));
            overflow.Initialize(pickup, weapons, font);
            pickup.OutcomeReported += OnLootOutcome;
            movement.BindJoystick(joystick);

            run = new RunState(() => clock);
            countdown = new ZoneRunCountdown(zones, run);
            countdown.TryStartRun(RunId.New(), 1);
            scheduler = new AutoAttackScheduler(run, StartSlash, initiallyEnabled: true);
            enemy = new MeleeEnemy(EnemyId, enemySettings);
            enemy.AttackStarted += attack => DrawWarning(attack.Area);
            enemy.AttackResolved += OnEnemyAttackResolved;
            enemy.DamageApplied += (_, _) =>
            {
                enemyFlashUntil = Time.timeAsDouble + .18;
                RefreshHealth();
            };
            enemy.Died += _ =>
            {
                warning.enabled = false;
                enemyBody.linearVelocity = Vector2.zero;
                enemyBody.simulated = false;
                enemyVisual.transform.localRotation = Quaternion.Euler(0, 0, -78);
                RefreshHealth();
            };
            health.DamageApplied += _ =>
            {
                playerFlashUntil = Time.timeAsDouble + .18;
                RefreshHealth();
            };

            string[] loot = { "dagger", "hunting_bow", "mace", "wooden_spear" };
            for (int index = 0; index < loot.Length; index++)
                SpawnLoot(WeaponInstance.Create(new WeaponDefinitionId(loot[index]), WeaponProvenance.ExplorationLoot,
                    WeaponLocation.Warehouse()), lootStarts[index].position);
            RefreshHealth();
            RefreshBag();
        }

        private void Update()
        {
            if (pausedForChoice)
                initialTime += Time.deltaTime;
            clock = TimeSpan.FromSeconds(Time.timeAsDouble - initialTime);
            UpdateMovement();
            if (!pausedForChoice && run.Result == null)
            {
                targets[0] = EnemyTarget();
                bool playing = sword.Advance(clock, ResolveSlash);
                if (!playing)
                {
                    locomotion.Present(movement.SampleDirection(), movement.IsDodging, movement.DodgeDirection, Time.deltaTime);
                    sword.ShowRest(locomotion.Facing);
                    scheduler.TryAttack(clock, Point(playerBody.position), equipped, targets);
                }
                bool awake = (playerBody.position - enemyBody.position).sqrMagnitude <= WakeDistance * WakeDistance;
                enemy.Advance(clock, Point(enemyBody.position), new TargetCandidate(PlayerId, Point(playerBody.position),
                    isAlive: health.CurrentHealth > 0, isAttackable: awake));
            }
            countdown.ProcessFrame(frame);
            frame.Clear();

            enemyVisual.color = enemy.Phase == MeleeEnemyPhase.Dead ? new Color(.48f, .42f, .50f, 1)
                : Time.timeAsDouble < enemyFlashUntil ? new Color(1, .72f, .45f, 1) : Color.white;
            playerVisual.color = Time.timeAsDouble < playerFlashUntil ? new Color(1, .46f, .40f, 1) : Color.white;
            UpdateMovement();
        }

        private void FixedUpdate()
        {
            if (enemy == null || !enemyBody.simulated)
                return;
            WorldPoint2D velocity = enemy.MovementVelocity;
            enemyBody.linearVelocity = pausedForChoice || run.Result != null
                ? Vector2.zero : new Vector2(velocity.X, velocity.Y);
        }

        private void OnDestroy()
        {
            if (pickup)
                pickup.OutcomeReported -= OnLootOutcome;
        }

        private bool StartSlash(BasicAttackRequest request)
            => sword.TryBegin(request, locomotion.Facing);

        private void ResolveSlash(BasicAttackRequest request)
        {
            targets[0] = EnemyTarget();
            if (request.Pattern is not MeleeSlashPattern pattern
                || !pattern.TryCreateSwing(request, Guid.NewGuid(), PlayerId, targets, out MeleeSlashSwing? swing))
                return;
            foreach (DamageRequest hit in swing!.Hits)
                enemy.ApplyDamage(hit, clock);
        }

        private TargetCandidate EnemyTarget()
            => new TargetCandidate(EnemyId, Point(enemyBody.position), enemy.Phase != MeleeEnemyPhase.Dead, true);

        private void OnEnemyAttackResolved(MeleeEnemyAttack _, IReadOnlyList<DamageRequest> hits)
        {
            warning.enabled = false;
            foreach (DamageRequest hit in hits)
                PlayerRunDamage.ApplyHit(health, run, run.CurrentRun!.Id, Mathf.RoundToInt(hit.Amount), clock, frame, movement.Dodge);
        }

        private void DrawWarning(AreaDamageRequest area)
        {
            float center = Mathf.Atan2(area.Direction.Y, area.Direction.X);
            float half = area.AngleDegrees * Mathf.Deg2Rad * .5f;
            const int segments = 40;
            warning.positionCount = segments + 2;
            warning.SetPosition(0, new Vector3(area.Origin.X, area.Origin.Y, 0));
            for (int index = 0; index <= segments; index++)
            {
                float angle = center - half + half * 2 * index / segments;
                warning.SetPosition(index + 1, new Vector3(area.Origin.X + area.Radius * Mathf.Cos(angle),
                    area.Origin.Y + area.Radius * Mathf.Sin(angle), 0));
            }
            warning.enabled = true;
        }

        private void UpdateMovement()
        {
            bool choice = overflow.IsOpen;
            if (choice != pausedForChoice)
            {
                pausedForChoice = choice;
                joystick.Release();
                joystick.gameObject.SetActive(!choice);
            }
            movement.enabled = !choice && !sword.IsPlaying && run.Result == null;
            if (run.Result != null)
                warning.enabled = false;
        }

        private void OnLootOutcome(WorldLoot? source, WeaponInstanceId _, InventoryPickupResult result)
        {
            if (result.Status == InventoryPickupStatus.PickedUp)
            {
                if (source)
                {
                    source!.gameObject.SetActive(false);
                    Destroy(source.gameObject);
                }

                if (result.Displaced is WeaponInstance displaced)
                    SpawnLoot(displaced, returnedLoot.position);
                RefreshBag();
            }
            UpdateMovement();
        }

        private void SpawnLoot(WeaponInstance item, Vector3 position)
        {
            var drop = new GameObject(definitions[item.DefinitionId].DisplayName);
            drop.transform.SetParent(transform, false);
            drop.transform.position = position;
            var visual = drop.AddComponent<SpriteRenderer>();
            visual.sprite = lootSprite;
            visual.sharedMaterial = worldMaterial;
            visual.sortingOrder = 2;
            var circle = drop.AddComponent<CircleCollider2D>();
            circle.radius = .18f;
            circle.isTrigger = true;
            drop.AddComponent<WorldLoot>().Initialize(item);
        }

        private void RefreshHealth()
            => healthLine.text = $"HP {health.CurrentHealth} / {health.MaxHealth}   ·   경비병 {Mathf.CeilToInt(enemy.CurrentHealth)} / {Mathf.CeilToInt(enemySettings.MaxHealth)}";

        private void RefreshBag()
        {
            var names = new[] { "빈 칸", "빈 칸", "빈 칸" };
            int count = 0;
            foreach (WeaponInstance item in Inventory.Snapshot())
            {
                if (item.Location.Kind != WeaponLocationKind.Carried)
                    continue;
                names[item.Location.SlotIndex!.Value] = definitions[item.DefinitionId].DisplayName;
                count++;
            }
            bagLine.text = $"낡은 검   ·   가방 {count}/3   ·   {string.Join("  /  ", names)}";
        }

        private static WorldPoint2D Point(Vector2 position) => new WorldPoint2D(position.x, position.y);
    }
}
