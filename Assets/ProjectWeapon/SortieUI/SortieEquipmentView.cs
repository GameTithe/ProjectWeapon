#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.SortieUI
{
    internal sealed class SortieEquipmentView
    {
        private static readonly Color Surface = new Color32(34, 29, 44, 255);
        private static readonly Color RowColor = new Color32(53, 48, 66, 255);
        private static readonly Color Selection = new Color32(116, 75, 55, 255);
        private static readonly Color Ink = new Color32(244, 232, 218, 255);
        private static readonly Color Gold = new Color32(246, 189, 126, 255);

        private readonly RectTransform root;
        private readonly RectTransform scrollContent;
        private readonly TMP_FontAsset font;
        private readonly CardButton[] equip;
        private readonly TextMeshProUGUI[] carry;
        private readonly List<WarehouseRow> warehouse = new List<WarehouseRow>();
        private readonly TextMeshProUGUI status;
        private readonly TextMeshProUGUI emptyWarehouse;
        private readonly UnityEngine.UI.Button remove;
        private readonly UnityEngine.UI.Button confirm;
        private readonly Action<WeaponInstanceId> putOn;

        public SortieEquipmentView(RectTransform parent, TMP_FontAsset font, InventorySlotCapacity layout,
            Action<int> select, Action remove, Action<WeaponInstanceId> putOn, Action confirm, Action close)
        {
            this.font = font;
            this.putOn = putOn;
            root = Rect(parent, "SortieEquipmentModal", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Background(root, new Color(0, 0, 0, .72f), true);
            var card = Rect(root, "Card", new Vector2(.07f, .06f), new Vector2(.93f, .94f),
                Vector2.zero, Vector2.zero);
            Background(card, Surface);
            Label(Top(card, "Title", 14, 46, 24), "출격 장비 선택", 31, Gold);

            var left = Rect(card, "Loadout", Vector2.zero, new Vector2(.48f, 1),
                new Vector2(24, 0), new Vector2(-12, 0));
            Label(Top(left, "EquipmentTitle", 67, 28, 0), "반입할 장비", 22, Ink);
            var equipRow = Top(left, "Equipment", 102, 68, 0);
            RowLayout(equipRow);
            equip = new CardButton[layout.EquippedSlots];
            for (int slot = 0; slot < equip.Length; slot++)
            {
                int index = slot;
                equip[slot] = Button(Rect(equipRow, $"Equipped{slot}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    string.Empty, () => select(index), 18);
            }

            this.remove = Button(Top(left, "Remove", 180, 40, 0), "선택 장비 빼기", remove, 18).Button;
            Label(Top(left, "CarryTitle", 225, 28, 0), "운반 슬롯", 22, Ink);
            var carryRow = Top(left, "Carry", 260, 37, 0);
            RowLayout(carryRow);
            carry = new TextMeshProUGUI[layout.CarriedSlots];
            for (int slot = 0; slot < carry.Length; slot++)
            {
                var tile = Rect(carryRow, $"CarryTile{slot}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                Background(tile, RowColor);
                carry[slot] = Label(Rect(tile, $"Carried{slot}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                    string.Empty, 16, Ink, TextAlignmentOptions.Center);
            }

            var right = Rect(card, "WarehouseArea", new Vector2(.48f, 0), Vector2.one,
                new Vector2(12, 0), new Vector2(-24, 0));
            Label(Top(right, "WarehouseTitle", 67, 28, 0), "창고 무기", 22, Ink);
            var listFrame = Rect(right, "Warehouse", Vector2.zero, Vector2.one, new Vector2(0, 115), new Vector2(0, -102));
            scrollContent = ScrollList(listFrame);
            emptyWarehouse = Label(Rect(listFrame, "WarehouseEmpty", Vector2.zero, Vector2.one,
                new Vector2(8, 0), new Vector2(-8, 0)), "보유 중인 창고 무기가 없습니다.", 18, Ink, TextAlignmentOptions.Center);

            var notice = Rect(card, "LossNotice", Vector2.zero, Vector2.right, new Vector2(24, 59), new Vector2(-24, 111));
            Label(notice, "사망·시간 초과 시 광고·코인으로 얻은 무기를 포함한 반입품과 전리품을 잃습니다.\n"
                + "무료 대여품은 창고에 보관할 수 없습니다.", 17, Gold);
            status = Label(Rect(card, "Status", Vector2.zero, new Vector2(.59f, 0),
                new Vector2(24, 10), new Vector2(-10, 49)), string.Empty, 17, Ink);
            Button(Rect(card, "Cancel", new Vector2(.59f, 0), new Vector2(.74f, 0),
                new Vector2(0, 10), new Vector2(-8, 49)), "닫기", close, 18);
            var start = Button(Rect(card, "Confirm", new Vector2(.74f, 0), Vector2.right,
                new Vector2(0, 10), new Vector2(-24, 49)), "출격 확인", confirm, 18);
            start.Button.image.color = Selection;
            this.confirm = start.Button;

            SetOpen(false);
        }

        public bool IsOpen => root.gameObject.activeSelf;
        public void SetOpen(bool open) => root.gameObject.SetActive(open);
        public void SetStatus(string message) => status.text = message;

        public void SetEquipment(int slot, string weaponName, bool allowed, bool selected)
        {
            CardButton card = equip[slot];
            card.Text.text = $"{slot + 1}번 장착\n{weaponName}";
            card.Button.interactable = allowed;
            card.Button.image.color = selected ? Selection : RowColor;
        }

        public void SetCarry(int slot, string text) => carry[slot].text = text;

        public void SetActions(bool canRemove, bool canStart)
        {
            remove.interactable = canRemove;
            confirm.interactable = canStart;
        }

        public void SetWarehouse(IReadOnlyList<WeaponInstance> items, Func<WeaponInstance, string> displayName,
            bool available)
        {
            emptyWarehouse.gameObject.SetActive(items.Count == 0);
            while (warehouse.Count < items.Count)
                AddWarehouseRow();

            for (int index = 0; index < warehouse.Count; index++)
            {
                WarehouseRow row = warehouse[index];
                bool visible = index < items.Count;
                row.Card.Button.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                WeaponInstance item = items[index];
                row.Id = item.Id;
                row.Card.Text.text = displayName(item);
                row.Card.Button.interactable = available;
            }
        }

        private void AddWarehouseRow()
        {
            var row = new WarehouseRow();
            var rect = Rect(scrollContent, "WarehouseWeapon", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 44;
            row.Card = Button(rect, string.Empty, () => putOn(row.Id), 18);
            warehouse.Add(row);
        }

        private RectTransform ScrollList(RectTransform frame)
        {
            Background(frame, new Color32(40, 35, 51, 255), true);
            var scroll = frame.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            var viewport = Rect(frame, "Viewport", Vector2.zero, Vector2.one, new Vector2(6, 5), new Vector2(-6, -5));
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var content = Rect(viewport, "Content", Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(.5f, 1);
            var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 5;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            return content;
        }

        private CardButton Button(RectTransform target, string caption, Action onClick, int size)
        {
            var image = Background(target, RowColor, true);
            var button = target.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            var label = Label(Rect(target, "Label", Vector2.zero, Vector2.one, new Vector2(8, 2), new Vector2(-8, -2)),
                caption, size, Ink, TextAlignmentOptions.Center);
            return new CardButton(button, label);
        }

        private TextMeshProUGUI Label(RectTransform rect, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = size * .78f;
            label.fontSizeMax = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.richText = false;
            return label;
        }

        private static UnityEngine.UI.Image Background(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static void RowLayout(RectTransform rect)
        {
            var layout = rect.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
        }

        private static RectTransform Top(Transform parent, string name, float top, float height, float inset)
            => Rect(parent, name, Vector2.up, Vector2.one, new Vector2(inset, -top - height), new Vector2(-inset, -top));

        private static RectTransform Rect(Transform parent, string name, Vector2 minimum, Vector2 maximum,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = minimum;
            rect.anchorMax = maximum;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private readonly struct CardButton
        {
            public CardButton(UnityEngine.UI.Button button, TextMeshProUGUI text)
            {
                Button = button;
                Text = text;
            }

            public UnityEngine.UI.Button Button { get; }
            public TextMeshProUGUI Text { get; }
        }

        private sealed class WarehouseRow
        {
            public CardButton Card;
            public WeaponInstanceId Id;
        }
    }
}
