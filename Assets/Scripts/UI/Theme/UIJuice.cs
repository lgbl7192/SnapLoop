using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Snaploop.UI
{
    public enum EaseType { Linear, OutQuad, OutCubic, InCubic, InOutCubic, OutBack, OutBackStrong, OutElastic }

    public static class Ease
    {
        public static float Apply(EaseType type, float t)
        {
            t = Mathf.Clamp01(t);
            switch (type)
            {
                case EaseType.OutQuad: return 1f - (1f - t) * (1f - t);
                case EaseType.OutCubic: { float u = 1f - t; return 1f - u * u * u; }
                case EaseType.InCubic: return t * t * t;
                case EaseType.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                case EaseType.OutBack: return OutBack(t, 1.70158f);
                case EaseType.OutBackStrong: return OutBack(t, 2.6f);
                case EaseType.OutElastic:
                    if (t <= 0f || t >= 1f) return t;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
                default: return t;
            }
        }

        static float OutBack(float t, float s)
        {
            float u = t - 1f;
            return 1f + (s + 1f) * u * u * u + s * u * u;
        }
    }

    /// <summary>
    /// One reusable tween per object (scale, alpha, offset from a home position, rotation), driven by unscaled time.
    /// Configure with the fluent setters, then <see cref="Play"/>. Re-playing reuses the component, so steady-state use allocates nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UITween : MonoBehaviour
    {
        RectTransform rt;
        CanvasGroup group;
        Vector2 home;
        bool homeSet;

        bool useScale, useAlpha, useMove, useRot;
        Vector3 scaleFrom, scaleTo;
        float alphaFrom, alphaTo, rotFrom, rotTo;
        Vector2 moveFrom, moveTo;
        float delay, duration, time;
        EaseType ease;
        bool deactivateOnDone;

        public bool Playing => enabled;

        public static UITween Get(Component c)
        {
            var tw = c.GetComponent<UITween>();
            if (tw == null)
            {
                tw = c.gameObject.AddComponent<UITween>();
                tw.enabled = false;
            }
            tw.rt = (RectTransform)c.transform;
            if (!tw.homeSet) { tw.home = tw.rt.anchoredPosition; tw.homeSet = true; }
            tw.useScale = tw.useAlpha = tw.useMove = tw.useRot = false;
            tw.deactivateOnDone = false;
            return tw;
        }

        /// <summary>Re-reads the resting position (call after re-laying out the object).</summary>
        public UITween Home() { home = rt.anchoredPosition; return this; }
        public UITween Home(Vector2 p) { home = p; return this; }

        public UITween Scale(float from, float to) { useScale = true; scaleFrom = Vector3.one * from; scaleTo = Vector3.one * to; return this; }
        public UITween Scale(Vector3 from, Vector3 to) { useScale = true; scaleFrom = from; scaleTo = to; return this; }
        public UITween Rotate(float fromDeg, float toDeg) { useRot = true; rotFrom = fromDeg; rotTo = toDeg; return this; }
        public UITween Move(Vector2 fromOffset, Vector2 toOffset) { useMove = true; moveFrom = fromOffset; moveTo = toOffset; return this; }

        public UITween Fade(float from, float to)
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            useAlpha = true; alphaFrom = from; alphaTo = to;
            return this;
        }

        /// <summary>Deactivates the object when the tween ends (for hide transitions).</summary>
        public UITween ThenHide() { deactivateOnDone = true; return this; }

        public void Play(float seconds, EaseType easing, float delaySeconds = 0f)
        {
            duration = Mathf.Max(0.0001f, seconds);
            ease = easing;
            delay = delaySeconds;
            time = 0f;
            Apply(0f);
            enabled = true;
        }

        public void Finish()
        {
            if (!enabled) return;
            Apply(1f);
            enabled = false;
            if (deactivateOnDone) gameObject.SetActive(false);
        }

        void OnDisable()
        {
            if (!homeSet || rt == null || time >= delay + duration) return;
            // Deactivated mid-tween: land on the end state so a later re-show doesn't resume or re-hide.
            time = delay + duration;
            deactivateOnDone = false;
            Apply(1f);
        }

        void Update()
        {
            time += Time.unscaledDeltaTime;
            float t = (time - delay) / duration;
            if (t < 0f) return;
            if (t >= 1f) { Finish(); return; }
            Apply(t);
        }

        void Apply(float t)
        {
            float e = Ease.Apply(ease, t);
            if (useScale) rt.localScale = Vector3.LerpUnclamped(scaleFrom, scaleTo, e);
            if (useAlpha) group.alpha = Mathf.Lerp(alphaFrom, alphaTo, Mathf.Clamp01(e));
            if (useMove) rt.anchoredPosition = home + Vector2.LerpUnclamped(moveFrom, moveTo, e);
            if (useRot) rt.localEulerAngles = new Vector3(0f, 0f, Mathf.LerpUnclamped(rotFrom, rotTo, e));
        }
    }

    /// <summary>
    /// Press feedback: the control squashes and its face sinks onto its lip, then springs back with a little bounce.
    /// Inside a ScrollRect the event system sends PointerUp when a drag starts, so scrolling cancels the press.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JuicyPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public RectTransform face;
        public float sink;
        public float squash = 0.03f;

        Selectable selectable;
        Vector2 faceHome;
        float x, v, target;

        void Awake()
        {
            selectable = GetComponent<Selectable>();
            if (face != null) faceHome = face.anchoredPosition;
            enabled = false;
        }

        public void Bind(RectTransform faceRect, float sinkUnits, float squashAmount)
        {
            face = faceRect;
            sink = sinkUnits;
            squash = squashAmount;
            if (face != null) faceHome = face.anchoredPosition;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (selectable != null && !selectable.IsInteractable()) return;
            target = 1f;
            enabled = true;
        }

        public void OnPointerUp(PointerEventData e) => Release();
        public void OnPointerExit(PointerEventData e) => Release();

        void Release()
        {
            if (target == 0f) return;
            target = 0f;
            enabled = true;
        }

        void OnDisable()
        {
            target = 0f; x = 0f; v = 0f;
            Apply(0f);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            if (target > 0f)
            {
                x = Mathf.MoveTowards(x, 1f, dt / 0.07f);
                v = 0f;
            }
            else
            {
                // Underdamped spring: overshoots slightly past rest for the bounce.
                v += (-520f * x - 15f * v) * dt;
                x += v * dt;
                if (Mathf.Abs(x) < 0.002f && Mathf.Abs(v) < 0.05f) { x = 0f; v = 0f; Apply(0f); enabled = false; return; }
            }
            Apply(x);
        }

        void Apply(float k)
        {
            float s = 1f - squash * k;
            transform.localScale = new Vector3(s, s, 1f);
            if (face != null) face.anchoredPosition = faceHome - new Vector2(0f, sink * Mathf.Max(0f, k));
        }
    }

    /// <summary>
    /// Breathing scale for the one next action. Runs a few beats and stops so idle screens go still;
    /// call <see cref="Restart"/> when the screen shows again.
    /// </summary>
    public sealed class Pulse : MonoBehaviour
    {
        public float amount = 0.03f;
        public float period = 1.4f;
        public int beats = 6;
        float t;

        public void Restart() { t = 0f; enabled = true; }

        void OnDisable() { transform.localScale = Vector3.one; t = 0f; }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (beats > 0 && t >= beats * period) { enabled = false; return; }
            float s = 1f + amount * (0.5f - 0.5f * Mathf.Cos(t / period * 2f * Mathf.PI));
            transform.localScale = new Vector3(s, s, 1f);
        }
    }

    public sealed class Spin : MonoBehaviour
    {
        public float degreesPerSecond = 20f;
        void Update() => transform.Rotate(0f, 0f, degreesPerSecond * Time.unscaledDeltaTime);
    }

    /// <summary>Slow pan and zoom (Ken Burns) of a RawImage's uvRect around its cover-fit rect.</summary>
    public sealed class KenBurns : MonoBehaviour
    {
        public RawImage target;
        public Rect baseRect = new Rect(0, 0, 1, 1);
        public float period = 14f;
        float t;

        void Update()
        {
            if (target == null) return;
            t += Time.unscaledDeltaTime;
            float k = 0.5f - 0.5f * Mathf.Cos(t / period * 2f * Mathf.PI);
            float zoom = Mathf.Lerp(1f, 0.86f, k);
            var r = baseRect;
            float w = r.width * zoom, h = r.height * zoom;
            float px = Mathf.Lerp(0.2f, 0.8f, k), py = Mathf.Lerp(0.65f, 0.35f, k);
            target.uvRect = new Rect(r.x + (r.width - w) * px, r.y + (r.height - h) * py, w, h);
        }
    }

    public enum GradientDirection { Vertical, Horizontal, Diagonal }

    /// <summary>Multiplies vertex colours by a two-stop gradient across the graphic's rect.</summary>
    [AddComponentMenu("UI/Effects/Snaploop Gradient")]
    public sealed class UIGradient : BaseMeshEffect
    {
        public Color top = Color.white;
        public Color bottom = Color.white;
        public GradientDirection direction = GradientDirection.Vertical;

        public void Set(Color a, Color b, GradientDirection dir = GradientDirection.Vertical)
        {
            top = a; bottom = b; direction = dir;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var r = ((RectTransform)transform).rect;
            if (r.width <= 0f || r.height <= 0f) return;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float tx = (v.position.x - r.xMin) / r.width, ty = (v.position.y - r.yMin) / r.height;
                float k = direction == GradientDirection.Vertical ? ty
                    : direction == GradientDirection.Horizontal ? 1f - tx
                    : (ty + 1f - tx) * 0.5f;
                v.color = v.color * Color.Lerp(bottom, top, Mathf.Clamp01(k));
                vh.SetUIVertex(v, i);
            }
        }
    }

    /// <summary>
    /// Ink outline in eight directions (Unity's Outline uses four, which gaps at sticker widths),
    /// plus an optional drop copy below for the embossed look.
    /// </summary>
    [AddComponentMenu("UI/Effects/Snaploop Ink Outline")]
    public sealed class InkOutline : Shadow
    {
        public float width = 5f;
        public Color dropColor = new Color(0, 0, 0, 0);
        public float drop;

        static readonly List<UIVertex> Verts = new List<UIVertex>(256);
        static readonly Vector2[] Dirs =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(0.707f, 0.707f), new Vector2(-0.707f, 0.707f), new Vector2(0.707f, -0.707f), new Vector2(-0.707f, -0.707f),
        };

        public void Set(Color ink, float widthUnits, Color dropTint, float dropUnits)
        {
            effectColor = ink;
            width = widthUnits;
            dropColor = dropTint;
            drop = dropUnits;
            useGraphicAlpha = true;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            Verts.Clear();
            vh.GetUIVertexStream(Verts);
            int count = Verts.Count;
            // Each pass turns the newest copy into the effect and re-appends the original, so the drop drawn first sits furthest back.
            if (drop > 0f && dropColor.a > 0f)
                ApplyShadowZeroAlloc(Verts, dropColor, 0, count, 0f, -drop - width);
            if (width > 0f && effectColor.a > 0f)
            {
                for (int d = 0; d < Dirs.Length; d++)
                {
                    int end = Verts.Count;
                    ApplyShadowZeroAlloc(Verts, effectColor, end - count, end, Dirs[d].x * width, Dirs[d].y * width);
                }
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(Verts);
        }
    }
}
