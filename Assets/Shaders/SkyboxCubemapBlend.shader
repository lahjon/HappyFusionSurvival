Shader "Skybox/CubemapBlend"
{
    Properties
    {
        _Tint     ("Tint Color", Color) = (.5, .5, .5, .5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _Tex  ("Skybox Day (HDR)",   CUBE) = "grey" {}
        [NoScaleOffset] _Tex2 ("Skybox Night (HDR)", CUBE) = "grey" {}
        _Blend ("Blend (0=Day, 1=Night)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEXCUBE(_Tex);
            half4 _Tex_HDR;
            UNITY_DECLARE_TEXCUBE(_Tex2);
            half4 _Tex2_HDR;
            half4 _Tint;
            half  _Exposure;
            float _Rotation;
            half  _Blend;

            float3 RotateAroundY(float3 v, float degrees)
            {
                float rad = degrees * UNITY_PI / 180.0;
                float s, c;
                sincos(rad, s, c);
                return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
            }

            struct appdata_t { float4 vertex : POSITION; };
            struct v2f     { float4 pos : SV_POSITION; float3 tc : TEXCOORD0; };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.tc  = RotateAroundY(v.vertex.xyz, _Rotation);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 s1  = UNITY_SAMPLE_TEXCUBE(_Tex,  i.tc);
                half4 s2  = UNITY_SAMPLE_TEXCUBE(_Tex2, i.tc);
                half3 c1  = DecodeHDR(s1, _Tex_HDR);
                half3 c2  = DecodeHDR(s2, _Tex2_HDR);
                half3 col = lerp(c1, c2, _Blend) * _Tint.rgb * unity_ColorSpaceDouble.rgb * _Exposure;
                return half4(col, 1);
            }
            ENDCG
        }
    }
}
