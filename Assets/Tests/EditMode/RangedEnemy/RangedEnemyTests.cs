#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.Enemies.Tests
{
    public sealed class RangedEnemyTests
    {
        private const string EnemyId = "ranged-enemy";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(0, 0);
        private readonly List<RangedEnemyAttack> warnings = new List<RangedEnemyAttack>();
        private readonly List<(RangedEnemyAttack Attack, IReadOnlyList<DamageRequest> Hits)> attacks =
            new List<(RangedEnemyAttack, IReadOnlyList<DamageRequest>)>();
        private RangedEnemySettings settings = null!;
        private RangedEnemy enemy = null!;

        [SetUp]
        public void SetUp()
        {
            warnings.Clear();
            attacks.Clear();
            settings = AssetDatabase.LoadAssetAtPath<RangedEnemySettings>(
                "Assets/ProjectWeapon/Enemies/Ranged/PrototypeRangedEnemySettings.asset");
            Assert.That(settings, Is.Not.Null);
            enemy = new RangedEnemy(EnemyId, settings);
            enemy.AttackStarted += warnings.Add;
            enemy.AttackResolved += (attack, hits) => attacks.Add((attack, hits));
        }

        [Test]
        public void WaitingStartsOneWarningAtTheDetectionBoundaryOnlyForAnEligiblePlayer()
        {
            enemy.Advance(Ms(0), Origin, Player(4.01f, 0));
            enemy.Advance(Ms(1), Origin, Player(2, 0, alive: false));
            Assert.That(warnings, Is.Empty);
            Assert.That(enemy.Phase, Is.EqualTo(RangedEnemyPhase.Waiting));

            enemy.Advance(Ms(20), Origin, Player(4f, 0));
            Assert.That(enemy.Phase, Is.EqualTo(RangedEnemyPhase.Telegraphing));
            RangedEnemyAttack warning = warnings.Single();
            Assert.That(warning.StartedAt, Is.EqualTo(Ms(20)));
            Assert.That(warning.HitsAt, Is.EqualTo(Ms(770)));
            Assert.That(warning.Area.Length, Is.EqualTo(4f));
            Assert.That(warning.Area.Width, Is.EqualTo(0.7f));
            Assert.That(warning.Area.GetCorner(farEnd: true, leftSide: true),
                Is.EqualTo(new WorldPoint2D(4f, 0.35f)));
        }

        [Test]
        public void TheLockedWarningIsTheSameRectangleUsedToHitOnlyAtTheScheduledTime()
        {
            enemy.Advance(Ms(100), Origin, Player(3, 0));
            RangedEnemyAttack warning = warnings.Single();
            var movedOrigin = new WorldPoint2D(8, 8);
            enemy.Advance(Ms(849), movedOrigin, Player(0, 3));
            Assert.That(enemy.ActiveAttack?.Area.Origin, Is.EqualTo(Origin));
            Assert.That(enemy.ActiveAttack?.Area.Direction, Is.EqualTo(new WorldPoint2D(1f, 0f)));
            Assert.That(attacks, Is.Empty);

            enemy.Advance(Ms(850), movedOrigin, Player(4f, 0.35f));
            enemy.Advance(Ms(850), movedOrigin, Player(4f, 0.35f));

            var result = attacks.Single();
            DamageRequest hit = result.Hits.Single();
            Assert.That(result.Attack.HitsAt, Is.EqualTo(Ms(850)));
            Assert.That(result.Attack.Area, Is.EqualTo(warning.Area));
            Assert.That(hit.ApplicationId, Is.EqualTo(warning.ApplicationId));
            Assert.That(hit.AttackerId, Is.EqualTo(EnemyId));
            Assert.That(hit.TargetId, Is.EqualTo("player"));
            Assert.That(hit.Amount, Is.EqualTo(6f));
            Assert.That(enemy.ActiveAttack, Is.Null);
        }

        [TestCase(2f, -0.35f, true)]
        [TestCase(0f, 0.35f, true)]
        [TestCase(2f, 0.36f, false)]
        [TestCase(2f, -0.36f, false)]
        [TestCase(4.01f, 0f, false)]
        [TestCase(-0.01f, 0f, false)]
        public void WidthAndLengthBoundariesMatchTheTelegraph(float x, float y, bool shouldHit)
        {
            enemy.Advance(Ms(100), Origin, Player(1, 0));
            RangedEnemyAttack warning = warnings.Single();
            TargetCandidate target = Player(x, y);
            Assert.That(warning.Area.Contains(target.Position), Is.EqualTo(shouldHit));

            enemy.Advance(Ms(850), Origin, target);
            Assert.That(attacks.Single().Hits.Count, Is.EqualTo(shouldHit ? 1 : 0));
        }

        [Test]
        public void DiagonalShotCanBeEvadedSidewaysWithoutLeavingDetectionRange()
        {
            enemy.Advance(Ms(100), Origin, Player(2, 2));
            RangedAttackStrip area = warnings.Single().Area;
            Assert.That(area.Contains(new WorldPoint2D(2, 2)), Is.True);

            TargetCandidate sidestep = Player(2.4f, 1.6f);
            Assert.That(sidestep.Position.SquaredDistanceTo(Origin), Is.LessThan(16d));
            enemy.Advance(Ms(850), Origin, sidestep);
            Assert.That(attacks.Single().Hits, Is.Empty);
        }

        [Test]
        public void MissStillConsumesOneHitAndFullRecovery()
        {
            enemy.Advance(Ms(100), Origin, Player(2, 0));
            enemy.Advance(Ms(861), Origin, Player(0, 2));
            Assert.That(attacks.Single().Attack.HitsAt, Is.EqualTo(Ms(850)));
            Assert.That(attacks.Single().Hits, Is.Empty);
            Assert.That(enemy.Phase, Is.EqualTo(RangedEnemyPhase.Recovering));
            enemy.Advance(Ms(2099), Origin, Player(2, 0));
            Assert.That(warnings.Count, Is.EqualTo(1));

            enemy.Advance(Ms(2100), Origin, Player(2, 0));
            enemy.Advance(Ms(2100), Origin, Player(2, 0));
            Assert.That(warnings.Count, Is.EqualTo(2));
            Assert.That(warnings[1].ApplicationId, Is.Not.EqualTo(warnings[0].ApplicationId));
        }

        [Test]
        public void NonFatalDamageAppliesOnceAndDeathAtTheImpactTimeCancelsTheWarning()
        {
            var hitTimes = new List<TimeSpan>();
            var deaths = new List<TimeSpan>();
            enemy.DamageApplied += (_, time) => hitTimes.Add(time);
            enemy.Died += deaths.Add;
            enemy.Advance(Ms(100), Origin, Player(2, 0));

            DamageRequest first = IncomingDamage(4);
            Assert.That(enemy.ApplyDamage(first, Ms(200)), Is.True);
            Assert.That(enemy.ApplyDamage(first, Ms(201)), Is.False);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(14f));
            Assert.That(enemy.ActiveAttack, Is.Not.Null);

            Assert.That(enemy.ApplyDamage(IncomingDamage(20), Ms(850)), Is.True);
            enemy.Advance(Ms(850), Origin, Player(2, 0));
            enemy.Advance(Ms(3000), Origin, Player(2, 0));

            Assert.That(enemy.Phase, Is.EqualTo(RangedEnemyPhase.Dead));
            Assert.That(enemy.CurrentHealth, Is.Zero);
            Assert.That(enemy.ActiveAttack, Is.Null);
            Assert.That(attacks, Is.Empty);
            Assert.That(hitTimes, Is.EqualTo(new[] { Ms(200), Ms(850) }));
            Assert.That(deaths, Is.EqualTo(new[] { Ms(850) }));
            Assert.That(warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void ActorStaysAtItsScenePositionWhileTrackingAndAttacking()
        {
            var gameObject = new GameObject("Ranged enemy placeholder");
            try
            {
                gameObject.transform.position = new Vector3(3f, 4f, 0f);
                var actor = gameObject.AddComponent<RangedEnemyActor>();
                actor.Initialize(EnemyId, settings);
                actor.Advance(Ms(0), Player(9, 4));
                Assert.That(actor.Brain.Phase, Is.EqualTo(RangedEnemyPhase.Waiting));
                actor.Advance(Ms(100), Player(5, 4));
                Assert.That(actor.Brain.ActiveAttack?.Area.Origin, Is.EqualTo(new WorldPoint2D(3f, 4f)));
                actor.Advance(Ms(850), Player(5, 4));
                Assert.That(actor.Brain.Phase, Is.EqualTo(RangedEnemyPhase.Recovering));
                Assert.That(gameObject.transform.position, Is.EqualTo(new Vector3(3f, 4f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static DamageRequest IncomingDamage(float amount)
        {
            var contact = AreaDamageRequest.Circle(Guid.NewGuid(), "player", Origin, 0, amount);
            return AreaDamageSelector.FindTargets(contact, new[] { new TargetCandidate(EnemyId, Origin, true, true) }).Single();
        }

        private static TargetCandidate Player(float x, float y, bool alive = true)
            => new TargetCandidate("player", new WorldPoint2D(x, y), alive, isAttackable: true);

        private static TimeSpan Ms(int time) => TimeSpan.FromMilliseconds(time);
    }
}
