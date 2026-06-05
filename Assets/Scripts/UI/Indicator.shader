// Unlit, billboard-friendly marker shader for ally/enemy world indicators.
// Loaded at runtime by WorldMarkerManager via Shader.Find("HappyFusion/Indicator"); listed in
// Project Settings > Graphics > Always Included Shaders so it survives build stripping (no material
// references it in a scene). The _ZTest property lets the same shader render either
// through walls (ZTest Always, ally) or depth-tested (ZTest LEqual, enemy) by picking
// the compare function per-material. Per-marker colour/alpha comes from a
// MaterialPropertyBlock overriding _Color (ZTest/renderQueue stay per-material).
Shader "HappyFusion/Indicator"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4 // LEqual
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            ZTest [_ZTest]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
