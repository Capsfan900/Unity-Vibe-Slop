using NUnit.Framework;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class ParryModuleSolverTests
    {
        [Test]
        public void SameCaptureProducesByteEquivalentModuleWithoutDroppingBeats()
        {
            var capture = Capture(0f, .4f, .8f);
            var stage = Stage();
            var solver = new ParryModuleSolver();

            var a = solver.Solve(capture, stage, new ParrySolverSettings());
            var b = solver.Solve(capture, stage, new ParrySolverSettings());

            Assert.IsTrue(a.success);
            Assert.AreEqual(JsonUtility.ToJson(a.module), JsonUtility.ToJson(b.module));
            Assert.AreEqual(capture.desiredBeats.Count, a.report.beats.Count);
            Assert.That(a.module.spawns, Has.Length.EqualTo(1));
            Assert.AreEqual("pshooter_enemy02", a.module.spawns[0].prefabKey);
        }

        [Test]
        public void UnsatisfiedBeatRemainsInReport()
        {
            var stage = Stage();
            stage.zones[0].size = Vector3.one;

            var result = new ParryModuleSolver().Solve(Capture(0f), stage, new ParrySolverSettings());

            Assert.IsFalse(result.success);
            Assert.AreEqual(1, result.report.beats.Count);
            Assert.IsFalse(result.report.beats[0].satisfied);
            Assert.IsNotEmpty(result.report.beats[0].failure);
        }

        [Test]
        public void ApplyToDraftAddsOnlyGeneratedSpawns()
        {
            var stage = Stage();
            stage.spawns = new[] { new SpawnDef { name = "Manual", prefabKey = "Enemy_Grunt" } };
            var draft = new LevelDraft { definition = stage };
            var result = new ParryModuleSolver().Solve(Capture(0f), stage, new ParrySolverSettings());

            ParryModuleSolver.ApplyToDraft(result, draft, "T0");

            Assert.AreEqual("Manual", draft.definition.spawns[0].name);
            Assert.AreEqual(2, draft.definition.spawns.Length);
        }

        static ParryCaptureFile Capture(params float[] times)
        {
            var capture = new ParryCaptureFile();
            for (int i = 0; i < times.Length; i++) capture.desiredBeats.Add(new DesiredParryBeat
            {
                ordinal = i + 1, captureSeconds = times[i], position = new Vector3(0f, 1f, i * 5f),
                velocity = Vector3.forward * 10f, lookDirection = Vector3.forward, playerSpeed = 10f, zoneId = "T0"
            });
            return capture;
        }

        static LevelDefinition Stage()
        {
            var stage = ScriptableObject.CreateInstance<LevelDefinition>();
            stage.zones = new[] { new ZoneDef { zoneId = "T0", center = new Vector3(0f, 0f, 20f), size = new Vector3(40f, 20f, 100f) } };
            return stage;
        }
    }
}
