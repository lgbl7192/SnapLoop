using System;
using Snaploop.Core;
using UnityEngine;
using UnityEngine.Video;

namespace Snaploop.Capture
{
    /// <summary>
    /// Supplies video frames to the clip renderer.
    ///
    /// Preferred: a private paused VideoPlayer that is stepped one frame per output frame, so the clip
    /// is smooth however slowly the device renders and encodes. If the clip can't be prepared, the
    /// platform can't step, or steps stop arriving, it falls back to sampling the game's own decoder
    /// (<see cref="GameContext.VideoTexture"/>, still looping behind the win card) paced by real time.
    /// That costs no extra decoder; a slow device just gets repeated frames.
    /// </summary>
    sealed class ClipVideoSource : IDisposable
    {
        enum Mode { Preparing, Stepping, Live }

        const float PrepareTimeoutSec = 6f;
        const float FirstFrameTimeoutSec = 2f;
        const float StepTimeoutSec = 0.6f;
        const int MaxStepTimeouts = 3;
        const int MaxTextureHeight = 1280;

        readonly GameObject go;
        readonly VideoPlayer player;
        RenderTexture rt;
        Mode mode = Mode.Preparing;
        float prepareTime;

        long frameCount = 1;
        double frameRate = ClipScene.Fps;
        long readyFrame = -1;
        long requestedFrame = -1;
        float waitTime;
        int timeouts;
        bool firstFrameSeen;
        float liveClock;
        int liveStartFrame = -1;

        public bool IsPrepared => mode != Mode.Preparing;
        public bool IsStepping => mode == Mode.Stepping;
        public float Aspect { get; private set; } = 9f / 16f;
        public string Note { get; private set; } = "";

        public Texture Texture => mode == Mode.Stepping && rt != null ? rt : GameContext.VideoTexture;

        public ClipVideoSource(Transform parent, string url)
        {
            var live = GameContext.VideoTexture;
            if (live != null && live.height > 0) Aspect = live.width / (float)live.height;

            go = new GameObject("ClipCaptureVideo");
            go.transform.SetParent(parent, false);
            player = go.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.isLooping = true;
            player.skipOnDrop = false;
            player.waitForFirstFrame = true;
            player.aspectRatio = VideoAspectRatio.Stretch;
            player.sendFrameReadyEvents = true;
            player.prepareCompleted += OnPrepared;
            player.errorReceived += OnError;
            player.frameReady += OnFrameReady;

            if (string.IsNullOrEmpty(url)) GoLive("no clip url");
            else
            {
                player.url = url;
                player.Prepare();
            }
        }

        public void Dispose()
        {
            if (player != null)
            {
                player.prepareCompleted -= OnPrepared;
                player.errorReceived -= OnError;
                player.frameReady -= OnFrameReady;
                player.Stop();
            }
            if (go != null) UnityEngine.Object.Destroy(go);
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }

        /// <summary>Call once per Unity frame.</summary>
        public void Tick(float dt)
        {
            switch (mode)
            {
                case Mode.Preparing:
                    prepareTime += dt;
                    if (prepareTime > PrepareTimeoutSec) GoLive("prepare timed out");
                    break;
                case Mode.Stepping:
                    if (requestedFrame >= 0 && readyFrame != requestedFrame) waitTime += dt;
                    break;
                case Mode.Live:
                    liveClock += dt;
                    break;
            }
        }

        /// <summary>
        /// Asks for the video frame that belongs to output frame <paramref name="outFrame"/>. Returns true
        /// when <see cref="Texture"/> is ready to be drawn for it.
        /// </summary>
        public bool Seek(int outFrame)
        {
            if (mode == Mode.Preparing) return false;
            if (mode == Mode.Live)
            {
                if (liveStartFrame < 0)
                {
                    liveStartFrame = outFrame;
                    liveClock = 0f;
                }
                return liveClock + 1e-4f >= (outFrame - liveStartFrame) / (float)ClipScene.Fps;
            }

            long target = (long)Math.Floor(outFrame * frameRate / ClipScene.Fps + 1e-6) % frameCount;
            if (readyFrame == target)
            {
                timeouts = 0;
                return true;
            }
            if (requestedFrame != target)
            {
                requestedFrame = target;
                waitTime = 0f;
                if (readyFrame >= 0 && target == readyFrame + 1) player.StepForward();
                else player.frame = target;
                return false;
            }
            float limit = firstFrameSeen ? StepTimeoutSec : FirstFrameTimeoutSec;
            if (waitTime < limit) return false;

            // The frame never showed up: draw what we have and move on, or give up on stepping.
            if (!firstFrameSeen || ++timeouts >= MaxStepTimeouts) GoLive("frame steps stopped arriving");
            requestedFrame = -1;
            return true;
        }

        void OnPrepared(VideoPlayer vp)
        {
            if (mode != Mode.Preparing) return;
            if (!vp.canStep)
            {
                GoLive("platform can't step this clip");
                return;
            }
            int w = (int)vp.width, h = (int)vp.height;
            if (w <= 0 || h <= 0)
            {
                GoLive("clip reports zero size");
                return;
            }
            Aspect = w / (float)h;
            if (h > MaxTextureHeight)
            {
                w = Mathf.RoundToInt(w * (MaxTextureHeight / (float)h));
                h = MaxTextureHeight;
            }
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "SnaploopClipVideoRT",
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            rt.Create();
            vp.targetTexture = rt;

            frameRate = vp.frameRate > 1f ? vp.frameRate : ClipScene.Fps;
            frameCount = (long)vp.frameCount;
            if (frameCount <= 0) frameCount = (long)Math.Round(vp.length * frameRate);
            if (frameCount <= 0) frameCount = long.MaxValue;
            vp.Pause();
            mode = Mode.Stepping;
            Note = "stepping " + frameCount + " frames @ " + frameRate.ToString("0.##") + " fps";
        }

        void OnFrameReady(VideoPlayer vp, long frame)
        {
            readyFrame = frame;
            firstFrameSeen = true;
        }

        void OnError(VideoPlayer vp, string message) => GoLive("decoder error: " + message);

        void GoLive(string reason)
        {
            if (mode == Mode.Live) return;
            mode = Mode.Live;
            Note = "live game video (" + reason + ")";
            Debug.LogWarning("[Snaploop] Clip capture falls back to the live game video: " + reason);
            if (player != null)
            {
                player.sendFrameReadyEvents = false;
                player.Stop();
            }
        }
    }
}
