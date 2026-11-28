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
    public sealed class DoomScythePatternTests
    {
        private const string Folder = "Assets/ProjectWeapon/Weapons/PrototypeDoomScythe/";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(2, 4);
        private readonly List<CircularStrike> strikes = new List<CircularStrike>();
        private WeaponDefinition scythe = null!;
        private RunState run = null!;
        private RunId runId;
        private TimeSpan clock;
        private TargetCandidate[] currentTargets = null!;
        private AutoAttackScheduler scheduler = null!;
        private DoomScytheSequence tail = null!;
        private string attacker = null!;

        [SetUp]
        public void SetUp()
        {
            strikes.Clear();
            tail = null!;
            attacker = "player";
            clock = TimeSpan.Zero;
            scythe = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Folder + "DoomScythe.asset");
            Assert.That(scythe, Is.Not.Null);
            run = new RunState(() => clock);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(5000)), Is.True);
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [Test]
        public void DefinitionLinksOneGradeFiveAttackAndDistinctTuning()
        {
            var pattern = AssetDatabase.LoadAssetAtPath<DoomScythePattern>(Folder + "DoomScytheShadowStrikes.asset");
            Assert.That(scythe.Id.Value, Is.EqualTo("doom_scythe"));
            Assert.That(scythe.Grade, Is.EqualTo(5));
            Assert.That(scythe.Kind, Is.EqualTo(WeaponKind.Scythe));
            Assert.That(scythe.BasicAttack, Is.SameAs(pattern));
            Assert.That(scythe.Skill, Is.Null);
            Assert.That(pattern.Range, Is.EqualTo(3f));
            Assert.That(pattern.BlastRadius, Is.GreaterThan(pattern.Range));
            Assert.That(pattern.SecondSlashDelay, Is.EqualTo(Ms(260)));
            Assert.That(pattern.ShadowBlastDelay, Is.EqualTo(Ms(700)));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(2150)));
        }

        [Test]
        public void TwoSlashesAndOneLargerBlastRefreshEligibilityAtTheSameLockedOrigin()
        {
            TargetCandidate initial = Target("initial", 3);
            TargetCandidate shared = Target("shared", 1);
            Assert.That(AttackAt(100, initial, shared, shared, Target("blast-only", 4.19f),
                Target("invalid", 2, attackable: false)), Is.True);
            AssertHit(strikes.Single(), 100, 19f, "initial", "shared");
            Assert.That(tail.NextHitAt, Is.EqualTo(Ms(360)));
            Assert.That(Resolve(359, shared), Is.Null);

            CircularStrike second = Resolve(360, Target("initial", 3.1f),
                Target("recently-dead", 2, alive: false), shared, shared, Target("arrived", 2.8f),
                new TargetCandidate("near-moved-player", new WorldPoint2D(20, 20), true, true))!;
            Assert.That(second, Is.Not.Null);
            AssertHit(second, 360, 19f, "shared", "arrived");
            Assert.That(tail.NextHitAt, Is.EqualTo(Ms(800)));
            Assert.That(Resolve(360, shared), Is.Null);
            Assert.That(Resolve(799, shared), Is.Null);

            CircularStrike blast = Resolve(800, Target("last-edge", -4.2f), Target("last-outside", -4.21f),
                Target("blast-only", 4.19f), shared, shared, Target("arrived", 2.8f, alive: false))!;
            Assert.That(blast, Is.Not.Null);
            AssertHit(blast, 800, 28f, "last-edge", "blast-only", "shared");
            Assert.That(tail.IsFinished, Is.True);
            Assert.That(tail.NextHitAt, Is.Null);
            Assert.That(Resolve(801, shared), Is.Null);
            Assert.That(strikes.Select(strike => strike.ApplicationId).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void OnlyTargetsWithinCastRangeStartTheFullSequenceAndTheCooldown()
        {
            Assert.That(AttackAt(100, Target("outside", 3.001f)), Is.False);
            attacker = "target";
            Assert.That(AttackAt(100, Target("target", 3)), Is.False);
            Assert.That(strikes, Is.Empty);
            attacker = "player";
            Assert.That(AttackAt(100, Target("target", 3)), Is.True);
            Assert.That(AttackAt(2249, Target("target", 3)), Is.False);
            Assert.That(AttackAt(2250, Target("target", 3)), Is.True);
            Assert.That(strikes.Count, Is.EqualTo(2));
        }

        [Test]
        public void AlreadyStartedEffectsContinueWhileAnActiveReturnBlocksNewAttacks()
        {
            TargetCandidate target = Target("target", 2);
            Assert.That(AttackAt(100, target), Is.True);
            clock = Ms(150);
            Assert.That(run.TryBeginExtraction(runId, true), Is.Not.Null);
            Assert.That(Resolve(360, target), Is.Not.Null);
            Assert.That(Resolve(800, target), Is.Not.Null);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Extracting));
            Assert.That(AttackAt(2250, target), Is.False);
        }

        [Test]
        public void AHostCanResolveOverduePartsInOrderAndResampleBetweenThem()
        {
            TargetCandidate old = Target("old", 2);
            Assert.That(AttackAt(100, old), Is.True);
            AssertHit(Resolve(850, old)!, 850, 19f, "old");
            Assert.That(Resolve(849, Target("new", 2)), Is.Null);
            AssertHit(Resolve(850, Target("old", 2, alive: false), Target("new", 4))!, 850, 28f, "new");
            Assert.That(tail.IsFinished, Is.True);
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.Extracted)]
        public void TerminalOutcomeAbandonsAnyUnprocessedParts(RunEndReason reason)
        {
            TargetCandidate target = Target("target", 1);
            Assert.That(AttackAt(100, target), Is.True);
            clock = Ms(150);
            if (reason == RunEndReason.Death)
                run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, clock) });
            else
            {
                ExtractionAttempt attempt = run.TryBeginExtraction(runId, true)!;
                clock = attempt.CompletesAt;
                run.ProcessSignals(new[] { RunSignal.CompleteExtraction(runId, attempt.Id, clock) });
            }

            Assert.That(run.Result?.Reason, Is.EqualTo(reason));
            Assert.That(Resolve((int)clock.TotalMilliseconds, target), Is.Null);
            Assert.That(tail.IsFinished, Is.True);
            Assert.That(strikes.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExactRunDeadlineCancelsTheRestEvenBeforeExpiryHasBeenProcessed()
        {
            TargetCandidate target = Target("target", 1);
            Assert.That(AttackAt(4740, target), Is.True);
            Assert.That(tail.NextHitAt, Is.EqualTo(Ms(5000)));
            Assert.That(Resolve(4999, target), Is.Null);
            Assert.That(run.Result, Is.Null);
            Assert.That(Resolve(5000, target), Is.Null);
            Assert.That(tail.IsFinished, Is.True);
            Assert.That(Resolve(5300, target), Is.Null);
            Assert.That(strikes.Count, Is.EqualTo(1));
        }

        [Test]
        public void AnotherRunWithAResetClockCannotReviveTheSequence()
        {
            TargetCandidate target = Target("target", 1);
            Assert.That(AttackAt(100, target), Is.True);
            var other = new RunState(() => TimeSpan.Zero);
            Assert.That(other.TryStartRun(RunId.New(), 1, Ms(5000)), Is.True);
            Assert.That(tail.TryResolveNext(other, TimeSpan.Zero, new[] { target }, out _), Is.False);
            Assert.That(tail.IsFinished, Is.True);
            Assert.That(Resolve(360, target), Is.Null);
        }

        private bool Execute(BasicAttackRequest request)
        {
            if (request.Pattern is not DoomScythePattern pattern ||
                !pattern.TryStart(request, attacker, currentTargets, out var immediate, out var sequence))
                return false;

            tail = sequence;
            strikes.Add(immediate);
            return true;
        }

        private bool AttackAt(int milliseconds, params TargetCandidate[] targets)
        {
            currentTargets = targets;
            clock = Ms(milliseconds);
            return scheduler.TryAttack(clock, Origin, scythe, targets);
        }

        private CircularStrike? Resolve(int milliseconds, params TargetCandidate[] targets)
        {
            clock = Ms(milliseconds);
            if (!tail.TryResolveNext(run, clock, targets, out CircularStrike? strike))
                return null;

            strikes.Add(strike);
            return strike;
        }

        private void AssertHit(CircularStrike strike, int at, float damage, params string[] ids)
        {
            Assert.That(strike.RunId, Is.EqualTo(runId));
            Assert.That(strike.EffectiveAt, Is.EqualTo(Ms(at)));
            CollectionAssert.AreEquivalent(ids, strike.Hits.Select(hit => hit.TargetId));
            Assert.That(strike.Hits.All(hit => hit.ApplicationId == strike.ApplicationId && hit.Amount == damage), Is.True);
        }

        private static TargetCandidate Target(string id, float x, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + x, Origin.Y), alive, attackable);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
