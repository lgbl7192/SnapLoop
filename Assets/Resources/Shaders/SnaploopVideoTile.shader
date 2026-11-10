// Rounded-rectangle video tiles. Every tile is one quad over its full grid cell; the rounded corners,
// the gaps between tiles and the thin outline are computed here from per-vertex edge flags, so a whole
// group of merged tiles draws as one shape with a single outer outline and no inner borders.
//
// TEXCOORD0  video UV
// TEXCOORD1  xy = position inside the tile (world units, centre = 0), zw = tile half size
// TEXCOORD2  exposed edges (left, right, bottom, top): 1 = the neighbour on that side is not in this tile's group
// TEXCOORD3  concave notches (bottom-left, bottom-right, top-left, top-right): both side neighbours are in the
//            group but the diagonal one is not
// TEXCOORD4  x = outline flash (0-1): a thicker, brighter outline that marks a fresh merge
// COLOR      rgb = outline colour, a = how much video shows inside (1 for tiles, low for empty slots)
// A tile with no exposed edges and no notches draws as a plain quad (used for the reveal).
// Lives in Resources so it is always included in builds (it is looked up with Shader.Find).
Shader "Snaploop/VideoTile"
{
    Properties
    {
        _MainTex ("Video", 2D) = "black" {}
        _Fade ("Fade", Range(0, 1)) = 1
        _Radius ("Corner radius (world units)", Float) = 0.08
        _Gap ("Gap between tiles (world units)", Float) = 0.04
        _Outline ("Outline width (world units)", Float) = 0.015
        _Base ("Colour under the video", Color) = (0.063, 0.071, 0.098, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed _Fade;
            float _Radius;
            float _Gap;
            float _Outline;
            fixed4 _Base;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 local : TEXCOORD1;
                float4 edges : TEXCOORD2;
                float4 notch : TEXCOORD3;
                float2 flash : TEXCOORD4;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 local : TEXCOORD1;
                float4 edges : TEXCOORD2;
                float4 notch : TEXCOORD3;
                float flash : TEXCOORD4;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.local = v.local;
                o.edges = v.edges;
                o.notch = v.notch;
                o.flash = saturate(v.flash.x);
                o.color = v.color;
                return o;
            }

            #define BIG 10000.0

            // Distance from p to the group outline, positive inside.
            float ShapeDistance(float2 p, float2 h, float4 e, float4 n)
            {
                float g = _Gap * 0.5;
                float r = _Radius;
                float xmin = -h.x + e.x * g, xmax = h.x - e.y * g;
                float ymin = -h.y + e.z * g, ymax = h.y - e.w * g;

                float d = BIG;
                if (e.x > 0.5) d = min(d, p.x - xmin);
                if (e.y > 0.5) d = min(d, xmax - p.x);
                if (e.z > 0.5) d = min(d, p.y - ymin);
                if (e.w > 0.5) d = min(d, ymax - p.y);

                // Outer corners are rounded only where both sides are exposed.
                float2 c;
                if (e.x > 0.5 && e.z > 0.5) { c = float2(xmin + r, ymin + r); if (p.x < c.x && p.y < c.y) d = min(d, r - length(p - c)); }
                if (e.y > 0.5 && e.z > 0.5) { c = float2(xmax - r, ymin + r); if (p.x > c.x && p.y < c.y) d = min(d, r - length(p - c)); }
                if (e.x > 0.5 && e.w > 0.5) { c = float2(xmin + r, ymax - r); if (p.x < c.x && p.y > c.y) d = min(d, r - length(p - c)); }
                if (e.y > 0.5 && e.w > 0.5) { c = float2(xmax - r, ymax - r); if (p.x > c.x && p.y > c.y) d = min(d, r - length(p - c)); }

                // Inner (concave) corners of a group: cut a small square so the neighbours' outlines meet.
                if (n.x > 0.5) d = min(d, max(p.x - (-h.x + g), p.y - (-h.y + g)));
                if (n.y > 0.5) d = min(d, max((h.x - g) - p.x, p.y - (-h.y + g)));
                if (n.z > 0.5) d = min(d, max(p.x - (-h.x + g), (h.y - g) - p.y));
                if (n.w > 0.5) d = min(d, max((h.x - g) - p.x, (h.y - g) - p.y));
                return d;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float d = ShapeDistance(i.local.xy, i.local.zw, i.edges, i.notch);
                float aa = max(fwidth(d), 1e-5);
                float alpha = saturate(d / aa + 0.5);
                clip(alpha - 0.004);
                float width = _Outline * (1.0 + 1.5 * i.flash);
                float outline = 1.0 - saturate((d - width) / aa + 0.5);

                fixed3 video = tex2D(_MainTex, i.uv).rgb;
                fixed3 inside = lerp(_Base.rgb, video, i.color.a);
                // Soft glow just inside the outline while flashing; d is huge away from exposed edges.
                inside = lerp(inside, fixed3(1, 1, 1), 0.35 * i.flash * saturate(1.0 - d / (width * 5.0)));
                fixed3 c = lerp(inside, i.color.rgb, outline) * _Fade;
                return fixed4(c, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
