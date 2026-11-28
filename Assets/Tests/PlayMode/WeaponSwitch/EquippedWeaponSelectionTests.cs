#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectWeapon.AutoAttack;
using ProjectWeapon.Combat;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace ProjectWeapon.WeaponSwitch.Tests
{
    public sealed class EquippedWeaponSelectionTests
    {
        private static readonly TargetCandidate[] Targets =
        {
            new TargetCandidate("target", new WorldPoint2D(1, 0), isAlive: true, isAttackable: true)
        };

        private readonly List<Object> fixtures = new List<Object>();
        private RunState run = null!;
        private RunId runId;
        private InventoryService inventory = null!;
        private WeaponInstance first = null!;
        private WeaponInstance second = null!;
        private WeaponDefinition firstDefinition = null!;
        private WeaponDefinition secondDefinition = null!;
        private WeaponCatalog catalog = null!;
        private EquippedWeaponSelection selection = null!;
        private WeaponActionLocks locks = null!;
        private TimeSpan now;
        private bool attackBusy;
        private bool skillBusy;

        [SetUp]
        public void Prepare()
        {
            now = TimeSpan.Zero;
            attackBusy = false;
            skillBusy = false;
            run = new RunState(() => now);
            runId = RunId.New();
            Assert.That(run.TryStartRun(runId, 1, Ms(10000)), Is.True);
            firstDefinition = Definition("first", 800);
            secondDefinition = Definition("second", 600);
            catalog = Make<WeaponCatalog>();
            using (var data = new SerializedObject(catalog))
            {
                var definitions = data.FindProperty("definitions");
                definitions.arraySize = 2;
                definitions.GetArrayElementAtIndex(0).objectReferenceValue = firstDefinition;
                definitions.GetArrayElementAtIndex(1).objectReferenceValue = secondDefinition;
                data.ApplyModifiedPropertiesWithoutUndo();
            }

            first = WeaponInstance.Create(firstDefinition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
            second = WeaponInstance.Create(secondDefinition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
            inventory = new InventoryService(new[] { second, first });
            locks = new WeaponActionLocks();
            selection = new EquippedWeaponSelection(run, inventory, catalog, () => attackBusy || skillBusy, locks);
        }

        [TearDown]
        public void Dispose()
        {
            foreach (Object item in fixtures)
                Object.DestroyImmediate(item);
            fixtures.Clear();
        }

        [Test]
        public void SlotIconChangesTheNextAttackAndSkillReferencesInOneInput()
        {
            GameObject root = NewObject("Equipment buttons");
            var binding = root.AddComponent<EquippedWeaponButtons>();
            var eventSystem = root.AddComponent<EventSystem>();
            var firstIcon = MakeButton(root.transform, "Slot 1");
            var secondIcon = MakeButton(root.transform, "Slot 2");
            binding.Bind(selection, firstIcon, secondIcon, () => now);
            var changes = new List<SelectedWeapon>();
            selection.ActiveSlotChanged += changes.Add;
            var attacks = new List<BasicAttackRequest>();
            var scheduler = new AutoAttackScheduler(run, request =>
            {
                attacks.Add(request);
                return true;
            }, initiallyEnabled: true);

            Assert.That(selection.ReadActiveWeapon()?.InstanceId, Is.EqualTo(first.Id));
            Assert.That(Attack(scheduler, out SelectedWeapon fired), Is.True);
            locks.RecordAccepted(CombatWeaponAction.BasicAttack, fired.InstanceId, Ms(800), Ms(1400));
            now = Ms(400);
            var click = new PointerEventData(eventSystem) { button = PointerEventData.InputButton.Left };
            secondIcon.OnPointerClick(click);
            secondIcon.OnPointerClick(click);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(selection.ReadActiveWeapon()?.InstanceId, Is.EqualTo(second.Id));

            Assert.That(selection.TryGetReadyWeapon(CombatWeaponAction.Skill, now, out SelectedWeapon skill), Is.True);
            Assert.That(skill.Definition.Skill, Is.SameAs(secondDefinition.Skill));
            now = Ms(799);
            Assert.That(Attack(scheduler, out _), Is.False);
            now = Ms(800);
            Assert.That(Attack(scheduler, out _), Is.True);
            Assert.That(attacks[0].Pattern, Is.SameAs(firstDefinition.BasicAttack));
            Assert.That(attacks[1].Pattern, Is.SameAs(secondDefinition.BasicAttack));
            Assert.That(inventory.Snapshot(), Is.EquivalentTo(new[] { first, second }));

            binding.enabled = false;
            firstIcon.OnPointerClick(click);
            Assert.That(selection.ActiveSlotIndex, Is.EqualTo(1));
        }

        [Test]
        public void BusyAndExtractionClicksAreDiscardedAndInvalidSlotsKeepTheSelection()
        {
            Assert.That(selection.TrySwitch(-1, now), Is.False);
            Assert.That(selection.TrySwitch(2, now), Is.False);
            Assert.That(selection.TrySwitch(0, now), Is.False);
            attackBusy = true;
            Assert.That(selection.TrySwitch(1, now), Is.False);
            attackBusy = false;
            skillBusy = true;
            Assert.That(selection.TrySwitch(1, now), Is.False);
            skillBusy = false;
            ExtractionAttempt attempt = run.TryBeginExtraction(runId, isInRange: true)!;
            Assert.That(selection.TrySwitch(1, now), Is.False);
            run.ProcessSignals(new[] { RunSignal.CancelExtraction(runId, attempt.Id, now) });
            Assert.That(selection.ActiveSlotIndex, Is.EqualTo(0));

            Assert.That(inventory.Move(second.Id, WeaponLocation.Equipped(1), WeaponLocation.Warehouse()),
                Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(selection.TrySwitch(1, now), Is.False);
            Assert.That(inventory.Move(second.Id, WeaponLocation.Warehouse(), WeaponLocation.Equipped(1)),
                Is.EqualTo(InventoryMoveResult.Moved));
            Assert.That(selection.TrySwitch(1, now), Is.True);
            now = Ms(10000);
            Assert.That(selection.TrySwitch(0, now), Is.False);
            Assert.That(selection.TryGetReadyWeapon(CombatWeaponAction.Skill, now, out _), Is.False);
        }

        [Test]
        public void BothActionsUseLaterOfSharedAndPreviouslyUsedInstanceAcrossSwitches()
        {
            locks.RecordAccepted(CombatWeaponAction.BasicAttack, second.Id, Ms(500), Ms(1200));
            locks.RecordAccepted(CombatWeaponAction.BasicAttack, first.Id, Ms(800), Ms(1000));
            locks.RecordAccepted(CombatWeaponAction.Skill, second.Id, Ms(1600), Ms(1100));
            locks.RecordAccepted(CombatWeaponAction.Skill, first.Id, Ms(600), Ms(2000));
            Assert.That(selection.TrySwitch(1, Ms(400)), Is.True);
            Assert.That(selection.TryGetReadyWeapon(CombatWeaponAction.BasicAttack, Ms(1199), out _), Is.False);
            Assert.That(selection.TryGetReadyWeapon(CombatWeaponAction.BasicAttack, Ms(1200), out _), Is.True);
            Assert.That(selection.TryGetReadyWeapon(CombatWeaponAction.Skill, Ms(1599), out _), Is.False);
            Assert.That(selection.TryGetReadyWeapon(CombatWeaponAction.Skill, Ms(1600), out _), Is.True);
            Assert.That(selection.TrySwitch(0, Ms(1600)), Is.True);
            Assert.That(selection.TryGetReadyWeapon(CombatWeaponAction.Skill, Ms(1999), out _), Is.False);
            Assert.That(selection.TryGetReadyWeapon(CombatWeaponAction.Skill, Ms(2000), out _), Is.True);
        }

        private bool Attack(AutoAttackScheduler scheduler, out SelectedWeapon weapon)
            => selection.TryGetReadyWeapon(CombatWeaponAction.BasicAttack, now, out weapon)
                && scheduler.TryAttack(now, default, weapon.Definition, Targets);

        private WeaponDefinition Definition(string id, int attackCooldown)
        {
            var attack = Make<MeleeSlashPattern>();
            using (var data = new SerializedObject(attack))
            {
                data.FindProperty("range").floatValue = 2f;
                data.FindProperty("cooldownMilliseconds").intValue = attackCooldown;
                data.ApplyModifiedPropertiesWithoutUndo();
            }

            var definition = Make<WeaponDefinition>();
            using var fields = new SerializedObject(definition);
            fields.FindProperty("definitionId").stringValue = id;
            fields.FindProperty("displayName").stringValue = id;
            fields.FindProperty("basicAttack").objectReferenceValue = attack;
            fields.FindProperty("skill").objectReferenceValue = Make<SkillPattern>();
            fields.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private T Make<T>() where T : ScriptableObject
        {
            T item = ScriptableObject.CreateInstance<T>();
            fixtures.Add(item);
            return item;
        }

        private GameObject NewObject(string name, params Type[] components)
        {
            var item = new GameObject(name, components);
            fixtures.Add(item);
            return item;
        }

        private UnityEngine.UI.Button MakeButton(Transform parent, string name)
        {
            var item = NewObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Button));
            item.transform.SetParent(parent, false);
            return item.GetComponent<UnityEngine.UI.Button>();
        }

        private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
        private sealed class SkillPattern : ScriptableObject { }
    }
}
#endif
