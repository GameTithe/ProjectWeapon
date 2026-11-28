#nullable enable

using System;
using ProjectWeapon.Inventory;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.RunBagUI
{
    internal sealed class RunBagView
    {
        private static readonly Color Surface = new Color32(34, 29, 44, 255);
        private static readonly Color Row = new Color32(56, 49, 68, 255);
        private static readonly Color Selected = new Color32(115, 77, 58, 255);
        private static readonly Color Ink = new Color32(244, 231, 219, 255);
        private static readonly Color Gold = new Color32(241, 187, 130, 255);
        private static readonly Color Danger = new Color32(107, 58, 62, 255);

        private readonly RectTransform root;
        private readonly TMP_FontAsset font;
        private readonly SlotCard[] slots;
        private readonly TextMeshProUGUI status;
        private readonly UnityEngine.UI.Button drop;

        public RunBagView(RectTransform parent, TMP_FontAsset font, InventorySlotCapacity capacity, Action<int> select,
            Action dropItem, Action close)
        {
            this.font = font;
            root = Rect(parent, "Run Bag Modal", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Background(root, new Color(0, 0, 0, .57f), true);

            var card = Rect(root, "Card", new Vector2(.12f, .14f), new Vector2(.88f, .86f), Vector2.zero, Vector2.zero);
            Background(card, Surface);
            Label(Top(card, "Title", 12, 47), "탐험 가방", 31, Gold);
            Label(Top(card, "Equipment Title", 68, 28), "장착", 22, Ink);
            Label(Top(card, "Carry Title", 174, 28), "운반", 22, Ink);

            slots = new SlotCard[capacity.EquippedSlots + capacity.CarriedSlots];
            MakeSlots(Top(card, "Equipped Slots", 102, 64), "장착", 0, capacity.EquippedSlots, select);
            MakeSlots(Top(card, "Carried Slots", 208, 64), "운반", capacity.EquippedSlots, capacity.CarriedSlots, select);

            status = Label(Rect(card, "Status", Vector2.zero, new Vector2(.59f, 0),
                new Vector2(24, 13), new Vector2(-8, 55)), string.Empty, 17, Ink);
            drop = Button(Rect(card, "Drop", new Vector2(.59f, 0), new Vector2(.80f, 0),
                new Vector2(0, 13), new Vector2(-8, 55)), "버리기", dropItem, Danger, out _);
            Button(Rect(card, "Close", new Vector2(.80f, 0), Vector2.right,
                new Vector2(0, 13), new Vector2(-24, 55)), "닫기", close, Row, out _);
            SetOpen(false);
        }

        public bool IsOpen => root.gameObject.activeSelf;
        public void SetOpen(bool open) => root.gameObject.SetActive(open);
        public void SetStatus(string message) => status.text = message;
        public void SetDrop(bool allowed) => drop.interactable = allowed;

        public void SetSlot(int index, string name, bool interactive, bool selected)
        {
            SlotCard slot = slots[index];
            slot.Button.interactable = interactive;
            slot.Button.image.color = selected ? Selected : Row;
            string marker = selected ? " · 선택" : string.Empty;
            slot.Label.text = $"{slot.Caption}{marker}\n{name}";
        }

        private void MakeSlots(RectTransform row, string caption, int offset, int count, Action<int> select)
        {
            var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            for (int index = 0; index < count; index++)
            {
                int slot = index + offset;
                var rect = Rect(row, $"Slot {slot}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var button = Button(rect, string.Empty, () => select(slot), Row, out TextMeshProUGUI label);
                slots[slot] = new SlotCard(button, label, $"{caption} {index + 1}");
            }
        }

        private UnityEngine.UI.Button Button(RectTransform rect, string caption, Action onClick, Color color,
            out TextMeshProUGUI text)
        {
            var image = Background(rect, color, true);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            text = Label(Rect(rect, "Label", Vector2.zero, Vector2.one, new Vector2(6, 2), new Vector2(-6, -2)),
                caption, 18, Ink);
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private TextMeshProUGUI Label(RectTransform rect, string text, float size, Color color)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.color = color;
            label.fontSize = label.fontSizeMax = size;
            label.fontSizeMin = size * .75f;
            label.enableAutoSizing = true;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            label.richText = false;
            return label;
        }

        private static UnityEngine.UI.Image Background(RectTransform target, Color color, bool raycast = false)
        {
            var image = target.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static RectTransform Top(Transform parent, string name, float top, float height) =>
            Rect(parent, name, Vector2.up, Vector2.one, new Vector2(24, -top - height), new Vector2(-24, -top));

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private readonly struct SlotCard
        {
            public SlotCard(UnityEngine.UI.Button button, TextMeshProUGUI label, string caption)
            {
                Button = button;
                Label = label;
                Caption = caption;
            }

            public UnityEngine.UI.Button Button { get; }
            public TextMeshProUGUI Label { get; }
            public string Caption { get; }
        }
    }
}
