using UnityEngine;

namespace Snaploop.UI
{
    /// <summary>Shared layout numbers, in canvas units (reference width 1080, scaled to screen width).</summary>
    public static class UILayout
    {
        public const float ReferenceWidth = 1080f;
        public const float ReferenceHeight = 1920f;
        public const float TopBar = 170f;
        public const float BottomBar = 220f;

        /// <summary>Canvas-unit to pixel factor (the CanvasScaler matches width).</summary>
        public static float Scale => Screen.width / ReferenceWidth;

        /// <summary>Screen-pixel rect between the HUD bars inside the safe area.</summary>
        public static Rect PuzzlePlayArea()
        {
            Rect safe = Screen.safeArea;
            float s = Scale;
            float top = TopBar * s, bottom = BottomBar * s;
            return new Rect(safe.x, safe.y + bottom, safe.width, Mathf.Max(1f, safe.height - top - bottom));
        }
    }

    /// <summary>Base for code-built screens: a root RectTransform that is shown or hidden.</summary>
    public abstract class UIScreen
    {
        public RectTransform Root { get; protected set; }

        public bool IsVisible => Root != null && Root.gameObject.activeSelf;

        public virtual void Show() => Root.gameObject.SetActive(true);

        public virtual void Hide() => Root.gameObject.SetActive(false);
    }

    /// <summary>Keeps a RectTransform matched to Screen.safeArea (notches, gesture bars).</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        Rect applied;
        Vector2Int appliedSize;
        RectTransform rt;

        void Awake()
        {
            rt = (RectTransform)transform;
            Apply();
        }

        void Update()
        {
            if (Screen.safeArea != applied || Screen.width != appliedSize.x || Screen.height != appliedSize.y) Apply();
        }

        void Apply()
        {
            applied = Screen.safeArea;
            appliedSize = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            rt.anchorMin = new Vector2(applied.xMin / Screen.width, applied.yMin / Screen.height);
            rt.anchorMax = new Vector2(applied.xMax / Screen.width, applied.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
