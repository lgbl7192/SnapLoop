using System;
using System.Collections;
using System.Collections.Generic;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>
    /// The hub: token wallet and stars up top, the brand, an "Up next" card, the selected theme pack's puzzles as
    /// thumbnail tiles (with a Packs button to switch), and one big Play button in the thumb zone that names the next puzzle.
    /// </summary>
    public sealed class LevelSelectScreen : UIScreen
    {
        sealed class Tile
        {
            public LevelInfo Level;
            public Button Button;
            public CanvasGroup Group;
            public RawImage Thumb;
            public Image Dim, Lock, Check, NumberDisc;
            public Image[] Stars;
            public GameObject NextFx, Flame;
            public Pulse PlayPulse;
        }

        static UITheme T => UITheme.Current;

        const string SelectedPackKey = "packs.selected";

        readonly MonoBehaviour host;
        readonly Action<int> onPlay;
        readonly Action<LevelInfo> onPreview;
        readonly RectTransform header, viewport, content, gridRoot, upNext, playDock, section;
        readonly ScrollRect scroll;
        readonly Text message;
        readonly List<Tile> tiles = new List<Tile>();

        readonly RectTransform starChip;
        readonly TokenPill tokenPill;
        readonly Text sectionTitle, sectionCaps;
        readonly Button packsButton;
        readonly Image packsDot;
        readonly RectTransform packCardRow;
        readonly PackCard packCard;
        readonly Button playButton;
        readonly Pulse playPulse;
        readonly Brand.LoopMark mark;
        readonly RectTransform brand;

        readonly RawImage upThumb;
        readonly KenBurns upBurns;
        readonly Text upCaps, upTitle;
        readonly Image[] upStars;
        readonly RectTransform upPiecesChip;
        readonly Image upAccent;

        PackInfo builtFor;
        int nextIndex = -1;
        int devTaps;
        float devTapTime;

        public event Action DevMenuRequested;

        /// <summary>Empty container between the brand and "Up next", kept for future retention features (daily rewards, streaks).</summary>
        public RectTransform RetentionSlot { get; }

        const int Columns = 3;
        float Pad => T.U(T.screenPad);
        float TileW => (UILayout.ReferenceWidth - 2f * Pad - (Columns - 1) * T.U(T.gap)) / Columns;
        float TileH => T.U(178f);

        public LevelSelectScreen(Transform safeRoot, MonoBehaviour coroutineHost, Action<int> onPlay, Action onSettings,
            Action onOpenPacks, Action<LevelInfo> onPreview)
        {
            host = coroutineHost;
            this.onPlay = onPlay;
            this.onPreview = onPreview;
            Root = UIFactory.Stretch(UIFactory.Rect("LevelSelect", safeRoot));

            // ---------------------------------------------------------------- header (fixed)
            float headerH = T.U(68f);
            header = UIFactory.Band(UIFactory.Rect("Header", Root), 1f, headerH);

            var chips = UIFactory.Rect("Chips", header);
            chips.anchorMin = new Vector2(0f, 0f);
            chips.anchorMax = new Vector2(0.7f, 1f);
            chips.offsetMin = new Vector2(Pad + T.U(8f), 0f);
            chips.offsetMax = Vector2.zero;
            var chipRow = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipRow.childAlignment = TextAnchor.MiddleLeft;
            chipRow.spacing = T.U(8f);
            chipRow.childControlWidth = true;
            chipRow.childControlHeight = false;
            chipRow.childForceExpandWidth = chipRow.childForceExpandHeight = false;
            tokenPill = new TokenPill("TokenPill", chips, 36f);
            starChip = UIFactory.Chip("StarsChip", chips, "0", UISprites.Star, T.star, 36f);

            var gear = UIFactory.RoundButton("Settings", header, UISprites.Gear, () => onSettings());
            UIFactory.Place((RectTransform)gear.transform, new Vector2(1f, 0.5f), ((RectTransform)gear.transform).sizeDelta, new Vector2(-Pad, 0f));

            if (Debug.isDebugBuild)
            {
                var dev = UIFactory.JuicyButton("Dev", header, ButtonTone.Neutral, ButtonSize.Medium, "DEV", () => DevMenuRequested?.Invoke());
                UIFactory.ButtonLabel(dev).fontSize = T.F(15f);
                UIFactory.Place((RectTransform)dev.transform, new Vector2(1f, 0.5f), new Vector2(T.U(64f), T.U(T.minTouch)),
                    new Vector2(-Pad - T.U(T.roundButton + 10f), 0f));
            }

            // ---------------------------------------------------------------- scroll body
            var viewportImage = UIFactory.Image("Viewport", Root, new Color(0, 0, 0, 0), null, raycast: true);
            viewport = UIFactory.Stretch(viewportImage.rectTransform, 0, 0, headerH, 0);
            viewportImage.gameObject.AddComponent<RectMask2D>();
            scroll = viewportImage.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;
            scroll.decelerationRate = 0.12f;

            content = UIFactory.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var col = content.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(0, 0, (int)T.U(4f), (int)(UIFactory.ButtonHeight(ButtonSize.Hero) + T.U(44f)));
            col.spacing = T.U(T.sectionGap);
            col.childControlWidth = col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            col.childAlignment = TextAnchor.UpperCenter;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;

            // Brand: loop mark, wordmark (5 quick taps open the hidden dev menu, also in release builds for recording clips), tagline.
            brand = UIFactory.Rect("Brand", content);
            brand.gameObject.AddComponent<LayoutElement>().preferredHeight = T.U(150f);
            mark = Brand.Mark(brand, 58f);
            UIFactory.Place(mark.Root, new Vector2(0.5f, 1f), mark.Root.sizeDelta, new Vector2(0f, -T.U(2f)));
            var word = Brand.Wordmark(brand, 50f);
            UIFactory.Place(word.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(300f), T.U(64f)), new Vector2(0f, -T.U(58f)));
            word.raycastTarget = true;
            var wordBtn = word.gameObject.AddComponent<Button>();
            wordBtn.transition = Selectable.Transition.None;
            wordBtn.onClick.AddListener(OnWordmarkTap);
            var tagline = UIFactory.Text("Tagline", brand, "Assemble the moving picture", TextRole.Caps, T.caps, T.textDim);
            UIFactory.Place(tagline.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(360f), T.U(20f)), new Vector2(0f, -T.U(126f)));

            RetentionSlot = UIFactory.Rect("RetentionSlot", content);
            RetentionSlot.gameObject.AddComponent<LayoutElement>().preferredHeight = 0f;

            // Up next.
            var upHolder = UIFactory.Rect("UpNextRow", content);
            upHolder.gameObject.AddComponent<LayoutElement>().preferredHeight = T.U(150f);
            upNext = UIFactory.Card("UpNext", upHolder, T.radiusLg);
            UIFactory.Stretch(upNext, Pad, Pad, 0, 0);
            var upBtn = upNext.gameObject.AddComponent<Button>();
            upBtn.transition = Selectable.Transition.None;
            upBtn.targetGraphic = upNext.Find("Face").GetComponent<Image>();
            upBtn.onClick.AddListener(Sfx.PlayClick);
            upBtn.onClick.AddListener(() => { if (nextIndex >= 0) this.onPlay(nextIndex); });
            upNext.gameObject.AddComponent<JuicyPress>().Bind(null, 0f, 0.025f);

            float thumbW = T.U(88f), thumbH = T.U(126f);
            var upMask = UIFactory.RoundRect("ThumbMask", upNext, Color.white, 14f);
            UIFactory.Place(upMask.rectTransform, new Vector2(0f, 0.5f), new Vector2(thumbW, thumbH), new Vector2(T.U(12f), 0f));
            upMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            upThumb = UIFactory.Stretch(UIFactory.Rect("Thumb", upMask.transform)).gameObject.AddComponent<RawImage>();
            upThumb.raycastTarget = false;
            upBurns = upThumb.gameObject.AddComponent<KenBurns>();
            upBurns.target = upThumb;
            upAccent = UIFactory.Stroke("Accent", upMask.transform, T.orange, 14f);

            float textX = T.U(12f) + thumbW + T.U(14f);
            upCaps = UIFactory.Text("Caps", upNext, "UP NEXT", TextRole.Caps, T.caps, T.orange, TextAnchor.UpperLeft);
            var capsRt = upCaps.rectTransform;
            capsRt.anchorMin = new Vector2(0f, 1f); capsRt.anchorMax = new Vector2(1f, 1f); capsRt.pivot = new Vector2(0f, 1f);
            capsRt.offsetMin = new Vector2(textX, -T.U(36f)); capsRt.offsetMax = new Vector2(-T.U(12f), -T.U(16f));
            upTitle = UIFactory.Text("Title", upNext, "", TextRole.Title, 25f, Color.white, TextAnchor.UpperLeft);
            var titleRt = upTitle.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f); titleRt.pivot = new Vector2(0f, 1f);
            titleRt.offsetMin = new Vector2(textX, -T.U(96f)); titleRt.offsetMax = new Vector2(-T.U(12f), -T.U(38f));
            var upChips = UIFactory.Rect("Chips", upNext);
            upChips.anchorMin = Vector2.zero; upChips.anchorMax = new Vector2(1f, 0f); upChips.pivot = new Vector2(0f, 0f);
            upChips.offsetMin = new Vector2(textX, T.U(14f)); upChips.offsetMax = new Vector2(-T.U(12f), T.U(14f + 28f));
            var upRow = upChips.gameObject.AddComponent<HorizontalLayoutGroup>();
            upRow.childAlignment = TextAnchor.MiddleLeft;
            upRow.spacing = T.U(8f);
            upRow.childControlWidth = true;
            upRow.childControlHeight = false;
            upRow.childForceExpandWidth = upRow.childForceExpandHeight = false;
            upPiecesChip = UIFactory.Chip("Pieces", upChips, "9 tiles", UISprites.Grid, T.cyan, 28f);
            var starsHolder = UIFactory.Rect("StarsHolder", upChips);
            starsHolder.sizeDelta = new Vector2(T.U(76f), T.U(28f));
            starsHolder.gameObject.AddComponent<LayoutElement>().preferredWidth = T.U(76f);
            upStars = UIFactory.Stars(starsHolder, T.U(20f), T.U(3f));
            UIFactory.Place((RectTransform)upStars[0].transform.parent.parent, new Vector2(0f, 0.5f), new Vector2(T.U(66f), T.U(20f)));

            // The selected pack: title, progress caps and the Packs button (pink: unlocks live there).
            section = UIFactory.Rect("Section", content);
            section.gameObject.AddComponent<LayoutElement>().preferredHeight = T.U(58f);
            float packsW = T.U(124f);
            sectionTitle = UIFactory.Text("Title", section, "All puzzles", TextRole.Title, T.titleS, Color.white, TextAnchor.UpperLeft);
            var strt = sectionTitle.rectTransform;
            strt.anchorMin = new Vector2(0f, 1f); strt.anchorMax = new Vector2(1f, 1f); strt.pivot = new Vector2(0f, 1f);
            strt.offsetMin = new Vector2(Pad + T.U(4f), -T.U(34f)); strt.offsetMax = new Vector2(-Pad - packsW - T.U(8f), -T.U(4f));
            sectionCaps = UIFactory.Text("Count", section, "", TextRole.Caps, T.caps, T.textDim, TextAnchor.UpperLeft);
            var scrt = sectionCaps.rectTransform;
            scrt.anchorMin = new Vector2(0f, 1f); scrt.anchorMax = new Vector2(1f, 1f); scrt.pivot = new Vector2(0f, 1f);
            scrt.offsetMin = new Vector2(Pad + T.U(4f), -T.U(56f)); scrt.offsetMax = new Vector2(-Pad - packsW - T.U(8f), -T.U(38f));
            packsButton = UIFactory.JuicyButton("Packs", section, ButtonTone.Pink, ButtonSize.Medium, "Packs", () => onOpenPacks?.Invoke(), UISprites.Grid);
            UIFactory.Place((RectTransform)packsButton.transform, new Vector2(1f, 0.5f), new Vector2(packsW, UIFactory.ButtonHeight(ButtonSize.Medium)), new Vector2(-Pad, 0f));
            packsDot = UIFactory.Image("Dot", packsButton.transform, T.dot, UISprites.Circle);
            UIFactory.Place(packsDot.rectTransform, new Vector2(1f, 1f), new Vector2(T.U(16f), T.U(16f)), new Vector2(T.U(3f), T.U(3f)));
            UIFactory.Ink(packsDot, 1.5f);

            // A locked or empty pack shows its card (unlock / buy / coming soon) above its tiles.
            packCardRow = UIFactory.Rect("PackCardRow", content);
            packCardRow.gameObject.AddComponent<LayoutElement>().preferredHeight = T.U(PackCard.HeightDp);
            packCard = new PackCard("PackCard", packCardRow, host, null, showPlay: false);
            UIFactory.Stretch(packCard.Root, Pad, Pad, 0, 0);

            gridRoot = UIFactory.Rect("Grid", content);
            var grid = gridRoot.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(TileW, TileH);
            grid.spacing = new Vector2(T.U(T.gap), T.U(T.gap) * 1.4f);
            grid.padding = new RectOffset((int)Pad, (int)Pad, (int)T.U(4f), 0);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;

            // ---------------------------------------------------------------- Play dock (thumb zone)
            playDock = UIFactory.Rect("PlayDock", Root);
            playDock.anchorMin = Vector2.zero;
            playDock.anchorMax = new Vector2(1f, 0f);
            playDock.pivot = new Vector2(0.5f, 0f);
            playDock.sizeDelta = new Vector2(0f, UIFactory.ButtonHeight(ButtonSize.Hero) + T.U(T.screenPad + 70f));
            var fade = UIFactory.Image("Fade", playDock, Color.white);
            UIFactory.Stretch(fade.rectTransform, 0, 0, 0, -T.U(40f));
            UIFactory.Gradient(fade, new Color(T.bgBottom.r, T.bgBottom.g, T.bgBottom.b, 0f), new Color(T.bgBottom.r, T.bgBottom.g, T.bgBottom.b, 0.96f));
            playButton = UIFactory.JuicyButton("Play", playDock, ButtonTone.Primary, ButtonSize.Hero, "Play",
                () => { if (nextIndex >= 0) this.onPlay(nextIndex); }, UISprites.Play, "Level 1");
            var prt = (RectTransform)playButton.transform;
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.sizeDelta = new Vector2(-2f * Pad, UIFactory.ButtonHeight(ButtonSize.Hero));
            prt.anchoredPosition = new Vector2(0f, T.U(T.screenPad));
            var parts = playButton.GetComponent<JuicyButtonParts>();
            playPulse = parts.face.gameObject.AddComponent<Pulse>();

            message = UIFactory.Text("Message", Root, "", TextRole.Body, T.body, T.textDim);
            UIFactory.Place(message.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(T.U(320f), T.U(160f)));

            GameServices.LevelSelectBottomInsetChanged += ApplyBottomInset;
            Tokens.Changed += OnWalletChanged;
            GameServices.PacksChanged += OnWalletChanged;
            ApplyBottomInset();
            Hide();
        }

        /// <summary>Lifts the Play dock and the scroll body above a loaded banner; nothing is reserved while no banner is loaded.</summary>
        void ApplyBottomInset()
        {
            float px = GameServices.LevelSelectBottomInset;
            float units = px > 0f ? Mathf.Max(0f, px - UILayout.SafeArea.yMin) / UILayout.Scale : 0f;
            viewport.offsetMin = new Vector2(viewport.offsetMin.x, units);
            playDock.anchoredPosition = new Vector2(0f, units);
        }

        void OnWalletChanged()
        {
            if (IsVisible) Refresh();
        }

        void OnWordmarkTap()
        {
            if (Time.unscaledTime - devTapTime > 0.6f) devTaps = 0;
            devTapTime = Time.unscaledTime;
            if (++devTaps >= 5)
            {
                devTaps = 0;
                DevMenuRequested?.Invoke();
            }
        }

        // ---------------------------------------------------------------- packs

        /// <summary>The pack whose puzzles the grid shows; remembered across launches, starter by default.</summary>
        public PackInfo CurrentPack
        {
            get
            {
                var p = PackCatalog.Get(PlayerPrefs.GetString(SelectedPackKey, PackInfo.StarterId));
                return p ?? PackCatalog.Starter;
            }
        }

        /// <summary>Shows <paramref name="pack"/>'s puzzles and scrolls to them.</summary>
        public void SelectPack(PackInfo pack)
        {
            if (pack == null) return;
            PlayerPrefs.SetString(SelectedPackKey, pack.id);
            PlayerPrefs.Save();
            RebuildGrid();
            Refresh();
            if (IsVisible)
            {
                host.StartCoroutine(LoadThumbnails());
                ScrollToSection();
            }
        }

        void ScrollToSection()
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            float top = -section.anchoredPosition.y - section.rect.height * (1f - section.pivot.y) - T.U(8f);
            float max = Mathf.Max(0f, content.rect.height - viewport.rect.height);
            scroll.StopMovement();
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(top, 0f, max));
        }

        public void Build(IReadOnlyList<LevelInfo> levels, string error)
        {
            message.text = error ?? (levels.Count == 0 ? "No levels found." : "");
            builtFor = null;
            RebuildGrid();
            Refresh();
        }

        void RebuildGrid()
        {
            var pack = CurrentPack;
            if (pack == builtFor && tiles.Count == (pack?.Levels.Count ?? 0)) return;
            foreach (var c in tiles) UnityEngine.Object.Destroy(c.Button.gameObject);
            tiles.Clear();
            builtFor = pack;
            if (pack == null) return;
            foreach (var level in pack.Levels) tiles.Add(BuildTile(level));
        }

        Tile BuildTile(LevelInfo level)
        {
            float r = T.radiusMd;
            var tile = new Tile { Level = level };
            Color accent = T.Category(level.category);

            var root = UIFactory.Rect("Level" + (level.PackIndex + 1), gridRoot);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            tile.Group = root.gameObject.AddComponent<CanvasGroup>();

            var fx = UIFactory.Rect("NextFx", root);
            UIFactory.Stretch(fx);
            var glow = UIFactory.SoftShadow("Glow", fx, 16f, 0f, 0.9f);
            glow.color = new Color(T.orange.r, T.orange.g, T.orange.b, 0.75f);
            tile.NextFx = fx.gameObject;

            UIFactory.SoftShadow("Shadow", root, 10f, 5f, 0.4f);
            var face = UIFactory.RoundRect("Face", root, Color.white, r);
            UIFactory.Stretch(face.rectTransform);
            UIFactory.Gradient(face, T.tileTop, T.tileBottom);

            float inset = T.U(5f), footer = T.U(34f);
            var mask = UIFactory.RoundRect("ThumbMask", root, Color.white, r - 4f);
            UIFactory.Stretch(mask.rectTransform, inset, inset, inset, footer);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            tile.Thumb = UIFactory.Stretch(UIFactory.Rect("Thumb", mask.transform)).gameObject.AddComponent<RawImage>();
            tile.Thumb.raycastTarget = false;
            tile.Thumb.color = Color.Lerp(accent, T.surfaceLow, 0.35f);
            var shade = UIFactory.Image("Shade", mask.transform, Color.white);
            var srt = shade.rectTransform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = new Vector2(1f, 0.5f); srt.offsetMin = srt.offsetMax = Vector2.zero;
            UIFactory.Gradient(shade, new Color(0.03f, 0.02f, 0.1f, 0f), new Color(0.03f, 0.02f, 0.1f, 0.8f));
            var title = UIFactory.Text("Title", mask.transform, level.DisplayTitle, TextRole.Label, 12f, Color.white, TextAnchor.LowerLeft);
            UIFactory.Stretch(title.rectTransform, T.U(8f), T.U(6f), T.U(60f), T.U(6f));
            tile.Dim = UIFactory.Image("Dim", mask.transform, new Color(T.ink.r, T.ink.g, T.ink.b, 0.62f));
            UIFactory.Stretch(tile.Dim.rectTransform);

            tile.NumberDisc = UIFactory.Image("Number", root, accent, UISprites.Circle);
            UIFactory.Place(tile.NumberDisc.rectTransform, new Vector2(0f, 1f), new Vector2(T.U(30f), T.U(30f)), new Vector2(T.U(9f), -T.U(9f)));
            UIFactory.Ink(tile.NumberDisc, 1.5f);
            var num = UIFactory.Text("Text", tile.NumberDisc.transform, (level.PackIndex + 1).ToString(), TextRole.Number, 15f, Color.white);
            UIFactory.Stretch(num.rectTransform, 0, 0, 0, T.U(1f));
            num.horizontalOverflow = HorizontalWrapMode.Overflow;

            tile.Check = UIFactory.Image("Check", root, T.green, UISprites.Circle);
            UIFactory.Place(tile.Check.rectTransform, new Vector2(1f, 1f), new Vector2(T.U(24f), T.U(24f)), new Vector2(-T.U(9f), -T.U(9f)));
            UIFactory.Ink(tile.Check, 1.5f);
            var checkIcon = UIFactory.Icon("Icon", tile.Check.transform, UISprites.Check, Color.white, 16f);
            UIFactory.Place(checkIcon.rectTransform, new Vector2(0.5f, 0.5f), checkIcon.rectTransform.sizeDelta);

            tile.Lock = UIFactory.Icon("Lock", root, UISprites.Lock, new Color(1f, 1f, 1f, 0.85f), 30f);
            UIFactory.Place(tile.Lock.rectTransform, new Vector2(0.5f, 0.5f), tile.Lock.rectTransform.sizeDelta, new Vector2(0f, footer * 0.5f));

            var starsRow = UIFactory.Rect("StarRow", root);
            UIFactory.Place(starsRow, new Vector2(0.5f, 0f), new Vector2(T.U(80f), footer), Vector2.zero);
            tile.Stars = UIFactory.Stars(starsRow, T.U(19f), T.U(3f), raisedMiddle: true);
            ((RectTransform)tile.Stars[0].transform.parent.parent).anchoredPosition = new Vector2(0f, -T.U(2f));

            var flame = UIFactory.Stroke("Flame", root, Color.white, r, 1.5f);
            UIFactory.Gradient(flame, T.sunsetA, T.sunsetB);
            tile.Flame = flame.gameObject;
            var pill = UIFactory.RoundRect("PlayPill", root, Color.white, 14f);
            UIFactory.Place(pill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(T.U(78f), T.U(30f)), new Vector2(0f, footer * 0.5f + T.U(6f)));
            UIFactory.Gradient(pill, T.primaryTop, T.primaryBottom);
            UIFactory.Ink(pill, 1.2f);
            var pillIcon = UIFactory.Icon("Icon", pill.transform, UISprites.Play, Color.white, 12f);
            UIFactory.Place(pillIcon.rectTransform, new Vector2(0f, 0.5f), pillIcon.rectTransform.sizeDelta, new Vector2(T.U(12f), 0f));
            var pillText = UIFactory.Text("Text", pill.transform, "Play", TextRole.Title, 16f, Color.white, TextAnchor.MiddleRight);
            UIFactory.Stretch(pillText.rectTransform, 0, T.U(12f), 0, T.U(1f));
            tile.PlayPulse = pill.gameObject.AddComponent<Pulse>();
            tile.PlayPulse.amount = 0.06f;

            tile.Button = root.gameObject.AddComponent<Button>();
            tile.Button.transition = Selectable.Transition.None;
            tile.Button.targetGraphic = hit;
            var nav = tile.Button.navigation; nav.mode = Navigation.Mode.None; tile.Button.navigation = nav;
            tile.Button.onClick.AddListener(Sfx.PlayClick);
            tile.Button.onClick.AddListener(() => OnTileTapped(level));
            root.gameObject.AddComponent<JuicyPress>().Bind(null, 0f, 0.06f);
            if (Thumbnails.TryGet(level.id, out var cached)) ApplyThumb(tile, cached);
            return tile;
        }

        void OnTileTapped(LevelInfo level)
        {
            if (PackAccess.IsLevelUnlocked(level)) onPlay(level.Index);
            else if (!PackAccess.IsUnlocked(PackCatalog.For(level))) onPreview?.Invoke(level);
        }

        public override void Show()
        {
            base.Show();
            RebuildGrid();
            Refresh();
            tokenPill.Refresh(false);
            host.StartCoroutine(LoadThumbnails());
            GameEvents.SetLevelSelectVisible(true);
            PlayEntrance();
        }

        public override void Hide()
        {
            base.Hide();
            GameEvents.SetLevelSelectVisible(false);
        }

        void PlayEntrance()
        {
            UITween.Get(Root).Fade(0f, 1f).Move(new Vector2(0f, -T.U(36f)), Vector2.zero).Play(T.medium * 0.9f, EaseType.OutCubic);
            UITween.Get(header).Fade(0f, 1f).Scale(0.92f, 1f).Play(T.medium, EaseType.OutBack, 0f);
            UITween.Get(mark.Root).Scale(0.6f, 1f).Play(T.medium, EaseType.OutBack, 0.12f);
            UITween.Get(upNext).Fade(0f, 1f).Scale(0.9f, 1f).Play(T.medium, EaseType.OutBack, 0.18f);
            UITween.Get(playButton).Fade(0f, 1f).Scale(0.85f, 1f).Play(T.medium, EaseType.OutBack, 0.24f);
            host.StartCoroutine(SnapMark());
            playPulse.Restart();
        }

        IEnumerator SnapMark()
        {
            mark.SetSnap(0f);
            float t = -0.35f;
            while (t < 0.4f)
            {
                t += Time.unscaledDeltaTime;
                mark.SetSnap(Ease.Apply(EaseType.OutBackStrong, t / 0.4f));
                yield return null;
            }
            mark.SetSnap(1f);
        }

        /// <summary>
        /// The global index of the next puzzle to play: the first open, uncleared puzzle in the selected pack, then in
        /// the other unlocked packs (display order); when everything open is cleared, the selected pack's first puzzle.
        /// </summary>
        static int FindNext(PackInfo current)
        {
            int found = FirstOpenUncleared(current);
            if (found >= 0) return found;
            foreach (var p in PackAccess.InDisplayOrder())
            {
                if (p == current) continue;
                found = FirstOpenUncleared(p);
                if (found >= 0) return found;
            }
            if (current != null && !current.IsEmpty && PackAccess.IsUnlocked(current)) return current.Levels[0].Index;
            var starter = PackCatalog.Starter;
            return starter != null && !starter.IsEmpty ? starter.Levels[0].Index : -1;
        }

        static int FirstOpenUncleared(PackInfo pack)
        {
            if (pack == null || !PackAccess.IsUnlocked(pack)) return -1;
            foreach (var l in pack.Levels)
                if (!Progress.IsCleared(l.id) && PackAccess.IsLevelUnlocked(l)) return l.Index;
            return -1;
        }

        public void Refresh()
        {
            var levels = LevelCatalog.Levels;
            var pack = CurrentPack;
            bool packOpen = PackAccess.IsUnlocked(pack);
            nextIndex = levels.Count > 0 ? FindNext(pack) : -1;

            int cleared = 0;
            for (int i = 0; i < tiles.Count; i++)
            {
                var c = tiles[i];
                int s = Progress.GetStars(c.Level.id);
                bool isCleared = Progress.IsCleared(c.Level.id);
                bool unlocked = PackAccess.IsLevelUnlocked(c.Level);
                bool isNext = c.Level.Index == nextIndex && !isCleared;
                if (isCleared) cleared++;

                UIFactory.SetStars(c.Stars, s);
                c.Stars[0].transform.parent.parent.gameObject.SetActive(unlocked);
                c.Check.gameObject.SetActive(isCleared && packOpen);
                c.Lock.gameObject.SetActive(!unlocked);
                c.Dim.gameObject.SetActive(!unlocked);
                c.NumberDisc.gameObject.SetActive(unlocked);
                c.Group.alpha = unlocked ? 1f : 0.75f;
                // Locked-pack tiles stay tappable: they open a preview.
                c.Button.interactable = unlocked || !packOpen;
                c.NextFx.SetActive(isNext);
                c.Flame.SetActive(isNext);
                c.PlayPulse.gameObject.SetActive(isNext);
                if (isNext) c.PlayPulse.Restart();
            }

            int totalStars = 0, maxStars = 0;
            foreach (var p in PackCatalog.Packs)
            {
                if (!PackAccess.IsUnlocked(p)) continue;
                totalStars += PackAccess.StarsIn(p, out int m);
                maxStars += m;
            }
            UIFactory.ChipText(starChip).text = totalStars + " / " + maxStars;
            tokenPill.Refresh(true);

            int n = pack?.Levels.Count ?? 0;
            sectionTitle.text = pack == null ? "All puzzles" : pack.DisplayTitle;
            sectionCaps.text = UIFactory.Track(n == 0 ? "Coming soon"
                : packOpen ? cleared + " of " + n + " cleared"
                : "Locked · " + n + (n == 1 ? " puzzle" : " puzzles"));
            sectionCaps.color = n > 0 && !packOpen ? T.pink : T.textDim;
            packsDot.gameObject.SetActive(AnyAffordableLockedPack());
            packCardRow.gameObject.SetActive(pack != null && (!packOpen || pack.IsEmpty));
            if (packCardRow.gameObject.activeSelf)
            {
                if (packCard.Pack != pack) packCard.Bind(pack);
                else packCard.Refresh();
            }

            upNext.gameObject.SetActive(nextIndex >= 0);
            playButton.gameObject.SetActive(nextIndex >= 0);
            if (nextIndex >= 0)
            {
                var next = levels[nextIndex];
                var nextPack = PackCatalog.For(next);
                bool allDone = Progress.IsCleared(next.id);
                Color accent = T.Category(next.category);
                string where = LevelLabel(next, nextPack);
                upCaps.text = UIFactory.Track((allDone ? "Play again" : "Up next") + " · " + where);
                upCaps.color = accent;
                upAccent.color = accent;
                upTitle.text = next.DisplayTitle;
                UIFactory.ChipText(upPiecesChip).text = next.PieceCount + " tiles";
                UIFactory.SetStars(upStars, Progress.GetStars(next.id));
                upThumb.color = Thumbnails.TryGet(next.id, out var tex) && tex != null ? Color.white : Color.Lerp(accent, T.surfaceLow, 0.35f);
                if (tex != null) ApplyUpNextThumb(tex);
                UIFactory.SetButtonText(playButton, "Play", where + " · " + next.DisplayTitle);
            }
        }

        /// <summary>"Level 3" in the starter pack, "Space 3" elsewhere.</summary>
        public static string LevelLabel(LevelInfo level, PackInfo pack)
        {
            string prefix = pack == null || pack.IsStarter ? "Level" : pack.DisplayTitle;
            return prefix + " " + (level.PackIndex + 1);
        }

        static bool AnyAffordableLockedPack()
        {
            foreach (var p in PackCatalog.Packs)
                if (!p.IsEmpty && !PackAccess.IsUnlocked(p) && PackAccess.CanAfford(p)) return true;
            return false;
        }

        // Optional thumbnails: StreamingAssets/Levels/<id>.jpg. Missing files just keep the category colour.
        IEnumerator LoadThumbnails()
        {
            yield return null;
            if (nextIndex >= 0)
            {
                var next = LevelCatalog.Get(nextIndex);
                if (next != null) yield return Thumbnails.Load(next.id, tex => { if (tex != null && LevelCatalog.Get(nextIndex) == next) ApplyUpNextThumb(tex); });
            }
            var snapshot = tiles.ToArray();
            foreach (var c in snapshot)
            {
                if (!IsVisible || builtFor != CurrentPack) yield break;
                yield return Thumbnails.Load(c.Level.id, tex => ApplyThumb(c, tex));
            }
        }

        void ApplyThumb(Tile c, Texture2D tex)
        {
            if (tex == null || c.Thumb == null) return;
            c.Thumb.texture = tex;
            c.Thumb.color = Color.white;
            c.Thumb.uvRect = Thumbnails.CoverRect(c.Thumb.rectTransform.rect, tex);
        }

        void ApplyUpNextThumb(Texture2D tex)
        {
            upThumb.texture = tex;
            upThumb.color = Color.white;
            upBurns.baseRect = Thumbnails.CoverRect(upThumb.rectTransform.rect, tex);
            upThumb.uvRect = upBurns.baseRect;
        }
    }
}
