#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;

namespace ProjectWeapon.AutoAttack.Tests
{
    public sealed class RepeaterBowPatternTests
    {
        private const string Folder = "Assets/ProjectWeapon/Weapons/PrototypeRepeaterBow/";
        private const string AttackerId = "player";
        private static readonly WeaponDefinitionId DefinitionId = new WeaponDefinitionId("repeater_bow");
        private static readonly WorldPoint2D Origin = new WorldPoint2D(2, 3);

        private RunState run = null!;
        private RunId runId;
        private TimeSpan now;
        private WeaponDefinition bow = null!;
        private RepeaterBowPattern pattern = null!;
        private AutoAttackScheduler scheduler = null!;
        private RepeaterBowVolley volley = null!;
        private ProjectileLifecycle first = null!;
        private string? aimedAt;
        private IReadOnlyList<TargetCandidate> targets = Array.Empty<TargetCandidate>();

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Folder + "PrototypeRepeaterBowWeaponCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            var lookup = catalog.CreateLookup();
            Assert.That(lookup.Keys, Is.EquivalentTo(new[] { DefinitionId }));
            bow = lookup[DefinitionId];
            Assert.That(bow.BasicAttack, Is.TypeOf<RepeaterBowPattern>());
            pattern = (RepeaterBowPattern)bow.BasicAttack;

            now = TimeSpan.Zero;
            run = new RunState(() => now);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(15000)), Is.True);
            scheduler = new AutoAttackScheduler(run, BeginVolley, initiallyEnabled: true);
            targets = new[] { Candidate("aim", 1.5f, 0) };
        }

        [Test]
        public void GradeTwoDefinitionReferencesItsSingleConfiguredVolley()
        {
            Assert.That(bow, Is.SameAs(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Folder + "RepeaterBow.asset")));
            Assert.That(bow.DisplayName, Is.EqualTo("연사궁"));
            Assert.That(bow.Grade, Is.EqualTo(2));
            Assert.That(bow.Kind, Is.EqualTo(WeaponKind.Bow));
            Assert.That(bow.PrototypeBasicAttackPattern, Is.EqualTo("연속 화살"));
            Assert.That(bow.Skill, Is.Null);
            Assert.That(bow.AttackVfx, Is.Null);
            Assert.That(pattern, Is.SameAs(AssetDatabase.LoadAssetAtPath<RepeaterBowPattern>(Folder + "RepeaterBowVolley.asset")));
            Assert.That(pattern.ShotCount, Is.EqualTo(3));
            Assert.That(pattern.ShotInterval, Is.EqualTo(Ms(120)));
            Assert.That(pattern.Range, Is.EqualTo(5f));
            Assert.That(pattern.ProjectileSpeed, Is.EqualTo(10f));
            Assert.That(pattern.ProjectileLifetime, Is.EqualTo(Ms(600)));
            Assert.That(pattern.ArrowDamage, Is.EqualTo(6f));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(1000)));
        }

        [Test]
        public void OneActivationLaunchesThreeIndependentArrowsAndOnlyContactEmitsDamage()
        {
            TargetCandidate target = Candidate("aim", 1.5f, 0);
            targets = new[]
            {
                Candidate("further", 0, 4),
                Candidate("dead", 0.1f, 0, isAlive: false),
                target
            };
            Assert.That(AttackAt(100), Is.True);
            Assert.That(aimedAt, Is.EqualTo(target.TargetId));
            Assert.That(volley.TargetId, Is.EqualTo(target.TargetId));
            Assert.That(volley.NextShotAt, Is.EqualTo(Ms(220)));

            var second = new ProjectileLifecycle();
            Assert.That(LaunchAt(219, second), Is.False);
            Assert.That(second.Phase, Is.EqualTo(ProjectilePhase.Inactive));
            Assert.That(LaunchAt(220, second), Is.True);
            Assert.That(volley.NextShotAt, Is.EqualTo(Ms(340)));
            var third = new ProjectileLifecycle();
            Assert.That(LaunchAt(339, third), Is.False);
            Assert.That(LaunchAt(340, third), Is.True);
            Assert.That(volley.IsFinished, Is.True);
            Assert.That(volley.NextShotAt, Is.Null);
            Assert.That(LaunchAt(460, new ProjectileLifecycle()), Is.False);

            var ids = new HashSet<Guid>();
            foreach (ProjectileLifecycle projectile in new[] { first, second, third })
            {
                Guid id = projectile.ApplicationId;
                Assert.That(id, Is.Not.EqualTo(Guid.Empty));
                Assert.That(ids.Add(id), Is.True);
                Assert.That(projectile.Phase, Is.EqualTo(ProjectilePhase.Active));
                Assert.That(projectile.Elapsed, Is.EqualTo(TimeSpan.Zero));
                Assert.That(projectile.Position.X, Is.EqualTo(Origin.X));
                Assert.That(projectile.Position.Y, Is.EqualTo(Origin.Y));
                Assert.That(projectile.Velocity.X, Is.EqualTo(10f));
                Assert.That(projectile.Advance(id, Ms(149)), Is.True);
                Assert.That(projectile.Position.X, Is.LessThan(target.Position.X));
                DamageRequest? damage = projectile.ContactTarget(id, Ms(150), target);
                Assert.That(damage.HasValue, Is.True);
                Assert.That(damage!.Value.TargetId, Is.EqualTo(target.TargetId));
                Assert.That(damage.Value.ApplicationId, Is.EqualTo(id));
                Assert.That(damage.Value.Amount, Is.EqualTo(pattern.ArrowDamage));
                Assert.That(projectile.Position.X, Is.EqualTo(target.Position.X));
                Assert.That(projectile.ContactTarget(id, Ms(150), target), Is.Null);
            }
        }

        [Test]
        public void AcquisitionIncludesExactRangeAndCooldownDoesNotCountIndividualShots()
        {
            targets = new[] { Candidate("outside", 5.001f, 0) };
            Assert.That(AttackAt(100), Is.False);
            TargetCandidate boundary = Candidate("boundary", 5, 0);
            targets = new[] { boundary };
            Assert.That(AttackAt(100), Is.True);
            Assert.That(first.Advance(first.ApplicationId, Ms(500)), Is.True);
            Assert.That(first.Position.X, Is.EqualTo(boundary.Position.X));
            Assert.That(first.ContactTarget(first.ApplicationId, Ms(500), boundary), Is.Not.Null);
            Assert.That(LaunchAt(220, new ProjectileLifecycle()), Is.True);
            Assert.That(LaunchAt(340, new ProjectileLifecycle()), Is.True);
            Assert.That(AttackAt(1099), Is.False);
            Assert.That(AttackAt(1100), Is.True);
            Assert.That(volley.NextShotAt, Is.EqualTo(Ms(1220)));
        }

        [Test]
        public void BlockedAndExpiredArrowsNeverDamageAndRentalFailureKeepsTheDueSlot()
        {
            Assert.That(AttackAt(100), Is.True);
            Guid firstId = first.ApplicationId;
            Assert.That(first.ContactWorld(firstId, Ms(100)), Is.True);
            Assert.That(first.ContactTarget(firstId, Ms(150), targets[0]), Is.Null);
            Assert.That(LaunchAt(220, first), Is.False);
            Assert.That(volley.NextShotAt, Is.EqualTo(Ms(220)));

            first.ResetForPool();
            Assert.That(LaunchAt(220, first), Is.True);
            Assert.That(first.ApplicationId, Is.Not.EqualTo(firstId));
            Assert.That(first.ContactTarget(firstId, Ms(150), targets[0]), Is.Null);
            Assert.That(first.ContactTarget(first.ApplicationId, pattern.ProjectileLifetime, targets[0]), Is.Null);
            Assert.That(first.Phase, Is.EqualTo(ProjectilePhase.Expired));
        }

        [Test]
        public void PendingShotsCancelDuringReturn()
        {
            Assert.That(AttackAt(100), Is.True);
            now = Ms(150);
            Assert.That(run.TryBeginExtraction(runId, isInRange: true), Is.Not.Null);
            Assert.That(LaunchAt(220, new ProjectileLifecycle()), Is.False);
            Assert.That(volley.IsFinished, Is.True);
        }

        [Test]
        public void PendingShotsCancelAtTheRunDeadline()
        {
            Assert.That(AttackAt(14999), Is.True);
            Assert.That(LaunchAt(15000, new ProjectileLifecycle()), Is.False);
            Assert.That(volley.IsFinished, Is.True);
        }

        [Test]
        public void OverlappingTargetUsesStationaryContactAndLateSlotsDoNotBackdateMovement()
        {
            targets = new[] { Candidate("overlap", 0, 0) };
            Assert.That(AttackAt(100), Is.True);
            Assert.That(first.Velocity.X, Is.Zero);
            Assert.That(first.Velocity.Y, Is.Zero);
            Assert.That(first.ContactTarget(first.ApplicationId, TimeSpan.Zero, targets[0]), Is.Not.Null);

            var later = new ProjectileLifecycle();
            Assert.That(LaunchAt(600, later), Is.True);
            Assert.That(later.Elapsed, Is.EqualTo(TimeSpan.Zero));
            Assert.That(LaunchAt(599, new ProjectileLifecycle()), Is.False);
            Assert.That(LaunchAt(600, new ProjectileLifecycle()), Is.True);
        }

        private bool BeginVolley(BasicAttackRequest request)
        {
            aimedAt = request.Target.TargetId;
            var projectile = new ProjectileLifecycle();
            if (!pattern.TryCreateVolley(request, AttackerId, out RepeaterBowVolley? created)
                || !created.TryLaunchNext(run, request.StartedAt, projectile))
                return false;

            volley = created;
            first = projectile;
            return true;
        }

        private bool AttackAt(int milliseconds)
        {
            now = Ms(milliseconds);
            return scheduler.TryAttack(now, Origin, bow, targets);
        }

        private bool LaunchAt(int milliseconds, ProjectileLifecycle projectile)
        {
            now = Ms(milliseconds);
            return volley.TryLaunchNext(run, now, projectile);
        }

        private static TargetCandidate Candidate(string id, float x, float y, bool isAlive = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + x, Origin.Y + y), isAlive, true);
        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
