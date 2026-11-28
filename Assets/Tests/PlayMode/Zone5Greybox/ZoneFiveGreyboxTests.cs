#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Dungeon;
using ProjectWeapon.Extraction;
using ProjectWeapon.Run;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace ProjectWeapon.Zone5Greybox.Tests
{
    public sealed class ZoneFiveGreyboxTests
    {
        private const string ScenePath = "Assets/Scenes/Zone5Greybox/Zone5Greybox.unity";
        private const string CatalogPath = "Assets/ProjectWeapon/Dungeon/PrototypeZoneCatalog.asset";
        private Scene scene;
        private PhysicsScene2D physics;
        private Rigidbody2D player = null!;
        private CircleCollider2D portal = null!;
        private RunState run = null!;
        private RunId runId;
        private ExtractionAccess extraction = null!;
        private Keyboard keyboard = null!;
        private InputSettings.UpdateMode updateMode;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            updateMode = InputSystem.settings.updateMode;
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            scene = SceneManager.GetSceneByPath(ScenePath);
            Assert.That(scene.isLoaded, Is.True);
            physics = scene.GetPhysicsScene2D();
            var roots = scene.GetRootGameObjects();
            player = roots.Single(root => root.name == "Start Player").GetComponent<Rigidbody2D>();
            portal = roots.Single(root => root.name == "Return Portal").GetComponent<CircleCollider2D>();

            var catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(CatalogPath);
            ZoneDefinition zone = catalog.GetZone(5);
            Assert.That(zone.RecommendedGrade, Is.EqualTo(5));
            Assert.That(zone.MinimumRewardGrade, Is.EqualTo(4));
            Assert.That(zone.MaximumRewardGrade, Is.EqualTo(5));
            Assert.That(zone.TimeLimitSeconds, Is.EqualTo(900));
            run = new RunState(() => TimeSpan.Zero);
            runId = RunId.New();
            Assert.That(new ZoneRunCountdown(catalog, run).TryStartRun(runId, 5), Is.True);
            Assert.That(run.CurrentRun!.Deadline, Is.EqualTo(TimeSpan.FromMinutes(15)));
            extraction = new ExtractionAccess(run, zone.ZoneId, ToPoint(PortalCenter()), portal.radius);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);

            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);

            InputSystem.settings.updateMode = updateMode;
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }

        [UnityTest]
        public IEnumerator CentralBarrierStopsThePlayerButTheWestGateConnectsSpawnToReturn()
        {
            Assert.That(player.position, Is.EqualTo(new Vector2(8.5f, 2.5f)));
            Assert.That(portal.isTrigger, Is.True);
            Assert.That(PortalCenter(), Is.EqualTo(new Vector2(8.5f, 9.5f)));
            Assert.That(extraction.CanStart(runId, ToPoint(player.position)), Is.False);

            yield return Drive(Key.W, 31);
            Assert.That(physics.OverlapPoint(new Vector2(8.5f, 4.5f)), Is.TypeOf<TilemapCollider2D>());
            Assert.That(player.position.y, Is.InRange(3.50f, 3.75f));
            Assert.That(player.position.x, Is.EqualTo(8.5f).Within(0.05f));
            Assert.That(extraction.TryStart(runId, ToPoint(player.position)), Is.Null);

            yield return Drive(Key.A, 25);
            yield return Drive(Key.W, 10);
            yield return Drive(Key.A, 25);
            yield return Drive(Key.W, 63);
            yield return Drive(Key.D, 50);

            Vector2 arrived = player.position;
            Assert.That(Vector2.Distance(arrived, PortalCenter()), Is.LessThan(0.25f));
            Assert.That(extraction.CanStart(runId, ToPoint(arrived)), Is.True);
            Assert.That(extraction.TryStart(runId, ToPoint(arrived)), Is.Not.Null);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Extracting));
        }

        [UnityTest]
        public IEnumerator MarkerBoundaryIsInclusiveAndOutsideOrWrongZoneCannotStart()
        {
            Assert.That(portal.radius, Is.EqualTo(0.75f));
            Vector2 center = PortalCenter();
            player.position = center + Vector2.right * (portal.radius + 0.01f);
            Assert.That(extraction.CanStart(runId, ToPoint(player.position)), Is.False);
            Assert.That(extraction.TryStart(runId, ToPoint(player.position)), Is.Null);

            player.position = center + Vector2.right * portal.radius;
            var wrongZone = new ExtractionAccess(run, 4, ToPoint(center), portal.radius);
            Assert.That(wrongZone.CanStart(runId, ToPoint(player.position)), Is.False);
            Assert.That(extraction.CanStart(runId, ToPoint(player.position)), Is.True);
            Assert.That(extraction.TryStart(runId, ToPoint(player.position)), Is.Not.Null);
            Assert.That(extraction.TryStart(runId, ToPoint(player.position)), Is.Null);
            yield return null;
        }

        private IEnumerator Drive(Key key, int steps)
        {
            SendKeys(key);
            for (int step = 0; step < steps; step++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(physics.Simulate(Time.fixedDeltaTime), Is.True);
            }
            SendKeys();
        }

        private Vector2 PortalCenter() => portal.transform.TransformPoint(portal.offset);

        private static WorldPoint2D ToPoint(Vector2 point) => new WorldPoint2D(point.x, point.y);

        private void SendKeys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }
    }
}
#endif
