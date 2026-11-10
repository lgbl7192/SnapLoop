using UnityEngine;
using UnityEngine.Rendering;

namespace Snaploop.UI
{
    /// <summary>
    /// World-space backdrop behind the board and every screen: the deep indigo gradient, a violet glow near the top
    /// and a faint jigsaw lattice that fades toward the bottom. Drawn by the board camera, so the video and the
    /// UI both sit on it. Rebuilt only when the screen size changes.
    /// </summary>
    public sealed class Backdrop : MonoBehaviour
    {
        const float Z = 20f;

        Camera cam;
        Mesh mesh;
        Transform glow, lattice;
        SpriteRenderer latticeRenderer;
        int builtW, builtH;

        // Board renderers share the transparent queue and sort by order first, so the backdrop pins itself behind them.
        const int SortingOrder = -100;

        public static Backdrop Create(Camera camera)
        {
            var go = new GameObject("Backdrop");
            var b = go.AddComponent<Backdrop>();
            b.Init(camera);
            return b;
        }

        void Init(Camera camera)
        {
            cam = camera;
            var t = UITheme.Current;
            var shader = Shader.Find("Sprites/Default");

            var grad = new GameObject("Gradient");
            grad.transform.SetParent(transform, false);
            mesh = new Mesh { name = "BackdropGradient" };
            grad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = grad.AddComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(shader) { name = "BackdropGradient" };
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = SortingOrder;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(transform, false);
            var gsr = glowGo.AddComponent<SpriteRenderer>();
            gsr.sprite = UISprites.SoftCircle;
            gsr.color = t.bgGlow;
            gsr.sortingOrder = SortingOrder + 1;
            glow = glowGo.transform;

            var latGo = new GameObject("Lattice");
            latGo.transform.SetParent(transform, false);
            latticeRenderer = latGo.AddComponent<SpriteRenderer>();
            var tex = UISprites.Lattice;
            latticeRenderer.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            latticeRenderer.color = new Color(1f, 1f, 1f, t.latticeOpacity);
            latticeRenderer.sortingOrder = SortingOrder + 2;
            lattice = latGo.transform;

            Rebuild();
        }

        void LateUpdate()
        {
            if (Screen.width != builtW || Screen.height != builtH) Rebuild();
        }

        void Rebuild()
        {
            builtW = Screen.width;
            builtH = Screen.height;
            var t = UITheme.Current;
            float h = cam.orthographicSize * 2f;
            float w = h * cam.aspect;
            Vector3 c = cam.transform.position;
            transform.position = new Vector3(c.x, c.y, Z);

            float x0 = -w * 0.5f, x1 = w * 0.5f, y0 = -h * 0.5f, y1 = h * 0.5f, ym = y0 + h * 0.55f;
            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(x0, y1), new Vector3(x1, y1),
                new Vector3(x0, ym), new Vector3(x1, ym),
                new Vector3(x0, y0), new Vector3(x1, y0),
            };
            mesh.colors = new[] { t.bgTop, t.bgTop, t.bgMid, t.bgMid, t.bgBottom, t.bgBottom };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3, 2, 3, 4, 4, 3, 5 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(w, h, 1f));

            float g = w * 1.5f / (UISprites.SoftCircle.rect.width / 100f);
            glow.localPosition = new Vector3(0f, y1 - h * 0.12f, -0.1f);
            glow.localScale = new Vector3(g, g * 0.75f, 1f);

            var sz = latticeRenderer.sprite.bounds.size;
            float s = Mathf.Max(w / sz.x, h / sz.y);
            lattice.localPosition = new Vector3(0f, 0f, -0.2f);
            lattice.localScale = new Vector3(s, s, 1f);
        }
    }
}
