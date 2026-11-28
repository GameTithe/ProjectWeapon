#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using UnityEditor;

namespace ProjectWeapon.Enemies.Tests
{
    public sealed class MeleeEnemyTests
    {
        private const string EnemyId = "melee-enemy";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(0, 0);
        private readonly List<MeleeEnemyAttack> warnings = new List<MeleeEnemyAttack>();
        private readonly List<(MeleeEnemyAttack Attack, IReadOnlyList<DamageRequest> Hits)> resolved =
            new List<(MeleeEnemyAttack, IReadOnlyList<DamageRequest>)>();
        private MeleeEnemySettings settings = null!;
        private MeleeEnemy enemy = null!;

        [SetUp]
        public void SetUp()
        {
            warnings.Clear();
            resolved.Clear();
            settings = AssetDatabase.LoadAssetAtPath<MeleeEnemySettings>(
                "Assets/ProjectWeapon/Enemies/Melee/PrototypeMeleeEnemySettings.asset");
            Assert.That(settings, Is.Not.Null);
            enemy = new MeleeEnemy(EnemyId, settings);
            enemy.AttackStarted += warnings.Add;
            enemy.AttackResolved += (attack, hits) => resolved.Add((attack, hits));
        }

        [Test]
        public void ChasesValidPlayerThenStopsAtInclusiveDecisionDistance()
        {
            enemy.Advance(Ms(0), Origin, Player(1.26f, 0));
            Assert.That(enemy.Phase, Is.EqualTo(MeleeEnemyPhase.Seeking));
            Assert.That(enemy.MovementVelocity.X, Is.EqualTo(2.2f));
            Assert.That(enemy.MovementVelocity.Y, Is.Zero);
            Assert.That(warnings, Is.Empty);

            enemy.Advance(Ms(1), Origin, Player(1.25f, 0));
            Assert.That(enemy.Phase, Is.EqualTo(MeleeEnemyPhase.Telegraphing));
            Assert.That(enemy.MovementVelocity.X, Is.Zero);
            Assert.That(enemy.MovementVelocity.Y, Is.Zero);
            Assert.That(warnings.Single().HitsAt, Is.EqualTo(Ms(451)));
        }

        [Test]
        public void ChangedEnemyOriginAndMovedPlayerDoNotRotateOrTranslateTheLockedSector()
        {
            enemy.Advance(Ms(100), Origin, Player(1f, 0));
            MeleeEnemyAttack warning = warnings.Single();
            Assert.That(warning.StartedAt, Is.EqualTo(Ms(100)));
            Assert.That(warning.Area.Shape, Is.EqualTo(DamageAreaShape.Sector));
            Assert.That(warning.Area.Radius, Is.EqualTo(1.4f));
            Assert.That(warning.Area.AngleDegrees, Is.EqualTo(90f));
            Assert.That(warning.Area.Origin, Is.EqualTo(Origin));
            Assert.That(warning.Area.Direction, Is.EqualTo(new WorldPoint2D(1f, 0)));

            var movedOrigin = new WorldPoint2D(6f, 8f);
            enemy.Advance(Ms(549), movedOrigin, Player(-1f, 0));
            Assert.That(enemy.ActiveAttack?.Area.ApplicationId, Is.EqualTo(warning.Area.ApplicationId));
            Assert.That(resolved, Is.Empty);

            enemy.Advance(Ms(550), movedOrigin, Player(1.4f, 0));
            enemy.Advance(Ms(550), movedOrigin, Player(1.4f, 0));
            var result = resolved.Single();
            Assert.That(result.Attack.HitsAt, Is.EqualTo(Ms(550)));
            Assert.That(result.Attack.Area, Is.EqualTo(warning.Area));
            Assert.That(result.Hits.Single().TargetId, Is.EqualTo("player"));
            Assert.That(result.Hits.Single().ApplicationId, Is.EqualTo(warning.Area.ApplicationId));
            Assert.That(result.Hits.Single().Amount, Is.EqualTo(8f));
            Assert.That(enemy.ActiveAttack, Is.Null);
        }

        [TestCase(0.8f, 0.8f, true)]
        [TestCase(0.8f, -0.8f, true)]
        [TestCase(0.8f, 0.81f, false)]
        [TestCase(1.41f, 0, false)]
        [TestCase(-1, 0, false)]
        public void ImpactUsesThePreviouslyTelegraphedInclusiveArea(float x, float y, bool hit)
        {
            enemy.Advance(Ms(100), Origin, Player(1f, 0));
            enemy.Advance(Ms(550), Origin, Player(x, y));

            Assert.That(resolved.Single().Hits, Has.Count.EqualTo(hit ? 1 : 0));
        }

        [Test]
        public void RecoveryUsesTheHitTimestampAndAllowsOnlyOneNewTelegraphAtTheBoundary()
        {
            enemy.Advance(Ms(100), Origin, Player(1f, 0));
            enemy.Advance(Ms(560), Origin, Player(1f, 0));
            enemy.Advance(Ms(1399), Origin, Player(1f, 0));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(enemy.Phase, Is.EqualTo(MeleeEnemyPhase.Recovering));
            Assert.That(enemy.MovementVelocity.X, Is.Zero);

            enemy.Advance(Ms(1400), Origin, Player(1f, 0));
            enemy.Advance(Ms(1400), Origin, Player(1f, 0));
            Assert.That(warnings, Has.Count.EqualTo(2));
            Assert.That(warnings[1].Area.ApplicationId, Is.Not.EqualTo(warnings[0].Area.ApplicationId));
        }

        [Test]
        public void DamageAppliesOnceAndDeathAtTheHitTimeCancelsTheWarning()
        {
            var hitTimes = new List<TimeSpan>();
            var deaths = new List<TimeSpan>();
            enemy.DamageApplied += (_, time) => hitTimes.Add(time);
            enemy.Died += deaths.Add;
            enemy.Advance(Ms(100), Origin, Player(1f, 0));

            DamageRequest first = IncomingDamage(7f);
            Assert.That(enemy.ApplyDamage(first, Ms(200)), Is.True);
            Assert.That(enemy.ApplyDamage(first, Ms(201)), Is.False);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(17f));
            Assert.That(enemy.Phase, Is.EqualTo(MeleeEnemyPhase.Telegraphing));

            Assert.That(enemy.ApplyDamage(IncomingDamage(24f), Ms(550)), Is.True);
            enemy.Advance(Ms(550), Origin, Player(1f, 0));
            enemy.Advance(Ms(2400), Origin, Player(1f, 0));

            Assert.That(enemy.CurrentHealth, Is.Zero);
            Assert.That(enemy.Phase, Is.EqualTo(MeleeEnemyPhase.Dead));
            Assert.That(enemy.ActiveAttack, Is.Null);
            Assert.That(enemy.MovementVelocity.X, Is.Zero);
            Assert.That(hitTimes, Is.EqualTo(new[] { Ms(200), Ms(550) }));
            Assert.That(deaths, Is.EqualTo(new[] { Ms(550) }));
            Assert.That(resolved, Is.Empty);
            Assert.That(warnings, Has.Count.EqualTo(1));
        }

        [Test]
        public void InvalidTargetDoesNotStartAttackAndCannotBeHitWhenItDiesDuringWarning()
        {
            enemy.Advance(Ms(0), Origin, Player(0.75f, 0, alive: false));
            Assert.That(enemy.MovementVelocity.X, Is.Zero);
            Assert.That(warnings, Is.Empty);

            enemy.Advance(Ms(100), Origin, Player(0.75f, 0));
            enemy.Advance(Ms(550), Origin, Player(0.75f, 0, alive: false));
            Assert.That(resolved.Single().Hits, Is.Empty);
        }

        private static DamageRequest IncomingDamage(float amount)
        {
            var area = AreaDamageRequest.Circle(Guid.NewGuid(), "player", Origin, radius: 0, amount: amount);
            return AreaDamageSelector.FindTargets(area, new[] { new TargetCandidate(EnemyId, Origin, true, true) }).Single();
        }

        private static TargetCandidate Player(float x, float y, bool alive = true)
            => new TargetCandidate("player", new WorldPoint2D(x, y), alive, isAttackable: true);

        private static TimeSpan Ms(int time) => TimeSpan.FromMilliseconds(time);
    }
}
