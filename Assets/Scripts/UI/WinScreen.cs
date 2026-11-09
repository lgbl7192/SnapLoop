using System;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>
    /// Celebration sheet after the reveal, with the solved loop still playing above it: stars slam in over a
    /// sunburst, a ribbon, time/hint/token chips, the pack's star goal, then the optional rewarded "Double tokens"
    /// offer (green, only while an ad is ready), Next (primary), Share clip and Levels.
    /// </summary>
    public sealed class WinScreen : UIScreen
    {
        static UITheme T => UITheme.Current;

        readonly RectTransform card, ribbon, burst, chipsRow, timeChip, hintChip, extraChip, tokenChip, goalFill;
        readonly Text ribbonTitle, goalText, credit;
        readonly Image[] slots, fills;
        readonly Button next, share, levels, doubleTokens;
        readonly Pulse doublePulse;
        readonly Action<PuzzleResult> onShare;
        PuzzleResult result;
        float anim = -1f;
        int starsLanded;
        bool doubleClaimed, doublePending;

        const float CardDp = 352f;
        float OfferDp => UIFactory.ButtonHeight(ButtonSize.Large) / T.dp + 10f;
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
            tokenChip = UIFactory.TokenChip("Tokens", chipsRow, "+0");

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

            doubleTokens = UIFactory.JuicyButton("DoubleTokens", card, ButtonTone.Reward, ButtonSize.Large, "Double tokens", OnDoubleTokens);
            var drt = (RectTransform)doubleTokens.transform;
            drt.anchorMin = new Vector2(0f, 0f); drt.anchorMax = new Vector2(1f, 0f); drt.pivot = new Vector2(0.5f, 0f);
            drt.sizeDelta = new Vector2(-2f * inner, largeH);
            drt.anchoredPosition = new Vector2(0f, nrt.anchoredPosition.y + heroH + T.U(10f));
            var dparts = doubleTokens.GetComponent<JuicyButtonParts>();
            var dIcon = UIFactory.TokenIcon("Token", dparts.content, T.U(28f));
            dIcon.SetAsFirstSibling();
            var dle = dIcon.gameObject.AddComponent<LayoutElement>();
            dle.preferredWidth = dle.preferredHeight = T.U(28f);
            UIFactory.SetRewardedLook(doubleTokens, true);
            doublePulse = dparts.face.gameObject.AddComponent<Pulse>();
            doubleTokens.gameObject.SetActive(false);
            GameServices.RewardedOffersChanged += RefreshDoubleOffer;

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

        /// <summary>Shows the result; <paramref name="nextLevel"/> is the next puzzle in the same pack, or null at its end.</summary>
        public void ShowResult(PuzzleResult r, LevelInfo nextLevel)
        {
            result = r;
            doubleClaimed = doublePending = false;
            Show();
            ribbonTitle.text = r.Stars >= 3 ? "Perfect!" : r.IsFirstClear ? "Solved!" : "Solved again!";

            int secs = Mathf.RoundToInt(r.ElapsedSec);
            UIFactory.ChipText(timeChip).text = (secs / 60) + ":" + (secs % 60).ToString("00");
            UIFactory.ChipText(hintChip).text = r.HintsUsed == 0 ? "No hints" : r.HintsUsed == 1 ? "1 hint" : r.HintsUsed + " hints";
            // Four chips don't fit the card: the token chip stands in for "First clear".
            bool paid = r.TokensEarned > 0;
            extraChip.gameObject.SetActive(r.ForcedByDevMenu || (r.IsFirstClear && !paid));
            UIFactory.ChipText(extraChip).text = r.ForcedByDevMenu ? "Dev win" : "First clear";
            tokenChip.gameObject.SetActive(paid);
            UIFactory.ChipText(tokenChip).text = "+" + r.TokensEarned;

            var pack = PackCatalog.For(r.Level);
            int total, max;
            if (pack != null) total = PackAccess.StarsIn(pack, out max);
            else
            {
                var all = LevelCatalog.Levels;
                total = 0;
                for (int i = 0; i < all.Count; i++) total += Progress.GetStars(all[i].id);
                max = all.Count * 3;
            }
            max = Mathf.Max(1, max);
            goalText.text = total + " / " + max + (pack == null || pack.IsStarter ? " stars collected" : " stars in " + pack.DisplayTitle);
            goalFill.anchorMax = new Vector2(Mathf.Clamp01(total / (float)max), 1f);

            if (nextLevel != null)
                UIFactory.SetButtonText(next, "Next puzzle", LevelSelectScreen.LevelLabel(nextLevel, PackCatalog.For(nextLevel)) + " · " + nextLevel.DisplayTitle);
            else if (pack != null && !pack.IsStarter) UIFactory.SetButtonText(next, "Pack complete!", "Back to the puzzles");
            else UIFactory.SetButtonText(next, "All done!", "Back to all puzzles");
            RefreshDoubleOffer();
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
            if (doubleTokens.gameObject.activeSelf)
                UITween.Get(doubleTokens).Scale(0.8f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, 0.48f);
            if (UIFx.Instance != null) UIFx.Instance.Confetti();
        }

        /// <summary>
        /// Shows the green "Double to N" offer only while <see cref="GameServices.DoubleTokens"/> says an ad is ready
        /// (caps and spacing are the ad policy's call), and grows the card to fit it. Once claimed it stays as a thank-you.
        /// </summary>
        void RefreshDoubleOffer()
        {
            if (!IsVisible) return;
            int earned = result.TokensEarned;
            var offer = GameServices.DoubleTokens;
            bool show = doubleClaimed || doublePending || (offer != null && offer.IsOffered(earned, doubleClaimed));
            bool wasShown = doubleTokens.gameObject.activeSelf;
            doubleTokens.gameObject.SetActive(show);
            if (show && !doubleClaimed)
            {
                UIFactory.SetRewardedLook(doubleTokens, true);
                UIFactory.SetButtonText(doubleTokens, "Double to " + (earned + TokenRules.DoubledBonus(earned)));
                doubleTokens.interactable = !doublePending;
                if (!wasShown) doublePulse.Restart();
            }
            LayoutCard(show);
        }

        void LayoutCard(bool withOffer)
        {
            float pad = T.U(12f);
            float cardH = T.U(CardDp + (withOffer ? OfferDp : 0f));
            card.sizeDelta = new Vector2(card.sizeDelta.x, cardH);
            burst.anchoredPosition = new Vector2(0f, pad + cardH - T.U(85f));
        }

        void OnDoubleTokens()
        {
            var offer = GameServices.DoubleTokens;
            if (offer == null || doubleClaimed || doublePending) return;
            if (!offer.IsOffered(result.TokensEarned, false)) { RefreshDoubleOffer(); return; }
            doublePending = true;
            doubleTokens.interactable = false;
            var forResult = result.Level;
            offer.Request(earned =>
            {
                doublePending = false;
                if (!IsVisible || result.Level != forResult) return;
                if (earned)
                {
                    int bonus = TokenRules.DoubledBonus(result.TokensEarned);
                    Tokens.Add(bonus);
                    doubleClaimed = true;
                    int total = result.TokensEarned + bonus;
                    UIFactory.ChipText(tokenChip).text = "+" + total;
                    UIFactory.SetRewardedLook(doubleTokens, false);
                    UIFactory.SetButtonText(doubleTokens, "Tokens doubled! +" + total);
                    doubleTokens.interactable = true;
                    UITween.Get(tokenChip).Scale(1.4f, 1f).Play(0.45f, EaseType.OutElastic);
                    Haptics.Success();
                    if (UIFx.Instance != null) UIFx.Instance.Confetti(40);
                }
                RefreshDoubleOffer();
            });
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
