#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Run;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.SettlementUI
{
    internal sealed class SettlementResultsView
    {
        private static readonly Color Surface = new Color32(35, 30, 45, 255);
        private static readonly Color SectionColor = new Color32(54, 47, 66, 255);
        private static readonly Color ButtonColor = new Color32(100, 71, 56, 255);
        private static readonly Color Ink = new Color32(244, 232, 221, 255);
        private static readonly Color Gold = new Color32(246, 187, 126, 255);
        private static readonly Color Green = new Color32(160, 225, 182, 255);
        private static readonly Color Rose = new Color32(249, 169, 161, 255);

        private readonly TMP_FontAsset font;
        private readonly RectTransform modal;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI notice;
        private readonly RectTransform body;
        private readonly RectTransform list;
        private readonly RectTransform items;
        private readonly RectTransform records;
        private readonly TextMeshProUGUI centerMessage;
        private readonly UnityEngine.UI.Button back;
        private readonly List<SectionView> itemSections = new List<SectionView>();
        private readonly List<SectionView> recordSections = new List<SectionView>();

        public SettlementResultsView(RectTransform parent, TMP_FontAsset font, Action dismiss)
        {
            this.font = font;
            modal = Rect(parent, "Settlement Modal", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Paint(modal, new Color(0, 0, 0, .67f), true);
            var card = Rect(modal, "Card", new Vector2(.09f, .05f), new Vector2(.91f, .95f), Vector2.zero, Vector2.zero);
            Paint(card, Surface);
            title = Label(Top(card, "Result Title", 12, 48, 24), string.Empty, 32, Gold);
            notice = Label(Top(card, "Notice", 58, 44, 24), string.Empty, 17, Gold);

            body = Rect(card, "Results Scroll", Vector2.zero, Vector2.one, new Vector2(24, 64), new Vector2(-24, -70));
            Paint(body, Surface, true);
            var scroll = body.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            var viewport = Rect(body, "Viewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            scroll.viewport = viewport;
            list = Rect(viewport, "Results Content", Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            list.pivot = new Vector2(.5f, 1);
            scroll.content = list;

            var columns = list.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            columns.spacing = 12;
            columns.childControlWidth = columns.childControlHeight = true;
            columns.childForceExpandWidth = true;
            columns.childForceExpandHeight = false;
            list.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit =
                UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            items = Column(list, "Weapons");
            records = Column(list, "Records");

            centerMessage = Label(Rect(card, "Center Message", new Vector2(0, .24f), new Vector2(1, .75f),
                new Vector2(24, 0), new Vector2(-24, 0)), string.Empty, 22, Ink);
            centerMessage.alignment = TextAlignmentOptions.Center;

            var last = Rect(card, "Return to Lobby", new Vector2(.65f, 0), Vector2.right,
                new Vector2(0, 11), new Vector2(-24, 54));
            var buttonImage = Paint(last, ButtonColor, true);
            back = last.gameObject.AddComponent<UnityEngine.UI.Button>();
            back.targetGraphic = buttonImage;
            back.onClick.AddListener(() => dismiss());
            Label(Rect(last, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "로비로 돌아가기", 19, Ink).alignment = TextAlignmentOptions.Center;
            SetOpen(false);
        }

        public bool IsOpen => modal.gameObject.activeSelf;
        public void SetOpen(bool active) => modal.gameObject.SetActive(active);

        public void Render(SettlementResultsDisplay display)
        {
            title.text = display.Title;
            title.color = display.Reason == RunEndReason.Extracted ? Green : display.Reason.HasValue ? Rose : Gold;
            bool detailed = display.Items.Count != 0 || display.Records.Count != 0;
            bool showNotice = detailed && display.Notice.Length != 0;
            notice.gameObject.SetActive(showNotice);
            notice.text = display.Notice;
            body.gameObject.SetActive(detailed);
            body.offsetMax = new Vector2(-24, showNotice ? -108 : -70);
            centerMessage.gameObject.SetActive(!detailed);
            centerMessage.text = display.Notice;
            back.gameObject.SetActive(display.RunId.HasValue);
            if (!detailed)
                return;

            RenderColumn(items, itemSections, display.Items);
            RenderColumn(records, recordSections, display.Records);
            list.anchoredPosition = Vector2.zero;
        }

        private void RenderColumn(RectTransform column, List<SectionView> sections, IReadOnlyList<SettlementSection> data)
        {
            while (sections.Count < data.Count)
                sections.Add(CreateSection(column));
            for (int index = 0; index < sections.Count; index++)
            {
                SectionView section = sections[index];
                section.Root.gameObject.SetActive(index < data.Count);
                if (index >= data.Count)
                    continue;

                SettlementSection values = data[index];
                section.Heading.text = values.Title;
                section.Body.text = string.Join("\n", values.Lines);
            }
        }

        private SectionView CreateSection(RectTransform column)
        {
            var area = Rect(column, "Section", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Paint(area, SectionColor);
            var layout = Vertical(area, 3);
            layout.padding = new RectOffset(12, 12, 7, 7);
            TextMeshProUGUI heading = Label(Rect(area, "Heading", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                string.Empty, 18, Gold);
            TextMeshProUGUI body = Label(Rect(area, "Lines", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                string.Empty, 18, Ink);
            body.alignment = TextAlignmentOptions.TopLeft;
            return new SectionView(area, heading, body);
        }

        private static RectTransform Column(RectTransform parent, string name)
        {
            var column = Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Vertical(column, 7);
            var width = column.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            width.minWidth = 0;
            width.preferredWidth = 1;
            width.flexibleWidth = 1;
            return column;
        }

        private static UnityEngine.UI.VerticalLayoutGroup Vertical(RectTransform target, float spacing)
        {
            var layout = target.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        private TextMeshProUGUI Label(RectTransform rect, string text, float fontSize, Color color)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            label.richText = false;
            return label;
        }

        private static UnityEngine.UI.Image Paint(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static RectTransform Top(Transform parent, string name, float top, float height, float inset)
            => Rect(parent, name, Vector2.up, Vector2.one, new Vector2(inset, -top - height), new Vector2(-inset, -top));

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

        private readonly struct SectionView
        {
            public SectionView(RectTransform root, TextMeshProUGUI heading, TextMeshProUGUI body)
            {
                Root = root;
                Heading = heading;
                Body = body;
            }

            public RectTransform Root { get; }
            public TextMeshProUGUI Heading { get; }
            public TextMeshProUGUI Body { get; }
        }
    }
}
