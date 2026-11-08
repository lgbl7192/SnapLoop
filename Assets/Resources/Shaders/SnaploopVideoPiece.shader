// Samples the shared video RenderTexture. Vertex colour encodes the look:
// rgb = tint used where the video is not shown (edges, board), a = how much video shows (1 = pure video).
// Lives in Resources so it is always included in builds (it is looked up with Shader.Find).
Shader "Snaploop/VideoPiece"
{
    Properties
    {
        _MainTex ("Video", 2D) = "black" {}
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On
        ZTest LEqual
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed _Fade;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 video = tex2D(_MainTex, i.uv).rgb;
                fixed3 c = lerp(i.color.rgb, video, i.color.a) * _Fade;
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
