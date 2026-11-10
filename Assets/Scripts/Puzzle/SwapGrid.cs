using System;

namespace Snaploop.Puzzle
{
    /// <summary>
    /// Grid-swap rules: every tile sits in exactly one cell and every cell holds exactly one tile. Moving a group
    /// by whole cells pushes the tiles it lands on into the cells it vacates, so the board is always a full grid
    /// and every drop inside the grid is legal. Groups come from a shared <see cref="TileGroups"/>; the tiles of
    /// a group always share one displacement (cell minus home cell). A moved group never breaks, but when it
    /// lands on part of another group, the covered tiles are pulled out of that group and the rest stays joined.
    /// Cell and tile indices both use row * cols + col with row 0 at the bottom; a tile's home cell is its own
    /// index. No Unity types, no allocations after <see cref="Reset"/>.
    /// </summary>
    public sealed class SwapGrid
    {
        /// <summary>Split: moved, and tiles were pulled out of at least one other group (group ids may change).</summary>
        public enum MoveResult { Moved, Split, NoChange, OffGrid }

        readonly TileGroups groups;
        int[] cellOf = new int[0], tileAt = new int[0];
        // Scratch for TryMove, per cell / tile / group id.
        bool[] inSource = new bool[0], inTarget = new bool[0], groupSeen = new bool[0], groupCovered = new bool[0];
        int[] newCell = new int[0], groupDeltaCol = new int[0], groupDeltaRow = new int[0], displaced = new int[0], oldGroup = new int[0];

        public SwapGrid(TileGroups groups)
        {
            this.groups = groups;
        }

        public int Cols { get; private set; }
        public int Rows { get; private set; }
        public int Count => cellOf.Length;
        public TileGroups Groups => groups;

        /// <summary>Groups pushed aside by the last <see cref="TryMove"/> (after any split).</summary>
        public int DisplacedCount { get; private set; }
        public int Displaced(int k) => displaced[k];

        /// <summary>Every tile at home, every tile its own group.</summary>
        public void Reset(int cols, int rows)
        {
            Cols = cols;
            Rows = rows;
            int n = cols * rows;
            groups.Reset(cols, rows);
            if (cellOf.Length != n)
            {
                cellOf = new int[n];
                tileAt = new int[n];
                inSource = new bool[n];
                inTarget = new bool[n];
                groupSeen = new bool[n];
                groupCovered = new bool[n];
                newCell = new int[n];
                groupDeltaCol = new int[n];
                groupDeltaRow = new int[n];
                displaced = new int[n];
                oldGroup = new int[n];
            }
            for (int i = 0; i < n; i++) cellOf[i] = tileAt[i] = i;
            DisplacedCount = 0;
        }

        /// <summary>Places tile i in cell layout[i]. The layout must be a permutation; groups are not touched.</summary>
        public void SetLayout(int[] layout)
        {
            int n = Count;
            if (layout == null || layout.Length != n) throw new ArgumentException("Layout size must match the grid.");
            for (int c = 0; c < n; c++) tileAt[c] = -1;
            for (int i = 0; i < n; i++)
            {
                int c = layout[i];
                if (c < 0 || c >= n || tileAt[c] >= 0) throw new ArgumentException("Layout is not a permutation.");
                cellOf[i] = c;
                tileAt[c] = i;
            }
        }

        public int CellOf(int tile) => cellOf[tile];
        public int TileAt(int cell) => tileAt[cell];
        public bool IsHome(int tile) => cellOf[tile] == tile;
        public int DispCol(int tile) => cellOf[tile] % Cols - tile % Cols;
        public int DispRow(int tile) => cellOf[tile] / Cols - tile / Cols;

        /// <summary>True when every tile is home (then all tiles are also one group once joined).</summary>
        public bool IsSolved()
        {
            for (int i = 0; i < cellOf.Length; i++)
                if (cellOf[i] != i) return false;
            return true;
        }

        /// <summary>
        /// Shifts group <paramref name="group"/> by (dc, dr) cells. The tiles it lands on move into the cells it left:
        /// each covered tile moves against the drag direction until it reaches a vacated cell, so a single tile is a
        /// plain swap and a group moved one row lets the covered row drop into the row it left. A covered group that
        /// does not move as one piece splits (see <see cref="MoveResult.Split"/>). Refused only off the grid.
        /// The moved group keeps the group of tile <paramref name="group"/>; read its id again after a split.
        /// </summary>
        public MoveResult TryMove(int group, int dc, int dr) => Move(group, dc, dr, true);

        /// <summary>What <see cref="TryMove"/> would return, without moving anything.</summary>
        public MoveResult CheckMove(int group, int dc, int dr) => Move(group, dc, dr, false);

        MoveResult Move(int group, int dc, int dr, bool commit)
        {
            DisplacedCount = 0;
            if (dc == 0 && dr == 0) return MoveResult.NoChange;
            int n = Count, cols = Cols;
            for (int c = 0; c < n; c++) { inSource[c] = inTarget[c] = false; newCell[c] = -1; }

            for (int i = 0; i < n; i++)
            {
                if (groups.GroupOf(i) != group) continue;
                int c = cellOf[i] % cols + dc, r = cellOf[i] / cols + dr;
                if (c < 0 || c >= cols || r < 0 || r >= Rows) return MoveResult.OffGrid;
                inSource[cellOf[i]] = true;
                inTarget[r * cols + c] = true;
            }

            int step = dr * cols + dc;
            for (int c = 0; c < n; c++)
            {
                if (!inTarget[c] || inSource[c]) continue;
                // c - step is one of the group's current cells; keep stepping back until it is one it vacates.
                int p = c - step;
                while (inTarget[p]) p -= step;
                newCell[tileAt[c]] = p;
            }

            // A covered group stays whole only if it is covered completely and every tile shifts by one delta.
            bool split = false;
            for (int g = 0; g < n; g++) groupSeen[g] = false;
            for (int i = 0; i < n; i++)
            {
                int g = groups.GroupOf(i);
                if (g == group) continue;
                bool covered = newCell[i] >= 0;
                int ddc = covered ? newCell[i] % cols - cellOf[i] % cols : 0;
                int ddr = covered ? newCell[i] / cols - cellOf[i] / cols : 0;
                if (!groupSeen[g])
                {
                    groupSeen[g] = true;
                    groupCovered[g] = covered;
                    groupDeltaCol[g] = ddc;
                    groupDeltaRow[g] = ddr;
                }
                else if (covered != groupCovered[g] || ddc != groupDeltaCol[g] || ddr != groupDeltaRow[g]) split = true;
            }

            if (!commit) return split ? MoveResult.Split : MoveResult.Moved;
            for (int i = 0; i < n; i++)
            {
                if (groups.GroupOf(i) == group)
                {
                    int c = cellOf[i] % cols + dc, r = cellOf[i] / cols + dr;
                    cellOf[i] = r * cols + c;
                }
                else if (newCell[i] >= 0) cellOf[i] = newCell[i];
            }
            for (int i = 0; i < n; i++) tileAt[cellOf[i]] = i;
            if (split) SplitMovedGroups();

            for (int g = 0; g < n; g++) groupSeen[g] = false;
            for (int i = 0; i < n; i++)
            {
                if (newCell[i] < 0) continue;
                int g = groups.GroupOf(i);
                if (groupSeen[g]) continue;
                groupSeen[g] = true;
                displaced[DisplacedCount++] = g;
            }
            return split ? MoveResult.Split : MoveResult.Moved;
        }

        // Regroups so that two picture neighbours stay joined only if they were joined and still sit side by side.
        void SplitMovedGroups()
        {
            int n = Count, cols = Cols;
            for (int i = 0; i < n; i++) oldGroup[i] = groups.GroupOf(i);
            groups.Reset(cols, Rows);
            for (int i = 0; i < n; i++)
            {
                if (i % cols < cols - 1 && oldGroup[i + 1] == oldGroup[i] && cellOf[i + 1] == cellOf[i] + 1 && cellOf[i] % cols < cols - 1)
                    groups.Merge(groups.GroupOf(i), groups.GroupOf(i + 1));
                if (i + cols < n && oldGroup[i + cols] == oldGroup[i] && cellOf[i + cols] == cellOf[i] + cols)
                    groups.Merge(groups.GroupOf(i), groups.GroupOf(i + cols));
            }
        }

        /// <summary>Moves a group back to its home cells (used by hints). Same rules as <see cref="TryMove"/>.</summary>
        public MoveResult TryMoveHome(int group) => TryMove(group, -DispCol(group), -DispRow(group));

        /// <summary>What <see cref="TryMoveHome"/> would return, without moving anything.</summary>
        public MoveResult CheckMoveHome(int group) => CheckMove(group, -DispCol(group), -DispRow(group));

        /// <summary>Puts every tile in its home cell (dev win). Groups stay consistent: all share displacement zero.</summary>
        public void PlaceAllHome()
        {
            for (int i = 0; i < cellOf.Length; i++) cellOf[i] = tileAt[i] = i;
            DisplacedCount = 0;
        }

        /// <summary>
        /// Finds two tiles that are neighbours in the picture, sit next to each other on the board in the right
        /// layout (same displacement), and are not yet in one group. Callers merge them and ask again.
        /// </summary>
        public bool FindJoin(out int a, out int b)
        {
            int n = Count, cols = Cols;
            for (int i = 0; i < n; i++)
            {
                int ga = groups.GroupOf(i);
                if (i % cols < cols - 1 && groups.GroupOf(i + 1) != ga && cellOf[i + 1] == cellOf[i] + 1 && cellOf[i] % cols < cols - 1)
                { a = i; b = i + 1; return true; }
                if (i + cols < n && groups.GroupOf(i + cols) != ga && cellOf[i + cols] == cellOf[i] + cols)
                { a = i; b = i + cols; return true; }
            }
            a = b = -1;
            return false;
        }

        /// <summary>Picture neighbours that sit correctly next to each other in <paramref name="layout"/>.</summary>
        public static int CountJoinedPairs(int cols, int rows, int[] layout)
        {
            int n = cols * rows, pairs = 0;
            for (int i = 0; i < n; i++)
            {
                if (i % cols < cols - 1 && layout[i + 1] == layout[i] + 1 && layout[i] % cols < cols - 1) pairs++;
                if (i + cols < n && layout[i + cols] == layout[i] + cols) pairs++;
            }
            return pairs;
        }

        /// <summary>
        /// Fills <paramref name="layout"/> with a scramble: one random cycle (Sattolo), so no tile starts in its own
        /// cell, re-rolled until no two picture neighbours start joined. Falls back to the roll with the fewest
        /// joined pairs (only tiny grids can run out of options).
        /// </summary>
        public static void Scramble(int cols, int rows, Random rng, int[] layout)
        {
            int n = cols * rows;
            int[] best = null;
            int bestPairs = int.MaxValue;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                for (int i = 0; i < n; i++) layout[i] = i;
                for (int i = n - 1; i > 0; i--)
                {
                    int j = rng.Next(i);
                    int t = layout[i]; layout[i] = layout[j]; layout[j] = t;
                }
                int pairs = CountJoinedPairs(cols, rows, layout);
                if (pairs == 0) return;
                if (pairs < bestPairs)
                {
                    bestPairs = pairs;
                    if (best == null) best = new int[n];
                    Array.Copy(layout, best, n);
                }
            }
            Array.Copy(best, layout, n);
        }
    }
}
