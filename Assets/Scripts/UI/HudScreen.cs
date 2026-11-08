using System;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>In-puzzle HUD: top bar (back, title, progress, settings) and bottom bar (hint).</summary>
    public sealed class HudScreen : UIScreen
    {
        readonly Text title, progress, loading, toast;
        readonly Button hint;
        readonly Text hintLabel;
        readonly CanvasGroup toastGroup, barsGroup;
        float toastTime;

        // Pre-built "n / total" strings so progress updates do not allocate.
        string[] progressCache = Array.Empty<string>();
        int cachedTotal = -1;

        public HudScreen(Transform safeRoot, Action onBack, Action onSettings, Action onHint)
        {
            Root = UIFactory.Stretch(UIFactory.Rect("Hud", safeRoot));
            barsGroup = Root.gameObject.AddComponent<CanvasGroup>();

            var top = UIFactory.Band(UIFactory.Rect("TopBar", Root), 1f, UILayout.TopBar);
            var back = UIFactory.Button("Back", top, "Levels", Palette.Surface, () => onBack(), 38);
            UIFactory.Place((RectTransform)back.transform, new Vector2(0f, 0.5f), new Vector2(220, 104), new Vector2(28, 0));
            var gear = UIFactory.Button("Settings", top, "Settings", Palette.Surface, () => onSettings(), 34);
            UIFactory.Place((RectTransform)gear.transform, new Vector2(1f, 0.5f), new Vector2(220, 104), new Vector2(-28, 0));

            title = UIFactory.Label("Title", top, "", 44, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(560, 70), new Vector2(0, 22));
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            progress = UIFactory.Label("Progress", top, "", 36, Palette.TextDim);
            UIFactory.Place(progress.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(560, 56), new Vector2(0, -32));

            var bottom = UIFactory.Band(UIFactory.Rect("BottomBar", Root), 0f, UILayout.BottomBar);
            hint = UIFactory.Button("Hint", bottom, "Hint", Palette.Accent, () => onHint(), 46);
            UIFactory.Place((RectTransform)hint.transform, new Vector2(0.5f, 0.5f), new Vector2(460, 130));
            hintLabel = UIFactory.ButtonLabel(hint);

            loading = UIFactory.Label("Loading", Root, "Loading…", 48, Palette.TextDim);
            UIFactory.Place(loading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(800, 100));

            var toastBg = UIFactory.Panel("Toast", Root, new Color(0f, 0f, 0f, 0.8f), raycast: false);
            UIFactory.Place(toastBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(940, 120), new Vector2(0, UILayout.BottomBar + 30));
            toastGroup = toastBg.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toast = UIFactory.Label("Text", toastBg.transform, "", 36, Palette.Text);
            UIFactory.Stretch(toast.rectTransform, 24, 24, 8, 8);
            Hide();
        }

        public void ShowFor(LevelInfo level)
        {
            Show();
            barsGroup.alpha = 1f;
            barsGroup.interactable = true;
            title.text = (level.Index + 1) + " · " + level.DisplayTitle;
            progress.text = "";
            SetLoading(true);
            SetHintInteractable(false);
        }

        public void SetLoading(bool on) => loading.gameObject.SetActive(on);

        public void SetHintInteractable(bool on) => hint.interactable = on;

        /// <summary>A hint that is not on offer is hidden, not greyed out.</summary>
        public void SetHintOffer(string label, bool offered, bool rewardedAd)
        {
            hintLabel.text = label;
            UIFactory.SetRewardedLook(hint, rewardedAd, Palette.Accent);
            hint.gameObject.SetActive(offered);
        }

        public void SetProgress(int placed, int total)
        {
            if (total != cachedTotal)
            {
                cachedTotal = total;
                progressCache = new string[total + 1];
                for (int i = 0; i <= total; i++) progressCache[i] = i + " / " + total;
            }
            progress.text = progressCache[Mathf.Clamp(placed, 0, total)];
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
        }

        public void Tick(float dt)
        {
            if (toastTime <= 0f) return;
            toastTime -= dt;
            toastGroup.alpha = Mathf.Clamp01(toastTime / 0.3f);
        }
    }
}
