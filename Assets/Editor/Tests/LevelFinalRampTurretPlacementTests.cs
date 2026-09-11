using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;
using T = VibeGame1.EditorTools.LevelTraversalAnalyzer;

namespace VibeGame1.Tests
{
    public class LevelFinalRampTurretPlacementTests
    {
        LevelDefinition def;
        [SetUp]
        public void Load()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            def = Object.Instantiate(shipped);
            LevelDefinitionAuthoring.Apply(def);
        }
        [TearDown] public void Clean() { Object.DestroyImmediate(def); }

        [Test]
        public void ThreeSingleShotTurretsAlternateBesideTheActualRamp()
        {
            Vector3[] expected =
            {
                new Vector3(-7f, 22.1f, 418.8f),
                new Vector3( 7f, 19.1f, 430.8f),
                new Vector3(-7f, 16.1f, 442.8f),
            };
            var ramp = def.ramps.Single(r => r.name == "T4_Ramp_Descent");
            for (int i = 0; i < expected.Length; i++)
            {
                var spawn = def.spawns.Single(s => s.name == "Spawn_T4_Surge_" + (i + 1));
                var pad = def.platforms.Single(p => p.name == "T4_TurretPad_" + (i + 1));
                Assert.That(Vector3.Distance(spawn.position, expected[i]), Is.LessThan(0.001f), spawn.name);
                Assert.AreEqual(spawn.position.x > 0f ? 210f : 150f, spawn.yaw, 0.001f, spawn.name);
                float progress = spawn.position.z - ramp.basePosition.z;
                Assert.That(progress, Is.InRange(0f, ramp.run), spawn.name + " is not beside the ramp");
                float surfaceY = ramp.basePosition.y + ramp.rise * (progress / ramp.run);
                Assert.AreEqual(surfaceY + 0.1f, spawn.position.y, 0.001f, spawn.name);
                Assert.AreEqual(surfaceY - 0.5f, pad.center.y, 0.001f, pad.name);
                Assert.Greater(Mathf.Abs(pad.center.x) - pad.size.x * 0.5f, ramp.width * 0.5f,
                    pad.name + " narrows the running surface");
            }
        }

        [Test]
        public void EveryFinalRampMuzzleHasAnUnblockedCrossRampLine()
        {
            var boxes = A.BoxesFrom(def);
            for (int i = 1; i <= 3; i++)
            {
                string padName = "T4_TurretPad_" + i;
                var spawn = def.spawns.Single(s => s.name == "Spawn_T4_Surge_" + i);
                var others = new List<A.Box>(boxes.Where(b => b.name != padName));
                Vector3 muzzle = spawn.position + Vector3.up * 1.5f;
                Vector3 routeChest = new Vector3(0f, spawn.position.y + 1.2f, spawn.position.z);
                Assert.IsTrue(T.LineClear(muzzle, routeChest, others), spawn.name + " is blocked from its ramp crossing");
            }
        }

        [Test]
        public void EngagementWindowsMatchTheRepairedRampBeats()
        {
            var sequence = def.projectileSequences.Single(s => s.name == "T4_SurgeRoute");
            var ramp = def.ramps.Single(r => r.name == "T4_Ramp_Descent");
            Assert.IsTrue(sequence.CoordinatesRuntime,
                "the fast descent needs one ordered owner; three independent acquire clocks skip late turrets");
            Assert.That(sequence.progressOrigin, Is.EqualTo(ramp.basePosition + Vector3.up * 1.2f));
            Assert.That(sequence.progressDirection, Is.EqualTo(ramp.Heading));
            Assert.That(sequence.memberProgressGates, Is.EqualTo(new[] { 0f, 14f, 28f }));
            Assert.That(sequence.firstMemberAcquireDelay, Is.Zero,
                "the visible approach supplies the read; a second 0.7 s arm-up pushes the rhythm off the ramp");
            float[] starts = { 8f, 20f, 32f };
            float[] ends = { 30f, 42f, 48f };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(starts[i], sequence.engagementWindows[i].arrivalStart, 0.001f);
                Assert.AreEqual(ends[i], sequence.engagementWindows[i].arrivalEnd, 0.001f);
            }
        }
    }
}
