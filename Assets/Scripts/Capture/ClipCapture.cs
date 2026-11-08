using System;
using Snaploop.Core;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Snaploop.Capture
{
    /// <summary>
    /// Vertical share clips. After every win (when "Win clips: Auto" is on) a 720x1280 MP4 replay of the
    /// puzzle is rendered in the background while the win card is up; the win card's Share button saves it
    /// to Movies/Snaploop and opens the Android share sheet (rendering it first if needed).
    ///
    /// Stays off the gameplay path: it only listens to <see cref="GameEvents"/>, only runs between
    /// RevealFinished and the next puzzle, and is cancelled the moment a puzzle starts, Next is tapped,
    /// or the app is backgrounded. Everything is created lazily on the first export.
    /// </summary>
    public sealed class ClipCapture : IShareProvider
    {
        const string AutoPrefKey = "snaploop.clips.auto";
        const int AutoDefaultMinMemoryMb = 3000;

        static ClipCapture instance;

        bool? available;
        ClipCaptureHost host;
        ClipJob job;
        bool shareWhenReady;
        bool saving;
#if UNITY_ANDROID && !UNITY_EDITOR
        string savedClipPath, savedUri;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
#if UNITY_EDITOR || UNITY_ANDROID
            instance = new ClipCapture();
            GameServices.Share = instance;
            GameEvents.RevealFinished += instance.OnRevealFinished;
            GameEvents.PuzzleStarted += (l, i) => instance.CancelJob();
            GameEvents.NextRequested += (from, to) => instance.CancelJob();
            GameEvents.PuzzleAbandoned += (l, i) => instance.CancelJob();

            var entry = new SettingsEntry { IsVisible = () => instance.IsAvailable };
            entry.Label = AutoLabel();
            entry.OnClick = () =>
            {
                AutoCapture = !AutoCapture;
                entry.Label = AutoLabel();
            };
            GameServices.SettingsEntries.Add(entry);
#endif
        }

        /// <summary>Render a clip automatically after each win. Default on, except on low-memory devices.</summary>
        public static bool AutoCapture
        {
            get => PlayerPrefs.GetInt(AutoPrefKey, SystemInfo.systemMemorySize >= AutoDefaultMinMemoryMb ? 1 : 0) == 1;
            set { PlayerPrefs.SetInt(AutoPrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        static string AutoLabel() => AutoCapture ? "Win clips: Auto" : "Win clips: When sharing";

        // ---------------------------------------------------------------- IShareProvider

        public bool IsAvailable
        {
            get
            {
                if (available == null)
                {
#if UNITY_EDITOR
                    available = true;
#elif UNITY_ANDROID
                    available = AndroidClipSink.IsPluginPresent();
#else
                    available = false;
#endif
                }
                return available.Value;
            }
        }

        public string ButtonLabel => "Share clip";

        public void Share(PuzzleResult result)
        {
            if (!IsAvailable || result.Level == null) return;
            EnsureHost();
            if (saving) return;

            if (job != null && SameWin(job.Result, result))
            {
                if (job.State == ClipJob.Phase.Done && job.OutputPath != null)
                {
                    SaveAndShare(job.OutputPath, result);
                    return;
                }
                if (job.IsRunning)
                {
                    shareWhenReady = true;
                    host.Status.Show("Making your clip " + Percent(job.Progress), sticky: true);
                    return;
                }
            }
            StartJob(result, share: true);
        }

        // ---------------------------------------------------------------- flow

        void OnRevealFinished(PuzzleResult result)
        {
            if (AutoCapture && IsAvailable) StartJob(result, share: false);
        }

        void StartJob(PuzzleResult result, bool share)
        {
            if (GameContext.IsPuzzleInProgress) return;
            EnsureHost();
            CancelJob();
            shareWhenReady = share;
            try
            {
                job = new ClipJob(result, host.transform);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                job = null;
                host.Status.Show("Couldn't make the clip", seconds: 3f);
                return;
            }
            if (share) host.Status.Show("Making your clip 0%", sticky: true);
        }

        void CancelJob()
        {
            shareWhenReady = false;
            if (job != null && job.IsRunning) job.Cancel();
            if (host != null && !saving) host.Status.Hide();
        }

        internal void Tick(float dt)
        {
            if (job != null && job.IsRunning)
            {
                if (GameContext.IsPuzzleInProgress)
                {
                    CancelJob();
                    return;
                }
                try
                {
                    job.Tick(dt);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    job.Fail(e.Message);
                }

                if (job.IsRunning)
                {
                    host.Status.Show((shareWhenReady ? "Making your clip " : "Making clip ") + Percent(job.Progress), sticky: true, subtle: !shareWhenReady);
                }
                else if (job.State == ClipJob.Phase.Done)
                {
                    if (shareWhenReady) SaveAndShare(job.OutputPath, job.Result);
                    else host.Status.Show("Clip ready. Tap Share clip", seconds: 2.5f, subtle: true);
                    shareWhenReady = false;
                }
                else if (job.State == ClipJob.Phase.Failed)
                {
                    host.Status.Show("Couldn't make the clip", seconds: 3f);
                    shareWhenReady = false;
                }
            }
            if (saving) PollSave();
        }

        internal void OnAppPaused()
        {
            if (job != null && job.IsRunning) CancelJob();
        }

        static bool SameWin(PuzzleResult a, PuzzleResult b) =>
            a.Level != null && b.Level != null && a.Level.id == b.Level.id && a.LevelIndex == b.LevelIndex &&
            Mathf.Approximately(a.ElapsedSec, b.ElapsedSec) && a.HintsUsed == b.HintsUsed;

        static string Percent(float p) => Mathf.RoundToInt(Mathf.Clamp01(p) * 100f) + "%";

        void EnsureHost()
        {
            if (host != null) return;
            var go = new GameObject("SnaploopClipCapture");
            UnityEngine.Object.DontDestroyOnLoad(go);
            host = go.AddComponent<ClipCaptureHost>();
            host.Init(this);
        }

        // ---------------------------------------------------------------- save + share

        void SaveAndShare(string path, PuzzleResult result)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (savedClipPath == path && !string.IsNullOrEmpty(savedUri))
            {
                using (var cls = new AndroidJavaClass("com.snaploop.capture.ClipShare"))
                using (var activity = CurrentActivity())
                    cls.CallStatic<bool>("share", activity, savedUri, ChooserTitle, ShareText(result));
                host.Status.Show("Opening share…", seconds: 1.5f);
                return;
            }
            int sdk;
            using (var cls = new AndroidJavaClass("com.snaploop.capture.ClipShare")) sdk = cls.CallStatic<int>("sdkInt");
            if (sdk < 29 && !Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => StartSave(path, result);
                callbacks.PermissionDenied += p => host.Status.Show(Permission.ShouldShowRequestPermissionRationale(p)
                    ? "Allow storage access to save clips"
                    : "Storage access is off for Snaploop in Android settings", seconds: 3.5f);
                Permission.RequestUserPermission(Permission.ExternalStorageWrite, callbacks);
                return;
            }
            StartSave(path, result);
#else
            Debug.Log("[Snaploop] Editor: clip frames are in " + path + " (Android saves an MP4 to Movies/Snaploop and opens the share sheet).");
#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(path);
#endif
            host.Status.Show("Frames saved to Recordings/Snaploop", seconds: 3f);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        const string ChooserTitle = "Share your Snaploop clip";

        static string ShareText(PuzzleResult r) => "Solved \"" + r.Level.DisplayTitle + "\" in Snaploop";

        void StartSave(string path, PuzzleResult result)
        {
            string name = "Snaploop_" + result.Level.id + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4";
            using (var cls = new AndroidJavaClass("com.snaploop.capture.ClipShare"))
            using (var activity = CurrentActivity())
                cls.CallStatic("saveAndShare", activity, path, name, true, ChooserTitle, ShareText(result));
            savedClipPath = path;
            savedUri = null;
            saving = true;
            host.Status.Show("Saving to gallery…", sticky: true);
        }

        void PollSave()
        {
            using (var cls = new AndroidJavaClass("com.snaploop.capture.ClipShare"))
            {
                int status = cls.CallStatic<int>("getStatus");
                if (status == 1) return;
                saving = false;
                if (status == 2)
                {
                    savedUri = cls.CallStatic<string>("getLastUri");
                    host.Status.Show("Saved to Movies/Snaploop", seconds: 3f);
                }
                else
                {
                    Debug.LogWarning("[Snaploop] Saving clip failed: " + cls.CallStatic<string>("getLastError"));
                    savedClipPath = null;
                    host.Status.Show("Couldn't save the clip", seconds: 3f);
                }
            }
        }

        static AndroidJavaObject CurrentActivity()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return player.GetStatic<AndroidJavaObject>("currentActivity");
        }
#else
        void PollSave() => saving = false;
#endif
    }

    /// <summary>Per-frame driver and status pill for <see cref="ClipCapture"/>. Created on first use.</summary>
    sealed class ClipCaptureHost : MonoBehaviour
    {
        ClipCapture owner;
        public CaptureStatusView Status { get; private set; }

        public void Init(ClipCapture capture)
        {
            owner = capture;
            Status = new CaptureStatusView(transform);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            owner?.Tick(dt);
            Status?.Tick(dt);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) owner?.OnAppPaused();
        }
    }
}
