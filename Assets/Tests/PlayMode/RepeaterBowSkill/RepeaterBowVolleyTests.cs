#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectWeapon.RepeaterBowSkill.Tests
{
    public sealed class RepeaterBowVolleyTests
    {
        private const string AssetPath = "Assets/ProjectWeapon/RepeaterBowSkill/RepeaterBowVolley.asset";
        private const string Player = "repeater-player";
        private const int TargetLayer = 8;
        private const int WallLayer = 9;
        private static readonly ManualSkillAim Right = ManualSkillAim.Along(new WorldPoint2D(5, 0));

        private readonly List<Object> assets = new List<Object>();
        private readonly List<RepeaterBowArrowImpact> impacts = new List<RepeaterBowArrowImpact>();
        private readonly Dictionary<Collider2D, TargetCandidate> targets = new Dictionary<Collider2D, TargetCandidate>();
        private Scene scene;
        private PhysicsScene2D physics;
        private RunState run = null!;
        private RunId runId;
        private TimeSpan now;
        private RepeaterBowVolleyPattern pattern = null!;
        private RepeaterBowVolleyEffect effect = null!;
        private ManualSkillExecutor executor = null!;
        private EquippedWeaponSelection selection = null!;
        private WeaponActionLocks locks = null!;
        private WeaponInstance primary = null!;
        private WeaponInstance secondary = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = SceneManager.CreateScene("KAN-144-" + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physics = scene.GetPhysicsScene2D();
            Assert.That(physics.IsValid(), Is.True);
            pattern = AssetDatabase.LoadAssetAtPath<RepeaterBowVolleyPattern>(AssetPath);
            Assert.That(pattern, Is.Not.Null);
            now = TimeSpan.Zero;
            run = new RunState(() => now);
            runId = RunId.New();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
            foreach (Object asset in assets)
                Object.Destroy(asset);
            assets.Clear();
            impacts.Clear();
            targets.Clear();
        }

        [Test]
        public void OneAcceptedDirectionStartsFiveIndependentArrowsAndKeepsBothSkillLocks()
        {
            Bind();
            Guid gesture = Guid.NewGuid();
            var origin = new WorldPoint2D(2, -1);
            Assert.That(Cast(100, gesture, origin), Is.EqualTo(ManualSkillActivationStatus.Started));

            IReadOnlyList<RepeaterBowArrowSnapshot> arrows = effect.GetActiveArrows();
            Assert.That(arrows.Count, Is.EqualTo(5));
            Assert.That(arrows.Select(arrow => arrow.ApplicationId).Distinct().Count(), Is.EqualTo(5));
            Assert.That(arrows.Select(arrow => arrow.Position), Is.All.EqualTo(origin));
            Assert.That(arrows.Select(arrow => arrow.LaunchedAt), Is.All.EqualTo(Ms(100)));
            double[] expectedAngles = { -20, -10, 0, 10, 20 };
            for (int i = 0; i < arrows.Count; i++)
            {
                WorldPoint2D velocity = arrows[i].Velocity;
                Assert.That(Math.Atan2(velocity.Y, velocity.X) * 180d / Math.PI, Is.EqualTo(expectedAngles[i]).Within(.0001));
                Assert.That(Math.Sqrt(velocity.SquaredDistanceTo(default)), Is.EqualTo(13d).Within(.0001));
                Assert.That(arrows[i].Lifetime.TotalSeconds * 13d, Is.EqualTo(6.8d).Within(.00001));
            }

            Assert.That(Cast(100, gesture), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(effect.GetActiveArrows().Count, Is.EqualTo(5));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, secondary.Id), Is.EqualTo(Ms(900)));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, primary.Id), Is.EqualTo(Ms(6100)));
            Assert.That(selection.TrySwitch(1, Ms(200)), Is.True);
            Assert.That(Cast(899, Guid.NewGuid(), weapon: secondary), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Advance(899);
            Assert.That(effect.GetActiveArrows(), Is.Empty);

            Assert.That(Cast(900, Guid.NewGuid(), weapon: secondary), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.GetActiveArrows().Count, Is.EqualTo(5));
            Advance(1500);
            Assert.That(selection.TrySwitch(0, Ms(1500)), Is.True);
            Assert.That(Cast(6099, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Cast(6100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
        }

        [Test]
        public void SameTargetCanTakeAllFiveDistinctContactsButNoArrowHitsTwice()
        {
            Bind();
            Target("broad", new Vector2(2, 0), new Vector2(.1f, 4));
            Target("broad", new Vector2(2, 0), new Vector2(.1f, 4));
            Target("behind", new Vector2(3, 0), new Vector2(.1f, 5));
            SyncPhysics();
            Assert.That(Cast(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(selection.TrySwitch(1, Ms(150)), Is.True);
            Advance(400);
            Advance(400);
            Advance(500);

            Assert.That(impacts.Count, Is.EqualTo(5));
            Assert.That(impacts.Select(impact => impact.Damage.TargetId), Is.All.EqualTo("broad"));
            Assert.That(impacts.Select(impact => impact.Damage.ApplicationId).Distinct().Count(), Is.EqualTo(5));
            Assert.That(impacts.Select(impact => impact.ContactAt), Is.Ordered);
            Assert.That(impacts.Select(impact => impact.WeaponInstanceId), Is.All.EqualTo(primary.Id));
            Assert.That(effect.GetActiveArrows(), Is.Empty);

            var settings = Temporary<MeleeEnemySettings>();
            var healthFields = new SerializedObject(settings);
            healthFields.FindProperty("maxHealth").floatValue = 100;
            healthFields.ApplyModifiedPropertiesWithoutUndo();
            var target = new MeleeEnemy("broad", settings);
            foreach (RepeaterBowArrowImpact impact in impacts)
            {
                Assert.That(impact.Damage.Amount, Is.EqualTo(8f));
                Assert.That(target.ApplyDamage(impact.Damage, impact.ContactAt), Is.True);
                Assert.That(target.ApplyDamage(impact.Damage, impact.ContactAt), Is.False);
            }
            Assert.That(target.CurrentHealth, Is.EqualTo(60));
        }

        [Test]
        public void SolidWallsWinExactTiesAndInvalidTargetsOrTriggerWallsDoNotBlock()
        {
            Bind();
            Target(Player, new Vector2(.5f, 0), new Vector2(.05f, 1));
            Target("dead", new Vector2(1, 0), new Vector2(.05f, 1), alive: false);
            Target("untargetable", new Vector2(1.2f, 0), new Vector2(.05f, 1), attackable: false);
            Box("ignored trigger", WallLayer, Polar(20, 2), new Vector2(.08f, .18f), trigger: true);
            Target("central-tie", new Vector2(2, 0), new Vector2(.08f, .18f));
            Box("tie wall", WallLayer, new Vector2(2, 0), new Vector2(.08f, .18f));
            Target("behind-wall", new Vector2(3, 0), new Vector2(.08f, .12f));
            Target("edge", Polar(20, 3), new Vector2(.08f, .18f));
            SyncPhysics();

            Assert.That(Cast(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            Advance(450);
            Assert.That(impacts.Single().Damage.TargetId, Is.EqualTo("edge"));
            Assert.That(impacts[0].ContactAt, Is.GreaterThan(Ms(300)).And.LessThan(Ms(400)));
            Assert.That(effect.GetActiveArrows().Count, Is.EqualTo(3));
        }

        [Test]
        public void ExactProjectileLifetimeExcludesTheEndpointAndPreservesAnEarlierContact()
        {
            Bind();
            Assert.That(Cast(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            RepeaterBowArrowSnapshot center = effect.GetActiveArrows()[2];
            float endX = (float)(center.Velocity.X * center.Lifetime.TotalSeconds);
            BoxCollider2D edge = Target("endpoint", new Vector2(endX, 0), new Vector2(.25f, .12f));
            edge.offset = new Vector2(.125f, 0);
            Target("before-expiry", Polar(10, 6.65f), new Vector2(.05f, .12f));
            Target("beyond-range", Polar(-10, 7.05f), new Vector2(.05f, .12f));
            SyncPhysics();

            now = center.LaunchedAt + center.Lifetime;
            effect.Advance(now, impacts);
            Assert.That(impacts.Single().Damage.TargetId, Is.EqualTo("before-expiry"));
            Assert.That(impacts[0].ContactAt, Is.LessThan(now));
            Assert.That(effect.GetActiveArrows(), Is.Empty);
        }

        [Test]
        public void EarlierArrowsContinueDuringTheSameExtractionButASecondActivationIsRejected()
        {
            Bind();
            Target("target", new Vector2(2, 0), new Vector2(.1f, 2));
            SyncPhysics();
            Assert.That(Cast(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            now = Ms(130);
            Assert.That(run.TryBeginExtraction(runId, isInRange: true), Is.Not.Null);
            Advance(400);
            Assert.That(impacts.Count, Is.EqualTo(5));
            Assert.That(Cast(400, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeathSuppressesUnobservedHitsAndAReplacementRunCannotReuseThem(bool replaceRun)
        {
            Bind();
            Target("later", new Vector2(2, 0), new Vector2(.1f, 2));
            SyncPhysics();
            Assert.That(Cast(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            now = Ms(130);
            Assert.That(run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, now) })?.Reason, Is.EqualTo(RunEndReason.Death));
            Assert.That(effect.GetActiveArrows(), Is.Empty);
            if (replaceRun)
            {
                Assert.That(run.TryReturnToTown(runId), Is.True);
                Assert.That(run.TryStartRun(RunId.New(), 1, Ms(2000)), Is.True);
            }
            Advance(400);
            Assert.That(impacts, Is.Empty);
        }

        [Test]
        public void AtTheRunDeadlineUnobservedArrowsCannotDamageOrReopen()
        {
            Bind(duration: 1000);
            Target("late", new Vector2(2, 0), new Vector2(.1f, 2));
            SyncPhysics();
            Assert.That(Cast(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Started));
            Advance(1000);
            Assert.That(effect.GetActiveArrows(), Is.Empty);
            Assert.That(impacts, Is.Empty);
            Assert.That(selection.TrySwitch(1, Ms(950)), Is.True);
            Assert.That(Cast(950, Guid.NewGuid(), weapon: secondary), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            now = Ms(1000);
            Assert.That(run.ProcessSignals(Array.Empty<RunSignal>())?.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(Cast(1000, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
        }

        [TestCase("hunting_bow", 2)]
        [TestCase("repeater_bow", 1)]
        public void MisboundPatternOrInvalidAimProducesNoProjectilesOrCooldown(string id, int grade)
        {
            Bind(id, grade);
            Assert.That(executor.TryActivate(Guid.NewGuid(), primary.Id, Ms(100), default, ManualSkillAim.AtSelf()),
                Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(Cast(100, Guid.NewGuid()), Is.EqualTo(ManualSkillActivationStatus.EffectRejected));
            Assert.That(effect.GetActiveArrows(), Is.Empty);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, primary.Id), Is.EqualTo(TimeSpan.Zero));
        }

        private void Bind(string id = "repeater_bow", int grade = 2, int duration = 20000)
        {
            WeaponDefinition definition = Temporary<WeaponDefinition>();
            var fields = new SerializedObject(definition);
            fields.FindProperty("definitionId").stringValue = id;
            fields.FindProperty("displayName").stringValue = "Repeater Bow Test";
            fields.FindProperty("grade").intValue = grade;
            fields.FindProperty("kind").enumValueIndex = (int)WeaponKind.Bow;
            fields.FindProperty("skill").objectReferenceValue = pattern;
            fields.ApplyModifiedPropertiesWithoutUndo();

            WeaponCatalog catalog = Temporary<WeaponCatalog>();
            var catalogFields = new SerializedObject(catalog);
            SerializedProperty definitions = catalogFields.FindProperty("definitions");
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = definition;
            catalogFields.ApplyModifiedPropertiesWithoutUndo();
            primary = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            secondary = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));

            Assert.That(run.TryStartRun(runId, 1, Ms(duration)), Is.True);
            effect = new RepeaterBowVolleyEffect(run, physics, 1 << TargetLayer, 1 << WallLayer,
                collider => targets.TryGetValue(collider, out TargetCandidate candidate) ? candidate : null);
            locks = new WeaponActionLocks();
            selection = new EquippedWeaponSelection(run, new InventoryService(new[] { primary, secondary }),
                catalog, () => false, locks);
            executor = new ManualSkillExecutor(run, selection, effect, () => false, _ => null, Player);
        }

        private ManualSkillActivationStatus Cast(int milliseconds, Guid gesture, WorldPoint2D origin = default,
            WeaponInstance? weapon = null)
        {
            now = Ms(milliseconds);
            return executor.TryActivate(gesture, (weapon ?? primary).Id, now, origin, Right);
        }

        private void Advance(int milliseconds)
        {
            now = Ms(milliseconds);
            effect.Advance(now, impacts);
        }

        private BoxCollider2D Target(string id, Vector2 position, Vector2 size, bool alive = true, bool attackable = true)
        {
            BoxCollider2D body = Box(id, TargetLayer, position, size, trigger: true);
            targets.Add(body, new TargetCandidate(id, new WorldPoint2D(position.x, position.y), alive, attackable));
            return body;
        }

        private BoxCollider2D Box(string objectName, int layer, Vector2 position, Vector2 size, bool trigger = false)
        {
            var entity = new GameObject(objectName) { layer = layer };
            SceneManager.MoveGameObjectToScene(entity, scene);
            entity.transform.position = position;
            BoxCollider2D box = entity.AddComponent<BoxCollider2D>();
            box.size = size;
            box.isTrigger = trigger;
            return box;
        }

        private void SyncPhysics()
        {
            Physics2D.SyncTransforms();
            Assert.That(physics.Simulate(.02f), Is.True);
        }

        private T Temporary<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            assets.Add(asset);
            return asset;
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

        private static Vector2 Polar(float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians) * distance, Mathf.Sin(radians) * distance);
        }
    }
}
#endif
