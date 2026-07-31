Shader "WaterFlow/2D/Water Flowd"
{
    Properties
    {
        _MainTex ("Flow Texture", 2D) = "white" {}
        _Bubble ("Bubble Texture", 2D) = "black" {}
        _ColorOut ("Outer Color", Color) = (0.13,0.60,0.84,1)
        _ColorIn ("Inner Color", Color) = (0,0.43,0.73,1)
        _ColorHighlight ("Foam Highlight", Color) = (0.14,0.74,0.74,1)
        _FlowSpeed ("Flow Speed (Along, Across)", Vector) = (-0.42,0,0,0)
        _SecondFlowSpeed ("Second Layer Speed", Vector) = (-0.23,0,0,0)
        _BubbleSpeed ("Bubble Speed", Vector) = (0.16,0,0,0)
        _FlowStrength ("Flow Strength", Range(0,2)) = 1
        _FoamThreshold ("Foam Threshold", Range(0,1)) = 0.58
        _Opacity ("Opacity", Range(0,1)) = 0.85
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-2"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "WaterFlowUnlit"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_Bubble);
            SAMPLER(sampler_Bubble);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Bubble_ST;
                half4 _ColorOut;
                half4 _ColorIn;
                half4 _ColorHighlight;
                float4 _FlowSpeed;
                float4 _SecondFlowSpeed;
                float4 _BubbleSpeed;
                float _FlowStrength;
                float _FoamThreshold;
                float _Opacity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 baseUV = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                half flowA = SAMPLE_TEXTURE2D(
                    _MainTex, sampler_MainTex, baseUV + _Time.y * _FlowSpeed.xy).r;
                half flowB = SAMPLE_TEXTURE2D(
                    _MainTex, sampler_MainTex, baseUV + _Time.y * _SecondFlowSpeed.xy).r;
                half flow = saturate((flowA * 0.7h + flowB * 0.3h) * _FlowStrength);

                float2 bubbleUV = input.uv * _Bubble_ST.xy + _Bubble_ST.zw;
                bubbleUV += _Time.y * _BubbleSpeed.xy;
                half bubble = SAMPLE_TEXTURE2D(_Bubble, sampler_Bubble, bubbleUV).r;
                half foam = smoothstep(_FoamThreshold, 1.0h, max(flow, bubble));

                half3 color = lerp(_ColorIn.rgb, _ColorOut.rgb, flow);
                color = lerp(color, _ColorHighlight.rgb, foam);
                half movingDetail = smoothstep(0.48h, 0.98h, flow) * 0.18h;
                half alpha = saturate(max(movingDetail, foam * 0.45h) * _Opacity) * input.color.a;
                return half4(color * input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
