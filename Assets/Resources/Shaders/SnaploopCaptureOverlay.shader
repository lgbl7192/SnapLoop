// Alpha-blended overlays for the offscreen clip renderer (text, gradients, flash, dimming).
// _TextureSampleAdd is (1,1,1,0) for Alpha8 font atlases, whose rgb samples as 0, like uGUI does.
// Lives in Resources so it is always included in builds (it is looked up with Shader.Find).
Shader "Snaploop/CaptureOverlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _TextureSampleAdd ("Sample Add", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;

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
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
            }
            ENDCG
        }
    }
    Fallback Off
}
