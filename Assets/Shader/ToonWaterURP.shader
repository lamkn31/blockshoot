Shader "WaterFlow/3D/ToonWater"
{
    Properties
    {
        [Header(Color)]
        _DeepColor      ("Deep Color", Color)        = (0.02, 0.22, 0.42, 1)
        _ShallowColor   ("Shallow Color", Color)     = (0.10, 0.62, 0.78, 1)
        _DepthMax       ("Depth Fade Distance", Range(0.01, 20)) = 3.0
        _DeepStrength   ("Deep Color Strength", Range(0.2, 5)) = 1.0
        _CrestColor     ("Crest Color (wave tops)", Color) = (0.55, 0.85, 0.95, 1)
        _CrestStrength  ("Crest Color Strength", Range(0, 1)) = 0.5

        [Header(Surface Waves Voronoi)]
        _WaveScale      ("Wave Scale (cell density)", Range(0.05, 3)) = 0.4
        _WaveHeight     ("Wave Height", Range(0, 1)) = 0.12
        _WaveSpeed      ("Wave Speed", Range(0, 3)) = 0.5

        [Header(Caustics Web)]
        _CausticColor   ("Caustic Color", Color) = (0.75, 1.0, 0.92, 1)
        _CausticScale   ("Caustic Scale", Range(0.05, 3)) = 0.5
        _CausticSpeed   ("Caustic Speed", Range(0, 2)) = 0.3
        _CausticThin    ("Caustic Line Thinness", Range(0, 0.95)) = 0.4
        _CausticStrength("Caustic Strength", Range(0, 2)) = 0.6

        [Header(Fresnel Rim)]
        _FresnelColor   ("Fresnel Color", Color) = (0.6, 0.9, 1.0, 1)
        _FresnelPower   ("Fresnel Power", Range(0.5, 12)) = 4

        [Header(Foam)]
        _FoamColor      ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDepth      ("Foam Edge Distance", Range(0.01, 4)) = 0.5
        _FoamCutoff     ("Foam Toon Cutoff", Range(0, 1)) = 0.6
        _FoamNoise      ("Foam Noise (optional)", 2D) = "black" {}
        _FoamNoiseScale ("Foam Noise Scale", Range(0.1, 20)) = 6
        _FoamSpeed      ("Foam Scroll (XY)", Vector) = (0.05, 0.03, 0, 0)
        _SurfaceFoam    ("Foam Edge Erode (noise)", Range(0, 1)) = 0.3

        [Header(Transparency)]
        _Alpha          ("Overall Alpha", Range(0, 1)) = 0.9
        _EdgeAlphaBoost ("Edge Opacity Boost", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderType"    = "Transparent"
            "Queue"         = "Transparent"
            "RenderPipeline"= "UniversalPipeline"
        }

        Pass
        {
            Name "ToonWaterForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float4 screenPos   : TEXCOORD3;
                float  fogFactor   : TEXCOORD4;
                float  waveT       : TEXCOORD5;   // 0..1 độ cao sóng để tô màu đỉnh
            };

            TEXTURE2D(_FoamNoise);   SAMPLER(sampler_FoamNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor;
                float  _DepthMax, _DeepStrength;
                float4 _CrestColor;
                float  _CrestStrength;
                float  _WaveScale, _WaveHeight, _WaveSpeed;
                float4 _CausticColor;
                float  _CausticScale, _CausticSpeed, _CausticThin, _CausticStrength;
                float4 _FresnelColor;
                float  _FresnelPower;
                float4 _FoamColor;
                float  _FoamDepth, _FoamCutoff;
                float4 _FoamNoise_ST;
                float  _FoamNoiseScale;
                float4 _FoamSpeed;
                float  _SurfaceFoam;
                float  _Alpha, _EdgeAlphaBoost;
            CBUFFER_END

            // hash 2D -> 2D (điểm ngẫu nhiên trong mỗi ô)
            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Voronoi mềm -> trả về khoảng cách tới điểm gần nhất (0..~1).
            // Điểm trong ô trôi theo thời gian để tạo sóng gợn nhẹ.
            float Voronoi(float2 uv)
            {
                float2 g = floor(uv);
                float2 f = frac(uv);
                float minDist = 1.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 cell = float2(x, y);
                    float2 pnt  = 0.5 + 0.5 * sin(_Time.y * _WaveSpeed + 6.2831 * Hash2(g + cell));
                    float  d    = length(cell + pnt - f);
                    minDist = min(minDist, d);
                }
                return minDist;
            }

            // Độ cao sóng tại vị trí thế giới (mặt XZ)
            float WaveHeight(float2 posXZ)
            {
                return Voronoi(posXZ * _WaveScale) * _WaveHeight;
            }

            // Voronoi F1 (khoảng cách tới điểm gần nhất) - dùng vẽ caustic web
            float VoronoiF1(float2 uv, float speed)
            {
                float2 g = floor(uv);
                float2 f = frac(uv);
                float f1 = 8.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 cell = float2(x, y);
                    float2 pnt  = 0.5 + 0.5 * sin(_Time.y * speed + 6.2831 * Hash2(g + cell));
                    f1 = min(f1, length(cell + pnt - f));
                }
                return f1;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);

                // Dịch chuyển đỉnh theo độ cao Voronoi
                float h = WaveHeight(posWS.xz);
                posWS.y += h;
                OUT.waveT = saturate(h / max(_WaveHeight, 0.0001));

                // Normal bằng finite-difference (2 mẫu lân cận)
                float  e  = 0.15;
                float  hx = WaveHeight(posWS.xz + float2(e, 0));
                float  hz = WaveHeight(posWS.xz + float2(0, e));
                float3 normalWS = normalize(float3(h - hx, e, h - hz));

                OUT.positionWS = posWS;
                OUT.normalWS   = normalWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv         = IN.uv;
                OUT.screenPos  = ComputeScreenPos(OUT.positionCS);
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // ---- Scene depth (linear eye space) at this pixel ----
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float  rawDepth = SampleSceneDepth(screenUV);
                float  sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float  surfEye  = LinearEyeDepth(IN.positionCS.z, _ZBufferParams);
                float  waterDepth = saturate((sceneEye - surfEye) / _DepthMax);

                // ---- Màu nước theo độ sâu (gradient mượt, deep <-> shallow) ----
                float depthT   = saturate(waterDepth * _DeepStrength);
                half3 waterCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);

                // ---- Màu đỉnh sóng: tô thêm sắc thái ở các đỉnh Voronoi ----
                waterCol = lerp(waterCol, _CrestColor.rgb, IN.waveT * _CrestStrength);

                // ---- Caustics: mạng lưới sáng gợn trên mặt nước (2 lớp Voronoi) ----
                float2 cuv = IN.positionWS.xz * _CausticScale;
                float  c1  = VoronoiF1(cuv + _Time.y * _CausticSpeed * float2(0.10, 0.07), _CausticSpeed);
                float  c2  = VoronoiF1(cuv * 1.8 - _Time.y * _CausticSpeed * float2(0.08, 0.05), _CausticSpeed);
                float  caustic = smoothstep(_CausticThin, 0.95, max(c1, c2));
                waterCol += _CausticColor.rgb * caustic * _CausticStrength;

                // ---- Fresnel rim (theo góc nhìn, dùng normal sóng) ----
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 normalWS = normalize(IN.normalWS);
                float  fres = pow(1.0 - saturate(dot(normalWS, V)), _FresnelPower);
                half3  waterFinal = waterCol + _FresnelColor.rgb * fres;

                // ---- Foam: chỉ ở mép giao cắt (depth). Noise chỉ để phá viền ----
                // _FoamNoise mặc định "black" -> foamN = 0 khi chưa gán texture
                float2 foamUV = IN.positionWS.xz * _FoamNoiseScale * 0.05 + _FoamSpeed.xy * _Time.y;
                float  foamN  = SAMPLE_TEXTURE2D(_FoamNoise, sampler_FoamNoise, foamUV).r;

                // edge = 1 tại đường bờ (vật cắt mặt nước), giảm dần ra xa
                float  edge   = 1.0 - saturate((sceneEye - surfEye) / _FoamDepth);
                // noise bào mòn mép ngoài của dải foam (foamN=0 -> dải foam đặc)
                float  foamBand = edge - foamN * _SurfaceFoam;
                float  foamMask = step(_FoamCutoff, foamBand);

                half3 finalColor = lerp(waterFinal, _FoamColor.rgb, foamMask);

                // ---- Alpha (more opaque at shallow edges & foam) ----
                float alpha = lerp(_Alpha, 1.0, waterDepth * _EdgeAlphaBoost);
                alpha = saturate(max(alpha, foamMask));

                finalColor = MixFog(finalColor, IN.fogFactor);
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
