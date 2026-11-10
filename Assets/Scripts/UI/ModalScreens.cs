using System;
using System.Collections.Generic;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>Full-screen scrim with a centred card. Base for settings, onboarding and dev menu.</summary>
    public abstract class ModalScreen : UIScreen
    {
        protected RectTransform Card;
        public event Action Closed;

        protected void BuildFrame(Transform canvasRoot, string name, Vector2 cardSize)
        {
            var scrim = UIFactory.Image(name, canvasRoot, Palette.Scrim, null, raycast: true);
            Root = UIFactory.Stretch(scrim.rectTransform);
            var card = UIFactory.Panel("Card", Root, Palette.Surface);
            Card = UIFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), cardSize);
            Root.gameObject.SetActive(false);
        }

        public void Close()
        {
            if (!IsVisible) return;
            Hide();
            Closed?.Invoke();
        }
    }

    public sealed class SettingsScreen : ModalScreen
    {
        readonly Text soundLabel, hapticsLabel;
        readonly RectTransform extras;
        readonly List<Button> extraButtons = new List<Button>();

        public SettingsScreen(Transform canvasRoot)
        {
            BuildFrame(canvasRoot, "Settings", new Vector2(900, 1100));
            var title = UIFactory.Label("Title", Card, "Settings", 64, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(800, 110), new Vector2(0, -30));

            var sound = UIFactory.Button("Sound", Card, "", Palette.SurfaceHigh, () =>
            {
                GameSettings.SoundOn = !GameSettings.SoundOn;
                Refresh();
            }, 44);
            UIFactory.Place((RectTransform)sound.transform, new Vector2(0.5f, 1f), new Vector2(760, 130), new Vector2(0, -170));
            soundLabel = UIFactory.ButtonLabel(sound);

            var haptics = UIFactory.Button("Haptics", Card, "", Palette.SurfaceHigh, () =>
            {
                GameSettings.HapticsOn = !GameSettings.HapticsOn;
                if (GameSettings.HapticsOn) Haptics.Tick();
                Refresh();
            }, 44);
            UIFactory.Place((RectTransform)haptics.transform, new Vector2(0.5f, 1f), new Vector2(760, 130), new Vector2(0, -320));
            hapticsLabel = UIFactory.ButtonLabel(haptics);

            // Rows registered by follow-up systems (Remove Ads, Restore Purchases, ...).
            extras = UIFactory.Rect("Extras", Card);
            extras.anchorMin = new Vector2(0f, 1f);
            extras.anchorMax = new Vector2(1f, 1f);
            extras.pivot = new Vector2(0.5f, 1f);
            extras.sizeDelta = new Vector2(0f, 400f);
            extras.anchoredPosition = new Vector2(0f, -470f);

            var close = UIFactory.Button("Close", Card, "Done", Palette.Accent, Close, 46);
            UIFactory.Place((RectTransform)close.transform, new Vector2(0.5f, 0f), new Vector2(460, 130), new Vector2(0, 40));
        }

        public override void Show()
        {
            base.Show();
            Refresh();
        }

        void Refresh()
        {
            soundLabel.text = "Sound: " + (GameSettings.SoundOn ? "On" : "Off");
            hapticsLabel.text = "Vibration: " + (GameSettings.HapticsOn ? "On" : "Off");

            foreach (var b in extraButtons) UnityEngine.Object.Destroy(b.gameObject);
            extraButtons.Clear();
            float y = 0f;
            foreach (var entry in GameServices.SettingsEntries)
            {
                if (entry.IsVisible != null && !entry.IsVisible()) continue;
                var e = entry;
                var b = UIFactory.Button(e.Label, extras, e.Label, Palette.SurfaceHigh, () => { e.OnClick?.Invoke(); Refresh(); }, 40);
                UIFactory.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(760, 115), new Vector2(0, -y));
                extraButtons.Add(b);
                y += 135f;
            }
        }
    }

    /// <summary>One-screen first-launch onboarding, shown over the first (already scrambling) puzzle.</summary>
    public sealed class OnboardingScreen : ModalScreen
    {
        readonly RectTransform finger, ghost;
        float t;

        public OnboardingScreen(Transform canvasRoot)
        {
            BuildFrame(canvasRoot, "Onboarding", new Vector2(920, 1060));
            var title = UIFactory.Label("Title", Card, "Assemble the\nmoving picture", 70, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(860, 210), new Vector2(0, -40));

            // Tiny animated demo: a piece slides into its slot.
            var demo = UIFactory.Rect("Demo", Card);
            UIFactory.Place(demo, new Vector2(0.5f, 1f), new Vector2(600, 330), new Vector2(0, -270));
            var slot = UIFactory.Panel("Slot", demo, new Color(1f, 1f, 1f, 0.08f), raycast: false);
            UIFactory.Place(slot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(150, 150), new Vector2(-150, 0));
            var piece = UIFactory.Panel("Piece", demo, Palette.Accent, raycast: false);
            ghost = UIFactory.Place(piece.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(150, 150));
            var dot = UIFactory.Image("Finger", demo, new Color(1f, 1f, 1f, 0.85f), UIFactory.Circle);
            finger = UIFactory.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(70, 70));

            var body = UIFactory.Label("Body", Card,
                "Every piece keeps playing.\nDrag pieces into place. They snap when close.", 40, Palette.TextDim);
            UIFactory.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(820, 160), new Vector2(0, -630));

            var go = UIFactory.Button("Go", Card, "Let's go", Palette.Accent, Close, 52);
            UIFactory.Place((RectTransform)go.transform, new Vector2(0.5f, 0f), new Vector2(520, 140), new Vector2(0, 50));
        }

        public void Tick(float dt)
        {
            if (!IsVisible) return;
            t = (t + dt) % 2.2f;
            float k = Mathf.Clamp01((t - 0.4f) / 1.0f);
            k = k * k * (3f - 2f * k);
            Vector2 from = new Vector2(170, -40), to = new Vector2(-150, 0);
            Vector2 p = Vector2.Lerp(from, to, k);
            ghost.anchoredPosition = p;
            finger.anchoredPosition = p + new Vector2(30, -40);
            float s = t > 1.4f ? 1f : (k > 0f && k < 1f ? 1.06f : 1f);
            ghost.localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>
    /// Dev menu: jump to any level, force a win (for recording clips), unlock/reset progress.
    /// Open with the DEV button (development builds) or by tapping the "Snaploop" wordmark 5 times.
    /// </summary>
    public sealed class DevMenuScreen : ModalScreen
    {
        readonly RectTransform list;
        readonly Text unlockLabel, fpsLabel;
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
            BuildFrame(canvasRoot, "DevMenu", new Vector2(960, 1600));

            var title = UIFactory.Label("Title", Card, "Dev menu", 56, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(800, 90), new Vector2(0, -20));

            var win = UIFactory.Button("Win", Card, "Win now", Palette.Accent, () => { Close(); this.onWin(); }, 38);
            UIFactory.Place((RectTransform)win.transform, new Vector2(0.5f, 1f), new Vector2(420, 105), new Vector2(-220, -130));
            var unlock = UIFactory.Button("Unlock", Card, "", Palette.SurfaceHigh, () =>
            {
                Progress.UnlockAll = !Progress.UnlockAll;
                RefreshLabels();
                this.onProgressChanged();
            }, 34);
            UIFactory.Place((RectTransform)unlock.transform, new Vector2(0.5f, 1f), new Vector2(420, 105), new Vector2(220, -130));
            unlockLabel = UIFactory.ButtonLabel(unlock);

            var reset = UIFactory.Button("Reset", Card, "Reset progress", Palette.SurfaceHigh, () =>
            {
                Progress.ResetAll();
                RefreshLabels();
                this.onProgressChanged();
            }, 34);
            UIFactory.Place((RectTransform)reset.transform, new Vector2(0.5f, 1f), new Vector2(420, 105), new Vector2(-220, -250));
            var onboarding = UIFactory.Button("Onboarding", Card, "Show onboarding", Palette.SurfaceHigh, () => { Close(); this.onShowOnboarding(); }, 34);
            UIFactory.Place((RectTransform)onboarding.transform, new Vector2(0.5f, 1f), new Vector2(420, 105), new Vector2(220, -250));
            var fps = UIFactory.Button("Fps", Card, "", Palette.SurfaceHigh, () => { ShowFps = !ShowFps; RefreshLabels(); }, 34);
            UIFactory.Place((RectTransform)fps.transform, new Vector2(0.5f, 1f), new Vector2(420, 105), new Vector2(-220, -370));
            fpsLabel = UIFactory.ButtonLabel(fps);
            var close = UIFactory.Button("Close", Card, "Close", Palette.SurfaceHigh, Close, 34);
            UIFactory.Place((RectTransform)close.transform, new Vector2(0.5f, 1f), new Vector2(420, 105), new Vector2(220, -370));

            var hint = UIFactory.Label("JumpHeader", Card, "Jump to level", 36, Palette.TextDim);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(800, 60), new Vector2(0, -490));

            var viewport = UIFactory.Image("Viewport", Card, new Color(0, 0, 0, 0.25f), UIFactory.Rounded, raycast: true);
            UIFactory.Stretch(viewport.rectTransform, 30, 30, 560, 30);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            list = UIFactory.Rect("List", viewport.transform);
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12;
            layout.padding = new RectOffset(16, 16, 16, 16);
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
            var levels = LevelCatalog.Levels;
            for (int i = 0; i < levels.Count; i++)
            {
                int index = i;
                var l = levels[i];
                string label = (i + 1) + ". " + l.DisplayTitle + "  (" + l.Cols + "x" + l.Rows + ")" + (Progress.IsCleared(l.id) ? "  - cleared" : "");
                var b = UIFactory.Button("Jump" + i, list, label, Palette.SurfaceHigh, () => { Close(); onJump(index); }, 34);
                UIFactory.ButtonLabel(b).alignment = TextAnchor.MiddleLeft;
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 96;
                levelRows.Add(b.gameObject);
            }
        }

        void RefreshLabels()
        {
            unlockLabel.text = "Unlock all: " + (Progress.UnlockAll ? "On" : "Off");
            fpsLabel.text = "FPS counter: " + (ShowFps ? "On" : "Off");
        }
    }
}
