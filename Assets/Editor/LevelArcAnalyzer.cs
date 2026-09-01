using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// <b>Is that jump actually possible, and does anything stand in the middle of the arc?</b>
    ///
    /// <para><c>FeatureTests.CheckHop</c> measures the box-to-box GAP between two platforms and compares it
    /// to a reachability envelope. That is necessary and it is not sufficient: a gap of 4.2 m says nothing
    /// about a pillar standing halfway along the arc. The one time this mattered — the buttress fin drawn up
    /// for The Ascent in BACKLOG §5b — the honest answer was "this needs a human to jump it", and the fin was
    /// never built.</para>
    ///
    /// <para>It never needed a human. Every platform in a <see cref="LevelDefinition"/> is an axis-aligned
    /// box with a known centre and size, and the player's flight is a ballistic arc with constants that are
    /// on the shipped <c>Player.prefab</c>. So this class flies the player's real capsule along the real arc
    /// and measures the distance to every other box on the way. A blocked route becomes a number and a
    /// failing test instead of a playtest nobody schedules.</para>
    ///
    /// <para><b>Everything here is pure.</b> No scene, no play mode, no <c>GameObject.Find</c>: it takes a
    /// list of boxes and a <see cref="MoveProfile"/> and returns numbers, so an EditMode test drives it and
    /// <c>-batchmode -executeMethod</c> runs it. The constants are READ off <c>Player.prefab</c>
    /// (<see cref="MoveProfile.FromPlayerPrefab"/>) rather than typed in here, because a movement number
    /// duplicated in an analyser is a number that will silently stop being true.</para>
    ///
    /// <para><b>What it does not model.</b> Enemies, moving gates, the NavMesh, and the player's own aim.
    /// Air control is modelled only where it is asked for (<see cref="AirControl"/>), and the default for a
    /// hop is NONE — a clean verdict here means the arc works with the stick released, which is the
    /// conservative direction. Sub-stepping is 2 ms, i.e. ≤ 4.5 cm of travel at the fastest speed the motor
    /// can produce, so a 0.6 m fin cannot be tunnelled through.</para>
    /// </summary>
    public static class LevelArcAnalyzer
    {
        // ============================================================ the world, as boxes

        /// <summary>One solid axis-aligned box. Everything the level is made of is one of these.</summary>
        public struct Box
        {
            public string name;
            public Vector3 min, max;

            public Box(string n, Vector3 center, Vector3 size)
            {
                name = n;
                Vector3 h = size * 0.5f;
                min = center - h;
                max = center + h;
            }

            public float Top { get { return max.y; } }
            public Vector3 Center { get { return (min + max) * 0.5f; } }
        }

        /// <summary>Every platform in a definition, in authoring order, as boxes.</summary>
        public static List<Box> BoxesFrom(LevelDefinition def)
        {
            var list = new List<Box>();
            if (def == null || def.platforms == null) return list;
            for (int i = 0; i < def.platforms.Length; i++)
            {
                var p = def.platforms[i];
                if (p == null) continue;
                list.Add(new Box(p.name, p.center, p.size));
            }
            return list;
        }

        public static int IndexOf(IList<Box> boxes, string name)
        {
            for (int i = 0; i < boxes.Count; i++) if (boxes[i].name == name) return i;
            return -1;
        }

        /// <summary>
        /// Only the boxes anywhere near a region. Purely an optimisation — a 4 m hop cannot be obstructed
        /// by the boss arena 200 m away — but it is what turns an exhaustive arc sweep from minutes into
        /// milliseconds, which is what lets this run as a test rather than as an errand.
        /// </summary>
        public static List<Box> Near(IList<Box> boxes, Vector3 min, Vector3 max)
        {
            var outp = new List<Box>();
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                if (b.max.x < min.x || b.min.x > max.x) continue;
                if (b.max.y < min.y || b.min.y > max.y) continue;
                if (b.max.z < min.z || b.min.z > max.z) continue;
                outp.Add(b);
            }
            return outp;
        }

        // ============================================================ the player, as numbers

        /// <summary>
        /// The movement constants this analysis is built on. Populated from the SHIPPED
        /// <c>Player.prefab</c> so that retuning the motor retunes the analyser, and a route that stops
        /// being possible fails a test rather than quietly becoming a lie.
        /// </summary>
        public struct MoveProfile
        {
            public float gravity;                     // negative
            public float jumpHeight;
            public float jumpCutGravityMultiplier;
            public float groundSpeed;
            public float airAccel;
            public float dashSpeed;

            public float slideBoost, slideMaxSpeed, slideHeight;

            public float wallJumpUpSpeed, wallJumpPushSpeed, wallCheckDistance, sameWallCosineLimit;
            public int maxWallJumps;

            public float capsuleRadius, standHeight, skinWidth;

            /// <summary>Vertical speed a jump leaves the ground at: sqrt(2 g h).</summary>
            public float JumpTakeoffSpeed { get { return Mathf.Sqrt(2f * -gravity * jumpHeight); } }

            /// <summary>
            /// Horizontal speed a slide-jump leaves at — the motor's own entry formula
            /// (<c>min(max(speed, groundSpeed) + slideBoost, slideMaxSpeed)</c>) evaluated for a player
            /// entering the slide at a full run. Jump-cancelling keeps it, which is the tech.
            /// </summary>
            public float SlideJumpSpeed
            {
                get { return Mathf.Min(groundSpeed + slideBoost, slideMaxSpeed); }
            }

            /// <summary>Radius the sweep actually uses: the capsule plus its skin.</summary>
            public float SweptRadius { get { return capsuleRadius + skinWidth; } }
        }

        public const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        /// <summary>Reads the profile off the shipped player prefab. Never invents a number.</summary>
        public static bool TryLoadProfile(out MoveProfile profile, out string error)
        {
            profile = new MoveProfile();
            error = null;

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (go == null) { error = PlayerPrefabPath + " not found — run VibeGame1/4. Build Prefabs."; return false; }

            var m = go.GetComponentInChildren<FirstPersonMotor>(true);
            if (m == null) { error = "Player.prefab has no FirstPersonMotor."; return false; }
            var cc = go.GetComponentInChildren<CharacterController>(true);
            if (cc == null) { error = "Player.prefab has no CharacterController."; return false; }

            profile.gravity = m.gravity;
            profile.jumpHeight = m.jumpHeight;
            profile.jumpCutGravityMultiplier = m.jumpCutGravityMultiplier;
            profile.groundSpeed = m.groundSpeed;
            profile.airAccel = m.airAccel;
            profile.dashSpeed = m.dashSpeed;
            profile.slideBoost = m.slideBoost;
            profile.slideMaxSpeed = m.slideMaxSpeed;
            profile.slideHeight = m.slideHeight;
            profile.wallJumpUpSpeed = m.wallJumpUpSpeed;
            profile.wallJumpPushSpeed = m.wallJumpPushSpeed;
            profile.wallCheckDistance = m.wallCheckDistance;
            profile.sameWallCosineLimit = m.sameWallCosineLimit;
            profile.maxWallJumps = m.maxWallJumps;
            profile.capsuleRadius = cc.radius;
            profile.standHeight = cc.height;
            profile.skinWidth = cc.skinWidth;
            return true;
        }

        // ============================================================ geometry

        /// <summary>
        /// Distance from a VERTICAL segment (the capsule's spine, at a fixed x/z) to an axis-aligned box.
        /// Exact, because the segment is axis-aligned: the closest approach separates cleanly per axis.
        /// Subtract the capsule radius from this and you have signed clearance.
        /// </summary>
        public static float SegmentBoxDistance(float x, float z, float y0, float y1, Box b)
        {
            float dx = Mathf.Max(Mathf.Max(b.min.x - x, x - b.max.x), 0f);
            float dz = Mathf.Max(Mathf.Max(b.min.z - z, z - b.max.z), 0f);
            float dy = Mathf.Max(Mathf.Max(b.min.y - y1, y0 - b.max.y), 0f);
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Signed clearance of a standing capsule whose FEET are at <paramref name="feet"/>.</summary>
        public static float Clearance(Vector3 feet, MoveProfile p, Box b)
        {
            float r = p.SweptRadius;
            return SegmentBoxDistance(feet.x, feet.z, feet.y + r, feet.y + p.standHeight - r, b) - r;
        }

        /// <summary>Can the player stand here without being inside something? Used to reject launch points.</summary>
        public static bool StandFree(Vector3 feet, MoveProfile p, IList<Box> boxes, int ignore)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                if (i == ignore) continue;
                if (Clearance(feet, p, boxes[i]) < 0f) return false;
            }
            return true;
        }

        // ============================================================ one arc

        /// <summary>What the player is asking the air-control stick to do during a flight.</summary>
        public enum AirControl
        {
            /// <summary>Stick released. The conservative reading — if an arc works here it works.</summary>
            None,
            /// <summary>Held forward along the flight direction. Cannot exceed groundSpeed, so it only
            /// tops a slow arc back up.</summary>
            Forward,
            /// <summary>Held back against the flight direction — the brake. This is how a player lands a
            /// 12 m/s wall push on a 4 m ledge, and ignoring it makes every wall route look impossible.</summary>
            Brake
        }

        public struct ArcOutcome
        {
            public bool landed;
            public int landedOn;
            public bool blocked;
            public int blockedBy;
            /// <summary>Smallest signed clearance to any box that is not an endpoint. Negative = a hit.</summary>
            public float minClearance;
            public int minClearanceBox;
            public float peakY;
            public float flightTime;
            public Vector3 landingFeet;
        }

        const float Dt = 0.002f;

        /// <summary>
        /// Flies the capsule from <paramref name="startFeet"/> with velocity <paramref name="v"/> until it
        /// lands on something, hits something, or falls out of the world.
        /// </summary>
        /// <param name="ignoreIndex">The platform being left. Skipped entirely — you are standing on it.</param>
        /// <param name="clearanceIgnore">Also skipped when accumulating min clearance (normally the target,
        /// whose clearance goes to zero at touchdown by definition), but still able to land you / block you.</param>
        public static ArcOutcome SweepArc(Vector3 startFeet, Vector3 v, bool holdJump, AirControl control,
                                          MoveProfile p, IList<Box> boxes,
                                          int ignoreIndex, int clearanceIgnore, float maxTime, float floorY)
        {
            var o = new ArcOutcome();
            o.landedOn = -1; o.blockedBy = -1; o.minClearanceBox = -1;
            o.minClearance = float.MaxValue;
            o.peakY = startFeet.y;

            float r = p.SweptRadius;
            float h = p.standHeight;

            Vector3 feet = startFeet;
            Vector3 wish = Vector3.zero;
            if (control != AirControl.None)
            {
                Vector3 flat = new Vector3(v.x, 0f, v.z);
                if (flat.sqrMagnitude > 0.0001f)
                    wish = flat.normalized * (control == AirControl.Brake ? -1f : 1f);
            }

            float t = 0f;
            while (t < maxTime)
            {
                // Gravity, with the jump cut applied exactly as the motor applies it: only while rising.
                float g = p.gravity;
                if (!holdJump && v.y > 0f) g += p.gravity * p.jumpCutGravityMultiplier;
                v.y += g * Dt;

                // AirAccelerate, mirrored from FirstPersonMotor: adds along wish only up to groundSpeed.
                if (wish.sqrMagnitude > 0.0001f)
                {
                    float current = v.x * wish.x + v.z * wish.z;
                    float add = p.groundSpeed - current;
                    if (add > 0f)
                    {
                        float a = Mathf.Min(p.airAccel * Dt, add);
                        v.x += wish.x * a;
                        v.z += wish.z * a;
                    }
                }

                Vector3 prev = feet;
                feet += v * Dt;
                t += Dt;
                if (feet.y > o.peakY) o.peakY = feet.y;

                float y0 = feet.y + r, y1 = feet.y + h - r;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (i == ignoreIndex) continue;
                    var b = boxes[i];
                    float d = SegmentBoxDistance(feet.x, feet.z, y0, y1, b) - r;
                    if (d < 0f)
                    {
                        // Came down onto the top face: that is a landing, not an obstruction.
                        bool fromAbove = v.y <= 0f && prev.y >= b.max.y - 0.03f;
                        if (fromAbove)
                        {
                            o.landed = true; o.landedOn = i; o.flightTime = t;
                            o.landingFeet = new Vector3(feet.x, b.max.y, feet.z);
                            return o;
                        }
                        o.blocked = true; o.blockedBy = i; o.flightTime = t;
                        if (i != clearanceIgnore && d < o.minClearance) { o.minClearance = d; o.minClearanceBox = i; }
                        return o;
                    }
                    if (i != clearanceIgnore && d < o.minClearance) { o.minClearance = d; o.minClearanceBox = i; }
                }

                if (feet.y < floorY) break;   // fell out of the world
            }

            o.flightTime = t;
            return o;
        }

        // ============================================================ one authored traversal

        public struct HopVerdict
        {
            public string from, to;
            public bool exists;
            public float gap, rise;            // the same two numbers CheckHop reports, for cross-reference
            public int cleanArcs, totalArcs;
            public int cleanLaunchPoints, launchPoints;
            /// <summary>Widest clean corridor found: the best arc's minimum clearance to third-party geometry.</summary>
            public float bestClearance;
            public float bestSpeed;
            public Vector3 bestLaunchFeet;
            public Vector3 bestVelocity;
            public bool bestHoldJump;
            public AirControl bestControl;
            /// <summary>The box that stops the most arcs. Empty when nothing does.</summary>
            public string chiefObstruction;
            public int obstructedArcs;

            public string Summary()
            {
                if (!exists)
                {
                    return string.Format("{0} -> {1}: NO CLEAN ARC  (gap {2:0.00} m, rise {3:0.00} m; " +
                                         "{4} arcs tried, {5} obstructed, chiefly by {6})",
                                         from, to, gap, rise, totalArcs, obstructedArcs,
                                         string.IsNullOrEmpty(chiefObstruction) ? "nothing (all fell short)" : chiefObstruction);
                }
                return string.Format("{0} -> {1}: ok   gap {2:0.00} m  rise {3:0.00} m   " +
                                     "clean {4}/{5} arcs from {6}/{7} launch points   best clearance {8:0.00} m" +
                                     "{9}",
                                     from, to, gap, rise, cleanArcs, totalArcs, cleanLaunchPoints, launchPoints,
                                     bestClearance >= 1e8f ? 99f : bestClearance,
                                     string.IsNullOrEmpty(chiefObstruction) ? "" : "   (nearest obstruction: " + chiefObstruction + ")");
            }
        }

        /// <summary>The CheckHop numbers, computed off the definition rather than off the built scene.</summary>
        public static void GapAndRise(Box a, Box b, out float gap, out float rise)
        {
            float dx = Mathf.Max(Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x), 0f);
            float dz = Mathf.Max(Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z), 0f);
            gap = Mathf.Sqrt(dx * dx + dz * dz);
            rise = b.max.y - a.max.y;
        }

        static float[] SpeedLadder(float max)
        {
            // Four speeds spread over what the moveset can produce. A hop that only works at exactly one
            // speed is not a hop anyone lands, so the count matters as much as the existence.
            return new float[] { max * 0.45f, max * 0.65f, max * 0.85f, max };
        }

        /// <summary>
        /// Flies a fan of plausible arcs from <paramref name="fromName"/> to <paramref name="toName"/> and
        /// reports whether any of them arrives without touching third-party geometry.
        /// </summary>
        /// <param name="maxSpeed">Top horizontal speed the moveset in question can leave the ground at:
        /// <c>groundSpeed</c> for the base kit, <c>SlideJumpSpeed</c> for a slide-jump line.</param>
        public static HopVerdict AnalyzeHop(IList<Box> boxes, string fromName, string toName,
                                            MoveProfile p, float maxSpeed, float floorY,
                                            AirControl[] controls = null)
        {
            var v = new HopVerdict();
            v.from = fromName; v.to = toName;
            v.bestClearance = float.MinValue;
            v.chiefObstruction = "";

            int ia0 = IndexOf(boxes, fromName), ib0 = IndexOf(boxes, toName);
            if (ia0 < 0 || ib0 < 0) { v.chiefObstruction = "missing platform"; return v; }

            Box a = boxes[ia0], b = boxes[ib0];
            GapAndRise(a, b, out v.gap, out v.rise);

            // Only the neighbourhood can obstruct this arc. Generous: 12 m out, 8 m down, 22 m up.
            Vector3 lo = Vector3.Min(a.min, b.min) - new Vector3(12f, 8f, 12f);
            Vector3 hi = Vector3.Max(a.max, b.max) + new Vector3(12f, 22f, 12f);
            boxes = Near(boxes, lo, hi);
            int ia = IndexOf(boxes, fromName), ib = IndexOf(boxes, toName);

            if (controls == null) controls = new AirControl[] { AirControl.None };

            float inset = p.SweptRadius + 0.05f;
            var launch = LaunchBand(a, b, inset, 5);
            var aim = GridOnTop(b, inset, 3);
            var speeds = SpeedLadder(maxSpeed);

            var blockCount = new Dictionary<string, int>();

            for (int li = 0; li < launch.Count; li++)
            {
                Vector3 lf = launch[li];
                if (!StandFree(lf, p, boxes, ia)) continue;   // you cannot take off from inside a pillar
                v.launchPoints++;
                bool anyCleanHere = false;

                for (int ai = 0; ai < aim.Count; ai++)
                {
                    Vector3 dir = aim[ai] - lf;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.25f) continue;
                    dir.Normalize();

                    for (int si = 0; si < speeds.Length; si++)
                    {
                        for (int jh = 0; jh < 2; jh++)
                        {
                            bool hold = jh == 0;
                            for (int ci = 0; ci < controls.Length; ci++)
                            {
                                v.totalArcs++;
                                Vector3 vel = dir * speeds[si] + Vector3.up * p.JumpTakeoffSpeed;
                                var o = SweepArc(lf, vel, hold, controls[ci], p, boxes, ia, ib, 4f, floorY);

                                if (o.blocked && o.blockedBy != ib)
                                {
                                    v.obstructedArcs++;
                                    string bn = boxes[o.blockedBy].name;
                                    int c; blockCount.TryGetValue(bn, out c);
                                    blockCount[bn] = c + 1;
                                    continue;
                                }
                                if (!o.landed || o.landedOn != ib) continue;

                                v.cleanArcs++;
                                anyCleanHere = true;
                                float clr = o.minClearance >= 1e8f ? 99f : o.minClearance;
                                if (clr > v.bestClearance)
                                {
                                    v.bestClearance = clr;
                                    v.bestSpeed = speeds[si];
                                    v.bestLaunchFeet = lf;
                                    v.bestVelocity = vel;
                                    v.bestHoldJump = hold;
                                    v.bestControl = controls[ci];
                                }
                            }
                        }
                    }
                }
                if (anyCleanHere) v.cleanLaunchPoints++;
            }

            v.exists = v.cleanArcs > 0;
            int best = 0;
            foreach (var kv in blockCount) if (kv.Value > best) { best = kv.Value; v.chiefObstruction = kv.Key; }
            if (v.bestClearance == float.MinValue) v.bestClearance = 0f;
            return v;
        }

        /// <summary>
        /// Standing positions on the SOURCE's top face from which a player would plausibly leave for the
        /// target: within 6 m of the facing edge, and laterally aligned with the target rather than
        /// spread over the whole deck.
        ///
        /// <para>This is not a nicety. A uniform grid over a 26 m arena puts four samples 8 m apart and
        /// misses a 6 m doorway entirely, and then reports the walk from the arena into The Ascent as
        /// impossible. Sampling where a player actually stands is both more honest and better resolved.</para>
        /// </summary>
        static List<Vector3> LaunchBand(Box a, Box b, float inset, int n)
        {
            const float Depth = 6f, Spread = 3f;
            float x0 = a.min.x + inset, x1 = a.max.x - inset;
            float z0 = a.min.z + inset, z1 = a.max.z - inset;

            // Along each axis: keep the part of the source that faces the target.
            if (b.min.x >= a.max.x) x0 = Mathf.Max(x0, a.max.x - Depth);
            else if (b.max.x <= a.min.x) x1 = Mathf.Min(x1, a.min.x + Depth);
            else { x0 = Mathf.Max(x0, b.min.x - Spread); x1 = Mathf.Min(x1, b.max.x + Spread); }

            if (b.min.z >= a.max.z) z0 = Mathf.Max(z0, a.max.z - Depth);
            else if (b.max.z <= a.min.z) z1 = Mathf.Min(z1, a.min.z + Depth);
            else { z0 = Mathf.Max(z0, b.min.z - Spread); z1 = Mathf.Min(z1, b.max.z + Spread); }

            if (x1 < x0) { x0 = x1 = Mathf.Clamp(b.Center.x, a.min.x + inset, a.max.x - inset); }
            if (z1 < z0) { z0 = z1 = Mathf.Clamp(b.Center.z, a.min.z + inset, a.max.z - inset); }

            var pts = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                float fx = n == 1 ? 0.5f : i / (float)(n - 1);
                for (int j = 0; j < n; j++)
                {
                    float fz = n == 1 ? 0.5f : j / (float)(n - 1);
                    pts.Add(new Vector3(Mathf.Lerp(x0, x1, fx), a.max.y, Mathf.Lerp(z0, z1, fz)));
                }
            }
            return pts;
        }

        /// <summary>An n×n grid of standing positions on a box's top face, inset so the capsule fits.</summary>
        static List<Vector3> GridOnTop(Box b, float inset, int n)
        {
            var pts = new List<Vector3>();
            float x0 = b.min.x + inset, x1 = b.max.x - inset;
            float z0 = b.min.z + inset, z1 = b.max.z - inset;
            if (x1 < x0) { x0 = x1 = b.Center.x; }
            if (z1 < z0) { z0 = z1 = b.Center.z; }
            for (int i = 0; i < n; i++)
            {
                float fx = n == 1 ? 0.5f : i / (float)(n - 1);
                for (int j = 0; j < n; j++)
                {
                    float fz = n == 1 ? 0.5f : j / (float)(n - 1);
                    pts.Add(new Vector3(Mathf.Lerp(x0, x1, fx), b.max.y, Mathf.Lerp(z0, z1, fz)));
                }
            }
            return pts;
        }

        // ============================================================ a wall-jump chimney

        public struct ChimneyGeometry
        {
            public bool valid;
            public string reason;
            public float width;          // face to face, metres
            public float depth;          // how much of the two faces overlap along the other axis
            public float shortTop, tallTop, baseY;
            public float clearSkyTo;     // how high the corridor is free of ceilings
            public string ceiling;       // what caps it, if anything
        }

        /// <summary>
        /// The static readability check on a pair of facing faces: are they the right distance apart, do
        /// they overlap enough to climb between, and is the column above them open?
        /// </summary>
        public static ChimneyGeometry MeasureChimney(IList<Box> boxes, string tallName, string shortName,
                                                     MoveProfile p)
        {
            var g = new ChimneyGeometry();
            int it = IndexOf(boxes, tallName), isb = IndexOf(boxes, shortName);
            if (it < 0 || isb < 0) { g.reason = "missing platform"; return g; }
            Box t = boxes[it], s = boxes[isb];

            // Which axis do they face across? The one where they do NOT overlap.
            bool xSep = s.min.x >= t.max.x || s.max.x <= t.min.x;
            bool zSep = s.min.z >= t.max.z || s.max.z <= t.min.z;
            if (xSep == zSep) { g.reason = "the two boxes do not face each other across exactly one axis"; return g; }

            if (xSep)
            {
                g.width = s.min.x >= t.max.x ? s.min.x - t.max.x : t.min.x - s.max.x;
                g.depth = Mathf.Min(s.max.z, t.max.z) - Mathf.Max(s.min.z, t.min.z);
            }
            else
            {
                g.width = s.min.z >= t.max.z ? s.min.z - t.max.z : t.min.z - s.max.z;
                g.depth = Mathf.Min(s.max.x, t.max.x) - Mathf.Max(s.min.x, t.min.x);
            }

            g.shortTop = s.max.y;
            g.tallTop = t.max.y;
            g.baseY = Mathf.Max(s.min.y, t.min.y);

            // The column between them, and what — if anything — caps it.
            float cx0, cx1, cz0, cz1;
            if (xSep)
            {
                cx0 = Mathf.Min(t.max.x, s.max.x); cx1 = Mathf.Max(t.min.x, s.min.x);
                if (cx0 > cx1) { float tmp = cx0; cx0 = cx1; cx1 = tmp; }
                cz0 = Mathf.Max(s.min.z, t.min.z); cz1 = Mathf.Min(s.max.z, t.max.z);
            }
            else
            {
                cz0 = Mathf.Min(t.max.z, s.max.z); cz1 = Mathf.Max(t.min.z, s.min.z);
                if (cz0 > cz1) { float tmp = cz0; cz0 = cz1; cz1 = tmp; }
                cx0 = Mathf.Max(s.min.x, t.min.x); cx1 = Mathf.Min(s.max.x, t.max.x);
            }

            g.clearSkyTo = float.MaxValue;
            g.ceiling = "";
            for (int i = 0; i < boxes.Count; i++)
            {
                if (i == it || i == isb) continue;
                var b = boxes[i];
                if (b.max.x <= cx0 || b.min.x >= cx1) continue;
                if (b.max.z <= cz0 || b.min.z >= cz1) continue;
                if (b.min.y <= g.baseY) continue;
                if (b.min.y < g.clearSkyTo) { g.clearSkyTo = b.min.y; g.ceiling = b.name; }
            }

            float capsule = 2f * p.capsuleRadius;
            g.valid = g.width >= 1.5f && g.width <= 3f
                      && g.depth >= capsule + 0.6f
                      && g.clearSkyTo > g.shortTop;
            if (!g.valid)
            {
                if (g.width < 1.5f || g.width > 3f) g.reason = "width " + g.width.ToString("0.00") + " m outside 1.5-3.0 m";
                else if (g.depth < capsule + 0.6f) g.reason = "only " + g.depth.ToString("0.00") + " m of face overlap";
                else g.reason = "capped by " + g.ceiling + " at y " + g.clearSkyTo.ToString("0.00");
            }
            return g;
        }

        // ============================================================ climbing it

        public struct ClimbOutcome
        {
            public bool success;
            public string landedOn;
            public int pushes;
            public float peakY;
            public float minClearance;
            public string note;
        }

        struct ClimbState
        {
            public Vector3 feet;
            public Vector3 vel;
            public int used;
            public bool hasLastWall;
            public Vector3 lastNormal;
            public float minClearance;
            public float peakY;
        }

        /// <summary>
        /// Finds a wall the way <see cref="FirstPersonMotor.FindWall"/> does — nearest face within
        /// <c>wallCheckDistance</c> of the capsule, most directly faced by the direction of travel wins,
        /// and the wall you last pushed off is refused.
        /// </summary>
        static bool FindWall(ClimbState st, MoveProfile p, IList<Box> boxes, out Vector3 normal)
        {
            normal = Vector3.zero;

            // Which way the player is FACING decides which wall wins, and the motor falls back to
            // transform.forward when velocity is negligible. That fallback is load-bearing here: pressed
            // against a chimney wall the CharacterController has already zeroed the horizontal velocity,
            // so a scan that needed velocity would find nothing and every chimney would stall two pushes
            // up. A climbing player is looking at the wall, so with no velocity the NEAREST eligible face
            // wins instead.
            Vector3 pref = new Vector3(st.vel.x, 0f, st.vel.z);
            bool haveDir = pref.sqrMagnitude >= 0.01f;
            if (haveDir) pref.Normalize();

            float r = p.capsuleRadius;
            float y0 = st.feet.y + r, y1 = st.feet.y + p.standHeight - r;
            float best = -2f;
            bool found = false;

            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                // Vertical overlap with the capsule, or it is a floor/ceiling rather than a wall.
                if (b.max.y <= y0 || b.min.y >= y1) continue;

                float dx0 = b.min.x - st.feet.x, dx1 = st.feet.x - b.max.x;
                float dz0 = b.min.z - st.feet.z, dz1 = st.feet.z - b.max.z;

                // Nearest face, and its outward normal.
                float dist; Vector3 n;
                if (dx0 > 0f) { dist = dx0; n = new Vector3(-1f, 0f, 0f); }
                else if (dx1 > 0f) { dist = dx1; n = new Vector3(1f, 0f, 0f); }
                else if (dz0 > 0f) { dist = dz0; n = new Vector3(0f, 0f, -1f); }
                else if (dz1 > 0f) { dist = dz1; n = new Vector3(0f, 0f, 1f); }
                else continue;   // we are inside its footprint

                // ...but only if the OTHER axis actually overlaps, or the "face" is a corner.
                if (n.x != 0f && (st.feet.z < b.min.z - p.capsuleRadius || st.feet.z > b.max.z + p.capsuleRadius)) continue;
                if (n.z != 0f && (st.feet.x < b.min.x - p.capsuleRadius || st.feet.x > b.max.x + p.capsuleRadius)) continue;

                if (dist - r > p.wallCheckDistance) continue;
                if (st.hasLastWall && Vector3.Dot(n, st.lastNormal) > p.sameWallCosineLimit) continue;

                float score = haveDir ? -(n.x * pref.x + n.z * pref.z) : -dist;
                if (score > best) { best = score; normal = n; found = true; }
            }
            return found;
        }

        static void ApplyWallJump(ref ClimbState st, Vector3 n, MoveProfile p)
        {
            float hx = st.vel.x, hz = st.vel.z;
            float into = hx * n.x + hz * n.z;
            if (into < 0f) { hx -= n.x * into; hz -= n.z * into; }
            hx += n.x * p.wallJumpPushSpeed;
            hz += n.z * p.wallJumpPushSpeed;
            float sp = Mathf.Sqrt(hx * hx + hz * hz);
            if (sp > p.dashSpeed) { float k = p.dashSpeed / sp; hx *= k; hz *= k; }
            st.vel = new Vector3(hx, p.wallJumpUpSpeed, hz);
            st.lastNormal = n;
            st.hasLastWall = true;
            st.used++;
        }

        /// <summary>
        /// Climbs a chimney: jump in, then push off alternating faces, searching over WHEN each push is
        /// taken and whether the stick is held or braked. Returns the best outcome found — where you top
        /// out, how many pushes it cost, and whether anything was clipped on the way.
        ///
        /// <para>This is a search, not a single simulation, because the player chooses the timing. If any
        /// sequence within <c>maxWallJumps</c> reaches the exit ledge, the route exists; if none does, it
        /// does not, and the report says what you land on instead.</para>
        /// </summary>
        public static ClimbOutcome ClimbChimney(IList<Box> boxes, MoveProfile p,
                                                Vector3 entryFeet, Vector3 entryVel,
                                                string exitName, string standingOnName, float floorY)
        {
            var best = new ClimbOutcome();
            best.landedOn = "(nothing — fell)";
            best.minClearance = float.MaxValue;

            // Nothing more than 20 m from the chimney can be involved in climbing it.
            boxes = Near(boxes, entryFeet - new Vector3(20f, 30f, 20f), entryFeet + new Vector3(20f, 30f, 20f));
            int exitIdx = IndexOf(boxes, exitName);
            int standingOn = IndexOf(boxes, standingOnName);

            var st = new ClimbState();
            st.feet = entryFeet;
            st.vel = entryVel;
            st.minClearance = float.MaxValue;
            st.peakY = entryFeet.y;

            Recurse(st, boxes, p, exitIdx, standingOn, floorY, 0, ref best);
            return best;
        }

        // Push timings sampled per airborne segment: the instant the wall comes into reach, a beat later,
        // and AT THE APEX (0.36 s is where an 11 m/s push tops out under gravity -30). The apex sample is
        // the one that matters — a push taken there buys the full 2.02 m of climb, and a search that only
        // ever pushed on contact concluded the chimney tops out 1.5 m short.
        static readonly float[] PushDelays = { 0.02f, 0.20f, 0.36f };

        /// <summary>When the player starts holding back after the final push. MaxValue = never.</summary>
        static readonly float[] BrakeDelays = { float.MaxValue, 0f, 0.12f, 0.24f, 0.36f };

        static void Recurse(ClimbState st, IList<Box> boxes, MoveProfile p, int exitIdx, int standingOn,
                            float floorY, int depth, ref ClimbOutcome best)
        {
            // NOT >=: at depth == maxWallJumps the pushes are spent but the player is still in the air,
            // and that final unpowered segment is the one that decides where they land. Returning here
            // threw away every route that used all five pushes — which is every route worth having.
            if (depth > p.maxWallJumps) return;

            // Every distinct push time from here, plus two ways of NOT pushing again: coast, or brake.
            // Braking matters and cannot be left out: the push leaves at 12 m/s and the ledges are 4 m
            // across, so a simulation that never touches the stick concludes every wall route overshoots
            // into the void. A player holding back sheds that in a third of a second.
            for (int di = 0; di < PushDelays.Length + BrakeDelays.Length; di++)
            {
                bool neverPush = di >= PushDelays.Length;
                float brakeAt = neverPush ? BrakeDelays[di - PushDelays.Length] : float.MaxValue;
                var s = st;
                float eligibleFor = -1f;
                float t = 0f;
                float r = p.SweptRadius, h = p.standHeight;

                while (t < 3f)
                {
                    // Gravity: the jump is held for a chimney — you are pressing it to push off anyway.
                    s.vel.y += p.gravity * Dt;

                    // Air control on the way to the landing, mirrored from FirstPersonMotor.AirAccelerate.
                    // WHEN the brake goes on is the whole question: hold back the instant you leave the
                    // wall and you stop dead against its face and drop; wait too long and you sail past
                    // the ledge. Searching the delay is searching what the player's hand does.
                    if (t >= brakeAt)
                    {
                        Vector3 flat = new Vector3(s.vel.x, 0f, s.vel.z);
                        if (flat.sqrMagnitude > 0.0001f)
                        {
                            Vector3 wish = -flat.normalized;
                            float current = s.vel.x * wish.x + s.vel.z * wish.z;
                            float add = p.groundSpeed - current;
                            if (add > 0f)
                            {
                                float acc = Mathf.Min(p.airAccel * Dt, add);
                                s.vel.x += wish.x * acc;
                                s.vel.z += wish.z * acc;
                            }
                        }
                    }
                    Vector3 prev = s.feet;
                    s.feet += s.vel * Dt;
                    t += Dt;
                    if (s.feet.y > s.peakY) s.peakY = s.feet.y;

                    // Collision: a wall stops the horizontal component, a ceiling stops the rise, a floor
                    // is a landing.
                    float y0 = s.feet.y + r, y1 = s.feet.y + h - r;
                    bool landed = false;
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        if (i == standingOn && t < 0.05f) continue;
                        var b = boxes[i];
                        float d = SegmentBoxDistance(s.feet.x, s.feet.z, y0, y1, b) - r;
                        if (d >= 0f) { if (d < s.minClearance) s.minClearance = d; continue; }

                        if (s.vel.y <= 0f && prev.y >= b.max.y - 0.03f)
                        {
                            // Topped out.
                            var o = new ClimbOutcome();
                            o.landedOn = b.name;
                            o.pushes = s.used;
                            o.peakY = s.peakY;
                            o.minClearance = s.minClearance == float.MaxValue ? 99f : s.minClearance;
                            o.success = i == exitIdx;
                            o.note = o.success ? "topped out on the exit ledge"
                                               : "landed on " + b.name + " instead of " + (exitIdx >= 0 ? boxes[exitIdx].name : "?");
                            if (Better(o, best)) best = o;
                            landed = true;
                            break;
                        }

                        // A wall or a ceiling. Push out along the shallowest axis and kill that component,
                        // the way a CharacterController does.
                        float px = Mathf.Min(b.max.x - s.feet.x, s.feet.x - b.min.x);
                        float pz = Mathf.Min(b.max.z - s.feet.z, s.feet.z - b.min.z);
                        if (b.min.y > prev.y + h - r && s.vel.y > 0f) { s.vel.y = 0f; s.feet.y = b.min.y - h + r - 0.001f; }
                        else if (px < pz) { s.vel.x = 0f; s.feet.x = prev.x; }
                        else { s.vel.z = 0f; s.feet.z = prev.z; }
                    }
                    if (landed) break;

                    if (s.feet.y < floorY)
                    {
                        var o = new ClimbOutcome();
                        o.landedOn = "(nothing — fell out of the world)";
                        o.pushes = s.used;
                        o.peakY = s.peakY;
                        o.minClearance = s.minClearance == float.MaxValue ? 99f : s.minClearance;
                        if (Better(o, best)) best = o;
                        break;
                    }

                    if (neverPush || s.used >= p.maxWallJumps) continue;

                    Vector3 n;
                    bool haveWall = FindWall(s, p, boxes, out n);
                    if (!haveWall) { eligibleFor = -1f; continue; }
                    if (eligibleFor < 0f) eligibleFor = 0f; else eligibleFor += Dt;

                    if (eligibleFor >= PushDelays[di] - 0.0011f)
                    {
                        ApplyWallJump(ref s, n, p);
                        Recurse(s, boxes, p, exitIdx, standingOn, floorY, depth + 1, ref best);
                        break;   // this branch is continued inside the recursion
                    }
                }
            }
        }

        static bool Better(ClimbOutcome a, ClimbOutcome b)
        {
            if (a.success != b.success) return a.success;
            if (a.success) return a.pushes < b.pushes || (a.pushes == b.pushes && a.minClearance > b.minClearance);
            return a.peakY > b.peakY;
        }

        // ============================================================ slide gates

        public struct LintelVerdict
        {
            public bool exists, slideFits, standingBlocked, jumpable, spansTheDeck;
            public float clearance, topAboveDeck;
            public string Summary(string lintel, string deck)
            {
                if (!exists) return lintel + " or " + deck + " missing";
                return string.Format("{0} over {1}: clearance {2:0.00} m (slide {3}, standing {4}), " +
                                     "top {5:0.00} m above the deck ({6}), spans the deck: {7}",
                                     lintel, deck, clearance,
                                     slideFits ? "fits" : "DOES NOT FIT",
                                     standingBlocked ? "blocked" : "NOT BLOCKED — the gate does nothing",
                                     topAboveDeck, jumpable ? "jumpable" : "NOT JUMPABLE — this walls a player in",
                                     spansTheDeck);
            }
        }

        /// <summary>
        /// The three properties a slide-under lintel has to have, and the reason the T1 fallen obelisk is
        /// fair: a slide fits, standing does not, and it can still simply be jumped over so it costs time
        /// rather than access.
        /// </summary>
        public static LintelVerdict CheckLintel(IList<Box> boxes, string lintelName, string deckName, MoveProfile p)
        {
            var v = new LintelVerdict();
            int il = IndexOf(boxes, lintelName), id = IndexOf(boxes, deckName);
            if (il < 0 || id < 0) return v;
            v.exists = true;
            Box l = boxes[il], d = boxes[id];

            v.clearance = l.min.y - d.max.y;
            v.topAboveDeck = l.max.y - d.max.y;
            v.slideFits = v.clearance > p.slideHeight + 0.05f;
            v.standingBlocked = v.clearance < p.standHeight;
            v.jumpable = v.topAboveDeck < p.jumpHeight - 0.2f;
            v.spansTheDeck = l.min.x <= d.min.x && l.max.x >= d.max.x;
            return v;
        }

        // ============================================================ report helper

        public static string Describe(MoveProfile p)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PLAYER, read off " + PlayerPrefabPath);
            sb.AppendLine("  gravity            " + p.gravity.ToString("0.0"));
            sb.AppendLine("  jumpHeight         " + p.jumpHeight.ToString("0.00") + " m  (takeoff " + p.JumpTakeoffSpeed.ToString("0.00") + " m/s)");
            sb.AppendLine("  groundSpeed        " + p.groundSpeed.ToString("0.0") + " m/s");
            sb.AppendLine("  slide-jump speed   " + p.SlideJumpSpeed.ToString("0.0") + " m/s");
            sb.AppendLine("  wall push          up " + p.wallJumpUpSpeed.ToString("0.0") + " / out " + p.wallJumpPushSpeed.ToString("0.0") + " m/s, max " + p.maxWallJumps);
            sb.AppendLine("  wall check         " + p.wallCheckDistance.ToString("0.00") + " m past the capsule");
            sb.AppendLine("  capsule            r " + p.capsuleRadius.ToString("0.00") + "  h " + p.standHeight.ToString("0.00") + "  skin " + p.skinWidth.ToString("0.00") + "  (slide h " + p.slideHeight.ToString("0.00") + ")");
            return sb.ToString();
        }
    }
}
