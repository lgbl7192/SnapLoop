using System;
using System.Collections.Generic;
using Snaploop.Levels;
using Snaploop.Puzzle;
using Snaploop.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Snaploop.Capture
{
    /// <summary>
    /// Deterministic replay of a solved puzzle for the share clip: scrambled pieces fly into their
    /// slots over the moving video, the board grows into the full uncut loop, then a Snaploop end card.
    ///
    /// Drawn offscreen with a CommandBuffer into a RenderTexture: no camera, no scene objects, nothing
    /// the game camera can see. Frame n always looks the same, so the clip can be rendered at whatever
    /// speed the device manages. It uses the same jigsaw cut (same seed) and the same piece shader as
    /// the puzzle the player just solved. Coordinates are output pixels, origin bottom-left.
    ///
    /// The target has no depth buffer; stacking is the order of the index buffer (painter's algorithm).
    /// </summary>
    sealed class ClipScene : IDisposable
    {
        public const int Fps = 30;

        // Timeline, seconds.
        const float IntroHoldSec = 0.6f;
        const float AssemblySec = 4.2f;
        const float MinSnapGapSec = 0.1f, MaxSnapGapSec = 0.42f;
        const float FlightSec = 0.42f;
        const float LandFlashSec = 0.3f;
        const float SettleSec = 0.3f;
        const float RevealGrowSec = 0.6f;
        const float RevealFlashSec = 0.45f;
        const float RevealHoldSec = 2.6f;
        const float HeaderFadeSec = 0.3f;
        const float EndCardSec = 2.0f;
        const float EndCardFadeSec = 0.35f;

        // Geometry, fractions of the shorter cell side (same look as PuzzleBoard).
        const float StripWidth = 0.05f, SlotLineWidth = 0.014f;
        const float ScrambleJitter = 0.18f, ScrambleSpread = 1.1f, LiftScale = 0.08f;

        // Layout, fractions of the output frame.
        const float TopBand = 0.17f, BottomBand = 0.12f, SideMargin = 0.06f;

        static readonly Color32 FillColor = new Color32(255, 255, 255, 255);
        static readonly Color32 EdgeDark = new Color32(12, 12, 16, 0);
        static readonly Color32 EdgeDarkInner = new Color32(12, 12, 16, 255);
        static readonly Color32 EdgeLift = new Color32(255, 255, 255, 0);
        static readonly Color32 EdgeLiftInner = new Color32(255, 255, 255, 255);
        static readonly Color32 BoardBg = new Color32(16, 17, 24, 26);
        static readonly Color32 SlotLine = new Color32(70, 74, 92, 0);

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int SampleAddId = Shader.PropertyToID("_TextureSampleAdd");
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        struct Piece
        {
            public Vector2 Home, From;
            public float Start;
            public int VertStart, VertCount, StripStart;
            public int[] Tris;
        }

        public int FrameCount { get; }
        public float DurationSec => FrameCount / (float)Fps;

        readonly int width, height;
        readonly float ui; // text/stroke scale relative to a 720 px wide frame
        readonly LevelInfo level;
        readonly Material pieceMat, overlayMat;
        readonly CommandBuffer cb = new CommandBuffer { name = "SnaploopClip" };
        readonly Matrix4x4 view, proj;
        readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        int blockIndex;

        readonly Mesh piecesMesh, boardMesh, quadMesh, topBandMesh, bottomBandMesh;
        Piece[] pieces;
        int[] byStart, byStack;
        Vector3[] verts, baseLocal;
        Color32[] colors;
        readonly List<int> indices = new List<int>();
        Rect boardRect, coverRect;
        float cellMin;
        readonly float revealAt, endAt;

        readonly Font font;
        readonly ClipText title, progress, brand, solved, brandBig, tagline, credit;
        readonly ClipText[] texts;
        bool fontDirty;
        int shownPlaced = -1;

        public ClipScene(LevelInfo level, float videoAspect, int width, int height, Font font)
        {
            this.level = level;
            this.width = width;
            this.height = height;
            this.font = font;
            ui = width / 720f;
            GetMatrices(width, height, out view, out proj);

            var pieceShader = Shader.Find("Snaploop/VideoPiece");
            var overlayShader = Shader.Find("Snaploop/CaptureOverlay");
            if (pieceShader == null || overlayShader == null)
                throw new InvalidOperationException("Clip capture shaders missing (Snaploop/VideoPiece, Snaploop/CaptureOverlay).");
            pieceMat = new Material(pieceShader) { name = "ClipPieces" };
            pieceMat.SetFloat(FadeId, 1f);
            overlayMat = new Material(overlayShader) { name = "ClipOverlay" };

            piecesMesh = new Mesh { name = "ClipPieces" };
            piecesMesh.MarkDynamic();
            boardMesh = new Mesh { name = "ClipBoard" };
            quadMesh = BuildQuad(new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255));
            topBandMesh = BuildQuad(new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 170));
            bottomBandMesh = BuildQuad(new Color32(0, 0, 0, 170), new Color32(0, 0, 0, 0));

            BuildPuzzle(Mathf.Clamp(videoAspect, 0.3f, 3f));

            int n = pieces.Length;
            float gap = Mathf.Clamp(AssemblySec / n, MinSnapGapSec, MaxSnapGapSec);
            for (int k = 0; k < n; k++) pieces[byStart[k]].Start = IntroHoldSec + k * gap;
            revealAt = IntroHoldSec + (n - 1) * gap + FlightSec + SettleSec;
            endAt = revealAt + RevealGrowSec + RevealHoldSec;
            FrameCount = Mathf.CeilToInt((endAt + EndCardSec) * Fps);

            title = new ClipText(font, 46, FontStyle.Bold, level.DisplayTitle);
            progress = new ClipText(font, 34, FontStyle.Normal);
            brand = new ClipText(font, 40, FontStyle.Bold, "Snaploop");
            solved = new ClipText(font, 92, FontStyle.Bold, "Solved!");
            brandBig = new ClipText(font, 124, FontStyle.Bold, "Snaploop");
            tagline = new ClipText(font, 40, FontStyle.Normal, "Assemble the moving picture");
            credit = new ClipText(font, 24, FontStyle.Normal, string.IsNullOrEmpty(level.credit) ? "" : "Video: " + level.credit);
            texts = new[] { title, progress, brand, solved, brandBig, tagline, credit };
            Font.textureRebuilt += OnFontRebuilt;
        }

        public void Dispose()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            cb.Release();
            foreach (var t in texts) t.Destroy();
            UnityEngine.Object.Destroy(piecesMesh);
            UnityEngine.Object.Destroy(boardMesh);
            UnityEngine.Object.Destroy(quadMesh);
            UnityEngine.Object.Destroy(topBandMesh);
            UnityEngine.Object.Destroy(bottomBandMesh);
            UnityEngine.Object.Destroy(pieceMat);
            UnityEngine.Object.Destroy(overlayMat);
        }

        void OnFontRebuilt(Font f)
        {
            if (f == font) fontDirty = true;
        }

        /// <summary>Pixel-space camera: x right, y up, origin at the bottom-left of the output frame.</summary>
        public static void GetMatrices(int w, int h, out Matrix4x4 view, out Matrix4x4 proj)
        {
            view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
            proj = Matrix4x4.Ortho(0f, w, 0f, h, -10f, 10f);
        }

        // ---------------------------------------------------------------- rendering

        public void Render(int frame, Texture video, RenderTexture target)
        {
            float t = frame / (float)Fps;
            pieceMat.SetTexture(MainTexId, video != null ? video : Texture2D.blackTexture);
            UpdateTexts(t);

            cb.Clear();
            blockIndex = 0;
            cb.SetRenderTarget(target);
            cb.ClearRenderTarget(false, true, Palette.Background);
            cb.SetViewProjectionMatrices(view, proj);

            if (t < revealAt) DrawAssembly(t);
            else DrawReveal(t - revealAt);
            DrawOverlays(t);

            Graphics.ExecuteCommandBuffer(cb);
        }

        /// <summary>
        /// Draws white over the bottom half (low y) of <paramref name="probe"/> through the same matrices
        /// and command path as <see cref="Render"/>. Reading it back tells whether the first row of a
        /// readback is the bottom of the frame on this device.
        /// </summary>
        public void RenderOrientationProbe(RenderTexture probe)
        {
            GetMatrices(probe.width, probe.height, out var v, out var p);
            cb.Clear();
            blockIndex = 0;
            cb.SetRenderTarget(probe);
            cb.ClearRenderTarget(false, true, Color.black);
            cb.SetViewProjectionMatrices(v, p);
            DrawRect(new Rect(0f, 0f, probe.width, probe.height * 0.5f), Color.white);
            Graphics.ExecuteCommandBuffer(cb);
        }

        void DrawAssembly(float t)
        {
            cb.DrawMesh(boardMesh, Matrix4x4.identity, pieceMat, 0, 0);

            int n = pieces.Length;
            for (int i = 0; i < n; i++)
            {
                ref Piece p = ref pieces[i];
                float f = (t - p.Start) / FlightSec;
                Vector2 pos;
                float scale = 1f;
                Color32 outer, inner;
                if (f <= 0f)
                {
                    pos = p.From;
                    outer = EdgeDark; inner = EdgeDarkInner;
                }
                else if (f < 1f)
                {
                    float e = 1f - (1f - f) * (1f - f) * (1f - f);
                    pos = Vector2.LerpUnclamped(p.From, p.Home, e);
                    scale = 1f + LiftScale * Mathf.Sin(f * Mathf.PI);
                    outer = EdgeLift; inner = EdgeLiftInner;
                }
                else
                {
                    pos = p.Home;
                    float k = Mathf.Clamp01((t - p.Start - FlightSec) / LandFlashSec);
                    outer = Color32.Lerp(EdgeLift, FillColor, k);
                    inner = Color32.Lerp(EdgeLiftInner, FillColor, k);
                }
                WritePiece(i, pos, scale, outer, inner);
            }

            // Placed pieces at the back, then the loose stack, then pieces in flight (latest on top).
            indices.Clear();
            foreach (int i in byStart) if (t >= pieces[i].Start + FlightSec) indices.AddRange(pieces[i].Tris);
            foreach (int i in byStack) if (t <= pieces[i].Start) indices.AddRange(pieces[i].Tris);
            foreach (int i in byStart) if (t > pieces[i].Start && t < pieces[i].Start + FlightSec) indices.AddRange(pieces[i].Tris);

            piecesMesh.SetVertices(verts);
            piecesMesh.SetColors(colors);
            piecesMesh.SetTriangles(indices, 0, false);
            cb.DrawMesh(piecesMesh, Matrix4x4.identity, pieceMat, 0, 0);
        }

        void DrawReveal(float r)
        {
            float g = Mathf.Clamp01(r / RevealGrowSec);
            float e = 1f - (1f - g) * (1f - g) * (1f - g);
            var rect = new Rect(
                Mathf.Lerp(boardRect.x, coverRect.x, e), Mathf.Lerp(boardRect.y, coverRect.y, e),
                Mathf.Lerp(boardRect.width, coverRect.width, e), Mathf.Lerp(boardRect.height, coverRect.height, e));
            cb.DrawMesh(quadMesh, RectMatrix(rect), pieceMat, 0, 0);
        }

        void DrawOverlays(float t)
        {
            float w = width, h = height;
            float r = t - revealAt;
            float header = 1f - Mathf.Clamp01(r / HeaderFadeSec);
            float end = Mathf.Clamp01((t - endAt) / EndCardFadeSec);

            if (header > 0f)
            {
                DrawMesh(topBandMesh, RectMatrix(new Rect(0f, h * (1f - TopBand - 0.04f), w, h * (TopBand + 0.04f))), Texture2D.whiteTexture, new Color(1f, 1f, 1f, header), false);
                DrawText(title, new Vector2(w * 0.5f, h - h * TopBand * 0.42f), 1f, Palette.Text, header);
                DrawText(progress, new Vector2(w * 0.5f, h - h * TopBand * 0.78f), 1f, Palette.TextDim, header);
            }

            float bottom = 1f - end;
            if (bottom > 0f)
            {
                DrawMesh(bottomBandMesh, RectMatrix(new Rect(0f, 0f, w, h * BottomBand)), Texture2D.whiteTexture, new Color(1f, 1f, 1f, bottom), false);
                DrawText(brand, new Vector2(w * 0.5f, h * BottomBand * 0.36f), 1f, Palette.Accent, bottom);
            }

            if (r >= 0f && r < RevealFlashSec)
                DrawRect(new Rect(0f, 0f, w, h), new Color(1f, 1f, 1f, 0.75f * (1f - r / RevealFlashSec)));

            const float solvedIn = 0.5f, solvedPop = 0.2f, solvedOut = 0.3f;
            float sr = r - solvedIn;
            float solvedEnd = endAt - revealAt - solvedIn;
            if (sr >= 0f && sr < solvedEnd)
            {
                float a = Mathf.Clamp01(sr / solvedPop) * Mathf.Clamp01((solvedEnd - sr) / solvedOut);
                float pop = 1f + 0.18f * (1f - Mathf.Clamp01(sr / solvedPop));
                DrawText(solved, new Vector2(w * 0.5f, h * 0.2f), pop, Palette.Text, a);
            }

            if (end > 0f)
            {
                DrawRect(new Rect(0f, 0f, w, h), new Color(0.03f, 0.035f, 0.05f, 0.62f * end));
                float s = Mathf.Lerp(0.9f, 1f, end);
                DrawText(brandBig, new Vector2(w * 0.5f, h * 0.52f), s, Palette.Accent, end);
                DrawText(tagline, new Vector2(w * 0.5f, h * 0.52f - 90f * ui), 1f, Palette.Text, end);
                DrawText(credit, new Vector2(w * 0.5f, h * 0.05f), 1f, Palette.TextDim, end);
            }
        }

        void UpdateTexts(float t)
        {
            int placed = 0;
            for (int i = 0; i < pieces.Length; i++) if (t >= pieces[i].Start + FlightSec) placed++;
            if (placed != shownPlaced)
            {
                shownPlaced = placed;
                progress.Set(placed + " / " + pieces.Length);
            }
            // Requesting every frame keeps our glyphs in the shared dynamic font atlas when the UI grows it.
            foreach (var x in texts) x.Request();
            bool rebuildAll = fontDirty;
            fontDirty = false;
            foreach (var x in texts) if (rebuildAll || x.NeedsMesh) x.BuildMesh();
        }

        void DrawText(ClipText text, Vector2 baselineCentre, float scale, Color color, float alpha)
        {
            if (text.Text.Length == 0 || alpha <= 0f || font == null) return;
            var tex = font.material != null ? font.material.mainTexture : null;
            if (tex == null) return;
            float s = scale * ui;
            var shadow = Matrix4x4.TRS(new Vector3(baselineCentre.x + 2f * ui, baselineCentre.y - 3f * ui, 0f), Quaternion.identity, new Vector3(s, s, 1f));
            DrawMesh(text.Mesh, shadow, tex, new Color(0f, 0f, 0f, 0.45f * alpha), true);
            var m = Matrix4x4.TRS(new Vector3(baselineCentre.x, baselineCentre.y, 0f), Quaternion.identity, new Vector3(s, s, 1f));
            color.a *= alpha;
            DrawMesh(text.Mesh, m, tex, color, true);
        }

        void DrawRect(Rect r, Color color) => DrawMesh(quadMesh, RectMatrix(r), Texture2D.whiteTexture, color, false);

        void DrawMesh(Mesh mesh, Matrix4x4 matrix, Texture tex, Color color, bool alphaTexture)
        {
            if (blockIndex == blocks.Count) blocks.Add(new MaterialPropertyBlock());
            var b = blocks[blockIndex++];
            b.SetTexture(MainTexId, tex);
            b.SetColor(ColorId, color);
            b.SetVector(SampleAddId, alphaTexture ? new Vector4(1f, 1f, 1f, 0f) : Vector4.zero);
            cb.DrawMesh(mesh, matrix, overlayMat, 0, 0, b);
        }

        static Matrix4x4 RectMatrix(Rect r) =>
            Matrix4x4.TRS(new Vector3(r.center.x, r.center.y, 0f), Quaternion.identity, new Vector3(r.width, r.height, 1f));

        // ---------------------------------------------------------------- geometry

        void BuildPuzzle(float aspect)
        {
            int cols = level.Cols, rows = level.Rows, n = cols * rows;
            var avail = new Rect(width * SideMargin, height * BottomBand, width * (1f - 2f * SideMargin), height * (1f - TopBand - BottomBand));
            float bw = avail.width, bh = bw / aspect;
            if (bh > avail.height) { bh = avail.height; bw = bh * aspect; }
            boardRect = new Rect(avail.center.x - bw * 0.5f, avail.center.y - bh * 0.5f, bw, bh);

            float frameAspect = width / (float)height;
            coverRect = aspect > frameAspect
                ? new Rect(width * 0.5f - height * aspect * 0.5f, 0f, height * aspect, height)
                : new Rect(0f, height * 0.5f - width / aspect * 0.5f, width, width / aspect);

            var cell = new Vector2(bw / cols, bh / rows);
            cellMin = Mathf.Min(cell.x, cell.y);
            var outlines = JigsawShape.Build(cols, rows, new Vector2(bw, bh), StableHash(level.id));

            int total = 0;
            for (int i = 0; i < n; i++) total += outlines[i].Count * 3;
            verts = new Vector3[total];
            baseLocal = new Vector3[total];
            colors = new Color32[total];
            var uvs = new Vector2[total];
            pieces = new Piece[n];
            var tris = new List<int>();

            Vector2 centre = boardRect.center;
            Vector2 half = new Vector2(bw, bh) * 0.5f;
            int v = 0;
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                var outline = outlines[idx];
                int m = outline.Count;
                Vector2 homeLocal = new Vector2((c + 0.5f) * cell.x, (r + 0.5f) * cell.y) - half;
                ref Piece p = ref pieces[idx];
                p.Home = centre + homeLocal;
                p.VertStart = v;
                p.VertCount = 3 * m;
                p.StripStart = v + m;

                tris.Clear();
                for (int k = 0; k < m; k++)
                {
                    Vector2 bp = outline[k];
                    Vector2 local = bp - homeLocal;
                    baseLocal[v + k] = new Vector3(local.x, local.y, 0f);
                    uvs[v + k] = new Vector2((bp.x + half.x) / bw, (bp.y + half.y) / bh);
                    colors[v + k] = FillColor;
                }
                Triangulator.Triangulate(outline, tris, v);

                float sw = StripWidth * cellMin;
                int so = v + m;
                for (int k = 0; k < m; k++)
                {
                    Vector2 prev = outline[(k + m - 1) % m], cur = outline[k], next = outline[(k + 1) % m];
                    Vector2 innerPt = cur + InsetDirection(prev, cur, next) * sw;
                    Vector2 innerLocal = innerPt - homeLocal;
                    baseLocal[so + 2 * k] = baseLocal[v + k];
                    baseLocal[so + 2 * k + 1] = new Vector3(innerLocal.x, innerLocal.y, 0f);
                    uvs[so + 2 * k] = uvs[v + k];
                    uvs[so + 2 * k + 1] = new Vector2((innerPt.x + half.x) / bw, (innerPt.y + half.y) / bh);
                    int a0 = so + 2 * k, a1 = a0 + 1;
                    int b0 = so + 2 * ((k + 1) % m), b1 = b0 + 1;
                    tris.Add(a0); tris.Add(b0); tris.Add(b1);
                    tris.Add(a0); tris.Add(b1); tris.Add(a1);
                }
                p.Tris = tris.ToArray();
                v += 3 * m;
            }

            Scramble(StableHash(level.id) ^ 0x51A9, cell);

            piecesMesh.Clear();
            piecesMesh.indexFormat = total > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            for (int i = 0; i < n; i++) WritePiece(i, pieces[i].From, 1f, EdgeDark, EdgeDarkInner);
            piecesMesh.SetVertices(verts);
            piecesMesh.SetUVs(0, uvs);
            piecesMesh.SetColors(colors);
            piecesMesh.bounds = new Bounds(new Vector3(width * 0.5f, height * 0.5f, 0f), new Vector3(width * 4f, height * 4f, 100f));

            BuildBoardMesh(outlines, centre, half, bw, bh);
        }

        /// <summary>Every piece starts on another piece's slot (one random cycle), loosely spread over the board.</summary>
        void Scramble(int seed, Vector2 cell)
        {
            int n = pieces.Length;
            var rng = new System.Random(seed);
            var perm = new int[n];
            for (int i = 0; i < n; i++) perm[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }

            Vector2 centre = boardRect.center;
            float hx = cell.x * 0.5f, hy = cell.y * 0.5f;
            for (int i = 0; i < n; i++)
            {
                Vector2 target = pieces[perm[i]].Home + new Vector2(
                    ((float)rng.NextDouble() * 2f - 1f) * ScrambleJitter * cell.x,
                    ((float)rng.NextDouble() * 2f - 1f) * ScrambleJitter * cell.y);
                target = centre + (target - centre) * ScrambleSpread;
                target.x = Mathf.Clamp(target.x, hx, width - hx);
                target.y = Mathf.Clamp(target.y, height * BottomBand + hy, height * (1f - TopBand) - hy);
                pieces[i].From = target;
            }

            byStack = Shuffled(n, rng);
            byStart = Shuffled(n, rng);
        }

        static int[] Shuffled(int n, System.Random rng)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
            return a;
        }

        void WritePiece(int i, Vector2 pos, float scale, Color32 outer, Color32 inner)
        {
            ref Piece p = ref pieces[i];
            int end = p.VertStart + p.VertCount;
            for (int k = p.VertStart; k < end; k++)
            {
                Vector3 b = baseLocal[k];
                verts[k] = new Vector3(pos.x + b.x * scale, pos.y + b.y * scale, 0f);
            }
            for (int k = p.StripStart; k < end; k += 2)
            {
                colors[k] = outer;
                colors[k + 1] = inner;
            }
        }

        void BuildBoardMesh(List<Vector2>[] outlines, Vector2 centre, Vector2 half, float bw, float bh)
        {
            var bv = new List<Vector3>();
            var buv = new List<Vector2>();
            var bc = new List<Color32>();
            var bt = new List<int>();

            float pad = cellMin * 0.04f;
            AddQuad(bv, buv, bc, bt, new Rect(boardRect.xMin - pad, boardRect.yMin - pad, bw + 2 * pad, bh + 2 * pad), BoardBg,
                new Rect(-pad / bw, -pad / bh, 1f + 2 * pad / bw, 1f + 2 * pad / bh));

            float lw = Mathf.Max(1f, SlotLineWidth * cellMin) * 0.5f;
            foreach (var outline in outlines)
            {
                int m = outline.Count;
                int start = bv.Count;
                for (int k = 0; k < m; k++)
                {
                    Vector2 prev = outline[(k + m - 1) % m], cur = outline[k], next = outline[(k + 1) % m];
                    Vector2 d = InsetDirection(prev, cur, next) * lw;
                    Vector2 a = centre + cur - d, b = centre + cur + d;
                    bv.Add(new Vector3(a.x, a.y, 0f));
                    bv.Add(new Vector3(b.x, b.y, 0f));
                    var uv = new Vector2((cur.x + half.x) / bw, (cur.y + half.y) / bh);
                    buv.Add(uv);
                    buv.Add(uv);
                    bc.Add(SlotLine);
                    bc.Add(SlotLine);
                }
                for (int k = 0; k < m; k++)
                {
                    int a0 = start + 2 * k, a1 = a0 + 1;
                    int b0 = start + 2 * ((k + 1) % m), b1 = b0 + 1;
                    bt.Add(a0); bt.Add(b0); bt.Add(b1);
                    bt.Add(a0); bt.Add(b1); bt.Add(a1);
                }
            }

            boardMesh.Clear();
            boardMesh.indexFormat = bv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            boardMesh.SetVertices(bv);
            boardMesh.SetUVs(0, buv);
            boardMesh.SetColors(bc);
            boardMesh.SetTriangles(bt, 0, true);
        }

        static void AddQuad(List<Vector3> v, List<Vector2> uv, List<Color32> c, List<int> t, Rect r, Color32 col, Rect uvRect)
        {
            int s = v.Count;
            v.Add(new Vector3(r.xMin, r.yMin, 0f)); uv.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            v.Add(new Vector3(r.xMax, r.yMin, 0f)); uv.Add(new Vector2(uvRect.xMax, uvRect.yMin));
            v.Add(new Vector3(r.xMax, r.yMax, 0f)); uv.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            v.Add(new Vector3(r.xMin, r.yMax, 0f)); uv.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            for (int i = 0; i < 4; i++) c.Add(col);
            t.Add(s); t.Add(s + 2); t.Add(s + 1);
            t.Add(s); t.Add(s + 3); t.Add(s + 2);
        }

        /// <summary>Unit quad centred on the origin, UV 0..1, with separate bottom and top vertex colours.</summary>
        static Mesh BuildQuad(Color32 bottom, Color32 top)
        {
            var m = new Mesh { name = "ClipQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.colors32 = new[] { bottom, bottom, top, top };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return m;
        }

        static Vector2 InsetDirection(Vector2 prev, Vector2 cur, Vector2 next)
        {
            Vector2 e0 = cur - prev, e1 = next - cur;
            Vector2 n0 = new Vector2(-e0.y, e0.x).normalized;
            Vector2 n1 = new Vector2(-e1.y, e1.x).normalized;
            Vector2 n = n0 + n1;
            if (n.sqrMagnitude < 1e-8f) return n0;
            n.Normalize();
            float cos = Vector2.Dot(n, n0);
            return n / Mathf.Max(cos, 0.5f);
        }

        // Must match PuzzleBoard.StableHash so the clip shows the same cut the player solved.
        static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char ch in s) h = h * 31 + ch;
                return h;
            }
        }
    }
}
