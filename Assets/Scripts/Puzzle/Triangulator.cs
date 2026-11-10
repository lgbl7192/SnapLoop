using System.Collections.Generic;
using UnityEngine;

namespace Snaploop.Puzzle
{
    /// <summary>Ear-clipping triangulation for simple polygons. Runs once per piece at level load.</summary>
    public static class Triangulator
    {
        /// <summary>
        /// Triangulates a simple CCW polygon. Appends indices (offset by <paramref name="baseIndex"/>)
        /// to <paramref name="indices"/> in CCW order.
        /// </summary>
        public static void Triangulate(IList<Vector2> poly, List<int> indices, int baseIndex)
        {
            int n = poly.Count;
            if (n < 3) return;

            var remaining = new List<int>(n);
            for (int k = 0; k < n; k++) remaining.Add(k);
            if (SignedArea(poly) < 0f) remaining.Reverse();

            int m = remaining.Count;
            int i = 0;
            int misses = 0;
            while (m > 3)
            {
                int i0 = remaining[(i + m - 1) % m];
                int i1 = remaining[i];
                int i2 = remaining[(i + 1) % m];

                // After a full lap without an ear (near-collinear points), clip anyway rather than loop forever.
                if (misses >= m || IsEar(poly, remaining, i0, i1, i2))
                {
                    indices.Add(baseIndex + i0);
                    indices.Add(baseIndex + i1);
                    indices.Add(baseIndex + i2);
                    remaining.RemoveAt(i);
                    m--;
                    misses = 0;
                    if (i >= m) i = 0;
                }
                else
                {
                    i = (i + 1) % m;
                    misses++;
                }
            }
            indices.Add(baseIndex + remaining[0]);
            indices.Add(baseIndex + remaining[1]);
            indices.Add(baseIndex + remaining[2]);
        }

        public static float SignedArea(IList<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += (poly[j].x * poly[i].y) - (poly[i].x * poly[j].y);
            return a * 0.5f;
        }

        static bool IsEar(IList<Vector2> poly, List<int> remaining, int i0, int i1, int i2)
        {
            Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
            if (Cross(b - a, c - b) <= 1e-9f) return false; // reflex or degenerate
            for (int k = 0; k < remaining.Count; k++)
            {
                int vi = remaining[k];
                if (vi == i0 || vi == i1 || vi == i2) continue;
                if (PointInTriangle(poly[vi], a, b, c)) return false;
            }
            return true;
        }

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            return d1 >= 0f && d2 >= 0f && d3 >= 0f;
        }

        /// <summary>Even-odd point-in-polygon test (no allocations).</summary>
        public static bool Contains(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
