using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>
    /// Snaploop's identity pieces: the golden-hour wordmark and the loop mark (a ring of four snapped arcs,
    /// the last one snapping in). Original shapes; no puzzle-piece play symbol.
    /// </summary>
    public static class Brand
    {
        static UITheme T => UITheme.Current;

        /// <summary>Tilted "Snaploop" in the display face: sunset gradient, ink outline and a deep purple extrude.</summary>
        public static Text Wordmark(Transform parent, float sizeDp, string text = "Snaploop")
        {
            var t = UIFactory.Label("Wordmark", parent, text, T.F(sizeDp), Color.white);
            t.font = UIFactory.FontFor(TextRole.Title);
            // Gradient before the outline, so the ink and extrude copies keep their own colours.
            UIFactory.Gradient(t, T.sunsetA, T.sunsetB);
            var ink = t.gameObject.AddComponent<InkOutline>();
            ink.Set(T.ink, T.U(sizeDp * 0.055f), T.sunsetExtrude, T.U(sizeDp * 0.09f));
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.rectTransform.localEulerAngles = new Vector3(0f, 0f, 4f);
            return t;
        }

        public sealed class LoopMark
        {
            public RectTransform Root;
            /// <summary>The fourth arc, which snaps into place.</summary>
            public RectTransform Snapper;
            public Vector2 SnapperOut;

            /// <summary>0 = fourth arc pulled out, 1 = snapped in.</summary>
            public void SetSnap(float k)
            {
                Snapper.anchoredPosition = Vector2.LerpUnclamped(SnapperOut, Vector2.zero, k);
            }
        }

        /// <summary>Ring of four rounded arcs in the signature colours; the bottom-right one sits pulled out until snapped.</summary>
        public static LoopMark Mark(Transform parent, float sizeDp)
        {
            var root = UIFactory.Rect("LoopMark", parent);
            float size = T.U(sizeDp);
            root.sizeDelta = new Vector2(size, size);
            // QuarterArc's outer radius is 109 of its 128 px, centred on the sprite's corner.
            float quad = size * 0.5f * 128f / 109f;
            Color[] colors = { T.sunsetA, Color.Lerp(T.sunsetA, T.sunsetB, 0.6f), T.sunsetC, T.sunsetB };
            var mark = new LoopMark { Root = root };
            for (int q = 0; q < 4; q++)
            {
                var holder = UIFactory.Rect("Arc" + q, root);
                UIFactory.Place(holder, new Vector2(0.5f, 0.5f), Vector2.zero);
                var ink = UIFactory.Image("Ink", holder, T.ink, UISprites.QuarterArcInk);
                var arc = UIFactory.Image("Face", holder, colors[q], UISprites.QuarterArc);
                foreach (var img in new[] { ink, arc })
                {
                    var rt = img.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = Vector2.zero;
                    rt.sizeDelta = new Vector2(quad, quad);
                    rt.anchoredPosition = Vector2.zero;
                    rt.localEulerAngles = new Vector3(0f, 0f, q * 90f);
                }
                UIFactory.Gradient(arc, Color.Lerp(colors[q], Color.white, 0.25f), colors[q]);
                if (q == 3)
                {
                    mark.Snapper = holder;
                    mark.SnapperOut = new Vector2(size * 0.09f, -size * 0.09f);
                }
            }
            mark.SetSnap(0f);
            return mark;
        }
    }
}
