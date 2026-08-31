using UnityEditor;
using UnityEngine;

namespace EnemyForge.Editor
{
    /// <summary>
    /// Auto-configures FBX files produced by enemy-forge on import: metre scale,
    /// Humanoid avatar, and sane mesh/material defaults.
    ///
    /// Drop this in any folder named "Editor". It only touches assets whose path
    /// contains <see cref="MarkerFolder"/>, so it will not hijack your other models.
    /// </summary>
    public class EnemyForgeImporter : AssetPostprocessor
    {
        /// Only FBXs under a folder with this in the path are processed.
        private const string MarkerFolder = "Enemies";

        private bool IsForgeAsset =>
            assetPath.Replace('\\', '/').Contains("/" + MarkerFolder + "/") &&
            assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!IsForgeAsset) return;

            var importer = (ModelImporter)assetImporter;

            // Only apply defaults on first import. Re-importing must not stomp
            // tweaks you have made in the inspector afterwards.
            if (!importer.importSettingsMissing) return;

            importer.globalScale = 1f;              // forge exports in metres
            importer.useFileScale = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.isReadable = false;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;

            // The rig ships with Unity Humanoid bone names, so the built-in
            // mapper resolves it without a hand-authored avatar.
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;

            Debug.Log($"[EnemyForge] Import defaults applied: {assetPath}");
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!IsForgeAsset) return;

            var importer = (ModelImporter)assetImporter;
            if (importer.animationType != ModelImporterAnimationType.Human) return;

            // Surface a clear warning rather than letting a silently-invalid
            // avatar fail later at runtime when you try to retarget animation.
            var animator = root.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid)
            {
                Debug.LogWarning(
                    $"[EnemyForge] '{root.name}' did not produce a valid Humanoid avatar. " +
                    "The mesh is probably too far from human proportions. Switch the model " +
                    "to Generic in the Rig tab, or re-run forge.py with --arm-span to widen " +
                    "the arm chain.", root);
            }
        }
    }
}
