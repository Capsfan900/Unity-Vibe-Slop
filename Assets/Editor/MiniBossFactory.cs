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
        const string EnemyDataDir = EnemyPaths.Souls;   // every legendary is a souls_enemy

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
            // PROTOTYPE. Sandbox pad only. The ai_skelly_tool test body, and the first BURNING enemy.
            BuildMiniBoss("Legendary_Revenant", EnemyDataDir + "/Legendary_Revenant.asset", Silhouette.Revenant);
            // PROTOTYPE. Sandbox pad only. The first body whose attacks are GENERATED per-character clips
            // (forge.py --motion), named on the attack data, with the art's own travel held in
            // lungeDistance. See docs/ARCHITECTURE.md -> The Argent Halberdier.
            BuildMiniBoss("Legendary_Halberdier", EnemyDataDir + "/Legendary_Halberdier.asset", Silhouette.Halberdier);
            // SHOWCASE (2026-09-06). Sandbox pad only, second row. The Knight silhouette in slate and cold
            // blue; every soulslike combat feature of the day on one body. See DataFactory, THE DRILLMASTER.
            BuildMiniBoss("Legendary_Drillmaster", EnemyDataDir + "/Legendary_Drillmaster.asset", Silhouette.Knight);
            // PROTOTYPE. Sandbox pad only (x -22, z -26). The roster's first FLURRY enemy: unarmed,
            // twelve attacks on twelve generated clips, with two-, four- and eight-hit strings on a
            // 0.73 s beat. See
            // DataFactory, THE FLURRY BRAWLER.
            BuildMiniBoss("Legendary_FlurryBrawler", EnemyDataDir + "/Legendary_FlurryBrawler.asset", Silhouette.FlurryBrawler);
            // ADDITIVE TEST BODY. V18 stays sandbox-only and never replaces the v15 prefab above.
            BuildMiniBoss(FlurryBrawlerV18Authoring.EnemyName,
                EnemyDataDir + "/" + FlurryBrawlerV18Authoring.EnemyName + ".asset",
                Silhouette.FlurryBrawlerV18);
            // ADDITIVE SANDBOX ELITE (2026-09-13). The Cinder Judge: V18's method on a new forge body,
            // plus the Storm Judgement ticking zone. Movement park only. See DataFactory, THE CINDER JUDGE.
            BuildMiniBoss(CinderJudgeAuthoring.EnemyName,
                EnemyDataDir + "/" + CinderJudgeAuthoring.EnemyName + ".asset",
                Silhouette.CinderJudge);
            // ADDITIVE SANDBOX ELITE (2026-09-13). The Orbit Dancer: V18's method on the orbit_dancer_v1
            // forge body, plus ORBIT STORM, ricochet discs. Movement park for now; the boss-roster plan's
            // Stage 5 places her as the T3 realm boss. See DataFactory, THE ORBIT DANCER.
            BuildMiniBoss(OrbitDancerAuthoring.EnemyName,
                EnemyDataDir + "/" + OrbitDancerAuthoring.EnemyName + ".asset",
                Silhouette.OrbitDancer);
            // ADDITIVE SANDBOX ELITE (2026-09-13). The Seraph Lancer: V18's method on the seraph_lancer_v1
            // forge body, plus SKY VERDICT, hover javelins. Movement park for now; the boss-roster plan's
            // Stage 5 places him as the T1 realm boss. See DataFactory, THE SERAPH LANCER.
            BuildMiniBoss(SeraphLancerAuthoring.EnemyName,
                EnemyDataDir + "/" + SeraphLancerAuthoring.EnemyName + ".asset",
                Silhouette.SeraphLancer);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // A controller PuppetAnimatorFactory deleted and recreated resolves to NULL on the freshly
            // built prefab for the rest of this editor session (ENGINEERING-LOG, "A rebuilt animator
            // controller reads NULL on the prefab in the same session") until both are force-imported.
            // Done here so a test run straight after 4b reads the real reference.
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { PuppetAnimatorFactory.ControllerDir }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            foreach (var guid in AssetDatabase.FindAssets("Legendary_ t:Prefab", new[] { PrefabDir }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            Debug.Log("[MiniBossFactory] Built 12 legendary mini-boss prefabs under " + PrefabDir);
        }

        enum Silhouette { Ninja, Knight, Spellsword, Marionette, Revenant, Halberdier, FlurryBrawler, FlurryBrawlerV18, CinderJudge, OrbitDancer, SeraphLancer }

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
            EnemyVisuals visuals;
            if (shape == Silhouette.FlurryBrawlerV18)
                visuals = visual.AddComponent<FlurryBrawlerV18Visuals>();
            else if (shape == Silhouette.CinderJudge)
                visuals = visual.AddComponent<CinderJudgeVisuals>();
            else if (shape == Silhouette.OrbitDancer)
                visuals = visual.AddComponent<OrbitDancerVisuals>();
            else if (shape == Silhouette.SeraphLancer)
                visuals = visual.AddComponent<SeraphLancerVisuals>();
            else if (animated)
                visuals = visual.AddComponent<PuppetVisuals>();
            else
                visuals = visual.AddComponent<EnemyVisuals>();
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

            if (animated) WireAnimatedBody((PuppetVisuals)visuals, spec, modelRoot, name, data);

            if (visuals is FlurryBrawlerV18Visuals v18)
            {
                // Rule 9: every presentation-profile value is rebuilt onto the prefab.
                v18.shoulderChargeAttack = "BrawlerV18_ShoulderCharge";
                v18.shoulderChargeClip = "ShoulderCharge";
                v18.dashAttack = "BrawlerV18_Dash";
                v18.clapAttack = "BrawlerV18_LevitateClap";
                v18.clapClip = "Clap";
                v18.comboAttack = "BrawlerV18_Combo2";
                v18.comboClip = "Combo2";
                v18.jumpClip = "Jump";
                v18.blockClip = "Block";
                v18.entranceProbeDelay = 0.12f;
                v18.entranceHoldSeconds = 0.95f;
                v18.hitHoldSeconds = 0.50f;
                v18.clapRingRadius = 3.8f;
                v18.clapRingSeconds = 0.34f;
                v18.clapSparkCount = 14;
                v18.clapSparkSpeed = 7f;
                v18.clapSparkSpread = 120f;

                // ---- SKYFALL SUPLEX: GrabRoot above SpinRoot + V18Grapple on the root (2026-09-13) ------
                // The Judge's StormRoot pattern: the grab's lift has its own transform and one writer.
                Transform grabRoot = null;
                if (v18.spinRoot != null)
                {
                    var gr = new GameObject("GrabRoot");
                    gr.transform.SetParent(v18.spinRoot.parent, false);
                    gr.transform.localPosition = Vector3.zero;
                    gr.transform.localRotation = Quaternion.identity;
                    v18.spinRoot.SetParent(gr.transform, false);
                    grabRoot = gr.transform;
                }
                else Debug.LogError("[MiniBossFactory] " + name + " has no SpinRoot to insert GrabRoot above.");
                var grapple = root.AddComponent<V18Grapple>();
                grapple.grabAttack = "BrawlerV18_Grab";
                grapple.slamAttack = AssetDatabase.LoadAssetAtPath<EnemyAttackData>("Assets/Data/Attacks/BrawlerV18_GrabSlam.asset");
                grapple.grabRoot = grabRoot;
                grapple.maxGrabDistance = 3.2f;
                // 11 m lift: under a 24 m realm ceiling with the player hanging below the hands.
                grapple.liftHeight = 11f;
                grapple.liftSeconds = 0.9f;
                grapple.holdSeconds = 0.35f;
                grapple.descendSeconds = 0.5f;
                grapple.throwDownSpeed = 14f;
                grapple.throwOutDistance = 6f;
                grapple.holdLocal = new Vector3(0f, -0.1f, 1.15f);
                grapple.slamTimeout = 3f;
            }

            if (visuals is CinderJudgeVisuals cj)
            {
                // ---- StormRoot: the ONE transform the storm writes ------------------------------
                // Inserted between LungeRoot (the base lean/lunge) and SpinRoot (PuppetVisuals' idle
                // wobble) so the lift and the tornado yaw never share a channel with either. Four
                // transforms, four owners: LungeRoot = EnemyVisuals, StormRoot = CinderJudgeVisuals,
                // SpinRoot = PuppetVisuals, TravelRoot = CompensateTravel.
                if (cj.spinRoot != null)
                {
                    var stormRoot = new GameObject("StormRoot");
                    stormRoot.transform.SetParent(cj.spinRoot.parent, false);
                    stormRoot.transform.localPosition = Vector3.zero;
                    stormRoot.transform.localRotation = Quaternion.identity;
                    cj.spinRoot.SetParent(stormRoot.transform, false);
                    cj.stormRoot = stormRoot.transform;
                }
                else
                {
                    Debug.LogError("[MiniBossFactory] " + name + " has no SpinRoot to insert StormRoot above; " +
                                   "the storm will not lift the body.");
                }

                // Rule 9: every presentation-profile value is rebuilt onto the prefab.
                cj.stormAttack = CinderJudgeAuthoring.StormAttackName;
                cj.shoulderChargeAttack = "CinderJudge_ShoulderCharge";
                cj.shoulderChargeClip = "ShoulderCharge";
                cj.roarClip = "Roar";
                cj.jumpClip = "Jump";
                cj.entranceProbeDelay = 0.12f;
                cj.entranceHoldSeconds = 1.20f;   // Roar is 1.25 s at 1x
                cj.hitHoldSeconds = 0.50f;
                // The float: 2.4 m is the middle of the brief's "2-3 m" -- above a standing player's eye
                // (so the spinning body is SEEN against the sky, not the floor) and low enough that the
                // capsule on the ground still reads as his. The descent is the strike's last 0.35 s.
                cj.stormFloatHeight = 2.4f;
                cj.stormDescendSeconds = 0.35f;
                // 540 deg/s = 1.5 rev/s: a tornado, not the Marionette's 5.8 rev/s blur. 9 deg a frame
                // at 60 fps, so it never aliases and the silhouette still reads as a body turning.
                cj.stormSpinDegPerSec = 540f;
                cj.stormFallSeconds = 0.22f;
                // From the manifest's Jump events: OnJumpTakeoff 0.28, OnJumpLand 0.85; the apex is the
                // midpoint of the airborne window.
                cj.jumpTakeoffNormalized = 0.28f;
                cj.jumpApexNormalized = 0.56f;
                cj.jumpLandNormalized = 0.85f;
                cj.landingHoldSeconds = 0.40f;
                cj.chargeSparkInterval = 0.12f;
                // >= LightningEffect.BundleSeconds (0.34): never two bundles alive, so the storm costs at
                // most two point lights at any instant.
                cj.stormArcInterval = 0.36f;
                cj.stormArcScale = 0.75f;
                cj.stormStrands = 4;
                cj.stormCoreIntensity = 1.4f;    // blooms modestly; parry glow 3.2 and alert tell 3.0 stay louder
                cj.stormCrackleSeconds = 0.05f;
                cj.stormJitterMetres = 0.22f;
                // The visor's yellow, not the seams' orange: the storm is a different substance from the
                // body's fire, and yellow is nowhere else in his read (the cue is red, the parry pale steel).
                cj.stormHue = new Color(1f, 0.82f, 0.29f, 1f);
                cj.landingRingSeconds = 0.34f;
                cj.landingSparkCount = 14;
                cj.landingSparkSpeed = 7f;
                cj.landingSparkSpread = 120f;
                // Ember seams: the Revenant's aura numbers (EmberAura precedent), written on THIS
                // component because it is the aura's only writer here. Rest a touch dimmer than the
                // Revenant (0.18 vs 0.22) because the albedo already paints the seams orange; the storm
                // peak is the 0.60 EmberAura documents as the ceiling before the parry read suffers.
                cj.emberHot = new Color(1f, 0.45f, 0.12f, 1f) * 1.5f;
                cj.glowAtRest = 0.18f;
                cj.glowAtBreak = 0.50f;
                cj.stormGlow = 0.60f;
                cj.pulseSpeed = 1.7f;
                cj.pulseAmount = 0.14f;
                cj.stormFlickerHz = 14f;

                // ---- the ticking zone, on the ROOT beside the brain ------------------------------
                var storm = root.AddComponent<CinderJudgeStorm>();
                storm.stormAttack = CinderJudgeAuthoring.StormAttackName;
                storm.tickInterval = 0.30f;
                // 3.6 m: V18's Clap radius, the circle the player already knows -- and 1.2 m outside the
                // Judge's 2.4 m preferredRange, so fighting distance is INSIDE it when it ignites.
                storm.radius = 3.6f;
                storm.height = 4.5f;
                storm.floorSlack = 0.6f;

                // ---- AEGIS OF JUDGEMENT: the magic shield, on the ROOT beside the brain (2026-09-13) --
                cj.shieldRaiseAttack = CinderJudgeAuthoring.ShieldRaiseAttackName;
                cj.shieldRaiseClip = "ShieldRaise";
                var shield = root.AddComponent<CinderJudgeShield>();
                shield.raiseAttack = CinderJudgeAuthoring.ShieldRaiseAttackName;
                shield.bashAttack = CinderJudgeAuthoring.ShieldBashAttackName;
                shield.recoilPosture = 18f;
                shield.shatterPostureFraction = 0.35f;
                shield.brokenSeconds = 2.5f;
                shield.domeRadius = 1.25f;
                shield.domeForward = 0.7f;
                shield.domeHeight = 1.35f;
                // Additive ember at 0.55: under the 1.05 bloom threshold at rest, flashes to ~1.2 on a deflect.
                shield.domeColor = new Color(1f, 0.55f, 0.18f, 1f) * 0.55f;
            }

            if (visuals is OrbitDancerVisuals od)
            {
                // Rule 9: every presentation-profile value is rebuilt onto the prefab.
                od.discThrowAttack = OrbitDancerAuthoring.DiscThrowAttackName;
                od.spinThrowAttack = OrbitDancerAuthoring.SpinThrowAttackName;
                od.shoulderChargeAttack = "OrbitDancer_ShoulderCharge";
                od.shoulderChargeClip = "ShoulderCharge";
                od.entranceClip = "SpinThrow";
                od.entranceProbeDelay = 0.12f;
                od.entranceHoldSeconds = 0.65f;   // SpinThrow is 0.68 s at 1x
                od.hitHoldSeconds = 0.35f;        // the fluidity pass's cap on a held pose
                od.chargeSparkInterval = 0.10f;
                // RightHand measured at (0.35, 0.84, 0.18) at rest; the wind-back carries it up and out.
                od.chargeHandOffset = new Vector3(0.38f, 1.25f, 0.25f);

                // ---- OrbitRoot: the satellites, the ONE transform this class writes ----------------
                // A child of LungeRoot (so the ring rides the lean and the lunge with the body) and a
                // sibling of SpinRoot, so it never shares a channel with the whirl or TravelRoot. Three
                // small discs on a 0.85 m ring at chest height: at 24 m she is the one with moving
                // satellites, and a throw visibly spends one (VisibleSatellites).
                od.satelliteCount = 3;
                od.orbitRadius = 0.85f;
                od.orbitHeight = 1.25f;
                // 140 deg/s = one lap every 2.6 s: unmistakably moving, far under any tell's tempo
                // (ANIMATION-VFX: idle motion stays below tell amplitude).
                od.orbitDegPerSec = 140f;
                od.orbitBobMetres = 0.07f;
                od.orbitBobHz = 0.7f;
                od.satellitesFollowThrownDiscs = true;
                var orbitRoot = new GameObject("OrbitRoot");
                orbitRoot.transform.SetParent(lungeRoot.transform, false);
                orbitRoot.transform.localPosition = new Vector3(0f, od.orbitHeight, 0f);
                orbitRoot.transform.localRotation = Quaternion.identity;
                od.orbitRoot = orbitRoot.transform;
                // M_NeonCyan: an existing ice-cyan emissive at 1.0 peak -- under the 1.05 bloom threshold,
                // so the satellites locate her without ever out-shouting a cue. No new material.
                var satMat = Mat("M_NeonCyan");
                for (int i = 0; i < od.satelliteCount; i++)
                {
                    Vector3 local = OrbitDancerVisuals.SatelliteLocal(i, od.satelliteCount, 0f, od.orbitRadius, 0f, 1f, 0f);
                    var sat = Prim(PrimitiveType.Cylinder, "Satellite" + (i + 1), orbitRoot.transform, local,
                                   new Vector3(0.26f, 0.006f, 0.26f), satMat);
                    var sr = sat.GetComponent<Renderer>();
                    sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    sr.receiveShadows = false;
                }

                // Teal seams: the Judge's aura numbers in teal, written on THIS component because it is
                // the aura's only writer here. Rest 0.16 (a hair under the Judge's 0.18: the albedo's
                // seams are already teal), 0.45 at the break, 0.55 through a throw's wind-up -- under the
                // 0.60 EmberAura documents as the ceiling before the parry read suffers.
                od.tealHot = new Color(0.30f, 1.0f, 0.92f, 1f) * 1.4f;
                od.glowAtRest = 0.16f;
                od.glowAtBreak = 0.45f;
                od.throwGlow = 0.55f;
                od.pulseSpeed = 2.2f;
                od.pulseAmount = 0.12f;

                // ---- the launcher, on the ROOT beside the brain ---------------------------------
                var discs = root.AddComponent<OrbitDancerDiscs>();
                discs.discThrowAttack = OrbitDancerAuthoring.DiscThrowAttackName;
                discs.spinThrowAttack = OrbitDancerAuthoring.SpinThrowAttackName;
                // 3 a volley: one straight, one banked each side. 2 on the whirl: both banked, because
                // you are already inside her reach. Not 4-5: the global cap below would swallow the
                // second volley, and three lines from three sides is the most a first-person frame reads.
                discs.volleyCount = 3;
                discs.whirlCount = 3;   // 2026-09-14 escalation (was 2)
                // 4 alive, any thrower (the plan's number): a volley plus one straggler; a second throw
                // inside the first's 4 s life launches only what fits, so the screen never fills.
                discs.liveCap = 4;
                // 3 walls, then it shatters: enough for a corridor to skip a disc back twice, few enough
                // that a disc never becomes furniture.
                discs.maxBounces = 4;   // 2026-09-14 escalation (was 3)
                // 4 s at 16 m/s is 64 m of travel: three bounces across a 30 m realm fit, a disc that
                // found no wall is gone before the next volley (6 s cooldown).
                discs.discLifetime = 4f;
                // 900 deg/s = 2.5 rev/s = 15 deg a frame at 60 fps: visibly a spinning disc, never a strobe.
                discs.spinDegPerSec = 900f;
                // 0.60 m across: reads as a disc (not a dot) at 12 m; the logical hit radius is still
                // Projectile's 1.0 m, so the visual never advertises a bigger collision than the truth.
                discs.discDiameter = 0.60f;
                // The sentries' rule: no flight shorter than CueLead + 0.12 s.
                discs.launchMargin = 0.12f;
                discs.fanDeg = 30f;
                // 20 m: a realm floor is 18 m in radius (2026-09-14) with walls at its rim; a
                // wall the disc could not reach in 1.25 s is not a bank, it is a miss.
                discs.bankRange = 20f;
                discs.bankProbeDeg = new[] { 40f, 65f, 90f };
                discs.maxBankIncidenceY = 0.35f;
                discs.muzzleBone = "RightHand";
                discs.muzzleFallbackHeight = 1.1f;
                discs.pingVolume = 0.55f;
                discs.pingRange = 24f;
                // The rim: teal at a 1.25 peak. Over the 1.05 bloom threshold so the disc has an edge of
                // light in a dark realm; under the sentry bolt's 1.6, which stays the brightest thing.
                discs.rimColor = new Color(0.36f, 1.25f, 1.15f, 1f);
            }

            if (visuals is SeraphLancerVisuals sl)
            {
                // ---- HoverRoot: the ONE transform the lift and the tracking yaw write ---------------
                // Inserted between LungeRoot (the base lean/lunge) and SpinRoot (PuppetVisuals' idle
                // wobble), the Judge's StormRoot pattern: the verdict's lift, the dive's hop and the
                // hovering body's tracking yaw never share a channel with either. Five transforms, five
                // owners: LungeRoot = EnemyVisuals, HoverRoot + WingRoot = SeraphLancerVisuals,
                // SpinRoot = PuppetVisuals, TravelRoot = CompensateTravel.
                Transform hoverRoot = null;
                if (sl.spinRoot != null)
                {
                    var hover = new GameObject("HoverRoot");
                    hover.transform.SetParent(sl.spinRoot.parent, false);
                    hover.transform.localPosition = Vector3.zero;
                    hover.transform.localRotation = Quaternion.identity;
                    sl.spinRoot.SetParent(hover.transform, false);
                    sl.hoverRoot = hover.transform;
                    hoverRoot = hover.transform;
                }
                else
                {
                    Debug.LogError("[MiniBossFactory] " + name + " has no SpinRoot to insert HoverRoot above; " +
                                   "the verdict will not lift the body.");
                }

                // ---- WingRoot: the light-wings' anchor, a child of HoverRoot ------------------------
                // Rides the lift and the tracking yaw (so the wings turn with the throwing arm) but
                // not SpinRoot's wobble or TravelRoot's compensation. Chest bone (0, 1.40, -0.07): the
                // wings root at the shoulder blades, a hand's breadth behind the spine. The feathers
                // themselves are built at Setup (runtime meshes on a runtime additive material, as the
                // Dancer's discs are), so the prefab carries the anchor and the numbers, not the glow.
                var wingRoot = new GameObject("WingRoot");
                wingRoot.transform.SetParent(hoverRoot != null ? hoverRoot : lungeRoot.transform, false);
                wingRoot.transform.localPosition = new Vector3(0f, 1.42f, -0.16f);
                wingRoot.transform.localRotation = Quaternion.identity;
                sl.wingRoot = wingRoot.transform;

                // Rule 9: every presentation-profile value is rebuilt onto the prefab.
                sl.skyVerdictAttack = SeraphLancerAuthoring.SkyVerdictAttackName;
                sl.heavyAttack = "SeraphLancer_Heavy";
                sl.shoulderChargeAttack = "SeraphLancer_ShoulderCharge";
                sl.shoulderChargeClip = "ShoulderCharge";
                sl.heavyClip = "HeavyAttack";
                sl.jumpClip = "Jump";
                sl.hoverClip = "HoverHold";
                sl.throwClip = "JavelinThrow";
                sl.entranceProbeDelay = 0.12f;
                sl.entranceHoldSeconds = 0.95f;   // Jump is 0.96 s at 1x
                sl.hitHoldSeconds = 0.35f;        // the fluidity pass's cap on a held pose
                // The hover: 3.0 m -- above the Judge's 2.4 m storm float, so the javelin's line is
                // plainly DOWNWARD (37 degrees at the band's near edge, 16 at its far edge) and the read
                // is "look up", while the capsule on the ground still reads as his. The descent is the
                // strike's last 0.45 s.
                sl.hoverHeight = 3.0f;
                sl.hoverDescendSeconds = 0.45f;
                sl.hoverFallSeconds = 0.22f;
                // 120 deg/s: the brain does not turn during Strike, so the hovering body tracks you
                // itself -- fast enough to keep the arm on a circling player, slow enough to read as a
                // body turning, never a snap.
                sl.hoverTrackDegPerSec = 120f;
                // From the manifest's Jump events: OnJumpTakeoff 0.28, OnJumpLand 0.85; the apex is the
                // midpoint of the airborne window.
                sl.jumpTakeoffNormalized = 0.28f;
                sl.jumpApexNormalized = 0.56f;
                sl.jumpLandNormalized = 0.85f;
                sl.landingHoldSeconds = 0.35f;
                // JavelinThrow carries 0.21 s of recovery after its 0.696 release; 0.25 lets the arm
                // finish before HoverHold returns for the 0.28 s breath the 1.10 s cadence leaves.
                sl.throwFollowThroughSeconds = 0.25f;
                sl.chargeSparkInterval = 0.12f;
                sl.landingRingSeconds = 0.34f;
                sl.landingSparkCount = 12;
                sl.landingSparkSpeed = 6f;
                sl.landingSparkSpread = 110f;
                // The dive's hop: 0.8 m over the Heavy's 1.13 s, peaking at the Jump-to-HeavyAttack
                // switch and back on the floor exactly at the impact. A third of the verdict's height:
                // a hop, unmistakably not the rise.
                sl.diveHopHeight = 0.8f;
                // Light-wings: three blades of light a side, 1.6 m long, fanned 28 degrees apart from
                // 18 degrees above the horizontal and raked 25 degrees back. Additive on a runtime
                // material normalised to a 1.0 peak -- under the 1.05 bloom threshold by construction,
                // alpha 0.55 at full spread -- so at 24 m he is the one with WINGS and the javelin tip
                // is still the bright thing. Unfold 0.35 s (from the take-off frame), fold 0.25 s.
                sl.feathersPerWing = 3;
                sl.wingLength = 1.6f;
                sl.wingChord = 0.42f;
                sl.wingSweepDeg = 28f;
                sl.wingRootPitchDeg = 18f;
                sl.wingRakeDeg = 25f;
                sl.wingUnfoldSeconds = 0.35f;
                sl.wingFoldSeconds = 0.25f;
                sl.wingAlpha = 0.55f;
                sl.wingFlickerHz = 9f;
                sl.wingFlickerAmount = 0.12f;
                sl.wingHue = new Color(0.72f, 1.0f, 0.90f, 1f);
                sl.entranceWingSeconds = 0.5f;
                // Verdigris seams: the Judge's aura numbers in verdigris, written on THIS component
                // because it is the aura's only writer here. Rest 0.14 (the palest body of the four; the
                // albedo's trim is already green), 0.42 at the break, 0.50 through the hover -- under the
                // 0.60 EmberAura documents as the ceiling before the parry read suffers.
                sl.verdigrisHot = new Color(0.25f, 0.75f, 0.62f, 1f) * 1.3f;
                sl.glowAtRest = 0.14f;
                sl.glowAtBreak = 0.42f;
                sl.hoverGlow = 0.50f;
                sl.pulseSpeed = 1.9f;
                sl.pulseAmount = 0.12f;

                // ---- the launcher, on the ROOT beside the brain ---------------------------------
                var javelins = root.AddComponent<SeraphLancerJavelins>();
                javelins.skyVerdictAttack = SeraphLancerAuthoring.SkyVerdictAttackName;
                // 3 a verdict, one after another: one to see, one to parry, one to confirm. Not the
                // Dancer's three at once -- the first boss teaches the beat, not the chaos.
                // 4 since 2026-09-14 (user: "absurd but fair"); the 1.10 s cadence is unchanged.
                javelins.javelinsPerVerdict = 4;
                // 1.10 s between releases = 0.57 s of arm-back (the clip's own anticipation, the readable
                // tell) + 0.25 s of follow-through + 0.28 s of HoverHold breath. With 0.4-0.9 s of
                // flight each, arrivals are 1.10 s apart: well over the parry contract's 0.69 s floor.
                javelins.javelinCadence = 1.10f;
                // 4 alive, any thrower: the three of one verdict plus one still flying BACK after a
                // Perfect, so a reflect can never cost him his third throw.
                javelins.liveCap = 5;
                // 5 s at 15 m/s is 75 m: a javelin that found nothing is gone long before the next
                // verdict (10 s cooldown), and a reflected one always reaches him.
                javelins.javelinLifetime = 5f;
                // 1.6 m of shaft, 5 cm thick, behind a 0.30 m tip: reads as a SPEAR at 14 m, not a
                // dot; the logical hit radius is still Projectile's 1.0 m.
                javelins.javelinLength = 1.6f;
                javelins.shaftDiameter = 0.05f;
                javelins.tipSize = 0.30f;
                // The sentries' rule: no flight shorter than CueLead + 0.12 s.
                javelins.launchMargin = 0.12f;
                // RightHand: measured at (0.35, 0.84, -0.08) at rest and 1.61 m up in the throw pose --
                // the airborne muzzle, 4.6 m over the floor at full lift.
                javelins.muzzleBone = "RightHand";
                javelins.muzzleFallbackHeight = 1.6f;
                // A javelin in a wall stays 1.5 s (1.05 held, then 0.45 of shrink): long enough to see
                // "that was for me", gone before the next release lands beside it.
                javelins.relicSeconds = 1.5f;
                javelins.relicHoldFraction = 0.7f;
                javelins.stuckVolume = 0.5f;
                javelins.stuckRange = 24f;
                // The tip: gold at a 1.45 peak. Over the 1.05 bloom threshold, above the Dancer's 1.25
                // disc rim (this is the roster's FIRST projectile lesson and must be the louder one),
                // under the sentry bolt's 1.6, which stays the brightest thing in the game.
                javelins.tipColor = new Color(1.45f, 1.05f, 0.40f, 1f);
                javelins.shaftColor = new Color(0.86f, 0.80f, 0.62f, 1f);
            }

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

            // ---- burning enemies ---------------------------------------------------------------
            // EmberAura is a presentation component like any other: it adds fire without the brain,
            // the timing or the moveset knowing anything about it, so any enemy can be lit by adding
            // it here. Hard rule 9 — the values are written, not left to field initialisers.
            if (shape == Silhouette.Revenant)
            {
                var aura = root.AddComponent<EmberAura>();
                // Over the 1.05 bloom threshold so it blooms, and far under the parry glow's 3.2 so a
                // deflect is still unmistakably the brightest thing this enemy ever does.
                aura.emberHot = new Color(1f, 0.45f, 0.12f, 1f) * 1.5f;
                aura.glowAtRest = 0.22f;
                aura.glowAtBreak = 0.55f;
                // The ember column is sized to THIS body, from the probe: bones run 0.96..1.20 but the
                // mesh reaches 1.96, so the fire has to rise past the spikes or it looks like it is
                // coming from the enemy's waist.
                aura.emberRadius = 0.38f;
                aura.emberFromHeight = 0.15f;
                aura.emberToHeight = 1.75f;
                aura.lightHeight = 1.0f;
            }

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

            // ---- optional, per model ----------------------------------------------------------------
            /// <summary>
            /// Forward shift of the mesh under the collider, metres. The forge places its skeleton on the
            /// z = 0 plane of the source drawing, and a body whose mass sits ahead of that plane (the
            /// Halberdier's torso is centred 0.25 m in front of its bones) would otherwise stand with
            /// its chest a quarter-metre ahead of the capsule that gets hit. Measured, never guessed.
            /// </summary>
            public float zShift;
            /// <summary>
            /// File under <see cref="ModelDir"/> holding the model's albedo texture, or empty for the
            /// shared M_Boss. A textured body gets its own URP/Lit material (cloned from M_Boss so the
            /// emission keyword and the matte settings match); EnemyVisuals still tints it through
            /// _BaseColor every frame, so the sink-and-snap and the parry flash read exactly as they do
            /// on every other enemy. The EnemyData bodyColor should then be near-white.
            /// </summary>
            public string albedo;
            /// <summary>
            /// Give the body an <see cref="EnemyWeaponTrail"/>: a strip swept from the weapon hand to
            /// <see cref="weaponFxPos"/> while an attack clip is in its contact window, normalised under
            /// the bloom cap. Only for a model whose weaponFxPos is really a blade tip in the bind pose.
            /// </summary>
            public bool bladeTrail;
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

                case Silhouette.Revenant:
                    return new ModelSpec
                    {
                        fbx = "EmberRevenant.fbx",
                        yLift = 0f,
                        // EVERY NUMBER BELOW WAS MEASURED, by VibeGame1/Probe Forge Models. That matters
                        // more on this body than on any previous one: the mesh spans y -0.17..1.96, but
                        // the SKELETON only spans 0.96 (Hips) to 1.20 (Head). The top 0.76 m is shoulder
                        // spikes and hood with no bones in it, so every pivot inferred from the bounds --
                        // the eye, the deathblow glyph -- would have floated most of a metre above the
                        // body, in mid-air, pointing at nothing.
                        yaw = 0f,          // 1.97 m wide in X, 1.49 deep: faces +Z like the others
                        // The head bone is at 1.20; the eye sits on it, pushed forward to the face.
                        eyePos = new Vector3(0f, 1.20f, 0.20f),
                        eyeSize = new Vector3(0.17f, 0.09f, 0.08f),
                        // RightShoulder measured at (0.03, 1.11, 0); the hand hangs to 0.78 and out to
                        // -0.16, so the blade lives low and to one side rather than out at shoulder
                        // height. weaponFxPos throws the cue sparks from along that blade.
                        armPos = new Vector3(0.05f, 1.11f, 0f),
                        handPos = new Vector3(-0.21f, -0.33f, 0.12f),
                        weaponFxPos = new Vector3(-0.30f, -0.05f, 0.10f),
                        // The CHEST bone, not a fraction of the bounds. 1.11 puts the glyph on the body
                        // and clear of the head at 1.20.
                        markHeight = 1.11f,
                        note = "tall lanky blade-bearer; 0.76 m of spike above the head bone -- measure, do not infer",

                        animated = true,
                        attackClip = "AttackSwing",
                        heavyClip = "AttackOverhead",
                        idleClip = "IdleCombat",
                        // It does not whirl. No spinPrefix, so PuppetVisuals plays every attack
                        // square-on and the whirl code never runs for this body.
                        spinClip = "",
                        spinPrefix = ""
                    };

                case Silhouette.FlurryBrawler:
                    return new ModelSpec
                    {
                        fbx = "FlurryBrawler.fbx",
                        // RE-MEASURED FOR THE v15 EXPORT (2026-09-07), and every one of these moved.
                        // The v14 body sank 0.23 m into the floor and needed lifting; v15 stands ON the
                        // rig origin -- the mesh spans y -0.00..1.95 -- so the old 0.23 m lift would
                        // HOVER it by a boot's height, which is exactly as broken and half as obvious.
                        // The lift stays on the VISUAL, never on NavMeshAgent.baseOffset: that would
                        // move the agent, the capsule and the distance/cone impact test with it (the
                        // model-swap contract, rule 3).
                        // Source: Tools/measure_forge_fbx.py on the shipped FBX, in Unity axes.
                        yLift = 0f,
                        // Faces +Z. The crown, the face and the toes all sit ahead of the z = 0 bone
                        // plane (crown zmean +0.29, feet +0.02) and nothing sits behind it. v15 is
                        // 1.18 m across the shoulders in its REST pose against v14's 2.02 -- that
                        // export bound in a T-pose and this one binds with the arms down, so the width
                        // claim was never about the fight. What fills the frame is the strings.
                        yaw = 0f,
                        // The mass is still ahead of the bones, but by half what it was: bounds
                        // z -0.18..0.42 (centre +0.12) while every bone lies between -0.01 and +0.02,
                        // and the torso slices measure zmean +0.08 (belly) and +0.15 (chest). -0.12
                        // puts the chest back over the thing that gets hit. Eyeball it at 4b against
                        // the capsule gizmo before trusting it further than that.
                        zShift = -0.12f,
                        // Head bone measured at (0, 1.76, 0.02); the head slice spans y 1.70..1.95 with
                        // z out to 0.42, so the face is well ahead of the skull bone and the eye rides
                        // the brow at z 0.28. It is a ROUND port, not a slot: the Halberdier's blue
                        // visor slit and the Revenant's narrow eye are both bars, so a disc is a
                        // different read at a glance, and EnemyVisuals drives it from Posture.Ratio --
                        // the eye burning acid-green is this fight's break meter, seen without looking
                        // at the bar. LOOK AT THIS ONE at 4b: it is the placement most likely to end up
                        // inside the head or floating off the brow.
                        eyePos = new Vector3(0f, 1.76f, 0.28f),
                        eyeSize = new Vector3(0.20f, 0.20f, 0.09f),
                        eyeRound = true,
                        // RightUpperArm (0.15, 1.63, 0.01) and RightHand (0.35, 0.84, 0.00), measured on
                        // the v15 rest pose. handPos is hand minus shoulder. Both moved: the arms hang
                        // lower and sit on the bone plane rather than 0.14 m ahead of it.
                        armPos = new Vector3(0.15f, 1.63f, 0.01f),
                        handPos = new Vector3(0.20f, -0.79f, -0.01f),
                        // THE FIST, not a blade. This body carries VFX_WeaponTip_L/R bones but no weapon
                        // mesh, and in the rest pose VFX_WeaponTip_R sits 1.15 m out to the SIDE -- a
                        // cue spark thrown from there would come off empty air a metre from the punch.
                        // So the FX marker is a hand's breadth ahead of the knuckles, along the way the
                        // punch travels.
                        weaponFxPos = new Vector3(0.06f, 0.02f, 0.16f),
                        // The CHEST bone (1.40), clear of the head at 1.76 and of the eye at 1.76.
                        markHeight = 1.40f,
                        albedo = "FlurryBrawler_albedo.png",
                        // NO BLADE TRAIL, deliberately. EnemyWeaponTrail sweeps a strip from the
                        // RightHand bone to weaponFxPos, which is right for an axe head that really
                        // sits there and wrong twice over here: the marker is a fist rather than a
                        // blade, and half this enemy's punches (Jab2, UppercutLeft, Clap, Slam) are
                        // anchored on the LEFT wrist, so a right-hand-only strip would trail the wrong
                        // arm through half the repertoire. A two-handed trail is a change to
                        // EnemyWeaponTrail and therefore a lead call, not something to smuggle in with
                        // an enemy.
                        bladeTrail = false,
                        note = "unarmed brawler, v15 export; 1.18 m across the shoulders at rest, 1.95 m tall, boots on the rig origin",

                        animated = true,
                        // The canonical clips back the pipeline mapping only. Every one of this enemy's
                        // twelve attacks NAMES its own clip (EnemyAttackData.clip), so these are the
                        // fallback for an attack that forgets to -- and the model ships both.
                        attackClip = "AttackSwing",
                        heavyClip = "AttackOverhead",
                        idleClip = "IdleCombat",
                        // It does not whirl: the flurries are entirely inside Burst2/4/8, and a spin
                        // prefix would turn the body under a clip that is already turning itself.
                        spinClip = "",
                        spinPrefix = ""
                    };

                case Silhouette.FlurryBrawlerV18:
                    return new ModelSpec
                    {
                        fbx = "FlurryBrawlerV18.fbx",
                        // Measured on the exact V18 source named in FlurryBrawlerV18.provenance.txt:
                        // bounds x -0.45..0.45, y 0.01..1.99, z -0.18..0.41, facing +Z. Feet already
                        // meet the origin and the mass sits 0.12 m ahead of the bone plane.
                        yLift = 0f,
                        yaw = 0f,
                        zShift = -0.12f,
                        // Head bone (0,1.76,0.04); the small posture port sits toward the front surface.
                        eyePos = new Vector3(0f, 1.76f, 0.28f),
                        eyeSize = new Vector3(0.20f, 0.20f, 0.09f),
                        eyeRound = true,
                        // RightUpperArm (0.15,1.63,0.01), RightHand (0.35,0.85,-0.02).
                        armPos = new Vector3(0.15f, 1.63f, 0.01f),
                        handPos = new Vector3(0.20f, -0.78f, -0.03f),
                        weaponFxPos = new Vector3(0.06f, 0.02f, 0.16f),
                        // Chest bone (0,1.40,-0.01).
                        markHeight = 1.40f,
                        albedo = "FlurryBrawlerV18_albedo.png",
                        bladeTrail = false,
                        note = "unarmed V18 test body; 0.90 m wide, 1.98 m tall, feet on origin, +Z facing",

                        animated = true,
                        attackClip = "AttackSwing",
                        heavyClip = "AttackOverhead",
                        // V18 deliberately has no IdleCombat in its filtered project manifest.
                        idleClip = "Idle",
                        spinClip = "",
                        spinPrefix = ""
                    };

                case Silhouette.CinderJudge:
                    return new ModelSpec
                    {
                        fbx = "CinderJudge.fbx",
                        // MEASURED in Blender on the exact source named in CinderJudge.provenance.txt
                        // (Tools/measure_forge_fbx.py, Unity axes): bounds x -0.45..0.44, y 0.00..1.95,
                        // z -0.22..0.40. Feet on the origin, so no lift. Faces +Z: feet zmean +0.02,
                        // head +0.17, crown +0.23, nothing behind the bone plane but the heels.
                        yLift = 0f,
                        yaw = 0f,
                        // Bounds centre z +0.09 and the chest band zmean +0.15 against bones on z ~0:
                        // the armour sits ahead of the skeleton by a little less than V18's (-0.12).
                        zShift = -0.09f,
                        // Head bone (0, 1.76, -0.02); the head slice reaches z 0.40, so the face is well
                        // ahead of the skull. The eye is the VISOR: a wide yellow SLOT across the helm,
                        // not V18's round port -- the one-glance difference between the two brawlers, and
                        // EnemyVisuals drives it from Posture.Ratio like every other eye. LOOK AT THIS
                        // ONE at 4b: a slot floating off the brow is the likeliest placement bug.
                        eyePos = new Vector3(0f, 1.79f, 0.30f),
                        eyeSize = new Vector3(0.26f, 0.08f, 0.08f),
                        eyeRound = false,
                        // RightUpperArm (0.15, 1.63, -0.03), RightHand (0.39, 0.84, -0.07): handPos is
                        // hand minus shoulder. The gauntlet hangs a little wider than V18's fist.
                        armPos = new Vector3(0.15f, 1.63f, -0.03f),
                        handPos = new Vector3(0.24f, -0.79f, -0.04f),
                        // The fist, a hand's breadth ahead of the knuckles along the punch, as on V18.
                        weaponFxPos = new Vector3(0.06f, 0.02f, 0.16f),
                        // The CHEST bone (0, 1.40, -0.06), clear of the head at 1.76 and the visor at 1.79.
                        markHeight = 1.40f,
                        albedo = "CinderJudge_albedo.png",
                        // No blade trail: an unarmed body, and half its contacts are on the left wrist.
                        bladeTrail = false,
                        note = "blackened-bronze arena enforcer; 0.89 m wide, 1.95 m tall, feet on origin, +Z facing, yellow visor slot",

                        animated = true,
                        attackClip = "AttackSwing",
                        // The user's list has no overhead; the generated HeavyAttack is the heavy clip.
                        heavyClip = "HeavyAttack",
                        // No IdleCombat in the filtered manifest (the user's list says idle).
                        idleClip = "Idle",
                        spinClip = "",
                        spinPrefix = ""
                    };

                case Silhouette.OrbitDancer:
                    return new ModelSpec
                    {
                        fbx = "OrbitDancer.fbx",
                        // MEASURED in Blender on the exact source named in OrbitDancer.provenance.txt
                        // (Tools/measure_forge_fbx.py, Unity axes): bounds x -0.52..0.52, y 0.00..1.95,
                        // z -0.46..0.61. Feet on the origin, so no lift. Faces +Z: feet zmean +0.18,
                        // head +0.34, crown +0.36; the only thing behind the plane is a 7-vertex shard.
                        yLift = 0f,
                        yaw = 0f,
                        // UNLIKE every other forge rig, this skeleton sits 0.15 m AHEAD of the drawing
                        // plane (Hips (0,1.06,0.15), Chest (0,1.40,0.16)) and the chest band's mass at
                        // zmean +0.34. -0.18 puts the hips just behind the capsule's centre line and the
                        // chest 0.16 ahead of it -- the same relation V18 (-0.12) and the Judge (-0.09)
                        // stand in. Eyeball it at 4b against the capsule gizmo before trusting it further.
                        zShift = -0.18f,
                        // Head bone (0, 1.76, 0.17); the head slice spans z 0.15..0.57, so the face is
                        // 0.3 m ahead of the skull bone. The eye is the MASK's eye-line: a thin wide SLIT,
                        // narrower and lower than the Judge's visor slot (0.26 x 0.08) -- the one-glance
                        // difference between the two -- and EnemyVisuals drives it from Posture.Ratio
                        // like every other eye. LOOK AT THIS ONE at 4b: a slit floating off the mask is
                        // the likeliest placement bug on a face this far ahead of its bone.
                        eyePos = new Vector3(0f, 1.79f, 0.46f),
                        eyeSize = new Vector3(0.22f, 0.05f, 0.07f),
                        eyeRound = false,
                        // RightUpperArm (0.15, 1.63, 0.15), RightHand (0.35, 0.84, 0.18): handPos is
                        // hand minus shoulder. The ring bracer hangs a little closer in than the Judge's
                        // gauntlet (0.20 out against 0.24).
                        armPos = new Vector3(0.15f, 1.63f, 0.15f),
                        handPos = new Vector3(0.20f, -0.79f, 0.03f),
                        // The palm, a hand's breadth ahead of the fingers along the throw: where the cue
                        // sparks throw from and where the disc appears (OrbitDancerDiscs uses the bone).
                        weaponFxPos = new Vector3(0.06f, 0.02f, 0.16f),
                        // The CHEST bone (0, 1.40, 0.16), clear of the head at 1.76 and the slit at 1.79.
                        markHeight = 1.40f,
                        albedo = "OrbitDancer_albedo.png",
                        // No blade trail: an unarmed body whose blade is the DISC, and half its contacts
                        // are on the left wrist (Jab2, HeavyAttack, ComboFinisher).
                        bladeTrail = false,
                        note = "masked obsidian chakram duelist; 1.04 m across the bracers, 1.95 m tall, feet on origin, +Z facing, skeleton 0.15 m ahead of the plane",

                        animated = true,
                        attackClip = "AttackSwing",
                        // The base kit has no overhead; the generated HeavyAttack is the heavy clip.
                        heavyClip = "HeavyAttack",
                        // No IdleCombat in the filtered manifest (the user's list says idle).
                        idleClip = "Idle",
                        spinClip = "",
                        spinPrefix = ""
                    };

                case Silhouette.SeraphLancer:
                    return new ModelSpec
                    {
                        fbx = "SeraphLancer.fbx",
                        // MEASURED in Blender on the exact source named in SeraphLancer.provenance.txt
                        // (Tools/measure_forge_fbx.py, Unity axes): bounds x -0.40..0.40, y 0.00..1.98,
                        // z -0.23..0.23. Feet on the origin, so no lift. Faces +Z: feet zmean +0.02,
                        // head +0.13, crown +0.17 (the crest leans forward over the brow).
                        yLift = 0f,
                        yaw = 0f,
                        // Bounds centre z 0.00, chest band zmean +0.04, belly -0.04, bones on z -0.04..-0.07:
                        // the skeleton sits ON the drawing plane and the armour barely ahead of it, so the
                        // mesh needs almost no shift under the collider (V18 -0.12, Judge -0.09, Dancer -0.18).
                        zShift = -0.02f,
                        // Head bone (0, 1.76, -0.02); the head slice reaches z 0.22. The eye is the HELM's
                        // visor: a narrow verdigris SLOT at the brow, thinner than the Judge's yellow slot
                        // (0.26 x 0.08) and higher than the Dancer's mask slit -- the one-glance difference
                        // between the three helmed bodies -- and EnemyVisuals drives it from Posture.Ratio
                        // like every other eye. LOOK AT THIS ONE at 4b: a slot floating off the visor is
                        // the likeliest placement bug.
                        eyePos = new Vector3(0f, 1.80f, 0.20f),
                        eyeSize = new Vector3(0.20f, 0.05f, 0.07f),
                        eyeRound = false,
                        // RightUpperArm (0.15, 1.63, 0.00), RightHand (0.35, 0.84, -0.08): handPos is
                        // hand minus shoulder. The bare arm hangs closer in than the Judge's gauntlet
                        // (0.20 out against 0.24).
                        armPos = new Vector3(0.15f, 1.63f, 0.00f),
                        handPos = new Vector3(0.20f, -0.79f, -0.08f),
                        // The palm, a hand's breadth ahead of the fingers along the throw: where the cue
                        // sparks throw from (SeraphLancerJavelins uses the bone for the javelin itself).
                        weaponFxPos = new Vector3(0.06f, 0.02f, 0.16f),
                        // The CHEST bone (0, 1.40, -0.07), clear of the head at 1.76 and the visor at 1.80.
                        markHeight = 1.40f,
                        albedo = "SeraphLancer_albedo.png",
                        // No blade trail: the javelin is a projectile, not a held blade, and half his
                        // contacts are on the left wrist (Jab2, HeavyAttack, ComboFinisher).
                        bladeTrail = false,
                        note = "pale-gold crested sky lancer with verdigris trim; 0.80 m wide (the narrowest forge body), 1.98 m to the crest, feet on origin, +Z facing, skeleton on the plane",

                        animated = true,
                        attackClip = "AttackSwing",
                        // The base kit has no overhead; the generated HeavyAttack is the heavy clip.
                        heavyClip = "HeavyAttack",
                        // No IdleCombat in the filtered manifest (the user's list says idle).
                        idleClip = "Idle",
                        spinClip = "",
                        spinPrefix = ""
                    };

                case Silhouette.Halberdier:
                    return new ModelSpec
                    {
                        fbx = "ArgentHalberdier.fbx",
                        // STANDS on armoured feet; feet on the collider base.
                        yLift = 0f,
                        // EVERY NUMBER BELOW WAS MEASURED, in Blender (the bridge was down) on the exact
                        // FBX shipped here, in Unity axes -- see the ENGINEERING-LOG entry for the
                        // script. Bounds x -0.56..0.52, y 0..1.86, z -0.69..0.93. The skeleton lies on
                        // the z = 0 plane (every bone at z 0.00) while the torso spans z 0.02..0.43 and
                        // the halberd sits at z 0.20..0.65 IN FRONT of it: the tail is the only thing
                        // behind (a thin ground-level strip out to z -0.69), so it faces +Z like every
                        // forge export, and the body mass is a quarter-metre ahead of its bones.
                        yaw = 0f,
                        zShift = -0.22f,
                        // The head bone is at y 1.70 (Unity probe; Blender's rest pose said 1.73) and the
                        // front of the helm at z ~0.41: the eye is a thin visor slit on the face, well
                        // under the horns (crown to 2.00).
                        eyePos = new Vector3(0f, 1.72f, 0.40f),
                        eyeSize = new Vector3(0.18f, 0.05f, 0.08f),
                        // From VibeGame1/Probe Forge Models on the imported model (its default pose is the
                        // take's first frame, the Idle, not Blender's rest pose): RightUpperArm
                        // (0.06, 1.54, 0.08), RightHand (0.25, 0.79, 0.18) -- the halberd hand hangs low
                        // and close. handPos is hand minus shoulder.
                        armPos = new Vector3(0.06f, 1.54f, 0.08f),
                        handPos = new Vector3(0.19f, -0.75f, 0.10f),
                        // The axe head: the far-right slice of the mesh (x 0.40..0.92) sits at y 0.81..1.56,
                        // z 0.20..0.65 -- out, up and forward of the idle hand. Cue sparks throw from there.
                        weaponFxPos = new Vector3(0.35f, 0.25f, 0.15f),
                        // The CHEST bone (1.54 in Unity), clear of the head at 1.70.
                        markHeight = 1.54f,
                        albedo = "ArgentHalberdier_albedo.png",
                        // The axe head really is at weaponFxPos in the bind pose, so a hand-to-head strip
                        // follows the halberd through every generated clip. From play: "make his attacks
                        // more visually appealing" -- see EnemyWeaponTrail for the budget.
                        bladeTrail = true,
                        note = "towering armoured halberdier with a tail; 1.86 m to the horns, bones on z = 0 with the body 0.25 m ahead",

                        animated = true,
                        // The canonical four still back the pipeline mapping, but every attack this
                        // enemy has NAMES its own generated clip (EnemyAttackData.clip), so these are
                        // only the fallback for an attack that forgets to.
                        attackClip = "AttackSwing",
                        heavyClip = "AttackOverhead",
                        idleClip = "IdleCombat",
                        spinClip = "",
                        spinPrefix = ""
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
        static void WireAnimatedBody(PuppetVisuals pv, ModelSpec spec, Transform modelRoot, string name,
                                     EnemyData data)
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

            // ---- a DEDICATED transform for cancelling clip travel -------------------------------
            // Only for models whose manifest has travelling clips. Same rule as SpinRoot: one
            // transform, one writer. PuppetVisuals.CompensateTravel is the writer.
            if (ForgeClipSplitter.AnyClipTravels(fbx))
            {
                Transform hips = null;
                foreach (var t in modelRoot.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Hips") { hips = t; break; }
                if (hips == null)
                {
                    Debug.LogError("[MiniBossFactory] " + name + " has travelling clips but no 'Hips' bone; the " +
                                   "mesh will walk off its collider on every thrust and charge.");
                }
                else
                {
                    var travel = new GameObject("TravelRoot");
                    travel.transform.SetParent(spin.transform, false);
                    travel.transform.localPosition = Vector3.zero;
                    travel.transform.localRotation = Quaternion.identity;
                    modelRoot.SetParent(travel.transform, false);
                    pv.travelRoot = travel.transform;
                    pv.hipsBone = hips;
                    pv.hipsRestLocal = modelRoot.InverseTransformPoint(hips.position);
                }
            }

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
            // 2087 deg/s CONSTANT — 5.8 revolutions a second, and the rate never changes: not between
            // passes, not into an impact, not during the strike. It is derived from the beat rather
            // than picked by feel: 2087 x 0.69 s = 1440 deg = exactly FOUR revolutions per beat, so the
            // body returns to the same yaw on every impact with a correction of zero.
            //
            // The previous value was an eased curve peaking at 4.5x average and decaying to 0.25x, on
            // the theory that decelerating into the player was a readable wind-up. Played, it read as a
            // PULSE — blur, slow, blur, slow — not as a spinning body. Constant is the brief.
            pv.spinDegPerSec = 2087f;
            pv.maxRateCorrection = 0.12f;
            // After the impact the clip runs at its AUTHORED rate for the follow-through, whatever
            // scale the wind-up needed to land the contact frame on the data's impact. Before this the
            // wind-up scale (x0.55-0.7 on most of the Halberdier's attacks) carried through the whole
            // clip and the loop cut back to idle 0.25 s after the blow: a slow-motion swing that
            // snapped straight to standing. Rule 9: written here.
            pv.recoverySpeed = 1f;
            pv.followThroughSeconds = 0.45f;
            // 75 deg/frame: the alias guard. Never binds above ~55 fps at these values; below that it
            // trades blur for legibility rather than letting the body strobe. See PuppetVisuals.
            pv.maxDegPerFrame = 75f;

            pv.attackClipLength = PuppetAnimatorFactory.ClipLength(fbx, spec.attackClip, 1f);
            pv.attackHitNormalized = ForgeClipSplitter.ReadHitNormalizedTime(fbx, spec.attackClip, 0.55f);
            pv.spinClipLength = PuppetAnimatorFactory.ClipLength(fbx, pv.clipSpin, 1f);
            pv.spinHitNormalized = ForgeClipSplitter.ReadHitNormalizedTime(fbx, pv.clipSpin, 0.4f);
            // The stab and the kick get their OWN contact frames. Scaling them by the swing's anchor
            // would land their blow at the wrong moment, which is the one thing the clip layer is not
            // allowed to do (timing is data-driven; the clip bends to it).
            pv.stabClipLength = PuppetAnimatorFactory.ClipLength(fbx, pv.clipStab, 1f);
            pv.stabHitNormalized = ForgeClipSplitter.ReadHitNormalizedTime(fbx, pv.clipStab, 0.55f);
            pv.kickClipLength = PuppetAnimatorFactory.ClipLength(fbx, pv.clipKick, 1f);
            pv.kickHitNormalized = ForgeClipSplitter.ReadHitNormalizedTime(fbx, pv.clipKick, 0.55f);

            // ---- locomotion stride speeds (2026-09-13 fluidity pass, rule 9) ---------------------
            // Measured on the IMPORTED clip (the sidecar under-reports travel on these rigs), so the
            // Walk/Run playback rate can match the body's real speed instead of sliding the feet.
            pv.walkStrideSpeed = StrideSpeed(fbx, pv.clipWalk);
            pv.runStrideSpeed = StrideSpeed(fbx, pv.clipRun);
            // Footfall dust only on the two forge bodies the user asked to feel more alive; every older
            // mini-boss keeps alpha 0 (no dust) so its look is unchanged.
            pv.footstepDust = name == FlurryBrawlerV18Authoring.EnemyName ? new Color(0.55f, 0.5f, 0.45f, 0.35f)
                            : name == CinderJudgeAuthoring.EnemyName ? new Color(0.9f, 0.45f, 0.18f, 0.35f)
                            : name == OrbitDancerAuthoring.EnemyName ? new Color(0.35f, 0.75f, 0.7f, 0.30f)   // bare feet: a lighter teal puff
                            : name == SeraphLancerAuthoring.EnemyName ? new Color(0.85f, 0.78f, 0.55f, 0.30f) // gold greaves: a pale dust
                            : new Color(0f, 0f, 0f, 0f);

            // ---- the NAMED clip table: every attack clip the model ships -------------------------
            // An EnemyAttackData may name its clip outright (EnemyAttackData.clip) -- the only way a
            // GENERATED, per-character clip is ever reached, since the pipeline mapping only knows
            // the four canonical names. Each entry carries its own length and contact anchor, read
            // from the manifest here at build time, so the clip still bends to the attack's clock.
            var withHit = ForgeClipSplitter.ClipsWithEvent(fbx, "OnAttackHit");
            // V18 Dash is intentionally eventless in the source. Its generated beats profile still has
            // an explicit 0.60 contact, so bake it without inventing a runtime AnimationEvent. The same
            // table pins Combo2 to its one approved source contact instead of interpreting its later
            // performance gestures as additional combat.
            float unusedExplicit;
            foreach (var allowedClip in FlurryBrawlerV18Authoring.ClipAllowlist)
                if (FlurryBrawlerV18Authoring.TryExplicitContact(name, allowedClip, out unusedExplicit) &&
                    !withHit.Contains(allowedClip))
                    withHit.Add(allowedClip);
            // The Cinder Judge's Roar is the storm's wind-up performance and carries no OnAttackHit; its
            // explicit 0.40 (the source OnRoar moment) is baked so the attack that names it validates.
            foreach (var allowedClip in CinderJudgeAuthoring.ClipAllowlist)
                if (CinderJudgeAuthoring.TryExplicitContact(name, allowedClip, out unusedExplicit) &&
                    !withHit.Contains(allowedClip))
                    withHit.Add(allowedClip);
            pv.namedClips = withHit.ToArray();
            pv.namedClipLengths = new float[withHit.Count];
            pv.namedClipHits = new float[withHit.Count];
            for (int i = 0; i < withHit.Count; i++)
            {
                pv.namedClipLengths[i] = PuppetAnimatorFactory.ClipLength(fbx, withHit[i], 1f);
                // The manifest's OnAttackHit is authored for the 15 canonical clips and a GUESS for a
                // generated one (the tool's "#hit" fraction). From play (2026-09-04): "the animations
                // don't line up with the attack hitboxes". So a generated clip's anchor is MEASURED on
                // the imported clip -- the frame where a hand or a foot reaches furthest forward of the
                // pelvis -- and the manifest is kept only when the art reaches no further anywhere else.
                float manifest = ForgeClipSplitter.ReadHitNormalizedTime(fbx, withHit[i], 0.55f);
                string why = "manifest (authored clip)";
                float explicitAnchor;
                float anchor;
                if (FlurryBrawlerV18Authoring.TryExplicitContact(name, withHit[i], out explicitAnchor))
                {
                    anchor = explicitAnchor;
                    why = "explicit generated beats profile (source has no OnAttackHit)";
                }
                else if (CinderJudgeAuthoring.TryExplicitContact(name, withHit[i], out explicitAnchor))
                {
                    anchor = explicitAnchor;
                    why = "explicit storm wind-up profile (Roar's OnRoar moment; source has no OnAttackHit)";
                }
                else
                {
                    anchor = ForgeClipSplitter.ClipIsGenerated(fbx, withHit[i])
                        ? MeasureContactFraction(fbx, withHit[i], manifest, out why)
                        : manifest;
                }
                pv.namedClipHits[i] = anchor;
                Debug.Log("[MiniBossFactory] " + name + " clip '" + withHit[i] + "': contact anchor " +
                          anchor.ToString("F2") + " -- " + why);
            }

            // Validate the clips the component names actually exist. A missing clip is SILENT at
            // runtime — CrossFade to a state that is not there simply does nothing and the puppet
            // keeps playing whatever it was playing, which reads as "the animation is broken" with
            // nothing in the console.
            var have = PuppetAnimatorFactory.ClipsIn(fbx);
            var names = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < have.Count; i++) names.Add(have[i].name);

            if (name == FlurryBrawlerV18Authoring.EnemyName)
            {
                var allowed = new System.Collections.Generic.HashSet<string>(FlurryBrawlerV18Authoring.ClipAllowlist);
                if (!names.SetEquals(allowed))
                    Debug.LogError("[MiniBossFactory] " + name + " must import exactly the approved " +
                        FlurryBrawlerV18Authoring.ClipAllowlist.Length + " clips. Expected: " +
                        string.Join(", ", allowed) + "; imported: " + string.Join(", ", names) + ".");
            }
            else if (name == CinderJudgeAuthoring.EnemyName)
            {
                var allowed = new System.Collections.Generic.HashSet<string>(CinderJudgeAuthoring.ClipAllowlist);
                if (!names.SetEquals(allowed))
                    Debug.LogError("[MiniBossFactory] " + name + " must import exactly the approved " +
                        CinderJudgeAuthoring.ClipAllowlist.Length + " clips. Expected: " +
                        string.Join(", ", allowed) + "; imported: " + string.Join(", ", names) + ".");
            }
            else if (name == OrbitDancerAuthoring.EnemyName)
            {
                var allowed = new System.Collections.Generic.HashSet<string>(OrbitDancerAuthoring.ClipAllowlist);
                if (!names.SetEquals(allowed))
                    Debug.LogError("[MiniBossFactory] " + name + " must import exactly the approved " +
                        OrbitDancerAuthoring.ClipAllowlist.Length + " clips. Expected: " +
                        string.Join(", ", allowed) + "; imported: " + string.Join(", ", names) + ".");
            }
            else if (name == SeraphLancerAuthoring.EnemyName)
            {
                var allowed = new System.Collections.Generic.HashSet<string>(SeraphLancerAuthoring.ClipAllowlist);
                if (!names.SetEquals(allowed))
                    Debug.LogError("[MiniBossFactory] " + name + " must import exactly the approved " +
                        SeraphLancerAuthoring.ClipAllowlist.Length + " clips. Expected: " +
                        string.Join(", ", allowed) + "; imported: " + string.Join(", ", names) + ".");
            }

            // ...and the same for every clip the enemy's ATTACKS name. Checked against the imported
            // clips AND the baked table: a name in the manifest that did not import would pass the
            // first and fail at runtime.
            if (data != null)
            {
                var combos = data.ResolveCombos();
                if (combos != null)
                    foreach (var combo in combos)
                    {
                        if (combo == null || combo.hits == null) continue;
                        foreach (var atk in combo.hits)
                        {
                            if (atk == null || string.IsNullOrEmpty(atk.clip)) continue;
                            if (!names.Contains(atk.clip) || pv.IndexOfNamedClip(atk.clip) < 0)
                                Debug.LogError("[MiniBossFactory] " + name + ": attack " + atk.name +
                                    " names clip '" + atk.clip + "' but " + fbx + (names.Contains(atk.clip)
                                    ? " ships it with no OnAttackHit event, so no contact frame can be baked."
                                    : " has no such clip. It has: " + string.Join(", ", names) + ".") +
                                    " The attack will fall back to the pipeline mapping at runtime.");
                        }
                    }
            }
            foreach (var wanted in new[] { pv.clipIdle, pv.clipWalk, pv.clipRun, pv.clipAttack,
                                           pv.clipHeavy, pv.clipSpin, pv.clipStab, pv.clipKick,
                                           pv.clipHit, pv.clipStagger, pv.clipDeath, pv.clipRoar })
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

            if (spec.bladeTrail) WireBladeTrail(pv, spec, modelRoot, name, data);
        }

        /// <summary>
        /// The blade trail (<see cref="EnemyWeaponTrail"/>). The two ends of the strip are the weapon
        /// HAND and the ModelSpec's weaponFxPos, both converted from model space into the hand bone's
        /// local space while the prefab stands in its bind pose -- so at build time they are the same
        /// points the cue sparks throw from, and at runtime they ride the bone through every clip. The
        /// attack table is the one PuppetVisuals bakes, so the strip's window is keyed to the SAME contact
        /// frame the blow is timed to. Rule 9: every number written here.
        /// </summary>
        /// <summary>
        /// Metres per second a looping gait clip's Hips cover at rate 1, sampled on the imported FBX (first
        /// frame to last, horizontal). 0 when the clip is missing, has no Hips, or does not travel — the
        /// puppet then keeps the authored rate.
        /// </summary>
        static float StrideSpeed(string fbxPath, string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return 0f;
            AnimationClip clip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                var c = o as AnimationClip;
                if (c != null && c.name == clipName) { clip = c; break; }
            }
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (clip == null || src == null || clip.length <= 0.01f) return 0f;
            var go = (GameObject)Object.Instantiate(src);
            try
            {
                Transform hips = null;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Hips") { hips = t; break; }
                if (hips == null) return 0f;
                clip.SampleAnimation(go, 0f);
                Vector3 a = go.transform.InverseTransformPoint(hips.position);
                clip.SampleAnimation(go, clip.length);
                Vector3 b = go.transform.InverseTransformPoint(hips.position);
                float travel = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                return travel < 0.05f ? 0f : travel / clip.length;
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void WireBladeTrail(PuppetVisuals pv, ModelSpec spec, Transform modelRoot, string name, EnemyData data)
        {
            Transform hand = null;
            foreach (var t in modelRoot.GetComponentsInChildren<Transform>(true))
                if (t.name == "RightHand") { hand = t; break; }
            if (hand == null)
            {
                Debug.LogError("[MiniBossFactory] " + name + ": bladeTrail requested but the rig has no " +
                               "'RightHand' bone; no trail wired.");
                return;
            }

            var trail = pv.gameObject.AddComponent<EnemyWeaponTrail>();
            trail.bladeBone = hand;
            trail.animator = pv.animator;
            Vector3 handModel = spec.armPos + spec.handPos;
            Vector3 tipModel = handModel + spec.weaponFxPos;
            trail.bladeBaseLocal = hand.InverseTransformPoint(modelRoot.TransformPoint(handModel));
            trail.bladeTipLocal = hand.InverseTransformPoint(modelRoot.TransformPoint(tipModel));
            trail.attackClips = (string[])pv.namedClips.Clone();
            trail.attackHits = (float[])pv.namedClipHits.Clone();
            // The contact window: the last ~22% of the clip before the contact and 12% after. On the
            // 0.75 s sweep that is 0.17 s of cut and 0.09 s of follow-through -- the swing, not the tell.
            trail.leadIn = 0.22f;
            trail.tail = 0.12f;
            trail.samples = 14;
            trail.subdivisions = 2;
            trail.fadeSeconds = 0.12f;
            // Four sparks at the contact frame: an accent. The parry throws ten; it must stay the event.
            trail.contactSparks = 4;
            trail.contactSparkSpeed = 5f;
            // The enemy's accent, normalised to a peak channel of 1.0 by SlashFx at Awake -- under the
            // 1.05 bloom threshold. Light on an enemy means "you deflected"; a swing may not glow.
            trail.hue = data != null ? SlashFx.NormaliseColor(data.emission) : Color.white;

            Debug.Log("[MiniBossFactory] " + name + " blade trail: bone '" + hand.name + "', base " +
                      trail.bladeBaseLocal.ToString("F2") + " tip " + trail.bladeTipLocal.ToString("F2") +
                      " (hand space), " + trail.attackClips.Length + " attack clips.");
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
            model.transform.localPosition = new Vector3(0f, spec.yLift, spec.zShift);
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
            // URP or magenta. The older forge FBXs carry vertex colours and no texture, and EnemyVisuals
            // overwrites _BaseColor from EnemyData every frame anyway, so the shared enemy body material
            // is exactly right for them. A model that ships an albedo (ModelSpec.albedo) gets its own
            // material, cloned from the same one so nothing but the base map differs.
            skin.sharedMaterial = BodyMaterialFor(spec, bodyMat);
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

        /// <summary>
        /// Where a clip's art actually STRIKES, as a 0..1 fraction: the sample at which the weapon's tip
        /// is moving fastest while out in front of the pelvis. The tip is found from the skin -- the
        /// vertex most weighted to <c>RightHand</c> and farthest from it, i.e. the end of whatever the
        /// hand holds -- so a halberd head 0.8 m from the fist is measured, not the fist. Sampled at 5 %
        /// steps on an instance of the FBX: deterministic, nothing at runtime.
        ///
        /// <para>Three guards, each from a clip that fooled a simpler rule (2026-09-04): a clip with a
        /// sidecar airborne window keeps its manifest outright (a leap's blow is the landing body, and
        /// its fastest tip moment is the raise before take-off); the tip must be moving at least
        /// <see cref="ContactMinTipSpeed"/> (a generated clip whose blade merely drifts has no strike to
        /// find, and its manifest guess is as good as any frame); and the manifest is kept when the
        /// measured frame is within one sample of it. Public so <c>HalberdierDataTests</c> can hold the
        /// baked anchors to the same measurement, and so the console can say which clips have no
        /// strike at all -- the actual finding on the Halberdier's generated clips.</para>
        /// </summary>
        public static float MeasureContactFraction(string fbxPath, string clipName, float manifestFraction, out string why)
        {
            why = "manifest";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            AnimationClip clip = null;
            foreach (var c in PuppetAnimatorFactory.ClipsIn(fbxPath))
                if (c.name == clipName) { clip = c; break; }
            if (source == null || clip == null || clip.length <= 0.01f) return manifestFraction;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Transform hips = null, hand = null;
                int handIdx = -1;
                if (smr != null && smr.sharedMesh != null)
                    for (int b = 0; b < smr.bones.Length; b++)
                    {
                        if (smr.bones[b] == null) continue;
                        if (smr.bones[b].name == "RightHand") { handIdx = b; hand = smr.bones[b]; }
                        if (smr.bones[b].name == "Hips") hips = smr.bones[b];
                    }
                if (hips == null || hand == null || handIdx < 0)
                {
                    why = "manifest (no Hips / RightHand bone to measure)";
                    return manifestFraction;
                }

                // The weapon tip in the hand's bind space: the hand-weighted vertex farthest from the joint.
                var mesh = smr.sharedMesh;
                var verts = mesh.vertices; var weights = mesh.boneWeights; var bind = mesh.bindposes;
                float farthest = -1f; Vector3 tipLocal = Vector3.zero;
                for (int v = 0; v < verts.Length && v < weights.Length; v++)
                {
                    var w = weights[v]; float hw = 0f;
                    if (w.boneIndex0 == handIdx) hw += w.weight0;
                    if (w.boneIndex1 == handIdx) hw += w.weight1;
                    if (w.boneIndex2 == handIdx) hw += w.weight2;
                    if (w.boneIndex3 == handIdx) hw += w.weight3;
                    if (hw < 0.5f) continue;
                    var local = bind[handIdx].MultiplyPoint3x4(verts[v]);
                    if (local.magnitude > farthest) { farthest = local.magnitude; tipLocal = local; }
                }
                if (farthest < 0.05f) { why = "manifest (nothing skinned to RightHand)"; return manifestFraction; }

                // A clip whose body leaves the ground (the sidecar's airborne window: a leap, a spin, a
                // charge) keeps the tool's contact: its blow is the BODY landing, not the blade, and the
                // tip's fastest moment is the pre-launch raise -- LeapSlam measured 15 m/s at 0.40 of
                // the clip, in the crouch before take-off, against a landing slam at 0.77+.
                var root = ForgeClipSplitter.ReadRoot(fbxPath, clipName);
                if (root != null && root.airborne != null && root.airborne.Length >= 2)
                {
                    why = "manifest " + manifestFraction.ToString("F2") + " kept: airborne clip (" +
                          root.airborne[0].ToString("F2") + "-" + root.airborne[1].ToString("F2") +
                          "), the body is the blow";
                    return manifestFraction;
                }

                Vector3 fwd = go.transform.forward;
                Vector3 prev = Vector3.zero;
                float bestSpeed = 0f, bestT = manifestFraction, bestReach = 0f, peakAny = 0f;
                for (int i = 1; i <= 19; i++)
                {
                    float t = i * 0.05f;
                    clip.SampleAnimation(go, clip.length * t);
                    Vector3 tip = hand.TransformPoint(tipLocal);
                    float reach = Vector3.Dot(tip - hips.position, fwd);
                    float speed = i > 1 ? (tip - prev).magnitude / (clip.length * 0.05f) : 0f;
                    prev = tip;
                    if (speed > peakAny) peakAny = speed;
                    if (reach <= 0f) continue;
                    if (speed > bestSpeed) { bestSpeed = speed; bestT = t; bestReach = reach; }
                }

                if (bestSpeed < ContactMinTipSpeed)
                {
                    why = "manifest " + manifestFraction.ToString("F2") + " kept: NO STRIKE in this clip (weapon tip " +
                          "peaks at " + peakAny.ToString("F0") + " m/s, needs " + ContactMinTipSpeed.ToString("F0") +
                          "); the blade never visibly connects -- prefer an authored strike clip";
                    return manifestFraction;
                }
                if (Mathf.Abs(bestT - manifestFraction) <= 0.051f)
                {
                    why = "manifest " + manifestFraction.ToString("F2") + " confirmed (tip " +
                          bestSpeed.ToString("F0") + " m/s, " + bestReach.ToString("F2") + " m out at " + bestT.ToString("F2") + ")";
                    return manifestFraction;
                }
                why = "MEASURED " + bestT.ToString("F2") + ": tip " + bestSpeed.ToString("F0") + " m/s, " +
                      bestReach.ToString("F2") + " m out (manifest said " + manifestFraction.ToString("F2") + ")";
                return bestT;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Weapon-tip speed (m/s) below which a clip is judged to have no strike in it. The
        /// authored forge strikes peak at 36-86; the Halberdier's generated clips at 1-8.</summary>
        public const float ContactMinTipSpeed = 10f;

        /// <summary>
        /// The body material for an imported model: the shared <paramref name="fallback"/> (M_Boss)
        /// unless the spec names an albedo, in which case a per-model URP/Lit clone of it carrying that
        /// texture, regenerated every build at <c>Assets/Materials/M_&lt;model&gt;.mat</c>. Cloning
        /// rather than authoring keeps the emission keyword, the matte settings and the shader
        /// identical, so the parry flash and the wind-up sink read the same on a textured body.
        /// </summary>
        static Material BodyMaterialFor(ModelSpec spec, Material fallback)
        {
            if (spec == null || string.IsNullOrEmpty(spec.albedo) || fallback == null) return fallback;

            string texPath = ModelDir + "/" + spec.albedo;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                Debug.LogError("[MiniBossFactory] MISSING ALBEDO: " + texPath + " (named by the ModelSpec for " +
                               spec.fbx + "). Copy it in beside the FBX. Falling back to " + fallback.name +
                               " so the body is not magenta, but it will be the wrong colour.");
                return fallback;
            }

            string matName = "M_" + System.IO.Path.GetFileNameWithoutExtension(spec.fbx);
            string matPath = MaterialDir + "/" + matName + ".mat";
            var fresh = new Material(fallback) { name = matName };
            fresh.SetTexture("_BaseMap", tex);
            fresh.SetColor("_BaseColor", Color.white);   // EnemyVisuals tints from EnemyData every frame
            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing == null)
            {
                DataFactory.EnsureFolder(MaterialDir);
                AssetDatabase.CreateAsset(fresh, matPath);
                existing = fresh;
            }
            else
            {
                existing.CopyPropertiesFromMaterial(fresh);
                existing.shader = fresh.shader;
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(existing);
            }
            return existing;
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
