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
        _DetailStrength ("Plasma Relief", Range(0, 1)) = 0.65
        _FilamentStrength ("Hot Filaments", Range(0, 1)) = 0.28
        _RimStrength ("Limb Glow", Range(0, 1)) = 0.35
        // Master multiplier on the FINAL colour AND alpha, driven per renderer by
        // SolarArenaVisual so an exterior shell can part before the camera reaches it.
        // Scaling _Alpha alone is not enough: the rim term below adds 0.22 independently,
        // so a shell faded that way keeps a glowing outline forever.
        _Fade ("Crossing Fade", Range(0, 1)) = 1
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
                float _DetailStrength;
                float _FilamentStrength;
                float _RimStrength;
                float _Fade;
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
                float3 p = normalize(input.positionOS);
                float t = _Time.y * _FlowSpeed;
                // Object-space currents have no longitude seam or pinched UV poles. Large slow
                // eddies carry a second scale of narrow hot filaments; dark troughs give them depth.
                float3 warp = sin(p.yzx * 4.1 + float3(t * 0.43, -t * 0.31, t * 0.27));
                float3 q = p + warp * 0.19;
                float broad = sin(dot(q, float3(0.58, 1.0, 0.37)) * _BandScale + t * 0.72);
                float crossFlow = sin(dot(q.zxy, float3(0.83, -0.43, 0.67)) * _BandScale * 1.37 - t * 0.51);
                float field = broad * 0.68 + crossFlow * 0.32;
                float band = smoothstep(-0.22, 0.72, field);
                float cells = sin(dot(q, float3(1.1, 0.73, -0.61)) * _BandScale * 3.1 + crossFlow * 2.4 - t);
                float detailFade = 1.0 - saturate(fwidth(cells) * 0.65);
                float relief = lerp(1.0, 0.74 + cells * 0.18, _DetailStrength * detailFade);
                float filamentField = abs(field + cells * 0.085);
                float aa = max(fwidth(filamentField), 0.012);
                float filament = 1.0 - smoothstep(0.035, 0.035 + aa, filamentField);
                filament *= detailFade;

                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                float rim = pow(saturate(1.0 - abs(dot(n, v))), _RimPower);
                float pulse = 1.0 + sin(_Time.y * 1.2 + dot(p, float3(2.3, 1.7, -1.1))) * _Pulse;
                half3 color = lerp(_CoreColor.rgb * 0.52, _BandColor.rgb, band * 0.86) * relief * pulse;
                color += _BandColor.rgb * (filament * _FilamentStrength + rim * _RimStrength);
                color = MixFog(color, input.fogFactor);
                half alpha = saturate(_Alpha * (0.68 + band * 0.32) + rim * 0.22);
                // Zero surface opacity reproduces the original additive ceiling/corona exactly.
                // Exterior shells also attenuate the scenery behind them, rather than just adding glow.
                half surface = saturate(_SurfaceOpacity);
                // Fogging RGB alone leaves an opaque fog-coloured disc against the brighter horizon.
                // White fogged toward black is URP's transmittance, and remains ONE with fog disabled.
                // Keep near/mid-range suns intact; release colour AND occlusion over the final 35%.
                half fogTransmission = MixFogColor(half3(1, 1, 1), half3(0, 0, 0), input.fogFactor).r;
                half fogVisibility = smoothstep(0.0h, 0.35h, fogTransmission);
                half fade = saturate(_Fade) * fogVisibility;
                return half4(color * lerp(alpha, 1.0h, surface) * fade, surface * fade);
            }
            ENDHLSL
        }
    }
}
