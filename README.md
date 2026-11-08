# Snaploop

Android-first video puzzle built in Unity. Every tile is a live window onto one looping clip: drag the tiles into place, correct neighbours join into one shape, and the finished puzzle plays the full uncut loop.

- Unity **6000.0.85f2** (Unity 6 LTS), built-in render pipeline, Android only, portrait.
- Product name, Android app label and in-app splash: **Snaploop**. Package id `com.snaploop.game` is a placeholder; change it before the first store upload.
- Clips are in Git LFS: run `git lfs install` before cloning (or `git lfs pull` afterwards).

## Project layout

| Path | What it is |
| --- | --- |
| `Assets/Scenes/Main.unity` | The only scene: a camera and the `SnaploopApp` component. All UI is built in code. |
| `Assets/Scripts/App/SnaploopApp.cs` | Composition root and screen flow (splash, level select, puzzle, win, next). |
| `Assets/Scripts/Puzzle/` | `PuzzleBoard` (free drag, snap, magnetism, scramble, hint, reveal), `TileBatch` (all tiles as one mesh), `TileGroups` (which tiles are joined). `JigsawShape` and `Triangulator` are the older tab-shaped cut, kept for reuse. |
| `Assets/Scripts/Video/VideoFeed.cs` | The single `VideoPlayer` writing into one shared `RenderTexture`. |
| `Assets/Scripts/Levels/` | `levels.json` loader and the difficulty ramp. |
| `Assets/Scripts/Core/` | Events, service hooks, settings/progress, haptics, generated sound effects. |
| `Assets/Scripts/UI/` | Code-built uGUI screens (level select, HUD, win card, settings, onboarding, dev menu). |
| `Assets/Resources/Shaders/SnaploopVideoPiece.shader` | The one shader every piece uses. |
| `Assets/Resources/Shaders/SnaploopVideoTile.shader` | The puzzle's tile shader: rounded tiles, gaps and outlines drawn per pixel from edge flags, so a joined group shows one outer outline. `SnaploopVideoPiece` above is the older jigsaw-piece shader. |
| `Assets/StreamingAssets/Levels/` | Clips, optional thumbnails, and `levels.json`. |
| `Assets/Editor/Snaploop/` | Editor menu: **Snaploop > Apply Player Settings / Validate Levels / Open Main Scene**. |
| `Assets/Scripts/Capture/`, `Assets/Plugins/Android/SnaploopCapture/` | Win clip capture: offscreen replay renderer and the Android encoder/share plugin. See [Vertical clip capture](#vertical-clip-capture-win-clips). |
| `Assets/Editor/Build/`, `.github/workflows/` | Build pipeline (see [docs/BUILD.md](docs/BUILD.md)). |

## Add a video level

1. Encode the clip as **H.264 MP4, 720x1280 portrait, 30 fps, no audio, about 2-3 Mbps, 8-12 s, looping cleanly**. One way to do it with ffmpeg (1 s crossfade so the end blends into the start):

   ```bash
   D=10; X=1   # output length and crossfade, seconds
   ffmpeg -i source.mp4 -filter_complex \
     "[0:v]fps=30,scale=720:1280:force_original_aspect_ratio=increase,crop=720:1280,setsar=1,split[a][b];\
      [a]trim=$X:$((D+X)),setpts=PTS-STARTPTS[main];\
      [b]trim=0:$X,setpts=PTS-STARTPTS+$((D-X))/TB,format=yuva420p,fade=in:st=$((D-X)):d=$X:alpha=1[tail];\
      [main][tail]overlay=eof_action=pass,format=yuv420p[v]" \
     -map "[v]" -an -c:v libx264 -profile:v high -b:v 2500k -maxrate 3000k -bufsize 5000k \
     -movflags +faststart -t $D my-level.mp4
   ```

2. Copy it to `Assets/StreamingAssets/Levels/<id>.mp4`.
3. Optional: add a thumbnail `Assets/StreamingAssets/Levels/<id>.jpg` (portrait, about 360x640). Level select falls back to a colour tile without it.
4. Add an entry to `Assets/StreamingAssets/Levels/levels.json`. Array order is play order:

   ```json
   { "id": "my-level", "title": "My Level", "file": "my-level.mp4", "cols": 4, "rows": 6,
     "durationSec": 10.0, "category": "nature", "credit": "Name / Pexels",
     "sourceUrl": "https://www.pexels.com/video/...", "license": "Pexels License" }
   ```

   - `cols` x `rows` sets difficulty. Use portrait grids (rows >= cols) so pieces stay roughly square on 9:16 clips: 3x3, 3x4, 3x5 early, then 4x5, 4x6, 4x7, up to about 36 pieces (4x9 or 5x7). The maximum is 10 per side.
   - If `cols`/`rows` are missing, the game picks a size from its ramp based on the level's position.
   - `credit` is shown on the win card.
5. Run **Snaploop > Validate Levels** in the Editor. It checks the JSON, flags missing clips, and warns about oversized files.

Progress is stored per level `id`, so you can reorder levels without losing stars. Renaming an `id` resets that level's progress.

## Build an Android APK

Build steps (Editor and GitHub Actions), signing, and secrets are in **[docs/BUILD.md](docs/BUILD.md)**, which belongs to the build pipeline.

Project-side settings are committed in `ProjectSettings/`: Snaploop name, portrait only, IL2CPP ARM64, min API 24, Unity splash off, gamma colour space, legacy Input Manager, and `Main.unity` in the Build Profiles scene list. If they drift, **Snaploop > Apply Player Settings** writes them again.

## Playing and testing

- First launch goes straight into level 1 with a one-screen onboarding ("Assemble the moving picture").
- Levels unlock in order. Stars: 3, minus 1 if any hint was used, minus 1 if slower than par (6 s per piece). The minimum is 1.
- Android back button: closes a dialog, otherwise returns to level select, otherwise sends the app to the background.
- **Dev menu**: tap the "Snaploop" wordmark on level select 5 times (works in release builds), or use the **DEV** button in development builds. It has: Win now (finishes the current puzzle, handy for recording reveals), jump to any level, unlock all, reset progress, show onboarding again, FPS counter.

## Extension points (ads/IAP, clip capture)

Gameplay code never calls ads or capture directly. Follow-up systems hook in from a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` method:

- `Snaploop.Core.GameEvents`: `PuzzleStarted`, `PuzzlePlayable`, `PieceSnapped`, `HintUsed`, `PuzzleCompleted`, `RevealStarted`, `RevealFinished`, `NextRequested`, `PuzzleAbandoned`.
- `GameServices.Hints` (`IHintGate`): the hint button calls `RequestHint(onResult)`. The default grants every hint for free. A rewarded-ad gate replaces it and can relabel the button through `ButtonLabel`.
- `GameServices.BetweenPuzzles` (`IBetweenPuzzlesGate`): runs when the player taps **Next** on the win card, before the next level loads. This is the only interstitial slot; it is never invoked mid-puzzle.
- `GameServices.Share` (`IShareProvider`): when it is set and `IsAvailable`, the win card shows a Share button.
- `GameServices.AddSettingsEntry(label, onClick, isVisible)`: adds rows such as "Remove Ads" or "Restore Purchases" to Settings.
- `GameContext`: the board camera, the `VideoPlayer`, the shared video texture, the board's screen rect, and `IsPuzzleInProgress`, for capture code.

Where ad and IAP IDs go is documented by the monetization work.

## Performance notes

- One video decoder per puzzle, whatever the piece count. All pieces sample the same `RenderTexture` by UV.
- All tiles are one mesh with one material: one draw call for tiles, plus one for the board slots and one for the reveal quad. Moving a group rewrites its tiles' slice of a persistent vertex array, uploaded at most once per frame; stacking order is an index-buffer rewrite on pick-up and drop only.
- No per-frame allocations in the gameplay loop. Progress strings are cached, and events are only raised on state changes.
- Targets 60 fps; the screen stays awake while the app is open.

## Vertical clip capture (win clips)

After a win, Snaploop can export a 9:16 MP4 of the puzzle: the scrambled pieces fly into place one by one over the moving scene, the full clip is revealed with a "Solved!" caption, and a short Snaploop end card closes it.

- **Format:** 720x1280, 30 fps, H.264 (about 6 Mbps), no audio. About 10 s long (about 9.9 s at 9 pieces, 10.6 s at 36).
- **When it runs:** with **Settings > Win clips: Auto** (the default on devices with 3 GB of RAM or more), rendering starts in the background once the reveal finishes. The win card's **Share clip** button then saves it to the gallery (`Movies/Snaploop`) and opens the Android share sheet. With **Win clips: When sharing**, nothing is rendered until the player taps Share clip. A small status pill shows progress ("Making your clip 40%").
- **Cancelled, never blocking:** starting a puzzle, tapping Next, leaving the puzzle or backgrounding the app cancels a running clip and deletes the partial file. Any error just shows "Couldn't make the clip". The game itself is unaffected.

### How it works

The clip is a deterministic replay, not a screen recording. `ClipCapture` (an `IShareProvider` installed before the first scene loads) builds a `ClipJob` from the win's `PuzzleResult`:

1. `ClipScene` rebuilds the same jigsaw cut as the game (same `JigsawShape` seed, triangulation and `Snaploop/VideoPiece` shader) in a 720x1280 layout. Each output frame is drawn with one `CommandBuffer` into an offscreen `RenderTexture`. Nothing is drawn by the game cameras, and the on-screen game is never read.
2. `ClipVideoSource` opens the level's clip in a second, paused `VideoPlayer` and steps it one frame per output frame, so the moving picture in the clip is smooth however slow encoding is. If stepping isn't supported or stalls, it falls back to the game's live video texture.
3. Frames are read back with `AsyncGPUReadback` (falling back to `ReadPixels`) and handed to a sink:
   - **Android:** `ClipEncoder.java` uploads each RGBA frame as a texture to a private EGL context whose surface is the input of a hardware `MediaCodec` H.264 encoder, then muxes with `MediaMuxer`. The frame buffer goes from C# to Java as a direct `ByteBuffer`, with no copy into the Java heap.
   - **Editor:** a PNG sequence in `Recordings/Snaploop/<level>-<time>/` (gitignored). The console prints the `ffmpeg` command that turns it into an MP4, and the folder opens in Finder/Explorer.
4. `ClipShare.java` copies the finished MP4 into the gallery and opens the share sheet with a `content://` URI.
   - On Android 10 and later it uses MediaStore with no permission.
   - On Android 7-9 it asks for the storage permission first. The manifest entry (`WRITE_EXTERNAL_STORAGE`, `maxSdkVersion` 28) is added at build time by `Assets/Editor/Snaploop/ClipCaptureAndroidPostprocess.cs`, which also adds a Proguard keep rule for the plugin.

### Why this approach

- **Free and small:** two Java files that use only Android framework APIs (MediaCodec, MediaMuxer, EGL, MediaStore). No paid assets, no extra Gradle dependencies, nothing that needs updating with Unity.
- **Safe for gameplay:** the replay only exists after a win, renders at most 2 frames per game frame into its own render texture, and the encoder runs on its own thread. A bounded pool of 3 frame buffers stops a slow encoder from building up memory. Every step is wrapped so a failure cancels the clip instead of the puzzle.
- **Better clips than recording the screen:** the output is always a clean 9:16 720x1280 with no UI, toasts or fingers, it doesn't depend on the phone's screen size, and it can be rendered later from the Share button.
- **Robust on mid-range phones:** Surface input avoids the vendor-specific YUV colour formats that make `ByteBuffer` input fragile. If a device's encoder rejects 720x1280 at 30 fps, it steps down to 576x1024, 540x960 or 480x848. Uploading frames through GLES2 works the same whether Unity runs on Vulkan or GLES3.

### Clip capture known limitations

- **Not yet run inside the game.** The C# side compiles against Unity 6000.0.85f2 but has not run in the Editor or on a device. The Java plugin was tested on its own on Android emulators (see the PR): encoding, gallery saving, the share sheet and cancelling work there. Still to check on a real phone: the replay's look, `VideoPlayer` frame stepping on Android, the readback orientation probe, and whether running a second video decoder next to the encoder fits on low-end chips.
- The replay is a stylised re-creation of the solve, not the player's actual moves or timing.
- No audio track.
- Android 7-9 needs the storage permission. If the player denies it, the clip isn't saved or shared.
- In the share sheet the file shows by its MediaStore number rather than a file name. This is cosmetic.
- If frame stepping falls back to the live video, the motion inside the clip follows real time and may look sped up or jumpy when encoding is slow.

## Known limitations

- **Not yet run in Unity.** The code compiles against Unity 6000.0.85f2's assemblies (player and editor) and the jigsaw geometry is unit-checked, but the project has not been opened in the Editor, played, or built to a device. That needs a Unity license; see [docs/BUILD.md](docs/BUILD.md).
- Video from `StreamingAssets` inside the APK depends on Unity's VideoPlayer reading the `jar:` URL. Unity supports this, but it hasn't been confirmed on a device yet. If a clip fails, the puzzle still runs on a placeholder texture and shows a toast.
- Level clips are stored in Git LFS. Run `git lfs install` before cloning; otherwise the `.mp4` files are small pointer files and every level falls back to the placeholder texture. The footage pipeline (sourcing, encoding, credits) is described in [tools/footage/README.md](tools/footage/README.md).
- No piece rotation; pieces only translate. Layout is computed when a level starts, so resizing mid-puzzle (foldables, split screen) is not handled.
- Sound effects are synthesized at startup. There's no music, and clips have no audio.
- UI text uses Unity's built-in LegacyRuntime font (no TextMeshPro assets).
- The package id `com.snaploop.game` is a placeholder.
