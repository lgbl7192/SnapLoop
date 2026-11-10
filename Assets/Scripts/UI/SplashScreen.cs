using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>In-app "Snaploop" splash shown while levels.json loads (the Unity splash is disabled).</summary>
    public sealed class SplashScreen : UIScreen
    {
        readonly CanvasGroup group;
        float fadeT = -1f;

        public SplashScreen(Transform canvasRoot)
        {
            var bg = UIFactory.Image("Splash", canvasRoot, Palette.Background, null, raycast: true);
            Root = UIFactory.Stretch(bg.rectTransform);
            group = bg.gameObject.AddComponent<CanvasGroup>();

            var word = UIFactory.Label("Wordmark", Root, "Snaploop", 150, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(word.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1000, 200), new Vector2(0, 40));
            var dot = UIFactory.Image("Dot", Root, Palette.Accent, UIFactory.Circle);
            UIFactory.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(36, 36), new Vector2(0, -80));
            var tag = UIFactory.Label("Tagline", Root, "moving picture puzzles", 44, Palette.TextDim);
            UIFactory.Place(tag.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1000, 80), new Vector2(0, -150));
        }

        public void FadeOut() => fadeT = 0f;

        /// <summary>Call every frame from the app.</summary>
        public void Tick(float dt)
        {
            if (fadeT < 0f || !IsVisible) return;
            fadeT += dt;
            group.alpha = 1f - Mathf.Clamp01(fadeT / 0.35f);
            group.blocksRaycasts = false;
            if (fadeT >= 0.35f) { Hide(); fadeT = -1f; }
        }
    }
}
