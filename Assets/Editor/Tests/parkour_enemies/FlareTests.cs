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
            Assert.AreEqual(1.45f, peak, Eps, "the shipped core peak");
            // 2026-09-06 polish pass. The flare used to sit at EXACTLY the bolt's 1.6 on the argument that
            // hue alone should carry flare-vs-bolt. Growing it to 1.40 m of core inside a 3.6 m aura while
            // keeping that peak would have made a grapple point out-read the one object on the span that
            // can kill the player. The hierarchy is now one-directional and must stay that way:
            // BRIGHTEST = the threat, BIGGEST = the tool.
            Assert.Less(peak, Projectile.HotCorePeak, "the bolt must remain the brightest thing on a span");
            Assert.Greater(SentryFlare.OuterScale, 3f, "and the flare must remain the biggest");
        }

        [Test]
        public void TheCoreAndHaloTogetherStayUnderTheAcesCeiling()
        {
            // The halo is an ADDITIVE sphere drawn over the core, so in the overlap the two peaks sum. Budget
            // them together or the flare clips: 1.9 + 0.75 was 2.65 against a 2.0 ceiling.
            float corePeak = Mathf.Max(SentryFlare.Core.r, Mathf.Max(SentryFlare.Core.g, SentryFlare.Core.b));
            float haloPeak = Mathf.Max(SentryFlare.Halo.r, Mathf.Max(SentryFlare.Halo.g, SentryFlare.Halo.b));
            float outerPeak = Mathf.Max(SentryFlare.Outer.r, Mathf.Max(SentryFlare.Outer.g, SentryFlare.Outer.b));
            // THREE shells now, all additive, all concentric: every one of them overlaps the core.
            Assert.LessOrEqual(corePeak + haloPeak + outerPeak, 2f + Eps, "core + halo + outer overlap must not overshoot the 2.0 ACES ceiling");
            Assert.Less(outerPeak, haloPeak, "the outer shell is the FALLOFF -- if it is not dimmer than the halo there is no step and no soft edge");
            Assert.Greater(SentryFlare.OuterScale, SentryFlare.HaloScale, "and it must be wider, or it is just a second halo");
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
            Assert.AreEqual(2.30f, SentryFlare.HaloWorldDiameter(1f, 1f), Eps);
            Assert.AreEqual(SentryFlare.OuterScale, SentryFlare.OuterWorldDiameter(1f, 1f), Eps);

            // glow 0.15: lerp(0.5, 1, 0.15) = 0.575 -> 2.6 * 0.575 = 1.495 m. Compounded, it would have been
            // that times the core's own 0.2875 factor -- 0.43 m, a THIRD of what the code's comment promises.
            float d15 = SentryFlare.HaloWorldDiameter(0.15f, 1f);
            Assert.AreEqual(2.30f * 0.575f, d15, Eps);
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
            Assert.AreEqual(1.40f, SentryFlare.CoreSize, Eps, "the shipped core diameter");
            Assert.AreEqual(2.30f, SentryFlare.HaloScale, Eps, "the shipped inner halo diameter");
            Assert.AreEqual(3.60f, SentryFlare.OuterScale, Eps, "the shipped outer falloff diameter");
            Assert.GreaterOrEqual(SentryFlare.CoreSize, 1.0f, "more than double the 0.5 m it shipped at");
            Assert.Greater(SentryFlare.HaloScale, SentryFlare.CoreSize * 1.5f, "the halo must be visibly bigger than the core, not the same shape restated");
            Assert.Greater(SentryFlare.PulseInterval, 0f);
            Assert.Greater(SentryFlare.PulseSeconds, 0f);
            Assert.LessOrEqual(SentryFlare.PulseSeconds, SentryFlare.PulseInterval, "a pulse must finish fading before the next one fires, or they stack into a flicker");
            Assert.Greater(SentryFlare.PulseSize, SentryFlare.CoreSize, "the pulse has to read bigger than the standing glow to be worth the extra draw call");
            Assert.Greater(SentryFlare.TrailSeconds, 0.18f, "up from the 0.18 s it shipped at -- a longer streak on a slow-moving flare reads as motion, not noise");
        }

        [Test]
        public void TheArrivalAndTheDeathAreShapedNotPopped()
        {
            // 2026-09-06 polish pass. "Polished" is shape and motion, not brightness: a flare used to
            // appear at full size on frame one and vanish when its glow curve happened to reach zero.
            Assert.AreEqual(0.18f, SentryFlare.SpawnPop(0f), 1e-3f, "it starts small");
            Assert.AreEqual(1f, SentryFlare.SpawnPop(SentryFlare.SpawnPopSeconds), 1e-3f, "and settles on exactly 1");
            Assert.AreEqual(1f, SentryFlare.SpawnPop(3f), Eps, "past the pop it costs nothing and changes nothing");

            float peak = 0f;
            for (float a = 0f; a <= SentryFlare.SpawnPopSeconds; a += 0.005f)
                peak = Mathf.Max(peak, SentryFlare.SpawnPop(a));
            Assert.Greater(peak, 1.02f, "an arrival with no overshoot is a fade-in, not a snap");
            Assert.Less(peak, 1.15f, "and an overshoot this big on a 3.6 m aura would read as a second explosion");

            Assert.AreEqual(1f, SentryFlare.DeathContract(0f, 4.5f), Eps);
            Assert.AreEqual(1f, SentryFlare.DeathContract(4.5f - SentryFlare.DeathContractSeconds, 4.5f), Eps,
                            "the contraction starts exactly DeathContractSeconds before the end, not earlier");
            Assert.AreEqual(0f, SentryFlare.DeathContract(4.5f, 4.5f), Eps, "and finishes at zero");
            Assert.Less(SentryFlare.DeathContract(4.4f, 4.5f), SentryFlare.DeathContract(4.3f, 4.5f), "monotonic");
            // It must not eat the grapple window: a flare is still hookable at 4.0 s of 4.5 (glow 0.11),
            // and the contraction has not begun by then.
            Assert.AreEqual(1f, SentryFlare.DeathContract(4.0f, 4.5f), Eps,
                            "the shrink must never start while the flare is still a usable hook");

            // The outer aura breathes on its OWN, far slower clock than the core's flicker.
            Assert.AreEqual(1f, SentryFlare.OuterBreath(0f), Eps);
            Assert.AreNotEqual(SentryFlare.Flicker(0.4f), SentryFlare.OuterBreath(0.4f),
                               "two rates on one object is the whole 'polished' read; one rate is a pulsing ball");
            Assert.Greater(SentryFlare.OuterWorldDiameter(0.15f, 1f), SentryFlare.HaloWorldDiameter(0.15f, 1f),
                           "the softest, widest shell is the LAST thing to go, so a dying flare stays findable");
        }

        [Test]
        public void TheTrailIsARecordedArcNotAStraightTangent()
        {
            // The flare flies a parabola. A two-point trail draws head -> head-minus-velocity, i.e. a
            // straight line along the CURRENT tangent -- pointing at a place the flare has never been.
            Assert.GreaterOrEqual(SentryFlare.TrailPoints, 4, "under four points an arc is an elbow");
            Assert.LessOrEqual(SentryFlare.TrailPoints, 12, "it is a 0.32 s streak, not a ribbon");

            // Proof that the arc is worth recording: over TrailSeconds the real path departs measurably
            // from the tangent line the old trail drew.
            Vector3 v = FlareMath.LaunchVelocity(Vector3.forward, 9f, 3f);
            float g = 4f, t0 = 1.0f;
            Vector3 p0 = FlareMath.Position(Vector3.zero, v, g, t0);
            Vector3 pBack = FlareMath.Position(Vector3.zero, v, g, t0 - SentryFlare.TrailSeconds);
            Vector3 tangent = v + Vector3.down * g * t0;
            Vector3 straight = p0 - tangent * SentryFlare.TrailSeconds;
            Assert.Greater(Vector3.Distance(pBack, straight), 0.15f,
                           "the tangent misses the real path by more than a core radius -- the streak was lying about where the flare came from");
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
