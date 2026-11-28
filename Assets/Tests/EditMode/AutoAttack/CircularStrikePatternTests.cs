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
    public sealed class CircularStrikePatternTests
    {
        private const string StarterPath = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private const string Attacker = "local-player";
        private static readonly WeaponDefinitionId MaceId = new WeaponDefinitionId("mace");
        private static readonly RunId RunId = new RunId(new Guid("bb861fc6-b853-4b84-bd80-763407fa13b4"));
        private static readonly WorldPoint2D Origin = new WorldPoint2D(3, -2);

        private readonly List<BasicAttackRequest> attempts = new List<BasicAttackRequest>();
        private readonly List<CircularStrike> strikes = new List<CircularStrike>();
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> weapons = null!;
        private IReadOnlyList<TargetCandidate> targets = Array.Empty<TargetCandidate>();
        private RunState run = null!;
        private AutoAttackScheduler scheduler = null!;
        private TimeSpan now;
        private Func<Guid> nextApplicationId = null!;

        [SetUp]
        public void SetUp()
        {
            attempts.Clear();
            strikes.Clear();
            targets = Array.Empty<TargetCandidate>();
            now = TimeSpan.Zero;
            nextApplicationId = Guid.NewGuid;
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(StarterPath + "PrototypeStarterWeaponCatalog.asset");
            weapons = catalog.CreateLookup();
            run = new RunState(() => now);
            Assert.That(run.TryStartRun(RunId, 1, Ms(20000)), Is.True);
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [Test]
        public void StarterCatalogLinksTheCircularStrikeOnlyToGradeOneMace()
        {
            var asset = AssetDatabase.LoadAssetAtPath<CircularStrikePattern>(StarterPath + "MaceCircularStrike.asset");
            WeaponDefinition mace = weapons[MaceId];

            Assert.That(asset, Is.Not.Null);
            Assert.That(mace.Grade, Is.EqualTo(1));
            Assert.That(mace.Kind, Is.EqualTo(WeaponKind.Mace));
            Assert.That(mace.BasicAttack, Is.SameAs(asset));
            Assert.That(weapons.Values.Count(weapon => weapon.BasicAttack is CircularStrikePattern), Is.EqualTo(1));
            Assert.That(asset.Range, Is.EqualTo(1.5f));
            Assert.That(asset.Cooldown, Is.EqualTo(Ms(1200)));
            Assert.That(asset.Damage, Is.EqualTo(12f));
        }

        [Test]
        public void ValidTargetStartsOneImmediateStrikeThatHitsAllDirectionsFromTheAttacker()
        {
            TargetCandidate nearby = Candidate("nearby", 0.5f, 0);
            targets = new[]
            {
                nearby,
                nearby,
                Candidate("side", 0, 1),
                Candidate("rear-boundary", -1.5f, 0),
                Candidate("outside", 1.51f, 0),
                Candidate("dead", 0, -0.1f, alive: false),
                Candidate("unattackable", 0, 0.1f, attackable: false),
                Candidate(Attacker, 1f, 0),
            };

            Assert.That(AttackAt(150), Is.True);
            Assert.That(attempts.Single().Target.TargetId, Is.EqualTo("nearby"));
            CircularStrike strike = strikes.Single();
            Assert.That(strike.RunId, Is.EqualTo(RunId));
            Assert.That(strike.ApplicationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(strike.EffectiveAt, Is.EqualTo(Ms(150)));
            Assert.That(strike.Hits.Select(hit => hit.TargetId),
                Is.EquivalentTo(new[] { "nearby", "side", "rear-boundary" }));
            Assert.That(strike.Hits.All(hit => hit.ApplicationId == strike.ApplicationId
                && hit.AttackerId == Attacker && hit.Amount == 12f), Is.True);
        }

        [Test]
        public void OverlappingSelectedTargetDoesNotShrinkTheCircle()
        {
            targets = new[] { Candidate("overlap", 0, 0), Candidate("edge", 0, -1.5f) };

            Assert.That(AttackAt(0), Is.True);
            Assert.That(attempts.Single().Target.TargetId, Is.EqualTo("overlap"));
            Assert.That(strikes.Single().Hits.Select(hit => hit.TargetId),
                Is.EquivalentTo(new[] { "overlap", "edge" }));
        }

        [Test]
        public void OneStrikePerCooldownUsesADifferentIdAtTheBoundary()
        {
            targets = new[] { Candidate("edge", 1.5f, 0) };

            Assert.That(AttackAt(100), Is.True);
            Assert.That(AttackAt(100), Is.False);
            Assert.That(AttackAt(1299), Is.False);
            Assert.That(AttackAt(1300), Is.True);

            Assert.That(strikes.Select(strike => strike.EffectiveAt), Is.EqualTo(new[] { Ms(100), Ms(1300) }));
            Assert.That(strikes[0].ApplicationId, Is.Not.EqualTo(strikes[1].ApplicationId));
        }

        [Test]
        public void MissingTargetAndRejectedStrikeDoNotConsumeCooldown()
        {
            targets = new[] { Candidate("outside", 1.51f, 0), Candidate("dead", 1f, 0, alive: false) };
            Assert.That(AttackAt(100), Is.False);
            Assert.That(attempts, Is.Empty);

            targets = new[] { Candidate("edge", 1.5f, 0) };
            nextApplicationId = () => Guid.Empty;
            Assert.That(AttackAt(100), Is.False);
            Assert.That(strikes, Is.Empty);

            nextApplicationId = Guid.NewGuid;
            Assert.That(AttackAt(100), Is.True);
            Assert.That(strikes, Has.Count.EqualTo(1));
        }

        [Test]
        public void ExtractionPreventsTheSchedulerFromStartingAStrike()
        {
            targets = new[] { Candidate("nearby", 0, 1f) };
            now = Ms(100);
            Assert.That(run.TryBeginExtraction(RunId, isInRange: true), Is.Not.Null);

            Assert.That(AttackAt(100), Is.False);
            Assert.That(attempts, Is.Empty);
            Assert.That(strikes, Is.Empty);
        }

        private bool Execute(BasicAttackRequest request)
        {
            attempts.Add(request);
            if (request.Pattern is not CircularStrikePattern pattern
                || !pattern.TryCreateStrike(request, nextApplicationId(), Attacker, targets, out CircularStrike? strike))
                return false;

            strikes.Add(strike!);
            return true;
        }

        private bool AttackAt(int milliseconds)
        {
            now = Ms(milliseconds);
            return scheduler.TryAttack(now, Origin, weapons[MaceId], targets);
        }

        private static TargetCandidate Candidate(
            string id, float offsetX, float offsetY, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + offsetX, Origin.Y + offsetY), alive, attackable);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
