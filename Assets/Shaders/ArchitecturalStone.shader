Shader "VibeGame1/Architectural Stone"
{
    Properties
    {
        _BaseColor ("Cold Stone", Color) = (0.28, 0.32, 0.38, 1)
        _EmissionColor ("Existing Readability Floor", Color) = (0, 0, 0, 1)
        _Smoothness ("Stone Smoothness", Range(0, 1)) = 0.22
        _Metallic ("Metallic", Range(0, 1)) = 0
        _SpecularHighlights ("Specular Highlights", Float) = 1
        _BlockSize ("Slab Width / Depth / Course Height (m)", Vector) = (2.8, 1.4, 0.7, 0)
        _JointWidth ("Joint Width (m)", Range(0.005, 0.06)) = 0.018
        _GrainStrength ("Stone Grain", Range(0, 0.3)) = 0.12
        _EdgeWear ("Edge Wear", Range(0, 0.3)) = 0.14
        _ReliefDepth ("Surface Relief (m)", Range(0, 0.02)) = 0.008
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        // One identical material buffer in every pass keeps SRP batching valid. Pattern coordinates
        // come from world position and the primitive's UVs, both preserved by Unity static batching.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _EmissionColor;
            float4 _BlockSize;
            half _Smoothness;
            half _Metallic;
            half _SpecularHighlights;
            float _JointWidth;
            float _GrainStrength;
            float _EdgeWear;
            float _ReliefDepth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ArchitecturalForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex StoneVertex
            #pragma fragment StoneFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct StoneAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct StoneVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 fogAndVertexLight : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 4);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            StoneVaryings StoneVertex(StoneAttributes input)
            {
                StoneVaryings output = (StoneVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fogAndVertexLight = half4(ComputeFogFactor(pos.positionCS.z),
                    VertexLighting(pos.positionWS, output.normalWS));
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            float StoneHash(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float StoneNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(StoneHash(cell), StoneHash(cell + float2(1, 0)), f.x),
                            lerp(StoneHash(cell + float2(0, 1)), StoneHash(cell + 1), f.x), f.y);
            }

            half4 StoneFragment(StoneVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 n = normalize(input.normalWS);
                float3 an = abs(n);
                // Large paving slabs above; narrower masonry courses down the sides. World metres
                // prevent a 30 m causeway stretching the same block over its entire length.
                float top = step(max(an.x, an.z), an.y);
                float2 plane = top > 0.5 ? input.positionWS.xz :
                    (an.x > an.z ? input.positionWS.zy : input.positionWS.xy);
                float2 blockSize = max(float2(_BlockSize.x, lerp(_BlockSize.z, _BlockSize.y, top)), 0.1);
                float2 grid = plane / blockSize;
                grid.x += frac(floor(grid.y) * 0.5);
                float2 cell = floor(grid);
                float2 edge = min(frac(grid), 1.0 - frac(grid)) * blockSize;
                float jointDistance = min(edge.x, edge.y);
                float footprint = max(length(ddx(plane)), length(ddy(plane)));
                float aa = max(footprint * 0.65, 0.002);
                float joint = 1.0 - smoothstep(_JointWidth, _JointWidth + aa, jointDistance);
                float grainFade = 1.0 - smoothstep(0.025, 0.10, footprint);
                float grain = (StoneNoise(plane * 18.0) - 0.5) * grainFade;
                float slabTone = lerp(0.94, 1.04, StoneHash(cell));

                // Recover the face's physical UV dimensions from derivatives. Unlike object scale,
                // these survive static batching and work on pitched ramp slabs too.
                float3 dpdx = ddx(input.positionWS);
                float3 dpdy = ddy(input.positionWS);
                float2 duvdx = ddx(input.uv);
                float2 duvdy = ddy(input.uv);
                float detUV = duvdx.x * duvdy.y - duvdx.y * duvdy.x;
                float invDetUV = rcp(max(abs(detUV), 1e-12));
                float2 faceSize = float2(length(dpdx * duvdy.y - dpdy * duvdx.y),
                    length(dpdy * duvdx.x - dpdx * duvdy.x)) * invDetUV;
                float2 faceEdge = min(input.uv, 1.0 - input.uv) * faceSize;
                float borderDistance = min(faceEdge.x, faceEdge.y);
                float wornEdge = 1.0 - smoothstep(0.025, 0.085 + aa, borderDistance);
                float inset = (1.0 - smoothstep(0.012, 0.012 + aa, abs(borderDistance - 0.15))) * 0.12;

                // Millimetres of shading relief, never displacement. The collider, silhouette and
                // NavMesh therefore retain the exact authored cube. Suppress subpixel grain at speed.
                float height = (-joint + grain * 0.18) * _ReliefDepth;
                float3 r1 = cross(dpdy, n);
                float3 r2 = cross(n, dpdx);
                float det = dot(dpdx, r1);
                float3 gradient = (ddx(height) * r1 + ddy(height) * r2) *
                    (sign(det) / max(abs(det), 1e-8));
                n = normalize(n - clamp(gradient, -0.35, 0.35));

                SurfaceData surface = (SurfaceData)0;
                float finish = slabTone * (1.0 + grain * _GrainStrength) * (1.0 - joint * 0.34 - inset);
                surface.albedo = _BaseColor.rgb * (finish + wornEdge * _EdgeWear);
                surface.metallic = _Metallic;
                surface.specular = half3(0.04, 0.04, 0.04);
                surface.smoothness = saturate(_Smoothness + wornEdge * 0.08 - joint * 0.12);
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0 - joint * 0.16;
                surface.emission = _EmissionColor.rgb; // no decorative emission is added
                surface.alpha = 1;

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = n;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    lighting.shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                lighting.fogCoord = input.fogAndVertexLight.x;
                lighting.vertexLighting = input.fogAndVertexLight.yzw;
                lighting.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, n);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
