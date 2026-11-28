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

namespace ProjectWeapon.OldSwordSkill.Tests
{
    public sealed class OldSwordWideSlashTests
    {
        private const string Starter = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private const string Attacker = "player";
        private static readonly WeaponDefinitionId OldSword = new WeaponDefinitionId("old_sword");
        private static readonly ManualSkillAim Right = ManualSkillAim.Along(new WorldPoint2D(8f, 0f));

        private readonly List<Object> temporary = new List<Object>();
        private readonly List<OldSwordWideSlashStrike> strikes = new List<OldSwordWideSlashStrike>();
        private WeaponCatalog catalog = null!;
        private WeaponDefinition sword = null!;
        private RunState run = null!;
        private RunId runId;
        private WeaponInstance weapon = null!;
        private WeaponActionLocks locks = null!;
        private ManualSkillExecutor executor = null!;
        private TimeSpan now;
        private IReadOnlyList<TargetCandidate> candidates = Array.Empty<TargetCandidate>();

        [SetUp]
        public void SetUp()
        {
            catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Starter + "PrototypeStarterWeaponCatalog.asset");
            sword = catalog.CreateLookup()[OldSword];
            now = TimeSpan.Zero;
            runId = RunId.New();
            run = new RunState(() => now);
            Assert.That(run.TryStartRun(runId, 1, Ms(20000)), Is.True);
            Bind(catalog, sword.Id, strikes.Add);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object copy in temporary)
                Object.DestroyImmediate(copy);

            temporary.Clear();
            strikes.Clear();
            candidates = Array.Empty<TargetCandidate>();
        }

        [Test]
        public void StarterCatalogBindsTheWideSlashOnlyToItsGradeOneOldSword()
        {
            var pattern = AssetDatabase.LoadAssetAtPath<OldSwordWideSlashPattern>(Starter + "OldSwordWideSlash.asset");
            Assert.That(pattern, Is.Not.Null);
            Assert.That(sword, Is.SameAs(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Starter + "OldSword.asset")));
            Assert.That(sword.Grade, Is.EqualTo(1));
            Assert.That(sword.Kind, Is.EqualTo(WeaponKind.Sword));
            Assert.That(sword.Skill, Is.SameAs(pattern));
            Assert.That(catalog.CreateLookup().Values.Single(item => item.Skill is OldSwordWideSlashPattern), Is.SameAs(sword));
            Assert.That(pattern.AimKind, Is.EqualTo(ManualSkillAimKind.Direction));
        }

        [Test]
        public void AuthenticDirectionEmitsOneImmediateSectorAndAppliesTypedDamageOncePerEligibleTarget()
        {
            var settings = ScriptableObject.CreateInstance<MeleeEnemySettings>();
            temporary.Add(settings);
            var health = new SerializedObject(settings);
            health.FindProperty("maxHealth").floatValue = 100f;
            health.ApplyModifiedPropertiesWithoutUndo();

            string[] included = { "direction", "edge-up", "edge-down", "radius", "at-origin" };
            var foes = included.ToDictionary(id => id, id => new MeleeEnemy(id, settings));
            var applied = new List<TimeSpan>();
            foreach (MeleeEnemy foe in foes.Values)
                foe.DamageApplied += (_, at) => applied.Add(at);

            Bind(catalog, sword.Id, strike =>
            {
                strikes.Add(strike);
                foreach (DamageRequest damage in strike.Hits)
                {
                    MeleeEnemy foe = foes[damage.TargetId];
                    Assert.That(foe.ApplyDamage(damage, strike.EffectiveAt), Is.True);
                    Assert.That(foe.ApplyDamage(damage, strike.EffectiveAt), Is.False);
                }
            });
            TargetCandidate nearest = Candidate("direction", 0.4f, 0f);
            candidates = new[]
            {
                nearest, nearest, Candidate("radius", 2.1f, 0f), Candidate("past-radius", 2.1001f, 0f),
                AtAngle("edge-up", 75), AtAngle("edge-down", -75), AtAngle("past-up", 75.01),
                AtAngle("past-down", -75.01), Candidate("side", 0f, 1f), Candidate("behind", -0.2f, 0f),
                Candidate("at-origin", 0f, 0f), Candidate(Attacker, 0f, 0f),
                Candidate("dead", 0.3f, 0f, alive: false), Candidate("blocked", 0.4f, 0f, attackable: false),
                Candidate("invalid", float.NaN, 0f)
            };

            Guid gesture = Guid.NewGuid();
            Assert.That(Activate(110, gesture, Right), Is.EqualTo(ManualSkillActivationStatus.Started));
            OldSwordWideSlashStrike swing = strikes.Single();
            Assert.That(swing.RunId, Is.EqualTo(runId));
            Assert.That(swing.WeaponInstanceId, Is.EqualTo(weapon.Id));
            Assert.That(swing.EffectiveAt, Is.EqualTo(Ms(110)));
            Assert.That(swing.Area.Shape, Is.EqualTo(DamageAreaShape.Sector));
            Assert.That(swing.Area.Radius, Is.EqualTo(2.1f));
            Assert.That(swing.Area.AngleDegrees, Is.EqualTo(150f));
            Assert.That(swing.Area.Direction.X, Is.EqualTo(1f));
            Assert.That(swing.Hits.Select(hit => hit.TargetId), Is.EquivalentTo(included));
            Assert.That(swing.Hits.All(hit => hit.ApplicationId == gesture && hit.AttackerId == Attacker
                && hit.Amount == 26f), Is.True);
            Assert.That(foes.Values.Select(foe => foe.CurrentHealth), Is.All.EqualTo(74f));
            Assert.That(applied, Is.EqualTo(Enumerable.Repeat(Ms(110), included.Length)));
        }

        [Test]
        public void AimUsesTheCastOriginAndCanPointAwayFromTheNearestTarget()
        {
            candidates = new[] { Candidate("nearer", 4.9f, 2f), Candidate("aimed", 5f, 3.2f) };
            var up = ManualSkillAim.Along(new WorldPoint2D(0f, 50f));
            Assert.That(Activate(300, Guid.NewGuid(), up, new WorldPoint2D(5f, 2f)),
                Is.EqualTo(ManualSkillActivationStatus.Started));
            OldSwordWideSlashStrike strike = strikes.Single();
            Assert.That(strike.Area.Origin.X, Is.EqualTo(5f));
            Assert.That(strike.Area.Origin.Y, Is.EqualTo(2f));
            Assert.That(strike.Hits.Single().TargetId, Is.EqualTo("aimed"));
        }

        [Test]
        public void DuplicateAndFiveSecondCooldownAllowOnlyOneWavePerAcceptedGesture()
        {
            candidates = new[] { Candidate("target", 1f, 0f) };
            Guid first = Guid.NewGuid();
            Assert.That(Activate(100, first, Right), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, weapon.Id), Is.EqualTo(Ms(5100)));
            Assert.That(Activate(5100, first, Right), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(Activate(5099, Guid.NewGuid(), Right), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Guid second = Guid.NewGuid();
            Assert.That(Activate(5100, second, Right), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(strikes.Select(item => item.Area.ApplicationId), Is.EqualTo(new[] { first, second }));
            Assert.That(strikes.Select(item => item.EffectiveAt), Is.EqualTo(new[] { Ms(100), Ms(5100) }));
        }

        [Test]
        public void InvalidOrCanceledAimPreservesCooldownAndValidEmptySlashStillStarts()
        {
            Assert.That(Activate(100, Guid.NewGuid(), ManualSkillAim.AtSelf()),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Activate(100, Guid.NewGuid(), ManualSkillAim.Along(default)),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Activate(100, Guid.NewGuid(), ManualSkillAim.Along(new WorldPoint2D(float.NaN, 1f))),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Guid canceled = Guid.NewGuid();
            Assert.That(executor.Cancel(canceled), Is.EqualTo(ManualSkillActivationStatus.Canceled));
            Assert.That(Activate(100, canceled, Right), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(strikes, Is.Empty);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, weapon.Id), Is.EqualTo(TimeSpan.Zero));
            Assert.That(Activate(100, Guid.NewGuid(), Right), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(strikes.Single().Hits, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MisboundDefinitionOrInvalidDamageNeverStartsOrSpendsCooldown(bool changeIdentity)
        {
            WeaponCatalog isolatedCatalog = Copy(catalog);
            WeaponDefinition isolatedWeapon = Copy(sword);
            var fields = new SerializedObject(isolatedWeapon);
            WeaponDefinitionId definition = OldSword;
            if (changeIdentity)
            {
                definition = new WeaponDefinitionId("wrong_skill_definition");
                fields.FindProperty("definitionId").stringValue = definition.Value;
            }
            else
            {
                OldSwordWideSlashPattern invalid = Copy((OldSwordWideSlashPattern)sword.Skill);
                var pattern = new SerializedObject(invalid);
                pattern.FindProperty("damage").floatValue = 0f;
                pattern.ApplyModifiedPropertiesWithoutUndo();
                fields.FindProperty("skill").objectReferenceValue = invalid;
            }
            fields.ApplyModifiedPropertiesWithoutUndo();
            var entries = new SerializedObject(isolatedCatalog);
            SerializedProperty definitions = entries.FindProperty("definitions");
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = isolatedWeapon;
            entries.ApplyModifiedPropertiesWithoutUndo();
            Bind(isolatedCatalog, definition, strikes.Add);

            Assert.That(Activate(100, Guid.NewGuid(), Right), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            Assert.That(strikes, Is.Empty);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, weapon.Id), Is.EqualTo(TimeSpan.Zero));
        }

        private void Bind(WeaponCatalog equippedCatalog, WeaponDefinitionId id, Action<OldSwordWideSlashStrike> onStrike)
        {
            weapon = WeaponInstance.Create(id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            var inventory = new InventoryService(new[] { weapon });
            locks = new WeaponActionLocks();
            var selection = new EquippedWeaponSelection(run, inventory, equippedCatalog, () => false, locks);
            executor = new ManualSkillExecutor(run, selection, new OldSwordWideSlashEffect(() => candidates, onStrike),
                () => false, _ => null, Attacker);
        }

        private ManualSkillActivationStatus Activate(int milliseconds, Guid gesture, ManualSkillAim aim, WorldPoint2D origin = default)
        {
            now = Ms(milliseconds);
            return executor.TryActivate(gesture, weapon.Id, now, origin, aim);
        }

        private T Copy<T>(T asset) where T : Object
        {
            T copy = Object.Instantiate(asset);
            temporary.Add(copy);
            return copy;
        }

        private static TimeSpan Ms(int time) => TimeSpan.FromMilliseconds(time);

        private static TargetCandidate AtAngle(string id, double degrees)
        {
            double radians = degrees * Math.PI / 180d;
            return Candidate(id, (float)Math.Cos(radians), (float)Math.Sin(radians));
        }

        private static TargetCandidate Candidate(string id, float x, float y, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(x, y), alive, attackable);
    }
}
