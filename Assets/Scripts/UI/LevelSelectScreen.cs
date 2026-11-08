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
    /// <summary>Grid of levels with thumbnail, stars/clear-check and lock state.</summary>
    public sealed class LevelSelectScreen : UIScreen
    {
        sealed class Card
        {
            public LevelInfo Level;
            public Button Button;
            public RawImage Thumb;
            public Image Lock;
            public Image Check;
            public Image[] Stars;
            public Text Number;
        }

        readonly MonoBehaviour host;
        readonly Action<int> onPlay;
        readonly RectTransform content, viewport;
        readonly Text message;
        readonly List<Card> cards = new List<Card>();
        readonly Dictionary<string, Texture2D> thumbs = new Dictionary<string, Texture2D>();
        int devTaps;
        float devTapTime;

        public event Action DevMenuRequested;

        const int Columns = 3;
        const float CardW = 316f, CardH = 560f, Gap = 24f;

        public LevelSelectScreen(Transform safeRoot, MonoBehaviour coroutineHost, Action<int> onPlay, Action onSettings)
        {
            host = coroutineHost;
            this.onPlay = onPlay;
            Root = UIFactory.Stretch(UIFactory.Rect("LevelSelect", safeRoot));

            var header = UIFactory.Band(UIFactory.Rect("Header", Root), 1f, 200f);
            var word = UIFactory.Label("Wordmark", header, "Snaploop", 84, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(word.rectTransform, new Vector2(0f, 0.5f), new Vector2(560, 120), new Vector2(40, 0));
            // Hidden dev menu: tap the wordmark 5 times quickly (works in release builds, for recording clips).
            word.raycastTarget = true;
            var wordBtn = word.gameObject.AddComponent<Button>();
            wordBtn.transition = Selectable.Transition.None;
            wordBtn.onClick.AddListener(OnWordmarkTap);

            var gear = UIFactory.Button("Settings", header, "Settings", Palette.Surface, () => onSettings(), 34);
            UIFactory.Place((RectTransform)gear.transform, new Vector2(1f, 0.5f), new Vector2(220, 104), new Vector2(-32, 0));

            if (Debug.isDebugBuild)
            {
                var dev = UIFactory.Button("Dev", header, "DEV", Palette.SurfaceHigh, () => DevMenuRequested?.Invoke(), 30);
                UIFactory.Place((RectTransform)dev.transform, new Vector2(1f, 0.5f), new Vector2(120, 104), new Vector2(-268, 0));
            }

            // Scrollable grid.
            var viewportImage = UIFactory.Image("Viewport", Root, new Color(0, 0, 0, 0), null, raycast: true);
            viewport = UIFactory.Stretch(viewportImage.rectTransform, 0, 0, 200, 0);
            viewportImage.gameObject.AddComponent<RectMask2D>();
            var scroll = viewportImage.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            content = UIFactory.Rect("Content", viewport.transform);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CardW, CardH);
            grid.spacing = new Vector2(Gap, Gap);
            grid.padding = new RectOffset(30, 30, 10, 60);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;

            message = UIFactory.Label("Message", Root, "", 40, Palette.TextDim);
            UIFactory.Place(message.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900, 400));
            GameServices.LevelSelectBottomInsetChanged += ApplyBottomInset;
            ApplyBottomInset();
            Hide();
        }

        /// <summary>Shrinks the grid above a loaded banner; nothing is reserved while no banner is loaded.</summary>
        void ApplyBottomInset()
        {
            float px = GameServices.LevelSelectBottomInset;
            float units = px > 0f ? Mathf.Max(0f, px - UILayout.SafeArea.yMin) / UILayout.Scale : 0f;
            viewport.offsetMin = new Vector2(viewport.offsetMin.x, units);
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
            foreach (var c in cards) UnityEngine.Object.Destroy(c.Button.gameObject);
            cards.Clear();
            message.text = error ?? (levels.Count == 0 ? "No levels found." : "");

            for (int i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                int index = i;
                var card = new Card { Level = level };
                var bg = UIFactory.Panel("Level" + (i + 1), content, Palette.Surface);
                card.Button = bg.gameObject.AddComponent<Button>();
                card.Button.targetGraphic = bg;
                card.Button.onClick.AddListener(Sfx.PlayClick);
                card.Button.onClick.AddListener(() => onPlay(index));

                // Thumbnail area (9:16-ish), masked to the card's rounded top.
                var thumbMask = UIFactory.Image("ThumbMask", bg.transform, Color.white, UIFactory.Rounded);
                UIFactory.Stretch(thumbMask.rectTransform, 10, 10, 10, 180);
                thumbMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                var thumbGo = UIFactory.Rect("Thumb", thumbMask.transform);
                UIFactory.Stretch(thumbGo);
                card.Thumb = thumbGo.gameObject.AddComponent<RawImage>();
                card.Thumb.raycastTarget = false;
                card.Thumb.color = CategoryColor(level.category);

                card.Number = UIFactory.Label("Number", bg.transform, (i + 1).ToString(), 40, Palette.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                UIFactory.Place(card.Number.rectTransform, new Vector2(0f, 1f), new Vector2(120, 60), new Vector2(26, -20));
                var outline = card.Number.gameObject.AddComponent<Shadow>();
                outline.effectColor = new Color(0, 0, 0, 0.7f);
                outline.effectDistance = new Vector2(2, -2);

                var title = UIFactory.Label("Title", bg.transform, level.DisplayTitle, 32, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Place(title.rectTransform, new Vector2(0.5f, 0f), new Vector2(CardW - 20, 50), new Vector2(0, 66));
                var grid = UIFactory.Label("Grid", bg.transform, level.PieceCount + " pieces", 26, Palette.TextDim);
                UIFactory.Place(grid.rectTransform, new Vector2(0.5f, 0f), new Vector2(CardW - 20, 40), new Vector2(0, 30));

                var starRow = UIFactory.Rect("StarRow", bg.transform);
                UIFactory.Place(starRow, new Vector2(0.5f, 0f), new Vector2(200, 50), new Vector2(0, 142));
                card.Stars = UIFactory.Stars(starRow, 46, 6);

                card.Check = UIFactory.Image("Check", bg.transform, Palette.Accent2, UIFactory.Check);
                UIFactory.Place(card.Check.rectTransform, new Vector2(1f, 1f), new Vector2(64, 64), new Vector2(-20, -18));

                card.Lock = UIFactory.Panel("Lock", bg.transform, new Color(0.05f, 0.05f, 0.08f, 0.72f), raycast: false);
                UIFactory.Stretch(card.Lock.rectTransform);
                var lockText = UIFactory.Label("LockText", card.Lock.transform, "Locked", 34, Palette.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(lockText.rectTransform);

                cards.Add(card);
            }
            Refresh();
        }

        public override void Show()
        {
            base.Show();
            Refresh();
            host.StartCoroutine(LoadThumbnails());
            GameEvents.SetLevelSelectVisible(true);
        }

        public override void Hide()
        {
            base.Hide();
            GameEvents.SetLevelSelectVisible(false);
        }

        public static bool IsUnlocked(IReadOnlyList<LevelInfo> levels, int i)
        {
            if (i <= 0 || Progress.UnlockAll) return true;
            return Progress.IsCleared(levels[i].id) || Progress.IsCleared(levels[i - 1].id);
        }

        public void Refresh()
        {
            var levels = LevelCatalog.Levels;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                int s = Progress.GetStars(c.Level.id);
                bool unlocked = IsUnlocked(levels, i);
                UIFactory.SetStars(c.Stars, s);
                c.Stars[0].transform.parent.gameObject.SetActive(unlocked);
                c.Check.gameObject.SetActive(s > 0);
                c.Lock.gameObject.SetActive(!unlocked);
                c.Button.interactable = unlocked;
            }
        }

        // Optional thumbnails: StreamingAssets/Levels/<id>.jpg. Missing files just keep the category colour.
        IEnumerator LoadThumbnails()
        {
            foreach (var c in cards)
            {
                if (!IsVisible) yield break;
                if (thumbs.TryGetValue(c.Level.id, out var cached))
                {
                    ApplyThumb(c, cached);
                    continue;
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
        }

        static void ApplyThumb(Card c, Texture2D tex)
        {
            if (tex == null) return;
            c.Thumb.texture = tex;
            c.Thumb.color = Color.white;
            // Cover-fit the thumbnail into the card area.
            var r = c.Thumb.rectTransform.rect;
            if (r.width <= 0 || r.height <= 0) return;
            float areaAspect = r.width / r.height, texAspect = tex.width / (float)tex.height;
            c.Thumb.uvRect = texAspect > areaAspect
                ? new Rect((1f - areaAspect / texAspect) * 0.5f, 0f, areaAspect / texAspect, 1f)
                : new Rect(0f, (1f - texAspect / areaAspect) * 0.5f, 1f, texAspect / areaAspect);
        }

        static Color CategoryColor(string category)
        {
            switch (category)
            {
                case "sunsets": return new Color32(196, 92, 60, 255);
                case "crowds": return new Color32(120, 70, 150, 255);
                case "downtown": return new Color32(60, 90, 140, 255);
                case "nature": return new Color32(50, 130, 90, 255);
                default: return new Color32(70, 80, 100, 255);
            }
        }
    }
}
