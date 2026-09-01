using VibeGame1;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Builds the three legendary mini-boss prefabs: Legendary_Ninja, Legendary_Knight,
    /// Legendary_Spellsword.
    ///
    /// Split out of <see cref="PrefabFactory"/> deliberately, following the <see cref="WandFactory"/>
    /// precedent: the mini-bosses are content, they are re-tuned far more often than the player rig,
    /// and a separate menu item means iterating on them never rebuilds the whole prefab set.
    ///
    /// <para><b>These are plain <see cref="EnemyController"/>s, not <see cref="BossController"/>s.</b>
    /// BossController owns deathblow segments, <c>Health.deathIsStagger</c>, the HUD boss bar and
    /// <c>RaiseBossDefeated</c> — and that last one stops the speedrun timer and clears the level. A
    /// mini-boss wired as a boss would end the run three times before the Warden. They read as
    /// "legendary" through their <c>EnemyData</c> (size, palette, souls, moveset) plus a distinct
    /// silhouette, not through a different controller.</para>
    ///
    /// <para>The rig mirrors <c>PrefabFactory.BuildEnemy</c> exactly — same child names, same
    /// <see cref="EnemyVisuals"/> bindings, same layer, same posture bar — because
    /// <c>EnemyVisuals</c> and <c>EnemyController</c> resolve those by reference, and the collider /
    /// NavMeshAgent footprint is re-sized at runtime from <c>EnemyData.scale</c>.</para>
    /// </summary>
    public static class MiniBossFactory
    {
        const string PrefabDir = "Assets/Prefabs";
        const string MaterialDir = "Assets/Materials";
        const string EnemyDataDir = "Assets/Data/Enemies";

        /// <summary>
        /// Imported enemy art. <b>This is the one folder in the project that holds source art rather than
        /// generated output</b>, and it is a deliberate exception to hard rule 4 ("everything is
        /// regenerable"): an FBX authored in enemy-forge is authored art, like the CC0 audio in
        /// <c>Resources/Audio</c>, not something a menu item can rebuild. It is committed, and the
        /// builders below reference it BY PATH and <b>fail loudly</b> if it is gone — never falling back
        /// to primitives, because a silent fallback would ship a boxy stand-in that looks like a bug.
        /// See docs/AUTHORING.md → "Importing a forge model".
        /// </summary>
        const string ModelDir = "Assets/Enemies";

        [MenuItem("VibeGame1/4b. Build Mini-Bosses")]
        public static void CreateAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[MiniBossFactory] Refusing to run in play mode. Exit play mode first.");
                return;
            }

            DataFactory.EnsureFolder(PrefabDir);

            BuildMiniBoss("Legendary_Ninja", EnemyDataDir + "/Legendary_Ninja.asset", Silhouette.Ninja);
            BuildMiniBoss("Legendary_Knight", EnemyDataDir + "/Legendary_Knight.asset", Silhouette.Knight);
            BuildMiniBoss("Legendary_Spellsword", EnemyDataDir + "/Legendary_Spellsword.asset", Silhouette.Spellsword);
            // PROTOTYPE. Not in Level_01 — sandbox pad only. See docs/ARCHITECTURE.md → The Pale Marionette.
            BuildMiniBoss("Legendary_Marionette", EnemyDataDir + "/Legendary_Marionette.asset", Silhouette.Marionette);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MiniBossFactory] Built 4 legendary mini-boss prefabs under " + PrefabDir);
        }

        enum Silhouette { Ninja, Knight, Spellsword, Marionette }

        // ------------------------------------------------------------------ the rig

        static void BuildMiniBoss(string name, string dataPath, Silhouette shape)
        {
            var data = Load<EnemyData>(dataPath);
            if (data == null)
            {
                Debug.LogError("[MiniBossFactory] Missing EnemyData " + dataPath +
                               " — run VibeGame1/3. Create Data first. Skipping " + name + ".");
                return;
            }

            var root = new GameObject(name);
            root.layer = Layers.Enemy;

            // Baseline footprint only: EnemyController.Init re-sizes agent and collider by data.scale.
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.45f;
            agent.height = 2f;
            agent.speed = 4f;
            agent.angularSpeed = 360f;
            agent.acceleration = 40f;
            agent.stoppingDistance = 1.5f;

            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.radius = 0.45f;
            col.height = 2f;
            col.isTrigger = false;

            root.AddComponent<Health>();
            root.AddComponent<Posture>();

            var ctrl = root.AddComponent<EnemyController>();
            ctrl.data = data;

            // M_Boss for all three: they are duels, and the boss body material is the visual language
            // the player already reads as "this one is a fight, not a filler".
            Material bodyMat = Mat("M_Boss");

            var spec = ModelFor(shape);

            var visual = Empty("Visual", root.transform, Vector3.zero);
            // A clip-carrying model gets PuppetVisuals — an EnemyVisuals SUBCLASS, so every shared
            // readability channel (colour sink-and-snap, cue flash, alert marker, posture-driven eye,
            // deathblow glyph) is inherited unchanged and only the clips and the whirl are added.
            // EnemyController resolves IEnemyPresentation, so the brain never learns about either.
            bool animated = spec != null && spec.animated;
            var visuals = animated
                ? visual.AddComponent<PuppetVisuals>()
                : visual.AddComponent<EnemyVisuals>();
            var flash = visual.AddComponent<EmissiveFlash>();

            var lungeRoot = Empty("LungeRoot", visual.transform, Vector3.zero);

            GameObject body, eye, weapon;
            Transform shoulder, hand;
            Transform modelRoot = null;

            if (spec != null)
            {
                // ---- imported body (see ModelDir) --------------------------------------------------
                if (!BuildModelBody(spec, lungeRoot.transform, bodyMat,
                                    out body, out eye, out weapon, out shoulder, out hand, out modelRoot))
                {
                    // Fail LOUDLY. BuildModelBody has already logged what is missing; bail rather than
                    // quietly shipping a prefab with no body, or silently reverting to primitives.
                    Object.DestroyImmediate(root);
                    return;
                }
            }
            else
            {
                BuildPrimitiveBody(shape, lungeRoot.transform, bodyMat,
                                   out body, out eye, out weapon, out shoulder, out hand);
            }

            var alert = Prim(PrimitiveType.Cube, "Alert", visual.transform, new Vector3(0f, 2.5f, 0f), new Vector3(0.25f, 0.25f, 0.25f), Mat("M_AlertTell"));
            alert.SetActive(false);

            visuals.body = body.GetComponentInChildren<Renderer>(true);
            visuals.eye = eye.GetComponent<Renderer>();
            visuals.weapon = weapon.GetComponent<Renderer>();
            visuals.lungeRoot = lungeRoot.transform;
            visuals.armPivot = shoulder;
            visuals.weaponPivot = hand;
            visuals.alertMarker = alert;
            // Same glyph as every other enemy, built by the same code — a mini-boss whose posture breaks
            // must read identically to a grunt whose posture breaks. Rule 9: written here, not defaulted.
            //
            // The height and the stand-off are per body, though, because the glyph is now mounted on the
            // TORSO. A hovering robed wraith and a squat wide-armed robot have their sternums at
            // different heights and different depths from a 0.45 m capsule, and a mark left on the centre
            // line renders INSIDE the mesh — invisible, with every assertion still passing.
            float markHeight = spec != null ? spec.markHeight : 1.45f;
            float markOffset = spec != null ? MeasureSurfaceOffset(body) : 0.80f;
            visuals.deathblowMarker = PrefabFactory.BuildDeathblowMarker(visual.transform, markHeight, markOffset);
            flash.renderers = new[] { visuals.body, visuals.weapon };

            if (animated) WireAnimatedBody((PuppetVisuals)visuals, spec, modelRoot, name);

            // All seven EnemyVisuals bindings must be live. A null one is silent at build time and only
            // shows up as a missing telegraph mid-fight, which is the worst possible place to find it.
            if (visuals.body == null || visuals.eye == null || visuals.weapon == null ||
                visuals.lungeRoot == null || visuals.armPivot == null ||
                visuals.weaponPivot == null || visuals.alertMarker == null ||
                visuals.deathblowMarker == null)
            {
                Debug.LogError("[MiniBossFactory] " + name + " has a NULL EnemyVisuals binding — " +
                    "body=" + (visuals.body != null) + " eye=" + (visuals.eye != null) +
                    " weapon=" + (visuals.weapon != null) + " lungeRoot=" + (visuals.lungeRoot != null) +
                    " armPivot=" + (visuals.armPivot != null) + " weaponPivot=" + (visuals.weaponPivot != null) +
                    " alert=" + (visuals.alertMarker != null) + " deathblow=" + (visuals.deathblowMarker != null));
            }

            // Posture bar, as on Grunt/Heavy. The HUD boss bar belongs to the Warden alone — these three
            // are read from the world-space bar, which is also the "execute me now" pulse on stagger.
            BuildPostureBar(visual.transform);

            Save(root, PrefabDir + "/" + name + ".prefab");
        }

        /// <summary>
        /// How far in front of the centre line the deathblow glyph must stand to sit ON this model's
        /// chest rather than inside it. Measured from the imported mesh's own DEPTH — the enemy is
        /// always turned to face the player during a deathblow (<c>BeginExecuted</c> does it), so the
        /// direction the glyph is pushed is the model's facing axis, not its width. Measuring rather
        /// than guessing is the point: the whole trap here is that a wrong value fails silently.
        /// </summary>
        static float MeasureSurfaceOffset(GameObject body)
        {
            var r = body != null ? body.GetComponentInChildren<Renderer>(true) : null;
            if (r == null) return 0.80f;
            // Tight: every centimetre of stand-off is a centimetre the spot sits NEARER the camera than
            // the body it marks, and that gap is what made the earlier fixed-size version balloon.
            float depth = r.bounds.extents.z;
            return Mathf.Clamp(depth + 0.13f, 0.40f, 0.85f);
        }

        // ------------------------------------------------------------------ imported bodies

        /// <summary>
        /// Where an imported model's landmarks are, in the FBX's own metre space. enemy-forge exports
        /// pre-normalised: feet at y = 0, crown at y = 2, facing +Z. <b>Every one of those was verified
        /// per model, not assumed</b> — see the log entry in docs/ENGINEERING-LOG.md.
        /// </summary>
        class ModelSpec
        {
            public string fbx;              // file name under ModelDir
            public float yLift;             // hover. 0 = the model's own feet sit on the collider base
            public float yaw;               // correction when the model does not face +Z
            public Vector3 eyePos;          // the glowing slot / furnace grate: EnemyVisuals.eye
            public Vector3 eyeSize;
            public bool eyeRound;           // a disc (the furnace port) rather than a slot
            public Vector3 armPos;          // shoulder the telegraph swings from
            public Vector3 handPos;         // local to armPos
            public Vector3 weaponFxPos;     // local to handPos: where the cue sparks are thrown from
            public float markHeight;        // sternum: where the deathblow glyph rides on THIS body
            public string note;

            // ---- animated models only (see WireAnimatedBody) ------------------------------------
            /// <summary>True when the FBX ships a rigged skeleton and a <c>.clips.json</c> manifest.</summary>
            public bool animated;
            /// <summary>Clip played for an ordinary attack; its contact frame is scaled onto the data's impact.</summary>
            public string attackClip;
            /// <summary>Clip played for a heavy / unblockable.</summary>
            public string heavyClip;
            /// <summary>Clip played for a SPIN PASS — picked for its POSE, not its swing. See the Marionette spec.</summary>
            public string spinClip;
            /// <summary>Resting loop, and the controller's default state.</summary>
            public string idleClip;
            /// <summary>Attacks whose asset name starts with this drive the whirl. Empty = never whirl.</summary>
            public string spinPrefix;
        }

        static ModelSpec ModelFor(Silhouette shape)
        {
            switch (shape)
            {
                case Silhouette.Spellsword:
                    return new ModelSpec
                    {
                        fbx = "AshenChorister.fbx",
                        // HOVERS. It has no legs — a tentacle skirt — so the tips are lifted just clear of
                        // the floor and it reads as gliding. Deliberately done on the VISUAL, not on
                        // NavMeshAgent.baseOffset: the agent, the capsule and the distance/cone impact
                        // test all stay exactly where they were (model-swap contract rule 3).
                        yLift = 0.10f,
                        yaw = 0f,
                        eyePos = new Vector3(0f, 1.58f, 0.235f),
                        eyeSize = new Vector3(0.26f, 0.14f, 0.10f),
                        armPos = new Vector3(0.30f, 1.52f, 0f),
                        handPos = new Vector3(0f, -0.18f, 0f),
                        weaponFxPos = new Vector3(0.72f, 0.06f, 0.02f),   // the scythe blade
                        // Sternum, well under the hood recess (1.58) so the violet glyph never sits on
                        // top of the one always-on emissive this body has.
                        markHeight = 1.28f,
                        note = "hooded scythe wraith; glowing eye slot in the hood recess"
                    };

                case Silhouette.Knight:
                    return new ModelSpec
                    {
                        fbx = "IronPenitent.fbx",
                        // STANDS. It has real feet, so no lift at all — feet on the collider base.
                        yLift = 0f,
                        yaw = 0f,
                        // The belly furnace grate. Bound as EnemyVisuals.eye on purpose: that is the ONE
                        // sanctioned always-on emissive on an enemy, it is already ember-orange, and it is
                        // already driven by Posture.Ratio — so the grate flares as his posture fills and
                        // the model's signature feature doubles as his posture read. No new emissive, no
                        // new material, and it peaks well under M_AlertTell (3.0), which stays the only
                        // thing on him allowed to shout.
                        eyePos = new Vector3(0f, 1.10f, 0.30f),
                        eyeSize = new Vector3(0.32f, 0.30f, 0.10f),
                        eyeRound = true,        // the port is a round grate; a squared-off slot read as a decal
                        armPos = new Vector3(0.35f, 1.12f, 0f),
                        handPos = new Vector3(0f, -0.04f, 0f),
                        weaponFxPos = new Vector3(0.66f, 0f, 0f),         // the outstretched fist
                        // ABOVE the belly furnace grate (1.10), not on it. He is squat, so his chest
                        // plate is low; 1.38 is the top of it and still clear of the grate's ember glow.
                        markHeight = 1.38f,
                        note = "furnace-bellied iron penitent; arms held wide, which is the spin silhouette"
                    };

                case Silhouette.Marionette:
                    return new ModelSpec
                    {
                        fbx = "PaleMarionette.fbx",
                        // STANDS on thin legs; feet on the collider base like the Penitent.
                        yLift = 0f,
                        // Faces +Z, verified the same way the other two were: the mesh is 1.75 m wide in
                        // X and only 0.51 m deep in Z, so the arm chain runs along X and the figure looks
                        // down Z. That flatness is also why the WHIRL reads — the silhouette pulses from
                        // a 1.75 m span to a 0.51 m edge four times a second.
                        yaw = 0f,
                        // The lantern under the hat brim. Bound as EnemyVisuals.eye, so it rides
                        // Posture.Ratio exactly like the Chorister's slot and the Penitent's grate: it
                        // burns hotter as the break approaches, and at 4.5 rev/s it strobes past you
                        // once per pass, which is literally the thing the player is timing off.
                        eyePos = new Vector3(0f, 1.66f, 0.24f),
                        eyeSize = new Vector3(0.20f, 0.12f, 0.10f),
                        // Shoulder height, out along the long arm. The pivots are empty transforms as
                        // on every other model — the ANIMATOR moves this body now, and driving these on
                        // top of a playing clip would fight it.
                        armPos = new Vector3(0.52f, 1.48f, 0f),
                        handPos = new Vector3(0f, -0.10f, 0f),
                        weaponFxPos = new Vector3(0.33f, 0f, 0f),   // the long hand: where the cue sparks throw from
                        // Chest, clear of the lantern at 1.66. It is a narrow body, so the glyph sits
                        // high enough to be off the thin legs and low enough not to touch the hat.
                        markHeight = 1.30f,
                        note = "lanky wide-brimmed puppet; 1.75 m arm span on a 0.51 m deep body — the whirl silhouette",

                        animated = true,
                        attackClip = "AttackSwing",
                        heavyClip = "AttackOverhead",
                        // CHOSEN BY MEASUREMENT, not by name. Sampling every clip's LeftHand/RightHand
                        // separation at 25/50/75% gave: AttackSwing 0.38/0.76/1.02 m (arms tucked
                        // against the body — a whirl of it reads as a turning stick), AttackOverhead
                        // 0.11/1.55/1.16, IdleCombat 1.24 flat, and Roar 2.04/1.95/2.03 with the hands
                        // at 1.37 m. Roar is the only clip that HOLDS the arms out for its whole
                        // length, which is the entire silhouette this fight is built on. It is played
                        // as a pose, not as a roar.
                        spinClip = "Roar",
                        idleClip = "IdleCombat",
                        spinPrefix = "Marionette_Spin"
                    };
            }
            return null;   // Ninja keeps the primitive silhouette
        }

        /// <summary>
        /// Everything an ANIMATED model needs on top of the shared rig: an <see cref="Animator"/>
        /// pointed at a generated controller, and the clip-timing constants
        /// <see cref="PuppetVisuals"/> uses to bend playback onto the data's clock.
        ///
        /// <para><b>The contact time is read from the forge manifest HERE, at build time.</b> Nothing at
        /// runtime opens the JSON. That keeps the shipped prefab self-contained and — more importantly —
        /// means the value is visible in the Inspector, which is the difference between a timing you can
        /// check and one you have to trust.</para>
        /// </summary>
        static void WireAnimatedBody(PuppetVisuals pv, ModelSpec spec, Transform modelRoot, string name)
        {
            string fbx = ModelDir + "/" + spec.fbx;

            var animator = modelRoot != null ? modelRoot.GetComponent<Animator>() : null;
            if (animator == null && modelRoot != null) animator = modelRoot.gameObject.AddComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError("[MiniBossFactory] " + name + " is flagged animated but has no model root to " +
                               "put an Animator on. The prefab will stand in its bind pose.");
                return;
            }

            var controller = PuppetAnimatorFactory.Build(fbx, name + "_Animator", spec.idleClip);
            if (controller == null)
            {
                Debug.LogError("[MiniBossFactory] " + name + ": no AnimatorController was produced from " +
                               fbx + ". Run VibeGame1/4a. Split Forge Animation Clips, then rebuild.");
                return;
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;          // NavMeshLocomotion owns the transform, not the clip
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // Default (Normal) update mode on purpose: the Animator runs on SCALED time, so the puppet
            // freezes during hitstop with everything else. Rule 1 reserves PlayerDelta for things the
            // player drives; an enemy dancing through a freeze frame would kill the impact read.
            animator.updateMode = AnimatorUpdateMode.Normal;

            pv.animator = animator;

            // ---- a DEDICATED transform for the whirl -------------------------------------------
            // The whirl gets its own empty parent, inserted between LungeRoot and the model, and NOT
            // the model root itself. The generic clips were imported with keepOriginalOrientation, so
            // the take's root curves are still in them and the Animator writes the model's own local
            // rotation every frame. Writing the whirl there too would be two writers on one channel —
            // the spin would stutter or vanish depending on evaluation order, with nothing in the
            // console. Three transforms, three owners: LungeRoot = base class lean/lunge,
            // SpinRoot = the whirl, Model = the Animator.
            var spin = new GameObject("SpinRoot");
            spin.transform.SetParent(modelRoot.parent, false);
            spin.transform.localPosition = Vector3.zero;
            spin.transform.localRotation = Quaternion.identity;
            modelRoot.SetParent(spin.transform, false);
            pv.spinRoot = spin.transform;

            pv.clipIdle = spec.idleClip;
            pv.clipAttack = spec.attackClip;
            pv.clipHeavy = spec.heavyClip;
            pv.clipSpin = string.IsNullOrEmpty(spec.spinClip) ? spec.attackClip : spec.spinClip;
            pv.spinAttackPrefix = spec.spinPrefix;

            // ---- the whirl's SHIPPED values, hard rule 9 ----------------------------------------
            // These have C# field initialisers on PuppetVisuals and were previously left to them,
            // which meant the numbers on the prefab were whatever the initialiser happened to be on
            // the day the prefab was last built. Editing the initialiser afterwards changed nothing
            // and said nothing — the exact trap rule 9 exists for. Written here, they are rebuilt
            // with the prefab and MarionetteDataTests reads them back off the asset.
            //
            // peak 4.5 / tail 0.25 on a 0.49 s pass is ~3300 deg/s at the fastest instant (about NINE
            // revolutions a second) decaying to ~184 deg/s as it arrives. The pair is what makes the
            // spin fast and the arrival readable at the same time; raising peak alone would just move
            // the blur later, not make the fight harder.
            pv.spinPeakMultiple = 4.5f;
            pv.spinTailMultiple = 0.25f;
            // 75 deg/frame: the alias guard. Never binds above ~55 fps at these values; below that it
            // trades blur for legibility rather than letting the body strobe. See PuppetVisuals.
            pv.maxDegPerFrame = 75f;

            pv.attackClipLength = PuppetAnimatorFactory.ClipLength(fbx, spec.attackClip, 1f);
            pv.attackHitNormalized = ForgeClipSplitter.ReadHitNormalizedTime(fbx, spec.attackClip, 0.55f);
            pv.spinClipLength = PuppetAnimatorFactory.ClipLength(fbx, pv.clipSpin, 1f);
            pv.spinHitNormalized = ForgeClipSplitter.ReadHitNormalizedTime(fbx, pv.clipSpin, 0.4f);

            // Validate the clips the component names actually exist. A missing clip is SILENT at
            // runtime — CrossFade to a state that is not there simply does nothing and the puppet
            // keeps playing whatever it was playing, which reads as "the animation is broken" with
            // nothing in the console.
            var have = PuppetAnimatorFactory.ClipsIn(fbx);
            var names = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < have.Count; i++) names.Add(have[i].name);
            foreach (var wanted in new[] { pv.clipIdle, pv.clipWalk, pv.clipRun, pv.clipAttack,
                                           pv.clipHeavy, pv.clipSpin, pv.clipHit, pv.clipStagger,
                                           pv.clipDeath, pv.clipRoar })
            {
                if (!names.Contains(wanted))
                    Debug.LogError("[MiniBossFactory] " + name + " names clip '" + wanted + "' but " + fbx +
                                   " has no such clip. It has: " + string.Join(", ", names) +
                                   ". A missing clip fails SILENTLY at runtime.");
            }

            Debug.Log("[MiniBossFactory] " + name + " animated: " + have.Count + " clips; attack '" +
                      pv.clipAttack + "' len " + pv.attackClipLength.ToString("F3") + "s anchor " +
                      pv.attackHitNormalized.ToString("F2") + " (" +
                      (pv.attackClipLength * pv.attackHitNormalized).ToString("F3") + "s in); spin '" +
                      pv.clipSpin + "' len " + pv.spinClipLength.ToString("F3") + "s anchor " +
                      pv.spinHitNormalized.ToString("F2") + " (" +
                      (pv.spinClipLength * pv.spinHitNormalized).ToString("F3") + "s in).");
        }

        /// <summary>
        /// Parents the imported mesh under <paramref name="lungeRoot"/> and rebinds every
        /// <see cref="EnemyVisuals"/> part to it. Returns false (having logged) when the art is missing —
        /// the caller must then abandon the prefab rather than fall back.
        /// </summary>
        static bool BuildModelBody(ModelSpec spec, Transform lungeRoot, Material bodyMat,
                                   out GameObject body, out GameObject eye, out GameObject weapon,
                                   out Transform shoulder, out Transform hand, out Transform modelRoot)
        {
            body = null; eye = null; weapon = null; shoulder = null; hand = null; modelRoot = null;

            string path = ModelDir + "/" + spec.fbx;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                Debug.LogError("[MiniBossFactory] MISSING SOURCE ART: " + path +
                    ". This mini-boss has an imported body and there is deliberately NO primitive " +
                    "fallback — a boxy stand-in would look like a bug rather than a missing file. " +
                    "Restore the FBX from git (it is a committed asset) or re-export it from enemy-forge " +
                    "into a path containing /Enemies/ so EnemyForgeImporter configures it. " +
                    "See docs/AUTHORING.md → Importing a forge model.");
                return false;
            }

            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            // Unpacked so the built prefab is plain GameObjects: this factory REGENERATES the prefab
            // every run, and a nested model-prefab instance would turn every rebind below into a stored
            // prefab override instead of a plain value.
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(lungeRoot, false);
            model.transform.localPosition = new Vector3(0f, spec.yLift, 0f);
            model.transform.localRotation = Quaternion.Euler(0f, spec.yaw, 0f);
            model.transform.localScale = Vector3.one;
            modelRoot = model.transform;

            var skin = model.GetComponentInChildren<Renderer>(true);
            if (skin == null)
            {
                Debug.LogError("[MiniBossFactory] " + path + " imported with no Renderer. Check the model importer.");
                Object.DestroyImmediate(model);
                return false;
            }
            // URP or magenta. The FBX carries vertex colours and no texture, and EnemyVisuals overwrites
            // _BaseColor from EnemyData every frame anyway, so the shared enemy body material is exactly
            // right here: same shader, same palette, no new material and no MaterialFactory edit.
            skin.sharedMaterial = bodyMat;
            body = skin.gameObject;

            // The glowing slot / grate. A separate renderer because EnemyVisuals.SetPostureRatio writes
            // _EmissionColor to `eye` alone — it must not be the body renderer, or the whole enemy lights.
            eye = Prim(spec.eyeRound ? PrimitiveType.Sphere : PrimitiveType.Cube,
                       "Eye", model.transform, spec.eyePos, spec.eyeSize, Mat("M_EnemyEye"));

            // ---- arm rig ------------------------------------------------------------------------
            // Empty pivots, NOT the FBX's own bones. The forge auto-rig drops a generic humanoid
            // skeleton inside the silhouette — the Chorister's arm bones hang inside the robe while the
            // art holds a scythe overhead — so driving those bones at the ±136 deg telegraph poses tears
            // the mesh. The wind-up therefore reads through LungeRoot's whole-body lean plus the base
            // colour sinking and snapping, which is the same read the primitives give.
            var arm = Empty("ArmPivot", model.transform, spec.armPos);
            shoulder = arm.transform;
            var wp = Empty("WeaponPivot", arm.transform, spec.handPos);
            hand = wp.transform;

            // EnemyVisuals.weapon must be non-null: WeaponPoint() reads its bounds to place the cue
            // spark and the parry flare. The blade is part of the single imported mesh, so this is a
            // 3 cm marker at the blade/fist instead — it swings with the pivot, throws the sparks from
            // the right place, and is far too small to see on a 3 m enemy.
            weapon = Prim(PrimitiveType.Cube, "WeaponFx", wp.transform, spec.weaponFxPos,
                          new Vector3(0.03f, 0.03f, 0.03f), bodyMat);
            var wr = weapon.GetComponent<Renderer>();
            wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wr.receiveShadows = false;

            return true;
        }

        /// <summary>The original primitive silhouettes. Still the path for the Ninja, and still the
        /// reference for what a mini-boss body has to provide.</summary>
        static void BuildPrimitiveBody(Silhouette shape, Transform lungeRoot, Material bodyMat,
                                       out GameObject body, out GameObject eye, out GameObject weapon,
                                       out Transform shoulder, out Transform hand)
        {
            // Body proportions carry most of the read at a glance: thin/fast, huge/slow, tall/ornate.
            Vector3 bodyScale;
            switch (shape)
            {
                case Silhouette.Ninja: bodyScale = new Vector3(0.68f, 0.98f, 0.68f); break;
                case Silhouette.Knight: bodyScale = new Vector3(1.25f, 1.02f, 1.25f); break;
                default: bodyScale = new Vector3(0.85f, 1.08f, 0.85f); break;
            }
            body = Prim(PrimitiveType.Capsule, "Body", lungeRoot, new Vector3(0f, 1f, 0f), bodyScale, bodyMat);
            eye = Prim(PrimitiveType.Cube, "Eye", lungeRoot, new Vector3(0f, 1.55f, 0.4f), new Vector3(0.3f, 0.12f, 0.15f), Mat("M_EnemyEye"));

            // ---- arm rig: shoulder -> upper arm -> forearm/hand -> weapon --------------------------
            // Same names and hierarchy as PrefabFactory.BuildEnemy: EnemyVisuals rotates the shoulder to
            // sell the wind-up, and the hand trails it. Do not rename these.
            var sh = Empty("ArmPivot", lungeRoot, new Vector3(0.5f, 1.45f, 0f));
            shoulder = sh.transform;
            Prim(PrimitiveType.Cube, "UpperArm", sh.transform, new Vector3(0f, -0.28f, 0f), new Vector3(0.17f, 0.56f, 0.17f), bodyMat);

            var hd = Empty("WeaponPivot", sh.transform, new Vector3(0f, -0.56f, 0f));
            hand = hd.transform;
            Prim(PrimitiveType.Cube, "ForeArm", hd.transform, new Vector3(0f, -0.22f, 0f), new Vector3(0.14f, 0.46f, 0.14f), bodyMat);

            switch (shape)
            {
                case Silhouette.Ninja:
                    // short, straight, held low — a blade you can throw five of in the time of one cleave
                    weapon = Prim(PrimitiveType.Cube, "Weapon", hd.transform, new Vector3(0f, -0.4f, 0.34f), new Vector3(0.08f, 0.1f, 1.05f), bodyMat);
                    weapon.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
                    // off-hand tanto: the second blade is the whole point of the archetype
                    var offhand = Empty("OffhandPivot", lungeRoot, new Vector3(-0.42f, 1.1f, 0.12f));
                    var tanto = Prim(PrimitiveType.Cube, "OffhandBlade", offhand.transform, new Vector3(0f, 0f, 0.3f), new Vector3(0.07f, 0.09f, 0.7f), bodyMat);
                    tanto.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
                    break;

                case Silhouette.Knight:
                    // a slab. Long enough that the overhead's arc is visible from 4 m.
                    weapon = Prim(PrimitiveType.Cube, "Weapon", hd.transform, new Vector3(0f, -0.62f, 0.55f), new Vector3(0.26f, 0.2f, 1.95f), bodyMat);
                    weapon.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
                    Prim(PrimitiveType.Cube, "PauldronR", lungeRoot, new Vector3(0.62f, 1.62f, 0f), new Vector3(0.45f, 0.28f, 0.5f), bodyMat);
                    Prim(PrimitiveType.Cube, "PauldronL", lungeRoot, new Vector3(-0.62f, 1.62f, 0f), new Vector3(0.45f, 0.28f, 0.5f), bodyMat);
                    Prim(PrimitiveType.Cube, "Shield", lungeRoot, new Vector3(-0.62f, 1.08f, 0.28f), new Vector3(0.16f, 0.95f, 0.7f), bodyMat);
                    break;

                default:
                    // long ceremonial blade plus a floating focus: the hybrid read, melee AND caster
                    weapon = Prim(PrimitiveType.Cube, "Weapon", hd.transform, new Vector3(0f, -0.52f, 0.5f), new Vector3(0.13f, 0.15f, 1.7f), bodyMat);
                    weapon.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
                    var focus = Empty("FocusPivot", lungeRoot, new Vector3(-0.55f, 1.5f, 0.25f));
                    Prim(PrimitiveType.Sphere, "Focus", focus.transform, Vector3.zero, new Vector3(0.26f, 0.26f, 0.26f), Mat("M_NeonPink"));
                    Prim(PrimitiveType.Cube, "Mantle", lungeRoot, new Vector3(0f, 1.72f, -0.1f), new Vector3(0.95f, 0.22f, 0.6f), bodyMat);
                    break;
            }
        }

        /// <summary>Two quads above the head: dark backing plus a left-anchored fill pivot scaled 0..1.
        /// Mirrors <c>PrefabFactory.BuildPostureBar</c>; <see cref="EnemyPostureBar"/> drives the pivot's
        /// X scale, never <c>Image.fillAmount</c>.</summary>
        static void BuildPostureBar(Transform parent)
        {
            const float width = 1.1f;
            const float height = 0.13f;

            var bar = Empty("PostureBar", parent, new Vector3(0f, 2.6f, 0f));
            var comp = bar.AddComponent<EnemyPostureBar>();

            var bg = Prim(PrimitiveType.Quad, "BarBG", bar.transform, Vector3.zero, new Vector3(width, height, 1f), Mat("M_Ground"));

            var fillPivot = Empty("FillPivot", bar.transform, new Vector3(-width * 0.5f, 0f, 0f));
            var fill = Prim(PrimitiveType.Quad, "BarFill", fillPivot.transform,
                new Vector3(width * 0.5f, 0f, -0.01f), new Vector3(width, height * 0.62f, 1f), Mat("M_NeonYellow"));

            comp.billboard = bar.transform;
            comp.fillPivot = fillPivot.transform;
            comp.fillRenderer = fill.GetComponent<Renderer>();
            comp.backgroundRenderer = bg.GetComponent<Renderer>();
            comp.headHeight = 2f;
            comp.heightAboveHead = 0.6f;
        }

        // ------------------------------------------------------------------ helpers
        // Copies of the PrefabFactory privates. Duplicated rather than made public on purpose: this file
        // must stay buildable while PrefabFactory is being edited independently.

        static Material Mat(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/" + name + ".mat");
            if (m == null) Debug.LogWarning("[MiniBossFactory] Material not found: " + MaterialDir + "/" + name + ".mat (run VibeGame1/2. Create Materials first)");
            return m;
        }

        static T Load<T>(string path) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static GameObject Empty(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }

        static void Save(GameObject temp, string path)
        {
            bool ok;
            PrefabUtility.SaveAsPrefabAsset(temp, path, out ok);
            if (!ok) Debug.LogError("[MiniBossFactory] Failed to save prefab " + path);
            else Debug.Log("[MiniBossFactory] Saved " + path);
            Object.DestroyImmediate(temp);
        }
    }
}
