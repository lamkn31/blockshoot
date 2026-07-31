Shader "WaterFlow/2D/Water"
{
    Properties
    {
        _NormalWater ("Water Detail", 2D) = "bump" {}
        _NoiseMap ("Noise Texture", 2D) = "black" {}
        _Highlight ("Highlight Color", Color) = (1,1,1,1)
        _Base ("Base Color", Color) = (0.05,0.45,0.76,1)
        _Shadow ("Shadow Color", Color) = (0,0.18,0.59,1)
        _EdgeColor ("Edge Color", Color) = (0.05,0.82,0.97,1)
        _NoiseSpeed ("Noise Speed (XY)", Vector) = (0.06,0.015,0,0)
        _DetailSpeed ("Detail Speed (XY)", Vector) = (-0.035,0.025,0,0)
        _Distortion ("Distortion", Range(0,0.15)) = 0
        _Contrast ("Color Contrast", Range(0.25,4)) = 0.25
        _BubbleMap ("Bubble Texture", 2D) = "black" {}
        _EdgeWidth ("Edge Width", Range(0.01,0.4)) = 0.09
        _EdgeInset ("Edge Gap", Range(0,0.3)) = 0.035
        _EdgeSoftness ("Edge Softness", Range(0.001,0.4)) = 0.055
        _EdgeWaveAmount ("Edge Wave Amount", Range(0,0.15)) = 0.025
        _EdgeWaveFrequency ("Edge Wave Frequency", Range(1,30)) = 1
        _EdgeWaveSpeed ("Edge Wave Speed", Range(-3,3)) = 1
        _EdgeBubbleTiling ("Bubble Density", Range(0.1,30)) = 1
        _EdgeBubbleSpeed ("Bubble Speed", Range(-3,3)) = 0.5
        _EdgeBubbleStrength ("Bubble Strength", Range(0,2)) = 0.85
        _BubbleSize ("Bubble Scale (Small - Large)", Range(0.1,8)) = 1
        _BubbleAlpha ("Bubble Alpha", Range(0,1)) = 0.8
        _EdgeFade ("Edge Fade", Range(0.1,4)) = 1
        _EdgeAlpha ("Edge Alpha", Range(0,1)) = 0.92
        _BubbleDrift ("Bubble Side Drift", Range(0,0.5)) = 0.12
        _BubbleDriftSpeed ("Bubble Drift Speed", Range(0,5)) = 1.1
        _BubbleCapFade ("Bubble End Fade", Range(0.01,3)) = 0.6
        [HideInInspector] _PathWidth ("Path Width", Float) = 1
        [HideInInspector] _PathLength ("Path Length", Float) = 1
        [HideInInspector] _PathClosed ("Path Closed", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "WaterUnlit"
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            TEXTURE2D(_NormalWater);
            SAMPLER(sampler_NormalWater);
            TEXTURE2D(_NoiseMap);
            SAMPLER(sampler_NoiseMap);
            TEXTURE2D(_BubbleMap);
            SAMPLER(sampler_BubbleMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _NormalWater_ST;
                float4 _NoiseMap_ST;
                float4 _BubbleMap_ST;
                half4 _Highlight;
                half4 _Base;
                half4 _Shadow;
                half4 _EdgeColor;
                float4 _NoiseSpeed;
                float4 _DetailSpeed;
                float _Distortion;
                float _Contrast;
                float _EdgeWidth;
                float _EdgeInset;
                float _EdgeSoftness;
                float _EdgeWaveAmount;
                float _EdgeWaveFrequency;
                float _EdgeWaveSpeed;
                float _EdgeBubbleTiling;
                float _EdgeBubbleSpeed;
                float _EdgeBubbleStrength;
                float _BubbleSize;
                float _BubbleAlpha;
                float _EdgeFade;
                float _EdgeAlpha;
                float _BubbleDrift;
                float _BubbleDriftSpeed;
                float _BubbleCapFade;
                float _PathWidth;
                float _PathLength;
                float _PathClosed;
            CBUFFER_END

            float Hash01(float value)
            {
                return frac(sin(value * 73.156f) * 43758.5453f);
            }

            float BubblePulse(float travelled, float seed)
            {
                float cell = floor(travelled);
                float local = frac(travelled);
                float center = lerp(0.18f, 0.82f, Hash01(cell + seed));
                float radius = lerp(0.025f, 0.08f, Hash01(cell + seed + 17.0f));
                return 1.0f - smoothstep(radius, radius + 0.018f, abs(local - center));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 noiseUV = input.uv * _NoiseMap_ST.xy + _NoiseMap_ST.zw;
                noiseUV += _Time.y * _NoiseSpeed.xy;

                half noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseUV).r;
                float2 detailUV = input.uv * _NormalWater_ST.xy + _NormalWater_ST.zw;
                detailUV += _Time.y * _DetailSpeed.xy;
                detailUV += (noise - 0.5h) * _Distortion;

                half3 detailSample = SAMPLE_TEXTURE2D(_NormalWater, sampler_NormalWater, detailUV).rgb;
                half detail = dot(detailSample, half3(0.299h, 0.587h, 0.114h));
                half pattern = saturate((noise * 0.58h + detail * 0.42h - 0.5h) * _Contrast + 0.5h);

                half3 color = lerp(_Shadow.rgb, _Base.rgb, smoothstep(0.05h, 0.95h, pattern));
                color = lerp(color, _Highlight.rgb, smoothstep(0.85h, 1.0h, pattern) * 0.18h);
                float edgeSide = input.uv.y < 0.5f ? 0.0f : 1.0f;
                float wavePhase = input.uv.x * _EdgeWaveFrequency + _Time.y * _EdgeWaveSpeed;
                float wave = (sin(wavePhase + edgeSide * 1.9f) +
                              0.45f * sin(wavePhase * 2.31f + edgeSide * 4.1f)) * _EdgeWaveAmount;
                float rawDistanceToEdge = min(input.uv.y, 1.0 - input.uv.y);
                float distanceFromWaveStart = rawDistanceToEdge - _EdgeInset - wave;
                float edgePosition = saturate(distanceFromWaveStart / max(_EdgeWidth, 0.0001f));
                half edge = sin(edgePosition * 3.14159265h);
                edge *= step(0.0f, distanceFromWaveStart) * step(distanceFromWaveStart, _EdgeWidth);
                edge = pow(saturate(edge), _EdgeFade);
                color = lerp(color, _EdgeColor.rgb, edge * _EdgeAlpha);

                // Screen-space UV giữ hình tròn ở mọi đoạn cong. Texture trôi theo Y
                // của màn hình; LineRenderer tự mask bubble khi chạm thành nước.
                float2 bubbleUV = input.screenPos.xy / input.screenPos.w;
                bubbleUV.x *= _ScreenParams.x / _ScreenParams.y;
                float bubbleTiling = _EdgeBubbleTiling / max(_BubbleSize, 0.0001f);
                bubbleUV = bubbleUV * bubbleTiling * _BubbleMap_ST.xy + _BubbleMap_ST.zw;
                bubbleUV.y -= _Time.y * abs(_EdgeBubbleSpeed);

                half bubble = SAMPLE_TEXTURE2D(_BubbleMap, sampler_BubbleMap, bubbleUV).r;
                half centerMask = smoothstep(
                    _EdgeInset + _EdgeWidth,
                    _EdgeInset + _EdgeWidth + _EdgeSoftness * 2.0h,
                    rawDistanceToEdge);
                half bubbleGlow = bubble * centerMask * _EdgeBubbleStrength * _BubbleAlpha;
                color = lerp(color, _Highlight.rgb, saturate(bubbleGlow));
                return half4(color, _Base.a);
            }
            ENDHLSL
        }
    }
}
