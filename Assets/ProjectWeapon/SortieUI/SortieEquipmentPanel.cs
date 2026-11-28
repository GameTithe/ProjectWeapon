#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.SortieUI
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class SortieEquipmentPanel : MonoBehaviour
    {
        private const string Empty = "빈 슬롯";
        private const string Blocked = "현재 출격할 수 없습니다.";
        private const string FillCarry = "운반품을 비운 뒤 출격하세요.";
        private static readonly InventorySlotCapacity Layout = InventorySlotCapacity.Prototype;

        private InventoryService inventory = null!;
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> definitions = null!;
        private SortieEquipmentView? view;
        private Func<SortieStartResult> tryStart = null!;
        private Func<bool> isAvailable = null!;
        private int selectedSlot;
        private bool submitting;

        public bool IsOpen => view?.IsOpen == true;

        public event Action<SortieManifest>? Confirmed;
        public event Action? Dismissed;

        /// <param name="available">The host's current availability check, including the selected zone.</param>
        /// <param name="confirm">Starts the run using the host's sortie API and returns the accepted manifest.</param>
        public void Initialize(InventoryService service, WeaponCatalog catalog, TMP_FontAsset font,
            Func<bool> available, Func<SortieStartResult> confirm)
        {
            if (view != null)
                throw new InvalidOperationException("This sortie panel is already initialized.");
            inventory = service ?? throw new ArgumentNullException(nameof(service));
            if (!catalog)
                throw new ArgumentNullException(nameof(catalog));
            if (!font)
                throw new ArgumentNullException(nameof(font));
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                throw new InvalidOperationException("Import TMP Essential Resources before initializing the sortie panel.");

            isAvailable = available ?? throw new ArgumentNullException(nameof(available));
            tryStart = confirm ?? throw new ArgumentNullException(nameof(confirm));
            definitions = catalog.CreateLookup();
            view = new SortieEquipmentView((RectTransform)transform, font, Layout, SelectSlot, Remove, PutOn, Confirm, Close);
        }

        public void Open()
        {
            if (view == null)
                throw new InvalidOperationException("Initialize the sortie panel before opening it.");
            if (submitting)
                return;

            selectedSlot = 0;
            view.SetOpen(true);
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen || submitting)
                return;

            view!.SetOpen(false);
            Dismissed?.Invoke();
        }

        /// <summary>Refresh after the host changes its zone availability or inventory outside this panel.</summary>
        public void Refresh()
        {
            if (IsOpen)
                Draw(ReadLoadout());
        }

        private void SelectSlot(int slot)
        {
            if (!IsOpen || submitting || slot < 0 || slot >= SupportedSlots)
                return;

            selectedSlot = slot;
            Refresh();
        }

        private void PutOn(WeaponInstanceId id)
        {
            if (!IsOpen || submitting || selectedSlot >= SupportedSlots)
                return;

            Loadout loadout = ReadLoadout();
            if (loadout.HasReservation || loadout.HasLoan || loadout.Equipped[selectedSlot] != null
                || !loadout.Warehouse.Any(item => item.Id == id))
            {
                Draw(loadout, "장비 목록이 바뀌었습니다. 다시 선택하세요.");
                return;
            }

            var result = inventory.Move(id, WeaponLocation.Warehouse(), WeaponLocation.Equipped(selectedSlot));
            loadout = ReadLoadout();
            if (result == InventoryMoveResult.Moved)
            {
                for (int slot = 0; slot < SupportedSlots; slot++)
                {
                    if (loadout.Equipped[slot] == null)
                    {
                        selectedSlot = slot;
                        break;
                    }
                }
            }

            Draw(loadout, result == InventoryMoveResult.Moved ? null : "장착할 수 없습니다. 다시 선택하세요.");
        }

        private void Remove()
        {
            if (!IsOpen || submitting || selectedSlot >= SupportedSlots)
                return;

            Loadout loadout = ReadLoadout();
            WeaponInstance? item = loadout.Equipped[selectedSlot];
            if (item == null || item.IsLoaned || loadout.HasReservation)
            {
                Draw(loadout);
                return;
            }

            var result = inventory.Move(item.Id, WeaponLocation.Equipped(selectedSlot), WeaponLocation.Warehouse());
            Draw(ReadLoadout(), result == InventoryMoveResult.Moved ? null : "장비를 뺄 수 없습니다. 다시 선택하세요.");
        }

        private void Confirm()
        {
            if (!IsOpen || submitting)
                return;

            Loadout loadout = ReadLoadout();
            string? reason = UnavailableReason(loadout);
            if (reason != null)
            {
                Draw(loadout, reason);
                return;
            }

            submitting = true;
            SortieStartResult result;
            try
            {
                result = tryStart();
            }
            finally
            {
                submitting = false;
            }

            if (result.Status == SortieStartStatus.Started && result.Manifest != null)
            {
                view!.SetOpen(false);
                Confirmed?.Invoke(result.Manifest);
                return;
            }

            string error = result.Status switch
            {
                SortieStartStatus.LoadoutNotReady => "장비 상태가 바뀌었습니다. 확인 후 다시 시도하세요.",
                SortieStartStatus.InventoryAlreadyReserved => "이미 출격 중입니다.",
                _ => "출격을 시작하지 못했습니다. 다시 시도하세요."
            };
            Draw(ReadLoadout(), error);
        }

        private void Draw(Loadout loadout, string? message = null)
        {
            string? unavailable = UnavailableReason(loadout);
            bool canEdit = !loadout.HasReservation && SupportedSlots > 0;
            bool canEquip = canEdit && loadout.Equipped[selectedSlot] == null && !loadout.HasLoan;
            bool showLoan = SupportedSlots > 0 && !loadout.Equipped.Any(item => item != null);
            for (int slot = 0; slot < Layout.EquippedSlots; slot++)
            {
                WeaponInstance? equipped = loadout.Equipped[slot];
                string label = slot >= SupportedSlots ? "사용 불가" : equipped == null ? Empty : Name(equipped);
                if (showLoan && slot == 0)
                    label = "낡은 검\n무료 대여 예정";
                view!.SetEquipment(slot, label, canEdit && slot < SupportedSlots, selectedSlot == slot);
            }

            for (int slot = 0; slot < Layout.CarriedSlots; slot++)
            {
                string label = slot >= inventory.Capacity.CarriedSlots ? "사용 불가"
                    : loadout.OccupiedCarry[slot] ? "비워야 함" : "전리품 공간";
                view!.SetCarry(slot, label);
            }

            WeaponInstance? selected = loadout.Equipped[selectedSlot];
            view!.SetWarehouse(loadout.Warehouse, Name, canEquip);
            view.SetActions(canEdit && selected != null && !selected.IsLoaned, unavailable == null);
            string? hint = null;
            if (canEquip && loadout.Warehouse.Count > 0)
                hint = "장착할 창고 무기를 선택하세요.";
            else if (selected != null && !selected.IsLoaned)
                hint = "장비를 빼거나 빈 장착 슬롯을 선택하세요.";
            view.SetStatus(message ?? unavailable ?? hint ?? string.Empty);
        }

        private string? UnavailableReason(Loadout loadout)
        {
            if (loadout.HasReservation)
                return "이미 출격 중입니다.";
            if (loadout.HasCarry)
                return FillCarry;
            if (SupportedSlots == 0 || loadout.HasOutsideSlot || !isAvailable())
                return Blocked;
            return null;
        }

        private int SupportedSlots => Math.Min(Layout.EquippedSlots, inventory.Capacity.EquippedSlots);

        private Loadout ReadLoadout()
        {
            var loadout = new Loadout();
            foreach (WeaponInstance item in inventory.Snapshot())
            {
                int slot = item.Location.SlotIndex.GetValueOrDefault();
                switch (item.Location.Kind)
                {
                    case WeaponLocationKind.Equipped when slot < Layout.EquippedSlots:
                        loadout.Equipped[slot] = item;
                        break;
                    case WeaponLocationKind.Equipped:
                        loadout.HasOutsideSlot = true;
                        break;
                    case WeaponLocationKind.Carried:
                        loadout.HasCarry = true;
                        if (slot < Layout.CarriedSlots)
                            loadout.OccupiedCarry[slot] = true;
                        break;
                    case WeaponLocationKind.Warehouse:
                        loadout.Warehouse.Add(item);
                        break;
                    case WeaponLocationKind.RunReserved:
                        loadout.HasReservation = true;
                        break;
                }
            }

            loadout.Warehouse.Sort((first, second) =>
            {
                int byName = StringComparer.Ordinal.Compare(Name(first), Name(second));
                return byName != 0 ? byName : StringComparer.Ordinal.Compare(first.Id.ToString(), second.Id.ToString());
            });
            return loadout;
        }

        private string Name(WeaponInstance item)
        {
            string name = definitions.TryGetValue(item.DefinitionId, out WeaponDefinition definition)
                ? definition.DisplayName : item.DefinitionId.Value;
            return item.IsLoaned ? $"{name} (대여품)" : name;
        }

        private sealed class Loadout
        {
            public readonly WeaponInstance?[] Equipped = new WeaponInstance?[Layout.EquippedSlots];
            public readonly bool[] OccupiedCarry = new bool[Layout.CarriedSlots];
            public readonly List<WeaponInstance> Warehouse = new List<WeaponInstance>();
            public bool HasLoan => Equipped.Any(item => item?.IsLoaned == true);
            public bool HasReservation;
            public bool HasCarry;
            public bool HasOutsideSlot;
        }
    }
}
