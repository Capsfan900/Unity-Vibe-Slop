using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Splits an ANIMATED enemy-forge FBX into named <see cref="AnimationClip"/>s, driven by the
    /// <c>&lt;model&gt;.clips.json</c> manifest that ships beside it.
    ///
    /// <para><b>Why this exists.</b> enemy-forge exports every animation as ONE continuous take. Unity
    /// imports that take as a single clip called "Take 001", which is useless: you cannot play "Walk"
    /// or "AttackSwing" out of it. The frame ranges that carve it up live in the manifest, and typing
    /// them into the Rig/Animation inspector by hand is exactly the kind of hand-authored state hard
    /// rule 4 forbids — re-import the FBX and the numbers are gone. So the manifest is the source of
    /// truth and this menu item is the only thing that writes <c>ModelImporter.clipAnimations</c>.</para>
    ///
    /// <para><b>Generic, not Humanoid.</b> <c>EnemyForgeImporter</c> (the tool's own vendored
    /// postprocessor) asks for a Humanoid avatar on first import. That is wrong for every model this
    /// project has taken: a forge silhouette is not human-proportioned, the mapper either fails or
    /// produces a mangled avatar, and retargeting buys nothing when the clips were authored ON this
    /// exact rig. This splitter forces <c>Generic</c> with <c>CopyFromOther = false</c>, so the clips
    /// play back bone-for-bone as authored.</para>
    ///
    /// <para><b>No AnimationEvents are written</b>, deliberately. The manifest carries them
    /// (<c>OnFootstep</c>, <c>OnAttackHit</c>, …) but an event with no receiver logs a warning every
    /// time the clip plays, and — more importantly — an event firing gameplay would make the fight's
    /// timing hostage to a clip length. Timing in this project is DATA-driven from
    /// <see cref="EnemyAttackData"/>. The manifest's <c>OnAttackHit</c> time is still used, but at
    /// BUILD time: <see cref="ReadHitNormalizedTime"/> hands it to the prefab factory, which bakes it
    /// onto the visuals component so playback speed can be scaled to make the clip's own contact frame
    /// land on the data's impact. See docs/ARCHITECTURE.md → "The Pale Marionette".</para>
    /// </summary>
    public static class ForgeClipSplitter
    {
        const string ModelDir = "Assets/Enemies";

        // ------------------------------------------------------------------ manifest shape

        [Serializable]
        class ClipEvent
        {
            public float time;          // NORMALISED 0..1 within the clip, not seconds
            public string function;
        }

        [Serializable]
        class ClipEntry
        {
            public string name;
            public int start;           // inclusive, in FBX frames
            public int end;             // inclusive
            public bool loop;
            public ClipEvent[] events;
        }

        [Serializable]
        class ClipManifest
        {
            public ClipEntry[] clips;
        }

        // ------------------------------------------------------------------ menu

        [MenuItem("VibeGame1/4a. Split Forge Animation Clips")]
        public static void SplitAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[ForgeClips] Refusing to run in play mode. Exit play mode first.");
                return;
            }

            var manifests = Directory.GetFiles(ModelDir, "*.clips.json", SearchOption.TopDirectoryOnly);
            if (manifests.Length == 0)
            {
                Debug.Log("[ForgeClips] No *.clips.json manifests under " + ModelDir +
                          " — nothing to split. (Static models have none; that is normal.)");
                return;
            }

            int done = 0;
            foreach (var m in manifests)
            {
                string modelName = Path.GetFileName(m);
                modelName = modelName.Substring(0, modelName.Length - ".clips.json".Length);
                if (Split(ModelDir + "/" + modelName + ".fbx")) done++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ForgeClips] Split " + done + " of " + manifests.Length + " animated forge models.");
        }

        /// <summary>
        /// Applies the manifest beside <paramref name="fbxPath"/> to that FBX's importer and reimports.
        /// Returns false (having logged) if either file is missing or the manifest is unreadable.
        /// </summary>
        public static bool Split(string fbxPath)
        {
            string manifestPath = ManifestPathFor(fbxPath);
            var entries = ReadManifest(manifestPath);
            if (entries == null) return false;

            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[ForgeClips] No ModelImporter at " + fbxPath +
                               " — the FBX is missing. Copy it into " + ModelDir +
                               " (see docs/AUTHORING.md → Importing a forge model).");
                return false;
            }

            // GENERIC. See the class remarks: Humanoid is wrong for forge silhouettes, and it is what
            // EnemyForgeImporter asks for on first import. Forcing it here means a reimport of the FBX
            // can never silently put the rig back to Humanoid and leave every clip retargeted to mush.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            // Root motion is deliberately NOT used: locomotion belongs to NavMeshLocomotion, and a clip
            // that moved the enemy would fight the agent for the transform.
            importer.animationPositionError = 0.5f;
            importer.animationRotationError = 0.5f;
            importer.animationScaleError = 0.5f;

            var clips = new ModelImporterClipAnimation[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                clips[i] = new ModelImporterClipAnimation
                {
                    name = e.name,
                    takeName = TakeName(importer),
                    firstFrame = e.start,
                    lastFrame = e.end,
                    loopTime = e.loop,
                    loopPose = e.loop,
                    wrapMode = e.loop ? WrapMode.Loop : WrapMode.Once,
                    // No root motion baking: the NavMeshAgent owns the transform.
                    lockRootRotation = false,
                    keepOriginalOrientation = true,
                    keepOriginalPositionY = true,
                    keepOriginalPositionXZ = true,
                    events = new AnimationEvent[0]   // deliberate — see class remarks
                };
            }
            importer.clipAnimations = clips;

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            Debug.Log("[ForgeClips] " + Path.GetFileName(fbxPath) + ": wrote " + clips.Length +
                      " clips (" + string.Join(", ", NamesOf(entries)) + ") as Generic rig.");
            return true;
        }

        /// <summary>
        /// The manifest's <c>OnAttackHit</c> time for one clip, as a 0..1 fraction of the clip — the
        /// frame on which the art actually makes contact. Returns <paramref name="fallback"/> when the
        /// clip or the event is absent. Read at BUILD time only; nothing at runtime consults the JSON.
        /// </summary>
        public static float ReadHitNormalizedTime(string fbxPath, string clipName, float fallback)
        {
            // OnAttackHit first, then OnRoar. A clip chosen for its POSE rather than for its swing —
            // the spin pass uses Roar, because it is the only clip that holds the arms out for its
            // whole length — has no contact event, and OnRoar is the moment its hold is fully open.
            return ReadEventNormalizedTime(fbxPath, clipName, fallback, "OnAttackHit", "OnRoar");
        }

        /// <summary>First of <paramref name="functions"/> present on the clip, as a 0..1 fraction.</summary>
        public static float ReadEventNormalizedTime(string fbxPath, string clipName, float fallback,
                                                    params string[] functions)
        {
            var entries = ReadManifest(ManifestPathFor(fbxPath));
            if (entries == null) return fallback;
            for (int f = 0; f < functions.Length; f++)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].name != clipName || entries[i].events == null) continue;
                    for (int e = 0; e < entries[i].events.Length; e++)
                        if (entries[i].events[e].function == functions[f])
                            return Mathf.Clamp01(entries[i].events[e].time);
                }
            }
            return fallback;
        }

        /// <summary>All clip names the manifest declares, for validation and logging.</summary>
        public static List<string> ClipNames(string fbxPath)
        {
            var entries = ReadManifest(ManifestPathFor(fbxPath));
            return entries == null ? new List<string>() : NamesOf(entries);
        }

        // ------------------------------------------------------------------ internals

        static string ManifestPathFor(string fbxPath)
        {
            return fbxPath.Substring(0, fbxPath.Length - ".fbx".Length) + ".clips.json";
        }

        static List<ClipEntry> ReadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                Debug.LogError("[ForgeClips] Missing manifest " + manifestPath +
                               ". An animated forge model ships its clip ranges alongside the FBX; " +
                               "without it the take cannot be split and every clip would be 'Take 001'.");
                return null;
            }

            // JsonUtility cannot parse a bare top-level ARRAY, which is what forge writes. Wrapping it
            // in an object is the standard workaround and keeps this dependency-free — pulling in
            // Newtonsoft for fifteen records would be the wrong trade.
            string json = "{\"clips\":" + File.ReadAllText(manifestPath) + "}";
            ClipManifest parsed;
            try { parsed = JsonUtility.FromJson<ClipManifest>(json); }
            catch (Exception ex)
            {
                Debug.LogError("[ForgeClips] Could not parse " + manifestPath + ": " + ex.Message);
                return null;
            }

            if (parsed == null || parsed.clips == null || parsed.clips.Length == 0)
            {
                Debug.LogError("[ForgeClips] " + manifestPath + " parsed to zero clips.");
                return null;
            }

            var list = new List<ClipEntry>();
            for (int i = 0; i < parsed.clips.Length; i++)
            {
                var c = parsed.clips[i];
                if (c == null || string.IsNullOrEmpty(c.name)) continue;
                if (c.end <= c.start)
                {
                    Debug.LogWarning("[ForgeClips] Skipping '" + c.name + "': end (" + c.end +
                                     ") is not after start (" + c.start + ").");
                    continue;
                }
                list.Add(c);
            }
            return list.Count > 0 ? list : null;
        }

        /// <summary>
        /// The name of the single take the FBX carries. Reading it rather than hard-coding "Take 001"
        /// matters: a clip whose takeName does not match an actual take imports EMPTY, silently, and
        /// the enemy then stands in a T-pose with no error anywhere.
        /// </summary>
        static string TakeName(ModelImporter importer)
        {
            var takes = importer.importedTakeInfos;
            if (takes != null && takes.Length > 0) return takes[0].name;
            Debug.LogWarning("[ForgeClips] " + importer.assetPath + " reports no takes; falling back to 'Take 001'. " +
                             "If the clips import empty, this is why.");
            return "Take 001";
        }

        static List<string> NamesOf(List<ClipEntry> entries)
        {
            var n = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++) n.Add(entries[i].name);
            return n;
        }
    }
}
