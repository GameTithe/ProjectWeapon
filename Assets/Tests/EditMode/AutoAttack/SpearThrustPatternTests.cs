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
    public sealed class SpearThrustPatternTests
    {
        private const string StarterPath = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private const string Attacker = "local-player";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(2, 4);
        private static readonly RunId TestRun = new RunId(new Guid("ee2bf289-c1a5-40a4-93d0-9bc16d1616f1"));
        private readonly List<SpearThrust> thrusts = new List<SpearThrust>();
        private AutoAttackScheduler scheduler = null!;
        private WeaponDefinition spear = null!;
        private Guid applicationId;

        [SetUp]
        public void SetUp()
        {
            thrusts.Clear();
            applicationId = Guid.NewGuid();
            var run = new RunState(() => TimeSpan.Zero);
            Assert.That(run.TryStartRun(TestRun, 1, Ms(20000)), Is.True);
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(StarterPath + "PrototypeStarterWeaponCatalog.asset");
            spear = catalog.CreateLookup()[new WeaponDefinitionId("wooden_spear")];
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [Test]
        public void CatalogLinksGradeOneWoodenSpearToItsThrustAsset()
        {
            var pattern = AssetDatabase.LoadAssetAtPath<SpearThrustPattern>(StarterPath + "WoodenSpearThrust.asset");

            Assert.That(pattern, Is.Not.Null);
            Assert.That(spear.Grade, Is.EqualTo(1));
            Assert.That(spear.Kind, Is.EqualTo(WeaponKind.Spear));
            Assert.That(spear.BasicAttack, Is.SameAs(pattern));
            Assert.That(pattern.Range, Is.EqualTo(2f));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(950)));
            Assert.That(pattern.Damage, Is.EqualTo(10f));
        }

        [Test]
        public void ThrustHitsOnlyTheNearestEligibleTargetOnItsLineAtStart()
        {
            TargetCandidate nearest = Candidate("nearest", 0.5f, 0.5f);
            Assert.That(AttackAt(120, Candidate("same-line", 1, 1), Candidate("side", 0, 1),
                Candidate("dead", 0, 0, alive: false), nearest, nearest), Is.True);

            Assert.That(thrusts, Has.Count.EqualTo(1));
            SpearThrust thrust = thrusts[0];
            Assert.That(thrust.RunId, Is.EqualTo(TestRun));
            Assert.That(thrust.Origin, Is.EqualTo(Origin));
            Assert.That(thrust.Tip, Is.EqualTo(nearest.Position));
            Assert.That(thrust.EffectiveAt, Is.EqualTo(Ms(120)));
            Assert.That(thrust.Hit.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(thrust.Hit.AttackerId, Is.EqualTo(Attacker));
            Assert.That(thrust.Hit.TargetId, Is.EqualTo("nearest"));
            Assert.That(thrust.Hit.Amount, Is.EqualTo(10f));
        }

        [Test]
        public void RangeAndCooldownBoundariesAreInclusive()
        {
            TargetCandidate boundary = Candidate("boundary", 2, 0);
            Assert.That(AttackAt(100, Candidate("out-of-range", 2.01f, 0)), Is.False);
            Assert.That(AttackAt(100, boundary), Is.True);
            Assert.That(AttackAt(100, boundary), Is.False);
            Assert.That(AttackAt(1049, boundary), Is.False);
            Assert.That(AttackAt(1050, boundary), Is.True);
            Assert.That(thrusts, Has.Count.EqualTo(2));
        }

        [Test]
        public void BadApplicationAndSelfTargetDoNotConsumeCooldown()
        {
            applicationId = Guid.Empty;
            Assert.That(AttackAt(100, Candidate("opponent", 1, 0)), Is.False);
            applicationId = Guid.NewGuid();
            Assert.That(AttackAt(100, Candidate(Attacker, 0, 0)), Is.False);
            Assert.That(thrusts, Is.Empty);
            Assert.That(AttackAt(100, Candidate("opponent", 1, 0)), Is.True);
        }

        private bool Execute(BasicAttackRequest attack)
        {
            if (attack.Pattern is not SpearThrustPattern pattern
                || !pattern.TryCreateThrust(attack, applicationId, Attacker, out SpearThrust thrust))
                return false;

            thrusts.Add(thrust);
            return true;
        }

        private bool AttackAt(int milliseconds, params TargetCandidate[] candidates)
            => scheduler.TryAttack(Ms(milliseconds), Origin, spear, candidates);

        private static TargetCandidate Candidate(string id, float offsetX, float offsetY, bool alive = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + offsetX, Origin.Y + offsetY), alive, true);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
