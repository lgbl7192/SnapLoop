using System;
using Snaploop.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>Bottom card shown after the reveal: stars, time, Next / Levels / optional Share.</summary>
    public sealed class WinScreen : UIScreen
    {
        readonly RectTransform card;
        readonly Text heading, stats, credit;
        readonly Image[] stars;
        readonly Button next, share;
        readonly Text nextLabel, shareLabel;
        readonly Action<PuzzleResult> onShare;
        PuzzleResult result;
        float anim = -1f;

        const float CardHeight = 700f;

        public WinScreen(Transform safeRoot, Action onNext, Action onLevels, Action<PuzzleResult> onShare)
        {
            this.onShare = onShare;
            Root = UIFactory.Stretch(UIFactory.Rect("Win", safeRoot));

            var cardImg = UIFactory.Panel("Card", Root, new Color(0.07f, 0.08f, 0.11f, 0.94f));
            card = cardImg.rectTransform;
            UIFactory.Band(card, 0f, CardHeight, 24f);
            card.anchoredPosition = new Vector2(0f, 24f);

            heading = UIFactory.Label("Heading", card, "Solved!", 76, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(900, 110), new Vector2(0, -30));

            var starRow = UIFactory.Rect("StarRow", card);
            UIFactory.Place(starRow, new Vector2(0.5f, 1f), new Vector2(500, 120), new Vector2(0, -150));
            stars = UIFactory.Stars(starRow, 110, 24);

            stats = UIFactory.Label("Stats", card, "", 38, Palette.TextDim);
            UIFactory.Place(stats.rectTransform, new Vector2(0.5f, 1f), new Vector2(900, 60), new Vector2(0, -280));

            next = UIFactory.Button("Next", card, "Next", Palette.Accent, () => onNext(), 52);
            UIFactory.Place((RectTransform)next.transform, new Vector2(0.5f, 0f), new Vector2(560, 140), new Vector2(0, 190));
            nextLabel = UIFactory.ButtonLabel(next);

            var levels = UIFactory.Button("Levels", card, "Levels", Palette.SurfaceHigh, () => onLevels(), 40);
            UIFactory.Place((RectTransform)levels.transform, new Vector2(0.5f, 0f), new Vector2(270, 110), new Vector2(-150, 50));

            share = UIFactory.Button("Share", card, "Share", Palette.Accent2, () => this.onShare?.Invoke(result), 40);
            UIFactory.Place((RectTransform)share.transform, new Vector2(0.5f, 0f), new Vector2(270, 110), new Vector2(150, 50));
            shareLabel = UIFactory.ButtonLabel(share);

            credit = UIFactory.Label("Credit", card, "", 26, Palette.TextDim);
            UIFactory.Place(credit.rectTransform, new Vector2(0.5f, 0f), new Vector2(900, 40), new Vector2(0, 8));
            Hide();
        }

        public void ShowResult(PuzzleResult r, bool hasNext)
        {
            result = r;
            Show();
            heading.text = r.IsFirstClear ? "Solved!" : "Solved again!";
            UIFactory.SetStars(stars, 0);
            int secs = Mathf.RoundToInt(r.ElapsedSec);
            string time = (secs / 60) + ":" + (secs % 60).ToString("00");
            stats.text = r.ForcedByDevMenu
                ? "Dev win"
                : time + "   ·   " + (r.HintsUsed == 0 ? "no hints" : r.HintsUsed == 1 ? "1 hint" : r.HintsUsed + " hints");
            nextLabel.text = hasNext ? "Next" : "All done!";
            credit.text = string.IsNullOrEmpty(r.Level.credit) ? "" : "Video: " + r.Level.credit;

            var sp = GameServices.Share;
            bool canShare = sp != null && sp.IsAvailable;
            share.gameObject.SetActive(canShare);
            if (canShare) shareLabel.text = sp.ButtonLabel;
            var levelsRt = (RectTransform)card.Find("Levels");
            levelsRt.anchoredPosition = new Vector2(canShare ? -150 : 0, 50);

            anim = 0f;
            card.anchoredPosition = new Vector2(0f, -CardHeight);
        }

        public void Tick(float dt)
        {
            if (anim < 0f || !IsVisible) return;
            anim += dt;
            float t = Mathf.Clamp01(anim / 0.3f);
            float e = 1f - (1f - t) * (1f - t) * (1f - t);
            card.anchoredPosition = new Vector2(0f, Mathf.Lerp(-CardHeight, 24f, e));
            int shown = Mathf.Clamp(Mathf.FloorToInt((anim - 0.3f) / 0.18f) + 1, 0, result.Stars);
            for (int i = 0; i < stars.Length; i++)
            {
                bool on = i < shown;
                stars[i].color = on ? Palette.Star : Palette.StarEmpty;
                float pop = on ? 1f + 0.25f * Mathf.Clamp01(1f - (anim - 0.3f - i * 0.18f) / 0.15f) : 1f;
                stars[i].rectTransform.localScale = new Vector3(pop, pop, 1f);
            }
            if (anim > 0.3f + 0.18f * 3 + 0.2f) anim = -1f;
        }
    }
}
