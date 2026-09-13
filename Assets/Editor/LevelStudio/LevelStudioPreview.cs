using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>Disposable, inert scene preview built through the campaign's world-content seam.</summary>
    public sealed class LevelStudioPreview : IDisposable
    {
        readonly LevelDefinition definition;
        readonly LevelPieceContext pieces;
        public GameObject root { get; private set; }

        public LevelStudioPreview(LevelDefinition definition, LevelPieceContext pieces = null)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.pieces = pieces ?? EditorContext.Default();
            root = new GameObject("LevelStudioPreview") { hideFlags = HideFlags.HideAndDontSave };
            Rebuild();
        }

        public void Rebuild()
        {
            if (root == null) throw new ObjectDisposedException(nameof(LevelStudioPreview));
            for (int i = root.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            LevelDefinitionBuilder.BuildWorldContent(definition, new LevelWorldContentContext
            {
                root = root.transform,
                pieces = pieces,
                builtSpawners = new Dictionary<string, EnemySpawner>(),
                pedestalStone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Stone.mat"),
                pedestalCyan = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_NeonCyan.mat"),
                safety = LevelWorldContentSafety.PreviewSafe,
                buildCloudSea = false
            });
            Hide(root.transform);
        }

        public Transform Find(LevelObjectRecord record)
        {
            if (root == null || record == null) return null;
            if (record.kind == LevelObjectKind.PlayerStart) return root.transform.Find("StartSpawn");
            string name = Name(record);
            if (!string.IsNullOrEmpty(name))
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) if (child.name == name) return child;
            return null;
        }

        public void Sync(LevelObjectRecord record)
        {
            var target = Find(record);
            Vector3 position, scale;
            Quaternion rotation;
            if (target == null || !LevelStudioSceneTool.TryGetTransform(record, out position, out rotation, out scale)) return;
            target.SetPositionAndRotation(position, rotation);
            target.localScale = scale;
        }

        public void SetHidden(LevelObjectRecord record, bool hidden) { var target = Find(record); if (target != null) target.gameObject.SetActive(!hidden); }
        public void Isolate(IEnumerable<LevelObjectRecord> records)
        {
            var visible = new HashSet<Transform>();
            foreach (var record in records ?? new LevelObjectRecord[0]) { var target = Find(record); if (target != null) visible.Add(target); }
            foreach (Transform child in root.transform) child.gameObject.SetActive(visible.Contains(child));
        }
        public void ShowAll() { if (root != null) foreach (Transform child in root.transform) child.gameObject.SetActive(true); }

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            root = null;
        }

        static void Hide(Transform transform)
        {
            transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < transform.childCount; i++) Hide(transform.GetChild(i));
        }

        static string Name(LevelObjectRecord record)
        {
            var platform = record.data as PlatformDef; if (platform != null) return platform.name;
            var ramp = record.data as RampDef; if (ramp != null) return ramp.name;
            var spawn = record.data as SpawnDef; if (spawn != null) return spawn.name;
            var pickup = record.data as PickupDef; if (pickup != null) return pickup.name;
            var checkpoint = record.data as CheckpointDef; if (checkpoint != null) return checkpoint.name;
            var torch = record.data as TorchDef; if (torch != null) return torch.name;
            var pedestal = record.data as PedestalDef; if (pedestal != null) return pedestal.name;
            var balloon = record.data as BalloonDef; if (balloon != null) return balloon.name;
            var water = record.data as WaterDef; if (water != null) return water.name;
            var arena = record.data as ArenaDef;
            if (arena != null) return record.kind == LevelObjectKind.Gate ? arena.gateName : record.kind == LevelObjectKind.ExitGate ? arena.exitGateName : arena.triggerName;
            var kill = record.data as KillZoneDef; if (kill != null) return kill.name;
            var leaderboard = record.data as WorldLeaderboardDef; if (leaderboard != null) return leaderboard.name;
            return null;
        }
    }
}
