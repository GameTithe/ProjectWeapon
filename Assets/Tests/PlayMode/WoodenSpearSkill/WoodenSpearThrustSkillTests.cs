#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
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
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectWeapon.WoodenSpearSkill.Tests
{
    public sealed class WoodenSpearThrustSkillTests
    {
        private const string Starter = "Assets/ProjectWeapon/Weapons/PrototypeStarter/";
        private static readonly WeaponDefinitionId SpearId = new WeaponDefinitionId("wooden_spear");
        private static readonly ManualSkillAim Forward = ManualSkillAim.Along(new WorldPoint2D(5, 0));
        private RunState run = null!;
        private RunId runId;
        private WeaponDefinition spear = null!;
        private WeaponInstance equipped = null!;
        private EquippedWeaponSelection selection = null!;
        private WeaponActionLocks locks = null!;
        private WoodenSpearThrustEffect effect = null!;
        private ManualSkillExecutor executor = null!;
        private TimeSpan clock;

        [SetUp]
        public void SetUp()
        {
            clock = TimeSpan.Zero;
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(Starter + "PrototypeStarterWeaponCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            spear = catalog.CreateLookup()[SpearId];
            equipped = WeaponInstance.Create(spear.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            var other = WeaponInstance.Create(spear.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            run = new RunState(() => clock);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(10000)), Is.True);
            effect = new WoodenSpearThrustEffect(run);
            locks = new WeaponActionLocks();
            selection = new EquippedWeaponSelection(run, new InventoryService(new[] { equipped, other }),
                catalog, () => effect.IsBusy, locks);
            executor = new ManualSkillExecutor(run, selection, effect, () => false, _ => null, "player");
        }

        [Test]
        public void CanonicalGradeOneDefinitionKeepsItsBasicAttackAndReferencesOneDirectionalSkill()
        {
            var skill = AssetDatabase.LoadAssetAtPath<WoodenSpearThrustPattern>(Starter + "WoodenSpearRepeatedThrust.asset");
            Assert.That(spear.Skill, Is.SameAs(skill));
            Assert.That(spear.Grade, Is.EqualTo(1));
            Assert.That(spear.Kind, Is.EqualTo(WeaponKind.Spear));
            Assert.That(AssetDatabase.GetAssetPath(spear.BasicAttack), Is.EqualTo(Starter + "WoodenSpearThrust.asset"));
            Assert.That(skill.AimKind, Is.EqualTo(ManualSkillAimKind.Direction));
            Assert.That(skill.Range, Is.EqualTo(2.7f));
            Assert.That(skill.Damage, Is.EqualTo(11f));
            Assert.That(skill.HalfWidth, Is.EqualTo(.25f));
            Assert.That(skill.Interval, Is.EqualTo(Ms(150)));
        }

        [Test]
        public void ThreeExactThrustsLockDirectionAndRefreshTheClosestEligibleTarget()
        {
            Guid release = Guid.NewGuid();
            Assert.That(Activate(100, Forward, release), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Activate(100, Forward, release), Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
            Assert.That(Resolve(99), Is.Null);
            WoodenSpearStrike first = Resolve(100, Target("back", -1), Target("player", .1f),
                Target("outside-side", .5f, .251f), Target("past-tip", 2.701f), Target("tip", 2.7f))!.Value;
            AssertStrike(first, 1, 100, "tip");
            Assert.That(effect.IsBusy, Is.True);
            Assert.That(selection.TrySwitch(1, Ms(100)), Is.False);
            Assert.That(Resolve(249), Is.Null);

            var live = Target("new", 1, .25f);
            WoodenSpearStrike second = Resolve(250, Target("tip", 2, alive: false), Target("far", 1.8f),
                live, live, Target("friend", .5f, attackable: false))!.Value;
            AssertStrike(second, 2, 250, "new");
            Assert.That(Resolve(250), Is.Null);
            Assert.That(Resolve(399), Is.Null);
            WoodenSpearStrike third = Resolve(400, Target("new", 1, alive: false), Target("far", 2.7f))!.Value;
            AssertStrike(third, 3, 400, "far");
            Assert.That(new[] { first.ApplicationId, second.ApplicationId, third.ApplicationId }.Distinct().Count(),
                Is.EqualTo(3));
            Assert.That(effect.IsBusy, Is.False);
            Assert.That(Resolve(400, Target("far", 1)), Is.Null);
        }

        [Test]
        public void InvalidDirectionDoesNotLockAndAcceptedWhiffsUseTheExactCooldowns()
        {
            Assert.That(Activate(100, ManualSkillAim.Along(default)), Is.EqualTo(ManualSkillActivationStatus.InvalidTarget));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, equipped.Id), Is.EqualTo(TimeSpan.Zero));
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(locks.ReadyAt(CombatWeaponAction.Skill, equipped.Id), Is.EqualTo(Ms(5300)));
            Assert.That(Resolve(100)!.Value.Hit, Is.Null);
            Assert.That(Resolve(250)!.Value.Hit, Is.Null);
            Assert.That(Resolve(400)!.Value.Hit, Is.Null);
            Assert.That(Activate(5299, Forward), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
            Assert.That(Activate(5300, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
        }

        [Test]
        public void CatchUpConsumesOneAtATimeAndRejectsBackdatedSnapshots()
        {
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            WoodenSpearStrike first = Resolve(450, Target("old", 1))!.Value;
            Assert.That(first.ScheduledAt, Is.EqualTo(Ms(100)));
            Assert.That(first.EffectiveAt, Is.EqualTo(Ms(450)));
            Assert.That(Resolve(449), Is.Null);
            Assert.That(Resolve(450, Target("old", 1, alive: false))!.Value.Hit, Is.Null);
            Assert.That(Resolve(450, Target("new", 1))!.Value.Hit?.TargetId, Is.EqualTo("new"));
            Assert.That(Resolve(451), Is.Null);
        }

        [TestCase(RunEndReason.Death)]
        [TestCase(RunEndReason.Extracted)]
        public void FinalizedRunCancelsRemainingThrusts(RunEndReason reason)
        {
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Resolve(100, Target("enemy", 1)), Is.Not.Null);
            clock = Ms(150);
            if (reason == RunEndReason.Death)
                run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, clock) });
            else
            {
                ExtractionAttempt channel = run.TryBeginExtraction(runId, isInRange: true)!;
                clock = channel.CompletesAt;
                run.ProcessSignals(new[] { RunSignal.CompleteExtraction(runId, channel.Id, clock) });
            }

            Assert.That(Resolve((int)clock.TotalMilliseconds, Target("enemy", 1)), Is.Null);
            Assert.That(effect.IsBusy, Is.False);
            Assert.That(effect.NextThrustAt, Is.Null);
        }

        [Test]
        public void ActiveExtractionOnlyAllowsPreviouslyStartedStages()
        {
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Resolve(100, Target("enemy", 1)), Is.Not.Null);
            clock = Ms(150);
            Assert.That(run.TryBeginExtraction(runId, isInRange: true), Is.Not.Null);
            Assert.That(Resolve(250, Target("enemy", 1)), Is.Not.Null);
            Assert.That(Resolve(400, Target("enemy", 1)), Is.Not.Null);
            Assert.That(Activate(5300, Forward), Is.EqualTo(ManualSkillActivationStatus.Unavailable));
        }

        [Test]
        public void ExactDeadlineAndLaterRunCannotConsumePendingStages()
        {
            Assert.That(Activate(9850, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Resolve(9850, Target("enemy", 1)), Is.Not.Null);
            Assert.That(Resolve(9999, Target("enemy", 1)), Is.Null);
            Assert.That(run.Result, Is.Null);
            Assert.That(Resolve(10000, Target("enemy", 1)), Is.Null);
            Assert.That(run.ProcessSignals(Array.Empty<RunSignal>())!.Reason, Is.EqualTo(RunEndReason.TimeExpired));
            Assert.That(run.TryReturnToTown(runId), Is.True);
            Assert.That(run.TryStartRun(RunId.New(), 1, Ms(10000)), Is.True);
            Assert.That(Resolve(10150, Target("enemy", 1)), Is.Null);
        }

        [Test]
        public void ReplacingTheRunDiscardsAStageThatWasNeverPumpedAfterTheOldResult()
        {
            Assert.That(Activate(100, Forward), Is.EqualTo(ManualSkillActivationStatus.Started));
            Assert.That(Resolve(100, Target("enemy", 1)), Is.Not.Null);
            clock = Ms(150);
            Assert.That(run.ProcessSignals(new[] { RunSignal.PlayerDied(runId, clock) })!.Reason,
                Is.EqualTo(RunEndReason.Death));
            Assert.That(run.TryReturnToTown(runId), Is.True);
            Assert.That(run.TryStartRun(RunId.New(), 1, Ms(10000)), Is.True);
            Assert.That(effect.IsBusy, Is.False);
            Assert.That(Resolve(250, Target("enemy", 1)), Is.Null);
            Assert.That(effect.NextThrustAt, Is.Null);
        }

        [UnityTest]
        public IEnumerator ActualDirectionalDragReleaseStartsOneSpearSequence()
        {
            var root = new GameObject("Spear Aim Canvas", typeof(RectTransform), typeof(Canvas));
            var events = new GameObject("Spear Aim EventSystem", typeof(EventSystem));
            try
            {
                RectTransform area = (RectTransform)root.transform;
                var button = new GameObject("Spear Skill Button", typeof(RectTransform), typeof(DirectionalSkillDrag));
                RectTransform rect = (RectTransform)button.transform;
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
                Assert.That(drag.IsAiming, Is.True);
                pointer.position += new Vector2(40, 0);
                ExecuteEvents.Execute(button, pointer, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(button, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(button, pointer, ExecuteEvents.pointerUpHandler);

                Assert.That(completed, Is.EqualTo(ManualSkillActivationStatus.Started));
                AssertStrike(Resolve(100, Target("enemy", 1))!.Value, 1, 100, "enemy");
                Assert.That(Resolve(100, Target("enemy", 1)), Is.Null);
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(events);
            }
        }

        private ManualSkillActivationStatus Activate(int at, ManualSkillAim aim, Guid? gesture = null)
        {
            clock = Ms(at);
            return executor.TryActivate(gesture ?? Guid.NewGuid(), equipped.Id, clock, default, aim);
        }

        private WoodenSpearStrike? Resolve(int at, params TargetCandidate[] targets)
        {
            clock = Ms(at);
            return effect.TryResolveNext(clock, targets, out WoodenSpearStrike strike) ? strike : null;
        }

        private void AssertStrike(WoodenSpearStrike strike, int number, int at, string expectedTarget)
        {
            Assert.That(strike.Number, Is.EqualTo(number));
            Assert.That(strike.RunId, Is.EqualTo(runId));
            Assert.That(strike.WeaponId, Is.EqualTo(equipped.Id));
            Assert.That(strike.Direction, Is.EqualTo(new WorldPoint2D(1, 0)));
            Assert.That(strike.ScheduledAt, Is.EqualTo(Ms(at)));
            Assert.That(strike.EffectiveAt, Is.EqualTo(Ms(at)));
            Assert.That(strike.Hit?.TargetId, Is.EqualTo(expectedTarget));
            Assert.That(strike.Hit?.ApplicationId, Is.EqualTo(strike.ApplicationId));
            Assert.That(strike.Hit?.Amount, Is.EqualTo(11f));
        }

        private static TargetCandidate Target(string id, float x, float y = 0, bool alive = true, bool attackable = true)
            => new TargetCandidate(id, new WorldPoint2D(x, y), alive, attackable);

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
#endif
