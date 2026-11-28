#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.ManualSkill.Tests
{
    public sealed class ManualSkillExecutorTests
    {
        private static readonly WorldPoint2D Origin = new WorldPoint2D(0, 0);
        private static readonly ManualSkillAim Self = ManualSkillAim.AtSelf();

        private readonly List<Object> created = new List<Object>();
        private TimeSpan clock;
        private bool busy;
        private bool dodging;
        private RunState run = null!;
        private RunId runId;
        private InventoryService inventory = null!;
        private WeaponInstance first = null!;
        private WeaponInstance second = null!;
        private WeaponActionLocks locks = null!;
        private EquippedWeaponSelection selection = null!;
        private ManualSkillExecutor executor = null!;
        private ManualSkillPattern pattern = null!;
        private WeaponDefinition weapon = null!;
        private RecordingEffect effect = null!;
        private TargetCandidate? liveEnemy;

        [SetUp]
        public void SetUp()
        {
            busy = dodging = false;
            clock = TimeSpan.Zero;
            run = new RunState(() => clock);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(5000)), Is.True);
            pattern = Create<ManualSkillPattern>();
            weapon = Create<WeaponDefinition>();
            var fields = new SerializedObject(weapon);
            fields.FindProperty("definitionId").stringValue = "test_manual_skill";
            fields.FindProperty("displayName").stringValue = "Test weapon";
            fields.FindProperty("kind").intValue = (int)WeaponKind.Sword;
            fields.FindProperty("skill").objectReferenceValue = pattern;
            fields.FindProperty("basicAttack").objectReferenceValue = pattern;
            fields.ApplyModifiedPropertiesWithoutUndo();
            var catalog = Create<WeaponCatalog>();
            var definitions = new SerializedObject(catalog);
            var entries = definitions.FindProperty("definitions");
            entries.arraySize = 1;
            entries.GetArrayElementAtIndex(0).objectReferenceValue = weapon;
            definitions.ApplyModifiedPropertiesWithoutUndo();
            first = WeaponInstance.Create(weapon.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            second = WeaponInstance.Create(weapon.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            inventory = new InventoryService(new[] { first, second });
            locks = new WeaponActionLocks();
            selection = new EquippedWeaponSelection(run, inventory, catalog, () => busy, locks);
            effect = new RecordingEffect();
            liveEnemy = null;
            executor = new ManualSkillExecutor(run, selection, effect, () => dodging, _ => liveEnemy, "player");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created)
                Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void AcceptedSkillLocksSharedAndCapturedInstanceAtTheSameTimestamp()
        {
            Guid gesture = Guid.NewGuid();
            Assert.That(Activate(first, 100, Self, gesture), Is.EqualTo(ManualSkillActivationStatus.Started));
            ManualSkillActivationRequest request = effect.Requests[0];
            Assert.That(request.Weapon.InstanceId, Is.EqualTo(first.Id));
            Assert.That(request.Pattern, Is.SameAs(pattern));
            Assert.That(request.RunId, Is.EqualTo(runId));
            Assert.That(request.GestureId, Is.EqualTo(gesture));
            Assert.That(request.StartedAt, Is.EqualTo(Ms(100)));
            Assert.That(request.AttackerId, Is.EqualTo("player"));
            Assert.That(ReadyAt(first), Is.EqualTo(Ms(1600)));
            Assert.That(ReadyAt(second), Is.EqualTo(Ms(900)));
            Assert.That(locks.ReadyAt(CombatWeaponAction.BasicAttack, first.Id), Is.EqualTo(TimeSpan.Zero));
            Assert.That(Activate(first, 150, Self, gesture), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));

            Assert.That(selection.TrySwitch(1, Ms(150)), Is.True);
            Assert.That(Activate(second, 899, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(second, 900, Self), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.Requests[1].Weapon.InstanceId, Is.EqualTo(second.Id));
            Assert.That(ReadyAt(second), Is.EqualTo(Ms(2400)));
            Assert.That(ReadyAt(first), Is.EqualTo(Ms(1700)));
        }

        [Test]
        public void SwitchingBetweenAimAndReleaseCannotFireAnotherInstance()
        {
            Assert.That(selection.TrySwitch(1, Ms(30)), Is.True);
            Guid stale = Guid.NewGuid();
            Assert.That(Activate(first, 50, Self, stale), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(selection.TrySwitch(0, Ms(51)), Is.True);
            Assert.That(Activate(first, 52, Self, stale), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(effect.Requests, Is.Empty);
            Assert.That(ReadyAt(first), Is.EqualTo(TimeSpan.Zero));
            Assert.That(inventory.Move(first.Id, WeaponLocation.Equipped(0), WeaponLocation.Warehouse()),
                Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(Activate(first, 60, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
        }

        [Test]
        public void RejectionAndCanceledGesturePreserveExistingCooldownsAndCannotBeRetried()
        {
            locks.RecordAccepted(CombatWeaponAction.Skill, first.Id, Ms(40), Ms(60));
            Assert.That(pattern.FailurePolicy, Is.EqualTo(ManualSkillFailurePolicy.PreserveResourcesAndCooldowns));
            Guid canceled = Guid.NewGuid();
            Assert.That(executor.Cancel(canceled), Is.EqualTo(ManualSkillActivationStatus.Canceled));
            Assert.That(Activate(first, 100, Self, canceled), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(executor.Cancel(canceled), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(effect.Requests, Is.Empty);

            Guid denied = Guid.NewGuid();
            effect.Start = _ => false;
            Assert.That(Activate(first, 100, Self, denied), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            Assert.That(ReadyAt(first), Is.EqualTo(Ms(60)));
            Assert.That(ReadyAt(second), Is.EqualTo(Ms(40)));
            effect.Start = _ => true;
            Assert.That(Activate(first, 100, Self, denied), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(Activate(first, 100, Self), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.Requests, Has.Count.EqualTo(2));
        }

        [Test]
        public void ReentrantEffectCannotSelectAnotherWeaponOrStartAnotherAction()
        {
            effect.Start = request =>
            {
                Assert.That(selection.TrySwitch(1, request.StartedAt), Is.False);
                Assert.That(selection.TryGetReadyWeapon(CombatWeaponAction.BasicAttack, request.StartedAt, out _), Is.False);
                Assert.That(Activate(first, 250, Self, request.GestureId),
                    Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
                Assert.That(Activate(first, 250, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
                return true;
            };
            Assert.That(Activate(first, 250, Self), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.Requests, Has.Count.EqualTo(1));
            Assert.That(ReadyAt(first), Is.EqualTo(Ms(1750)));
            Assert.That(ReadyAt(second), Is.EqualTo(Ms(1050)));
            Assert.That(selection.TrySwitch(1, Ms(250)), Is.True);
        }

        [Test]
        public void BusyDodgeExtractionOldRunAndExactDeadlineDenyWithoutLocks()
        {
            busy = true;
            Assert.That(Activate(first, 30, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            busy = false;
            dodging = true;
            Assert.That(Activate(first, 40, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            dodging = false;
            clock = Ms(50);
            var attempt = run.TryBeginExtraction(runId, isInRange: true)!;
            Assert.That(Activate(first, 50, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(first, 5000, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            clock = Ms(51);
            run.ProcessSignals(new[] { RunSignal.CancelExtraction(runId, attempt.Id, clock) });
            Assert.That(Activate(first, 5000, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            clock = Ms(5000);
            Assert.That(run.ProcessSignals(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(run.TryReturnToTown(runId), Is.True);
            Assert.That(run.TryStartRun(RunId.New(), 1, Ms(5000)), Is.True);
            Assert.That(Activate(first, 5001, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(effect.Requests, Is.Empty);
            Assert.That(ReadyAt(first), Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void EnemyResolutionUsesCurrentValidIdentityAndAcceptsExactRange()
        {
            SetAim(ManualSkillAimKind.Enemy);
            Guid stale = Guid.NewGuid();
            Assert.That(Activate(first, 100, ManualSkillAim.AtEnemy("enemy"), stale),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            liveEnemy = new TargetCandidate("enemy", new WorldPoint2D(4, 0), false, true);
            Assert.That(Activate(first, 100, ManualSkillAim.AtEnemy("enemy")),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            liveEnemy = new TargetCandidate("enemy", new WorldPoint2D(4.01f, 0), true, true);
            Assert.That(Activate(first, 100, ManualSkillAim.AtEnemy("enemy")),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            liveEnemy = new TargetCandidate("other", new WorldPoint2D(1, 0), true, true);
            Assert.That(Activate(first, 100, ManualSkillAim.AtEnemy("enemy")),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            liveEnemy = new TargetCandidate("enemy", new WorldPoint2D(4, 0), true, true);
            Assert.That(Activate(first, 100, ManualSkillAim.AtEnemy("enemy"), stale),
                Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(Activate(first, 100, ManualSkillAim.AtEnemy("enemy")),
                Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.Requests[0].Target?.Position.X, Is.EqualTo(4f));
        }

        [Test]
        public void DirectionAimMustBeNonzeroAndIsNormalizedBeforeDispatch()
        {
            SetAim(ManualSkillAimKind.Direction);
            Assert.That(Activate(first, 100, Self), Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Activate(first, 100, ManualSkillAim.Along(new WorldPoint2D(0, 0))),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Activate(first, 100, ManualSkillAim.Along(new WorldPoint2D(float.NaN, 1))),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Activate(first, 100, ManualSkillAim.Along(new WorldPoint2D(3, 4))),
                Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.Requests[0].Aim.Vector.X, Is.EqualTo(.6f));
            Assert.That(effect.Requests[0].Aim.Vector.Y, Is.EqualTo(.8f));
        }

        [Test]
        public void GroundRangeAndStaleSamplesAreValidatedBeforeTheEffect()
        {
            SetAim(ManualSkillAimKind.Ground);
            Assert.That(Activate(first, 100, ManualSkillAim.AtGround(new WorldPoint2D(1, float.PositiveInfinity))),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Activate(first, 100, ManualSkillAim.AtGround(new WorldPoint2D(4.001f, 0))),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Activate(first, 99, ManualSkillAim.AtGround(new WorldPoint2D(4, 0))),
                Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(first, 100, ManualSkillAim.AtGround(new WorldPoint2D(4, 0))),
                Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.Requests[0].Aim.Vector.X, Is.EqualTo(4f));
        }

        [Test]
        public void EffectExceptionReleasesTheDispatchGuardWithoutRecordingCooldown()
        {
            Guid gesture = Guid.NewGuid();
            effect.Start = _ => throw new InvalidOperationException("Effect did not start.");
            Assert.Throws<InvalidOperationException>(() => Activate(first, 100, Self, gesture));
            Assert.That(ReadyAt(first), Is.EqualTo(TimeSpan.Zero));
            Assert.That(ReadyAt(second), Is.EqualTo(TimeSpan.Zero));
            Assert.That(selection.TrySwitch(1, Ms(100)), Is.True);
            Assert.That(Activate(second, 100, Self, gesture), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            effect.Start = _ => true;
            Assert.That(Activate(second, 100, Self), Is.EqualTo(ManualSkillActivationStatus.Started));
        }

        [Test]
        public void SkillCannotStartFromTheSwitchEventOrASelectionForAnotherRun()
        {
            ManualSkillActivationStatus? reentrant = null;
            selection.ActiveSlotChanged += changed =>
            {
                reentrant = Activate(second, 100, Self);
                Assert.That(selection.TrySwitch(0, Ms(100)), Is.False);
                Assert.That(selection.ReadActiveWeapon()?.InstanceId, Is.EqualTo(changed.InstanceId));
            };
            Assert.That(selection.TrySwitch(1, Ms(100)), Is.True);
            Assert.That(reentrant, Is.EqualTo(ManualSkillActivationStatus.Unavailable));

            var otherRun = new RunState(() => Ms(100));
            Assert.That(otherRun.TryStartRun(RunId.New(), 1, Ms(5000)), Is.True);
            var wrongRunner = new ManualSkillExecutor(otherRun, selection, effect, () => false, _ => null, "player");
            Assert.That(wrongRunner.TryActivate(Guid.NewGuid(), second.Id, Ms(100), Origin, Self),
                Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(effect.Requests, Is.Empty);
            Assert.That(Activate(second, 100, Self), Is.EqualTo(ManualSkillActivationStatus.Started));
        }

        [Test]
        public void MissingOrInvalidSkillNeverDispatches()
        {
            ApplySkillToWeapon(null);
            Assert.That(Activate(first, 100, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            ApplySkillToWeapon(pattern);
            var config = new SerializedObject(pattern);
            config.FindProperty("failurePolicy").intValue = 999;
            config.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(Activate(first, 100, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(effect.Requests, Is.Empty);
            Assert.That(ReadyAt(first), Is.EqualTo(TimeSpan.Zero));
        }

        private ManualSkillActivationStatus Activate(WeaponInstance item, int milliseconds, ManualSkillAim aim,
            Guid? gestureId = null)
            => executor.TryActivate(gestureId ?? Guid.NewGuid(), item.Id, Ms(milliseconds), Origin, aim);

        private TimeSpan ReadyAt(WeaponInstance item) => locks.ReadyAt(CombatWeaponAction.Skill, item.Id);

        private void SetAim(ManualSkillAimKind kind)
        {
            var fields = new SerializedObject(pattern);
            fields.FindProperty("aimKind").intValue = (int)kind;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        private void ApplySkillToWeapon(ManualSkillPattern? skill)
        {
            var fields = new SerializedObject(weapon);
            fields.FindProperty("skill").objectReferenceValue = skill;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        private T Create<T>() where T : ScriptableObject
        {
            T item = ScriptableObject.CreateInstance<T>();
            created.Add(item);
            return item;
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

        private sealed class RecordingEffect : IManualSkillEffect
        {
            public List<ManualSkillActivationRequest> Requests { get; } = new List<ManualSkillActivationRequest>();
            public Func<ManualSkillActivationRequest, bool> Start { get; set; } = _ => true;

            public bool TryStart(ManualSkillActivationRequest request)
            {
                Requests.Add(request);
                return Start(request);
            }
        }
    }
}
