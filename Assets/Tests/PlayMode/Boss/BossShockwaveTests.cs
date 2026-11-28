#nullable enable

#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Extraction;
using ProjectWeapon.Run;
using UnityEditor;
using UnityEngine;

namespace ProjectWeapon.Enemies.Boss.Tests
{
    public sealed class BossShockwaveTests
    {
        private const string Prefab = "Assets/ProjectWeapon/Enemies/Boss/AbandonedCastleBoss.prefab";
        private BossShockwaveActor actor = null!;
        private RunState run = null!;
        private RunId id;
        private TimeSpan clock;

        [SetUp]
        public void SpawnBoss()
        {
            clock = TimeSpan.Zero;
            run = new RunState(() => clock);
            id = RunId.New();
            Assert.That(run.TryStartRun(id, 1, TimeSpan.FromSeconds(480)), Is.True);
            var prefab = AssetDatabase.LoadAssetAtPath<BossShockwaveActor>(Prefab);
            Assert.That(prefab, Is.Not.Null);
            actor = UnityEngine.Object.Instantiate(prefab);
            actor.Initialize(run);
        }

        [TearDown]
        public void CleanUp()
        {
            if (actor)
                UnityEngine.Object.DestroyImmediate(actor.gameObject);
        }

        [TestCase(1.6f, true)]
        [TestCase(1.61f, false)]
        public void WarningRingMatchesLockedAreaAndResolvesOnceAtTheCutoff(float position, bool hits)
        {
            int resolutions = 0;
            int damageRequests = 0;
            actor.Boss!.ShockwaveResolved += (_, damage, _) =>
            {
                resolutions++;
                damageRequests += damage.Count;
            };
            var ring = actor.GetComponentInChildren<LineRenderer>(true);
            actor.Advance(clock, Player(position));
            ShockwaveCast cast = actor.Boss.ActiveCast!.Value;
            Assert.That(ring.enabled, Is.True);
            Assert.That(ring.positionCount, Is.EqualTo(64));
            Assert.That(ring.loop, Is.True);
            Assert.That(ring.GetPosition(0).x, Is.EqualTo(cast.Area.Radius).Within(0.0001f));
            Assert.That(ring.GetPosition(16).y, Is.EqualTo(cast.Area.Radius).Within(0.0001f));

            actor.transform.position = new Vector3(20, 20, 0);
            actor.Advance(cast.HitsAt - TimeSpan.FromTicks(1), Player(position));
            Assert.That(resolutions, Is.Zero);
            Assert.That(ring.enabled, Is.True);
            actor.Advance(cast.HitsAt, Player(position));
            actor.Advance(cast.HitsAt, Player(position));
            Assert.That(resolutions, Is.EqualTo(1));
            Assert.That(damageRequests, Is.EqualTo(hits ? 1 : 0));
            Assert.That(ring.enabled, Is.False);
        }

        [Test]
        public void DamageFlashesThenTheLethalHitCancelsTheWarningAndEmitsOneDefeat()
        {
            int resolutions = 0;
            int defeats = 0;
            actor.Boss!.ShockwaveResolved += (_, _, _) => resolutions++;
            actor.Boss.Defeated += (runId, targetId, defeatedAt) =>
            {
                Assert.That(runId, Is.EqualTo(id));
                Assert.That(targetId, Is.EqualTo("zone1-boss"));
                Assert.That(defeatedAt, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
                defeats++;
            };

            actor.Advance(clock, Player(1f));
            var ring = actor.GetComponentInChildren<LineRenderer>(true);
            Assert.That(ring.enabled, Is.True);
            var body = actor.GetComponentInChildren<SpriteRenderer>();
            Assert.That(body.sprite, Is.Not.Null);
            Color idle = body.color;

            DamageRequest hit = Hit(20);
            Assert.That(actor.ApplyDamage(hit, TimeSpan.FromMilliseconds(300)), Is.True);
            Assert.That(actor.ApplyDamage(hit, TimeSpan.FromMilliseconds(300)), Is.False);
            Assert.That(body.color, Is.Not.EqualTo(idle));
            DamageRequest lethal = Hit(90);
            Assert.That(actor.ApplyDamage(lethal, TimeSpan.FromMilliseconds(500)), Is.True);
            Assert.That(actor.ApplyDamage(lethal, TimeSpan.FromMilliseconds(500)), Is.False);
            actor.Advance(TimeSpan.FromSeconds(1), Player(1f));

            Assert.That(resolutions, Is.Zero);
            Assert.That(defeats, Is.EqualTo(1));
            Assert.That(ring.enabled, Is.False);
            Assert.That(body.color, Is.EqualTo(Color.gray));
        }

        [Test]
        public void PlayerCanBypassTheBossAndCompleteARealExtraction()
        {
            int defeats = 0;
            actor.Boss!.Defeated += (_, _, _) => defeats++;
            actor.Advance(clock, Player(3));
            Assert.That(actor.Boss.ActiveCast, Is.Null);

            var exit = new WorldPoint2D(4, 2);
            var extraction = new ExtractionAccess(run, 1, exit, .75f);
            ExtractionAttempt attempt = extraction.TryStart(id, exit)!;
            Assert.That(attempt, Is.Not.Null);
            clock = attempt.CompletesAt;
            RunResult result = run.ProcessSignals(new[] { RunSignal.CompleteExtraction(id, attempt.Id, clock) })!;
            Assert.That(result.Succeeded, Is.True);
            Assert.That(defeats, Is.Zero);
            Assert.That(actor.Boss.IsDead, Is.False);
            Assert.That(actor.ApplyDamage(Hit(90), clock), Is.False);
        }

        [Test]
        public void AttemptCannotStartWhenTheTelegraphWouldEndAtTheDeadline()
        {
            clock = run.CurrentRun!.Deadline - TimeSpan.FromMilliseconds(900);
            actor.Advance(clock, Player(1));
            Assert.That(actor.Boss!.ActiveCast, Is.Null);
            actor.Advance(run.CurrentRun.Deadline, Player(1));
            Assert.That(actor.Boss.ActiveCast, Is.Null);
        }

        private DamageRequest Hit(float amount)
        {
            var request = AreaDamageRequest.Circle(Guid.NewGuid(), "player", default, 1f, amount);
            return AreaDamageSelector.FindTargets(request,
                new[] { new TargetCandidate(actor.Boss!.TargetId, default, true, true) }).Single();
        }

        private static TargetCandidate Player(float x)
            => new TargetCandidate("player", new WorldPoint2D(x, 0), true, true);
    }
}
#endif
