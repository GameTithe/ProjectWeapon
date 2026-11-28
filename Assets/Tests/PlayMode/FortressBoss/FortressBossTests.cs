#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using ProjectWeapon.Zone4Fortress;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace ProjectWeapon.FortressBoss.Tests
{
    public sealed class FortressBossTests
    {
        private const string ScenePath = "Assets/Scenes/Zone4Greybox/Zone4Greybox.unity";
        private const float Step = .02f;
        private static readonly Vector2 TestApproach = new Vector2(17.5f, 7.3f);
        private Scene scene;
        private PhysicsScene2D physics;
        private Keyboard keyboard = null!;
        private Zone4FortressTrial trial = null!;
        private FortressBossActor boss = null!;
        private Rigidbody2D player = null!;
        private LineRenderer warning = null!;
        private InputSettings.UpdateMode updateMode;
        private InputSettings.BackgroundBehavior backgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private float fixedStep;
        private TimeSpan now;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            updateMode = InputSystem.settings.updateMode;
            backgroundBehavior = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            fixedStep = Time.fixedDeltaTime;
            Time.fixedDeltaTime = Step;
            now = TimeSpan.Zero;

            void Configure(Scene loaded, LoadSceneMode _)
            {
                if (loaded.path == ScenePath)
                    loaded.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Zone4FortressTrial>())
                        .Single().ConfigureClock(() => now);
            }

            SceneManager.sceneLoaded += Configure;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D));
            }
            finally
            {
                SceneManager.sceneLoaded -= Configure;
            }

            scene = SceneManager.GetSceneByPath(ScenePath);
            physics = scene.GetPhysicsScene2D();
            var roots = scene.GetRootGameObjects();
            trial = roots.SelectMany(root => root.GetComponentsInChildren<Zone4FortressTrial>()).Single();
            player = roots.Single(root => root.name == "PrototypeMovementPlayer").GetComponent<Rigidbody2D>();
            roots.SelectMany(root => root.GetComponentsInChildren<TilemapCollider2D>()).Single().ProcessTilemapChanges();
            yield return null;
            boss = trial.Boss;
            warning = boss.GetComponentInChildren<LineRenderer>();
            Assert.That(boss.transform.position, Is.EqualTo(new Vector3(17.5f, 9.5f, 0f)));
            Assert.That(boss.CurrentHealth, Is.EqualTo(250f));
            Assert.That(trial.Run.CurrentRun!.ZoneId, Is.EqualTo(4));
        }

        [UnityTearDown]
        public IEnumerator Clean()
        {
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = updateMode;
            InputSystem.settings.backgroundBehavior = backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            Time.fixedDeltaTime = fixedStep;
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator ActualOutlineTipHitsAtTheLockedTimeAndCannotDamageTwice()
        {
            PlacePlayer(TestApproach);
            yield return At(100);
            FortressCleave strike = boss.ActiveAttack!.Value;
            Assert.That(strike.Area.Shape, Is.EqualTo(DamageAreaShape.Sector));
            Assert.That(strike.Area.Origin, Is.EqualTo(new WorldPoint2D(17.5f, 9.5f)));
            Assert.That(strike.Area.Direction, Is.EqualTo(new WorldPoint2D(0f, -1f)));
            Assert.That(strike.Area.Radius, Is.EqualTo(2.6f));
            Assert.That(strike.Area.AngleDegrees, Is.EqualTo(120f));
            Assert.That(strike.HitsAt, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(warning.enabled && warning.loop, Is.True);
            Assert.That(warning.GetPosition(0), Is.EqualTo(boss.transform.position));

            Vector3 tip = warning.GetPosition(warning.positionCount / 2);
            Assert.That(Vector2.Distance(tip, boss.transform.position), Is.EqualTo(2.6f).Within(.00001f));
            PlacePlayer(tip);
            yield return At(999);
            Assert.That(trial.PlayerHealth, Is.EqualTo(100));
            Assert.That(boss.ActiveAttack!.Value.Area.ApplicationId, Is.EqualTo(strike.Area.ApplicationId));
            yield return At(1000);
            Assert.That(trial.PlayerHealth, Is.EqualTo(62));
            Assert.That(boss.ActiveAttack, Is.Null);
            Assert.That(warning.enabled, Is.False);
            yield return At(2649);
            Assert.That(boss.ActiveAttack, Is.Null);
            Assert.That(trial.PlayerHealth, Is.EqualTo(62));
            yield return At(2650);
            Assert.That(boss.ActiveAttack, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator WalkingToTheSideOrBehindAnActualWallAvoidsTheFixedAttack()
        {
            PlacePlayer(new Vector2(15.5f, 8f));
            yield return At(50);
            Assert.That(boss.ActiveAttack, Is.Null, "Solid wall must block starting the attack.");
            PlacePlayer(TestApproach);
            yield return At(100);
            Vector3 locked = warning.GetPosition(warning.positionCount / 2);
            PlacePlayer(new Vector2(15.5f, 9.5f));
            yield return At(900);
            Assert.That(warning.GetPosition(warning.positionCount / 2), Is.EqualTo(locked));
            yield return At(1000);
            Assert.That(trial.PlayerHealth, Is.EqualTo(100), "The clear side of the sector must be safe.");

            PlacePlayer(TestApproach);
            yield return At(2650);
            Assert.That(boss.ActiveAttack, Is.Not.Null);
            PlacePlayer(new Vector2(15.5f, 8f));
            yield return At(3550);
            Assert.That(trial.PlayerHealth, Is.EqualTo(100), "An intervening tilemap wall blocks the strike.");
            Assert.That(warning.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator APreDeadlineStrikeAppliesWhenFirstObservedAtTheDeadline()
        {
            PlacePlayer(TestApproach);
            yield return At(719000);
            Assert.That(boss.ActiveAttack!.Value.HitsAt, Is.EqualTo(TimeSpan.FromMilliseconds(719900)));
            yield return At(720000);
            Assert.That(trial.PlayerHealth, Is.EqualTo(62));
            Assert.That(trial.Run.Result!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(warning.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator AStrikeDueExactlyAtTheDeadlineNeverTelegraphsOrDamages()
        {
            PlacePlayer(TestApproach);
            yield return At(719100);
            Assert.That(boss.ActiveAttack, Is.Null);
            Assert.That(warning.enabled, Is.False);
            yield return At(720000);
            Assert.That(trial.PlayerHealth, Is.EqualTo(100));
            Assert.That(trial.Run.Result!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
        }

        [UnityTest]
        public IEnumerator ActualBasicAttacksDefeatTheBossAndPublishOneSignalInTheActiveRun()
        {
            var defeats = new List<FortressBossDefeat>();
            boss.Defeated += defeats.Add;
            var body = boss.GetComponent<SpriteRenderer>();
            Color initialColor = body.color;
            var spark = boss.GetComponentsInChildren<SpriteRenderer>().Single(renderer => renderer != body);
            for (int swing = 1; swing <= 25; swing++)
            {
                int at = swing * 3000;
                PlacePlayer(new Vector2(17.5f, 8.5f));
                yield return At(at);
                Assert.That(boss.CurrentHealth, Is.EqualTo(250 - swing * 10));
                if (swing == 1)
                {
                    Assert.That(body.color, Is.Not.EqualTo(initialColor));
                    Assert.That(spark.enabled, Is.True);
                }
                if (boss.CurrentHealth == 0f)
                    break;
                PlacePlayer(new Vector2(15.5f, 9.5f));
                yield return At(at + 900);
            }
            yield return At(76000);
            FortressBossDefeat defeat = defeats.Single();
            Assert.That(defeat.RunId, Is.EqualTo(trial.Run.CurrentRun!.Id));
            Assert.That(defeat.BossId, Is.EqualTo(FortressBossActor.BossId));
            Assert.That(defeat.ZoneId, Is.EqualTo(4));
            Assert.That(defeat.DefeatedAt, Is.EqualTo(TimeSpan.FromSeconds(75)));
            Assert.That(boss.ActiveAttack, Is.Null);
            Assert.That(warning.enabled, Is.False);
            Assert.That(trial.PlayerHealth, Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator ExistingWalkableRouteCanReturnWhileTheOptionalBossHasFullHealth()
        {
            yield return WalkTo(Key.D, Vector2.right, 3.5f);
            yield return WalkTo(Key.W, Vector2.up, 6.5f);
            yield return WalkTo(Key.D, Vector2.right, 8.5f);
            yield return WalkTo(Key.S, Vector2.down, -4.5f);
            yield return WalkTo(Key.D, Vector2.right, 15.5f);
            yield return WalkTo(Key.S, Vector2.down, -3.9f);
            Assert.That(Vector2.Distance(player.position, new Vector2(15.5f, 3.5f)), Is.LessThan(.75f));
            Assert.That(boss.ActiveAttack, Is.Null);
            SendKeys(Key.E);
            yield return At(15000);
            SendKeys();
            Assert.That(trial.Run.Phase, Is.EqualTo(RunPhase.Extracting));
            yield return At(18000);
            Assert.That(trial.Run.Result!.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(trial.PlayerHealth, Is.EqualTo(100));
            Assert.That(boss.CurrentHealth, Is.EqualTo(250f));
            Assert.That(boss.Defeat, Is.Null);
        }

        private void PlacePlayer(Vector2 position)
        {
            player.position = position;
            Physics2D.SyncTransforms();
        }

        private IEnumerator At(int milliseconds)
        {
            now = TimeSpan.FromMilliseconds(milliseconds);
            yield return null;
        }

        private IEnumerator WalkTo(Key key, Vector2 direction, float goal)
        {
            SendKeys(key);
            for (int step = 0; step < 200; step++)
            {
                if (Vector2.Dot(player.position, direction) >= goal)
                {
                    SendKeys();
                    yield break;
                }
                yield return new WaitForFixedUpdate();
                Assert.That(physics.Simulate(Step), Is.True);
            }
            Assert.Fail($"Did not walk to {goal} along {direction}. Player stopped at {player.position}.");
        }

        private void SendKeys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }
    }
}
#endif
