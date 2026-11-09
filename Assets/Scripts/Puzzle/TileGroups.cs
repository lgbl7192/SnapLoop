namespace Snaploop.Puzzle
{
    /// <summary>
    /// Which tiles are joined. Tiles in one group sit in their correct relative positions and are drawn as one
    /// shape (see <see cref="EdgeMask"/>). Independent of how tiles move, so a free-drag mode and a grid-swap
    /// mode can share it. Tile index = row * cols + col, row 0 at the bottom. A group id is the index of one of
    /// its tiles. No allocations after <see cref="Reset"/>.
    /// </summary>
    public sealed class TileGroups
    {
        public const byte ExposedLeft = 1, ExposedRight = 2, ExposedBottom = 4, ExposedTop = 8;
        public const byte NotchBottomLeft = 16, NotchBottomRight = 32, NotchTopLeft = 64, NotchTopRight = 128;
        public const byte AllExposed = ExposedLeft | ExposedRight | ExposedBottom | ExposedTop;

        public enum Side { Left = 0, Right = 1, Bottom = 2, Top = 3 }

        int[] groupOf = new int[0];
        int[] size = new int[0];

        public int Cols { get; private set; }
        public int Rows { get; private set; }
        public int Count => groupOf.Length;

        /// <summary>Every tile becomes its own group.</summary>
        public void Reset(int cols, int rows)
        {
            Cols = cols;
            Rows = rows;
            int n = cols * rows;
            if (groupOf.Length != n)
            {
                groupOf = new int[n];
                size = new int[n];
            }
            for (int i = 0; i < n; i++)
            {
                groupOf[i] = i;
                size[i] = 1;
            }
        }

        public int GroupOf(int tile) => groupOf[tile];

        public bool IsGroupId(int id) => groupOf[id] == id;

        public int SizeOf(int group) => size[group];

        public bool SameGroup(int a, int b) => groupOf[a] == groupOf[b];

        /// <summary>Grid neighbour on the given side, or -1 at the board edge.</summary>
        public int Neighbor(int tile, Side side)
        {
            int c = tile % Cols, r = tile / Cols;
            switch (side)
            {
                case Side.Left: return c > 0 ? tile - 1 : -1;
                case Side.Right: return c < Cols - 1 ? tile + 1 : -1;
                case Side.Bottom: return r > 0 ? tile - Cols : -1;
                default: return r < Rows - 1 ? tile + Cols : -1;
            }
        }

        /// <summary>Moves every tile of group <paramref name="from"/> into group <paramref name="into"/>.</summary>
        public void Merge(int into, int from)
        {
            if (into == from) return;
            for (int i = 0; i < groupOf.Length; i++)
                if (groupOf[i] == from) groupOf[i] = into;
            size[into] += size[from];
            size[from] = 0;
        }

        /// <summary>
        /// Exposed edges and concave notches for one tile: an edge is exposed when the neighbour on that side is
        /// missing or in another group; a notch marks an inner corner of an L-shaped group.
        /// </summary>
        public byte EdgeMask(int tile)
        {
            int g = groupOf[tile];
            int c = tile % Cols, r = tile / Cols;
            bool l = c > 0 && groupOf[tile - 1] == g;
            bool rt = c < Cols - 1 && groupOf[tile + 1] == g;
            bool b = r > 0 && groupOf[tile - Cols] == g;
            bool t = r < Rows - 1 && groupOf[tile + Cols] == g;

            int mask = 0;
            if (!l) mask |= ExposedLeft;
            if (!rt) mask |= ExposedRight;
            if (!b) mask |= ExposedBottom;
            if (!t) mask |= ExposedTop;
            if (l && b && groupOf[tile - Cols - 1] != g) mask |= NotchBottomLeft;
            if (rt && b && groupOf[tile - Cols + 1] != g) mask |= NotchBottomRight;
            if (l && t && groupOf[tile + Cols - 1] != g) mask |= NotchTopLeft;
            if (rt && t && groupOf[tile + Cols + 1] != g) mask |= NotchTopRight;
            return (byte)mask;
        }
    }
}
