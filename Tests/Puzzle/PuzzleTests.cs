using System;
using System.Collections.Generic;
using Snaploop.Puzzle;

static class PuzzleTests
{
    static int failures, passes;

    static void Check(bool ok, string what)
    {
        if (ok) passes++;
        else failures++;
        if (!ok || verbose) Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
    }

    static void Eq<T>(T expected, T actual, string what) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), what + " (expected " + expected + ", got " + actual + ")");

    static bool verbose = true;

    static SwapGrid Grid(int cols, int rows)
    {
        var g = new SwapGrid(new TileGroups());
        g.Reset(cols, rows);
        return g;
    }

    static void JoinAll(SwapGrid g)
    {
        while (g.FindJoin(out int a, out int b)) g.Groups.Merge(g.Groups.GroupOf(a), g.Groups.GroupOf(b));
    }

    static void MergeTiles(SwapGrid g, params int[] tiles)
    {
        for (int k = 1; k < tiles.Length; k++) g.Groups.Merge(g.Groups.GroupOf(tiles[0]), g.Groups.GroupOf(tiles[k]));
    }

    static int[] Layout(SwapGrid g)
    {
        var a = new int[g.Count];
        for (int i = 0; i < a.Length; i++) a[i] = g.CellOf(i);
        return a;
    }

    static bool SameLayout(int[] a, int[] b)
    {
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>Every cell holds one tile and every group's tiles share one displacement.</summary>
    static bool Consistent(SwapGrid g)
    {
        var seen = new bool[g.Count];
        for (int i = 0; i < g.Count; i++)
        {
            int c = g.CellOf(i);
            if (seen[c] || g.TileAt(c) != i) return false;
            seen[c] = true;
            int root = g.Groups.GroupOf(i);
            if (g.DispCol(i) != g.DispCol(root) || g.DispRow(i) != g.DispRow(root)) return false;
        }
        return true;
    }

    static void ScrambleTests()
    {
        int[][] sizes = { new[] { 2, 2 }, new[] { 3, 3 }, new[] { 3, 4 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 6 }, new[] { 6, 8 } };
        foreach (var s in sizes)
        {
            int cols = s[0], rows = s[1], n = cols * rows;
            bool permutation = true, noneHome = true, noPairs = true;
            for (int seed = 0; seed < 300; seed++)
            {
                var layout = new int[n];
                SwapGrid.Scramble(cols, rows, new Random(seed), layout);
                var seen = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    if (layout[i] < 0 || layout[i] >= n || seen[layout[i]]) permutation = false;
                    else seen[layout[i]] = true;
                    if (layout[i] == i) noneHome = false;
                }
                if (SwapGrid.CountJoinedPairs(cols, rows, layout) != 0) noPairs = false;
            }
            string grid = cols + "x" + rows;
            Check(permutation, "scramble " + grid + ": a permutation of the cells");
            Check(noneHome, "scramble " + grid + ": no tile starts home (never pre-solved)");
            Check(noPairs, "scramble " + grid + ": no picture neighbours start joined");
        }

        var g = Grid(4, 4);
        var l = new int[16];
        SwapGrid.Scramble(4, 4, new Random(7), l);
        g.SetLayout(l);
        JoinAll(g);
        Check(!g.IsSolved(), "scrambled board is not solved");
        bool singles = true;
        for (int i = 0; i < 16; i++) if (g.Groups.SizeOf(g.Groups.GroupOf(i)) != 1) singles = false;
        Check(singles, "scrambled board starts with no groups");
    }

    static void SingleTileTests()
    {
        var g = Grid(3, 3);
        Eq(SwapGrid.MoveResult.Moved, g.TryMove(0, 1, 0), "single tile: move right");
        Eq(1, g.CellOf(0), "single tile: dropped tile lands in the target cell");
        Eq(0, g.CellOf(1), "single tile: covered tile takes the vacated cell (plain swap)");
        Eq(1, g.DisplacedCount, "single tile: one displaced group");
        Eq(1, g.Displaced(0), "single tile: the displaced group is the covered tile");
        Check(Consistent(g), "single tile: board stays consistent");

        g = Grid(3, 3);
        Eq(SwapGrid.MoveResult.Moved, g.TryMove(0, 2, 2), "single tile: diagonal long swap");
        Eq(8, g.CellOf(0), "single tile: long swap target");
        Eq(0, g.CellOf(8), "single tile: long swap partner goes to the origin");
        Check(g.CellOf(4) == 4 && g.CellOf(1) == 1, "single tile: tiles in between stay put");

        g = Grid(3, 3);
        var before = Layout(g);
        Eq(SwapGrid.MoveResult.OffGrid, g.TryMove(2, 1, 0), "single tile: off the right edge is refused");
        Eq(SwapGrid.MoveResult.OffGrid, g.TryMove(0, 0, -1), "single tile: off the bottom edge is refused");
        Eq(SwapGrid.MoveResult.NoChange, g.TryMove(4, 0, 0), "single tile: dropping in place is no change");
        Check(SameLayout(before, Layout(g)), "single tile: refused moves change nothing");
    }

    static void GroupTests()
    {
        // 3 cols x 4 rows. Bottom row joined at home, dragged up one row: the covered row drops into the bottom row.
        var g = Grid(3, 4);
        MergeTiles(g, 0, 1, 2);
        Eq(SwapGrid.MoveResult.Moved, g.TryMove(0, 0, 1), "group: bottom row moves up one row");
        Check(g.CellOf(0) == 3 && g.CellOf(1) == 4 && g.CellOf(2) == 5, "group: keeps its layout");
        Check(g.CellOf(3) == 0 && g.CellOf(4) == 1 && g.CellOf(5) == 2, "group: covered row drops into the cells it left, same columns");
        Eq(3, g.DisplacedCount, "group: three loose tiles displaced");
        Check(Consistent(g), "group: board stays consistent");

        // Two rows up: row 2 drops into row 0, row 1 is untouched.
        g = Grid(3, 4);
        MergeTiles(g, 0, 1, 2);
        g.TryMove(0, 0, 2);
        Check(g.CellOf(6) == 0 && g.CellOf(7) == 1 && g.CellOf(8) == 2, "group: a two-row jump swaps with the covered row");
        Check(g.CellOf(3) == 3 && g.CellOf(4) == 4 && g.CellOf(5) == 5, "group: rows it jumps over stay");

        // 2x2 block moved one column right on a 4x4: the covered column moves to the column it left, same rows.
        g = Grid(4, 4);
        MergeTiles(g, 0, 1, 4, 5);
        Eq(SwapGrid.MoveResult.Moved, g.TryMove(0, 1, 0), "group: 2x2 block one column right");
        Check(g.CellOf(2) == 0 && g.CellOf(6) == 4, "group: covered column wraps to the vacated column");
        Check(g.CellOf(0) == 1 && g.CellOf(5) == 6, "group: block lands one column right");
        Check(Consistent(g), "group: consistent after a sideways move");

        // Two horizontal pairs swap places intact.
        g = Grid(4, 4);
        MergeTiles(g, 0, 1);
        MergeTiles(g, 10, 11);
        Eq(SwapGrid.MoveResult.Moved, g.TryMove(0, 2, 2), "group: pair dropped exactly on another pair");
        Check(g.CellOf(10) == 0 && g.CellOf(11) == 1, "group: the covered pair moves as one piece");
        Check(g.Groups.SameGroup(10, 11) && Consistent(g), "group: covered pair keeps its group");

        // Covering part of another group pulls just the covered tile out of it; the rest of that group stays.
        g = Grid(4, 4);
        MergeTiles(g, 9, 10, 11);
        Eq(SwapGrid.MoveResult.Split, g.TryMove(0, 2, 2), "split: single tile on part of a group is still a legal drop");
        Eq(10, g.CellOf(0), "split: dropped tile lands");
        Eq(0, g.CellOf(10), "split: covered tile swaps out");
        Check(!g.Groups.SameGroup(10, 11) && !g.Groups.SameGroup(9, 10), "split: covered tile leaves its group");
        Check(g.Groups.SameGroup(9, 11) == false && g.Groups.SizeOf(g.Groups.GroupOf(9)) == 1, "split: leftovers that no longer touch are separate");
        Eq(1, g.DisplacedCount, "split: one displaced group");
        Check(Consistent(g), "split: consistent");

        g = Grid(4, 4);
        MergeTiles(g, 8, 9, 10, 11);
        Eq(SwapGrid.MoveResult.Split, g.TryMove(0, 1, 2), "split: drop on the middle of a row group");
        Check(g.Groups.SameGroup(10, 11) && g.Groups.SizeOf(g.Groups.GroupOf(10)) == 2, "split: untouched neighbours stay joined");
        Eq(1, g.Groups.SizeOf(g.Groups.GroupOf(8)), "split: cut-off end becomes loose");

        g = Grid(4, 4);
        MergeTiles(g, 0, 1);
        MergeTiles(g, 9, 10);
        Eq(SwapGrid.MoveResult.Split, g.TryMove(0, 2, 2), "split: pair overlapping half of another pair");
        Check(g.Groups.SameGroup(0, 1) && Consistent(g), "split: the moved pair never breaks");

        // CheckMove never changes the board.
        g = Grid(3, 3);
        var before = Layout(g);
        Eq(SwapGrid.MoveResult.Moved, g.CheckMove(0, 1, 1), "check: reports a legal move");
        MergeTiles(g, 4, 5);
        Eq(SwapGrid.MoveResult.Split, g.CheckMove(0, 1, 1), "check: reports a split");
        Check(SameLayout(before, Layout(g)) && g.Groups.SameGroup(4, 5), "check: does not move or regroup anything");
    }

    static void JoinTests()
    {
        // 3x3: tiles 0 and 1 swapped. Moving tile 0 home also puts tile 1 home; everything joins into one group.
        var g = Grid(3, 3);
        g.TryMove(0, 1, 0);
        JoinAll(g);
        Check(!g.Groups.SameGroup(0, 1), "join: swapped tiles are not joined");
        g.TryMove(0, -1, 0);
        JoinAll(g);
        Check(g.IsSolved(), "join: swapping back solves");
        Eq(9, g.Groups.SizeOf(g.Groups.GroupOf(0)), "join: solved board is one group");

        // Neighbours join anywhere: tiles 0 and 1 placed side by side in the top row, away from home.
        g = Grid(3, 3);
        g.SetLayout(new[] { 6, 7, 2, 3, 4, 5, 0, 1, 8 });
        Check(g.FindJoin(out int a, out int b) && a == 0 && b == 1, "join: correct neighbours away from home are found");
        JoinAll(g);
        Check(g.Groups.SameGroup(0, 1) && g.Groups.SameGroup(6, 7), "join: groups form anywhere on the board");
        Check(!g.Groups.SameGroup(0, 2), "join: no join across a wrong neighbour");

        // No wrap-around: tile 0 in the last column and tile 1 at the start of the next row are not side by side.
        g = Grid(3, 3);
        g.SetLayout(new[] { 2, 3, 0, 1, 4, 5, 6, 7, 8 });
        JoinAll(g);
        Check(!g.Groups.SameGroup(0, 1), "join: no join across the board edge");
    }

    static void PlayTests()
    {
        // Random play: legal drops keep the board consistent and groups never split.
        var rng = new Random(42);
        bool consistent = true, groupsKept = true;
        for (int game = 0; game < 40; game++)
        {
            int cols = 3 + game % 4, rows = cols + game % 3;
            var g = Grid(cols, rows);
            var l = new int[cols * rows];
            SwapGrid.Scramble(cols, rows, rng, l);
            g.SetLayout(l);
            for (int move = 0; move < 400; move++)
            {
                int tile = rng.Next(g.Count);
                int grp = g.Groups.GroupOf(tile);
                int size = g.Groups.SizeOf(grp);
                var r = g.TryMove(grp, rng.Next(-cols + 1, cols), rng.Next(-rows + 1, rows));
                if ((r == SwapGrid.MoveResult.Moved || r == SwapGrid.MoveResult.Split) && g.Groups.SizeOf(g.Groups.GroupOf(tile)) != size) groupsKept = false;
                JoinAll(g);
                if (!Consistent(g)) consistent = false;
            }
        }
        Check(consistent, "play: 16k random drops keep every cell filled and every group in one piece");
        Check(groupsKept, "play: the moved group never changes size");

        // Hint-only play: moving the smallest loose group home always works until the board is solved.
        bool alwaysSolved = true;
        int worstHints = 0;
        for (int game = 0; game < 200; game++)
        {
            int cols = 3 + game % 4, rows = cols + game % 3, n = cols * rows;
            var g = Grid(cols, rows);
            var l = new int[n];
            SwapGrid.Scramble(cols, rows, rng, l);
            g.SetLayout(l);
            // Mix in some random play first so hints start from messy mid-game boards with groups.
            for (int move = 0; move < game % 25; move++)
            {
                g.TryMove(g.Groups.GroupOf(rng.Next(n)), rng.Next(-2, 3), rng.Next(-2, 3));
                JoinAll(g);
            }
            int hints = 0;
            while (!g.IsSolved() && hints <= n)
            {
                // Same preference as PuzzleBoard: smallest group whose move splits nothing, else smallest overall.
                int best = -1, bestScore = int.MaxValue;
                for (int grp = 0; grp < n; grp++)
                {
                    if (!g.Groups.IsGroupId(grp) || (g.DispCol(grp) == 0 && g.DispRow(grp) == 0)) continue;
                    int score = g.Groups.SizeOf(grp) + (g.CheckMoveHome(grp) == SwapGrid.MoveResult.Split ? n : 0);
                    if (score < bestScore) { best = grp; bestScore = score; }
                }
                if (best < 0) break;
                g.TryMoveHome(best);
                JoinAll(g);
                hints++;
            }
            if (!g.IsSolved()) alwaysSolved = false;
            worstHints = Math.Max(worstHints, hints);
        }
        Check(alwaysSolved, "hint: repeated hints always solve the board (worst case " + worstHints + " hints)");
    }

    static void GroupEdgeTests()
    {
        var groups = new TileGroups();
        groups.Reset(3, 3);
        groups.Merge(0, 1);
        groups.Merge(0, 3);
        Eq((byte)(TileGroups.ExposedLeft | TileGroups.ExposedBottom | TileGroups.NotchTopRight), groups.EdgeMask(0), "edges: L-shaped group corner tile");
        Eq((byte)(TileGroups.ExposedRight | TileGroups.ExposedBottom | TileGroups.ExposedTop), groups.EdgeMask(1), "edges: L-shaped group arm tile");
    }

    static int Main(string[] args)
    {
        verbose = Array.IndexOf(args, "-q") < 0;
        ScrambleTests();
        SingleTileTests();
        GroupTests();
        JoinTests();
        PlayTests();
        GroupEdgeTests();
        Console.WriteLine();
        Console.WriteLine(passes + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }
}
