using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;

namespace VibeGame1.Tests
{
    /// <summary>
    /// <b>Span 1's opening: the runway before the first sentry, and the space around the first ramp.</b>
    ///
    /// <para>2026-09-07, the user's ask: <i>"make the first ramp in level one top section have a little bit
    /// more runway before the first enemy so you can have more time to parry the projectile, and just
    /// space out that ramp and all the objects a tad bit more."</i></para>
    ///
    /// <para>The parry WINDOW is not a level number — <see cref="Projectile.CueLead"/> is a flat 0.28 s
    /// everywhere and <see cref="ProjectileShooter.CueMargin"/> holds a near bolt's flight at 0.44 s. What
    /// the LEVEL owns is (a) how far the player runs before the sentry is even awake and (b) whether the
    /// player is standing on a deck or in mid-air when the cue lands. Both were wrong in the T1 opening:
    /// <c>T1_Perch_W</c> at z 44 armed at z 13.1 — before the level's first ramp — and its opening bolt
    /// arrived at z ~27.5, in the gap between <c>T1_Stone_2</c> and <c>T1_Stone_3</c>. These tests pin the
    /// fix as SHIPPED DATA so a later spacing pass cannot silently close it again.</para>
    ///
    /// <para>What passing does NOT mean: nothing here says the parry feels fair. It says the sentry is
    /// asleep for the whole first ramp and that its first bolt arrives over ground the player is standing
    /// on. Only a human run can say the rest.</para>
    /// </summary>
    public class LevelT1OpeningTests
    {
        static LevelDefinition def;
        static EnemyData sentry;

        /// <summary>The route centreline used for the reaction arithmetic: x 0, chest 2.0 m (the T1 stone
        /// tops run 0.0 → 1.5, so a 1.2 m chest offset sits between 1.2 and 2.7). An approximation, stated
        /// rather than hidden — the assertions below leave room for it.</summary>
        const float RouteX = 0f, RouteChestY = 2f;

        [OneTimeSetUp]
        public void Load()
        {
            def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelArcReport.DefaultLevel);
            Assert.IsNotNull(def, "Level_01_Level.asset is missing");
            sentry = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy01"));
            Assert.IsNotNull(sentry, "pshooter_enemy01.asset is missing");
        }

        static PlatformDef Plat(string name)
        {
            foreach (var p in def.platforms) if (p != null && p.name == name) return p;
            Assert.Fail(name + " is not in the level");
            return null;
        }

        static RampDef Ramp(string name)
        {
            foreach (var r in def.ramps) if (r != null && r.name == name) return r;
            Assert.Fail(name + " is not in the level");
            return null;
        }

        static SpawnDef Spawn(string name)
        {
            foreach (var s in def.spawns) if (s != null && s.name == name) return s;
            Assert.Fail(name + " is not in the level");
            return null;
        }

        static Vector3 Min(PlatformDef p) { return p.center - p.size * 0.5f; }
        static Vector3 Max(PlatformDef p) { return p.center + p.size * 0.5f; }

        /// <summary>Where a sentry's bolt leaves: the spawn point plus the shooter's 1.3 m muzzle offset.</summary>
        static Vector3 Muzzle(string spawnName) { return Spawn(spawnName).position + Vector3.up * 1.3f; }

        /// <summary>How far away the player wakes a shooter — <c>EnemyController.WakeRange</c>.</summary>
        static float WakeRange { get { return Mathf.Max(sentry.aggroRange, sentry.projectileMaxRange); } }

        /// <summary>The z on the route centreline where a muzzle first has the player inside its wake band.</summary>
        static float WakeZ(Vector3 muzzle)
        {
            float dx = muzzle.x - RouteX, dy = muzzle.y - RouteChestY;
            float sq = WakeRange * WakeRange - dx * dx - dy * dy;
            Assert.Greater(sq, 0f, "the perch is further off the route than its own wake range");
            return muzzle.z - Mathf.Sqrt(sq);
        }

        // ------------------------------------------------------------------ the runway

        /// <summary>
        /// THE ASK. The whole of <c>T1_Ramp_Stone12</c> — the level's first ramp, and the first thing after
        /// the T0 descent's run-out — is run before the first sentry is awake. Measured to the ramp's TOP
        /// edge: at the moment the player leaves the ramp, the perch is still outside its own wake range.
        /// Under the pre-2026-09-07 perch at (-7.5, 3.5, 44) this distance was 25.9 m against a 32 m wake,
        /// i.e. the sentry had been armed since before the ramp began.
        /// </summary>
        [Test]
        public void TheFirstSentryIsAsleepForTheWholeOfTheFirstRamp()
        {
            var ramp = Ramp("T1_Ramp_Stone12");
            Vector3 chestAtRampTop = ramp.basePosition + new Vector3(0f, ramp.rise + 1.2f, ramp.run);
            float d = Vector3.Distance(Muzzle("Spawn_T1_GruntA"), chestAtRampTop);
            Assert.Greater(d, WakeRange,
                "T1_Perch_W is " + d.ToString("0.0") + " m from the top of the level's first ramp but wakes at " +
                WakeRange.ToString("0.0") + " m: the first sentry arms while the player is still on the ramp");
        }

        /// <summary>
        /// The sentry arms AFTER the first ramp is finished, not before it starts. Belt and braces on the
        /// test above, from the other direction: the solved wake point is north of the ramp's top edge.
        /// </summary>
        [Test]
        public void TheWakePointIsPastTheFirstRampsTopEdge()
        {
            var ramp = Ramp("T1_Ramp_Stone12");
            float wake = WakeZ(Muzzle("Spawn_T1_GruntA"));
            Assert.Greater(wake, ramp.basePosition.z + ramp.run,
                "the first sentry wakes at z " + wake.ToString("0.0") + ", on or before the first ramp (top edge z " +
                (ramp.basePosition.z + ramp.run).ToString("0.0") + ")");
        }

        /// <summary>
        /// THE OTHER HALF OF THE ASK: the first parry cue must land on GROUND. Flying the shipped constants
        /// — wake, then <c>projectileAcquireDelay</c> of approach, then a bolt at <c>projectileSpeed</c>
        /// (floored to a 0.44 s flight by <see cref="ProjectileMath.LaunchSpeed"/>) — the first bolt's
        /// impact point must fall inside <c>T1_Stone_4</c>'s footprint, so the player is standing on the
        /// widest deck of the chain with the causeway ahead when the cue fires. Before this pass the same
        /// arithmetic put it at z ~27.5: airborne, over the Stone_2 → Stone_3 choice.
        /// </summary>
        [Test]
        public void TheFirstBoltArrivesOverADeckAndNotInMidAir()
        {
            A.MoveProfile profile; string err;
            Assert.IsTrue(A.TryLoadProfile(out profile, out err), err);
            float runSpeed = profile.groundSpeed;

            Vector3 muzzle = Muzzle("Spawn_T1_GruntA");
            float fireZ = WakeZ(muzzle) + sentry.projectileAcquireDelay * runSpeed;
            Vector3 chestAtFire = new Vector3(RouteX, RouteChestY, fireZ);
            float dist = Vector3.Distance(muzzle, chestAtFire);
            float speed = ProjectileMath.LaunchSpeed(dist, sentry.projectileSpeed,
                                                     Projectile.CueLead, ProjectileShooter.CueMargin);
            float impactZ = fireZ + (dist / speed) * runSpeed;

            var deck = Plat("T1_Stone_4");
            Assert.GreaterOrEqual(impactZ, Min(deck).z,
                "the first bolt lands at z " + impactZ.ToString("0.0") + ", short of T1_Stone_4 (z " +
                Min(deck).z.ToString("0.0") + "..." + Max(deck).z.ToString("0.0") + ") — the cue is in mid-air again");
            Assert.LessOrEqual(impactZ, Max(deck).z,
                "the first bolt lands at z " + impactZ.ToString("0.0") + ", past T1_Stone_4");
        }

        /// <summary>
        /// A perch that stands inside <c>projectileMinRange</c> of the deck it covers goes SILENT exactly
        /// where it is meant to be firing. T1_Perch_W is broadside to the causeway now, so the nearest
        /// chest point on that deck has to stay outside the muzzle's dead zone.
        /// </summary>
        [Test]
        public void TheWestPerchNeverFallsInsideItsOwnDeadZone()
        {
            Vector3 muzzle = Muzzle("Spawn_T1_GruntA");
            var deck = Plat("T1_Causeway");
            Vector3 lo = Min(deck), hi = Max(deck);
            Vector3 nearest = new Vector3(Mathf.Clamp(muzzle.x, lo.x, hi.x),
                                          hi.y + 1.2f,
                                          Mathf.Clamp(muzzle.z, lo.z, hi.z));
            float d = Vector3.Distance(muzzle, nearest);
            Assert.Greater(d, sentry.projectileMinRange,
                "T1_Perch_W is " + d.ToString("0.0") + " m off the causeway, inside its own " +
                sentry.projectileMinRange.ToString("0.0") + " m near edge");
            Assert.Less(d, sentry.projectileMaxRange, "T1_Perch_W cannot reach the causeway at all");
        }

        /// <summary>Both decks the west perch claims are still inside the bolt band from its new home.</summary>
        [TestCase("T1_Causeway")]
        [TestCase("T1_Stone_4")]
        public void TheWestPerchStillCoversWhatItClaims(string deckName)
        {
            Vector3 muzzle = Muzzle("Spawn_T1_GruntA");
            var deck = Plat(deckName);
            float d = Vector3.Distance(muzzle, deck.center + new Vector3(0f, deck.size.y * 0.5f + 1.2f, 0f));
            Assert.LessOrEqual(d, sentry.projectileMaxRange, deckName + " is " + d.ToString("0.0") + " m out, past the band");
        }

        // ------------------------------------------------------------------ the spacing

        /// <summary>
        /// The four opening stones and their "tad" of room, pinned as shipped values. These are the numbers
        /// the 2026-09-07 pass bought; a later pass that wants them back has to change this test and say why.
        /// </summary>
        [TestCase("T1_Stone_1", 7f, 6.4f)]
        [TestCase("T1_Stone_2", 5.5f, 4f)]
        [TestCase("T1_Stone_3", 5f, 4f)]
        [TestCase("T1_Stone_4", 6f, 6f)]
        public void TheOpeningStonesKeepTheirRoom(string name, float minWidth, float minDepth)
        {
            var p = Plat(name);
            Assert.GreaterOrEqual(p.size.x, minWidth - 0.001f, name + " narrowed to " + p.size.x);
            Assert.GreaterOrEqual(p.size.z, minDepth - 0.001f, name + " shortened to " + p.size.z);
        }

        /// <summary>
        /// <c>T1_Stone_1</c> grew SOUTHWARD only. Its north edge is the anchor for two things that must not
        /// move: the first ramp's 0.2 m base overlap, and the 8.5 m committed gap that gates the
        /// <c>T1_Fast_1</c> slide line (a base jump clears ~7.3 m, a slide-jump clears it).
        /// </summary>
        [Test]
        public void TheFirstDeckGrewSouthAndLeftItsNorthEdgeAlone()
        {
            var s1 = Plat("T1_Stone_1");
            Assert.AreEqual(16.5f, Max(s1).z, 0.001f, "T1_Stone_1's north edge moved; the ramp seam and the Fast_1 gate hang off it");
            Assert.LessOrEqual(Min(s1).z, 10.2f, "T1_Stone_1 lost its southern run-up");

            float gate = Min(Plat("T1_Fast_1")).z - Max(s1).z;
            Assert.AreEqual(8.5f, gate, 0.001f, "the committed entry onto T1_Fast_1 is " + gate.ToString("0.00") + " m, not 8.5");
        }

        /// <summary>The step up from the start pad is a step, not a hop: Ground_Start and T1_Stone_1 are
        /// the same height and now 2.1 m apart, so the opening reads as a plaza rather than two islands.</summary>
        [Test]
        public void TheStartPadAndTheFirstDeckReadAsOnePlace()
        {
            float gap = Min(Plat("T1_Stone_1")).z - Max(Plat("Ground_Start")).z;
            Assert.LessOrEqual(gap, 2.2f, "the opening step is " + gap.ToString("0.00") + " m again");
            Assert.Greater(gap, 0f, "Ground_Start and T1_Stone_1 have merged");
        }

        /// <summary>
        /// The first ramp is 3.5 m wide and FULLY SUPPORTED at both ends — its x span lies inside both the
        /// deck it leaves and the deck it meets, so nothing overhangs. It is the second ramp in the level
        /// that can say that, and it is the first one the player runs up.
        /// </summary>
        [Test]
        public void TheFirstRampIsWideAndFullySupported()
        {
            var ramp = Ramp("T1_Ramp_Stone12");
            Assert.GreaterOrEqual(ramp.width, 3.5f - 0.001f, "the first ramp narrowed to " + ramp.width);
            Assert.AreEqual(0f, ramp.yaw, 0.001f, "the first ramp is meant to run straight up the band the two decks share");

            float lo = ramp.basePosition.x - ramp.width * 0.5f, hi = ramp.basePosition.x + ramp.width * 0.5f;
            foreach (var deck in new[] { Plat("T1_Stone_1"), Plat("T1_Stone_2") })
            {
                Assert.GreaterOrEqual(lo, Min(deck).x - 0.001f, "the ramp hangs west of " + deck.name);
                Assert.LessOrEqual(hi, Max(deck).x + 0.001f, "the ramp hangs east of " + deck.name);
            }

            // Both ends still MEET their deck (a ramp that stops short is a jump with a shorter run-up).
            // The authored overlap is EXACTLY 0.2 m at both ends (16.30 + 3.90 against a deck edge at
            // 20.0), so these need the same 0.001 epsilon every other comparison in this file uses:
            // 16.30f + 3.90f lands on 20.199999 in float, and a bare >= 0.2f fails on arithmetic, not
            // on geometry. The base end only passed by rounding luck.
            Assert.GreaterOrEqual(Max(Plat("T1_Stone_1")).z - ramp.basePosition.z, 0.2f - 0.001f, "the ramp's base does not overlap T1_Stone_1");
            Assert.GreaterOrEqual(ramp.basePosition.z + ramp.run - Min(Plat("T1_Stone_2")).z, 0.2f - 0.001f, "the ramp's top does not reach T1_Stone_2");
        }

        /// <summary>
        /// The stones grew along the axis their gaps are NOT measured on, so the wall-run corridors are
        /// untouched: every opening stone still clears <c>T1_Wall_Start</c>'s run line, and
        /// <c>T1_Stone_3</c>'s 0.25 m seam with <c>T1_Fast_1</c> has not closed.
        /// </summary>
        [Test]
        public void TheWiderStonesStayOutOfTheRunLinesAndTheSeam()
        {
            float runLine = Min(Plat("T1_Wall_Start")).x - 0.8f;   // 2 x the shipped capsule radius
            foreach (var n in new[] { "T1_Stone_1", "T1_Stone_2", "T1_Stone_3", "T1_Fast_1" })
                Assert.Less(Max(Plat(n)).x, runLine, n + " reaches under the opening wall's run line");

            Assert.Less(Max(Plat("T1_Stone_4")).x, Min(Plat("T1_Wall_Causeway")).x - 0.8f,
                        "T1_Stone_4 reaches under the causeway wall's run line");

            float seam = Min(Plat("T1_Fast_1")).x - Max(Plat("T1_Stone_3")).x;
            Assert.GreaterOrEqual(seam, 0.25f - 0.001f, "the T1_Stone_3 / T1_Fast_1 seam closed to " + seam.ToString("0.00"));
        }
    }
}
