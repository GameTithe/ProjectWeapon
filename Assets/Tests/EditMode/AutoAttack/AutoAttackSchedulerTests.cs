#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Extraction;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.AutoAttack.Tests
{
    public sealed class AutoAttackSchedulerTests
    {
        private static readonly RunSignal[] NoSignals = Array.Empty<RunSignal>();
        private static readonly WorldPoint2D Origin = new WorldPoint2D(0, 0);
        private static readonly TargetCandidate[] BoundaryTarget = { Candidate("boundary", 2) };
        private readonly List<BasicAttackRequest> executed = new List<BasicAttackRequest>();
        private readonly List<Object> assets = new List<Object>();
        private RunState run = null!;
        private RunId id;
        private ExtractionChannel channel = null!;
        private AutoAttackScheduler scheduler = null!;
        private WeaponDefinition weapon = null!;
        private PatternStub pattern = null!;
        private TimeSpan now;

        [SetUp]
        public void SetUp()
        {
            now = TimeSpan.Zero;
            run = new RunState(() => now);
            id = RunId.New();
            Assert.That(run.TryStartRun(id, 1, At(60_000)), Is.True);
            channel = new ExtractionChannel(run);
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
            pattern = MakePattern(2f, 500);
            weapon = MakeWeapon("test_weapon", pattern);
            executed.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in assets)
                Object.DestroyImmediate(item);

            assets.Clear();
        }

        [Test]
        public void ExecutesNearestEligibleTargetOnlyOnceAndCarriesItsSnapshot()
        {
            var candidates = new[]
            {
                Candidate("dead", .5f, alive: false),
                Candidate("blocked", .6f, attackable: false),
                Candidate("b", -1),
                Candidate("a", 1),
                Candidate("far", 2.001f)
            };

            Assert.That(Tick(0, candidates), Is.True);
            Assert.That(Tick(0, candidates), Is.False);
            Assert.That(executed, Has.Count.EqualTo(1));
            BasicAttackRequest attack = executed[0];
            Assert.That(attack.Target.TargetId, Is.EqualTo("a"));
            Assert.That(attack.Target.Position.X, Is.EqualTo(1));
            Assert.That(attack.Origin.X, Is.EqualTo(0));
            Assert.That(attack.RunId, Is.EqualTo(id));
            Assert.That(attack.WeaponId, Is.EqualTo(weapon.Id));
            Assert.That(attack.Pattern, Is.SameAs(pattern));
            Assert.That(attack.StartedAt, Is.EqualTo(At(0)));
        }

        [Test]
        public void UsesExactCooldownAndRangeWithoutCatchUp()
        {
            Assert.That(Tick(0), Is.True);
            Assert.That(Tick(499), Is.False);
            Assert.That(Tick(500), Is.True);
            Assert.That(Tick(3500), Is.True);
            Assert.That(Tick(3500), Is.False);
            Assert.That(executed, Has.Count.EqualTo(3));
            Assert.That(executed[2].Target.TargetId, Is.EqualTo("boundary"));
        }

        [Test]
        public void ToggleIsImmediateAndDoesNotResetCooldown()
        {
            Assert.That(Tick(0), Is.True);
            Assert.That(scheduler.TrySetEnabled(false), Is.True);
            Assert.That(Tick(600), Is.False);
            Assert.That(scheduler.TrySetEnabled(true), Is.True);
            Assert.That(Tick(600), Is.True);
            Assert.That(scheduler.TrySetEnabled(false), Is.True);
            Assert.That(scheduler.TrySetEnabled(true), Is.True);
            Assert.That(Tick(600), Is.False);
            Assert.That(Tick(1099), Is.False);
            Assert.That(Tick(1100), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ChannelSuppressesAttacksAndCancelRestoresThePriorToggle(bool previouslyEnabled)
        {
            Assert.That(scheduler.TrySetEnabled(previouslyEnabled), Is.True);
            Assert.That(Tick(0), Is.EqualTo(previouslyEnabled));
            now = At(100);
            ExtractionAttempt attempt = run.TryBeginExtraction(id, isInRange: true)!;
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.True);
            Assert.That(scheduler.IsEffective, Is.False);
            Assert.That(scheduler.IsEnabled, Is.EqualTo(previouslyEnabled));
            Assert.That(scheduler.TrySetEnabled(!previouslyEnabled), Is.False);
            Assert.That(Tick(100), Is.False);
            Assert.That(Tick(800), Is.False);

            now = At(900);
            channel.ProcessFrame(new[] { RunSignal.CancelExtraction(id, attempt.Id, now) });
            Assert.That(channel.IsAutomaticAttackSuppressed, Is.False);
            Assert.That(scheduler.IsEnabled, Is.EqualTo(previouslyEnabled));
            Assert.That(Tick(900), Is.EqualTo(previouslyEnabled));
        }

        [Test]
        public void EndingExtractionSuccessfullyDoesNotAllowAReadyAttack()
        {
            Assert.That(run.TryBeginExtraction(id, isInRange: true), Is.Not.Null);
            Assert.That(Tick(0), Is.False);
            Assert.That(Tick(2999), Is.False);
            Assert.That(Tick(3000), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Succeeded));
            Assert.That(executed, Is.Empty);
        }

        [Test]
        public void DeadlineBlocksAttacksAndNextRunHasAFreshCooldown()
        {
            Assert.That(Tick(0), Is.True);
            now = At(100);
            channel.ProcessFrame(new[] { RunSignal.PlayerDied(id, now) });
            Assert.That(Tick(150), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Failed));
            Assert.That(run.TryReturnToTown(id), Is.True);
            Assert.That(Tick(150), Is.False);

            id = RunId.New();
            Assert.That(run.TryStartRun(id, 1, At(1000)), Is.True);
            Assert.That(Tick(150), Is.True);
            now = At(1150);
            Assert.That(scheduler.TryAttack(now, Origin, weapon, BoundaryTarget), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Exploring));
            Assert.That(Tick(1150), Is.False);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Failed));
            Assert.That(executed, Has.Count.EqualTo(2));
            Assert.That(executed[1].RunId, Is.EqualTo(id));
        }

        [Test]
        public void DeathPreventsAnOtherwiseReadyAttack()
        {
            now = At(100);
            channel.ProcessFrame(new[] { RunSignal.PlayerDied(id, now) });

            Assert.That(run.Phase, Is.EqualTo(RunPhase.Failed));
            Assert.That(scheduler.TryAttack(now, Origin, weapon, BoundaryTarget), Is.False);
            Assert.That(executed, Is.Empty);
        }

        [Test]
        public void MissingOrInvalidWeaponPatternsAndTargetsNeverStartOrSpendCooldown()
        {
            Assert.That(scheduler.TryAttack(now, Origin, null, BoundaryTarget), Is.False);
            Assert.That(scheduler.TryAttack(now, Origin, MakeWeapon("none", null), BoundaryTarget), Is.False);
            Assert.That(scheduler.TryAttack(now, Origin, MakeWeapon("wrong", Make<OtherPattern>()), BoundaryTarget),
                Is.False);
            Assert.That(scheduler.TryAttack(now, Origin, MakeWeapon("", pattern), BoundaryTarget), Is.False);
            Assert.That(scheduler.TryAttack(now, Origin, MakeWeapon("range", MakePattern(-1f, 500)), BoundaryTarget),
                Is.False);
            Assert.That(scheduler.TryAttack(now, Origin, MakeWeapon("cooldown", MakePattern(2f, 0)), BoundaryTarget),
                Is.False);
            Assert.That(scheduler.TryAttack(now, new WorldPoint2D(float.NaN, 0), weapon, BoundaryTarget), Is.False);
            Assert.That(Tick(0, Array.Empty<TargetCandidate>()), Is.False);
            Assert.That(Tick(0, new[] { Candidate("far", 2.001f), Candidate("", .01f) }), Is.False);
            Assert.That(executed, Is.Empty);
            Assert.That(Tick(0), Is.True);
        }

        [Test]
        public void RejectedDispatchCanBeRetriedWithoutReenteringTheExecutor()
        {
            int executions = 0;
            scheduler = new AutoAttackScheduler(run, request =>
            {
                executions++;
                Assert.That(scheduler.TryAttack(request.StartedAt, Origin, weapon, BoundaryTarget), Is.False);
                return executions == 2;
            }, initiallyEnabled: true);

            Assert.That(Tick(0), Is.False);
            Assert.That(Tick(0), Is.True);
            Assert.That(Tick(0), Is.False);
            Assert.That(executions, Is.EqualTo(2));
        }

        [Test]
        public void NewWeaponTimingDoesNotShortenAnAlreadyStartedCooldown()
        {
            Assert.That(Tick(0), Is.True);
            weapon = MakeWeapon("new_weapon", MakePattern(2f, 50));
            Assert.That(Tick(50), Is.False);
            Assert.That(Tick(500), Is.True);
            Assert.That(Tick(549), Is.False);
            Assert.That(Tick(550), Is.True);
        }

        private bool Tick(int milliseconds, IReadOnlyList<TargetCandidate>? candidates = null)
        {
            now = At(milliseconds);
            channel.ProcessFrame(NoSignals);
            return scheduler.TryAttack(now, Origin, weapon, candidates ?? BoundaryTarget);
        }

        private bool Execute(BasicAttackRequest request)
        {
            executed.Add(request);
            return true;
        }

        private PatternStub MakePattern(float range, int cooldownMs)
        {
            var result = Make<PatternStub>();
            using var serialized = new SerializedObject(result);
            serialized.FindProperty("range").floatValue = range;
            serialized.FindProperty("cooldownMilliseconds").intValue = cooldownMs;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return result;
        }

        private WeaponDefinition MakeWeapon(string weaponId, ScriptableObject? basicAttack)
        {
            var result = Make<WeaponDefinition>();
            using var serialized = new SerializedObject(result);
            serialized.FindProperty("definitionId").stringValue = weaponId;
            serialized.FindProperty("basicAttack").objectReferenceValue = basicAttack;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return result;
        }

        private T Make<T>() where T : ScriptableObject
        {
            T item = ScriptableObject.CreateInstance<T>();
            assets.Add(item);
            return item;
        }

        private static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

        private static TargetCandidate Candidate(string targetId, float x, bool alive = true, bool attackable = true) =>
            new TargetCandidate(targetId, new WorldPoint2D(x, 0), alive, attackable);

        private sealed class PatternStub : BasicAttackPattern { }
        private sealed class OtherPattern : ScriptableObject { }
    }
}
