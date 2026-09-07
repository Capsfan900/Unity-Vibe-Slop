# Graphics polish — plan only, 2026-09-06

**Brief.** Make the game look markedly more finished and more materially believable, keep it
recognisably the same dark-fantasy game, and cost neither framerate nor readability. Plan only:
nothing in this pass was edited.

---

## 1. What the style actually IS today

Read from the files, not assumed.

| Layer | Shipped state | Where |
|---|---|---|
| World geometry | **100% `GameObject.CreatePrimitive(PrimitiveType.Cube)`**, non-uniformly scaled, no bevels, UVs meaningless | `Assets/Scripts/Level/LevelPieceFactory.cs:275`, `Assets/Editor/LevelGreyboxBuilder.cs:380` |
| Surfaces | 30 `URP/Lit` materials, **untextured flat colours**, `metallic 0` everywhere, `smoothness 0` everywhere except `M_Enemy` 0.34 / `M_Balloon` 0.4 / `M_Water` 0.7 — and `Configure` sets `_SPECULARHIGHLIGHTS_OFF` on every matte one | `Assets/Editor/MaterialFactory.cs:236-250` |
| Characters | 4 of 10 carry an imported ~12k-tri FBX (Chorister, Penitent, Revenant, Halberdier); Grunt / Heavy / Warden / Ninja are primitive rigs | `Assets/Editor/MiniBossFactory.cs`, `Assets/Editor/PrefabFactory.cs` |
| Lighting | **One** directional key `#C9542E @ 1.05` low from +Z, Trilight ambient (sky/equator/ground `#6B4045` / `#82503A` / `#1F1010` x1.35), ~40 flickering point lights, **no baked GI, no lightmaps, no light probes, no reflection probes**, `RenderSettings.skybox = null` | `Assets/Editor/ProjectSetup.cs:249-346`; no `LightingData.asset` exists |
| Sky | `Starfield` — a procedural mesh: gradient dome, 64-segment horizon ruin silhouette, nebulae, 1200 stars, the 38 deg eclipse disc and an HDR corona rim. **Two draw calls.** | `Assets/Scripts/Feel/Starfield.cs` |
| Fog | Linear, `#1A0708`, **45 to 240 m** | `Assets/Editor/ProjectSetup.cs:305-311` |
| Post | ACES, Bloom 1.05 / 0.60 / 0.62, Vignette 0.27, FilmGrain 0.26, contrast +20, saturation -14, exposure +0.15, WB +14/+6, CA driven at runtime by `CameraFX` only | `ProjectSetup.SetupVolumeProfile` |
| AA | Camera is **FXAA** (`Assets/Editor/PrefabFactory.cs:751`); MSAA off in both RP assets; `QualitySettings.antiAliasing = 0` on both tiers | |
| SSAO | Present on `PC_Renderer.asset` and **deliberately disabled** by `ProjectSetup.SetupRendererFeatures` | `Assets/Editor/ProjectSetup.cs:225-247` |
| Renderers | PC = **Deferred**, renderScale 1. WebGL maps to quality level 0 "Mobile" -> `Mobile_RPAsset` (**Forward, renderScale 0.8, soft shadows OFF**) | `ProjectSettings/QualitySettings.asset:119-134` |

**The honest diagnosis.** The game does not read as unfinished because it is dark or low-poly. It
reads as unfinished because **every surface in it is a flat, matte, untextured, un-normal-mapped,
un-occluded, hard-edged box lit by one light and a constant**. Light has nothing to break on. What
is carrying the look today is the sky, the hue discipline in `MaterialFactory.Table`, the fog and the
effects — all of which are good and none of which should be touched.

Three things restore material believability without moving one hue: **micro-variation** so light
breaks, **contact occlusion** so boxes stop floating, and **edge definition** so silhouettes read as
objects. None of those adds glow or raises bloom.

---

## 2. Ranked plan

### Tier A — free or near-free, data in factories that already exist. Do these first.

**A1. Give the structural palette a specular response.** *Look:* stone catches a low ember skim; a
torch puts a sheen on the wall it is bolted to; a platform lip separates from its face.
*Touches:* `Assets/Editor/MaterialFactory.cs` `Table` — one `smoothness` argument per spec
(`M_Ground` ~0.08, `M_Stone` ~0.12, `M_Platform` ~0.18, `M_Gate` / `M_Torch` post ~0.15).
**Data-only, already rule-9 compliant.** *Cost:* ~0 — URP/Lit computes the lobe anyway; the keyword
just picks a branch. WebGL identical. *Precedent:* `M_Enemy 0.34` was added for exactly this reason
and is documented as "the only thing that gives an enemy shape". *Risk:* LOW but real — a highlight
is brightness, and brightness is this game's currency. Cap every structural material at 0.25 so the
lobe stays broad and dim. *Proof:* an EditMode test over the shipped `.mat` assets asserting every
structural `_Smoothness` in [0.05, 0.25] and `_Metallic` == 0, plus a `LevelRouteShots` capture pair.
Generator: **2. Create Materials**.

**A2. FXAA -> SMAA on the gameplay camera.** *Look:* the emissive trim bars stop crawling. A 0.06 m
bar at 30 m is currently a dotted, shimmering line, and FXAA — a luminance-edge blur — is the worst
choice for thin high-contrast lines on near-black. *Touches:* `Assets/Editor/PrefabFactory.cs:751`,
one enum plus `antialiasingQuality`. *Cost:* comparable to FXAA, well under 1 ms; spatial only, so no
ghosting and no temporal smear on a 200 deg/s turn — unlike TAA/STP (Tier C). MSAA is not an option:
it does not work in the PC tier's deferred path. *Risk:* LOW. *Proof:* EditMode test on the built
Player prefab's `UniversalAdditionalCameraData`. Generator: **4. Build Prefabs**.

**A3. Re-enable SSAO on the PC renderer, tuned conservatively.** *Look:* platforms sit on the floor
instead of hovering; interior corners and the enemy's feet gain contact. *Touches:*
`ProjectSetup.SetupRendererFeatures` (today the function that turns it off) — data written into
`PC_Renderer.asset`. *Cost:* Unity documents SSAO at **1-3 ms desktop**; `Downsample` on quarters the
pixel work. *WebGL:* **do not ship it there** — `Mobile_Renderer.asset` has no SSAO feature and
renderScale is already 0.8. Desktop-only, explicitly. *Risk:* **MEDIUM, and it is the one item that
argues with a shipped invariant.** AO multiplies down exactly the ambient equator term that
`FeatureTests > Lighting_EquatorLitsVerticals` (floor 0.15 linear luminance) exists to protect, and
it darkens the bottom of the frame — the place the vignette was already cut from 0.34 to 0.27 to stop
it being "a footing tax". Mitigation: `Intensity` at most 0.5, `Radius` ~0.3,
`DirectLightingStrength 0` so it only ever touches ambient. *Proof:*
`PerfProbe.Start("running", 300)` before/after; an `EnemyPortrait` capture at 4.5 m to confirm the
torso value has not fallen; a new EditMode test pinning the four shipped SSAO numbers. Generator:
**1. Project Setup**. *Decision-reversal — see question 1.*

**A4. WebGL parity sweep.** `Mobile_RPAsset` ships `m_SoftShadowsSupported: 0` while the key light is
set `LightShadows.Soft`, so the browser build has hard, stair-stepped shadow edges the desktop build
does not; and `m_UpscalingFilter: 0` (Automatic) at renderScale 0.8 should be verified to resolve to
FSR 1.0 rather than a bilinear stretch — the asset already authors `m_FsrSharpness: 0.92`.
*Cost:* a few shadow taps; FSR is ~0.1 ms. *Risk:* LOW. *Proof:* a WebGL build and a human's eye.

**A5. Bring fog into the band that matters.** — **BUILT 2026-09-06.**
*Shipped:* `ProjectSetup.FogColor` **`#0E1C34`** (lin lum .0117), `FogStartDistance` **36**,
`FogEndDistance` **170**; `SandboxBuilder.EnsureEnvironment` now reads those three constants instead of
mirroring literals. Four new pins in `SkyEclipseTests`.

*Why the numbers differ from the plan's guess.* The plan proposed moving `fogStartDistance` to ~30 and
leaving colour and end alone. Three things came out of actually measuring it:

1. **The colour was the bug, not the range.** `#060D18` is lin lum .0039, which is the dome's **zenith**
   value (`#060A17`, .0032) — not its horizon band (`#13233F`, .0170, 4.3x brighter). Since a
   first-person platformer reads geometry forward and slightly down, the old fog converged distant
   surfaces toward something *darker than the sky behind them*: a hole in the backdrop. That is exactly
   the risk this item was gated on, and it is a property of the colour, so it was fixed there. At .0117
   the fog sits **above a shadowed stone face** (~.009 linear), so a receding platform's dark riser now
   gets *lighter* and only its lit top gets slightly darker — compression toward a mid value, which is
   what aerial perspective is. The "darkens the deck you are about to land on" failure is inverted, not
   merely accepted.
2. **The start floor is 31 m, not 25.** The eclipse **halo** is a flat soft disc of lateral radius
   `discR * 2.3` = 19.8 m sitting 24.1 m down the eclipse axis, so its corners are 31.2 m from the
   camera — 6 m past the dome radius everyone quotes. The plan's ~30 would have fogged a wedge across
   the halo. 36 clears the whole built mesh with 4.8 m of margin, at a cost of ~1.5% of the ramp.
   `TheSkyIsFogImmuneByGeometryNotByAssumption` measures the mesh rather than trusting the comment.
   Going under ~32 needs the dome sized to the fog first, which is a bigger change than this item —
   not attempted.
3. **The end mattered as much as the start.** Nothing in Level_01 is read past ~90 m (the tiles are
   walled arenas; the longest sightline is the spawn pad to the T1 arena). A 240 m end spent only the
   first 37% of the ramp on the entire course. 170 spends 48%.

*Resulting fog factors:* 8 m 0% · 12 m 0% · 25 m 0% · 50 m **10%** · 64 m **21%** · 87 m **38%** ·
100 m 48%. Every landing target in the level is inside 12 m (longest hop `T3_Entry` → `T3_Pillar_1`,
9 m; T3's pillar hops 5-6 m) and combat is 3-8 m, so foot placement and deflect reads are at fog factor
**exactly zero** — the ramp only touches route preview.

*Not done, named:* `SandboxBuilder`'s **ambient** is still the warm pre-cold-pass set (`#7A5540` equator
against `ProjectSetup`'s `#3F5E88`). Fixing it changes how every enemy reads in the workshop and is its
own pass.

<details><summary>Original plan text</summary>

**A5. Bring fog into the band that matters.** Fog is 45 to 240 m; combat is 3-8 m and traversal spans
read at 20-60 m, so **there is no aerial perspective anywhere the player actually looks**. Moving
`fogStartDistance` to ~30 (still above the 25 m `Starfield` radius that keeps the sky fog-immune)
gives spans a depth ramp for free. *Touches:* `ProjectSetup.SetupSceneEnvironment` and the mirror in
`SandboxBuilder.EnsureEnvironment`. *Risk:* **MEDIUM — rank it last in Tier A and gate it on a
capture.** The fog colour is near-black, so it darkens rather than whitens: good for a dark enemy
against the sky's mid-red horizon band, bad for the platform you are about to land on. *Proof:*
`LevelRouteShots` at span distances, then a human.

</details>

### Tier B — real projects with a real payoff

**B1. A triplanar world-surface shader — the biggest single change available.** One custom
URP-compatible shader (`VibeGame1/World/Surface`, SRP-batcher-safe) sampling a **procedurally
generated, tileable** noise-normal and a low-frequency albedo mottle in **world space**. Triplanar is
the correct tool here specifically because the world is stretched primitive cubes: it bypasses UVs
entirely and keeps texture density uniform across a 30 x 0.5 x 4 m box. The maps are generated by a
new editor tool alongside `Assets/Editor/UiSprites.cs`, which already writes tiny PNGs on every
build — **no paid assets, no artist, fully regenerable (rule 4)**. *Cost:* 3 samples where there are
currently 0 (2 with the cheap X/Z-plus-Y variant); must be measured on WebGL at 0.8 render scale,
with a single-axis planar fallback as the Mobile variant. *Risk:* LOW to readability if the mean
albedo is held to the shipped hex (those values were measured against the 0.15 ambient floor; only
the *variance* may change) and normal strength stays at or under 0.4. *Trap:* the shader is resolved
by name and must be added to `AlwaysIncludedShaders` — `ProjectSetup.EnsureAlwaysIncludedShader`
exists precisely because a stripped runtime shader renders **nothing**, silently, in a player build.
*Proof:* EditMode test that the generated map's mean equals the shipped colour within 2%; PerfProbe;
a capture.

**B2. A fake bevel / edge-catch term in the same shader.** Low-poly art's own rule is that wide
bevels give broad readable highlights; approximating one from the triplanar blend weights lightens
up-facing edges without adding a triangle. This has a direct gameplay payoff: **platform lips are the
thing a first-person platformer must read**, and they are currently a value-identical corner.

**B3. One baked reflection probe of the eclipse sky per tile.** A1's specular has nothing to reflect —
`RenderSettings.skybox` is deliberately null. A probe gives smooth surfaces a correct dark-red
environment term. Unity's Web docs confirm **all reflection probes are supported on WebGL**, and
`ReflectionProbeBlending` is already on in both RP assets. *Conflict:* the probes belong in
`LevelGreyboxBuilder` / `LevelDefinitionBuilder`, which are the level team's files — name it, do not
take it.

**B4. Baked GI.** The level is fully static and already flagged `ContributeGI`. Baking is the single
biggest "finish" lever left, and WebGL supports baked directional lightmaps (realtime GI it does
not). But it argues with hard rule 4 (a `LightingData.asset` is a build artefact of a scene that
`6. Build Level` regenerates from nothing), it adds minutes to every rebuild, and `FlickerLight`
torches cannot be baked. **Propose, do not start.**

### Tier C — what I would explicitly NOT do, and why

- **Motion blur.** It deletes the arc-read the parry depends on. `docs/ANIMATION-VFX.md` 3.1 already
  records that this project has none and that the **weapon trail is the deliberate substitute**;
  adding blur makes the trail redundant and smears every tell. Competitive FPS players disable it
  universally.
- **Depth of field.** It hides a sentry at 30 m and the grapple flare you must *find* at 30 m — the
  two things the game asks you to spot at distance.
- **A standing chromatic aberration.** `CameraFX.ChromaticPulse` owns CA as a transient *event*
  channel (deflect, flare toss). A constant CA makes that channel invisible and stacks a second
  permanent noise source on top of grain 0.26.
- **TAA or STP.** Both are temporal; both ghost on fast turns and smear small bright moving objects,
  which is exactly what a 32 m/s bolt is. STP implicitly enables TAA. They look better in a still and
  worse in this game.
- **Raising bloom, or emission on any surface.** The 1.05 cap with exactly two exceptions
  (`Projectile.HotCore`, `SentryFlare.Core` + `Halo`) is the game's information architecture, not a
  taste setting. Same for `metallic 0` on trims: a metallic trim gains an environment specular that
  competes with the tells.
- **A hue-shifting colour-grade LUT.** It silently invalidates every measured colour in
  `MaterialFactory` — the deathblow mark took three attempts to land on violet. A LUT is only
  admissible if it is hue-neutral and shapes the toe/shoulder alone.
- **Adaptive Probe Volumes.** APV's streaming and sky occlusion rely on compute shaders, which
  WebGL 2.0 does not provide; it would be a desktop-only lighting system with a different look from
  the shipped build.
- **SSR / SSGI, and higher-poly "realistic" assets.** Expensive, WebGL-hostile, and the second fails
  the brief outright while breaking the measured-pivot pipeline (`ForgeModelProbe`, `PoseSilhouette`).
- **DBuffer decals.** Not supported on OpenGL / GLES, i.e. not on WebGL. Screen-space decals would
  work, but there is no decal content to place and inventing some is out of mandate.

### Tier D — only a human at the screen can decide

Whether specular makes the world too light-catching for a dark-fantasy read; whether AO costs footing
at the bottom of the frame; where fog start belongs; whether the triplanar noise scale reads as
*stone* or as *static* at 16 m/s; and whether A1 + A2 + A3 alone already answer "more polished",
making B1 unnecessary.

---

## 3. Do first

1. **A1** — smoothness pass in `MaterialFactory.Table`. One number per spec, zero frame cost, largest
   look-per-byte in the repo.
2. **A2** — FXAA -> SMAA in `PrefabFactory`. One enum; stops the trims crawling.
3. **A3** — SSAO on the PC renderer, `DirectLightingStrength 0`, desktop-only, with the equator-floor
   test extended to measure an enemy torso *with AO on*.

## 4. Questions

1. **SSAO was disabled deliberately in `Assets/Editor/ProjectSetup.cs:236` with no logged reason.**
   Framerate decision, art decision, or template leftover? If it was art, A3 is off the list.
2. Is **WebGL a real ship target or an insurance policy?** It decides whether B1 needs a
   single-sample fallback variant and whether A3 could ever be global.
3. Is a **bake step (B4)** acceptable at the cost of minutes added to `0. Rebuild Everything` and a
   scene-bound artefact that argues with rule 4?
4. Reflection probes (B3) land in the **level team's** builders. Propose them there, or let
   `ProjectSetup` own a single scene-wide probe instead?
5. **How dark is too dark?** The fog change (A5) and AO both spend value at the bottom of the frame.
   One screenshot from you at a span landing tells me more than any test.

## 5. Files a future implementing pass would touch

`Assets/Editor/MaterialFactory.cs` · `Assets/Editor/PrefabFactory.cs` (camera only) ·
`Assets/Editor/ProjectSetup.cs` · `Assets/Settings/Mobile_RPAsset.asset` ·
`Assets/Shaders/World/*` (new) · `Assets/Editor/Tests/*` (new material / AA / SSAO pins) ·
`docs/ARCHITECTURE.md` (art direction) · `docs/DATAFLOW.md` (**needs a new "Rendering — the surface
stack" map; there is none today**) · `docs/ANIMATION-VFX.md` (audit). Generators to re-run:
**1. Project Setup**, **2. Create Materials**, **4. Build Prefabs**, then **Health Check**.

## Sources

- [Configure screen space ambient occlusion in URP (Unity 6)](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/ssao-renderer-feature-reference.html)
- [Configure for better performance in URP](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html)
- [Unity Manual — Web graphics (baked GI only, all reflection probes supported)](https://docs.unity3d.com/6000.0/Documentation/Manual/webgl-graphics.html)
- [Deferred Rendering Path in URP — requires Shader Model 4.5, no OpenGL APIs](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@16.0/manual/rendering/deferred-rendering-path.html)
- [Introduction to Adaptive Probe Volumes — compute shader dependency](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/probevolumes-concept.html)
- [Introduction to Spatial-Temporal Post-processing in URP — STP implies TAA](https://docs.unity3d.com/6000.1/Documentation/Manual/urp/stp/stp-upscaler.html)
- [Decal Renderer Feature — DBuffer unsupported on OpenGL/GLES](https://docs.unity.cn/6000.0/Documentation/Manual/urp/renderer-feature-decal-reference.html)
- [URP Lit Shader — Surface and Detail Inputs](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/lit-shader.html)
- [Cheap Triplanar Mapping — 80.lv](https://80.lv/articles/cheap-triplanar-mapping)
- [Triplanar Mapping — Catlike Coding](https://catlikecoding.com/unity/tutorials/advanced-rendering/triplanar-mapping/)
- [Low Poly Art in Games: style, examples, design principles](https://rocketbrush.com/blog/low-poly-art-in-games)
- [What is Chromatic Aberration in Gaming? (competitive players disable CA, motion blur, grain)](https://spotlightfx.com/blog/what-is-chromatic-aberration-in-games)
