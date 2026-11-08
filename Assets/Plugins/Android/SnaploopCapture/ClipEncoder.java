package com.snaploop.capture;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.opengl.EGL14;
import android.opengl.EGLConfig;
import android.opengl.EGLContext;
import android.opengl.EGLDisplay;
import android.opengl.EGLExt;
import android.opengl.EGLSurface;
import android.opengl.GLES20;
import android.util.Log;
import android.util.Range;
import android.view.Surface;

import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * Encodes RGBA frames handed over by Unity into an H.264 MP4 (no audio track).
 *
 * Frames are drawn into the encoder's input Surface with a private EGL context instead of being
 * converted to YUV on the CPU. Surface input is the path every hardware AVC encoder must support,
 * and it leaves colour conversion and buffer layout to the device, which avoids the per-vendor
 * YUV420 stride/format bugs of ByteBuffer input.
 *
 * Threading: the public methods are called from Unity's main thread. Codec, EGL and muxer work
 * runs on one private thread. Unity only ever waits for a free frame buffer (bounded pool), so a
 * slow encoder slows the export down instead of growing memory.
 *
 * Expected input: tightly packed RGBA32, width*height*4 bytes, first row = bottom of the image
 * (Unity's texture convention, which is also GL's). Unity measures this per device and calls
 * {@link #setFlipVertical} if its readback rows come out the other way.
 */
public final class ClipEncoder {
    public static final int STATE_PREPARING = 0;
    public static final int STATE_READY = 1;
    public static final int STATE_FINISHING = 2;
    public static final int STATE_DONE = 3;
    public static final int STATE_FAILED = 4;
    public static final int STATE_CANCELLED = 5;

    private static final String TAG = "SnaploopClip";
    private static final String MIME = MediaFormat.MIMETYPE_VIDEO_AVC;
    private static final int POOL_SIZE = 3;
    private static final long DRAIN_TIMEOUT_US = 10_000;
    private static final int EOS_DRAIN_MAX_TRIES = 500; // x 10 ms

    private final String outputPath;
    private final int requestedWidth, requestedHeight, fps, bitrate;

    private volatile int state = STATE_PREPARING;
    private volatile String error = "";
    private volatile boolean cancelled;
    private volatile int width, height;
    private volatile int framesEncoded;
    private volatile boolean flipVertical;

    private final BlockingQueue<Frame> work = new LinkedBlockingQueue<>();
    private final BlockingQueue<ByteBuffer> pool = new ArrayBlockingQueue<>(POOL_SIZE);
    private Thread thread;

    private MediaCodec codec;
    private MediaMuxer muxer;
    private int track = -1;
    private boolean muxerStarted;
    private int samplesWritten;
    private final MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();

    private static final class Frame {
        final ByteBuffer data;
        final long ptsUs;

        Frame(ByteBuffer data, long ptsUs) {
            this.data = data;
            this.ptsUs = ptsUs;
        }
    }

    private static final Frame END = new Frame(null, -1);

    public ClipEncoder(String outputPath, int width, int height, int fps, int bitrate) {
        this.outputPath = outputPath;
        this.requestedWidth = width;
        this.requestedHeight = height;
        this.fps = fps;
        this.bitrate = bitrate;
    }

    /** Starts the encoder thread. Poll {@link #getState()} until READY before submitting frames. */
    public void start() {
        if (thread != null) return;
        thread = new Thread(this::run, "SnaploopClipEncoder");
        thread.start();
    }

    public int getState() { return state; }
    public String getError() { return error; }
    /** Encoded size; valid once READY. May be smaller than requested if the encoder can't do 720x1280. */
    public int getWidth() { return width; }
    public int getHeight() { return height; }
    public int getFramesEncoded() { return framesEncoded; }
    public String getOutputPath() { return outputPath; }

    /** Set when the submitted rows run top to bottom instead of Unity's bottom to top. */
    public void setFlipVertical(boolean flip) { flipVertical = flip; }

    /** Number of frames that can be submitted right now without waiting. */
    public int freeSlots() {
        return state == STATE_READY ? pool.size() : 0;
    }

    /**
     * Copies one frame and queues it. Returns 1 when queued, 0 when no buffer freed up in time
     * (frame dropped), -1 when the encoder is not accepting frames.
     */
    public int submitFrame(ByteBuffer rgba, long ptsUs) {
        if (state != STATE_READY || rgba == null) return -1;
        ByteBuffer dst = pool.poll();
        if (dst == null) {
            try {
                dst = pool.poll(250, TimeUnit.MILLISECONDS);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                return 0;
            }
            if (dst == null) return 0;
        }
        dst.clear();
        ByteBuffer src = rgba.duplicate();
        src.clear();
        if (src.remaining() > dst.remaining()) src.limit(dst.remaining());
        dst.put(src);
        dst.flip();
        work.offer(new Frame(dst, ptsUs));
        return 1;
    }

    /** No more frames: flush the encoder and close the file. State goes to DONE or FAILED. */
    public void finish() {
        if (state == STATE_READY) {
            state = STATE_FINISHING;
            work.offer(END);
        }
    }

    /** Stops as soon as possible and deletes the partial file. Safe to call in any state. */
    public void cancel() {
        cancelled = true;
        work.offer(END);
    }

    // ------------------------------------------------------------------ encoder thread

    private void run() {
        Surface inputSurface = null;
        Egl egl = null;
        QuadRenderer renderer = null;
        try {
            codec = MediaCodec.createEncoderByType(MIME);
            MediaCodecInfo.VideoCapabilities caps =
                codec.getCodecInfo().getCapabilitiesForType(MIME).getVideoCapabilities();
            int[] size = pickSize(caps);
            width = size[0];
            height = size[1];

            MediaFormat format = MediaFormat.createVideoFormat(MIME, width, height);
            format.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
            format.setInteger(MediaFormat.KEY_BIT_RATE, pickBitrate(caps));
            format.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
            format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
            codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            inputSurface = codec.createInputSurface();
            codec.start();

            egl = new Egl(inputSurface);
            renderer = new QuadRenderer(width, height);

            File out = new File(outputPath);
            File dir = out.getParentFile();
            if (dir != null && !dir.exists() && !dir.mkdirs()) throw new IllegalStateException("Cannot create " + dir);
            if (out.exists() && !out.delete()) throw new IllegalStateException("Cannot overwrite " + out);
            muxer = new MediaMuxer(outputPath, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);

            for (int i = 0; i < POOL_SIZE; i++)
                pool.offer(ByteBuffer.allocateDirect(width * height * 4).order(ByteOrder.nativeOrder()));
            if (!cancelled) state = STATE_READY;

            while (!cancelled) {
                Frame frame = work.take();
                if (cancelled) break;
                if (frame == END) {
                    drain(false);
                    codec.signalEndOfInputStream();
                    drain(true);
                    break;
                }
                drain(false);
                renderer.draw(frame.data, flipVertical);
                EGLExt.eglPresentationTimeANDROID(egl.display, egl.surface, frame.ptsUs * 1000L);
                if (!EGL14.eglSwapBuffers(egl.display, egl.surface))
                    throw new IllegalStateException("eglSwapBuffers failed: 0x" + Integer.toHexString(EGL14.eglGetError()));
                pool.offer(frame.data);
                framesEncoded++;
            }
        } catch (Throwable t) {
            error = t.toString();
            Log.e(TAG, "Clip encoding failed", t);
        } finally {
            boolean closed = release(renderer, egl, inputSurface);
            // Unblock a submitter that may be waiting for a buffer.
            for (int i = pool.size(); i < POOL_SIZE; i++) pool.offer(ByteBuffer.allocate(0));
            if (cancelled) {
                deleteOutput();
                state = STATE_CANCELLED;
            } else if (error.isEmpty() && closed && samplesWritten > 0) {
                state = STATE_DONE;
            } else {
                if (error.isEmpty()) error = samplesWritten == 0 ? "No frames were encoded" : "Muxer did not close cleanly";
                deleteOutput();
                state = STATE_FAILED;
            }
        }
    }

    private int[] pickSize(MediaCodecInfo.VideoCapabilities caps) {
        int[][] candidates = {
            {requestedWidth, requestedHeight},
            {576, 1024},
            {540, 960},
            {480, 848},
        };
        if (caps != null) {
            for (int[] c : candidates) {
                if (caps.areSizeAndRateSupported(c[0], c[1], fps)) return c;
            }
            for (int[] c : candidates) {
                if (caps.isSizeSupported(c[0], c[1])) return c;
            }
        }
        return candidates[0];
    }

    private int pickBitrate(MediaCodecInfo.VideoCapabilities caps) {
        long scaled = (long) bitrate * width * height / ((long) requestedWidth * requestedHeight);
        int b = (int) Math.max(1_000_000L, scaled);
        if (caps != null) {
            Range<Integer> range = caps.getBitrateRange();
            b = range.clamp(b);
        }
        return b;
    }

    private void drain(boolean endOfStream) {
        int idleTries = 0;
        while (true) {
            int index = codec.dequeueOutputBuffer(info, endOfStream ? DRAIN_TIMEOUT_US : 0);
            if (index == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!endOfStream) return;
                if (cancelled || ++idleTries > EOS_DRAIN_MAX_TRIES)
                    throw new IllegalStateException("Encoder did not signal end of stream");
            } else if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                if (muxerStarted) throw new IllegalStateException("Output format changed twice");
                track = muxer.addTrack(codec.getOutputFormat());
                muxer.start();
                muxerStarted = true;
            } else if (index >= 0) {
                idleTries = 0;
                ByteBuffer data = codec.getOutputBuffer(index);
                if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) info.size = 0;
                if (info.size > 0 && data != null && muxerStarted) {
                    data.position(info.offset);
                    data.limit(info.offset + info.size);
                    muxer.writeSampleData(track, data, info);
                    samplesWritten++;
                }
                codec.releaseOutputBuffer(index, false);
                if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) return;
            }
        }
    }

    /** Releases everything; returns true if the muxer was stopped cleanly (file is complete). */
    private boolean release(QuadRenderer renderer, Egl egl, Surface inputSurface) {
        boolean closed = false;
        try { if (renderer != null) renderer.release(); } catch (Throwable ignored) { }
        try { if (egl != null) egl.release(); } catch (Throwable ignored) { }
        try { if (codec != null) codec.stop(); } catch (Throwable ignored) { }
        try { if (codec != null) codec.release(); } catch (Throwable ignored) { }
        try { if (inputSurface != null) inputSurface.release(); } catch (Throwable ignored) { }
        if (muxer != null) {
            try {
                if (muxerStarted) {
                    muxer.stop();
                    closed = true;
                }
            } catch (Throwable t) {
                if (error.isEmpty()) error = "Muxer stop failed: " + t;
            }
            try { muxer.release(); } catch (Throwable ignored) { }
        }
        codec = null;
        muxer = null;
        return closed;
    }

    private void deleteOutput() {
        File f = new File(outputPath);
        if (f.exists() && !f.delete()) Log.w(TAG, "Could not delete partial clip " + outputPath);
    }

    // ------------------------------------------------------------------ EGL / GL

    /** Minimal EGL 1.4 setup on the encoder's input Surface, private to the encoder thread. */
    private static final class Egl {
        private static final int EGL_RECORDABLE_ANDROID = 0x3142;
        final EGLDisplay display;
        final EGLContext context;
        final EGLSurface surface;

        Egl(Surface window) {
            display = EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY);
            if (display == EGL14.EGL_NO_DISPLAY) throw new IllegalStateException("No EGL display");
            int[] version = new int[2];
            if (!EGL14.eglInitialize(display, version, 0, version, 1)) throw new IllegalStateException("eglInitialize failed");

            int[] attribs = {
                EGL14.EGL_RED_SIZE, 8,
                EGL14.EGL_GREEN_SIZE, 8,
                EGL14.EGL_BLUE_SIZE, 8,
                EGL14.EGL_RENDERABLE_TYPE, EGL14.EGL_OPENGL_ES2_BIT,
                EGL_RECORDABLE_ANDROID, 1,
                EGL14.EGL_NONE
            };
            EGLConfig[] configs = new EGLConfig[1];
            int[] count = new int[1];
            if (!EGL14.eglChooseConfig(display, attribs, 0, configs, 0, 1, count, 0) || count[0] == 0)
                throw new IllegalStateException("No recordable EGL config");

            int[] ctxAttribs = {EGL14.EGL_CONTEXT_CLIENT_VERSION, 2, EGL14.EGL_NONE};
            context = EGL14.eglCreateContext(display, configs[0], EGL14.EGL_NO_CONTEXT, ctxAttribs, 0);
            if (context == null || context == EGL14.EGL_NO_CONTEXT) throw new IllegalStateException("eglCreateContext failed");

            int[] surfAttribs = {EGL14.EGL_NONE};
            surface = EGL14.eglCreateWindowSurface(display, configs[0], window, surfAttribs, 0);
            if (surface == null || surface == EGL14.EGL_NO_SURFACE) throw new IllegalStateException("eglCreateWindowSurface failed");
            if (!EGL14.eglMakeCurrent(display, surface, surface, context)) throw new IllegalStateException("eglMakeCurrent failed");
        }

        void release() {
            EGL14.eglMakeCurrent(display, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT);
            EGL14.eglDestroySurface(display, surface);
            EGL14.eglDestroyContext(display, context);
            EGL14.eglReleaseThread();
            // No eglTerminate: the default display is shared with Unity's own GL context.
        }
    }

    /** Uploads an RGBA frame to a texture and draws it over the whole surface. */
    private static final class QuadRenderer {
        private static final String VS =
            "attribute vec2 aPos;\n" +
            "uniform float uFlip;\n" +
            "varying vec2 vUv;\n" +
            "void main() {\n" +
            "  vUv = aPos * 0.5 + 0.5;\n" +
            "  vUv.y = mix(vUv.y, 1.0 - vUv.y, uFlip);\n" +
            "  gl_Position = vec4(aPos, 0.0, 1.0);\n" +
            "}\n";
        private static final String FS =
            "precision mediump float;\n" +
            "varying vec2 vUv;\n" +
            "uniform sampler2D uTex;\n" +
            "void main() { gl_FragColor = vec4(texture2D(uTex, vUv).rgb, 1.0); }\n";

        private final int width, height, program, texture, aPos;
        private final FloatBuffer quad;

        QuadRenderer(int width, int height) {
            this.width = width;
            this.height = height;
            program = link(compile(GLES20.GL_VERTEX_SHADER, VS), compile(GLES20.GL_FRAGMENT_SHADER, FS));
            aPos = GLES20.glGetAttribLocation(program, "aPos");
            float[] v = {-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f};
            quad = ByteBuffer.allocateDirect(v.length * 4).order(ByteOrder.nativeOrder()).asFloatBuffer();
            quad.put(v).position(0);

            int[] t = new int[1];
            GLES20.glGenTextures(1, t, 0);
            texture = t[0];
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, texture);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_NEAREST);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_NEAREST);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE);
            GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE);
            GLES20.glPixelStorei(GLES20.GL_UNPACK_ALIGNMENT, 4);
            GLES20.glTexImage2D(GLES20.GL_TEXTURE_2D, 0, GLES20.GL_RGBA, width, height, 0,
                GLES20.GL_RGBA, GLES20.GL_UNSIGNED_BYTE, null);
            checkGl("texture setup");
        }

        void draw(ByteBuffer rgba, boolean flip) {
            GLES20.glViewport(0, 0, width, height);
            GLES20.glUseProgram(program);
            GLES20.glActiveTexture(GLES20.GL_TEXTURE0);
            GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, texture);
            GLES20.glTexSubImage2D(GLES20.GL_TEXTURE_2D, 0, 0, 0, width, height,
                GLES20.GL_RGBA, GLES20.GL_UNSIGNED_BYTE, rgba);
            GLES20.glUniform1i(GLES20.glGetUniformLocation(program, "uTex"), 0);
            GLES20.glUniform1f(GLES20.glGetUniformLocation(program, "uFlip"), flip ? 1f : 0f);
            GLES20.glEnableVertexAttribArray(aPos);
            GLES20.glVertexAttribPointer(aPos, 2, GLES20.GL_FLOAT, false, 0, quad);
            GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4);
            GLES20.glDisableVertexAttribArray(aPos);
            checkGl("draw");
        }

        void release() {
            GLES20.glDeleteTextures(1, new int[] {texture}, 0);
            GLES20.glDeleteProgram(program);
        }

        private static int compile(int type, String src) {
            int s = GLES20.glCreateShader(type);
            GLES20.glShaderSource(s, src);
            GLES20.glCompileShader(s);
            int[] ok = new int[1];
            GLES20.glGetShaderiv(s, GLES20.GL_COMPILE_STATUS, ok, 0);
            if (ok[0] == 0) {
                String log = GLES20.glGetShaderInfoLog(s);
                GLES20.glDeleteShader(s);
                throw new IllegalStateException("Shader compile failed: " + log);
            }
            return s;
        }

        private static int link(int vs, int fs) {
            int p = GLES20.glCreateProgram();
            GLES20.glAttachShader(p, vs);
            GLES20.glAttachShader(p, fs);
            GLES20.glLinkProgram(p);
            GLES20.glDeleteShader(vs);
            GLES20.glDeleteShader(fs);
            int[] ok = new int[1];
            GLES20.glGetProgramiv(p, GLES20.GL_LINK_STATUS, ok, 0);
            if (ok[0] == 0) {
                String log = GLES20.glGetProgramInfoLog(p);
                GLES20.glDeleteProgram(p);
                throw new IllegalStateException("Program link failed: " + log);
            }
            return p;
        }

        private static void checkGl(String what) {
            int e = GLES20.glGetError();
            if (e != GLES20.GL_NO_ERROR) throw new IllegalStateException("GL error 0x" + Integer.toHexString(e) + " in " + what);
        }
    }
}
