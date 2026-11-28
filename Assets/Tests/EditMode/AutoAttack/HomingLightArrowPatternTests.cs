#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectWeapon.AutoAttack.Tests
{
    public sealed class HomingLightArrowPatternTests
    {
        private const string WeaponFolder = "Assets/ProjectWeapon/Weapons/SunBow/";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(0, 0);
        private readonly List<HomingLightArrow> arrows = new List<HomingLightArrow>();
        private AutoAttackScheduler scheduler = null!;
        private WeaponDefinition bow = null!;
        private Guid applicationId;

        [SetUp]
        public void SetUp()
        {
            arrows.Clear();
            applicationId = Guid.NewGuid();
            bow = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponFolder + "SunBow.asset");
            Assert.That(bow, Is.Not.Null);
            var run = new RunState(() => TimeSpan.Zero);
            Assert.That(run.TryStartRun(new RunId(Guid.NewGuid()), 1, Ms(20000)), Is.True);
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [Test]
        public void GradeFourBowLinksOneHomingBasicAttack()
        {
            var pattern = AssetDatabase.LoadAssetAtPath<HomingLightArrowPattern>(WeaponFolder + "SunBowHomingLightArrow.asset");
            Assert.That(bow.Id.Value, Is.EqualTo("sun_bow"));
            Assert.That(bow.Grade, Is.EqualTo(4));
            Assert.That(bow.Kind, Is.EqualTo(WeaponKind.Bow));
            Assert.That(bow.BasicAttack, Is.SameAs(pattern));
            Assert.That(bow.Skill, Is.Null);
            Assert.That(pattern.Range, Is.EqualTo(6f));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(1500)));
        }

        [Test]
        public void MovingLockedTargetSteersWithBoundedTurnAndOnlyHitsOnceOnContact()
        {
            var locked = Candidate("locked", 3, 0);
            var other = Candidate("other", 4, 0);
            Assert.That(AttackAt(100, other, Candidate("dead", 1, 0, alive: false), locked), Is.True);
            Assert.That(arrows.Count, Is.EqualTo(1));
            HomingLightArrow arrow = arrows[0];
            Assert.That(arrow.LockedTargetId, Is.EqualTo("locked"));
            Assert.That(arrow.Position, Is.EqualTo(Origin));
            Assert.That(arrow.Velocity.X, Is.EqualTo(10).Within(0.0001));

            // A 90 degree change in target heading turns only 54 degrees in 100ms.
            locked = Candidate("locked", 0, 4);
            Assert.That(arrow.Advance(applicationId, Ms(100), locked), Is.True);
            double expected = Math.PI * 54 / 180;
            Assert.That(arrow.Velocity.X, Is.EqualTo(10 * Math.Cos(expected)).Within(0.0001));
            Assert.That(arrow.Velocity.Y, Is.EqualTo(10 * Math.Sin(expected)).Within(0.0001));
            WorldPoint2D firstPosition = arrow.Position;
            Assert.That(firstPosition.X, Is.EqualTo(Math.Cos(expected)).Within(0.0001));
            Assert.That(firstPosition.Y, Is.EqualTo(Math.Sin(expected)).Within(0.0001));
            Assert.That(arrow.Advance(applicationId, Ms(100), locked), Is.True);
            Assert.That(arrow.Position, Is.EqualTo(firstPosition));
            Assert.That(arrow.Advance(applicationId, Ms(200), locked), Is.True);
            Assert.That(arrow.Position.Y, Is.GreaterThan(firstPosition.Y));
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.Active));
            Assert.That(arrow.ContactTarget(applicationId, Ms(200), other), Is.Null);

            DamageRequest? hit = arrow.ContactTarget(applicationId, Ms(200), locked);
            Assert.That(hit?.TargetId, Is.EqualTo("locked"));
            Assert.That(hit?.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(hit?.AttackerId, Is.EqualTo("archer"));
            Assert.That(hit?.Amount, Is.EqualTo(22f));
            Assert.That(arrow.ContactTarget(applicationId, Ms(200), locked), Is.Null);
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.HitTarget));
        }

        [TestCase("missing")]
        [TestCase("differentId")]
        [TestCase("dead")]
        [TestCase("unattackable")]
        [TestCase("invalidPosition")]
        public void LosingLockCoastsAndNeitherReacquiresNorRetargets(string condition)
        {
            TargetCandidate locked = Candidate("locked", 3, 0);
            Assert.That(AttackAt(100, locked), Is.True);
            HomingLightArrow arrow = arrows[0];
            TargetCandidate? observation = condition switch
            {
                "missing" => null,
                "differentId" => Candidate("other", 0, 1),
                "dead" => Candidate("locked", 0, 1, alive: false),
                "unattackable" => new TargetCandidate("locked", new WorldPoint2D(0, 1), true, false),
                _ => Candidate("locked", float.NaN, 1)
            };

            Assert.That(arrow.Advance(applicationId, Ms(100), observation), Is.True);
            Assert.That(arrow.IsTracking, Is.False);
            Assert.That(arrow.LockedTargetId, Is.EqualTo("locked"));
            Assert.That(arrow.Advance(applicationId, Ms(200), Candidate("locked", 0, 2)), Is.True);
            Assert.That(arrow.Position.X, Is.EqualTo(2).Within(0.0001));
            Assert.That(arrow.Position.Y, Is.Zero);
            Assert.That(arrow.ContactTarget(applicationId, Ms(200), Candidate("other", 1, 0)), Is.Null);
            Assert.That(arrow.ContactTarget(applicationId, Ms(200), locked), Is.Null);
            Assert.That(arrow.ContactWorld(applicationId, Ms(200)), Is.True);
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.Blocked));
        }

        [Test]
        public void PoolRejectsOldCallbacksAndExactLifetimeWinsOverAContact()
        {
            TargetCandidate locked = Candidate("locked", 3, 0);
            Assert.That(AttackAt(100, locked), Is.True);
            HomingLightArrow arrow = arrows[0];
            Assert.That(arrow.Lifetime, Is.EqualTo(Ms(1200)));
            Assert.That(arrow.Advance(applicationId, Ms(1200), locked), Is.True);
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.Expired));
            Assert.That(arrow.ContactTarget(applicationId, Ms(1200), locked), Is.Null);
            Assert.That(arrow.ContactWorld(applicationId, Ms(1200)), Is.False);

            arrow.ResetForPool();
            Assert.That(arrow.LockedTargetId, Is.Null);
            Assert.That(arrow.Position, Is.EqualTo(default(WorldPoint2D)));
            Assert.That(arrow.Velocity, Is.EqualTo(default(WorldPoint2D)));
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.Inactive));

            Guid staleId = applicationId;
            applicationId = Guid.NewGuid();
            Assert.That(AttackAt(1600, locked), Is.True);
            Assert.That(arrows[1], Is.SameAs(arrow));
            Assert.That(arrow.Advance(staleId, Ms(200), null), Is.False);
            Assert.That(arrow.ContactTarget(staleId, TimeSpan.Zero, locked), Is.Null);
            Assert.That(arrow.ContactWorld(staleId, TimeSpan.Zero), Is.False);
            Assert.That(arrow.IsTracking, Is.True);
            Assert.That(arrow.ContactTarget(applicationId, TimeSpan.Zero, locked)?.TargetId, Is.EqualTo("locked"));
        }

        [Test]
        public void LaunchRangeAndCooldownAreInclusiveAndFailedLaunchConsumesNoCooldown()
        {
            Assert.That(AttackAt(100, Candidate("outside", 6.001f, 0)), Is.False);
            var boundary = Candidate("boundary", 6, 0);
            Guid validId = applicationId;
            applicationId = Guid.Empty;
            Assert.That(AttackAt(100, boundary), Is.False);
            applicationId = validId;
            Assert.That(AttackAt(100, boundary), Is.True);
            Assert.That(AttackAt(100, boundary), Is.False);
            Assert.That(AttackAt(1599, boundary), Is.False);
            Assert.That(arrows.Count, Is.EqualTo(1));
            applicationId = Guid.NewGuid();
            Assert.That(AttackAt(1600, boundary), Is.True);
            Assert.That(arrows.Count, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlaceholderColliderSweepConfirmsWorldOrTargetContact(bool blocked)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            var targetObject = new GameObject("Target");
            SceneManager.MoveGameObjectToScene(targetObject, scene);
            targetObject.transform.position = new Vector3(2, 0);
            BoxCollider2D targetCollider = targetObject.AddComponent<BoxCollider2D>();
            var wallObject = new GameObject("Wall");
            SceneManager.MoveGameObjectToScene(wallObject, scene);
            wallObject.transform.position = new Vector3(1, blocked ? 0 : 2);
            BoxCollider2D wallCollider = wallObject.AddComponent<BoxCollider2D>();
            try
            {
                Physics2D.SyncTransforms();
                TargetCandidate target = Candidate("target", 2, 0);
                Assert.That(AttackAt(100, target), Is.True);
                HomingLightArrow arrow = arrows[0];
                WorldPoint2D start = arrow.Position;
                Assert.That(arrow.Advance(applicationId, Ms(250), target), Is.True);
                WorldPoint2D end = arrow.Position;
                var contacts = new List<RaycastHit2D>();
                scene.GetPhysicsScene2D().Linecast(new Vector2(start.X, start.Y), new Vector2(end.X, end.Y),
                    ContactFilter2D.noFilter, contacts);
                RaycastHit2D contact = contacts.Find(
                    hit => hit.collider == targetCollider || hit.collider == wallCollider);
                Assert.That(contact.collider, Is.Not.Null);

                if (contact.collider == targetCollider)
                {
                    Assert.That(blocked, Is.False);
                    Assert.That(arrow.ContactTarget(applicationId, Ms(250), target)?.TargetId, Is.EqualTo("target"));
                }
                else
                {
                    Assert.That(blocked, Is.True);
                    Assert.That(arrow.ContactWorld(applicationId, Ms(250)), Is.True);
                    Assert.That(arrow.ContactTarget(applicationId, Ms(250), target), Is.Null);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private bool Execute(BasicAttackRequest request)
        {
            HomingLightArrow rental = arrows.Count == 1 && arrows[0].Phase == ProjectilePhase.Inactive
                ? arrows[0]
                : new HomingLightArrow();
            if (request.Pattern is not HomingLightArrowPattern pattern ||
                !pattern.TryLaunch(request, applicationId, "archer", rental))
                return false;

            arrows.Add(rental);
            return true;
        }

        private bool AttackAt(int milliseconds, params TargetCandidate[] targets)
            => scheduler.TryAttack(Ms(milliseconds), Origin, bow, targets);

        private static TargetCandidate Candidate(string id, float x, float y, bool alive = true)
            => new TargetCandidate(id, new WorldPoint2D(x, y), alive, isAttackable: true);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
