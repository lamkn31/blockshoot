// Stamps stencil ref = _StencilRef wherever the car's silhouette is visible, but draws NOTHING
// to the color buffer (ColorMask 0). Add this as an EXTRA material slot on every visible car
// renderer (MeshRenderer / SkinnedMeshRenderer / SpriteRenderer) you want to "pop out" over the
// tutorial dim.
//
// Pairs with BusSort/TutorialDimOverlay: that fullscreen UI panel paints black everywhere the
// stencil != _StencilRef, so the car (and only the car) stays clear and floats above the dim.
//
// Render order: Queue Geometry+50 (2050) — after the car's normal opaque materials (2000) so the
// car's depth is already laid down; ZTest Always + ZWrite Off means we mark the car's WHOLE
// silhouette regardless of what is in front of it, so the car "pops out" ABOVE every other object
// (never occluded by them), without disturbing depth.
// Works for SkinnedMeshRenderer too: Unity feeds already-skinned positions in object space.
//
// Alpha clip: SpriteRenderers (vd số countdown của xe Timer) có góc TRONG SUỐT trong texture. Nếu
// chỉ ghi stencil theo quad thì cả phần trong suốt cũng "nổi" → hiện nguyên khối chữ nhật cỡ texture.
// Sample alpha của sprite (_MainTex do SpriteRenderer tự bind PerRendererData) rồi clip theo _Cutoff
// để CHỈ phần thấy được ghi stencil. Mesh xe không có sprite → _MainTex mặc định "white" (alpha 1)
// → luôn qua clip → giữ nguyên hành vi cũ.

Shader "BusSort/TutorialStencilMask"
{
    Properties
    {
        [IntRange] _StencilRef ("Stencil Ref", Range(0, 255)) = 200
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+50" "RenderType"="Opaque" }

        Pass
        {
            Name "CarStencilWrite"
            ColorMask 0
            ZWrite Off
            ZTest Always
            Cull Back

            Stencil
            {
                Ref [_StencilRef]
                Comp Always
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Bỏ qua pixel trong suốt (mesh xe → _MainTex "white" nên alpha=1, luôn qua).
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
