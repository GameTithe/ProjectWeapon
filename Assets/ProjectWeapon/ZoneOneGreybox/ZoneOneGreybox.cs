#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Extraction;
using ProjectWeapon.Inventory;
using ProjectWeapon.Movement;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectWeapon.ZoneOneGreybox
{
    [DisallowMultipleComponent]
    public sealed class ZoneOneGreybox : MonoBehaviour
    {
        [SerializeField] private ZoneCatalog catalog = null!;
        [SerializeField] private Rigidbody2D player = null!;
        [SerializeField] private SpriteRenderer extractionMarker = null!;
        [SerializeField, Min(0.01f)] private float extractionRadius = 0.75f;

        [SerializeField] private ZoneOneEncounter encounter = null!;

        private RunId runId;
        private RunState run = null!;
        private ZoneRunCountdown countdown = null!;
        private ExtractionAccess access = null!;
        private InputAction? interact;
        private readonly List<RunSignal> frameSignals = new List<RunSignal>();
        private PlayerMovement? movement;
        private Func<TimeSpan> readTime = null!;
        private bool recoveryBlocked;
        private bool restoreMovement;

        public ZoneDefinition Zone => catalog.GetZone(1);
        public Rigidbody2D Player => player;
        public ZoneOneEncounter Encounter => encounter;
        public Vector2 ExtractionCenter => extractionMarker.transform.position;
        public float ExtractionRadius => extractionRadius;
        public RunPhase Phase => run.Phase;
        public RunState Run => run;
        public bool CanStartExtraction => !recoveryBlocked && access.CanStart(runId, PlayerPoint());

        private void Start()
        {
            if (run != null)
                return;

            double startedAt = Time.timeAsDouble;
            TimeSpan Clock() => TimeSpan.FromSeconds(Time.timeAsDouble - startedAt);
            var preview = new RunState(Clock);
            var timer = new ZoneRunCountdown(catalog, preview);
            timer.TryStartRun(RunId.New(), Zone.ZoneId);
            BindRun(preview, timer, Clock, new InventoryService(Array.Empty<WeaponInstance>()), null);
        }

        /// <summary>Called by the lobby on sceneLoaded, before this room starts its standalone preview.</summary>
        public void BindRun(RunState controlled, ZoneRunCountdown timer, Func<TimeSpan> clock,
            InventoryService field, WeaponDefinition? activeWeapon)
        {
            if (run != null)
                throw new InvalidOperationException("This instance already owns a run.");
            if (controlled?.CurrentRun is not { } session || session.ZoneId != Zone.ZoneId || controlled.Result != null)
                throw new ArgumentException("An active run in this zone is required.", nameof(controlled));

            run = controlled;
            countdown = timer ?? throw new ArgumentNullException(nameof(timer));
            readTime = clock ?? throw new ArgumentNullException(nameof(clock));
            runId = session.Id;
            Vector2 center = ExtractionCenter;
            access = new ExtractionAccess(run, Zone.ZoneId, new WorldPoint2D(center.x, center.y), extractionRadius);
            interact = InputSystem.actions?.FindAction("Player/Interact");
            movement = player.GetComponent<PlayerMovement>();
            if (encounter)
                encounter.Initialize(run, player, movement?.Dodge, () => movement?.IsDodging == true,
                    readTime, field, activeWeapon);
        }

        public void StopMovement()
        {
            if (movement != null)
                movement.enabled = false;
            player.linearVelocity = Vector2.zero;
        }

        public void SetRecoveryBlocked(bool blocked)
        {
            if (recoveryBlocked == blocked)
                return;

            recoveryBlocked = blocked;
            if (encounter)
                encounter.SetRecoveryBlocked(blocked);
            if (blocked)
            {
                restoreMovement = movement != null && movement.enabled;
                StopMovement();
            }
            else if (restoreMovement && movement != null && run.Result == null)
            {
                movement.enabled = true;
            }
        }

        private void Update()
        {
            if (recoveryBlocked)
                return;

            TimeSpan now = readTime();
            float move = movement?.SampleDirection().magnitude ?? 0f;
            bool wantsChest = interact?.WasPressedThisFrame() == true
                && access.TryStart(runId, PlayerPoint(), move) == null;

            bool dodged = Keyboard.current?.spaceKey.wasPressedThisFrame == true
                && movement?.TryDodge(run, now) == true;
            frameSignals.Clear();
            if (encounter)
                frameSignals.AddRange(encounter.ObserveAttacks(now));
            if (dodged && countdown.Extraction.ObserveAcceptedDodge(runId, now) is RunSignal dodgeCancel)
                frameSignals.Add(dodgeCancel);
            if (countdown.Extraction.ObserveMovement(move, now) is RunSignal moveCancel)
                frameSignals.Add(moveCancel);
            countdown.ProcessFrame(frameSignals);
            if (wantsChest && encounter)
                encounter.TryOpenChest();
            if (encounter)
                encounter.AdvancePlayerAttack(now);
            extractionMarker.color = run.Phase switch
            {
                RunPhase.Succeeded => new Color(0.62f, 1f, 0.64f),
                RunPhase.Extracting => new Color(0.90f, 0.95f, 1f),
                _ => CanStartExtraction ? Color.white : new Color(0.60f, 0.68f, 0.78f)
            };
        }

        public bool TryStartExtraction() => !recoveryBlocked && access.TryStart(runId, PlayerPoint()) != null;

        private WorldPoint2D PlayerPoint()
        {
            Vector2 position = player.position;
            return new WorldPoint2D(position.x, position.y);
        }

        private void OnDrawGizmosSelected()
        {
            if (extractionMarker == null)
                return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(extractionMarker.transform.position, extractionRadius);
        }
    }
}
