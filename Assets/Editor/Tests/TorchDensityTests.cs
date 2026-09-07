using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>The light budget, as a rule a level author cannot break by accident.</b>
    ///
    /// <para>Written for the 2026-09-06 "route beacon" pass, where the level designer proposed taking
    /// Level_01 from 44 torches to ~58 by putting one on the far corner of every route deck. The right
    /// answer to "is that too many lights?" is not the TOTAL — <see cref="FlickerLight"/> already culls
    /// the light (never the ember mesh) at 42 m, so a torch 60 m down the route costs a distance check.
    /// The number that actually binds is LOCAL OVERLAP: URP's AdditionalLightsPerObjectLimit is 4 on both
    /// shipped RP assets, and the 5th light reaching a surface is dropped by the renderer having been
    /// paid for in culling and in the flicker Update. Worse, WHICH one is dropped changes as the camera
    /// moves, so an over-budget cluster reads as a torch popping on and off while you run past it.</para>
    ///
    /// <para><b>The ruling this file pins:</b> no eye point on a torch may be reached by more than 4 torch
    /// lights. Measured at the torch positions themselves, because that is where the player walks and it
    /// is the densest sample available without a scene. Level_01 as authored peaks at 3.</para>
    ///
    /// <para>These tests are pure and fast (no scene, no play mode) so they run while the editor is busy.</para>
    /// </summary>
    public class TorchDensityTests
    {
        const string LevelPath = LevelArcReport.DefaultLevel;

        /// <summary>Eye height above a deck. Matches LightAudit's probe offset.</summary>
        const float EyeHeight = 1.6f;

        // ------------------------------------------------------------------ the instrument

        static GameObject BuildTorch(out Light light, out FlickerLight flicker)
        {
            // LevelPieceFactory.Torch only touches ctx.Material(), which returns null safely when no
            // material resolver is supplied — so the torch can be built headless and measured.
            var ctx = new LevelPieceContext();
            var def = new TorchDef();
            def.name = "Torch_Probe";
            def.basePosition = Vector3.zero;
            var go = LevelPieceFactory.Torch(def, null, ctx, null, 0);
            light = go.GetComponentInChildren<Light>(true);
            flicker = go.GetComponentInChildren<FlickerLight>(true);
            return go;
        }

        [Test]
        public void TorchLightIsWhatTheDensityRulingAssumes()
        {
            Light light; FlickerLight flicker;
            var go = BuildTorch(out light, out flicker);
            try
            {
                Assert.IsNotNull(light, "the torch lost its Light; the whole budget argument changes");
                Assert.AreEqual(LightType.Point, light.type);
                Assert.AreEqual(9f, light.range, 1e-4f,
                    "torch light range: the radius every overlap number below is computed from");
                Assert.AreEqual(2.5f, light.intensity, 1e-4f);
                Assert.AreEqual(LightShadows.None, light.shadows,
                    "a shadow-casting torch is a whole extra shadow pass, times 44");
                Assert.AreEqual(1.9f, light.transform.localPosition.y, 1e-4f,
                    "light height above the deck; the probe arithmetic uses it");

                Assert.IsNotNull(flicker, "no FlickerLight means no distance cull and no strided update");
                Assert.AreEqual(42f, flicker.cullDistance, 1e-4f,
                    "beyond this the Light is switched off and only the ember mesh carries the beacon");
                Assert.GreaterOrEqual(flicker.frameStride, 2,
                    "flicker is noise: animating every torch every frame buys nothing and costs five " +
                    "Perlin samples plus a transform write each");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void BothShippedRenderPipelineAssetsAgreeOnTheLimit()
        {
            string[] paths = { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" };
            foreach (string path in paths)
            {
                var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                Assert.IsNotNull(urp, path + " is missing");
                Assert.AreEqual(4, urp.maxAdditionalLightsCount,
                    path + ": the per-object limit the torch spacing is budgeted against changed");
            }
        }

        // ------------------------------------------------------------------ the ruling

        [Test]
        public void NoTorchClusterExceedsThePerObjectLightLimit()
        {
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelPath);
            Assert.IsNotNull(def, LevelPath + " is missing");
            Assert.Greater(def.torches.Length, 0, "no torches to audit");

            const float range = 9f;
            const float lightUp = 1.9f;
            int worst = 0;
            string worstAt = "";
            var offenders = new List<string>();

            for (int i = 0; i < def.torches.Length; i++)
            {
                Vector3 eye = def.torches[i].basePosition + Vector3.up * EyeHeight;
                int reaching = 0;
                for (int j = 0; j < def.torches.Length; j++)
                {
                    Vector3 lp = def.torches[j].basePosition + Vector3.up * lightUp;
                    if (Vector3.Distance(lp, eye) <= range) reaching++;
                }
                if (reaching > worst) { worst = reaching; worstAt = def.torches[i].name; }
                if (reaching > 4) offenders.Add(def.torches[i].name + " (" + reaching + ")");
            }

            Assert.IsEmpty(offenders,
                "torch clusters over the 4-light per-object limit — the 5th light is culled having " +
                "contributed nothing, and which one is dropped changes as you move (visible popping): " +
                string.Join(", ", offenders.ToArray()));
            // The spiral (T2_L5..L8, which folds back over itself 9 m up) is the only place in Level_01
            // where this is close; as authored the worst cluster is 3.
            Assert.LessOrEqual(worst, 4, "worst cluster " + worst + " at " + worstAt);
        }

        // ------------------------------------------------------------------ the language

        [Test]
        public void TorchEmberStaysWellUnderTheBoltInWarmBrightness()
        {
            var m = MaterialFactory.Get("M_Torch");
            Assert.IsNotNull(m, "run VibeGame1/2. Create Materials");
            float torch = m.GetColor("_EmissionColor").maxColorComponent;

            // The torch ember IS a documented bloom exception (fire = safety, the bonfire read), so it
            // is allowed over 1.05. What it may never do is climb toward the enemy bolt: both are warm,
            // and the bolt is the one warm thing the player must react to at 32 m/s. Making torches read
            // further by BRIGHTENING them is therefore forbidden — spend size or count, never intensity.
            Assert.Greater(torch, 1.05f,
                "fire is a licensed bloom exception; a torch that does not bloom is not a beacon");
            Assert.GreaterOrEqual(Projectile.HotCorePeak / torch, 1.35f,
                "the bolt must stay clearly brighter than a torch (" + Projectile.HotCorePeak + " vs " + torch + ")");
        }

        [Test]
        public void EveryNavigationalTrimStaysUnderTheBloomThreshold()
        {
            // Route trim is the NEAR instrument: it says "here is the edge you are about to leave".
            // A trim that blooms starts speaking the tell/deflect language instead, which is why raising
            // one to do a distant beacon's job is the wrong lever. The headroom each key actually has is
            // printed in the failure message so a future beacon pass can see what it has to spend.
            string[] keys = { "M_NeonCyan", "M_NeonYellow", "M_NeonPink", "M_NeonRed" };
            foreach (string key in keys)
            {
                var m = MaterialFactory.Get(key);
                Assert.IsNotNull(m, "run VibeGame1/2. Create Materials");
                float peak = m.GetColor("_EmissionColor").maxColorComponent;
                Assert.Less(peak, 1.05f,
                    key + " peak " + peak.ToString("0.000") + " crossed the bloom threshold: a navigational " +
                    "trim that blooms reads as a combat cue");
            }
        }
    }
}
