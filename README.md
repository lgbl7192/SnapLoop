# Snaploop

Android-first video jigsaw built in Unity. Every piece is a live window onto one looping clip: drag the pieces into place, they snap together, and the finished puzzle plays the full uncut loop.

- Unity **6000.0.85f2** (Unity 6 LTS), built-in render pipeline, Android only, portrait.
- Product name, Android app label and in-app splash: **Snaploop**. Package id `com.snaploop.game` is a placeholder; change it before the first store upload.

## Project layout

| Path | What it is |
| --- | --- |
| `Assets/Scenes/Main.unity` | The only scene: a camera and the `SnaploopApp` component. All UI is built in code. |
| `Assets/Scripts/App/SnaploopApp.cs` | Composition root and screen flow (splash, level select, puzzle, win, next). |
| `Assets/Scripts/Puzzle/` | `PuzzleBoard` (drag, snap, magnetism, scramble, hint, reveal), `JigsawShape` (tab outlines), `Triangulator`. |
| `Assets/Scripts/Video/VideoFeed.cs` | The single `VideoPlayer` writing into one shared `RenderTexture`. |
| `Assets/Scripts/Levels/` | `levels.json` loader and the difficulty ramp. |
| `Assets/Scripts/Core/` | Events, service hooks, settings/progress, haptics, generated sound effects. |
| `Assets/Scripts/UI/` | Code-built uGUI screens (level select, HUD, win card, settings, onboarding, dev menu). |
| `Assets/Resources/Shaders/SnaploopVideoPiece.shader` | The one shader every piece uses. |
| `Assets/StreamingAssets/Levels/` | Clips, optional thumbnails, and `levels.json`. |
| `Assets/Editor/Snaploop/` | Editor menu: **Snaploop > Apply Player Settings / Validate Levels / Open Main Scene**. |
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
- All pieces are one mesh with one material: one draw call for pieces, plus one for the board and one for the reveal quad. Moving a piece rewrites its slice of a persistent vertex array, uploaded at most once per frame.
- No per-frame allocations in the gameplay loop. Progress strings are cached, and events are only raised on state changes.
- Targets 60 fps; the screen stays awake while the app is open.

## Known limitations

- **Not yet run in Unity.** The code compiles against Unity 6000.0.85f2's assemblies (player and editor) and the jigsaw geometry is unit-checked, but the project has not been opened in the Editor, played, or built to a device. That needs a Unity license; see [docs/BUILD.md](docs/BUILD.md).
- Video from `StreamingAssets` inside the APK depends on Unity's VideoPlayer reading the `jar:` URL. Unity supports this, but it hasn't been confirmed on a device yet. If a clip fails, the puzzle still runs on a placeholder texture and shows a toast.
- The three bundled clips (`sunset-shore`, `beach-waves`, `neon-street`, from Pexels) are placeholders until the real 30-level set lands.
- No piece rotation; pieces only translate. Layout is computed when a level starts, so resizing mid-puzzle (foldables, split screen) is not handled.
- Sound effects are synthesized at startup. There's no music, and clips have no audio.
- UI text uses Unity's built-in LegacyRuntime font (no TextMeshPro assets).
- The package id `com.snaploop.game` is a placeholder.
