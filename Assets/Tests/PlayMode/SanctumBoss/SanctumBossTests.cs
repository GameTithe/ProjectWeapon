#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.Zone5Sanctum.Tests
{
    public sealed class SanctumBossTests
    {
        private const string ScenePath = "Assets/Scenes/Zone5Greybox/Zone5Greybox.unity";
        private static readonly Vector2 Center = new Vector2(13.85f, 8.5f);
        private Scene scene;
        private PhysicsScene2D physics;
        private Zone5SanctumTrial trial = null!;
        private Rigidbody2D player = null!;
        private Keyboard keyboard = null!;
        private InputSettings.UpdateMode previousMode;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousInput;
        private TimeSpan clock;

        [UnitySetUp]
        public IEnumerator LoadAuthoredTrial()
        {
            clock = TimeSpan.Zero;
            previousMode = InputSystem.settings.updateMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();

            void Configure(Scene loaded, LoadSceneMode mode)
            {
                if (loaded.path != ScenePath)
                    return;
                scene = loaded;
                var roots = scene.GetRootGameObjects();
                trial = roots.Single(root => root.name == "Sanctum Boss Trial").GetComponent<Zone5SanctumTrial>();
                player = roots.Single(root => root.name == "Start Player").GetComponent<Rigidbody2D>();
                trial.ConfigureClock(() => clock);
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

            yield return null;
            Assert.That(trial.Run.CurrentRun?.ZoneId, Is.EqualTo(5));
            Assert.That(trial.Run.CurrentRun?.Deadline, Is.EqualTo(TimeSpan.FromSeconds(900)));
            Assert.That((Vector2)trial.Boss.transform.position, Is.EqualTo(Center));
            Assert.That(trial.Boss.CurrentHealth, Is.EqualTo(300f));
            Assert.That(trial.Boss.ActiveAttack, Is.Null);
            physics = scene.GetPhysicsScene2D();
            Assert.That(physics.IsValid(), Is.True);
        }

        [UnityTearDown]
        public IEnumerator UnloadAuthoredTrial()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode = previousMode;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousInput;
        }

        [UnityTest]
        public IEnumerator BothDrawnBoundariesUseTheLockedCenterAndOuterEdgeActuallyHits()
        {
            SetPlayer(Center + Vector2.right * 1.5f);
            clock = Ms(100);
            yield return null;

            SanctumRingStrike strike = trial.Boss.ActiveAttack!.Value;
            Assert.That(strike.Center, Is.EqualTo(Point(Center)));
            Assert.That(strike.InnerRadius, Is.EqualTo(1.35f));
            Assert.That(strike.OuterRadius, Is.EqualTo(3.1f));
            Assert.That(strike.HitsAt, Is.EqualTo(Ms(1150)));
            Assert.That(strike.Damage, Is.EqualTo(42));
            Assert.That(strike.Contains(Point(Center + Vector2.right * 1.34f)), Is.False);
            Assert.That(strike.Contains(Point(Center + Vector2.right * 1.35f)), Is.True);
            Assert.That(strike.Contains(Point(Center + Vector2.right * 3.1f)), Is.True);
            Assert.That(strike.Contains(Point(Center + Vector2.right * 3.11f)), Is.False);

            LineRenderer inner = Warning("Inner Warning");
            LineRenderer outer = Warning("Outer Warning");
            AssertRing(inner, Center, strike.InnerRadius);
            AssertRing(outer, Center, strike.OuterRadius);

            // This endpoint is on the authored north passage with an unobstructed ray.
            SetPlayer(Center + new Vector2(-Mathf.Sqrt(3.1f * 3.1f - 1f), 1));
            clock = Ms(1149);
            yield return null;
            Assert.That(trial.PlayerHealth, Is.EqualTo(100));
            Assert.That(trial.Boss.ActiveAttack?.DamageId, Is.EqualTo(strike.DamageId));

            clock = Ms(1150);
            yield return null;
            Assert.That(trial.PlayerHealth, Is.EqualTo(58));
            Assert.That(trial.Boss.ActiveAttack, Is.Null);
            Assert.That(inner.enabled || outer.enabled, Is.False);

            yield return null;
            Assert.That(trial.PlayerHealth, Is.EqualTo(58));
        }

        [UnityTest]
        public IEnumerator InnerHoleAndRealSanctumWallPreventDamageAndCooldownIsExact()
        {
            var wallSide = new Vector2(10.85f, 8.5f);
            SetPlayer(wallSide);
            clock = Ms(50);
            yield return null;
            Assert.That(trial.Boss.ActiveAttack, Is.Null, "The east partition blocks detection.");

            SetPlayer(Center + Vector2.right * 1.5f);
            clock = Ms(100);
            yield return null;
            Assert.That(trial.Boss.ActiveAttack, Is.Not.Null);
            SetPlayer(Center + Vector2.right * .8f);
            clock = Ms(1150);
            yield return null;
            Assert.That(trial.PlayerHealth, Is.EqualTo(100));

            SetPlayer(Center + Vector2.right * 1.5f);
            clock = Ms(3049);
            yield return null;
            Assert.That(trial.Boss.ActiveAttack, Is.Null);
            clock = Ms(3050);
            yield return null;
            Assert.That(trial.Boss.ActiveAttack?.HitsAt, Is.EqualTo(Ms(4100)));

            SetPlayer(wallSide);
            Assert.That(trial.Boss.ActiveAttack!.Value.Contains(Point(wallSide)), Is.True);
            clock = Ms(4100);
            yield return null;
            Assert.That(trial.PlayerHealth, Is.EqualTo(100), "The same solid partition blocks impact.");
        }

        [UnityTest]
        public IEnumerator LateObservationOfEarlierHitDamagesAtExpiryFrame()
        {
            SetPlayer(Center + Vector2.right * 1.5f);
            clock = Ms(898900);
            yield return null;
            Assert.That(trial.Boss.ActiveAttack?.HitsAt, Is.EqualTo(Ms(899950)));
            clock = Ms(900000);
            yield return null;
            Assert.That(trial.PlayerHealth, Is.EqualTo(58));
            Assert.That(trial.Run.Result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(trial.Boss.ActiveAttack, Is.Null);
        }

        [UnityTest]
        public IEnumerator ExactExpirySuppressesThePendingHitAndBothWarnings()
        {
            SetPlayer(Center + Vector2.right * 1.5f);
            clock = Ms(898950);
            yield return null;
            Assert.That(trial.Boss.ActiveAttack?.HitsAt, Is.EqualTo(Ms(900000)));
            clock = Ms(900000);
            yield return null;
            Assert.That(trial.Run.Result?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(trial.PlayerHealth, Is.EqualTo(100));
            Assert.That(Warning("Inner Warning").enabled || Warning("Outer Warning").enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator ActualBasicWeaponFlashesAndDefeatsOnceAndCancelsWarning()
        {
            SanctumBossActor boss = trial.Boss;
            SpriteRenderer body = boss.GetComponent<SpriteRenderer>();
            Color original = body.color;
            int defeatCount = 0;
            boss.Defeated += _ => defeatCount++;

            SetPlayer(Center + Vector2.right * .8f);
            clock = Ms(100);
            yield return null;
            Assert.That(boss.CurrentHealth, Is.LessThan(300f));
            Assert.That(body.color, Is.Not.EqualTo(original));
            clock = Ms(220);
            yield return null;
            Assert.That(body.color, Is.EqualTo(original));

            for (int second = 1; second <= 40 && !boss.Defeat.HasValue; second++)
            {
                clock = TimeSpan.FromSeconds(second);
                yield return null;
            }

            Assert.That(boss.CurrentHealth, Is.Zero);
            Assert.That(defeatCount, Is.EqualTo(1));
            SanctumBossDefeat defeat = boss.Defeat!.Value;
            Assert.That(defeat.RunId, Is.EqualTo(trial.Run.CurrentRun!.Id));
            Assert.That(defeat.ZoneId, Is.EqualTo(5));
            Assert.That(defeat.BossId, Is.EqualTo(SanctumBossActor.BossId));
            Assert.That(defeat.DefeatedAt, Is.EqualTo(clock));
            Assert.That(trial.PlayerHealth, Is.EqualTo(100), "The player stays inside the inner hole.");
            Assert.That(Warning("Inner Warning").enabled || Warning("Outer Warning").enabled, Is.False);
            clock += TimeSpan.FromSeconds(10);
            yield return null;
            Assert.That(defeatCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ActualWestBypassWalksToThePortalAndExtractsWithBossUnhurt()
        {
            Vector2 startingPoint = player.position;
            Assert.That(startingPoint, Is.EqualTo(new Vector2(8.5f, 2.5f)));
            yield return Drive(Key.W, 31);
            Assert.That(player.position.y, Is.InRange(3.50f, 3.75f));
            yield return Drive(Key.A, 25);
            yield return Drive(Key.W, 10);
            yield return Drive(Key.A, 25);
            yield return Drive(Key.W, 63);
            yield return Drive(Key.D, 50);
            Assert.That(Vector2.Distance(player.position, new Vector2(8.5f, 9.5f)), Is.LessThan(.25f));
            Assert.That(trial.Boss.ActiveAttack, Is.Null);

            SendKey(Key.E);
            yield return null;
            SendKey();
            var attempt = trial.Run.ActiveExtraction;
            Assert.That(attempt, Is.Not.Null);
            clock = attempt!.CompletesAt;
            yield return null;
            Assert.That(trial.Run.Result?.Reason, Is.EqualTo(RunEndReason.Extracted));
            Assert.That(trial.PlayerHealth, Is.EqualTo(100));
            Assert.That(trial.Boss.CurrentHealth, Is.EqualTo(300f));
            Assert.That(trial.Boss.Defeat, Is.Null);
        }

        private IEnumerator Drive(Key key, int steps)
        {
            SendKey(key);
            for (int step = 0; step < steps; step++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(physics.Simulate(Time.fixedDeltaTime), Is.True);
                clock += TimeSpan.FromMilliseconds(20);
            }
            SendKey();
        }

        private void SendKey(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private void SetPlayer(Vector2 position)
        {
            player.position = position;
            Physics2D.SyncTransforms();
        }

        private LineRenderer Warning(string name) => trial.Boss.transform.Find(name).GetComponent<LineRenderer>();

        private static void AssertRing(LineRenderer ring, Vector2 center, float radius)
        {
            Assert.That(ring.enabled && ring.loop && ring.useWorldSpace, Is.True);
            Assert.That(ring.positionCount, Is.GreaterThan(30));
            for (int point = 0; point < ring.positionCount; point++)
                Assert.That(Vector2.Distance(ring.GetPosition(point), center), Is.EqualTo(radius).Within(.00001f));
        }

        private static WorldPoint2D Point(Vector2 value) => new WorldPoint2D(value.x, value.y);
        private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
    }
}
#endif
