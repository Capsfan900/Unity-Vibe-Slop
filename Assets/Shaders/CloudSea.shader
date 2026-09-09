Shader "VibeGame1/Cloud Sea"
{
    Properties
    {
        _DeepColor ("Deep Cloud", Color) = (0.10, 0.18, 0.28, 0.45)
        _CloudColor ("Billow", Color) = (0.42, 0.55, 0.66, 0.92)
        _CrestColor ("Billow Crown", Color) = (0.65, 0.74, 0.80, 0.95)
        _WaveHeight ("Wave Height", Range(0, 2)) = 1.50
        _FlowSpeed ("Flow Speed", Range(0, 0.2)) = 0.035
        _LargeScale ("Billow Scale", Range(0.005, 0.08)) = 0.030
        _DetailScale ("Wisp Scale", Range(0.02, 0.2)) = 0.11
        _WarpStrength ("Billow Warp", Range(0, 40)) = 14
        _DetailStrength ("Wisp Erosion", Range(0, 1)) = 0.26
        _EdgeFeather ("Edge Feather", Range(0.01, 0.3)) = 0.12
        _HazeStart ("Cloud Haze Start", Float) = 80
        _HazeEnd ("Cloud Haze End", Float) = 280
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent-10"
        }

        Pass
        {
            Name "CloudSeaForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _CloudColor;
                half4 _CrestColor;
                float _WaveHeight;
                float _FlowSpeed;
                float _LargeScale;
                float _DetailScale;
                float _WarpStrength;
                float _DetailStrength;
                float _EdgeFeather;
                float _HazeStart;
                float _HazeEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + 1.0);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm3(float2 p)
            {
                float value = ValueNoise(p) * 0.57;
                p = mul(float2x2(0.80, -0.60, 0.60, 0.80), p * 2.03) + 17.1;
                value += ValueNoise(p) * 0.29;
                p = mul(float2x2(0.60, 0.80, -0.80, 0.60), p * 2.01) + 9.7;
                value += ValueNoise(p) * 0.14;
                return value;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float t = _Time.y * _FlowSpeed;

                // Broad crossing swells provide the ocean roll. A low-frequency cloud field lifts and
                // depresses whole banks, breaking the sine silhouette into irregular soft masses. The
                // absolute coefficient ceiling remains 0.82 + 0.28 = 1.10 for clearance proofs.
                float swellA = sin(dot(world.xz, float2(0.035, 0.014)) - t * 3.2) * 0.43;
                float swellB = sin(dot(world.xz, float2(-0.021, 0.047)) + t * 2.1) * 0.25;
                float swellC = sin(dot(world.xz, float2(0.071, -0.031)) - t * 4.0) * 0.14;
                float cloudLift = (Fbm3(world.xz * (_LargeScale * 0.72)
                                  + float2(t * 0.36, -t * 0.18)) - 0.5) * 0.56;
                float wave = (swellA + swellB + swellC + cloudLift) * _WaveHeight;
                world.y += wave;

                output.positionWS = world;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y * _FlowSpeed;
                float2 world = input.positionWS.xz;

                // A slow low-frequency field bends a second flow rather than simply scrolling one
                // texture. The result is a family of large, connected cloud banks.
                float2 warpSample = world * (_LargeScale * 0.71) + float2(t * 0.41, -t * 0.29);
                float2 warp = float2(ValueNoise(warpSample), ValueNoise(warpSample + 37.4)) - 0.5;
                float2 billowP = world * _LargeScale + warp * (_WarpStrength * _LargeScale)
                               + float2(t * 0.82, t * 0.23);
                float billowNoise = Fbm3(billowP);

                // Anisotropic counter-flowing noise erodes only the cloud boundaries. It creates torn
                // wisps and negative space without drawing the bright contour rings of the first pass.
                float2 flow = normalize(float2(0.94, 0.34));
                float2 across = float2(-flow.y, flow.x);
                float2 stretched = float2(dot(world, flow) * (_DetailScale * 0.32),
                                          dot(world, across) * (_DetailScale * 1.18));
                stretched += warp * 0.65 + float2(-t * 0.72, t * 0.31);
                float wispNoise = ValueNoise(stretched) * 0.68
                                + ValueNoise(stretched * 2.07 + 13.7) * 0.32;
                float densityField = billowNoise + (wispNoise - 0.5) * _DetailStrength;
                float outer = smoothstep(0.22, 0.56, densityField);
                float body = smoothstep(0.32, 0.76, densityField);
                float crown = smoothstep(0.57, 0.88, billowNoise + (wispNoise - 0.5) * 0.06);

                // Broad nested value bands make the centre of each lobe feel raised. No narrow field
                // ever reaches the crown colour, so the result stays soft rather than outlined.
                // A supported low field and compressed crown contrast read as depth in one bank,
                // rather than isolated luminous brush strokes floating over a black plane.
                half3 color = lerp(_DeepColor.rgb, _CloudColor.rgb, 0.10 + body * 0.78);
                color = lerp(color, _CrestColor.rgb, crown * 0.38);
                color *= lerp(0.88, 1.03, saturate(billowNoise * 0.85 + crown * 0.25));

                float edgeDistance = min(min(input.uv.x, 1.0 - input.uv.x),
                                         min(input.uv.y, 1.0 - input.uv.y));
                float edgeFade = smoothstep(0.0, max(0.001, _EdgeFeather), edgeDistance);
                half alpha = lerp(_DeepColor.a, _CloudColor.a, outer);
                alpha = saturate(alpha + (outer - body) * 0.16 + crown * 0.04);

                // The high crest sees a broad ocean. Route fog must not erase its banks at 140 m;
                // this scenery-only range settles into the same fog colour before the far clip.
                float distanceToCamera = distance(input.positionWS, _WorldSpaceCameraPos);
                float haze = smoothstep(_HazeStart, max(_HazeStart + 1.0, _HazeEnd), distanceToCamera);
                color = lerp(color, unity_FogColor.rgb, haze);
                // Fogged RGB alone still reveals stars and dark nebula discs through low-density
                // banks. Full haze must replace the background, matching the sky's lower atmosphere
                // when the far plane clips this world-space grid below the geometric horizon.
                alpha = lerp(alpha, 1.0h, haze) * edgeFade;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
