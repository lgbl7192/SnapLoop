package com.snaploop.capture;

import android.annotation.TargetApi;
import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.media.MediaScannerConnection;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.MediaStore;
import android.util.Log;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * Saves a finished clip to the shared Movies/Snaploop gallery folder and opens the Android
 * share sheet for it. Sharing uses the MediaStore content URI, so no FileProvider is needed.
 *
 * Android 10+ (API 29) needs no permission. Android 7-9 needs WRITE_EXTERNAL_STORAGE, which the
 * Unity side declares (maxSdkVersion 28) and requests before calling {@link #saveToGallery}.
 */
public final class ClipShare {
    private static final String TAG = "SnaploopClip";
    private static final String MIME = "video/mp4";
    private static final String ALBUM = "Snaploop";

    public static final int STATUS_IDLE = 0;
    public static final int STATUS_WORKING = 1;
    public static final int STATUS_DONE = 2;
    public static final int STATUS_FAILED = 3;

    private static volatile int status = STATUS_IDLE;
    private static volatile String lastError = "";
    private static volatile String lastUri = "";

    private ClipShare() { }

    public static int sdkInt() { return Build.VERSION.SDK_INT; }

    public static int getStatus() { return status; }
    public static String getLastError() { return lastError; }
    /** Gallery URI of the last saved clip; pass it to {@link #share} to share it again without a second copy. */
    public static String getLastUri() { return lastUri; }

    /**
     * Saves the clip to the gallery on a background thread, then (if {@code openShareSheet}) opens the
     * share sheet. Poll {@link #getStatus()} for the outcome.
     */
    public static void saveAndShare(final Activity activity, final String sourcePath, final String displayName,
                                    final boolean openShareSheet, final String chooserTitle, final String text) {
        status = STATUS_WORKING;
        lastError = "";
        lastUri = "";
        final Context app = activity.getApplicationContext();
        Thread t = new Thread(() -> {
            String uri = saveToGallery(app, sourcePath, displayName);
            if (uri.isEmpty()) {
                status = STATUS_FAILED;
                return;
            }
            lastUri = uri;
            if (openShareSheet && !share(activity, uri, chooserTitle, text)) {
                status = STATUS_FAILED;
                return;
            }
            status = STATUS_DONE;
        }, "SnaploopClipSave");
        t.start();
    }

    /** Copies the clip into the gallery. Returns the content URI as a string, or "" on failure. */
    public static String saveToGallery(Context context, String sourcePath, String displayName) {
        try {
            File src = new File(sourcePath);
            if (!src.isFile()) throw new IOException("Clip not found: " + sourcePath);
            Uri uri = Build.VERSION.SDK_INT >= 29
                ? insertScoped(context.getContentResolver(), src, displayName)
                : insertLegacy(context, src, displayName);
            return uri.toString();
        } catch (Throwable t) {
            lastError = t.toString();
            Log.e(TAG, "Saving clip to gallery failed", t);
            return "";
        }
    }

    @TargetApi(29)
    private static Uri insertScoped(ContentResolver resolver, File src, String displayName) throws IOException {
        ContentValues values = new ContentValues();
        values.put(MediaStore.MediaColumns.DISPLAY_NAME, displayName);
        values.put(MediaStore.MediaColumns.MIME_TYPE, MIME);
        values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_MOVIES + "/" + ALBUM);
        values.put(MediaStore.MediaColumns.IS_PENDING, 1);
        Uri collection = MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY);
        Uri item = resolver.insert(collection, values);
        if (item == null) throw new IOException("MediaStore insert returned null");
        try {
            try (OutputStream out = resolver.openOutputStream(item, "w");
                 InputStream in = new FileInputStream(src)) {
                if (out == null) throw new IOException("Cannot open gallery item for writing");
                copy(in, out);
            }
            values.clear();
            values.put(MediaStore.MediaColumns.IS_PENDING, 0);
            resolver.update(item, values, null, null);
            return item;
        } catch (IOException | RuntimeException e) {
            resolver.delete(item, null, null);
            throw e;
        }
    }

    @SuppressWarnings("deprecation")
    private static Uri insertLegacy(Context context, File src, String displayName) throws IOException {
        File dir = new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MOVIES), ALBUM);
        if (!dir.isDirectory() && !dir.mkdirs()) throw new IOException("Cannot create " + dir);
        File dst = new File(dir, displayName);
        try (InputStream in = new FileInputStream(src); OutputStream out = new FileOutputStream(dst)) {
            copy(in, out);
        }
        long now = System.currentTimeMillis();
        ContentValues values = new ContentValues();
        values.put(MediaStore.MediaColumns.DATA, dst.getAbsolutePath());
        values.put(MediaStore.MediaColumns.DISPLAY_NAME, displayName);
        values.put(MediaStore.MediaColumns.TITLE, displayName);
        values.put(MediaStore.MediaColumns.MIME_TYPE, MIME);
        values.put(MediaStore.MediaColumns.SIZE, dst.length());
        values.put(MediaStore.MediaColumns.DATE_ADDED, now / 1000L);
        values.put(MediaStore.Video.VideoColumns.DATE_TAKEN, now);
        Uri item = context.getContentResolver().insert(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, values);
        if (item == null) throw new IOException("MediaStore insert returned null");
        // The insert alone leaves duration and size columns empty, so galleries would show 0:00.
        MediaScannerConnection.scanFile(context, new String[] { dst.getAbsolutePath() }, new String[] { MIME }, null);
        return item;
    }

    /** Opens the system share sheet for a content URI returned by {@link #saveToGallery}. */
    public static boolean share(final Activity activity, String uriString, String chooserTitle, String text) {
        if (activity == null || uriString == null || uriString.isEmpty()) {
            lastError = "Nothing to share";
            return false;
        }
        final Uri uri = Uri.parse(uriString);
        Intent send = new Intent(Intent.ACTION_SEND);
        send.setType(MIME);
        send.putExtra(Intent.EXTRA_STREAM, uri);
        if (text != null && !text.isEmpty()) send.putExtra(Intent.EXTRA_TEXT, text);
        send.setClipData(ClipData.newRawUri("", uri));
        send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        final Intent chooser = Intent.createChooser(send, chooserTitle);
        chooser.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        activity.runOnUiThread(() -> {
            try {
                activity.startActivity(chooser);
            } catch (ActivityNotFoundException e) {
                lastError = e.toString();
                Log.e(TAG, "No app can share the clip", e);
            }
        });
        return true;
    }

    private static void copy(InputStream in, OutputStream out) throws IOException {
        byte[] buf = new byte[64 * 1024];
        int n;
        while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        out.flush();
    }
}
