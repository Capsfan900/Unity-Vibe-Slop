using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;
using A = VibeGame1.EditorTools.LevelArcAnalyzer;

namespace VibeGame1.Tests
{
    [Category("LevelLines")]
    public class LevelArcClearanceTests
    {
        LevelDefinition def;
        A.MoveProfile profile;

        [OneTimeSetUp]
        public void Load()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            def = Object.Instantiate(shipped);
            LevelDefinitionAuthoring.Apply(def);
            string error;
            Assert.IsTrue(A.TryLoadProfile(out profile, out error), error);
        }
        [OneTimeTearDown] public void Clean() { Object.DestroyImmediate(def); }

        static IEnumerable<TestCaseData> MainHops()
        {
            string[][] routes =
            {
                new[] { "T1_Stone_1", "T1_Stone_2", "T1_Stone_3", "T1_Stone_4", "T1_Causeway" },
                new[] { "T2_L1", "T2_L2", "T2_L3", "T2_L4", "T2_L5", "T2_L6", "T2_L7", "T2_L8", "T2_L9", "T2_L10", "T2_L11" },
                new[] { "T3_Pillar_1", "T3_Pillar_2", "T3_Pillar_3", "T3_Pillar_4", "T3_Span", "T3_Step_1", "T3_Step_2" },
            };
            foreach (var route in routes)
                for (int i = 0; i + 1 < route.Length; i++)
                    yield return new TestCaseData(route[i], route[i + 1]).SetName(route[i] + "_To_" + route[i + 1]);
        }

        [Test, TestCaseSource(nameof(MainHops))]
        public void OpenMainCourseIsCleanAtBaseRunSpeed(string from, string to)
        {
            var verdict = A.AnalyzeHop(A.BoxesFrom(def), from, to, profile, profile.groundSpeed, def.killZone.center.y);
            Assert.IsTrue(verdict.exists, verdict.Summary());
            Assert.GreaterOrEqual(verdict.cleanLaunchPoints, 3, verdict.Summary());
        }

        static IEnumerable<TestCaseData> PortalGates()
        {
            yield return new TestCaseData("T1_Causeway", new Vector3(0f, 1.5f, 73.05f), new Vector3(12f, 1f, 1f));
            yield return new TestCaseData("T2_L11", new Vector3(0f, 19.5f, 200.92f), new Vector3(12f, 1f, 1f));
            yield return new TestCaseData("T3_Step_2", new Vector3(0f, 26.5f, 341.67f), new Vector3(12f, 1f, 1f));
        }

        [Test, TestCaseSource(nameof(PortalGates))]
        public void PortalEntryRequiresProjectileEarnedCarry(string from, Vector3 targetCenter, Vector3 targetSize)
        {
            var boxes = A.BoxesFrom(def);
            boxes.Add(new A.Box("PortalEntry", targetCenter, targetSize));
            var refused = A.AnalyzeHop(boxes, from, "PortalEntry", profile, profile.SlideJumpSpeed, def.killZone.center.y);
            Assert.IsFalse(refused.exists, refused.Summary() + " -- the portal is not a speed check");

            // Parkour shooters ship 9 m/s of forward gain. Two clean answers provide a robust earned-speed
            // proof without depending on an exact one-contact threshold at the edge of the capsule sweep.
            float earned = profile.groundSpeed + 18f;
            var answered = A.AnalyzeHop(boxes, from, "PortalEntry", profile, earned, def.killZone.center.y);
            Assert.IsTrue(answered.exists, answered.Summary());
            Assert.GreaterOrEqual(answered.cleanLaunchPoints, 2, answered.Summary());
        }
    }
}
