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
    public sealed class DaggerFlurryPatternTests
    {
        private const string StarterPath = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private const string Attacker = "local-player";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(0, 0);
        private static readonly RunId RunId = new RunId(new Guid("7f1d40eb-1f81-4aac-8d77-4d8093173961"));
        private static readonly WeaponDefinitionId DaggerId = new WeaponDefinitionId("dagger");

        private readonly List<DaggerFlurry> flurries = new List<DaggerFlurry>();
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> weapons = null!;
        private RunState run = null!;
        private AutoAttackScheduler scheduler = null!;
        private TimeSpan now;

        [SetUp]
        public void SetUp()
        {
            flurries.Clear();
            now = TimeSpan.Zero;
            run = new RunState(() => now);
            Assert.That(run.TryStartRun(RunId, 1, Ms(20000)), Is.True);
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(StarterPath + "PrototypeStarterWeaponCatalog.asset");
            weapons = catalog.CreateLookup();
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [Test]
        public void StarterCatalogLinksThePatternOnlyToGradeOneDagger()
        {
            var asset = AssetDatabase.LoadAssetAtPath<DaggerFlurryPattern>(StarterPath + "DaggerFlurry.asset");
            WeaponDefinition dagger = weapons[DaggerId];

            Assert.That(asset, Is.Not.Null);
            Assert.That(dagger.Grade, Is.EqualTo(1));
            Assert.That(dagger.Kind, Is.EqualTo(WeaponKind.Dagger));
            Assert.That(dagger.BasicAttack, Is.SameAs(asset));
            Assert.That(weapons.Values.Count(weapon => weapon.BasicAttack is DaggerFlurryPattern), Is.EqualTo(1));
            Assert.That(asset.Range, Is.EqualTo(1.15f));
            Assert.That(asset.Cooldown, Is.EqualTo(Ms(650)));
            Assert.That(asset.StrikeCount, Is.EqualTo(2));
            Assert.That(asset.StrikeInterval, Is.EqualTo(Ms(120)));
            Assert.That(asset.StrikeDamage, Is.EqualTo(5f));
        }

        [Test]
        public void OneActivationLocksTheNearestTargetAndOnlyTwoScheduledHitsResolve()
        {
            TargetCandidate nearest = Candidate("nearest", 0.5f);
            Assert.That(AttackAt(100, Candidate("farther", 1f), nearest), Is.True);
            DaggerFlurry flurry = flurries.Single();
            Assert.That(flurry.RunId, Is.EqualTo(RunId));
            Assert.That(flurry.TargetId, Is.EqualTo("nearest"));
            Assert.That(flurry.NextHitAt, Is.EqualTo(Ms(100)));
            Assert.That(flurry.TryResolveNext(run, Ms(99), Origin, nearest, out _), Is.False);
            Assert.That(flurry.TryResolveNext(run, Ms(100), Origin, nearest, out DaggerStrike first), Is.True);
            Assert.That(flurry.TryResolveNext(run, Ms(100), Origin, nearest, out _), Is.False);
            Assert.That(flurry.TryResolveNext(run, Ms(219), Origin, nearest, out _), Is.False);
            Assert.That(flurry.NextHitAt, Is.EqualTo(Ms(220)));
            Assert.That(flurry.TryResolveNext(run, Ms(220), Origin, nearest, out DaggerStrike second), Is.True);
            Assert.That(flurry.TryResolveNext(run, Ms(221), Origin, nearest, out _), Is.False);
            Assert.That(flurry.IsFinished, Is.True);
            Assert.That(flurry.NextHitAt, Is.Null);

            Assert.That(first.Index, Is.Zero);
            Assert.That(second.Index, Is.EqualTo(1));
            Assert.That(first.EffectiveAt, Is.EqualTo(Ms(100)));
            Assert.That(second.EffectiveAt, Is.EqualTo(Ms(220)));
            Assert.That(first.Hit!.Value.TargetId, Is.EqualTo("nearest"));
            Assert.That(first.Hit.Value.AttackerId, Is.EqualTo(Attacker));
            Assert.That(second.Hit!.Value.TargetId, Is.EqualTo("nearest"));
            Assert.That(first.Hit.Value.Amount + second.Hit.Value.Amount, Is.EqualTo(10f));
            Assert.That(first.Hit.Value.ApplicationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(second.Hit.Value.ApplicationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(first.Hit.Value.ApplicationId, Is.Not.EqualTo(second.Hit.Value.ApplicationId));
        }

        [TestCase("nearest", 1.151f, true)]
        [TestCase("nearest", 0.5f, false)]
        [TestCase("different", 0.5f, true)]
        public void TheFollowUpMissesWhenTheLockedTargetIsOutOfRangeDeadOrReplaced(string id, float x, bool alive)
        {
            TargetCandidate nearest = Candidate("nearest", 0.5f);
            Assert.That(AttackAt(100, nearest), Is.True);
            DaggerFlurry flurry = flurries.Single();
            Assert.That(flurry.TryResolveNext(run, Ms(100), Origin, nearest, out _), Is.True);

            Assert.That(flurry.TryResolveNext(run, Ms(220), Origin, Candidate(id, x, alive), out DaggerStrike second), Is.True);
            Assert.That(second.Hit, Is.Null);
            Assert.That(flurry.TryResolveNext(run, Ms(221), Origin, nearest, out _), Is.False);
        }

        [Test]
        public void SchedulerRejectsOutOfRangeAndOnlyReactivatesAtTheCooldownBoundary()
        {
            TargetCandidate edge = Candidate("edge", 1.15f);
            Assert.That(AttackAt(100, Candidate("outside", 1.151f)), Is.False);
            Assert.That(AttackAt(100, edge), Is.True);
            Assert.That(AttackAt(100, edge), Is.False);
            Assert.That(flurries.Single().TryResolveNext(run, Ms(100), Origin, edge, out DaggerStrike hit), Is.True);
            Assert.That(hit.Hit?.TargetId, Is.EqualTo("edge"));
            Assert.That(AttackAt(749, edge), Is.False);
            Assert.That(AttackAt(750, edge), Is.True);
            Assert.That(flurries.Count, Is.EqualTo(2));
        }

        [Test]
        public void LateFramePreservesScheduledTimesAndUsesTheCurrentDamageClock()
        {
            TargetCandidate nearest = Candidate("nearest", 0.5f);
            Assert.That(AttackAt(100, nearest), Is.True);
            DaggerFlurry flurry = flurries.Single();

            Assert.That(flurry.TryResolveNext(run, Ms(250), Origin, nearest, out DaggerStrike first), Is.True);
            Assert.That(flurry.TryResolveNext(run, Ms(249), Origin, nearest, out _), Is.False);
            Assert.That(flurry.TryResolveNext(run, Ms(250), Origin, nearest, out DaggerStrike second), Is.True);
            Assert.That(flurry.TryResolveNext(run, Ms(250), Origin, nearest, out _), Is.False);
            Assert.That(first.ScheduledAt, Is.EqualTo(Ms(100)));
            Assert.That(second.ScheduledAt, Is.EqualTo(Ms(220)));
            Assert.That(first.EffectiveAt, Is.EqualTo(Ms(250)));
            Assert.That(second.EffectiveAt, Is.EqualTo(Ms(250)));
        }

        [Test]
        public void ActiveExtractionCancelsTheRemainingStrikeBeforeItBecomesDue()
        {
            TargetCandidate nearest = Candidate("nearest", 0.5f);
            Assert.That(AttackAt(100, nearest), Is.True);
            DaggerFlurry flurry = flurries.Single();
            Assert.That(flurry.TryResolveNext(run, Ms(100), Origin, nearest, out _), Is.True);

            now = Ms(150);
            Assert.That(run.TryBeginExtraction(RunId, isInRange: true), Is.Not.Null);
            Assert.That(flurry.TryResolveNext(run, now, Origin, nearest, out _), Is.False);
            Assert.That(flurry.IsFinished, Is.True);
        }

        [Test]
        public void RunDeadlineSuppressesPendingDamageAtZeroRemainingTime()
        {
            TargetCandidate nearest = Candidate("nearest", 0.5f);
            Assert.That(AttackAt(19900, nearest), Is.True);
            DaggerFlurry flurry = flurries.Single();
            Assert.That(flurry.TryResolveNext(run, Ms(19900), Origin, nearest, out _), Is.True);

            Assert.That(flurry.TryResolveNext(run, Ms(20000), Origin, nearest, out _), Is.False);
            Assert.That(flurry.IsFinished, Is.True);
        }

        private bool Execute(BasicAttackRequest request)
        {
            if (request.Pattern is not DaggerFlurryPattern pattern
                || !pattern.TryCreateFlurry(request, Attacker, out DaggerFlurry? flurry))
                return false;

            flurries.Add(flurry!);
            return true;
        }

        private bool AttackAt(int milliseconds, params TargetCandidate[] candidates)
        {
            now = Ms(milliseconds);
            return scheduler.TryAttack(now, Origin, weapons[DaggerId], candidates);
        }

        private static TargetCandidate Candidate(string id, float x, bool alive = true)
            => new TargetCandidate(id, new WorldPoint2D(x, 0), alive, isAttackable: true);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
