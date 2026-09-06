// The PYRE meter's fire. A UGUI shader (modelled on UI/Default, so it runs on a CanvasRenderer under
// URP with clip rects and stencil masks intact) that draws a burning loading bar entirely from
// procedural value noise: no texture, no particle system, one quad.
//
// The quad is the bar's rect PLUS a fixed flame headroom above it (FireBarView / HudExtensions.PyreFire
// size it). _BarTop is the fraction of the quad's height the bar body occupies; everything above is the
// room the flames may lick into, so the overshoot is bounded by the rect itself and the thing still
// reads as a loading bar. _Fill is where the fire ends horizontally (the meter), _Heat grows with the
// charge so the fire burns higher and faster as it fills, _Kick is a short pulse on a gain, _Full holds
// a steady roaring band at the top. _T is UNSCALED time handed in by the component: hitstop and the
// pause menu must not freeze a fire.
//
// Readability budget: every channel is clamped to 1.0 at the end -- under the scene's 1.05 bloom
// threshold. Light means "you deflected" in this game; the HUD is never allowed to bloom.
Shader "VibeGame1/UI/FireBar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Fill ("Fill", Range(0, 1)) = 0.5
        _Heat ("Heat", Range(0, 1)) = 0.5
        _Kick ("Kick", Range(0, 1)) = 0
        _Full ("Full", Range(0, 1)) = 0
        _T ("Unscaled time", Float) = 0
        _BarTop ("Bar top (fraction of the quad height)", Range(0.2, 1)) = 0.52
        _Aspect ("Quad aspect (width / height)", Float) = 18
        _Ember ("Ember (cool end of the ramp)", Color) = (0.55, 0.10, 0.02, 1)
        _Flame ("Flame", Color) = (1.0, 0.45, 0.08, 1)
        _Core ("Core (hottest)", Color) = (1.0, 0.92, 0.70, 1)

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

            float _Fill, _Heat, _Kick, _Full, _T, _BarTop, _Aspect;
            fixed4 _Ember, _Flame, _Core;

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

            // ---- value noise, three octaves, no texture ------------------------------------------
            float hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float n = 0.5 * vnoise(p);
                n += 0.25 * vnoise(p * 2.03 + 7.1);
                n += 0.125 * vnoise(p * 4.07 + 3.3);
                return n / 0.875;          // 0..1
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float x = IN.texcoord.x;
                float v = IN.texcoord.y;

                float heat = saturate(_Heat + 0.6 * _Kick);
                float body = _BarTop;

                // Scrolling noise in bar space; x is stretched by the aspect so the cells are roughly
                // square and the tongues are as wide as they are tall. Rises with heat and the kick.
                float speed = lerp(0.7, 2.4, heat) * (1.0 + 0.8 * _Kick + 0.5 * _Full);
                float2 p = float2(x * _Aspect * 1.1, v * 3.2 - _T * speed);
                float n = fbm(p);
                float n2 = fbm(p * 1.7 + float2(11.3, _T * 0.35));

                // The meter: fire only where the bar is filled, with a licking front.
                float lick = 0.025 + 0.045 * heat;
                float edge = _Fill + (n - 0.5) * lick;
                float inside = 1.0 - smoothstep(edge - 0.015, edge + 0.005, x);

                // Tongues above the body: how far a flame may reach into the headroom, 0..1 of it.
                float reach = saturate((n - 0.32) * 1.9) * lerp(0.18, 1.0, heat) * (0.75 + 0.5 * n2);
                reach = lerp(reach, max(reach, 0.55 + 0.35 * n), _Full);   // full: a steady roaring band
                float above = saturate((v - body) / max(0.0001, 1.0 - body));
                float flame = v <= body ? 1.0 : saturate(1.0 - above / max(0.02, reach));

                // Temperature: hotter low in the body, hotter near the fill front, hotter with heat.
                float front = exp(-abs(x - _Fill) * 45.0) * (0.55 + 0.45 * heat);
                float depth = v <= body ? (1.0 - 0.35 * v / max(0.001, body)) : (1.0 - above);
                float temp = saturate(flame * depth * (0.5 + 0.55 * heat) * (0.55 + 0.6 * n) + front);

                // Ramp: ember -> flame -> core. Clamped to 1.0 per channel at the end (bloom cap).
                float3 col = lerp(_Ember.rgb, _Flame.rgb, saturate(temp * 1.5));
                col = lerp(col, _Core.rgb, saturate((temp - 0.62) * 2.6));
                col = min(col, 1.0);

                float bodyAlpha = 0.82 + 0.18 * n;
                float tongueAlpha = flame * flame * (0.7 + 0.3 * n2);
                float alpha = inside * (v <= body ? bodyAlpha : tongueAlpha);

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
