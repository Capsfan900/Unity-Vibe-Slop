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
            string once = JsonUtility.ToJson(def);
            LevelDefinitionAuthoring.ApplySolarRealms(def);
            Assert.AreEqual(once, JsonUtility.ToJson(def));
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
                new Vector3(0f, 8.2f, 87f), new Vector3(0f, 24.55f, 170f),
                new Vector3(0f, 32.2f, 270f), new Vector3(0f, 22.3f, 390f)
            };
            float[] radii = { 12f, 13f, 12f, 18f };

            for (int i = 0; i < gates.Length; i++)
            {
                var realm = def.arenas.Single(a => a.gateName == gates[i]).solarRealm;
                Assert.IsNotNull(realm, gates[i]);
                Assert.IsTrue(realm.enabled, gates[i]);
                Assert.AreEqual(themes[i], realm.themeMaterialKey, gates[i]);
                Assert.That(Vector3.Distance(centers[i], realm.exteriorCenter), Is.LessThan(Eps), gates[i]);
                Assert.That(realm.exteriorRadius, Is.EqualTo(radii[i]).Within(Eps), gates[i]);
                Assert.That(realm.realmFloorRadius, Is.EqualTo(20f).Within(Eps), gates[i]);
                Assert.That(realm.realmShellRadius, Is.EqualTo(30f).Within(Eps), gates[i]);
                Assert.IsFalse(string.IsNullOrEmpty(realm.arenaPickupName), gates[i] + " pickup");
                Assert.Less(Vector3.Distance(realm.arenaPickupPosition, realm.realmCenter), realm.realmFloorRadius,
                    gates[i] + " pickup must sit inside its realm floor");
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
            foreach (var arena in def.arenas)
            {
                var r = arena.solarRealm;
                Assert.Greater(Mathf.Abs(r.realmCenter.x) - r.realmShellRadius, 400f,
                    arena.gateName + " realm could appear as a stray planet from the route");
            }
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
        public void SolarMaterialsShipOnTheProceduralUrpShader()
        {
            foreach (var name in new[] { "M_SolarCyan", "M_SolarGold", "M_SolarAzure", "M_SolarGhost" })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + name + ".mat");
                Assert.IsNotNull(mat, name + ": run 2. Create Materials");
                Assert.AreEqual("VibeGame1/Solar Arena", mat.shader.name, name);
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
