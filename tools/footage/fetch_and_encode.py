#!/usr/bin/env python3
"""Rebuild Snaploop's level clips, thumbnails and levels.json from tools/footage/clips.json.

Requires python3 (stdlib only) and ffmpeg/ffprobe on PATH. No accounts or API keys:
Pexels sources are fetched through the public per-video download link.

    python3 tools/footage/fetch_and_encode.py                  # build missing clips + levels.json
    python3 tools/footage/fetch_and_encode.py --only reef-school --force
    python3 tools/footage/fetch_and_encode.py --manifest-only  # only rewrite levels.json / CREDITS.md
    python3 tools/footage/fetch_and_encode.py --check          # report loop-seam similarity
"""

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
CLIPS_JSON = os.path.join(HERE, "clips.json")
CACHE_DIR = os.path.join(HERE, ".cache")
OUT_DIR = os.path.join(REPO, "Assets", "StreamingAssets", "Levels")
LEVELS_JSON = os.path.join(OUT_DIR, "levels.json")
CREDITS_MD = os.path.join(HERE, "CREDITS.md")

WIDTH, HEIGHT, FPS = 720, 1280, 30
THUMB_W, THUMB_H = 360, 640
PEXELS_DOWNLOAD = "https://www.pexels.com/download/video/{id}/"
USER_AGENT = "Mozilla/5.0 (X11; Linux x86_64) SnaploopFootage/1.0"

X264_ARGS = [
    "-c:v", "libx264", "-preset", "slow",
    "-profile:v", "high", "-level:v", "3.1",
    "-b:v", "2500k", "-maxrate", "3000k", "-bufsize", "5000k",
    "-g", str(FPS), "-keyint_min", str(FPS), "-sc_threshold", "0",
    "-pix_fmt", "yuv420p",
    "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709",
    "-an", "-map_metadata", "-1", "-movflags", "+faststart",
]


def die(msg):
    print(f"error: {msg}", file=sys.stderr)
    sys.exit(1)


def load_clips():
    with open(CLIPS_JSON, encoding="utf-8") as f:
        data = json.load(f)
    defaults = data.get("defaults", {})
    levels = []
    seen = set()
    for raw in data["levels"]:
        lvl = {**defaults, **raw}
        if not re.fullmatch(r"[a-z0-9]+(-[a-z0-9]+)*", lvl["id"]):
            die(f"level id must be kebab-case: {lvl['id']!r}")
        if lvl["id"] in seen:
            die(f"duplicate level id: {lvl['id']}")
        seen.add(lvl["id"])
        if "pexelsId" not in lvl and "sourceFile" not in lvl:
            die(f"{lvl['id']}: needs pexelsId or sourceFile")
        if not 8.0 <= float(lvl["durationSec"]) <= 12.0:
            print(f"warning: {lvl['id']} durationSec {lvl['durationSec']} is outside 8-12 s", file=sys.stderr)
        levels.append(lvl)
    return levels


def probe(path):
    out = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "v:0", "-count_packets",
         "-show_entries", "stream=width,height,nb_read_packets:format=duration,size,bit_rate",
         "-of", "json", path],
        check=True, capture_output=True, text=True).stdout
    info = json.loads(out)
    s, fmt = info["streams"][0], info["format"]
    return {
        "width": int(s["width"]), "height": int(s["height"]),
        "frames": int(s.get("nb_read_packets", 0)),
        "duration": float(fmt["duration"]), "size": int(fmt["size"]),
        "bitrate": int(fmt.get("bit_rate", 0)),
    }


def source_path(lvl):
    if "sourceFile" in lvl:
        p = lvl["sourceFile"]
        return p if os.path.isabs(p) else os.path.join(HERE, p)
    return os.path.join(CACHE_DIR, f"pexels-{lvl['pexelsId']}.mp4")


def download(lvl):
    dst = source_path(lvl)
    if os.path.exists(dst) or "pexelsId" not in lvl:
        return dst
    os.makedirs(CACHE_DIR, exist_ok=True)
    url = PEXELS_DOWNLOAD.format(id=lvl["pexelsId"])
    print(f"  downloading {url}")
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    tmp = dst + ".part"
    with urllib.request.urlopen(req, timeout=120) as resp, open(tmp, "wb") as f:
        shutil.copyfileobj(resp, f)
    os.replace(tmp, dst)
    return dst


def encode(lvl, src, dst):
    """Trim, cover-crop to 9:16, and make a seamless loop by crossfading the tail into the head.

    Takes D+F seconds of source. Output frame t in [0, F) is a fade from source[D + t] to
    source[t]; frames [F, D) are untouched. So the last output frame (source[D-1]) is followed
    on loop by a frame that is ~source[D], and the fade ends exactly on source[F].
    """
    info = probe(src)
    start = float(lvl.get("startSec", 0.0))
    dur = float(lvl["durationSec"])
    fade = float(lvl["fadeSec"])
    if start + dur + fade > info["duration"] - 0.05:
        die(f"{lvl['id']}: startSec+durationSec+fadeSec = {start + dur + fade:.2f}s exceeds "
            f"source length {info['duration']:.2f}s")

    n = round(dur * FPS)
    fn = round(fade * FPS)
    cx = float(lvl.get("cropX", 0.5))
    cy = float(lvl.get("cropY", 0.5))
    aspect = WIDTH / HEIGHT
    crop = (f"crop='if(gt(iw/ih,{aspect}),ih*{aspect},iw)':'if(gt(iw/ih,{aspect}),ih,iw/{aspect})'"
            f":'(iw-ow)*{cx}':'(ih-oh)*{cy}'")
    graph = (
        f"[0:v]{crop},scale={WIDTH}:{HEIGHT}:flags=lanczos,setsar=1,fps={FPS},format=yuv420p,"
        f"trim=end_frame={n + fn},setpts=PTS-STARTPTS,split=3[a][b][c];"
        f"[a]trim=start_frame=0:end_frame={fn},setpts=PTS-STARTPTS[head];"
        f"[b]trim=start_frame={fn}:end_frame={n},setpts=PTS-STARTPTS[body];"
        f"[c]trim=start_frame={n}:end_frame={n + fn},setpts=PTS-STARTPTS[tail];"
        f"[tail][head]xfade=transition=fade:duration={fn / FPS}:offset=0[x];"
        f"[x][body]concat=n=2:v=1:a=0,fps={FPS}[out]"
    )
    tmp = dst + ".tmp.mp4"
    cmd = ["ffmpeg", "-v", "error", "-y", "-ss", f"{start}", "-t", f"{dur + fade + 1}", "-i", src,
           "-filter_complex", graph, "-map", "[out]", "-frames:v", str(n), "-r", str(FPS),
           *X264_ARGS, tmp]
    subprocess.run(cmd, check=True)
    out = probe(tmp)
    if out["frames"] != n or (out["width"], out["height"]) != (WIDTH, HEIGHT):
        os.remove(tmp)
        die(f"{lvl['id']}: unexpected output {out['width']}x{out['height']} / {out['frames']} frames (want {n})")
    os.replace(tmp, dst)


def make_thumbnail(lvl, clip, dst):
    """Portrait JPG for level select, taken from the clip itself (default: its midpoint)."""
    at = float(lvl.get("thumbSec", float(lvl["durationSec"]) / 2))
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{at}", "-i", clip, "-frames:v", "1",
                    "-vf", f"scale={THUMB_W}:{THUMB_H}:flags=lanczos", "-q:v", "3", dst], check=True)


def ensure_meta(path):
    """Unity .meta with a GUID derived from the asset path, so rebuilds on any machine agree."""
    meta = path + ".meta"
    if os.path.exists(meta):
        return
    rel = os.path.relpath(path, REPO).replace(os.sep, "/")
    guid = hashlib.md5(f"snaploop-footage:{rel}".encode()).hexdigest()
    with open(meta, "w", encoding="utf-8", newline="\n") as f:
        f.write(f"fileFormatVersion: 2\nguid: {guid}\nDefaultImporter:\n  externalObjects: {{}}\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def frame_ssim(path, a, b):
    """SSIM between frame indices a and b of a clip."""
    res = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", path, "-i", path, "-lavfi",
         f"[0:v]trim=start_frame={a}:end_frame={a + 1},setpts=PTS-STARTPTS[a];"
         f"[1:v]trim=start_frame={b}:end_frame={b + 1},setpts=PTS-STARTPTS[b];"
         "[a][b]ssim=stats_file=-", "-f", "null", "-"],
        capture_output=True, text=True, check=True)
    m = re.search(r"All:([0-9.]+)", res.stdout)
    return float(m.group(1)) if m else math.nan


def step_ssims(path):
    """SSIM of every consecutive frame pair (k, k+1)."""
    res = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", path, "-i", path, "-lavfi",
         "[1:v]trim=start_frame=1,setpts=PTS-STARTPTS[b];[0:v][b]ssim=stats_file=-", "-f", "null", "-"],
        capture_output=True, text=True, check=True)
    return [float(m) for m in re.findall(r"All:([0-9.]+)", res.stdout)]


def check(lvl, path):
    """The loop point always lands on a keyframe, so compare it with the clip's other keyframe steps."""
    n = probe(path)["frames"]
    seam = frame_ssim(path, n - 1, 0)
    steps = step_ssims(path)
    key_steps = sorted(steps[k] for k in range(FPS - 1, len(steps), FPS))
    key_med = key_steps[len(key_steps) // 2]
    flag = "" if seam >= key_med - 0.05 else "  <-- seam rougher than in-clip keyframe steps; inspect"
    print(f"  {lvl['id']:<22} seam SSIM {seam:.3f}  keyframe-step median {key_med:.3f}{flag}")


def write_manifest(levels):
    entries = []
    for lvl in levels:
        path = os.path.join(OUT_DIR, f"{lvl['id']}.mp4")
        dur = round(probe(path)["frames"] / FPS, 2) if os.path.exists(path) else float(lvl["durationSec"])
        entries.append({
            "id": lvl["id"], "title": lvl["title"], "file": f"{lvl['id']}.mp4",
            "cols": int(lvl["cols"]), "rows": int(lvl["rows"]), "durationSec": dur,
            "category": lvl["category"], "credit": lvl["credit"],
            "sourceUrl": lvl.get("sourceUrl", ""), "license": lvl["license"],
        })
    with open(LEVELS_JSON, "w", encoding="utf-8") as f:
        json.dump({"levels": entries}, f, indent=2, ensure_ascii=False)
        f.write("\n")

    lines = ["# Snaploop level footage credits", "",
             "Generated by `fetch_and_encode.py` from `clips.json`. All clips are used under the "
             "[Pexels License](https://www.pexels.com/license/) (free for commercial use, attribution "
             "not required but given here).", "",
             "| # | Level | Grid | Category | Creator | Source |", "|---|---|---|---|---|---|"]
    for i, e in enumerate(entries, 1):
        creator = e["credit"].removesuffix(" / Pexels")
        lines.append(f"| {i} | {e['title']} | {e['cols']}x{e['rows']} | {e['category']} | {creator} | "
                     f"[{e['sourceUrl'].rstrip('/').rsplit('-', 1)[-1] or 'link'}]({e['sourceUrl']}) |")
    with open(CREDITS_MD, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", nargs="+", metavar="ID", help="only process these level ids")
    ap.add_argument("--force", action="store_true", help="re-encode even if the output clip exists")
    ap.add_argument("--manifest-only", action="store_true", help="skip download/encode; rewrite levels.json")
    ap.add_argument("--check", action="store_true", help="report loop-seam SSIM for built clips")
    args = ap.parse_args()

    for tool in ("ffmpeg", "ffprobe"):
        if not shutil.which(tool):
            die(f"{tool} not found on PATH")

    levels = load_clips()
    ids = {lvl["id"] for lvl in levels}
    if args.only:
        unknown = set(args.only) - ids
        if unknown:
            die(f"unknown level ids: {', '.join(sorted(unknown))}")
    selected = [lvl for lvl in levels if not args.only or lvl["id"] in args.only]
    os.makedirs(OUT_DIR, exist_ok=True)

    if not args.manifest_only:
        for i, lvl in enumerate(selected, 1):
            dst = os.path.join(OUT_DIR, f"{lvl['id']}.mp4")
            if os.path.exists(dst) and not args.force:
                continue
            print(f"[{i}/{len(selected)}] {lvl['id']}")
            src = download(lvl)
            if not os.path.exists(src):
                die(f"{lvl['id']}: source not found at {src}")
            encode(lvl, src, dst)
            make_thumbnail(lvl, dst, os.path.join(OUT_DIR, f"{lvl['id']}.jpg"))
            o = probe(dst)
            print(f"  -> {os.path.relpath(dst, REPO)}  {o['size'] / 1e6:.2f} MB  {o['bitrate'] / 1e6:.2f} Mbps")

    for lvl in levels:
        clip = os.path.join(OUT_DIR, f"{lvl['id']}.mp4")
        thumb = os.path.join(OUT_DIR, f"{lvl['id']}.jpg")
        if not os.path.exists(clip):
            continue
        if not os.path.exists(thumb):
            make_thumbnail(lvl, clip, thumb)
        ensure_meta(clip)
        ensure_meta(thumb)

    write_manifest(levels)
    ensure_meta(LEVELS_JSON)
    print(f"wrote {os.path.relpath(LEVELS_JSON, REPO)} ({len(levels)} levels)")

    if args.check:
        for lvl in selected:
            path = os.path.join(OUT_DIR, f"{lvl['id']}.mp4")
            if os.path.exists(path):
                check(lvl, path)

    stray = sorted(f for f in os.listdir(OUT_DIR)
                   if f.endswith((".mp4", ".jpg")) and f.rsplit(".", 1)[0] not in ids)
    if stray:
        print(f"warning: files not in clips.json (not listed in levels.json): {', '.join(stray)}", file=sys.stderr)


if __name__ == "__main__":
    main()
