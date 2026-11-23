#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Inventory;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.LootUI
{
    internal sealed class LootOverflowView
    {
        private static readonly Color Surface = new Color(0.10f, 0.14f, 0.18f);
        private static readonly Color RowColor = new Color(0.19f, 0.25f, 0.30f);
        private static readonly Color SelectedColor = new Color(0.15f, 0.42f, 0.39f);
        private static readonly Color Ink = new Color(0.96f, 0.97f, 0.98f);
        private readonly RectTransform root;
        private readonly RectTransform list;
        private readonly TMPro.TextMeshProUGUI incoming;
        private readonly TMPro.TextMeshProUGUI status;
        private readonly UnityEngine.UI.Button confirm;
        private readonly TMP_FontAsset font;
        private readonly Action<WeaponInstanceId> select;
        private readonly Dictionary<WeaponInstanceId, UnityEngine.UI.Button> rows =
            new Dictionary<WeaponInstanceId, UnityEngine.UI.Button>();

        public LootOverflowView(RectTransform parent, TMP_FontAsset font, Action<WeaponInstanceId> select,
            Action onConfirm, Action onCancel)
        {
            this.font = font;
            this.select = select;
            root = Rect(parent, "LootOverflowModal", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0, 0, 0, 0.65f);
            var card = Rect(root, "Card", new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.90f),
                Vector2.zero, Vector2.zero);
            var cardImage = card.gameObject.AddComponent<UnityEngine.UI.Image>();
            cardImage.color = Surface;
            cardImage.raycastTarget = false;

            Label(Top(card, "Title", 18, 24, 44), "가방이 가득 찼습니다", 32);
            var incomingArea = Rect(card, "IncomingArea", Vector2.zero, new Vector2(0.40f, 1),
                new Vector2(24, 114), new Vector2(-12, -76));
            var incomingImage = incomingArea.gameObject.AddComponent<UnityEngine.UI.Image>();
            incomingImage.color = RowColor;
            incomingImage.raycastTarget = false;
            Label(Top(incomingArea, "IncomingTitle", 12, 14, 32), "새 무기", 23);
            incoming = Label(Rect(incomingArea, "Incoming", Vector2.zero, Vector2.one,
                new Vector2(14, 12), new Vector2(-14, -49)), string.Empty, 24);
            incoming.alignment = TextAlignmentOptions.TopLeft;

            var choices = Rect(card, "Choices", new Vector2(0.42f, 0), Vector2.one,
                new Vector2(0, 114), new Vector2(-24, -76));
            Label(Top(choices, "ChoicesTitle", 0, 0, 32), "교체할 운반 무기", 23);
            list = MakeScrollList(choices);
            status = Label(Rect(card, "Status", Vector2.zero, Vector2.right,
                new Vector2(24, 66), new Vector2(-24, 104)), string.Empty, 20);

            var cancel = ButtonAt(Rect(card, "Cancel", new Vector2(0.48f, 0), new Vector2(0.71f, 0),
                new Vector2(0, 15), new Vector2(-10, 58)), "취소");
            cancel.onClick.AddListener(() => onCancel());
            confirm = ButtonAt(Rect(card, "Confirm", new Vector2(0.71f, 0), Vector2.right,
                new Vector2(0, 15), new Vector2(-24, 58)), "교체 확정");
            confirm.onClick.AddListener(() => onConfirm());

            root.gameObject.SetActive(false);
        }

        public void Open(string description, string note)
        {
            ClearRows();
            list.anchoredPosition = Vector2.zero;
            incoming.text = description;
            SetStatus(note);
            confirm.interactable = false;
            root.gameObject.SetActive(true);
        }

        public void AddCandidate(WeaponInstanceId id, string description, bool allowed)
        {
            var row = Rect(list, "Candidate", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var sizing = row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            sizing.minHeight = 48;
            sizing.preferredHeight = 48;
            UnityEngine.UI.Button button = ButtonAt(row, description);
            button.interactable = allowed;
            button.onClick.AddListener(() => select(id));
            rows.Add(id, button);
        }

        public void Highlight(WeaponInstanceId? id)
        {
            foreach (var entry in rows)
                entry.Value.image.color = id.HasValue && entry.Key == id.Value ? SelectedColor : RowColor;

            confirm.interactable = id.HasValue;
        }

        public void SetStatus(string message) => status.text = message;

        public void Close()
        {
            root.gameObject.SetActive(false);
            ClearRows();
        }

        private void ClearRows()
        {
            foreach (var row in rows.Values)
            {
                row.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(row.gameObject);
            }

            rows.Clear();
        }

        private static RectTransform MakeScrollList(RectTransform choices)
        {
            var scrollRect = Rect(choices, "ScrollView", Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(0, -38));
            var scroll = scrollRect.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            var viewport = Rect(scrollRect, "Viewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            var content = Rect(viewport, "Content", Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(0.5f, 1);
            var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            return content;
        }

        private UnityEngine.UI.Button ButtonAt(RectTransform target, string caption)
        {
            var image = target.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = RowColor;
            var button = target.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            var text = Label(Rect(target, "Label", Vector2.zero, Vector2.one,
                new Vector2(10, 3), new Vector2(-10, -3)), caption, 21);
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private TMPro.TextMeshProUGUI Label(RectTransform target, string caption, int size)
        {
            var text = target.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            text.font = font;
            text.text = caption;
            text.fontSize = size;
            text.color = Ink;
            text.richText = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Top(RectTransform parent, string name, float top, float inset, float height) =>
            Rect(parent, name, Vector2.up, Vector2.one, new Vector2(inset, -top - height), new Vector2(-inset, -top));

        private static RectTransform Rect(RectTransform parent, string name, Vector2 min, Vector2 max,
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
    }
}
