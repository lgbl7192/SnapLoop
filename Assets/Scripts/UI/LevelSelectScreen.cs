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
    /// <summary>
    /// The hub: progress up top, the brand, an "Up next" card, every puzzle as a thumbnail tile, and one big
    /// Play button in the thumb zone that names the next puzzle.
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

        readonly MonoBehaviour host;
        readonly Action<int> onPlay;
        readonly RectTransform header, viewport, content, gridRoot, upNext, playDock;
        readonly Text message;
        readonly List<Tile> tiles = new List<Tile>();
        readonly Dictionary<string, Texture2D> thumbs = new Dictionary<string, Texture2D>();

        readonly RectTransform starChip, clearedChip, sectionCount;
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

        int nextIndex;
        int devTaps;
        float devTapTime;

        public event Action DevMenuRequested;

        /// <summary>Empty container between the brand and "Up next", kept for future retention features (daily rewards, streaks).</summary>
        public RectTransform RetentionSlot { get; }

        const int Columns = 3;
        float Pad => T.U(T.screenPad);
        float TileW => (UILayout.ReferenceWidth - 2f * Pad - (Columns - 1) * T.U(T.gap)) / Columns;
        float TileH => T.U(178f);

        public LevelSelectScreen(Transform safeRoot, MonoBehaviour coroutineHost, Action<int> onPlay, Action onSettings)
        {
            host = coroutineHost;
            this.onPlay = onPlay;
            Root = UIFactory.Stretch(UIFactory.Rect("LevelSelect", safeRoot));

            // ---------------------------------------------------------------- header (fixed)
            float headerH = T.U(68f);
            header = UIFactory.Band(UIFactory.Rect("Header", Root), 1f, headerH);

            var chips = UIFactory.Rect("Chips", header);
            chips.anchorMin = new Vector2(0f, 0f);
            chips.anchorMax = new Vector2(0.7f, 1f);
            chips.offsetMin = new Vector2(Pad, 0f);
            chips.offsetMax = Vector2.zero;
            var chipRow = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipRow.childAlignment = TextAnchor.MiddleLeft;
            chipRow.spacing = T.U(8f);
            chipRow.childControlWidth = false;
            chipRow.childControlHeight = false;
            chipRow.childForceExpandWidth = chipRow.childForceExpandHeight = false;
            starChip = UIFactory.Chip("StarsChip", chips, "0", UISprites.Star, T.star, 36f);
            clearedChip = UIFactory.Chip("ClearedChip", chips, "0/0", UISprites.Check, T.green, 36f);

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
            var scroll = viewportImage.gameObject.AddComponent<ScrollRect>();
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
            upBtn.onClick.AddListener(() => this.onPlay(nextIndex));
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
            upRow.childControlWidth = upRow.childControlHeight = false;
            upRow.childForceExpandWidth = upRow.childForceExpandHeight = false;
            upPiecesChip = UIFactory.Chip("Pieces", upChips, "9 tiles", UISprites.Grid, T.cyan, 28f);
            var starsHolder = UIFactory.Rect("StarsHolder", upChips);
            starsHolder.sizeDelta = new Vector2(T.U(76f), T.U(28f));
            upStars = UIFactory.Stars(starsHolder, T.U(20f), T.U(3f));
            UIFactory.Place((RectTransform)upStars[0].transform.parent.parent, new Vector2(0f, 0.5f), new Vector2(T.U(66f), T.U(20f)));

            // All puzzles.
            var section = UIFactory.Rect("Section", content);
            section.gameObject.AddComponent<LayoutElement>().preferredHeight = T.U(26f);
            var sectionLabel = UIFactory.Text("Label", section, "All puzzles", TextRole.Caps, T.caps, T.textDim, TextAnchor.MiddleLeft);
            UIFactory.Stretch(sectionLabel.rectTransform, Pad + T.U(4f), Pad, 0, 0);
            var sc = UIFactory.Text("Count", section, "", TextRole.Label, T.labelS, T.textMute, TextAnchor.MiddleRight);
            sectionCount = UIFactory.Stretch(sc.rectTransform, Pad, Pad + T.U(4f), 0, 0);

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
                () => this.onPlay(nextIndex), UISprites.Play, "Level 1");
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

        public void Build(IReadOnlyList<LevelInfo> levels, string error)
        {
            foreach (var c in tiles) UnityEngine.Object.Destroy(c.Button.gameObject);
            tiles.Clear();
            message.text = error ?? (levels.Count == 0 ? "No levels found." : "");
            upNext.gameObject.SetActive(levels.Count > 0);
            playButton.gameObject.SetActive(levels.Count > 0);

            for (int i = 0; i < levels.Count; i++)
                tiles.Add(BuildTile(levels[i], i));
            Refresh();
        }

        Tile BuildTile(LevelInfo level, int index)
        {
            float r = T.radiusMd;
            var tile = new Tile { Level = level };
            Color accent = T.Category(level.category);

            var root = UIFactory.Rect("Level" + (index + 1), gridRoot);
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
            var num = UIFactory.Text("Text", tile.NumberDisc.transform, (index + 1).ToString(), TextRole.Number, 15f, Color.white);
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
            tile.Button.onClick.AddListener(() => onPlay(index));
            root.gameObject.AddComponent<JuicyPress>().Bind(null, 0f, 0.06f);
            return tile;
        }

        public override void Show()
        {
            base.Show();
            Refresh();
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

        public static bool IsUnlocked(IReadOnlyList<LevelInfo> levels, int i)
        {
            if (i <= 0 || Progress.UnlockAll) return true;
            return Progress.IsCleared(levels[i].id) || Progress.IsCleared(levels[i - 1].id);
        }

        /// <summary>The first unlocked level not yet cleared, or the first level when everything is cleared.</summary>
        static int FindNext(IReadOnlyList<LevelInfo> levels)
        {
            for (int i = 0; i < levels.Count; i++)
                if (!Progress.IsCleared(levels[i].id) && IsUnlocked(levels, i)) return i;
            return 0;
        }

        public void Refresh()
        {
            var levels = LevelCatalog.Levels;
            int totalStars = 0, cleared = 0;
            nextIndex = levels.Count > 0 ? FindNext(levels) : -1;
            for (int i = 0; i < tiles.Count; i++)
            {
                var c = tiles[i];
                int s = Progress.GetStars(c.Level.id);
                bool isCleared = Progress.IsCleared(c.Level.id);
                bool unlocked = IsUnlocked(levels, i);
                bool isNext = i == nextIndex && !isCleared;
                totalStars += s;
                if (isCleared) cleared++;

                UIFactory.SetStars(c.Stars, s);
                c.Stars[0].transform.parent.parent.gameObject.SetActive(unlocked);
                c.Check.gameObject.SetActive(isCleared);
                c.Lock.gameObject.SetActive(!unlocked);
                c.Dim.gameObject.SetActive(!unlocked);
                c.NumberDisc.gameObject.SetActive(unlocked);
                c.Group.alpha = unlocked ? 1f : 0.75f;
                c.Button.interactable = unlocked;
                c.NextFx.SetActive(isNext);
                c.Flame.SetActive(isNext);
                c.PlayPulse.gameObject.SetActive(isNext);
                if (isNext) c.PlayPulse.Restart();
            }

            UIFactory.ChipText(starChip).text = totalStars + " / " + levels.Count * 3;
            UIFactory.ChipText(clearedChip).text = cleared + " / " + levels.Count;
            ((Text)sectionCount.GetComponent<Text>()).text = cleared + " of " + levels.Count + " cleared";

            if (nextIndex >= 0)
            {
                var next = levels[nextIndex];
                bool allDone = cleared == levels.Count;
                Color accent = T.Category(next.category);
                upCaps.text = UIFactory.Track((allDone ? "Play again" : "Up next") + " · Level " + (nextIndex + 1));
                upCaps.color = accent;
                upAccent.color = accent;
                upTitle.text = next.DisplayTitle;
                UIFactory.ChipText(upPiecesChip).text = next.PieceCount + " tiles";
                UIFactory.SetStars(upStars, Progress.GetStars(next.id));
                upThumb.color = thumbs.TryGetValue(next.id, out var tex) && tex != null ? Color.white : Color.Lerp(accent, T.surfaceLow, 0.35f);
                if (tex != null) ApplyUpNextThumb(tex);
                UIFactory.SetButtonText(playButton, "Play", "Level " + (nextIndex + 1) + " · " + next.DisplayTitle);
            }
        }

        // Optional thumbnails: StreamingAssets/Levels/<id>.jpg. Missing files just keep the category colour.
        IEnumerator LoadThumbnails()
        {
            yield return null;
            if (nextIndex >= 0 && nextIndex < tiles.Count) yield return LoadThumb(tiles[nextIndex]);
            foreach (var c in tiles)
            {
                if (!IsVisible) yield break;
                yield return LoadThumb(c);
            }
        }

        IEnumerator LoadThumb(Tile c)
        {
            if (thumbs.TryGetValue(c.Level.id, out var cached))
            {
                ApplyThumb(c, cached);
                yield break;
            }
            string url = LevelCatalog.GetLevelFileUrl(c.Level.id + ".jpg");
            using (var req = UnityWebRequestTexture.GetTexture(url, true))
            {
                yield return req.SendWebRequest();
                Texture2D tex = null;
                if (req.result == UnityWebRequest.Result.Success) tex = DownloadHandlerTexture.GetContent(req);
                thumbs[c.Level.id] = tex;
                ApplyThumb(c, tex);
            }
        }

        void ApplyThumb(Tile c, Texture2D tex)
        {
            if (tex == null) return;
            c.Thumb.texture = tex;
            c.Thumb.color = Color.white;
            c.Thumb.uvRect = CoverRect(c.Thumb.rectTransform.rect, tex);
            if (nextIndex >= 0 && nextIndex < tiles.Count && tiles[nextIndex] == c) ApplyUpNextThumb(tex);
        }

        void ApplyUpNextThumb(Texture2D tex)
        {
            upThumb.texture = tex;
            upThumb.color = Color.white;
            upBurns.baseRect = CoverRect(upThumb.rectTransform.rect, tex);
            upThumb.uvRect = upBurns.baseRect;
        }

        static Rect CoverRect(Rect area, Texture2D tex)
        {
            if (area.width <= 0 || area.height <= 0) return new Rect(0, 0, 1, 1);
            float areaAspect = area.width / area.height, texAspect = tex.width / (float)tex.height;
            return texAspect > areaAspect
                ? new Rect((1f - areaAspect / texAspect) * 0.5f, 0f, areaAspect / texAspect, 1f)
                : new Rect(0f, (1f - texAspect / areaAspect) * 0.5f, 1f, texAspect / areaAspect);
        }
    }
}
