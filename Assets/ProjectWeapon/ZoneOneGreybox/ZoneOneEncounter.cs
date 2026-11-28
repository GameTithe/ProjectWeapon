#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.AutoAttack;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Enemies;
using ProjectWeapon.Enemies.Boss;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.MaceSkill;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.OldSwordSkill;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;
using ProjectWeapon.WoodenSpearSkill;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectWeapon.ZoneOneGreybox
{
    [DisallowMultipleComponent]
    public sealed class ZoneOneEncounter : MonoBehaviour, IManualSkillEffect
    {
        private const string PlayerId = "zone1-player";
        private const string MeleeId = "zone1-melee";
        private const string RangedId = "zone1-ranged";
        private const float MeleeDetectionRadius = 2.4f;
        private const float ChestRadius = 0.85f;

        [SerializeField] private MeleeEnemyActor melee = null!;
        [SerializeField] private MeleeEnemySettings meleeSettings = null!;
        [SerializeField] private RangedEnemyActor ranged = null!;
        [SerializeField] private RangedEnemySettings rangedSettings = null!;
        [SerializeField] private BossShockwaveActor boss = null!;
        [SerializeField] private FixedLootChest chest = null!;
        [SerializeField] private LootPickupTrigger pickup = null!;
        [SerializeField] private Collider2D walls = null!;
        [SerializeField] private WeaponDefinition demoSword = null!;
        [SerializeField] private LineRenderer meleeWarning = null!;
        [SerializeField] private LineRenderer rangedWarning = null!;

        private readonly List<RaycastHit2D> wallHits = new List<RaycastHit2D>();
        private readonly TargetCandidate[] enemies = new TargetCandidate[3];
        private readonly List<RunSignal> frameSignals = new List<RunSignal>();
        private readonly List<(DamageRequest Hit, TimeSpan EffectiveAt)> damageEvents = new List<(DamageRequest, TimeSpan)>();
        private readonly PlayerHealth health = new PlayerHealth(100);
        private InventoryService field = null!;
        private RunState run = null!;
        private Rigidbody2D player = null!;
        private Rigidbody2D meleeBody = null!;
        private AutoAttackScheduler attacks = null!;
        private DaggerFlurry? activeFlurry;
        private WoodenSpearThrustEffect spearThrust = null!;
        private SpriteRenderer meleeVisual = null!;
        private SpriteRenderer rangedVisual = null!;
        private PlayerDodge? playerDodge;
        private WeaponDefinition? weapon;
        private Func<TimeSpan> readTime = null!;
        private bool recoveryBlocked;

        public MeleeEnemyActor Melee => melee;
        public RangedEnemyActor Ranged => ranged;
        public BossShockwaveActor Boss => boss;
        public FixedLootChest Chest => chest;
        public PlayerHealth Health => health;
        public int PlayerHealth => health.CurrentHealth;
        public IReadOnlyList<WeaponInstance> Collected => field.Snapshot().Where(item => item.Location.Kind == WeaponLocationKind.Carried).ToArray();
        public IReadOnlyList<WeaponInstance> Held => field.Snapshot();

        internal void Initialize(RunState state, Rigidbody2D playerBody, PlayerDodge? dodge,
            Func<bool> isDodging, Func<TimeSpan> readTime, InventoryService field, WeaponDefinition? activeWeapon)
        {
            run = state;
            spearThrust = new WoodenSpearThrustEffect(run);
            this.readTime = readTime;
            this.field = field ?? throw new ArgumentNullException(nameof(field));
            weapon = activeWeapon != null || field.Snapshot().Count > 0 ? activeWeapon : demoSword;
            player = playerBody;
            playerDodge = dodge;
            meleeBody = melee.GetComponent<Rigidbody2D>();
            meleeVisual = melee.GetComponent<SpriteRenderer>();
            rangedVisual = ranged.GetComponent<SpriteRenderer>();
            chest.BindRun(run);
            pickup.Initialize(new LootPickupCoordinator(field), CanCollect);
            pickup.OutcomeReported += (source, _, result) =>
            {
                if (source && result.Status == InventoryPickupStatus.PickedUp)
                    source!.gameObject.SetActive(false);
            };

            melee.Initialize(MeleeId, meleeSettings);
            ranged.Initialize(RangedId, rangedSettings);
            boss.Initialize(run);
            boss.Boss!.ShockwaveResolved += (_, hits, effectiveAt) => RecordDamage(hits, effectiveAt);
            melee.Brain.AttackResolved += (attack, hits) => RecordDamage(hits, attack.HitsAt);
            ranged.Brain.AttackResolved += (attack, hits) => RecordDamage(hits, attack.HitsAt);
            melee.Brain.Died += _ => meleeVisual.color = Color.gray;
            ranged.Brain.Died += _ => rangedVisual.color = Color.gray;
            attacks = new AutoAttackScheduler(run, TrySwing, initiallyEnabled: true, isDodging);
        }

        /// <summary>Shares the equipped selection and live encounter targets with the room's manual input.</summary>
        public ManualSkillExecutor BindPlayerEquipment(EquippedWeaponSelection equipment, Func<bool> isDodging)
        {
            weapon = equipment.ReadActiveWeapon()?.Definition;
            equipment.ActiveSlotChanged += selected => weapon = selected.Definition;
            return new ManualSkillExecutor(run, equipment, this, isDodging, _ => null, PlayerId);
        }

        public bool IsWeaponActionBusy => spearThrust.IsBusy
            || (run.Phase == RunPhase.Exploring && activeFlurry is { IsFinished: false });

        public static bool SupportsManualSkill(ManualSkillPattern pattern)
            => pattern is OldSwordWideSlashPattern or MaceShockwavePattern or WoodenSpearThrustPattern;

        private IReadOnlyList<TargetCandidate> ReadManualTargets() => ReadManualTargets(player.position);

        private IReadOnlyList<TargetCandidate> ReadManualTargets(Vector2 origin)
        {
            enemies[0] = ReadMeleeTarget(origin);
            enemies[1] = ReadRangedTarget(origin);
            enemies[2] = ReadBossTarget(origin);
            return enemies;
        }

        bool IManualSkillEffect.TryStart(ManualSkillActivationRequest request)
        {
            if (request.Pattern is WoodenSpearThrustPattern)
            {
                if (!spearThrust.TryStart(request))
                    return false;
                AdvanceSpearThrust(request.StartedAt);
                return true;
            }

            IReadOnlyList<DamageRequest>? hits = request.Pattern switch
            {
                OldSwordWideSlashPattern sword when sword.TryCreateStrike(request, ReadManualTargets(), out var slash)
                    => slash.Hits,
                MaceShockwavePattern mace when mace.TryCreateStrike(request, ReadManualTargets(), out var shockwave)
                    => shockwave.Hits,
                _ => null
            };
            if (hits == null)
                return false;

            foreach (DamageRequest hit in hits)
                HitEnemy(hit, request.StartedAt);
            return true;
        }

        public bool TryOpenChest()
        {
            Vector2 position = chest.transform.position;
            return CanCollect() && (player.position - position).sqrMagnitude <= ChestRadius * ChestRadius
                && HasClearPath(player.position, position) && chest.TryOpen(out _);
        }

        internal void SetRecoveryBlocked(bool blocked)
        {
            recoveryBlocked = blocked;
            if (blocked)
                meleeBody.linearVelocity = Vector2.zero;
        }

        internal IReadOnlyList<RunSignal> ObserveAttacks(TimeSpan now)
        {
            if (run.Phase != RunPhase.Exploring)
                activeFlurry = null;
            frameSignals.Clear();
            damageEvents.Clear();
            if (run.Result != null || run.CurrentRun is not { } current)
            {
                meleeBody.linearVelocity = Vector2.zero;
                meleeWarning.enabled = false;
                rangedWarning.enabled = false;
                boss.Advance(now, default);
                return frameSignals;
            }

            Vector2 point = player.position;
            bool alive = health.CurrentHealth > 0;
            bool seesMelee = (point - meleeBody.position).sqrMagnitude <= MeleeDetectionRadius * MeleeDetectionRadius
                && HasClearPath(meleeBody.position, point);
            if (now < current.Deadline || melee.Brain.ActiveAttack.HasValue)
                melee.Advance(now, new TargetCandidate(PlayerId, Point(point), alive, seesMelee));
            if (now < current.Deadline || ranged.Brain.ActiveAttack.HasValue)
                ranged.Advance(now, new TargetCandidate(PlayerId, Point(point), alive,
                    HasClearPath(ranged.transform.position, point)));

            boss.Advance(now, new TargetCandidate(PlayerId, Point(point), alive,
                HasClearPath(boss.transform.position, point)));

            damageEvents.Sort((first, second) => first.EffectiveAt.CompareTo(second.EffectiveAt));
            foreach ((DamageRequest hit, TimeSpan effectiveAt) in damageEvents)
                PlayerRunDamage.ApplyHit(health, run, current.Id, Mathf.RoundToInt(hit.Amount), effectiveAt,
                    frameSignals, playerDodge);
            DrawMeleeWarning();
            DrawRangedWarning();
            return frameSignals;
        }

        internal void AdvancePlayerAttack(TimeSpan now)
        {
            Vector2 origin = player.position;
            AdvanceDagger(now, origin);
            if (AdvanceSpearThrust(now) || spearThrust.IsBusy || !attacks.IsEffective || activeFlurry != null)
                return;

            enemies[0] = ReadMeleeTarget(origin);
            enemies[1] = ReadRangedTarget(origin);
            enemies[2] = ReadBossTarget(origin);
            attacks.TryAttack(now, Point(origin), weapon, enemies);
        }

        private TargetCandidate ReadMeleeTarget(Vector2 origin)
            => new TargetCandidate(MeleeId, Point(meleeBody.position), melee.Brain.CurrentHealth > 0,
                HasClearPath(origin, meleeBody.position));

        private TargetCandidate ReadRangedTarget(Vector2 origin)
        {
            Vector2 position = ranged.transform.position;
            return new TargetCandidate(RangedId, Point(position), ranged.Brain.CurrentHealth > 0,
                HasClearPath(origin, position));
        }

        private TargetCandidate ReadBossTarget(Vector2 origin)
        {
            Vector2 position = boss.transform.position;
            BossShockwave target = boss.Boss!;
            return new TargetCandidate(target.TargetId, Point(position), !target.IsDead, HasClearPath(origin, position));
        }

        private bool AdvanceSpearThrust(TimeSpan now)
        {
            bool resolved = false;
            while (spearThrust.NextThrustAt is { } due && due <= now && spearThrust.PendingOrigin is { } lockedOrigin
                && spearThrust.TryResolveNext(now, ReadManualTargets(new Vector2(lockedOrigin.X, lockedOrigin.Y)),
                    out WoodenSpearStrike strike))
            {
                resolved = true;
                if (strike.Hit is DamageRequest hit)
                    HitEnemy(hit, strike.EffectiveAt);
            }
            return resolved;
        }

        private void AdvanceDagger(TimeSpan now, Vector2 origin)
        {
            if (activeFlurry is not { } flurry)
                return;

            while (flurry.NextHitAt is { } due)
            {
                TargetCandidate target = default;
                if (now >= due)
                    target = flurry.TargetId switch
                    {
                        MeleeId => ReadMeleeTarget(origin),
                        RangedId => ReadRangedTarget(origin),
                        _ when StringComparer.Ordinal.Equals(flurry.TargetId, boss.Boss!.TargetId) => ReadBossTarget(origin),
                        _ => default
                    };

                if (!flurry.TryResolveNext(run, now, Point(origin), target, out DaggerStrike strike))
                    break;
                if (strike.Hit is DamageRequest hit)
                    HitEnemy(hit, strike.EffectiveAt);
            }

            if (flurry.IsFinished)
                activeFlurry = null;
        }

        private bool TrySwing(BasicAttackRequest attack)
        {
            if (attack.Pattern is DaggerFlurryPattern dagger)
            {
                if (!dagger.TryCreateFlurry(attack, PlayerId, out activeFlurry))
                    return false;
                AdvanceDagger(attack.StartedAt, new Vector2(attack.Origin.X, attack.Origin.Y));
                return true;
            }

            Guid id = Guid.NewGuid();
            if (attack.Pattern is SpearThrustPattern spear && spear.TryCreateThrust(attack, id, PlayerId, out SpearThrust thrust))
            {
                HitEnemy(thrust.Hit, thrust.EffectiveAt);
                return true;
            }

            IReadOnlyList<DamageRequest>? hits = attack.Pattern switch
            {
                MeleeSlashPattern sword when sword.TryCreateSwing(attack, id, PlayerId, enemies, out MeleeSlashSwing? swing)
                    => swing!.Hits,
                CircularStrikePattern mace when mace.TryCreateStrike(attack, id, PlayerId, enemies, out CircularStrike? strike)
                    => strike!.Hits,
                _ => null
            };
            if (hits == null)
                return false;

            foreach (DamageRequest hit in hits)
                HitEnemy(hit, attack.StartedAt);
            return true;
        }

        private void HitEnemy(DamageRequest hit, TimeSpan effectiveAt)
        {
            if (StringComparer.Ordinal.Equals(hit.TargetId, MeleeId))
                melee.ApplyDamage(hit, effectiveAt);
            else if (StringComparer.Ordinal.Equals(hit.TargetId, RangedId))
                ranged.ApplyDamage(hit, effectiveAt);
            else if (StringComparer.Ordinal.Equals(hit.TargetId, boss.Boss!.TargetId))
                boss.ApplyDamage(hit, effectiveAt);
        }

        private bool CanCollect() => !recoveryBlocked && run.Phase == RunPhase.Exploring
            && run.CurrentRun is { } current && readTime() < current.Deadline;

        private void RecordDamage(IReadOnlyList<DamageRequest> hits, TimeSpan effectiveAt)
        {
            foreach (DamageRequest hit in hits)
                damageEvents.Add((hit, effectiveAt));
        }

        private bool HasClearPath(Vector2 origin, Vector2 target)
        {
            Vector2 offset = target - origin;
            if (offset.sqrMagnitude == 0)
                return true;

            var filter = new ContactFilter2D { useTriggers = false };
            gameObject.scene.GetPhysicsScene2D().Raycast(origin, offset, offset.magnitude, filter, wallHits);
            foreach (RaycastHit2D hit in wallHits)
                if (hit.collider == walls)
                    return false;
            return true;
        }

        private void DrawMeleeWarning()
        {
            meleeWarning.enabled = melee.Brain.ActiveAttack.HasValue;
            if (melee.Brain.ActiveAttack is not { } attack)
                return;

            const int segments = 16;
            AreaDamageRequest area = attack.Area;
            meleeWarning.positionCount = segments + 2;
            meleeWarning.SetPosition(0, Vector(area.Origin));
            float halfAngle = area.AngleDegrees * Mathf.Deg2Rad * .5f;
            float direction = Mathf.Atan2(area.Direction.Y, area.Direction.X);
            for (int i = 0; i <= segments; i++)
            {
                float angle = direction - halfAngle + 2f * halfAngle * i / segments;
                meleeWarning.SetPosition(i + 1, new Vector3(area.Origin.X + Mathf.Cos(angle) * area.Radius,
                    area.Origin.Y + Mathf.Sin(angle) * area.Radius));
            }
        }

        private void DrawRangedWarning()
        {
            rangedWarning.enabled = ranged.Brain.ActiveAttack.HasValue;
            if (ranged.Brain.ActiveAttack is not { } attack)
                return;

            RangedAttackStrip strip = attack.Area;
            rangedWarning.positionCount = 4;
            rangedWarning.SetPosition(0, Vector(strip.GetCorner(false, true)));
            rangedWarning.SetPosition(1, Vector(strip.GetCorner(true, true)));
            rangedWarning.SetPosition(2, Vector(strip.GetCorner(true, false)));
            rangedWarning.SetPosition(3, Vector(strip.GetCorner(false, false)));
        }

        private static WorldPoint2D Point(Vector2 point) => new WorldPoint2D(point.x, point.y);
        private static Vector3 Vector(WorldPoint2D point) => new Vector3(point.X, point.Y);
    }
}
