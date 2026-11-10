using System;
using Snaploop.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>Palette shared by every screen.</summary>
    public static class Palette
    {
        public static readonly Color Background = new Color32(16, 18, 25, 255);
        public static readonly Color Surface = new Color32(30, 33, 45, 255);
        public static readonly Color SurfaceHigh = new Color32(44, 48, 64, 255);
        public static readonly Color Accent = new Color32(255, 112, 77, 255);   // sunset coral
        public static readonly Color Accent2 = new Color32(46, 196, 182, 255);  // teal
        public static readonly Color Text = new Color32(245, 246, 250, 255);
        public static readonly Color TextDim = new Color32(165, 170, 190, 255);
        public static readonly Color Star = new Color32(255, 200, 61, 255);
        public static readonly Color StarEmpty = new Color32(70, 74, 92, 255);
        public static readonly Color Scrim = new Color(0f, 0f, 0f, 0.62f);
    }

    /// <summary>
    /// Builds uGUI elements in code (no prefabs), with procedurally generated sprites so the UI needs
    /// no imported art. Uses Unity's built-in LegacyRuntime font.
    /// </summary>
    public static class UIFactory
    {
        static Font font;
        static Sprite rounded, circle, star, check;

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Sprite Rounded => rounded != null ? rounded : (rounded = MakeRounded(64, 30));
        public static Sprite Circle => circle != null ? circle : (circle = MakeRounded(64, 32, sliced: false));
        public static Sprite Star => star != null ? star : (star = MakeStar(96));
        public static Sprite Check => check != null ? check : (check = MakeCheck(96));

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Anchors at a normalized point with a fixed size.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset = default)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        /// <summary>Full-width band anchored to the top (anchorY = 1) or bottom (anchorY = 0).</summary>
        public static RectTransform Band(RectTransform rt, float anchorY, float height, float inset = 0f)
        {
            rt.anchorMin = new Vector2(0f, anchorY);
            rt.anchorMax = new Vector2(1f, anchorY);
            rt.pivot = new Vector2(0.5f, anchorY);
            rt.sizeDelta = new Vector2(-2f * inset, height);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        public static Image Image(string name, Transform parent, Color color, Sprite sprite = null, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sprite.border.sqrMagnitude > 0 ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            }
            return img;
        }

        public static Image Panel(string name, Transform parent, Color color, bool raycast = true)
        {
            var img = Image(name, parent, color, Rounded, raycast);
            img.pixelsPerUnitMultiplier = 1f;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            return t;
        }

        public static Button Button(string name, Transform parent, string label, Color bg, UnityAction onClick,
            int fontSize = 44, Color? textColor = null)
        {
            var img = Image(name, parent, bg, Rounded, raycast: true);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            btn.targetGraphic = img;
            if (onClick != null)
            {
                btn.onClick.AddListener(Sfx.PlayClick);
                btn.onClick.AddListener(onClick);
            }
            if (!string.IsNullOrEmpty(label))
            {
                var t = Label("Label", img.transform, label, fontSize, textColor ?? Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                Stretch(t.rectTransform, 12, 12, 0, 0);
            }
            return btn;
        }

        public static Text ButtonLabel(Button b) => b.GetComponentInChildren<Text>(true);

        /// <summary>Row of three star images; returns them left to right.</summary>
        public static Image[] Stars(Transform parent, float size, float spacing)
        {
            var row = Rect("Stars", parent);
            var stars = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                stars[i] = Image("Star" + i, row, Palette.StarEmpty, Star);
                Place(stars[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(size, size), new Vector2((i - 1) * (size + spacing), 0f));
            }
            return stars;
        }

        public static void SetStars(Image[] stars, int count)
        {
            for (int i = 0; i < stars.Length; i++) stars[i].color = i < count ? Palette.Star : Palette.StarEmpty;
        }

        // ------------------------------------------------------------ procedural sprites

        static Sprite MakeRounded(int size, float radius, bool sliced = true)
        {
            var tex = NewTex(size, "Rounded");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = sliced ? radius + 1 : 0;
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        static Sprite MakeStar(int size)
        {
            var poly = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float ang = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = (i % 2 == 0 ? 0.48f : 0.2f) * size;
                poly[i] = new Vector2(size * 0.5f + Mathf.Cos(ang) * r, size * 0.52f + Mathf.Sin(ang) * r);
            }
            return Rasterize(size, "Star", p => Puzzle.Triangulator.Contains(poly, p));
        }

        static Sprite MakeCheck(int size)
        {
            Vector2 a = new Vector2(0.18f, 0.52f) * size, b = new Vector2(0.42f, 0.28f) * size, c = new Vector2(0.84f, 0.74f) * size;
            float th = 0.09f * size;
            return Rasterize(size, "Check", p => SegDist(p, a, b) < th || SegDist(p, b, c) < th);
        }

        // 4x supersampled mask rasterizer.
        static Sprite Rasterize(int size, string name, Func<Vector2, bool> inside)
        {
            var tex = NewTex(size, name);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                    if (inside(new Vector2(x + 0.25f + 0.5f * sx, y + 0.25f + 0.5f * sy))) hits++;
                px[y * size + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 4));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        static Texture2D NewTex(int size, string name)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }
    }
}
