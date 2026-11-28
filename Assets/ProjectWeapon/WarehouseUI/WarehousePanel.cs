#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectWeapon.Inventory;
using ProjectWeapon.Run;
using ProjectWeapon.Save;
using ProjectWeapon.WeaponSale;
using ProjectWeapon.Weapons;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.WarehouseUI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class WarehousePanel : MonoBehaviour
    {
        private IReadOnlyDictionary<WeaponDefinitionId, WeaponDefinition> definitions = null!;
        private WeaponSalePrices prices = null!;
        private RunState run = null!;
        private Func<SaveSnapshot?> readSnapshot = null!;
        private Func<SaveSnapshot, IReadOnlyList<WeaponInstance>, SaveSnapshot?> commitMove = null!;
        private Func<Guid, WeaponInstanceId, WeaponSaleResult> sell = null!;
        private WarehouseView? view;
        private WeaponInstanceId selected;
        private SaleQuote? pending;
        private bool submitting;

        public SaveSnapshot? ActiveSnapshot { get; private set; }
        public bool IsOpen => view?.IsOpen == true;
        public event Action? Closed;

        /// <param name="commitMove">Persist the proposed positions only against the supplied snapshot revision.
        /// Return the committed snapshot on success or null on rejection or conflict.</param>
        /// <param name="sell">Bind WeaponSaleService.TrySell, using the same catalog and price asset.</param>
        public void Initialize(WeaponCatalog catalog, TMP_FontAsset font, WeaponSalePrices prices, RunState run,
            Func<SaveSnapshot?> readSnapshot, Func<SaveSnapshot, IReadOnlyList<WeaponInstance>, SaveSnapshot?> commitMove,
            Func<Guid, WeaponInstanceId, WeaponSaleResult> sell)
        {
            if (view != null)
                throw new InvalidOperationException("The warehouse is already initialized.");
            if (!catalog)
                throw new ArgumentNullException(nameof(catalog));
            if (!font)
                throw new ArgumentNullException(nameof(font));
            if (!prices)
                throw new ArgumentNullException(nameof(prices));
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                throw new InvalidOperationException("Import TMP Essential Resources before opening the warehouse.");

            definitions = catalog.CreateLookup();
            this.prices = prices;
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            this.readSnapshot = readSnapshot ?? throw new ArgumentNullException(nameof(readSnapshot));
            this.commitMove = commitMove ?? throw new ArgumentNullException(nameof(commitMove));
            this.sell = sell ?? throw new ArgumentNullException(nameof(sell));
            view = new WarehouseView((RectTransform)transform, font, Select, UseSlot, StoreSelected,
                OpenConfirmation, Confirm, Cancel, Close);
        }

        public void Open()
        {
            if (view == null)
                throw new InvalidOperationException("Initialize the warehouse before opening it.");
            if (IsOpen)
                return;

            selected = default;
            pending = null;
            view.SetOpen(true);
            Refresh();
        }

        public void Refresh()
        {
            if (!IsOpen || submitting)
                return;

            Render(Reload() ? string.Empty : "창고 저장을 읽지 못했습니다.");
        }

        public void Close()
        {
            if (!IsOpen || submitting)
                return;

            pending = null;
            view!.SetOpen(false);
            Closed?.Invoke();
        }

        private bool CanAct => IsOpen && !submitting && pending == null;
        private bool CanEdit => ActiveSnapshot != null && run.Phase == RunPhase.Town
            && ActiveSnapshot.Weapons.All(item => item.Location.Kind != WeaponLocationKind.RunReserved);

        private bool Reload()
        {
            try
            {
                ActiveSnapshot = readSnapshot();
            }
            catch (IOException)
            {
                ActiveSnapshot = null;
            }
            catch (UnauthorizedAccessException)
            {
                ActiveSnapshot = null;
            }
            return ActiveSnapshot != null;
        }

        private WeaponInstance? Selected() => ActiveSnapshot?.Weapons.FirstOrDefault(item => item.Id == selected);

        private void Select(WeaponInstanceId id)
        {
            if (!CanAct)
                return;

            selected = id;
            Render();
        }

        private void UseSlot(int slot)
        {
            if (!CanAct || ActiveSnapshot == null)
                return;

            WeaponInstance? equipped = InSlot(slot);
            if (equipped != null)
                Select(equipped.Id);
            else if (Selected() is { } item && item.Location.Kind == WeaponLocationKind.Warehouse)
                Move(item, WeaponLocation.Equipped(slot));
        }

        private void StoreSelected()
        {
            if (CanAct && Selected() is { } item && item.Location.Kind == WeaponLocationKind.Equipped)
                Move(item, WeaponLocation.Warehouse());
        }

        private void Move(WeaponInstance item, WeaponLocation destination)
        {
            if (!CanEdit || !CanAct || item.IsLoaned)
                return;

            SaveSnapshot expected = ActiveSnapshot!;
            var staged = new InventoryService(expected.Weapons);
            if (staged.Move(item.Id, item.Location, destination) != InventoryMoveResult.Moved)
            {
                Render(Reload() ? "장착 상태가 바뀌었습니다." : "창고 저장을 읽지 못했습니다.");
                return;
            }

            submitting = true;
            SaveSnapshot? committed = null;
            try
            {
                committed = commitMove(expected, staged.Snapshot());
            }
            catch (IOException)
            {
                // Read the authoritative result even when the write may have completed.
            }
            catch (UnauthorizedAccessException)
            {
            }
            finally
            {
                submitting = false;
            }

            if (committed != null && committed.Revision > expected.Revision)
            {
                ActiveSnapshot = committed;
                Render(destination.Kind == WeaponLocationKind.Warehouse ? "창고에 보관했습니다." : "장착했습니다.");
                return;
            }

            Render(Reload() ? "최신 장비 상태를 불러왔습니다." : "창고 저장을 읽지 못했습니다.");
        }

        private void OpenConfirmation()
        {
            if (!CanAct)
                return;

            if (!Reload())
            {
                Render("창고 저장을 읽지 못했습니다.");
                return;
            }

            WeaponInstance? item = Selected();
            if (!TryQuote(item, out long quotedPrice))
            {
                Render("현재 이 무기를 판매할 수 없습니다.");
                return;
            }

            pending = new SaleQuote(Guid.NewGuid(), item!.Id, item.DefinitionId, Name(item), quotedPrice);
            Render();
        }

        private void Cancel()
        {
            if (!IsOpen || submitting || pending == null)
                return;

            pending = null;
            Refresh();
        }

        private void Confirm()
        {
            if (!IsOpen || submitting || pending is not SaleQuote quote)
                return;
            if (!Reload())
            {
                Render("저장 상태를 확인하지 못했습니다. 다시 시도하세요.");
                return;
            }

            // A persisted receipt permits replay even when the item is no longer in the warehouse.
            if (!ActiveSnapshot!.TryGetWeaponSale(quote.RequestId, out _))
            {
                WeaponInstance? item = ActiveSnapshot.Weapons.FirstOrDefault(weapon => weapon.Id == quote.WeaponId);
                if (!TryQuote(item, out long price) || price != quote.Price || !item!.DefinitionId.Equals(quote.Definition))
                {
                    pending = null;
                    Render("무기 상태나 가격이 바뀌었습니다. 판매를 다시 확인하세요.");
                    return;
                }
            }

            submitting = true;
            WeaponSaleResult result;
            try
            {
                result = sell(quote.RequestId, quote.WeaponId);
            }
            catch (IOException)
            {
                Render("판매 응답을 확인하지 못했습니다. 다시 시도하세요.");
                return;
            }
            catch (UnauthorizedAccessException)
            {
                Render("판매 응답을 확인하지 못했습니다. 다시 시도하세요.");
                return;
            }
            finally
            {
                submitting = false;
            }

            if ((result.Status == WeaponSaleStatus.Sold || result.Status == WeaponSaleStatus.AlreadySold)
                && result.ActiveSnapshot != null && result.Receipt is { } receipt
                && receipt.RequestId == quote.RequestId && receipt.InstanceId == quote.WeaponId)
            {
                AdoptSnapshot(result.ActiveSnapshot);
                pending = null;
                selected = default;
                Render($"판매 완료 · {receipt.Price:N0} 코인");
                return;
            }

            if (result.Status != WeaponSaleStatus.Sold && result.Status != WeaponSaleStatus.AlreadySold)
                AdoptSnapshot(result.ActiveSnapshot);
            if (result.Status == WeaponSaleStatus.RevisionConflict)
            {
                Render("창고가 갱신됐습니다. 판매를 다시 시도하세요.");
                return;
            }

            pending = null;
            Render(FailureText(result.Status));
        }

        private void AdoptSnapshot(SaveSnapshot? snapshot)
        {
            if (snapshot != null && (ActiveSnapshot == null || snapshot.Revision >= ActiveSnapshot.Revision))
                ActiveSnapshot = snapshot;
        }

        private bool TryQuote(WeaponInstance? item, out long price)
        {
            price = 0;
            if (!CanEdit || item == null || item.IsLoaned || item.Location.Kind != WeaponLocationKind.Warehouse
                || !definitions.TryGetValue(item.DefinitionId, out WeaponDefinition definition)
                || !definition || definition.Grade < 1 || definition.Grade > 5)
                return false;

            bool hasDuplicate = ActiveSnapshot!.Weapons.Any(weapon => weapon.Id != item.Id && !weapon.IsLoaned
                && weapon.DefinitionId.Equals(item.DefinitionId)
                && (weapon.Location.Kind == WeaponLocationKind.Warehouse || weapon.Location.Kind == WeaponLocationKind.Equipped));
            if (!hasDuplicate)
                return false;

            price = prices.GetPrice(definition.Grade);
            return price > 0;
        }

        private WeaponInstance? InSlot(int slot) => ActiveSnapshot?.Weapons.FirstOrDefault(item =>
            item.Location.Kind == WeaponLocationKind.Equipped && item.Location.SlotIndex == slot);

        private string Name(WeaponInstance item) => definitions.TryGetValue(item.DefinitionId, out WeaponDefinition value)
            && value ? value.DisplayName : item.DefinitionId.Value;

        private void Render(string message = "")
        {
            WeaponInstance? item = Selected();
            bool canMove = CanEdit && item != null && !item.IsLoaned;
            bool canEquip = canMove && item!.Location.Kind == WeaponLocationKind.Warehouse;
            bool canStore = canMove && item!.Location.Kind == WeaponLocationKind.Equipped;
            bool canSell = TryQuote(item, out long price);
            WeaponInstance[] stored = ActiveSnapshot?.Weapons.Where(weapon => weapon.Location.Kind == WeaponLocationKind.Warehouse
                && !weapon.IsLoaned).OrderBy(Name, StringComparer.Ordinal)
                .ThenBy(weapon => weapon.Id.ToString(), StringComparer.Ordinal).ToArray() ?? Array.Empty<WeaponInstance>();
            view!.SetWarehouse(stored, Name, selected);
            for (int slot = 0; slot < InventorySlotCapacity.Prototype.EquippedSlots; slot++)
            {
                WeaponInstance? equipped = InSlot(slot);
                string caption = equipped == null ? (canEquip ? "빈 슬롯에 장착" : "비어 있음")
                    : Name(equipped) + (equipped.IsLoaned ? "\n대여품" : string.Empty);
                view.SetSlot(slot, caption, equipped != null || canEquip, equipped != null && equipped.Id == selected);
            }
            view.SetDetails(ActiveSnapshot?.ConfirmedCoins, item == null ? "무기 선택"
                : Name(item) + (item.IsLoaned ? " · 대여품" : string.Empty), canStore, canSell, price);
            view.SetStatus(message.Length != 0 ? message : !CanEdit ? "지금은 장비를 변경할 수 없습니다."
                : canEquip && !canSell ? "현재 이 무기를 판매할 수 없습니다."
                : string.Empty);
            view.SetConfirmation(pending is SaleQuote quote ? $"{quote.Name} · {quote.Price:N0} 코인" : null, message);
        }

        private static string FailureText(WeaponSaleStatus status) => status switch
        {
            WeaponSaleStatus.NoOwnedDuplicate => "같은 무기를 한 개 남겨야 판매할 수 있습니다.",
            WeaponSaleStatus.ProvenanceRestricted => "대여품은 판매할 수 없습니다.",
            WeaponSaleStatus.NotInTown => "로비에서만 판매할 수 있습니다.",
            WeaponSaleStatus.MissingInstance or WeaponSaleStatus.InvalidLocation => "창고의 무기 상태가 바뀌었습니다.",
            WeaponSaleStatus.InvalidPrice or WeaponSaleStatus.StorageLimit => "현재 판매 가격을 적용할 수 없습니다.",
            _ => "판매를 완료하지 못했습니다."
        };

        private readonly struct SaleQuote
        {
            public SaleQuote(Guid requestId, WeaponInstanceId weaponId, WeaponDefinitionId definition, string name, long price)
            {
                RequestId = requestId;
                WeaponId = weaponId;
                Definition = definition;
                Name = name;
                Price = price;
            }

            public Guid RequestId { get; }
            public WeaponInstanceId WeaponId { get; }
            public WeaponDefinitionId Definition { get; }
            public string Name { get; }
            public long Price { get; }
        }
    }
}
