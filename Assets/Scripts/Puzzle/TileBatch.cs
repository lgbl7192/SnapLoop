using UnityEngine;
using UnityEngine.Rendering;

namespace Snaploop.Puzzle
{
    /// <summary>
    /// All tiles of a puzzle as ONE mesh (one quad per grid cell) for the Snaploop/VideoTile shader, which
    /// samples the shared video texture by UV and draws rounded corners, gaps and outlines from per-vertex
    /// edge flags. Pure visuals: callers decide where tiles are, how they group and in what order they stack.
    /// Setters write persistent arrays; <see cref="Flush"/> uploads only what changed. No allocations after
    /// <see cref="Build"/>.
    /// </summary>
    public sealed class TileBatch
    {
        const MeshUpdateFlags UploadFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        public Mesh Mesh { get; }
        public int Count { get; private set; }
        public Vector2 CellSize { get; private set; }

        Vector2[] home;
        Vector3[] verts;
        Vector2[] uv0;
        Vector4[] local;
        Vector4[] edges;
        Vector4[] notches;
        Vector2[] flashes;
        Color32[] colors;
        int[] indices;
        bool vertsDirty, edgesDirty, flashDirty, colorsDirty, indicesDirty;

        static readonly Vector2[] Corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };

        public TileBatch(string name)
        {
            Mesh = new Mesh { name = name };
            Mesh.MarkDynamic();
        }

        /// <summary>
        /// Lays out cols x rows tiles over <paramref name="boardRect"/> (world units), each showing its cell of the
        /// video. Tiles start at home, fully exposed, drawn in index order.
        /// </summary>
        public void Build(int cols, int rows, Rect boardRect, Color32 color)
        {
            int n = cols * rows;
            Count = n;
            CellSize = new Vector2(boardRect.width / cols, boardRect.height / rows);
            Vector2 h = CellSize * 0.5f;

            home = new Vector2[n];
            verts = new Vector3[n * 4];
            uv0 = new Vector2[n * 4];
            local = new Vector4[n * 4];
            edges = new Vector4[n * 4];
            notches = new Vector4[n * 4];
            flashes = new Vector2[n * 4];
            colors = new Color32[n * 4];
            indices = new int[n * 6];

            for (int i = 0; i < n; i++)
            {
                int c = i % cols, r = i / cols;
                home[i] = new Vector2(boardRect.xMin + (c + 0.5f) * CellSize.x, boardRect.yMin + (r + 0.5f) * CellSize.y);
                for (int k = 0; k < 4; k++)
                {
                    int v = i * 4 + k;
                    Vector2 corner = Corners[k];
                    uv0[v] = new Vector2((c + (corner.x + 1f) * 0.5f) / cols, (r + (corner.y + 1f) * 0.5f) / rows);
                    local[v] = new Vector4(corner.x * h.x, corner.y * h.y, h.x, h.y);
                    colors[v] = color;
                }
                SetTile(i, home[i], 1f);
                SetEdges(i, TileGroups.AllExposed);
            }
            for (int i = 0; i < n; i++) WriteQuadIndices(i, i);

            Mesh.Clear();
            Mesh.indexFormat = IndexFormat.UInt16;
            Mesh.SetVertices(verts);
            Mesh.SetUVs(0, uv0);
            Mesh.SetUVs(1, local);
            Mesh.SetUVs(2, edges);
            Mesh.SetUVs(3, notches);
            Mesh.SetUVs(4, flashes);
            Mesh.SetColors(colors);
            Mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            Mesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
            vertsDirty = edgesDirty = flashDirty = colorsDirty = indicesDirty = false;
        }

        /// <summary>World-space centre of a tile when it is in its correct cell.</summary>
        public Vector2 HomeOf(int tile) => home[tile];

        /// <summary>Places a tile's centre and scales its quad around that centre.</summary>
        public void SetTile(int tile, Vector2 center, float scale)
        {
            Vector2 h = CellSize * (0.5f * scale);
            int v = tile * 4;
            for (int k = 0; k < 4; k++)
                verts[v + k] = new Vector3(center.x + Corners[k].x * h.x, center.y + Corners[k].y * h.y, 0f);
            vertsDirty = true;
        }

        /// <summary>Applies a <see cref="TileGroups.EdgeMask"/> (exposed edges and concave notches).</summary>
        public void SetEdges(int tile, byte mask)
        {
            var e = new Vector4(
                (mask & TileGroups.ExposedLeft) != 0 ? 1f : 0f,
                (mask & TileGroups.ExposedRight) != 0 ? 1f : 0f,
                (mask & TileGroups.ExposedBottom) != 0 ? 1f : 0f,
                (mask & TileGroups.ExposedTop) != 0 ? 1f : 0f);
            var n = new Vector4(
                (mask & TileGroups.NotchBottomLeft) != 0 ? 1f : 0f,
                (mask & TileGroups.NotchBottomRight) != 0 ? 1f : 0f,
                (mask & TileGroups.NotchTopLeft) != 0 ? 1f : 0f,
                (mask & TileGroups.NotchTopRight) != 0 ? 1f : 0f);
            int v = tile * 4;
            for (int k = 0; k < 4; k++)
            {
                edges[v + k] = e;
                notches[v + k] = n;
            }
            edgesDirty = true;
        }

        /// <summary>Outline flash, 0 (none) to 1 (thick and bright); shows on the tile's exposed edges only.</summary>
        public void SetFlash(int tile, float amount)
        {
            var f = new Vector2(amount, 0f);
            int v = tile * 4;
            if (flashes[v] == f) return;
            for (int k = 0; k < 4; k++) flashes[v + k] = f;
            flashDirty = true;
        }

        /// <summary>rgb = outline colour, a = video strength inside the tile.</summary>
        public void SetColor(int tile, Color32 color)
        {
            int v = tile * 4;
            for (int k = 0; k < 4; k++) colors[v + k] = color;
            colorsDirty = true;
        }

        /// <summary>Stacking: <paramref name="order"/> lists tile indices from bottom to top.</summary>
        public void SetDrawOrder(int[] order)
        {
            for (int k = 0; k < order.Length; k++) WriteQuadIndices(k, order[k]);
            indicesDirty = true;
        }

        public void Flush()
        {
            if (vertsDirty) Mesh.SetVertices(verts, 0, verts.Length, UploadFlags);
            if (edgesDirty)
            {
                Mesh.SetUVs(2, edges, 0, edges.Length, UploadFlags);
                Mesh.SetUVs(3, notches, 0, notches.Length, UploadFlags);
            }
            if (flashDirty) Mesh.SetUVs(4, flashes, 0, flashes.Length, UploadFlags);
            if (colorsDirty) Mesh.SetColors(colors, 0, colors.Length, UploadFlags);
            if (indicesDirty) Mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            vertsDirty = edgesDirty = flashDirty = colorsDirty = indicesDirty = false;
        }

        void WriteQuadIndices(int slot, int tile)
        {
            int s = slot * 6, v = tile * 4;
            indices[s] = v; indices[s + 1] = v + 2; indices[s + 2] = v + 1;
            indices[s + 3] = v; indices[s + 4] = v + 3; indices[s + 5] = v + 2;
        }
    }
}
