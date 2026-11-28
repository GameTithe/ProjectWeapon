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
    public sealed class ShadowDaggerTests
    {
        private const string Folder = "Assets/ProjectWeapon/Weapons/PrototypeShadowDagger/";
        private const string AttackerId = "player";
        private static readonly WeaponDefinitionId DefinitionId = new WeaponDefinitionId("shadow_dagger");
        private static readonly WorldPoint2D Origin = new WorldPoint2D(2, 3);

        private RunState run = null!;
        private RunId runId;
        private TimeSpan now;
        private WeaponDefinition dagger = null!;
        private ShadowDaggerPattern pattern = null!;
        private AutoAttackScheduler scheduler = null!;
        private ShadowDaggerStrike pending = null!;
        private IReadOnlyList<TargetCandidate> targets = Array.Empty<TargetCandidate>();

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Folder + "PrototypeShadowDaggerWeaponCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            var lookup = catalog.CreateLookup();
            Assert.That(lookup.Keys, Is.EquivalentTo(new[] { DefinitionId }));
            dagger = lookup[DefinitionId];
            Assert.That(dagger.BasicAttack, Is.TypeOf<ShadowDaggerPattern>());
            pattern = (ShadowDaggerPattern)dagger.BasicAttack;
            now = TimeSpan.Zero;
            run = new RunState(() => now);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(15000)), Is.True);
            scheduler = new AutoAttackScheduler(run, Schedule, initiallyEnabled: true);
            targets = new[] { Candidate("aim", 1, 0) };
        }

        [Test]
        public void GradeThreeDefinitionLinksOneConfiguredShadowStrike()
        {
            Assert.That(dagger, Is.SameAs(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Folder + "ShadowDagger.asset")));
            Assert.That(dagger.DisplayName, Is.EqualTo("그림자 단검"));
            Assert.That(dagger.Grade, Is.EqualTo(3));
            Assert.That(dagger.Kind, Is.EqualTo(WeaponKind.Dagger));
            Assert.That(dagger.PrototypeBasicAttackPattern, Is.EqualTo("분신 타격"));
            Assert.That(dagger.Skill, Is.Null);
            Assert.That(dagger.AttackVfx, Is.Null);
            Assert.That(pattern, Is.SameAs(AssetDatabase.LoadAssetAtPath<ShadowDaggerPattern>(Folder + "ShadowDaggerStrike.asset")));
            Assert.That(pattern.Range, Is.EqualTo(2f));
            Assert.That(pattern.Delay, Is.EqualTo(Ms(230)));
            Assert.That(pattern.Damage, Is.EqualTo(15f));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(900)));
        }

        [Test]
        public void SchedulerLocksTheClosestTargetAndResolvesExactlyOnceFromTheCastOrigin()
        {
            targets = new[]
            {
                Candidate("farther", 1.5f, 0),
                Candidate("dead", 0.1f, 0, isAlive: false),
                Candidate("friendly", 0.2f, 0, isAttackable: false),
                Candidate("aim", 1, 0)
            };
            Assert.That(CastAt(100), Is.True);
            Assert.That(pending.TargetId, Is.EqualTo("aim"));
            Assert.That(pending.RunId, Is.EqualTo(runId));
            Assert.That(pending.Origin.X, Is.EqualTo(Origin.X));
            Assert.That(pending.Origin.Y, Is.EqualTo(Origin.Y));
            Assert.That(pending.HitsAt, Is.EqualTo(Ms(330)));

            TargetCandidate movedToBoundary = Candidate("aim", 2, 0);
            Assert.That(pending.TryResolve(run, Ms(100), movedToBoundary, out ShadowStrikeResolution early), Is.False);
            Assert.That(early.Hit, Is.Null);
            Assert.That(pending.TryResolve(run, Ms(329), movedToBoundary, out _), Is.False);
            Assert.That(pending.TryResolve(run, Ms(330), movedToBoundary, out ShadowStrikeResolution actual), Is.True);
            Assert.That(actual.EffectiveAt, Is.EqualTo(Ms(330)));
            Assert.That(actual.Hit!.Value.TargetId, Is.EqualTo("aim"));
            Assert.That(actual.Hit.Value.AttackerId, Is.EqualTo(AttackerId));
            Assert.That(actual.Hit.Value.Amount, Is.EqualTo(pattern.Damage));
            Assert.That(actual.Hit.Value.ApplicationId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(pending.TryResolve(run, Ms(330), movedToBoundary, out _), Is.False);
        }

        [TestCase("outside")]
        [TestCase("dead")]
        [TestCase("nonattackable")]
        [TestCase("absent")]
        [TestCase("other-id")]
        [TestCase("nonfinite")]
        public void MissingInvalidOrDifferentTargetConsumesOneStrikeWithoutDamage(string scenario)
        {
            Assert.That(CastAt(100), Is.True);
            TargetCandidate target = scenario switch
            {
                "outside" => Candidate("aim", 2.001f, 0),
                "dead" => Candidate("aim", 1, 0, isAlive: false),
                "nonattackable" => Candidate("aim", 1, 0, isAttackable: false),
                "absent" => default,
                "other-id" => Candidate("replacement", 1, 0),
                _ => Candidate("aim", float.PositiveInfinity, 0)
            };
            Assert.That(pending.TryResolve(run, Ms(330), target, out ShadowStrikeResolution miss), Is.True);
            Assert.That(miss.Hit, Is.Null);
            Assert.That(pending.IsFinished, Is.True);
            Assert.That(pending.TryResolve(run, Ms(331), targets[0], out _), Is.False);
        }

        [Test]
        public void FailedAcquisitionLeavesCooldownAvailableAndNewCastUsesFreshApplicationId()
        {
            targets = new[] { Candidate("outside", 2.001f, 0) };
            Assert.That(CastAt(100), Is.False);
            targets = new[] { Candidate("boundary", 2, 0) };
            Assert.That(CastAt(100), Is.True);
            Assert.That(pending.TryResolve(run, Ms(342), targets[0], out ShadowStrikeResolution first), Is.True);
            Assert.That(first.EffectiveAt, Is.EqualTo(Ms(342)));
            Assert.That(CastAt(999), Is.False);
            Assert.That(CastAt(1000), Is.True);
            Assert.That(pending.TryResolve(run, Ms(1230), targets[0], out ShadowStrikeResolution next), Is.True);
            Assert.That(next.Hit!.Value.ApplicationId, Is.Not.EqualTo(first.Hit!.Value.ApplicationId));
        }

        [Test]
        public void TerminalRunCannotReceiveDelayedDamage()
        {
            Assert.That(CastAt(100), Is.True);
            now = Ms(200);
            run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, now) });
            Assert.That(pending.TryResolve(run, Ms(330), targets[0], out _), Is.False);
            Assert.That(pending.IsFinished, Is.True);
        }

        [Test]
        public void PendingHitMayResolveBeforeExtractionFinishes()
        {
            Assert.That(CastAt(100), Is.True);
            now = Ms(150);
            Assert.That(run.TryBeginExtraction(runId, isInRange: true), Is.Not.Null);
            Assert.That(pending.TryResolve(run, Ms(330), targets[0], out ShadowStrikeResolution result), Is.True);
            Assert.That(result.Hit, Is.Not.Null);
        }

        [Test]
        public void DifferentActiveRunCannotReceiveDelayedDamage()
        {
            Assert.That(CastAt(100), Is.True);
            var otherRun = new RunState(() => TimeSpan.Zero);
            Assert.That(otherRun.TryStartRun(RunId.New(), 1, Ms(15000)), Is.True);
            Assert.That(pending.TryResolve(otherRun, Ms(330), targets[0], out _), Is.False);
            Assert.That(pending.IsFinished, Is.True);
        }

        [Test]
        public void ExactDeadlineCancelsThePendingStrikeBeforeExpiryIsProcessed()
        {
            Assert.That(CastAt(14770), Is.True);
            Assert.That(pending.HitsAt, Is.EqualTo(Ms(15000)));
            Assert.That(pending.TryResolve(run, Ms(15000), targets[0], out _), Is.False);
            Assert.That(pending.IsFinished, Is.True);
        }

        private bool Schedule(BasicAttackRequest request)
        {
            if (!pattern.TrySchedule(request, AttackerId, out ShadowDaggerStrike? started))
                return false;

            pending = started;
            return true;
        }

        private bool CastAt(int milliseconds)
        {
            now = Ms(milliseconds);
            return scheduler.TryAttack(now, Origin, dagger, targets);
        }

        private static TargetCandidate Candidate(string id, float x, float y, bool isAlive = true, bool isAttackable = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + x, Origin.Y + y), isAlive, isAttackable);
        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
