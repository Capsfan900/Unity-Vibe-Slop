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

        [Test]
        public void ShippedLevelHasFourExactSolarAnchorsAndThemes()
        {
            string[] gates = { "T1_Gate", "T2_Gate", "T3_Gate", "Boss_Gate" };
            string[] themes = { "SolarCyan", "SolarGold", "SolarAzure", "SolarGhost" };
            Vector3[] centers =
            {
                new Vector3(0f, 8.2f, 87.3f), new Vector3(0f, 24.55f, 216.8f),
                new Vector3(0f, 32.2f, 356.3f), new Vector3(0f, 22.3f, 486.3f)
            };
            float[] radii = { 16f, 17f, 16f, 25f };
            float[] visualRadii = { 22f, 23f, 22f, 31f };

            for (int i = 0; i < gates.Length; i++)
            {
                var realm = def.arenas.Single(a => a.gateName == gates[i]).solarRealm;
                Assert.IsNotNull(realm, gates[i]);
                Assert.IsTrue(realm.enabled, gates[i]);
                Assert.AreEqual(themes[i], realm.themeMaterialKey, gates[i]);
                Assert.That(Vector3.Distance(centers[i], realm.exteriorCenter), Is.LessThan(Eps), gates[i]);
                Assert.That(realm.exteriorRadius, Is.EqualTo(radii[i]).Within(Eps), gates[i]);
                Assert.That(realm.visualRadius, Is.EqualTo(visualRadii[i]).Within(Eps), gates[i]);
                Assert.Greater(realm.visualRadius, realm.exteriorRadius,
                    gates[i] + " visual shell must not advance the physical portal boundary");
                Assert.That(realm.realmFloorRadius, Is.EqualTo(20f).Within(Eps), gates[i]);
                Assert.That(realm.realmShellRadius, Is.EqualTo(30f).Within(Eps), gates[i]);
                Assert.IsFalse(string.IsNullOrEmpty(realm.arenaPickupName), gates[i] + " pickup");
                Assert.Less(Vector3.Distance(realm.arenaPickupPosition, realm.realmCenter), realm.realmFloorRadius,
                    gates[i] + " pickup must sit inside its realm floor");
            }
        }

        [Test]
        public void SolarTransitionsShipAtTheirAuditedGatesRetriesAndReturns()
        {
            string[] gates = { "T1_Gate", "T2_Gate", "T3_Gate", "Boss_Gate" };
            float[] entries = { 63.25f, 191.75f, 332.25f, 453.3f };
            float[] triggers = { 78.3f, 206.8f, 345.3f, 471.3f };
            float[] exits = { 145.625f, 264.75f, 390.75f, 0f };
            Vector3[] retries =
            {
                new Vector3(0f, 2.2f, 62f), new Vector3(0f, 20.2f, 188f),
                new Vector3(-3f, 27.2f, 329f), new Vector3(0f, 16.2f, 450f)
            };
            Vector3[] returns =
            {
                new Vector3(10f, 5.2f, 151.375f), new Vector3(2.75f, 21.7f, 268f),
                new Vector3(0f, 28.2f, 393.15f), Vector3.zero
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
            Assert.That(def.platforms.Single(p => p.name == "Boss_Approach").center.z, Is.EqualTo(448.3f).Within(Eps));

            // Realm migration identity is separate from course geometry: these anchors never move.
            Assert.That(def.spawns.Single(s => s.name == "Spawn_Legendary_Knight").position.z, Is.EqualTo(173f).Within(Eps));
            Assert.That(def.spawns.Single(s => s.name == "Spawn_Boss").position.z, Is.EqualTo(396f).Within(Eps));
        }

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
            if (gate == "Boss_Gate")
                return name == "Boss_Approach" || name == "T4_TurretPad_3";
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
            string[] gates = { "T1_Gate", "T2_Gate", "T3_Gate", "Boss_Gate" };
            string[] approaches = { "T1_Causeway", "T2_L11", "T3_Step_2", "Boss_Approach" };
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
                    Assert.That(gap, Is.GreaterThan(1f).And.LessThanOrEqualTo(8.5f),
                        gates[i] + " boss transition remains an ordinary open jump; gap=" + gap);
            }
        }

        [Test]
        public void EveryDoorwayCrossesItsSphere_AndRealmCellsDoNotOverlap()
        {
            var realms = def.arenas.Select(a => a.solarRealm).Where(r => r != null && r.enabled).ToArray();
            Assert.AreEqual(4, realms.Length);
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
            Assert.GreaterOrEqual(def.killZone.center.z + def.killZone.size.z * 0.5f, 540f);
            Assert.GreaterOrEqual(def.killZone.size.x, 240f);
        }

        [Test]
        public void SolarMaterialsShipOnTheProceduralUrpShader()
        {
            foreach (var name in new[] { "M_SolarCyan", "M_SolarGold", "M_SolarAzure", "M_SolarGhost" })
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
