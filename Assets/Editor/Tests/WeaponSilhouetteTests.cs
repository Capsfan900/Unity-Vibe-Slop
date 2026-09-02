using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE WEAPON SET measured on screen, from the player's own eye — the contract that let the
    /// archetypes get their lengths back after the dagger pass flattened them.
    ///
    /// <para><b>The deal this file enforces.</b> The user collapsed every weapon into a dagger because
    /// daggers "show the animations best and feel best". What the dagger silhouette was actually buying
    /// was three framing properties, and length was never one of them — POSE was what broke the frame.
    /// So the three properties are asserted here directly, on the shipped assets, through the shipped
    /// rig, and length is free again:
    ///   1. a HELD pose (idle, guard) never crosses the crosshair — the enemy you are about to parry
    ///      stays readable;
    ///   2. a held pose covers only a corner of the frame, never the screen;
    ///   3. the TIP stays in frame where contact legibility needs it (held poses and the strike frame —
    ///      NOT the hammer wind-up, whose head deliberately leaves the top of the frame as its
    ///      commitment cue).</para>
    ///
    /// <para>Nothing here compares an authored Euler — see <c>PoseSilhouetteTests</c> for why. Every
    /// number comes out of <see cref="WeaponSilhouette"/>, which rasterises the real viewmodel prefab
    /// at the real <c>viewmodelScale</c> under the real serialised pose, through the Player prefab's
    /// own camera. Rule 9: everything reads the shipped <c>.asset</c>, never a C# default.</para>
    /// </summary>
    public class WeaponSilhouetteTests
    {
        // The loadout, dagger-first because it is the reference the others are measured against.
        const string Dagger = "Dagger";
        const string Dev = "DevBlade";
        const string Sword = "Sword";
        const string Hammer = "Hammer";
        static readonly string[] All = { Dagger, Dev, Sword, Hammer };

        /// <summary>A held pose may cover at most this fraction of the frame. Measured 2026-09: the set
        /// idles at 0.4-1.6% — an order of magnitude of headroom, and anything past 5% means a pose has
        /// swung the weapon across the screen.</summary>
        const float MaxHeldCoverage = 0.05f;
        /// <summary>Fraction of the crosshair disc a held pose may touch. The Rosethorn control itself
        /// reads 0.9% — a sliver of pommel at the disc's edge — so zero would fail the reference the
        /// player likes. What this catches is a weapon IN the disc: the hammer's first guard shipped at
        /// 16.1% with its head dead centre, and this threshold is what caught it.</summary>
        const float MaxHeldCrosshair = 0.015f;
        /// <summary>Above this IoU two weapons read as the same thing on screen — which is the exact
        /// state ("just daggers") this pass exists to undo.</summary>
        const float MaxPairIoU = 0.60f;

        static WeaponData W(string n) =>
            AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Data/Weapons/" + n + ".asset");

        // ---------------------------------------------------------------- staged measurement, once

        static Dictionary<string, WeaponSilhouette.Shot> cache;

        static WeaponSilhouette.Shot Shot(string weapon, string pose)
        {
            if (cache == null)
            {
                cache = new Dictionary<string, WeaponSilhouette.Shot>();
                WeaponViewmodel vm; Camera cam;
                GameObject player = WeaponSilhouette.StagePlayer(out vm, out cam);
                Assert.IsNotNull(player, "Player.prefab missing — run VibeGame1/4. Build Prefabs");
                Assert.IsNotNull(vm, "Player.prefab has no WeaponViewmodel");
                Assert.IsNotNull(cam, "Player.prefab has no camera");
                try
                {
                    foreach (var n in All)
                    {
                        var w = W(n);
                        Assert.IsNotNull(w, n + ".asset missing — run VibeGame1/3. Create Data");
                        foreach (var p in new[] { "idle", "windup", "strike", "guard", "parry" })
                            cache[n + "." + p] = WeaponSilhouette.Measure(vm, cam, w, p);
                    }
                }
                finally { Object.DestroyImmediate(player); }
            }
            return cache[weapon + "." + pose];
        }

        // ---------------------------------------------------------------- the ladder

        [Test]
        public void ExtentAboveTheFist_SpreadsAgain()
        {
            // THE number the dagger pass flattened (everything landed in a 0.27-0.32 m band). The set
            // must climb dagger < dev < sword < hammer and span at least 2x end to end, or the pass
            // has quietly collapsed back into one silhouette.
            float d = WeaponSilhouette.ExtentAboveFist(W(Dagger));
            float v = WeaponSilhouette.ExtentAboveFist(W(Dev));
            float s = WeaponSilhouette.ExtentAboveFist(W(Sword));
            float h = WeaponSilhouette.ExtentAboveFist(W(Hammer));
            Assert.Greater(d, 0.20f, "dagger extent implausibly small — grip or prefab missing?");
            Assert.Less(d, v, "dev blade must out-reach the dagger  (dagger " + d + " dev " + v + ")");
            Assert.Less(v, s, "sword must out-reach the dev blade   (dev " + v + " sword " + s + ")");
            Assert.Less(s, h, "hammer must out-reach the sword      (sword " + s + " hammer " + h + ")");
            Assert.GreaterOrEqual(h / d, 2f,
                "the set spans less than 2x (" + d + " → " + h + "); that is a dagger family, not a ladder");
        }

        [Test]
        public void RosethornIsTheControl_UnchangedToTheMillimetre()
        {
            // The user kept the dagger because it feels best, so it is the fixed point of this pass:
            // if the sword or the maul read badly the fault is THEIR pose or geometry, never a reason
            // to move this one. These are the dagger-pass numbers, asserted off the shipped asset.
            var w = W(Dagger);
            Assert.AreEqual(0.22f, w.attackDuration, 1e-4f, "dagger attackDuration moved");
            Assert.AreEqual(0.06f, w.hitDelay, 1e-4f, "dagger hitDelay moved");
            Assert.AreEqual(1.3f, w.hitOffset, 1e-4f, "dagger hitOffset moved");
            Assert.AreEqual(0.9f, w.hitRadius, 1e-4f, "dagger hitRadius moved");
            Assert.AreEqual(4, w.comboLength, "dagger comboLength moved");
            Assert.AreEqual(0.5f, w.viewmodelScale, 1e-4f, "dagger viewmodelScale moved");
            Assert.AreEqual(18f, w.parryPostureDamage, 1e-4f, "dagger parryPostureDamage moved");
        }

        [Test]
        public void TheTimingLadder_AHammerCommitsAndADaggerFlicks()
        {
            var d = W(Dagger); var v = W(Dev); var s = W(Sword); var h = W(Hammer);
            // Swing time climbs the whole ladder, and the ends differ by ~4x — a difference of KIND,
            // felt inside one swing, not a stat-sheet nuance.
            Assert.Less(d.attackDuration, v.attackDuration, "dev must swing slower than the dagger");
            Assert.Less(v.attackDuration, s.attackDuration, "sword must swing slower than the dev blade");
            Assert.Less(s.attackDuration, h.attackDuration, "hammer must swing slower than the sword");
            Assert.GreaterOrEqual(h.attackDuration / d.attackDuration, 3f,
                "hammer/dagger swing ratio " + (h.attackDuration / d.attackDuration) +
                " — the commitment gap has been flattened");
            // The contact frame arrives later the heavier the weapon: the wind-up IS the readable cost.
            Assert.Less(d.hitDelay, s.hitDelay, "sword contact must arrive later than the dagger's");
            Assert.Less(s.hitDelay, h.hitDelay, "hammer contact must arrive later than the sword's");
            // And the combo shortens as the weapon commits: flurry 4, generalist 3, maul 2.
            Assert.AreEqual(2, h.comboLength, "the maul chains twice, no more — weight, not rhythm");
            Assert.Greater(d.comboLength, s.comboLength, "the dagger must out-chain the sword");
            // Reach climbs the three real archetypes (the dev blade is a cheat and exempt).
            float rd = d.hitOffset + d.hitRadius, rs = s.hitOffset + s.hitRadius, rh = h.hitOffset + h.hitRadius;
            Assert.Less(rd, rs, "sword must out-reach the dagger  (" + rd + " vs " + rs + ")");
            Assert.Less(rs, rh, "hammer must out-reach the sword (" + rs + " vs " + rh + ")");
        }

        [Test]
        public void SixDeflectEconomy_TheSwordStaysAt25()
        {
            // LOAD-BEARING, not a tuning knob: 25 x 1.4 x 6 = 210 is exactly the Pale Marionette's
            // posture bar, and MarionetteDataTests.SixCleanDeflects_BreakIt asserts the outcome. This
            // assertion lives here so a future weapon pass trips over the constraint INSIDE weapon-land,
            // next to the numbers it will be editing, with the reason attached. Change it only together
            // with Legendary_Marionette.maxPosture, keeping the arithmetic on six.
            Assert.AreEqual(25f, W(Sword).parryPostureDamage, 1e-4f,
                "Sword.parryPostureDamage is the Marionette's six-deflect economy — see MarionetteDataTests");
        }

        // ---------------------------------------------------------------- the framing contract

        [Test]
        public void HeldPoses_NeverCrossTheCrosshair()
        {
            foreach (var n in All)
                foreach (var p in new[] { "idle", "guard" })
                {
                    var s = Shot(n, p);
                    Assert.IsTrue(s.valid, s.name + " did not rasterise");
                    Assert.LessOrEqual(s.crosshair, MaxHeldCrosshair,
                        s.name + " covers " + s.crosshair.ToString("P1") +
                        " of the crosshair disc — the player cannot read the enemy through their own weapon");
                }
        }

        [Test]
        public void HeldPoses_CoverACornerOfTheFrame_NeverTheScreen()
        {
            foreach (var n in All)
                foreach (var p in new[] { "idle", "guard" })
                {
                    var s = Shot(n, p);
                    Assert.IsTrue(s.valid, s.name + " did not rasterise");
                    Assert.LessOrEqual(s.coverage, MaxHeldCoverage,
                        s.name + " covers " + s.coverage.ToString("P1") + " of the frame (limit " +
                        MaxHeldCoverage.ToString("P0") + ") — this is the mistake the dagger pass was fixing");
                }
        }

        [Test]
        public void TheTip_StaysInFrame_WhereContactIsRead()
        {
            // Held poses and the strike frame. NOT the wind-up: the hammer's head leaving the top of
            // the frame during its 0.40 s wind-up is authored — the empty screen IS the commitment cue.
            foreach (var n in All)
                foreach (var p in new[] { "idle", "guard", "strike" })
                {
                    var s = Shot(n, p);
                    Assert.IsTrue(s.valid, s.name + " did not rasterise");
                    Assert.IsTrue(s.tipInFrame,
                        s.name + " tip at (" + s.tipX.ToString("0.00") + "," + s.tipY.ToString("0.00") +
                        ") is outside the frame — the contact point of the swing is illegible");
                }
        }

        [Test]
        public void NoTwoWeapons_ShareASilhouette()
        {
            // The literal statement of the user's request. At idle — the pose the player stares at for
            // whole levels — every pair must be distinguishable at a glance. IoU 1.0 is "just daggers".
            for (int i = 0; i < All.Length; i++)
                for (int j = i + 1; j < All.Length; j++)
                {
                    var a = Shot(All[i], "idle");
                    var b = Shot(All[j], "idle");
                    float iou = WeaponSilhouette.IoU(a.mask, b.mask);
                    Assert.LessOrEqual(iou, MaxPairIoU,
                        All[i] + " and " + All[j] + " overlap at IoU " + iou.ToString("0.00") +
                        " at idle — they read as the same weapon");
                }
        }

        [Test]
        public void EveryWeapon_HasAPrefabAndAGripTheHandCanClose()
        {
            // WeaponViewmodel.CloseHandOn moves the fist to the prefab's Grip* part; without one the
            // hand grips empty air beside the weapon (the exact bug the hammer shipped mid-flight with).
            foreach (var n in All)
            {
                var w = W(n);
                Assert.IsNotNull(w.viewmodelPrefab, n + " has no viewmodelPrefab — run VibeGame1/4. Build Prefabs");
                Assert.IsNotNull(WeaponViewmodel.FindGrip(w.viewmodelPrefab.transform),
                    n + "'s viewmodel has no Grip* part — the hand would close on empty air");
            }
        }
    }
}
