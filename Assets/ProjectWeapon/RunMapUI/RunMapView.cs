#nullable enable

using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectWeapon.RunMapUI
{
    internal sealed class RunMapView
    {
        private static readonly Color Surface = new Color32(35, 30, 46, 255);
        private static readonly Color Field = new Color32(46, 41, 59, 255);
        private static readonly Color ButtonColor = new Color32(69, 56, 78, 255);
        private static readonly Color Ink = new Color32(245, 232, 220, 255);
        private static readonly Color Gold = new Color32(249, 196, 131, 255);

        private readonly RectTransform launcher;
        private readonly RectTransform modal;
        private readonly TMP_FontAsset font;
        private readonly TextMeshProUGUI heading;
        private readonly TextMeshProUGUI empty;
        private readonly RectTransform legend;
        private readonly RunMapGraphic map;

        public RunMapView(RectTransform parent, TMP_FontAsset font, Action open, Action close,
            Action<int> press, Action<int> release)
        {
            this.font = font;
            launcher = Rect(parent, "Map Button", Vector2.one, Vector2.one, new Vector2(-113, -61), new Vector2(-18, -17));
            Button(launcher, "지도", open);
            var input = launcher.gameObject.AddComponent<EventTrigger>();
            BindPointer(input, EventTriggerType.PointerDown, press);
            BindPointer(input, EventTriggerType.PointerUp, release);
            SetLauncher(false);

            modal = Rect(parent, "Map Modal", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Paint(modal, new Color(0, 0, 0, .58f), true);
            var card = Rect(modal, "Map Card", new Vector2(.15f, .13f), new Vector2(.85f, .87f), Vector2.zero, Vector2.zero);
            Paint(card, Surface);

            heading = Label(Rect(card, "Zone", new Vector2(0, 1), new Vector2(.78f, 1),
                new Vector2(25, -69), new Vector2(-12, -15)), string.Empty, 29, Gold);
            var closeButton = Rect(card, "Close Map", Vector2.one, Vector2.one, new Vector2(-111, -67), new Vector2(-25, -18));
            Button(closeButton, "닫기", close);

            var field = Rect(card, "Map Field", Vector2.zero, Vector2.one, new Vector2(25, 55), new Vector2(-25, -82));
            Paint(field, Field);
            var graph = Rect(field, "Rooms and Connections", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            map = graph.gameObject.AddComponent<RunMapGraphic>();
            map.raycastTarget = false;
            empty = Label(Rect(field, "Missing Map", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
                "표시할 지도 데이터가 없습니다.", 20, Ink);
            empty.alignment = TextAlignmentOptions.Center;

            legend = Rect(card, "Legend", Vector2.zero, Vector2.right, new Vector2(25, 9), new Vector2(-25, 48));
            Paint(Rect(legend, "Current Room Color", Vector2.zero, Vector2.zero, new Vector2(4, 13), new Vector2(16, 25)), Gold);
            Label(Rect(legend, "Current Room Label", Vector2.zero, Vector2.one, new Vector2(26, 0), Vector2.zero),
                "현재 방", 17, Ink);
            SetOpen(false);
        }

        public bool IsOpen => modal.gameObject.activeSelf;
        public void SetLauncher(bool active) => launcher.gameObject.SetActive(active);
        public void SetOpen(bool active) => modal.gameObject.SetActive(active);

        public void Render(RunMapDisplay display)
        {
            heading.text = string.IsNullOrEmpty(display.ZoneName) ? $"{display.ZoneId}구역"
                : $"{display.ZoneId}구역 · {display.ZoneName}";
            empty.gameObject.SetActive(display.Rooms.Count == 0);
            legend.gameObject.SetActive(display.CurrentRoomId != null);
            map.Render(display);
        }

        private static void BindPointer(EventTrigger trigger, EventTriggerType kind, Action<int> change)
        {
            var entry = new EventTrigger.Entry { eventID = kind };
            entry.callback.AddListener(data =>
            {
                if (data is PointerEventData pointer && pointer.button == PointerEventData.InputButton.Left)
                    change(pointer.pointerId);
            });
            trigger.triggers.Add(entry);
        }

        private void Button(RectTransform rect, string text, Action callback)
        {
            var image = Paint(rect, ButtonColor, true);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => callback());
            Label(Rect(rect, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), text, 18, Ink)
                .alignment = TextAlignmentOptions.Center;
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

        private static UnityEngine.UI.Image Paint(RectTransform target, Color color, bool raycast = false)
        {
            var image = target.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

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
    }
}
