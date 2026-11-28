#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Enemies;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectWeapon.AutoAttack.Tests
{
    public sealed class FrostSpearPatternTests
    {
        private const string Folder = "Assets/ProjectWeapon/Weapons/FrostSpear/";
        private const int WallLayer = 2;
        private static readonly LayerMask SolidLayers = 1 << WallLayer;
        private static readonly WorldPoint2D Origin = new WorldPoint2D(0, 0);
        private readonly Dictionary<Collider2D, TargetCandidate> candidates = new Dictionary<Collider2D, TargetCandidate>();
        private WeaponDefinition spear = null!;
        private AutoAttackScheduler scheduler = null!;
        private Scene scene;
        private FrostSpearStrike? strike;
        private Guid applicationId;

        [SetUp]
        public void SetUp()
        {
            spear = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Folder + "FrostSpear.asset");
            Assert.That(spear, Is.Not.Null);
            candidates.Clear();
            strike = null;
            scene = EditorSceneManager.NewPreviewScene();
            applicationId = Guid.NewGuid();
            var run = new RunState(() => TimeSpan.Zero);
            Assert.That(run.TryStartRun(RunId.New(), 1, Ms(5000)), Is.True);
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid())
                EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void RealDefinitionLinksOneGradeThreeBasicAttack()
        {
            var pattern = AssetDatabase.LoadAssetAtPath<FrostSpearPattern>(Folder + "FrostSpearPiercingFreeze.asset");
            Assert.That(spear.Id.Value, Is.EqualTo("frost_spear"));
            Assert.That(spear.Grade, Is.EqualTo(3));
            Assert.That(spear.Kind, Is.EqualTo(WeaponKind.Spear));
            Assert.That(spear.BasicAttack, Is.SameAs(pattern));
            Assert.That(spear.Skill, Is.Null);
            Assert.That(pattern.Range, Is.EqualTo(4.5f));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(1250)));
        }

        [Test]
        public void SweptCollidersPierceDistinctTargetsButStopAtWallBeforeEqualAndFartherContacts()
        {
            TargetCandidate first = Candidate("first", 1);
            AddTarget(first, 1);
            AddTarget(first, 1.1f);
            AddTarget(Candidate("dead", 1.5f, alive: false), 1.5f);
            AddTarget(Candidate("friendly", 1.7f, attackable: false), 1.7f);
            AddTarget(Candidate("attacker", 1.9f), 1.9f);
            AddTarget(Candidate("second", 2), 2);
            AddTarget(Candidate("off-axis", 2.5f), 2.5f, 0.7f);
            AddTarget(Candidate("tie", 3.5f), 3.5f);
            Box(3.5f, 0, WallLayer, isTrigger: false);
            AddTarget(Candidate("behind", 4), 4);

            Assert.That(AttackAt(100, first), Is.True);
            FrostSpearStrike actual = strike!.Value;
            Assert.That(actual.Origin, Is.EqualTo(Origin));
            Assert.That(actual.End.X, Is.EqualTo(3.4f).Within(0.001));
            Assert.That(actual.End.Y, Is.Zero);
            CollectionAssert.AreEqual(new[] { "first", "second" }, actual.Hits.Select(hit => hit.Damage.TargetId));
            foreach (MovementFreezeHit hit in actual.Hits)
            {
                Assert.That(hit.Damage.ApplicationId, Is.EqualTo(applicationId));
                Assert.That(hit.Damage.Amount, Is.EqualTo(14f));
                Assert.That(hit.EffectiveAt, Is.EqualTo(Ms(100)));
                Assert.That(hit.ResumeAt, Is.EqualTo(Ms(1000)));
            }
        }

        [Test]
        public void ExactRangeAndCooldownAreInclusiveButRunDeadlinePreventsAnAttack()
        {
            TargetCandidate boundary = Candidate("boundary", 4.5f);
            AddTarget(boundary, 4.6f);
            AddTarget(Candidate("past-tip", 4.7f), 4.7f);
            Box(3f, 0, WallLayer, isTrigger: true);
            Assert.That(AttackAt(100, Candidate("too-far", 4.501f)), Is.False);
            Guid valid = applicationId;
            applicationId = Guid.Empty;
            Assert.That(AttackAt(100, boundary), Is.False);
            applicationId = valid;
            Assert.That(AttackAt(100, boundary), Is.True);
            Assert.That(strike!.Value.End.X, Is.EqualTo(4.5f));
            CollectionAssert.AreEqual(new[] { "boundary" }, strike.Value.Hits.Select(hit => hit.Damage.TargetId));
            Assert.That(AttackAt(100, boundary), Is.False);
            Assert.That(AttackAt(1349, boundary), Is.False);
            Assert.That(AttackAt(1350, boundary), Is.True);
            Assert.That(AttackAt(5000, boundary), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SamePositionContactsUseContainmentAndSolidWinsAtOrigin(bool blocked)
        {
            TargetCandidate target = Candidate("overlap", 0);
            AddTarget(target, 0);
            AddTarget(target, 0.05f);
            AddTarget(Candidate("separate", 1), 1);
            if (blocked)
                Box(0, 0, WallLayer, isTrigger: false);
            Assert.That(AttackAt(100, target), Is.True);
            Assert.That(strike!.Value.End, Is.EqualTo(Origin));
            Assert.That(strike.Value.Hits.Count, Is.EqualTo(blocked ? 0 : 1));
        }

        [Test]
        public void EnemyMovementOutputStopsImmediatelyAndResumesAtExactExpiry()
        {
            var enemy = CreateEnemy(out Rigidbody2D body);
            enemy.Advance(Ms(50), Candidate("player", 0));
            Assert.That(body.linearVelocity.x, Is.LessThan(0));

            MovementFreezeHit hit = CreateEnemyHit();
            Assert.That(enemy.ApplyMovementFreezeHit(hit), Is.True);
            Assert.That(enemy.Brain.CurrentHealth, Is.EqualTo(10f));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            Assert.That(enemy.ApplyMovementFreezeHit(hit), Is.False);
            enemy.Advance(Ms(999), Candidate("player", 0));
            Assert.That(enemy.Brain.MovementVelocity.X, Is.LessThan(0));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
            enemy.Advance(Ms(1000), Candidate("player", 0));
            Assert.That(body.linearVelocity.x, Is.LessThan(0));
        }

        [Test]
        public void FrozenActorResolvesItsPreviouslyScheduledAttackAtTheOriginalTime()
        {
            MeleeEnemyActor enemy = CreateEnemy(out Rigidbody2D body);
            TargetCandidate player = Candidate("player", 1);
            enemy.Advance(Ms(50), player);
            TimeSpan originalImpact = enemy.Brain.ActiveAttack!.Value.HitsAt;
            int resolutions = 0;
            enemy.Brain.AttackResolved += (attack, hits) =>
            {
                Assert.That(attack.HitsAt, Is.EqualTo(originalImpact));
                Assert.That(hits.Single().TargetId, Is.EqualTo("player"));
                resolutions++;
            };

            MovementFreezeHit hit = CreateEnemyHit();
            Assert.That(enemy.ApplyMovementFreezeHit(hit), Is.True);
            Assert.That(hit.ResumeAt, Is.GreaterThan(originalImpact));
            Assert.That(enemy.Brain.ActiveAttack!.Value.HitsAt, Is.EqualTo(originalImpact));
            enemy.Advance(originalImpact - TimeSpan.FromTicks(1), player);
            Assert.That(resolutions, Is.Zero);
            enemy.Advance(originalImpact, player);
            Assert.That(resolutions, Is.EqualTo(1));
            Assert.That(enemy.Brain.Phase, Is.EqualTo(MeleeEnemyPhase.Recovering));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
        }

        private MovementFreezeHit CreateEnemyHit()
        {
            TargetCandidate target = Candidate("enemy", 2);
            Assert.That(AttackAt(100, target), Is.True);
            return strike!.Value.Hits.Single();
        }

        private MeleeEnemyActor CreateEnemy(out Rigidbody2D body)
        {
            var actorObject = new GameObject("Melee actor");
            SceneManager.MoveGameObjectToScene(actorObject, scene);
            actorObject.transform.position = new Vector3(2, 0);
            var enemy = actorObject.AddComponent<MeleeEnemyActor>();
            var settings = AssetDatabase.LoadAssetAtPath<MeleeEnemySettings>(
                "Assets/ProjectWeapon/Enemies/Melee/PrototypeMeleeEnemySettings.asset");
            enemy.Initialize("enemy", settings);
            body = actorObject.GetComponent<Rigidbody2D>();
            body.position = new Vector2(2, 0);
            var hurtbox = actorObject.AddComponent<BoxCollider2D>();
            hurtbox.isTrigger = true;
            candidates.Add(hurtbox, Candidate("enemy", 2));
            return enemy;
        }

        private bool Execute(BasicAttackRequest request)
        {
            if (request.Pattern is not FrostSpearPattern pattern || !pattern.TryCreateStrike(request,
                    applicationId, "attacker", scene.GetPhysicsScene2D(), SolidLayers, LookupTarget, out var result))
                return false;

            strike = result;
            return true;
        }

        private bool AttackAt(int milliseconds, params TargetCandidate[] targets)
        {
            Physics2D.SyncTransforms();
            return scheduler.TryAttack(Ms(milliseconds), Origin, spear, targets);
        }

        private TargetCandidate? LookupTarget(Collider2D collider)
            => candidates.TryGetValue(collider, out TargetCandidate target) ? target : null;

        private void AddTarget(TargetCandidate target, float x, float y = 0)
            => candidates.Add(Box(x, y, 0, isTrigger: true), target);

        private BoxCollider2D Box(float x, float y, int layer, bool isTrigger)
        {
            var item = new GameObject("Sweep fixture");
            SceneManager.MoveGameObjectToScene(item, scene);
            item.transform.position = new Vector3(x, y);
            item.layer = layer;
            var shape = item.AddComponent<BoxCollider2D>();
            shape.size = new Vector2(0.2f, 0.2f);
            shape.isTrigger = isTrigger;
            return shape;
        }

        private static TargetCandidate Candidate(string id, float x, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(x, 0), alive, attackable);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
