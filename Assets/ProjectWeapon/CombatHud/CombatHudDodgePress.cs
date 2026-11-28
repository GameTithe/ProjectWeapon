#nullable enable

using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectWeapon.CombatHud
{
    /// <summary>Dodge is accepted when a touch begins. Holding or releasing the touch has no effect.</summary>
    internal sealed class CombatHudDodgePress : MonoBehaviour, IPointerDownHandler, ISubmitHandler
    {
        internal Action? Begin { get; set; }

        public void OnPointerDown(PointerEventData pointer)
        {
            if (pointer.button == PointerEventData.InputButton.Left)
                Begin?.Invoke();
        }

        public void OnSubmit(BaseEventData data) => Begin?.Invoke();
    }
}
