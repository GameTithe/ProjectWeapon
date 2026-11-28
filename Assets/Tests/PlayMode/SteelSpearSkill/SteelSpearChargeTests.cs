#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Enemies;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Movement;
using ProjectWeapon.Player;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectWeapon.SteelSpearSkill.Tests
{
    public sealed class SteelSpearChargeTests
    {
        private const string Asset = "Assets/ProjectWeapon/SteelSpearSkill/SteelSpearCharge.asset";
        private const string WeaponPath = "Assets/ProjectWeapon/Weapons/PrototypeSteelSpear/SteelSpear.asset";
        private const string BasicPath = "Assets/ProjectWeapon/Weapons/PrototypeSteelSpear/SteelSpearPiercingThrust.asset";
        private const string PlayerPrefab = "Assets/ProjectWeapon/Movement/PrototypeMovementPlayer.prefab";
        private static readonly ManualSkillAim Right = ManualSkillAim.Along(new WorldPoint2D(7, 0));
        private readonly List<SteelSpearChargeImpact> impacts = new List<SteelSpearChargeImpact>();
        private readonly List<ScriptableObject> temporary = new List<ScriptableObject>();
        private IReadOnlyList<TargetCandidate> candidates = Array.Empty<TargetCandidate>();
        private Scene scene;
        private Rigidbody2D body = null!;
        private CircleCollider2D shape = null!;
        private BoxCollider2D wall = null!;
        private PlayerMovement movement = null!;
        private WeaponDefinition steelSpear = null!;
        private SteelSpearChargePattern pattern = null!;
        private RunState run = null!;
        private RunId runId;
        private WeaponInstance first = null!;
        private WeaponInstance second = null!;
        private EquippedWeaponSelection selection = null!;
        private WeaponActionLocks locks = null!;
        private ManualSkillExecutor executor = null!;
        private SteelSpearChargeEffect effect = null!;
        private WorldPoint2D sample;
        private TimeSpan now;
        private int samples;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            steelSpear = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
            Assert.That(steelSpear, Is.Not.Null);
            pattern = AssetDatabase.LoadAssetAtPath<SteelSpearChargePattern>(Asset);
            Assert.That(pattern, Is.Not.Null);
            scene = SceneManager.CreateScene("SteelSpear_" + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.That(prefab, Is.Not.Null);
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            body = actor.GetComponent<Rigidbody2D>();
            shape = actor.GetComponent<CircleCollider2D>();
            movement = actor.GetComponent<PlayerMovement>();
            movement.enabled = false; // The fixture grants this position authority exclusively to the charge.
            body.position = Vector2.zero;

            var barrier = new GameObject("Solid test wall");
            SceneManager.MoveGameObjectToScene(barrier, scene);
            wall = barrier.AddComponent<BoxCollider2D>();
            wall.size = new Vector2(.2f, 4f);
            PlaceWall(10);
            now = TimeSpan.Zero;
            samples = 0;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Clean()
        {
            foreach (ScriptableObject asset in temporary)
                Object.DestroyImmediate(asset);
            temporary.Clear();
            impacts.Clear();
            candidates = Array.Empty<TargetCandidate>();
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void EquippedCatalogSteelSpearMovesTheActualColliderThenAppliesOneNearestThrust()
        {
            StartFixture();
            Assert.That(selection.ReadActiveWeapon()!.Value.Definition, Is.SameAs(steelSpear));
            Assert.That(steelSpear.Id.Value, Is.EqualTo("steel_spear"));
            Assert.That(steelSpear.Grade, Is.EqualTo(2));
            Assert.That(steelSpear.Kind, Is.EqualTo(WeaponKind.Spear));
            Assert.That(AssetDatabase.GetAssetPath(steelSpear.BasicAttack), Is.EqualTo(BasicPath));
            Assert.That(steelSpear.Skill, Is.SameAs(pattern));
            Guid gesture = Guid.NewGuid();
            Assert.That(Activate(100, gesture), Is.EqualTo(ManualSkillActivationStatus.Started));
            var charge = effect.PendingCharge!.Value;
            Assert.That(charge.Origin, Is.EqualTo(default(WorldPoint2D)));
            Assert.That(charge.Direction, Is.EqualTo(new WorldPoint2D(1, 0)));
            Assert.That(charge.Distance, Is.EqualTo(2.4f));
            Assert.That(charge.ThrustAt, Is.EqualTo(Ms(280)));
            Assert.That(charge.WeaponInstanceId, Is.EqualTo(first.Id));
            Assert.That(body.position, Is.EqualTo(Vector2.zero));

            Assert.That(Advance(190), Is.False);
            Assert.That(body.position.x, Is.EqualTo(1.2f).Within(.0001));
            Assert.That(samples, Is.Zero);
            Assert.That(selection.TrySwitch(1, Ms(190)), Is.False);
            Assert.That(movement.Dodge.IsInvulnerable(runId, Ms(190)), Is.False);
            var health = new PlayerHealth(100);
            Assert.That(PlayerRunDamage.ApplyHit(health, run, runId, 1, Ms(190), new List<RunSignal>(), movement.Dodge), Is.True);

            Assert.That(Advance(279), Is.False);
            Assert.That(body.position.x, Is.LessThan(2.4f));
            Assert.That(impacts, Is.Empty);
            candidates = new[]
            {
                At("dead", 2.8f, alive: false), At("behind", 2.2f), At("self", 2.5f),
                At("hidden", 2.7f, attackable: false), At("side", 2.7f, .4f),
                At("zeta", 3.7f), At("alpha", 3.7f), At("alpha", 3.7f),
                At("farther", 4.6f), At("out-of-reach", 4.601f)
            };
            Assert.That(Advance(280), Is.True);
            Assert.That(body.position.x, Is.EqualTo(2.4f).Within(.0001));
            Assert.That(body.position.y, Is.Zero);
            Assert.That(samples, Is.EqualTo(1));
            SteelSpearChargeImpact impact = impacts[0];
            Assert.That(impact.ObservedAt, Is.EqualTo(Ms(280)));
            Assert.That(impact.Area.Origin, Is.EqualTo(sample));
            Assert.That(impact.Area.Origin.X, Is.EqualTo(body.position.x));
            Assert.That(impact.Area.Radius, Is.EqualTo(2.2f));
            Assert.That(impact.Area.AngleDegrees, Is.EqualTo(45f));
            Assert.That(impact.Hit?.TargetId, Is.EqualTo("alpha"));
            var enemySettings = Temporary<MeleeEnemySettings>();
            var settings = new SerializedObject(enemySettings);
            settings.FindProperty("maxHealth").floatValue = 100;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var enemy = new MeleeEnemy("alpha", enemySettings);
            DamageRequest hit = impact.Hit!.Value;
            Assert.That(hit.ApplicationId, Is.EqualTo(gesture));
            Assert.That(hit.Amount, Is.EqualTo(44));
            Assert.That(enemy.ApplyDamage(hit, impact.ObservedAt), Is.True);
            Assert.That(enemy.ApplyDamage(hit, impact.ObservedAt), Is.False);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(56));
            Assert.That(Advance(281), Is.False);
            Assert.That(impacts, Has.Count.EqualTo(1));
        }

        [Test]
        public void SweepStopsAtWallWithTheRealHullAndTheThrustCannotPassThroughIt()
        {
            PlaceWall(1.9f);
            StartFixture();
            Assert.That(shape.radius, Is.EqualTo(.34f));
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(250), Is.False);
            float stop = body.position.x;
            Assert.That(stop, Is.EqualTo(1.45f).Within(.012));
            Assert.That(scene.GetPhysicsScene2D().OverlapPoint(new Vector2(1.9f, 0)), Is.SameAs(wall));

            candidates = new[] { At("behind-wall", 2f), At("clear", 1.66f, .02f) };
            Assert.That(Advance(280), Is.True);
            Assert.That(body.position.x, Is.EqualTo(stop));
            Assert.That(impacts[0].Area.Origin.X, Is.EqualTo(stop));
            Assert.That(impacts[0].Hit?.TargetId, Is.EqualTo("clear"));
        }

        [Test]
        public void ACurrentWedgeEdgeCanHitFromTheStopAndOutsideAngleMisses()
        {
            StartFixture();
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            float angle = 22.51f * Mathf.Deg2Rad;
            candidates = new[]
            {
                At("outside-angle", 2.4f + Mathf.Cos(angle), Mathf.Sin(angle)),
                At("radius-edge", 4.6f)
            };
            Assert.That(Advance(280), Is.True);
            Assert.That(impacts[0].Hit?.TargetId, Is.EqualTo("radius-edge"));
        }

        [Test]
        public void InstanceLockAndSharedLockAreIndependentAndANoTargetThrustStillOnlyFiresOnce()
        {
            StartFixture();
            Guid gesture = Guid.NewGuid();
            Assert.That(Activate(100, gesture), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(Ms(6100)));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, second.Id), Is.EqualTo(Ms(900)));
            Assert.That(Advance(280), Is.True);
            Assert.That(impacts[0].Hit, Is.Null);
            Assert.That(selection.TrySwitch(1, Ms(280)), Is.True);
            Assert.That(Activate(899, Guid.NewGuid(), second), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(900, Guid.NewGuid(), second), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(1080), Is.True);
            Assert.That(selection.TrySwitch(0, Ms(1080)), Is.True);
            Assert.That(Activate(6099, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(6100, gesture), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(Activate(6100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
        }

        [TestCase(820)]
        [TestCase(821)]
        public void ThrustAtOrAfterTheDeadlineRejectsTheActivationWithoutMotionOrLock(int started)
        {
            StartFixture(duration: 1000);
            Assert.That(Activate(started, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(TimeSpan.Zero));
            Assert.That(effect.PendingCharge, Is.Null);
            Assert.That(Advance(1000), Is.False);
            Assert.That(impacts, Is.Empty);
            Assert.That(body.position, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void AnUnappliedDashCannotMoveOrThrustAtTheDeadlineEvenBeforeRunFinalization()
        {
            StartFixture(duration: 1000);
            Assert.That(Activate(819, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(900), Is.False);
            Vector2 moved = body.position;
            Assert.That(moved.x, Is.GreaterThan(0));
            Assert.That(Advance(1000), Is.False);
            Assert.That(run.Result, Is.Null);
            Assert.That(body.position, Is.EqualTo(moved));
            Assert.That(impacts, Is.Empty);
            Assert.That(effect.PendingCharge, Is.Null);
            Assert.That(run.ProcessSignals(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
        }

        [Test]
        public void DeathAndReplacementHidePreviewAndNeverMoveOrHitAgain()
        {
            StartFixture();
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(170), Is.False);
            Vector2 stopped = body.position;
            now = Ms(180);
            Assert.That(run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, now) })?.Reason, Is.EqualTo(RunEndReason.Death));
            Assert.That(effect.PendingCharge, Is.Null);
            Assert.That(run.TryReturnToTown(runId), Is.True);
            Assert.That(run.TryStartRun(RunId.New(), 1, Ms(1000)), Is.True);
            Assert.That(Advance(280), Is.False);
            Assert.That(body.position, Is.EqualTo(stopped));
            Assert.That(impacts, Is.Empty);
            Assert.That(samples, Is.Zero);
        }

        [Test]
        public void StartingExtractionEndsAnyUnobservedForcingAndRejectsASecondCharge()
        {
            StartFixture();
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(170), Is.False);
            Vector2 stopped = body.position;
            now = Ms(180);
            Assert.That(run.TryBeginExtraction(runId, isInRange: true), Is.Not.Null);
            Assert.That(effect.PendingCharge, Is.Null);
            Assert.That(Advance(280), Is.False);
            Assert.That(Activate(1000, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(body.position, Is.EqualTo(stopped));
            Assert.That(impacts, Is.Empty);
        }

        [TestCase(100)]
        [TestCase(170)]
        public void AChannelStartedAndCanceledBetweenFramesCannotReviveTheCharge(int channelStarted)
        {
            StartFixture();
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            if (channelStarted > 100)
                Assert.That(Advance(150), Is.False);
            Vector2 stopped = body.position;
            now = Ms(channelStarted);
            ExtractionAttempt attempt = run.TryBeginExtraction(runId, isInRange: true)!;
            Assert.That(attempt, Is.Not.Null);
            now += Ms(1);
            Assert.That(run.ProcessSignals(new[] { RunSignal.CancelExtraction(runId, attempt.Id, now) }), Is.Null);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));

            Assert.That(effect.PendingCharge, Is.Null);
            Assert.That(Advance(280), Is.False);
            Assert.That(body.position, Is.EqualTo(stopped));
            Assert.That(impacts, Is.Empty);
            Assert.That(samples, Is.Zero);

            Assert.That(selection.TrySwitch(1, Ms(280)), Is.True);
            Assert.That(Activate(900, Guid.NewGuid(), second), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(1080), Is.True);
            Assert.That(impacts, Has.Count.EqualTo(1));
            Assert.That(impacts[0].Charge.WeaponInstanceId, Is.EqualTo(second.Id));
        }

        [TestCase("wooden_spear", 2, WeaponKind.Spear)]
        [TestCase("steel_spear", 1, WeaponKind.Spear)]
        [TestCase("steel_spear", 2, WeaponKind.Sword)]
        public void WrongWeaponIsRejectedWithoutCooldown(string id, int grade, WeaponKind kind)
        {
            var wrongWeapon = Temporary<WeaponDefinition>();
            var fields = new SerializedObject(wrongWeapon);
            fields.FindProperty("definitionId").stringValue = id;
            fields.FindProperty("displayName").stringValue = "Steel Spear test";
            fields.FindProperty("grade").intValue = grade;
            fields.FindProperty("kind").intValue = (int)kind;
            fields.FindProperty("skill").objectReferenceValue = pattern;
            fields.ApplyModifiedPropertiesWithoutUndo();
            StartFixture(weapon: wrongWeapon);
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(TimeSpan.Zero));
            Assert.That(effect.PendingCharge, Is.Null);
        }

        [Test]
        public void InvalidAimAndUnavailableBodyDoNotSpendTheSkill()
        {
            StartFixture();
            Assert.That(executor.TryActivate(Guid.NewGuid(), first.Id, Ms(100), default, ManualSkillAim.Along(default)),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            body.simulated = false;
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(TimeSpan.Zero));
            Assert.That(effect.PendingCharge, Is.Null);
        }

        private void PlaceWall(float x)
        {
            wall.transform.position = new Vector3(x, 0);
            Physics2D.SyncTransforms();
        }

        private void StartFixture(int duration = 20000, WeaponDefinition? weapon = null)
        {
            WeaponDefinition definition = weapon ?? steelSpear;
            var catalog = Temporary<WeaponCatalog>();
            var catalogFields = new SerializedObject(catalog);
            SerializedProperty definitions = catalogFields.FindProperty("definitions");
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = definition;
            catalogFields.ApplyModifiedPropertiesWithoutUndo();

            first = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            second = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            run = new RunState(() => now);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(duration)), Is.True);
            effect = new SteelSpearChargeEffect(run, new SteelSpearChargeMotion(body, shape, wall), point =>
            {
                sample = point;
                samples++;
                return candidates;
            }, impacts.Add);
            locks = new WeaponActionLocks();
            selection = new EquippedWeaponSelection(run, new InventoryService(new[] { first, second }), catalog,
                () => effect.PendingCharge.HasValue, locks);
            executor = new ManualSkillExecutor(run, selection, effect, () => movement.IsDodging, _ => null, "self");
        }

        private ManualSkillActivationStatus Activate(int ms, Guid gesture, WeaponInstance? weapon = null)
        {
            now = Ms(ms);
            return executor.TryActivate(gesture, (weapon ?? first).Id, now, default, Right);
        }

        private bool Advance(int ms)
        {
            now = Ms(ms);
            return effect.Advance(now);
        }

        private T Temporary<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            temporary.Add(asset);
            return asset;
        }

        private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);
        private static TargetCandidate At(string id, float x, float y = 0, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(x, y), alive, attackable);
    }
}
#endif
