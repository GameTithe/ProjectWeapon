#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;

namespace ProjectWeapon.AutoAttack.Tests
{
    public sealed class PiercingSpearTests
    {
        private const string AssetPath = "Assets/ProjectWeapon/Weapons/PrototypeSteelSpear/SteelSpear.asset";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(0, 0);

        [Test]
        public void OneSteelSpearThrustPiercesDistinctTargetsAlongItsLockedDirection()
        {
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetPath);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Id.Value, Is.EqualTo("steel_spear"));
            Assert.That(definition.Grade, Is.EqualTo(2));
            Assert.That(definition.Kind, Is.EqualTo(WeaponKind.Spear));
            Assert.That(definition.PrototypeBasicAttackPattern, Is.EqualTo("관통 찌르기"));
            var pattern = (PiercingSpearPattern)definition.BasicAttack;
            var candidates = new[]
            {
                Target("outside tip", pattern.Range + 0.01f, 0),
                Target("tip", pattern.Range, 0),
                Target("right at half-width", 2, pattern.HalfWidth),
                Target("aim", 1, 0),
                Target("behind", -1.5f, 0),
                Target("right outside", 2, pattern.HalfWidth + 0.01f),
                Target("right at half-width", 2, pattern.HalfWidth),
                new TargetCandidate("dead", new WorldPoint2D(1.2f, 0), false, true)
            };
            TimeSpan now = TimeSpan.Zero;
            var run = new RunState(() => now);
            Assert.That(run.TryStartRun(RunId.New(), 1, TimeSpan.FromSeconds(10)), Is.True);
            Guid application = Guid.NewGuid();
            PiercingSpearThrust? thrust = null;
            int fired = 0;
            var scheduler = new AutoAttackScheduler(run, request =>
            {
                Assert.That(request.Target.TargetId, Is.EqualTo("aim"));
                fired++;
                return pattern.TryCreateThrust(request, application, "player", candidates, out thrust);
            }, initiallyEnabled: true);

            now = TimeSpan.FromMilliseconds(100);
            Assert.That(scheduler.TryAttack(now, Origin, definition, candidates), Is.True);
            Assert.That(thrust!.EffectiveAt, Is.EqualTo(now));
            Assert.That(thrust.Tip.X, Is.EqualTo(pattern.Range));
            Assert.That(thrust.Tip.Y, Is.Zero);
            Assert.That(thrust.Hits.Select(hit => hit.TargetId), Is.EqualTo(new[] { "aim", "right at half-width", "tip" }));
            Assert.That(thrust.Hits.All(hit => hit.ApplicationId == application && hit.Amount == pattern.Damage), Is.True);
            Assert.That(scheduler.TryAttack(now, Origin, definition, candidates), Is.False);
            now += pattern.Cooldown - TimeSpan.FromMilliseconds(1);
            Assert.That(scheduler.TryAttack(now, Origin, definition, candidates), Is.False);
            now += TimeSpan.FromMilliseconds(1);
            Assert.That(scheduler.TryAttack(now, Origin, definition, candidates), Is.True);
            Assert.That(fired, Is.EqualTo(2));
        }

        [Test]
        public void CoincidentTargetDoesNotMakeAnUnspecifiedDirectionDamageNearbyTargets()
        {
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetPath);
            Assert.That(definition, Is.Not.Null);
            var pattern = (PiercingSpearPattern)definition.BasicAttack;
            var run = new RunState(() => TimeSpan.Zero);
            Assert.That(run.TryStartRun(RunId.New(), 1, TimeSpan.FromSeconds(10)), Is.True);
            var candidates = new[] { Target("at origin", 0, 0), Target("elsewhere", 0.01f, 0) };
            PiercingSpearThrust? thrust = null;
            var scheduler = new AutoAttackScheduler(run, request =>
                pattern.TryCreateThrust(request, Guid.NewGuid(), "player", candidates, out thrust), initiallyEnabled: true);

            Assert.That(scheduler.TryAttack(TimeSpan.Zero, Origin, definition, candidates), Is.True);
            Assert.That(thrust!.Hits.Single().TargetId, Is.EqualTo("at origin"));
            Assert.That(thrust.Tip.X, Is.Zero);
            Assert.That(thrust.Tip.Y, Is.Zero);
        }

        [Test]
        public void AnOutOfRangeTargetDoesNotCommitCooldown()
        {
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetPath);
            Assert.That(definition, Is.Not.Null);
            var pattern = (PiercingSpearPattern)definition.BasicAttack;
            var run = new RunState(() => TimeSpan.Zero);
            Assert.That(run.TryStartRun(RunId.New(), 1, TimeSpan.FromSeconds(10)), Is.True);
            int fired = 0;
            var scheduler = new AutoAttackScheduler(run, _ => { fired++; return true; }, initiallyEnabled: true);
            Assert.That(scheduler.TryAttack(TimeSpan.Zero, Origin, definition,
                new[] { Target("outside", pattern.Range + 0.01f, 0) }), Is.False);
            Assert.That(scheduler.TryAttack(TimeSpan.Zero, Origin, definition,
                new[] { Target("edge", pattern.Range, 0) }), Is.True);
            Assert.That(fired, Is.EqualTo(1));
        }

        private static TargetCandidate Target(string id, float x, float y)
            => new TargetCandidate(id, new WorldPoint2D(x, y), true, true);
    }
}
