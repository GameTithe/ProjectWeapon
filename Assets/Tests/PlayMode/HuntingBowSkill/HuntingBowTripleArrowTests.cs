#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.ManualSkill;
using ProjectWeapon.ManualSkillUI;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectWeapon.HuntingBowSkill.Tests
{
    public sealed class HuntingBowTripleArrowTests
    {
        private const string Starter = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private const int TargetLayer = 8;
        private const int SolidLayer = 9;
        private static readonly WeaponDefinitionId BowId = new WeaponDefinitionId("hunting_bow");
        private static readonly ManualSkillAim Forward = ManualSkillAim.Along(new WorldPoint2D(5, 0));
        private readonly Dictionary<Collider2D, TargetCandidate> targets = new Dictionary<Collider2D, TargetCandidate>();
        private Scene scene;
        private PhysicsScene2D physicsScene;
        private RunState run = null!;
        private RunId runId;
        private WeaponDefinition bow = null!;
        private WeaponInstance firstBow = null!;
        private WeaponInstance secondBow = null!;
        private EquippedWeaponSelection selection = null!;
        private WeaponActionLocks locks = null!;
        private HuntingBowTripleArrowEffect effect = null!;
        private ManualSkillExecutor executor = null!;
        private TimeSpan clock;

        [SetUp]
        public void SetUp()
        {
            targets.Clear();
            clock = TimeSpan.Zero;
            scene = SceneManager.CreateScene("KAN-134 " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physicsScene = scene.GetPhysicsScene2D();
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Starter + "PrototypeStarterWeaponCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            bow = catalog.CreateLookup()[BowId];
            firstBow = WeaponInstance.Create(bow.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            secondBow = WeaponInstance.Create(bow.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            run = new RunState(() => clock);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(10000)), Is.True);
            effect = new HuntingBowTripleArrowEffect(run, physicsScene, 1 << TargetLayer, 1 << SolidLayer,
                collider => targets.TryGetValue(collider, out TargetCandidate target) ? target : null);
            locks = new WeaponActionLocks();
            selection = new EquippedWeaponSelection(run, new InventoryService(new[] { firstBow, secondBow }),
                catalog, () => false, locks);
            executor = new ManualSkillExecutor(run, selection, effect, () => false, _ => null, "player");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void CanonicalGradeOneBowReferencesOneDirectionalTripleArrowPattern()
        {
            var skill = AssetDatabase.LoadAssetAtPath<HuntingBowTripleArrowPattern>(Starter + "HuntingBowTripleArrow.asset");
            Assert.That(skill, Is.Not.Null);
            Assert.That(bow.Skill, Is.SameAs(skill));
            Assert.That(bow.Grade, Is.EqualTo(1));
            Assert.That(bow.Kind, Is.EqualTo(WeaponKind.Bow));
            Assert.That(skill.AimKind, Is.EqualTo(ManualSkillAimKind.Direction));
            Assert.That(skill.Range, Is.EqualTo(6f));
            Assert.That(skill.Speed, Is.EqualTo(10f));
            Assert.That(skill.Damage, Is.EqualTo(11f));
            Assert.That(skill.SpreadDegrees, Is.EqualTo(15f));
        }

        [Test]
        public void SameReleaseLaunchesSixUnitFlightsAtMinusFifteenZeroAndPlusFifteenAroundTheAim()
        {
            var origin = new WorldPoint2D(2, 3);
            Guid gesture = Guid.NewGuid();
            Assert.That(Activate(100, ManualSkillAim.Along(new WorldPoint2D(0, 2)), gesture, origin),
                Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Activate(100, Forward, gesture), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));

            var arrows = effect.GetActiveArrows();
            Assert.That(arrows.Select(arrow => arrow.ApplicationId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(arrows.Select(arrow => arrow.Position), Is.All.EqualTo(origin));
            Assert.That(arrows.Select(arrow => arrow.Lifetime), Is.All.EqualTo(Ms(600)));
            Assert.That(arrows.Select(arrow => arrow.Elapsed), Is.All.EqualTo(TimeSpan.Zero));
            CollectionAssert.AreEqual(new[] { 75, 90, 105 }, arrows.Select(arrow =>
                Mathf.RoundToInt(Mathf.Atan2(arrow.Velocity.Y, arrow.Velocity.X) * Mathf.Rad2Deg)).ToArray());
            Assert.That(Advance(100), Is.Empty);
            Assert.That(Advance(400), Is.Empty);
            foreach (var arrow in effect.GetActiveArrows())
                Assert.That(Math.Sqrt(arrow.Position.SquaredDistanceTo(origin)), Is.EqualTo(3d).Within(.0001));
            Assert.That(Advance(699), Is.Empty);
            Assert.That(effect.GetActiveArrows().Count, Is.EqualTo(3));
            Assert.That(Advance(700), Is.Empty);
            Assert.That(effect.GetActiveArrows(), Is.Empty);
        }

        [Test]
        public void RealSweepFindsTheFirstEligibleContactPerArrowAndWallsBlockOnlyTheirOwnFlight()
        {
            MakeTarget("dead", new Vector2(.5f, 0), new Vector2(.04f, .2f), alive: false);
            MakeTarget("player", new Vector2(.75f, 0), new Vector2(.04f, .2f));
            MakeTarget("friend", new Vector2(1, 0), new Vector2(.04f, .2f), attackable: false);
            MakeTarget("center", new Vector2(2, 0), new Vector2(.04f, .2f));
            MakeTarget("behind-center", new Vector2(3, 0), new Vector2(.04f, .2f));
            MakeBox("wall", OnRay(15, 2), new Vector2(.15f, .2f), SolidLayer);
            MakeTarget("behind-wall", OnRay(15, 3), new Vector2(.15f, .2f));
            MakeTarget("left", OnRay(-15, 3), new Vector2(.15f, .2f));
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            var arrows = effect.GetActiveArrows();

            Assert.That(Advance(250), Is.Empty);
            Assert.That(selection.TrySwitch(1, Ms(250)), Is.True);
            List<HuntingBowArrowImpact> hits = Advance(450);
            Assert.That(hits.Select(hit => hit.RunId), Is.All.EqualTo(runId));
            Assert.That(hits.Select(hit => hit.WeaponId), Is.All.EqualTo(firstBow.Id));
            Assert.That(hits[0].ContactAt, Is.EqualTo(Ms(298)).Within(Ms(1)));
            Assert.That(hits[1].ContactAt, Is.GreaterThan(hits[0].ContactAt).And.LessThan(Ms(450)));
            Assert.That(hits.Select(hit => hit.Damage.TargetId), Is.EqualTo(new[] { "center", "left" }));
            Assert.That(hits.Select(hit => hit.Damage.Amount), Is.All.EqualTo(11f));
            Assert.That(hits.Select(hit => hit.Damage.ApplicationId),
                Is.EqualTo(new[] { arrows[1].ApplicationId, arrows[0].ApplicationId }));
            Assert.That(Advance(650), Is.Empty);
            Assert.That(effect.GetActiveArrows(), Is.Empty);
        }

        [Test]
        public void OneWideTargetMayReceiveThreeIndependentImpactsWhileEachArrowStopsOnce()
        {
            MakeTarget("wide", new Vector2(2, 0), new Vector2(.1f, 2f));
            MakeTarget("beyond-wide", new Vector2(3, 0), new Vector2(.1f, 3f));
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));

            List<HuntingBowArrowImpact> hits = Advance(350);
            Assert.That(hits, Has.Count.EqualTo(3));
            Assert.That(hits.Select(hit => hit.Damage.ApplicationId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(hits.Select(hit => hit.Damage.TargetId), Is.All.EqualTo("wide"));
            Assert.That(hits.Select(hit => hit.Damage.Amount), Is.All.EqualTo(11f));
            Assert.That(Advance(350), Is.Empty);
            Assert.That(Advance(400), Is.Empty);
        }

        [TestCase(5.99f, true)]
        [TestCase(6.01f, false)]
        public void RangeUsesFirstSweptContactAndNeverHitsPastSixUnits(float nearEdge, bool expectsHit)
        {
            MakeTarget("range-edge", new Vector2(nearEdge + .01f, 0), new Vector2(.02f, .1f));
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(700).Select(hit => hit.Damage.TargetId),
                Is.EqualTo(expectsHit ? new[] { "range-edge" } : Array.Empty<string>()));
            Assert.That(Advance(700), Is.Empty);
            Assert.That(effect.GetActiveArrows(), Is.Empty);
        }

        [Test]
        public void ExactRangeContactExpiresWithoutDamage()
        {
            var edge = new GameObject("Exact six-unit target");
            SceneManager.MoveGameObjectToScene(edge, scene);
            edge.layer = TargetLayer;
            var collider = edge.AddComponent<EdgeCollider2D>();
            collider.points = new[] { new Vector2(6f, -.1f), new Vector2(6f, .1f) };
            collider.isTrigger = true;
            targets.Add(collider, new TargetCandidate("exact-range", new WorldPoint2D(6f, 0f), true, true));
            Physics2D.SyncTransforms();
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(1 << TargetLayer);
            var geometry = new List<RaycastHit2D>();
            Assert.That(physicsScene.Raycast(Vector2.zero, Vector2.right, 6.01f, filter, geometry), Is.EqualTo(1));
            Assert.That(geometry[0].distance, Is.EqualTo(6f).Within(.00001f));

            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(700), Is.Empty);
            Assert.That(effect.GetActiveArrows(), Is.Empty);
        }

        [Test]
        public void InvalidAimPreservesLocksAndSuccessfulWhiffChargesSharedAndInstanceCooldowns()
        {
            Assert.That(Activate(100, ManualSkillAim.Along(default)), Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(effect.GetActiveArrows(), Is.Empty);
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, firstBow.Id), Is.EqualTo(TimeSpan.Zero));
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, firstBow.Id), Is.EqualTo(Ms(5900)));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, secondBow.Id), Is.EqualTo(Ms(900)));
            Assert.That(selection.TrySwitch(1, Ms(100)), Is.True);
            Assert.That(Activate(899, Forward, weapon: secondBow), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(900, Forward, weapon: secondBow), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(selection.TrySwitch(0, Ms(900)), Is.True);
            Assert.That(Advance(5899), Is.Empty);
            Assert.That(Activate(5899, Forward), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(5900, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(effect.GetActiveArrows().Count, Is.EqualTo(3));
        }

        [Test]
        public void ExactDeadlineClearsUnprocessedFlightsEvenBeforeTheRunPublishesAResult()
        {
            MakeTarget("enemy", new Vector2(2, 0), new Vector2(.1f, .2f));
            Assert.That(Activate(9850, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Advance(9999), Is.Empty);
            Assert.That(run.Result, Is.Null);
            Assert.That(Advance(10000), Is.Empty);
            Assert.That(effect.GetActiveArrows(), Is.Empty);
            Assert.That(Advance(9999), Is.Empty);
            Assert.That(Activate(10000, Forward), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeathOrRunReplacementDiscardsAnUnprocessedVolley(bool replaceRun)
        {
            MakeTarget("enemy", new Vector2(1, 0), new Vector2(.1f, .2f));
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            clock = Ms(125);
            Assert.That(run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, clock) })!.Reason,
                Is.EqualTo(RunEndReason.Death));
            if (replaceRun)
            {
                Assert.That(run.TryReturnToTown(runId), Is.True);
                Assert.That(run.TryStartRun(RunId.New(), 1, Ms(10000)), Is.True);
            }
            Assert.That(Advance(300), Is.Empty);
            Assert.That(effect.GetActiveArrows(), Is.Empty);
        }

        [Test]
        public void AnAcceptedVolleyCanLandWhileExtractingButFinalizedExtractionDiscardsAnUnprocessedVolley()
        {
            MakeTarget("enemy", new Vector2(1, 0), new Vector2(.1f, .2f));
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            clock = Ms(125);
            ExtractionAttempt first = run.TryBeginExtraction(runId, isInRange: true)!;
            Assert.That(first, Is.Not.Null);
            Assert.That(Advance(225).Single().Damage.TargetId, Is.EqualTo("enemy"));
            run.ProcessSignals(new[] { RunSignal.CancelExtraction(runId, first.Id, clock) });

            Assert.That(Activate(5900, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            clock = Ms(5925);
            ExtractionAttempt second = run.TryBeginExtraction(runId, isInRange: true)!;
            Assert.That(second, Is.Not.Null);
            clock = second.CompletesAt;
            Assert.That(run.ProcessSignals(new[] { RunSignal.CompleteExtraction(runId, second.Id, clock) })!.Reason,
                Is.EqualTo(RunEndReason.Extracted));
            Assert.That(Advance((int)clock.TotalMilliseconds), Is.Empty);
            Assert.That(effect.GetActiveArrows(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator ActualDirectionalDragReleaseStartsOneVolley()
        {
            var root = new GameObject("Bow Aim Canvas", typeof(RectTransform), typeof(Canvas));
            var events = new GameObject("Bow Aim EventSystem", typeof(EventSystem));
            try
            {
                var area = (RectTransform)root.transform;
                var button = new GameObject("Bow Skill Button", typeof(RectTransform), typeof(DirectionalSkillDrag));
                var rect = (RectTransform)button.transform;
                rect.SetParent(area, false);
                rect.sizeDelta = new Vector2(54, 54);
                var arrow = new GameObject("Aim Arrow", typeof(RectTransform), typeof(DirectionalSkillArrow));
                arrow.transform.SetParent(area, false);
                var drag = button.GetComponent<DirectionalSkillDrag>();
                drag.Bind(executor, selection, area, arrow.GetComponent<DirectionalSkillArrow>(), () => clock, () => default);
                ManualSkillActivationStatus? completed = null;
                drag.Finished += (_, status) => completed = status;
                yield return null;

                clock = Ms(100);
                Canvas.ForceUpdateCanvases();
                var pointer = new PointerEventData(events.GetComponent<EventSystem>())
                {
                    button = PointerEventData.InputButton.Left,
                    pointerId = 42,
                    position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
                };
                ExecuteEvents.Execute(button, pointer, ExecuteEvents.pointerDownHandler);
                pointer.position += new Vector2(40, 0);
                ExecuteEvents.Execute(button, pointer, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(button, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(button, pointer, ExecuteEvents.pointerUpHandler);

                Assert.That(completed, Is.EqualTo(ManualSkillActivationStatus.Started));
                var arrows = effect.GetActiveArrows();
                Assert.That(arrows.Count, Is.EqualTo(3));
                Assert.That(arrows[1].Velocity.X, Is.EqualTo(10f).Within(.0001));
                Assert.That(arrows[1].Velocity.Y, Is.EqualTo(0f).Within(.0001));
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(events);
            }
        }

        private ManualSkillActivationStatus Activate(int at, ManualSkillAim aim, Guid? gesture = null,
            WorldPoint2D origin = default, WeaponInstance? weapon = null)
        {
            clock = Ms(at);
            return executor.TryActivate(gesture ?? Guid.NewGuid(), (weapon ?? firstBow).Id, clock, origin, aim);
        }

        private List<HuntingBowArrowImpact> Advance(int at)
        {
            clock = Ms(at);
            var hits = new List<HuntingBowArrowImpact>();
            effect.Advance(clock, hits);
            return hits;
        }

        private void MakeTarget(string id, Vector2 position, Vector2 size, bool alive = true, bool attackable = true)
        {
            BoxCollider2D collider = MakeBox(id, position, size, TargetLayer);
            collider.isTrigger = true;
            targets.Add(collider, new TargetCandidate(id, new WorldPoint2D(position.x, position.y), alive, attackable));
        }

        private BoxCollider2D MakeBox(string label, Vector2 position, Vector2 size, int layer)
        {
            var box = new GameObject(label);
            SceneManager.MoveGameObjectToScene(box, scene);
            box.layer = layer;
            box.transform.position = position;
            var collider = box.AddComponent<BoxCollider2D>();
            collider.size = size;
            Physics2D.SyncTransforms();
            return collider;
        }

        private static Vector2 OnRay(float degrees, float distance)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
#endif
