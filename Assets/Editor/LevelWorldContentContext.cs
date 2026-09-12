using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Construction policy for an explicit world-content root. PreviewSafe keeps authoring geometry,
    /// renderers, markers and collider data, but disables every generated gameplay MonoBehaviour and
    /// Collider before returning. The CloudSea presentation component stays enabled because disabling it
    /// deliberately releases its generated mesh; it has no input, collision, combat or event behaviour.
    /// </summary>
    public enum LevelWorldContentSafety
    {
        CampaignRuntime,
        PreviewSafe
    }

    /// <summary>
    /// Explicit dependencies for data-driven level world construction.  Unlike the campaign wrapper,
    /// this context neither chooses a scene nor owns scene cleanup, saving, NavMesh or player bootstrap.
    /// A preview supplies its own root and asset-resolution policy; the campaign wrapper supplies the
    /// canonical editor context.
    /// </summary>
    public sealed class LevelWorldContentContext
    {
        public Transform root;
        public LevelPieceContext pieces;
        public IDictionary<string, EnemySpawner> builtSpawners;
        public Material pedestalStone;
        public Material pedestalCyan;
        public Material cloudSea;
        public LevelWorldContentSafety safety = LevelWorldContentSafety.CampaignRuntime;
        public bool buildCloudSea;

        public bool BuildsRuntimeBehaviours { get { return safety == LevelWorldContentSafety.CampaignRuntime; } }
    }

    /// <summary>Artifacts produced below one explicit world-content root.</summary>
    public sealed class LevelWorldContentResult
    {
        public Transform root;
        public GameObject startSpawn;
        public LevelPieceFactory.Counts counts;
        public IDictionary<string, EnemySpawner> builtSpawners;
    }
}
