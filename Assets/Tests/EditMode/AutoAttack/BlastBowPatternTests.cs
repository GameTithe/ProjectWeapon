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
    public sealed class BlastBowPatternTests
    {
        private const string Folder = "Assets/ProjectWeapon/Weapons/PrototypeBlastBow/";
        private const string AttackerId = "player";
        private static readonly WeaponDefinitionId DefinitionId = new WeaponDefinitionId("blast_bow");
        private static readonly WorldPoint2D Origin = new WorldPoint2D(2, 4);

        private RunState run = null!;
        private RunId runId;
        private TimeSpan now;
        private WeaponDefinition bow = null!;
        private BlastBowPattern pattern = null!;
        private AutoAttackScheduler scheduler = null!;
        private BlastBowFlight flight = null!;
        private string? aimedAt;
        private int launches;
        private IReadOnlyList<TargetCandidate> targets = Array.Empty<TargetCandidate>();

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Folder + "PrototypeBlastBowWeaponCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            var lookup = catalog.CreateLookup();
            Assert.That(lookup.Keys, Is.EquivalentTo(new[] { DefinitionId }));
            bow = lookup[DefinitionId];
            Assert.That(bow.BasicAttack, Is.TypeOf<BlastBowPattern>());
            pattern = (BlastBowPattern)bow.BasicAttack;
            now = TimeSpan.Zero;
            launches = 0;
            aimedAt = null;
            run = new RunState(() => now);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(15000)), Is.True);
            scheduler = new AutoAttackScheduler(run, Launch, initiallyEnabled: true);
            targets = new[] { Candidate("aim", 3, 0) };
        }

        [Test]
        public void GradeThreeDefinitionLoadsOneConfiguredExplosiveArrow()
        {
            Assert.That(bow, Is.SameAs(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Folder + "BlastBow.asset")));
            Assert.That(bow.DisplayName, Is.EqualTo("폭발궁"));
            Assert.That(bow.Grade, Is.EqualTo(3));
            Assert.That(bow.Kind, Is.EqualTo(WeaponKind.Bow));
            Assert.That(bow.PrototypeBasicAttackPattern, Is.EqualTo("폭발 화살"));
            Assert.That(bow.Skill, Is.Null);
            Assert.That(bow.AttackVfx, Is.Null);
            Assert.That(pattern, Is.SameAs(AssetDatabase.LoadAssetAtPath<BlastBowPattern>(Folder + "BlastBowArrow.asset")));
            Assert.That(pattern.Range, Is.EqualTo(6f));
            Assert.That(pattern.Speed, Is.EqualTo(12f));
            Assert.That(pattern.BlastRadius, Is.EqualTo(1.25f));
            Assert.That(pattern.BlastDamage, Is.EqualTo(20f));
            Assert.That(pattern.Lifetime, Is.EqualTo(Ms(650)));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(1250)));
        }

        [Test]
        public void FirstTargetContactCreatesOneCircularBatchAtTheReportedImpact()
        {
            TargetCandidate aim = Candidate("aim", 3, 0);
            targets = new[] { Candidate("dead-near", 1, 0, isAlive: false), Candidate("far", 5, 0), aim };
            Assert.That(AttackAt(100), Is.True);
            Assert.That(aimedAt, Is.EqualTo("aim"));
            Assert.That(launches, Is.EqualTo(1));
            Assert.That(flight.StartedAt, Is.EqualTo(Ms(100)));
            Assert.That(flight.Phase, Is.EqualTo(ProjectilePhase.Active));
            Assert.That(flight.Advance(flight.ApplicationId, Ms(325)), Is.True);
            Assert.That(flight.Position.X, Is.EqualTo(4.7f).Within(0.0001f));

            var impact = new WorldPoint2D(4.75f, 4.5f);
            TargetCandidate nearbyBoundary = Candidate("radius-boundary", 2.75f, 1.75f);
            var candidates = new[]
            {
                aim, aim, nearbyBoundary,
                Candidate("past-radius", 2.75f, 1.751f),
                Candidate("dead", 2.75f, 0.5f, isAlive: false),
                Candidate("friendly", 2.75f, 0.5f, isAttackable: false),
                Candidate(AttackerId, 2.75f, 0.5f)
            };

            BlastBowExplosion? result = flight.ContactTarget(flight.ApplicationId, Ms(325), impact, aim, candidates);
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.RunId, Is.EqualTo(runId));
            Assert.That(result.EffectiveAt, Is.EqualTo(Ms(325)));
            Assert.That(result.Center.X, Is.EqualTo(impact.X));
            Assert.That(result.Center.Y, Is.EqualTo(impact.Y));
            Assert.That(result.Radius, Is.EqualTo(pattern.BlastRadius));
            Assert.That(result.Hits.Select(hit => hit.TargetId), Is.EquivalentTo(new[] { "aim", "radius-boundary" }));
            Assert.That(result.Hits.All(hit => hit.Amount == pattern.BlastDamage
                && hit.ApplicationId == flight.ApplicationId && hit.AttackerId == AttackerId), Is.True);
            Assert.That(flight.Phase, Is.EqualTo(ProjectilePhase.HitTarget));
            Assert.That(flight.ContactTarget(flight.ApplicationId, Ms(325), impact, aim, candidates), Is.Null);
            Assert.That(flight.ContactWorld(flight.ApplicationId, Ms(325), impact, candidates), Is.Null);
        }

        [Test]
        public void SolidContactExplodesOnceAndHitsNearbyTargetsWithoutATargetContact()
        {
            Assert.That(AttackAt(100), Is.True);
            var impact = new WorldPoint2D(3, 4);
            TargetCandidate nearWall = Candidate("near-wall", 1, 0.5f);

            BlastBowExplosion? result = flight.ContactWorld(flight.ApplicationId, Ms(180), impact, new[] { nearWall });
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.EffectiveAt, Is.EqualTo(Ms(180)));
            Assert.That(result.Hits.Single().TargetId, Is.EqualTo("near-wall"));
            Assert.That(flight.Phase, Is.EqualTo(ProjectilePhase.Blocked));
            Assert.That(flight.ContactWorld(flight.ApplicationId, Ms(180), impact, targets), Is.Null);
            Assert.That(flight.ContactTarget(flight.ApplicationId, Ms(180), impact, targets[0], targets), Is.Null);
        }

        [Test]
        public void InvalidContactsDoNotExplodeOrConsumeTheFirstValidImpact()
        {
            Assert.That(AttackAt(100), Is.True);
            var impact = new WorldPoint2D(5, 4);
            Assert.That(flight.ContactWorld(Guid.NewGuid(), Ms(250), impact, targets), Is.Null);
            Assert.That(flight.ContactWorld(flight.ApplicationId, Ms(250), new WorldPoint2D(float.NaN, 4), targets), Is.Null);
            Assert.That(flight.ContactTarget(flight.ApplicationId, Ms(250), impact,
                Candidate("dead", 3, 0, isAlive: false), targets), Is.Null);
            Assert.That(flight.ContactTarget(flight.ApplicationId, Ms(250), impact, Candidate(AttackerId, 3, 0), targets),
                Is.Null);
            Assert.That(flight.Phase, Is.EqualTo(ProjectilePhase.Active));

            BlastBowExplosion? explosion = flight.ContactTarget(flight.ApplicationId, Ms(350), impact,
                targets[0], Array.Empty<TargetCandidate>());
            Assert.That(explosion!.Hits.Single().TargetId, Is.EqualTo(targets[0].TargetId));
        }

        [Test]
        public void LifetimeBoundaryAndCancelledRunHaveNoExplosion()
        {
            Assert.That(AttackAt(100), Is.True);
            Assert.That(flight.ContactWorld(flight.ApplicationId, Ms(750), Origin, targets), Is.Null);
            Assert.That(flight.Phase, Is.EqualTo(ProjectilePhase.Expired));
            Assert.That(flight.ContactTarget(flight.ApplicationId, Ms(751), Origin, targets[0], targets), Is.Null);

            Assert.That(AttackAt(1350), Is.True);
            now = Ms(1410);
            run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, now) });
            Assert.That(flight.ContactWorld(flight.ApplicationId, now, Origin, targets), Is.Null);
            Assert.That(flight.Phase, Is.EqualTo(ProjectilePhase.Inactive));
        }

        [Test]
        public void ExactRunDeadlinePreventsAStillLiveArrowFromExploding()
        {
            Assert.That(AttackAt(14700), Is.True);
            Assert.That(flight.ContactWorld(flight.ApplicationId, Ms(15000), Origin, targets), Is.Null);
            Assert.That(flight.Phase, Is.EqualTo(ProjectilePhase.Inactive));
        }

        [Test]
        public void ExplicitCancellationDiscardsPendingCollision()
        {
            Assert.That(AttackAt(100), Is.True);
            flight.Cancel();
            Assert.That(flight.ContactWorld(flight.ApplicationId, Ms(200), Origin, targets), Is.Null);
            Assert.That(flight.Phase, Is.EqualTo(ProjectilePhase.Inactive));
        }

        [Test]
        public void RangeAndCooldownGateOneArrowPerActivation()
        {
            targets = new[] { Candidate("outside", 6.001f, 0) };
            Assert.That(AttackAt(100), Is.False);
            Assert.That(launches, Is.Zero);
            targets = new[] { Candidate("boundary", 6, 0) };
            Assert.That(AttackAt(100), Is.True);
            Guid first = flight.ApplicationId;
            Assert.That(AttackAt(100), Is.False);
            Assert.That(flight.Advance(first, Ms(600)), Is.True);
            Assert.That(flight.Position.X, Is.EqualTo(targets[0].Position.X));
            Assert.That(AttackAt(1349), Is.False);
            Assert.That(AttackAt(1350), Is.True);
            Assert.That(launches, Is.EqualTo(2));
            Assert.That(flight.ApplicationId, Is.Not.EqualTo(first));
        }

        private bool Launch(BasicAttackRequest request)
        {
            if (!pattern.TryLaunch(request, run, AttackerId, out BlastBowFlight? launched))
                return false;

            flight = launched;
            aimedAt = request.Target.TargetId;
            launches++;
            return true;
        }

        private bool AttackAt(int milliseconds)
        {
            now = Ms(milliseconds);
            return scheduler.TryAttack(now, Origin, bow, targets);
        }

        private static TargetCandidate Candidate(string id, float x, float y, bool isAlive = true, bool isAttackable = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + x, Origin.Y + y), isAlive, isAttackable);
        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
