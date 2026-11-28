#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;

namespace ProjectWeapon.AutoAttack.Tests
{
    public sealed class SingleArrowPatternTests
    {
        private const string StarterPath = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private const string Attacker = "archer";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(2, 4);
        private static readonly WeaponDefinitionId BowId = new WeaponDefinitionId("hunting_bow");
        private readonly List<ProjectileLifecycle> arrows = new List<ProjectileLifecycle>();
        private WeaponDefinition bow = null!;
        private AutoAttackScheduler scheduler = null!;
        private Guid applicationId;

        [SetUp]
        public void SetUp()
        {
            arrows.Clear();
            applicationId = Guid.NewGuid();
            var run = new RunState(() => TimeSpan.Zero);
            Assert.That(run.TryStartRun(new RunId(Guid.NewGuid()), 1, Ms(20000)), Is.True);
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(StarterPath + "PrototypeStarterWeaponCatalog.asset");
            bow = catalog.CreateLookup()[BowId];
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [Test]
        public void CatalogLinksExactlyOneGradeOneBowToSingleArrow()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(StarterPath + "PrototypeStarterWeaponCatalog.asset");
            var pattern = AssetDatabase.LoadAssetAtPath<SingleArrowPattern>(StarterPath + "HuntingBowSingleArrow.asset");

            Assert.That(pattern, Is.Not.Null);
            Assert.That(bow.Grade, Is.EqualTo(1));
            Assert.That(bow.Kind, Is.EqualTo(WeaponKind.Bow));
            Assert.That(bow.BasicAttack, Is.SameAs(pattern));
            Assert.That(catalog.CreateLookup().Values.Count(weapon => weapon.BasicAttack is SingleArrowPattern), Is.EqualTo(1));
        }

        [Test]
        public void LaunchAimsAtNearestValidTargetAndDamagesOnlyTheFirstConfirmedContact()
        {
            TargetCandidate first = Candidate("nearest", 1.5f, 2);
            Assert.That(AttackAt(100, Candidate("farther", 5, 0), Candidate("dead", 1, 0, alive: false),
                Candidate("friendly", 0, 2, attackable: false), first), Is.True);
            Assert.That(arrows, Has.Count.EqualTo(1));

            ProjectileLifecycle arrow = arrows[0];
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.Active));
            Assert.That(arrow.Position, Is.EqualTo(Origin));
            Assert.That(arrow.Velocity.X, Is.EqualTo(6f));
            Assert.That(arrow.Velocity.Y, Is.EqualTo(8f));
            Assert.That(arrow.Advance(applicationId, Ms(125)), Is.True);
            Assert.That(arrow.Position, Is.EqualTo(new WorldPoint2D(2.75f, 5f)));
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.Active));

            DamageRequest? hit = arrow.ContactTarget(applicationId, Ms(250), first);
            Assert.That(hit?.TargetId, Is.EqualTo("nearest"));
            Assert.That(hit?.AttackerId, Is.EqualTo(Attacker));
            Assert.That(hit?.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(hit?.Amount, Is.EqualTo(8f));
            Assert.That(arrow.ContactTarget(applicationId, Ms(250), first), Is.Null);
            Assert.That(arrow.ContactTarget(applicationId, Ms(250), Candidate("other", 1.5f, 2)), Is.Null);
            Assert.That(arrow.Phase, Is.EqualTo(ProjectilePhase.HitTarget));
        }

        [Test]
        public void OutOfRangeAndRejectedLaunchLeaveCooldownAvailableUntilOneArrowStarts()
        {
            Assert.That(AttackAt(100, Candidate("outside", 5.001f, 0)), Is.False);
            TargetCandidate boundary = Candidate("boundary", 5, 0);
            Guid valid = applicationId;
            applicationId = Guid.Empty;
            Assert.That(AttackAt(100, boundary), Is.False);
            Assert.That(arrows, Is.Empty);

            applicationId = valid;
            Assert.That(AttackAt(100, boundary), Is.True);
            Assert.That(arrows[0].ContactTarget(applicationId, Ms(500), boundary)?.TargetId, Is.EqualTo("boundary"));
            Assert.That(AttackAt(100, boundary), Is.False);
            Assert.That(AttackAt(1199, boundary), Is.False);
            applicationId = Guid.NewGuid();
            Assert.That(AttackAt(1200, boundary), Is.True);
            Assert.That(arrows, Has.Count.EqualTo(2));
            Assert.That(arrows[0].ApplicationId, Is.Not.EqualTo(arrows[1].ApplicationId));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SolidContactOrExactLifetimePreventsLaterDamage(bool hitWall)
        {
            TargetCandidate target = Candidate("target", 5, 0);
            Assert.That(AttackAt(100, target), Is.True);
            ProjectileLifecycle arrow = arrows[0];
            if (hitWall)
                Assert.That(arrow.ContactWorld(applicationId, Ms(250)), Is.True);

            Assert.That(arrow.ContactTarget(applicationId, Ms(hitWall ? 251 : 550), target), Is.Null);
            Assert.That(arrow.Phase, Is.EqualTo(hitWall ? ProjectilePhase.Blocked : ProjectilePhase.Expired));
        }

        [Test]
        public void AnOverlappingTargetWaitsForContactAtTheOrigin()
        {
            TargetCandidate target = Candidate("overlap", 0, 0);
            Assert.That(AttackAt(100, target), Is.True);
            ProjectileLifecycle arrow = arrows[0];

            Assert.That(arrow.Velocity, Is.EqualTo(default(WorldPoint2D)));
            Assert.That(arrow.ContactTarget(applicationId, TimeSpan.Zero, target)?.TargetId, Is.EqualTo("overlap"));
        }

        private bool Execute(BasicAttackRequest request)
        {
            var rental = new ProjectileLifecycle();
            if (request.Pattern is not SingleArrowPattern pattern
                || !pattern.TryLaunch(request, applicationId, Attacker, rental))
                return false;

            arrows.Add(rental);
            return true;
        }

        private bool AttackAt(int milliseconds, params TargetCandidate[] targets)
            => scheduler.TryAttack(Ms(milliseconds), Origin, bow, targets);

        private static TargetCandidate Candidate(
            string id, float x, float y, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + x, Origin.Y + y), alive, attackable);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
