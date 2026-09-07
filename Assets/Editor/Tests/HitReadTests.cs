using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The HIT-READ pass, 2026-09-07. Two independent claims, pinned against the SHIPPED numbers (rule 9):
    ///
    /// <para><b>1. A hit's flash scales with damage/maxHP, not with a fixed pop.</b>
    /// <see cref="EnemyVisuals.HitReactionFor"/> is the pure, static half of
    /// <see cref="EnemyVisuals"/>'s hit reaction -- deliberately factored out (same pattern as
    /// <see cref="PuppetVisuals.ResolveRate(float,float,float)"/>) so the mapping is testable with no
    /// instance, no scene and no spawned enemy.</para>
    ///
    /// <para><b>2. The Sunbreaker no longer shares the enemy bolt's hue.</b> Read straight off the shipped
    /// <c>Assets/Data/Weapons/Hammer.asset</c> and <see cref="Projectile.HotCore"/>, exactly the way
    /// <c>WandDataTests.EveryWandColour_IsTellableFromEveryOther</c> already holds the wand set to a
    /// >= 45 deg hue floor.</para>
    /// </summary>
    public class HitReadTests
    {
        // ---------------------------------------------------------------------------------------
        // 1. Damage-scaled hit flash
        // ---------------------------------------------------------------------------------------

        [Test]
        public void AChip_GetsTheFloorReaction()
        {
            // At/under 2% of max HP -- a dagger jab on anything but a very low-HP grunt -- the reaction
            // is the OLD fixed pop exactly, so a block (which carries no fraction at all) is unchanged.
            float boost, decay;
            EnemyVisuals.HitReactionFor(0f, out boost, out decay);
            Assert.AreEqual(EnemyVisuals.HitFlashFloorBoost, boost, 0.0001f);
            Assert.AreEqual(EnemyVisuals.HitFlashFloorDecay, decay, 0.0001f);

            EnemyVisuals.HitReactionFor(EnemyVisuals.HitFlashFractionFloor, out boost, out decay);
            Assert.AreEqual(EnemyVisuals.HitFlashFloorBoost, boost, 0.0001f,
                "a hit at exactly the fraction floor must still read as the floor reaction, or the floor " +
                "is not actually a floor.");
        }

        [Test]
        public void AFinisherScaleHit_GetsTheCeilingReaction()
        {
            // At/over 30% of max HP -- a Sunbreaker finisher on a grunt, or any hit near a boss phase
            // break -- the reaction maxes out. Past the ceiling fraction, MORE damage buys no more pop:
            // there is nothing brighter than white on this channel (see WriteBody's Color.Lerp clamp).
            float boost, decay;
            EnemyVisuals.HitReactionFor(EnemyVisuals.HitFlashFractionCeiling, out boost, out decay);
            Assert.AreEqual(EnemyVisuals.HitFlashCeilingBoost, boost, 0.0001f);
            Assert.AreEqual(EnemyVisuals.HitFlashCeilingDecay, decay, 0.0001f);

            EnemyVisuals.HitReactionFor(1f, out boost, out decay);
            Assert.AreEqual(EnemyVisuals.HitFlashCeilingBoost, boost, 0.0001f,
                "a one-shot execute (damage far past max HP) must clamp to the ceiling, not exceed it.");
            Assert.AreEqual(EnemyVisuals.HitFlashCeilingDecay, decay, 0.0001f);
        }

        [Test]
        public void TheMappingIsMonotonic_HarderHitsNeverReadSofter()
        {
            // A bigger fraction of max HP must never produce a weaker pop or a shorter linger than a
            // smaller one -- otherwise a heavier hit could visibly read as LESS important than a lighter
            // one, which is the exact bug this pass exists to fix.
            float[] fractions = { 0f, 0.02f, 0.05f, 0.10f, 0.16f, 0.22f, 0.30f, 0.60f, 1f };
            float lastBoost = -1f, lastDecay = float.MaxValue;
            foreach (var f in fractions)
            {
                float boost, decay;
                EnemyVisuals.HitReactionFor(f, out boost, out decay);
                Assert.GreaterOrEqual(boost, lastBoost - 0.0001f, "boost dipped going from fraction " + f);
                Assert.LessOrEqual(decay, lastDecay + 0.0001f,
                    "decay rate rose (a shorter linger) going from fraction " + f + " -- a bigger hit must " +
                    "never clear FASTER than a smaller one.");
                lastBoost = boost; lastDecay = decay;
            }
        }

        [Test]
        public void TheFloorRegisters_AndTheCeilingVisiblyLingers()
        {
            // Duration on screen = boost / decayRate (Update decays tintBoost linearly at decayRate/s).
            // The floor must clear the "not sub-pixel" bar the rest of this project's VFX audit uses --
            // a handful of frames at 60 fps, not one -- and the ceiling must be materially longer, or the
            // "duration" axis of this pass is decorative rather than real.
            float fb, fd, cb, cd;
            EnemyVisuals.HitReactionFor(0f, out fb, out fd);
            EnemyVisuals.HitReactionFor(1f, out cb, out cd);
            float floorSeconds = fb / fd;
            float ceilingSeconds = cb / cd;
            Assert.Greater(floorSeconds, 0.08f, "the floor hit-flash (" + floorSeconds.ToString("0.###") +
                " s) is under ~5 frames at 60 fps -- a chip that reads as nothing is worse than one that " +
                "reads as generic, and this is the number the whole pass exists to keep off the floor.");
            Assert.Greater(ceilingSeconds, floorSeconds * 2f,
                "a finisher-scale hit (" + ceilingSeconds.ToString("0.###") + " s) does not visibly outlast " +
                "a chip (" + floorSeconds.ToString("0.###") + " s) by at least 2x -- the duration axis is " +
                "not doing any real work.");
        }

        // ---------------------------------------------------------------------------------------
        // 2. The Sunbreaker clears the bolt's hue
        // ---------------------------------------------------------------------------------------

        const string HammerPath = "Assets/Data/Weapons/Hammer.asset";
        const float MinHueDegFromBolt = 45f;   // WandDataTests' own floor, applied to the same collision

        static float Peak(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

        static float Hue(Color c)
        {
            float p = Mathf.Max(0.0001f, Peak(c));
            float h, s, v;
            Color.RGBToHSV(new Color(c.r / p, c.g / p, c.b / p, 1f), out h, out s, out v);
            return h * 360f;
        }

        static float CircularDeg(float a, float b)
        {
            float d = Mathf.Abs(a - b);
            return d > 180f ? 360f - d : d;
        }

        [Test]
        public void TheSunbreakerExists()
        {
            var hammer = AssetDatabase.LoadAssetAtPath<WeaponData>(HammerPath);
            Assert.IsNotNull(hammer, HammerPath + " missing -- PrefabFactory and the weapon pedestal both " +
                "hardcode this path. Fix: VibeGame1/3. Create Data (then 4. Build Prefabs).");
        }

        [Test]
        public void TheSunbreakerClearsTheBoltsHueByAtLeast45Degrees()
        {
            var hammer = AssetDatabase.LoadAssetAtPath<WeaponData>(HammerPath);
            Assert.IsNotNull(hammer, HammerPath);

            float hammerHue = Hue(hammer.neon);
            float boltHue = Hue(Projectile.HotCore);
            float d = CircularDeg(hammerHue, boltHue);

            Assert.GreaterOrEqual(d, MinHueDegFromBolt,
                "Hammer.neon (" + hammerHue.ToString("0") + " deg) is only " + d.ToString("0") +
                " deg from Projectile.HotCore, the enemy bolt (" + boltHue.ToString("0") + " deg) -- under " +
                MinHueDegFromBolt + " deg the player's own weapon trail competes with the single most " +
                "important thing to read in a parry game. WandDataTests holds every wand to this exact " +
                "floor against each OTHER; this is the same rule against the bolt.");
        }

        [Test]
        public void TheSunbreakerIsStillWarm()
        {
            // "Warm" here means it does not fall in cold-blue territory (roughly 150-330 deg on the wheel,
            // the world's whole palette since the 2026-09-06 cold pass) -- clearing the bolt must not
            // accidentally turn the weapon into "the world," which speaks nothing per section 4 rule 10.
            var hammer = AssetDatabase.LoadAssetAtPath<WeaponData>(HammerPath);
            Assert.IsNotNull(hammer, HammerPath);
            float h = Hue(hammer.neon);
            bool warm = h <= 150f || h >= 330f;
            Assert.IsTrue(warm, "Hammer.neon sits at " + h.ToString("0") +
                " deg, inside the cold-world band (150-330) -- rule 10 reserves warm for a combat tell or " +
                "fire, and the Sunbreaker is meant to read as a Pyre weapon, not as scenery.");
        }
    }
}
