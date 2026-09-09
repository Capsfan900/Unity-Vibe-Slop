using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE LOOK OF A MELEE HIT, pinned as arithmetic — the impact confirm (<see cref="WeaponImpactFx"/>)
    /// and the swing ribbon's weight curve (<see cref="WeaponTrail"/>).
    ///
    /// <para><b>The regression this file exists to stop.</b> The hit confirm used to be built inline in
    /// <c>WeaponController.SpawnSpark</c>: an unpooled lit sphere at <c>WeaponData.neon * 4</c>. The
    /// hammer's hue peaks at 0.878, so a routine chip hit rendered at <b>3.51</b> — above the alert tell
    /// (3.00), the deathblow mark (2.60) and the deflect's own body flash (3.20,
    /// <c>EnemyVisuals.ParryGlow</c>). Every other impact in the game goes through <see cref="SlashFx"/>,
    /// which normalises whatever colour it is handed to a peak channel of exactly 1.0 — so the ONE effect
    /// that skipped it was also the loudest thing on the screen, and the loudness hierarchy meant
    /// nothing. Rule 9 of docs/ANIMATION-VFX.md allows exactly two bloom exceptions and this was never
    /// one of them.</para>
    ///
    /// <para>Rule 9 (the project's, not the doc's): every number below is read off the shipped
    /// <c>.asset</c> files, never off a C# default, so a retuned weapon retunes its impact with it.</para>
    /// </summary>
    public class WeaponImpactVfxTests
    {
        const string Dagger = "Dagger";
        const string Sword = "Sword";
        const string Hammer = "Hammer";

        /// <summary><c>EnemyVisuals.cs:89</c> — the deflect's body flash, the loudest moment in the game
        /// and the one this must never approach. Restated here because the field is private; if it moves,
        /// this assertion is where a weapon pass should find out.</summary>
        const float DeflectBodyPeak = 3.2f;
        /// <summary><c>MaterialFactory</c>'s <c>M_AlertTell</c> — the unblockable alarm.</summary>
        const float AlertTellPeak = 3.0f;

        static WeaponData W(string n)
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Data/Weapons/" + n + ".asset");
            Assert.IsNotNull(w, n + ".asset missing — run VibeGame1/3. Create Data");
            return w;
        }

        static float Peak(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

        /// <summary>Rec.709 luminance of an sRGB colour in LINEAR space — the units every albedo
        /// decision in MaterialFactory is argued in.</summary>
        static float LinearLuminance(Color srgb)
        {
            Color l = srgb.linear;
            return 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b;
        }

        // ---------------------------------------------------------------- the bloom budget

        [Test]
        public void TheHitConfirmCannotBloom_AndIsQuieterThanEveryAlarm()
        {
            Assert.AreEqual(1.0f, WeaponImpactFx.PeakChannel, 1e-4f,
                "the hit confirm is drawn only through SlashFx, which normalises to exactly 1.0");
            Assert.Less(WeaponImpactFx.PeakChannel, 1.05f,
                "1.05 is the scene's bloom threshold — a hit you LANDED must not bloom; " +
                "a hit you must REACT to (the tell, the cue, the deflect) is what blooms");
            Assert.Less(WeaponImpactFx.PeakChannel, DeflectBodyPeak / 3f,
                "the deflect must stay at least 3x louder than an ordinary hit, or the two payoffs " +
                "read as the same event");
            Assert.Less(WeaponImpactFx.PeakChannel, AlertTellPeak);

            // The structural half: whatever hue is handed in, SlashFx CAPS its peak at 1.0. It is a
            // ceiling, not a normalisation - Normalise() divides only when the peak exceeds 1 and leaves
            // a dimmer hue alone, which is why Rosethorn's #5FD66A correctly stays at 0.839 rather than
            // being pushed up to full green. The guarantee that matters is that no weapon can ever hand
            // the hit confirm a colour that blooms; asserting equality with 1.0 asserted the opposite
            // thing and would have failed the moment any weapon was given a hue below full saturation.
            foreach (var n in new[] { Dagger, Sword, Hammer })
                Assert.LessOrEqual(Peak(SlashFx.NormaliseColor(W(n).neon)), 1f + 1e-3f,
                    n + "'s hue is not capped at 1.0 - SlashFx is no longer the only writer");
        }

        // ---------------------------------------------------------------- weight reads before a stat

        [Test]
        public void TheMassCurve_RanksTheRosterByCommitment()
        {
            float d = WeaponImpactFx.Mass(W(Dagger));
            float s = WeaponImpactFx.Mass(W(Sword));
            float h = WeaponImpactFx.Mass(W(Hammer));
            Assert.AreEqual(0f, d, 1e-3f, "the needle is the bottom of the ladder");
            Assert.AreEqual(1f, h, 1e-3f, "the maul is the top of the ladder");
            Assert.Less(d, s); Assert.Less(s, h);
            Assert.Less(s, 0.5f,
                "the sword sits nearer the dagger than the maul — a 0.44 s generalist is not a heavy");
        }

        [Test]
        public void TheImpact_GrowsWithTheWeapon_AndTheDebrisSlowsDown()
        {
            WeaponData d = W(Dagger), s = W(Sword), h = W(Hammer);

            // Size and debris count climb: bulk with exposed mass reads as heavy before any stat does.
            Assert.Less(WeaponImpactFx.FlareSize(d, false), WeaponImpactFx.FlareSize(s, false));
            Assert.Less(WeaponImpactFx.FlareSize(s, false), WeaponImpactFx.FlareSize(h, false));
            Assert.Less(WeaponImpactFx.SparkCount(d, false), WeaponImpactFx.SparkCount(h, false));
            Assert.Less(WeaponImpactFx.SparkSpread(d), WeaponImpactFx.SparkSpread(h),
                "a needle punctures into a tight cone; a maul sprays");

            // And the debris SLOWS. SlashFx pulls sparks at -16 m/s², so a slower spark falls in a
            // visibly heavier arc — this inversion is the whole weight cue and must never be "fixed".
            Assert.Greater(WeaponImpactFx.SparkSpeed(d), WeaponImpactFx.SparkSpeed(h),
                "heavy debris must travel SLOWER so gravity bends it — see WeaponImpactFx.SparkSpeedHeavy");

            // The flare stays punctuation at both ends: never a lingering glow over the enemy.
            Assert.LessOrEqual(WeaponImpactFx.FlareSeconds(h), 0.16f);
            Assert.LessOrEqual(WeaponImpactFx.FlareSize(h, true), 0.55f,
                "even the maul's finisher is a contact flash, not an explosion");
            // EnemyVisuals fires a 0.5 m flare on a deflect. No ROUTINE hit may be the biggest flash in
            // a fight; the maul's extra weight is carried by the ring and the debris instead.
            Assert.Less(WeaponImpactFx.FlareSize(h, false), 0.5f,
                "a plain maul hit is now flashing bigger than a deflect");
        }

        [Test]
        public void TheShockwave_IsTheMaulsAlone()
        {
            Assert.IsTrue(WeaponImpactFx.HasShockwave(W(Hammer)),
                "the maul is the one melee hit that earns a ring — Sekiro's own split is sparks for a " +
                "block, sparks AND a shockwave for the big one");
            Assert.IsFalse(WeaponImpactFx.HasShockwave(W(Sword)));
            Assert.IsFalse(WeaponImpactFx.HasShockwave(W(Dagger)));
            Assert.Greater(WeaponImpactFx.ShockwaveRadius, 0.05f,
                "standing rule 4: under ~0.05 m a line is sub-pixel at combat distance");
            Assert.Greater(WeaponImpactFx.ShockwaveSeconds, WeaponImpactFx.FlareSeconds(W(Hammer)),
                "the wave must outlive the flash, or the ring reads as part of the bang");
        }

        [Test]
        public void TheFinisher_IsMarkedButIsNotASecondAlarm()
        {
            WeaponData s = W(Sword);
            Assert.Greater(WeaponImpactFx.FlareSize(s, true), WeaponImpactFx.FlareSize(s, false));
            Assert.Less(WeaponImpactFx.FlareSize(s, true), WeaponImpactFx.FlareSize(W(Hammer), false),
                "a sword finisher must not out-read a plain maul hit — weapon identity outranks combo step");
            Assert.Less(WeaponImpactFx.FinisherScale, 1.5f);
            Assert.Less(WeaponImpactFx.FlareSize(W(Hammer), true), 0.5f,
                "the maul finisher must also stay below the deflect's contact flare");
        }

        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        static SlashFx EffectAt(Vector3 point)
        {
            foreach (var fx in Resources.FindObjectsOfTypeAll<SlashFx>())
                if (fx.gameObject.activeSelf && fx.transform.position == point) return fx;
            Assert.Fail("effect was not spawned");
            return null;
        }

        static void DisposeEffect(SlashFx fx)
        {
            if (fx == null) return;
            // EditMode cleanup owns the transient materials; the runtime owner uses deferred Destroy.
            foreach (string field in new[] { "coreMat", "fringeMat" })
            {
                var info = typeof(SlashFx).GetField(field, Hidden);
                var material = (Material)info.GetValue(fx);
                info.SetValue(fx, null);
                Object.DestroyImmediate(material);
            }
            Object.DestroyImmediate(fx.gameObject);
        }

        [Test]
        public void AStaleLiveCounterCannotPermanentlySilenceThePool()
        {
            var liveField = typeof(SlashFx).GetField("live", BindingFlags.Static | BindingFlags.NonPublic);
            liveField.SetValue(null, 28);
            Vector3 point = new Vector3(777f, 888f, 999f);
            SlashFx.Flare(point, Color.cyan, 0.2f, 0.1f);
            var fx = EffectAt(point);
            try
            {
                Assert.AreEqual(1, liveField.GetValue(null),
                    "cap recovery must recount the one genuinely active effect, not retain stale debt");
            }
            finally { DisposeEffect(fx); }
        }

        [TestCase("Sparks")]
        [TestCase("Arc")]
        [TestCase("Ring")]
        [TestCase("Flare")]
        [TestCase("Beam")]
        public void PooledPrimitives_ReplaceOldGeometryOnTheSpawnFrame(string kind)
        {
            Vector3 oldPoint = new Vector3(321f, 654f, 987f);
            Vector3 point = new Vector3(-321f, -654f, -987f);
            SlashFx.Sparks(oldPoint, Vector3.up, Color.cyan, 24, 3f, 25f);
            var fx = EffectAt(oldPoint);
            try
            {
                var lines = fx.GetComponentsInChildren<LineRenderer>(true);
                Assert.AreEqual(48, lines.Length, "the bounded 24-spark reserve has two lines per spark");
                var coreMaterial = (Material)typeof(SlashFx).GetField("coreMat", Hidden).GetValue(fx);
                typeof(SlashFx).GetMethod("Retire", Hidden).Invoke(fx, null);
                switch (kind)
                {
                    case "Sparks": SlashFx.Sparks(point, Vector3.up, Color.green, 7, 3f, 25f); break;
                    case "Arc": SlashFx.Arc(point, Vector3.forward, Color.green, 0.4f, 90f, 0.1f); break;
                    case "Ring": SlashFx.Ring(point, Vector3.forward, Color.green, 0.4f, 0.1f); break;
                    case "Flare": SlashFx.Flare(point, Color.green, 0.4f, 0.1f); break;
                    case "Beam": SlashFx.Beam(point, point + Vector3.right, Color.green, 0.02f, 0.1f); break;
                }
                Assert.AreSame(fx, EffectAt(point));
                Assert.AreEqual(48, fx.GetComponentsInChildren<LineRenderer>(true).Length,
                    "a warmed reserve must reuse its renderers");
                Assert.AreSame(coreMaterial, (Material)typeof(SlashFx).GetField("coreMat", Hidden).GetValue(fx),
                    "renting a different primitive must reuse its material");
                int active = 0;
                foreach (var line in lines)
                {
                    if (!line.gameObject.activeSelf) continue;
                    active++;
                    for (int i = 0; i < line.positionCount; i++)
                        Assert.Less(Vector3.Distance(point, line.GetPosition(i)), 1.1f,
                            kind + " rendered a previous effect's position before its first Update");
                }
                Assert.AreEqual(kind == "Sparks" ? 14 : 2, active,
                    "inactive reserve slots must stay hidden; shape fixes must not add draw calls");
            }
            finally { DisposeEffect(fx); }
        }

        [Test]
        public void ContactGlint_IsAnImmediateFourPointStar_WithNoCrossingConnectors()
        {
            Vector3 point = new Vector3(111f, 222f, 333f);
            SlashFx.Flare(point, Color.cyan, 0.4f, 0.1f);
            var fx = EffectAt(point);
            try
            {
                var lines = fx.GetComponentsInChildren<LineRenderer>();
                Assert.AreEqual(2, lines.Length);
                var line = lines[0];
                Assert.IsTrue(line.loop, "a glint traces one continuous star perimeter");
                Assert.AreEqual(8, line.positionCount);
                Assert.AreEqual(0.4f, Vector3.Distance(point, line.GetPosition(0)), 1e-4f,
                    "the contact frame is full size, without waiting for Update");
                Vector3 planeNormal = Vector3.Cross(line.GetPosition(0) - point, line.GetPosition(2) - point).normalized;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 a = line.GetPosition(i) - point;
                    Vector3 b = line.GetPosition((i + 1) % 8) - point;
                    Assert.Greater(Vector3.Dot(Vector3.Cross(a, b), planeNormal), 0f,
                        "the perimeter must advance around the centre instead of crossing the glint");
                    if (i % 2 == 1) Assert.Less(a.magnitude, 0.05f, "spikes taper to a narrow waist");
                }
                typeof(SlashFx).GetMethod("UpdateFlare", Hidden).Invoke(fx, new object[] { 0.5f });
                Assert.AreEqual(0.2f, Vector3.Distance(point, line.GetPosition(0)), 1e-4f,
                    "a contact flash dissipates after its immediate peak");
            }
            finally { DisposeEffect(fx); }
        }

        // ---------------------------------------------------------------- the ribbon carries it too

        [Test]
        public void TheRibbon_CarriesWeight_AndTheSwordReferenceDoesNotMove()
        {
            float md = WeaponImpactFx.Mass(W(Dagger));
            float ms = WeaponImpactFx.Mass(W(Sword));
            float mh = WeaponImpactFx.Mass(W(Hammer));

            // The sword is the reference the whole set is compared against, and PrefabFactory's
            // serialised 0.030 m / 0.11 s were tuned on it. The mass curve must leave it where it is.
            Assert.AreEqual(1f, WeaponTrail.WidthScale(ms), 0.02f,
                "the sword's ribbon width moved — the reference is supposed to be unchanged");
            Assert.AreEqual(1f, WeaponTrail.FadeScale(ms), 0.03f,
                "the sword's ribbon fade moved — the reference is supposed to be unchanged");

            Assert.Less(WeaponTrail.WidthScale(md), WeaponTrail.WidthScale(ms));
            Assert.Less(WeaponTrail.WidthScale(ms), WeaponTrail.WidthScale(mh));
            Assert.Less(WeaponTrail.FadeScale(md), WeaponTrail.FadeScale(mh));

            // FeatureTests' Trail_WidthCapped reads the AUTHORED 0.030 (limit 0.05). The maul's drawn
            // width is the authored value times the curve, so the cap has to be restated on the product.
            const float Authored = 0.030f;
            Assert.LessOrEqual(Authored * WeaponTrail.WidthScale(mh), 0.055f,
                "the maul's ribbon is becoming a banner across the frame, not a blade edge");
        }

        [Test]
        public void TheRibbon_DiesInsideTheSwingItBelongsTo()
        {
            // The ribbon is the player's read on "the hitbox is live". It must not still be on screen
            // when the weapon is ready to swing again, or it stops meaning that. The post-strike leg is
            // WeaponViewmodel's follow-through hold plus the recovery lerp; the arithmetic is restated
            // here from the shipped data because that is what the fade is racing.
            const float Authored = 0.11f;      // PrefabFactory's serialised fadeSeconds
            const float FollowThroughHold = 0.07f;
            foreach (var n in new[] { Dagger, Sword, Hammer })
            {
                var w = W(n);
                float swing = Mathf.Clamp(w.attackDuration * 0.2f, 0.04f,
                                          Mathf.Max(0.04f, w.attackDuration - w.hitDelay));
                float rest = Mathf.Max(0.01f, w.attackDuration - w.hitDelay - swing);
                float hold = Mathf.Min(FollowThroughHold, rest * 0.45f);
                float post = hold + Mathf.Max(0.01f, rest - hold);

                float fade = Authored * WeaponTrail.FadeScale(WeaponImpactFx.Mass(w));
                Assert.Less(fade, post,
                    n + "'s ribbon fades over " + fade.ToString("0.000") + " s but the whole leg after " +
                    "the strike is only " + post.ToString("0.000") + " s — the trail outlives the swing");
                Assert.Greater(fade, 0.05f,
                    n + "'s ribbon dies inside 3 frames at 60 Hz — the eye never lands on it");
            }
        }

        // ---------------------------------------------------------------- the weapon has a body

        [Test]
        public void TheWeaponsMass_IsVisible_AndSitsAboveTheGloveHoldingIt()
        {
            var core = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_WeaponCore.mat");
            var glove = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Gauntlet.mat");
            var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Stone.mat");
            Assert.IsNotNull(core, "M_WeaponCore.mat missing — run VibeGame1/2. Create Materials");
            Assert.IsNotNull(glove, "M_Gauntlet.mat missing — run VibeGame1/2. Create Materials");
            Assert.IsNotNull(stone, "M_Stone.mat missing — run VibeGame1/2. Create Materials");

            float c = LinearLuminance(core.GetColor("_BaseColor"));
            float g = LinearLuminance(glove.GetColor("_BaseColor"));
            float s = LinearLuminance(stone.GetColor("_BaseColor"));

            Assert.Greater(c, g * 2f,
                "the weapon's guard, grip, pommel and head are DARKER than the glove gripping them (" +
                c.ToString("0.0000") + " vs " + g.ToString("0.0000") + ") — MaterialFactory states the " +
                "opposite hierarchy. Run VibeGame1/2. Create Materials if this is stale");
            Assert.Less(c, s,
                "the weapon's mass is brighter than a stone wall — it is an object, not a light source");
            Assert.Less(Peak(core.GetColor("_EmissionColor")), 0.01f,
                "the mass parts must never emit: the hot channel on a weapon belongs to the blade " +
                "(M_Energy via EnergyGlow) and to the Pyre embers");
            Assert.Greater(core.GetFloat("_Smoothness"), 0.1f,
                "a matte near-black bevel has no channel left to describe curvature on a backlit course");
        }
    }
}
