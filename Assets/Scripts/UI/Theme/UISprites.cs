using System;
using UnityEngine;

namespace Snaploop.UI
{
    /// <summary>
    /// Procedural sprites for the whole UI: 9-slice rounded panels, soft shadows, strokes, rings and
    /// a small icon set. Everything is a white mask drawn from signed distance functions (exact
    /// anti-aliasing at any size), tinted by the Image colour. Built lazily, once.
    /// </summary>
    public static class UISprites
    {
        /// <summary>Corner radius of <see cref="Rounded"/> and <see cref="Stroke"/> in sprite pixels.</summary>
        public const float SpriteRadius = 60f;
        /// <summary>Blur width of <see cref="Shadow"/> in sprite pixels.</summary>
        public const float ShadowBlur = 48f;
        const int Icon = 128;

        static Sprite rounded, stroke, shadow, circle, softCircle, ring, annulus, spinner, tile;
        static Sprite back, close, gear, sparkle, lockIcon, share, next, chevron, grid, clock, check, star, play, sound, vibrate;

        public static Sprite Rounded => rounded ??= Make(128, p => Box(p, new Vector2(64, 64), new Vector2(64, 64), SpriteRadius), "Rounded", 62f);
        public static Sprite Stroke => stroke ??= Make(128, p => Mathf.Abs(Box(p, new Vector2(64, 64), new Vector2(61, 61), SpriteRadius - 3f)) - 3f, "Stroke", 62f);
        public static Sprite Circle => circle ??= Make(Icon, p => Disc(p, new Vector2(64, 64), 63f), "Circle");
        public static Sprite Ring => ring ??= Make(Icon, p => Mathf.Abs(Disc(p, new Vector2(64, 64), 58f)) - 5f, "Ring");

        /// <summary>Annulus for radial-filled progress rings.</summary>
        public static Sprite Annulus => annulus ??= Make(256, p => Mathf.Abs(Disc(p, new Vector2(128, 128), 112f)) - 15f, "Annulus");

        /// <summary>Rounded square, used for tiles and the logo.</summary>
        public static Sprite Tile => tile ??= Make(Icon, p => Box(p, new Vector2(64, 64), new Vector2(62, 62), 26f), "Tile");

        /// <summary>Blurred rounded rect, 9-sliced. Place it <see cref="ShadowBlur"/> units larger than the caster.</summary>
        public static Sprite Shadow => shadow ??= MakeSoft(192, p => Box(p, new Vector2(96, 96), new Vector2(96 - ShadowBlur, 96 - ShadowBlur), 28f), ShadowBlur, "Shadow", 95f);

        /// <summary>Radial falloff for glows.</summary>
        public static Sprite SoftCircle => softCircle ??= MakeSoft(Icon, p => Disc(p, new Vector2(64, 64), 2f), 62f, "SoftCircle", 0f);

        public static Sprite Spinner => spinner ??= Make(Icon, p => Arc(p, new Vector2(64, 64), 48f, 0f, 270f) - 9f, "Spinner");

        public static Sprite Back => back ??= MakeIcon("Back", p => Min(Seg(p, V(0.62f, 0.84f), V(0.34f, 0.5f)), Seg(p, V(0.34f, 0.5f), V(0.62f, 0.16f))) - 0.08f);
        public static Sprite Chevron => chevron ??= MakeIcon("Chevron", p => Min(Seg(p, V(0.38f, 0.82f), V(0.66f, 0.5f)), Seg(p, V(0.66f, 0.5f), V(0.38f, 0.18f))) - 0.075f);
        public static Sprite Close => close ??= MakeIcon("Close", p => Min(Seg(p, V(0.26f, 0.26f), V(0.74f, 0.74f)), Seg(p, V(0.26f, 0.74f), V(0.74f, 0.26f))) - 0.08f);
        public static Sprite Next => next ??= MakeIcon("Next", p => Min(Seg(p, V(0.18f, 0.5f), V(0.8f, 0.5f)), Min(Seg(p, V(0.8f, 0.5f), V(0.54f, 0.76f)), Seg(p, V(0.8f, 0.5f), V(0.54f, 0.24f)))) - 0.08f);
        public static Sprite Check => check ??= MakeIcon("Check", p => Min(Seg(p, V(0.2f, 0.52f), V(0.42f, 0.3f)), Seg(p, V(0.42f, 0.3f), V(0.8f, 0.72f))) - 0.09f);
        public static Sprite Play => play ??= MakeIcon("Play", p => Poly(p, PlayPts) - 0.06f);
        public static Sprite Star => star ??= MakeIcon("Star", p => Poly(p, StarPts) - 0.035f);
        public static Sprite Sparkle => sparkle ??= MakeIcon("Sparkle", p => Min(Poly(p, SparkleBig), Poly(p, SparkleSmall)) - 0.02f);

        public static Sprite Grid => grid ??= MakeIcon("Grid", p =>
            Min(Min(Box(p, V(0.29f, 0.29f), V(0.17f, 0.17f), 0.06f), Box(p, V(0.71f, 0.29f), V(0.17f, 0.17f), 0.06f)),
                Min(Box(p, V(0.29f, 0.71f), V(0.17f, 0.17f), 0.06f), Box(p, V(0.71f, 0.71f), V(0.17f, 0.17f), 0.06f))));

        public static Sprite Clock => clock ??= MakeIcon("Clock", p =>
            Min(Mathf.Abs(Disc(p, V(0.5f, 0.5f), 0.37f)) - 0.065f,
                Min(Seg(p, V(0.5f, 0.5f), V(0.5f, 0.74f)), Seg(p, V(0.5f, 0.5f), V(0.67f, 0.5f))) - 0.06f));

        public static Sprite Lock => lockIcon ??= MakeIcon("Lock", p =>
        {
            float body = Box(p, V(0.5f, 0.36f), V(0.3f, 0.25f), 0.07f);
            Vector2 c = V(0.5f, 0.6f);
            float shackle = p.y >= c.y
                ? Mathf.Abs(Disc(p, c, 0.17f)) - 0.065f
                : Min(Seg(p, V(0.33f, 0.6f), V(0.33f, 0.48f)), Seg(p, V(0.67f, 0.6f), V(0.67f, 0.48f))) - 0.065f;
            float d = Min(body, shackle);
            float hole = Min(Disc(p, V(0.5f, 0.39f), 0.06f), Box(p, V(0.5f, 0.29f), V(0.025f, 0.07f), 0.02f));
            return Mathf.Max(d, -hole);
        });

        public static Sprite Share => share ??= MakeIcon("Share", p =>
            Min(Min(Seg(p, V(0.5f, 0.4f), V(0.5f, 0.84f)), Min(Seg(p, V(0.5f, 0.86f), V(0.32f, 0.68f)), Seg(p, V(0.5f, 0.86f), V(0.68f, 0.68f)))),
                Min(Seg(p, V(0.22f, 0.56f), V(0.22f, 0.18f)), Min(Seg(p, V(0.22f, 0.18f), V(0.78f, 0.18f)), Seg(p, V(0.78f, 0.18f), V(0.78f, 0.56f))))) - 0.065f);

        public static Sprite Gear => gear ??= MakeIcon("Gear", p =>
        {
            Vector2 c = V(0.5f, 0.5f);
            float d = Disc(p, c, 0.3f);
            Vector2 q = p - c;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                var r = new Vector2(q.x * cs + q.y * sn, -q.x * sn + q.y * cs);
                d = Min(d, Box(r, V(0.36f, 0f), V(0.1f, 0.085f), 0.035f));
            }
            return Mathf.Max(d, -Disc(p, c, 0.13f));
        });

        public static Sprite Sound => sound ??= MakeIcon("Sound", p =>
        {
            float body = Poly(p, SpeakerPts) - 0.03f;
            Vector2 c = V(0.46f, 0.5f);
            float w1 = Arc01(p, c, 0.22f, -50f, 50f) - 0.05f, w2 = Arc01(p, c, 0.36f, -50f, 50f) - 0.05f;
            return Min(body, Min(w1, w2));
        });

        public static Sprite Vibrate => vibrate ??= MakeIcon("Vibrate", p =>
            Min(Mathf.Abs(Box(p, V(0.5f, 0.5f), V(0.17f, 0.3f), 0.07f)) - 0.05f,
                Min(Seg(p, V(0.16f, 0.36f), V(0.16f, 0.64f)), Seg(p, V(0.84f, 0.36f), V(0.84f, 0.64f))) - 0.05f));

        static Sprite tokenHex, tokenHexInk, tokenPiece;

        /// <summary>Snaploop's token: a rounded hexagon (face). Drawn in code; not a coin.</summary>
        public static Sprite TokenHex => tokenHex ??= MakeIcon("TokenHex", p => Poly(p, HexPts) - 0.045f);

        /// <summary><see cref="TokenHex"/> grown by the ink width, drawn behind it as rim and outline.</summary>
        public static Sprite TokenHexInk => tokenHexInk ??= MakeIcon("TokenHexInk", p => Poly(p, HexPts) - 0.085f);

        /// <summary>Jigsaw piece embossed on the token: a square with two knobs and one socket.</summary>
        public static Sprite TokenPiece => tokenPiece ??= MakeIcon("TokenPiece", p =>
        {
            float body = Box(p, V(0.47f, 0.45f), V(0.2f, 0.2f), 0.05f);
            float knobs = Min(Disc(p, V(0.47f, 0.7f), 0.095f), Disc(p, V(0.72f, 0.45f), 0.095f));
            float socket = Disc(p, V(0.29f, 0.45f), 0.085f);
            return Mathf.Max(Min(body, knobs), -socket);
        });

        static readonly Vector2[] HexPts = HexPoly(V(0.5f, 0.5f), 0.4f);

        static Vector2[] HexPoly(Vector2 c, float r)
        {
            var pts = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = (90f + i * 60f) * Mathf.Deg2Rad;
                pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return pts;
        }

        static Sprite hand, rays, ribbonTail, quarterArc, quarterArcInk, starInk;
        static Texture2D lattice;

        /// <summary>Star grown by the ink width, drawn behind <see cref="Star"/> as its outline.</summary>
        public static Sprite StarInk => starInk ??= MakeIcon("StarInk", p => Poly(p, StarPts) - 0.075f);

        public static Sprite Hand => hand ??= MakeIcon("Hand", p =>
        {
            float finger = Seg(p, V(0.42f, 0.5f), V(0.42f, 0.86f)) - 0.085f;
            float palm = Box(p, V(0.55f, 0.36f), V(0.22f, 0.22f), 0.13f);
            float knuckles = Min(Seg(p, V(0.58f, 0.42f), V(0.58f, 0.6f)), Seg(p, V(0.71f, 0.4f), V(0.71f, 0.55f))) - 0.075f;
            float thumb = Seg(p, V(0.3f, 0.42f), V(0.2f, 0.52f)) - 0.07f;
            return Min(Min(finger, palm), Min(knuckles, thumb));
        });

        /// <summary>14 soft rays fading from the centre, for the win sunburst.</summary>
        public static Sprite Rays
        {
            get
            {
                if (rays != null) return rays;
                const int size = 256;
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 q = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f);
                    float r = q.magnitude / (size * 0.5f);
                    float a = Mathf.Repeat(Mathf.Atan2(q.y, q.x) / (2f * Mathf.PI) * 14f, 1f);
                    float wedge = Mathf.Clamp01((0.32f - Mathf.Abs(a - 0.5f)) * 14f);
                    float fade = Mathf.Clamp01(1f - r);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(wedge * fade * fade * 255f));
                }
                return rays = ToSprite(px, size, "Rays", 0f);
            }
        }

        /// <summary>Ribbon tail with a forked end on the right; mirror it for the left tail.</summary>
        public static Sprite RibbonTail => ribbonTail ??= Make(Icon, p => Poly(p / Icon, TailPts) * Icon, "RibbonTail");

        /// <summary>A quarter of the logo loop (10° to 80°), with round ends.</summary>
        public static Sprite QuarterArc => quarterArc ??= Make(Icon, p => Arc(p, new Vector2(0, 0), 92f, 12f, 78f) - 17f, "QuarterArc");
        public static Sprite QuarterArcInk => quarterArcInk ??= Make(Icon, p => Arc(p, new Vector2(0, 0), 92f, 12f, 78f) - 24f, "QuarterArcInk");

        /// <summary>
        /// Faint jigsaw lattice for the backdrop (white lines; tint and opacity come from the renderer).
        /// Some edges are skipped and the lines fade toward the bottom. Painted once.
        /// </summary>
        public static Texture2D Lattice
        {
            get
            {
                if (lattice != null) return lattice;
                const int w = 432, h = 960, cols = 8;
                float s = w / (float)cols;
                int rows = Mathf.CeilToInt(h / s) + 1;
                var rng = new System.Random(7);
                var skipH = new bool[(cols + 1) * (rows + 1)];
                var skipV = new bool[(cols + 1) * (rows + 1)];
                for (int i = 0; i < skipH.Length; i++) { skipH[i] = rng.NextDouble() < 0.35; skipV[i] = rng.NextDouble() < 0.35; }

                var px = new Color32[w * h];
                float knobR = 0.17f * s, knobOff = 0.13f * s, line = 1.4f;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int ci = Mathf.FloorToInt(x / s), cj = Mathf.FloorToInt(y / s);
                    float best = 99f, fx = x + 0.5f, fy = y + 0.5f;
                    // A knob only reaches into the two cells beside its edge, so the cell's own four edges suffice.
                    for (int e = 0; e < 2; e++)
                    {
                        int hj = cj + e, k = hj * (cols + 1) + ci;
                        if (hj <= rows && !skipH[k])
                            best = Mathf.Min(best, KnobEdge(fx - ci * s, fy - hj * s, s, knobR, (((ci + hj) & 1) == 0 ? 1f : -1f) * knobOff));
                        int vi = ci + e;
                        k = cj * (cols + 1) + vi;
                        if (vi <= cols && !skipV[k])
                            best = Mathf.Min(best, KnobEdge(fy - cj * s, fx - vi * s, s, knobR, (((vi + cj) & 1) == 0 ? -1f : 1f) * knobOff));
                    }
                    float a = Mathf.Clamp01(line - best) * Mathf.Lerp(0.45f, 1f, y / (float)h);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                lattice = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Lattice", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                lattice.SetPixels32(px);
                lattice.Apply(false, true);
                return lattice;
            }
        }

        /// <summary>Distance to an edge along +x of length <paramref name="len"/> with a round knob in its middle.</summary>
        static float KnobEdge(float x, float y, float len, float r, float off)
        {
            if (x < -r || x > len + r) return 99f;
            float half = len * 0.5f;
            float dLine = Mathf.Abs(x - half) > r * 0.8f && x >= 0f && x <= len ? Mathf.Abs(y) : 99f;
            float dx = x - half, dy = y - off;
            bool bulgeSide = off >= 0f ? y >= 0f : y <= 0f;
            float dKnob = bulgeSide ? Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r) : 99f;
            return Mathf.Min(dLine, dKnob);
        }

        static readonly Vector2[] TailPts = { V(0.02f, 0.14f), V(0.98f, 0.14f), V(0.7f, 0.5f), V(0.98f, 0.86f), V(0.02f, 0.86f) };
        static readonly Vector2[] PlayPts = { V(0.34f, 0.24f), V(0.34f, 0.76f), V(0.78f, 0.5f) };
        static readonly Vector2[] SpeakerPts = { V(0.14f, 0.38f), V(0.3f, 0.38f), V(0.5f, 0.2f), V(0.5f, 0.8f), V(0.3f, 0.62f), V(0.14f, 0.62f) };
        static readonly Vector2[] StarPts = StarPoly(V(0.5f, 0.52f), 5, 0.43f, 0.19f, 90f);
        static readonly Vector2[] SparkleBig = StarPoly(V(0.42f, 0.44f), 4, 0.36f, 0.09f, 90f);
        static readonly Vector2[] SparkleSmall = StarPoly(V(0.77f, 0.78f), 4, 0.16f, 0.045f, 90f);

        // ------------------------------------------------------------ distance functions (negative inside)

        static Vector2 V(float x, float y) => new Vector2(x, y);
        static float Min(float a, float b) => a < b ? a : b;
        static float Disc(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        static float Box(Vector2 p, Vector2 c, Vector2 half, float r)
        {
            float qx = Mathf.Abs(p.x - c.x) - half.x + r, qy = Mathf.Abs(p.y - c.y) - half.y + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Distance to a circular arc from <paramref name="fromDeg"/> to <paramref name="toDeg"/> (CCW, 0 = +x).</summary>
        static float Arc(Vector2 p, Vector2 c, float r, float fromDeg, float toDeg)
        {
            Vector2 q = p - c;
            float ang = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;
            float rel = Mathf.Repeat(ang - fromDeg, 360f);
            if (rel <= toDeg - fromDeg) return Mathf.Abs(q.magnitude - r);
            Vector2 a = c + r * new Vector2(Mathf.Cos(fromDeg * Mathf.Deg2Rad), Mathf.Sin(fromDeg * Mathf.Deg2Rad));
            Vector2 b = c + r * new Vector2(Mathf.Cos(toDeg * Mathf.Deg2Rad), Mathf.Sin(toDeg * Mathf.Deg2Rad));
            return Mathf.Min((p - a).magnitude, (p - b).magnitude);
        }

        static float Arc01(Vector2 p, Vector2 c, float r, float fromDeg, float toDeg)
        {
            // Wave arcs only on the speaker's open side.
            return p.x < c.x ? 99f : Arc(p, c, r, fromDeg, toDeg);
        }

        static float Poly(Vector2 p, Vector2[] v)
        {
            float d = (p - v[0]).sqrMagnitude, s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i], w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / e.sqrMagnitude);
                d = Mathf.Min(d, b.sqrMagnitude);
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }

        static Vector2[] StarPoly(Vector2 c, int points, float outer, float inner, float startDeg)
        {
            var pts = new Vector2[points * 2];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = (startDeg + i * 180f / points) * Mathf.Deg2Rad;
                float r = i % 2 == 0 ? outer : inner;
                pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return pts;
        }

        // ------------------------------------------------------------ rasterizing

        /// <summary>Icon from a distance function in normalized coordinates (0..1, y up).</summary>
        static Sprite MakeIcon(string name, Func<Vector2, float> sdf01) =>
            Make(Icon, p => sdf01(p / Icon) * Icon, name);

        /// <summary>Hard-edged shape from a distance function in pixels, anti-aliased over one pixel.</summary>
        static Sprite Make(int size, Func<Vector2, float> sdf, string name, float border = 0f)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = sdf(new Vector2(x + 0.5f, y + 0.5f));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - d) * 255f));
            }
            return ToSprite(px, size, name, border);
        }

        /// <summary>Soft shape: alpha falls off smoothly over <paramref name="blur"/> pixels outside the shape.</summary>
        static Sprite MakeSoft(int size, Func<Vector2, float> sdf, float blur, string name, float border)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = sdf(new Vector2(x + 0.5f, y + 0.5f));
                float t = 1f - Mathf.Clamp01(d / blur);
                float a = t * t * (3f - 2f * t);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
            return ToSprite(px, size, name, border);
        }

        static Sprite ToSprite(Color32[] px, int size, string name, float border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
    }
}
