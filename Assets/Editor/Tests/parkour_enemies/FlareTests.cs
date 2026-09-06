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
            Assert.AreEqual(1.6f, peak, Eps, "the same peak as Projectile.HotCore: hue carries flare-vs-bolt, never brightness");
        }

        [Test]
        public void TheCoreAndHaloTogetherStayUnderTheAcesCeiling()
        {
            // The halo is an ADDITIVE sphere drawn over the core, so in the overlap the two peaks sum. Budget
            // them together or the flare clips: 1.9 + 0.75 was 2.65 against a 2.0 ceiling.
            float corePeak = Mathf.Max(SentryFlare.Core.r, Mathf.Max(SentryFlare.Core.g, SentryFlare.Core.b));
            float haloPeak = Mathf.Max(SentryFlare.Halo.r, Mathf.Max(SentryFlare.Halo.g, SentryFlare.Halo.b));
            Assert.LessOrEqual(corePeak + haloPeak, 2f + Eps, "core + halo overlap must not overshoot the 2.0 ACES ceiling");
            Assert.Greater(corePeak, 1.05f, "the core is still a documented exception to the bloom cap");
            Assert.Less(haloPeak, corePeak * 0.5f, "the halo buys range with area, not brightness -- it must stay far below the core");
            Assert.Greater(SentryFlare.Halo.b, SentryFlare.Halo.r, "the halo is the same violet family as the core, never amber like the bolt");
        }

        [Test]
        public void TheHaloIsDrivenInWorldSpaceAndNeverCollapses()
        {
            // REGRESSION GUARD: the halo is a child of the sphere the core scaling writes to. Driven in LOCAL
            // space its scale compounded the core's, squaring both the glow curve and the flicker -- at glow 0.1
            // the "never a pinprick" halo was smaller than the core it was meant to surround.
            float f1 = SentryFlare.Flicker(0f);
            Assert.AreEqual(1f, f1, Eps, "flicker at birth is unity");

            Assert.AreEqual(SentryFlare.HaloScale, SentryFlare.HaloWorldDiameter(1f, 1f), Eps, "at full glow the halo is exactly HaloScale across");
            Assert.AreEqual(2.6f, SentryFlare.HaloWorldDiameter(1f, 1f), Eps);

            // glow 0.15: lerp(0.5, 1, 0.15) = 0.575 -> 2.6 * 0.575 = 1.495 m. Compounded, it would have been
            // that times the core's own 0.2875 factor -- 0.43 m, a THIRD of what the code's comment promises.
            float d15 = SentryFlare.HaloWorldDiameter(0.15f, 1f);
            Assert.AreEqual(2.6f * 0.575f, d15, Eps);
            Assert.Greater(d15, SentryFlare.CoreWorldDiameter(0.15f, 1f) * 2f, "even a dying halo stays a wide aura around the core, not a pinprick inside it");

            // The flicker is applied ONCE: doubling it doubles the diameter, it never squares.
            Assert.AreEqual(d15 * 1.12f, SentryFlare.HaloWorldDiameter(0.15f, 1.12f), Eps, "flicker applies linearly, exactly once");
            Assert.AreEqual(SentryFlare.CoreSize, SentryFlare.CoreWorldDiameter(1f, 1f), Eps);
        }

        [Test]
        public void TheIdlePulseRespectsTheSharedSlashFxBudget()
        {
            // SlashFx pools 28 live effects and Spawn() returns null at the cap. A flare pulses forever, every
            // 0.55 s: unbudgeted, a span full of flares would push the combat tells out of the pool.
            Assert.IsTrue(SentryFlare.PulseAllowed(0));
            Assert.IsTrue(SentryFlare.PulseAllowed(SentryFlare.PulseLiveBudget));
            Assert.IsFalse(SentryFlare.PulseAllowed(SentryFlare.PulseLiveBudget + 1), "past the budget the standing core+halo is the read");
            Assert.LessOrEqual(SentryFlare.PulseLiveBudget, 4, "the pool is 28 slots shared with every combat tell");
        }

        [Test]
        public void TheFlareIsMuchLargerAndAddsAHalo()
        {
            // 2026-09-06 VFX pass, from the user: "the flare needs to be much larger and more visible" -- it is
            // a usable traversal tool (DASH-grapple, tossed up), not a decoration.
            Assert.AreEqual(1.15f, SentryFlare.CoreSize, Eps, "the shipped core diameter");
            Assert.AreEqual(2.6f, SentryFlare.HaloScale, Eps, "the shipped halo diameter");
            Assert.GreaterOrEqual(SentryFlare.CoreSize, 1.0f, "more than double the 0.5 m it shipped at");
            Assert.Greater(SentryFlare.HaloScale, SentryFlare.CoreSize * 1.5f, "the halo must be visibly bigger than the core, not the same shape restated");
            Assert.Greater(SentryFlare.PulseInterval, 0f);
            Assert.Greater(SentryFlare.PulseSeconds, 0f);
            Assert.LessOrEqual(SentryFlare.PulseSeconds, SentryFlare.PulseInterval, "a pulse must finish fading before the next one fires, or they stack into a flicker");
            Assert.Greater(SentryFlare.PulseSize, SentryFlare.CoreSize, "the pulse has to read bigger than the standing glow to be worth the extra draw call");
            Assert.Greater(SentryFlare.TrailSeconds, 0.18f, "up from the 0.18 s it shipped at -- a longer streak on a slow-moving flare reads as motion, not noise");
        }

        [Test]
        public void TheSentriesBurstAndThePlayerGrapples()
        {
            foreach (var n in new[] { "pshooter_enemy01", "pshooter_enemy02" })
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
