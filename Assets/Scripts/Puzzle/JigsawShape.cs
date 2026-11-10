using System.Collections.Generic;
using UnityEngine;

namespace Snaploop.Puzzle
{
    /// <summary>
    /// Generates classic jigsaw outlines for a cols x rows grid. Each interior edge is sampled once
    /// and shared by both neighbours, so pieces always fit exactly. Coordinates are board space
    /// (origin at board centre, y up). Outlines are counter-clockwise.
    /// </summary>
    public static class JigsawShape
    {
        const int SamplesPerCurve = 8;
        const float TabSize = 0.1f;   // relative to the shorter cell side
        const float Jitter = 0.035f;

        /// <summary>Returns one CCW outline per piece, indexed [row * cols + col], row 0 at the bottom.</summary>
        public static List<Vector2>[] Build(int cols, int rows, Vector2 boardSize, int seed)
        {
            var rng = new System.Random(seed);
            float cw = boardSize.x / cols, ch = boardSize.y / rows;
            float s = Mathf.Min(cw, ch);
            Vector2 origin = -boardSize * 0.5f;

            // hEdges[r, c]: horizontal line at y = r (1..rows-1), spanning column c, sampled left -> right.
            var hEdges = new List<Vector2>[rows + 1, cols];
            for (int r = 1; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var a = origin + new Vector2(c * cw, r * ch);
                var b = a + new Vector2(cw, 0f);
                hEdges[r, c] = Edge(a, b, new Vector2(0f, 1f), s, rng);
            }

            // vEdges[c, r]: vertical line at x = c (1..cols-1), spanning row r, sampled bottom -> top.
            var vEdges = new List<Vector2>[cols + 1, rows];
            for (int c = 1; c < cols; c++)
            for (int r = 0; r < rows; r++)
            {
                var a = origin + new Vector2(c * cw, r * ch);
                var b = a + new Vector2(0f, ch);
                vEdges[c, r] = Edge(a, b, new Vector2(1f, 0f), s, rng);
            }

            var outlines = new List<Vector2>[cols * rows];
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var bl = origin + new Vector2(c * cw, r * ch);
                var br = bl + new Vector2(cw, 0f);
                var tr = bl + new Vector2(cw, ch);
                var tl = bl + new Vector2(0f, ch);
                var o = new List<Vector2>(4 * (3 * SamplesPerCurve + 2));

                AppendEdge(o, r == 0 ? null : hEdges[r, c], bl, br, false);
                AppendEdge(o, c == cols - 1 ? null : vEdges[c + 1, r], br, tr, false);
                AppendEdge(o, r == rows - 1 ? null : hEdges[r + 1, c], tr, tl, true);
                AppendEdge(o, c == 0 ? null : vEdges[c, r], tl, bl, true);
                outlines[r * cols + c] = o;
            }
            return outlines;
        }

        // Appends every point of the edge except its last (which is the next edge's first).
        static void AppendEdge(List<Vector2> o, List<Vector2> edge, Vector2 from, Vector2 to, bool reversed)
        {
            if (edge == null) { o.Add(from); return; }
            if (!reversed) for (int i = 0; i < edge.Count - 1; i++) o.Add(edge[i]);
            else for (int i = edge.Count - 1; i > 0; i--) o.Add(edge[i]);
        }

        /// <summary>
        /// Samples one edge from a to b. The tab profile (three cubic Béziers, after the widely used
        /// Draradech jigsaw generator) occupies a centred span of length s; the rest is straight.
        /// </summary>
        static List<Vector2> Edge(Vector2 a, Vector2 b, Vector2 normal, float s, System.Random rng)
        {
            float flip = rng.NextDouble() < 0.5 ? -1f : 1f;
            float t = TabSize;
            float ja = J(rng), jb = J(rng), jc = J(rng), jd = J(rng), je = J(rng);

            // Control points in (along, across) units of the tab span, along in [0,1].
            var p = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0.2f, ja),
                new Vector2(0.5f + jb + jd, -t + jc),
                new Vector2(0.5f - t + jb, t + jc),
                new Vector2(0.5f - 2f * t + jb - jd, 3f * t + jc),
                new Vector2(0.5f + 2f * t + jb - jd, 3f * t + jc),
                new Vector2(0.5f + t + jb, t + jc),
                new Vector2(0.5f + jb + jd, -t + jc),
                new Vector2(0.8f, je),
                new Vector2(1f, 0f),
            };

            float len = Vector2.Distance(a, b);
            Vector2 dir = (b - a) / len;
            float lead = (len - s) * 0.5f;
            var pts = new List<Vector2>(3 * SamplesPerCurve + 3);
            pts.Add(a);
            if (lead > 1e-5f) pts.Add(a + dir * lead);
            for (int seg = 0; seg < 3; seg++)
            {
                int k = seg * 3;
                for (int i = 1; i <= SamplesPerCurve; i++)
                {
                    float u = i / (float)SamplesPerCurve;
                    Vector2 q = Bezier(p[k], p[k + 1], p[k + 2], p[k + 3], u);
                    pts.Add(a + dir * (lead + q.x * s) + normal * (q.y * s * flip));
                }
            }
            if (lead > 1e-5f) pts.Add(b);
            else pts[pts.Count - 1] = b;
            return pts;
        }

        static float J(System.Random rng) => ((float)rng.NextDouble() * 2f - 1f) * Jitter;

        static Vector2 Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }
    }
}
