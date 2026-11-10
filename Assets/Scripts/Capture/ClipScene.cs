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
    /// Deterministic replay of a solved puzzle for the share clip: the scrambled grid is solved one swap at a
    /// time (a tile lifts into its cell, the tile there slides into the cell it left), landed tiles join into
    /// one shape, the board grows into the full uncut loop, then a Snaploop end card.
    ///
    /// Drawn offscreen with a CommandBuffer into a RenderTexture: no camera, no scene objects, nothing
    /// the game camera can see. Frame n always looks the same, so the clip can be rendered at whatever
    /// speed the device manages. It uses the same tile grid (<see cref="TileBatch"/>, <see cref="TileGroups"/>)
    /// and the same Snaploop/VideoTile shader as the puzzle. Coordinates are output pixels, origin bottom-left.
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
        const float SlideSec = 0.24f;
        const float SettleSec = 0.3f;
        const float RevealGrowSec = 0.6f;
        const float RevealFlashSec = 0.45f;
        const float RevealHoldSec = 2.6f;
        const float HeaderFadeSec = 0.3f;
        const float EndCardSec = 2.0f;
        const float EndCardFadeSec = 0.35f;

        // Tile look, fractions of the shorter cell side (same as PuzzleBoard).
        const float CornerRadius = 0.07f, GapWidth = 0.06f, OutlineWidth = 0.025f;
        const float LiftScale = 0.08f;

        // Layout, fractions of the output frame.
        const float TopBand = 0.17f, BottomBand = 0.12f, SideMargin = 0.06f;

        static readonly Color32 TileColor = new Color32(255, 255, 255, 255);
        static readonly Color32 SlotColor = new Color32(62, 66, 86, 30);

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int SampleAddId = Shader.PropertyToID("_TextureSampleAdd");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int GapId = Shader.PropertyToID("_Gap");
        static readonly int OutlineId = Shader.PropertyToID("_Outline");

        public int FrameCount { get; }
        public float DurationSec => FrameCount / (float)Fps;

        readonly int width, height;
        readonly float ui; // text/stroke scale relative to a 720 px wide frame
        readonly LevelInfo level;
        readonly Material tileMat, overlayMat;
        readonly CommandBuffer cb = new CommandBuffer { name = "SnaploopClip" };
        readonly Matrix4x4 view, proj;
        readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        int blockIndex;

        readonly TileBatch tiles, slots;
        readonly TileGroups groups = new TileGroups();
        readonly Mesh revealMesh, quadMesh, topBandMesh, bottomBandMesh;

        // Per tile.
        int[] startCell;
        float[] landAt;
        bool[] landed;
        int[] byLanding, drawOrder, layer;
        int joinedCount = -1;

        // Per swap step: the mover lifts from its cell into its home cell, the tile there slides back.
        int[] stepMover, stepMoverFrom, stepPushed;
        float[] stepStart;
        int stepCount;

        Rect boardRect, coverRect;
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

            var tileShader = Shader.Find("Snaploop/VideoTile");
            var overlayShader = Shader.Find("Snaploop/CaptureOverlay");
            if (tileShader == null || overlayShader == null)
                throw new InvalidOperationException("Clip capture shaders missing (Snaploop/VideoTile, Snaploop/CaptureOverlay).");
            tileMat = new Material(tileShader) { name = "ClipTiles" };
            tileMat.SetFloat(FadeId, 1f);
            overlayMat = new Material(overlayShader) { name = "ClipOverlay" };
            tiles = new TileBatch("ClipTiles");
            slots = new TileBatch("ClipSlots");

            revealMesh = BuildRevealQuad();
            quadMesh = BuildQuad(new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255));
            topBandMesh = BuildQuad(new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 170));
            bottomBandMesh = BuildQuad(new Color32(0, 0, 0, 170), new Color32(0, 0, 0, 0));

            BuildPuzzle(Mathf.Clamp(videoAspect, 0.3f, 3f));

            float gap = Mathf.Clamp(AssemblySec / Mathf.Max(1, stepCount), MinSnapGapSec, MaxSnapGapSec);
            for (int k = 0; k < stepCount; k++) stepStart[k] = IntroHoldSec + k * gap;
            PlanLandings();
            revealAt = IntroHoldSec + Mathf.Max(0, stepCount - 1) * gap + FlightSec + SettleSec;
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
            UnityEngine.Object.Destroy(tiles.Mesh);
            UnityEngine.Object.Destroy(slots.Mesh);
            UnityEngine.Object.Destroy(revealMesh);
            UnityEngine.Object.Destroy(quadMesh);
            UnityEngine.Object.Destroy(topBandMesh);
            UnityEngine.Object.Destroy(bottomBandMesh);
            UnityEngine.Object.Destroy(tileMat);
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
            tileMat.SetTexture(MainTexId, video != null ? video : Texture2D.blackTexture);
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

        int LandedCount(float t)
        {
            // byLanding is sorted by landing time, so the landed tiles are always a prefix of it.
            int k = 0;
            while (k < byLanding.Length && t >= landAt[byLanding[k]]) k++;
            return k;
        }

        void DrawAssembly(float t)
        {
            int n = tiles.Count;
            int landedCount = LandedCount(t);
            UpdateJoins(landedCount);

            // Replay the swaps that have started; later steps override earlier ones for the same tile.
            for (int i = 0; i < n; i++)
            {
                tiles.SetTile(i, tiles.HomeOf(startCell[i]), 1f);
                layer[i] = 1;
            }
            for (int k = 0; k < stepCount && t > stepStart[k]; k++)
            {
                int m = stepMover[k], p = stepPushed[k];
                Vector2 a = tiles.HomeOf(stepMoverFrom[k]), b = tiles.HomeOf(m);
                float f = Mathf.Clamp01((t - stepStart[k]) / FlightSec);
                float e = 1f - (1f - f) * (1f - f) * (1f - f);
                tiles.SetTile(m, Vector2.LerpUnclamped(a, b, e), 1f + LiftScale * Mathf.Sin(f * Mathf.PI));
                layer[m] = f < 1f ? 3 : 1;
                float sf = Mathf.Clamp01((t - stepStart[k] - FlightSec * 0.5f) / SlideSec);
                tiles.SetTile(p, Vector2.Lerp(b, a, sf), 1f);
                layer[p] = sf > 0f && sf < 1f ? 2 : 1;
            }
            for (int k = 0; k < landedCount; k++) layer[byLanding[k]] = 0;

            // Landed tiles at the back, then resting tiles, then sliding tiles, then the lifted one on top.
            int w = 0;
            for (int l = 0; l <= 3; l++)
                for (int i = 0; i < n; i++)
                    if (layer[i] == l) drawOrder[w++] = i;
            tiles.SetDrawOrder(drawOrder);
            tiles.Flush();

            cb.DrawMesh(slots.Mesh, Matrix4x4.identity, tileMat, 0, 0);
            cb.DrawMesh(tiles.Mesh, Matrix4x4.identity, tileMat, 0, 0);
        }

        /// <summary>Joins every landed tile with its landed grid neighbours and refreshes the outlines.</summary>
        void UpdateJoins(int landedCount)
        {
            if (landedCount == joinedCount) return;
            joinedCount = landedCount;
            int n = tiles.Count;
            groups.Reset(level.Cols, level.Rows);
            for (int i = 0; i < n; i++) landed[i] = false;
            for (int k = 0; k < landedCount; k++) landed[byLanding[k]] = true;
            for (int k = 0; k < landedCount; k++)
            {
                int i = byLanding[k];
                for (int s = 0; s < 4; s++)
                {
                    int nb = groups.Neighbor(i, (TileGroups.Side)s);
                    if (nb >= 0 && landed[nb] && !groups.SameGroup(i, nb)) groups.Merge(groups.GroupOf(nb), groups.GroupOf(i));
                }
            }
            for (int i = 0; i < n; i++) tiles.SetEdges(i, groups.EdgeMask(i));
        }

        void DrawReveal(float r)
        {
            float g = Mathf.Clamp01(r / RevealGrowSec);
            float e = 1f - (1f - g) * (1f - g) * (1f - g);
            var rect = new Rect(
                Mathf.Lerp(boardRect.x, coverRect.x, e), Mathf.Lerp(boardRect.y, coverRect.y, e),
                Mathf.Lerp(boardRect.width, coverRect.width, e), Mathf.Lerp(boardRect.height, coverRect.height, e));
            cb.DrawMesh(revealMesh, RectMatrix(rect), tileMat, 0, 0);
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
            int placed = Mathf.Min(LandedCount(t), tiles.Count);
            if (placed != shownPlaced)
            {
                shownPlaced = placed;
                progress.Set(placed + " / " + tiles.Count);
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
            float cellMin = Mathf.Min(cell.x, cell.y);
            tileMat.SetFloat(RadiusId, CornerRadius * cellMin);
            tileMat.SetFloat(GapId, GapWidth * cellMin);
            tileMat.SetFloat(OutlineId, OutlineWidth * cellMin);

            tiles.Build(cols, rows, boardRect, TileColor);
            slots.Build(cols, rows, boardRect, SlotColor);
            slots.Flush();
            groups.Reset(cols, rows);

            startCell = new int[n];
            landAt = new float[n];
            landed = new bool[n];
            drawOrder = new int[n];
            layer = new int[n];
            stepMover = new int[n];
            stepMoverFrom = new int[n];
            stepPushed = new int[n];
            stepStart = new float[n];
            PlanSwaps(new System.Random(unchecked(level.Index * 7919 + cols * 131 + rows)));
        }

        /// <summary>
        /// Scrambles onto cells like the game (no tile home, no neighbours joined), then plans one swap per step in
        /// a random order: the mover goes to its home cell and the tile there takes the mover's old cell.
        /// </summary>
        void PlanSwaps(System.Random rng)
        {
            int n = tiles.Count, cols = level.Cols, rows = level.Rows;
            SwapGrid.Scramble(cols, rows, rng, startCell);
            var cellOf = (int[])startCell.Clone();
            var tileAt = new int[n];
            for (int i = 0; i < n; i++) tileAt[cellOf[i]] = i;

            // A tile still sliding from a recent step should not move again yet, or it would jump.
            int rest = Mathf.CeilToInt((FlightSec * 0.5f + SlideSec) / MinSnapGapSec);
            var lastTouched = new int[n];
            for (int i = 0; i < n; i++) lastTouched[i] = -rest;
            var pending = new List<int>(Shuffled(n, rng));
            stepCount = 0;
            while (true)
            {
                pending.RemoveAll(x => cellOf[x] == x);
                if (pending.Count == 0) break;
                int pick = 0;
                for (int k = 0; k < pending.Count; k++)
                {
                    int c = pending[k];
                    if (stepCount - lastTouched[c] >= rest && stepCount - lastTouched[tileAt[c]] >= rest) { pick = k; break; }
                }
                int i = pending[pick];
                int from = cellOf[i], pushed = tileAt[i];
                stepMover[stepCount] = i;
                stepMoverFrom[stepCount] = from;
                // The last swap of a cycle lands both tiles; the pushed one then needs no separate step.
                stepPushed[stepCount] = pushed;
                lastTouched[i] = lastTouched[pushed] = stepCount;
                stepCount++;
                cellOf[i] = i; tileAt[i] = i;
                cellOf[pushed] = from; tileAt[from] = pushed;
            }
        }

        /// <summary>When each tile reaches its home cell for good; tiles never leave home once there.</summary>
        void PlanLandings()
        {
            int n = tiles.Count;
            for (int i = 0; i < n; i++) landAt[i] = startCell[i] == i ? 0f : float.MaxValue;
            for (int k = 0; k < stepCount; k++)
            {
                landAt[stepMover[k]] = stepStart[k] + FlightSec;
                int p = stepPushed[k];
                if (stepMoverFrom[k] == p) landAt[p] = stepStart[k] + FlightSec * 0.5f + SlideSec;
            }
            byLanding = new int[n];
            for (int i = 0; i < n; i++) byLanding[i] = i;
            Array.Sort(byLanding, (a, b) => landAt[a].CompareTo(landAt[b]));
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

        /// <summary>Unit quad for the tile shader with no exposed edges, so it draws as a plain unclipped video quad.</summary>
        static Mesh BuildRevealQuad()
        {
            var m = BuildQuad(TileColor, TileColor);
            m.name = "ClipReveal";
            var zero = new[] { Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero };
            m.SetUVs(1, zero);
            m.SetUVs(2, zero);
            m.SetUVs(3, zero);
            return m;
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
    }
}
