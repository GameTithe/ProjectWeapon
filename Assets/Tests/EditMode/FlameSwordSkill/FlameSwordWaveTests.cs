#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Enemies;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectWeapon.FlameSwordSkill.Tests
{
    public sealed class FlameSwordWaveTests
    {
        private const string PatternPath = "Assets/ProjectWeapon/FlameSwordSkill/FlameSwordWave.asset";
        private const string WeaponPath = "Assets/ProjectWeapon/Weapons/PrototypeFlameSword/FlameSword.asset";
        private const string Attacker = "player";
        private static readonly ManualSkillAim Right = ManualSkillAim.Along(new WorldPoint2D(8, 0));
        private readonly List<Object> temporary = new List<Object>();
        private readonly List<FlameSwordWaveImpact> impacts = new List<FlameSwordWaveImpact>();
        private FlameSwordWavePattern pattern = null!;
        private RunState run = null!;
        private RunId runId;
        private TimeSpan now;
        private WeaponInstance first = null!;
        private WeaponInstance second = null!;
        private EquippedWeaponSelection selection = null!;
        private WeaponCatalog fixtureCatalog = null!;
        private WeaponActionLocks locks = null!;
        private ManualSkillExecutor executor = null!;
        private FlameSwordWaveEffect effect = null!;
        private WorldPoint2D sampledOrigin;
        private int reads;
        private IReadOnlyList<TargetCandidate> candidates = Array.Empty<TargetCandidate>();

        [SetUp]
        public void SetUp()
        {
            pattern = AssetDatabase.LoadAssetAtPath<FlameSwordWavePattern>(PatternPath);
            Assert.That(pattern, Is.Not.Null);
            now = TimeSpan.Zero;
            run = new RunState(() => now);
            runId = RunId.New();
            reads = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object asset in temporary)
                Object.DestroyImmediate(asset);
            temporary.Clear();
            impacts.Clear();
            candidates = Array.Empty<TargetCandidate>();
        }

        [Test]
        public void SavedFlameSwordIsTheOnlyDefinitionWithTheWaveAndFiresThroughTheExecutor()
        {
            WeaponDefinition definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Id.Value, Is.EqualTo("flame_sword"));
            Assert.That(definition.Grade, Is.EqualTo(2));
            Assert.That(definition.Kind, Is.EqualTo(WeaponKind.Sword));
            Assert.That(definition.PrototypeSkillPattern, Is.EqualTo("\uD654\uC5FC \uD30C\uB3D9"));
            Assert.That(definition.Skill, Is.SameAs(pattern));
            Assert.That(definition.BasicAttack, Is.Not.Null);
            var connectedWeapons = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { "Assets/ProjectWeapon/Weapons" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponDefinition>)
                .Where(weapon => weapon && weapon.Skill == pattern);
            Assert.That(connectedWeapons, Is.EqualTo(new[] { definition }));

            StartEquippedRun(definition, 20000);
            candidates = new[] { Candidate("target", 1, 0) };
            Assert.That(Activate(75, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(294), Is.False);
            Assert.That(Advance(295), Is.True);
            Assert.That(impacts.Single().Hits.Single().Amount, Is.EqualTo(38));
        }

        [Test]
        public void OneTelegraphedSectorUsesTheOriginalAimAndLiveLineOfSight()
        {
            StartFixture();
            Guid gesture = Guid.NewGuid();
            var origin = new WorldPoint2D(5f, 2f);
            candidates = new[] { Candidate("later-moved", 6, 2) };
            Assert.That(Activate(100, gesture, origin), Is.EqualTo(ManualSkillActivationStatus.Started));
            FlameSwordWave preview = effect.PendingWave!.Value;
            Assert.That(preview.RunId, Is.EqualTo(runId));
            Assert.That(preview.WeaponInstanceId, Is.EqualTo(first.Id));
            Assert.That(preview.ScheduledAt, Is.EqualTo(Ms(320)));
            Assert.That(preview.Area.Shape, Is.EqualTo(DamageAreaShape.Sector));
            Assert.That(preview.Area.Origin, Is.EqualTo(origin));
            Assert.That(preview.Area.Direction, Is.EqualTo(new WorldPoint2D(1, 0)));
            Assert.That(preview.Area.Radius, Is.EqualTo(4.5f));
            Assert.That(preview.Area.AngleDegrees, Is.EqualTo(65f));
            Assert.That(preview.Area.Amount, Is.EqualTo(38f));
            Assert.That(Advance(319), Is.False);
            Assert.That(impacts, Is.Empty);
            Assert.That(reads, Is.Zero);

            string[] included = { "new", "radius", "inside-angle", "other-side" };
            var healthSettings = Temporary<MeleeEnemySettings>();
            var healthFields = new SerializedObject(healthSettings);
            healthFields.FindProperty("maxHealth").floatValue = 100;
            healthFields.ApplyModifiedPropertiesWithoutUndo();
            var targets = included.ToDictionary(id => id, id => new MeleeEnemy(id, healthSettings));
            candidates = new[]
            {
                Candidate("new", 6, 2), Candidate("new", 6, 2), Candidate("radius", 9.5f, 2),
                Candidate("outside-radius", 9.5001f, 2), AtAngle("inside-angle", origin, 32.49),
                AtAngle("other-side", origin, -32.49), AtAngle("outside-angle", origin, 32.51),
                Candidate("later-moved", 6, 4), Candidate("behind", 4, 2), Candidate("side", 5, 3),
                Candidate("occluded", 6, 2, attackable: false), Candidate("dead", 6, 2, alive: false),
                Candidate("invalid", float.NaN, 2), Candidate(Attacker, 5, 2)
            };

            Assert.That(Advance(320), Is.True);
            FlameSwordWaveImpact impact = impacts.Single();
            Assert.That(sampledOrigin, Is.EqualTo(origin));
            Assert.That(impact.Wave.Area.ApplicationId, Is.EqualTo(gesture));
            Assert.That(impact.ObservedAt, Is.EqualTo(Ms(320)));
            Assert.That(impact.Hits.Select(hit => hit.TargetId), Is.EquivalentTo(included));
            foreach (DamageRequest hit in impact.Hits)
            {
                Assert.That(hit.ApplicationId, Is.EqualTo(gesture));
                Assert.That(hit.Amount, Is.EqualTo(38f));
                Assert.That(targets[hit.TargetId].ApplyDamage(hit, impact.ObservedAt), Is.True);
                Assert.That(targets[hit.TargetId].ApplyDamage(hit, impact.ObservedAt), Is.False);
            }
            Assert.That(targets.Values.Select(enemy => enemy.CurrentHealth), Is.All.EqualTo(62));
            Assert.That(effect.PendingWave, Is.Null);
            Assert.That(Advance(320), Is.False);
            Assert.That(Advance(321), Is.False);
            Assert.That(reads, Is.EqualTo(1));
        }

        [Test]
        public void RealExecutorPreservesTheSharedAndCapturedWeaponCooldownBoundaries()
        {
            StartFixture();
            Guid gesture = Guid.NewGuid();
            Assert.That(Activate(100, gesture), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(selection.TrySwitch(1, Ms(319)), Is.False);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, second.Id), Is.EqualTo(Ms(900)));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(Ms(6100)));
            Assert.That(Advance(320), Is.True);
            Assert.That(impacts.Single().Hits, Is.Empty);
            Assert.That(selection.TrySwitch(1, Ms(320)), Is.True);
            Assert.That(Activate(899, Guid.NewGuid(), weapon: second), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(900, Guid.NewGuid(), weapon: second), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(1120), Is.True);
            Assert.That(selection.TrySwitch(0, Ms(1120)), Is.True);
            Assert.That(Activate(6099, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(6100, gesture), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(Activate(6100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(6320), Is.True);
            Assert.That(impacts.Select(hit => hit.Wave.WeaponInstanceId), Is.EqualTo(new[] { first.Id, second.Id, first.Id }));
        }

        [Test]
        public void PreviouslyLaunchedWaveContinuesDuringChannelingAndNewActivationIsRejected()
        {
            StartFixture();
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            now = Ms(150);
            Assert.That(run.TryBeginExtraction(runId, isInRange: true), Is.Not.Null);
            Assert.That(Advance(320), Is.True);
            Assert.That(impacts, Has.Count.EqualTo(1));
            Assert.That(Activate(900, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Extracting));
        }

        [Test]
        public void LegalScheduledHitObservesLateBeforeTimeoutArbitration()
        {
            StartFixture(duration: 1000);
            candidates = new[] { Candidate("target", 1, 0) };
            Assert.That(Activate(779, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));

            Assert.That(Advance(1100), Is.True);
            FlameSwordWaveImpact impact = impacts.Single();
            Assert.That(impact.Wave.ScheduledAt, Is.EqualTo(Ms(999)));
            Assert.That(impact.ObservedAt, Is.EqualTo(Ms(1100)));
            Assert.That(impact.Hits.Count, Is.EqualTo(1));
            Assert.That(run.ProcessSignals(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(Advance(1100), Is.False);
        }

        [TestCase(780)]
        [TestCase(781)]
        public void HitScheduledAtOrAfterDeadlineCannotStartOrSpendCooldown(int activationTime)
        {
            StartFixture(duration: 1000);
            Assert.That(Activate(activationTime, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            Assert.That(effect.PendingWave, Is.Null);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(TimeSpan.Zero));
            Assert.That(Advance(1000), Is.False);
            Assert.That(impacts, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerminalResultCancelsUnobservedWave(bool replaceRun)
        {
            StartFixture();
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            now = Ms(200);
            Assert.That(run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, now) })?.Reason, Is.EqualTo(RunEndReason.Death));
            Assert.That(effect.PendingWave, Is.Null, "Terminal state must hide a stale preview before Advance.");
            if (replaceRun)
            {
                Assert.That(run.TryReturnToTown(runId), Is.True);
                Assert.That(run.TryStartRun(RunId.New(), 1, Ms(1000)), Is.True);
            }
            Assert.That(Advance(320), Is.False);
            Assert.That(effect.PendingWave, Is.Null);
            Assert.That(impacts, Is.Empty);
            Assert.That(reads, Is.Zero);
        }

        [Test]
        public void ReusedEffectAcceptsNewRunWithoutAdvancingTheAbandonedWave()
        {
            StartFixture();
            Guid previousGesture = Guid.NewGuid();
            Assert.That(Activate(100, previousGesture), Is.EqualTo(ManualSkillActivationStatus.Started));
            now = Ms(200);
            Assert.That(run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, now) })?.Reason, Is.EqualTo(RunEndReason.Death));
            Assert.That(effect.PendingWave, Is.Null);
            Assert.That(run.TryReturnToTown(runId), Is.True);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(1000)), Is.True);
            BindExecutor();

            Guid nextGesture = Guid.NewGuid();
            Assert.That(Activate(220, nextGesture), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.PendingWave?.RunId, Is.EqualTo(runId));
            Assert.That(Advance(320), Is.False);
            Assert.That(Advance(440), Is.True);
            Assert.That(impacts.Single().Wave.Area.ApplicationId, Is.EqualTo(nextGesture));
        }

        [Test]
        public void AlreadyCommittedSuccessCancelsUnobservedWave()
        {
            StartFixture();
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            now = Ms(150);
            ExtractionAttempt attempt = run.TryBeginExtraction(runId, isInRange: true)!;
            Assert.That(attempt, Is.Not.Null);
            now = attempt.CompletesAt;
            Assert.That(run.ProcessSignals(new[] { RunSignal.CompleteExtraction(runId, attempt.Id, now) })?.Succeeded, Is.True);
            Assert.That(effect.Advance(now), Is.False);
            Assert.That(effect.PendingWave, Is.Null);
            Assert.That(impacts, Is.Empty);
        }

        [TestCase("other_sword", 2, WeaponKind.Sword)]
        [TestCase("flame_sword", 1, WeaponKind.Sword)]
        [TestCase("flame_sword", 2, WeaponKind.Dagger)]
        public void WrongDefinitionCannotStartOrSpendCooldown(string id, int grade, WeaponKind kind)
        {
            StartFixture(id: id, grade: grade, kind: kind);
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            Assert.That(effect.PendingWave, Is.Null);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void InvalidDirectionAndTuningDoNotSpendCooldown()
        {
            FlameSwordWavePattern invalid = Object.Instantiate(pattern);
            temporary.Add(invalid);
            var fields = new SerializedObject(invalid);
            fields.FindProperty("windupMilliseconds").intValue = 0;
            fields.ApplyModifiedPropertiesWithoutUndo();
            StartFixture(skill: invalid);
            Assert.That(executor.TryActivate(Guid.NewGuid(), first.Id, Ms(100), default, ManualSkillAim.Along(default)),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Activate(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            Assert.That(effect.PendingWave, Is.Null);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(TimeSpan.Zero));
        }

        private void StartFixture(int duration = 20000, string id = "flame_sword", int grade = 2,
            WeaponKind kind = WeaponKind.Sword, FlameSwordWavePattern? skill = null)
        {
            WeaponDefinition definition = Temporary<WeaponDefinition>();
            var weaponFields = new SerializedObject(definition);
            weaponFields.FindProperty("definitionId").stringValue = id;
            weaponFields.FindProperty("displayName").stringValue = "Flame Sword Test";
            weaponFields.FindProperty("grade").intValue = grade;
            weaponFields.FindProperty("kind").enumValueIndex = (int)kind;
            weaponFields.FindProperty("skill").objectReferenceValue = skill ? skill : pattern;
            weaponFields.ApplyModifiedPropertiesWithoutUndo();
            StartEquippedRun(definition, duration);
        }

        private void StartEquippedRun(WeaponDefinition definition, int duration)
        {
            fixtureCatalog = Temporary<WeaponCatalog>();
            var catalogFields = new SerializedObject(fixtureCatalog);
            SerializedProperty definitions = catalogFields.FindProperty("definitions");
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = definition;
            catalogFields.ApplyModifiedPropertiesWithoutUndo();

            first = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            second = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            Assert.That(run.TryStartRun(runId, 1, Ms(duration)), Is.True);
            effect = new FlameSwordWaveEffect(run, origin =>
            {
                reads++;
                sampledOrigin = origin;
                return candidates;
            }, impacts.Add);
            BindExecutor();
        }

        private void BindExecutor()
        {
            locks = new WeaponActionLocks();
            selection = new EquippedWeaponSelection(run, new InventoryService(new[] { first, second }), fixtureCatalog,
                () => effect.PendingWave.HasValue, locks);
            executor = new ManualSkillExecutor(run, selection, effect, () => false, _ => null, Attacker);
        }

        private ManualSkillActivationStatus Activate(int milliseconds, Guid gesture, WorldPoint2D origin = default,
            WeaponInstance? weapon = null)
        {
            now = Ms(milliseconds);
            return executor.TryActivate(gesture, (weapon ?? first).Id, now, origin, Right);
        }

        private bool Advance(int milliseconds)
        {
            now = Ms(milliseconds);
            return effect.Advance(now);
        }

        private T Temporary<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            temporary.Add(asset);
            return asset;
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

        private static TargetCandidate Candidate(string id, float x, float y, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(x, y), alive, attackable);

        private static TargetCandidate AtAngle(string id, WorldPoint2D origin, double degrees)
        {
            double radians = degrees * Math.PI / 180d;
            return Candidate(id, origin.X + (float)Math.Cos(radians), origin.Y + (float)Math.Sin(radians));
        }
    }
}
