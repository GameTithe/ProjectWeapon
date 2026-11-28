#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.WarehouseUI
{
    internal sealed class WarehouseView
    {
        private static readonly Color Panel = new Color32(35, 30, 45, 255);
        private static readonly Color Shelf = new Color32(45, 39, 57, 255);
        private static readonly Color Row = new Color32(59, 51, 73, 255);
        private static readonly Color Selected = new Color32(113, 76, 58, 255);
        private static readonly Color Ink = new Color32(247, 237, 223, 255);
        private static readonly Color Gold = new Color32(247, 189, 118, 255);
        private static readonly Color Muted = new Color32(198, 188, 207, 255);

        private readonly RectTransform root;
        private readonly RectTransform content;
        private readonly RectTransform confirmOverlay;
        private readonly TMP_FontAsset font;
        private readonly TextMeshProUGUI coins;
        private readonly TextMeshProUGUI itemName;
        private readonly TextMeshProUGUI status;
        private readonly TextMeshProUGUI empty;
        private readonly TextMeshProUGUI quote;
        private readonly TextMeshProUGUI confirmationStatus;
        private readonly Action<WeaponInstanceId> select;
        private readonly List<ItemButton> rows = new List<ItemButton>();
        private readonly ActionButton[] equipment;
        private readonly ActionButton store;
        private readonly ActionButton sell;

        public WarehouseView(RectTransform parent, TMP_FontAsset font, Action<WeaponInstanceId> select,
            Action<int> useSlot, Action storeSelected, Action prepareSale, Action confirm, Action cancel, Action close)
        {
            this.font = font;
            this.select = select;
            root = Rect(parent, "WarehouseModal", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fill(root, new Color(0.04f, 0.03f, 0.06f, .82f), receivesRaycasts: true);
            RectTransform card = Rect(root, "WarehouseCard", new Vector2(.06f, .07f), new Vector2(.94f, .93f),
                Vector2.zero, Vector2.zero);
            Fill(card, Panel);

            Text(Top(card, "Title", 14, 46, 24), "창고", 32, Gold);
            var coinArea = Rect(card, "Coins", new Vector2(.45f, 1), Vector2.one,
                new Vector2(0, -56), new Vector2(-131, -16));
            coins = Text(coinArea, string.Empty, 22, Ink, TextAlignmentOptions.MidlineRight);
            var closeArea = Rect(card, "Close", Vector2.one, Vector2.one, new Vector2(-108, -56), new Vector2(-24, -16));
            MakeButton(closeArea, "닫기", close, 17);

            RectTransform left = Rect(card, "Stock", Vector2.zero, new Vector2(.55f, 1),
                new Vector2(24, 0), new Vector2(-12, 0));
            Text(Top(left, "StockTitle", 72, 28), "미반입 보관", 22, Ink);
            var scroll = Rect(left, "StockView", Vector2.zero, Vector2.one,
                new Vector2(0, 61), new Vector2(0, -109));
            content = BuildScroll(scroll);
            empty = Text(Rect(scroll, "EmptyStock", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "창고에 보관 중인 무기가 없습니다.", 17, Muted, TextAlignmentOptions.Center);

            RectTransform right = Rect(card, "Loadout", new Vector2(.55f, 0), Vector2.one,
                new Vector2(12, 0), new Vector2(-24, 0));
            Text(Top(right, "EquipmentTitle", 72, 28), "장착", 22, Ink);
            var slots = Top(right, "Slots", 109, 67);
            var horizontal = slots.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            horizontal.spacing = 8;
            horizontal.childControlWidth = horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = horizontal.childForceExpandHeight = true;
            equipment = new ActionButton[InventorySlotCapacity.Prototype.EquippedSlots];
            for (int slot = 0; slot < equipment.Length; slot++)
            {
                int index = slot;
                RectTransform button = Rect(slots, $"EquipSlot{slot}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                equipment[index] = MakeButton(button, string.Empty, () => useSlot(index), 17);
            }

            itemName = Text(Top(right, "SelectedItem", 191, 34), "무기 선택", 22, Gold);
            store = MakeButton(Top(right, "Store", 234, 41), "창고에 넣기", storeSelected, 18);
            sell = MakeButton(Top(right, "PrepareSale", 285, 41), "판매", prepareSale, 18);
            sell.Button.image.color = Selected;

            Text(Top(right, "LoanNotice", 342, 39), "대여품은 보관하거나 판매할 수 없습니다.", 16, Muted);
            status = Text(Rect(card, "Status", Vector2.zero, Vector2.right,
                new Vector2(24, 11), new Vector2(-24, 53)), string.Empty, 16, Gold);

            confirmOverlay = Rect(root, "SaleConfirmation", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Fill(confirmOverlay, new Color(0.02f, 0.015f, 0.03f, .78f), receivesRaycasts: true);
            var confirmCard = Rect(confirmOverlay, "ConfirmCard", new Vector2(.24f, .26f), new Vector2(.76f, .74f),
                Vector2.zero, Vector2.zero);
            Fill(confirmCard, Panel);
            Text(Top(confirmCard, "ConfirmTitle", 17, 35, 22), "무기 판매", 27, Gold);
            quote = Text(Top(confirmCard, "SaleQuote", 62, 39, 22), string.Empty, 23, Ink);
            Text(Top(confirmCard, "SaleConsequence", 106, 30, 22), "판매하면 이 무기가 제거됩니다.", 17, Muted);
            confirmationStatus = Text(Top(confirmCard, "SaleStatus", 140, 36, 22), string.Empty, 16, Gold);
            MakeButton(Rect(confirmCard, "CancelSale", Vector2.zero, new Vector2(.50f, 0),
                new Vector2(22, 15), new Vector2(-4, 61)), "취소", cancel, 18);
            var commit = MakeButton(Rect(confirmCard, "ConfirmSale", new Vector2(.50f, 0), Vector2.right,
                new Vector2(4, 15), new Vector2(-22, 61)), "판매 확정", confirm, 18);
            commit.Button.image.color = Selected;

            SetOpen(false);
        }

        public bool IsOpen => root.gameObject.activeSelf;

        public void SetOpen(bool open)
        {
            confirmOverlay.gameObject.SetActive(false);
            root.gameObject.SetActive(open);
        }

        public void SetSlot(int index, string caption, bool interactable, bool active)
        {
            ActionButton button = equipment[index];
            button.Caption.text = $"{index + 1}번 슬롯\n{caption}";
            button.Button.interactable = interactable;
            button.Button.image.color = active ? Selected : Row;
        }

        public void SetDetails(long? balance, string selection, bool canStore, bool canSell, long price)
        {
            coins.text = balance.HasValue ? $"확정 코인 {balance.Value:N0}" : "확정 코인 확인 불가";
            itemName.text = selection;
            store.Button.interactable = canStore;
            sell.Button.interactable = canSell;
            sell.Caption.text = canSell ? $"판매 · {price:N0} 코인" : "판매";
        }

        public void SetStatus(string message) => status.text = message;

        public void SetConfirmation(string? caption, string message)
        {
            confirmOverlay.gameObject.SetActive(caption != null);
            if (caption != null)
            {
                quote.text = caption;
                confirmationStatus.text = message;
            }
        }

        public void SetWarehouse(IReadOnlyList<WeaponInstance> items, Func<WeaponInstance, string> name,
            WeaponInstanceId active)
        {
            empty.gameObject.SetActive(items.Count == 0);
            while (rows.Count < items.Count)
            {
                var row = new ItemButton();
                RectTransform target = Rect(content, "WarehouseWeapon", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                target.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 43;
                row.Action = MakeButton(target, string.Empty, () => select(row.Id), 18);
                row.Action.Caption.alignment = TextAlignmentOptions.MidlineLeft;
                row.Action.Caption.margin = new Vector4(14, 0, 10, 0);
                rows.Add(row);
            }

            for (int index = 0; index < rows.Count; index++)
            {
                ItemButton row = rows[index];
                bool visible = index < items.Count;
                row.Action.Button.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                row.Id = items[index].Id;
                row.Action.Caption.text = name(items[index]);
                row.Action.Button.image.color = row.Id == active ? Selected : Row;
            }
        }

        private static RectTransform BuildScroll(RectTransform frame)
        {
            Fill(frame, Shelf, receivesRaycasts: true);
            var scroll = frame.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            RectTransform viewport = Rect(frame, "Viewport", Vector2.zero, Vector2.one,
                new Vector2(6, 6), new Vector2(-6, -6));
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            RectTransform scrollContent = Rect(viewport, "Content", Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            scrollContent.pivot = new Vector2(.5f, 1);
            var layout = scrollContent.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 5;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            scrollContent.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = scrollContent;
            scroll.horizontal = false;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36;
            return scrollContent;
        }

        private ActionButton MakeButton(RectTransform rect, string caption, Action clicked, int size)
        {
            var image = Fill(rect, Row, receivesRaycasts: true);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => clicked());
            var label = Text(Rect(rect, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                caption, size, Ink, TextAlignmentOptions.Center);
            return new ActionButton(button, label);
        }

        private TextMeshProUGUI Text(RectTransform rect, string value, int size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = value;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static UnityEngine.UI.Image Fill(RectTransform rect, Color color, bool receivesRaycasts = false)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = receivesRaycasts;
            return image;
        }

        private static RectTransform Top(RectTransform parent, string name, int top, int height, int inset = 0)
            => Rect(parent, name, Vector2.up, Vector2.one, new Vector2(inset, -top - height), new Vector2(-inset, -top));

        private static RectTransform Rect(RectTransform parent, string name, Vector2 min, Vector2 max,
            Vector2 lower, Vector2 upper)
        {
            var item = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)item.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = lower;
            rect.offsetMax = upper;
            return rect;
        }

        private readonly struct ActionButton
        {
            public ActionButton(UnityEngine.UI.Button button, TextMeshProUGUI caption)
            {
                Button = button;
                Caption = caption;
            }

            public UnityEngine.UI.Button Button { get; }
            public TextMeshProUGUI Caption { get; }
        }

        private sealed class ItemButton
        {
            public WeaponInstanceId Id { get; set; }
            public ActionButton Action { get; set; }
        }
    }
}
