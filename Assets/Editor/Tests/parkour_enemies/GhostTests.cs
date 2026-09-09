using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The Sentry ghost (2026-09-06, user-directed body redesign). Three things are pinned here and each
    /// one is a rule someone could break by accident later: the ghost may be LUMINOUS but never BLOOM, it
    /// may be COLD but never warm and never violet, and it may FLOAT but never move its own collider.
    /// </summary>
    public class GhostTests
    {
        const float Eps = 1e-4f;

        static GameObject Ghost()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/pshooter_enemy01.prefab");
            if (p == null) Assert.Ignore("run 4. Build Prefabs");
            return p;
        }

        [Test]
        public void TheGhostIsLuminousAndCannotBloom()
        {
            // "An enemy body never glows until it is deflected" is now stated as a BUDGET rather than a
            // ban (ANIMATION-VFX section 4 rule 9). Every term that can land on one pixel of this body is
            // counted: the lit albedo, the constant emission floor, and ALL FOUR mist wisps stacked.
            Assert.LessOrEqual(SentryGhostVisual.MaxStackedPeak, SentryGhostVisual.BloomThreshold,
                               "a ghost that blooms steals the meaning of a deflect");
            Assert.AreEqual(0.35f + 0.28f + 0.40f, SentryGhostVisual.MaxStackedPeak, Eps, "the shipped budget, term by term");
            Assert.Greater(SentryGhostVisual.ShellEmissionPeak, 0.15f, "and it must still be visibly LIT -- the user asked for a glowing ghost");

            // The deflect spike must stay in a different league, or light on a body stops meaning anything.
            Assert.Greater(3.2f / SentryGhostVisual.ShellEmissionPeak, 6f,
                           "EnemyVisuals.ParryGlow (3.2) must remain many times the ghost's floor");
            // ...and far under the tells that ARE allowed to bloom.
            Assert.Less(SentryGhostVisual.MaxStackedPeak, Projectile.HotCorePeak, "the bolt still out-reads the body it comes from");
        }

        [Test]
        public void TheGhostIsColdAndIsNeitherATellNorAFlare()
        {
            // Warm means a combat tell or it means fire (section 4 rule 10). A body may be neither, and
            // this one is read at 25 m down a span with an amber bolt in flight across it.
            foreach (var c in new[] { SentryGhostVisual.AuraTint, SentryGhostVisual.MistTint })
            {
                Assert.Greater(c.b, c.r * 1.4f, "cold: the blue channel must dominate, and not narrowly");
                Assert.Greater(c.g, c.r, "green over red too -- a pink or blush cast would put an enemy body in the tells' family");
            }
            // Violet is spoken for: SentryFlare.Core means "use this". A grapple point and the enemy that
            // threw it must not share a hue. The flare leans MAGENTA (red above green); the ghost must not.
            Assert.Greater(SentryFlare.Core.r, SentryFlare.Core.g, "guard: the flare is the magenta-leaning one");
            Assert.Less(SentryGhostVisual.AuraTint.r, SentryGhostVisual.AuraTint.g, "the ghost is not");

            // The mist is exactly one wisp's worth of light, no more.
            float mistPeak = Mathf.Max(SentryGhostVisual.MistTint.r, Mathf.Max(SentryGhostVisual.MistTint.g, SentryGhostVisual.MistTint.b));
            Assert.AreEqual(SentryGhostVisual.MistPeak, mistPeak, Eps, "MistTint is already scaled: it must not be re-scaled anywhere else");
        }

        [Test]
        public void TheGhostShellMaterialMatchesTheDataItIsTintedWith()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_SentryGhost.mat");
            if (mat == null) Assert.Ignore("run 2. Create Materials");
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy01"));
            if (data == null) Assert.Ignore("run 3. Create Data");

            // EnemyVisuals writes EnemyData.bodyColor into the SHELL's _BaseColor only. The hem, the arms
            // and the mouth keep the material's own albedo, so if these two ever drift the ghost ships as
            // a body and a skirt in two different colours.
            Color albedo = mat.GetColor("_BaseColor");
            Assert.AreEqual(albedo.r, data.bodyColor.r, 0.01f, "shell and hem must be the same colour");
            Assert.AreEqual(albedo.g, data.bodyColor.g, 0.01f);
            Assert.AreEqual(albedo.b, data.bodyColor.b, 0.01f);

            float albedoPeak = Mathf.Max(albedo.r, Mathf.Max(albedo.g, albedo.b));
            Assert.Greater(albedoPeak, 0.6f, "the ghost separates from a near-black world by VALUE; that is the whole readability plan");
            Assert.LessOrEqual(albedoPeak, 0.9f, "an albedo at 1.0 under the level's brightest light would break the bloom budget");

            float emPeak = mat.GetColor("_EmissionColor").maxColorComponent;
            Assert.LessOrEqual(emPeak, 0.20f + Eps, "the hem's baked emission is a floor, not a tell");
            // Hem worst case: lit + its own emission + the whole mist.
            Assert.Less(SentryGhostVisual.LitShellCeiling + emPeak + SentryGhostVisual.MistPeak * SentryGhostVisual.MistMaxCount,
                        SentryGhostVisual.BloomThreshold, "the hem is inside the budget too, not just the shell");

            // The parry flash is tinted 25% by the normalised emission. Violet used to drag a quarter of
            // the loudest moment in the game off bone-white.
            Assert.Greater(data.emission.b, data.emission.r, "cold accent");
            float emMin = Mathf.Min(data.emission.r, Mathf.Min(data.emission.g, data.emission.b));
            Assert.Less(data.emission.maxColorComponent - emMin, 0.35f,
                        "near-neutral: EnemyVisuals.ParryGlow must stay the steel-on-steel bone it was tuned to be");
        }

        [Test]
        public void TheGhostFloatsWithoutMovingItsOwnCollider()
        {
            var p = Ghost();
            var gv = p.GetComponentInChildren<SentryGhostVisual>(true);
            if (gv == null) Assert.Ignore("pshooter_enemy01 was built before SentryGhostVisual existed; run 4. Build Prefabs");

            // THE INVARIANT. Everything that floats hangs off FloatRoot, which is a child of LungeRoot.
            // The root's capsule, the agent and the deathblow height are exactly what they always were --
            // there is already one open defect about a marker whose framing was broken by a body change.
            var col = p.GetComponent<CapsuleCollider>();
            Assert.IsNotNull(col);
            Assert.AreEqual(2f, col.height, Eps, "the collider is untouched by the redesign");
            Assert.AreEqual(0.45f, col.radius, Eps);
            Assert.AreEqual(1f, col.center.y, Eps);

            Assert.IsNotNull(gv.floatRoot, "nothing floats without it");
            Assert.AreEqual(0f, gv.floatRoot.localPosition.y, Eps, "the float is an OFFSET from zero, never a baked lift");
            Assert.IsTrue(gv.floatRoot.parent != null && gv.floatRoot.parent.name == "LungeRoot",
                          "it must sit under LungeRoot so the wind-up's body lean still carries the whole ghost");
            Assert.Less(gv.bobAmplitude, 0.12f, "a big bob on a ranged sentry makes its own muzzle height wander");
            Assert.Greater(gv.bobAmplitude, 0.02f, "and no bob at all is a statue, not a ghost");
            Assert.Less(gv.bobHz, 1f, "slow: a fast bob reads as a hover-jitter, not as weightlessness");

            var mark = p.GetComponentInChildren<DeathblowMarker>(true);
            if (mark != null)
            {
                Assert.AreEqual(1.45f, mark.bodyHeight, Eps, "the deathblow glyph still sits on the sternum of the SHELL (0.68..1.92)");
                Assert.AreEqual(0.58f, mark.surfaceOffset, Eps, "and still stands clear of it: the shell's radius is 0.53");
                // mark.sentry is NOT checked here: EnemyVisuals.Setup flips it at runtime from
                // EnemyData.rangedOnly, so on the prefab asset it is legitimately false.
            }
        }

        [Test]
        public void TheGhostHasASilhouetteAHemAndAFaceAndNoSword()
        {
            var p = Ghost();
            var gv = p.GetComponentInChildren<SentryGhostVisual>(true);
            if (gv == null) Assert.Ignore("run 4. Build Prefabs");

            Assert.GreaterOrEqual(gv.hem.Length, 5, "under five tatters a hem reads as a polygon, not as cloth");
            for (int i = 0; i < gv.hem.Length; i++) Assert.IsNotNull(gv.hem[i], "tatter " + i);
            Assert.Greater(gv.hemWaveDegrees, 3f, "a hem that does not move is a skirt");
            Assert.Less(gv.hemWaveDegrees, 20f, "and one that flails reads as an attack");

            var vis = p.GetComponentInChildren<EnemyVisuals>(true);
            Assert.IsNotNull(vis);
            Assert.IsNull(vis.weapon, "a ghost carries no 1.35 m cube sword; WeaponPoint falls back to the pivot");
            Assert.IsNotNull(vis.armPivot, "the arm rig SURVIVES: it is what every authored wind-up pose is written against");
            Assert.IsNotNull(vis.weaponPivot);
            Assert.AreEqual(0.5f, vis.armPivot.localPosition.x, Eps, "the shoulder is where it always was, so no telegraph moved");
            Assert.AreEqual(1.45f, vis.armPivot.localPosition.y, Eps);

            Assert.IsNotNull(vis.eye, "the eye is the facing read at range and the posture tell");
            Assert.IsNotNull(gv.mirrorEye, "and the second eye must be mirrored, or the ghost blinks out of one side");
            Assert.AreNotSame(vis.eye, gv.mirrorEye);

            // The visible body must still cover most of the 2 m capsule: a silhouette floating above its
            // own hitbox is a lie about where the enemy is.
            var rends = p.GetComponentsInChildren<Renderer>(true);
            float low = 99f, high = -99f;
            foreach (var r in rends)
            {
                if (r == null || r.name == "Alert" || r.name.StartsWith("Bar") || r.name == "MarkSpot") continue;
                Bounds b = r.localBounds;
                low = Mathf.Min(low, WorldY(r, b.min.y));
                high = Mathf.Max(high, WorldY(r, b.max.y));
            }
            Assert.Less(low, 0.45f, "the hem must hang low enough that the ghost is not floating above its own collider");
            Assert.Greater(high, 1.7f, "and the shell must still fill the top of it");
        }

        static float WorldY(Renderer r, float localY)
        {
            return r.transform.TransformPoint(new Vector3(0f, localY, 0f)).y;
        }

        [Test]
        public void TheMistIsCheapAndBounded()
        {
            var p = Ghost();
            var gv = p.GetComponentInChildren<SentryGhostVisual>(true);
            if (gv == null) Assert.Ignore("run 4. Build Prefabs");

            Assert.LessOrEqual(gv.mistCount, SentryGhostVisual.MistMaxCount,
                               "the bloom budget is computed against MistMaxCount; asking for more silently breaks it");
            Assert.Greater(gv.mistCount, 2, "one or two wisps read as debris, not as mist");

            // NOT a particle system, and nothing in the prefab: the wisps are built at Awake off ONE static
            // additive material, so the whole effect is mistCount transform writes per frame.
            Assert.IsNull(p.GetComponentInChildren<ParticleSystem>(true),
                          "this enemy appears several times per span -- the mist must never become a particle system");

            // It must not fill the frame at knifing distance. Widest wisp centre + its own radius.
            float reach = gv.mistRadius + gv.mistRadiusSpread * 0.5f + gv.mistSize * 0.5f;
            Assert.Less(reach, 1.0f, "at 3 m a 2 m-wide haze is a screen wipe, not an atmosphere");
            Assert.Greater(reach, 0.5f, "and mist tighter than the body is just a fuzzy outline");
            Assert.Greater(gv.mistOrbitSeconds, 4f, "slow: fast wisps read as insects");
            Assert.Less(gv.mistHighHeight, 1.9f, "the mist stays on the body and never climbs into the alert marker's space (y 2.5)");
        }

        [Test]
        public void OnlyTheSentryGruntIsAGhost()
        {
            foreach (var n in new[] { "Enemy_Grunt", "Enemy_Heavy", "Boss" })
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + n + ".prefab");
                if (p == null) continue;
                Assert.IsNull(p.GetComponentInChildren<SentryGhostVisual>(true), n + " keeps the pill body");
                var vis = p.GetComponentInChildren<EnemyVisuals>(true);
                if (vis != null) Assert.IsNotNull(vis.weapon, n + " still carries its blade");
            }

            var heavyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/pshooter_enemy02.prefab");
            if (heavyPrefab != null)
            {
                Assert.IsNull(heavyPrefab.GetComponentInChildren<SentryGhostVisual>(true),
                    "the Heavy Sentry is a stone reliquary, not the pale ghost");
                var visuals = heavyPrefab.GetComponentInChildren<EnemyVisuals>(true);
                Assert.IsNotNull(visuals);
                Assert.IsNull(visuals.weapon,
                    "the ranged-only reliquary has three face apertures instead of a misleading melee blade");
            }

            // The Heavy Sentry lost the violet with the ghost: no BODY may wear the flare's hue.
            var heavy = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy02"));
            if (heavy == null) Assert.Ignore("run 3. Create Data");
            Assert.Greater(heavy.bodyColor.b, heavy.bodyColor.r, "cold, not violet");
            Assert.Greater(heavy.emission.b, heavy.emission.r, "and its parry-flash accent with it");

            var ghost = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy01"));
            if (ghost == null) Assert.Ignore("run 3. Create Data");
            Assert.Greater(ghost.bodyColor.maxColorComponent, heavy.bodyColor.maxColorComponent * 2f,
                           "pale ghost against dark heavy: the two sentries separate by VALUE at a glance down a span");
        }
    }
}
