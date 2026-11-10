using System;
using System.Collections;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.Video;

namespace Snaploop.Video
{
    /// <summary>
    /// The game's single video decoder. One VideoPlayer renders into one RenderTexture that every
    /// piece (and the reveal quad) samples by UV, so piece count never adds decode cost.
    /// </summary>
    public sealed class VideoFeed : MonoBehaviour
    {
        const float PrepareTimeoutSec = 10f;
        const int MaxTextureHeight = 1280;

        VideoPlayer player;
        RenderTexture rt;
        Texture2D fallback;
        Action<bool> pendingCallback;
        Coroutine timeoutRoutine;
        int loadToken;
        bool pausedByApp;

        /// <summary>Texture to sample: the RenderTexture, or a generated fallback when the clip failed.</summary>
        public Texture Texture { get; private set; }

        /// <summary>Pixel aspect (width / height) of the current clip.</summary>
        public float Aspect { get; private set; } = 9f / 16f;

        public bool UsingFallback => Texture == fallback && fallback != null;

        public string LastError { get; private set; }

        void Awake()
        {
            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.source = UnityEngine.Video.VideoSource.Url;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.skipOnDrop = true;
            player.waitForFirstFrame = true;
            player.aspectRatio = VideoAspectRatio.Stretch;
            player.prepareCompleted += OnPrepared;
            player.errorReceived += OnError;
            GameContext.Video = player;
        }

        void OnDestroy()
        {
            if (player != null)
            {
                player.prepareCompleted -= OnPrepared;
                player.errorReceived -= OnError;
            }
            if (rt != null) rt.Release();
        }

        /// <summary>Starts preparing <paramref name="level"/>'s clip. <paramref name="onReady"/> gets false if the fallback is used.</summary>
        public void Load(LevelInfo level, Action<bool> onReady)
        {
            loadToken++;
            LastError = null;
            pendingCallback = onReady;
            player.Stop();
            player.url = LevelCatalog.GetLevelFilePath(level.file);
            if (timeoutRoutine != null) StopCoroutine(timeoutRoutine);
            timeoutRoutine = StartCoroutine(PrepareTimeout(loadToken, level));
            player.Prepare();
        }

        public void Stop()
        {
            loadToken++;
            pendingCallback = null;
            player.Stop();
        }

        /// <summary>Restart from the first frame (used so the reveal shows the full uncut loop).</summary>
        public void Restart()
        {
            if (!player.isPrepared) return;
            player.time = 0;
            player.Play();
        }

        void OnPrepared(VideoPlayer vp)
        {
            int w = (int)vp.width, h = (int)vp.height;
            if (w <= 0 || h <= 0) { Fail("Clip reports zero size."); return; }
            Aspect = w / (float)h;

            if (h > MaxTextureHeight)
            {
                w = Mathf.RoundToInt(w * (MaxTextureHeight / (float)h));
                h = MaxTextureHeight;
            }
            EnsureRenderTexture(w, h);
            vp.targetTexture = rt;
            vp.Play();
            Texture = rt;
            GameContext.VideoTexture = rt;
            Complete(true);
        }

        void OnError(VideoPlayer vp, string message) => Fail(message);

        IEnumerator PrepareTimeout(int token, LevelInfo level)
        {
            yield return new WaitForSecondsRealtime(PrepareTimeoutSec);
            if (token == loadToken && pendingCallback != null)
                Fail("Timed out preparing " + level.file);
        }

        void Fail(string message)
        {
            if (pendingCallback == null) return;
            LastError = message;
            Debug.LogError("[Snaploop] Video failed: " + message + " (" + player.url + ")");
            player.Stop();
            if (fallback == null) fallback = BuildFallbackTexture();
            Aspect = 9f / 16f;
            Texture = fallback;
            GameContext.VideoTexture = fallback;
            Complete(false);
        }

        void Complete(bool ok)
        {
            if (timeoutRoutine != null) { StopCoroutine(timeoutRoutine); timeoutRoutine = null; }
            var cb = pendingCallback;
            pendingCallback = null;
            cb?.Invoke(ok);
        }

        void EnsureRenderTexture(int w, int h)
        {
            if (rt != null && rt.width == w && rt.height == h) return;
            if (rt != null) { rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "SnaploopVideoRT",
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            rt.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = prev;
        }

        void OnApplicationPause(bool paused)
        {
            if (player == null) return;
            if (paused && player.isPlaying) { player.Pause(); pausedByApp = true; }
            else if (!paused && pausedByApp) { player.Play(); pausedByApp = false; }
        }

        static Texture2D BuildFallbackTexture()
        {
            const int w = 90, h = 160;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "SnaploopFallback", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                bool check = ((x / 15) + (y / 15)) % 2 == 0;
                var c = Color.Lerp(new Color(0.95f, 0.45f, 0.3f), new Color(0.2f, 0.3f, 0.75f), v);
                c = Color.Lerp(c, new Color(0.1f, 0.8f, 0.7f), u * 0.4f);
                if (check) c *= 0.85f;
                c.a = 1f;
                px[y * w + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
