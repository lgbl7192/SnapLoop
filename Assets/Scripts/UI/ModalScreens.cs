using System;
using System.Collections.Generic;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>Scrim plus a card crowned by a ribbon, inside the safe area, that pops in. Base for settings and the dev menu.</summary>
    public abstract class ModalScreen : UIScreen
    {
        protected static UITheme T => UITheme.Current;
        protected RectTransform Card;
        protected RectTransform Safe;
        public event Action Closed;

        protected void BuildFrame(Transform canvasRoot, string name, string ribbon, Color ribbonColor, float widthDp, float heightDp, float scrimAlpha = -1f)
        {
            var scrimColor = T.dialogScrim;
            if (scrimAlpha >= 0f) scrimColor.a = scrimAlpha;
            var scrim = UIFactory.Image(name, canvasRoot, scrimColor, null, raycast: true);
            Root = UIFactory.Stretch(scrim.rectTransform);
            Safe = UIFactory.Stretch(UIFactory.Rect("Safe", Root));
            Safe.gameObject.AddComponent<SafeAreaFitter>();
            if (widthDp > 0f)
            {
                Card = UIFactory.Card("Card", Safe, T.radiusLg);
                UIFactory.Place(Card, new Vector2(0.5f, 0.5f), new Vector2(T.U(widthDp), T.U(heightDp)), new Vector2(0f, -T.U(8f)));
                if (!string.IsNullOrEmpty(ribbon))
                {
                    var r = UIFactory.Ribbon("Ribbon", Card, ribbon, ribbonColor, Mathf.Min(230f, widthDp - 60f));
                    UIFactory.Place(r, new Vector2(0.5f, 1f), r.sizeDelta, new Vector2(0f, T.U(22f)));
                }
            }
            Root.gameObject.SetActive(false);
        }

        protected void SetCardHeight(float heightDp) => Card.sizeDelta = new Vector2(Card.sizeDelta.x, T.U(heightDp));

        public override void Show()
        {
            base.Show();
            UITween.Get(Root).Fade(0f, 1f).Play(T.quick, EaseType.OutCubic);
            if (Card != null) UITween.Get(Card).Scale(0.8f, 1f).Play(0.32f, EaseType.OutBack);
        }

        public void Close()
        {
            if (!IsVisible) return;
            Hide();
            Closed?.Invoke();
        }

        protected static Text Caps(Transform parent, string text, float topDp, float leftDp)
        {
            var t = UIFactory.Text("Section", parent, text, TextRole.Caps, T.caps, T.textMute, TextAnchor.MiddleLeft);
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(T.U(leftDp), -T.U(topDp + 18f)); rt.offsetMax = new Vector2(-T.U(leftDp), -T.U(topDp));
            return t;
        }

        /// <summary>Grouped list box: rows of <paramref name="rowDp"/> separated by soft dividers, laid out top-down.</summary>
        protected static RectTransform Group(Transform parent, float topDp, float sideDp)
        {
            var box = UIFactory.RoundRect("Group", parent, T.surfaceLow, T.radiusMd);
            UIFactory.Stroke("Hairline", box.transform, T.strokeSoft, T.radiusMd);
            var rt = box.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(T.U(sideDp), 0f); rt.offsetMax = new Vector2(-T.U(sideDp), 0f);
            rt.anchoredPosition = new Vector2(0f, -T.U(topDp));
            return rt;
        }

        protected static RectTransform Row(RectTransform group, string name, int index, float rowDp)
        {
            var row = UIFactory.Rect(name, group);
            row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f); row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, T.U(rowDp));
            row.anchoredPosition = new Vector2(0f, -T.U(rowDp) * index);
            if (index > 0)
            {
                var div = UIFactory.Image("Divider", row, T.strokeSoft);
                var drt = div.rectTransform;
                drt.anchorMin = new Vector2(0f, 1f); drt.anchorMax = new Vector2(1f, 1f); drt.pivot = new Vector2(0.5f, 1f);
                drt.offsetMin = new Vector2(T.U(14f), -1.5f); drt.offsetMax = new Vector2(-T.U(14f), 0f);
            }
            return row;
        }
    }

    public sealed class SettingsScreen : ModalScreen
    {
        const float Width = 350f, RowDp = 58f, ExtraRowDp = 54f;
        readonly Toggle sound, haptics;
        readonly RectTransform extrasGroup, done;
        readonly Text extrasCaps, version;
        readonly List<GameObject> extraRows = new List<GameObject>();

        public SettingsScreen(Transform canvasRoot)
        {
            BuildFrame(canvasRoot, "Settings", "Settings", T.ribbonInfo, Width, 420f);

            Caps(Card, "Game", 44f, 22f);
            var group = Group(Card, 66f, 16f);
            group.sizeDelta = new Vector2(group.sizeDelta.x, T.U(RowDp * 2f));
            sound = ToggleRow(group, "Sound", 0, UISprites.Sound, T.cyan, v => GameSettings.SoundOn = v);
            haptics = ToggleRow(group, "Vibration", 1, UISprites.Vibrate, T.orange, v =>
            {
                GameSettings.HapticsOn = v;
                if (v) Haptics.Tick();
            });

            // Rows registered by follow-up systems (Remove Ads, Restore Purchases, ...).
            extrasCaps = Caps(Card, "More", 66f + RowDp * 2f + 18f, 22f);
            extrasGroup = Group(Card, 66f + RowDp * 2f + 40f, 16f);

            var close = UIFactory.JuicyButton("Close", Card, ButtonTone.Neutral, ButtonSize.Large, "Done", Close);
            done = (RectTransform)close.transform;
            done.anchorMin = new Vector2(0f, 0f); done.anchorMax = new Vector2(1f, 0f); done.pivot = new Vector2(0.5f, 0f);
            done.sizeDelta = new Vector2(-T.U(32f), UIFactory.ButtonHeight(ButtonSize.Large));
            done.anchoredPosition = new Vector2(0f, T.U(34f));

            version = UIFactory.Text("Version", Card, "Snaploop " + Application.version, TextRole.Body, T.bodyS - 1f, T.textMute);
            UIFactory.Place(version.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(300f), T.U(20f)), new Vector2(0f, T.U(9f)));

            GameServices.SettingsChanged += () => { if (IsVisible) Refresh(); };
        }

        static Toggle ToggleRow(RectTransform group, string label, int index, Sprite icon, Color color, UnityEngine.Events.UnityAction<bool> onChanged)
        {
            var row = Row(group, label, index, RowDp);
            var badge = UIFactory.IconBadge("Badge", row, icon, color);
            UIFactory.Place(badge.rectTransform, new Vector2(0f, 0.5f), badge.rectTransform.sizeDelta, new Vector2(T.U(12f), 0f));
            var t = UIFactory.Text("Label", row, label, TextRole.Label, T.label, Color.white, TextAnchor.MiddleLeft);
            UIFactory.Stretch(t.rectTransform, T.U(62f), T.U(80f), 0, 0);
            var sw = UIFactory.Switch("Switch", row, true, onChanged);
            UIFactory.Place((RectTransform)sw.transform, new Vector2(1f, 0.5f), ((RectTransform)sw.transform).sizeDelta, new Vector2(-T.U(10f), 0f));
            return sw;
        }

        public override void Show()
        {
            Refresh();
            base.Show();
        }

        void Refresh()
        {
            sound.SetIsOnWithoutNotify(GameSettings.SoundOn);
            haptics.SetIsOnWithoutNotify(GameSettings.HapticsOn);

            foreach (var go in extraRows) UnityEngine.Object.Destroy(go);
            extraRows.Clear();
            int n = 0;
            foreach (var entry in GameServices.SettingsEntries)
            {
                if (entry.IsVisible != null && !entry.IsVisible()) continue;
                extraRows.Add(ExtraRow(entry, n++));
            }
            extrasCaps.gameObject.SetActive(n > 0);
            extrasGroup.gameObject.SetActive(n > 0);
            extrasGroup.sizeDelta = new Vector2(extrasGroup.sizeDelta.x, T.U(ExtraRowDp * n));
            float extrasDp = n > 0 ? 40f + ExtraRowDp * n : 0f;
            SetCardHeight(66f + RowDp * 2f + extrasDp + 22f + 66f + 34f + 10f);
        }

        GameObject ExtraRow(SettingsEntry entry, int index)
        {
            var e = entry;
            var row = Row(extrasGroup, e.Label, index, ExtraRowDp);
            SplitLabel(e.Label, out string title, out string value);
            bool info = e.OnClick == null;

            // The title is the row's first Text, so tools can find the row by its label.
            var t = UIFactory.Text("Label", row, title, TextRole.Label, T.label - 1f, info ? T.green : Color.white, TextAnchor.MiddleLeft);
            UIFactory.Stretch(t.rectTransform, T.U(info ? 44f : 16f), T.U(110f), 0, 0);
            if (info)
            {
                var check = UIFactory.Icon("Check", row, UISprites.Check, T.green, 22f);
                UIFactory.Place(check.rectTransform, new Vector2(0f, 0.5f), check.rectTransform.sizeDelta, new Vector2(T.U(14f), 0f));
                return row.gameObject;
            }

            if (!string.IsNullOrEmpty(value))
            {
                var v = UIFactory.Text("Value", row, value, TextRole.Label, T.labelS, T.gold, TextAnchor.MiddleRight);
                UIFactory.Stretch(v.rectTransform, T.U(150f), T.U(36f), 0, 0);
            }
            var chevron = UIFactory.Icon("Chevron", row, UISprites.Chevron, T.textMute, 18f);
            UIFactory.Place(chevron.rectTransform, new Vector2(1f, 0.5f), chevron.rectTransform.sizeDelta, new Vector2(-T.U(12f), 0f));

            var hit = row.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            var btn = row.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            btn.onClick.AddListener(Sfx.PlayClick);
            btn.onClick.AddListener(() => { e.OnClick?.Invoke(); Refresh(); });
            row.gameObject.AddComponent<JuicyPress>().Bind(null, 0f, 0.03f);
            return row.gameObject;
        }

        /// <summary>"Remove Ads - $4.99" and "Win clips: Auto" read better as a title plus a value.</summary>
        static void SplitLabel(string label, out string title, out string value)
        {
            title = label ?? "";
            value = null;
            int i = title.IndexOf(" - ", StringComparison.Ordinal);
            int sep = 3;
            if (i < 0) { i = title.IndexOf(": ", StringComparison.Ordinal); sep = 2; }
            if (i <= 0) return;
            value = title.Substring(i + sep);
            title = title.Substring(0, i);
        }
    }

    /// <summary>
    /// First-launch coach over the first (already scrambling) puzzle: one screen, "Assemble the moving picture",
    /// with a demo of a moving piece being dragged into its slot and snapping in.
    /// </summary>
    public sealed class OnboardingScreen : ModalScreen
    {
        const int Grid = 3;
        const float Loop = 2.8f;
        readonly Image[] cells = new Image[Grid * Grid];
        readonly Image piece, slotMark, ring;
        readonly RectTransform pieceRt, hand, coach, title, levelPill, go;
        readonly Vector2 slotPos, startPos;
        readonly Image handImage;
        readonly int slotIndex = 5;
        float t;

        public OnboardingScreen(Transform canvasRoot)
        {
            BuildFrame(canvasRoot, "Onboarding", null, Color.clear, 0f, 0f, scrimAlpha: 0.62f);

            var pill = UIFactory.RoundRect("LevelPill", Safe, Color.white, 15f);
            levelPill = pill.rectTransform;
            UIFactory.Place(levelPill, new Vector2(0.5f, 1f), new Vector2(T.U(96f), T.U(30f)), new Vector2(0f, -T.U(70f)));
            UIFactory.Gradient(pill, T.gold, T.orange);
            UIFactory.Ink(pill, 1.2f);
            var pillText = UIFactory.Text("Text", pill.transform, "Level 1", TextRole.Caps, T.caps, T.ink);
            UIFactory.Stretch(pillText.rectTransform);

            var titleText = UIFactory.Text("Title", Safe, "Assemble the\nmoving picture", TextRole.Title, T.titleL, Color.white);
            title = titleText.rectTransform;
            UIFactory.Place(title, new Vector2(0.5f, 1f), new Vector2(T.U(360f), T.U(96f)), new Vector2(0f, -T.U(112f)));

            // Coach card with the golden-hour border and glow.
            coach = UIFactory.Rect("Coach", Safe);
            float cw = 290f, ch = 330f;
            UIFactory.Place(coach, new Vector2(0.5f, 0.5f), new Vector2(T.U(cw), T.U(ch)), new Vector2(0f, T.U(4f)));
            var glow = UIFactory.SoftShadow("Glow", coach, 22f, 0f, 0.5f);
            glow.color = new Color(T.sunsetB.r, T.sunsetB.g, T.sunsetB.b, 0.5f);
            var face = UIFactory.RoundRect("Face", coach, Color.white, T.radiusLg);
            UIFactory.Stretch(face.rectTransform);
            UIFactory.Gradient(face, T.panelTop, T.panelBottom);
            var border = UIFactory.Stroke("Border", coach, Color.white, T.radiusLg, 1f);
            UIFactory.Gradient(border, T.sunsetA, T.sunsetC);

            float cell = T.U(54f), gap = T.U(4f);
            var board = UIFactory.Rect("Demo", coach);
            float bw = Grid * cell + (Grid - 1) * gap;
            UIFactory.Place(board, new Vector2(0.5f, 1f), new Vector2(bw, bw), new Vector2(0f, -T.U(22f)));
            var boardBg = UIFactory.RoundRect("Board", board, T.surfaceSunk, 12f);
            UIFactory.Stretch(boardBg.rectTransform, -T.U(6f), -T.U(6f), -T.U(6f), -T.U(6f));
            for (int i = 0; i < cells.Length; i++)
            {
                int x = i % Grid, y = i / Grid;
                var c = UIFactory.RoundRect("Cell" + i, board, Color.white, 7f);
                UIFactory.Place(c.rectTransform, new Vector2(0f, 1f), new Vector2(cell, cell), new Vector2(x * (cell + gap), -y * (cell + gap)));
                c.rectTransform.pivot = new Vector2(0f, 1f);
                cells[i] = c;
            }
            var slotRt = cells[slotIndex].rectTransform;
            slotPos = slotRt.anchoredPosition;
            startPos = slotPos + new Vector2(T.U(46f), -T.U(150f));
            slotMark = UIFactory.Stroke("Slot", slotRt, new Color(1f, 1f, 1f, 0.5f), 7f);
            piece = UIFactory.RoundRect("Piece", board, Color.white, 7f);
            pieceRt = piece.rectTransform;
            pieceRt.anchorMin = pieceRt.anchorMax = new Vector2(0f, 1f);
            pieceRt.pivot = new Vector2(0.5f, 0.5f);
            pieceRt.sizeDelta = new Vector2(cell, cell);
            UIFactory.Ink(piece, 1.5f);
            ring = UIFactory.Image("Ring", board, T.gold, UISprites.Ring);
            ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = new Vector2(0f, 1f);
            handImage = UIFactory.Icon("Hand", board, UISprites.Hand, Color.white, 46f);
            UIFactory.Ink(handImage, 1.6f);
            hand = handImage.rectTransform;
            hand.anchorMin = hand.anchorMax = new Vector2(0f, 1f);
            hand.pivot = new Vector2(0.42f, 0.86f);

            var body = UIFactory.Text("Body", coach, "Every tile keeps playing.\nDrag one onto its spot to swap it in.",
                TextRole.Body, T.body, T.textDim, TextAnchor.UpperCenter);
            var brt = body.rectTransform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = new Vector2(1f, 0f); brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(T.U(18f), T.U(18f)); brt.offsetMax = new Vector2(-T.U(18f), T.U(18f + 60f));

            var btn = UIFactory.JuicyButton("Go", Safe, ButtonTone.Primary, ButtonSize.Hero, "Let's go", Close, UISprites.Play);
            go = (RectTransform)btn.transform;
            go.anchorMin = new Vector2(0f, 0f); go.anchorMax = new Vector2(1f, 0f); go.pivot = new Vector2(0.5f, 0f);
            go.sizeDelta = new Vector2(-T.U(2f * T.screenPad), UIFactory.ButtonHeight(ButtonSize.Hero));
            go.anchoredPosition = new Vector2(0f, T.U(T.screenPad + 8f));
            btn.GetComponent<JuicyButtonParts>().face.gameObject.AddComponent<Pulse>();
        }

        public override void Show()
        {
            base.Show();
            t = 0f;
            UITween.Get(levelPill).Scale(0.4f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, 0.05f);
            UITween.Get(title).Scale(0.7f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, 0.12f);
            UITween.Get(coach).Scale(0.85f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, 0.2f);
            UITween.Get(go).Scale(0.8f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, 0.3f);
        }

        /// <summary>Colour of the "moving picture" at a cell, drifting through the golden-hour gradient over time.</summary>
        Color Picture(int x, int y, float time)
        {
            float k = Mathf.Repeat((x + (Grid - 1 - y)) / (2f * Grid) + time * 0.12f, 1f);
            float tri = k < 0.5f ? k * 2f : 2f - k * 2f;
            return tri < 0.5f ? Color.Lerp(T.sunsetC, T.sunsetB, tri * 2f) : Color.Lerp(T.sunsetB, T.sunsetA, tri * 2f - 1f);
        }

        public void Tick(float dt)
        {
            if (!IsVisible) return;
            t += dt;
            float phase = Mathf.Repeat(t, Loop);
            for (int i = 0; i < cells.Length; i++)
                cells[i].color = i == slotIndex ? new Color(1f, 1f, 1f, 0.06f) : Picture(i % Grid, i / Grid, t);
            piece.color = Picture(slotIndex % Grid, slotIndex / Grid, t);

            Vector2 center = slotPos + new Vector2(pieceRt.sizeDelta.x * 0.5f, -pieceRt.sizeDelta.y * 0.5f);
            Vector2 from = startPos + new Vector2(pieceRt.sizeDelta.x * 0.5f, -pieceRt.sizeDelta.y * 0.5f);
            // 0-0.3 hand presses, 0.3-1.4 drags, 1.4 snap, then hold and fade.
            float drag = Mathf.Clamp01((phase - 0.3f) / 1.1f);
            float e = Ease.Apply(EaseType.InOutCubic, drag);
            Vector2 p = Vector2.Lerp(from, center, e);
            bool snapped = phase >= 1.4f;
            pieceRt.anchoredPosition = snapped ? center : p;
            float lift = snapped ? 1f + 0.12f * Mathf.Clamp01(1f - (phase - 1.4f) / 0.18f) : (drag > 0f ? 1.08f : 1f);
            pieceRt.localScale = new Vector3(lift, lift, 1f);
            slotMark.gameObject.SetActive(!snapped);

            float ringT = (phase - 1.4f) / 0.5f;
            ring.gameObject.SetActive(ringT >= 0f && ringT < 1f);
            if (ringT >= 0f && ringT < 1f)
            {
                float s = pieceRt.sizeDelta.x * (1f + 1.2f * Ease.Apply(EaseType.OutCubic, ringT));
                ring.rectTransform.sizeDelta = new Vector2(s, s);
                ring.rectTransform.anchoredPosition = center;
                ring.color = new Color(T.gold.r, T.gold.g, T.gold.b, 1f - ringT);
            }

            float handA = phase < 1.6f ? 1f : Mathf.Clamp01(1f - (phase - 1.6f) / 0.3f);
            if (phase < 0.25f) handA = Mathf.Clamp01(phase / 0.25f);
            hand.anchoredPosition = (snapped ? center : p) + new Vector2(T.U(8f), -T.U(10f));
            float press = phase < 0.3f ? Mathf.Lerp(1.1f, 0.92f, phase / 0.3f) : 0.92f;
            hand.localScale = new Vector3(press, press, 1f);
            handImage.color = new Color(1f, 1f, 1f, handA);
        }
    }

    /// <summary>
    /// Dev menu: jump to any level, force a win (for recording clips), unlock/reset progress.
    /// Open with the DEV button (development builds) or by tapping the "Snaploop" wordmark 5 times.
    /// </summary>
    public sealed class DevMenuScreen : ModalScreen
    {
        readonly RectTransform list;
        readonly Text jumpHeader;
        readonly Button unlock, fps;
        readonly Action<int> onJump;
        readonly Action onWin;
        readonly Action onShowOnboarding;
        readonly Action onProgressChanged;
        readonly List<GameObject> levelRows = new List<GameObject>();

        public static bool ShowFps;

        public DevMenuScreen(Transform canvasRoot, Action<int> onJump, Action onWin, Action onShowOnboarding, Action onProgressChanged)
        {
            this.onJump = onJump;
            this.onWin = onWin;
            this.onShowOnboarding = onShowOnboarding;
            this.onProgressChanged = onProgressChanged;
            BuildFrame(canvasRoot, "DevMenu", "Dev menu", T.ribbonInfo, 360f, 600f);

            float bh = UIFactory.ButtonHeight(ButtonSize.Medium);
            Button Cell(string name, ButtonTone tone, string label, UnityEngine.Events.UnityAction click, int col, int row)
            {
                var b = UIFactory.JuicyButton(name, Card, tone, ButtonSize.Medium, label, click);
                UIFactory.ButtonLabel(b).fontSize = T.F(16f);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = new Vector2(col * 0.5f, 1f); rt.anchorMax = new Vector2(col * 0.5f + 0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                float inner = T.U(16f), gap = T.U(5f);
                rt.offsetMin = new Vector2(col == 0 ? inner : gap, 0f);
                rt.offsetMax = new Vector2(col == 0 ? -gap : -inner, 0f);
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, bh);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -T.U(44f) - row * (bh + T.U(8f)));
                return b;
            }

            Cell("Win", ButtonTone.Primary, "Win now", () => { Close(); this.onWin(); }, 0, 0);
            unlock = Cell("Unlock", ButtonTone.Neutral, "", () =>
            {
                Progress.UnlockAll = !Progress.UnlockAll;
                RefreshLabels();
                this.onProgressChanged();
            }, 1, 0);
            Cell("Reset", ButtonTone.Neutral, "Reset progress", () =>
            {
                Progress.ResetAll();
                RefreshLabels();
                this.onProgressChanged();
            }, 0, 1);
            Cell("Onboarding", ButtonTone.Neutral, "Onboarding", () => { Close(); this.onShowOnboarding(); }, 1, 1);
            fps = Cell("Fps", ButtonTone.Neutral, "", () => { ShowFps = !ShowFps; RefreshLabels(); }, 0, 2);
            Cell("Close", ButtonTone.Blue, "Close", Close, 1, 2);

            float listTop = 44f + 3f * (bh / T.dp + 8f) + 8f;
            jumpHeader = Caps(Card, "Jump to level", listTop, 22f);

            var viewport = UIFactory.Sunk("Viewport", Card, T.radiusMd);
            viewport.raycastTarget = true;
            UIFactory.Stretch(viewport.rectTransform, T.U(16f), T.U(16f), T.U(listTop + 24f), T.U(16f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30f;
            list = UIFactory.Rect("List", viewport.transform);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            int p = (int)T.U(8f);
            layout.spacing = T.U(6f);
            layout.padding = new RectOffset(p, p, p, p);
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = list;
            scroll.viewport = viewport.rectTransform;
        }

        public override void Show()
        {
            base.Show();
            RefreshLabels();
            foreach (var go in levelRows) UnityEngine.Object.Destroy(go);
            levelRows.Clear();
            jumpHeader.text = UIFactory.Track(GameServices.DevMenuEntries.Count > 0 ? "Tools and levels" : "Jump to level");
            foreach (var entry in GameServices.DevMenuEntries)
            {
                var e = entry;
                Button b = null;
                b = ListRow("Tool", e.Label(), ButtonTone.Blue, () =>
                {
                    e.OnClick?.Invoke();
                    UIFactory.SetButtonText(b, e.Label());
                });
                levelRows.Add(b.gameObject);
            }
            var levels = LevelCatalog.Levels;
            for (int i = 0; i < levels.Count; i++)
            {
                int index = i;
                var l = levels[i];
                string label = (i + 1) + ". " + l.DisplayTitle + "  (" + l.Cols + "x" + l.Rows + ")" +
                               (l.PackId != PackInfo.StarterId ? " · " + l.PackId : "") + (Progress.IsCleared(l.id) ? " · cleared" : "");
                var b = ListRow("Jump" + i, label, ButtonTone.Neutral, () => { Close(); onJump(index); });
                levelRows.Add(b.gameObject);
            }
        }

        Button ListRow(string name, string label, ButtonTone tone, UnityEngine.Events.UnityAction click)
        {
            var b = UIFactory.JuicyButton(name, list, tone, ButtonSize.Medium, label, click);
            var parts = b.GetComponent<JuicyButtonParts>();
            parts.label.font = UIFactory.FontFor(TextRole.Label);
            parts.label.fontSize = T.F(T.labelS);
            parts.label.alignment = TextAnchor.MiddleLeft;
            parts.label.horizontalOverflow = HorizontalWrapMode.Wrap;
            parts.label.resizeTextForBestFit = true;
            parts.label.resizeTextMinSize = T.F(T.labelS * 0.7f);
            parts.label.resizeTextMaxSize = T.F(T.labelS);
            parts.label.verticalOverflow = VerticalWrapMode.Truncate;
            parts.label.gameObject.AddComponent<LayoutElement>().preferredHeight = T.U(38f);
            parts.content.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            // Long tool labels shrink to the row instead of overflowing it.
            var texts = parts.label.transform.parent;
            texts.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            var le = texts.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth = 0f;
            b.gameObject.AddComponent<LayoutElement>().preferredHeight = T.U(46f);
            return b;
        }

        void RefreshLabels()
        {
            UIFactory.SetButtonText(unlock, "Unlock: " + (Progress.UnlockAll ? "On" : "Off"));
            UIFactory.SetButtonText(fps, "FPS: " + (ShowFps ? "On" : "Off"));
        }
    }
}
