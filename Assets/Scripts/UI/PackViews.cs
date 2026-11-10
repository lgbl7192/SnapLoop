using System;
using System.Collections;
using System.Collections.Generic;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>Level thumbnails (StreamingAssets/Levels/&lt;id&gt;.jpg), loaded once and shared by every screen. A missing file caches null.</summary>
    public static class Thumbnails
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static bool TryGet(string levelId, out Texture2D tex) => cache.TryGetValue(levelId, out tex);

        public static IEnumerator Load(string levelId, Action<Texture2D> onDone)
        {
            if (cache.TryGetValue(levelId, out var cached))
            {
                onDone?.Invoke(cached);
                yield break;
            }
            using (var req = UnityWebRequestTexture.GetTexture(LevelCatalog.GetLevelFileUrl(levelId + ".jpg"), true))
            {
                yield return req.SendWebRequest();
                Texture2D tex = null;
                if (req.result == UnityWebRequest.Result.Success) tex = DownloadHandlerTexture.GetContent(req);
                cache[levelId] = tex;
                onDone?.Invoke(tex);
            }
        }

        /// <summary>UV rect that crops <paramref name="tex"/> to fill <paramref name="area"/> without stretching.</summary>
        public static Rect CoverRect(Rect area, Texture tex)
        {
            if (area.width <= 0 || area.height <= 0 || tex == null) return new Rect(0, 0, 1, 1);
            float areaAspect = area.width / area.height, texAspect = tex.width / (float)tex.height;
            return texAspect > areaAspect
                ? new Rect((1f - areaAspect / texAspect) * 0.5f, 0f, areaAspect / texAspect, 1f)
                : new Rect(0f, (1f - texAspect / areaAspect) * 0.5f, 1f, texAspect / areaAspect);
        }
    }

    /// <summary>Counts a number label up (or down) to its target over a short time, like the wallet in the style guide.</summary>
    public sealed class CountUp : MonoBehaviour
    {
        public Text label;
        public string suffix = "";
        float shown, from, target, t = 1f;
        const float Duration = 0.7f;

        public void Set(int value, bool animate)
        {
            if (!animate || !isActiveAndEnabled)
            {
                shown = target = value;
                t = 1f;
                label.text = value + suffix;
                return;
            }
            from = shown;
            target = value;
            t = 0f;
        }

        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / Duration);
            shown = Mathf.Lerp(from, target, Ease.Apply(EaseType.OutCubic, t));
            label.text = Mathf.RoundToInt(shown) + suffix;
        }
    }

    /// <summary>The token wallet: a dark pill with the token glyph overhanging its left edge. Level select and the pack screen only.</summary>
    public sealed class TokenPill
    {
        public RectTransform Root { get; }
        readonly CountUp counter;
        readonly RectTransform icon;
        int value = -1;

        public TokenPill(string name, Transform parent, float heightDp = 36f)
        {
            float h = heightDp;
            var bg = UIFactory.RoundRect(name, parent, new Color(T.surfaceSunk.r, T.surfaceSunk.g, T.surfaceSunk.b, 0.85f), h * 0.5f);
            var hairline = UIFactory.Stroke("Hairline", bg.transform, T.strokeSoft, h * 0.5f);
            hairline.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Root = bg.rectTransform;
            Root.sizeDelta = new Vector2(T.U(90f), T.U(h));
            var row = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset((int)T.U(h * 0.95f), (int)T.U(14f), 0, 0);
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            if (parent.GetComponent<LayoutGroup>() == null)
                bg.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            icon = UIFactory.TokenIcon("Token", Root, T.U(h * 1.15f));
            icon.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIFactory.Place(icon, new Vector2(0f, 0.5f), icon.sizeDelta, new Vector2(-T.U(h * 0.22f), 0f));

            var text = UIFactory.Text("Text", Root, "0", TextRole.Number, h * 0.5f, Color.white);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            counter = text.gameObject.AddComponent<CountUp>();
            counter.label = text;
        }

        static UITheme T => UITheme.Current;

        /// <summary>The token glyph, e.g. as the target of a token flight.</summary>
        public RectTransform Icon => icon;

        public void Refresh(bool animate) => SetValue(Tokens.Balance, animate);

        /// <summary>Shows <paramref name="amount"/> instead of the live balance (the win card counts claimed tokens in).</summary>
        public void SetValue(int amount, bool animate)
        {
            if (amount == value) return;
            bool grew = value >= 0 && amount > value;
            counter.Set(amount, animate && value >= 0);
            value = amount;
            if (grew && animate) Bump();
        }

        public void Bump() => UITween.Get(icon).Scale(1.35f, 1f).Play(0.4f, EaseType.OutElastic);
    }

    /// <summary>One toast line above everything, shared by the hub, the pack screen and the preview.</summary>
    public sealed class ToastView
    {
        readonly RectTransform root;
        readonly CanvasGroup group;
        readonly Text text;
        float time;

        static UITheme T => UITheme.Current;

        public ToastView(Transform canvasRoot)
        {
            var safe = UIFactory.Stretch(UIFactory.Rect("ToastSafe", canvasRoot));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            root = safe;
            var bg = UIFactory.RoundRect("Toast", safe, new Color(T.ink.r, T.ink.g, T.ink.b, 0.94f), 18f);
            UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(352f), T.U(58f)),
                new Vector2(0f, UIFactory.ButtonHeight(ButtonSize.Hero) + T.U(T.screenPad + 24f)));
            UIFactory.Stroke("Hairline", bg.transform, T.stroke, 18f);
            group = bg.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            text = UIFactory.Text("Text", bg.transform, "", TextRole.Label, T.labelS, Color.white);
            UIFactory.Stretch(text.rectTransform, T.U(16f), T.U(16f), T.U(4f), T.U(4f));
        }

        public void Show(string message, float seconds = 3f)
        {
            root.SetAsLastSibling();
            text.text = message;
            time = seconds;
            group.alpha = 1f;
            UITween.Get(group).Scale(0.8f, 1f).Play(T.quick, EaseType.OutBack);
        }

        public void Tick(float dt)
        {
            if (time <= 0f) return;
            time -= dt;
            group.alpha = Mathf.Clamp01(time / 0.3f);
        }
    }

    /// <summary>Unlock-with-tokens and buy flows shared by every pack view.</summary>
    public static class PackActions
    {
        /// <summary>Set by the app; shows a one-line toast.</summary>
        public static Action<string> Toast;

        public static void UnlockWithTokens(PackInfo pack)
        {
            if (pack == null || PackAccess.IsUnlocked(pack)) return;
            if (!PackAccess.CanAfford(pack))
            {
                int missing = pack.tokenCost - Tokens.Balance;
                Toast?.Invoke("Clear puzzles to earn " + missing + " more tokens. 1, 2 or 3 stars pay " +
                              TokenRules.OneStar + ", " + TokenRules.TwoStars + " or " + TokenRules.ThreeStars + ".");
                return;
            }
            if (!PackAccess.TryUnlockWithTokens(pack)) return;
            Celebrate(pack, toast: true);
        }

        public static void Buy(PackInfo pack)
        {
            var store = GameServices.PackStore;
            if (pack == null || !pack.CanBuy || store == null || PackAccess.IsUnlocked(pack)) return;
            store.Buy(pack.productId, pack.DisplayTitle, owned =>
            {
                // The store already thanks the player; no second toast.
                if (owned) Celebrate(pack, toast: false);
            });
        }

        public static bool CanBuy(PackInfo pack) => pack != null && pack.CanBuy && GameServices.PackStore != null;

        public static string Price(PackInfo pack) => GameServices.PackStore?.PriceLabel(pack.productId) ?? "";

        static void Celebrate(PackInfo pack, bool toast)
        {
            Haptics.Success();
            if (UIFx.Instance != null) UIFx.Instance.Confetti(50);
            int n = pack.Levels.Count;
            if (toast) Toast?.Invoke(pack.DisplayTitle + " unlocked! " + n + (n == 1 ? " new puzzle." : " new puzzles."));
        }
    }

    /// <summary>
    /// A pack as a card: cover (thumbnail, or a drawn stub while the pack has no clips), title, clip count,
    /// "Seasonal" badge, and either progress plus Play (unlocked) or "Unlock for N tokens" plus "Buy $1.99" (locked).
    /// With too few tokens the token button shows progress instead of hiding.
    /// </summary>
    public sealed class PackCard
    {
        public const float HeightDp = 200f;

        static UITheme T => UITheme.Current;

        public RectTransform Root { get; }
        public PackInfo Pack { get; private set; }

        readonly MonoBehaviour host;
        readonly Action<PackInfo> onOpen;
        readonly RawImage cover;
        readonly Image coverStub, coverStubIcon, glow;
        readonly Text stubLabel, caps, title, progressText, note;
        readonly RectTransform seasonal, progressBar, progressFill, buttons;
        readonly Button play, unlock, buy;
        readonly RectTransform unlockFill;

        public PackCard(string name, Transform parent, MonoBehaviour coroutineHost, Action<PackInfo> onOpen, bool showPlay = true)
        {
            host = coroutineHost;
            this.onOpen = onOpen;
            Root = UIFactory.Rect(name, parent);
            Root.sizeDelta = new Vector2(0f, T.U(HeightDp));

            var glowRt = UIFactory.Rect("Glow", Root);
            UIFactory.Stretch(glowRt);
            glow = UIFactory.SoftShadow("Glow", glowRt, 18f, 0f, 0.6f);

            var card = UIFactory.Card("Card", Root, T.radiusLg);
            UIFactory.Stretch(card);
            var btn = card.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = card.Find("Face").GetComponent<Image>();
            btn.onClick.AddListener(Sfx.PlayClick);
            btn.onClick.AddListener(() => { if (Pack != null) this.onOpen?.Invoke(Pack); });
            card.gameObject.AddComponent<JuicyPress>().Bind(null, 0f, 0.02f);

            float pad = T.U(12f), coverW = T.U(100f);
            var mask = UIFactory.RoundRect("CoverMask", card, Color.white, 16f);
            var mrt = mask.rectTransform;
            mrt.anchorMin = new Vector2(0f, 0f); mrt.anchorMax = new Vector2(0f, 1f); mrt.pivot = new Vector2(0f, 0.5f);
            mrt.offsetMin = new Vector2(pad, pad); mrt.offsetMax = new Vector2(pad + coverW, -pad);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            coverStub = UIFactory.Image("Stub", mask.transform, Color.white);
            UIFactory.Stretch(coverStub.rectTransform);
            coverStubIcon = UIFactory.Image("StubIcon", mask.transform, new Color(1f, 1f, 1f, 0.9f), UISprites.TokenPiece);
            UIFactory.Place(coverStubIcon.rectTransform, new Vector2(0.5f, 0.56f), new Vector2(T.U(58f), T.U(58f)));
            UIFactory.Ink(coverStubIcon, 1.5f);
            stubLabel = UIFactory.Text("StubLabel", mask.transform, "Coming soon", TextRole.Caps, 10f, Color.white);
            UIFactory.Place(stubLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(coverW, T.U(28f)), new Vector2(0f, T.U(14f)));
            cover = UIFactory.Stretch(UIFactory.Rect("Cover", mask.transform)).gameObject.AddComponent<RawImage>();
            cover.raycastTarget = false;
            UIFactory.Stroke("Hairline", mask.transform, T.stroke, 16f);

            seasonal = UIFactory.RoundRect("Seasonal", card, Color.white, 11f).rectTransform;
            UIFactory.Gradient(seasonal.GetComponent<Image>(), T.gold, T.orange);
            UIFactory.Ink(seasonal.GetComponent<Image>(), 1.2f);
            UIFactory.Place(seasonal, new Vector2(1f, 1f), new Vector2(T.U(86f), T.U(22f)), new Vector2(-T.U(10f), T.U(9f)));
            var seasonalText = UIFactory.Text("Text", seasonal, "Seasonal", TextRole.Caps, 10f, T.ink);
            UIFactory.Stretch(seasonalText.rectTransform);

            float textX = pad + coverW + T.U(14f), right = T.U(14f);
            caps = UIFactory.Text("Caps", card, "", TextRole.Caps, T.caps, T.textDim, TextAnchor.UpperLeft);
            TopRow(caps.rectTransform, textX, right + T.U(80f), 16f, 18f);
            title = UIFactory.Text("Title", card, "", TextRole.Title, 24f, Color.white, TextAnchor.UpperLeft);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            TopRow(title.rectTransform, textX, right, 36f, 34f);

            buttons = UIFactory.Rect("Buttons", card);
            buttons.anchorMin = Vector2.zero; buttons.anchorMax = new Vector2(1f, 0f); buttons.pivot = new Vector2(0.5f, 0f);
            float mh = UIFactory.ButtonHeight(ButtonSize.Medium), gap = T.U(8f);
            buttons.offsetMin = new Vector2(textX, pad); buttons.offsetMax = new Vector2(-right, pad + 2f * mh + gap);

            unlock = UIFactory.JuicyButton("Unlock", buttons, ButtonTone.Pink, ButtonSize.Medium, "Unlock for 150", () => PackActions.UnlockWithTokens(Pack));
            var parts = unlock.GetComponent<JuicyButtonParts>();
            var fill = UIFactory.RoundRect("Progress", parts.face.transform, Color.white, 14f);
            unlockFill = fill.rectTransform;
            unlockFill.anchorMin = Vector2.zero; unlockFill.anchorMax = new Vector2(0.5f, 1f);
            unlockFill.offsetMin = new Vector2(T.U(3f), T.U(3f)); unlockFill.offsetMax = new Vector2(-T.U(3f), -T.U(3f));
            UIFactory.Gradient(fill, new Color(T.gold.r, T.gold.g, T.gold.b, 0.5f), new Color(T.orange.r, T.orange.g, T.orange.b, 0.4f), GradientDirection.Horizontal);
            unlockFill.SetAsFirstSibling();
            var tokenIcon = UIFactory.TokenIcon("Token", parts.content, T.U(26f));
            tokenIcon.SetAsFirstSibling();
            var le = tokenIcon.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.preferredHeight = T.U(26f);
            Stack(unlock, 1, mh, gap);

            buy = UIFactory.JuicyButton("Buy", buttons, ButtonTone.Blue, ButtonSize.Medium, "Buy $1.99", () => PackActions.Buy(Pack));
            Stack(buy, 0, mh, gap);

            play = UIFactory.JuicyButton("Play", buttons, ButtonTone.Blue, ButtonSize.Medium, "Play", () => { if (Pack != null) this.onOpen?.Invoke(Pack); }, UISprites.Play);
            Stack(play, 0, mh, gap);
            play.gameObject.SetActive(showPlay);
            showPlayButton = showPlay;

            var bar = UIFactory.RoundRect("ProgressBar", card, T.surfaceSunk, 7f);
            progressBar = bar.rectTransform;
            progressBar.anchorMin = Vector2.zero; progressBar.anchorMax = new Vector2(1f, 0f); progressBar.pivot = new Vector2(0.5f, 0f);
            progressBar.offsetMin = new Vector2(textX, pad + mh + gap + T.U(10f)); progressBar.offsetMax = new Vector2(-right, pad + mh + gap + T.U(24f));
            UIFactory.Stroke("Ink", bar.transform, T.InkColor, 7f);
            var pf = UIFactory.RoundRect("Fill", bar.transform, Color.white, 7f);
            progressFill = pf.rectTransform;
            progressFill.anchorMin = Vector2.zero; progressFill.anchorMax = new Vector2(0f, 1f);
            progressFill.offsetMin = progressFill.offsetMax = Vector2.zero;
            UIFactory.Gradient(pf, T.gold, T.orange, GradientDirection.Horizontal);
            progressText = UIFactory.Text("ProgressText", card, "", TextRole.Label, T.labelS, T.textDim, TextAnchor.LowerLeft);
            var prt = progressText.rectTransform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = new Vector2(1f, 0f); prt.pivot = new Vector2(0.5f, 0f);
            prt.offsetMin = new Vector2(textX, pad + mh + gap + T.U(28f)); prt.offsetMax = new Vector2(-right, pad + mh + gap + T.U(48f));

            note = UIFactory.Text("Note", card, "", TextRole.Body, T.bodyS, T.textDim, TextAnchor.UpperLeft);
            var nrt = note.rectTransform;
            nrt.anchorMin = Vector2.zero; nrt.anchorMax = Vector2.one;
            nrt.offsetMin = new Vector2(textX, pad); nrt.offsetMax = new Vector2(-right, -T.U(76f));

            GameServices.PacksChanged += Refresh;
            Tokens.Changed += Refresh;
        }

        readonly bool showPlayButton;
        string coverFor;

        static void TopRow(RectTransform rt, float left, float right, float topDp, float heightDp)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, -T.U(topDp + heightDp)); rt.offsetMax = new Vector2(-right, -T.U(topDp));
        }

        /// <summary>Row 0 is the bottom button, row 1 the one above it.</summary>
        static void Stack(Button b, int row, float h, float gap)
        {
            var rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, h);
            rt.anchoredPosition = new Vector2(0f, row * (h + gap));
        }

        public void Bind(PackInfo pack)
        {
            Pack = pack;
            Refresh();
            LoadCover();
        }

        public void Refresh()
        {
            if (Pack == null || Root == null) return;
            var p = Pack;
            Color accent = T.Pack(p.id);
            bool unlocked = PackAccess.IsUnlocked(p), empty = p.IsEmpty, featured = PackAccess.IsFeatured(p);
            int n = p.Levels.Count;

            title.text = p.DisplayTitle;
            caps.text = UIFactory.Track(empty ? "Coming soon" : n + (n == 1 ? " puzzle" : " puzzles") + (unlocked && !p.IsFree ? " · unlocked" : ""));
            caps.color = empty ? T.textMute : accent;
            seasonal.gameObject.SetActive(featured);
            glow.color = new Color(accent.r, accent.g, accent.b, featured ? 0.55f : 0f);
            UIFactory.Gradient(coverStub, Color.Lerp(accent, Color.white, 0.1f), Color.Lerp(accent, T.ink, 0.6f));
            stubLabel.gameObject.SetActive(empty);

            bool showProgress = unlocked && !empty;
            progressBar.gameObject.SetActive(showProgress);
            progressText.gameObject.SetActive(showProgress);
            if (showProgress)
            {
                int cleared = PackAccess.ClearedIn(p);
                int stars = PackAccess.StarsIn(p, out int max);
                progressFill.anchorMax = new Vector2(n > 0 ? cleared / (float)n : 0f, 1f);
                progressText.text = cleared + " of " + n + " cleared · " + stars + "/" + max + " stars";
            }
            play.gameObject.SetActive(showPlayButton && unlocked && !empty);

            bool locked = !unlocked && !empty;
            bool tokens = locked && p.CanUnlockWithTokens;
            bool canBuy = locked && PackActions.CanBuy(p);
            unlock.gameObject.SetActive(tokens);
            buy.gameObject.SetActive(canBuy);
            if (tokens)
            {
                bool afford = PackAccess.CanAfford(p);
                UIFactory.SetTone(unlock, afford ? ButtonTone.Pink : ButtonTone.Neutral);
                unlockFill.gameObject.SetActive(!afford);
                unlockFill.anchorMax = new Vector2(TokenRules.UnlockProgress(Tokens.Balance, p.tokenCost), 1f);
                UIFactory.SetButtonText(unlock, afford ? "Unlock for " + p.tokenCost : Tokens.Balance + " / " + p.tokenCost);
            }
            if (canBuy) UIFactory.SetButtonText(buy, "Buy " + PackActions.Price(p));
            // With one way to unlock, its button takes the bottom slot.
            if (tokens) Stack(unlock, canBuy ? 1 : 0, UIFactory.ButtonHeight(ButtonSize.Medium), T.U(8f));

            note.gameObject.SetActive(empty || (locked && !tokens && !canBuy));
            note.text = empty ? "New clips for this pack are on the way." : "This pack isn't available yet.";
        }

        void LoadCover()
        {
            var level = Pack?.CoverLevel;
            cover.gameObject.SetActive(false);
            coverFor = level?.id;
            if (level == null) return;
            if (Thumbnails.TryGet(level.id, out var tex)) ApplyCover(level.id, tex);
            else if (host != null && host.isActiveAndEnabled) host.StartCoroutine(Thumbnails.Load(level.id, t => ApplyCover(level.id, t)));
        }

        void ApplyCover(string levelId, Texture2D tex)
        {
            if (tex == null || levelId != coverFor) return;
            cover.texture = tex;
            cover.gameObject.SetActive(true);
            cover.uvRect = Thumbnails.CoverRect(cover.rectTransform.rect, tex);
        }
    }
}
