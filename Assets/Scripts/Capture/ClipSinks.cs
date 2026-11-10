using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Snaploop.Capture
{
    enum SinkState { Preparing, Ready, Finishing, Done, Failed }

    /// <summary>Where rendered frames go: the Android MP4 encoder, or a PNG sequence in the Editor.</summary>
    interface IClipSink : IDisposable
    {
        SinkState State { get; }
        /// <summary>Actual output size; valid once Ready (the encoder may pick a smaller size).</summary>
        int Width { get; }
        int Height { get; }
        /// <summary>Frames that can be submitted now without waiting.</summary>
        int FreeSlots { get; }
        string Error { get; }
        /// <summary>The MP4 file (Android) or frame folder (Editor) once Done.</summary>
        string OutputPath { get; }
        /// <summary>True when submitted rows run top to bottom and must be flipped.</summary>
        bool FlipVertical { set; }
        void Submit(NativeArray<byte> rgba, long ptsUs);
        void Finish();
    }

    /// <summary>
    /// Bridge to com.snaploop.capture.ClipEncoder (Assets/Plugins/Android/SnaploopCapture).
    /// Hot calls go through raw JNI with cached method IDs; frame bytes are handed over as a direct
    /// ByteBuffer that wraps the readback memory, and Java copies them into its own pool.
    /// </summary>
    sealed class AndroidClipSink : IClipSink
    {
        const string EncoderClass = "com.snaploop.capture.ClipEncoder";

        readonly AndroidJavaObject encoder;
        readonly IntPtr obj, mSubmit, mFreeSlots, mState;
        readonly jvalue[] submitArgs = new jvalue[2];
        static readonly jvalue[] NoArgs = new jvalue[0];
        int width, height;
        string error = "";
        bool finished, disposed;

        public AndroidClipSink(string path, int width, int height, int fps, int bitrate)
        {
            encoder = new AndroidJavaObject(EncoderClass, path, width, height, fps, bitrate);
            obj = encoder.GetRawObject();
            IntPtr cls = encoder.GetRawClass();
            mSubmit = AndroidJNIHelper.GetMethodID(cls, "submitFrame", "(Ljava/nio/ByteBuffer;J)I");
            mFreeSlots = AndroidJNIHelper.GetMethodID(cls, "freeSlots", "()I");
            mState = AndroidJNIHelper.GetMethodID(cls, "getState", "()I");
            OutputPath = path;
            encoder.Call("start");
        }

        public static bool IsPluginPresent()
        {
            try
            {
                using (new AndroidJavaClass(EncoderClass)) return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Snaploop] Clip encoder plugin not found: " + e.Message);
                return false;
            }
        }

        public SinkState State
        {
            get
            {
                if (disposed) return SinkState.Failed;
                switch (CallInt(mState))
                {
                    case 0: return SinkState.Preparing;
                    case 1: return SinkState.Ready;
                    case 2: return SinkState.Finishing;
                    case 3: return SinkState.Done;
                    default: return SinkState.Failed;
                }
            }
        }

        public int Width => width > 0 ? width : (width = encoder.Call<int>("getWidth"));
        public int Height => height > 0 ? height : (height = encoder.Call<int>("getHeight"));
        public int FreeSlots => disposed ? 0 : CallInt(mFreeSlots);
        public string OutputPath { get; }

        public string Error
        {
            get
            {
                if (!disposed && string.IsNullOrEmpty(error)) error = encoder.Call<string>("getError") ?? "";
                return error;
            }
        }

        public bool FlipVertical
        {
            set { if (!disposed) encoder.Call("setFlipVertical", value); }
        }

        public void Submit(NativeArray<byte> rgba, long ptsUs)
        {
            if (disposed) return;
            IntPtr buffer = AndroidJNI.NewDirectByteBuffer(rgba);
            submitArgs[0] = new jvalue { l = buffer };
            submitArgs[1] = new jvalue { j = ptsUs };
            AndroidJNI.CallIntMethod(obj, mSubmit, submitArgs);
            AndroidJNI.DeleteLocalRef(buffer);
            ClearJavaException();
        }

        public void Finish()
        {
            if (disposed || finished) return;
            finished = true;
            encoder.Call("finish");
        }

        public void Dispose()
        {
            if (disposed) return;
            var s = State;
            if (s != SinkState.Done && s != SinkState.Failed) encoder.Call("cancel");
            disposed = true;
            encoder.Dispose();
        }

        int CallInt(IntPtr method)
        {
            int v = AndroidJNI.CallIntMethod(obj, method, NoArgs);
            ClearJavaException();
            return v;
        }

        static void ClearJavaException()
        {
            if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero)
            {
                AndroidJNI.ExceptionDescribe();
                AndroidJNI.ExceptionClear();
            }
        }
    }

    /// <summary>
    /// Editor fallback: writes numbered PNGs (encoded on worker threads) into Recordings/Snaploop/,
    /// which is git-ignored, and logs an ffmpeg command that turns them into the same MP4.
    /// </summary>
    sealed class PngSequenceSink : IClipSink
    {
        const int MaxPending = 4;

        readonly int fps;
        readonly List<Task> pending = new List<Task>();
        int index;
        bool flip, finishing, failed;

        public PngSequenceSink(string folder, int width, int height, int fps)
        {
            Width = width;
            Height = height;
            this.fps = fps;
            OutputPath = folder;
            Directory.CreateDirectory(folder);
        }

        public SinkState State
        {
            get
            {
                pending.RemoveAll(t => t.IsCompleted && !t.IsFaulted);
                if (failed || pending.Exists(t => t.IsFaulted)) { failed = true; return SinkState.Failed; }
                if (!finishing) return SinkState.Ready;
                if (pending.Count > 0) return SinkState.Finishing;
                return SinkState.Done;
            }
        }

        public int Width { get; }
        public int Height { get; }
        public int FreeSlots => State == SinkState.Ready ? Math.Max(0, MaxPending - pending.Count) : 0;
        public string Error { get; private set; } = "";
        public string OutputPath { get; }
        public bool FlipVertical { set => flip = value; }

        public void Submit(NativeArray<byte> rgba, long ptsUs)
        {
            int rowBytes = Width * 4;
            int size = rowBytes * Height;
            if (rgba.Length < size) return;
            var bytes = new byte[size];
            NativeArray<byte>.Copy(rgba, bytes, size);
            string file = Path.Combine(OutputPath, "frame_" + index.ToString("0000") + ".png");
            index++;
            int w = Width, h = Height;
            bool flipRows = flip;
            pending.Add(Task.Run(() =>
            {
                try
                {
                    byte[] src = bytes;
                    if (flipRows)
                    {
                        src = new byte[size];
                        for (int y = 0; y < h; y++) Buffer.BlockCopy(bytes, y * rowBytes, src, (h - 1 - y) * rowBytes, rowBytes);
                    }
                    byte[] png = ImageConversion.EncodeArrayToPNG(src, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h);
                    File.WriteAllBytes(file, png);
                }
                catch (Exception e)
                {
                    Error = e.Message;
                    throw;
                }
            }));
        }

        public void Finish()
        {
            if (finishing) return;
            finishing = true;
            Debug.Log("[Snaploop] Clip frames written to " + OutputPath + "\nMake an MP4 with:\n  ffmpeg -framerate " + fps +
                      " -i \"" + Path.Combine(OutputPath, "frame_%04d.png") + "\" -c:v libx264 -pix_fmt yuv420p -movflags +faststart snaploop-clip.mp4");
        }

        public void Dispose()
        {
            pending.Clear();
        }
    }

    /// <summary>
    /// Reads rendered frames back to the CPU in order. Uses AsyncGPUReadback where supported, so the GPU
    /// never stalls the frame; otherwise a synchronous ReadPixels (slower, still off the gameplay path).
    /// </summary>
    sealed class FrameReadback : IDisposable
    {
        struct Pending
        {
            public AsyncGPUReadbackRequest Request;
            public long PtsUs;
        }

        readonly bool useAsync = SystemInfo.supportsAsyncGPUReadback;
        readonly Queue<Pending> queue = new Queue<Pending>();
        Texture2D cpu;

        public int InFlight => queue.Count;
        public int Errors { get; private set; }

        public void Request(RenderTexture rt, long ptsUs, IClipSink sink)
        {
            if (useAsync)
            {
                queue.Enqueue(new Pending { Request = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32), PtsUs = ptsUs });
                return;
            }
            sink.Submit(ReadSync(rt), ptsUs);
        }

        /// <summary>Hands every completed readback, in submission order, to the sink.</summary>
        public void Poll(IClipSink sink)
        {
            while (queue.Count > 0 && queue.Peek().Request.done)
            {
                var p = queue.Dequeue();
                if (p.Request.hasError) { Errors++; continue; }
                sink.Submit(p.Request.GetData<byte>(), p.PtsUs);
            }
        }

        /// <summary>Blocking read through the same path as <see cref="Request"/> (used once, for the orientation probe).</summary>
        public NativeArray<byte> ReadNow(RenderTexture rt)
        {
            if (!useAsync) return ReadSync(rt);
            var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) throw new InvalidOperationException("Orientation probe readback failed");
            return req.GetData<byte>();
        }

        NativeArray<byte> ReadSync(RenderTexture rt)
        {
            if (cpu == null || cpu.width != rt.width || cpu.height != rt.height)
            {
                if (cpu != null) UnityEngine.Object.Destroy(cpu);
                cpu = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            RenderTexture.active = prev;
            return cpu.GetRawTextureData<byte>();
        }

        public void Dispose()
        {
            queue.Clear();
            if (cpu != null) UnityEngine.Object.Destroy(cpu);
        }
    }
}
