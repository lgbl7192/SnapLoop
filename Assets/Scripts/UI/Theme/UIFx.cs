using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>
    /// Pooled celebration particles on their own overlay canvas, above the game UI and below monetization overlays:
    /// snap bursts and rings at a screen point, and win confetti. Everything is preallocated, so playing effects allocates nothing.
    /// </summary>
    public sealed class UIFx : MonoBehaviour
    {
        public const int SortingOrder = 20;
        const int MaxParticles = 160;
        const int MaxRings = 6;
        const float Gravity = -2600f;

        public static UIFx Instance { get; private set; }

        RectTransform root;
        readonly RectTransform[] rts = new RectTransform[MaxParticles];
        readonly Image[] imgs = new Image[MaxParticles];
        readonly Vector2[] pos = new Vector2[MaxParticles];
        readonly Vector2[] vel = new Vector2[MaxParticles];
        readonly Vector2[] size = new Vector2[MaxParticles];
        readonly float[] rot = new float[MaxParticles];
        readonly float[] spin = new float[MaxParticles];
        readonly float[] age = new float[MaxParticles];
        readonly float[] life = new float[MaxParticles];
        readonly float[] drag = new float[MaxParticles];
        readonly float[] gravity = new float[MaxParticles];
        readonly bool[] flip = new bool[MaxParticles];
        readonly Color[] tint = new Color[MaxParticles];

        readonly RectTransform[] ringRts = new RectTransform[MaxRings];
        readonly Image[] ringImgs = new Image[MaxRings];
        readonly float[] ringAge = new float[MaxRings];
        readonly float[] ringLife = new float[MaxRings];
        readonly float[] ringSize = new float[MaxRings];

        const int MaxTokens = 14;
        const float TokenFlightSec = 0.62f;
        const float TokenStaggerSec = 0.055f;
        readonly RectTransform[] tokenRts = new RectTransform[MaxTokens];
        readonly Vector2[] tokenFrom = new Vector2[MaxTokens];
        readonly Vector2[] tokenCtrl = new Vector2[MaxTokens];
        readonly float[] tokenAge = new float[MaxTokens];
        readonly bool[] tokenLive = new bool[MaxTokens];
        Vector2 tokenTo;
        int tokensInFlight;
        System.Action onTokenArrived, onTokensDone;

        int next, nextRing, live;

        public static UIFx Create()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Fx");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0f;
            var fx = go.AddComponent<UIFx>();
            fx.Build((RectTransform)go.transform);
            Instance = fx;
            return fx;
        }

        void Build(RectTransform canvasRoot)
        {
            root = canvasRoot;
            for (int i = 0; i < MaxParticles; i++)
            {
                var img = UIFactory.Image("P", root, Color.white);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                img.gameObject.SetActive(false);
                imgs[i] = img;
                rts[i] = img.rectTransform;
            }
            for (int i = 0; i < MaxRings; i++)
            {
                var img = UIFactory.Image("Ring", root, Color.white, UISprites.Ring);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                img.gameObject.SetActive(false);
                ringImgs[i] = img;
                ringRts[i] = img.rectTransform;
            }
            float tokenSize = UITheme.Current.U(30f);
            for (int i = 0; i < MaxTokens; i++)
            {
                var tok = UIFactory.TokenIcon("Token", root, tokenSize);
                tok.anchorMin = tok.anchorMax = new Vector2(0.5f, 0.5f);
                tok.gameObject.SetActive(false);
                tokenRts[i] = tok;
            }
            enabled = false;
        }

        /// <summary>
        /// Tokens spray out of <paramref name="fromScreen"/> and stream into <paramref name="toScreen"/> (screen pixels).
        /// <paramref name="onEachArrive"/> runs as each one lands (tick a counter), <paramref name="onDone"/> after the last.
        /// A new flight lands any flight still in the air first.
        /// </summary>
        public void FlyTokens(Vector2 fromScreen, Vector2 toScreen, int count, System.Action onEachArrive, System.Action onDone)
        {
            FinishTokens();
            var t = UITheme.Current;
            Vector2 from = ToLocal(fromScreen);
            tokenTo = ToLocal(toScreen);
            count = Mathf.Clamp(count, 1, MaxTokens);
            onTokenArrived = onEachArrive;
            onTokensDone = onDone;
            tokensInFlight = count;
            for (int i = 0; i < count; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                tokenFrom[i] = from + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * t.U(Random.Range(4f, 18f));
                tokenCtrl[i] = from + new Vector2(Random.Range(-1f, 1f) * t.U(110f), t.U(Random.Range(70f, 150f)));
                tokenAge[i] = -i * TokenStaggerSec;
                tokenLive[i] = true;
                tokenRts[i].anchoredPosition = tokenFrom[i];
                tokenRts[i].localScale = Vector3.zero;
                tokenRts[i].SetAsLastSibling();
                tokenRts[i].gameObject.SetActive(true);
            }
            enabled = true;
        }

        /// <summary>Lands every token in flight at once (their callbacks still run).</summary>
        public void FinishTokens()
        {
            if (tokensInFlight <= 0) return;
            for (int i = 0; i < MaxTokens; i++)
            {
                if (!tokenLive[i]) continue;
                tokenLive[i] = false;
                tokenRts[i].gameObject.SetActive(false);
                onTokenArrived?.Invoke();
            }
            EndTokenFlight();
        }

        void EndTokenFlight()
        {
            tokensInFlight = 0;
            var done = onTokensDone;
            onTokenArrived = onTokensDone = null;
            done?.Invoke();
        }

        bool TickTokens(float dt)
        {
            if (tokensInFlight <= 0) return false;
            for (int i = 0; i < MaxTokens; i++)
            {
                if (!tokenLive[i]) continue;
                tokenAge[i] += dt;
                float f = tokenAge[i] / TokenFlightSec;
                if (f < 0f) continue;
                if (f >= 1f)
                {
                    tokenLive[i] = false;
                    tokenRts[i].gameObject.SetActive(false);
                    tokensInFlight--;
                    onTokenArrived?.Invoke();
                    if (tokensInFlight <= 0) { EndTokenFlight(); return false; }
                    continue;
                }
                float e = Ease.Apply(EaseType.InOutCubic, f);
                float u = 1f - e;
                tokenRts[i].anchoredPosition = u * u * tokenFrom[i] + 2f * u * e * tokenCtrl[i] + e * e * tokenTo;
                float s = f < 0.18f ? Ease.Apply(EaseType.OutBack, f / 0.18f) : Mathf.Lerp(1f, 0.6f, (f - 0.18f) / 0.82f);
                tokenRts[i].localScale = new Vector3(s, s, 1f);
            }
            return true;
        }

        Vector2 ToLocal(Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPos, null, out var local);
            return local;
        }

        /// <summary>Snap pop: a ring plus a small spray of sparks and stars at a screen point.</summary>
        public void Burst(Vector2 screenPos, Color color, int count = 12)
        {
            var t = UITheme.Current;
            Vector2 p = ToLocal(screenPos);
            Ring(p, color, t.U(110f), 0.45f);
            for (int i = 0; i < count; i++)
            {
                int k = Take();
                float ang = (i + Random.value * 0.6f) / count * Mathf.PI * 2f;
                float speed = t.U(Random.Range(140f, 260f));
                bool isStar = i % 3 == 0;
                float s = t.U(isStar ? Random.Range(9f, 13f) : Random.Range(5f, 8f));
                Spawn(k, p, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * speed, new Vector2(s, s),
                    isStar ? UISprites.Star : UISprites.Circle, i % 2 == 0 ? color : t.star,
                    Random.Range(0.35f, 0.55f), 5f, Gravity * 0.25f, false);
            }
        }

        /// <summary>70 ballistic confetti rectangles fanned up from 28% of the screen height, fading over 2.6 s.</summary>
        public void Confetti(int count = 70)
        {
            var t = UITheme.Current;
            var r = root.rect;
            Vector2 origin = new Vector2(0f, r.yMin + r.height * 0.28f);
            for (int i = 0; i < count; i++)
            {
                int k = Take();
                float ang = (90f + Random.Range(-100f, 100f)) * Mathf.Deg2Rad;
                float speed = Random.Range(0.55f, 1.05f) * r.height * 1.05f;
                var c = ConfettiColor(t, i);
                Spawn(k, origin + new Vector2(Random.Range(-0.12f, 0.12f) * r.width, 0f),
                    new Vector2(Mathf.Cos(ang) * 0.75f, Mathf.Sin(ang)) * speed,
                    new Vector2(t.U(Random.Range(5f, 11f)), t.U(Random.Range(3f, 7f))),
                    null, c, 2.6f * Random.Range(0.88f, 1f), 1.1f, Gravity * 0.62f, true);
                age[k] = -Random.Range(0f, 0.12f) * 2.6f;
                imgs[k].gameObject.SetActive(false);
            }
        }

        static Color ConfettiColor(UITheme t, int i)
        {
            switch (i % 6)
            {
                case 0: return t.gold;
                case 1: return t.cyan;
                case 2: return t.pink;
                case 3: return t.green;
                case 4: return t.purple;
                default: return t.orange;
            }
        }

        void Ring(Vector2 p, Color color, float maxSize, float seconds)
        {
            int k = nextRing;
            nextRing = (nextRing + 1) % MaxRings;
            ringRts[k].anchoredPosition = p;
            ringImgs[k].color = color;
            ringAge[k] = 0f;
            ringLife[k] = seconds;
            ringSize[k] = maxSize;
            ringRts[k].sizeDelta = Vector2.zero;
            ringImgs[k].gameObject.SetActive(true);
            enabled = true;
        }

        int Take()
        {
            int k = next;
            next = (next + 1) % MaxParticles;
            return k;
        }

        void Spawn(int k, Vector2 p, Vector2 v, Vector2 s, Sprite sprite, Color c, float seconds, float dragPerSec, float g, bool flips)
        {
            if (life[k] <= 0f) live++;
            pos[k] = p; vel[k] = v; size[k] = s;
            rot[k] = Random.Range(0f, 360f);
            spin[k] = Random.Range(-540f, 540f);
            age[k] = 0f; life[k] = seconds;
            drag[k] = dragPerSec; gravity[k] = g; flip[k] = flips;
            tint[k] = c;
            imgs[k].sprite = sprite;
            imgs[k].type = Image.Type.Simple;
            imgs[k].color = c;
            rts[k].sizeDelta = s;
            rts[k].localScale = Vector3.one;
            rts[k].anchoredPosition = p;
            imgs[k].gameObject.SetActive(true);
            enabled = true;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            bool any = false;

            for (int k = 0; k < MaxParticles; k++)
            {
                if (life[k] <= 0f) continue;
                any = true;
                age[k] += dt;
                if (age[k] < 0f) continue;
                if (!imgs[k].gameObject.activeSelf) imgs[k].gameObject.SetActive(true);
                if (age[k] >= life[k])
                {
                    life[k] = 0f;
                    live--;
                    imgs[k].gameObject.SetActive(false);
                    continue;
                }
                vel[k].y += gravity[k] * dt;
                vel[k] *= 1f - Mathf.Min(1f, drag[k] * dt);
                pos[k] += vel[k] * dt;
                rot[k] += spin[k] * dt;
                rts[k].anchoredPosition = pos[k];
                rts[k].localEulerAngles = new Vector3(0f, 0f, rot[k]);
                float f = age[k] / life[k];
                if (flip[k])
                {
                    rts[k].sizeDelta = new Vector2(size[k].x, size[k].y * Mathf.Abs(Mathf.Cos(age[k] * 9f + k)));
                    var c = tint[k]; c.a = 1f - f; imgs[k].color = c;
                }
                else
                {
                    float s = f < 0.2f ? Ease.Apply(EaseType.OutBack, f / 0.2f) : 1f - (f - 0.2f) / 0.8f;
                    rts[k].localScale = new Vector3(s, s, 1f);
                }
            }

            for (int k = 0; k < MaxRings; k++)
            {
                if (ringLife[k] <= 0f) continue;
                any = true;
                ringAge[k] += dt;
                float f = ringAge[k] / ringLife[k];
                if (f >= 1f)
                {
                    ringLife[k] = 0f;
                    ringImgs[k].gameObject.SetActive(false);
                    continue;
                }
                float s = ringSize[k] * Ease.Apply(EaseType.OutCubic, f);
                ringRts[k].sizeDelta = new Vector2(s, s);
                var c = ringImgs[k].color; c.a = 1f - f; ringImgs[k].color = c;
            }

            if (TickTokens(dt)) any = true;
            if (!any) enabled = false;
        }

        /// <summary>Stops everything (for example when leaving the win card early).</summary>
        public void Clear()
        {
            for (int k = 0; k < MaxParticles; k++) { life[k] = 0f; imgs[k].gameObject.SetActive(false); }
            for (int k = 0; k < MaxRings; k++) { ringLife[k] = 0f; ringImgs[k].gameObject.SetActive(false); }
            for (int k = 0; k < MaxTokens; k++) { tokenLive[k] = false; tokenRts[k].gameObject.SetActive(false); }
            tokensInFlight = 0;
            onTokenArrived = onTokensDone = null;
            live = 0;
            enabled = false;
        }
    }
}
