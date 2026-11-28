#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.RunBagUI
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class RunBagPanel : MonoBehaviour
    {
        private RunBagActions actions = null!;
        private RunBagView? view;
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> definitions = null!;
        private WeaponInstance?[] displayed = Array.Empty<WeaponInstance?>();
        private WeaponLocation[] slots = Array.Empty<WeaponLocation>();
        private int selected = -1;
        private int closedAtFrame = -1;

        public bool IsOpen => view?.IsOpen == true;
        public bool OwnsGameplayInput => IsOpen || closedAtFrame == Time.frameCount;

        public void Initialize(RunBagActions actions, WeaponCatalog catalog, TMP_FontAsset font)
        {
            if (view != null)
                throw new InvalidOperationException("This run bag is already initialized.");
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
            if (!catalog)
                throw new ArgumentNullException(nameof(catalog));
            if (!font)
                throw new ArgumentNullException(nameof(font));
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                throw new InvalidOperationException("Import TMP Essential Resources before initializing the run bag.");
            definitions = catalog.CreateLookup();
            InventorySlotCapacity capacity = actions.Capacity;
            slots = Enumerable.Range(0, capacity.EquippedSlots).Select(WeaponLocation.Equipped)
                .Concat(Enumerable.Range(0, capacity.CarriedSlots).Select(WeaponLocation.Carried)).ToArray();
            displayed = new WeaponInstance?[slots.Length];
            view = new RunBagView((RectTransform)transform, font, capacity, ClickSlot, Drop, Close);
        }

        public bool Open()
        {
            if (view == null || !actions.CanOpen)
                return false;

            selected = -1;
            view.SetOpen(true);
            Refresh();
            return true;
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            closedAtFrame = Time.frameCount;
            selected = -1;
            view!.SetOpen(false);
        }

        /// <summary>Refresh after gameplay changes the inventory or the run phase.</summary>
        public void Refresh()
        {
            if (!IsOpen)
                return;
            if (!actions.CanOpen)
            {
                Close();
                return;
            }
            Draw();
        }

        private void Update()
        {
            if (IsOpen && !actions.CanOpen)
                Close();
        }

        private void OnDisable() => Close();

        private void ClickSlot(int index)
        {
            if (!CanRequestChange())
                return;

            if (selected < 0 || index == selected)
            {
                selected = selected == index || displayed[index] == null ? -1 : index;
                Draw();
                return;
            }

            WeaponInstance source = displayed[selected]!;
            WeaponLocation destination = slots[index];
            bool equipped = source.Location.Kind == WeaponLocationKind.Equipped
                || destination.Kind == WeaponLocationKind.Equipped;
            if (equipped && !actions.CanChangeEquippedSlots)
            {
                Draw("현재 장착 무기를 바꿀 수 없습니다.");
                return;
            }

            InventoryMoveResult result = actions.Move(source.Id, source.Location, destination, displayed[index]?.Id);
            selected = -1;
            Draw(result == InventoryMoveResult.Moved ? "무기를 옮겼습니다." : "가방이 바뀌었습니다. 다시 선택하세요.");
        }

        private void Drop()
        {
            if (!CanRequestChange() || selected < 0 || displayed[selected] is not WeaponInstance source)
                return;
            if (source.Location.Kind == WeaponLocationKind.Equipped && !actions.CanChangeEquippedSlots)
            {
                Draw("현재 장착 무기를 버릴 수 없습니다.");
                return;
            }

            InventoryMoveResult result = actions.Drop(source.Id, source.Location);
            if (result == InventoryMoveResult.Moved)
                selected = -1;
            Draw(result switch
            {
                InventoryMoveResult.Moved => "무기를 떨어뜨렸습니다.",
                InventoryMoveResult.DestinationRejected => "여기에는 무기를 버릴 수 없습니다.",
                _ => "가방이 바뀌었습니다. 다시 선택하세요."
            });
        }

        private bool CanRequestChange()
        {
            if (!IsOpen || !actions.CanOpen)
            {
                Close();
                return false;
            }
            if (actions.CanChangeSlots)
                return true;

            Draw();
            return false;
        }

        private void Draw(string? message = null)
        {
            WeaponInstanceId? previousSelection = selected >= 0 ? displayed[selected]?.Id : null;
            Array.Clear(displayed, 0, displayed.Length);
            foreach (WeaponInstance item in actions.Snapshot())
            {
                if (item.Location.Kind == WeaponLocationKind.Equipped && item.Location.SlotIndex is int equipped)
                    displayed[equipped] = item;
                if (item.Location.Kind == WeaponLocationKind.Carried && item.Location.SlotIndex is int carried)
                    displayed[actions.Capacity.EquippedSlots + carried] = item;
            }
            if (selected >= 0 && (displayed[selected] == null || displayed[selected]?.Id != previousSelection))
                selected = -1;

            bool editable = actions.CanChangeSlots;
            for (int index = 0; index < slots.Length; index++)
            {
                WeaponInstance? item = displayed[index];
                string label = item != null ? DisplayName(item) : "빈 슬롯";
                view!.SetSlot(index, label, editable && (selected >= 0 || item != null), index == selected);
            }
            view!.SetDrop(editable && selected >= 0);
            view.SetStatus(message ?? (!editable ? "귀환 중에는 가방을 변경할 수 없습니다."
                : selected < 0 ? "옮기거나 버릴 무기를 선택하세요." : "이동할 슬롯을 선택하거나 무기를 버리세요."));
        }

        private string DisplayName(WeaponInstance item)
        {
            string name = definitions.TryGetValue(item.DefinitionId, out WeaponDefinition definition)
                ? definition.DisplayName : item.DefinitionId.Value;
            return item.IsLoaned ? $"{name} · 대여" : name;
        }
    }
}
