#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections;
using NUnit.Framework;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.WeaponSwitch;
using ProjectWeapon.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectWeapon.ManualSkill.Tests
{
    public sealed class ManualSkillInputPlayModeTests
    {
        [UnityTest]
        public IEnumerator DuplicatedGestureAndSwitchDuringSkillAreIgnoredAcrossUnityFrames()
        {
            var pattern = ScriptableObject.CreateInstance<ManualSkillPattern>();
            var definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            var catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
            var root = new GameObject("Equipment fixture", typeof(EquippedWeaponButtons));
            try
            {
                var data = new SerializedObject(definition);
                data.FindProperty("definitionId").stringValue = "test_weapon";
                data.FindProperty("displayName").stringValue = "Test Weapon";
                data.FindProperty("skill").objectReferenceValue = pattern;
                data.ApplyModifiedPropertiesWithoutUndo();
                var catalogData = new SerializedObject(catalog);
                var weapons = catalogData.FindProperty("definitions");
                weapons.arraySize = 1;
                weapons.GetArrayElementAtIndex(0).objectReferenceValue = definition;
                catalogData.ApplyModifiedPropertiesWithoutUndo();

                var first = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(0));
                var second = WeaponInstance.Create(definition.Id, WeaponProvenance.Personal, WeaponLocation.Equipped(1));
                var inventory = new InventoryService(new[] { first, second });
                TimeSpan now = TimeSpan.Zero;
                var run = new RunState(() => now);
                Assert.That(run.TryStartRun(RunId.New(), 1, TimeSpan.FromSeconds(10)), Is.True);
                var locks = new WeaponActionLocks();
                bool busy = false;
                var selection = new EquippedWeaponSelection(run, inventory, catalog, () => busy, locks);
                Button firstButton = ButtonIn(root.transform, "First");
                Button secondButton = ButtonIn(root.transform, "Second");
                root.GetComponent<EquippedWeaponButtons>().Bind(selection, firstButton, secondButton, () => now);
                var effect = new TestEffect(() =>
                {
                    secondButton.onClick.Invoke();
                    busy = true;
                });
                var executor = new ManualSkillExecutor(run, selection, effect, () => false, _ => null, "player");
                Guid gesture = Guid.NewGuid();
                var aim = ManualSkillAim.AtSelf();

                yield return null;
                Assert.That(executor.TryActivate(gesture, first.Id, now, default, aim),
                    Is.EqualTo(ManualSkillActivationStatus.Started));
                Assert.That(selection.ActiveSlotIndex, Is.EqualTo(0));
                yield return null;
                Assert.That(executor.TryActivate(gesture, first.Id, now, default, aim),
                    Is.EqualTo(ManualSkillActivationStatus.DuplicateGesture));
                secondButton.onClick.Invoke();
                Assert.That(selection.ActiveSlotIndex, Is.EqualTo(0));

                busy = false;
                now = TimeSpan.FromMilliseconds(799);
                secondButton.onClick.Invoke();
                Assert.That(selection.ActiveSlotIndex, Is.EqualTo(1));
                Assert.That(executor.TryActivate(Guid.NewGuid(), second.Id, now, default, aim),
                    Is.EqualTo(ManualSkillActivationStatus.Unavailable));
                now = TimeSpan.FromMilliseconds(800);
                Assert.That(executor.TryActivate(Guid.NewGuid(), second.Id, now, default, aim),
                    Is.EqualTo(ManualSkillActivationStatus.Started));
                Assert.That(effect.Starts, Is.EqualTo(2));
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(catalog);
                Object.Destroy(definition);
                Object.Destroy(pattern);
            }
        }

        private static Button ButtonIn(Transform parent, string name)
        {
            var button = new GameObject(name, typeof(RectTransform), typeof(Button));
            button.transform.SetParent(parent, worldPositionStays: false);
            return button.GetComponent<Button>();
        }

        private sealed class TestEffect : IManualSkillEffect
        {
            private readonly Action onStart;
            public TestEffect(Action onStart) => this.onStart = onStart;
            public int Starts { get; private set; }

            public bool TryStart(ManualSkillActivationRequest request)
            {
                Starts++;
                onStart();
                return true;
            }
        }
    }
}
#endif
