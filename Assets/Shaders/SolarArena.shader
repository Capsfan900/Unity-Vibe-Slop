Shader "VibeGame1/Solar Arena"
{
    Properties
    {
        _CoreColor ("Hot Core", Color) = (1.8, 1.25, 0.75, 1)
        _BandColor ("Plasma Bands", Color) = (0.2, 0.8, 1.8, 1)
        _Alpha ("Opacity", Range(0, 1)) = 0.48
        _SurfaceOpacity ("Exterior Surface Opacity", Range(0, 1)) = 0
        _FlowSpeed ("Flow Speed", Range(-4, 4)) = 0.65
        _BandScale ("Band Scale", Range(1, 30)) = 11
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _Pulse ("Pulse", Range(0, 1)) = 0.16
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+5" }
        Pass
        {
            Name "SolarArenaForward"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            // Sphere and cylinder primitives have outward-facing caps. Back-face culling keeps their
            // far side from adding a second copy of every HDR plasma layer into the same pixel.
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _BandColor;
                float _Alpha;
                float _SurfaceOpacity;
                float _FlowSpeed;
                float _BandScale;
                float _RimPower;
                float _Pulse;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.positionOS = normalize(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(world);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.positionOS;
                float longitude = atan2(p.z, p.x);
                float t = _Time.y * _FlowSpeed;
                // Three unequal currents keep rotation visible and avoid a uniform glowing ball.
                float broad = sin(p.y * _BandScale + longitude * 2.7 + t * 2.1);
                float cross = sin((p.x * 0.73 + p.z * 1.17) * (_BandScale * 0.72) - t * 1.4);
                float knots = sin((p.x * p.z + p.y * 0.31) * (_BandScale * 2.2) + t * 3.0);
                float field = broad * 0.52 + cross * 0.34 + knots * 0.14;
                float band = smoothstep(0.02, 0.72, field);

                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                float rim = pow(saturate(1.0 - abs(dot(n, v))), _RimPower);
                float pulse = 1.0 + sin(_Time.y * 2.4 + longitude * 1.3) * _Pulse;
                half3 color = lerp(_CoreColor.rgb, _BandColor.rgb, band) * pulse;
                color += _BandColor.rgb * rim * 0.9;
                color = MixFog(color, input.fogFactor);
                half alpha = saturate(_Alpha * (0.68 + band * 0.32) + rim * 0.22);
                // Zero surface opacity reproduces the original additive ceiling/corona exactly.
                // Exterior shells also attenuate the scenery behind them, rather than just adding glow.
                half surface = saturate(_SurfaceOpacity);
                return half4(color * lerp(alpha, 1.0h, surface), surface);
            }
            ENDHLSL
        }
    }
}
