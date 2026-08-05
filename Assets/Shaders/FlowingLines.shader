Shader "Custom/FlowingLines"
{
    Properties
    {
        [Header(Wind Lines)]
        _LineColor    ("Line Color",   Color) = (1, 1, 1, 1)
        _Lanes        ("Lanes (so lan gio)", Float) = 8.0
        _Density      ("Density (so vet moi lan)", Float) = 2.0
        _StreakLen    ("Streak Length (do dai vet)", Range(0.02, 0.9)) = 0.28
        _Taper        ("Taper (do nhon dau)", Float) = 2.0
        _Thickness    ("Thickness (nho=manh)", Float) = 1400.0
        _Intensity    ("Intensity", Float) = 0.8

        [Header(Direction)]
        // huong gio: (1,0)=ngang phai, (0,1)=doc len, (-1,0)=trai...
        _TravelDir    ("Travel Direction (xy)", Vector) = (1, 0, 0, 0)
        _TravelSpeed  ("Travel Speed", Float) = 0.5
        _SpeedVary    ("Speed Variation", Range(0, 1)) = 0.5
        _Curve        ("Curve Amount", Float) = 0.04
        _CurveFreq    ("Curve Frequency", Float) = 3.0
        _Bend         ("Bend (uon cong theo mesh)", Float) = 0.3
        _BendCenter   ("Bend Center", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define PI 3.14159265

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _LineColor;
                float  _Lanes;
                float  _Density;
                float  _StreakLen;
                float  _Taper;
                float  _Thickness;
                float  _Intensity;
                float4 _TravelDir;
                float  _TravelSpeed;
                float  _SpeedVary;
                float  _Curve;
                float  _CurveFreq;
                float  _Bend;
                float  _BendCenter;
            CBUFFER_END

            float hash(float n) { return frac(sin(n) * 43758.5453); }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float t = _Time.y;

                // huong gio (chuan hoa)
                float2 dir = _TravelDir.xy;
                float  len = length(dir);
                float2 d1  = (len > 1e-5) ? dir / len : float2(1, 0); // truc chay
                float2 d2  = float2(-d1.y, d1.x);                     // truc vuong goc

                float along  = dot(IN.uv, d1);
                float across = dot(IN.uv, d2);

                // uon cong theo mesh: cong vong cung (parabol) theo truc ngang
                float bc = across - _BendCenter;
                along += _Bend * bc * bc;

                // uon luon nhe
                along += sin(across * _CurveFreq) * _Curve;

                float acc = 0.0;

                // chia thanh cac lan gio
                float laneF = across * _Lanes;
                int   lane  = (int)floor(laneF);
                float fLane = frac(laneF);

                // xet lan hien tai va lan ke -> vet co the nam vat qua
                [unroll]
                for (int k = 0; k < 2; k++)
                {
                    int   li   = lane + k;
                    float seed = hash((float)li * 1.37);
                    float seed2= hash((float)li * 2.71 + 5.1);

                    // vi tri tam vet trong lan (ngau nhien) -> vet manh, khong dinh giua
                    float laneCenter = (float)k + 0.5 + (seed2 - 0.5) * 0.6;
                    float across_d   = (fLane - laneCenter + 0.5); // khoang cach toi tam vet
                    float thick = exp(-across_d * across_d * _Thickness);
                    if (thick < 0.001) continue;

                    // toc do rieng cho moi lan
                    float spd = _TravelSpeed * (1.0 - _SpeedVary * seed);

                    // nhieu vet chay noi tiep trong lan
                    float s = frac(along * _Density - t * spd + seed);

                    // hinh vet: thuon nhon 2 dau (sin), dau nhon hon theo Taper
                    float dash = 0.0;
                    if (s < _StreakLen)
                    {
                        float u = s / _StreakLen;      // 0..1 doc than vet
                        dash = sin(u * PI);            // 0 -> 1 -> 0
                        dash = pow(dash, _Taper);      // nhon 2 dau
                    }

                    acc += dash * thick;
                }

                float alpha = saturate(acc * _Intensity) * _LineColor.a;
                return half4(_LineColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
