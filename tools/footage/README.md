# Level footage pipeline

Builds the puzzle clips in `Assets/StreamingAssets/Levels/` from real footage:

- `clips.json` is the source of truth: play order, title, optional `pack`, category, grid (`cols` x `rows`), source (Pexels or NASA id), credit, source URL, license, plus encode settings (`startSec`, `durationSec`, `fadeSec`, `cropX`/`cropY`, `thumbSec`).
- `fetch_and_encode.py` downloads each source from Pexels (public download link) or the NASA Image and Video Library (no account or API key for either), encodes it, makes a thumbnail, and writes `levels.json`, the Unity `.meta` files and `CREDITS.md`.
- Levels without `pack` form the free `starter` pack. Theme-pack levels carry `"pack": "<packId>"` (`sealife`, `animals`, `space`, `christmas`), which is copied into `levels.json`. Within each pack, grids ramp from 3x3 to 6x6. `packs.json` (titles, prices, covers) belongs to the game code, not this folder.
- Output per level: `<id>.mp4` (H.264 High, 720x1280, 30 fps, about 2.5 Mbps, no audio, 1 s keyframes) and `<id>.jpg` (360x640 thumbnail for level select).

Needs `python3` (stdlib only), `ffmpeg`/`ffprobe`, and `git lfs install` (clips are LFS-tracked via `.gitattributes`).

## How a clip is made

1. Cover-crop the source to 9:16. Landscape sources are cropped horizontally; `cropX` 0 is left, 0.5 is center, 1 is right.
2. Scale to 720x1280 and resample to 30 fps.
3. Take `durationSec + fadeSec` seconds starting at `startSec`.
4. Crossfade the extra tail second into the head. The last frame flows straight into the first, so the loop has no jump. Fast camera moves show a light 1 s ghost during the fade; use a shorter `fadeSec` or pick a calmer segment if that bothers you.

## Add a level

1. Find a Pexels video (portrait or 4K landscape, at least `durationSec + fadeSec + startSec` long, ideally 11 s or more). Avoid AI-looking or heavily used stock.
2. Add an entry to `clips.json` at the position you want it in play order:

   ```json
   { "id": "harbor-gulls", "title": "Harbor Gulls", "category": "nature", "cols": 4, "rows": 5,
     "pexelsId": 1234567, "credit": "Creator Name / Pexels",
     "sourceUrl": "https://www.pexels.com/video/<slug>-1234567/", "startSec": 2.0 }
   ```

   For a theme pack, add `"pack": "sealife"` (or another pack id) and keep the entry with the rest of that pack.

   For your own footage, use `"sourceFile": "my-clips/harbor.mov"` (relative to this folder) instead of `pexelsId`.

   For NASA footage, use `"nasaId"` (the id on images.nasa.gov) with `"license": "Public domain (NASA)"`. `"nasaRendition"` picks the asset file (`large` by default, `orig` for the full-resolution master). For long features, add `"fetchStartSec"`/`"fetchDurationSec"`: the script then pulls only that window over HTTP, and `startSec` counts from the start of the window. Use segments without titles, captions, logos or NASA insignia.

   ```json
   { "id": "hurricane-eye", "title": "Hurricane Eye", "pack": "space", "category": "earth", "cols": 5, "rows": 5,
     "nasaId": "jsc2022m000251_Expedition_67_Space_Station_Flyover_of_Hurricane_Ian_220928",
     "fetchStartSec": 243, "fetchDurationSec": 13, "credit": "NASA / ISS",
     "license": "Public domain (NASA)", "licenseUrl": "https://www.nasa.gov/nasa-brand-center/images-and-media/",
     "sourceUrl": "https://images.nasa.gov/details/jsc2022m000251_...", "startSec": 1.0 }
   ```
3. Build it: `python3 tools/footage/fetch_and_encode.py --only harbor-gulls --check`
4. Watch the clip and the loop point. To change trim or crop, edit `clips.json`, then re-run with `--force`. Then commit `clips.json`, `CREDITS.md`, and these files in `Assets/StreamingAssets/Levels/`: the `.mp4`, the `.jpg`, both `.meta` files, and `levels.json`.

Other commands:

- `fetch_and_encode.py`: builds any missing clips and rewrites `levels.json`.
- `--manifest-only`: only rewrites `levels.json` and `CREDITS.md`, for reordering or retitling levels or changing grids.
- `--only <id> --force`: re-encodes one clip and its thumbnail after a trim or crop change.
- `--check`: compares the loop-point SSIM with the clip's other keyframe steps and flags clips that need a look.

Downloads are cached in `.cache/` (git-ignored, about 3.5 GB for the full set).

To remove a level, delete its entry from `clips.json`. Then delete its `.mp4`, `.jpg` and both `.meta` files, and re-run the script with `--manifest-only`.

## Licensing

Each clip's license is recorded in `levels.json` (`license`) and `CREDITS.md`, along with credit and source URL.

- **Pexels** (all starter clips and most pack clips): the [Pexels License](https://www.pexels.com/license/) allows free commercial use with modification and does not require attribution. It does not allow selling unaltered copies, redistributing the clips as a stock library, or implying endorsement by people or brands shown. Pexels gives no model or property releases. Some starter clips show identifiable people or signage, so don't use them in ads that suggest those people or brands endorse Snaploop. The theme-pack clips avoid recognizable people and brands.
- **NASA** (six `space` clips): NASA-produced imagery is generally not copyrighted in the US and is treated as public domain under the [NASA media usage guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/). Credit NASA, don't use the NASA logo ("meatball"), insignia or astronaut likenesses, and don't imply NASA endorses Snaploop. The segments used have no logos, captions or people. Before adding a NASA clip, check its images.nasa.gov page for third-party credits, because some features include non-NASA material.
