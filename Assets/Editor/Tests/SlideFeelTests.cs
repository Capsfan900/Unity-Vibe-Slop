using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The slide's WEIGHT pass and the player body: the eye's plop, the lens rattle, and the legs thrown
    /// out in front of the camera. Companion to <see cref="SlideImpulseTests"/>, same rules — simulate
    /// and assert the outcome, never assert a constant back at itself, and say plainly what cannot be
    /// proven here: whether any of it FEELS right is a human sliding in the sandbox and looking down.
    ///
    /// <para>Two of these read shipped assets and SKIP rather than fail when the asset predates the
    /// change: <c>Player.prefab</c> before <c>4. Build Prefabs</c> has no body, and <c>GameFeel.asset</c>
    /// before <c>3. Create Data</c> has no rumble key — and a missing YAML key deserialises to the field
    /// initialiser, so a value assert would pass on exactly the un-regenerated asset (ENGINEERING-LOG,
    /// "Rule 9 bit anyway"). The YAML is grepped for the key first.</para>
    /// </summary>
    public class SlideFeelTests
    {
        const float HalfFovDeg = 47.5f;              // 95° vertical FOV
        const float SlideEyeHeight = 1.60f - 0.55f;  // CameraPivot 1.60 minus PlayerFeedback.slideCameraDrop

        static GameFeelSettings Shipped()
        {
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>("Assets/Data/GameFeel.asset");
            Assert.IsNotNull(feel, "Assets/Data/GameFeel.asset is missing — run VibeGame1/3. Create Data.");
            return feel;
        }

        static PlayerBody ShippedBody()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            Assert.IsNotNull(prefab, "Assets/Prefabs/Player.prefab is missing — run VibeGame1/4. Build Prefabs.");
            var body = prefab.GetComponentInChildren<PlayerBody>(true);
            if (body == null)
                Assert.Ignore("Player.prefab has no PlayerBody yet — run VibeGame1/4. Build Prefabs, then re-run.");
            return body;
        }

        // ---------------------------------------------------------------- the spring

        static float SettleWithSpring(float target, float omega, float zeta, float dt, float seconds, out float peak)
        {
            float x = 0f, v = 0f; peak = 0f;
            for (float t = 0f; t < seconds; t += dt)
            {
                SlideImpulse.Spring(x, v, target, omega, zeta, dt, out x, out v);
                if (target > 0f ? x > peak : x < peak) peak = x;
            }
            return x;
        }

        [Test]
        public void TheSpringReachesItsTargetAndOvershootsByTheAnalyticFraction()
        {
            // The shipped eye spring: 4.5 Hz, ζ 0.55, a 0.55 m drop.
            float omega = 2f * Mathf.PI * 4.5f;
            float peak;
            float x = SettleWithSpring(0.55f, omega, 0.55f, 1f / 240f, 2f, out peak);
            Assert.AreEqual(0.55f, x, 0.002f, "did not settle on the slide height.");
            float overshoot = (peak - 0.55f) / 0.55f;
            Assert.AreEqual(SlideImpulse.OvershootFraction(0.55f), overshoot, 0.01f,
                "first-swing overshoot " + overshoot.ToString("0.000") + " disagrees with the closed form.");
            Assert.That(overshoot, Is.InRange(0.08f, 0.16f),
                "the plop should go a little past the floor, not bounce: " + (overshoot * 0.55f).ToString("0.000") + " m.");
        }

        [Test]
        public void TheSpringIsFrameRateIndependent()
        {
            // The same reason SlideImpulseTests runs the decay law at three rates: a frame-rate-dependent
            // slide has shipped here once. Sample the position at fixed wall-clock instants.
            float omega = 2f * Mathf.PI * 6f;
            float[] rates = { 20f, 60f, 240f };
            float[] at = { 0.05f, 0.10f, 0.20f, 0.40f };
            float[,] pos = new float[rates.Length, at.Length];
            for (int r = 0; r < rates.Length; r++)
            {
                float dt = 1f / rates[r], x = 0f, v = 0f, t = 0f;
                int next = 0;
                while (next < at.Length)
                {
                    SlideImpulse.Spring(x, v, 1f, omega, 0.6f, dt, out x, out v);
                    t += dt;
                    while (next < at.Length && t >= at[next] - 1e-4f) pos[r, next++] = x;
                }
            }
            for (int i = 0; i < at.Length; i++)
            {
                // The samples are at most one 20 fps frame apart in time, so allow what the curve moves
                // in 50 ms near its steepest, not a tolerance that would hide a blow-up.
                Assert.AreEqual(pos[2, i], pos[0, i], 0.12f, "20 vs 240 fps at t=" + at[i]);
                Assert.AreEqual(pos[2, i], pos[1, i], 0.05f, "60 vs 240 fps at t=" + at[i]);
                Assert.IsFalse(float.IsNaN(pos[0, i]) || Mathf.Abs(pos[0, i]) > 2f, "the spring blew up at 20 fps.");
            }
        }

        [Test]
        public void CriticalAndOverDampedSpringsNeverOvershoot()
        {
            float omega = 2f * Mathf.PI * 6f;
            float peak;
            SettleWithSpring(1f, omega, 1f, 1f / 60f, 1.5f, out peak);
            Assert.LessOrEqual(peak, 1f + 1e-4f, "critical damping overshot.");
            SettleWithSpring(1f, omega, 1.6f, 1f / 60f, 1.5f, out peak);
            Assert.LessOrEqual(peak, 1f + 1e-4f, "over-damping overshot.");
            Assert.AreEqual(0f, SlideImpulse.OvershootFraction(1f), 1e-6f);
            Assert.AreEqual(0f, SlideImpulse.OvershootFraction(1.3f), 1e-6f);
        }

        [Test]
        public void TheReleaseRisesPastNeutralOnStandUp()
        {
            // The same spring returning to 0 from the slide height overshoots UPWARD: the eye lifts a
            // little past standing as the body comes up, which is the stand-up's whole cue.
            float omega = 2f * Mathf.PI * 4.5f;
            float x = 0.55f, v = 0f, min = 0.55f;
            for (float t = 0f; t < 1.5f; t += 1f / 120f)
            {
                SlideImpulse.Spring(x, v, 0f, omega, 0.55f, 1f / 120f, out x, out v);
                if (x < min) min = x;
            }
            Assert.Less(min, -0.03f, "no upward overshoot on stand-up (min " + min.ToString("0.000") + ").");
            Assert.AreEqual(0f, x, 0.002f, "did not settle back to neutral.");
        }

        // ---------------------------------------------------------------- the rumble

        [Test]
        public void TheRumbleIsQuietBeforeTheScrapeIs_AndZeroAtTheEndSpeed()
        {
            const float max = 0.006f;
            Assert.AreEqual(0f, SlideImpulse.RumbleAmplitude(0f, max), 1e-9f, "must be zero at slideEndSpeed.");
            Assert.AreEqual(max, SlideImpulse.RumbleAmplitude(1f, max), 1e-9f);
            Assert.AreEqual(max, SlideImpulse.RumbleAmplitude(3f, max), 1e-9f, "clamps above the band.");
            // Quadratic: at a third of the band it is a tenth of full. The scrape floors at 40% there.
            Assert.Less(SlideImpulse.RumbleAmplitude(0.33f, max), 0.12f * max,
                "the rattle should be gone by the last third of a slide; the scrape carries the tail.");
            float last = 0f;
            for (float f = 0f; f <= 1f; f += 0.1f)
            {
                float a = SlideImpulse.RumbleAmplitude(f, max);
                Assert.GreaterOrEqual(a, last, "not monotonic at " + f);
                last = a;
            }
        }

        // ---------------------------------------------------------------- the body

        [Test]
        public void TheBodyExists_UnderTheRoot_BelowTheLens_OnThePlayerLayer()
        {
            var body = ShippedBody();
            var prefab = body.transform.root.gameObject;

            Assert.AreEqual(prefab.transform, body.transform.parent,
                "the body must hang off the ROOT (yaws with the look), not the camera pivot.");
            Assert.IsNull(body.GetComponentInParent<Camera>(), "the body is under the camera.");
            Assert.IsNotNull(body.torso); Assert.IsNotNull(body.legL); Assert.IsNotNull(body.legR);
            Assert.IsNotNull(body.kneeL); Assert.IsNotNull(body.kneeR);
            Assert.IsNotNull(body.kneeL.Find("Boot"), "no left boot.");
            Assert.IsNotNull(body.kneeR.Find("Boot"), "no right boot.");

            var renderers = body.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 6, "hips, chest and two legs of three segments each expected.");
            foreach (var r in renderers)
            {
                // Rest-pose extents in the prefab's own frame. Nothing above 1.35: the lens is at 1.60
                // with a 0.03 near clip and at -89 deg pitch anything higher is inside the camera.
                var b = r.bounds;
                Assert.LessOrEqual(b.max.y, 1.35f + 1e-3f, r.name + " reaches y " + b.max.y.ToString("0.00"));
                Assert.GreaterOrEqual(b.min.y, -0.06f, r.name + " is under the floor at y " + b.min.y.ToString("0.00"));
                Assert.LessOrEqual(Mathf.Max(Mathf.Abs(b.max.x), Mathf.Abs(b.min.x)), 0.40f + 1e-3f,
                    r.name + " pokes outside the 0.40 m capsule.");
                Assert.IsFalse(r.receiveShadows, r.name + " receives shadows; a floor shadow would paint the boots black.");
                // Legs are drawn; the torso is a SHADOW PROXY only (ShadowsOnly): a chest block under the
                // lens fills the frame the moment you look down, and sat in front of the legs on a slide.
                bool torsoPart = r.transform.IsChildOf(body.torso);
                Assert.AreEqual(torsoPart ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                                          : UnityEngine.Rendering.ShadowCastingMode.On, r.shadowCastingMode,
                    r.name + (torsoPart ? " is torso and must be shadow-only; it blocks the first-person view."
                                        : " casts no shadow; the legs are the player's shadow proxy."));
                Assert.AreEqual(Layers.Player, r.gameObject.layer, r.name + " is not on the Player layer.");
                Assert.IsNull(r.GetComponent<Collider>(), r.name + " carries a collider.");
                StringAssert.StartsWith("Universal Render Pipeline/", r.sharedMaterial.shader.name,
                    r.name + ": a non-URP shader renders magenta.");
            }
        }

        [Test]
        public void TheSlidePosePutsTheLeadingBootInFrame_AndTheKneeAtItsEdge()
        {
            // The request: "make it so I can see my feet when sliding". Forward kinematics of the shipped
            // slide pose, seen from the slide eye height through the shipped FOV. No scene needed.
            var body = ShippedBody();

            Vector3 hip = new Vector3(0f, body.hipHeight - body.slideHipSink, body.slideHipForward);
            Vector3 ankle = hip + PlayerBody.AnkleFromHip(body.slideLeadHip, body.slideLeadKnee,
                                                          body.thighLength, body.shinLength);
            // The boot cube extends ~0.19 m past the ankle along the shin.
            Vector3 bootTip = ankle + new Vector3(0f, 0.02f, 0.19f);

            float bootDeg = PlayerBody.DegreesBelowHorizon(SlideEyeHeight, bootTip.z, bootTip.y);
            Assert.Less(bootDeg, HalfFovDeg - 3f,
                "the leading boot sits " + bootDeg.ToString("0.0") + " deg below the horizon; the frame ends at " +
                HalfFovDeg + " — you would not see your feet.");
            Assert.Greater(bootDeg, 20f,
                "the boot is only " + bootDeg.ToString("0.0") + " deg down: that is in your face, not on the floor.");
            Assert.GreaterOrEqual(ankle.y, -0.05f, "the ankle is through the floor (" + ankle.y.ToString("0.00") + ").");
            Assert.Greater(bootTip.z, 0.9f, "the boot is not out in front (" + bootTip.z.ToString("0.00") + " m).");

            // The trailing leg is TIGHTER, so the pair reads as two legs, and still in the lower frame.
            Assert.Greater(body.slideLeadHip, body.slideTrailHip, "the trailing leg should be tucked more than the lead.");
            Vector3 ankleT = hip + PlayerBody.AnkleFromHip(body.slideTrailHip, body.slideTrailKnee,
                                                           body.thighLength, body.shinLength);
            float trailDeg = PlayerBody.DegreesBelowHorizon(SlideEyeHeight, ankleT.z + 0.19f, ankleT.y + 0.02f);
            Assert.Less(trailDeg, HalfFovDeg + 2f, "the trailing boot is well out of frame.");

            // The throw is a real throw: under-damped, and capped so it cannot fling past the pose.
            Assert.Less(body.slideBlendDamping, 1f, "the legs are placed, not thrown.");
            Assert.That(body.slideBlendMax, Is.InRange(1.05f, 1.3f));
            float reach = 1f / (body.slideBlendHz * 2f * Mathf.Sqrt(1f - body.slideBlendDamping * body.slideBlendDamping));
            Assert.That(reach, Is.InRange(0.08f, 0.16f),
                "time to the pose is " + reach.ToString("0.000") + " s; the brief is a 0.10-0.14 s throw.");
        }

        [Test]
        public void TheGaitAndTheHandsStrideTogether()
        {
            // WeaponViewmodel.bobT advances PlayerDelta * speed * 1.3; the legs must use the same phase
            // rate or the feet and the hands drift in and out of step as you run.
            var body = ShippedBody();
            Assert.AreEqual(1.3f, body.gaitPhasePerMetre, 1e-4f);
            Assert.AreEqual(9f, body.landingRecoverySpeed, 1e-4f, "the knee dip and the camera dip should spring back together.");
        }

        // ---------------------------------------------------------------- the asset

        [Test]
        public void TheAssetCarriesTheBodyPassValues()
        {
            var feel = Shipped();
            // Grep the YAML for the new key FIRST: a key missing from the file deserialises to the C#
            // initialiser and every value assert below would pass on the un-regenerated asset.
            string yaml = System.IO.File.ReadAllText("Assets/Data/GameFeel.asset");
            if (!yaml.Contains("slideRumble:") || !yaml.Contains("slideCrouchHz:"))
                Assert.Ignore("GameFeel.asset predates the body pass (no slideRumble key in the YAML) — run " +
                              "VibeGame1/3. Create Data, then re-run. Until then these values are code defaults.");

            Assert.That(feel.slideRumble, Is.InRange(0.002f, 0.010f),
                "rumble " + feel.slideRumble + " m: under 2 mm is invisible, over 1 cm is a shake, not texture.");
            Assert.That(feel.slideCrouchHz, Is.InRange(3f, 8f), "the eye should plop in about a tenth of a second.");
            Assert.That(feel.slideCrouchDamping, Is.InRange(0.4f, 0.8f), "the plop needs a small overshoot, not a bounce.");
            float over = SlideImpulse.OvershootFraction(feel.slideCrouchDamping) * 0.55f;
            Assert.That(over, Is.InRange(0.03f, 0.10f),
                "the eye overshoots the slide height by " + over.ToString("0.000") + " m; the brief is ~0.06.");
            Assert.GreaterOrEqual(feel.slideKickPitch, 1.5f, "the commit dip should answer the legs being thrown out.");
        }
    }
}
