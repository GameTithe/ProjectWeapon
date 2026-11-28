#nullable enable

using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectWeapon.Combat.Tests
{
    public sealed class PullMotionPlayModeTests
    {
        private const float StepSeconds = 0.02f;
        private const int Walls = 8;
        private Scene scene;
        private PhysicsScene2D physics;

        [SetUp]
        public void SetUp()
        {
            scene = SceneManager.CreateScene("Pull validation " + Guid.NewGuid(),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physics = scene.GetPhysicsScene2D();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void BodyMovesAtConfiguredSpeedAndStopsExactlyAtCenter()
        {
            Rigidbody2D body = Actor(new Vector2(.48f, 0), RigidbodyType2D.Dynamic);
            Assert.That(PullMotion.TryStart(body, Vector2.zero, Settings(speed: 5), true, out PullMotion? motion), Is.True);
            PullStep first = Advance(body, motion!);
            Assert.That(first.Status, Is.EqualTo(PullStatus.Active));
            Assert.That(body.position.x, Is.EqualTo(.38f).Within(.001f));

            for (int i = 0; i < 10; i++)
                Advance(body, motion!);

            Assert.That(motion!.Status, Is.EqualTo(PullStatus.ReachedCenter));
            Assert.That(body.position.x, Is.EqualTo(0).Within(.001f));
        }

        [Test]
        public void CastStopsBeforeSolidWallAndIgnoresCloserTrigger()
        {
            Rigidbody2D body = Actor(new Vector2(2, 0), RigidbodyType2D.Kinematic);
            var targetSensor = body.gameObject.AddComponent<CircleCollider2D>();
            targetSensor.radius = .95f;
            targetSensor.isTrigger = true;
            Obstacle(1.7f, true);
            Obstacle(1, false);
            Physics2D.SyncTransforms();
            Assert.That(PullMotion.TryStart(body, Vector2.zero, Settings(speed: 10), true, out PullMotion? motion), Is.True);
            for (int i = 0; i < 10; i++)
                Advance(body, motion!);

            Assert.That(motion!.Status, Is.EqualTo(PullStatus.Blocked));
            Assert.That(body.position.x, Is.EqualTo(1.41f).Within(.02f));
            Assert.That(Advance(body, motion!).Displacement, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void RadiusBoundaryIsInclusiveAndStoppingDoesNotMoveTheBody()
        {
            Rigidbody2D body = Actor(new Vector2(2, 0), RigidbodyType2D.Dynamic);
            Assert.That(PullMotion.TryStart(body, Vector2.zero, Settings(range: 1.99f), true, out _), Is.False);
            Assert.That(PullMotion.TryStart(body, Vector2.zero, Settings(range: 2), false, out _), Is.False);
            Assert.That(PullMotion.TryStart(body, Vector2.zero, Settings(range: 2), true, out PullMotion? motion), Is.True);

            PullStep interrupted = motion!.Advance(StepSeconds, isStillEligible: false);
            Assert.That(interrupted.Status, Is.EqualTo(PullStatus.Cancelled));
            Assert.That(interrupted.Displacement, Is.EqualTo(Vector2.zero));
            Assert.That(body.position.x, Is.EqualTo(2));
        }

        [Test]
        public void PartialFinalStepDoesNotRoundDurationUpOrMoveAgain()
        {
            Rigidbody2D body = Actor(new Vector2(2, 0), RigidbodyType2D.Kinematic);
            Assert.That(PullMotion.TryStart(body, Vector2.zero, Settings(speed: 10, duration: .03f), true, out PullMotion? motion), Is.True);
            Assert.That(Advance(body, motion!).Status, Is.EqualTo(PullStatus.Active));
            Assert.That(Advance(body, motion!).Status, Is.EqualTo(PullStatus.DurationElapsed));
            Assert.That(Advance(body, motion!).Displacement, Is.EqualTo(Vector2.zero));
            Assert.That(body.position.x, Is.EqualTo(1.7f).Within(.005f));
        }

        private static PullSettings Settings(float range = 3, float speed = 5, float duration = 1)
            => new PullSettings(range, speed, duration, 1 << Walls);

        private PullStep Advance(Rigidbody2D body, PullMotion motion)
        {
            PullStep step = motion.Advance(StepSeconds, true);
            if (body.bodyType == RigidbodyType2D.Dynamic)
                body.linearVelocity = step.Displacement / StepSeconds;
            else
                body.MovePosition(body.position + step.Displacement);
            Assert.That(physics.Simulate(StepSeconds), Is.True);
            return step;
        }

        private Rigidbody2D Actor(Vector2 position, RigidbodyType2D bodyType)
        {
            var actor = new GameObject("Eligible pull target");
            SceneManager.MoveGameObjectToScene(actor, scene);
            var body = actor.AddComponent<Rigidbody2D>();
            body.gravityScale = 0;
            body.bodyType = bodyType;
            body.position = position;
            actor.AddComponent<CircleCollider2D>().radius = .3f;
            Physics2D.SyncTransforms();
            return body;
        }

        private void Obstacle(float x, bool trigger)
        {
            var wall = new GameObject(trigger ? "Trigger" : "Solid wall");
            SceneManager.MoveGameObjectToScene(wall, scene);
            wall.layer = Walls;
            wall.transform.position = new Vector3(x, 0, 0);
            var collider = wall.AddComponent<BoxCollider2D>();
            collider.isTrigger = trigger;
            collider.size = new Vector2(.2f, 4);
        }
    }
}
