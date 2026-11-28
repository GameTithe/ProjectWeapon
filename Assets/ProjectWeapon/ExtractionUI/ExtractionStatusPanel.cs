#nullable enable

using System;
using ProjectWeapon.Extraction;
using ProjectWeapon.Run;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.ExtractionUI
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class ExtractionStatusPanel : MonoBehaviour
    {
        private static readonly Color Panel = new Color32(32, 27, 42, 245);
        private static readonly Color Track = new Color32(59, 52, 74, 255);
        private static readonly Color Ink = new Color32(244, 233, 219, 255);
        private static readonly Color Blue = new Color32(121, 203, 240, 255);
        private static readonly Color Amber = new Color32(246, 183, 120, 255);
        private static readonly Color Green = new Color32(139, 222, 174, 255);

        private RunState run = null!;
        private ExtractionChannel channel = null!;
        private RectTransform? content;
        private RectTransform track = null!;
        private RectTransform fill = null!;
        private UnityEngine.UI.Image fillImage = null!;
        private TextMeshProUGUI title = null!;
        private TextMeshProUGUI percentage = null!;
        private int lastPercentage = -1;

        /// <summary>Use the same run and channel that the owning gameplay frame resolves.</summary>
        public void Initialize(RunState run, ExtractionChannel channel, TMP_FontAsset font)
        {
            if (content)
                throw new InvalidOperationException("This extraction status panel is already initialized.");
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
            if (!font)
                throw new ArgumentNullException(nameof(font));
            if (!Resources.Load<TMP_Settings>("TMP Settings"))
                throw new InvalidOperationException("Import TMP Essential Resources before initializing the extraction panel.");

            content = Rect(transform, "Status", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image(content, Panel);
            title = Label(Rect(content, "Title", new Vector2(0, .28f), Vector2.one,
                new Vector2(16, 0), new Vector2(-16, -5)), font, Ink);
            percentage = Label(Rect(content, "Percentage", new Vector2(.75f, .28f), Vector2.one,
                Vector2.zero, new Vector2(-16, -5)), font, Blue);
            percentage.alignment = TextAlignmentOptions.MidlineRight;
            track = Rect(content, "Progress Track", Vector2.zero, Vector2.right,
                new Vector2(16, 12), new Vector2(-16, 22));
            Image(track, Track);
            fill = Rect(track, "Progress Fill", Vector2.zero, Vector2.up, Vector2.zero, Vector2.zero);
            fillImage = Image(fill, Blue);

            Render();
        }

        /// <summary>Refresh after an accepted start or after resolving the frame's observations.</summary>
        public void Render()
        {
            if (!content)
                return;

            ExtractionStatusDisplay status = ExtractionStatusDisplay.Read(run, channel);
            bool visible = status.Kind != ExtractionStatusKind.Hidden;
            content!.gameObject.SetActive(visible);
            if (!visible)
                return;

            bool hasProgress = status.Kind != ExtractionStatusKind.Cancelled;
            track.gameObject.SetActive(hasProgress);
            percentage.gameObject.SetActive(hasProgress);
            title.rectTransform.anchorMin = new Vector2(0, hasProgress ? .28f : 0);
            title.rectTransform.anchorMax = new Vector2(hasProgress ? .75f : 1, 1);
            title.text = status.Title;
            title.color = status.Kind == ExtractionStatusKind.Cancelled ? Amber : Ink;

            if (!hasProgress)
                return;

            Color progressColor = status.Kind == ExtractionStatusKind.Succeeded ? Green : Blue;
            fill.anchorMax = new Vector2(status.Fraction, 1);
            fillImage.color = progressColor;
            percentage.color = progressColor;
            if (lastPercentage != status.Percentage)
            {
                percentage.SetText("{0:0}%", status.Percentage);
                lastPercentage = status.Percentage;
            }
        }

        private static UnityEngine.UI.Image Image(RectTransform target, Color color)
        {
            var image = target.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Label(RectTransform target, TMP_FontAsset font, Color color)
        {
            var label = target.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.color = color;
            label.fontSize = label.fontSizeMax = 22;
            label.fontSizeMin = 16;
            label.enableAutoSizing = true;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            label.richText = false;
            return label;
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
