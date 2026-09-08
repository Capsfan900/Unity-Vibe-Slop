// A rainbow that swirls around the BORDER of a UI rect and nothing else.
//
// Modelled on UI/Default (and on VibeGame1/UI/FireBar next door) so it runs on a CanvasRenderer under a
// ScreenSpaceOverlay canvas with UGUI clip rects and stencil masks intact. One quad, no texture.
//
// Shape: a rounded-rectangle signed distance field evaluated in ASPECT-CORRECTED rect space, so the band
// is the same thickness in pixels on the long sides as on the short ones. Only the band within
// _Thickness INSIDE the rounded outline is lit; everything further in is alpha 0, so the radio's glass,
// its ticker text and its progress bar stay fully readable through the middle.
//
// Swirl: every fragment resolves a perimeter coordinate t in 0..1 by walking the rect (up the right side,
// left along the top, down the left, right along the bottom). Hue is a function of t, so the spectrum is
// laid out AROUND the frame rather than across it, and t is offset by time, so the whole spectrum travels
// -- light chasing around the frame. A single brighter "comet" head rides the same coordinate at its own
// rate so the eye has one thing to follow instead of a uniform smear.
//
// _T is UNSCALED time handed in by RainbowBorderView: the radio keeps playing through hitstop and the
// pause menu, so its frame must keep moving or the HUD reads as broken.
//
// Readability budget (docs/ANIMATION-VFX.md section 4): the HUD never blooms. RGB is scaled by _Peak and
// hard-clamped, and the shipped _Peak is well under the 1.05 threshold. Light means "you deflected"; a
// decoration around the music player must never out-shout a cue flash.
Shader "VibeGame1/UI/RainbowBorder"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _T ("Unscaled time", Float) = 0
        _Aspect ("Rect aspect (width / height)", Float) = 2.586
        _Radius ("Corner radius (fraction of rect height)", Range(0, 0.5)) = 0.1
        _Thickness ("Band thickness (fraction of rect height)", Range(0.005, 0.5)) = 0.055
        _Feather ("Edge feather (fraction of rect height)", Range(0.001, 0.1)) = 0.012
        _HueCycles ("Spectra per lap", Range(0.25, 4)) = 1
        _SwirlSpeed ("Laps per second (hue travel)", Float) = 0.18
        _CometSpeed ("Laps per second (comet head)", Float) = 0.42
        _CometLength ("Comet length (fraction of a lap)", Range(0.02, 1)) = 0.34
        _CometGain ("Comet brightness gain", Range(0, 1)) = 0.26
        _Saturation ("Saturation", Range(0, 1)) = 0.78
        _Peak ("Peak channel (bloom budget)", Range(0, 1.05)) = 0.82
        _Alpha ("Band alpha", Range(0, 1)) = 0.85

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            float _T, _Aspect, _Radius, _Thickness, _Feather;
            float _HueCycles, _SwirlSpeed, _CometSpeed, _CometLength, _CometGain;
            float _Saturation, _Peak, _Alpha;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            // Hue 0..1 -> RGB, full value. The classic six-segment ramp written branchlessly.
            float3 HueToRgb(float h)
            {
                float3 k = frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0;
                return saturate(abs(k) - 1.0);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Rect space, aspect corrected: height is 1, width is _Aspect.
                float w = max(0.001, _Aspect);
                float2 half_ = float2(w * 0.5, 0.5);
                float2 c = (IN.texcoord - 0.5) * float2(w, 1.0);

                // Rounded-rectangle SDF. 0 on the outline, negative inside.
                float r = min(_Radius, min(half_.x, half_.y));
                float2 q = abs(c) - (half_ - r);
                float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;

                // The band: inside the outline, within _Thickness of it. Both edges feathered, so the
                // interior is exactly alpha 0 and the frame does not alias at 300x116.
                float f = max(0.0005, _Feather);
                float outer = 1.0 - smoothstep(-f, f, sd);
                float inner = smoothstep(-_Thickness - f, -_Thickness + f, sd);
                float band = outer * inner;
                if (band <= 0.0) return fixed4(0, 0, 0, 0);

                // Perimeter coordinate: bottom-right corner -> up the right -> left along the top ->
                // down the left -> right along the bottom. Arc length, so hue travels at an even speed
                // on a wide rect instead of racing across the short ends the way an atan2 sweep does.
                float perimeter = 2.0 * (w + 1.0);
                float s;
                if (abs(c.x) * half_.y >= abs(c.y) * half_.x)
                    s = c.x > 0.0 ? (c.y + 0.5)
                                  : (1.0 + w + (0.5 - c.y));
                else
                    s = c.y > 0.0 ? (1.0 + (half_.x - c.x))
                                  : (2.0 + w + (c.x + half_.x));
                float t = s / perimeter;

                float hue = frac(t * _HueCycles - _T * _SwirlSpeed);
                float3 col = lerp(float3(1, 1, 1), HueToRgb(hue), _Saturation);

                // One comet head riding the same lap: brightest at its head, trailing behind it.
                float d = frac(t - _T * _CometSpeed);
                float comet = pow(saturate(1.0 - d / max(0.001, _CometLength)), 2.0);

                float gain = 1.0 - _CometGain + _CometGain * 2.0 * comet;
                col = min(col * _Peak * gain, 1.0);

                float alpha = band * _Alpha * (0.72 + 0.28 * comet);

                fixed4 color = fixed4(col, alpha) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
