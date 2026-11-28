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

namespace ProjectWeapon.MaceSkill.Tests
{
    public sealed class MaceShockwaveTests
    {
        private const string Starter = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private const string Attacker = "player";
        private static readonly WeaponDefinitionId Mace = new WeaponDefinitionId("mace");
        private static readonly ManualSkillAim Self = ManualSkillAim.AtSelf();
        private static readonly WorldPoint2D Origin = new WorldPoint2D(1f, 0f);

        private readonly List<Object> created = new List<Object>();
        private readonly List<MaceShockwaveStrike> strikes = new List<MaceShockwaveStrike>();
        private WeaponCatalog catalog = null!;
        private RunState run = null!;
        private RunId runId;
        private WeaponInstance first = null!;
        private WeaponInstance second = null!;
        private EquippedWeaponSelection selection = null!;
        private WeaponActionLocks locks = null!;
        private ManualSkillExecutor executor = null!;
        private TimeSpan now;
        private IReadOnlyList<TargetCandidate> candidates = Array.Empty<TargetCandidate>();

        [SetUp]
        public void SetUp()
        {
            catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Starter + "PrototypeStarterWeaponCatalog.asset");
            now = TimeSpan.Zero;
            runId = RunId.New();
            run = new RunState(() => now);
            Assert.That(run.TryStartRun(runId, 1, Ms(15000)), Is.True);
            Bind(catalog, Mace, strikes.Add);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created)
                Object.DestroyImmediate(item);

            created.Clear();
            strikes.Clear();
            candidates = Array.Empty<TargetCandidate>();
        }

        [Test]
        public void ActualGradeOneCatalogMaceHasOneSelfShockwaveAndRetainsItsBasicAttack()
        {
            WeaponDefinition mace = catalog.CreateLookup()[Mace];
            var pattern = AssetDatabase.LoadAssetAtPath<MaceShockwavePattern>(Starter + "MaceShockwave.asset");
            Assert.That(pattern, Is.Not.Null);
            Assert.That(mace.Grade, Is.EqualTo(1));
            Assert.That(mace.Kind, Is.EqualTo(WeaponKind.Mace));
            Assert.That(mace.Skill, Is.SameAs(pattern));
            Assert.That(catalog.CreateLookup().Values.Single(item => item.Skill is MaceShockwavePattern), Is.SameAs(mace));
            Assert.That(pattern.AimKind, Is.EqualTo(ManualSkillAimKind.Self));
            Assert.That(mace.BasicAttack, Is.SameAs(AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                Starter + "MaceCircularStrike.asset")));
        }

        [Test]
        public void SuccessfulSelfCastHitsEveryDirectionAndExactRadiusOnceAtTheCastTime()
        {
            var settings = ScriptableObject.CreateInstance<MeleeEnemySettings>();
            created.Add(settings);
            var health = new SerializedObject(settings);
            health.FindProperty("maxHealth").floatValue = 70f;
            health.ApplyModifiedPropertiesWithoutUndo();
            string[] included = { "west", "east", "north", "south", "overlapping" };
            var foes = included.ToDictionary(id => id, id => new MeleeEnemy(id, settings));
            var applicationTimes = new List<TimeSpan>();
            foreach (MeleeEnemy foe in foes.Values)
                foe.DamageApplied += (_, time) => applicationTimes.Add(time);

            Bind(catalog, Mace, strike =>
            {
                strikes.Add(strike);
                foreach (DamageRequest hit in strike.Hits)
                {
                    MeleeEnemy foe = foes[hit.TargetId];
                    Assert.That(foe.ApplyDamage(hit, strike.EffectiveAt), Is.True);
                    Assert.That(foe.ApplyDamage(hit, strike.EffectiveAt), Is.False);
                }
            });
            TargetCandidate east = Candidate("east", 2.5f, 0f);
            candidates = new[]
            {
                east, east, Candidate("west", -0.5f, 0f), Candidate("north", 1f, 1.9f),
                Candidate("south", 1f, -1.9f), Candidate("overlapping", 1f, 0f),
                Candidate("past-radius", 1f, 1.9001f), Candidate("diagonal-outside", 2.5f, 1.5f),
                Candidate(Attacker, 1f, 0f), Candidate("dead", 1f, 1f, alive: false),
                Candidate("blocked", 1f, 1f, attackable: false), Candidate("invalid", 1f, float.NaN)
            };
            Guid gesture = Guid.NewGuid();
            Assert.That(Activate(125, gesture, first, Self), Is.EqualTo(ManualSkillActivationStatus.Started));

            MaceShockwaveStrike strike = strikes.Single();
            Assert.That(strike.RunId, Is.EqualTo(runId));
            Assert.That(strike.WeaponInstanceId, Is.EqualTo(first.Id));
            Assert.That(strike.Area.Shape, Is.EqualTo(DamageAreaShape.Circle));
            Assert.That(strike.Area.Origin, Is.EqualTo(Origin));
            Assert.That(strike.Area.Radius, Is.EqualTo(1.9f));
            Assert.That(strike.Area.ApplicationId, Is.EqualTo(gesture));
            Assert.That(strike.Hits.Select(hit => hit.TargetId), Is.EquivalentTo(included));
            Assert.That(strike.Hits.All(hit => hit.ApplicationId == gesture && hit.AttackerId == Attacker
                && hit.Amount == 30f), Is.True);
            Assert.That(foes.Values.Select(foe => foe.CurrentHealth), Is.All.EqualTo(40f));
            Assert.That(applicationTimes, Is.EqualTo(Enumerable.Repeat(Ms(125), included.Length)));
        }

        [Test]
        public void CommonAndInstanceCooldownsAndDuplicateGestureApplyAtExactBoundaries()
        {
            Guid initial = Guid.NewGuid();
            Guid nextInstance = Guid.NewGuid();
            Guid reuseFirst = Guid.NewGuid();
            Assert.That(Activate(100, initial, first, Self), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(Ms(5600)));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, second.Id), Is.EqualTo(Ms(900)));
            Assert.That(selection.TrySwitch(1, Ms(101)), Is.True);
            Assert.That(Activate(899, Guid.NewGuid(), second, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(900, nextInstance, second, Self), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(selection.TrySwitch(0, Ms(901)), Is.True);
            Assert.That(Activate(5599, Guid.NewGuid(), first, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(5600, initial, first, Self), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(Activate(5600, reuseFirst, first, Self), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(strikes.Select(strike => strike.Area.ApplicationId),
                Is.EqualTo(new[] { initial, nextInstance, reuseFirst }));
            Assert.That(strikes.Select(strike => strike.WeaponInstanceId), Is.EqualTo(new[] { first.Id, second.Id, first.Id }));
            Assert.That(strikes.All(strike => strike.Hits.Count == 0), Is.True);
        }

        [Test]
        public void WrongAimDeadlineAndReplacedRunDoNotApplyDamageOrConsumeCooldown()
        {
            Assert.That(Activate(100, Guid.NewGuid(), first, ManualSkillAim.Along(new WorldPoint2D(1f, 0f))),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Activate(15000, Guid.NewGuid(), first, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(run.ProcessSignals(Array.Empty<RunSignal>())!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(run.TryReturnToTown(runId), Is.True);
            Assert.That(run.TryStartRun(RunId.New(), 1, Ms(15000)), Is.True);
            Assert.That(Activate(15001, Guid.NewGuid(), first, Self), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(strikes, Is.Empty);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(TimeSpan.Zero));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void InvalidRadiusOrWrongDefinitionCannotUseTheShockwave(bool wrongDefinition)
        {
            WeaponDefinition definition = Copy(catalog.CreateLookup()[Mace]);
            var weapon = new SerializedObject(definition);
            if (wrongDefinition)
                weapon.FindProperty("definitionId").stringValue = "misbound_shockwave";
            else
            {
                var skill = Copy((MaceShockwavePattern)definition.Skill);
                var radius = new SerializedObject(skill);
                radius.FindProperty("radius").floatValue = 0f;
                radius.ApplyModifiedPropertiesWithoutUndo();
                weapon.FindProperty("skill").objectReferenceValue = skill;
            }

            weapon.ApplyModifiedPropertiesWithoutUndo();
            WeaponCatalog isolated = Copy(catalog);
            var entries = new SerializedObject(isolated);
            SerializedProperty definitions = entries.FindProperty("definitions");
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = definition;
            entries.ApplyModifiedPropertiesWithoutUndo();
            Bind(isolated, definition.Id, strikes.Add);

            Assert.That(Activate(100, Guid.NewGuid(), first, Self), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            Assert.That(strikes, Is.Empty);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, first.Id), Is.EqualTo(TimeSpan.Zero));
        }

        private void Bind(WeaponCatalog equippedCatalog, WeaponDefinitionId id, Action<MaceShockwaveStrike> apply)
        {
            first = WeaponInstance.Create(id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            second = WeaponInstance.Create(id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            locks = new WeaponActionLocks();
            selection = new EquippedWeaponSelection(run, new InventoryService(new[] { first, second }), equippedCatalog,
                () => false, locks);
            executor = new ManualSkillExecutor(run, selection, new MaceShockwaveEffect(() => candidates, apply),
                () => false, _ => null, Attacker);
        }

        private ManualSkillActivationStatus Activate(int milliseconds, Guid gesture, WeaponInstance weapon, ManualSkillAim aim)
        {
            now = Ms(milliseconds);
            return executor.TryActivate(gesture, weapon.Id, now, Origin, aim);
        }

        private T Copy<T>(T asset) where T : Object
        {
            T copy = Object.Instantiate(asset);
            created.Add(copy);
            return copy;
        }

        private static TimeSpan Ms(int time) => TimeSpan.FromMilliseconds(time);
        private static TargetCandidate Candidate(string id, float x, float y, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(x, y), alive, attackable);
    }
}
