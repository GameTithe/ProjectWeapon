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

namespace ProjectWeapon.DaggerSkill.Tests
{
    public sealed class DaggerDoubleSlashTests
    {
        private const string Starter = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private const string Attacker = "player";
        private static readonly WeaponDefinitionId Dagger = new WeaponDefinitionId("dagger");
        private static readonly ManualSkillAim Right = ManualSkillAim.Along(new WorldPoint2D(2f, 0f));

        private readonly List<DaggerDoubleSlashStrike> strikes = new List<DaggerDoubleSlashStrike>();
        private WeaponCatalog catalog = null!;
        private RunState run = null!;
        private RunId runId;
        private WeaponInstance first = null!;
        private WeaponInstance second = null!;
        private EquippedWeaponSelection selection = null!;
        private WeaponActionLocks locks = null!;
        private DaggerDoubleSlashEffect effect = null!;
        private ManualSkillExecutor executor = null!;
        private TimeSpan now;
        private IReadOnlyList<TargetCandidate> candidates = Array.Empty<TargetCandidate>();

        [SetUp]
        public void SetUp()
        {
            strikes.Clear();
            candidates = Array.Empty<TargetCandidate>();
            catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Starter + "PrototypeStarterWeaponCatalog.asset");
            now = TimeSpan.Zero;
            runId = RunId.New();
            run = new RunState(() => now);
            Assert.That(run.TryStartRun(runId, 1, Ms(10000)), Is.True);
            first = WeaponInstance.Create(Dagger, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            second = WeaponInstance.Create(Dagger, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            Bind(strikes.Add);
        }

        [Test]
        public void ActualGradeOneDaggerLinksOnlyTheDirectionalDoubleSlash()
        {
            WeaponDefinition dagger = catalog.CreateLookup()[Dagger];
            var pattern = AssetDatabase.LoadAssetAtPath<DaggerDoubleSlashPattern>(Starter + "DaggerDoubleSlash.asset");
            Assert.That(pattern, Is.Not.Null);
            Assert.That(dagger.Grade, Is.EqualTo(1));
            Assert.That(dagger.Kind, Is.EqualTo(WeaponKind.Dagger));
            Assert.That(dagger.Skill, Is.SameAs(pattern));
            Assert.That(catalog.CreateLookup().Values.Single(item => item.Skill is DaggerDoubleSlashPattern),
                Is.SameAs(dagger));
            Assert.That(pattern.AimKind, Is.EqualTo(ManualSkillAimKind.Direction));
        }

        [Test]
        public void TwoWavesUseLockedCastAndDistinctDamageIdsWithCurrentTargets()
        {
            var settings = ScriptableObject.CreateInstance<MeleeEnemySettings>();
            try
            {
                var health = new SerializedObject(settings);
                health.FindProperty("maxHealth").floatValue = 60f;
                health.ApplyModifiedPropertiesWithoutUndo();
                string[] ids = { "both", "leaves", "enters", "angle-edge" };
                var foes = ids.ToDictionary(id => id, id => new MeleeEnemy(id, settings));
                Bind(strike =>
                {
                    strikes.Add(strike);
                    foreach (DamageRequest hit in strike.Hits)
                    {
                        MeleeEnemy foe = foes[hit.TargetId];
                        Assert.That(hit.Amount, Is.EqualTo(17f));
                        Assert.That(foe.ApplyDamage(hit, strike.EffectiveAt), Is.True);
                        Assert.That(foe.ApplyDamage(hit, strike.EffectiveAt), Is.False);
                    }
                });
                TargetCandidate boundary = Candidate("both", 1.2f, 0f);
                candidates = new[]
                {
                    boundary, boundary, Candidate("leaves", 1f, 0f), Candidate("angle-edge", 0.5f, 0.5f),
                    Candidate("past-radius", 1.2001f, 0f), Candidate("past-angle", 0.5f, 0.5001f),
                    Candidate("enters", -0.1f, 0f), Candidate(Attacker, 0f, 0f),
                    Candidate("dead", 0.3f, 0f, alive: false), Candidate("blocked", 0.4f, 0f, attackable: false)
                };

                Guid gesture = Guid.NewGuid();
                Assert.That(Activate(200, gesture, first, Right), Is.EqualTo(ManualSkillActivationStatus.Started));
                Assert.That(strikes.Single().Hits.Select(hit => hit.TargetId),
                    Is.EquivalentTo(new[] { "both", "leaves", "angle-edge" }));
                Assert.That(selection.TrySwitch(1, Ms(250)), Is.True);
                candidates = new[]
                {
                    boundary, Candidate("leaves", 1.201f, 0f), Candidate("enters", 0.7f, 0f),
                    Candidate("angle-edge", 0.5f, 0.5f, alive: false)
                };
                Advance(309);
                Assert.That(strikes, Has.Count.EqualTo(1));
                Advance(310);
                Advance(311);
                Assert.That(strikes, Has.Count.EqualTo(2));
                Assert.That(strikes.Select(strike => strike.StrikeIndex), Is.EqualTo(new[] { 0, 1 }));
                Assert.That(strikes.Select(strike => strike.EffectiveAt), Is.EqualTo(new[] { Ms(200), Ms(310) }));
                Assert.That(strikes.All(strike => strike.RunId == runId && strike.WeaponInstanceId == first.Id
                    && strike.GestureId == gesture && strike.Area.Origin.X == 0f && strike.Area.Direction.X == 1f
                    && strike.Area.Radius == 1.2f && strike.Area.AngleDegrees == 90f), Is.True);
                Assert.That(strikes[1].Area.ApplicationId, Is.Not.EqualTo(strikes[0].Area.ApplicationId));
                Assert.That(strikes[1].Hits.Select(hit => hit.TargetId), Is.EquivalentTo(new[] { "both", "enters" }));
                Assert.That(foes["both"].CurrentHealth, Is.EqualTo(26f));
                Assert.That(foes["leaves"].CurrentHealth, Is.EqualTo(43f));
                Assert.That(foes["enters"].CurrentHealth, Is.EqualTo(43f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void LateFramePreservesFollowUpTimestampAndAcceptedGestureCannotReplay()
        {
            Guid gesture = Guid.NewGuid();
            Assert.That(Activate(100, gesture, first, Right), Is.EqualTo(ManualSkillActivationStatus.Started));
            Advance(250);
            Assert.That(strikes[1].EffectiveAt, Is.EqualTo(Ms(210)));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(Ms(4900)));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, second.Id), Is.EqualTo(Ms(900)));
            Assert.That(Activate(4899, Guid.NewGuid(), first, Right), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(4900, gesture, first, Right), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(strikes, Has.Count.EqualTo(2));
            Assert.That(Activate(4900, Guid.NewGuid(), first, Right), Is.EqualTo(ManualSkillActivationStatus.Started));
        }

        [TestCase(9800, 10000)]
        [TestCase(9890, 10001)]
        public void FollowUpAtDeadlineOrFirstObservedAfterDeadlineIsDiscarded(int castAt, int advanceAt)
        {
            Assert.That(Activate(castAt, Guid.NewGuid(), first, Right), Is.EqualTo(ManualSkillActivationStatus.Started));
            Advance(advanceAt);
            Assert.That(strikes, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerminalAndReplacedRunBothDiscardPendingFollowUp(bool startNext)
        {
            Assert.That(Activate(100, Guid.NewGuid(), first, Right), Is.EqualTo(ManualSkillActivationStatus.Started));
            now = Ms(120);
            Assert.That(run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, now) })!.Reason,
                Is.EqualTo(RunEndReason.Death));
            if (startNext)
            {
                Assert.That(run.TryReturnToTown(runId), Is.True);
                Assert.That(run.TryStartRun(RunId.New(), 1, Ms(10000)), Is.True);
            }

            Advance(250);
            Assert.That(strikes, Has.Count.EqualTo(1));
        }

        [Test]
        public void InvalidAimAndCanceledGestureDoNotScheduleDamageOrSpendCooldown()
        {
            Assert.That(Activate(100, Guid.NewGuid(), first, ManualSkillAim.AtSelf()),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Activate(100, Guid.NewGuid(), first, ManualSkillAim.Along(default)),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Guid canceled = Guid.NewGuid();
            Assert.That(executor.Cancel(canceled), Is.EqualTo(ManualSkillActivationStatus.Canceled));
            Assert.That(Activate(100, canceled, first, Right), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Advance(300);
            Assert.That(strikes, Is.Empty);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(TimeSpan.Zero));
        }

        private void Bind(Action<DaggerDoubleSlashStrike> apply)
        {
            locks = new WeaponActionLocks();
            selection = new EquippedWeaponSelection(run, new InventoryService(new[] { first, second }), catalog,
                () => false, locks);
            effect = new DaggerDoubleSlashEffect(run, () => candidates, apply);
            executor = new ManualSkillExecutor(run, selection, effect, () => false, _ => null, Attacker);
        }

        private ManualSkillActivationStatus Activate(int milliseconds, Guid gesture, WeaponInstance weapon, ManualSkillAim aim)
        {
            now = Ms(milliseconds);
            return executor.TryActivate(gesture, weapon.Id, now, default, aim);
        }

        private void Advance(int milliseconds)
        {
            now = Ms(milliseconds);
            effect.Advance(now);
        }

        private static TimeSpan Ms(int time) => TimeSpan.FromMilliseconds(time);

        private static TargetCandidate Candidate(string id, float x, float y, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(x, y), alive, attackable);
    }
}
