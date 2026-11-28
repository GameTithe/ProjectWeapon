#nullable enable

using System;
using UnityEngine;

namespace ProjectWeapon.WeaponSwitch
{
    [DisallowMultipleComponent]
    public sealed class EquippedWeaponButtons : MonoBehaviour
    {
        private EquippedWeaponSelection? selection;
        private UnityEngine.UI.Button? firstSlot;
        private UnityEngine.UI.Button? secondSlot;
        private Func<TimeSpan>? sampleClock;

        public void Bind(EquippedWeaponSelection equipment, UnityEngine.UI.Button first, UnityEngine.UI.Button second,
            Func<TimeSpan> sampleCombatClock)
        {
            if (equipment == null)
                throw new ArgumentNullException(nameof(equipment));
            if (!first)
                throw new ArgumentNullException(nameof(first));
            if (!second)
                throw new ArgumentNullException(nameof(second));
            if (first == second)
                throw new ArgumentException("Each equipment slot needs a distinct button.", nameof(second));
            if (sampleCombatClock == null)
                throw new ArgumentNullException(nameof(sampleCombatClock));

            RemoveListeners();
            selection = equipment;
            firstSlot = first;
            secondSlot = second;
            sampleClock = sampleCombatClock;
            if (isActiveAndEnabled)
                AddListeners();
        }

        private void OnEnable() => AddListeners();
        private void OnDisable() => RemoveListeners();

        private void AddListeners()
        {
            if (firstSlot)
                firstSlot!.onClick.AddListener(SelectFirst);
            if (secondSlot)
                secondSlot!.onClick.AddListener(SelectSecond);
        }

        private void RemoveListeners()
        {
            if (firstSlot)
                firstSlot!.onClick.RemoveListener(SelectFirst);
            if (secondSlot)
                secondSlot!.onClick.RemoveListener(SelectSecond);
        }

        private void SelectFirst() => selection!.TrySwitch(0, sampleClock!());
        private void SelectSecond() => selection!.TrySwitch(1, sampleClock!());
    }
}
