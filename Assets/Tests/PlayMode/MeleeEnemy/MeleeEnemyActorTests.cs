#nullable enable

using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Enemies.Tests
{
    public sealed class MeleeEnemyActorTests
    {
        [UnityTest]
        public IEnumerator PhysicsBodyMovesDuringPursuitAndStopsDuringWarningOrDeath()
        {
            var actorObject = new GameObject("Melee enemy test");
            var settings = ScriptableObject.CreateInstance<MeleeEnemySettings>();
            try
            {
                var actor = actorObject.AddComponent<MeleeEnemyActor>();
                var body = actorObject.GetComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                actor.Initialize("enemy", settings);
                var far = new TargetCandidate("player", new WorldPoint2D(5, 0), true, true);

                actor.Advance(TimeSpan.Zero, far);
                Assert.That(body.linearVelocity.x, Is.EqualTo(2.2f));
                yield return new WaitForFixedUpdate();
                yield return null;
                Assert.That(body.position.x, Is.GreaterThan(0f));

                var position = new WorldPoint2D(body.position.x, body.position.y);
                var near = new TargetCandidate("player", new WorldPoint2D(position.X + 1f, position.Y), true, true);
                actor.Advance(TimeSpan.FromMilliseconds(20), near);
                Assert.That(actor.Brain.Phase, Is.EqualTo(MeleeEnemyPhase.Telegraphing));
                Assert.That(actor.Brain.ActiveAttack?.Area.Origin, Is.EqualTo(position));
                Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));

                AreaDamageRequest incoming = AreaDamageRequest.Circle(Guid.NewGuid(), "player", position, 0, 30);
                var enemy = new TargetCandidate("enemy", position, true, true);
                Assert.That(actor.ApplyDamage(AreaDamageSelector.FindTargets(incoming, new[] { enemy }).Single(),
                    TimeSpan.FromMilliseconds(21)), Is.True);
                Assert.That(actor.Brain.Phase, Is.EqualTo(MeleeEnemyPhase.Dead));
                Assert.That(actor.Brain.ActiveAttack, Is.Null);
                Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            }
            finally
            {
                Object.Destroy(actorObject);
                Object.Destroy(settings);
            }
        }
    }
}
