using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class SolarArenaTests
    {
        const float Eps = 0.001f;
        LevelDefinition def;

        [SetUp]
        public void Load()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            Assert.IsNotNull(shipped);
            def = Object.Instantiate(shipped);
        }

        [TearDown] public void Clean() { Object.DestroyImmediate(def); }

        [Test]
        public void SolarMigrationIsIdempotent_AndKeepsHistoricalSpawnAnchors()
        {
            var before = def.spawns.Where(s => s.name == "Spawn_Legendary_Ninja" ||
                                               s.name == "Spawn_Legendary_Knight" ||
                                               s.name == "Spawn_Legendary_Spellsword" ||
                                               s.name == LevelDefinitionAuthoring.GrapplerSpawner ||
                                               s.name == "Spawn_Boss")
                                   .ToDictionary(s => s.name, s => s.position);
            LevelDefinitionAuthoring.ApplySolarRealms(def);
            string once = EditorJsonUtility.ToJson(def, true);
            LevelDefinitionAuthoring.ApplySolarRealms(def);
            Assert.AreEqual(once, EditorJsonUtility.ToJson(def, true),
                "a second solar migration changed serialized level data");
            foreach (var pair in before)
                Assert.AreEqual(pair.Value, def.spawns.Single(s => s.name == pair.Key).position,
                    pair.Key + " must move only in the built scene, never in authored data");
        }

        // The five gated fights in course order. T4_Gate is the fourth mini realm at the foot of the
        // descent (2026-09-13); Boss_Gate is the Warden, 72 m further on than before it existed.
        static readonly string[] Gates = { "T1_Gate", "T2_Gate", "T3_Gate", LevelDefinitionAuthoring.GrapplerGate, "Boss_Gate" };

        [Test]
        public void ShippedLevelHasFiveExactSolarAnchorsAndThemes()
        {
            string[] themes = { "SolarCyan", "SolarGold", "SolarAzure", "SolarViolet", "SolarGhost" };
            Vector3[] centers =
            {
                new Vector3(0f, 8.2f, 87.3f), new Vector3(0f, 24.55f, 216.8f),
                new Vector3(0f, 32.2f, 356.3f), new Vector3(0f, 22.3f, 479.7f), new Vector3(0f, 22.3f, 558.3f)
            };
            float[] radii = { 16f, 17f, 16f, 16f, 25f };
            float[] visualRadii = { 22f, 23f, 22f, 22f, 31f };

            for (int i = 0; i < Gates.Length; i++)
            {
                var realm = def.arenas.Single(a => a.gateName == Gates[i]).solarRealm;
                Assert.IsNotNull(realm, Gates[i]);
                Assert.IsTrue(realm.enabled, Gates[i]);
                Assert.AreEqual(themes[i], realm.themeMaterialKey, Gates[i]);
                Assert.That(Vector3.Distance(centers[i], realm.exteriorCenter), Is.LessThan(Eps), Gates[i]);
                Assert.That(realm.exteriorRadius, Is.EqualTo(radii[i]).Within(Eps), Gates[i]);
                Assert.That(realm.visualRadius, Is.EqualTo(visualRadii[i]).Within(Eps), Gates[i]);
                Assert.Greater(realm.visualRadius, realm.exteriorRadius,
                    Gates[i] + " visual shell must not advance the physical portal boundary");
                Assert.That(realm.realmFloorRadius, Is.EqualTo(30f).Within(Eps), Gates[i]);
                Assert.That(realm.realmShellRadius, Is.EqualTo(45f).Within(Eps), Gates[i]);
                Assert.That(realm.realmWallHeight, Is.EqualTo(24f).Within(Eps), Gates[i]);
                Assert.That(realm.realmCeilingHeight, Is.EqualTo(24f).Within(Eps), Gates[i]);
                Assert.IsFalse(string.IsNullOrEmpty(realm.arenaPickupName), Gates[i] + " pickup");
                Assert.Less(Vector3.Distance(realm.arenaPickupPosition, realm.realmCenter), realm.realmFloorRadius,
                    Gates[i] + " pickup must sit inside its realm floor");
            }
        }

        [Test]
        public void RealmCellsAreOneSizeSpacedAHundredMetresApartInCourseOrder()
        {
            // The plan's numbers: 1.5x the 2026-09-07 cell (20 -> 30 floor, 30 -> 45 shell), 24 m of wall
            // and ceiling for a lift-and-throw, centres 100 m apart at x 700 in course order.
            for (int i = 0; i < Gates.Length; i++)
            {
                var r = def.arenas.Single(a => a.gateName == Gates[i]).solarRealm;
                Assert.That(Vector3.Distance(r.realmCenter, new Vector3(700f, 0f, 100f * i)), Is.LessThan(Eps), Gates[i] + " cell");
                Assert.That(Vector3.Distance(r.playerEntryPosition - r.realmCenter, new Vector3(0f, 1.2f, -19.5f)), Is.LessThan(Eps), Gates[i] + " entry");
                Assert.That(Vector3.Distance(r.enemySpawnPosition - r.realmCenter, new Vector3(0f, 0.1f, 6f)), Is.LessThan(Eps), Gates[i] + " enemy");
                if (r.hasReturn)
                    Assert.That(Vector3.Distance(r.realmExitPosition - r.realmCenter, new Vector3(0f, 1.5f, -26f)), Is.LessThan(Eps), Gates[i] + " exit");
                Assert.That(Mathf.Abs(r.arenaPickupPosition.x - r.realmCenter.x), Is.EqualTo(10.5f).Within(Eps), Gates[i] + " pickup x");
                // Every realm-local point keeps at least a metre of floor beyond it, and the wall top corner
                // stays inside the opaque shell so the room never shows the campaign sky.
                float cornerToShellCentre = Mathf.Sqrt(Mathf.Pow(r.realmFloorRadius + 0.35f + 0.5f, 2f) +
                                                       Mathf.Pow(r.realmWallHeight - r.realmCeilingHeight * 0.5f, 2f));
                Assert.Less(cornerToShellCentre, r.realmShellRadius, Gates[i] + " wall corner pokes through the shell");
            }
        }

        [Test]
        public void SolarTransitionsShipAtTheirAuditedGatesRetriesAndReturns()
        {
            string[] gates = Gates;
            float[] entries = { 63.25f, 191.75f, 332.25f, 455.9f, 525.3f };
            float[] triggers = { 78.3f, 206.8f, 345.3f, 470.7f, 543.3f };
            float[] exits = { 145.625f, 264.75f, 390.75f, 514.2f, 0f };
            Vector3[] retries =
            {
                new Vector3(0f, 2.2f, 62f), new Vector3(0f, 20.2f, 188f),
                new Vector3(-3f, 27.2f, 329f), new Vector3(0f, 16.2f, 450f), new Vector3(0f, 16.2f, 522f)
            };
            Vector3[] returns =
            {
                new Vector3(10f, 5.2f, 151.375f), new Vector3(2.75f, 21.7f, 268f),
                new Vector3(0f, 28.2f, 393.15f), new Vector3(0f, 16.2f, 517.6f), Vector3.zero
            };

            for (int i = 0; i < gates.Length; i++)
            {
                var arena = def.arenas.Single(a => a.gateName == gates[i]);
                Assert.That(arena.gateClosedPosition.z, Is.EqualTo(entries[i]).Within(Eps), gates[i] + " entry");
                Assert.That(arena.triggerPosition.z, Is.EqualTo(triggers[i]).Within(Eps), gates[i] + " trigger");
                if (arena.hasExitGate)
                    Assert.That(arena.exitGateClosedPosition.z, Is.EqualTo(exits[i]).Within(Eps), gates[i] + " exit");
                Assert.That(Vector3.Distance(arena.solarRealm.retryPosition, retries[i]), Is.LessThan(Eps), gates[i] + " retry");
                Assert.That(Vector3.Distance(arena.solarRealm.returnPosition, returns[i]), Is.LessThan(Eps), gates[i] + " return");
            }
        }

        [Test]
        public void RigidSectionSpacingCarriesEveryAuthoredContentType()
        {
            Assert.That(def.platforms.Single(p => p.name == "T2_L1").center.z, Is.EqualTo(151.375f).Within(Eps));
            Assert.That(def.platforms.Single(p => p.name == "T2_L11").center.z, Is.EqualTo(183.5f).Within(Eps));
            Assert.That(def.spawns.Single(s => s.name == "Spawn_T2_GruntA").position.z, Is.EqualTo(185f).Within(Eps));
            Assert.That(def.pickups.Single(p => p.name == "Pickup_T2_Hook").position.z, Is.EqualTo(151.375f).Within(Eps));
            Assert.That(def.checkpoints.Single(c => c.name == "Checkpoint_2").position.z, Is.EqualTo(151.375f).Within(Eps));
            Assert.That(def.torches.Single(t => t.name == "Torch_T2_Mid").basePosition.z, Is.EqualTo(142f).Within(Eps));

            Assert.That(def.platforms.Single(p => p.name == "T3_Pillar_1").center.z, Is.EqualTo(266f).Within(Eps));
            float t3GruntZ = LevelDefinitionAuthoring.Perches.Single(p => p.spawn == "Spawn_T3_Grunt").center.z + 70f;
            Assert.That(def.spawns.Single(s => s.name == "Spawn_T3_Grunt").position.z, Is.EqualTo(t3GruntZ).Within(Eps));
            Assert.That(def.pickups.Single(p => p.name == "Pickup_T3_Surge").position.z, Is.EqualTo(268f).Within(Eps));
            Assert.That(def.checkpoints.Single(c => c.name == "Checkpoint_3").position.z, Is.EqualTo(268f).Within(Eps));
            Assert.That(def.torches.Single(t => t.name == "Torch_T3_Span_S").basePosition.z, Is.EqualTo(301f).Within(Eps));
            Assert.AreEqual(4, def.balloons.Length, "the expanded red section keeps its optional aerial line");
            Assert.That(def.waters.Single(w => w.name == "T3_Water_Span").center.z, Is.EqualTo(305f).Within(Eps));
            Assert.That(def.ramps.Single(r => r.name == "T4_Ramp_Descent").basePosition.z, Is.EqualTo(394.8f).Within(Eps));
            Assert.That(def.spawns.Single(s => s.name == "Spawn_T4_Surge_1").position.z, Is.EqualTo(418.8f).Within(Eps));
            // The descent runs out onto the grappler approach; the Warden approach is 72 m further on.
            Assert.That(def.platforms.Single(p => p.name == LevelDefinitionAuthoring.GrapplerApproach).center.z, Is.EqualTo(449.6f).Within(Eps));
            Assert.That(def.platforms.Single(p => p.name == "Boss_Approach").center.z, Is.EqualTo(520.3f).Within(Eps));
            Assert.That(def.checkpoints.Single(c => c.name == LevelDefinitionAuthoring.GrapplerCheckpoint).position.z, Is.EqualTo(447f).Within(Eps));
            Assert.That(def.checkpoints.Single(c => c.name == "Checkpoint_4").position.z, Is.EqualTo(522f).Within(Eps));

            // Realm migration identity is separate from course geometry: these anchors never move.
            Assert.That(def.spawns.Single(s => s.name == "Spawn_Legendary_Knight").position.z, Is.EqualTo(173f).Within(Eps));
            Assert.That(def.spawns.Single(s => s.name == "Spawn_Boss").position.z, Is.EqualTo(396f).Within(Eps));
            Assert.That(def.spawns.Single(s => s.name == LevelDefinitionAuthoring.GrapplerSpawner).position.z, Is.EqualTo(479.7f).Within(Eps));
        }

        [Test]
        public void FourthMiniRealmIsAGatedFightOnTheT4RouteBeforeTheWarden()
        {
            var arena = def.arenas.Single(a => a.gateName == LevelDefinitionAuthoring.GrapplerGate);
            Assert.IsTrue(arena.enabled);
            Assert.IsTrue(arena.hasExitGate, "a mini realm reopens onto the route");
            Assert.AreEqual(LevelDefinitionAuthoring.GrapplerSpawner, arena.clearSpawnerName);
            Assert.AreEqual(arena.clearSpawnerName, arena.solarRealm.enemySpawnerName);
            var spawn = def.spawns.Single(s => s.name == LevelDefinitionAuthoring.GrapplerSpawner);
            Assert.AreEqual(LevelDefinitionAuthoring.GrapplerPrefabKey, spawn.prefabKey);
            Assert.IsFalse(spawn.isBoss, "a mini-boss is an ordinary enemy: only the Warden wakes a BossController");
            Assert.AreEqual(1, def.pickups.Count(p => p.name == LevelDefinitionAuthoring.GrapplerPickup));

            // Course order: T3's return -> the descent -> this gate -> its return -> the Warden's gate.
            var t3 = def.arenas.Single(a => a.gateName == "T3_Gate");
            var warden = def.arenas.Single(a => a.gateName == "Boss_Gate");
            var ramp = def.ramps.Single(r => r.name == "T4_Ramp_Descent");
            Assert.Greater(arena.gateClosedPosition.z, t3.solarRealm.returnPosition.z);
            Assert.Greater(arena.gateClosedPosition.z, ramp.TopPosition.z, "the sun stands at the FOOT of the descent");
            Assert.Greater(arena.exitGateClosedPosition.z, arena.solarRealm.exteriorCenter.z + arena.solarRealm.visualRadius);
            Assert.Greater(arena.solarRealm.returnPosition.z, arena.exitGateClosedPosition.z);
            Assert.Less(arena.solarRealm.returnPosition.z, warden.gateClosedPosition.z, "the return rejoins BEFORE the Warden's gate");
            Assert.AreEqual(Array_IndexOf(def.arenas, arena) + 1, Array_IndexOf(def.arenas, warden), "authored in course order");

            // The checkpoint before it sits on the run-out deck, after the ramp and before the gate.
            var checkpoint = def.checkpoints.Single(c => c.name == LevelDefinitionAuthoring.GrapplerCheckpoint);
            Assert.Greater(checkpoint.position.z, ramp.TopPosition.z);
            Assert.Less(checkpoint.position.z, arena.gateClosedPosition.z);
            var splits = def.runSplits.Select(s => s.name).ToArray();
            CollectionAssert.AreEqual(new[] { "Lancer", "Judge", "Dancer", "Grappler", "Warden" }, splits);
        }

        static int Array_IndexOf<T>(T[] array, T item) { return System.Array.IndexOf(array, item); }

        [Test]
        public void ExteriorSunsHaveBreathingRoomAndNoLegacyCourtGeometry()
        {
            string[] removedNames =
            {
                "T1_Arena", "T1_Stone_5", "T1_Obelisk_E", "T2_Arena", "T2_Bridge", "T2_Bridge_Rail_L",
                "T2_Bridge_Rail_R", "T2_Entry", "T3_Arena", "T3_Entry", "T3_Step_3", "Boss_Arena"
            };
            foreach (string name in removedNames)
                Assert.IsFalse(def.platforms.Any(p => p.name == name), name + " is obsolete court geometry");
            Assert.IsFalse(def.platforms.Any(p => p.name.StartsWith("Wall_T1_") || p.name.StartsWith("Wall_T2_") ||
                                                   p.name.StartsWith("Wall_T3_") || p.name.StartsWith("Wall_Boss_") ||
                                                   p.name.StartsWith("Pillar_Boss_")),
                "no legacy court wall or pillar may remain visible through an exterior sun");
            Assert.IsFalse(def.torches.Any(t => t.name.StartsWith("Torch_T1_Arena_") ||
                                                t.name.StartsWith("Torch_T2_Arena_") ||
                                                t.name.StartsWith("Torch_T3_Arena_") ||
                                                t.name.StartsWith("Torch_Boss_") ||
                                                t.name.StartsWith("Torch_T2_Entry_") ||
                                                t.name.StartsWith("Torch_T3_Entry_")),
                "legacy court torches would still silhouette inside the plasma");

            foreach (var arena in def.arenas)
            {
                var realm = arena.solarRealm;
                foreach (var piece in def.platforms)
                    AssertSolarClearance(arena.gateName, realm, piece.name,
                        DistanceToBox(realm.exteriorCenter, piece.center, piece.size),
                        IsApproachPlatform(arena.gateName, piece.name));
                foreach (var ramp in def.ramps)
                    AssertSolarClearance(arena.gateName, realm, ramp.name,
                        DistanceToRamp(realm.exteriorCenter, ramp), false);
                foreach (var torch in def.torches)
                    AssertSolarClearance(arena.gateName, realm, torch.name,
                        DistanceToBox(realm.exteriorCenter, torch.basePosition + Vector3.up * 0.95f,
                                      new Vector3(0.3f, 1.9f, 0.3f)),
                        IsApproachTorch(arena.gateName, torch.name));
                foreach (var gate in def.arenas)
                {
                    AssertSolarClearance(arena.gateName, realm, gate.gateName + " entry gate",
                        DistanceToBox(realm.exteriorCenter, gate.gateClosedPosition, gate.gateSize),
                        gate.gateName == arena.gateName);
                    if (gate.hasExitGate)
                        AssertSolarClearance(arena.gateName, realm, gate.gateName + " exit gate",
                            DistanceToBox(realm.exteriorCenter, gate.exitGateClosedPosition, gate.exitGateSize), false);
                }
            }
        }

        static void AssertSolarClearance(string gateName, SolarRealmDef realm, string piece,
                                         float centerToSurface, bool approach)
        {
            float clearance = centerToSurface - realm.visualRadius;
            float required = approach ? 1.45f : 11.95f; // authored 1.5 / 12 m, with YAML float tolerance.
            Assert.GreaterOrEqual(clearance, required,
                gateName + " " + (approach ? "approach" : "unrelated silhouette") + " " + piece +
                " has only " + clearance.ToString("0.00") + " m around the visible sun");
        }

        static bool IsApproachPlatform(string gate, string name)
        {
            if (gate == "T1_Gate")
                return name == "T1_Causeway" || name == "T1_Rail_L" || name == "T1_Rail_R" ||
                       name == "T1_Wall_Causeway" || name == "T1_Wall_Landing" || name == "T1_Perch_E";
            if (gate == "T2_Gate") return name == "T2_L11";
            if (gate == "T3_Gate") return name == "T3_Step_1" || name == "T3_Step_2";
            if (gate == LevelDefinitionAuthoring.GrapplerGate) return name == LevelDefinitionAuthoring.GrapplerApproach;
            if (gate == "Boss_Gate") return name == "Boss_Approach";
            return false;
        }

        static bool IsApproachTorch(string gate, string name)
        {
            if (gate == "T1_Gate")
                return name == "Torch_T1_Causeway_N" || name == "Torch_Beacon_T1_Causeway_2";
            return gate == "T2_Gate" &&
                   (name == "Torch_T2_Top" || name == "Torch_Beacon_T2_L11");
        }

        static float DistanceToBox(Vector3 point, Vector3 boxCenter, Vector3 boxSize)
        {
            Vector3 half = boxSize * 0.5f;
            Vector3 delta = point - boxCenter;
            Vector3 outside = new Vector3(
                Mathf.Max(Mathf.Abs(delta.x) - half.x, 0f),
                Mathf.Max(Mathf.Abs(delta.y) - half.y, 0f),
                Mathf.Max(Mathf.Abs(delta.z) - half.z, 0f));
            return outside.magnitude;
        }

        static float DistanceToRamp(Vector3 point, RampDef ramp)
        {
            Vector3 local = Quaternion.Inverse(ramp.Rotation) * (point - ramp.BoxCenter);
            Vector3 half = ramp.BoxScale * 0.5f;
            Vector3 outside = new Vector3(
                Mathf.Max(Mathf.Abs(local.x) - half.x, 0f),
                Mathf.Max(Mathf.Abs(local.y) - half.y, 0f),
                Mathf.Max(Mathf.Abs(local.z) - half.z, 0f));
            return outside.magnitude;
        }

        [Test]
        public void EveryExteriorApproachReachesThePortalAcrossAnOpenGap()
        {
            string[] gates = Gates;
            string[] approaches = { "T1_Causeway", "T2_L11", "T3_Step_2", LevelDefinitionAuthoring.GrapplerApproach, "Boss_Approach" };
            for (int i = 0; i < gates.Length; i++)
            {
                var realm = def.arenas.Single(a => a.gateName == gates[i]).solarRealm;
                var deck = def.platforms.Single(p => p.name == approaches[i]);
                float playerCenterY = deck.center.y + deck.size.y * 0.5f + 0.9f;
                float dy = Mathf.Abs(playerCenterY - realm.exteriorCenter.y);
                Assert.Less(dy, realm.exteriorRadius, gates[i] + " portal misses the take-off height");
                float nearTriggerZ = realm.exteriorCenter.z -
                    Mathf.Sqrt(realm.exteriorRadius * realm.exteriorRadius - dy * dy);
                float gap = nearTriggerZ - (deck.center.z + deck.size.z * 0.5f);
                if (i < 3)
                    Assert.That(gap, Is.InRange(13.5f, 15.5f),
                        gates[i] + " must require projectile-earned carry; gap=" + gap);
                else
                    // The two T4 suns are ordinary open jumps: the descent's surge is a bonus you fly in
                    // with, never a carry the gap could demand (a missed ladder must not soft-lock the run).
                    Assert.That(gap, Is.GreaterThan(1f).And.LessThanOrEqualTo(8.5f),
                        gates[i] + " transition remains an ordinary open jump; gap=" + gap);
            }
        }

        [Test]
        public void EveryDoorwayCrossesItsSphere_AndRealmCellsDoNotOverlap()
        {
            var realms = def.arenas.Select(a => a.solarRealm).Where(r => r != null && r.enabled).ToArray();
            Assert.AreEqual(5, realms.Length);
            foreach (var arena in def.arenas)
            {
                var r = arena.solarRealm;
                Vector3 left = arena.triggerPosition + Vector3.left * arena.triggerSize.x * 0.5f;
                Vector3 right = arena.triggerPosition + Vector3.right * arena.triggerSize.x * 0.5f;
                Assert.Less(Vector3.Distance(left, r.exteriorCenter), r.exteriorRadius, arena.gateName + " left edge bypass");
                Assert.Less(Vector3.Distance(right, r.exteriorCenter), r.exteriorRadius, arena.gateName + " right edge bypass");
            }
            for (int i = 0; i < realms.Length; i++)
                for (int j = i + 1; j < realms.Length; j++)
                    Assert.Greater(Vector3.Distance(realms[i].realmCenter, realms[j].realmCenter),
                        realms[i].realmShellRadius + realms[j].realmShellRadius,
                        "disconnected realm shells overlap");
        }

        [Test]
        public void RemoteRealmShellsStayBeyondGameplayAndCaptureCameraFarPlanes()
        {
            const float PlayerCameraFarClip = 300f;
            float routeHalfWidth = def.platforms.Max(p => Mathf.Abs(p.center.x) + p.size.x * 0.5f);
            foreach (var arena in def.arenas)
            {
                var r = arena.solarRealm;
                float emptyX = Mathf.Abs(r.realmCenter.x) - r.realmShellRadius - routeHalfWidth;
                Assert.Greater(emptyX, PlayerCameraFarClip,
                    arena.gateName + " old route could be visible or physically intrude inside the remote shell");
            }
        }

        [Test]
        public void RemoteRealmsSupportPlayerEnemyPickupAndExitOnTheirGeneratedFloor()
        {
            foreach (var arena in def.arenas)
            {
                var r = arena.solarRealm;
                AssertInsideFloor(r, r.playerEntryPosition, arena.gateName + " player entry");
                AssertInsideFloor(r, r.enemySpawnPosition, arena.gateName + " enemy");
                AssertInsideFloor(r, r.arenaPickupPosition, arena.gateName + " pickup");
                if (r.hasReturn) AssertInsideFloor(r, r.realmExitPosition, arena.gateName + " return portal");
            }
        }

        static void AssertInsideFloor(SolarRealmDef realm, Vector3 position, string label)
        {
            float radial = Vector2.Distance(new Vector2(position.x, position.z),
                                            new Vector2(realm.realmCenter.x, realm.realmCenter.z));
            Assert.LessOrEqual(radial, realm.realmFloorRadius - 1f, label + " lacks a metre of floor margin");
            Assert.That(position.y - realm.realmCenter.y, Is.InRange(0f, 1.5f),
                label + " is not on the generated floor");
        }

        [Test]
        public void MiniRealmReturnsStandOnTheNextAuthoredDeck_FinalHasNoReturn()
        {
            foreach (var arena in def.arenas)
            {
                var r = arena.solarRealm;
                if (!r.hasReturn) continue;
                var ground = def.platforms.FirstOrDefault(p =>
                    r.returnPosition.x >= p.center.x - p.size.x * 0.5f &&
                    r.returnPosition.x <= p.center.x + p.size.x * 0.5f &&
                    r.returnPosition.z >= p.center.z - p.size.z * 0.5f &&
                    r.returnPosition.z <= p.center.z + p.size.z * 0.5f &&
                    Mathf.Abs(r.returnPosition.y - (p.center.y + p.size.y * 0.5f + 0.2f)) < Eps);
                Assert.IsNotNull(ground, arena.gateName + " return has no authored ground at " + r.returnPosition);
                Assert.Greater(r.returnPosition.z, arena.exitGateClosedPosition.z, arena.gateName);
            }
            var final = def.arenas.Single(a => a.gateName == "Boss_Gate").solarRealm;
            Assert.IsFalse(final.hasReturn);
        }

        [Test]
        public void EveryNonRealmPickupAndCheckpointHasAuthoredGround()
        {
            var realmPickups = def.arenas.Select(a => a.solarRealm)
                .Where(r => r != null && r.enabled)
                .Select(r => r.arenaPickupName)
                .ToArray();
            foreach (var pickup in def.pickups.Where(p => !realmPickups.Contains(p.name)))
            {
                var ground = def.platforms.FirstOrDefault(p =>
                    pickup.position.x >= p.center.x - p.size.x * 0.5f &&
                    pickup.position.x <= p.center.x + p.size.x * 0.5f &&
                    pickup.position.z >= p.center.z - p.size.z * 0.5f &&
                    pickup.position.z <= p.center.z + p.size.z * 0.5f &&
                    pickup.position.y - (p.center.y + p.size.y * 0.5f) >= 0.9f &&
                    pickup.position.y - (p.center.y + p.size.y * 0.5f) <= 1.8f);
                Assert.IsNotNull(ground, pickup.name + " has no supporting route deck at " + pickup.position);
            }

            foreach (var checkpoint in def.checkpoints)
            {
                var ground = def.platforms.FirstOrDefault(p =>
                    checkpoint.position.x >= p.center.x - p.size.x * 0.5f &&
                    checkpoint.position.x <= p.center.x + p.size.x * 0.5f &&
                    checkpoint.position.z >= p.center.z - p.size.z * 0.5f &&
                    checkpoint.position.z <= p.center.z + p.size.z * 0.5f &&
                    Mathf.Abs(checkpoint.position.y - (p.center.y + p.size.y * 0.5f)) < Eps);
                Assert.IsNotNull(ground, checkpoint.name + " has no walkable top at " + checkpoint.position);
            }
        }

        [Test]
        public void KillZoneCoversTheExpandedCourseBounds()
        {
            Assert.LessOrEqual(def.killZone.center.z - def.killZone.size.z * 0.5f, -190f);
            Assert.GreaterOrEqual(def.killZone.center.z + def.killZone.size.z * 0.5f, 620f);
            Assert.GreaterOrEqual(def.killZone.size.x, 240f);
        }

        [Test]
        public void RetiredSlideGatesAreGoneAndStayGone()
        {
            // The user's 2026-09-13 call: no more bars across the middle of a section. Apply removes them
            // from an already-shipped asset and nothing re-adds them; the realm arenas are untouched.
            CollectionAssert.AreEquivalent(new[] { "T1_Fallen_Obelisk", "T3_Fallen_Lintel" },
                LevelDefinitionAuthoring.RemovedSlideGates);
            LevelDefinitionAuthoring.Apply(def);
            foreach (string name in LevelDefinitionAuthoring.RemovedSlideGates)
            {
                Assert.IsFalse(def.platforms.Any(p => p.name == name), name + " must be removed by Apply");
                Assert.IsFalse(LevelDefinitionAuthoring.HybridCourseStructures.Any(s => s.name == name), name + " must not be authored");
                Assert.IsFalse(LevelDefinitionAuthoring.Reshapes.Any(r => r.name == name), name + " must not be reshaped");
            }
        }

        [Test]
        public void SolarMaterialsShipOnTheProceduralUrpShader()
        {
            foreach (var name in new[] { "M_SolarCyan", "M_SolarGold", "M_SolarAzure", "M_SolarGhost", "M_SolarViolet" })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + name + ".mat");
                Assert.IsNotNull(mat, name + ": run 2. Create Materials");
                Assert.AreEqual("VibeGame1/Solar Arena", mat.shader.name, name);
                Assert.That(mat.GetFloat("_SurfaceOpacity"), Is.InRange(0.85f, 0.98f),
                    name + " exterior must attenuate scenery rather than merely adding brightness");
                var core = mat.GetColor("_CoreColor");
                var band = mat.GetColor("_BandColor");
                Assert.Less(core.maxColorComponent, 1.05f, name + " body must retain saturated detail");
                Assert.Greater(band.maxColorComponent, 1.05f, name + " moving bands carry the bloom exception");
                Assert.Greater(band.maxColorComponent - Mathf.Min(band.r, Mathf.Min(band.g, band.b)), 0.45f,
                    name + " bands must remain chromatic after ACES");
            }
            var corona = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_SolarCorona.mat");
            Assert.IsNotNull(corona, "run 2. Create Materials");
            Assert.AreEqual("VibeGame1/Solar Arena", corona.shader.name);
            Assert.LessOrEqual(corona.GetFloat("_Alpha"), 0.05f, "corona body must not veil the plasma");
            Assert.AreEqual(0f, corona.GetFloat("_SurfaceOpacity"), "corona stays additive");
            foreach (var name in new[] { "M_SolarRealmCyan", "M_SolarRealmGold", "M_SolarRealmAzure", "M_SolarRealmGhost" })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + name + ".mat");
                Assert.IsNotNull(mat, name + ": run 2. Create Materials");
                Assert.AreEqual("Universal Render Pipeline/Lit", mat.shader.name, name);
                Assert.LessOrEqual(mat.GetColor("_EmissionColor").maxColorComponent, 1.05f,
                    name + " is the eye-level enclosure and must stay below bloom");
                Assert.AreEqual(0f, mat.GetFloat("_Cull"), Eps, name + " must render from inside the shell");
            }
        }

        [Test]
        public void SceneExportPreservesSolarDefinitionAndAuthoredPlacementOverrides()
        {
            var root = new GameObject("Level");
            var trigger = new GameObject("ArenaTrigger");
            trigger.transform.SetParent(root.transform);
            trigger.AddComponent<BoxCollider>();
            var fight = trigger.AddComponent<BossArenaTrigger>();
            var gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gate.name = "Gate";
            gate.transform.SetParent(root.transform);
            fight.gate = gate.transform;

            var portalGo = new GameObject("SolarPortal");
            portalGo.transform.SetParent(root.transform);
            portalGo.AddComponent<SphereCollider>();
            var portal = portalGo.AddComponent<SolarArenaPortal>();
            portal.arena = fight;
            portal.definition = new SolarRealmDef
            {
                enabled = true, themeMaterialKey = "SolarGhost", exteriorCenter = new Vector3(1f, 2f, 3f),
                exteriorRadius = 18f, realmCenter = new Vector3(260f, 0f, 240f),
                enemySpawnerName = "Spawn_Boss", arenaPickupName = "Pickup_Boss_Hook"
            };
            fight.solarPortal = portal;

            var spawn = new GameObject("Spawn_Boss");
            spawn.transform.SetParent(root.transform);
            spawn.transform.position = new Vector3(260f, 0.1f, 244f);
            spawn.AddComponent<EnemySpawner>();
            var spawnPlacement = spawn.AddComponent<SolarRealmPlacement>();
            spawnPlacement.authoredPosition = new Vector3(0f, 16.1f, 396f);
            spawnPlacement.authoredYaw = 180f;

            var pickups = new GameObject("Pickups");
            pickups.transform.SetParent(root.transform);
            var pickup = new GameObject("Pickup_Boss_Hook");
            pickup.transform.SetParent(pickups.transform);
            pickup.transform.position = new Vector3(253f, 1.2f, 235f);
            var pickupPlacement = pickup.AddComponent<SolarRealmPlacement>();
            pickupPlacement.authoredPosition = new Vector3(-8f, 17.2f, 376f);

            var exported = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                LevelDefinitionExporter.ExportInto(root, exported);
                Assert.AreEqual(JsonUtility.ToJson(portal.definition), JsonUtility.ToJson(exported.arenas.Single().solarRealm));
                Assert.AreNotSame(portal.definition, exported.arenas.Single().solarRealm);
                Assert.AreEqual(spawnPlacement.authoredPosition, exported.spawns.Single().position);
                Assert.AreEqual(pickupPlacement.authoredPosition, exported.pickups.Single().position);
            }
            finally
            {
                Object.DestroyImmediate(exported);
                Object.DestroyImmediate(root);
            }
        }
    }
}
