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
            // Progress down the 144 m slope from its lip at z -159.8: 30 / 47 / 71 / 96 / 113.
            // Compare with a TOLERANCE, element by element. NUnit uses Vector3.Equals, which is exact
            // float equality per component - unlike Vector3.==, which is approximate. These positions are
            // computed from a slope() helper, so a value that prints as (3.20, 19.60, -63.80) can still
            // differ from the same literal in its low bits, and the failure reads "Expected: (3.20, 19.60,
            // -63.80) But was: (3.20, 19.60, -63.80)", which tells you nothing.
            var expected = new[]
            {
                new Vector3(-8.7f, 28.6f, -129.8f),
                new Vector3( 8.7f, 24.35f, -112.8f),
                new Vector3(-8.7f, 18.35f, -88.8f),
                new Vector3( 3.2f, 19.6f, -63.8f),
                new Vector3(-3.2f, 15.1f, -46.8f)
            };
            for (int i = 0; i < expected.Length; i++)
                Assert.That(Vector3.Distance(shots[i].position, expected[i]), Is.LessThan(0.001f),
                    "perch " + (i + 1) + " is at " + shots[i].position + ", authored as " + expected[i]);

            Assert.That(volley.progressOrigin, Is.EqualTo(new Vector3(0f, 0f, -159.8f)));
            Assert.That(volley.progressDirection, Is.EqualTo(Vector3.forward));
            Assert.That(volley.memberProgressGates, Is.EqualTo(new[] { 0f, 18f, 40f, 64f, 87f }));
            Assert.That(volley.shotResolutionTimeout, Is.EqualTo(1.25f).Within(0.001f));
        }

        [Test]
        public void OpeningIsA144MetreDescentAtOneInFourGradeWithBreathingRoom()
        {
            var ramp = def.ramps.Single(r => r.name == "T0_Ramp_Descent");
            var entry = def.platforms.Single(p => p.name == "T0_Entry");
            var runout = def.platforms.Single(p => p.name == "T0_RunOut");
            // Lengthened at the TOP on 2026-09-07 (120 -> 144 m of run). The bottom is pinned at
            // z -15.8 / y 0 because T0_RunOut and then Ground_Start follow it, so the extra 24 m has to
            // appear above the lip: base y 30 -> 36, base z -135.8 -> -159.8. Grade stays exactly 1:4.
            Assert.That(ramp.basePosition, Is.EqualTo(new Vector3(0f, 36f, -159.8f)));
            Assert.That(ramp.run, Is.EqualTo(144f).Within(0.001f));
            Assert.That(ramp.rise, Is.EqualTo(-36f).Within(0.001f));
            Assert.That(-ramp.rise / ramp.run, Is.EqualTo(0.25f).Within(0.0001f), "the 1:4 grade is fixed");
            Assert.That(ramp.width, Is.EqualTo(14f).Within(0.001f));
            Assert.That(Vector3.Distance(ramp.TopPosition, new Vector3(0f, 0f, -15.8f)), Is.LessThan(0.001f));
            Assert.That(entry.size.x, Is.EqualTo(14f).Within(0.001f));
            Assert.That(entry.size.z, Is.GreaterThanOrEqualTo(10f),
                "the crest is a threshold to read the hill from, not a doorstep");
            Assert.That(runout.size.x, Is.EqualTo(14f).Within(0.001f));
            Assert.That(def.playerStart, Is.EqualTo(new Vector3(0f, 36.3f, -162.3f)));
        }

        /// <summary>
        /// The first perch is the one the player reported as waking too late. It has to be inside the
        /// sentry's WAKE radius from the spawn point - which EnemyController measures with y flattened -
        /// so it is already tracking before the player has moved, and inside projectileMaxRange from the
        /// gate so its arm-up starts the instant the gate opens rather than after a closing run.
        /// </summary>
        [Test]
        public void FirstPerchIsAwakeAndInRangeFromTheCrestBeforeTheGateOpens()
        {
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("pshooter_enemy03"));
            Assert.IsNotNull(data, "shipped surge turret data missing");
            float wake = Mathf.Max(data.aggroRange, data.projectileMaxRange);

            var first = def.spawns.Single(s => s.name == "Spawn_T0_Surge_1");
            var flat = new Vector2(first.position.x - def.playerStart.x, first.position.z - def.playerStart.z);
            Assert.That(flat.magnitude, Is.LessThan(wake - 1.5f),
                "perch 1 must already be awake at the player's start, with margin; wake is horizontal only");

            var volley = def.projectileSequences.Single(s => s.name == "T0_SurgeVolley");
            var ramp = def.ramps.Single(r => r.name == "T0_Ramp_Descent");
            Assert.That(volley.progressOrigin.z, Is.EqualTo(ramp.basePosition.z).Within(0.001f),
                "the gate coordinate's origin is the lip of the hill, so gate 0 opens at the slowest " +
                "moment on it; leaving it below the lip would hand the first beat a 21 m/s arrival");
            Vector3 gate = RampChest(volley.progressOrigin.z + volley.memberProgressGates[0]);
            float atGate = Vector3.Distance(first.position + Vector3.up * 1.3f, gate);
            Assert.That(atGate, Is.LessThan(data.projectileMaxRange - 2f),
                "perch 1 must be in band AT the gate or the arm-up does not start there");

            // Announcement = how far below its gate each perch sits. Beat 1 alone used to be 22 m while
            // the rest were 26-32, which is why its bolt flew half as long as every other one.
            var perches = volley.spawnerNames.Select(n => def.spawns.Single(s => s.name == n)).ToArray();
            for (int i = 0; i < perches.Length; i++)
            {
                float announcement = perches[i].position.z - (volley.progressOrigin.z + volley.memberProgressGates[i]);
                Assert.That(announcement, Is.InRange(25f, 33f),
                    perches[i].name + " announcement " + announcement + " m is out of the ladder's band");
            }
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
                // The rear terrace is the tight one and lands on EXACTLY 6.00 m; the epsilon is for
                // float32 in SurfaceY, not slack in the rule.
                Assert.That(clearance, Is.GreaterThan(5.99f), piece.name + " route clearance");
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
            // The first gate is 0 and must stay 0. It was raised to 14 on 2026-09-07 to buy runway and
            // the user reported the sliding parry rhythm broke; runway is bought by lengthening the ramp
            // ABOVE progressOrigin, never by gating the first beat later. See ENGINEERING-LOG.
            Assert.That(volley.memberProgressGates[0], Is.EqualTo(0f),
                "the first beat must be able to open as soon as the player is on the slope");
            var slope = def.ramps.Single(r => r.name == "T0_Ramp_Descent");
            Assert.That(volley.memberProgressGates.All(p => p >= 0f && p < slope.run), Is.True,
                "every launch gate lies on the descent");
            Assert.That(def.spawns.Where(s => s.name.StartsWith("Spawn_T0_Surge_"))
                           .All(s => s.position.z > slope.basePosition.z &&
                                     s.position.z < slope.TopPosition.z - 20f), Is.True,
                "every perch sits on the hill with room below the last contact to feel the surge");
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
                    members, 0.11f, 1.1f, 1.25f, new Vector3(0f, 0f, -159.8f), Vector3.forward,
                    new[] { 0f, 18f, 40f, 64f, 87f });

                LevelDefinitionExporter.ExportInto(root, fresh);

                var saved = fresh.projectileSequences.Single();
                Assert.That(saved.name, Is.EqualTo("T0_SurgeVolley"));
                Assert.That(saved.spawnerNames, Is.EqualTo(members.Select(m => m.name).ToArray()));
                Assert.That(saved.recoveryGap, Is.EqualTo(0.11f).Within(0.0001f));
                Assert.That(saved.readinessTimeout, Is.EqualTo(1.1f).Within(0.0001f));
                Assert.That(saved.shotResolutionTimeout, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(saved.progressOrigin, Is.EqualTo(new Vector3(0f, 0f, -159.8f)));
                Assert.That(saved.progressDirection, Is.EqualTo(Vector3.forward));
                Assert.That(saved.memberProgressGates, Is.EqualTo(new[] { 0f, 18f, 40f, 64f, 87f }));
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

        [Test]
        public void ResolutionTimeoutRetiresTrackedIncomingBolt_WhileZeroKeepsLegacyWait()
        {
            var host = new GameObject("SequenceResolutionTest");
            var marker = new GameObject("Member");
            try
            {
                var member = marker.AddComponent<EnemySpawner>();
                var sequence = host.AddComponent<ProjectileVolleySequence>();
                sequence.Configure(new[] { member }, 0.11f, 1.1f, 1.25f,
                    Vector3.zero, Vector3.zero, null);
                var boltGo = new GameObject("TimedOutBolt");
                var bolt = boltGo.AddComponent<Projectile>();
                SetLaunchedShot(sequence, bolt, Time.time - 2f);
                InvokePrivate(sequence, "Update");
                Assert.IsTrue(bolt == null, "the coordinator retires only its still-incoming tracked bolt");
                Assert.That(sequence.CurrentIndex, Is.EqualTo(1));

                sequence.Configure(new[] { member }, 0.11f, 1.1f);
                boltGo = new GameObject("LegacyBolt");
                bolt = boltGo.AddComponent<Projectile>();
                SetLaunchedShot(sequence, bolt, Time.time - 10f);
                InvokePrivate(sequence, "Update");
                Assert.IsTrue(bolt != null, "zero timeout preserves the legacy projectile-lifetime wait");
                Assert.That(sequence.CurrentIndex, Is.Zero);
                sequence.Restart();
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(marker);
            }
        }

        [Test]
        public void ResetCleansIncomingBolt_ButResolvedReflectionSurvives()
        {
            var host = new GameObject("SequenceResetTest");
            var marker = new GameObject("Member");
            GameObject reflectedGo = null;
            try
            {
                var member = marker.AddComponent<EnemySpawner>();
                var sequence = host.AddComponent<ProjectileVolleySequence>();
                sequence.Configure(new[] { member }, 0.11f, 1.1f, 1.25f,
                    Vector3.zero, Vector3.zero, null);

                var incomingGo = new GameObject("IncomingBolt");
                var incoming = incomingGo.AddComponent<Projectile>();
                SetLaunchedShot(sequence, incoming, Time.time);
                sequence.Restart();
                Assert.IsTrue(incoming == null, "restart removes the old incoming attack");

                reflectedGo = new GameObject("ReflectedBolt");
                var reflected = reflectedGo.AddComponent<Projectile>();
                typeof(Projectile).GetField("reflected",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(reflected, true);
                SetLaunchedShot(sequence, reflected, Time.time - 2f);
                InvokePrivate(sequence, "Update");
                Assert.IsTrue(reflected != null, "a perfect-parry return remains alive to hit its shooter");
                Assert.That(sequence.CurrentIndex, Is.EqualTo(1));
            }
            finally
            {
                if (reflectedGo != null) Object.DestroyImmediate(reflectedGo);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(marker);
            }
        }

        static void SetLaunchedShot(ProjectileVolleySequence sequence, Projectile bolt, float launchedAt)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ProjectileVolleySequence);
            type.GetField("activeBolt", flags).SetValue(sequence, bolt);
            type.GetField("shotLaunched", flags).SetValue(sequence, true);
            type.GetField("shotLaunchedAt", flags).SetValue(sequence, launchedAt);
        }

        static void InvokePrivate(ProjectileVolleySequence sequence, string method)
        {
            typeof(ProjectileVolleySequence).GetMethod(method,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(sequence, null);
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
