using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE UNBLOCKABLE TELL'S GEOMETRY, pinned to the shipped prefab (hard rule 9).
    ///
    /// <para>The backlog carried "the alert tell may want to be bigger, not brighter — past peak 3.0 you
    /// only buy white; the next lever is the 0.25 m cube's size". It was measured rather than reasoned
    /// about, by rendering the shipped grunt through the shipped volume profile at six distances and
    /// five cube sizes and DIFFING the frame with the tell on against the frame with it off (the diff
    /// counts the bloom halo, which is most of what the tell actually is). At 1920x1080, FOV 95:</para>
    ///
    /// <code>
    ///  dist   size   changed px   % frame   bbox px     gap to top edge
    ///   3.0   0.25         2734    0.132%   47 x 60          285 px
    ///   3.0   0.40         7163    0.345%   79 x 99          264 px
    ///   2.0   0.25         8376    0.404%   78 x 115         125 px
    ///   2.0   0.40        22555    1.088%  130 x 186          83 px
    ///   1.5   0.25        12591    0.607%  114 x 120           0 px   <- CLIPPED
    ///   6.0   0.25          558    0.027%   22 x 26          419 px
    /// </code>
    ///
    /// <para><b>The conclusion was to leave the size alone.</b> Two measured reasons. First, size is not
    /// the scarce thing at the distance the tell is read from: at the grunt's 3 m preferredRange the
    /// 0.25 m cube already renders 60 px tall with its bloom, which is 5.6% of the frame height — the
    /// same on-screen height as the deathblow mark's 0.115 rad angular size (57 px). A pre-render
    /// derivation said the tell was only 0.69x the mark; the bloom halo, which that derivation ignored,
    /// closes the gap entirely. Second, growth is spent at the wrong end: the tell is 2.5 m up, so it
    /// climbs toward the top of the frame as the enemy closes, and at 1.5 m it is ALREADY clipped by the
    /// top edge (gap 0 px, and zero pixels above 90% peak — the saturated core is off-screen and only
    /// the skirt of the halo is left). A bigger cube reaches that edge sooner: at 2 m the gap falls from
    /// 125 px to 83 px going from 0.25 to 0.40.</para>
    ///
    /// <para>So these assertions are not "0.25 is optimal". They are a tripwire: the numbers above were
    /// measured against exactly this cube at exactly this height, and changing either invalidates them.
    /// If you change one, re-measure before you believe the result.</para>
    /// </summary>
    public class AlertTellFramingTests
    {
        const string GruntPrefab = "Assets/Prefabs/Enemy_Grunt.prefab";
        const string GruntData = "Assets/Data/Enemies/Grunt.asset";

        // The shipped values the render above was taken against.
        const float TellSize = 0.25f;
        const float TellHeight = 2.5f;

        // PrefabFactory: cam.fieldOfView = 95 (vertical). PlayerLook's eye is at 1.6.
        const float FovV = 95f;
        const float EyeHeight = 1.6f;
        // The player's aim rests on the enemy's centre of mass; the lock-on assist targets the torso.
        const float AimHeight = 1.2f;
        // The closest the tell still has to be readable at. The grunt's attacks reach 2.6-3.0 m and
        // lunge 0.7-1.15 m into that, so the tell is still up while the enemy is this close.
        const float CloseRange = 1.5f;

        static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(GruntPrefab);

        static GameObject Marker()
        {
            var p = Prefab();
            if (p == null) return null;
            var vis = p.GetComponentInChildren<EnemyVisuals>(true);
            return vis != null ? vis.alertMarker : null;
        }

        [Test]
        public void TheTellIsShippedAndWiredToItsOwnMaterial()
        {
            Assert.IsNotNull(Prefab(), GruntPrefab + " missing — run VibeGame1/4. Build Prefabs.");
            var m = Marker();
            Assert.IsNotNull(m, "EnemyVisuals.alertMarker is not wired on the grunt — every unblockable " +
                "attack would raise nothing, and the one signal that means 'steel will not answer this' " +
                "is simply absent with no error.");
            Assert.IsFalse(m.activeSelf, "the alert marker ships ACTIVE — it would be up permanently, " +
                "which is the same as it never being up at all.");
            var r = m.GetComponent<Renderer>();
            Assert.IsNotNull(r, "the alert marker has no Renderer.");
            Assert.IsTrue(r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("M_AlertTell"),
                "the alert marker's material is " +
                (r.sharedMaterial != null ? r.sharedMaterial.name : "null") +
                " — a navigational trim key here means the tell and the level trims have been merged " +
                "again, and the trims are held under the ACES desaturation ceiling where the tell " +
                "cannot bloom.");
        }

        [Test]
        public void TheCubeIsStillTheSizeTheRenderWasMeasuredAgainst()
        {
            var m = Marker();
            Assert.IsNotNull(m);
            Vector3 s = m.transform.localScale;
            Assert.AreEqual(TellSize, s.x, 0.001f, MeasurementWarning("size", s.x, TellSize));
            Assert.AreEqual(s.x, s.y, 0.001f, "the tell is no longer a cube — it was measured as one.");
            Assert.AreEqual(s.x, s.z, 0.001f, "the tell is no longer a cube — it was measured as one.");
            Assert.AreEqual(TellHeight, m.transform.localPosition.y, 0.001f,
                MeasurementWarning("height", m.transform.localPosition.y, TellHeight));
        }

        [Test]
        public void TheTellsCentreSurvivesTheClosestRangeItIsReadAt()
        {
            // The failure mode the render actually found. The tell rides 2.5 m up while the camera sits
            // at 1.6 and looks DOWN at the torso, so the angle from the view axis to the tell grows as
            // the enemy closes. Past ~1.37 m the centre of the cube leaves the top of the frame
            // entirely. This asserts the centre still clears the edge at CloseRange; the TOP of the cube
            // is already clipped there, which is measured and accepted, not a regression.
            var m = Marker();
            Assert.IsNotNull(m);
            float y = m.transform.localPosition.y;

            float toTell = Mathf.Atan2(y - EyeHeight, CloseRange) * Mathf.Rad2Deg;
            float viewPitch = Mathf.Atan2(EyeHeight - AimHeight, CloseRange) * Mathf.Rad2Deg;
            float fromAxis = toTell + viewPitch;
            float halfFov = FovV * 0.5f;

            Assert.Less(fromAxis, halfFov,
                "at " + CloseRange + " m the tell's centre sits " + fromAxis.ToString("0.0") +
                " deg off the view axis, past the " + halfFov.ToString("0.0") +
                " deg half-FOV — it is off the top of the screen at the moment the enemy is closest and " +
                "the warning matters most. Raising the marker makes this worse, not better; the lever " +
                "is to LOWER it (or to give it the angular sizing DeathblowMarker uses).");
        }

        [Test]
        public void GrowingTheTellWouldPushItThroughThePostureBar()
        {
            // The other reason size is not free: the airspace above the head is already occupied. The
            // world posture bar is built at local y 2.6 (PrefabFactory.BuildPostureBar) and the tell's
            // centre is at 2.5, so a 0.25 cube's top edge is already 0.025 m past it. This is the
            // documented budget, asserted so a size bump has to confront it.
            var p = Prefab();
            Assert.IsNotNull(p);
            var bar = p.GetComponentInChildren<EnemyPostureBar>(true);
            Assert.IsNotNull(bar, "the grunt has no world posture bar — the height budget below is " +
                "measured against it, so this test no longer means what it says.");

            var m = Marker();
            float tellTop = m.transform.localPosition.y + m.transform.localScale.y * 0.5f;
            float barY = bar.transform.localPosition.y;

            Assert.Less(tellTop - barY, 0.10f,
                "the tell's top edge is at " + tellTop.ToString("0.###") + " and the posture bar sits at " +
                barY.ToString("0.###") + " — " + (tellTop - barY).ToString("0.###") +
                " m of overlap. Two combat readouts occupying the same band above the head is exactly " +
                "the confusion DeathblowMarker moved the deathblow glyph to the sternum to avoid. If the " +
                "tell has to grow, it has to move down at the same time.");
        }

        static string MeasurementWarning(string what, float actual, float measured)
        {
            return "the alert tell's " + what + " is " + actual + ", not the " + measured +
                   " every number in this class's summary was rendered against. That may well be the " +
                   "right call — but the footprint table is now fiction. Re-measure (render the shipped " +
                   "grunt through SampleSceneProfile at 1.5-6 m and diff tell-on against tell-off), " +
                   "update the table, then update this constant.";
        }
    }
}
