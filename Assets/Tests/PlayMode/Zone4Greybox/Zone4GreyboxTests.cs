#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Extraction;
using ProjectWeapon.Movement;
using ProjectWeapon.Run;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace ProjectWeapon.Zone4Greybox.Tests
{
    public sealed class Zone4GreyboxTests
    {
        private const int ZoneId = 4;
        private const float StepTime = 0.02f;
        private const string ScenePath = "Assets/Scenes/Zone4Greybox/Zone4Greybox.unity";
        private const string CatalogPath = "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset";
        private Scene scene;
        private PhysicsScene2D physics;
        private Keyboard? keyboard;
        private Rigidbody2D player = null!;
        private CircleCollider2D returnArea = null!;
        private ZoneCatalog catalog = null!;
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
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            scene = SceneManager.GetSceneByPath(ScenePath);
            Assert.That(scene.isLoaded, Is.True);
            physics = scene.GetPhysicsScene2D();
            Assert.That(physics.IsValid(), Is.True);
            player = Find<Rigidbody2D>();
            Assert.That(player.GetComponent<PlayerMovement>(), Is.Not.Null);
            returnArea = scene.GetRootGameObjects().Single(root => root.name == "Return Point")
                .GetComponent<CircleCollider2D>();
            catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(returnArea.isTrigger, Is.True);
            Find<TilemapCollider2D>().ProcessTilemapChanges();
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
            Assert.That(player.position, Is.EqualTo(new Vector2(1.5f, 4.5f)));
            Assert.That(access.CanStart(id, Position), Is.False);

            SetMovement(Key.D);
            for (int step = 0; step < 70; step++)
                yield return SimulateStep();
            Assert.That(player.position.x, Is.InRange(4.50f, 4.67f));
            Assert.That(player.position.y, Is.EqualTo(4.5f).Within(0.02f));

            yield return WalkTo(Key.A, Vector2.left, -3.5f);
            yield return WalkTo(Key.W, Vector2.up, 6.5f);
            yield return WalkTo(Key.D, Vector2.right, 8.5f);
            yield return WalkTo(Key.S, Vector2.down, -4.5f);
            yield return WalkTo(Key.D, Vector2.right, 15.5f);
            Assert.That(access.CanStart(id, Position), Is.False);

            yield return WalkTo(Key.S, Vector2.down, -3.9f);
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
            Assert.That(definition.RecommendedGrade, Is.EqualTo(4));
            Assert.That(definition.MinimumRewardGrade, Is.EqualTo(3));
            Assert.That(definition.MaximumRewardGrade, Is.EqualTo(5));
            Assert.That(definition.TimeLimitSeconds, Is.EqualTo(720));
            Assert.That(returnArea.radius, Is.EqualTo(.75f));
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

            now = TimeSpan.FromSeconds(717);
            Assert.That(access.TryStart(id, edge), Is.Not.Null);
            now = TimeSpan.FromSeconds(720);
            Assert.That(countdown.ProcessFrame(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(access.CanStart(id, edge), Is.False);
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
