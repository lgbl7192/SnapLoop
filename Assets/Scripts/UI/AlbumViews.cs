using System;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>
    /// One album page as a row of small portrait cells: filled slots show the thumbnail of the clip that filled them,
    /// empty ones are sunk wells. Used on the hub's album card and on the win card.
    /// </summary>
    public sealed class AlbumStrip
    {
        static UITheme T => UITheme.Current;

        public RectTransform Root { get; }
        readonly MonoBehaviour host;
        readonly RectTransform[] cells = new RectTransform[AlbumRules.SlotsPerPage];
        readonly RawImage[] thumbs = new RawImage[AlbumRules.SlotsPerPage];
        readonly Image[] wells = new Image[AlbumRules.SlotsPerPage];
        readonly Image[] rings = new Image[AlbumRules.SlotsPerPage];
        readonly string[] boundIds = new string[AlbumRules.SlotsPerPage];

        /// <param name="widthUnits">Strip width in canvas units; cells share it.</param>
        public AlbumStrip(string name, Transform parent, MonoBehaviour coroutineHost, float widthUnits, float heightUnits)
        {
            host = coroutineHost;
            Root = UIFactory.Rect(name, parent);
            Root.sizeDelta = new Vector2(widthUnits, heightUnits);
            int n = AlbumRules.SlotsPerPage;
            float gap = T.U(3f);
            float w = (widthUnits - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                var well = UIFactory.RoundRect("Slot" + i, Root, T.surfaceSunk, 6f);
                var rt = well.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(w, heightUnits);
                rt.anchoredPosition = new Vector2(i * (w + gap), 0f);
                UIFactory.Stroke("Hairline", well.transform, T.strokeSoft, 6f);
                var mask = UIFactory.RoundRect("Mask", well.transform, Color.white, 6f);
                UIFactory.Stretch(mask.rectTransform);
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                var thumb = UIFactory.Stretch(UIFactory.Rect("Thumb", mask.transform)).gameObject.AddComponent<RawImage>();
                thumb.raycastTarget = false;
                var ring = UIFactory.Stroke("New", well.transform, T.gold, 6f, 1.5f);
                ring.gameObject.SetActive(false);
                cells[i] = rt;
                wells[i] = well;
                thumbs[i] = thumb;
                rings[i] = ring;
            }
        }

        /// <summary>Shows <paramref name="page"/>; <paramref name="highlightSlot"/> (or -1) gets a gold ring and pops in.</summary>
        public void Bind(int page, int highlightSlot = -1)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                string id = Album.At(page, i);
                boundIds[i] = id;
                bool filled = id != null;
                thumbs[i].gameObject.SetActive(filled);
                wells[i].color = filled ? T.surfaceLow : T.surfaceSunk;
                rings[i].gameObject.SetActive(i == highlightSlot);
                if (!filled) continue;
                var level = AlbumScreen.FindLevel(id);
                thumbs[i].texture = null;
                thumbs[i].color = level != null ? Color.Lerp(T.Category(level.category), T.surfaceLow, 0.3f) : T.surfaceHigh;
                LoadThumb(i, id);
            }
            if (highlightSlot >= 0 && highlightSlot < cells.Length)
                UITween.Get(cells[highlightSlot]).Scale(1.9f, 1f).Play(0.5f, EaseType.OutBack, 0.35f);
        }

        void LoadThumb(int i, string id)
        {
            if (Thumbnails.TryGet(id, out var tex)) { ApplyThumb(i, id, tex); return; }
            if (host != null && host.isActiveAndEnabled) host.StartCoroutine(Thumbnails.Load(id, t => ApplyThumb(i, id, t)));
        }

        void ApplyThumb(int i, string id, Texture2D tex)
        {
            if (tex == null || boundIds[i] != id) return;
            thumbs[i].texture = tex;
            thumbs[i].color = Color.white;
            thumbs[i].uvRect = Thumbnails.CoverRect(cells[i].rect, tex);
        }
    }

    /// <summary>The hub's album card (in the level select's retention slot): current page, progress, and the page bonus. Tap opens the album.</summary>
    public sealed class AlbumCard
    {
        public const float HeightDp = 112f;
        static UITheme T => UITheme.Current;

        public RectTransform Root { get; }
        readonly Text caps, title, reward;
        readonly AlbumStrip strip;
        readonly RectTransform fill;

        public AlbumCard(string name, Transform parent, MonoBehaviour host, float widthUnits, Action onOpen)
        {
            Root = UIFactory.Card(name, parent, T.radiusLg);
            var btn = Root.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = Root.Find("Face").GetComponent<Image>();
            btn.onClick.AddListener(Sfx.PlayClick);
            btn.onClick.AddListener(() => onOpen?.Invoke());
            Root.gameObject.AddComponent<JuicyPress>().Bind(null, 0f, 0.025f);

            float pad = T.U(14f);
            caps = UIFactory.Text("Caps", Root, "", TextRole.Caps, T.caps, T.gold, TextAnchor.UpperLeft);
            TopRow(caps.rectTransform, pad, T.U(130f), T.U(12f), T.U(18f));
            title = UIFactory.Text("Title", Root, "", TextRole.Title, T.titleXS + 1f, Color.white, TextAnchor.UpperLeft);
            TopRow(title.rectTransform, pad, T.U(130f), T.U(30f), T.U(24f));

            var chip = UIFactory.TokenChip("Reward", Root, "+" + AlbumRules.PageBonus, 28f);
            chip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(1f, 1f);
            chip.anchoredPosition = new Vector2(-pad, -T.U(14f));
            reward = UIFactory.ChipText(chip);

            var bar = UIFactory.RoundRect("Bar", Root, new Color(0, 0, 0, 0.35f), 4f);
            var brt = bar.rectTransform;
            brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f);
            brt.offsetMin = new Vector2(pad, -T.U(62f)); brt.offsetMax = new Vector2(-pad, -T.U(56f));
            var f = UIFactory.RoundRect("Fill", bar.transform, Color.white, 4f);
            fill = f.rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f); fill.offsetMin = fill.offsetMax = Vector2.zero;
            UIFactory.Gradient(f, T.gold, T.orange, GradientDirection.Horizontal);

            strip = new AlbumStrip("Strip", Root, host, widthUnits - 2f * pad, T.U(36f));
            UIFactory.Place(strip.Root, new Vector2(0.5f, 0f), strip.Root.sizeDelta, new Vector2(0f, T.U(12f)));
        }

        static void TopRow(RectTransform rt, float left, float right, float top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, -(top + height)); rt.offsetMax = new Vector2(-right, -top);
        }

        public void Refresh()
        {
            int filled = Album.Filled, page = AlbumRules.CurrentPage(filled);
            int onPage = AlbumRules.FilledOnPage(filled, page);
            caps.text = UIFactory.Track("Album · Page " + (page + 1));
            title.text = onPage == 0 && filled == 0 ? "Clear a puzzle to start" : onPage + " of " + AlbumRules.SlotsPerPage + " clips";
            reward.text = "+" + AlbumRules.PageBonus;
            fill.anchorMax = new Vector2(onPage / (float)AlbumRules.SlotsPerPage, 1f);
            strip.Bind(page);
        }
    }

    /// <summary>
    /// The album: one page at a time as a 4 x 3 grid of portrait slots holding the clips that filled them, the page's
    /// token bonus, and arrows to flip back through finished pages.
    /// </summary>
    public sealed class AlbumScreen : ModalScreen
    {
        const int Cols = 4, Rows = 3;
        const float Width = 350f;

        readonly MonoBehaviour host;
        readonly Text pageTitle, footer;
        readonly RawImage[] thumbs = new RawImage[AlbumRules.SlotsPerPage];
        readonly Image[] wells = new Image[AlbumRules.SlotsPerPage];
        readonly Text[] numbers = new Text[AlbumRules.SlotsPerPage];
        readonly Text[] titles = new Text[AlbumRules.SlotsPerPage];
        readonly RectTransform[] slots = new RectTransform[AlbumRules.SlotsPerPage];
        readonly string[] boundIds = new string[AlbumRules.SlotsPerPage];
        readonly Button prev, next;
        int page;

        public AlbumScreen(Transform canvasRoot, MonoBehaviour coroutineHost)
        {
            host = coroutineHost;
            float inner = 16f, gap = 8f;
            float cellW = (Width - 2f * inner - (Cols - 1) * gap) / Cols, cellH = cellW * 1.4f;
            float gridTop = 100f, gridH = Rows * cellH + (Rows - 1) * gap;
            float height = gridTop + gridH + 40f + 66f + 24f;
            BuildFrame(canvasRoot, "Album", "Album", T.ribbonWin, Width, height);

            pageTitle = UIFactory.Text("Page", Card, "", TextRole.Title, T.titleS, Color.white);
            UIFactory.Place(pageTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(200f), T.U(34f)), new Vector2(0f, -T.U(50f)));
            prev = UIFactory.RoundButton("Prev", Card, UISprites.Back, () => Flip(-1));
            UIFactory.Place((RectTransform)prev.transform, new Vector2(0f, 1f), ((RectTransform)prev.transform).sizeDelta, new Vector2(T.U(46f), -T.U(67f)));
            next = UIFactory.RoundButton("NextPage", Card, UISprites.Back, () => Flip(1));
            UIFactory.Place((RectTransform)next.transform, new Vector2(1f, 1f), ((RectTransform)next.transform).sizeDelta, new Vector2(-T.U(46f), -T.U(67f)));
            var nextIcon = next.GetComponent<JuicyButtonParts>().icon;
            if (nextIcon != null) nextIcon.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            for (int i = 0; i < slots.Length; i++)
            {
                int c = i % Cols, r = i / Cols;
                var well = UIFactory.RoundRect("Slot" + i, Card, T.surfaceSunk, 12f);
                var rt = well.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(T.U(cellW), T.U(cellH));
                rt.anchoredPosition = new Vector2(T.U(inner + c * (cellW + gap)), -T.U(gridTop + r * (cellH + gap)));
                UIFactory.Stroke("Hairline", well.transform, T.strokeSoft, 12f);
                numbers[i] = UIFactory.Text("Number", well.transform, (i + 1).ToString(), TextRole.Number, 18f, T.textMute);
                UIFactory.Stretch(numbers[i].rectTransform);
                var mask = UIFactory.RoundRect("Mask", well.transform, Color.white, 12f);
                UIFactory.Stretch(mask.rectTransform);
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                thumbs[i] = UIFactory.Stretch(UIFactory.Rect("Thumb", mask.transform)).gameObject.AddComponent<RawImage>();
                thumbs[i].raycastTarget = false;
                var shade = UIFactory.Image("Shade", thumbs[i].transform, Color.white);
                var srt = shade.rectTransform;
                srt.anchorMin = Vector2.zero; srt.anchorMax = new Vector2(1f, 0.45f); srt.offsetMin = srt.offsetMax = Vector2.zero;
                UIFactory.Gradient(shade, new Color(0.03f, 0.02f, 0.1f, 0f), new Color(0.03f, 0.02f, 0.1f, 0.8f));
                titles[i] = UIFactory.Text("Title", thumbs[i].transform, "", TextRole.Label, 10f, Color.white, TextAnchor.LowerLeft);
                UIFactory.Stretch(titles[i].rectTransform, T.U(5f), T.U(4f), T.U(20f), T.U(4f));
                slots[i] = rt;
                wells[i] = well;
            }

            footer = UIFactory.Text("Footer", Card, "", TextRole.Label, T.labelS, T.textDim);
            UIFactory.Place(footer.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(Width - 32f), T.U(34f)), new Vector2(0f, -T.U(gridTop + gridH + 4f)));

            var close = UIFactory.JuicyButton("Close", Card, ButtonTone.Neutral, ButtonSize.Large, "Done", Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = new Vector2(0f, 0f); crt.anchorMax = new Vector2(1f, 0f); crt.pivot = new Vector2(0.5f, 0f);
            crt.sizeDelta = new Vector2(-T.U(32f), UIFactory.ButtonHeight(ButtonSize.Large));
            crt.anchoredPosition = new Vector2(0f, T.U(20f));
        }

        public static LevelInfo FindLevel(string id)
        {
            var levels = LevelCatalog.Levels;
            for (int i = 0; i < levels.Count; i++)
                if (levels[i].id == id) return levels[i];
            return null;
        }

        public override void Show()
        {
            page = Album.CurrentPage;
            Bind();
            base.Show();
        }

        void Flip(int delta)
        {
            int p = Mathf.Clamp(page + delta, 0, Album.CurrentPage);
            if (p == page) return;
            page = p;
            Bind();
            UITween.Get(Card).Scale(0.96f, 1f).Play(0.25f, EaseType.OutBack);
        }

        void Bind()
        {
            int filled = Album.Filled, onPage = AlbumRules.FilledOnPage(filled, page);
            pageTitle.text = "Page " + (page + 1);
            prev.gameObject.SetActive(page > 0);
            next.gameObject.SetActive(page < Album.CurrentPage);
            footer.text = onPage >= AlbumRules.SlotsPerPage
                ? "Page complete! It paid " + AlbumRules.PageBonus + " tokens."
                : onPage + " of " + AlbumRules.SlotsPerPage + " · fill the page for " + AlbumRules.PageBonus + " tokens";
            footer.color = onPage >= AlbumRules.SlotsPerPage ? T.gold : T.textDim;

            for (int i = 0; i < slots.Length; i++)
            {
                string id = Album.At(page, i);
                boundIds[i] = id;
                bool isFilled = id != null;
                thumbs[i].gameObject.SetActive(isFilled);
                numbers[i].gameObject.SetActive(!isFilled);
                wells[i].color = isFilled ? T.surfaceLow : T.surfaceSunk;
                if (!isFilled) continue;
                var level = FindLevel(id);
                titles[i].text = level != null ? level.DisplayTitle : "";
                thumbs[i].texture = null;
                thumbs[i].color = level != null ? Color.Lerp(T.Category(level.category), T.surfaceLow, 0.3f) : T.surfaceHigh;
                int slot = i;
                if (Thumbnails.TryGet(id, out var tex)) ApplyThumb(slot, id, tex);
                else if (host != null && host.isActiveAndEnabled) host.StartCoroutine(Thumbnails.Load(id, t => ApplyThumb(slot, id, t)));
            }
        }

        void ApplyThumb(int i, string id, Texture2D tex)
        {
            if (tex == null || boundIds[i] != id) return;
            thumbs[i].texture = tex;
            thumbs[i].color = Color.white;
            thumbs[i].uvRect = Thumbnails.CoverRect(slots[i].rect, tex);
        }
    }
}
