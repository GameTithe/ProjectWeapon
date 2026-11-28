#nullable enable

using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.CombatStatusUI
{
    internal sealed class CombatHudView
    {
        private static readonly Color Surface = new Color32(29, 25, 41, 245);
        private static readonly Color Ink = new Color32(246, 239, 218, 255);
        private static readonly Color TimerExpired = new Color32(255, 168, 123, 255);
        private static readonly Color SlotActive = new Color32(35, 57, 75, 250);
        private static readonly Color SlotMarker = new Color32(112, 211, 244, 255);
        private readonly TextMeshProUGUI health;
        private readonly RectTransform healthFill;
        private readonly TextMeshProUGUI timer;
        private readonly SlotView[] slots;
        private long? displayedSeconds;

        public CombatHudView(RectTransform parent, TMP_FontAsset font)
        {
            var bar = Rect(parent, "CombatHUD", Vector2.up, Vector2.one,
                new Vector2(16, -64), new Vector2(-16, -16));
            var layout = bar.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            RectTransform healthCard = Card(bar, "Health", 1.6f);
            var healthText = Rect(healthCard, "HealthValue", new Vector2(0, .25f), Vector2.one,
                new Vector2(10, 0), new Vector2(-10, 0));
            health = Label(healthText, font, "HP —", 21, TextAlignmentOptions.MidlineLeft);
            var healthTrack = Rect(healthCard, "HealthTrack", Vector2.zero, new Vector2(1, .25f),
                new Vector2(10, 5), new Vector2(-10, 0));
            Image(healthTrack, new Color32(73, 40, 53, 255));
            healthFill = Rect(healthTrack, "HealthFill", Vector2.zero, Vector2.up, Vector2.zero, Vector2.zero);
            Image(healthFill, new Color32(215, 81, 94, 255));

            RectTransform timeCard = Card(bar, "Countdown", 1f);
            var timeText = Rect(timeCard, "CountdownValue", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            timer = Label(timeText, font, "--:--", 24, TextAlignmentOptions.Center);
            slots = new[] { MakeSlot(bar, font, 1), MakeSlot(bar, font, 2) };
        }

        public void RenderHealth(int current, int max)
        {
            health.text = string.Format(CultureInfo.InvariantCulture, "HP {0} / {1}", current, max);
            healthFill.anchorMax = new Vector2((float)current / max, 1);
        }

        public void RenderTime(TimeSpan? remaining)
        {
            long? seconds = remaining.HasValue ? (long)Math.Ceiling(remaining.Value.TotalSeconds) : null;
            if (seconds == displayedSeconds)
                return;

            displayedSeconds = seconds;
            timer.color = seconds == 0 ? TimerExpired : Ink;
            timer.text = seconds.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", seconds.Value / 60, seconds.Value % 60)
                : "--:--";
        }

        public void RenderSlot(int index, string caption, bool active)
        {
            SlotView slot = slots[index];
            slot.Caption.text = string.Format(CultureInfo.InvariantCulture, "{0}  {1}", index + 1, caption);
            slot.Surface.color = active ? SlotActive : Surface;
            slot.Marker.enabled = active;
        }

        private static SlotView MakeSlot(RectTransform bar, TMP_FontAsset font, int number)
        {
            RectTransform card = Card(bar, $"EquipmentSlot{number}", 1.4f);
            var marker = Rect(card, "ActiveMarker", Vector2.zero, Vector2.up,
                new Vector2(0, 6), new Vector2(4, -6));
            UnityEngine.UI.Image markerImage = Image(marker, SlotMarker);
            markerImage.enabled = false;
            var caption = Rect(card, "SlotValue", Vector2.zero, Vector2.one,
                new Vector2(12, 0), new Vector2(-8, 0));
            return new SlotView(card.GetComponent<UnityEngine.UI.Image>(),
                Label(caption, font, $"{number}  비어 있음", 21, TextAlignmentOptions.MidlineLeft), markerImage);
        }

        private static RectTransform Card(RectTransform parent, string name, float share)
        {
            var card = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            card.SetParent(parent, false);
            card.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = share;
            Image(card, Surface);
            return card;
        }

        private static UnityEngine.UI.Image Image(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Label(RectTransform rect, TMP_FontAsset font, string text, float size,
            TextAlignmentOptions alignment)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = Ink;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = false;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform Rect(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        private readonly struct SlotView
        {
            public SlotView(UnityEngine.UI.Image surface, TextMeshProUGUI caption, UnityEngine.UI.Image marker)
            {
                Surface = surface;
                Caption = caption;
                Marker = marker;
            }

            public UnityEngine.UI.Image Surface { get; }
            public TextMeshProUGUI Caption { get; }
            public UnityEngine.UI.Image Marker { get; }
        }
    }
}
