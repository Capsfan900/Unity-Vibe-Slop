using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The Berserk Eclipse sky, asserted as shipped values (rule 9): an enormous black solar disc ringed
    /// by burning light over a world drowned in red — without ever walking into the two documented traps.
    ///
    /// Trap 1, ACES: above ~1.25 intensity the tonemapper desaturates a saturated red toward ORANGE.
    /// The whole red field therefore lives in LDR vertex colours on a material tinted exactly 1.0 — it
    /// physically cannot cross the knee — and only the near-white corona rim rides an HDR material over
    /// the 1.05 bloom threshold, where desaturating toward white is what burning should do anyway.
    ///
    /// Trap 2, readability: the sky is the backdrop every enemy silhouette is read against. The Trilight
    /// equator term is the only light on a backlit enemy torso; FeatureTests enforces a 0.15
    /// linear-luminance floor on the LIVE RenderSettings, and this suite enforces the same floor on the
    /// factory constant so a bad value fails in EditMode, before anyone plays it.
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
                    "the red field must stay LDR or ACES turns it orange and it smears in bloom");
                Assert.Greater(rimPeak, 1.05f,
                    "the corona rim is supposed to cross the bloom threshold — it is the burning light");
                Assert.AreEqual(Starfield.CoronaHdrBoost, rimPeak, 0.0001f,
                    "shipped corona boost drifted from the asserted constant");
            });
        }

        [Test]
        public void DomeIsRedNotPurple()
        {
            RunOnBuiltSky((mesh, mats) =>
            {
                // The dome is written first; its vertex count comes from the builder's own constants.
                int domeVerts = Starfield.DomeVertexCount;
                Color[] cols = mesh.colors;
                Assert.GreaterOrEqual(cols.Length, domeVerts, "dome vertex layout changed; update this test");

                float r = 0f, g = 0f, b = 0f;
                for (int i = 0; i < domeVerts; i++) { r += cols[i].r; g += cols[i].g; b += cols[i].b; }

                // The old violet dome had b > r everywhere. Blood red means red decisively dominant
                // over BOTH other channels, not merely warmer.
                Assert.Greater(r, b * 2.0f, "dome mean red must dominate blue — this is the whole ask");
                Assert.Greater(r, g * 2.0f, "red over green, or the field greys out under ACES");
            });
        }

        [Test]
        public void DiscIsNearBlackButNotAHoleInTheWorld()
        {
            RunOnBuiltSky((mesh, mats) =>
            {
                // The disc ships #0D0304: reads as black beside the burning rim, but keeps ~2/255 under
                // an 8-10/255 enemy body so an overlap is dim-on-dark, not shape-into-void.
                Color[] cols = mesh.colors;
                int discVerts = 0;
                for (int i = 0; i < cols.Length; i++)
                {
                    Color c = cols[i];
                    float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    if (c.a >= 0.999f && peak > 0.001f && peak < 0.07f && c.r > c.g) discVerts++;
                }
                Assert.GreaterOrEqual(discVerts, 40,
                    "expected the dead-sun disc: near-black (but never float-zero) red-leaning vertices");
            });
        }

        [Test]
        public void HorizonHasASilhouette()
        {
            // 2026-09-06 VFX pass: "the sky and surrounds must look polished and like a complete game" --
            // before this the horizon was fog and nothing else. A jagged near-black ring reads as distant
            // ruins without spending any of the light budget or introducing a new hue.
            RunOnBuiltSky((mesh, mats) =>
            {
                int domeVerts = Starfield.DomeVertexCount;
                Color[] cols = mesh.colors;
                Vector3[] verts = mesh.vertices;
                int silhouetteVerts = 0;
                for (int i = domeVerts; i < cols.Length; i++)
                {
                    float peak = Mathf.Max(cols[i].r, Mathf.Max(cols[i].g, cols[i].b));
                    // Near black, full alpha, and clearly below eye level (the y component of a unit-ish
                    // direction at radius ~25 with a small negative pitch).
                    if (cols[i].a >= 0.999f && peak > 0.001f && peak < 0.12f && verts[i].y < 0f)
                        silhouetteVerts++;
                }
                Assert.GreaterOrEqual(silhouetteVerts, 64,
                    "expected a full ring of near-black silhouette geometry just below the horizon");
            });
        }

        // ---------------------------------------------------------------- environment palette

        [Test]
        public void EnvironmentPaletteIsBloodRed()
        {
            Assert.Greater(ProjectSetup.VoidColor.r, ProjectSetup.VoidColor.b * 2f, "fog must be red, not violet");
            Assert.Greater(ProjectSetup.AmbientSky.r, ProjectSetup.AmbientSky.b, "sky ambient must lean red");
            Assert.GreaterOrEqual(ProjectSetup.AmbientGround.r, ProjectSetup.AmbientGround.b, "ground bounce must not lean violet");
            Assert.Greater(ProjectSetup.KeyLightColor.r, ProjectSetup.KeyLightColor.g, "key light is a dying red sun");
            Assert.Greater(ProjectSetup.KeyLightColor.g, ProjectSetup.KeyLightColor.b, "ember, not magenta");
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
            // The old cool sky term measured ~0.130 linear; the red replacement ships ~0.135. This band
            // guards both directions: dimmer costs landing legibility, much brighter flattens the frame.
            float lum = Lum(ProjectSetup.AmbientSky.linear);
            Assert.That(lum, Is.InRange(0.10f, 0.18f),
                "sky ambient luminance drifted — this term is what makes platform TOPS readable");
        }

        // ---------------------------------------------------------------- helpers

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
