using System;
using System.Collections;
using Snaploop.Core;
using Snaploop.Levels;
using Snaploop.Puzzle;
using Snaploop.UI;
using Snaploop.Video;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Snaploop.App
{
    /// <summary>
    /// Composition root and screen flow. Lives on the single GameObject in Main.unity (and is
    /// auto-created if missing). Everything else, including the UI, is built from code.
    /// Flow: Splash -> (first launch: level 1 + onboarding) | Level select -> Puzzle -> Reveal -> Win -> Next.
    /// </summary>
    public sealed class SnaploopApp : MonoBehaviour
    {
        public static SnaploopApp Instance { get; private set; }

        Camera cam;
        VideoFeed feed;
        PuzzleBoard board;
        Transform canvasRoot, safeRoot;

        SplashScreen splash;
        LevelSelectScreen levelSelect;
        HudScreen hud;
        WinScreen win;
        SettingsScreen settings;
        OnboardingScreen onboarding;
        DevMenuScreen devMenu;
        PackScreen packs;
        LevelPreviewScreen preview;
        AlbumScreen album;
        ToastView toast;
        Image flash;
        Text fpsText;

        int currentIndex = -1;
        PuzzleResult lastResult;
        AlbumFill lastAlbumFill;
        bool hintPending;
        bool transitioning;
        float flashT = -1f;
        float fpsTimer;
        int fpsFrames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureExists()
        {
            if (Instance == null && FindAnyObjectByType<SnaploopApp>() == null)
                new GameObject("Snaploop").AddComponent<SnaploopApp>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.Portrait;

            SetupCamera();
            SetupEventSystem();
            gameObject.AddComponent<Sfx>();

            feed = new GameObject("VideoFeed").AddComponent<VideoFeed>();
            feed.transform.SetParent(transform, false);
            board = new GameObject("PuzzleBoard").AddComponent<PuzzleBoard>();
            board.transform.SetParent(transform, false);
            board.Init(cam, feed);
            board.LevelReady += OnLevelReady;
            board.ProgressChanged += OnProgressChanged;
            board.Completed += OnCompleted;
            board.RevealFinished += OnRevealFinished;
            board.PieceLanded += OnPieceLanded;
            GameEvents.RevealStarted += OnRevealStarted;
            GameServices.HintOfferChanged += RefreshHintOffer;
            Tokens.Changed += RefreshHintOffer;
            GameServices.AddDevMenuEntry(
                () => "Movement: " + (GameSettings.DevFreeDrag ? "free drag" : "grid swap") + " (tap to switch, from next level)",
                () => GameSettings.DevFreeDrag = !GameSettings.DevFreeDrag);

            BuildUI();
        }

        void OnDestroy()
        {
            GameEvents.RevealStarted -= OnRevealStarted;
            GameServices.HintOfferChanged -= RefreshHintOffer;
            Tokens.Changed -= RefreshHintOffer;
            if (Instance == this) Instance = null;
        }

        IEnumerator Start()
        {
            splash.Show();
            float t0 = Time.realtimeSinceStartup;
            yield return LevelCatalog.Load(null);
            yield return PackCatalog.Load();
            levelSelect.Build(LevelCatalog.Levels, LevelCatalog.LoadError);
            while (Time.realtimeSinceStartup - t0 < 0.9f) yield return null;

            var starter = PackCatalog.Starter;
            if (!GameSettings.OnboardingSeen && starter != null && !starter.IsEmpty)
            {
                PlayLevel(starter.Levels[0].Index);
                OpenModal(onboarding);
            }
            else
            {
                ShowLevelSelect();
            }
            splash.FadeOut();
        }

        // ---------------------------------------------------------------- setup

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Background;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            GameContext.BoardCamera = cam;
            Backdrop.Create(cam).transform.SetParent(transform, true);
        }

        void SetupEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.layer = 5;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UILayout.ReferenceWidth, UILayout.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasRoot = canvasGo.transform;

            var safe = UIFactory.Rect("SafeArea", canvasRoot);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            safeRoot = safe;

            levelSelect = new LevelSelectScreen(safeRoot, this, OnLevelCardTapped, () => OpenModal(settings), ShowPacks,
                level => { preview.ShowFor(level); OpenModal(preview); });
            levelSelect.DevMenuRequested += () => OpenModal(devMenu);
            levelSelect.OpenAlbum += () => OpenModal(album);
            // Debug builds also get a Dev button in the HUD, where "Win now" applies to the running puzzle.
            hud = new HudScreen(safeRoot, OnBackToLevels, () => OpenModal(settings), OnHintClicked,
                Debug.isDebugBuild ? () => OpenModal(devMenu) : (Action)null);
            win = new WinScreen(safeRoot, this, OnNextClicked, OnBackToLevels, r => GameServices.Share?.Share(r));
            packs = new PackScreen(safeRoot, this, ShowLevelSelect, OpenPack);

            flash = UIFactory.Image("Flash", canvasRoot, new Color(1f, 1f, 1f, 0f));
            UIFactory.Stretch(flash.rectTransform);
            flash.gameObject.SetActive(false);

            UIFx.Create();

            fpsText = UIFactory.Label("Fps", canvasRoot, "", 30, Color.green, TextAnchor.UpperLeft);
            UIFactory.Place(fpsText.rectTransform, new Vector2(0f, 1f), new Vector2(300, 50), new Vector2(12, -8));

            settings = new SettingsScreen(canvasRoot);
            settings.Closed += OnModalClosed;
            onboarding = new OnboardingScreen(canvasRoot);
            onboarding.Closed += () => { GameSettings.OnboardingSeen = true; OnModalClosed(); };
            devMenu = new DevMenuScreen(canvasRoot, i => PlayLevel(i), DevWin, () => OpenModal(onboarding), () => levelSelect.Refresh());
            devMenu.Closed += OnModalClosed;
            preview = new LevelPreviewScreen(canvasRoot, this);
            preview.Closed += OnModalClosed;
            album = new AlbumScreen(canvasRoot, this);
            album.Closed += OnModalClosed;

            toast = new ToastView(canvasRoot);
            PackActions.Toast = msg => toast.Show(msg);
            if (Debug.isDebugBuild) AddPackDevEntries();

            splash = new SplashScreen(canvasRoot);
        }

        /// <summary>Debug builds only, so the release dev menu (wordmark taps) can't hand out tokens or paid packs.</summary>
        static void AddPackDevEntries()
        {
            GameServices.AddDevMenuEntry(() => "Tokens: " + Tokens.Balance + " (tap: +100)", () => Tokens.Add(100));
            GameServices.AddDevMenuEntry(() => "Reset tokens to 0", Tokens.DevReset);
            GameServices.AddDevMenuEntry(() =>
            {
                int filled = Album.Filled, page = AlbumRules.CurrentPage(filled);
                return "Album: page " + (page + 1) + ", " + AlbumRules.FilledOnPage(filled, page) + "/" + AlbumRules.SlotsPerPage + " (tap: fill one slot)";
            }, DevFillAlbumSlot);
            GameServices.AddDevMenuEntry(() =>
            {
                int total = PackCatalog.Packs.Count, open = PackAccess.UnlockedCount();
                return "Packs: " + open + "/" + total + " unlocked (tap: " + (open < total ? "unlock all" : "lock all") + ")";
            }, () => PackAccess.DevSetAllUnlocked(PackAccess.UnlockedCount() < PackCatalog.Packs.Count));
            GameServices.AddDevMenuEntry(() =>
            {
                int m = PackAccess.DevMonthOverride;
                return "Season month: " + (m == 0 ? "device (" + PackAccess.CurrentMonth + ")" : m.ToString()) + " (tap to change)";
            }, () =>
            {
                PackAccess.DevMonthOverride = (PackAccess.DevMonthOverride + 1) % 13;
                GameServices.NotifyPacksChanged();
            });
        }

        /// <summary>Dev: puts the next level that isn't in the album yet into it, as a first clear would (pays a finished page).</summary>
        static void DevFillAlbumSlot()
        {
            foreach (var l in LevelCatalog.Levels)
            {
                if (Album.Contains(l.id)) continue;
                Album.RecordClear(l.id);
                return;
            }
        }

        // ---------------------------------------------------------------- flow

        void ShowLevelSelect()
        {
            board.Clear();
            board.StopVideo();
            hud.Hide();
            win.Hide();
            packs.Hide();
            UIFx.Instance?.Clear();
            currentIndex = -1;
            transitioning = false;
            levelSelect.Show();
        }

        void OnLevelCardTapped(int index)
        {
            if (!PackAccess.IsLevelUnlocked(LevelCatalog.Get(index))) return;
            PlayLevel(index);
        }

        void ShowPacks()
        {
            levelSelect.Hide();
            packs.Show();
        }

        void OpenPack(PackInfo pack)
        {
            packs.Hide();
            levelSelect.Show();
            levelSelect.SelectPack(pack);
        }

        /// <summary>Starts a level. Public for tools/dev code; ignores lock state.</summary>
        public void PlayLevel(int index)
        {
            var level = LevelCatalog.Get(index);
            if (level == null) return;
            currentIndex = index;
            hintPending = false;
            transitioning = false;
            levelSelect.Hide();
            packs.Hide();
            win.Hide();
            UIFx.Instance?.Clear();
            hud.ShowFor(level);
            RefreshHintOffer();
            board.SetPlayArea(UILayout.PuzzlePlayArea());
            board.InputEnabled = !AnyModalOpen();
            board.Begin(level);
        }

        void OnBackToLevels()
        {
            if (transitioning) return;
            ShowLevelSelect();
        }

        void OnLevelReady(bool ok)
        {
            hud.SetLoading(false);
            hud.SetHintInteractable(true);
            if (!ok) hud.Toast("This clip couldn't play here. Showing a placeholder.");
        }

        void OnProgressChanged(int placed, int total) => hud.SetProgress(placed, total);

        void OnPieceLanded(Vector2 screenPos)
        {
            if (UIFx.Instance != null) UIFx.Instance.Burst(screenPos, UITheme.Current.sunsetA);
        }

        void OnHintClicked()
        {
            if (hintPending || !board.IsPlaying) return;
            hintPending = true;
            hud.SetHintInteractable(false);
            board.InputEnabled = false;
            int token = currentIndex;
            GameServices.Hints.RequestHint(granted =>
            {
                hintPending = false;
                if (token != currentIndex || !board.IsPlaying) return;
                board.InputEnabled = !AnyModalOpen();
                hud.SetHintInteractable(true);
                if (granted && !board.ApplyHint()) GameServices.Hints.Refund();
            });
        }

        void RefreshHintOffer()
        {
            if (hud == null) return;
            var gate = GameServices.Hints;
            hud.SetHintOffer(gate.ButtonLabel, gate.IsOffered, gate.IsRewardedAd, gate.TokenCost);
        }

        void OnCompleted(PuzzleResult result)
        {
            lastResult = result;
            lastAlbumFill = result.IsFirstClear ? Album.RecordClear(result.Level.id) : default;
            hud.SetHintInteractable(false);
        }

        void OnRevealStarted(LevelInfo level, int index)
        {
            hud.SetBarsVisible(false);
            flash.gameObject.SetActive(true);
            flashT = 0f;
        }

        void OnRevealFinished(PuzzleResult result)
        {
            hud.Hide();
            win.ShowResult(result, PackAccess.NextInPack(result.Level), lastAlbumFill);
        }

        void OnNextClicked()
        {
            if (transitioning) return;
            transitioning = true;
            int from = currentIndex;
            var nextLevel = PackAccess.NextInPack(LevelCatalog.Get(currentIndex));
            int next = nextLevel != null && PackAccess.IsLevelUnlocked(nextLevel) ? nextLevel.Index : -1;
            GameEvents.RaiseNextRequested(from, next);

            bool proceeded = false;
            GameServices.BetweenPuzzles.BeforeNextPuzzle(lastResult, () =>
            {
                if (proceeded) return;
                proceeded = true;
                transitioning = false;
                if (next >= 0) PlayLevel(next);
                else ShowLevelSelect();
            });
        }

        void DevWin()
        {
            if (board.IsPlaying) board.ForceWin();
            else if (currentIndex < 0 && LevelCatalog.Levels.Count > 0) hud.Toast("Start a level first.");
        }

        // ---------------------------------------------------------------- modals

        void OpenModal(ModalScreen modal)
        {
            modal.Root.SetAsLastSibling();
            splash.Root.SetAsLastSibling();
            modal.Show();
            board.InputEnabled = false;
        }

        void OnModalClosed()
        {
            board.InputEnabled = !AnyModalOpen() && !hintPending;
            levelSelect.Refresh();
        }

        bool AnyModalOpen() =>
            (settings != null && settings.IsVisible) ||
            (onboarding != null && onboarding.IsVisible) ||
            (devMenu != null && devMenu.IsVisible) ||
            (preview != null && preview.IsVisible) ||
            (album != null && album.IsVisible);

        // ---------------------------------------------------------------- per-frame

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            splash.Tick(dt);
            hud.Tick(dt);
            win.Tick(dt);
            onboarding.Tick(dt);
            toast.Tick(dt);

            if (flashT >= 0f)
            {
                flashT += dt;
                float a = 0.75f * (1f - Mathf.Clamp01(flashT / 0.45f));
                flash.color = new Color(1f, 1f, 1f, a);
                if (flashT >= 0.45f) { flashT = -1f; flash.gameObject.SetActive(false); }
            }

            UpdateFps(dt);

            if (Input.GetKeyDown(KeyCode.Escape)) OnAndroidBack();
        }

        void UpdateFps(float dt)
        {
            if (!DevMenuScreen.ShowFps)
            {
                if (fpsText.text.Length > 0) fpsText.text = "";
                return;
            }
            fpsFrames++;
            fpsTimer += dt;
            if (fpsTimer >= 0.5f)
            {
                fpsText.text = Mathf.RoundToInt(fpsFrames / fpsTimer) + " fps";
                fpsFrames = 0;
                fpsTimer = 0f;
            }
        }

        void OnAndroidBack()
        {
            if (album.IsVisible) album.Close();
            else if (preview.IsVisible) preview.Close();
            else if (devMenu.IsVisible) devMenu.Close();
            else if (settings.IsVisible) settings.Close();
            else if (onboarding.IsVisible) onboarding.Close();
            else if (hud.IsVisible || win.IsVisible) OnBackToLevels();
            else if (packs.IsVisible) ShowLevelSelect();
            else MoveTaskToBack();
        }

        static void MoveTaskToBack()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    activity.Call<bool>("moveTaskToBack", true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("moveTaskToBack failed: " + e.Message);
            }
#endif
        }
    }
}
