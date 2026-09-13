using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    public class LevelStudioEditingTests
    {
        LevelDefinition definition;
        LevelStudioSceneTool tool;

        [SetUp]
        public void SetUp()
        {
            definition = ScriptableObject.CreateInstance<LevelDefinition>();
            definition.zones = new[] { new ZoneDef { zoneId = "T0", size = Vector3.one * 20f } };
            definition.platforms = new[] { new PlatformDef { meta = Meta("T0.Platform.01"), name = "Platform", center = Vector3.zero } };
            definition.spawns = new[] { new SpawnDef { meta = Meta("T0.Sentry.01"), prefabKey = "pshooter_enemy01", position = Vector3.zero } };
            tool = new LevelStudioSceneTool(definition);
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void EveryCatalogKindHasAnEditorAdapter()
        {
            foreach (LevelObjectKind kind in System.Enum.GetValues(typeof(LevelObjectKind)))
                Assert.IsTrue(LevelStudioSceneTool.Supports(kind), kind.ToString());
        }

        [Test]
        public void MovingASelectedSpawnChangesOnlyItsDraftAndUndoRestoresIt()
        {
            var spawn = LevelObjectCatalog.Enumerate(definition).Single(x => x.kind == LevelObjectKind.Spawn);

            tool.ApplyTransform(spawn, Matrix4x4.TRS(new Vector3(3f, 1f, 9f), Quaternion.Euler(0f, 45f, 0f), Vector3.one));

            Assert.AreEqual(new Vector3(3f, 1f, 9f), definition.spawns[0].position);
            Assert.AreEqual(45f, definition.spawns[0].yaw, 0.01f);
            Assert.AreEqual(Vector3.zero, definition.platforms[0].center);
            Undo.PerformUndo();
            Assert.AreEqual(Vector3.zero, definition.spawns[0].position);
        }

        [Test]
        public void SetZoneBoundsParticipatesInUndo()
        {
            tool.SetZoneBounds("T0", new Bounds(Vector3.up, new Vector3(4f, 5f, 6f)));

            Assert.AreEqual(Vector3.up, definition.zones[0].center);
            Assert.AreEqual(new Vector3(4f, 5f, 6f), definition.zones[0].size);
            Undo.PerformUndo();
            Assert.AreEqual(Vector3.zero, definition.zones[0].center);
        }

        [Test]
        public void DuplicateAndDeleteUseTheAuthoredArrayAndStableIds()
        {
            var platform = LevelObjectCatalog.Enumerate(definition).Single(x => x.kind == LevelObjectKind.Platform);

            string duplicateId = tool.Duplicate(platform);

            Assert.AreEqual(2, definition.platforms.Length);
            Assert.AreEqual("T0.Platform.02", duplicateId);
            Assert.AreEqual(duplicateId, definition.platforms[1].meta.objectId);
            Assert.IsTrue(tool.Delete(LevelObjectCatalog.Enumerate(definition).Single(x => x.meta.objectId == duplicateId)));
            Assert.AreEqual(1, definition.platforms.Length);
            Undo.PerformUndo();
            Assert.AreEqual(2, definition.platforms.Length);
        }

        [Test]
        public void PreviewBuildsInertDontSaveContentAndRebuildsInPlace()
        {
            definition.spawns = new SpawnDef[0];
            definition.sky.enabled = false;
            var pieces = new LevelPieceContext { materials = key => null, prefabs = key => null, items = key => null };
            var preview = new LevelStudioPreview(definition, pieces);
            var root = preview.root;

            Assert.IsNotNull(root.transform.Find("Platform"));
            Assert.AreNotEqual(0, root.hideFlags & HideFlags.DontSaveInEditor);
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) Assert.IsFalse(collider.enabled);
            definition.platforms[0].center = Vector3.up;
            preview.Rebuild();
            Assert.AreSame(root, preview.root);
            Assert.AreEqual(Vector3.up, root.transform.Find("Platform").position);

            preview.Dispose();
            Assert.IsTrue(root == null);
        }

        [Test]
        public void PreviewSyncMovesOneObjectWithoutRebuildingThePreview()
        {
            definition.spawns = new SpawnDef[0];
            definition.sky.enabled = false;
            var pieces = new LevelPieceContext { materials = key => null, prefabs = key => null, items = key => null };
            var preview = new LevelStudioPreview(definition, pieces);
            var platform = LevelObjectCatalog.Enumerate(definition).Single(x => x.kind == LevelObjectKind.Platform);
            var child = preview.Find(platform);
            definition.platforms[0].center = Vector3.up;

            preview.Sync(platform);

            Assert.AreSame(child, preview.Find(platform));
            Assert.AreEqual(Vector3.up, child.position);
            preview.Dispose();
        }

        [Test]
        public void CopyPasteKeepsTheTargetStableIdentityAndParticipatesInUndo()
        {
            var platform = LevelObjectCatalog.Enumerate(definition).Single(x => x.kind == LevelObjectKind.Platform);
            Assert.IsTrue(tool.Copy(platform));
            definition.platforms[0].center = Vector3.up;

            Assert.IsTrue(tool.Paste(platform));

            Assert.AreEqual(Vector3.zero, definition.platforms[0].center);
            Assert.AreEqual("T0.Platform.01", definition.platforms[0].meta.objectId);
            Undo.PerformUndo();
            Assert.AreEqual(Vector3.up, definition.platforms[0].center);
        }

        static LevelObjectMeta Meta(string id) { return new LevelObjectMeta { objectId = id, zoneIdOverride = "T0" }; }
    }
}
