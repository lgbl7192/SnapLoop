using System;
using System.IO;
using Snaploop.Core;
using Snaploop.Levels;
using Snaploop.UI;
using UnityEngine;

namespace Snaploop.Capture
{
    /// <summary>
    /// One clip export: prepare the video source and encoder, render every frame of the replay
    /// offscreen, read it back, encode, close the file. Driven by <see cref="Tick"/> from the capture
    /// host; any exception there fails the job and frees everything without touching the game.
    /// </summary>
    sealed class ClipJob : IDisposable
    {
        public enum Phase { Preparing, Rendering, Draining, Finishing, Done, Failed, Cancelled }

        public const int Width = 720, Height = 1280;
        const int Bitrate = 6_000_000;
        const int MaxFramesPerTick = 2;
        const int MaxReadbackErrors = 10;
        const float PrepareTimeoutSec = 10f;
        const float FinishTimeoutSec = 20f;

        public readonly PuzzleResult Result;
        public Phase State { get; private set; } = Phase.Preparing;
        public bool IsRunning => State < Phase.Done;
        public float Progress { get; private set; }
        public string OutputPath { get; private set; }
        public string Error { get; private set; } = "";

        IClipSink sink;
        ClipVideoSource source;
        ClipScene scene;
        RenderTexture target;
        FrameReadback readback;
        int frame;
        float phaseTime;
        string sourceNote = "";
        readonly float startedAt;

        public ClipJob(PuzzleResult result, Transform host)
        {
            Result = result;
            startedAt = Time.realtimeSinceStartup;
            var level = result.Level;
            source = new ClipVideoSource(host, LevelCatalog.GetLevelFilePath(level.file));
            try
            {
                sink = CreateSink(level);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        static IClipSink CreateSink(LevelInfo level)
        {
            string id = SafeName(level.id);
#if UNITY_ANDROID && !UNITY_EDITOR
            string dir = Path.Combine(Application.temporaryCachePath, "clips");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "*.mp4"))
            {
                try { File.Delete(old); } catch (IOException) { }
            }
            return new AndroidClipSink(Path.Combine(dir, "snaploop-" + id + ".mp4"), Width, Height, ClipScene.Fps, Bitrate);
#else
            string root = Path.GetDirectoryName(Application.dataPath) ?? ".";
            string dir = Path.Combine(root, "Recordings", "Snaploop", id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            return new PngSequenceSink(dir, Width, Height, ClipScene.Fps);
#endif
        }

        public void Tick(float dt)
        {
            if (!IsRunning) return;
            phaseTime += dt;
            source?.Tick(dt);
            switch (State)
            {
                case Phase.Preparing: TickPreparing(); break;
                case Phase.Rendering: TickRendering(); break;
                case Phase.Draining: TickDraining(); break;
                case Phase.Finishing: TickFinishing(); break;
            }
        }

        void TickPreparing()
        {
            var s = sink.State;
            if (s == SinkState.Failed) { Fail("encoder: " + sink.Error); return; }
            if (phaseTime > PrepareTimeoutSec) { Fail("encoder did not start"); return; }
            if (s != SinkState.Ready || !source.IsPrepared) return;

            int w = sink.Width, h = sink.Height;
            if (w <= 0 || h <= 0) { Fail("encoder reported no size"); return; }
            scene = new ClipScene(Result.Level, source.Aspect, w, h, UIFactory.Font);
            target = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "SnaploopClipRT",
                useMipMap = false,
                autoGenerateMips = false,
            };
            target.Create();
            readback = new FrameReadback();
            sink.FlipVertical = ProbeFlip();
            Enter(Phase.Rendering);
        }

        /// <summary>
        /// Renders a half-white probe and reads it back through the real path, so a device whose
        /// readback rows come out top-first still produces an upright clip.
        /// </summary>
        bool ProbeFlip()
        {
            var probe = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
            probe.Create();
            try
            {
                scene.RenderOrientationProbe(probe);
                var data = readback.ReadNow(probe);
                bool bottomFirst = data.Length >= 4 && data[0] > 127;
                if (!bottomFirst) Debug.Log("[Snaploop] Clip readback rows are top-first on this device; flipping.");
                return !bottomFirst;
            }
            finally
            {
                probe.Release();
                UnityEngine.Object.Destroy(probe);
            }
        }

        void TickRendering()
        {
            readback.Poll(sink);
            if (sink.State == SinkState.Failed) { Fail("encoder: " + sink.Error); return; }
            if (readback.Errors > MaxReadbackErrors) { Fail("GPU readback kept failing"); return; }

            int total = scene.FrameCount;
            for (int i = 0; i < MaxFramesPerTick && frame < total; i++)
            {
                if (sink.FreeSlots - readback.InFlight <= 0) break;
                if (!source.Seek(frame)) break;
                scene.Render(frame, source.Texture, target);
                readback.Request(target, frame * 1_000_000L / ClipScene.Fps, sink);
                frame++;
            }
            Progress = 0.95f * frame / total;
            if (frame >= total) Enter(Phase.Draining);
        }

        void TickDraining()
        {
            readback.Poll(sink);
            if (readback.InFlight > 0 && phaseTime < FinishTimeoutSec) return;
            ReleaseRendering();
            sink.Finish();
            Enter(Phase.Finishing);
        }

        void TickFinishing()
        {
            var s = sink.State;
            if (s == SinkState.Failed) { Fail("encoder: " + sink.Error); return; }
            if (s != SinkState.Done)
            {
                if (phaseTime > FinishTimeoutSec) Fail("encoder did not finish");
                return;
            }
            OutputPath = sink.OutputPath;
            Progress = 1f;
            Debug.Log("[Snaploop] Clip ready: " + OutputPath + " (" + sink.Width + "x" + sink.Height + ", " + frame + " frames, made in " +
                      (Time.realtimeSinceStartup - startedAt).ToString("0.0") + " s, video: " + sourceNote + ")");
            State = Phase.Done;
            Dispose();
        }

        void Enter(Phase p)
        {
            if (p == Phase.Draining && source != null) sourceNote = source.Note;
            State = p;
            phaseTime = 0f;
        }

        public void Fail(string reason)
        {
            if (!IsRunning) return;
            Error = reason;
            State = Phase.Failed;
            Debug.LogWarning("[Snaploop] Clip export failed: " + reason);
            Dispose();
        }

        public void Cancel()
        {
            if (!IsRunning) return;
            State = Phase.Cancelled;
            Dispose();
        }

        void ReleaseRendering()
        {
            readback?.Dispose();
            readback = null;
            scene?.Dispose();
            scene = null;
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.Destroy(target);
                target = null;
            }
            source?.Dispose();
            source = null;
        }

        public void Dispose()
        {
            ReleaseRendering();
            sink?.Dispose();
            sink = null;
        }

        static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "clip";
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '-';
            return new string(chars);
        }
    }
}
