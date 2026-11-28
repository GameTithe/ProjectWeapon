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
    public sealed class AbyssScythePatternTests
    {
        private const string AssetPath = "Assets/ProjectWeapon/Weapons/PrototypeAbyssScythe/";
        private const string Attacker = "preview-player";
        private static readonly WorldPoint2D Origin = new WorldPoint2D(3f, -2f);
        private readonly List<CircularStrike> immediate = new List<CircularStrike>();
        private readonly List<AbyssScytheFollowUp> scheduled = new List<AbyssScytheFollowUp>();
        private WeaponDefinition weapon = null!;
        private AbyssScythePattern pattern = null!;
        private RunId runId;
        private RunState run = null!;
        private AutoAttackScheduler scheduler = null!;
        private TimeSpan now;
        private IReadOnlyList<TargetCandidate> targets = null!;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(AssetPath + "PrototypeAbyssScytheWeaponCatalog.asset");
            weapon = catalog.CreateLookup()[new WeaponDefinitionId("abyss_scythe")];
            pattern = (AbyssScythePattern)weapon.BasicAttack;
            immediate.Clear();
            scheduled.Clear();
            targets = Array.Empty<TargetCandidate>();
            now = TimeSpan.Zero;
            run = new RunState(() => now);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 5, Ms(10_000)), Is.True);
            scheduler = new AutoAttackScheduler(run, Execute, initiallyEnabled: true);
        }

        [Test]
        public void GradeFourScytheCatalogUsesOneConfiguredBasicAttack()
        {
            var patternAsset = AssetDatabase.LoadAssetAtPath<AbyssScythePattern>(AssetPath + "AbyssScytheCircularStrike.asset");
            Assert.That(weapon.Grade, Is.EqualTo(4));
            Assert.That(weapon.Kind, Is.EqualTo(WeaponKind.Scythe));
            Assert.That((int)weapon.Kind, Is.EqualTo(7));
            Assert.That(pattern, Is.SameAs(patternAsset));
            Assert.That(weapon.Skill, Is.Null);
            Assert.That(pattern.Range, Is.EqualTo(2.25f));
            Assert.That(pattern.StrikeInterval, Is.EqualTo(Ms(280)));
            Assert.That(pattern.Damage, Is.EqualTo(14f));
            Assert.That(pattern.Cooldown, Is.EqualTo(Ms(1600)));
        }

        [Test]
        public void OneCastHitsEachEligibleTargetOnlyOncePerPulseWithDistinctApplicationIds()
        {
            TargetCandidate near = Candidate("near", 0.5f, 0);
            targets = new[]
            {
                near,
                near,
                Candidate("rear-edge", -2.25f, 0),
                Candidate("north", 0, 1.8f),
                Candidate("outside", 2.26f, 0),
                Candidate("dead", 0, -1f, alive: false),
                Candidate("untargetable", 0, 1f, attackable: false),
                Candidate(Attacker, 1f, 0)
            };

            Assert.That(AttackAt(100), Is.True);
            CircularStrike first = immediate.Single();
            AbyssScytheFollowUp followUp = scheduled.Single();
            Assert.That(first.RunId, Is.EqualTo(runId));
            Assert.That(first.EffectiveAt, Is.EqualTo(Ms(100)));
            Assert.That(followUp.Origin.X, Is.EqualTo(Origin.X));
            Assert.That(followUp.Origin.Y, Is.EqualTo(Origin.Y));
            Assert.That(followUp.Radius, Is.EqualTo(2.25f));
            Assert.That(followUp.HitsAt, Is.EqualTo(Ms(380)));
            Assert.That(ResolveAt(379, out _), Is.False);

            Assert.That(ResolveAt(380, out CircularStrike? second), Is.True);
            Assert.That(second!.EffectiveAt, Is.EqualTo(Ms(380)));
            Assert.That(second.RunId, Is.EqualTo(runId));
            Assert.That(second.ApplicationId, Is.Not.EqualTo(first.ApplicationId));
            foreach (CircularStrike strike in new[] { first, second })
            {
                Assert.That(strike.ApplicationId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(strike.Hits.Select(hit => hit.TargetId), Is.EquivalentTo(new[] { "near", "rear-edge", "north" }));
                Assert.That(strike.Hits.All(hit => hit.Amount == 14f && hit.AttackerId == Attacker
                    && hit.ApplicationId == strike.ApplicationId), Is.True);
            }

            Assert.That(ResolveAt(380, out _), Is.False);
            Assert.That(ResolveAt(800, out _), Is.False);
            Assert.That(followUp.IsFinished, Is.True);
        }

        [Test]
        public void FollowUpRechecksCurrentTargetsAroundTheCastPointAndUsesTheObservedHitTime()
        {
            targets = new[] { Candidate("leaves", 0.5f, 0), Candidate("dies", 1f, 0) };
            Assert.That(AttackAt(100), Is.True);
            Assert.That(immediate.Single().Hits, Has.Count.EqualTo(2));

            targets = new[]
            {
                Candidate("leaves", 3f, 0),
                Candidate("dies", 1f, 0, alive: false),
                Candidate("enters", -2.25f, 0),
                Candidate("near-new-attacker-position-only", 4f, 0)
            };
            Assert.That(ResolveAt(425, out CircularStrike? second), Is.True);
            Assert.That(second!.EffectiveAt, Is.EqualTo(Ms(425)));
            Assert.That(second.Hits.Single().TargetId, Is.EqualTo("enters"));
        }

        [Test]
        public void EmptySecondPulseIsStillConsumedAndCannotReplayOnLaterTargets()
        {
            targets = new[] { Candidate("initial", 1f, 0) };
            Assert.That(AttackAt(100), Is.True);
            targets = Array.Empty<TargetCandidate>();
            Assert.That(ResolveAt(380, out CircularStrike? strike), Is.True);
            Assert.That(strike!.Hits, Is.Empty);

            targets = new[] { Candidate("arrives-later", 0, 1f) };
            Assert.That(ResolveAt(381, out _), Is.False);
        }

        [Test]
        public void CooldownAppliesToTheWholeCastAndMissingTargetDoesNotConsumeIt()
        {
            targets = new[] { Candidate("outside", 2.26f, 0), Candidate("dead", 0, 1f, alive: false) };
            Assert.That(AttackAt(100), Is.False);
            Assert.That(immediate, Is.Empty);
            Assert.That(scheduled, Is.Empty);

            targets = new[] { Candidate("exact-edge", 2.25f, 0) };
            Assert.That(AttackAt(100), Is.True);
            Assert.That(AttackAt(100), Is.False);
            Assert.That(ResolveAt(380, out _), Is.True);
            Assert.That(AttackAt(1699), Is.False);
            Assert.That(AttackAt(1700), Is.True);
            Assert.That(immediate.Select(strike => strike.EffectiveAt), Is.EqualTo(new[] { Ms(100), Ms(1700) }));
            Assert.That(scheduled, Has.Count.EqualTo(2));
        }

        [Test]
        public void ChannelPreventsNewCastsWhileThePreviousPulseCanFinishInTheActiveRun()
        {
            targets = new[] { Candidate("near", 1f, 0) };
            Assert.That(AttackAt(100), Is.True);
            now = Ms(200);
            Assert.That(run.TryBeginExtraction(runId, true), Is.Not.Null);

            Assert.That(ResolveAt(380, out _), Is.True);
            Assert.That(AttackAt(1700), Is.False);
            Assert.That(immediate, Has.Count.EqualTo(1));
        }

        [TestCase(9720, 10000)]
        [TestCase(9700, 10000)]
        [TestCase(9700, 10100)]
        public void PendingPulseCannotHitAtOrAfterOriginalDeadline(int startedAt, int observedAt)
        {
            targets = new[] { Candidate("near", 1f, 0) };
            Assert.That(AttackAt(startedAt), Is.True);
            Assert.That(ResolveAt(observedAt, out CircularStrike? expired), Is.False);
            Assert.That(expired, Is.Null);
            Assert.That(scheduled.Single().IsFinished, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerminalOrDifferentRunCancelsAnyUnprocessedPulse(bool startNewRun)
        {
            targets = new[] { Candidate("near", 1f, 0) };
            Assert.That(AttackAt(100), Is.True);
            now = Ms(200);
            run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, now) });
            if (startNewRun)
            {
                Assert.That(run.TryReturnToTown(runId), Is.True);
                Assert.That(run.TryStartRun(RunId.New(), 5, Ms(10_000)), Is.True);
            }

            Assert.That(ResolveAt(400, out CircularStrike? cancelled), Is.False);
            Assert.That(cancelled, Is.Null);
            Assert.That(scheduled.Single().IsFinished, Is.True);
        }

        [TestCase(0, 14f)]
        [TestCase(1600, 14f)]
        [TestCase(280, -1f)]
        [TestCase(280, float.NaN)]
        public void InvalidTuningProducesNeitherPulseAndDoesNotPutTheSchedulerOnCooldown(int interval, float damage)
        {
            AbyssScythePattern invalid = UnityEngine.Object.Instantiate(pattern);
            WeaponDefinition copy = UnityEngine.Object.Instantiate(weapon);
            try
            {
                var tuning = new SerializedObject(invalid);
                tuning.FindProperty("strikeIntervalMilliseconds").intValue = interval;
                tuning.FindProperty("damage").floatValue = damage;
                tuning.ApplyModifiedPropertiesWithoutUndo();
                var weaponData = new SerializedObject(copy);
                weaponData.FindProperty("basicAttack").objectReferenceValue = invalid;
                weaponData.ApplyModifiedPropertiesWithoutUndo();
                targets = new[] { Candidate("near", 1f, 0) };

                Assert.That(scheduler.TryAttack(now, Origin, copy, targets), Is.False);
                Assert.That(immediate, Is.Empty);
                Assert.That(scheduled, Is.Empty);
                Assert.That(AttackAt(0), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(invalid);
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        private bool Execute(BasicAttackRequest request)
        {
            if (request.Pattern is not AbyssScythePattern current
                || !current.TryStart(request, Attacker, targets, out var first, out var followUp))
                return false;

            immediate.Add(first);
            scheduled.Add(followUp);
            return true;
        }

        private bool AttackAt(int milliseconds)
        {
            now = Ms(milliseconds);
            return scheduler.TryAttack(now, Origin, weapon, targets);
        }

        private bool ResolveAt(int milliseconds, out CircularStrike? strike)
        {
            now = Ms(milliseconds);
            return scheduled.Single().TryResolve(run, now, targets, out strike);
        }

        private static TargetCandidate Candidate(string id, float dx, float dy, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(Origin.X + dx, Origin.Y + dy), alive, attackable);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
