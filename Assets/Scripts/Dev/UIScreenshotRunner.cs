#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Snaploop.Core;
using Snaploop.Levels;
using Snaploop.Monetization;
using Snaploop.Puzzle;
using Snaploop.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.Dev
{
    /// <summary>
    /// Walks the app through every screen state and saves a PNG of each, for the ui-screenshots workflow.
    /// Inert unless the player is launched with <c>-snaploop-screenshots &lt;dir&gt;</c> (optionally
    /// <c>-snaploop-size 1080x2400</c>). Development builds and the editor only.
    ///
    /// It presses the real buttons (found by GameObject name) so the shots show the normal flow, and
    /// simulates a phone cutout and gesture bar through <see cref="UILayout.SimulatedSafeArea"/>.
    /// A step whose button or screen is missing is logged and skipped; the run never blocks.
    /// </summary>
    public sealed class UIScreenshotRunner : MonoBehaviour
    {
        const string DirArg = "-snaploop-screenshots";
        const string SizeArg = "-snaploop-size";
        const float TopInset = 0.055f, BottomInset = 0.02f;
        const float WatchdogSec = 360f;

        string outDir;
        int shots;
        bool waitOk;
        readonly List<string> skipped = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            string dir = ReadArg(DirArg);
            if (string.IsNullOrEmpty(dir)) return;
            UILayout.SimulatedSafeArea = Rect.MinMaxRect(0f, BottomInset, 1f, 1f - TopInset);
            var go = new GameObject("UIScreenshotRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<UIScreenshotRunner>().outDir = dir;
            Application.logMessageReceived += ForwardExceptions;
            // CI has no audio device; FMOD's errors would otherwise pop the console over every shot.
            Debug.developerConsoleEnabled = false;
            Debug.developerConsoleVisible = false;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(outDir);
            StartCoroutine(Watchdog());
            yield return ApplySize();
            BuildPhoneChrome();

            // The splash starts fading 0.9 s after launch, so it is shot as soon as the size is applied.
            yield return Step("splash", Shot("splash"), 10f);
            yield return Step("onboarding", Onboarding(), 40f);
            yield return Step("hud", Hud(), 45f);
            yield return Step("hint", Hint(), 40f);
            yield return Step("settings", Settings(), 15f);
            yield return Step("dev-menu", HudDevMenu(), 15f);
            yield return Step("win", Win(), 60f);
            yield return Step("level-select", LevelSelect(), 30f);
            yield return Step("album", AlbumPage(), 20f);
            yield return Step("level-select-banner", Banner(), 20f);
            yield return Step("dev-menu", HubDevMenu(), 15f);
            yield return Step("store", Store(), 25f);
            yield return Step("packs", Packs(), 20f);
            Finish(0);
        }

        PuzzleBoard board;
        AdsService ads;
        bool devShot;

        /// <summary>
        /// Runs one step in its own coroutine so an exception or a stall in it costs that shot only.
        /// </summary>
        IEnumerator Step(string name, IEnumerator body, float timeout)
        {
            bool done = false;
            var co = StartCoroutine(Run(body, () => done = true));
            float end = Time.realtimeSinceStartup + timeout;
            while (!done && Time.realtimeSinceStartup < end) yield return null;
            if (done) yield break;
            if (co != null) StopCoroutine(co);
            Skip(name + " (step failed or timed out; see exceptions above)");
        }

        static IEnumerator Run(IEnumerator body, Action onDone)
        {
            yield return body;
            onDone();
        }

        IEnumerator Onboarding()
        {
            yield return WaitFor(() => FindActive("Onboarding", "Canvas") != null, 25f);
            if (!waitOk) { Skip("onboarding"); yield break; }
            yield return Wait(1.1f);
            yield return Shot("onboarding");
            Click("Go", "Onboarding");
        }

        IEnumerator Hud()
        {
            yield return WaitFor(() => (board = FindAnyObjectByType<PuzzleBoard>()) != null && board.IsPlaying, 30f);
            if (!waitOk) { Skip("hud"); yield break; }
            yield return Wait(0.8f);
            for (int i = 0; i < 3 && board.IsPlaying; i++)
            {
                board.ApplyHint();
                yield return Wait(0.12f);
            }
            yield return Wait(1.2f);
            yield return Shot("hud");
        }

        // Hint: rewarded test ad, then the hint piece flying home.
        IEnumerator Hint()
        {
            yield return WaitFor(() => FindButton("Hint", "Hud") != null, 12f);
            if (!Click("Hint", "Hud")) { Skip("hint"); yield break; }
            yield return WaitFor(() => FindActive("TestAd") != null, 4f);
            if (waitOk)
            {
                yield return Wait(1.2f);
                yield return Shot("ad-rewarded");
                yield return WaitFor(() => FindButton("Claim", "TestAd") != null, 12f);
                Click("Claim", "TestAd");
            }
            yield return Wait(0.2f);
            yield return Shot("hint");
            yield return Wait(1f);
        }

        IEnumerator Settings()
        {
            if (!Click("Settings", "Hud")) { Skip("settings"); yield break; }
            yield return Wait(0.6f);
            yield return Shot("settings");
            Click("Close", "Settings");
            yield return Wait(0.3f);
        }

        IEnumerator HudDevMenu()
        {
            if (!Click("Dev", "Hud")) yield break;
            yield return Wait(0.6f);
            yield return Shot("dev-menu");
            devShot = true;
            Click("Close", "DevMenu");
            yield return Wait(0.3f);
        }

        // Finish with hints so the win card shows real stats, and force the next interstitial.
        IEnumerator Win()
        {
            ads = MonetizationRuntime.Instance != null ? MonetizationRuntime.Instance.Ads : null;
            if (ads != null) ads.ForceNextInterstitial = true;
            if (board == null) board = FindAnyObjectByType<PuzzleBoard>();
            for (int guard = 0; guard < 80 && board != null && board.IsPlaying; guard++)
            {
                board.ApplyHint();
                yield return Wait(0.1f);
            }
            yield return WaitFor(() => FindActive("Win", "SafeArea") != null, 20f);
            if (!waitOk) { Skip("win"); yield break; }
            yield return Wait(WinScreen.HoldSec + 0.9f);
            yield return Shot("win");
            if (Click("ClaimPlain", "Win"))
            {
                yield return Wait(0.45f);
                yield return Shot("win-claim-flight");
                yield return Wait(1.2f);
                yield return Shot("win-claimed");
            }
            Click("Next", "Win");
            yield return WaitFor(() => FindActive("TestAd") != null, 6f);
            if (!waitOk) { Skip("ad-interstitial"); yield break; }
            yield return Wait(2.6f);
            yield return Shot("ad-interstitial");
            Click("Close", "TestAd");
        }

        // A few cleared levels so the hub shows stars and the next-up state.
        IEnumerator LevelSelect()
        {
            var levels = LevelCatalog.Levels;
            if (levels.Count > 3)
            {
                Progress.RecordClear(levels[1].id, 3);
                Progress.RecordClear(levels[2].id, 2);
            }
            yield return WaitFor(() => FindButton("Back", "Hud") != null, 10f);
            yield return Wait(0.5f);
            Click("Back", "Hud");
            yield return WaitFor(() => FindActive("LevelSelect", "SafeArea") != null, 10f);
            if (!waitOk) { Skip("level-select"); yield break; }
            yield return Wait(2.5f);
            yield return Shot("level-select");
        }

        // The hub's album card opens the album; a few more first clears make the page readable.
        IEnumerator AlbumPage()
        {
            var levels = LevelCatalog.Levels;
            for (int i = 0; i < levels.Count && Album.Filled < 7; i++) Album.RecordClear(levels[i].id);
            if (!Click("AlbumCard", "LevelSelect")) { Skip("album"); yield break; }
            yield return WaitFor(() => FindActive("Album", "Canvas") != null, 6f);
            if (!waitOk) { Skip("album"); yield break; }
            yield return Wait(1.2f);
            yield return Shot("album");
            Click("Close", "Album");
            yield return Wait(0.4f);
        }

        IEnumerator Banner()
        {
            if (ads == null) { Skip("level-select-banner"); yield break; }
            ads.DevSkipGrace();
            yield return WaitFor(() => FindActive("TestBanner") != null, 8f);
            if (!waitOk) { Skip("level-select-banner"); yield break; }
            yield return Wait(0.6f);
            yield return Shot("level-select-banner");
        }

        IEnumerator HubDevMenu()
        {
            if (devShot) yield break;
            if (!Click("Dev", "LevelSelect") && !Click("Dev", "Header")) { Skip("dev-menu"); yield break; }
            yield return Wait(0.6f);
            yield return Shot("dev-menu");
            Click("Close", "DevMenu");
            yield return Wait(0.3f);
        }

        // Remove Ads in Settings opens the test store.
        IEnumerator Store()
        {
            if (!Click("Settings", "LevelSelect") && !Click("Settings", "Header")) { Skip("store"); yield break; }
            yield return Wait(0.5f);
            if (ClickLabel("Remove Ads", "Settings"))
            {
                yield return WaitFor(() => FindActive("TestStore") != null, 8f);
                if (waitOk)
                {
                    yield return Wait(0.6f);
                    yield return Shot("store");
                    Click("Cancel", "TestStore");
                }
                else Skip("store");
            }
            else Skip("store");
            yield return Wait(0.5f);
            Click("Close", "Settings");
        }

        // The hub's Packs button opens the theme packs screen.
        IEnumerator Packs()
        {
            yield return Wait(0.3f);
            if (!Click("Packs", "LevelSelect")) { Skip("packs"); yield break; }
            yield return WaitFor(() => FindActive("Packs", "SafeArea") != null, 6f);
            if (!waitOk) { Skip("packs"); yield break; }
            yield return Wait(0.8f);
            yield return Shot("packs");
            Click("Back", "Packs");
            yield return Wait(0.3f);
        }

        // ------------------------------------------------------------ steps

        IEnumerator ApplySize()
        {
            string size = ReadArg(SizeArg);
            if (string.IsNullOrEmpty(size)) yield break;
            var parts = size.ToLowerInvariant().Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h)) yield break;
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
            float t0 = Time.realtimeSinceStartup;
            while ((Screen.width != w || Screen.height != h) && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Debug.Log("[Screenshots] screen " + Screen.width + "x" + Screen.height + " (wanted " + size + ")");
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            shots++;
            Debug.Log("[Screenshots] saved " + name + ".png");
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            waitOk = false;
            while (Time.realtimeSinceStartup < end)
            {
                if (condition()) { waitOk = true; yield break; }
                yield return null;
            }
        }

        IEnumerator Watchdog()
        {
            yield return Wait(WatchdogSec);
            Debug.LogError("[Screenshots] watchdog: giving up after " + WatchdogSec + " s");
            Finish(2);
        }

        static void ForwardExceptions(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception && !condition.StartsWith("[Screenshots]", StringComparison.Ordinal))
                Console.WriteLine("[Screenshots] exception: " + condition + "\n" + stackTrace);
        }

        void Skip(string shot)
        {
            skipped.Add(shot);
            Debug.LogWarning("[Screenshots] skipped " + shot);
        }

        void Finish(int code)
        {
            Debug.Log("[Screenshots] done: " + shots + " saved" + (skipped.Count > 0 ? ", skipped: " + string.Join(", ", skipped) : ""));
            Application.Quit(code);
        }

        // ------------------------------------------------------------ finding UI

        static bool HasAncestor(Transform t, string ancestor, bool directParentOnly)
        {
            if (string.IsNullOrEmpty(ancestor)) return true;
            for (var p = t.parent; p != null; p = p.parent)
            {
                if (p.name == ancestor) return true;
                if (directParentOnly) return false;
            }
            return false;
        }

        /// <summary>Active object called <paramref name="name"/> directly under <paramref name="parent"/> (any parent if null).</summary>
        static GameObject FindActive(string name, string parent = null)
        {
            foreach (var rt in FindObjectsByType<RectTransform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (rt.name == name && rt.gameObject.activeInHierarchy && HasAncestor(rt, parent, true))
                    return rt.gameObject;
            return null;
        }

        static Button FindButton(string name, string ancestor)
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (b.name == name && b.isActiveAndEnabled && b.interactable && HasAncestor(b.transform, ancestor, false))
                    return b;
            return null;
        }

        static bool Click(string name, string ancestor)
        {
            var b = FindButton(name, ancestor);
            if (b == null)
            {
                Debug.Log("[Screenshots] no button " + ancestor + "/" + name);
                return false;
            }
            return Press(b, ancestor + "/" + name);
        }

        static bool Press(Button b, string what)
        {
            try { b.onClick.Invoke(); }
            catch (Exception e) { Debug.LogError("[Screenshots] " + what + " threw: " + e); }
            return true;
        }

        static bool ClickLabel(string labelPrefix, string ancestor)
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!b.isActiveAndEnabled || !b.interactable || !HasAncestor(b.transform, ancestor, false)) continue;
                var label = b.GetComponentInChildren<Text>();
                if (label != null && label.text.StartsWith(labelPrefix, StringComparison.Ordinal))
                    return Press(b, ancestor + "/" + labelPrefix);
            }
            Debug.Log("[Screenshots] no button labelled " + labelPrefix);
            return false;
        }

        // ------------------------------------------------------------ simulated phone chrome

        /// <summary>A punch-hole camera, clock and gesture handle drawn in the simulated insets.</summary>
        void BuildPhoneChrome()
        {
            var go = new GameObject("PhoneChrome", typeof(RectTransform));
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var root = (RectTransform)go.transform;
            float w = Screen.width, h = Screen.height, top = h * TopInset, bottom = h * BottomInset;

            var hole = UIFactory.Image("Camera", root, Color.black, UIFactory.Circle);
            UIFactory.Place(hole.rectTransform, new Vector2(0.5f, 1f), new Vector2(top * 0.36f, top * 0.36f), new Vector2(0f, -top * 0.32f));
            var clock = UIFactory.Label("Clock", root, "9:41", Mathf.RoundToInt(top * 0.3f), new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(clock.rectTransform, new Vector2(0f, 1f), new Vector2(w * 0.3f, top * 0.6f), new Vector2(w * 0.06f, -top * 0.2f));
            var handle = UIFactory.Image("GestureHandle", root, new Color(1f, 1f, 1f, 0.7f), UIFactory.Circle);
            handle.type = Image.Type.Simple;
            UIFactory.Place(handle.rectTransform, new Vector2(0.5f, 0f), new Vector2(w * 0.3f, Mathf.Max(3f, bottom * 0.16f)), new Vector2(0f, bottom * 0.42f));
        }

        static string ReadArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
#endif
