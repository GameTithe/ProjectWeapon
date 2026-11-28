#nullable enable

using System;
using System.Collections.Generic;
using ProjectWeapon.Catalog;
using ProjectWeapon.Save;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectWeapon.CatalogUI
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class CatalogListPanel : MonoBehaviour
    {
        private const float RowHeight = 59f;
        private static readonly Color32 PanelColor = new Color32(14, 20, 34, 255);
        private static readonly Color32 TextColor = new Color32(239, 231, 213, 255);
        private static readonly Color32 MutedColor = new Color32(174, 184, 204, 255);
        private static readonly Color32[] GradeColors =
        {
            new Color32(187, 194, 207, 255), new Color32(136, 202, 147, 255),
            new Color32(125, 185, 246, 255), new Color32(207, 155, 238, 255),
            new Color32(245, 199, 104, 255)
        };

        private readonly List<Row> rows = new List<Row>();
        private Func<SaveSnapshot?>? readSnapshot;
        private TMP_FontAsset font = null!;
        private TMP_Text summary = null!;
        private RectTransform viewport = null!;
        private ScrollRect scroll = null!;

        public event Action? Closed;

        public void Initialize(TMP_FontAsset textFont, Func<SaveSnapshot?> snapshotReader)
        {
            if (readSnapshot != null)
                throw new InvalidOperationException("This catalog is already initialized.");
            if (!textFont)
                throw new ArgumentNullException(nameof(textFont));
            readSnapshot = snapshotReader ?? throw new ArgumentNullException(nameof(snapshotReader));
            font = textFont;
            Build();
            gameObject.SetActive(false);
        }

        public void Open()
        {
            if (readSnapshot == null)
                throw new InvalidOperationException("Initialize the catalog before opening it.");

            SaveSnapshot? snapshot = readSnapshot();
            CatalogProgress? progress = snapshot == null ? null : new CatalogProgress(snapshot.Catalog);
            gameObject.SetActive(true);
            viewport.gameObject.SetActive(progress != null);
            scroll.enabled = progress != null;
            scroll.StopMovement();
            if (progress == null)
            {
                summary.text = "저장 복구 대기";
                return;
            }

            summary.text = $"회수 {CatalogRoster.CountRecovered(progress)} / {CatalogRoster.Entries.Count}";
            foreach (Row row in rows)
                row.Show(progress.GetStage(row.Entry.Id));
            scroll.content.anchoredPosition = Vector2.zero;
            scroll.verticalScrollbar.SetValueWithoutNotify(1f);
            scroll.verticalNormalizedPosition = 1f;
        }

        public void Close()
        {
            if (!gameObject.activeSelf)
                return;
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void Build()
        {
            var root = (RectTransform)transform;
            Fill(root, "Catalog Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, PanelColor);

            RectTransform header = Fill(root, "Catalog Header", new Vector2(0, 1), Vector2.one,
                new Vector2(28, -76), new Vector2(-28, -16), new Color32(25, 34, 54, 255));
            Label(Rect(header, "Title", Vector2.zero, new Vector2(.58f, 1),
                new Vector2(18, 0), Vector2.zero), "무기 도감", 29, TextColor, TextAlignmentOptions.MidlineLeft);
            summary = Label(Rect(header, "Recovery Count", new Vector2(.55f, 0), new Vector2(1, 1),
                Vector2.zero, new Vector2(-110, 0)), "", 18, new Color32(246, 208, 142, 255),
                TextAlignmentOptions.MidlineRight);

            RectTransform close = Fill(header, "Close Catalog", Vector2.right, Vector2.one, new Vector2(-94, 9),
                new Vector2(-10, -9), new Color32(65, 49, 42, 255), receiveInput: true);
            var button = close.gameObject.AddComponent<Button>();
            button.targetGraphic = close.GetComponent<Image>();
            button.onClick.AddListener(Close);
            Label(Rect(close, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), "닫기", 19, TextColor,
                TextAlignmentOptions.Center);

            RectTransform list = Fill(root, "Catalog List", Vector2.zero, Vector2.one, new Vector2(28, 18),
                new Vector2(-28, -88), new Color32(17, 24, 40, 255), receiveInput: true);
            scroll = list.gameObject.AddComponent<ScrollRect>();
            viewport = Rect(list, "Viewport", Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-18, -6));
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Rect(viewport, "Weapon Entries", Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, CatalogRoster.Entries.Count * RowHeight);
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32;

            RectTransform track = Fill(list, "Catalog Scrollbar", Vector2.right, Vector2.one, new Vector2(-14, 6),
                new Vector2(-5, -6), new Color32(35, 45, 65, 255), receiveInput: true);
            RectTransform handle = Fill(track, "Thumb", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color32(122, 138, 168, 255), receiveInput: true);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            for (int index = 0; index < CatalogRoster.Entries.Count; index++)
                AddRow(content, CatalogRoster.Entries[index], index);
        }

        private void AddRow(RectTransform content, CatalogEntry entry, int index)
        {
            float top = -index * RowHeight;
            Color32 accent = GradeColors[entry.Grade - 1];
            RectTransform row = Fill(content, entry.Id.Value, Vector2.up, Vector2.one,
                new Vector2(0, top - RowHeight + 4), new Vector2(0, top), new Color32(30, 39, 58, 255));
            Fill(row, "Grade Edge", Vector2.zero, Vector2.up, Vector2.zero, new Vector2(4, 0), accent);
            RectTransform tile = Fill(row, "Weapon Tile", Vector2.zero, Vector2.up,
                new Vector2(14, 6), new Vector2(58, -6), new Color32(19, 27, 44, 255));
            RectTransform glyphRect = Rect(tile, "Weapon Silhouette", Vector2.zero, Vector2.one,
                new Vector2(6, 6), new Vector2(-6, -6));
            var glyph = glyphRect.gameObject.AddComponent<CatalogGlyph>();
            glyph.raycastTarget = false;
            glyph.SetShape(entry.Silhouette);
            TMP_Text unknown = Label(Rect(tile, "Unknown", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "?", 20, MutedColor, TextAlignmentOptions.Center);
            TMP_Text name = Label(Rect(row, "Weapon Name", Vector2.zero, new Vector2(.39f, 1),
                new Vector2(69, 15), Vector2.zero), "", 21, TextColor, TextAlignmentOptions.MidlineLeft);
            Label(Rect(row, "Weapon Grade", Vector2.zero, new Vector2(.39f, 1),
                new Vector2(70, 0), new Vector2(0, -29)), $"{entry.Grade}등급", 14, accent,
                TextAlignmentOptions.MidlineLeft);
            TMP_Text stage = Label(Rect(row, "Catalog Stage", new Vector2(.38f, 0), new Vector2(.59f, 1),
                Vector2.zero, Vector2.zero), "", 18, MutedColor, TextAlignmentOptions.MidlineLeft);
            Label(Rect(row, "Acquisition", new Vector2(.59f, 0), Vector2.one, Vector2.zero, new Vector2(-6, 0)),
                entry.Acquisition, 16, MutedColor, TextAlignmentOptions.MidlineLeft);
            rows.Add(new Row(entry, accent, glyph, unknown, name, stage));
        }

        private TMP_Text Label(RectTransform parent, string value, float size, Color color, TextAlignmentOptions alignment)
        {
            var label = parent.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            label.text = value;
            return label;
        }

        private static RectTransform Fill(RectTransform parent, string name, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax, Color color, bool receiveInput = false)
        {
            RectTransform child = Rect(parent, name, min, max, offsetMin, offsetMax);
            var image = child.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = receiveInput;
            return child;
        }

        private static RectTransform Rect(RectTransform parent, string name, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var child = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            child.SetParent(parent, false);
            child.anchorMin = min;
            child.anchorMax = max;
            child.offsetMin = offsetMin;
            child.offsetMax = offsetMax;
            return child;
        }

        private sealed class Row
        {
            private readonly Color accent;
            private readonly CatalogGlyph glyph;
            private readonly TMP_Text unknown;
            private readonly TMP_Text name;
            private readonly TMP_Text status;

            internal Row(CatalogEntry entry, Color accent, CatalogGlyph glyph, TMP_Text unknown, TMP_Text name, TMP_Text status)
            {
                Entry = entry;
                this.accent = accent;
                this.glyph = glyph;
                this.unknown = unknown;
                this.name = name;
                this.status = status;
            }

            internal CatalogEntry Entry { get; }

            internal void Show(CatalogStage stage)
            {
                bool revealed = stage >= CatalogStage.Recovered;
                bool hasShape = stage >= CatalogStage.Discovered;
                unknown.gameObject.SetActive(!hasShape);
                glyph.gameObject.SetActive(hasShape);
                glyph.color = revealed ? accent : new Color32(112, 125, 154, 255);
                name.text = revealed ? Entry.Name : "???";
                status.text = CatalogRoster.StageLabel(stage);
                status.color = revealed ? accent : MutedColor;
            }
        }
    }
}
