using System;
using Snaploop.Core;
using Snaploop.Levels;
using Snaploop.Video;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace Snaploop.Puzzle
{
    /// <summary>
    /// Runs one puzzle in free-drag mode: rounded video tiles over the shared video texture, scramble,
    /// drag with mild magnetism, snapping to the board or to a correct neighbour, groups, hints,
    /// completion and the reveal.
    ///
    /// Visuals live in <see cref="TileBatch"/> (one mesh, one material, one draw call for all tiles) and
    /// joins in <see cref="TileGroups"/>; this class only holds the movement rules. A grid-swap mode would
    /// replace the drag/snap rules below and keep both of those.
    ///
    /// Tiles move as groups. A tile's position is its home position plus its group's offset, so every
    /// group keeps its tiles in their correct relative layout; offset zero means "in place". Locked
    /// (placed) tiles all belong to one group at offset zero.
    /// </summary>
    public sealed class PuzzleBoard : MonoBehaviour
    {
        enum State { Idle, Loading, Intro, Scrambling, Playing, Settling, Reveal, Done }

        // Tuning (fractions of the shorter cell side unless noted).
        const float SnapRadius = 0.3f;
        const float MagnetRadius = 0.6f;
        const float MagnetStrength = 0.35f;
        const float PickSlack = 0.55f;
        const float CornerRadius = 0.07f;
        const float GapWidth = 0.06f;
        const float OutlineWidth = 0.025f;
        const float MinOutlinePixels = 1.5f;
        const float ScrambleJitter = 0.06f;
        const float LiftScale = 1.05f;
        const float SnapTweenSec = 0.11f;
        const float HintTweenSec = 0.45f;
        const float IntroHoldSec = 0.75f;
        const float ScrambleTweenSec = 0.55f;
        const float RevealGrowSec = 0.6f;
        const float RevealHoldSec = 1.1f;
        const byte GhostAmount = 30; // 0-255 video strength in empty slots

        static readonly Color32 TileColor = new Color32(255, 255, 255, 255);
        static readonly Color32 SlotColor = new Color32(62, 66, 86, GhostAmount);

        public event Action<int, int> ProgressChanged;
        public event Action<PuzzleResult> Completed;
        public event Action<PuzzleResult> RevealFinished;
        public event Action<bool> LevelReady;

        public bool IsPlaying => state == State.Playing;
        public bool InputEnabled { get; set; } = true;
        public int PlacedCount => placedCount;
        public int PieceCount => tiles?.Count ?? 0;
        public LevelInfo Level => level;
        public float Elapsed => elapsed;

        Camera cam;
        VideoFeed feed;
        Material material;
        GameObject tilesGo, slotsGo, revealGo;
        readonly TileBatch tiles = new TileBatch("Tiles");
        readonly TileBatch slots = new TileBatch("Slots");
        readonly TileGroups groups = new TileGroups();

        // Per tile.
        Vector2[] home;
        int[] drawOrder;   // tile indices, bottom to top
        bool[] placed, fresh;

        // Per group, indexed by group id (only meaningful while TileGroups.IsGroupId(id)).
        Vector2[] offset;
        Vector2[] pivot;   // scale pivot in home coordinates
        float[] scale, targetScale;
        bool[] tweening, snapOnArrival, dirty;
        Vector2[] tweenFrom, tweenTo, vacated;
        float[] tweenTime, tweenDuration, tweenDelay;
        bool[] scratch;

        Vector2[] candidates;
        int candidateCount;
        int lockedGroup = -1;

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

        int dragGroup = -1;
        int dragFinger = -1;
        Vector2 grabOffset;
        Vector2 dragStartOffset;
        Vector2 dragMin, dragMax; // dragged group's bounds at offset zero
        int loadToken;

        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int GapId = Shader.PropertyToID("_Gap");
        static readonly int OutlineId = Shader.PropertyToID("_Outline");

        public void Init(Camera camera, VideoFeed videoFeed)
        {
            cam = camera;
            feed = videoFeed;
            var shader = Shader.Find("Snaploop/VideoTile");
            if (shader == null)
            {
                Debug.LogError("[Snaploop] Shader Snaploop/VideoTile missing; falling back to Unlit/Texture (no outlines).");
                shader = Shader.Find("Unlit/Texture");
            }
            material = new Material(shader) { name = "SnaploopTiles" };

            slotsGo = MakeRendererObject("Slots", slots.Mesh, -1);
            tilesGo = MakeRendererObject("Tiles", tiles.Mesh, 0);
            revealGo = MakeRendererObject("Reveal", BuildQuad(), 1);
            revealGo.SetActive(false);
            SetVisible(false);
        }

        GameObject MakeRendererObject(string name, Mesh mesh, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.sortingOrder = sortingOrder;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        void SetVisible(bool v)
        {
            tilesGo.SetActive(v);
            slotsGo.SetActive(v);
            if (!v) revealGo.SetActive(false);
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
                ProgressChanged?.Invoke(placedCount, tiles.Count);
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
            dragGroup = -1;
            dragFinger = -1;
            if (tilesGo != null) SetVisible(false);
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

            float pixel = playAreaScreen.height > 0f ? playArea.height / playAreaScreen.height : 0f;
            material.SetFloat(RadiusId, CornerRadius * cellMin);
            material.SetFloat(GapId, GapWidth * cellMin);
            material.SetFloat(OutlineId, Mathf.Max(OutlineWidth * cellMin, MinOutlinePixels * pixel));

            tiles.Build(cols, rows, boardRect, TileColor);
            slots.Build(cols, rows, boardRect, SlotColor);
            slots.Flush();
            groups.Reset(cols, rows);

            home = new Vector2[n];
            drawOrder = new int[n];
            placed = new bool[n];
            fresh = new bool[n];
            vacated = new Vector2[n];
            offset = new Vector2[n];
            pivot = new Vector2[n];
            scale = new float[n];
            targetScale = new float[n];
            tweening = new bool[n];
            snapOnArrival = new bool[n];
            dirty = new bool[n];
            tweenFrom = new Vector2[n];
            tweenTo = new Vector2[n];
            tweenTime = new float[n];
            tweenDuration = new float[n];
            tweenDelay = new float[n];
            scratch = new bool[n];
            candidates = new Vector2[4 * n + 1];

            for (int i = 0; i < n; i++)
            {
                home[i] = tiles.HomeOf(i);
                drawOrder[i] = i;
                pivot[i] = home[i];
                scale[i] = targetScale[i] = 1f;
            }

            lockedGroup = -1;
            placedCount = 0;
            hintsUsed = 0;
            elapsed = 0f;
            forcedWin = false;
            tiles.Flush();
            revealGo.SetActive(false);
        }

        static Mesh BuildQuad()
        {
            // Plain quad for the reveal: no exposed edges, so the tile shader draws it unclipped.
            var m = new Mesh { name = "RevealQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            var zero = new[] { Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero };
            m.SetUVs(1, zero);
            m.SetUVs(2, zero);
            m.SetUVs(3, zero);
            m.colors32 = new[] { TileColor, TileColor, TileColor, TileColor };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return m;
        }

        // ---------------------------------------------------------------- per-frame

        void Update()
        {
            if (state == State.Idle || state == State.Loading || home == null) return;
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

            if (state == State.Scrambling && !anyTween)
            {
                state = State.Playing;
                stateTime = 0f;
                GameEvents.RaisePuzzlePlayable(level, level.Index);
            }
            if (state == State.Settling && !anyTween) BeginReveal();

            WriteDirtyTiles();
            tiles.Flush();
        }

        bool UpdateTweens(float dt)
        {
            bool any = false;
            for (int g = 0; g < offset.Length; g++)
            {
                if (!groups.IsGroupId(g)) continue;
                if (tweening[g])
                {
                    any = true;
                    if (tweenDelay[g] > 0f) tweenDelay[g] -= dt;
                    else
                    {
                        tweenTime[g] += dt;
                        float t = Mathf.Clamp01(tweenTime[g] / tweenDuration[g]);
                        float e = 1f - (1f - t) * (1f - t) * (1f - t);
                        offset[g] = t >= 1f ? tweenTo[g] : Vector2.LerpUnclamped(tweenFrom[g], tweenTo[g], e);
                        dirty[g] = true;
                        if (t >= 1f)
                        {
                            tweening[g] = false;
                            if (snapOnArrival[g])
                            {
                                snapOnArrival[g] = false;
                                OnSnapArrived(g);
                            }
                        }
                    }
                }
                if (!Mathf.Approximately(scale[g], targetScale[g]))
                {
                    scale[g] = Mathf.MoveTowards(scale[g], targetScale[g], dt * 1.2f);
                    dirty[g] = true;
                }
            }
            return any;
        }

        void WriteDirtyTiles()
        {
            for (int i = 0; i < home.Length; i++)
            {
                int g = groups.GroupOf(i);
                if (!dirty[g]) continue;
                float s = scale[g];
                Vector2 p = pivot[g] + offset[g];
                tiles.SetTile(i, p + (home[i] + offset[g] - p) * s, s);
            }
            for (int g = 0; g < dirty.Length; g++) dirty[g] = false;
        }

        void RefreshEdges()
        {
            for (int i = 0; i < home.Length; i++) tiles.SetEdges(i, groups.EdgeMask(i));
        }

        // ---------------------------------------------------------------- scramble

        void StartScramble()
        {
            int n = home.Length;
            // Sattolo's algorithm: a random single cycle, so no tile starts in its own cell.
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
                Vector2 target = home[perm[i]] + new Vector2(
                    ((float)rng.NextDouble() * 2f - 1f) * ScrambleJitter * cellSize.x,
                    ((float)rng.NextDouble() * 2f - 1f) * ScrambleJitter * cellSize.y);
                GroupBounds(i, out Vector2 min, out Vector2 max);
                StartTween(i, ClampOffset(target - home[i], min, max), ScrambleTweenSec, (float)rng.NextDouble() * 0.15f);
            }
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = drawOrder[i]; drawOrder[i] = drawOrder[j]; drawOrder[j] = t;
            }
            tiles.SetDrawOrder(drawOrder);
            state = State.Scrambling;
            stateTime = 0f;
        }

        void StartTween(int g, Vector2 to, float duration, float delay = 0f)
        {
            tweenFrom[g] = offset[g];
            tweenTo[g] = to;
            tweenTime[g] = 0f;
            tweenDuration[g] = Mathf.Max(0.01f, duration);
            tweenDelay[g] = delay;
            tweening[g] = true;
        }

        // Moves a group's tiles to the top (or bottom) of the stack in place; other tiles keep their order.
        void Restack(int g, bool toFront)
        {
            int n = drawOrder.Length;
            if (toFront)
            {
                int w = 0;
                for (int r = 0; r < n; r++)
                    if (groups.GroupOf(drawOrder[r]) != g) drawOrder[w++] = drawOrder[r];
                for (int i = 0; i < n; i++)
                    if (groups.GroupOf(i) == g) drawOrder[w++] = i;
            }
            else
            {
                int w = n - 1;
                for (int r = n - 1; r >= 0; r--)
                    if (groups.GroupOf(drawOrder[r]) != g) drawOrder[w--] = drawOrder[r];
                for (int i = n - 1; i >= 0; i--)
                    if (groups.GroupOf(i) == g) drawOrder[w--] = i;
            }
            tiles.SetDrawOrder(drawOrder);
        }

        // ---------------------------------------------------------------- input

        void HandleInput()
        {
            if (!InputEnabled)
            {
                if (dragGroup >= 0) Release();
                return;
            }

            if (Input.touchCount > 0 || dragFinger >= 0)
            {
                for (int t = 0; t < Input.touchCount; t++)
                {
                    Touch touch = Input.GetTouch(t);
                    if (dragGroup < 0 && touch.phase == TouchPhase.Began)
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
                else if (dragGroup >= 0 && Input.GetMouseButton(0)) Drag(Input.mousePosition);
                else if (dragGroup >= 0 && !Input.GetMouseButton(0)) Release();
            }
        }

        bool IsOverUI(int pointerId)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return pointerId >= 0 ? es.IsPointerOverGameObject(pointerId) : es.IsPointerOverGameObject();
        }

        bool IsMovable(int g) => !tweening[g] && g != lockedGroup;

        bool TryPick(Vector2 screen)
        {
            if (!playAreaScreen.Contains(screen)) return false;
            Vector2 world = ScreenToWorld(screen);
            Vector2 half = cellSize * 0.5f;
            int hit = -1;
            for (int k = drawOrder.Length - 1; k >= 0; k--)
            {
                int i = drawOrder[k];
                if (!IsMovable(groups.GroupOf(i))) continue;
                Vector2 d = world - (home[i] + offset[groups.GroupOf(i)]);
                if (Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y) { hit = i; break; }
            }
            if (hit < 0)
            {
                // Fat-finger fallback: nearest loose tile centre within reach.
                float best = PickSlack * cellMin;
                best *= best;
                for (int i = 0; i < home.Length; i++)
                {
                    if (!IsMovable(groups.GroupOf(i))) continue;
                    float d = (home[i] + offset[groups.GroupOf(i)] - world).sqrMagnitude;
                    if (d < best) { best = d; hit = i; }
                }
            }
            if (hit < 0) return false;

            int g = groups.GroupOf(hit);
            dragGroup = g;
            grabOffset = offset[g] - world;
            dragStartOffset = offset[g];
            GroupBounds(g, out dragMin, out dragMax);
            pivot[g] = (dragMin + dragMax) * 0.5f;
            targetScale[g] = LiftScale;
            Restack(g, toFront: true);
            CollectCandidates(g);
            Sfx.PlayPickup();
            return true;
        }

        // Bounding box of a group's tiles at offset zero.
        void GroupBounds(int g, out Vector2 min, out Vector2 max)
        {
            Vector2 half = cellSize * 0.5f;
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < home.Length; i++)
            {
                if (groups.GroupOf(i) != g) continue;
                min = Vector2.Min(min, home[i] - half);
                max = Vector2.Max(max, home[i] + half);
            }
        }

        // Offsets the dragged group can snap to: zero (its place on the board) and the offset of every group
        // holding one of its grid neighbours (joining them in the right relative position).
        void CollectCandidates(int g)
        {
            candidateCount = 0;
            candidates[candidateCount++] = Vector2.zero;
            for (int i = 0; i < home.Length; i++)
            {
                if (groups.GroupOf(i) != g) continue;
                for (int s = 0; s < 4; s++)
                {
                    int nb = groups.Neighbor(i, (TileGroups.Side)s);
                    if (nb < 0) continue;
                    int ng = groups.GroupOf(nb);
                    if (ng == g || tweening[ng]) continue;
                    candidates[candidateCount++] = offset[ng];
                }
            }
        }

        bool NearestCandidate(Vector2 at, out Vector2 best, out float dist)
        {
            best = Vector2.zero;
            dist = float.MaxValue;
            for (int k = 0; k < candidateCount; k++)
            {
                float d = Vector2.Distance(at, candidates[k]);
                if (d < dist) { dist = d; best = candidates[k]; }
            }
            return candidateCount > 0;
        }

        void Drag(Vector2 screen)
        {
            int g = dragGroup;
            Vector2 target = ClampOffset(ScreenToWorld(screen) + grabOffset, dragMin, dragMax);
            float radius = MagnetRadius * cellMin;
            if (NearestCandidate(target, out Vector2 snap, out float d) && d < radius)
            {
                float k = 1f - d / radius;
                target = Vector2.Lerp(target, snap, MagnetStrength * k * k);
            }
            if (target != offset[g])
            {
                offset[g] = target;
                dirty[g] = true;
            }
        }

        void Release()
        {
            int g = dragGroup;
            dragGroup = -1;
            dragFinger = -1;
            if (g < 0) return;
            targetScale[g] = 1f;
            if (NearestCandidate(offset[g], out Vector2 snap, out float d) && d <= SnapRadius * cellMin)
                SnapGroup(g, snap, SnapTweenSec, dragStartOffset);
        }

        // ---------------------------------------------------------------- snapping, joining, locking

        float Epsilon => 0.001f * cellMin;

        bool SameOffset(Vector2 a, Vector2 b) => (a - b).sqrMagnitude <= Epsilon * Epsilon;

        // vacatedOffset: where the group came from; loose tiles it lands on when it locks are moved there.
        void SnapGroup(int g, Vector2 target, float tweenSec, Vector2 vacatedOffset, float delay = 0f)
        {
            StartTween(g, target, tweenSec, delay);
            snapOnArrival[g] = true;
            vacated[g] = vacatedOffset;
        }

        void OnSnapArrived(int g)
        {
            Vector2 at = offset[g];
            Vector2 cameFrom = vacated[g];
            int survivor = g;

            // Join every adjacent resting group that sits in the same relative position (offset zero joins
            // the placed tiles, since the locked group always rests at zero).
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < home.Length && !merged; i++)
                {
                    if (groups.GroupOf(i) != survivor) continue;
                    for (int s = 0; s < 4; s++)
                    {
                        int nb = groups.Neighbor(i, (TileGroups.Side)s);
                        if (nb < 0) continue;
                        int ng = groups.GroupOf(nb);
                        if (ng == survivor || ng == dragGroup || tweening[ng] || !SameOffset(offset[ng], at)) continue;
                        survivor = MergeGroups(survivor, ng);
                        merged = true;
                        break;
                    }
                }
            }

            // A group at offset zero is in place even when no placed neighbour touches it.
            if (SameOffset(at, Vector2.zero) && survivor != lockedGroup)
            {
                if (lockedGroup >= 0) survivor = MergeGroups(lockedGroup, survivor);
                else lockedGroup = survivor;
            }

            int newlyPlaced = 0;
            if (survivor == lockedGroup)
            {
                for (int i = 0; i < home.Length; i++)
                {
                    fresh[i] = !placed[i] && groups.GroupOf(i) == lockedGroup;
                    if (fresh[i]) { placed[i] = true; newlyPlaced++; }
                }
                Restack(lockedGroup, toFront: false);
            }
            else Restack(survivor, toFront: true);

            RefreshEdges();
            dirty[survivor] = true;

            // ForceWin already completed the puzzle; its tiles just land and join quietly.
            if (forcedWin) return;

            Sfx.PlaySnap();
            Haptics.Tick();
            if (newlyPlaced > 0)
            {
                placedCount = groups.SizeOf(lockedGroup);
                DisplaceCoveringTiles(cameFrom);
                ProgressChanged?.Invoke(placedCount, home.Length);
                GameEvents.RaisePieceSnapped(placedCount, home.Length);
                if (placedCount == home.Length) Complete();
            }
        }

        // Keeps the locked group (if any) as the survivor so placed tiles never move.
        int MergeGroups(int a, int b)
        {
            int into = b == lockedGroup ? b : a;
            int from = into == a ? b : a;
            groups.Merge(into, from);
            tweening[from] = snapOnArrival[from] = false;
            scale[into] = targetScale[into] = 1f;
            if (from == lockedGroup) lockedGroup = into;
            return into;
        }

        // Loose groups lying on cells that were just filled (fresh[]) move by the placed tiles' old offset, so
        // they end up roughly where those tiles were picked up.
        void DisplaceCoveringTiles(Vector2 cameFrom)
        {
            for (int g = 0; g < scratch.Length; g++) scratch[g] = false;
            float reach = 0.5f * cellMin;
            for (int j = 0; j < home.Length; j++)
            {
                int hg = groups.GroupOf(j);
                if (hg == lockedGroup || hg == dragGroup || tweening[hg] || scratch[hg]) continue;
                Vector2 pos = home[j] + offset[hg];
                for (int i = 0; i < home.Length; i++)
                {
                    if (!fresh[i]) continue;
                    if ((pos - home[i]).sqrMagnitude < reach * reach) { scratch[hg] = true; break; }
                }
            }
            int moved = 0;
            for (int hg = 0; hg < scratch.Length; hg++)
            {
                if (!scratch[hg]) continue;
                GroupBounds(hg, out Vector2 min, out Vector2 max);
                Vector2 nudge = new Vector2(moved * 0.12f, -moved * 0.12f) * cellMin;
                StartTween(hg, ClampOffset(offset[hg] + cameFrom + nudge, min, max), 0.25f);
                Restack(hg, toFront: true);
                moved++;
            }
        }

        /// <summary>Auto-places one loose tile (the smallest loose group). Returns false if nothing to place.</summary>
        public bool ApplyHint()
        {
            if (state != State.Playing) return false;
            if (dragGroup >= 0) Release();
            int best = -1, bestSize = int.MaxValue, ties = 0;
            for (int g = 0; g < offset.Length; g++)
            {
                if (!groups.IsGroupId(g) || !IsMovable(g) || snapOnArrival[g]) continue;
                int size = groups.SizeOf(g);
                if (size < bestSize) { best = g; bestSize = size; ties = 1; }
                else if (size == bestSize && UnityEngine.Random.Range(0, ++ties) == 0) best = g;
            }
            if (best < 0) return false;
            hintsUsed++;
            Restack(best, toFront: true);
            GameEvents.RaiseHintUsed(level, level.Index);
            SnapGroup(best, Vector2.zero, HintTweenSec, offset[best]);
            return true;
        }

        /// <summary>Dev/recording: sends every loose tile home and completes the puzzle.</summary>
        public void ForceWin()
        {
            if (state == State.Intro || state == State.Scrambling)
            {
                for (int g = 0; g < tweening.Length; g++) tweening[g] = false;
                state = State.Playing;
            }
            if (state != State.Playing) return;
            if (dragGroup >= 0) Release();
            forcedWin = true;
            int k = 0;
            for (int g = 0; g < offset.Length; g++)
            {
                if (!groups.IsGroupId(g) || g == lockedGroup) continue;
                SnapGroup(g, Vector2.zero, 0.35f, offset[g], k++ * 0.015f);
            }
            placedCount = home.Length;
            ProgressChanged?.Invoke(placedCount, home.Length);
            Complete();
        }

        void Complete()
        {
            state = State.Settling;
            stateTime = 0f;
            int stars = Progress.ComputeStars(home.Length, elapsed, hintsUsed);
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

            tilesGo.SetActive(false);
            slotsGo.SetActive(false);
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

        // Keeps a group whose home bounds are min/max inside the play area.
        Vector2 ClampOffset(Vector2 o, Vector2 min, Vector2 max)
        {
            float loX = playArea.xMin - min.x, hiX = playArea.xMax - max.x;
            float loY = playArea.yMin - min.y, hiY = playArea.yMax - max.y;
            return new Vector2(
                loX <= hiX ? Mathf.Clamp(o.x, loX, hiX) : (loX + hiX) * 0.5f,
                loY <= hiY ? Mathf.Clamp(o.y, loY, hiY) : (loY + hiY) * 0.5f);
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
    }
}
