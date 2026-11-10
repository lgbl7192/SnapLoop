using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>In-app splash shown while levels.json loads (the Unity splash is disabled): the loop mark snaps shut, then the wordmark pops.</summary>
    public sealed class SplashScreen : UIScreen
    {
        static UITheme T => UITheme.Current;

        readonly CanvasGroup group;
        readonly Brand.LoopMark mark;
        readonly RectTransform word, tag;
        float fadeT = -1f;
        float introT = -1f;

        public SplashScreen(Transform canvasRoot)
        {
            var bg = UIFactory.Image("Splash", canvasRoot, Color.white, null, raycast: true);
            Root = UIFactory.Stretch(bg.rectTransform);
            UIFactory.Gradient(bg, T.bgTop, T.bgBottom);
            group = bg.gameObject.AddComponent<CanvasGroup>();

            var glow = UIFactory.Image("Glow", Root, T.bgGlow, UISprites.SoftCircle);
            UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(T.U(420f), T.U(420f)), new Vector2(0f, T.U(40f)));

            mark = Brand.Mark(Root, 96f);
            UIFactory.Place(mark.Root, new Vector2(0.5f, 0.5f), mark.Root.sizeDelta, new Vector2(0f, T.U(70f)));
            var w = Brand.Wordmark(Root, 62f);
            word = w.rectTransform;
            UIFactory.Place(word, new Vector2(0.5f, 0.5f), new Vector2(T.U(340f), T.U(80f)), new Vector2(0f, -T.U(30f)));
            var t = UIFactory.Text("Tagline", Root, "Assemble the moving picture", TextRole.Caps, T.caps + 1f, T.textDim);
            tag = t.rectTransform;
            UIFactory.Place(tag, new Vector2(0.5f, 0.5f), new Vector2(T.U(360f), T.U(24f)), new Vector2(0f, -T.U(82f)));
        }

        public override void Show()
        {
            base.Show();
            group.alpha = 1f;
            group.blocksRaycasts = true;
            introT = 0f;
            mark.SetSnap(0f);
            UITween.Get(mark.Root).Scale(0.5f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack);
            UITween.Get(word).Scale(0.6f, 1f).Fade(0f, 1f).Play(T.medium, EaseType.OutBack, 0.32f);
            UITween.Get(tag).Fade(0f, 1f).Move(new Vector2(0f, -T.U(10f)), Vector2.zero).Play(T.medium, EaseType.OutCubic, 0.45f);
        }

        public void FadeOut() => fadeT = 0f;

        /// <summary>Call every frame from the app.</summary>
        public void Tick(float dt)
        {
            if (!IsVisible) return;
            if (introT >= 0f)
            {
                introT += dt;
                float k = Mathf.Clamp01((introT - 0.18f) / 0.32f);
                mark.SetSnap(Ease.Apply(EaseType.OutBackStrong, k));
                if (k >= 1f) introT = -1f;
            }
            if (fadeT < 0f) return;
            fadeT += dt;
            group.alpha = 1f - Mathf.Clamp01(fadeT / 0.35f);
            group.blocksRaycasts = false;
            if (fadeT >= 0.35f) { Hide(); fadeT = -1f; }
        }
    }
}
