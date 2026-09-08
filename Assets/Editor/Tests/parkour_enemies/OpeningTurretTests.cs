using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>The authored five-beat opening: left, right, left, then the elevated pair.</summary>
    public class OpeningTurretTests
    {
        LevelDefinition def;

        [SetUp]
        public void LoadAuthoredCopy()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            Assert.IsNotNull(shipped, "Level_01 definition missing");
            def = Object.Instantiate(shipped);
        }

        [TearDown] public void Clean() { Object.DestroyImmediate(def); }

        [Test]
        public void VolleyIsFiveExistingTurretsInLeftRightLeftOverheadOrder()
        {
            var volley = def.projectileSequences.Single(s => s.name == "T0_SurgeVolley");
            Assert.That(volley.spawnerNames, Is.EqualTo(new[]
            {
                "Spawn_T0_Surge_1", "Spawn_T0_Surge_2", "Spawn_T0_Surge_3",
                "Spawn_T0_Surge_4", "Spawn_T0_Surge_5"
            }));

            var shots = volley.spawnerNames.Select(n => def.spawns.Single(s => s.name == n)).ToArray();
            Assert.That(shots.Length, Is.EqualTo(5));
            Assert.That(shots.All(s => s.prefabKey == "pshooter_enemy03"), Is.True);
            Assert.That(Mathf.Sign(shots[0].position.x), Is.EqualTo(-1f), "first is left");
            Assert.That(Mathf.Sign(shots[1].position.x), Is.EqualTo(1f), "second is right");
            Assert.That(Mathf.Sign(shots[2].position.x), Is.EqualTo(-1f), "third returns left");
            Assert.That(shots.Select(s => s.position), Is.EqualTo(new[]
            {
                new Vector3(-7.7f, 22.1f, -103.8f),
                new Vector3( 7.7f, 16.6f,  -81.8f),
                new Vector3(-7.7f, 11.1f,  -59.8f),
                new Vector3( 3.2f, 13.6f,  -37.8f),
                new Vector3(-3.2f,  9.1f,  -21.8f)
            }));

            Assert.That(volley.progressOrigin, Is.EqualTo(new Vector3(0f, 0f, -123.8f)));
            Assert.That(volley.progressDirection, Is.EqualTo(Vector3.forward));
            Assert.That(volley.memberProgressGates, Is.EqualTo(new[] { 0f, 14f, 36f, 58f, 78f }));
        }

        [Test]
        public void OpeningIsA108MetreDescentAtOneInFourGradeWithBreathingRoom()
        {
            var ramp = def.ramps.Single(r => r.name == "T0_Ramp_Descent");
            var entry = def.platforms.Single(p => p.name == "T0_Entry");
            var runout = def.platforms.Single(p => p.name == "T0_RunOut");
            Assert.That(ramp.basePosition, Is.EqualTo(new Vector3(0f, 27f, -123.8f)));
            Assert.That(ramp.run, Is.EqualTo(108f).Within(0.001f));
            Assert.That(ramp.rise, Is.EqualTo(-27f).Within(0.001f));
            Assert.That(ramp.width, Is.EqualTo(12f).Within(0.001f));
            Assert.That(Vector3.Distance(ramp.TopPosition, new Vector3(0f, 0f, -15.8f)), Is.LessThan(0.001f));
            Assert.That(entry.size.x, Is.EqualTo(12f).Within(0.001f));
            Assert.That(runout.size.x, Is.EqualTo(12f).Within(0.001f));
            Assert.That(def.playerStart, Is.EqualTo(new Vector3(0f, 27.3f, -127f)));
        }

        [Test]
        public void FloatingDaisIsAnOpenConnectedCyanCrownAboveTheSlope()
        {
            var pieces = def.platforms.Where(p => p.name.StartsWith("T0_OverheadDais_")).ToArray();
            Assert.That(pieces.Length, Is.EqualTo(10), "two terraces, stepped open Z connection and tapered undersides");
            Assert.That(pieces.Count(p => p.trim && p.trimMaterialKey == "NeonCyan"), Is.EqualTo(2));

            var ramp = def.ramps.Single(r => r.name == "T0_Ramp_Descent");
            foreach (var piece in pieces)
            {
                var uphillEdge = new Vector3(piece.center.x, 0f, piece.center.z - piece.size.z * 0.5f);
                float clearance = piece.center.y - piece.size.y * 0.5f - LevelDescentReport.SurfaceY(ramp, uphillEdge);
                Assert.That(clearance, Is.GreaterThanOrEqualTo(6f), piece.name + " route clearance");
            }

            var front = pieces.Single(p => p.name == "T0_OverheadDais_Front");
            var rear = pieces.Single(p => p.name == "T0_OverheadDais_Rear");
            var fourth = def.spawns.Single(s => s.name == "Spawn_T0_Surge_4");
            var fifth = def.spawns.Single(s => s.name == "Spawn_T0_Surge_5");
            Assert.That(fourth.position.y, Is.EqualTo(front.center.y + front.size.y * 0.5f + 0.1f).Within(0.001f));
            Assert.That(fifth.position.y, Is.EqualTo(rear.center.y + rear.size.y * 0.5f + 0.1f).Within(0.001f));
            Assert.That(Mathf.Abs(fourth.position.x - front.center.x), Is.LessThan(front.size.x * 0.5f));
            Assert.That(Mathf.Abs(fifth.position.x - rear.center.x), Is.LessThan(rear.size.x * 0.5f));
        }

        [Test]
        public void EveryAuthoredBeatHasAnUnblockedFrontalApproachLine()
        {
            var volley = def.projectileSequences.Single(s => s.name == "T0_SurgeVolley");
            var targets = volley.memberProgressGates
                .Select(progress => RampChest(volley.progressOrigin.z + progress)).ToArray();
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy03"));
            Assert.IsNotNull(data, "shipped surge turret data missing");

            for (int i = 0; i < targets.Length; i++)
            {
                var shot = def.spawns.Single(s => s.name == "Spawn_T0_Surge_" + (i + 1));
                Vector3 muzzle = shot.position + Vector3.up * 1.3f;
                float distance = Vector3.Distance(muzzle, targets[i]);
                Assert.That(distance, Is.InRange(data.projectileMinRange, data.projectileMaxRange), shot.name + " range");
                Assert.IsNull(LevelDescentReport.Blocker(def, muzzle, targets[i]), shot.name + " authored line");
                Vector3 source = shot.position - targets[i]; source.y = 0f;
                Assert.IsTrue(ParryMath.IsFacing(Vector3.forward, source, 75f), shot.name + " forward cone");
                float speed = ProjectileMath.LaunchSpeed(distance, data.projectileSpeed,
                    Projectile.CueLead, ProjectileShooter.CueMargin);
                Assert.IsTrue(ProjectileMath.ArrivesInFront(muzzle, targets[i], Vector3.forward * 27.5f, speed, 75f),
                    shot.name + " remains frontal at the motor's final horizontal-speed clamp");
            }

            Assert.That(volley.memberProgressGates, Is.Ordered.Ascending);
            Assert.That(volley.memberProgressGates.All(p => p >= 0f && p < 108f), Is.True,
                "every launch gate lies on the descent");
        }

        [Test]
        public void FiveCleanContactsReachTheExistingFullSurgeBeforeAStackCanDecay()
        {
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy03"));
            var stats = AssetDatabase.LoadAssetAtPath<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            var volley = def.projectileSequences.Single(s => s.name == "T0_SurgeVolley");
            Assert.IsNotNull(data);
            Assert.IsNotNull(stats);
            Assert.That(volley.spawnerNames.Length, Is.EqualTo(data.parrySurgeMaxStacks));
            Assert.That(SurgeMath.Multiplier(volley.spawnerNames.Length, data.parrySurgeStep),
                Is.EqualTo(SurgeMath.MaxMultiplier(data.parrySurgeMaxStacks, data.parrySurgeStep)).Within(0.0001f));
            Assert.That(volley.recoveryGap, Is.GreaterThan(stats.parrySuccessRecovery + 0.02f),
                "the next launch waits until the player can raise the blade again");
            Assert.That(volley.recoveryGap + Projectile.CueLead + ProjectileShooter.CueMargin,
                Is.LessThan(data.parrySurgeSeconds), "the nominal next contact stays inside stack decay");
        }

        [Test]
        public void ApplyingTheOpeningAgainDoesNotDuplicateTheVolleyOrItsDais()
        {
            LevelDefinitionAuthoring.ApplyOpeningDescent(def);
            string once = JsonUtility.ToJson(def);
            LevelDefinitionAuthoring.ApplyOpeningDescent(def);
            Assert.That(JsonUtility.ToJson(def), Is.EqualTo(once));
        }

        [Test]
        public void FreshSceneExportPreservesTheOrderedSequenceMetadata()
        {
            var root = new GameObject("Level");
            var fresh = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                var members = new EnemySpawner[5];
                for (int i = 0; i < members.Length; i++)
                {
                    var marker = new GameObject("Spawn_T0_Surge_" + (i + 1));
                    marker.transform.SetParent(root.transform, false);
                    members[i] = marker.AddComponent<EnemySpawner>();
                }
                var host = new GameObject("T0_SurgeVolley");
                host.transform.SetParent(root.transform, false);
                host.AddComponent<ProjectileVolleySequence>().Configure(
                    members, 0.11f, 1.1f, new Vector3(0f, 0f, -123.8f), Vector3.forward,
                    new[] { 0f, 14f, 36f, 58f, 78f });

                LevelDefinitionExporter.ExportInto(root, fresh);

                var saved = fresh.projectileSequences.Single();
                Assert.That(saved.name, Is.EqualTo("T0_SurgeVolley"));
                Assert.That(saved.spawnerNames, Is.EqualTo(members.Select(m => m.name).ToArray()));
                Assert.That(saved.recoveryGap, Is.EqualTo(0.11f).Within(0.0001f));
                Assert.That(saved.readinessTimeout, Is.EqualTo(1.1f).Within(0.0001f));
                Assert.That(saved.progressOrigin, Is.EqualTo(new Vector3(0f, 0f, -123.8f)));
                Assert.That(saved.progressDirection, Is.EqualTo(Vector3.forward));
                Assert.That(saved.memberProgressGates, Is.EqualTo(new[] { 0f, 14f, 36f, 58f, 78f }));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(fresh);
            }
        }

        [Test]
        public void AdvancingStartsAFiniteReadinessWindowForTheNextMember()
        {
            var host = new GameObject("SequenceStateTest");
            var markers = new GameObject[2];
            try
            {
                var members = new EnemySpawner[2];
                for (int i = 0; i < members.Length; i++)
                {
                    markers[i] = new GameObject("Member_" + i);
                    members[i] = markers[i].AddComponent<EnemySpawner>();
                }
                var sequence = host.AddComponent<ProjectileVolleySequence>();
                sequence.Configure(members, 0.11f, 1.1f);

                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(ProjectileVolleySequence).GetMethod("Advance", flags).Invoke(sequence, null);
                bool deadlineActive = (bool)typeof(ProjectileVolleySequence).GetField("bandSeen", flags).GetValue(sequence);
                float deadlineStart = (float)typeof(ProjectileVolleySequence).GetField("enteredBandAt", flags).GetValue(sequence);
                float nextLaunch = (float)typeof(ProjectileVolleySequence).GetField("nextLaunchAt", flags).GetValue(sequence);

                Assert.That(sequence.CurrentIndex, Is.EqualTo(1));
                Assert.IsTrue(deadlineActive, "later members cannot wait forever outside their firing band");
                Assert.That(deadlineStart, Is.EqualTo(nextLaunch).Within(0.0001f),
                    "the timeout begins after the inter-shot recovery gap, not during it");
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (var marker in markers) if (marker != null) Object.DestroyImmediate(marker);
            }
        }

        [Test]
        public void GatedMemberWaitsPassivelyUntilPlayerCrossesItsAuthoredPosition()
        {
            var host = new GameObject("SequenceGateTest");
            var player = new GameObject("SequenceGatePlayer");
            var markers = new GameObject[2];
            try
            {
                var members = new EnemySpawner[2];
                for (int i = 0; i < members.Length; i++)
                {
                    markers[i] = new GameObject("GatedMember_" + i);
                    members[i] = markers[i].AddComponent<EnemySpawner>();
                }
                var sequence = host.AddComponent<ProjectileVolleySequence>();
                sequence.Configure(members, 0.11f, 1.1f, Vector3.zero, Vector3.forward, new[] { 0f, 10f });

                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var type = typeof(ProjectileVolleySequence);
                type.GetMethod("Advance", flags).Invoke(sequence, null);
                type.GetField("player", flags).SetValue(sequence, player.transform);
                player.transform.position = new Vector3(0f, 0f, 9.9f);
                type.GetMethod("Update", flags).Invoke(sequence, null);
                Assert.IsFalse((bool)type.GetField("bandSeen", flags).GetValue(sequence),
                    "readiness time must not be consumed before the position gate");

                player.transform.position = new Vector3(0f, 0f, 10f);
                type.GetMethod("Update", flags).Invoke(sequence, null);
                Assert.IsTrue((bool)type.GetField("bandSeen", flags).GetValue(sequence),
                    "crossing the gate latches the finite readiness window");

                type.GetMethod("ResetState", flags).Invoke(sequence, null);
                Assert.That(sequence.CurrentIndex, Is.Zero);
                Assert.IsFalse((bool)type.GetField("bandSeen", flags).GetValue(sequence),
                    "respawn/replacement reset clears the latched gate");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(player);
                foreach (var marker in markers) if (marker != null) Object.DestroyImmediate(marker);
            }
        }

        Vector3 RampChest(float z)
        {
            var ramp = def.ramps.Single(r => r.name == "T0_Ramp_Descent");
            var p = new Vector3(0f, 0f, z);
            p.y = LevelDescentReport.SurfaceY(ramp, p) + 1.2f;
            return p;
        }
    }
}
