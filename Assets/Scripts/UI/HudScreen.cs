using System;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>
    /// In-puzzle HUD, kept minimal so the video is the hero: back, level and title, a progress ring for pieces
    /// placed and settings on top; the hint (rewarded style when it plays an ad) at the bottom.
    /// </summary>
    public sealed class HudScreen : UIScreen
    {
        static UITheme T => UITheme.Current;

        readonly RectTransform top, bottom;
        readonly Text caps, title, loading, toast;
        readonly UIFactory.RingGauge ring;
        readonly Button hint;
        readonly RectTransform loadingRoot;
        readonly CanvasGroup toastGroup, barsGroup;
        float toastTime;

        // Pre-built "n/total" strings so progress updates do not allocate.
        string[] progressCache = Array.Empty<string>();
        int cachedTotal = -1;

        public HudScreen(Transform safeRoot, Action onBack, Action onSettings, Action onHint, Action onDev = null)
        {
            Root = UIFactory.Stretch(UIFactory.Rect("Hud", safeRoot));
            barsGroup = Root.gameObject.AddComponent<CanvasGroup>();
            float pad = T.U(T.screenPad);

            top = UIFactory.Band(UIFactory.Rect("TopBar", Root), 1f, UILayout.TopBar);
            var back = UIFactory.RoundButton("Back", top, UISprites.Back, () => onBack());
            UIFactory.Place((RectTransform)back.transform, new Vector2(0f, 0.5f), ((RectTransform)back.transform).sizeDelta, new Vector2(pad, 0f));
            var gear = UIFactory.RoundButton("Settings", top, UISprites.Gear, () => onSettings());
            UIFactory.Place((RectTransform)gear.transform, new Vector2(1f, 0.5f), ((RectTransform)gear.transform).sizeDelta, new Vector2(-pad, 0f));

            ring = UIFactory.Ring("Progress", top, 54f);
            UIFactory.Place(ring.Root, new Vector2(1f, 0.5f), ring.Root.sizeDelta, new Vector2(-pad - T.U(T.roundButton + 10f), 0f));

            float textLeft = pad + T.U(T.roundButton + 12f);
            float textRight = pad + T.U(T.roundButton + 10f + 54f + 10f);
            caps = UIFactory.Text("Level", top, "", TextRole.Caps, T.caps, T.star, TextAnchor.LowerLeft);
            var crt = caps.rectTransform;
            crt.anchorMin = new Vector2(0f, 0.5f); crt.anchorMax = new Vector2(1f, 0.5f);
            crt.offsetMin = new Vector2(textLeft, T.U(6f)); crt.offsetMax = new Vector2(-textRight, T.U(24f));
            title = UIFactory.Text("Title", top, "", TextRole.Title, T.titleXS + 3f, Color.white, TextAnchor.UpperLeft);
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.5f); trt.anchorMax = new Vector2(1f, 0.5f);
            trt.offsetMin = new Vector2(textLeft, -T.U(26f)); trt.offsetMax = new Vector2(-textRight, T.U(4f));
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = T.F(14f);
            title.resizeTextMaxSize = T.F(T.titleXS + 3f);
            title.verticalOverflow = VerticalWrapMode.Truncate;

            bottom = UIFactory.Band(UIFactory.Rect("BottomBar", Root), 0f, UILayout.BottomBar);
            hint = UIFactory.JuicyButton("Hint", bottom, ButtonTone.Blue, ButtonSize.Large, "Hint", () => onHint(), UISprites.Sparkle);
            UIFactory.Place((RectTransform)hint.transform, new Vector2(0.5f, 0.5f), new Vector2(T.U(220f), UIFactory.ButtonHeight(ButtonSize.Large)));

            if (onDev != null)
            {
                var dev = UIFactory.JuicyButton("Dev", bottom, ButtonTone.Neutral, ButtonSize.Medium, "DEV", () => onDev());
                UIFactory.ButtonLabel(dev).fontSize = T.F(14f);
                UIFactory.Place((RectTransform)dev.transform, new Vector2(0f, 0.5f), new Vector2(T.U(60f), T.U(T.minTouch)), new Vector2(pad, 0f));
            }

            loadingRoot = UIFactory.Rect("Loading", Root);
            UIFactory.Place(loadingRoot, new Vector2(0.5f, 0.5f), new Vector2(T.U(260f), T.U(110f)));
            var spinner = UIFactory.Icon("Spinner", loadingRoot, UISprites.Spinner, T.sunsetB, 44f);
            UIFactory.Place(spinner.rectTransform, new Vector2(0.5f, 1f), spinner.rectTransform.sizeDelta);
            UIFactory.Gradient(spinner, T.sunsetA, T.sunsetB);
            spinner.gameObject.AddComponent<Spin>().degreesPerSecond = -300f;
            loading = UIFactory.Text("Text", loadingRoot, "Loading…", TextRole.Label, T.label, T.textDim);
            UIFactory.Place(loading.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(260f), T.U(40f)));

            var toastBg = UIFactory.RoundRect("Toast", Root, new Color(T.ink.r, T.ink.g, T.ink.b, 0.92f), 18f);
            UIFactory.Place(toastBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(352f), T.U(52f)), new Vector2(0f, UILayout.BottomBar + T.U(10f)));
            UIFactory.Stroke("Hairline", toastBg.transform, T.stroke, 18f);
            toastGroup = toastBg.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastGroup.blocksRaycasts = false;
            toast = UIFactory.Text("Text", toastBg.transform, "", TextRole.Label, T.labelS, Color.white);
            UIFactory.Stretch(toast.rectTransform, T.U(16f), T.U(16f), T.U(4f), T.U(4f));
            Hide();
        }

        public void ShowFor(LevelInfo level)
        {
            Show();
            barsGroup.alpha = 1f;
            barsGroup.interactable = true;
            barsGroup.blocksRaycasts = true;
            caps.text = UIFactory.Track("Level " + (level.Index + 1));
            caps.color = T.Category(level.category);
            title.text = level.DisplayTitle;
            ring.Set(0f, "", instant: true);
            SetLoading(true);
            SetHintInteractable(false);
            UITween.Get(top).Fade(0f, 1f).Move(new Vector2(0f, T.U(30f)), Vector2.zero).Play(T.medium, EaseType.OutCubic);
            UITween.Get(bottom).Fade(0f, 1f).Move(new Vector2(0f, -T.U(30f)), Vector2.zero).Play(T.medium, EaseType.OutCubic, 0.08f);
        }

        public void SetLoading(bool on) => loadingRoot.gameObject.SetActive(on);

        public void SetHintInteractable(bool on) => hint.interactable = on;

        /// <summary>A hint that is not on offer is hidden, not greyed out.</summary>
        public void SetHintOffer(string label, bool offered, bool rewardedAd)
        {
            UIFactory.SetButtonText(hint, label);
            UIFactory.SetRewardedLook(hint, rewardedAd);
            if (offered && !hint.gameObject.activeSelf)
            {
                hint.gameObject.SetActive(true);
                UITween.Get(hint).Scale(0.6f, 1f).Play(T.medium, EaseType.OutBack);
            }
            else if (!offered) hint.gameObject.SetActive(false);
        }

        public void SetProgress(int placed, int total)
        {
            if (total != cachedTotal)
            {
                cachedTotal = total;
                progressCache = new string[total + 1];
                for (int i = 0; i <= total; i++) progressCache[i] = i + "/" + total;
            }
            int p = Mathf.Clamp(placed, 0, total);
            ring.Set(total > 0 ? p / (float)total : 0f, progressCache[p]);
            if (p > 0) UITween.Get(ring.Root).Scale(1.18f, 1f).Play(T.quick, EaseType.OutBack);
        }

        /// <summary>Fades the bars out during the reveal so the full loop is unobstructed.</summary>
        public void SetBarsVisible(bool on)
        {
            barsGroup.alpha = on ? 1f : 0f;
            barsGroup.interactable = on;
            barsGroup.blocksRaycasts = on;
        }

        public void Toast(string message, float seconds = 3f)
        {
            toast.text = message;
            toastTime = seconds;
            toastGroup.alpha = 1f;
            UITween.Get(toastGroup).Scale(0.8f, 1f).Play(T.quick, EaseType.OutBack);
        }

        public void Tick(float dt)
        {
            ring.Tick(dt);
            if (toastTime <= 0f) return;
            toastTime -= dt;
            toastGroup.alpha = Mathf.Clamp01(toastTime / 0.3f);
        }
    }
}
