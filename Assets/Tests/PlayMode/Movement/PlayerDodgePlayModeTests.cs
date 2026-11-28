#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using ProjectWeapon.Run;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Movement.Tests
{
    public sealed class PlayerDodgePlayModeTests
    {
        private const string PlayerPrefabPath =
            "Assets/ProjectWeapon/Movement/PrototypeMovementPlayer.prefab";
        private Scene scene;
        private Rigidbody2D body;
        private PlayerMovement movement;
        private RunState run;
        private RunId runId;
        private TimeSpan now;
        private float previousFixedDeltaTime;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousFixedDeltaTime = Time.fixedDeltaTime;
            Time.fixedDeltaTime = 0.02f;
            now = TimeSpan.Zero;
            scene = SceneManager.CreateScene($"Dodge-{Guid.NewGuid():N}",
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var player = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(player, scene);
            body = player.GetComponent<Rigidbody2D>();
            movement = player.GetComponent<PlayerMovement>();
            run = new RunState(() => now);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(10000)), Is.True);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.fixedDeltaTime = previousFixedDeltaTime;
            if (scene.IsValid())
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator ButtonClickRollsOnceToTheSouthAndCooldownIsMeasuredFromStart()
        {
            var buttonObject = new GameObject("Dodge", typeof(RectTransform), typeof(Button));
            SceneManager.MoveGameObjectToScene(buttonObject, scene);
            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() => movement.TryDodge(run, now));
            int accepted = 0;
            movement.DodgeStarted += (_, _) => accepted++;

            var press = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            button.OnPointerClick(press);
            button.OnPointerClick(press);
            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(movement.IsDodging, Is.True);
            Assert.That(movement.DodgeDirection, Is.EqualTo(Vector2.down));
            Assert.That(body.position, Is.EqualTo(Vector2.zero));
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode2D.Continuous));

            yield return SimulateDodge();
            Assert.That(body.position.x, Is.Zero.Within(0.001f));
            Assert.That(body.position.y, Is.EqualTo(-2f).Within(0.01f));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode2D.Discrete));

            now = Ms(1249);
            button.OnPointerClick(press);
            Assert.That(accepted, Is.EqualTo(1));
            now = Ms(1250);
            button.OnPointerClick(press);
            Assert.That(accepted, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator AcceptedDirectionLocksAndTheSolidWallStopsTheRoll()
        {
            var control = new GameObject("Joystick", typeof(RectTransform), typeof(VirtualJoystick));
            SceneManager.MoveGameObjectToScene(control, scene);
            ((RectTransform)control.transform).sizeDelta = new Vector2(100, 100);
            var joystick = control.GetComponent<VirtualJoystick>();
            movement.BindJoystick(joystick);
            var right = new PointerEventData(null)
            {
                button = PointerEventData.InputButton.Left,
                pointerId = 1,
                position = Vector2.right * 50f
            };
            joystick.OnPointerDown(right);
            Assert.That(movement.SampleDirection(), Is.EqualTo(Vector2.right));
            Assert.That(movement.TryDodge(run, now), Is.True);
            Assert.That(movement.DodgeDirection, Is.EqualTo(Vector2.right));

            var wall = new GameObject("Solid wall");
            SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = new Vector3(0.8f, 0f, 0f);
            var collider = wall.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(0.06f, 3f);
            joystick.OnPointerUp(right);

            yield return SimulateDodge();

            Assert.That(body.position.x, Is.InRange(0.3f, 0.46f));
            Assert.That(body.position.y, Is.Zero.Within(0.001f));
            Assert.That(movement.IsDodging, Is.False);
            now = Ms(1250);
            Assert.That(movement.TryDodge(run, now), Is.True);
            Assert.That(movement.DodgeDirection, Is.EqualTo(Vector2.right));
        }

        [UnityTest]
        public IEnumerator DodgeAtExtractionCompletionNotifiesBeforePhysicsAndDeadlineStillRejects()
        {
            now = Ms(500);
            ExtractionAttempt attempt = run.TryBeginExtraction(runId, isInRange: true);
            Assert.That(attempt, Is.Not.Null);
            now = attempt.CompletesAt;

            int observations = 0;
            movement.DodgeStarted += (observedRun, effectiveAt) =>
            {
                observations++;
                Assert.That(observedRun, Is.EqualTo(runId));
                Assert.That(effectiveAt, Is.EqualTo(now));
                Assert.That(body.position, Is.EqualTo(Vector2.zero));
                Assert.That(run.ActiveExtraction, Is.SameAs(attempt));
                run.ProcessSignals(new[]
                {
                    RunSignal.CompleteExtraction(observedRun, attempt.Id, effectiveAt),
                    RunSignal.CancelExtraction(observedRun, attempt.Id, effectiveAt)
                });
            };

            Assert.That(movement.TryDodge(run, now), Is.True);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(observations, Is.EqualTo(1));
            yield return SimulateDodge();

            now = Ms(10000);
            Assert.That(movement.TryDodge(run, now), Is.False);
            Assert.That(observations, Is.EqualTo(1));
        }

        private IEnumerator SimulateDodge()
        {
            PhysicsScene2D physics = scene.GetPhysicsScene2D();
            for (int step = 0; movement.IsDodging && step < 15; step++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(physics.Simulate(Time.fixedDeltaTime), Is.True);
            }
            Assert.That(movement.IsDodging, Is.False);
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
#endif
