using System;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>
    /// Celebration sheet after the reveal, with the solved loop still playing above it: stars slam in over a
    /// sunburst, a ribbon, time/hint chips, total-star goal, then Next (primary), Share clip and Levels.
    /// </summary>
    public sealed class WinScreen : UIScreen
    {
        static UITheme T => UITheme.Current;

        readonly RectTransform card, ribbon, burst, chipsRow, timeChip, hintChip, extraChip, goalFill;
        readonly Text ribbonTitle, goalText, credit;
        readonly Image[] slots, fills;
        readonly Button next, share, levels;
        readonly Action<PuzzleResult> onShare;
        PuzzleResult result;
        float anim = -1f;
        int starsLanded;

        const float CardDp = 352f;
        static readonly float[] StarTimes = { 0.18f, 0.51f, 0.84f };

        public WinScreen(Transform safeRoot, Action onNext, Action onLevels, Action<PuzzleResult> onShare)
        {
            this.onShare = onShare;
            Root = UIFactory.Stretch(UIFactory.Rect("Win", safeRoot));
            float pad = T.U(12f);
            float cardH = T.U(CardDp);

            var scrim = UIFactory.Image("Scrim", Root, Color.white);
            var srt = scrim.rectTransform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = new Vector2(1f, 0.75f); srt.offsetMin = srt.offsetMax = Vector2.zero;
            UIFactory.Gradient(scrim, new Color(T.bgBottom.r, T.bgBottom.g, T.bgBottom.b, 0f), new Color(T.bgBottom.r, T.bgBottom.g, T.bgBottom.b, 0.85f));

            // Stars and sunburst float above the card.
            burst = UIFactory.Rect("Burst", Root);
            // Centred about 80 dp above the card so the stars clear the ribbon; the card covers the lower rays.
            UIFactory.Place(burst, new Vector2(0.5f, 0f), new Vector2(T.U(330f), T.U(330f)), new Vector2(0f, pad + cardH - T.U(85f)));
            var glow = UIFactory.Image("Glow", burst, new Color(T.gold.r, T.gold.g, T.gold.b, 0.45f), UISprites.SoftCircle);
            UIFactory.Stretch(glow.rectTransform, T.U(60f), T.U(60f), T.U(60f), T.U(60f));
            var rays = UIFactory.Image("Rays", burst, new Color(T.gold.r, T.gold.g, T.gold.b, 0.55f), UISprites.Rays);
            UIFactory.Stretch(rays.rectTransform);

            card = UIFactory.Card("Card", Root, 28f);
            card.anchorMin = new Vector2(0f, 0f);
            card.anchorMax = new Vector2(1f, 0f);
            card.pivot = new Vector2(0.5f, 0f);
            card.sizeDelta = new Vector2(-2f * pad, cardH);
            card.anchoredPosition = new Vector2(0f, pad);

            var starRow = UIFactory.Rect("StarRow", burst);
            UIFactory.Place(starRow, new Vector2(0.5f, 0.5f), new Vector2(T.U(240f), T.U(90f)), new Vector2(0f, -T.U(4f)));
            float starSize = T.U(62f), gap = T.U(10f);
            slots = UIFactory.Stars(starRow, starSize, gap, raisedMiddle: true);
            fills = UIFactory.Stars(starRow, starSize, gap, raisedMiddle: true);
            UIFactory.SetStars(fills, 3);

            ribbon = UIFactory.Ribbon("Ribbon", card, "Solved!", T.ribbonWin, 230f);
            UIFactory.Place(ribbon, new Vector2(0.5f, 1f), ribbon.sizeDelta, new Vector2(0f, T.U(22f)));
            ribbonTitle = ribbon.Find("Band/Title").GetComponent<Text>();

            chipsRow = UIFactory.Rect("Chips", card);
            UIFactory.Place(chipsRow, new Vector2(0.5f, 1f), new Vector2(T.U(340f), T.U(30f)), new Vector2(0f, -T.U(42f)));
            var row = chipsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = T.U(8f);
            row.childControlWidth = true;
            row.childControlHeight = false;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            timeChip = UIFactory.Chip("Time", chipsRow, "0:00", UISprites.Clock, T.cyan);
            hintChip = UIFactory.Chip("Hints", chipsRow, "No hints", UISprites.Sparkle, T.star);
            extraChip = UIFactory.Chip("Extra", chipsRow, "First clear", UISprites.Check, T.green);

            float inner = T.U(16f);
            var goal = UIFactory.Sunk("Goal", card, 14f);
            var grt = goal.rectTransform;
            grt.anchorMin = new Vector2(0f, 1f); grt.anchorMax = new Vector2(1f, 1f); grt.pivot = new Vector2(0.5f, 1f);
            grt.sizeDelta = new Vector2(-2f * inner, T.U(46f));
            grt.anchoredPosition = new Vector2(0f, -T.U(84f));
            var goalStar = UIFactory.StarIcon("Star", goal.transform, T.U(24f));
            UIFactory.Place((RectTransform)goalStar.transform.parent, new Vector2(0f, 0.5f), new Vector2(T.U(24f), T.U(24f)), new Vector2(T.U(12f), 0f));
            UIFactory.SetStar(goalStar, true);
            goalText = UIFactory.Text("Text", goal.transform, "", TextRole.Label, T.labelS, T.text, TextAnchor.UpperLeft);
            UIFactory.Stretch(goalText.rectTransform, T.U(46f), T.U(12f), T.U(6f), T.U(18f));
            var bar = UIFactory.RoundRect("Bar", goal.transform, new Color(0, 0, 0, 0.35f), 4f);
            var brt = bar.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f); brt.pivot = new Vector2(0f, 0f);
            brt.offsetMin = new Vector2(T.U(46f), T.U(9f)); brt.offsetMax = new Vector2(-T.U(14f), T.U(17f));
            var fill = UIFactory.RoundRect("Fill", bar.transform, Color.white, 4f);
            goalFill = fill.rectTransform;
            goalFill.anchorMin = Vector2.zero; goalFill.anchorMax = new Vector2(0f, 1f); goalFill.offsetMin = goalFill.offsetMax = Vector2.zero;
            UIFactory.Gradient(fill, T.gold, T.orange, GradientDirection.Horizontal);

            float heroH = UIFactory.ButtonHeight(ButtonSize.Hero), largeH = UIFactory.ButtonHeight(ButtonSize.Large);
            next = UIFactory.JuicyButton("Next", card, ButtonTone.Primary, ButtonSize.Hero, "Next puzzle", () => onNext(), UISprites.Next, "Level 2");
            var nrt = (RectTransform)next.transform;
            nrt.anchorMin = new Vector2(0f, 0f); nrt.anchorMax = new Vector2(1f, 0f); nrt.pivot = new Vector2(0.5f, 0f);
            nrt.sizeDelta = new Vector2(-2f * inner, heroH);
            nrt.anchoredPosition = new Vector2(0f, T.U(30f) + largeH + T.U(10f));

            share = UIFactory.JuicyButton("Share", card, ButtonTone.Blue, ButtonSize.Large, "Share clip", () => this.onShare?.Invoke(result), UISprites.Share);
            levels = UIFactory.JuicyButton("Levels", card, ButtonTone.Neutral, ButtonSize.Large, "Levels", () => onLevels(), UISprites.Grid);

            credit = UIFactory.Text("Credit", card, "", TextRole.Body, T.bodyS - 1f, T.textMute);
            UIFactory.Place(credit.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(340f), T.U(20f)), new Vector2(0f, T.U(7f)));
            Hide();
        }

        void LayoutSecondary(bool canShare)
        {
            float inner = T.U(16f), gap = T.U(10f), h = UIFactory.ButtonHeight(ButtonSize.Large), y = T.U(30f);
            var lrt = (RectTransform)levels.transform;
            lrt.pivot = new Vector2(0f, 0f);
            if (canShare)
            {
                var srt = (RectTransform)share.transform;
                srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(0.5f, 0f); srt.pivot = new Vector2(0f, 0f);
                srt.offsetMin = new Vector2(inner, y); srt.offsetMax = new Vector2(-gap * 0.5f, y + h);
                lrt.anchorMin = new Vector2(0.5f, 0f); lrt.anchorMax = new Vector2(1f, 0f);
                lrt.offsetMin = new Vector2(gap * 0.5f, y); lrt.offsetMax = new Vector2(-inner, y + h);
            }
            else
            {
                lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 0f);
                lrt.offsetMin = new Vector2(inner, y); lrt.offsetMax = new Vector2(-inner, y + h);
            }
        }

        public void ShowResult(PuzzleResult r, bool hasNext)
        {
            result = r;
            Show();
            ribbonTitle.text = r.Stars >= 3 ? "Perfect!" : r.IsFirstClear ? "Solved!" : "Solved again!";

            int secs = Mathf.RoundToInt(r.ElapsedSec);
            UIFactory.ChipText(timeChip).text = (secs / 60) + ":" + (secs % 60).ToString("00");
            UIFactory.ChipText(hintChip).text = r.HintsUsed == 0 ? "No hints" : r.HintsUsed == 1 ? "1 hint" : r.HintsUsed + " hints";
            extraChip.gameObject.SetActive(r.ForcedByDevMenu || r.IsFirstClear);
            UIFactory.ChipText(extraChip).text = r.ForcedByDevMenu ? "Dev win" : "First clear";

            var all = LevelCatalog.Levels;
            int total = 0;
            for (int i = 0; i < all.Count; i++) total += Progress.GetStars(all[i].id);
            int max = Mathf.Max(1, all.Count * 3);
            goalText.text = total + " / " + max + " stars collected";
            goalFill.anchorMax = new Vector2(Mathf.Clamp01(total / (float)max), 1f);

            var nextLevel = hasNext ? LevelCatalog.Get(r.LevelIndex + 1) : null;
            if (nextLevel != null) UIFactory.SetButtonText(next, "Next puzzle", "Level " + (r.LevelIndex + 2) + " · " + nextLevel.DisplayTitle);
            else UIFactory.SetButtonText(next, "All done!", "Back to all puzzles");
            credit.text = string.IsNullOrEmpty(r.Level.credit) ? "" : "Video: " + r.Level.credit;

            var sp = GameServices.Share;
            bool canShare = sp != null && sp.IsAvailable;
            share.gameObject.SetActive(canShare);
            if (canShare) UIFactory.SetButtonText(share, sp.ButtonLabel);
            LayoutSecondary(canShare);

            UIFactory.SetStars(slots, 0);
            for (int i = 0; i < fills.Length; i++)
            {
                var rt = (RectTransform)fills[i].transform.parent;
                rt.gameObject.SetActive(false);
            }
            starsLanded = 0;
            anim = 0f;

            UITween.Get(card).Move(new Vector2(0f, -T.U(CardDp + 40f)), Vector2.zero).Play(T.medium, EaseType.OutCubic);
            UITween.Get(burst).Fade(0f, 1f).Scale(0.6f, 1f).Play(T.medium, EaseType.OutBack, 0.1f);
            UITween.Get(burst.Find("Rays")).Rotate(0f, 120f).Play(9f, EaseType.OutCubic);
            UITween.Get(ribbon).Scale(0.4f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, 0.26f);
            UITween.Get(next).Scale(0.8f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, 0.4f);
            if (UIFx.Instance != null) UIFx.Instance.Confetti();
        }

        public override void Hide()
        {
            base.Hide();
            anim = -1f;
        }

        public void Tick(float dt)
        {
            if (anim < 0f || !IsVisible) return;
            anim += dt;
            while (starsLanded < StarTimes.Length && anim >= T.medium + StarTimes[starsLanded])
            {
                int i = starsLanded++;
                if (i >= result.Stars) continue;
                var rt = (RectTransform)fills[i].transform.parent;
                rt.gameObject.SetActive(true);
                UITween.Get(rt).Scale(2.2f, 1f).Fade(0f, 1f).Play(0.375f, EaseType.OutElastic);
                Haptics.Tick();
                Sfx.PlayClick();
            }
            if (starsLanded >= StarTimes.Length) anim = -1f;
        }
    }
}
