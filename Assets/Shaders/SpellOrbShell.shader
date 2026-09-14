// The spellbook orb's glass shell. One sphere, layered in the fragment: a fresnel rim that defines the
// silhouette and carries the spell's HUE, an object-space swirling field seen through the glass that
// gives the orb an interior, an optional heat wobble on the vertices (fire), and an optional darkened
// centre (void). The core inside it is a separate opaque sphere and is the only part of the orb that
// may cross the bloom threshold; this shell clamps its own output at _PeakCap (shipped 1.0) so it is
// structurally unable to bloom and therefore never whitens — which is what keeps gold gold.
//
// Premultiplied-style blend (One, OneMinusSrcAlpha): colour is added, alpha occludes. That is how the
// void variant darkens the core behind it with zero colour and centre-weighted alpha.
Shader "VibeGame1/Spell Orb Shell"
{
    Properties
    {
        _RimColor ("Rim (glass edge)", Color) = (0.6, 0.8, 1, 1)
        _SwirlColor ("Inner swirl", Color) = (0.3, 0.5, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.85
        _SwirlStrength ("Swirl Strength", Range(0, 1)) = 0.5
        _SwirlScale ("Swirl Scale", Range(1, 30)) = 7
        _SwirlSpeed ("Swirl Speed", Range(-4, 4)) = 0.8
        _Flow ("Vertical Flow (+ rises, - sinks)", Range(-2, 2)) = 0
        _Wobble ("Heat Wobble", Range(0, 1)) = 0
        _Dark ("Void Darkening", Range(0, 1)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Charge ("Charge", Range(0, 1)) = 0
        _PeakCap ("Peak Cap", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Name "SpellOrbShellForward"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half4 _SwirlColor;
                float _RimPower;
                float _RimStrength;
                float _SwirlStrength;
                float _SwirlScale;
                float _SwirlSpeed;
                float _Flow;
                float _Wobble;
                float _Dark;
                float _Opacity;
                float _Charge;
                float _PeakCap;
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
                float3 pos = input.positionOS.xyz;
                // Heat wobble: a small displacement along the normal, two frequencies so it boils
                // instead of breathing. 0.03 of a 0.5-radius primitive is 6% of the shell.
                float w = sin(_Time.y * 5.3 + pos.y * 17.0 + pos.x * 9.0) * 0.6
                        + sin(_Time.y * 8.1 - pos.z * 21.0 + pos.y * 7.0) * 0.4;
                pos += input.normalOS * w * 0.03 * _Wobble;
                float3 world = TransformObjectToWorld(pos);
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
                float rate = 1.0 + _Charge * 2.0;
                float t = _Time.y * _SwirlSpeed * rate;
                // Vertical flow: the field slides along object Y, so embers rise and rot sinks. The
                // shell's object Y is aligned to world up by SpellbookVisual each frame.
                float3 q = p - float3(0.0, _Flow * _Time.y * 0.55, 0.0);
                float3 warp = sin(q.yzx * 3.7 + float3(t * 0.53, -t * 0.41, t * 0.29));
                q += warp * 0.22;
                float a = sin(dot(q, float3(0.61, 1.0, 0.43)) * _SwirlScale + t * 0.8);
                float b = sin(dot(q.zxy, float3(0.87, -0.47, 0.71)) * _SwirlScale * 1.31 - t * 0.6);
                float field = a * 0.65 + b * 0.35;
                float swirl = smoothstep(0.05, 0.75, field);

                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                float facing = saturate(dot(n, v));
                float rim = pow(saturate(1.0 - facing), _RimPower);

                // Glass: the centre is see-through and the edge carries hue; the swirl lives inside and
                // thins toward the rim so the two layers do not stack into one flat disc.
                half3 color = _RimColor.rgb * rim * _RimStrength
                            + _SwirlColor.rgb * swirl * _SwirlStrength * (0.35 + 0.65 * facing);
                color *= 1.0 + _Charge * 0.35;
                // Structural cap: this shell cannot cross the bloom threshold however it is tuned.
                color = min(color, _PeakCap.xxx);
                color = MixFog(color, input.fogFactor);

                half alpha = saturate(rim * _RimStrength * 0.85 + swirl * _SwirlStrength * 0.4);
                // Void: centre-weighted occlusion with no colour pulls the core behind it into shadow.
                half dark = _Dark * facing * facing;
                color *= 1.0 - dark * 0.7;
                alpha = saturate(alpha + dark);
                half o = saturate(_Opacity);
                return half4(color * o, alpha * o);
            }
            ENDHLSL
        }
    }
}
