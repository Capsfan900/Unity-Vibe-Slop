// A UGUI fill that reads as a FLUID in a tube: a surface that waves and flows along the bar, a bright
// meniscus at the surface and at the fill's leading edge, a body that darkens toward the bottom, and a
// tilt (_Slosh) plus phase (_SloshPhase) the component drives from the player's acceleration.
//
// Modelled on Unity's UI-Default so it works under URP with a CanvasRenderer: same stencil / clip-rect /
// alpha-clip plumbing, premultiplied "Blend One OneMinusSrcAlpha", the Image's vertex colour as the tint.
// Nothing here exceeds 1.0 in any channel — the meniscus brightens TOWARD white, never past it — because
// light in this game means "you deflected" and the HUD must never out-shout the enemy's cue flash.
//
// UV contract: the fill Image is a null-sprite Simple image whose RectTransform anchors span 0.._Fill of
// the bar (BarView drives them), so uv.x is 0..1 across the VISIBLE fill and bar-space x = uv.x * _Fill.
// _Aspect (bar width / bar height) converts bar-space x into height units for the leading-edge meniscus.
Shader "VibeGame1/UI/FluidBar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        _Fill ("Fill ratio (bar space)", Range(0, 1)) = 1
        _Aspect ("Bar width / height", Float) = 23.3
        _Level ("Fluid level, fraction of height", Range(0, 1)) = 0.86
        _Wave ("Wave amplitude, fraction of height", Range(0, 0.3)) = 0.045
        _WaveLength ("Waves across the bar", Range(0.25, 6)) = 1.6
        _Flow ("Flow, waves per second", Range(-3, 3)) = 0.32
        _Slosh ("Surface tilt, height fraction per bar width", Range(-1, 1)) = 0
        _SloshPhase ("Wave phase offset", Float) = 0
        _Glow ("Meniscus brightness 0..1", Range(0, 1)) = 0.55
        _Pulse ("Meniscus pulse 0..1", Range(0, 1)) = 0
        _Deep ("Darkening toward the bottom", Range(0, 1)) = 0.35
        _Edge ("Meniscus width, fraction of height", Range(0.01, 0.6)) = 0.16
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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "FluidBar"
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

            float _Fill, _Aspect, _Level, _Wave, _WaveLength, _Flow, _Slosh, _SloshPhase;
            float _Glow, _Pulse, _Deep, _Edge;

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

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 tex = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                float2 uv = IN.texcoord;

                // Bar space: where this pixel sits along the WHOLE bar, 0..1, and its height fraction.
                float x = uv.x * _Fill;
                float t = _Time.y;

                // The surface: two harmonics so it reads as water rather than as a sine, flowing along
                // the bar; the tilt is about the bar's centre so both bars jostle as one liquid.
                float phase = x * _WaveLength * 6.2831853 - t * _Flow * 6.2831853 + _SloshPhase;
                float wave = sin(phase) * 0.62 + sin(phase * 2.17 + 1.3) * 0.38;
                float surface = _Level + _Wave * wave + _Slosh * (x - 0.5);
                surface = clamp(surface, 0.05, 1.0);

                // Inside the fluid = below the surface. Anti-aliased against the pixel size.
                float d = surface - uv.y;
                float aa = max(fwidth(uv.y), 1e-4) * 1.5;
                float inside = smoothstep(-aa, aa, d);

                // Meniscus: a bright band just under the surface, and along the leading edge of the fill
                // (converted to height units through the aspect so it is the same thickness on screen).
                float atSurface = 1.0 - smoothstep(0.0, _Edge, d);
                float edgeDist = (_Fill - x) * _Aspect;
                float atEdge = 1.0 - smoothstep(0.0, _Edge, edgeDist);
                float glow = saturate(max(atSurface, atEdge) * _Glow + _Pulse * max(atSurface, atEdge));

                // Body: the Image's colour, darker toward the bottom of the fluid.
                float depth = saturate(d / max(surface, 0.001));
                float3 body = IN.color.rgb * tex.rgb * (1.0 - _Deep * depth);
                // Brighten toward white for the meniscus; never past 1 in any channel.
                float3 col = lerp(body, saturate(body + (1.0 - body) * 0.72), glow);
                col = min(col, 1.0);

                half4 color = half4(col, IN.color.a * tex.a * inside);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                color.rgb *= color.a;   // premultiplied, matching the blend mode above
                return color;
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
