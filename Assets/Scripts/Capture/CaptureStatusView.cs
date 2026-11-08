using Snaploop.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.Capture
{
    /// <summary>
    /// Small non-interactive pill near the top of the screen ("Making clip 42%", "Saved to Movies/Snaploop").
    /// Lives on its own overlay canvas so the core UI is untouched; never blocks touches.
    /// </summary>
    sealed class CaptureStatusView
    {
        readonly GameObject canvasGo;
        readonly CanvasGroup group;
        readonly Image pill;
        readonly Text label;
        float remaining;
        bool sticky;

        public CaptureStatusView(Transform parent)
        {
            canvasGo = new GameObject("ClipStatusCanvas", typeof(RectTransform));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UILayout.ReferenceWidth, UILayout.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            var safe = UIFactory.Rect("SafeArea", canvasGo.transform);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            group = safe.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            pill = UIFactory.Panel("Pill", safe, new Color(0f, 0f, 0f, 0.72f), raycast: false);
            UIFactory.Place(pill.rectTransform, new Vector2(0.5f, 1f), new Vector2(700, 96), new Vector2(0, -40));
            label = UIFactory.Label("Text", pill.transform, "", 36, Palette.Text);
            UIFactory.Stretch(label.rectTransform, 24, 24, 4, 4);
            canvasGo.SetActive(false);
        }

        /// <summary>Shows a message. Sticky messages stay until replaced or hidden; others fade after <paramref name="seconds"/>.</summary>
        public void Show(string message, float seconds = 2f, bool sticky = false, bool subtle = false)
        {
            if (label.text != message) label.text = message;
            this.sticky = sticky;
            remaining = seconds;
            pill.color = new Color(0f, 0f, 0f, subtle ? 0.5f : 0.75f);
            label.color = subtle ? Palette.TextDim : Palette.Text;
            group.alpha = 1f;
            if (!canvasGo.activeSelf) canvasGo.SetActive(true);
        }

        public void Hide()
        {
            sticky = false;
            remaining = 0f;
            canvasGo.SetActive(false);
        }

        public void Tick(float dt)
        {
            if (!canvasGo.activeSelf || sticky) return;
            remaining -= dt;
            group.alpha = Mathf.Clamp01(remaining / 0.3f);
            if (remaining <= 0f) canvasGo.SetActive(false);
        }
    }
}
