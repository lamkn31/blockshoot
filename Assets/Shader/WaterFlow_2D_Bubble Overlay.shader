Shader "WaterFlow/2D/Bubble Overlay"
{
    Properties { _BubbleMap ("Bubble Texture", 2D) = "black" {} _Color ("Color", Color) = (0.45,0.92,1,0.9) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_BubbleMap); SAMPLER(sampler_BubbleMap);
            CBUFFER_START(UnityPerMaterial) float4 _BubbleMap_ST; half4 _Color; CBUFFER_END
            Varyings vert(Attributes input) { Varyings o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz); o.uv = input.uv; return o; }
            half4 frag(Varyings input) : SV_Target { half a = SAMPLE_TEXTURE2D(_BubbleMap, sampler_BubbleMap, input.uv).r; return half4(_Color.rgb, _Color.a * a); }
            ENDHLSL
        }
    }
}
