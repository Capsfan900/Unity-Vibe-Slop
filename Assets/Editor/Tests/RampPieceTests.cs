using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The Ramp piece (2026-09-07): the first sloped surface in the game. What has to stay true:
    /// the rise/run arithmetic a designer authors is the angle the slab is actually built at; the
    /// enum stays append-only so old level JSON still loads; a ramp survives the JSON round trip and
    /// the definition mirror; and the runtime and editor-time contexts build the same slab.
    ///
    /// This fixture covers GEOMETRY AND DATA only. Nothing here says what the motor does on a slope.
    /// </summary>
    public class RampPieceTests
    {
        static RampDef Sample()
        {
            return new RampDef
            {
                name = "Ramp_0", basePosition = new Vector3(2f, 1f, -3f),
                width = 4f, run = 6f, rise = 2f, thickness = 0.5f, yaw = 90f,
                materialKey = "Platform", isStatic = true,
            };
        }

        [Test]
        public void TheAngleIsDerivedFromRiseOverRun_AndTheDefaultsAreTheDocumentedOnes()
        {
            var fresh = new RampDef();
            Assert.AreEqual(6f, fresh.run, 1e-4f, "the default run");
            Assert.AreEqual(2f, fresh.rise, 1e-4f, "the default rise");
            Assert.AreEqual(18.4349f, fresh.AngleDegrees, 0.01f, "2 m up over 6 m is 18.43 deg, as the tooltip says");
            Assert.Less(fresh.AngleDegrees, 45f, "a ramp steeper than a CharacterController's slope limit is not walkable");

            Assert.AreEqual(45f, new RampDef { run = 6f, rise = 6f }.AngleDegrees, 0.01f);
            Assert.AreEqual(26.5651f, new RampDef { run = 6f, rise = 3f }.AngleDegrees, 0.01f);
            Assert.AreEqual(0f, new RampDef { run = 6f, rise = 0f }.AngleDegrees, 1e-4f, "no rise is a flat slab, not a divide by zero");
            Assert.Less(new RampDef { run = 6f, rise = -2f }.AngleDegrees, 0f, "a negative rise descends");
            Assert.AreEqual(0f, new RampDef { run = 0f, rise = 0f }.AngleDegrees, 1e-3f, "a zero run must not divide by zero");

            // The sloped face is the hypotenuse, which is what the slab's length has to be.
            Assert.AreEqual(Mathf.Sqrt(6f * 6f + 2f * 2f), fresh.SlopeLength, 1e-4f);
            Assert.Greater(fresh.SlopeLength, fresh.run, "the face is longer than the ground it covers");
        }

        [Test]
        public void TheTopEdgeIsExactlyRunAlongTheYawAndRiseUp()
        {
            var r = Sample();   // yaw 90 climbs toward +x
            Vector3 top = r.TopPosition;
            Assert.AreEqual(2f + 6f, top.x, 1e-3f);
            Assert.AreEqual(1f + 2f, top.y, 1e-3f);
            Assert.AreEqual(-3f, top.z, 1e-3f);
            // NUnit compares Vector3 with Equals (BIT-EXACT), not the == epsilon, so a trig-derived
            // heading fails against a literal while printing two identical-looking vectors. This has
            // cost this project a session before -- see ENGINEERING-LOG. Compare per-component.
            Assert.AreEqual(1f, r.Heading.x, 1e-4f, "yaw 90 heads +x");
            Assert.AreEqual(0f, r.Heading.y, 1e-4f, "a heading is horizontal");
            Assert.AreEqual(0f, r.Heading.z, 1e-4f, "yaw 90 heads +x");

            foreach (float yaw in new[] { 0f, 90f, 180f, 270f, 37f })
            {
                var q = new RampDef { basePosition = Vector3.zero, run = 6f, rise = 2f, yaw = yaw };
                Assert.AreEqual(6f, new Vector2(q.TopPosition.x, q.TopPosition.z).magnitude, 1e-3f, "run is horizontal at yaw " + yaw);
                Assert.AreEqual(2f, q.TopPosition.y, 1e-3f, "rise is vertical at yaw " + yaw);
            }
        }

        [Test]
        public void TheBuiltSlabSurfaceRunsFromTheBaseToTheTop()
        {
            // The whole contract of the piece: the box the factory builds has its walkable face passing
            // through basePosition and TopPosition, and its normal pitched by the derived angle.
            var r = Sample();
            Quaternion rot = r.Rotation;
            Vector3 normal = rot * Vector3.up;
            Vector3 surfaceCentre = r.BoxCenter + normal * (r.thickness * 0.5f);
            Assert.Less((surfaceCentre - (r.basePosition + r.TopPosition) * 0.5f).magnitude, 1e-3f,
                        "the slab's top face is not centred on the authored span");
            Assert.AreEqual(Mathf.Cos(r.AngleDegrees * Mathf.Deg2Rad), normal.y, 1e-3f,
                            "the ground normal a motor reads is the derived slope angle");
            Assert.AreEqual(r.SlopeLength, r.BoxScale.z, 1e-4f);
            Assert.AreEqual(r.width, r.BoxScale.x, 1e-4f);
            Assert.AreEqual(r.thickness, r.BoxScale.y, 1e-4f);
        }

        [Test]
        public void TheKindEnumIsAppendOnly_SoOlderLevelJsonStillLoads()
        {
            // Level JSON stores these as NUMBERS. Reordering would turn every saved platform into a wall.
            Assert.AreEqual(0, (int)LevelPieceKind.Platform);
            Assert.AreEqual(1, (int)LevelPieceKind.WallFace);
            Assert.AreEqual(2, (int)LevelPieceKind.Balloon);
            Assert.AreEqual(3, (int)LevelPieceKind.Water);
            Assert.AreEqual(4, (int)LevelPieceKind.Spawn);
            Assert.AreEqual(5, (int)LevelPieceKind.Pickup);
            Assert.AreEqual(6, (int)LevelPieceKind.Checkpoint);
            Assert.AreEqual(7, (int)LevelPieceKind.Torch);
            Assert.AreEqual(8, (int)LevelPieceKind.PlayerStart);
            Assert.AreEqual(9, (int)LevelPieceKind.Ramp, "Ramp must be APPENDED, never inserted");
        }

        [Test]
        public void ARampSnapsToTheMetreGrid_LikeThePiecesItHasToMeet()
        {
            Assert.AreEqual(LevelEditorMath.PlatformGrid, LevelEditorMath.GridFor(LevelPieceKind.Ramp),
                            "a ramp's ends meet platform edges, which are on the metre");
            Assert.AreEqual(LevelEditorMath.GridFor(LevelPieceKind.Platform), LevelEditorMath.GridFor(LevelPieceKind.Ramp));
        }

        [Test]
        public void RampsSurviveTheJsonRoundTrip()
        {
            var d = LevelDocument.NewDefault("ramp level");
            d.ramps.Add(Sample());
            var back = LevelDocument.FromJson(d.ToJson());
            Assert.IsNotNull(back);
            Assert.AreEqual(1, back.ramps.Count, "the ramp did not survive the round trip");
            var r = back.ramps[0];
            Assert.AreEqual("Ramp_0", r.name);
            Assert.AreEqual(new Vector3(2f, 1f, -3f), r.basePosition);
            Assert.AreEqual(6f, r.run, 1e-4f); Assert.AreEqual(2f, r.rise, 1e-4f);
            Assert.AreEqual(4f, r.width, 1e-4f); Assert.AreEqual(0.5f, r.thickness, 1e-4f);
            Assert.AreEqual(90f, r.yaw, 1e-4f);
            Assert.AreEqual("Platform", r.materialKey);
            Assert.AreEqual(d.PieceCount, back.PieceCount);
        }

        [Test]
        public void AFileSavedBeforeRampsExisted_StillLoads()
        {
            // Backward compatibility is exactly the rule every other list follows: JsonUtility leaves a
            // missing list NULL, and the editor indexes every one of them, so FromJson fills it in.
            var old = "{\"levelId\":\"old\",\"displayName\":\"Old\",\"parTime\":90.0," +
                      "\"platforms\":[{\"name\":\"Ground\",\"center\":{\"x\":0,\"y\":-0.5,\"z\":0}," +
                      "\"size\":{\"x\":16,\"y\":1,\"z\":16},\"materialKey\":\"Platform\"}]}";
            var back = LevelDocument.FromJson(old);
            Assert.IsNotNull(back);
            Assert.IsNotNull(back.ramps, "a pre-ramp file must not come back with a null ramp list");
            Assert.AreEqual(0, back.ramps.Count);
            Assert.AreEqual(1, back.platforms.Count, "the old pieces still load");
            Assert.AreEqual(1, back.PieceCount);
        }

        [Test]
        public void TheDefinitionMirrorsRampsBothWays()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                Assert.IsNotNull(def.ramps, "a fresh definition has an empty ramp array, never null");
                var d = LevelDocument.NewDefault("ramp level");
                d.ramps.Add(Sample());
                d.CopyTo(def);
                Assert.AreEqual(1, def.ramps.Length);
                Assert.AreEqual(6f, def.ramps[0].run, 1e-4f);
                var again = LevelDocument.FromDefinition(def);
                Assert.AreEqual(1, again.ramps.Count);
                Assert.AreEqual("Ramp_0", again.ramps[0].name);
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void TheRuntimeAndEditorContextsBuildTheSameRamp()
        {
            // The same rule as LevelEditorTests.TheRuntimeFactory_BuildsWhatTheEditorBuilderBuilds: one
            // factory, two contexts, identical objects by name, position, rotation and scale.
            var doc = LevelDocument.NewDefault("ramp level");
            doc.ramps.Add(Sample());
            doc.ramps.Add(new RampDef { name = "Ramp_1", basePosition = new Vector3(-4f, 0f, 8f), yaw = 180f });

            var a = new GameObject("~RampA").transform;
            var b = new GameObject("~RampB").transform;
            try
            {
                var editorCtx = EditorContext.Default();
                var runtimeCtx = new LevelPieceContext
                {
                    materials = editorCtx.materials, prefabs = editorCtx.prefabs, items = editorCtx.items,
                    instantiate = prefab => Object.Instantiate(prefab), markStatic = null,
                };
                var ca = LevelPieceFactory.BuildDocument(doc, a, editorCtx);
                var cb = LevelPieceFactory.BuildDocument(doc, b, runtimeCtx);
                Assert.AreEqual(ca.ToString(), cb.ToString(), "the two contexts built different counts");
                Assert.AreEqual(2, ca.ramps, "both ramps were built and counted");

                foreach (var name in new[] { "Ramp_0", "Ramp_1" })
                {
                    var ta = a.Find(name); var tb = b.Find(name);
                    Assert.IsNotNull(ta, "editor context built no " + name);
                    Assert.IsNotNull(tb, "runtime context built no " + name);
                    Assert.Less((ta.position - tb.position).magnitude, 1e-3f, name + " is at a different position");
                    Assert.Less(Quaternion.Angle(ta.rotation, tb.rotation), 0.01f, name + " is at a different rotation");
                    Assert.Less((ta.localScale - tb.localScale).magnitude, 1e-3f, name + " is a different size");
                    Assert.IsNotNull(tb.GetComponent<BoxCollider>(), name + " has no collider to stand on");
                    Assert.AreEqual(0, tb.gameObject.layer, name + " must be on Default so the NavMesh bake sees it");
                    var piece = tb.GetComponent<LevelPiece>();
                    Assert.IsNotNull(piece, name + " has no LevelPiece tag");
                    Assert.AreEqual(LevelPieceKind.Ramp, piece.kind);
                    Assert.GreaterOrEqual(piece.index, 0);
                }
                Assert.AreEqual(0, a.Find("Ramp_0").GetComponent<LevelPiece>().index);
                Assert.AreEqual(1, a.Find("Ramp_1").GetComponent<LevelPiece>().index);

                // Hard rule 10: a traversal piece is geometry, nothing else. The LevelPiece tag is data.
                foreach (var mb in b.Find("Ramp_0").GetComponents<MonoBehaviour>())
                    Assert.IsInstanceOf<LevelPiece>(mb, "a ramp must carry no behaviour: found " + mb.GetType().Name);
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
            }
        }

        [Test]
        public void ABuiltRampReadsBackIntoTheNumbersThatMadeIt()
        {
            // Export Current Level To Definition uses RampFrom; a ramp that exported as a flat platform
            // would silently un-slope the level on the next build.
            var root = new GameObject("~RampExport").transform;
            try
            {
                var source = new List<RampDef>
                {
                    Sample(),
                    new RampDef { name = "R1", basePosition = new Vector3(-3f, 2f, 5f), width = 5f, run = 8f, rise = 3f, thickness = 0.4f, yaw = 215f },
                    new RampDef { name = "R2", basePosition = new Vector3(1f, 0f, 1f), width = 3f, run = 4f, rise = -1.5f, thickness = 0.6f, yaw = 0f },
                };
                foreach (var r in source)
                {
                    var go = LevelPieceFactory.Ramp(r, root, new LevelPieceContext(), null, 0);
                    var back = LevelPieceFactory.RampFrom(go.transform, r.name, r.materialKey, r.isStatic);
                    Assert.Less((back.basePosition - r.basePosition).magnitude, 1e-3f, r.name + " base");
                    Assert.AreEqual(r.width, back.width, 1e-3f, r.name + " width");
                    Assert.AreEqual(r.thickness, back.thickness, 1e-3f, r.name + " thickness");
                    Assert.AreEqual(r.run, back.run, 1e-3f, r.name + " run");
                    Assert.AreEqual(r.rise, back.rise, 1e-3f, r.name + " rise");
                    Assert.AreEqual(0f, Mathf.DeltaAngle(r.yaw, back.yaw), 0.05f, r.name + " yaw");
                    Assert.Less((back.TopPosition - r.TopPosition).magnitude, 1e-3f, r.name + " top edge");
                }
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }
    }
}
