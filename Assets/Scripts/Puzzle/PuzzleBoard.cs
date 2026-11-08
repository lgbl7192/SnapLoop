using System;
using System.Collections.Generic;
using Snaploop.Core;
using Snaploop.Levels;
using Snaploop.Video;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace Snaploop.Puzzle
{
    /// <summary>
    /// Runs one puzzle: builds jigsaw geometry over the shared video texture, scrambles, handles
    /// drag / magnetism / snap, hints, completion and the reveal.
    ///
    /// Rendering: every piece lives in ONE mesh drawn with ONE material that samples the video
    /// RenderTexture by UV. Moving a piece rewrites its slice of a persistent vertex array and
    /// uploads once per frame, so there are no per-frame allocations and pieces cost a single draw call.
    /// Stacking uses depth (each loose piece gets its own z), so no index-buffer reordering.
    /// </summary>
    public sealed class PuzzleBoard : MonoBehaviour
    {
        enum State { Idle, Loading, Intro, Scrambling, Playing, Settling, Reveal, Done }

        struct Piece
        {
            public Vector2 Home;
            public Vector2 Pos;
            public float Scale;
            public float TargetScale;
            public bool Placed;
            public int Order;

            public bool Tweening;
            public Vector2 TweenFrom, TweenTo;
            public float TweenTime, TweenDuration, TweenDelay;

            public int VertStart, VertCount, StripStart;
            public Vector2[] Outline; // local to Home, for hit testing
        }

        // Tuning (fractions of the shorter cell side unless noted).
        const float SnapRadius = 0.32f;
        const float MagnetRadius = 0.6f;
        const float MagnetStrength = 0.35f;
        const float PickSlack = 0.55f;
        const float StripWidth = 0.05f;
        const float SlotLineWidth = 0.014f;
        const float ScrambleJitter = 0.18f;
        const float LiftScale = 1.07f;
        const float SnapTweenSec = 0.11f;
        const float HintTweenSec = 0.45f;
        const float IntroHoldSec = 0.75f;
        const float ScrambleTweenSec = 0.55f;
        const float RevealGrowSec = 0.6f;
        const float RevealHoldSec = 1.1f;
        const byte GhostAmount = 26; // 0-255 video strength in empty slots

        const float ZBoard = 2f, ZSlots = 1.9f, ZPlaced = 1f, ZLooseBase = 0.5f, ZStep = 0.01f, ZStripOffset = -0.004f;

        static readonly Color32 FillColor = new Color32(255, 255, 255, 255);
        static readonly Color32 EdgeDark = new Color32(12, 12, 16, 0);
        static readonly Color32 EdgeDarkInner = new Color32(12, 12, 16, 255);
        static readonly Color32 EdgeLift = new Color32(255, 255, 255, 0);
        static readonly Color32 EdgeLiftInner = new Color32(255, 255, 255, 255);
        static readonly Color32 BoardBg = new Color32(18, 15, 51, GhostAmount);
        static readonly Color32 SlotLine = new Color32(92, 80, 190, 0);
        const float SnapPopScale = 1.08f;
        const float ShadowAlpha = 0.5f;

        public event Action<int, int> ProgressChanged;
        /// <summary>A placed piece finished settling into its slot (screen position of its centre). Visual only.</summary>
        public event Action<Vector2> PieceLanded;
        public event Action<PuzzleResult> Completed;
        public event Action<PuzzleResult> RevealFinished;
        public event Action<bool> LevelReady;

        public bool IsPlaying => state == State.Playing;
        public bool InputEnabled { get; set; } = true;
        public int PlacedCount => placedCount;
        public int PieceCount => pieces?.Length ?? 0;
        public LevelInfo Level => level;
        public float Elapsed => elapsed;

        Camera cam;
        VideoFeed feed;
        Material material;
        Mesh piecesMesh, boardMesh;
        GameObject piecesGo, boardGo, revealGo;
        MeshRenderer piecesRenderer, boardRenderer, revealRenderer;
        SpriteRenderer dragShadow;
        int shadowPiece = -1;

        Piece[] pieces;
        int[] order;      // piece indices from bottom to top of the loose stack
        Vector3[] verts;
        Vector3[] baseLocal;
        Color32[] colors;
        bool vertsDirty, colorsDirty;

        LevelInfo level;
        State state;
        Rect playArea;     // world units
        Rect boardRect;    // world units
        Rect playAreaScreen;
        float cellMin;
        Vector2 cellSize;
        int placedCount;
        int hintsUsed;
        float elapsed;
        float stateTime;
        float fade;
        bool forcedWin;
        PuzzleResult result;

        int dragIndex = -1;
        int dragFinger = -1;
        Vector2 grabOffset;
        Vector2 dragStartPos;
        int loadToken;

        static readonly int FadeId = Shader.PropertyToID("_Fade");

        public void Init(Camera camera, VideoFeed videoFeed)
        {
            cam = camera;
            feed = videoFeed;
            var shader = Shader.Find("Snaploop/VideoPiece");
            if (shader == null)
            {
                Debug.LogError("[Snaploop] Shader Snaploop/VideoPiece missing; falling back to Unlit/Texture (no edges).");
                shader = Shader.Find("Unlit/Texture");
            }
            material = new Material(shader) { name = "SnaploopPieces" };

            boardGo = MakeRendererObject("Board", out boardRenderer);
            piecesGo = MakeRendererObject("Pieces", out piecesRenderer);
            revealGo = MakeRendererObject("Reveal", out revealRenderer);
            revealGo.GetComponent<MeshFilter>().sharedMesh = BuildQuad();
            revealGo.SetActive(false);

            piecesMesh = new Mesh { name = "PiecesMesh" };
            piecesMesh.MarkDynamic();
            piecesGo.GetComponent<MeshFilter>().sharedMesh = piecesMesh;
            boardMesh = new Mesh { name = "BoardMesh" };
            boardGo.GetComponent<MeshFilter>().sharedMesh = boardMesh;

            // Soft shadow under the lifted piece. Transparent, so it draws over pieces behind the
            // dragged one and is hidden by the dragged piece itself (which writes depth).
            var shadowGo = new GameObject("DragShadow");
            shadowGo.transform.SetParent(transform, false);
            dragShadow = shadowGo.AddComponent<SpriteRenderer>();
            dragShadow.sprite = Snaploop.UI.UISprites.Shadow;
            dragShadow.drawMode = SpriteDrawMode.Sliced;
            dragShadow.color = new Color(0.02f, 0.01f, 0.08f, 0f);
            shadowGo.SetActive(false);
            SetVisible(false);
        }

        GameObject MakeRendererObject(string name, out MeshRenderer mr)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        void SetVisible(bool v)
        {
            piecesGo.SetActive(v);
            boardGo.SetActive(v);
            if (!v) revealGo.SetActive(false);
            if (!v) { shadowPiece = -1; dragShadow.gameObject.SetActive(false); }
        }

        /// <summary>Screen-pixel rectangle the puzzle may use (between the HUD bars).</summary>
        public void SetPlayArea(Rect screenRect)
        {
            playAreaScreen = screenRect;
        }

        // ---------------------------------------------------------------- level lifecycle

        public void Begin(LevelInfo lvl)
        {
            Clear();
            level = lvl;
            state = State.Loading;
            int token = ++loadToken;
            feed.Load(lvl, ok =>
            {
                if (token != loadToken) return;
                material.mainTexture = feed.Texture;
                BuildLevel(feed.Aspect);
                state = State.Intro;
                stateTime = 0f;
                fade = 0f;
                SetVisible(true);
                GameContext.IsPuzzleInProgress = true;
                LevelReady?.Invoke(ok);
                ProgressChanged?.Invoke(placedCount, pieces.Length);
                GameEvents.RaisePuzzleStarted(level, level.Index);
            });
        }

        /// <summary>Stops the current puzzle and hides everything (back to menu).</summary>
        public void Clear()
        {
            loadToken++;
            if (GameContext.IsPuzzleInProgress && state != State.Reveal && state != State.Done && level != null)
                GameEvents.RaisePuzzleAbandoned(level, level.Index);
            GameContext.IsPuzzleInProgress = false;
            state = State.Idle;
            dragIndex = -1;
            dragFinger = -1;
            if (piecesGo != null) SetVisible(false);
        }

        public void StopVideo() => feed.Stop();

        void BuildLevel(float aspect)
        {
            int cols = level.Cols, rows = level.Rows, n = cols * rows;
            playArea = ScreenToWorldRect(playAreaScreen);

            // Fit the clip aspect inside the play area with a small margin.
            float margin = playArea.width * 0.03f;
            float availW = playArea.width - 2f * margin, availH = playArea.height - 2f * margin;
            float bw = availW, bh = bw / aspect;
            if (bh > availH) { bh = availH; bw = bh * aspect; }
            boardRect = new Rect(playArea.center.x - bw * 0.5f, playArea.center.y - bh * 0.5f, bw, bh);
            GameContext.BoardScreenRect = WorldToScreenRect(boardRect);
            cellSize = new Vector2(bw / cols, bh / rows);
            cellMin = Mathf.Min(cellSize.x, cellSize.y);

            int seed = StableHash(level.id);
            var outlines = JigsawShape.Build(cols, rows, new Vector2(bw, bh), seed);

            // Vertex budget: fill (n verts) + inner bevel strip (2n verts) per piece.
            int totalVerts = 0;
            for (int i = 0; i < n; i++) totalVerts += outlines[i].Count * 3;
            verts = new Vector3[totalVerts];
            baseLocal = new Vector3[totalVerts];
            colors = new Color32[totalVerts];
            var uvs = new Vector2[totalVerts];
            var tris = new List<int>(totalVerts * 3);
            pieces = new Piece[n];
            order = new int[n];

            Vector2 boardCenter = boardRect.center;
            Vector2 half = new Vector2(bw, bh) * 0.5f;
            int v = 0;
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                var outline = outlines[idx];
                int m = outline.Count;
                Vector2 homeLocal = new Vector2((c + 0.5f) * cellSize.x, (r + 0.5f) * cellSize.y) - half;

                ref Piece p = ref pieces[idx];
                p.Home = boardCenter + homeLocal;
                p.Pos = p.Home;
                p.Scale = 1f;
                p.TargetScale = 1f;
                p.VertStart = v;
                p.VertCount = 3 * m;
                p.StripStart = v + m;
                p.Outline = new Vector2[m];

                for (int k = 0; k < m; k++)
                {
                    Vector2 bp = outline[k];
                    Vector2 local = bp - homeLocal;
                    p.Outline[k] = local;
                    baseLocal[v + k] = new Vector3(local.x, local.y, 0f);
                    uvs[v + k] = new Vector2((bp.x + half.x) / bw, (bp.y + half.y) / bh);
                    colors[v + k] = FillColor;
                }
                Triangulator.Triangulate(outline, tris, v);

                // Inner bevel strip: outer ring on the outline, inner ring inset toward the interior.
                float w = StripWidth * cellMin;
                int so = v + m;
                for (int k = 0; k < m; k++)
                {
                    Vector2 prev = outline[(k + m - 1) % m], cur = outline[k], next = outline[(k + 1) % m];
                    Vector2 inner = cur + InsetDirection(prev, cur, next) * w;
                    Vector2 innerLocal = inner - homeLocal;
                    baseLocal[so + 2 * k] = new Vector3(p.Outline[k].x, p.Outline[k].y, ZStripOffset);
                    baseLocal[so + 2 * k + 1] = new Vector3(innerLocal.x, innerLocal.y, ZStripOffset);
                    uvs[so + 2 * k] = uvs[v + k];
                    uvs[so + 2 * k + 1] = new Vector2((inner.x + half.x) / bw, (inner.y + half.y) / bh);
                    int a0 = so + 2 * k, a1 = so + 2 * k + 1;
                    int b0 = so + 2 * ((k + 1) % m), b1 = b0 + 1;
                    tris.Add(a0); tris.Add(b0); tris.Add(b1);
                    tris.Add(a0); tris.Add(b1); tris.Add(a1);
                }
                v += 3 * m;
                order[idx] = idx;
                p.Order = idx;
            }

            placedCount = 0;
            hintsUsed = 0;
            elapsed = 0f;
            forcedWin = false;
            for (int i = 0; i < n; i++) SetEdgeColors(i, EdgeDark, EdgeDarkInner);

            piecesMesh.Clear();
            piecesMesh.indexFormat = totalVerts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            for (int i = 0; i < n; i++) WritePiece(i);
            piecesMesh.SetVertices(verts);
            piecesMesh.SetUVs(0, uvs);
            piecesMesh.SetColors(colors);
            piecesMesh.SetTriangles(tris, 0, false);
            piecesMesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
            vertsDirty = colorsDirty = false;

            BuildBoardMesh(outlines, homeOffset: boardCenter, half, bw, bh);
            revealGo.SetActive(false);
        }

        void BuildBoardMesh(List<Vector2>[] outlines, Vector2 homeOffset, Vector2 half, float bw, float bh)
        {
            var bv = new List<Vector3>();
            var buv = new List<Vector2>();
            var bc = new List<Color32>();
            var bt = new List<int>();

            // Background with a faint ghost of the video so empty slots read as "the picture goes here".
            float pad = cellMin * 0.04f;
            AddQuad(bv, buv, bc, bt, new Rect(boardRect.xMin - pad, boardRect.yMin - pad, bw + 2 * pad, bh + 2 * pad), ZBoard, BoardBg,
                new Rect(-pad / bw, -pad / bh, 1f + 2 * pad / bw, 1f + 2 * pad / bh));

            // Slot outlines (thin lines centred on each piece outline).
            float w = SlotLineWidth * cellMin * 0.5f;
            foreach (var outline in outlines)
            {
                int m = outline.Count;
                int start = bv.Count;
                for (int k = 0; k < m; k++)
                {
                    Vector2 prev = outline[(k + m - 1) % m], cur = outline[k], next = outline[(k + 1) % m];
                    Vector2 d = InsetDirection(prev, cur, next) * w;
                    Vector2 a = homeOffset + cur - d, b = homeOffset + cur + d;
                    bv.Add(new Vector3(a.x, a.y, ZSlots));
                    bv.Add(new Vector3(b.x, b.y, ZSlots));
                    buv.Add(new Vector2((cur.x + half.x) / bw, (cur.y + half.y) / bh));
                    buv.Add(buv[buv.Count - 1]);
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

        static void AddQuad(List<Vector3> v, List<Vector2> uv, List<Color32> c, List<int> t, Rect r, float z, Color32 col, Rect uvRect)
        {
            int s = v.Count;
            v.Add(new Vector3(r.xMin, r.yMin, z)); uv.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            v.Add(new Vector3(r.xMax, r.yMin, z)); uv.Add(new Vector2(uvRect.xMax, uvRect.yMin));
            v.Add(new Vector3(r.xMax, r.yMax, z)); uv.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            v.Add(new Vector3(r.xMin, r.yMax, z)); uv.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            for (int i = 0; i < 4; i++) c.Add(col);
            t.Add(s); t.Add(s + 2); t.Add(s + 1);
            t.Add(s); t.Add(s + 3); t.Add(s + 2);
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "RevealQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.colors32 = new[] { FillColor, FillColor, FillColor, FillColor };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return m;
        }

        // Unit vector pointing into a CCW polygon at vertex cur, scaled for a roughly constant inset width.
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

        // ---------------------------------------------------------------- per-frame

        void Update()
        {
            if (state == State.Idle || state == State.Loading || pieces == null) return;
            float dt = Time.deltaTime;
            stateTime += dt;

            if (fade < 1f)
            {
                fade = Mathf.Min(1f, fade + dt / 0.25f);
                material.SetFloat(FadeId, fade);
            }

            switch (state)
            {
                case State.Intro:
                    if (stateTime >= IntroHoldSec) StartScramble();
                    break;
                case State.Playing:
                    if (InputEnabled) elapsed += dt;
                    HandleInput();
                    break;
                case State.Reveal:
                    UpdateReveal();
                    break;
            }

            bool anyTween = UpdateTweens(dt);
            UpdateDragShadow();

            if (state == State.Scrambling && !anyTween)
            {
                state = State.Playing;
                stateTime = 0f;
                GameEvents.RaisePuzzlePlayable(level, level.Index);
            }
            if (state == State.Settling && !anyTween) BeginReveal();

            if (vertsDirty)
            {
                piecesMesh.SetVertices(verts, 0, verts.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                vertsDirty = false;
            }
            if (colorsDirty)
            {
                piecesMesh.SetColors(colors, 0, colors.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                colorsDirty = false;
            }
        }

        bool UpdateTweens(float dt)
        {
            bool any = false;
            for (int i = 0; i < pieces.Length; i++)
            {
                ref Piece p = ref pieces[i];
                bool changed = false;
                if (p.Tweening)
                {
                    any = true;
                    if (p.TweenDelay > 0f) p.TweenDelay -= dt;
                    else
                    {
                        p.TweenTime += dt;
                        float t = Mathf.Clamp01(p.TweenTime / p.TweenDuration);
                        float e = 1f - (1f - t) * (1f - t) * (1f - t);
                        p.Pos = Vector2.LerpUnclamped(p.TweenFrom, p.TweenTo, e);
                        changed = true;
                        if (t >= 1f)
                        {
                            p.Tweening = false;
                            if (p.Placed) OnPlacedTweenDone(i);
                        }
                    }
                }
                if (!Mathf.Approximately(p.Scale, p.TargetScale))
                {
                    p.Scale = Mathf.MoveTowards(p.Scale, p.TargetScale, dt * 1.2f);
                    changed = true;
                }
                if (changed) WritePiece(i);
            }
            return any;
        }

        void WritePiece(int i)
        {
            ref Piece p = ref pieces[i];
            // A popping placed piece sits just in front of its placed neighbours so the overlap doesn't z-fight.
            float z = p.Placed && !p.Tweening ? (p.Scale > 1.0001f ? ZPlaced - 0.05f : ZPlaced) : ZLooseBase - p.Order * ZStep;
            float s = p.Scale;
            int end = p.VertStart + p.VertCount;
            for (int k = p.VertStart; k < end; k++)
            {
                Vector3 b = baseLocal[k];
                verts[k] = new Vector3(p.Pos.x + b.x * s, p.Pos.y + b.y * s, z + b.z);
            }
            vertsDirty = true;
        }

        void SetEdgeColors(int i, Color32 outer, Color32 inner)
        {
            ref Piece p = ref pieces[i];
            int end = p.VertStart + p.VertCount;
            for (int k = p.StripStart; k < end; k += 2)
            {
                colors[k] = outer;
                colors[k + 1] = inner;
            }
            colorsDirty = true;
        }

        // ---------------------------------------------------------------- scramble

        void StartScramble()
        {
            int n = pieces.Length;
            // Sattolo's algorithm: a random single cycle, so no piece starts in its own slot.
            var perm = new int[n];
            for (int i = 0; i < n; i++) perm[i] = i;
            var rng = new System.Random(Environment.TickCount);
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i);
                int t = perm[i]; perm[i] = perm[j]; perm[j] = t;
            }

            for (int i = 0; i < n; i++)
            {
                ref Piece p = ref pieces[i];
                Vector2 target = pieces[perm[i]].Home + new Vector2(
                    ((float)rng.NextDouble() * 2f - 1f) * ScrambleJitter * cellSize.x,
                    ((float)rng.NextDouble() * 2f - 1f) * ScrambleJitter * cellSize.y);
                StartTween(i, ClampToPlayArea(target), ScrambleTweenSec, (float)rng.NextDouble() * 0.15f);
            }
            // Randomize stacking too.
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = order[i]; order[i] = order[j]; order[j] = t;
            }
            ApplyOrder();
            state = State.Scrambling;
            stateTime = 0f;
        }

        void StartTween(int i, Vector2 to, float duration, float delay = 0f)
        {
            ref Piece p = ref pieces[i];
            p.TweenFrom = p.Pos;
            p.TweenTo = to;
            p.TweenTime = 0f;
            p.TweenDuration = Mathf.Max(0.01f, duration);
            p.TweenDelay = delay;
            p.Tweening = true;
        }

        void ApplyOrder()
        {
            for (int k = 0; k < order.Length; k++)
            {
                pieces[order[k]].Order = k;
                WritePiece(order[k]);
            }
        }

        void BringToFront(int index)
        {
            int at = Array.IndexOf(order, index);
            if (at < 0) return;
            for (int k = at; k < order.Length - 1; k++) order[k] = order[k + 1];
            order[order.Length - 1] = index;
            ApplyOrder();
        }

        // ---------------------------------------------------------------- input

        void HandleInput()
        {
            if (!InputEnabled)
            {
                if (dragIndex >= 0) Release();
                return;
            }

            if (Input.touchCount > 0 || dragFinger >= 0)
            {
                for (int t = 0; t < Input.touchCount; t++)
                {
                    Touch touch = Input.GetTouch(t);
                    if (dragIndex < 0 && touch.phase == TouchPhase.Began)
                    {
                        if (!IsOverUI(touch.fingerId) && TryPick(touch.position)) dragFinger = touch.fingerId;
                    }
                    else if (touch.fingerId == dragFinger)
                    {
                        if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) Release();
                        else Drag(touch.position);
                    }
                }
                // Finger vanished without an Ended phase (e.g. app focus change).
                if (dragFinger >= 0 && Input.touchCount == 0) Release();
            }
            else if (!Input.touchSupported)
            {
                if (Input.GetMouseButtonDown(0) && !IsOverUI(-1)) TryPick(Input.mousePosition);
                else if (dragIndex >= 0 && Input.GetMouseButton(0)) Drag(Input.mousePosition);
                else if (dragIndex >= 0 && !Input.GetMouseButton(0)) Release();
            }
        }

        bool IsOverUI(int pointerId)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return pointerId >= 0 ? es.IsPointerOverGameObject(pointerId) : es.IsPointerOverGameObject();
        }

        bool TryPick(Vector2 screen)
        {
            if (!playAreaScreen.Contains(screen)) return false;
            Vector2 world = ScreenToWorld(screen);
            int hit = -1;
            for (int k = order.Length - 1; k >= 0; k--)
            {
                int i = order[k];
                ref Piece p = ref pieces[i];
                if (p.Placed || p.Tweening) continue;
                if (Triangulator.Contains(p.Outline, (world - p.Pos) / p.Scale)) { hit = i; break; }
            }
            if (hit < 0)
            {
                // Fat-finger fallback: nearest loose piece centre within reach.
                float best = PickSlack * cellMin;
                best *= best;
                for (int i = 0; i < pieces.Length; i++)
                {
                    if (pieces[i].Placed || pieces[i].Tweening) continue;
                    float d = (pieces[i].Pos - world).sqrMagnitude;
                    if (d < best) { best = d; hit = i; }
                }
            }
            if (hit < 0) return false;

            dragIndex = hit;
            ref Piece h = ref pieces[hit];
            grabOffset = h.Pos - world;
            dragStartPos = h.Pos;
            h.TargetScale = LiftScale;
            BringToFront(hit);
            shadowPiece = hit;
            SetEdgeColors(hit, EdgeLift, EdgeLiftInner);
            Sfx.PlayPickup();
            return true;
        }

        void Drag(Vector2 screen)
        {
            ref Piece p = ref pieces[dragIndex];
            Vector2 target = ClampToPlayArea(ScreenToWorld(screen) + grabOffset);
            float d = Vector2.Distance(target, p.Home);
            float radius = MagnetRadius * cellMin;
            if (d < radius)
            {
                float k = 1f - d / radius;
                target = Vector2.Lerp(target, p.Home, MagnetStrength * k * k);
            }
            if (target != p.Pos)
            {
                p.Pos = target;
                WritePiece(dragIndex);
            }
        }

        void Release()
        {
            int i = dragIndex;
            dragIndex = -1;
            dragFinger = -1;
            if (i < 0) return;
            ref Piece p = ref pieces[i];
            p.TargetScale = 1f;
            SetEdgeColors(i, EdgeDark, EdgeDarkInner);
            if (Vector2.Distance(p.Pos, p.Home) <= SnapRadius * cellMin) Place(i, SnapTweenSec, dragStartPos);
        }

        // ---------------------------------------------------------------- placing

        /// <summary>
        /// Locks piece i into its slot. Any loose piece lying on that slot is moved to
        /// <paramref name="vacated"/> (where the placed piece came from) so slots never stay buried.
        /// </summary>
        void Place(int i, float tweenSec, Vector2 vacated)
        {
            ref Piece p = ref pieces[i];
            p.Placed = true;
            p.TargetScale = 1f;
            StartTween(i, p.Home, tweenSec);
            placedCount++;

            float clear = 0.5f * cellMin;
            int displaced = 0;
            for (int k = 0; k < pieces.Length; k++)
            {
                if (k == i || pieces[k].Placed) continue;
                if ((pieces[k].Pos - p.Home).sqrMagnitude < clear * clear)
                {
                    Vector2 offset = new Vector2(displaced * 0.12f, -displaced * 0.12f) * cellMin;
                    StartTween(k, ClampToPlayArea(vacated + offset), 0.25f);
                    BringToFront(k);
                    displaced++;
                }
            }

            Sfx.PlaySnap();
            Haptics.Tick();
            ProgressChanged?.Invoke(placedCount, pieces.Length);
            GameEvents.RaisePieceSnapped(placedCount, pieces.Length);

            if (placedCount == pieces.Length) Complete();
        }

        void OnPlacedTweenDone(int i)
        {
            // Placed pieces drop to the back layer and lose their bevel so the picture becomes seamless.
            SetEdgeColors(i, FillColor, FillColor);
            pieces[i].Scale = SnapPopScale;
            pieces[i].TargetScale = 1f;
            WritePiece(i);
            if (PieceLanded != null && cam != null) PieceLanded(cam.WorldToScreenPoint(pieces[i].Home));
        }

        void UpdateDragShadow()
        {
            if (shadowPiece < 0 || pieces == null || shadowPiece >= pieces.Length)
            {
                if (dragShadow.gameObject.activeSelf) dragShadow.gameObject.SetActive(false);
                return;
            }
            ref Piece p = ref pieces[shadowPiece];
            float lift = p.Placed && !p.Tweening ? 0f : Mathf.Clamp01((p.Scale - 1f) / (LiftScale - 1f));
            if (lift <= 0f && dragIndex != shadowPiece)
            {
                shadowPiece = -1;
                dragShadow.gameObject.SetActive(false);
                return;
            }
            if (!dragShadow.gameObject.activeSelf) dragShadow.gameObject.SetActive(true);
            float z = ZLooseBase - p.Order * ZStep + ZStep * 0.5f;
            dragShadow.transform.localPosition = new Vector3(p.Pos.x + cellMin * 0.05f * lift, p.Pos.y - cellMin * 0.09f * lift, z);
            // Scale the sliced sprite so its baked blur band matches the wanted blur at any cell size.
            float blur = cellMin * 0.22f;
            float k = blur / (Snaploop.UI.UISprites.ShadowBlur / dragShadow.sprite.pixelsPerUnit);
            dragShadow.transform.localScale = new Vector3(k, k, 1f);
            dragShadow.size = new Vector2(cellSize.x * p.Scale + 2f * blur, cellSize.y * p.Scale + 2f * blur) / k;
            dragShadow.color = new Color(0.02f, 0.01f, 0.08f, ShadowAlpha * lift);
        }

        /// <summary>Auto-places one random loose piece. Returns false if nothing to place.</summary>
        public bool ApplyHint()
        {
            if (state != State.Playing) return false;
            if (dragIndex >= 0) Release();
            int loose = 0;
            for (int i = 0; i < pieces.Length; i++) if (!pieces[i].Placed && !pieces[i].Tweening) loose++;
            if (loose == 0) return false;
            int pick = UnityEngine.Random.Range(0, loose);
            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i].Placed || pieces[i].Tweening) continue;
                if (pick-- > 0) continue;
                hintsUsed++;
                BringToFront(i);
                GameEvents.RaiseHintUsed(level, level.Index);
                Place(i, HintTweenSec, pieces[i].Pos);
                return true;
            }
            return false;
        }

        /// <summary>Dev/recording: snaps every remaining piece home and completes the puzzle.</summary>
        public void ForceWin()
        {
            if (state == State.Intro || state == State.Scrambling)
            {
                for (int i = 0; i < pieces.Length; i++) pieces[i].Tweening = false;
                state = State.Playing;
            }
            if (state != State.Playing) return;
            if (dragIndex >= 0) Release();
            forcedWin = true;
            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i].Placed) continue;
                ref Piece p = ref pieces[i];
                p.Placed = true;
                StartTween(i, p.Home, 0.35f, i * 0.015f);
            }
            placedCount = pieces.Length;
            ProgressChanged?.Invoke(placedCount, pieces.Length);
            Complete();
        }

        void Complete()
        {
            state = State.Settling;
            stateTime = 0f;
            int stars = Progress.ComputeStars(pieces.Length, elapsed, hintsUsed);
            bool first = Progress.RecordClear(level.id, stars);
            result = new PuzzleResult
            {
                Level = level,
                LevelIndex = level.Index,
                ElapsedSec = elapsed,
                HintsUsed = hintsUsed,
                Stars = stars,
                IsFirstClear = first,
                ForcedByDevMenu = forcedWin,
            };
            GameContext.IsPuzzleInProgress = false;
            Completed?.Invoke(result);
            GameEvents.RaisePuzzleCompleted(result);
        }

        // ---------------------------------------------------------------- reveal

        Rect revealFrom, revealTo;

        void BeginReveal()
        {
            state = State.Reveal;
            stateTime = 0f;
            feed.Restart();
            Sfx.PlayWin();
            Haptics.Success();

            revealFrom = boardRect;
            Rect screen = ScreenToWorldRect(new Rect(0, 0, Screen.width, Screen.height));
            float aspect = boardRect.width / boardRect.height;
            float w = screen.width, h = w / aspect;
            if (h > screen.height) { h = screen.height; w = h * aspect; }
            revealTo = new Rect(screen.center.x - w * 0.5f, screen.center.y - h * 0.5f, w, h);

            piecesGo.SetActive(false);
            boardGo.SetActive(false);
            revealGo.SetActive(true);
            SetRevealRect(revealFrom);
            GameEvents.RaiseRevealStarted(level, level.Index);
        }

        void UpdateReveal()
        {
            float t = Mathf.Clamp01(stateTime / RevealGrowSec);
            float e = t < 1f ? 1f - Mathf.Pow(1f - t, 3f) : 1f;
            Rect r = new Rect(
                Mathf.Lerp(revealFrom.x, revealTo.x, e), Mathf.Lerp(revealFrom.y, revealTo.y, e),
                Mathf.Lerp(revealFrom.width, revealTo.width, e), Mathf.Lerp(revealFrom.height, revealTo.height, e));
            SetRevealRect(r);
            if (stateTime >= RevealGrowSec + RevealHoldSec)
            {
                state = State.Done;
                GameContext.BoardScreenRect = WorldToScreenRect(revealTo);
                RevealFinished?.Invoke(result);
                GameEvents.RaiseRevealFinished(result);
            }
        }

        void SetRevealRect(Rect r)
        {
            revealGo.transform.position = new Vector3(r.center.x, r.center.y, -1f);
            revealGo.transform.localScale = new Vector3(r.width, r.height, 1f);
        }

        // ---------------------------------------------------------------- helpers

        Vector2 ClampToPlayArea(Vector2 pos)
        {
            float hx = cellSize.x * 0.5f, hy = cellSize.y * 0.5f;
            return new Vector2(
                Mathf.Clamp(pos.x, playArea.xMin + hx, Mathf.Max(playArea.xMin + hx, playArea.xMax - hx)),
                Mathf.Clamp(pos.y, playArea.yMin + hy, Mathf.Max(playArea.yMin + hy, playArea.yMax - hy)));
        }

        Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector3 w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            return new Vector2(w.x, w.y);
        }

        Rect ScreenToWorldRect(Rect s)
        {
            Vector2 a = ScreenToWorld(s.min), b = ScreenToWorld(s.max);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        Rect WorldToScreenRect(Rect w)
        {
            Vector3 a = cam.WorldToScreenPoint(new Vector3(w.xMin, w.yMin, 0f));
            Vector3 b = cam.WorldToScreenPoint(new Vector3(w.xMax, w.yMax, 0f));
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

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
