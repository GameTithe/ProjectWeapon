#nullable enable

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ProjectWeapon.CoinDrawUI
{
    internal sealed class CoinDrawResultView
    {
        private static readonly Color Surface = new Color32(29, 25, 41, 250);
        private static readonly Color Ink = new Color32(246, 239, 218, 255);
        private static readonly Color Row = new Color32(48, 48, 66, 255);
        private static readonly Color ActionSurface = new Color32(35, 75, 92, 255);
        private const string Discovery =
            "뽑기 획득은 도감의 발견 조건입니다. 회수 등록은 무기를 보유한 채 성공 귀환해야 합니다.";

        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI status;
        private readonly TextMeshProUGUI notice;
        private readonly TextMeshProUGUI[] awards = new TextMeshProUGUI[10];
        private readonly UnityEngine.UI.Button single;
        private readonly UnityEngine.UI.Button ten;
        private readonly UnityEngine.UI.Button retry;
        private readonly UnityEngine.UI.Button acknowledge;
        private readonly TMP_FontAsset font;

        public CoinDrawResultView(RectTransform parent, TMP_FontAsset font, Action drawSingle, Action drawTen,
            Action retryPurchase, Action acknowledgeResult)
        {
            this.font = font;
            var card = Rect(parent, "CoinDrawResult", new Vector2(.12f, .12f), new Vector2(.88f, .88f),
                Vector2.zero, Vector2.zero);
            Back(card, Surface);
            title = Label(Rect(card, "Title", Vector2.up, Vector2.one, new Vector2(16, -56), new Vector2(-16, -10)), 28);
            status = Label(Rect(card, "Status", Vector2.up, Vector2.one, new Vector2(16, -90), new Vector2(-16, -56)), 20);
            notice = Label(Rect(card, "DiscoveryNotice", Vector2.zero, Vector2.right,
                new Vector2(16, 54), new Vector2(-16, 117)), 18);
            notice.textWrappingMode = TextWrappingModes.Normal;
            notice.text = Discovery;

            var list = Rect(card, "Awards", Vector2.zero, Vector2.one, new Vector2(16, 120), new Vector2(-16, -93));
            for (int index = 0; index < awards.Length; index++)
            {
                int column = index % 2;
                int row = index / 2;
                var cell = Rect(list, $"Award{index + 1:00}", new Vector2(column * .5f, .8f - row * .2f),
                    new Vector2((column + 1) * .5f, 1f - row * .2f), new Vector2(3, 2), new Vector2(-3, -2));
                Back(cell, Row);
                awards[index] = Label(Rect(cell, "AwardLabel", Vector2.zero, Vector2.one,
                    new Vector2(8, 0), new Vector2(-8, 0)), 18);
                awards[index].alignment = TextAlignmentOptions.MidlineLeft;
            }

            single = Button(card, "DrawSingle", "1회 뽑기", .15f, .49f, drawSingle);
            ten = Button(card, "DrawTen", "10회 뽑기", .51f, .85f, drawTen);
            retry = Button(card, "RetryDraw", "같은 요청 다시 확인", .31f, .69f, retryPurchase);
            acknowledge = Button(card, "ConfirmDrawResult", "확인", .31f, .69f, acknowledgeResult);
        }

        public void ShowReady() => Show("무기 뽑기", string.Empty, ready: true);
        public void ShowBusy() => Show("무기 뽑기", "거래 확인 중");
        public void ShowProblem(string message, bool canRetry)
            => Show("무기 뽑기", message, offerRetry: canRetry, offerAcknowledge: !canRetry);

        public void ShowReceipt(IReadOnlyList<string> captions, int inWarehouse)
        {
            Show("뽑기 결과", $"확정 지급 {captions.Count}개  ·  현재 창고 {inWarehouse}개", offerAcknowledge: true);
            notice.gameObject.SetActive(true);
            for (int index = 0; index < captions.Count; index++)
            {
                awards[index].text = captions[index];
                awards[index].transform.parent.gameObject.SetActive(true);
            }
        }

        private void Show(string heading, string message, bool ready = false, bool offerRetry = false,
            bool offerAcknowledge = false)
        {
            title.text = heading;
            status.text = message;
            notice.gameObject.SetActive(false);
            foreach (TextMeshProUGUI award in awards)
                award.transform.parent.gameObject.SetActive(false);
            single.gameObject.SetActive(ready);
            ten.gameObject.SetActive(ready);
            retry.gameObject.SetActive(offerRetry);
            acknowledge.gameObject.SetActive(offerAcknowledge);
        }

        private UnityEngine.UI.Button Button(RectTransform parent, string name, string caption, float minX,
            float maxX, Action activate)
        {
            var target = Rect(parent, name, new Vector2(minX, 0), new Vector2(maxX, 0),
                new Vector2(0, 10), new Vector2(0, 50));
            var image = Back(target, ActionSurface);
            image.raycastTarget = true;
            var button = target.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => activate());
            var label = Label(Rect(target, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), 20);
            label.text = caption;
            return button;
        }

        private static UnityEngine.UI.Image Back(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TextMeshProUGUI Label(RectTransform rect, float fontSize)
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = Ink;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.richText = false;
            return text;
        }

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
