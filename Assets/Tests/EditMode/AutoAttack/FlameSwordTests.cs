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
    public sealed class FlameSwordTests
    {
        private const string AssetPath = "Assets/ProjectWeapon/Weapons/PrototypeFlameSword/FlameSword.asset";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(0, 0);

        [Test]
        public void OneImmediateFireSwingUsesTheNearestCandidateAndTheActualCone()
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetPath);
            Assert.That(weapon, Is.Not.Null);
            Assert.That(weapon.Id.Value, Is.EqualTo("flame_sword"));
            Assert.That(weapon.Grade, Is.EqualTo(2));
            Assert.That(weapon.PrototypeBasicAttackPattern, Is.EqualTo("화염 베기"));
            Assert.That(weapon.Skill, Is.Null);
            var pattern = (MeleeSlashPattern)weapon.BasicAttack;
            var candidates = new[]
            {
                new TargetCandidate("dead", new WorldPoint2D(0.1f, 0), false, true),
                Target("behind", -1.5f, 0),
                Target("edge", pattern.Range, 0),
                Target("in-cone", 1.1f, 0.3f),
                Target("in-cone", 1.1f, 0.3f),
                Target("side", 0.3f, 1.4f),
                Target("out-of-range", pattern.Range + 0.01f, 0),
                Target("aim", 1, 0)
            };
            TimeSpan now = TimeSpan.Zero;
            var run = new RunState(() => now);
            var swings = new List<MeleeSlashSwing>();
            Assert.That(run.TryStartRun(RunId.New(), 1, TimeSpan.FromSeconds(15)), Is.True);
            var scheduler = new AutoAttackScheduler(run, request =>
            {
                Assert.That(request.Target.TargetId, Is.EqualTo("aim"));
                Assert.That(pattern.TryCreateSwing(request, Guid.NewGuid(), "player", candidates, out var swing), Is.True);
                swings.Add(swing!);
                return true;
            }, initiallyEnabled: true);

            now = TimeSpan.FromMilliseconds(100);
            Assert.That(scheduler.TryAttack(now, Origin, weapon, candidates), Is.True);
            Assert.That(scheduler.TryAttack(now, Origin, weapon, candidates), Is.False);
            Assert.That(swings.Single().EffectiveAt, Is.EqualTo(now));
            Assert.That(swings.Single().Hits.Select(hit => hit.TargetId), Is.EquivalentTo(new[] { "aim", "edge", "in-cone" }));
            Assert.That(swings.Single().Hits.All(hit => hit.Amount == pattern.Damage), Is.True);

            now += pattern.Cooldown - TimeSpan.FromMilliseconds(1);
            Assert.That(scheduler.TryAttack(now, Origin, weapon, candidates), Is.False);
            now += TimeSpan.FromMilliseconds(1);
            Assert.That(scheduler.TryAttack(now, Origin, weapon, candidates), Is.True);
            Assert.That(swings, Has.Count.EqualTo(2));
        }

        [Test]
        public void MissingValidTargetLeavesTheCooldownAvailableAndExtractionSuppressesAttack()
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetPath);
            Assert.That(weapon, Is.Not.Null);
            var pattern = (MeleeSlashPattern)weapon.BasicAttack;
            var state = new RunState(() => TimeSpan.Zero);
            var id = RunId.New();
            Assert.That(state.TryStartRun(id, 1, TimeSpan.FromSeconds(15)), Is.True);
            int fired = 0;
            var scheduler = new AutoAttackScheduler(state, _ => { fired++; return true; }, initiallyEnabled: true);

            Assert.That(scheduler.TryAttack(TimeSpan.Zero, Origin, weapon,
                new[] { Target("outside", pattern.Range + 0.01f, 0) }), Is.False);
            Assert.That(scheduler.TryAttack(TimeSpan.Zero, Origin, weapon,
                new[] { Target("at boundary", pattern.Range, 0) }), Is.True);
            Assert.That(state.TryBeginExtraction(id, isInRange: true), Is.Not.Null);
            Assert.That(scheduler.TryAttack(pattern.Cooldown, Origin, weapon,
                new[] { Target("in range", 1, 0) }), Is.False);
            Assert.That(fired, Is.EqualTo(1));
        }

        private static TargetCandidate Target(string id, float x, float y)
            => new TargetCandidate(id, new WorldPoint2D(x, y), true, true);
    }
}
