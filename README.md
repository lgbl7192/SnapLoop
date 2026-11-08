# Snaploop

Android-first video jigsaw built in Unity. Every piece is a live window onto one looping clip: drag the pieces into place, they snap together, and the finished puzzle plays the full uncut loop.

- Unity **6000.0.85f2** (Unity 6 LTS), built-in render pipeline, Android only, portrait.
- Product name, Android app label and in-app splash: **Snaploop**. Package id `com.snaploop.game` is a placeholder; change it before the first store upload.
- Clips are in Git LFS: run `git lfs install` before cloning (or `git lfs pull` afterwards).

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
| `Assets/Scripts/Monetization/` | Ads (rewarded hints, between-puzzle interstitials, test and LevelPlay adapters) and the Remove Ads purchase. See [Monetization](#monetization-ads-and-remove-ads). |
| `Assets/Resources/Monetization/MonetizationConfig.asset` | Every ad and IAP id and the interstitial frequency cap. |
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
- **Dev menu**: tap the "Snaploop" wordmark on level select 5 times (works in release builds), or use the **DEV** button in development builds. It has: Win now (finishes the current puzzle, handy for recording reveals), jump to any level, unlock all, reset progress, show onboarding again, FPS counter. In the Editor and development builds it also has the monetization toggles listed under [Monetization](#monetization-ads-and-remove-ads).

## Extension points (ads/IAP, clip capture)

Gameplay code never calls ads or capture directly. Follow-up systems hook in from a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` method:

- `Snaploop.Core.GameEvents`: `PuzzleStarted`, `PuzzlePlayable`, `PieceSnapped`, `HintUsed`, `PuzzleCompleted`, `RevealStarted`, `RevealFinished`, `NextRequested`, `PuzzleAbandoned`.
- `GameServices.Hints` (`IHintGate`): the hint button calls `RequestHint(onResult)`. The default grants every hint for free. A rewarded-ad gate replaces it and can relabel the button through `ButtonLabel`.
- `GameServices.BetweenPuzzles` (`IBetweenPuzzlesGate`): runs when the player taps **Next** on the win card, before the next level loads. This is the only interstitial slot; it is never invoked mid-puzzle.
- `GameServices.Share` (`IShareProvider`): when it is set and `IsAvailable`, the win card shows a Share button.
- `GameServices.AddSettingsEntry(label, onClick, isVisible)`: adds rows such as "Remove Ads" or "Restore Purchases" to Settings. `GameServices.NotifySettingsChanged()` refreshes an open Settings screen.
- `GameServices.AddDevMenuEntry(label, onClick)`: adds a row to the dev menu. The dev menu also opens in release builds, so guard debug-only entries with `Debug.isDebugBuild`.
- `GameContext`: the board camera, the `VideoPlayer`, the shared video texture, the board's screen rect, and `IsPuzzleInProgress`, for capture code.

## Monetization (ads and Remove Ads)

Code lives in `Assets/Scripts/Monetization/` and installs itself before the first scene loads (`MonetizationRuntime`). Gameplay never calls it; it plugs into the hint gate, the between-puzzles slot and the Settings rows above.

**Rules, enforced in `AdsController` / `InterstitialPacing`:**

- Rewarded ads are only used for hints. Each hint costs one rewarded ad. If none loads within a few seconds, the player gets "No ad available right now" and no hint.
- Interstitials are only shown from the between-puzzles slot, which runs when the player taps **Next** after a win. They are never shown while `GameContext.IsPuzzleInProgress` is true.
- Default frequency cap: no interstitial after the first 3 levels, then at most one every 2nd win, with at least 60 s between two. In practice the first one comes after level 5.
- Once Remove Ads is owned, no interstitials load or show. By default buyers also get hints without watching an ad (`freeHintsWithRemoveAds`).
- Release builds with no ad network configured run with no ads at all, and hints are free.

### Where the IDs go

Everything is in one asset: **`Assets/Resources/Monetization/MonetizationConfig.asset`** (select it in the Project window and edit it in the Inspector).

| Field | What to put there |
| --- | --- |
| `adapter` | `Auto` (default): test ads in the Editor and development builds; in release builds, LevelPlay if it's set up, otherwise no ads. `TestAds`, `LevelPlay` and `Disabled` force one choice. Test ads never run in a release build. |
| `levelPlayAppKeyAndroid` | LevelPlay app key for the Android app. |
| `rewardedAdUnitIdAndroid` | LevelPlay ad unit id of the rewarded unit (hints). |
| `interstitialAdUnitIdAndroid` | LevelPlay ad unit id of the interstitial unit (between puzzles). |
| `hintPlacement`, `betweenPuzzlesPlacement` | Optional placement names, used for reporting. |
| `interstitialFreeLevels`, `interstitialEveryNthWin`, `interstitialMinIntervalSec` | Frequency cap: 3, 2 and 60 by default. |
| `rewardedLoadWaitSec`, `grantHintWhenNoAd`, `freeHintsWithRemoveAds` | Hint behaviour when no ad is available, and for buyers. |
| `removeAdsProductId` | Google Play product id of the Remove Ads non-consumable. The placeholder is `remove_ads`. |
| `removeAdsFallbackPrice` | Shown until Google Play returns the localized price. Default `$4.99`. |
| `fakeStoreInDevelopmentBuilds` | On by default: development builds use the fake store. Turn it off to test real billing from a development build. |

### Testing without accounts

- **Test ads**: in the Editor and development builds, ads are a full-screen overlay with a yellow **TEST AD** banner. The rewarded one ("Hint unlocks in 5 s") only grants the hint if you wait for the countdown. The interstitial can be closed after 2 s.
- **Fake store**: in the Editor and development builds, **Settings > Remove Ads - $4.99** opens a **TEST STORE** dialog with Buy, Cancel, and "Simulate a failed payment". No money moves.
- **Dev menu** (Editor and development builds only; these rows are not registered in release builds):
  - **Remove Ads: owned/not owned** sets both the local entitlement and the fake store's account.
  - **Forget local Remove Ads** clears only the local entitlement, so you can try **Restore Purchases**.
  - **Force interstitial on next Next** lets the next Next ignore the level, win and cooldown caps. It never overrides Remove Ads or the mid-puzzle rule.
  - **Test ads: fill/no fill** simulates no fill, to see the "No ad available" path.
- In the Unity console, development builds log each between-puzzles decision, for example `[Monetization] Interstitial: SkipFreeLevel`.

### Remove Ads purchase

- Unity IAP 5.4.4 (`com.unity.purchasing`, in `Packages/manifest.json`) with one non-consumable. Release builds use Google Play billing through it.
- The entitlement is saved in `Application.persistentDataPath/entitlements.json`, not PlayerPrefs, so the dev menu's "Reset progress" doesn't remove it. On every launch the app asks Google Play for past purchases and grants Remove Ads again if it's owned. A reinstall or a new device restores it automatically, and the **Restore Purchases** row does the same on demand.
- The purchase is granted when Google Play reports it and confirmed after that. If confirmation is interrupted, Google Play delivers the purchase again on the next launch. A refund doesn't remove the entitlement automatically, and there is no server-side receipt validation.

### What Lance needs to set up later

None of this has been created yet. No accounts, listings or ad units exist.

1. **Google Play Console** (developer account, app listing): create an in-app product, a one-time non-consumable, with the id from `removeAdsProductId`, priced at $4.99, and activate it. Billing only works for builds installed from a Play testing track (internal testing is enough) and signed with the upload key, with license testers added. Development builds are rejected by Play, so test real billing with a release build. Put the Google Play license key in **Project Settings > Services > In-App Purchasing** if you want Unity's client-side receipt validation (optional).
2. **Unity Cloud project**: link the project (**Edit > Project Settings > Services**) and toggle **In-App Purchasing** on. Unity IAP works without this, but the link stops its warnings and enables Unity's revenue dashboard.
3. **Ads, using Unity LevelPlay**:
   - Create a LevelPlay account and an Android app, then create one rewarded and one interstitial ad unit.
   - Install the LevelPlay package (`com.unity.services.levelplay`, via Package Manager or **Ads Mediation > Integration Manager**), and add any ad network adapters you want.
   - Add `SNAPLOOP_LEVELPLAY` to **Project Settings > Player > Android > Scripting Define Symbols**.
   - Fill in `levelPlayAppKeyAndroid`, `rewardedAdUnitIdAndroid` and `interstitialAdUnitIdAndroid`.
   - The adapter is `Assets/Scripts/Monetization/Ads/LevelPlayAdsService.cs`. It compiles against LevelPlay 9.6.0 but hasn't been run against a live account.
   - If you add AdMob as a mediated network, its Android app id also goes in LevelPlay's network settings in the Unity Editor.
   - To use a different mediation SDK, implement `IAdsService` and select it in `MonetizationRuntime.CreateAdsService`.
4. **Privacy and consent**: before real ads go live, add a consent flow (GDPR/UMP) and a privacy policy URL for the Play listing, and fill in Play's Data safety form for the ad SDK. None of this is in the MVP.

## Performance notes

- One video decoder per puzzle, whatever the piece count. All pieces sample the same `RenderTexture` by UV.
- All pieces are one mesh with one material: one draw call for pieces, plus one for the board and one for the reveal quad. Moving a piece rewrites its slice of a persistent vertex array, uploaded at most once per frame.
- No per-frame allocations in the gameplay loop. Progress strings are cached, and events are only raised on state changes.
- Targets 60 fps; the screen stays awake while the app is open.

## Known limitations

- **Not yet run in Unity.** The code compiles against Unity 6000.0.85f2's assemblies (player and editor) and the jigsaw geometry is unit-checked, but the project has not been opened in the Editor, played, or built to a device. That needs a Unity license; see [docs/BUILD.md](docs/BUILD.md).
- Video from `StreamingAssets` inside the APK depends on Unity's VideoPlayer reading the `jar:` URL. Unity supports this, but it hasn't been confirmed on a device yet. If a clip fails, the puzzle still runs on a placeholder texture and shows a toast.
- Level clips are stored in Git LFS. Run `git lfs install` before cloning; otherwise the `.mp4` files are small pointer files and every level falls back to the placeholder texture. The footage pipeline (sourcing, encoding, credits) is described in [tools/footage/README.md](tools/footage/README.md).
- No piece rotation; pieces only translate. Layout is computed when a level starts, so resizing mid-puzzle (foldables, split screen) is not handled.
- Sound effects are synthesized at startup. There's no music, and clips have no audio.
- UI text uses Unity's built-in LegacyRuntime font (no TextMeshPro assets).
- The package id `com.snaploop.game` is a placeholder.
- Monetization has only been compiled, not run. The test ad and test store overlays, the Unity IAP flow and the LevelPlay adapter haven't been run in Unity or on a device. No real ad network or Play billing has been tried, because no accounts exist yet.
