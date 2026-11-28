#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.AutoAttack;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Extraction;
using ProjectWeapon.Movement;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectWeapon.Zone5Sanctum
{
    /// <summary>Runs the authored sanctum encounter as an unsaved local trial.</summary>
    [DisallowMultipleComponent]
    public sealed class Zone5SanctumTrial : MonoBehaviour
    {
        private const string PlayerId = "sanctum-player";

        [SerializeField] private ZoneCatalog zones = null!;
        [SerializeField] private Rigidbody2D playerBody = null!;
        [SerializeField] private Collider2D walls = null!;
        [SerializeField] private CircleCollider2D returnArea = null!;
        [SerializeField] private WeaponDefinition validationSword = null!;
        [SerializeField] private SanctumBossActor boss = null!;

        private readonly PlayerHealth health = new PlayerHealth(100);
        private readonly List<RunSignal> signals = new List<RunSignal>();
        private readonly TargetCandidate[] targets = new TargetCandidate[1];
        private RunState run = null!;
        private RunId runId;
        private ZoneRunCountdown countdown = null!;
        private ExtractionAccess extraction = null!;
        private PlayerMovement movement = null!;
        private AutoAttackScheduler attacks = null!;
        private InputAction? interact;
        private Func<TimeSpan> readClock = null!;

        public RunState Run => run;
        public SanctumBossActor Boss => boss;
        public int PlayerHealth => health.CurrentHealth;

        public void ConfigureClock(Func<TimeSpan> clock)
        {
            if (run != null)
                throw new InvalidOperationException("Configure the clock before starting the sanctum trial.");
            readClock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        private void Start()
        {
            double startedAt = Time.timeAsDouble;
            readClock ??= () => TimeSpan.FromSeconds(Time.timeAsDouble - startedAt);
            run = new RunState(readClock);
            runId = RunId.New();
            countdown = new ZoneRunCountdown(zones, run);
            if (!countdown.TryStartRun(runId, SanctumBossActor.ZoneId))
                throw new InvalidOperationException("The sanctum trial could not start.");

            movement = playerBody.GetComponent<PlayerMovement>();
            Vector2 center = returnArea.transform.TransformPoint(returnArea.offset);
            extraction = new ExtractionAccess(run, SanctumBossActor.ZoneId, Point(center), returnArea.radius);
            interact = InputSystem.actions?.FindAction("Player/Interact");
            attacks = new AutoAttackScheduler(run, TrySwing, initiallyEnabled: true, () => movement.IsDodging);
            boss.Bind(run, PlayerId, playerBody, health, walls);
        }

        private void Update()
        {
            if (run.Result != null)
                return;

            TimeSpan now = readClock();
            Vector2 position = playerBody.position;
            float move = movement.SampleDirection().magnitude;
            if (interact?.WasPressedThisFrame() == true)
                extraction.TryStart(runId, Point(position), move);
            bool dodged = Keyboard.current?.spaceKey.wasPressedThisFrame == true && movement.TryDodge(run, now);

            signals.Clear();
            if (boss.Advance(now) is { } impact)
                foreach (DamageRequest hit in impact.Hits)
                    PlayerRunDamage.ApplyHit(health, run, runId, Mathf.RoundToInt(hit.Amount),
                        impact.Attack.HitsAt, signals, movement.Dodge);
            if (dodged && countdown.Extraction.ObserveAcceptedDodge(runId, now) is RunSignal dodgeCancel)
                signals.Add(dodgeCancel);
            if (countdown.Extraction.ObserveMovement(move, now) is RunSignal moveCancel)
                signals.Add(moveCancel);
            countdown.ProcessFrame(signals);

            if (run.Result != null)
                movement.enabled = false;
            else
            {
                targets[0] = boss.ReadTarget(position);
                attacks.TryAttack(now, Point(position), validationSword, targets);
            }
        }

        private bool TrySwing(BasicAttackRequest request)
        {
            if (request.Pattern is not MeleeSlashPattern sword
                || !sword.TryCreateSwing(request, Guid.NewGuid(), PlayerId, targets, out MeleeSlashSwing? swing))
                return false;

            foreach (DamageRequest hit in swing!.Hits)
                boss.ApplyDamage(hit, request.StartedAt);
            return true;
        }

        private static WorldPoint2D Point(Vector2 value) => new WorldPoint2D(value.x, value.y);
    }
}
