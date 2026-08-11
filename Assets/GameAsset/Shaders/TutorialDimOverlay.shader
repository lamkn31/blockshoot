// Fullscreen black "dim" panel for the tutorial. Put this material on a UI Image that stretches
// over the whole screen, inside a Screen Space - Camera canvas (so it shares the gameplay camera's
// stencil buffer with the 3D scene).
//
// It paints _Color everywhere the stencil != _StencilRef. The car renders its silhouette into the
// stencil via BusSort/TutorialStencilMask (Stencil Ref = same value), so the dim is rejected over
// the car and the car "pops out" above the darkened scene.
//
// Alpha is multiplied by the Image's vertex color, so the Image color's alpha and a CanvasGroup
// alpha both work for fading the dim in/out.

Shader "BusSort/TutorialDimOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Dim Color", Color) = (0, 0, 0, 0.8)
        [IntRange] _StencilRef ("Car Stencil Ref", Range(0, 255)) = 200
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_StencilRef]
            Comp NotEqual   // keep the car's silhouette clear; dim everything else
            Pass Keep
        }

        Pass
        {
            Name "TutorialDim"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float  _StencilRef;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return tex * _Color * IN.color;
            }
            ENDHLSL
        }
    }
}
