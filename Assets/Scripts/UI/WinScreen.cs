using System;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>
    /// Celebration sheet after the reveal, with the solved loop still playing above it: stars slam in over a
    /// sunburst, a ribbon, time/hint/token chips, the pack's star goal and, on a first clear, the album slot it filled.
    /// The buttons wait <see cref="HoldSec"/> so the reveal gets watched: Claim (and the rewarded "Claim x3", only
    /// while an ad is ready), Next (primary), Share clip and Levels. Claimed tokens fly into the counter top left.
    /// The clear's tokens are already in the wallet, so leaving without claiming loses nothing.
    /// </summary>
    public sealed class WinScreen : UIScreen
    {
        static UITheme T => UITheme.Current;

        public const float HoldSec = 1.5f;
        const float CardDp = 352f;
        const float AlbumDp = 50f;

        readonly MonoBehaviour host;
        readonly RectTransform card, ribbon, burst, chipsRow, timeChip, hintChip, extraChip, tokenChip, goalFill;
        readonly RectTransform actions, claimRow, claimedNote, albumRow;
        readonly CanvasGroup actionsGroup;
        readonly Text ribbonTitle, goalText, credit, claimedText, albumLabel;
        readonly Image[] slots, fills;
        readonly Button next, share, levels, claim, claimTriple;
        readonly Pulse triplePulse;
        readonly TokenPill wallet;
        readonly AlbumStrip albumStrip;
        readonly Action<PuzzleResult> onShare;
        PuzzleResult result;
        AlbumFill album;
        float anim = -1f, hold = -1f;
        int starsLanded;
        bool hasClaim, claimed, triplePending;
        int unclaimed, walletShown;

        float OfferDp => UIFactory.ButtonHeight(ButtonSize.Large) / T.dp + 10f;
        static readonly float[] StarTimes = { 0.18f, 0.51f, 0.84f };

        public WinScreen(Transform safeRoot, MonoBehaviour coroutineHost, Action onNext, Action onLevels, Action<PuzzleResult> onShare)
        {
            host = coroutineHost;
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

            // Album: the slot this first clear filled, on its page.
            albumRow = UIFactory.Rect("Album", card);
            albumRow.anchorMin = new Vector2(0f, 0f); albumRow.anchorMax = new Vector2(1f, 0f); albumRow.pivot = new Vector2(0.5f, 0f);
            albumRow.sizeDelta = new Vector2(-2f * inner, T.U(AlbumDp - 6f));
            albumLabel = UIFactory.Text("Label", albumRow, "", TextRole.Label, T.labelS - 1f, T.textDim, TextAnchor.UpperLeft);
            var alrt = albumLabel.rectTransform;
            alrt.anchorMin = new Vector2(0f, 1f); alrt.anchorMax = new Vector2(1f, 1f); alrt.pivot = new Vector2(0.5f, 1f);
            alrt.sizeDelta = new Vector2(0f, T.U(17f)); alrt.anchoredPosition = Vector2.zero;
            float cardInnerW = UILayout.ReferenceWidth - 2f * pad - 2f * inner;
            albumStrip = new AlbumStrip("Strip", albumRow, host, cardInnerW, T.U(25f));
            UIFactory.Place(albumStrip.Root, new Vector2(0.5f, 0f), albumStrip.Root.sizeDelta);

            // Everything tappable sits in one group so the hold can keep it hidden and inert.
            actions = UIFactory.Stretch(UIFactory.Rect("Actions", card));
            actionsGroup = actions.gameObject.AddComponent<CanvasGroup>();

            float heroH = UIFactory.ButtonHeight(ButtonSize.Hero), largeH = UIFactory.ButtonHeight(ButtonSize.Large);
            next = UIFactory.JuicyButton("Next", actions, ButtonTone.Primary, ButtonSize.Hero, "Next puzzle", () => onNext(), UISprites.Next, "Level 2");
            var nrt = (RectTransform)next.transform;
            nrt.anchorMin = new Vector2(0f, 0f); nrt.anchorMax = new Vector2(1f, 0f); nrt.pivot = new Vector2(0.5f, 0f);
            nrt.sizeDelta = new Vector2(-2f * inner, heroH);
            nrt.anchoredPosition = new Vector2(0f, T.U(30f) + largeH + T.U(10f));

            share = UIFactory.JuicyButton("Share", actions, ButtonTone.Blue, ButtonSize.Large, "Share clip", () => this.onShare?.Invoke(result), UISprites.Share);
            levels = UIFactory.JuicyButton("Levels", actions, ButtonTone.Neutral, ButtonSize.Large, "Levels", () => onLevels(), UISprites.Grid);

            claimRow = UIFactory.Rect("ClaimRow", actions);
            claimRow.anchorMin = new Vector2(0f, 0f); claimRow.anchorMax = new Vector2(1f, 0f); claimRow.pivot = new Vector2(0.5f, 0f);
            claimRow.sizeDelta = new Vector2(-2f * inner, largeH);
            claim = UIFactory.JuicyButton("ClaimPlain", claimRow, ButtonTone.Blue, ButtonSize.Large, "Claim", OnClaim, null, "+0");
            AddTokenGlyph(claim);
            claimTriple = UIFactory.JuicyButton("ClaimTriple", claimRow, ButtonTone.Reward, ButtonSize.Large, "Claim x3", OnClaimTriple, null, "+0");
            AddTokenGlyph(claimTriple);
            UIFactory.SetRewardedLook(claimTriple, true);
            triplePulse = claimTriple.GetComponent<JuicyButtonParts>().face.gameObject.AddComponent<Pulse>();
            claimTriple.gameObject.SetActive(false);

            var note = UIFactory.Sunk("Claimed", claimRow, 16f);
            claimedNote = note.rectTransform;
            UIFactory.Stretch(claimedNote, 0, 0, T.U(6f), T.U(6f));
            var check = UIFactory.Icon("Check", note.transform, UISprites.Check, T.green, 22f);
            UIFactory.Place(check.rectTransform, new Vector2(0f, 0.5f), check.rectTransform.sizeDelta, new Vector2(T.U(14f), 0f));
            claimedText = UIFactory.Text("Text", note.transform, "", TextRole.Label, T.label, Color.white);
            UIFactory.Stretch(claimedText.rectTransform, T.U(40f), T.U(14f), 0, 0);
            claimedNote.gameObject.SetActive(false);
            GameServices.RewardedOffersChanged += RefreshClaimOffer;

            credit = UIFactory.Text("Credit", card, "", TextRole.Body, T.bodyS - 1f, T.textMute);
            UIFactory.Place(credit.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(340f), T.U(20f)), new Vector2(0f, T.U(7f)));

            // The wallet the claimed tokens fly into. Only on this card, never over a puzzle.
            wallet = new TokenPill("Wallet", Root, 36f);
            UIFactory.Place(wallet.Root, new Vector2(0f, 1f), wallet.Root.sizeDelta, new Vector2(T.U(T.screenPad + 10f), -T.U(T.screenPad)));
            Hide();
        }

        static void AddTokenGlyph(Button b)
        {
            var parts = b.GetComponent<JuicyButtonParts>();
            var icon = UIFactory.TokenIcon("Token", parts.content, T.U(28f));
            icon.SetAsFirstSibling();
            var le = icon.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.preferredHeight = T.U(28f);
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

        /// <summary>
        /// Shows the result; <paramref name="nextLevel"/> is the next puzzle in the same pack, or null at its end.
        /// <paramref name="albumFill"/> is what this clear did to the album (its page bonus is already in the wallet).
        /// </summary>
        public void ShowResult(PuzzleResult r, LevelInfo nextLevel, AlbumFill albumFill)
        {
            result = r;
            album = albumFill;
            claimed = triplePending = false;
            unclaimed = Mathf.Max(0, r.TokensEarned) + Mathf.Max(0, albumFill.Bonus);
            hasClaim = unclaimed > 0;
            walletShown = Mathf.Max(0, Tokens.Balance - unclaimed);
            Show();
            wallet.SetValue(walletShown, false);
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
            credit.text = string.IsNullOrEmpty(r.Level.credit) ? "" : "Video: " + r.Level.credit;

            albumRow.gameObject.SetActive(album.Filled);
            if (album.Filled)
            {
                albumStrip.Bind(album.Page, album.Slot);
                albumLabel.text = album.PageCompleted
                    ? "Album page " + (album.Page + 1) + " complete!  +" + album.Bonus + " tokens"
                    : "Album page " + (album.Page + 1) + " · " + (album.Slot + 1) + " of " + AlbumRules.SlotsPerPage +
                      " · +" + AlbumRules.PageBonus + " when full";
                albumLabel.color = album.PageCompleted ? T.gold : T.textDim;
            }

            var sp = GameServices.Share;
            bool canShare = sp != null && sp.IsAvailable;
            share.gameObject.SetActive(canShare);
            if (canShare) UIFactory.SetButtonText(share, sp.ButtonLabel);
            LayoutSecondary(canShare);
            claimedNote.gameObject.SetActive(false);
            claim.gameObject.SetActive(true);
            claim.interactable = true;
            RefreshClaimOffer();

            UIFactory.SetStars(slots, 0);
            for (int i = 0; i < fills.Length; i++)
            {
                var rt = (RectTransform)fills[i].transform.parent;
                rt.gameObject.SetActive(false);
            }
            starsLanded = 0;
            anim = 0f;
            hold = 0f;
            SetActionsLive(false);

            UITween.Get(card).Move(new Vector2(0f, -T.U(CardDp + 40f)), Vector2.zero).Play(T.medium, EaseType.OutCubic);
            UITween.Get(burst).Fade(0f, 1f).Scale(0.6f, 1f).Play(T.medium, EaseType.OutBack, 0.1f);
            UITween.Get(burst.Find("Rays")).Rotate(0f, 120f).Play(9f, EaseType.OutCubic);
            UITween.Get(ribbon).Scale(0.4f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, 0.26f);
            UITween.Get(wallet.Root).Fade(0f, 1f).Move(new Vector2(0f, T.U(20f)), Vector2.zero).Play(T.medium, EaseType.OutCubic, 0.3f);
            if (album.Filled) UITween.Get(albumRow).Fade(0f, 1f).Play(T.medium, EaseType.OutCubic, 0.5f);
            if (UIFx.Instance != null) UIFx.Instance.Confetti();
        }

        void SetActionsLive(bool live)
        {
            actionsGroup.alpha = live ? 1f : 0f;
            actionsGroup.interactable = live;
            actionsGroup.blocksRaycasts = live;
        }

        /// <summary>End of the hold: the buttons pop in, Claim first.</summary>
        void RevealActions()
        {
            SetActionsLive(true);
            float d = 0f;
            if (claimRow.gameObject.activeSelf)
            {
                UITween.Get(claimRow).Scale(0.8f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, d);
                if (claimTriple.gameObject.activeSelf) triplePulse.Restart();
                d += 0.08f;
            }
            UITween.Get(next).Scale(0.8f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, d);
            UITween.Get(levels).Fade(0f, 1f).Play(T.medium, EaseType.OutCubic, d + 0.08f);
            if (share.gameObject.activeSelf) UITween.Get(share).Fade(0f, 1f).Play(T.medium, EaseType.OutCubic, d + 0.08f);
        }

        /// <summary>
        /// Claim (always, while there is something to claim) and the green "Claim x3" only while
        /// <see cref="GameServices.ClaimTriple"/> says an ad is ready (caps and spacing are the ad policy's call).
        /// </summary>
        void RefreshClaimOffer()
        {
            if (!IsVisible) return;
            bool showRow = hasClaim;
            claimRow.gameObject.SetActive(showRow);
            if (showRow && !claimed)
            {
                var offer = GameServices.ClaimTriple;
                bool triple = triplePending || (offer != null && offer.IsOffered(result.TokensEarned, false));
                bool wasShown = claimTriple.gameObject.activeSelf;
                claimTriple.gameObject.SetActive(triple);
                claimTriple.interactable = !triplePending;
                claim.interactable = !triplePending;
                UIFactory.SetButtonText(claim, "Claim", "+" + unclaimed);
                UIFactory.SetButtonText(claimTriple, "Claim x" + TokenRules.ClaimMultiplier, "+" + (unclaimed + TokenRules.TripleBonus(result.TokensEarned)));
                LayoutClaimButtons(triple);
                if (triple && !wasShown && hold < 0f) triplePulse.Restart();
            }
            LayoutCard(showRow, album.Filled);
        }

        void LayoutClaimButtons(bool withTriple)
        {
            float gap = T.U(10f);
            var prt = (RectTransform)claim.transform;
            prt.pivot = new Vector2(0f, 0f);
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(withTriple ? 0.4f : 1f, 1f);
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = new Vector2(withTriple ? -gap * 0.5f : 0f, 0f);
            var trt = (RectTransform)claimTriple.transform;
            trt.pivot = new Vector2(0f, 0f);
            trt.anchorMin = new Vector2(0.4f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(gap * 0.5f, 0f);
            trt.offsetMax = Vector2.zero;
        }

        /// <summary>Bottom-up: secondary row, Next, then the claim row and the album row when shown; the card grows to fit.</summary>
        void LayoutCard(bool withClaim, bool withAlbum)
        {
            float pad = T.U(12f), largeH = UIFactory.ButtonHeight(ButtonSize.Large), heroH = UIFactory.ButtonHeight(ButtonSize.Hero);
            float y = T.U(30f) + largeH + T.U(10f) + heroH + T.U(10f);
            if (withClaim)
            {
                claimRow.anchoredPosition = new Vector2(0f, y);
                y += largeH + T.U(10f);
            }
            if (withAlbum) albumRow.anchoredPosition = new Vector2(0f, y);
            float cardH = T.U(CardDp + (withClaim ? OfferDp : 0f) + (withAlbum ? AlbumDp : 0f));
            card.sizeDelta = new Vector2(card.sizeDelta.x, cardH);
            burst.anchoredPosition = new Vector2(0f, pad + cardH - T.U(85f));
        }

        void OnClaim()
        {
            if (claimed || triplePending || unclaimed <= 0) return;
            Collect(unclaimed, claim);
        }

        void OnClaimTriple()
        {
            var offer = GameServices.ClaimTriple;
            if (offer == null || claimed || triplePending) return;
            if (!offer.IsOffered(result.TokensEarned, false)) { RefreshClaimOffer(); return; }
            triplePending = true;
            RefreshClaimOffer();
            var forResult = result.Level;
            offer.Request(earned =>
            {
                triplePending = false;
                if (!IsVisible || result.Level != forResult || claimed) return;
                if (earned)
                {
                    int bonus = TokenRules.TripleBonus(result.TokensEarned);
                    Tokens.Add(bonus);
                    UIFactory.ChipText(tokenChip).text = "+" + (result.TokensEarned + bonus);
                    UITween.Get(tokenChip).Scale(1.4f, 1f).Play(0.45f, EaseType.OutElastic);
                    Collect(unclaimed + bonus, claimTriple);
                    if (UIFx.Instance != null) UIFx.Instance.Confetti(40);
                    return;
                }
                RefreshClaimOffer();
            });
        }

        /// <summary>Marks the win's tokens claimed and flies <paramref name="amount"/> of them from the button into the counter.</summary>
        void Collect(int amount, Button from)
        {
            claimed = true;
            unclaimed = 0;
            Haptics.Success();
            Vector2 fromPos = ScreenCenter((RectTransform)from.transform);
            Vector2 toPos = ScreenCenter(wallet.Icon);
            claim.gameObject.SetActive(false);
            claimTriple.gameObject.SetActive(false);
            claimedNote.gameObject.SetActive(true);
            claimedText.text = "+" + amount + " tokens claimed";
            UITween.Get(claimedNote).Scale(0.85f, 1f).Play(0.3f, EaseType.OutBack);

            int start = walletShown, target = Tokens.Balance;
            if (UIFx.Instance == null)
            {
                walletShown = target;
                wallet.SetValue(target, true);
                return;
            }
            int count = Mathf.Clamp(amount / 2, 5, 14), landed = 0;
            UIFx.Instance.FlyTokens(fromPos, toPos, count, () =>
            {
                landed++;
                walletShown = start + (target - start) * landed / count;
                wallet.SetValue(walletShown, true);
                wallet.Bump();
                Haptics.Tick();
            }, () =>
            {
                walletShown = Tokens.Balance;
                wallet.SetValue(walletShown, true);
            });
        }

        static Vector2 ScreenCenter(RectTransform rt) =>
            RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

        public override void Hide()
        {
            base.Hide();
            anim = -1f;
            hold = -1f;
        }

        public void Tick(float dt)
        {
            if (!IsVisible) return;
            if (hold >= 0f)
            {
                hold += dt;
                if (hold >= HoldSec)
                {
                    hold = -1f;
                    RevealActions();
                }
            }
            if (anim < 0f) return;
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
            if (starsLanded >= StarTimes.Length)
            {
                anim = -1f;
                if (album.PageCompleted && UIFx.Instance != null) UIFx.Instance.Confetti(40);
            }
        }
    }
}
