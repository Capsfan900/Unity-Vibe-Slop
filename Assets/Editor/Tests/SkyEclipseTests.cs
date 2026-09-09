using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The Berserk Eclipse sky, asserted as shipped values (rule 9): an enormous black solar disc ringed
    /// by burning light over a world drowned in COLD light — without ever walking into the documented traps.
    ///
    /// 2026-09-06 COLD PASS. The user asked for a blue world and a handful of ringed planets. Every
    /// environment colour moved from blood red to deep blue at MATCHED Rec.709 linear luminance, and the
    /// tests below pin that parity, not the hex — a future hue pass may move the hue again, but if it
    /// moves the LIGHT it will fail here rather than in a playtest.
    ///
    /// Trap 1, ACES: above ~1.25 intensity the tonemapper desaturates a saturated colour. The whole
    /// field therefore lives in LDR vertex colours on a material tinted exactly 1.0 — it physically
    /// cannot cross the knee — and only the near-white corona rim rides an HDR material over the 1.05
    /// bloom threshold, where desaturating toward white is what burning should do anyway.
    ///
    /// Trap 2, readability: the sky is the backdrop every enemy silhouette is read against. The Trilight
    /// equator term is the only light on a backlit enemy torso; FeatureTests enforces a 0.15
    /// linear-luminance floor on the LIVE RenderSettings, and this suite enforces the same floor on the
    /// factory constant so a bad value fails in EditMode, before anyone plays it.
    ///
    /// Trap 3, new with the cold pass: the SKY MUST NEVER SPEAK THE COMBAT LANGUAGE. Warm means exactly
    /// two things in this game — an attack tell, or fire (a torch, a checkpoint). So the corona went cold
    /// with everything else, and the planets are capped far under it. Both are pinned below.
    /// </summary>
    public class SkyEclipseTests
    {
        // ---------------------------------------------------------------- geometry

        [Test]
        public void EclipseIsBerserkScale_NotADecoration()
        {
            // Under ~36 degrees the disc reads as "a moon"; the reference reads as "the sky is a dead
            // sun". At FOV 70 the shipped 38 degrees fills over half the vertical frame.
            Assert.GreaterOrEqual(Starfield.DefaultEclipseDiameterDeg, 36f,
                "the eclipse must dominate the frame, not decorate it");
        }

        [Test]
        public void BottomLimbSitsJustAboveTheHorizon()
        {
            float limb = Starfield.DefaultEclipsePitchDeg - Starfield.DefaultEclipseDiameterDeg * 0.5f;
            // Above 0: the black centre stays off the eye-level band that combat-range enemy heads are
            // read against (the corona glow floods that band instead). Under 6: it still hangs
            // oppressively low — parked at the zenith it would be a ceiling light, not the Eclipse.
            Assert.GreaterOrEqual(limb, 0.5f, "black disc drops onto the eye-level silhouette band");
            Assert.LessOrEqual(limb, 6f, "the eclipse must hang low and oppressive, not overhead");
        }

        // ---------------------------------------------------------------- built mesh

        [Test]
        public void CoronaRimIsTheOnlyThingAllowedToBloom()
        {
            RunOnBuiltSky((mesh, mats) =>
            {
                Assert.AreEqual(2, mesh.subMeshCount, "expected LDR field + hot corona rim submeshes");
                Assert.AreEqual(2, mats.Length);

                float fieldPeak = PeakTint(mats[0]);
                float rimPeak = PeakTint(mats[1]);

                // The field's tint is exactly 1.0 and vertex colours clamp at 1.0, so no tuning of the
                // red field can ever reach the 1.05 bloom threshold or the ~1.25 ACES desat knee.
                Assert.LessOrEqual(fieldPeak, 1.0001f,
                    "the field must stay LDR or ACES desaturates it and it smears in bloom");
                Assert.Greater(rimPeak, 1.05f,
                    "the corona rim is supposed to cross the bloom threshold — it is the burning light");
                Assert.AreEqual(Starfield.CoronaHdrBoost, rimPeak, 0.0001f,
                    "shipped corona boost drifted from the asserted constant");
            });
        }

        [Test]
        public void DomeIsColdBlue()
        {
            RunOnBuiltSky((mesh, mats) =>
            {
                // The dome is written first; its vertex count comes from the builder's own constants.
                int domeVerts = Starfield.DomeVertexCount;
                Color[] cols = mesh.colors;
                Assert.GreaterOrEqual(cols.Length, domeVerts, "dome vertex layout changed; update this test");

                float r = 0f, g = 0f, b = 0f;
                for (int i = 0; i < domeVerts; i++) { r += cols[i].r; g += cols[i].g; b += cols[i].b; }

                // The user's ask, stated as an assertion: blue decisively dominant over BOTH other
                // channels, not merely cooler. Blue over green is a smaller ratio than blue over red on
                // purpose — a blue with no green in it renders as flat ultramarine, not as night.
                Assert.Greater(b, r * 1.8f, "dome mean blue must dominate red — this is the whole ask");
                Assert.Greater(b, g * 1.2f, "blue over green, or the field reads teal rather than midnight");
            });
        }

        [Test]
        public void DiscIsNearBlackButNotAHoleInTheWorld()
        {
            RunOnBuiltSky((mesh, mats) =>
            {
                // The disc ships #03050A: reads as black beside the burning rim, but keeps ~2/255 under
                // an 8-10/255 enemy body so an overlap is dim-on-dark, not shape-into-void.
                Color[] cols = mesh.colors;
                int discVerts = 0;
                for (int i = 0; i < cols.Length; i++)
                {
                    Color c = cols[i];
                    float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    if (c.a >= 0.999f && peak > 0.001f && peak < 0.07f && c.b > c.r) discVerts++;
                }
                Assert.GreaterOrEqual(discVerts, 40,
                    "expected the dead-sun disc: near-black (but never float-zero) blue-leaning vertices");
            });
        }

        [Test]
        public void HorizonHasASilhouette()
        {
            // The distant ruins are a quiet layer within the haze. Opaque random spires read as a
            // repeating row of black planets/mountains from the high, open route.
            RunOnBuiltSky((mesh, mats) =>
            {
                int domeVerts = Starfield.DomeVertexCount;
                Color[] cols = mesh.colors;
                Vector3[] verts = mesh.vertices;
                int silhouetteVerts = 0;
                for (int i = domeVerts; i < cols.Length; i++)
                {
                    // This scenery ring sits at 97.5% of the dome radius. Inspect its authored alpha
                    // rather than accidentally counting the new opaque lower atmosphere as ruins.
                    if (Mathf.Abs(verts[i].magnitude - 25f * 0.975f) > 0.001f) continue;
                    Assert.That(cols[i].a, Is.InRange(0.10f, 0.26f),
                        "distant ruin silhouettes must blend into the sky rather than forming black teeth");
                    silhouetteVerts++;
                }
                Assert.GreaterOrEqual(silhouetteVerts, 64,
                    "expected a full ring of subdued ruin silhouettes within the horizon haze");
            });
        }

        // ---------------------------------------------------------------- environment palette

        [Test]
        public void EnvironmentPaletteIsCold()
        {
            Assert.Greater(ProjectSetup.VoidColor.b, ProjectSetup.VoidColor.r * 2f, "the camera clear must be cold blue, not blood");
            Assert.Greater(ProjectSetup.FogColor.b, ProjectSetup.FogColor.r * 2f, "fog must be cold blue, not blood");
            Assert.Greater(ProjectSetup.AmbientSky.b, ProjectSetup.AmbientSky.r, "sky ambient must lean blue");
            Assert.Greater(ProjectSetup.AmbientEquator.b, ProjectSetup.AmbientEquator.r, "the equator lights every wall and every enemy; it carries the palette");
            Assert.Greater(ProjectSetup.AmbientGround.b, ProjectSetup.AmbientGround.r, "ground bounce must not lean warm");
            Assert.Greater(ProjectSetup.KeyLightColor.b, ProjectSetup.KeyLightColor.r, "the key is a pale cold sun");
            Assert.Greater(ProjectSetup.KeyLightColor.g, ProjectSetup.KeyLightColor.r, "blue through green, not violet");
        }

        [Test]
        public void TheColdPassChangedHueAndNotLightLevel()
        {
            // The single most important property of the pass. Every structural albedo, the enemy
            // readability floor and the 0.010 linear albedo floor in FeatureTests were all tuned against
            // the ambient's LUMINANCE. These are the luminances the blood-red palette shipped with; the
            // cold replacements were fitted to them to four decimal places, so nothing downstream had to
            // be re-tuned. If a later pass moves a hue it must land here too, or something the player
            // needs to see got darker without anyone noticing.
            Assert.AreEqual(0.2045f, Lum(ProjectSetup.AmbientEquator.linear), 0.0025f,
                "equator luminance must match the value every enemy body colour was tuned against");
            Assert.AreEqual(0.1348f, Lum(ProjectSetup.AmbientSky.linear), 0.0025f,
                "sky luminance must match — this is what makes platform TOPS readable on a landing");
            Assert.AreEqual(0.0110f, Lum(ProjectSetup.AmbientGround.linear), 0.0025f,
                "ground bounce luminance must match, or undersides lose their weight");
            Assert.AreEqual(0.1896f, Lum(ProjectSetup.KeyLightColor.linear), 0.0025f,
                "the key light must cast exactly as much light as the dying red sun did");
        }

        [Test]
        public void TheWorldWentColdButTheTellsDidNot()
        {
            // The whole readability argument for a blue world: warm on cold is the highest-contrast pair
            // in the wheel, so every warm TELL got easier to read, not harder. If a future palette pass
            // takes the tells cold with the world it destroys the game's combat readability to satisfy a
            // colour request — this test is the tripwire for exactly that.
            Assert.Greater(Projectile.HotCore.r, Projectile.HotCore.b * 3f,
                "the enemy bolt is AMBER and must stay amber against a cold world");
            Assert.Greater(Projectile.HotCore.r, 1.05f,
                "the bolt is a documented bloom exception — the brightest threat on the span");
            Assert.Greater(Projectile.CueCore.r, Projectile.CueCore.b,
                "the bolt's deflect cue flashes warm-bone, never cold");
            // The key light is the complement of the tell. That is not decoration: it is why the tell reads.
            Assert.Greater(ProjectSetup.KeyLightColor.b, ProjectSetup.KeyLightColor.r,
                "world cold, tells warm — if both ends drift the same way the contrast is gone");
        }

        [Test]
        public void TheFlareStaysVioletAndKeepsItsBudget()
        {
            // The flare is the one tell that is neither amber nor bone, and the cold pass deliberately
            // did NOT take it blue: its blue channel is now the world's dominant channel, so it was
            // pushed toward magenta instead. Nothing in the cold palette has a strong red channel, so red
            // is what separates "use this" from "that is a wall".
            Assert.Greater(SentryFlare.Core.b, SentryFlare.Core.g, "still violet, never cyan");
            Assert.Greater(SentryFlare.Core.r, SentryFlare.Core.g,
                "magenta lift: the red channel is what a cold world cannot imitate");
            float peak = Mathf.Max(SentryFlare.Core.r, Mathf.Max(SentryFlare.Core.g, SentryFlare.Core.b));
            // 2026-09-06: this pinned 1.6 until the flare pass traded brightness for SIZE (core 1.15 -> 1.40 m,
            // aura 2.6 -> 3.6 m, peak 1.6 -> 1.45). Both are deliberate and they disagreed, so the assertion is
            // now the RULE the two passes actually share: the bolt is the brightest thing (it can kill you), the
            // flare is the biggest (it is a tool). FlareTests owns the exact value.
            Assert.Greater(peak, 1.05f, "the flare is still a documented bloom exception");
            Assert.LessOrEqual(peak, Projectile.HotCorePeak + 1e-4f,
                "the flare must never out-shine the bolt: threat reads brighter than tool");
        }

        [Test]
        public void EquatorKeepsTheEnemyReadabilityFloor()
        {
            // Mirrors FeatureTests.Lighting_EquatorLitsVerticals (which measures live RenderSettings in
            // play mode) at the factory constant, so a hue pass that dims the only light on a backlit
            // enemy torso fails here, in EditMode, before anyone has to play it.
            Assert.GreaterOrEqual(Lum(ProjectSetup.AmbientEquator.linear), 0.15f,
                "the equator term is the entire light budget on every backlit enemy the player faces");
        }

        [Test]
        public void PlatformTopsKeepTheirFootingLight()
        {
            // Blue-violet measured ~0.130, blood red ~0.1348, and the cold pass ships the same 0.1348.
            // This band guards both directions: dimmer costs landing legibility, much brighter flattens
            // the frame. TheColdPassChangedHueAndNotLightLevel pins the exact value; this pins the range.
            float lum = Lum(ProjectSetup.AmbientSky.linear);
            Assert.That(lum, Is.InRange(0.10f, 0.18f),
                "sky ambient luminance drifted — this term is what makes platform TOPS readable");
        }

        // ---------------------------------------------------------------- fog: the depth ramp

        /// <summary>The dome's horizon band, mirrored from <c>Starfield.BuildDome</c>'s local
        /// <c>horizon</c> constant (private there). This is the backdrop every combat-range enemy
        /// silhouette is read against, and — because a first-person platformer looks forward and
        /// slightly down — it is what distant geometry is read against too. If that literal moves in
        /// Starfield and this one does not, <see cref="FogIsTheSkyBleedingIn_NotAHolePunchedInIt"/>
        /// starts asserting against a colour the sky no longer has.</summary>
        static readonly Color DomeHorizonBand = Parse("#13233F");
        /// <summary>Same, for <c>Starfield.BuildDome</c>'s <c>zenith</c>.</summary>
        static readonly Color DomeZenith = Parse("#060A17");

        /// <summary>
        /// A5 (2026-09-06). The fog colour has to be the SKY, not the void.
        ///
        /// <para>The old fog was #060D18 — linear luminance .0039, which is the dome's ZENITH value, not
        /// its horizon. Fogging toward it converged distant geometry to something 4.3x DARKER than the
        /// sky behind it: a hole punched in the backdrop rather than haze in front of it, and the exact
        /// reason "more fog" had always been the wrong lever here. Aerial perspective converges a
        /// surface toward the light scattered along the line of sight, so the fog must sit between the
        /// two sky elevations geometry is actually silhouetted against.</para>
        ///
        /// <para>The lower bound is the load-bearing half: fog brighter than a shadowed stone face
        /// (~.009 linear at the shipped albedos and ambient) means distance LIGHTENS the dark parts of a
        /// far platform instead of eating them — which is the whole answer to "a depth ramp that makes
        /// the next deck harder to read is a regression".</para>
        /// </summary>
        [Test]
        public void FogIsTheSkyBleedingIn_NotAHolePunchedInIt()
        {
            float fog = Lum(ProjectSetup.FogColor.linear);
            float horizon = Lum(DomeHorizonBand.linear);
            float zenith = Lum(DomeZenith.linear);

            Assert.Greater(fog, zenith,
                "fog at or under the zenith value is extinction, not haze — distant geometry reads as a hole in the sky");
            Assert.Greater(fog, horizon,
                "the composited lower atmosphere now owns the horizon; fog below the bare dome recreates the dark valley");
            Assert.That(fog, Is.InRange(0.03f, 0.04f),
                "fog must bridge the cloud/sky tonal valley without becoming a luminous wall");
            Assert.Greater(fog, Lum(ProjectSetup.VoidColor.linear) * 2f,
                "the fog and the camera clear are different jobs; fog that equals the clear is the pre-A5 value");

            // Same hue family as the band it stands in front of: the fog may never introduce a colour
            // the sky does not already have.
            Assert.Greater(ProjectSetup.FogColor.b, ProjectSetup.FogColor.g, "fog must stay in the horizon band's blue");
            Assert.Greater(ProjectSetup.FogColor.g, ProjectSetup.FogColor.r, "blue through green, not violet — the sandbox's old #0C0912 was violet");
        }

        [Test]
        public void FogProfileIsTheApprovedStrongerRouteHaze()
        {
            Assert.AreEqual(Parse("#20344D"), ProjectSetup.FogColor,
                "the raised opening requires the measured cloud/sky convergence radiance");
            Assert.AreEqual(36f, ProjectSetup.FogStartDistance, 1e-5f,
                "the sky-mesh and landing clearance remain unchanged");
            Assert.AreEqual(140f, ProjectSetup.FogEndDistance, 1e-5f,
                "170 m was visually negligible; 140 m puts the longest route read at half fog");
        }

        /// <summary>
        /// THE trap, and it is not the one everybody quotes. "Keep fogStartDistance above the Starfield
        /// radius (25)" understates the clearance by 6 m: the eclipse HALO is a flat soft disc of lateral
        /// radius <c>discR * 2.3</c> parked 24 m down the eclipse axis, so its outermost verts are
        /// ~31 m from the camera — the widest thing in the mesh. A start distance between 25 and 31
        /// would fog a WEDGE across the halo, which is the sort of artefact that gets blamed on the
        /// tonemapper for three sessions.
        ///
        /// <para>So this measures the built mesh instead of trusting any comment. Getting under ~32
        /// means sizing the dome to the fog first, which is a different and much larger job.</para>
        /// </summary>
        [Test]
        public void TheSkyIsFogImmuneByGeometryNotByAssumption()
        {
            RunOnBuiltSky((mesh, mats) =>
            {
                float maxDist = 0f;
                foreach (var v in mesh.vertices) maxDist = Mathf.Max(maxDist, v.magnitude);

                Assert.Greater(maxDist, 25f,
                    "sanity: the halo should reach PAST the dome radius — if it does not, this test is measuring the wrong mesh");
                Assert.Greater(ProjectSetup.FogStartDistance, maxDist,
                    "fog starts inside the sky mesh (outermost vert " + maxDist.ToString("0.0") +
                    " m): the eclipse halo will fog as a wedge. Raise FogStartDistance or shrink the sky.");
                Assert.Greater(ProjectSetup.FogStartDistance, maxDist + 2f,
                    "under 2 m of margin over the sky mesh is not a margin — one widening of the halo silently fogs it");
            });
        }

        /// <summary>
        /// Fog must never touch a surface the player is about to stand on. Every jump in Level_01 lands
        /// within 12 m — the ordinary T3 pillar hops are 5-6 m, while the larger portal approaches are
        /// covered by the solar transition instead of a visible landing target. Combat resolves at 3-8 m. Both are far inside the start distance, so foot
        /// placement and deflect reads are at fog factor EXACTLY zero, by construction rather than by
        /// eye. What the ramp is allowed to touch is the route AHEAD, which is preview.
        /// </summary>
        [Test]
        public void FogNeverTouchesCombatOrALandingTarget()
        {
            Assert.AreEqual(0f, FogFactor(8f), 1e-6f, "the 3-8 m combat band must be completely unfogged");
            Assert.AreEqual(0f, FogFactor(12f), 1e-6f, "the farthest landing target in Level_01 must be completely unfogged");
            Assert.GreaterOrEqual(ProjectSetup.FogStartDistance, 24f,
                "fog inside 24 m starts eating the near read; combat is 3-8 m and landings reach 12 m");
        }

        /// <summary>
        /// A5's actual claim, pinned as numbers. The 20-60 m traversal band and the 64-90 m next-arena
        /// read had no depth ramp at all (45 -> 240 gave 7.7% at 60 m and 21% at 87 m). A first A5 pass
        /// at 36 -> 170 was correct but still visually negligible in an identical-camera comparison.
        /// The stronger 36 -> 140 profile uses the safe end of these bounds without crossing it.
        /// </summary>
        [Test]
        public void FogRampsAcrossTheBandTheGameIsActuallyPlayedIn()
        {
            // The pillar line seen from its realm return: a hint of separation, no more.
            Assert.That(FogFactor(25f), Is.InRange(0f, 0.05f), "the near preview band must stay essentially clear");
            // A span's far end / the T2 bridge from the entry. This is the number A5 exists for.
            Assert.That(FogFactor(50f), Is.InRange(0.10f, 0.22f),
                "50 m is the traversal read; under 7% is the pre-A5 nothing, over 22% starts hiding the route");
            Assert.That(FogFactor(64f), Is.InRange(0.25f, 0.35f),
                "the next-arena read needs clear depth separation before the longest sightline");
            // The next arena: spawn pad -> T1 arena is the level's longest sightline at ~87 m.
            Assert.That(FogFactor(87f), Is.InRange(0.45f, 0.50f),
                "the longest sightline in Level_01 must read as FAR, without losing the torches that mark it");
            // Beyond the level's real depth. Nothing is read past ~90 m; the ramp must not be sized to
            // the far clip plane, which is what the old 240 did.
            Assert.LessOrEqual(ProjectSetup.FogEndDistance, 200f,
                "an end past 200 m spends the ramp on distances this level does not have");
            Assert.Greater(ProjectSetup.FogEndDistance, ProjectSetup.FogStartDistance + 80f,
                "too short a ramp is a visible fog wall crossing the geometry");
        }

        /// <summary>Unity's linear fog factor: 0 = untouched, 1 = fully the fog colour.</summary>
        static float FogFactor(float distance)
        {
            float span = ProjectSetup.FogEndDistance - ProjectSetup.FogStartDistance;
            return Mathf.Clamp01((distance - ProjectSetup.FogStartDistance) / span);
        }

        static Color Parse(string hex)
        {
            Assert.IsTrue(ColorUtility.TryParseHtmlString(hex, out var c), "bad hex in test: " + hex);
            return c;
        }

        // ---------------------------------------------------------------- planets

        [Test]
        public void ThereAreAFewPlanets_NotASolarSystem()
        {
            Assert.AreEqual(Starfield.PlanetCount, Starfield.Planets.Length,
                "the advertised count and the shipped table drifted apart");
            Assert.That(Starfield.PlanetCount, Is.InRange(3, 5),
                "\"a bunch... not too many just enough to spice up the scene\" — the user, 2026-09-06");
        }

        [Test]
        public void PlanetsAreSceneryAndCanNeverCompeteWithATell()
        {
            // The sky's brightness order is fixed and this is the bottom of it: planets < the corona rim
            // (CoronaHdrBoost 1.35) < the enemy bolt (1.6) and the sentry flare (1.6) < the deathblow
            // mark (2.6) < the alert tell (3.0). A "glow" on a planet is a soft gradient and a lit limb,
            // never an emissive — nothing in the backdrop may read as a thing to act on.
            Assert.Less(Starfield.PlanetPeakCeiling, Starfield.CoronaHdrBoost,
                "a planet must sit clearly under the one sky element allowed to bloom");
            Assert.Less(Starfield.PlanetPeakCeiling, 1.05f,
                "under the bloom threshold outright: scenery does not glow");

            foreach (var planet in Starfield.Planets)
            {
                Assert.LessOrEqual(Peak(planet.lit), Starfield.PlanetPeakCeiling + 0.0001f,
                    "planet lit limb over the ceiling");
                Assert.LessOrEqual(Peak(planet.shadow), Starfield.PlanetPeakCeiling + 0.0001f,
                    "planet shadow limb over the ceiling");
                Assert.LessOrEqual(Peak(planet.ringColor), Starfield.PlanetPeakCeiling + 0.0001f,
                    "ring dust over the ceiling");
                Assert.Greater(Peak(planet.lit), Peak(planet.shadow) * 2f,
                    "without a real lit-to-shadow range a planet reads as a flat coin, not a sphere");
                Assert.Greater(planet.lit.b, planet.lit.r,
                    "planets belong to the cold world, not to the warm tells");
            }
        }

        [Test]
        public void AtLeastTwoPlanetsHaveSaturnRings()
        {
            int ringed = 0;
            foreach (var planet in Starfield.Planets)
            {
                if (planet.ringOuter <= planet.ringInner || planet.ringAlpha <= 0f) continue;
                ringed++;
                Assert.Greater(planet.ringInner, 1.05f,
                    "the ring must clear the body or it is a halo, not a ring");
                Assert.Greater(planet.ringOuter, planet.ringInner * 1.2f,
                    "a band this thin is sub-pixel at sky distance");
                Assert.That(planet.ringFlatten, Is.InRange(0.10f, 0.55f),
                    "face-on reads as a target reticle, edge-on as a scratch — Saturn sits around 0.3");
            }
            Assert.GreaterOrEqual(ringed, 2, "the user asked for rings like Saturn on at least a couple");
        }

        [Test]
        public void PlanetsStayOutOfTheFightingSightline()
        {
            // Bolts, enemy silhouettes and the parry cue are all read near eye level, and the eclipse is
            // the level's focal image. A planet parked in either place would sit behind something the
            // player has 0.28 s to answer. High in the dome, and outside the eclipse halo's ~44 degree
            // radius (2.3x the 38 degree disc, halved).
            Vector3 eclipse = DirectionOf(0f, Starfield.DefaultEclipsePitchDeg);
            foreach (var planet in Starfield.Planets)
            {
                Assert.GreaterOrEqual(planet.pitchDeg, 40f,
                    "a planet below 40 degrees drops into the band where bolts and heads are read");
                float sep = Vector3.Angle(eclipse, DirectionOf(planet.yawDeg, planet.pitchDeg));
                Assert.Greater(sep, 44f,
                    "planet sits inside the eclipse halo and crowds the level's focal image");
                Assert.Less(planet.diameterDeg, Starfield.DefaultEclipseDiameterDeg * 0.3f,
                    "the eclipse must stay the biggest thing in the sky by a wide margin");
            }
        }

        [Test]
        public void ThePlanetsCostNoExtraDrawCall()
        {
            // They are baked into the same mesh and the same two materials as the rest of the sky. A
            // per-planet renderer would be four more draw calls and four more materials on WebGL, which
            // is a shipped build target. Two submeshes in, two submeshes out.
            RunOnBuiltSky((mesh, mats) =>
            {
                Assert.AreEqual(2, mats.Length, "the sky is still exactly two materials");
                Assert.AreEqual(2, mesh.subMeshCount, "the sky is still exactly two submeshes");

                // Every planet vertex lives in submesh 0, whose tint is 1.0 — so no colour anywhere in
                // the field, planets included, can reach the bloom threshold. Checked on the built mesh
                // rather than on the table, because that is where a stray bright vertex would hide.
                float worst = 0f;
                foreach (var c in mesh.colors) worst = Mathf.Max(worst, Peak(c));
                Assert.LessOrEqual(worst, 1.0001f,
                    "a vertex colour over 1.0 in the LDR field — the sky can only bloom via the rim material");
            });
        }

        // ---------------------------------------------------------------- helpers

        static float Peak(Color c)
        {
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }

        /// <summary>Mirrors Starfield's own yaw/pitch convention (0 = +Z), which is private.</summary>
        static Vector3 DirectionOf(float yawDeg, float pitchDeg)
        {
            float yaw = yawDeg * Mathf.Deg2Rad;
            float pitch = pitchDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(pitch);
            return new Vector3(Mathf.Sin(yaw) * c, Mathf.Sin(pitch), Mathf.Cos(yaw) * c).normalized;
        }

        delegate void SkyAssert(Mesh mesh, Material[] mats);

        static void RunOnBuiltSky(SkyAssert assert)
        {
            var parent = new GameObject("~SkyEclipseTest");
            try
            {
                GameObject sky = Starfield.Build(parent.transform);
                var mesh = sky.GetComponent<MeshFilter>().sharedMesh;
                var mats = sky.GetComponent<MeshRenderer>().sharedMaterials;
                assert(mesh, mats);
            }
            finally
            {
                // Runtime-created mesh and materials are not children of the GameObject; destroy them
                // explicitly or every test run leaks them into the editor session.
                var filter = parent.GetComponentInChildren<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) Object.DestroyImmediate(filter.sharedMesh);
                var renderer = parent.GetComponentInChildren<MeshRenderer>();
                if (renderer != null)
                    foreach (var m in renderer.sharedMaterials)
                        if (m != null) Object.DestroyImmediate(m);
                Object.DestroyImmediate(parent);
            }
        }

        static float PeakTint(Material m)
        {
            Assert.IsNotNull(m);
            Assert.IsTrue(m.HasProperty("_Color"), "sky material lost its _Color tint");
            Color c = m.GetColor("_Color");
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }

        /// <summary>Rec.709 relative luminance of an already-linear colour.</summary>
        static float Lum(Color linear)
        {
            return linear.r * 0.2126f + linear.g * 0.7152f + linear.b * 0.0722f;
        }
    }
}
