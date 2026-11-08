using System.Collections.Generic;
using UnityEngine;

namespace Snaploop.Capture
{
    /// <summary>
    /// A single line of text as a mesh of glyph quads from a dynamic font, so the offscreen clip
    /// renderer can draw text without a camera or canvas. Centred horizontally on x = 0, baseline at y = 0.
    /// The owner calls <see cref="Request"/> for every line, then <see cref="BuildMesh"/>, and rebuilds
    /// all meshes when <see cref="Font.textureRebuilt"/> fires (glyph UVs move when the atlas grows).
    /// </summary>
    sealed class ClipText
    {
        public readonly Mesh Mesh = new Mesh { name = "ClipText" };
        public float Width { get; private set; }
        public string Text { get; private set; } = "";
        public bool NeedsMesh { get; private set; } = true;

        readonly Font font;
        readonly int size;
        readonly FontStyle style;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color32> colors = new List<Color32>();
        readonly List<int> tris = new List<int>();

        public ClipText(Font font, int size, FontStyle style, string text = "")
        {
            this.font = font;
            this.size = size;
            this.style = style;
            Mesh.MarkDynamic();
            Set(text);
        }

        public void Set(string text)
        {
            text ??= "";
            if (text == Text) return;
            Text = text;
            NeedsMesh = true;
        }

        public void Request()
        {
            if (font != null && Text.Length > 0) font.RequestCharactersInTexture(Text, size, style);
        }

        public void BuildMesh()
        {
            NeedsMesh = false;
            verts.Clear(); uvs.Clear(); colors.Clear(); tris.Clear();
            Mesh.Clear();
            Width = 0f;
            if (font == null || Text.Length == 0) return;

            float x = 0f;
            foreach (char ch in Text)
            {
                if (!font.GetCharacterInfo(ch, out CharacterInfo ci, size, style)) continue;
                int s = verts.Count;
                verts.Add(new Vector3(x + ci.minX, ci.minY, 0f));
                verts.Add(new Vector3(x + ci.maxX, ci.minY, 0f));
                verts.Add(new Vector3(x + ci.maxX, ci.maxY, 0f));
                verts.Add(new Vector3(x + ci.minX, ci.maxY, 0f));
                uvs.Add(ci.uvBottomLeft);
                uvs.Add(ci.uvBottomRight);
                uvs.Add(ci.uvTopRight);
                uvs.Add(ci.uvTopLeft);
                for (int i = 0; i < 4; i++) colors.Add(new Color32(255, 255, 255, 255));
                tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
                tris.Add(s); tris.Add(s + 3); tris.Add(s + 2);
                x += ci.advance;
            }
            Width = x;
            var shift = new Vector3(x * 0.5f, 0f, 0f);
            for (int i = 0; i < verts.Count; i++) verts[i] -= shift;
            Mesh.SetVertices(verts);
            Mesh.SetUVs(0, uvs);
            Mesh.SetColors(colors);
            Mesh.SetTriangles(tris, 0, true);
        }

        public void Destroy() => Object.Destroy(Mesh);
    }
}
