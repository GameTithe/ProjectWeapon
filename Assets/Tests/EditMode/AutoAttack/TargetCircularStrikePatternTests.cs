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
    public sealed class TargetCircularStrikePatternTests
    {
        private const string AssetPath = "Assets/ProjectWeapon/Weapons/PrototypeExpansion/";
        private const string Attacker = "local-player";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(4, -3);
        private readonly List<CircularStrike> strikes = new List<CircularStrike>();
        private readonly List<BasicAttackRequest> attempts = new List<BasicAttackRequest>();
        private RunId id;
        private RunState run = null!;
        private AutoAttackScheduler scheduler = null!;
        private WeaponDefinition axe = null!;
        private IReadOnlyList<TargetCandidate> targets = Array.Empty<TargetCandidate>();
        private IReadOnlyList<TargetCandidate>? impactTargets;
        private TimeSpan now;
        private Guid applicationOverride;

        [SetUp]
        public void Prepare()
        {
            strikes.Clear();
            attempts.Clear();
            targets = Array.Empty<TargetCandidate>();
            impactTargets = null;
            applicationOverride = Guid.NewGuid();
            now = TimeSpan.Zero;
            id = RunId.New();
            run = new RunState(() => now);
            Assert.That(run.TryStartRun(id, 1, Ms(15000)), Is.True);
            axe = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetPath + "LavaAxe.asset");
            Assert.That(axe, Is.Not.Null);
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [Test]
        public void GradeThreeLavaAxeHasOneStandaloneAttackWithDraftTuning()
        {
            var pattern = AssetDatabase.LoadAssetAtPath<TargetCircularStrikePattern>(AssetPath + "LavaAxeFireArea.asset");
            Assert.That(axe.Id.Value, Is.EqualTo("lava_axe"));
            Assert.That(axe.Grade, Is.EqualTo(3));
            Assert.That(axe.Kind, Is.EqualTo(WeaponKind.Axe));
            Assert.That(axe.PrototypeBasicAttackPattern, Is.EqualTo("화염 범위 타격"));
            Assert.That(axe.BasicAttack, Is.SameAs(pattern));
            Assert.That(axe.Skill, Is.Null);
            Assert.That(axe.AttackVfx, Is.Null);
            Assert.That(WeaponDefinitionValidator.ValidateCatalogMetadata(new[] { axe }), Is.Empty);
            Assert.That(pattern.Range, Is.EqualTo(2.5f));
            Assert.That(pattern.Radius, Is.EqualTo(1.25f));
            Assert.That(pattern.Damage, Is.EqualTo(18f));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(1450)));
        }

        [Test]
        public void NearestTargetAtCastLimitCentersOneImmediateAreaWhoseEdgeCanExtendPastCastRange()
        {
            var nearest = At("chosen", 2.5f, 0);
            targets = new[] { At("outside-cast", 3, 0), nearest, At("dead-candidate", 1, 0, alive: false) };
            impactTargets = new[]
            {
                nearest, nearest,
                At("area-edge", 3.75f, 0),
                At("side-edge", 2.5f, 1.25f),
                At("just-outside-area", 3.76f, 0),
                At("near-the-caster", .1f, 0),
                At("dead", 2.5f, 0, alive: false),
                At("untargetable", 2.5f, 0, attackable: false),
                At(Attacker, 2.5f, 0)
            };

            Assert.That(AttackAt(150), Is.True);
            Assert.That(attempts.Single().Target.TargetId, Is.EqualTo("chosen"));
            CircularStrike strike = strikes.Single();
            Assert.That(strike.RunId, Is.EqualTo(id));
            Assert.That(strike.EffectiveAt, Is.EqualTo(Ms(150)));
            Assert.That(strike.Hits.Select(hit => hit.TargetId), Is.EquivalentTo(new[] { "chosen", "area-edge", "side-edge" }));
            Assert.That(strike.Hits.All(hit => hit.ApplicationId == strike.ApplicationId && hit.AttackerId == Attacker
                && hit.Amount == 18f), Is.True);
        }

        [Test]
        public void RepeatedTickAndCooldownAreGatedUntilTheExactBoundary()
        {
            targets = new[] { At("chosen", 1, 0) };
            Assert.That(AttackAt(100), Is.True);
            Assert.That(AttackAt(100), Is.False);
            Assert.That(AttackAt(1549), Is.False);
            applicationOverride = Guid.NewGuid();
            Assert.That(AttackAt(1550), Is.True);
            Assert.That(strikes.Select(strike => strike.EffectiveAt), Is.EqualTo(new[] { Ms(100), Ms(1550) }));
            Assert.That(strikes.Select(strike => strike.ApplicationId).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void OutOfRangeInvalidApplicationOrVanishedTargetDoesNotConsumeCooldown()
        {
            targets = new[] { At("outside", 2.51f, 0) };
            Assert.That(AttackAt(50), Is.False);
            Assert.That(attempts, Is.Empty);

            var chosen = At("chosen", 2.5f, 0);
            targets = new[] { chosen };
            Guid accepted = applicationOverride;
            applicationOverride = Guid.Empty;
            Assert.That(AttackAt(100), Is.False);

            applicationOverride = accepted;
            impactTargets = new[] { At("bystander", 2.8f, 0), At("chosen", 2.5f, 0, alive: false) };
            Assert.That(AttackAt(100), Is.False);
            Assert.That(strikes, Is.Empty);

            impactTargets = new[] { chosen };
            Assert.That(AttackAt(100), Is.True);
            Assert.That(strikes, Has.Count.EqualTo(1));
        }

        [Test]
        public void AuthoritativeExtractionPreventsTheCast()
        {
            targets = new[] { At("in-range", 1, 0) };
            Assert.That(run.TryBeginExtraction(id, true), Is.Not.Null);
            Assert.That(AttackAt(0), Is.False);
            Assert.That(attempts, Is.Empty);
        }

        private bool Execute(BasicAttackRequest request)
        {
            attempts.Add(request);
            if (request.Pattern is not TargetCircularStrikePattern pattern
                || !pattern.TryCreateStrike(request, applicationOverride, Attacker, impactTargets ?? targets, out CircularStrike? strike))
                return false;
            strikes.Add(strike!);
            return true;
        }

        private bool AttackAt(int milliseconds)
        {
            now = Ms(milliseconds);
            return scheduler.TryAttack(now, Origin, axe, targets);
        }

        private static TargetCandidate At(string target, float x, float y, bool alive = true, bool attackable = true)
            => new TargetCandidate(target, new WorldPoint2D(Origin.X + x, Origin.Y + y), alive, attackable);

        private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
    }
}
