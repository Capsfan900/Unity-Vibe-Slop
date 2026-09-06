using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>The sentry flare (2026-09-06): the arc, the glow, the grapple window, and the shipped wiring.</summary>
    public class FlareTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void TheFlareFloatsUpHangsAndSinks()
        {
            Vector3 v = FlareMath.LaunchVelocity(Vector3.forward, 9f, 3f);
            Assert.AreEqual(9f, v.y, Eps); Assert.AreEqual(3f, v.z, Eps); Assert.AreEqual(0f, v.x, Eps);
            Vector3 p0 = Vector3.zero;
            float apexT = 9f / 4f;   // v / g
            Vector3 apex = FlareMath.Position(p0, v, 4f, apexT);
            Assert.AreEqual(10.125f, apex.y, 1e-3f, "9 m/s up under 4 m/s^2 apexes ~10 m up");
            Assert.Greater(FlareMath.Position(p0, v, 4f, 1f).y, FlareMath.Position(p0, v, 4f, 4.4f).y, "it is sinking by the end of its life");
            Assert.Greater(FlareMath.Position(p0, v, 4f, 4.4f).y, 0f, "and still above the perch when it fades at 4.5 s");
            Assert.AreEqual(9f, FlareMath.LaunchVelocity(Vector3.up, 9f, 3f).magnitude, Eps, "degenerate facing goes straight up");
        }

        [Test]
        public void TheGlowIsTheGrappleWindow()
        {
            Assert.AreEqual(1f, FlareMath.Glow(0f, 4.5f), Eps);
            Assert.AreEqual(0.5f, FlareMath.Glow(2.25f, 4.5f), Eps);
            Assert.AreEqual(0f, FlareMath.Glow(4.5f, 4.5f), Eps);
            Assert.IsTrue(FlareMath.Grappleable(4.0f, 4.5f, SentryFlare.MinGrappleGlow), "at 4.0 of 4.5 s it still glows 11% and takes a hook");
            Assert.IsFalse(FlareMath.Grappleable(4.2f, 4.5f, SentryFlare.MinGrappleGlow), "a dying ember is not a hook");
            Assert.IsFalse(FlareMath.Grappleable(-0.1f, 4.5f, SentryFlare.MinGrappleGlow));
            Assert.AreEqual(0f, FlareMath.Glow(1f, 0f), Eps, "zero life is dark, not NaN");
        }

        [Test]
        public void TheFlareIsATellSoItMayBloom()
        {
            float peak = Mathf.Max(SentryFlare.Core.r, Mathf.Max(SentryFlare.Core.g, SentryFlare.Core.b));
            Assert.Greater(peak, 1.05f); Assert.LessOrEqual(peak, 2f);
        }

        [Test]
        public void TheSentriesBurstAndThePlayerGrapples()
        {
            foreach (var n in new[] { "Sentry_Grunt", "Sentry_Heavy" })
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + n + ".prefab");
                if (p == null) Assert.Ignore("run 4. Build Prefabs");
                var b = p.GetComponent<SentryBurst>();
                if (b == null) Assert.Ignore(n + " was built before SentryBurst existed; run 4. Build Prefabs");
                Assert.Greater(b.flareLife, 3f, n + ": a flare must live long enough to be used creatively");
                Assert.Greater(b.flareUpSpeed / Mathf.Max(0.01f, b.flareGravity), 1.5f, n + ": it must hang, not pop");
            }
            foreach (var n in new[] { "Enemy_Grunt", "Enemy_Heavy" })
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + n + ".prefab");
                if (p != null) Assert.IsNull(p.GetComponent<SentryBurst>(), n + " is a souls_enemy: it never bursts");
            }
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (player == null) Assert.Ignore("run 4. Build Prefabs");
            var g = player.GetComponent<FlareGrapple>();
            if (g == null) Assert.Ignore("Player was built before FlareGrapple existed; run 4. Build Prefabs");
            Assert.Greater(g.tossUpSpeed, 10f, "the toss has to clear a ledge, not a kerb");
            Assert.GreaterOrEqual(g.range, 25f);
        }

        [Test]
        public void TheBurstReadsFromRangeAndStaysInTheRewardFamily()
        {
            // 2026-09-06 VFX pass: the burst has to announce "an event happened here" from far outside
            // the sparks' own travel distance, and it has to look like the flare it throws (violet-white
            // reward), never like the amber bolt (threat).
            float huePeak = Mathf.Max(SentryBurst.BurstHue.r, Mathf.Max(SentryBurst.BurstHue.g, SentryBurst.BurstHue.b));
            Assert.LessOrEqual(huePeak, 1.0f + Eps, "the burst goes through SlashFx's pooled Flare/Ring/Sparks, which normalise to 1.0 -- it must not ask for more than that system gives it");
            Assert.Greater(SentryBurst.BurstHue.b, SentryBurst.BurstHue.r, "violet-leaning, like SentryFlare.Core, never amber like the bolt");

            Assert.GreaterOrEqual(SentryBurst.BurstFlareSize, 2.0f, "a routine parry spark reads at arm's length; a body dying on a span must read bigger than that");
            Assert.GreaterOrEqual(SentryBurst.BurstRingRadius, 2.0f, "the shockwave has to outgrow the sparks' own travel radius to add anything");
            Assert.Greater(SentryBurst.BurstRingSeconds, SentryBurst.BurstFlareSeconds, "the ring is the part still expanding after the flash has already popped");
            Assert.LessOrEqual(SentryBurst.BurstSparkCount, 24, "SlashFx.Sparks clamps at 24; asking for more silently does nothing");
        }

        [Test]
        public void TheTossHasAVisualAnchoredToThePlayer()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (player == null) Assert.Ignore("run 4. Build Prefabs");
            var g = player.GetComponent<FlareGrapple>();
            if (g == null) Assert.Ignore("Player was built before FlareGrapple existed; run 4. Build Prefabs");
            // Before this pass the toss played sparks only at the (already-consumed) flare's position and
            // an FovKick -- nothing read as "thrown FROM here". The ring and chroma pulse are that read.
            Assert.Greater(g.tossRingRadius, 0.5f, "a ring under a pixel is not a ring");
            Assert.Greater(g.tossChroma, 0f);
            Assert.Greater(g.tossChromaSeconds, 0f);
            Assert.AreNotEqual(g.tossFovKick, g.tossChroma, "the g-force pulse must be its own channel, not a copy of the ordinary FovKick punch");
        }
    }
}
