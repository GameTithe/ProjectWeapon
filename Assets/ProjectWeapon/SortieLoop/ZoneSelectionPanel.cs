#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectWeapon.Dungeon;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.SortieLoop
{
    internal sealed class ZoneSelectionPanel
    {
        private static readonly Color Background = new Color32(25, 20, 33, 255);
        private static readonly Color Card = new Color32(42, 34, 54, 255);
        private static readonly Color Row = new Color32(54, 45, 67, 255);
        private static readonly Color ActionColor = new Color32(102, 70, 50, 255);
        private static readonly Color Ink = new Color32(248, 236, 219, 255);
        private static readonly Color Accent = new Color32(248, 190, 116, 255);
        private static readonly Color Muted = new Color32(204, 191, 210, 255);

        private readonly RectTransform root;
        private readonly List<ZoneRow> rows = new List<ZoneRow>();

        internal ZoneSelectionPanel(RectTransform parent, TMP_FontAsset font, IEnumerable<ZoneDefinition> zones,
            Action<int> select, Action close)
        {
            var orderedZones = zones.OrderBy(zone => zone.ZoneId).ToArray();
            root = Place(parent, "Zone Selection", 0, 0, 1, 1, typeof(UnityEngine.UI.Image));
            root.GetComponent<UnityEngine.UI.Image>().color = Background;
            RectTransform card = Place(root, "Zone List", .08f, .08f, .92f, .92f, typeof(UnityEngine.UI.Image));
            SetBackground(card, Card);
            Label(card, "Title", "구역 선택", font, 30, Accent, Alignment.Left, .047f, .83f, .65f, .98f);
            CreateButton(card, "Close Zones", "닫기", font, .81f, .853f, .958f, .955f, close);
            Label(card, "Guide", "권장 등급은 장비 준비를 위한 안내입니다.", font, 16, Muted,
                Alignment.Left, .047f, .758f, .81f, .83f);

            Label(card, "Zone Header", "구역", font, 16, Accent, Alignment.Left, .065f, .681f, .36f, .746f);
            Label(card, "Recommended Header", "권장 등급", font, 16, Accent, Alignment.Center, .37f, .681f, .48f, .746f);
            Label(card, "Reward Header", "보상 등급", font, 16, Accent, Alignment.Center, .49f, .681f, .60f, .746f);
            Label(card, "Time Header", "제한 시간", font, 16, Accent, Alignment.Center, .61f, .681f, .718f, .746f);
            Label(card, "Entry Header", "입장 상태", font, 16, Accent, Alignment.Center, .725f, .681f, .95f, .746f);

            for (int i = 0; i < orderedZones.Length; i++)
            {
                ZoneDefinition zone = orderedZones[i];
                float slot = .61f / orderedZones.Length;
                float top = .674f - i * slot;
                RectTransform row = Place(card, $"Zone {zone.ZoneId}", .043f, top - slot + .009f, .957f,
                    top, typeof(UnityEngine.UI.Image));
                SetBackground(row, Row);

                Label(row, "Zone Name", $"{zone.ZoneId}. {zone.Theme}", font, 21, Ink,
                    Alignment.Left, .025f, .05f, .351f, .95f);
                Label(row, "Recommended Grade", zone.RecommendedGrade.ToString(CultureInfo.InvariantCulture), font,
                    20, Ink, Alignment.Center, .36f, .05f, .477f, .95f);
                Label(row, "Reward Grades", $"{zone.MinimumRewardGrade}~{zone.MaximumRewardGrade}", font,
                    20, Ink, Alignment.Center, .486f, .05f, .611f, .95f);

                int seconds = zone.TimeLimitSeconds;
                Label(row, "Time Limit", string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}",
                    seconds / 60, seconds % 60), font, 20, Ink, Alignment.Center, .619f, .05f, .743f, .95f);
                TextMeshProUGUI state = Label(row, "Entry Status", string.Empty, font, 17, Muted,
                    Alignment.Center, .748f, .05f, .985f, .95f);
                UnityEngine.UI.Button enter = CreateButton(row, $"Select Zone {zone.ZoneId}", "해금 · 선택",
                    font, .751f, .10f, .977f, .90f, () => select(zone.ZoneId));
                rows.Add(new ZoneRow(zone.ZoneId, state, enter));
            }

            Close();
        }

        internal bool IsOpen => root.gameObject.activeSelf;

        internal void Open(ZoneProgression progression, Func<int, bool> canEnter)
        {
            foreach (ZoneRow row in rows)
            {
                bool unlocked = progression.IsUnlocked(row.ZoneId);
                bool playable = unlocked && canEnter(row.ZoneId);
                row.Enter.gameObject.SetActive(playable);
                row.State.gameObject.SetActive(!playable);
                row.State.text = unlocked ? "해금 · 입장 준비 중" : "잠김";
            }
            root.gameObject.SetActive(true);
        }

        internal void Close() => root.gameObject.SetActive(false);

        internal static UnityEngine.UI.Button CreateButton(RectTransform parent, string name, string text,
            TMP_FontAsset font, float left, float bottom, float right, float top, Action onClick)
        {
            RectTransform rect = Place(parent, name, left, bottom, right, top,
                typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            var image = rect.GetComponent<UnityEngine.UI.Image>();
            image.color = ActionColor;
            var button = rect.GetComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            Label(rect, "Label", text, font, 18, Ink, Alignment.Center, .035f, .03f, .965f, .97f);
            return button;
        }

        internal static void SetAnchors(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = new Vector2(left, bottom);
            rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Place(RectTransform parent, string name, float left, float bottom,
            float right, float top, params Type[] components)
        {
            var child = new GameObject(name, new[] { typeof(RectTransform) }.Concat(components).ToArray());
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            SetAnchors(rect, left, bottom, right, top);
            return rect;
        }

        private static TextMeshProUGUI Label(RectTransform parent, string name, string text, TMP_FontAsset font,
            float size, Color color, Alignment alignment, float left, float bottom, float right, float top)
        {
            var rect = Place(parent, name, left, bottom, right, top, typeof(TextMeshProUGUI));
            var label = rect.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.fontSizeMin = 11;
            label.fontSizeMax = size;
            label.enableAutoSizing = true;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = alignment == Alignment.Left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Midline;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private static void SetBackground(RectTransform rect, Color color)
        {
            var image = rect.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private enum Alignment { Left, Center }

        private readonly struct ZoneRow
        {
            internal ZoneRow(int zoneId, TextMeshProUGUI state, UnityEngine.UI.Button enter)
            {
                ZoneId = zoneId;
                State = state;
                Enter = enter;
            }

            internal int ZoneId { get; }
            internal TextMeshProUGUI State { get; }
            internal UnityEngine.UI.Button Enter { get; }
        }
    }
}
