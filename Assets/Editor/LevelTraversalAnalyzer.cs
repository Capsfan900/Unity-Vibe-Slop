using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// The parkour-first traversal primitives, proven the way <see cref="LevelArcAnalyzer"/> proves a hop:
    /// by flying the shipped capsule with the shipped numbers, off the definition, with no scene.
    ///
    /// <list type="bullet">
    /// <item><b>A balloon POP</b>: vertical replaced by the orb's launch speed, horizontal trimmed to the
    /// motor's <c>launchCarryCap</c>, gravity scaled by <c>launchGravityScale</c> for
    /// <c>launchFloatSeconds</c>, then the ordinary fall. The re-armed dash is modelled as one dash's
    /// worth of horizontal travel (<c>dashSpeed × dashDuration</c>) available once per link.</item>
    /// <item><b>A shooter's line</b>: a straight segment from the perch's muzzle to a deck's standing
    /// point, inside the shooter's band and not crossing any box.</item>
    /// <item><b>Water</b>: a sheet is a floor; the run along it leaves at the water floor speed.</item>
    /// </list>
    /// </summary>
    public static class LevelTraversalAnalyzer
    {
        const float Dt = 0.002f;

        // ============================================================ lines of sight

        /// <summary>True when the segment a→b crosses no box (slab test per box).</summary>
        public static bool LineClear(Vector3 a, Vector3 b, IList<LevelArcAnalyzer.Box> boxes, int ignore = -1)
        {
            Vector3 d = b - a;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (i == ignore) continue;
                var bx = boxes[i];
                float t0 = 0f, t1 = 1f;
                bool hit = true;
                for (int axis = 0; axis < 3 && hit; axis++)
                {
                    float o = a[axis], dd = d[axis], lo = bx.min[axis], hi = bx.max[axis];
                    if (Mathf.Abs(dd) < 1e-6f)
                    {
                        if (o < lo || o > hi) hit = false;
                        continue;
                    }
                    float ta = (lo - o) / dd, tb = (hi - o) / dd;
                    if (ta > tb) { float tmp = ta; ta = tb; tb = tmp; }
                    t0 = Mathf.Max(t0, ta); t1 = Mathf.Min(t1, tb);
                    if (t0 > t1) hit = false;
                }
                if (hit) return false;
            }
            return true;
        }

        public struct ShooterVerdict
        {
            public string perch, spawn;
            public List<string> covered, blocked, outOfBand;
            /// <summary>
            /// Decks this perch covers that the player CANNOT ANSWER while running the route: the
            /// bearing to the muzzle is more than the parry's facing cone away from the direction the
            /// route travels, so turning to the bolt means turning off the line. F6 of the bolt-timing
            /// plan — the point is that bad perch PLACEMENT becomes a measured fault caught by the arc
            /// report, instead of something a player discovers by dying to it.
            /// </summary>
            public List<string> forcedLookAway;
            public string Summary()
            {
                string baseLine = string.Format("{0} ({1}): covers {2}; blocked {3}; out of band {4}", perch, spawn,
                                     covered.Count == 0 ? "-" : string.Join(",", covered.ToArray()),
                                     blocked.Count == 0 ? "-" : string.Join(",", blocked.ToArray()),
                                     outOfBand.Count == 0 ? "-" : string.Join(",", outOfBand.ToArray()));
                if (forcedLookAway != null && forcedLookAway.Count > 0)
                    baseLine += "; FORCED LOOK-AWAY " + string.Join(",", forcedLookAway.ToArray());
                return baseLine;
            }
        }

        /// <summary>
        /// Which of the named decks a shooter standing on <paramref name="perchName"/> can put a bolt
        /// across: the muzzle (feet + 1.5 m) to the deck centre's chest height (top + 1.2 m), inside
        /// [minRange, maxRange], crossing nothing but the perch and the target deck.
        /// </summary>
        public static ShooterVerdict AnalyzeShooter(IList<LevelArcAnalyzer.Box> boxes, string perchName, string spawnName,
                                                    IEnumerable<string> decks, float minRange, float maxRange)
        {
            var v = new ShooterVerdict { perch = perchName, spawn = spawnName,
                                         covered = new List<string>(), blocked = new List<string>(), outOfBand = new List<string>() };
            int ip = LevelArcAnalyzer.IndexOf(boxes, perchName);
            if (ip < 0) { v.blocked.Add("missing perch"); return v; }
            var perch = boxes[ip];
            Vector3 muzzle = new Vector3((perch.min.x + perch.max.x) * 0.5f, perch.max.y + 1.5f, (perch.min.z + perch.max.z) * 0.5f);
            foreach (var d in decks)
            {
                int id = LevelArcAnalyzer.IndexOf(boxes, d);
                if (id < 0) { v.blocked.Add(d + "?"); continue; }
                var deck = boxes[id];
                Vector3 chest = new Vector3((deck.min.x + deck.max.x) * 0.5f, deck.max.y + 1.2f, (deck.min.z + deck.max.z) * 0.5f);
                float dist = Vector3.Distance(muzzle, chest);
                if (dist < minRange || dist > maxRange) { v.outOfBand.Add(d); continue; }
                // Ignore the perch and the target deck (the segment starts above one and ends above the other).
                var others = new List<LevelArcAnalyzer.Box>();
                for (int i = 0; i < boxes.Count; i++) if (i != ip && i != id) others.Add(boxes[i]);
                if (LineClear(muzzle, chest, others)) v.covered.Add(d); else v.blocked.Add(d);
            }
            return v;
        }

        /// <summary>
        /// The angle, in degrees, between the direction the ROUTE travels across
        /// <paramref name="deckName"/> and the bearing from that deck to the muzzle. Zero means the bolt
        /// comes from straight ahead; 180 means it comes from directly behind.
        ///
        /// <para>The tangent is taken from the hop LEAVING the deck when there is one, because that is
        /// where the player is looking while standing on it; the arriving hop is the fallback for the
        /// last deck of a route. Both are flattened to XZ — a bolt from above is still answerable, and
        /// pitch is not what the facing cone tests.</para>
        /// </summary>
        public static float RouteBearingOffsetDeg(IList<LevelArcAnalyzer.Box> boxes, string deckName,
                                                  Vector3 muzzle, string prevDeck, string nextDeck)
        {
            int id = LevelArcAnalyzer.IndexOf(boxes, deckName);
            if (id < 0) return 0f;
            var deck = boxes[id];
            Vector3 chest = new Vector3((deck.min.x + deck.max.x) * 0.5f, deck.max.y + 1.2f, (deck.min.z + deck.max.z) * 0.5f);

            Vector3 tangent = Vector3.zero;
            int inext = string.IsNullOrEmpty(nextDeck) ? -1 : LevelArcAnalyzer.IndexOf(boxes, nextDeck);
            if (inext >= 0)
            {
                var nb = boxes[inext];
                tangent = new Vector3((nb.min.x + nb.max.x) * 0.5f - chest.x, 0f, (nb.min.z + nb.max.z) * 0.5f - chest.z);
            }
            if (tangent.sqrMagnitude < 1e-4f)
            {
                int iprev = string.IsNullOrEmpty(prevDeck) ? -1 : LevelArcAnalyzer.IndexOf(boxes, prevDeck);
                if (iprev < 0) return 0f;                       // no route context: cannot judge, do not cry wolf
                var pb = boxes[iprev];
                tangent = new Vector3(chest.x - (pb.min.x + pb.max.x) * 0.5f, 0f, chest.z - (pb.min.z + pb.max.z) * 0.5f);
            }
            if (tangent.sqrMagnitude < 1e-4f) return 0f;

            Vector3 toMuzzle = new Vector3(muzzle.x - chest.x, 0f, muzzle.z - chest.z);
            if (toMuzzle.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.Angle(tangent.normalized, toMuzzle.normalized);
        }

        /// <summary>
        /// <see cref="AnalyzeShooter"/> plus F6's placement check. <paramref name="routeOf"/> maps a deck
        /// name to its (previous, next) neighbours on the baseline route; a deck the route does not visit
        /// is simply not judged, because there is no direction of travel to be wrong about.
        ///
        /// <para><paramref name="coneDeg"/> is the parry's own facing cone read off the shipped data, not
        /// a literal — a report that invents its own threshold lies the moment the data is retuned.</para>
        /// </summary>
        public static ShooterVerdict AnalyzeShooterPlacement(IList<LevelArcAnalyzer.Box> boxes, string perchName,
                                                             string spawnName, IEnumerable<string> decks,
                                                             float minRange, float maxRange, float coneDeg,
                                                             System.Func<string, string[]> routeOf)
        {
            var v = AnalyzeShooter(boxes, perchName, spawnName, decks, minRange, maxRange);
            v.forcedLookAway = new List<string>();
            int ip = LevelArcAnalyzer.IndexOf(boxes, perchName);
            if (ip < 0 || routeOf == null) return v;
            var perch = boxes[ip];
            Vector3 muzzle = new Vector3((perch.min.x + perch.max.x) * 0.5f, perch.max.y + 1.5f, (perch.min.z + perch.max.z) * 0.5f);

            foreach (var d in v.covered)
            {
                string[] pn = routeOf(d);
                if (pn == null) continue;                       // off-route deck: nothing to be wrong about
                float off = RouteBearingOffsetDeg(boxes, d, muzzle, pn[0], pn[1]);
                if (off > coneDeg) v.forcedLookAway.Add(d + "(" + off.ToString("0") + "deg)");
            }
            return v;
        }

        // ============================================================ balloon chains

        public struct LinkVerdict
        {
            public string from, to;
            public bool reached;
            public bool neededDash;
            public float horizontal, rise;
            public float closest;           // metres from the capsule centre to the orb at closest approach
            public string obstruction;
            public string Summary()
            {
                return string.Format("{0} -> {1}: {2}  ({3:0.0} m across, {4:+0.0;-0.0} m rise, closest {5:0.00} m{6}{7})",
                                     from, to, reached ? "ok" : "MISS", horizontal, rise, closest,
                                     neededDash ? ", with the dash" : "",
                                     string.IsNullOrEmpty(obstruction) ? "" : ", obstructed by " + obstruction);
            }
        }

        public struct ChainVerdict
        {
            public List<LinkVerdict> links;
            public bool complete;
            public string Summary()
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(complete ? "CHAIN ok" : "CHAIN BROKEN");
                foreach (var l in links) sb.Append("\n    ").Append(l.Summary());
                return sb.ToString();
            }
        }

        /// <summary>
        /// Flies the chain: a run-jump off <paramref name="fromDeck"/> into the first orb, then a pop out
        /// of each orb toward the next, and a final pop from the last orb onto <paramref name="toDeck"/>.
        /// A link that falls short is retried with one dash's worth of horizontal travel added at the
        /// float's end (the re-armed dash); a link that needs it is reported as such.
        /// </summary>
        public static ChainVerdict AnalyzeChain(IList<LevelArcAnalyzer.Box> boxes, IList<BalloonDef> orbs,
                                                string fromDeck, string toDeck, LevelArcAnalyzer.MoveProfile p, float floorY)
        {
            var v = new ChainVerdict { links = new List<LinkVerdict>(), complete = true };
            int ia = LevelArcAnalyzer.IndexOf(boxes, fromDeck), ib = LevelArcAnalyzer.IndexOf(boxes, toDeck);
            if (ia < 0 || ib < 0 || orbs == null || orbs.Count == 0) { v.complete = false; return v; }
            var a = boxes[ia];

            // ---- deck -> first orb: a run-jump from the deck edge nearest the orb.
            {
                var o0 = orbs[0];
                Vector3 feet = new Vector3(Mathf.Clamp(o0.position.x, a.min.x + p.SweptRadius, a.max.x - p.SweptRadius), a.max.y,
                                           Mathf.Clamp(o0.position.z, a.min.z + p.SweptRadius, a.max.z - p.SweptRadius));
                Vector3 flat = o0.position - feet; flat.y = 0f;
                Vector3 vel = (flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward) * p.groundSpeed;
                vel.y = Mathf.Sqrt(2f * -p.gravity * p.jumpHeight);
                var l = FlyToOrb(feet, vel, o0, boxes, p, ia, true, 0f, false);
                l.from = fromDeck; l.to = o0.name;
                if (!l.reached) v.complete = false;
                v.links.Add(l);
            }

            // ---- orb -> orb
            for (int i = 0; i + 1 < orbs.Count; i++)
            {
                var l = PopTo(orbs[i], orbs[i + 1], boxes, p, false);
                if (!l.reached) l = PopTo(orbs[i], orbs[i + 1], boxes, p, true);
                if (!l.reached) v.complete = false;
                v.links.Add(l);
            }

            // ---- last orb -> deck: land anywhere on the deck's top.
            {
                var last = orbs[orbs.Count - 1];
                var b = boxes[ib];
                Vector3 aim = new Vector3(Mathf.Clamp(last.position.x, b.min.x, b.max.x), b.max.y, Mathf.Clamp(last.position.z + 4f, b.min.z, b.max.z));
                var l = PopToDeck(last, aim, ib, boxes, p, floorY, false);
                if (!l.reached) l = PopToDeck(last, aim, ib, boxes, p, floorY, true);
                l.from = last.name; l.to = toDeck;
                if (!l.reached) v.complete = false;
                v.links.Add(l);
            }
            return v;
        }

        static Vector3 PopVelocity(BalloonDef from, Vector3 target, LevelArcAnalyzer.MoveProfile p)
        {
            Vector3 flat = target - from.position; flat.y = 0f;
            Vector3 dir = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
            // The carry a chaining player has at the orb is the previous pop's trimmed carry; the cap is the
            // most the motor lets through, so it is the honest upper bound of a chain.
            return dir * p.launchCarryCap + Vector3.up * from.launchSpeed;
        }

        static LinkVerdict PopTo(BalloonDef from, BalloonDef to, IList<LevelArcAnalyzer.Box> boxes, LevelArcAnalyzer.MoveProfile p, bool dash)
        {
            Vector3 feet = from.position - Vector3.up * (p.standHeight * 0.5f);   // the orb pops at the capsule's centre
            var l = FlyToOrb(feet, PopVelocity(from, to.position, p), to, boxes, p, -1, false, p.launchFloatSeconds, dash);
            l.from = from.name; l.to = to.name;
            return l;
        }

        static LinkVerdict PopToDeck(BalloonDef from, Vector3 aim, int deckIndex, IList<LevelArcAnalyzer.Box> boxes,
                                     LevelArcAnalyzer.MoveProfile p, float floorY, bool dash)
        {
            Vector3 feet = from.position - Vector3.up * (p.standHeight * 0.5f);
            Vector3 vel = PopVelocity(from, aim, p);
            var l = new LinkVerdict { neededDash = dash, closest = float.MaxValue };
            Vector3 flat = aim - from.position; flat.y = 0f; l.horizontal = flat.magnitude; l.rise = aim.y - feet.y;
            var deck = boxes[deckIndex];
            float t = 0f, floatLeft = p.launchFloatSeconds; bool dashed = !dash;
            Vector3 prev = feet;
            while (t < 6f)
            {
                float g = floatLeft > 0f ? p.gravity * p.launchGravityScale : p.GravityFor(vel.y, false);
                vel.y += g * Dt; floatLeft -= Dt;
                if (!dashed && floatLeft <= 0f) { dashed = true; Vector3 d = new Vector3(vel.x, 0f, vel.z).normalized; feet += d * p.dashSpeed * p.dashDuration; }
                prev = feet; feet += vel * Dt; t += Dt;
                float r = p.SweptRadius;
                for (int i = 0; i < boxes.Count; i++)
                {
                    var b = boxes[i];
                    float d = LevelArcAnalyzer.SegmentBoxDistance(feet.x, feet.z, feet.y + r, feet.y + p.standHeight - r, b) - r;
                    if (d < 0f)
                    {
                        bool fromAbove = vel.y <= 0f && prev.y >= b.max.y - 0.03f;
                        if (i == deckIndex && fromAbove) { l.reached = true; l.closest = 0f; return l; }
                        l.obstruction = b.name; return l;
                    }
                }
                if (feet.y < floorY) break;
            }
            return l;
        }

        static LinkVerdict FlyToOrb(Vector3 feet, Vector3 vel, BalloonDef orb, IList<LevelArcAnalyzer.Box> boxes,
                                    LevelArcAnalyzer.MoveProfile p, int ignore, bool holdJump, float floatSeconds, bool dash)
        {
            var l = new LinkVerdict { neededDash = dash, closest = float.MaxValue };
            Vector3 flat = orb.position - feet; flat.y = 0f; l.horizontal = flat.magnitude;
            l.rise = orb.position.y - (feet.y + p.standHeight * 0.5f);
            float t = 0f, floatLeft = floatSeconds; bool dashed = !dash;
            float r = p.SweptRadius;
            while (t < 4f)
            {
                float g = floatLeft > 0f ? p.gravity * p.launchGravityScale : p.GravityFor(vel.y, holdJump);
                vel.y += g * Dt; floatLeft -= Dt;
                if (!dashed && floatLeft <= 0f) { dashed = true; Vector3 d = new Vector3(vel.x, 0f, vel.z).normalized; feet += d * p.dashSpeed * p.dashDuration; }
                feet += vel * Dt; t += Dt;
                Vector3 centre = feet + Vector3.up * (p.standHeight * 0.5f);
                float dist = Vector3.Distance(centre, orb.position);
                if (dist < l.closest) l.closest = dist;
                if (dist <= orb.radius + r) { l.reached = true; return l; }
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (i == ignore) continue;
                    var b = boxes[i];
                    if (LevelArcAnalyzer.SegmentBoxDistance(feet.x, feet.z, feet.y + r, feet.y + p.standHeight - r, b) - r < 0f)
                    { l.obstruction = b.name; return l; }
                }
                if (vel.y < 0f && feet.y < orb.position.y - orb.radius - p.standHeight) break;   // fallen past it
            }
            return l;
        }

        // ============================================================ water

        public struct WaterVerdict
        {
            public string name, deck;
            public bool onADeck, insideDeck, flowIsUnit;
            public float lift;              // sheet bottom minus deck top; should be ~0
            public string Summary()
            {
                return string.Format("{0}: {1} on {2} (lift {3:0.00} m, inside {4}, flow unit {5})", name,
                                     onADeck ? "ok" : "FLOATING", deck ?? "nothing", lift, insideDeck, flowIsUnit);
            }
        }

        /// <summary>A sheet must lie on a platform top, inside it in plan, with a unit flow.</summary>
        public static WaterVerdict AnalyzeWater(IList<LevelArcAnalyzer.Box> boxes, WaterDef w)
        {
            var v = new WaterVerdict { name = w.name, flowIsUnit = Mathf.Abs(w.flowDirection.magnitude - 1f) < 0.01f || w.flowDirection == Vector3.zero, lift = float.MaxValue };
            float bottom = w.center.y - w.size.y * 0.5f;
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                bool inPlan = w.center.x - w.size.x * 0.5f >= b.min.x - 0.01f && w.center.x + w.size.x * 0.5f <= b.max.x + 0.01f &&
                              w.center.z - w.size.z * 0.5f >= b.min.z - 0.01f && w.center.z + w.size.z * 0.5f <= b.max.z + 0.01f;
                float lift = bottom - b.max.y;
                if (Mathf.Abs(lift) < 0.06f && w.center.x >= b.min.x && w.center.x <= b.max.x && w.center.z >= b.min.z && w.center.z <= b.max.z)
                {
                    v.onADeck = true; v.deck = b.name; v.lift = lift; v.insideDeck = inPlan;
                    return v;
                }
            }
            return v;
        }
    }
}
