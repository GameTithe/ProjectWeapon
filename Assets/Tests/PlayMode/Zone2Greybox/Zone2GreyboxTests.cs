#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Extraction;
using ProjectWeapon.Inventory;
using ProjectWeapon.Loot;
using ProjectWeapon.Movement;
using ProjectWeapon.Run;
using ProjectWeapon.Zone2Prison;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace ProjectWeapon.Zone2Greybox.Tests
{
    public sealed class Zone2GreyboxTests
    {
        private const int ZoneId = 2;
        private const float StepTime = 0.02f;
        private const string ScenePath = "Assets/Scenes/Zone2Greybox/Zone2Greybox.unity";
        private const string CatalogPath = "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset";
        private Scene scene;
        private PhysicsScene2D physics;
        private Keyboard? keyboard;
        private Rigidbody2D player = null!;
        private CircleCollider2D returnArea = null!;
        private ZoneCatalog catalog = null!;
        private Zone2PrisonTrial trial = null!;
        private InputSettings.UpdateMode previousUpdateMode;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
        private float previousFixedDeltaTime;
        private TimeSpan now;

        [UnitySetUp]
        public IEnumerator Prepare()
        {
            previousUpdateMode = InputSystem.settings.updateMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            previousFixedDeltaTime = Time.fixedDeltaTime;
            Time.fixedDeltaTime = StepTime;
            now = TimeSpan.Zero;
            keyboard = InputSystem.AddDevice<Keyboard>();
            SceneManager.sceneLoaded += ConfigureTrial;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            }
            finally
            {
                SceneManager.sceneLoaded -= ConfigureTrial;
            }
            scene = SceneManager.GetSceneByPath(ScenePath);
            Assert.That(scene.isLoaded, Is.True);
            physics = scene.GetPhysicsScene2D();
            Assert.That(physics.IsValid(), Is.True);
            player = Find<PlayerMovement>().GetComponent<Rigidbody2D>();
            Assert.That(player.GetComponent<PlayerMovement>(), Is.Not.Null);
            returnArea = scene.GetRootGameObjects().Single(root => root.name == "Return Point")
                .GetComponent<CircleCollider2D>();
            catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(returnArea.isTrigger, Is.True);
            Find<TilemapCollider2D>().ProcessTilemapChanges();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Dispose()
        {
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = previousUpdateMode;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            Time.fixedDeltaTime = previousFixedDeltaTime;
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator AuthoredWallsBlockMovementAndAWalkableRouteReachesTheReturnPoint()
        {
            var run = new RunState(() => now);
            var countdown = new ZoneRunCountdown(catalog, run);
            RunId id = RunId.New();
            Assert.That(countdown.TryStartRun(id, ZoneId), Is.True);
            ExtractionAccess access = CreateAccess(run);
            Assert.That(player.position, Is.EqualTo(new Vector2(1.5f, 9.5f)));
            Assert.That(access.CanStart(id, Position), Is.False);

            SetMovement(Key.D);
            for (int step = 0; step < 70; step++)
                yield return SimulateStep();
            Assert.That(player.position.x, Is.InRange(5.50f, 5.67f));
            Assert.That(player.position.y, Is.EqualTo(9.5f).Within(0.02f));

            yield return WalkTo(Key.A, Vector2.left, -4.5f);
            yield return WalkTo(Key.S, Vector2.down, -4.5f);
            yield return WalkTo(Key.A, Vector2.left, -2.5f);
            yield return WalkTo(Key.S, Vector2.down, -1.5f);
            yield return WalkTo(Key.D, Vector2.right, 14.25f);
            Assert.That(access.CanStart(id, Position), Is.False);

            yield return WalkTo(Key.D, Vector2.right, 14.65f);
            SetMovement();
            yield return SimulateStep();
            now = TimeSpan.FromSeconds(15);
            ExtractionAttempt? attempt = access.TryStart(id, Position);
            Assert.That(attempt, Is.Not.Null);
            now += RunState.ExtractionDuration;
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.Extracted));
        }

        [Test]
        public void ZoneSettingsAndExactRadiusAndTimeoutUseTheExistingRunServices()
        {
            ZoneDefinition definition = catalog.GetZone(ZoneId);
            Assert.That(definition.RecommendedGrade, Is.EqualTo(2));
            Assert.That(definition.MinimumRewardGrade, Is.EqualTo(1));
            Assert.That(definition.MaximumRewardGrade, Is.EqualTo(3));
            Assert.That(definition.TimeLimitSeconds, Is.EqualTo(600));
            Assert.That(returnArea.radius, Is.EqualTo(1f));
            WorldPoint2D edge = new WorldPoint2D(Center.X - returnArea.radius, Center.Y);
            WorldPoint2D outside = new WorldPoint2D(edge.X - .01f, edge.Y);
            var run = new RunState(() => now);
            var countdown = new ZoneRunCountdown(catalog, run);
            RunId id = RunId.New();
            Assert.That(countdown.TryStartRun(id, ZoneId), Is.True);
            ExtractionAccess access = CreateAccess(run);
            Assert.That(access.CanStart(id, edge), Is.True);
            Assert.That(access.CanStart(id, outside), Is.False);
            Assert.That(access.CanStart(RunId.New(), edge), Is.False);

            now = TimeSpan.FromSeconds(597);
            Assert.That(access.TryStart(id, edge), Is.Not.Null);
            now = TimeSpan.FromSeconds(600);
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(access.CanStart(id, edge), Is.False);
        }

        [UnityTest]
        public IEnumerator ActualPrisonWalkIncludesGuardCombatOptionalChestSentryAndThreeSecondReturn()
        {
            Assert.That(trial.Zone, Is.SameAs(catalog.GetZone(ZoneId)));
            Assert.That(Enumerable.Range(1, 5).Select(trial.Zone.GetRewardProbabilityBasisPoints),
                Is.EqualTo(new[] { 4500, 4500, 1000, 0, 0 }));
            Assert.That((Vector2)trial.Guard.transform.position, Is.EqualTo(new Vector2(2.5f, 7.5f)));
            Assert.That((Vector2)trial.Sentry.transform.position, Is.EqualTo(new Vector2(9.5f, 1.5f)));
            Assert.That((Vector2)trial.Chest.transform.position, Is.EqualTo(new Vector2(1.5f, 3.5f)));
            Assert.That(trial.TryOpenChest(), Is.False);

            yield return WalkTo(Key.D, Vector2.right, 2.5f);
            yield return WalkTo(Key.S, Vector2.down, -8.55f);
            SetMovement();
            yield return WaitUntil(() => trial.Guard.Brain.CurrentHealth <= 0, "The guard did not fall to the validation sword.");
            Assert.That(trial.PlayerHealth, Is.LessThan(100));

            yield return WalkTo(Key.D, Vector2.right, 4.5f);
            yield return WalkTo(Key.S, Vector2.down, -4.5f);
            yield return WalkTo(Key.A, Vector2.left, -2.5f);
            Assert.That(trial.TryOpenChest(), Is.False, "The direct path does not enter the optional chest radius.");
            yield return WalkTo(Key.A, Vector2.left, -1.5f);
            yield return WalkTo(Key.S, Vector2.down, -3.5f);
            yield return PressInteract();
            Assert.That(trial.Chest.IsOpen, Is.True);
            WorldLoot drop = Find<WorldLoot>();
            var dropId = drop.Item!.Id;
            Assert.That(trial.TryOpenChest(), Is.False);
            yield return WalkTo(Key.D, Vector2.right, 2.05f);
            SetMovement();
            yield return WaitUntil(() => trial.Carried.Count == 1, "The physical pickup never reached the local bag.");
            Assert.That(trial.Carried.Single().Id, Is.EqualTo(dropId));
            Assert.That(trial.Carried.Single().Location.Kind, Is.EqualTo(WeaponLocationKind.Carried));
            Assert.That(trial.Carried.Single().DefinitionId.Value, Is.EqualTo("wooden_spear"));
            Assert.That(drop.gameObject.activeSelf, Is.False);

            yield return WalkTo(Key.S, Vector2.down, -1.5f);
            yield return WalkTo(Key.D, Vector2.right, 6.7f);
            Assert.That(trial.Sentry.Brain.CurrentHealth, Is.GreaterThan(0));
            SetMovement();
            int beforeShot = trial.PlayerHealth;
            yield return WaitUntil(() => trial.PlayerHealth < beforeShot, "The lower-corridor sentry did not land a shot.");
            Assert.That(trial.PlayerHealth, Is.EqualTo(beforeShot - 6));
            yield return WalkTo(Key.D, Vector2.right, 8.5f);
            SetMovement();
            yield return WaitUntil(() => trial.Sentry.Brain.CurrentHealth <= 0, "The sentry did not fall to the validation sword.");

            yield return WalkTo(Key.D, Vector2.right, 14.2f);
            yield return PressInteract();
            Assert.That(trial.Run.Phase, Is.EqualTo(RunPhase.Exploring));
            yield return WalkTo(Key.D, Vector2.right, 14.75f);
            yield return PressInteract();
            ExtractionAttempt attempt = trial.Run.ActiveExtraction!;
            Assert.That(attempt, Is.Not.Null);
            Assert.That(attempt.CompletesAt - attempt.StartedAt, Is.EqualTo(TimeSpan.FromSeconds(3)));
            now = attempt.CompletesAt - TimeSpan.FromTicks(1);
            yield return null;
            Assert.That(trial.Run.Phase, Is.EqualTo(RunPhase.Extracting));
            now = attempt.CompletesAt;
            yield return null;
            Assert.That(trial.Run.Result?.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(trial.Run.Result?.EffectiveAt, Is.EqualTo(attempt.CompletesAt));
        }

        [UnityTest]
        public IEnumerator RealPrisonReturnAtExactDeadlineFailsAndAnUnopenedChestStaysClosed()
        {
            player.position = new Vector2(Center.X - returnArea.radius, Center.Y);
            now = TimeSpan.FromSeconds(597);
            yield return PressInteract();
            ExtractionAttempt attempt = trial.Run.ActiveExtraction!;
            Assert.That(attempt, Is.Not.Null);
            Assert.That(attempt.CompletesAt, Is.EqualTo(trial.Run.CurrentRun!.Deadline));
            now = TimeSpan.FromSeconds(600);
            yield return null;
            Assert.That(trial.Run.Result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(trial.Carried, Is.Empty);
            player.position = trial.Chest.transform.position;
            yield return PressInteract();
            Assert.That(trial.TryOpenChest(), Is.False);
            Assert.That(trial.Chest.IsOpen, Is.False);
        }

        private void ConfigureTrial(Scene loaded, LoadSceneMode _)
        {
            if (loaded.path != ScenePath)
                return;

            trial = loaded.GetRootGameObjects().Select(root => root.GetComponent<Zone2PrisonTrial>())
                .Single(component => component != null)!;
            trial.ConfigureClock(() => now);
        }

        private IEnumerator PressInteract()
        {
            SetMovement(Key.E);
            yield return null;
            SetMovement();
            yield return null;
        }

        private IEnumerator WaitUntil(Func<bool> condition, string failure)
        {
            for (int step = 0; step < 200 && !condition(); step++)
                yield return SimulateStep();

            Assert.That(condition(), Is.True, failure);
        }

        private IEnumerator WalkTo(Key key, Vector2 direction, float goal)
        {
            SetMovement(key);
            for (int step = 0; step < 200; step++)
            {
                if (Vector2.Dot(player.position, direction) >= goal)
                    yield break;
                yield return SimulateStep();
            }
            Assert.Fail($"Did not walk to {goal} along {direction}. Player stopped at {player.position}.");
        }

        private IEnumerator SimulateStep()
        {
            now += TimeSpan.FromMilliseconds(20);
            yield return new WaitForFixedUpdate();
            Assert.That(physics.Simulate(StepTime), Is.True);
        }

        private void SetMovement(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private T Find<T>() where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>()).Single();

        private ExtractionAccess CreateAccess(RunState run) => new ExtractionAccess(run, ZoneId, Center, returnArea.radius);
        private WorldPoint2D Center
        {
            get
            {
                Vector3 center = returnArea.transform.TransformPoint(returnArea.offset);
                return new WorldPoint2D(center.x, center.y);
            }
        }
        private WorldPoint2D Position => new WorldPoint2D(player.position.x, player.position.y);
    }
}
#endif
