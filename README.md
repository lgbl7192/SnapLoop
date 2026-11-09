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
| `Assets/Scripts/UI/` | Code-built uGUI screens (level select, HUD, win card, settings, onboarding, dev menu). Shared look in `UI/Theme/`; see [UI look and screenshots](#ui-look-and-screenshots). |
| `Assets/Resources/UI/UITheme.asset` | Design tokens: palette, gradients, fonts, type scale, spacing, radii, motion. |
| `Assets/Art/` | Bundled OFL fonts (with their licences) and the app icon placeholder. |
| `Assets/Scripts/Monetization/` | Ads (rewarded hints, interstitials on Next, level-select banner, the gating rules, test and LevelPlay adapters), consent and remote-config seams, and the Remove Ads purchase. See [Monetization](#monetization-ads-and-remove-ads). |
| `Assets/Resources/Monetization/MonetizationConfig.asset` | Every ad and IAP id and every ad rule number. |
| `Tests/AdPolicy/` | Plain .NET unit tests for the ad rules (no Unity needed). |
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
- **Dev menu**: tap the "Snaploop" wordmark on level select 5 times (works in release builds), or use the **DEV** button in development builds. It has: Win now (finishes the current puzzle, handy for recording reveals), jump to any level, unlock all, reset progress, show onboarding again, FPS counter. In the Editor and development builds it also has the monetization toggles listed under [Monetization](#monetization-ads-and-remove-ads).

## UI look and screenshots

The UI is a dark "midnight arcade" style with Snaploop's own golden-hour accent (gold to coral to pink) on the wordmark and loop mark. Orange is kept for the one next action (Play, Next); green with an AD badge only ever means a rewarded ad.

- **Tokens**: `Assets/Resources/UI/UITheme.asset` (`UITheme.cs`). Edit colours, sizes and timings there; every screen reads it. Sizes are in dp (1 dp = 2.75 canvas units on the 1080-wide reference canvas).
- **Fonts**: Lilita One (titles, numbers) and Nunito SemiBold/ExtraBold/Black (body, buttons, caps), both SIL Open Font License; licences sit next to the fonts in `Assets/Art/Fonts/`.
- **Building blocks** (`UIFactory`): sticker buttons with a lip and press sink (`JuicyButton`, `RoundButton`), cards, chips, ribbons, stars, the progress ring and switches. Sprites (rounded 9-slices, soft shadows, icons, the backdrop lattice) are generated at runtime by `UISprites`, so there are no image assets to maintain.
- **Motion** (`UIJuice`, `UIFx`): tweens, press springs and pulses run on unscaled time and allocate nothing per frame; snap bursts and win confetti come from a fixed particle pool.
- **Safe area**: every screen sits inside a `SafeAreaFitter`; touch targets are at least 48 dp.

**See every screen without Unity**: the **UI screenshots** workflow (`.github/workflows/ui-screenshots.yml`) runs on pull requests that change UI files (`Assets/Scripts/UI/`, `Assets/Resources/UI/`, `Assets/Art/`, the monetization overlays, the screenshot runner or the workflow itself), or by hand from the Actions tab (**Run workflow** on any branch). It builds a Linux development player, presses through onboarding, HUD, hint (rewarded test ad), settings, dev menu, win card, interstitial, level select (with and without the test banner) and the test store at 1080x2400 and 720x1600, and uploads the PNGs as the `ui-screenshots` artifact. The run uses the Unity seat, so it queues behind Android builds. The driver is `Assets/Scripts/Dev/UIScreenshotRunner.cs` (inert unless the player is started with `-snaploop-screenshots <dir>`); it finds buttons by GameObject name, so keep those names when you restyle a screen.

## Extension points (ads/IAP, clip capture)

Gameplay code never calls ads or capture directly. Follow-up systems hook in from a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` method:

- `Snaploop.Core.GameEvents`: `PuzzleStarted`, `PuzzlePlayable`, `PieceSnapped`, `HintUsed`, `PuzzleCompleted`, `RevealStarted`, `RevealFinished`, `NextRequested`, `PuzzleAbandoned`.
- `GameServices.Hints` (`IHintGate`): the hint button calls `RequestHint(onResult)`. The default grants every hint for free. A rewarded-ad gate replaces it and sets the button's `ButtonLabel`, whether it is shown at all (`IsOffered`; a hint that isn't on offer is hidden, not greyed out) and whether it gets the rewarded look (`IsRewardedAd`: green with an AD badge, from `UIFactory.SetRewardedLook`). Call `GameServices.NotifyHintOfferChanged()` when any of these change.
- `GameServices.BetweenPuzzles` (`IBetweenPuzzlesGate`): runs when the player taps **Next** on the win card, before the next level loads. This is the only interstitial slot; it is never invoked mid-puzzle.
- `GameServices.Share` (`IShareProvider`): when it is set and `IsAvailable`, the win card shows a Share button.
- `GameServices.AddSettingsEntry(label, onClick, isVisible)`: adds rows such as "Remove Ads" or "Restore Purchases" to Settings. `GameServices.NotifySettingsChanged()` refreshes an open Settings screen.
- `GameServices.AddDevMenuEntry(label, onClick)`: adds a row to the dev menu. The dev menu also opens in release builds, so guard debug-only entries with `Debug.isDebugBuild`.
- `GameServices.SetLevelSelectBottomInset(screenPixels)`: reserves space at the bottom of level select (for a loaded banner); the level grid shrinks to stay clear of it. 0 gives the space back.
- `GameContext`: the board camera, the `VideoPlayer`, the shared video texture, the board's screen rect, `IsPuzzleInProgress`, and `IsLevelSelectVisible` (changes raise `GameEvents.LevelSelectVisibilityChanged`).

## Monetization (ads and Remove Ads)

Code lives in `Assets/Scripts/Monetization/` and installs itself before the first scene loads (`MonetizationRuntime`). Gameplay never calls it; it plugs into the hint gate, the between-puzzles slot, the level-select hooks and the Settings rows above. It follows Lance's ad strategy (written for Bricksplode), cut down to Snaploop's MVP; see [Ad strategy: what's in and what's later](#ad-strategy-whats-in-and-whats-later).

**How it's built:**

- `AdsService` is the only code that asks for an ad. The game sees it through `IAdsService` (`IsRewardedReady`, `ShowRewarded`, `MaybeShowInterstitial`, `Changed`).
- `IAdNetwork` wraps one SDK and knows none of the rules: `TestAdNetwork` (fake ads), `LevelPlayAdNetwork` (Unity LevelPlay seam), `NoAdNetwork`.
- `AdGate` (the rules), `AdLedger` (what the rules read, saved to `Application.persistentDataPath/ad-ledger.json`), `AdRules` (the numbers) and `AdBackoff` are plain C# in `Ads/Policy/`, with unit tests in `Tests/AdPolicy/`.
- `IConsentProvider`, `IRemoteConfigProvider` and `IAdTelemetry` are seams with stub or debug-log implementations only.

### The rules

**Rewarded (hints only):**

- Each hint costs one rewarded ad, and the hint is granted only if the ad is watched to the end. An ad closed early still counts toward the caps and the interstitial cooldown.
- Caps: 8 per session, 20 per day. Available from the first session, and still available after buying Remove Ads.
- The hint button is hidden (not greyed out) while no rewarded ad is loaded or a cap is reached, and appears as soon as one loads. When it costs an ad it reads "Get a hint", turns green and has an **AD** badge (`UIFactory.SetRewardedLook`, the shared look for any future rewarded button).
- Builds with no ad network, and builds where rewarded ads are switched off, keep free hints.

**Interstitials (only on Next after a win):** Snaploop has no fail or retry, so the win card's **Next** is the only slot. Checked in this order; the first rule that fails blocks the ad:

| # | Rule | Default | Config key |
| --- | --- | --- | --- |
| 1 | Kill switch | on | `ads_inter_enabled` |
| 2 | Player owns Remove Ads | | |
| 3 | No puzzle in progress (Snaploop guard, `GameContext.IsPuzzleInProgress`) | | |
| 4 | Grace: not in the first 2 sessions, and not before 5 cleared levels. The first interstitial can come in session 3. | 2 sessions, 5 levels | `ads_inter_grace_sessions`, `ads_inter_grace_levels` |
| 5 | Level gap: at least 3 level ends (wins) since the last interstitial | 3 | `ads_inter_min_levels` |
| 6 | Time gap since the last interstitial | 90 s | `ads_inter_min_seconds` |
| 7 | Cooldown after a rewarded ad | 90 s | `ads_inter_after_rewarded_seconds` |
| 8 | Session cap | 4 | `ads_inter_max_per_session` |
| 9 | Daily cap | 10 | `ads_inter_max_per_day` |
| 10 | Rage-quit pause: leaving the app within 30 s of an interstitial closing, twice in a row, stops interstitials for 24 h. Finishing a level after an interstitial resets the count. | 30 s, 2, 24 h | `ads_ragequit_window_seconds`, `ads_ragequit_count`, `ads_ragequit_suppress_hours` |
| 11 | Consent has finished and allows ad requests | | |
| 12 | An interstitial is already loaded (never waits for one) | | |

**Banner (level select only):** never during play or on the win card. Only from day 3 after install (2 full local days) and after 5 sessions (`ads_banner_grace_days`, `ads_banner_grace_sessions`). Off after Remove Ads; kill switch `ads_banner_enabled`. It takes no space until one has loaded; then the level grid shrinks above it. The app refreshes it every 60 s (`ads_banner_refresh_seconds`, minimum 30) while level select is on screen, and destroys it when you leave level select, so nothing refreshes during play.

**Sessions and days:** a session starts on a cold launch or when the app returns after 30 minutes in the background (`ads_session_timeout_min`). Daily counters reset at local midnight. If the device clock goes backwards, stored times are clamped to "now": that can restart a cooldown, but never allows more ads.

**Loading and reliability:**

- No ad is requested before consent has finished.
- One rewarded ad is kept loaded at all times; one interstitial only once the player is past the grace period.
- Failed loads retry after 4 s, doubling up to 3 min, with ±20% jitter. A load with no answer after 60 s counts as failed.
- Show watchdog: if an ad's callbacks go missing, the show is ended so later ads don't hang: 10 s if it never appeared, 5 s after the app comes back to the front, 5 min at most.
- Development builds always use LevelPlay's test ids, so they can never show a live ad.

**Config can only reduce ads.** The numbers in `AdRules.cs` are the shipped defaults and also the ceiling. The values in the config asset, and remote config values on top of them, may only move toward fewer ads (longer gaps and grace, lower caps, kill switches off). A value of the wrong type, out of range, or more permissive than the ceiling is ignored, falls back, and is logged as `config_rejected`. Remote values are applied at session start, never mid-session. Raising ad volume needs a code change.

**Telemetry:** every gate block (`ad_gate_blocked` with the rule), interstitial decision (`ad_decision` with the player's context) and ad lifecycle event (`ad_request`, `ad_loaded`, `ad_load_failed`, `ad_load_timeout`, `ad_displayed`, `ad_show_failed`, `ad_closed`, `ad_watchdog`, `rewarded_offer`/`_start`/`_earned`/`_abandoned`, `banner_shown`/`_hidden`, `session_start`/`_background`, consent events) goes to `IAdTelemetry`. The only implementation writes `[AdTelemetry] …` lines to the Unity log in development builds. There is no analytics SDK.

### Where the IDs and numbers go

Everything is in one asset: **`Assets/Resources/Monetization/MonetizationConfig.asset`** (select it in the Project window and edit it in the Inspector).

| Field | What to put there |
| --- | --- |
| `adapter` | `Auto` (default): test ads in the Editor and development builds; in release builds, LevelPlay if it's set up, otherwise no ads. `TestAds`, `LevelPlay` and `Disabled` force one choice. Test ads never run in a release build. |
| `levelPlayAndroid` | Release ids: `appKey`, and the `rewarded`, `interstitial` and `banner` ad unit ids. Empty for now. Used by release builds only. |
| `levelPlayTestAndroid` | Ids every development build uses, whatever is above. Defaults are LevelPlay's public demo ids from its Unity sample (app key `25b63cf85`). |
| `hintPlacement`, `nextLevelPlacement`, `levelSelectBannerPlacement` | Placement names reported to the network: `hint`, `next_level`, `level_select`. |
| `adRules` | Every number in the tables above (field names match, e.g. `interMaxPerDay` for `ads_inter_max_per_day`). They can only be made stricter than the defaults. |
| `testRewardedSeconds`, `testInterstitialCloseDelaySec` | Length of the fake ads. |
| `removeAdsProductId` | Google Play product id of the Remove Ads non-consumable. The placeholder is `remove_ads`. |
| `removeAdsFallbackPrice` | Shown until Google Play returns the localized price. Default `$4.99`. |
| `fakeStoreInDevelopmentBuilds` | On by default: development builds use the fake store. Turn it off to test real billing from a development build. |

### Testing without accounts

- **Test ads**: in the Editor and development builds, ads are a full-screen overlay with a yellow **TEST AD** banner. The rewarded one only grants the hint if you wait for the 5 s countdown and tap **Get reward**; **Close (no reward)** abandons it. The interstitial can be closed after 2 s. The banner is a yellow **TEST BANNER** strip at the bottom of level select, with a load counter that goes up on each refresh.
- **Fake store**: in the Editor and development builds, **Settings > Remove Ads - $4.99** opens a **TEST STORE** dialog with Buy, Cancel, and "Simulate a failed payment". No money moves.
- **Dev menu** (Editor and development builds only; these rows are not registered in release builds):
  - **Remove Ads: owned/not owned** sets both the local entitlement and the fake store's account.
  - **Forget local Remove Ads** clears only the local entitlement, so you can try **Restore Purchases**.
  - **Force interstitial on next Next** skips grace, gaps, caps and the rage-quit pause once. It never overrides the kill switch, Remove Ads, the mid-puzzle rule or consent.
  - **Test ads: fill/no fill** simulates no fill: the hint button disappears and loads retry with backoff.
  - **Ad ledger: session N, cleared M, day D (tap: skip grace)** makes this install look past every grace period, so interstitials and the banner can be tried right away.
  - **Reset ad ledger (fresh install)** forgets all ad history.
  - **Privacy choices row: shown/hidden** makes the consent stub ask for the Settings row.
- In the Unity console, development builds log every decision and lifecycle event as `[AdTelemetry] …`, for example `[AdTelemetry] ad_gate_blocked format=interstitial placement=next_level rule=Grace`.
- **Unit tests** for the rules: `dotnet run --project Tests/AdPolicy` (any .NET 8 SDK; Unity isn't needed).

### Consent and privacy

- `IConsentProvider` is the seam for Google's User Messaging Platform (UMP). Today it's `StubConsentProvider`, which finishes immediately and allows ads.
- On every launch, consent is gathered before any ad request. Until it finishes, nothing loads and hints that need an ad stay hidden.
- Settings shows a **Privacy choices** row when the provider says it's required. With the stub, that only happens through the dev menu, and tapping it shows a toast saying where the UMP form will open.

### Remove Ads purchase

- Remove Ads removes interstitials and banners only. Hint ads stay, because the player chooses to watch them.
- Unity IAP 5.4.4 (`com.unity.purchasing`, in `Packages/manifest.json`) with one non-consumable. Release builds use Google Play billing through it.
- The entitlement is saved in `Application.persistentDataPath/entitlements.json`, not PlayerPrefs, so the dev menu's "Reset progress" doesn't remove it. On every launch the app asks Google Play for past purchases and grants Remove Ads again if it's owned. A reinstall or a new device restores it automatically, and the **Restore Purchases** row does the same on demand.
- The purchase is granted when Google Play reports it and confirmed after that. If confirmation is interrupted, Google Play delivers the purchase again on the next launch. A refund doesn't remove the entitlement automatically, and there is no server-side receipt validation.

### Ad strategy: what's in and what's later

| Strategy section | In Snaploop now | Later |
| --- | --- | --- |
| 2. Formats and placements | Rewarded hint, interstitial on Next after a win, banner on level select | Retry interstitial (Snaploop has no fail state) |
| 3. Rewarded | Hint offer, caps, reward only on a full watch, hidden until loaded, shared green look with AD badge | Other offers (revive, double coins, save streak, try a skin, extra spin): they need soft currency or a fail state |
| 4. Interstitial gates | The strategy's rules 1–6 and 8–12 plus the mid-puzzle guard, rage-quit pause (4.1), sessions, local-midnight reset, clock clamp | Frustration block (rule 7: Snaploop has no fail state); exploration skip and ad engine (4.2: need analytics) |
| 5. Banners | All of it | |
| 6. Retention systems | | Soft currency, daily rewards, spins, streaks, daily challenge, shop, reminders (post-MVP) |
| 7. Purchase | Remove Ads via Unity IAP instead of RevenueCat | |
| 8. Consent | Seam, "no request before consent", Privacy choices row | UMP itself, non-personalized ads without TCF consent, reloading ads after a consent change |
| 9. Remote config and A/B | Keys, validation, reduce-only guarantee, applied at session start, stub provider | A real provider (e.g. Unity Remote Config or Firebase); A/B tests |
| 10. Telemetry | Every gate block, decision and ad lifecycle event, to the debug log | An analytics SDK, `ad_paid` impression revenue, level and economy events |
| 11. Architecture | `IAdsService`, `IAdNetwork`, `AdGate`, `AdLedger`, preloading, backoff, 60 s load timeout, show watchdog, test ids in debug | Replacing loaded ads before they expire (55 min); LevelPlay handles expiry itself, but this hasn't been checked |

### What Lance needs to set up later

None of this has been created yet. No accounts, listings or ad units exist.

1. **Google Play Console** (developer account, app listing): create an in-app product, a one-time non-consumable, with the id from `removeAdsProductId`, priced at $4.99, and activate it. Billing only works for builds installed from a Play testing track (internal testing is enough) and signed with the upload key, with license testers added. Development builds are rejected by Play, so test real billing with a release build. Put the Google Play license key in **Project Settings > Services > In-App Purchasing** if you want Unity's client-side receipt validation (optional).
2. **Unity Cloud project**: link the project (**Edit > Project Settings > Services**) and toggle **In-App Purchasing** on. Unity IAP works without this, but the link stops its warnings and enables Unity's revenue dashboard.
3. **Ads, using Unity LevelPlay**:
   - Create a LevelPlay account and an Android app, then create one rewarded, one interstitial and one banner ad unit. **Turn auto refresh off on the banner ad unit**: the game refreshes it itself.
   - Install the LevelPlay package (`com.unity.services.levelplay`, via Package Manager or **Ads Mediation > Integration Manager**), and add any ad network adapters you want.
   - Add `SNAPLOOP_LEVELPLAY` to **Project Settings > Player > Android > Scripting Define Symbols**.
   - Fill in `levelPlayAndroid` (app key and the three ad unit ids). Leave `levelPlayTestAndroid` alone unless LevelPlay's demo ids stop working; then replace them with test ids for your own app (LevelPlay dashboard test mode or test devices).
   - The adapter is `Assets/Scripts/Monetization/Ads/LevelPlayAdNetwork.cs`. It compiles against LevelPlay 9.6.0 but hasn't been run against a live account.
   - If you add AdMob as a mediated network, its Android app id also goes in LevelPlay's network settings in the Unity Editor.
   - To use a different mediation SDK, implement `IAdNetwork` and select it in `MonetizationRuntime.CreateAdNetwork`.
4. **Consent**: add Google's User Messaging Platform (UMP) SDK, set up the GDPR and US state messages under **Privacy & messaging** in an AdMob account, and replace `StubConsentProvider` with a UMP-backed `IConsentProvider`. Also add a privacy policy URL to the Play listing and fill in Play's Data safety form for the ad SDK.
5. **Remote config (optional)**: to tune ad rules without a release, implement `IRemoteConfigProvider` (Unity Remote Config or Firebase) using the `ads_*` keys above. Values can only make ads rarer.

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

1. `ClipScene` rebuilds the puzzle's tile grid with the game's own `TileBatch`, `TileGroups` and `Snaploop/VideoTile` shader in a 720x1280 layout. Tiles join into one outlined shape as they land, just like in play. Each output frame is drawn with one `CommandBuffer` into an offscreen `RenderTexture`. Nothing is drawn by the game cameras, and the on-screen game is never read.
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
- Monetization has only been compiled, not run. The ad rules are unit-tested in plain .NET, but the test ad, banner and store overlays, the Unity IAP flow and the LevelPlay adapter haven't been run in Unity or on a device. No real ad network, consent form or Play billing has been tried, because no accounts exist yet.
