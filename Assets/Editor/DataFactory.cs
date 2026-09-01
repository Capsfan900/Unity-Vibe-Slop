using System.IO;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Creates (or overwrites) every ScriptableObject data asset with the initial tuning values.
    /// Idempotent: existing assets are loaded and their fields rewritten so references stay valid.
    /// </summary>
    public static class DataFactory
    {
        const string DataRoot = "Assets/Data";
        const string AttacksDir = DataRoot + "/Attacks";
        const string EnemiesDir = DataRoot + "/Enemies";
        const string WeaponsDir = DataRoot + "/Weapons";
        const string ItemsDir = DataRoot + "/Items";
        const string MovesetsDir = DataRoot + "/Movesets";
        const string LevelsDir = DataRoot + "/Levels";

        [MenuItem("VibeGame1/3. Create Data")]
        public static void CreateAll()
        {
            EnsureFolder(DataRoot);
            EnsureFolder(AttacksDir);
            EnsureFolder(EnemiesDir);
            EnsureFolder(WeaponsDir);
            EnsureFolder(ItemsDir);
            EnsureFolder(MovesetsDir);
            EnsureFolder(LevelsDir);

            // ---------------- Attacks ----------------
            // Every wind-up stays >= 0.45s. The cue fires cueLead (0.28s) before each impact regardless of
            // combo gaps, so chaining hits raises PRESSURE, never unreadability.
            var gruntJab = Attack("Grunt_Jab", a =>
            {
                // the quick opener/filler — short recovery keeps the exchange rolling
                a.windup = 0.45f; a.impactDelay = 0.04f; a.strikeDuration = 0.16f; a.recovery = 0.3f;
                a.range = 2.6f; a.coneDeg = 70f; a.damage = 13f; a.lungeDistance = 0.7f; a.comboGap = 0.16f;
                // SILHOUETTE, MEASURED on screen at 3.8 m: tilt 10 deg, length 0.89 body-heights,
                // tip 0.92 bh right of centre at 0.20 bh up -- a LEVEL BAR at chest height, out past the
                // enemy's left, with the body barely moving. Smallest read in the moveset; that IS the
                // tell, because it is the fast one.
                // Two earlier passes died here. Cocking the blade forward-down made it VANISH: a blade
                // pointing at the camera has no silhouette and one pointing at the floor is black on a
                // black platform. Only a blade held ACROSS the view has an outline. This one is kept
                // because it MEASURED well, not because the numbers looked right.
                // at hip height, out past the enemy's right hip, with the body barely moving. Smallest
                // read in the moveset, which is the tell: it is the fast one.
                // The first pass cocked the blade forward-down (34, 8, -12) and it VANISHED -- a blade
                // pointing at the camera has no silhouette, and one pointing at the floor is black on a
                // black platform. Only a blade held ACROSS the view has an outline.
                Pose(a, new Vector3(-20f, 60f, -40f), new Vector3(88f, -166f, 149f),
                     new Vector3(0f, -0.06f, -0.08f), new Vector3(2f, 12f, 0f), 0.35f);
            });
            var gruntSlash = Attack("Grunt_Slash", a =>
            {
                a.windup = 0.5f; a.impactDelay = 0.05f; a.strikeDuration = 0.2f; a.recovery = 0.35f;
                a.range = 2.7f; a.coneDeg = 70f; a.damage = 18f; a.lungeDistance = 0.75f; a.comboGap = 0.2f;
                // SILHOUETTE, MEASURED: tilt -45 deg, length 0.88 bh, tip 0.40 bh LEFT of centre at
                // 0.61 bh up -- the MIDDLE rung, a true 45-degree diagonal climbing to the player's left.
                // The jab's bar goes right, this goes left and up: opposite side AND opposite angle.
                // The old value was authored as a diagonal and MEASURED at tilt 87 -- a vertical, i.e.
                // the shape the heavy was supposed to own. Solved against the screen, not the rig.
                Pose(a, new Vector3(-3f, -71f, -69f), new Vector3(38f, -51f, -23f),
                     new Vector3(0.14f, 0.06f, -0.16f), new Vector3(-6f, 20f, 0f), 0.45f);
            });
            var gruntHeavy = Attack("Grunt_Heavy", a =>
            {
                // the rhythm-breaker that ends a chain: slower, hits harder, best deflect value
                a.windup = 0.72f; a.impactDelay = 0.06f; a.strikeDuration = 0.22f; a.recovery = 0.5f;
                a.range = 2.8f; a.coneDeg = 60f; a.damage = 26f; a.lungeDistance = 1.15f;
                a.comboGap = 0.22f; a.parryPostureMultiplier = 1.3f;
                // SILHOUETTE, MEASURED: tilt 88 deg, length 0.87 bh, tip 0.20 bh right of centre at
                // 1.16 bh UP -- a vertical MAST standing more than a body-height above the head, while the
                // body sinks and coils back. Lowest body and highest weapon in the moveset, against the
                // jab's unmoved body and chest-height bar.
                // THE PAIR THAT MATTERS: jab (0.45 s) vs heavy (0.72 s, best deflect value). They are now
                // a horizontal bar and a vertical mast -- told apart by outline alone at three frames.
                // The old value MEASURED at tilt -3, length 0.38 bh: a SHORT HORIZONTAL STUB at head
                // height, i.e. the same family as the jab and half its length. It read as the smallest
                // shape in the moveset when it is meant to be the biggest.
                Pose(a, new Vector3(161f, -109f, -27f), new Vector3(43f, -3f, -18f),
                     new Vector3(0f, -0.20f, -0.34f), new Vector3(-20f, 0f, 0f), 0.55f);
            });
            var heavyStep = Attack("Heavy_Step", a =>
            {
                // an advancing jab so the Heavy can close distance mid-combo instead of whiffing
                a.windup = 0.5f; a.impactDelay = 0.05f; a.strikeDuration = 0.18f; a.recovery = 0.3f;
                a.range = 3.0f; a.coneDeg = 70f; a.damage = 20f; a.lungeDistance = 1.4f; a.comboGap = 0.18f;
                // SILHOUETTE, MEASURED at 4.5 m: tilt -55 deg, length 0.74 bh, tip 0.12 bh left of centre
                // at 0.02 bh -- a steep bar hanging LOW, level with the body's middle. The ONLY wind-up
                // whose body travels TOWARD the player: the read is "it is closing", which is what a step
                // is, and the low blade keeps an outline without competing with the body.
                Pose(a, new Vector3(36f, 56f, 7f), new Vector3(0f, 88f, 32f),
                     new Vector3(0f, -0.10f, 0.22f), new Vector3(10f, 0f, 0f), 0.4f);
            });
            var heavyOverhead = Attack("Heavy_Overhead", a =>
            {
                a.windup = 0.75f; a.recovery = 0.5f; a.range = 3.2f; a.coneDeg = 60f;
                a.damage = 32f; a.lungeDistance = 1.3f; a.comboGap = 0.22f; a.parryPostureMultiplier = 1.3f;
                // SILHOUETTE, MEASURED: tilt 88 deg, length 0.89 bh, tip 0.04 bh LEFT of centre at
                // 1.20 bh UP -- a PURE VERTICAL mast, dead centre, over a body that rises. The tallest
                // shape the Heavy makes.
                // THE PAIR THAT MATTERS: overhead (vertical answer) vs sweep (horizontal answer). The old
                // values MEASURED at tilt 74 / tip (0.36, 0.82) and tilt 63 / tip (0.40, 0.79) -- two
                // steep diagonals landing in the same place, i.e. the two attacks with opposite correct
                // responses looked the same. They are now 88 and 0 degrees.
                // The old overhead also measured 0.51 bh long: it lay back over the head pointing away
                // from the camera and foreshortened to half a blade, and it turned its lit face away, so
                // the biggest-damage attack drew the DARKEST, SHORTEST mark in the set.
                Pose(a, new Vector3(50f, 165f, 114f), new Vector3(45f, 3f, -20f),
                     new Vector3(0f, 0.24f, -0.06f), new Vector3(-14f, 0f, 0f), 0.5f);
            });
            var heavySweep = Attack("Heavy_Sweep", a =>
            {
                a.windup = 0.55f; a.recovery = 0.35f; a.range = 3.1f; a.coneDeg = 110f;
                a.damage = 23f; a.lungeDistance = 0.8f; a.comboGap = 0.2f;
                // SILHOUETTE, MEASURED: tilt 0 deg, length 0.90 bh, tip 0.91 bh right of centre at
                // 0.10 bh -- a PERFECTLY LEVEL BAR at chest height, torso wound 34 degrees. Widest shape
                // the Heavy makes, against the overhead's tallest. Vertical answer vs horizontal answer,
                // told apart by outline alone; see the overhead's note for what these used to measure.
                Pose(a, new Vector3(65f, 146f, -164f), new Vector3(92f, -183f, 129f),
                     new Vector3(0.20f, -0.08f, -0.14f), new Vector3(0f, 34f, 0f), 0.5f);
            });
            var bossSlash = Attack("Boss_Slash", a =>
            {
                a.windup = 0.5f; a.recovery = 0.6f; a.range = 3.9f; a.coneDeg = 80f;
                a.damage = 30f; a.lungeDistance = 1.5f;
                // SILHOUETTE, MEASURED at 5.7 m: tilt -60 deg, length 0.80 bh, tip 0.39 bh LEFT of centre
                // at 0.87 bh up -- a STEEP diagonal climbing to the player's left, high. The highest of
                // the boss's three diagonals; the double-slash pair sits half a body lower and shallower.
                Pose(a, new Vector3(165f, -46f, -58f), new Vector3(28f, -46f, -14f),
                     new Vector3(-0.18f, 0.10f, -0.20f), new Vector3(-8f, -24f, 0f), 0.45f);
            });
            var bossDoubleA = Attack("Boss_DoubleSlash_A", a =>
            {
                a.windup = 0.45f; a.recovery = 0.7f; a.range = 3.9f; a.coneDeg = 80f;
                a.damage = 25f; a.comboGap = 0.15f; a.lungeDistance = 1.3f;
                // SILHOUETTE, MEASURED: tilt +30 deg, length 0.86 bh, tip 0.73 bh RIGHT of centre at
                // 0.36 bh -- a SHALLOW diagonal, mid-height, wound right. Half a body lower and 30 degrees
                // flatter than Boss_Slash, and mirrored by hit B so the pair reads as one right-then-left
                // double rather than two unrelated fast swings.
                // Confusing A with B costs nothing -- they always play in that order. Confusing either
                // with the slam or the thrust costs everything, which is where the budget went.
                Pose(a, new Vector3(-136f, -31f, -112f), new Vector3(-14f, 157f, 26f),
                     new Vector3(0.22f, -0.04f, -0.12f), new Vector3(0f, 30f, 0f), 0.4f);
            });
            var bossDoubleB = Attack("Boss_DoubleSlash_B", a =>
            {
                // was 0.3 — at/below human visual reaction time, so the second hit was unparryable
                a.windup = 0.45f; a.recovery = 0.7f; a.range = 3.9f; a.coneDeg = 80f;
                a.damage = 25f; a.lungeDistance = 1.3f;
                // SILHOUETTE, MEASURED: tilt -30 deg, tip 0.39 bh LEFT of centre at 0.36 bh -- hit A's
                // angle and height, opposite side. Same beat, mirrored.
                Pose(a, new Vector3(-4f, -49f, -51f), new Vector3(-21f, -143f, -7f),
                     new Vector3(-0.22f, -0.04f, -0.12f), new Vector3(0f, -30f, 0f), 0.4f);
            });
            var bossSlam = Attack("Boss_Slam", a =>
            {
                a.windup = 0.9f; a.recovery = 1.1f; a.range = 4.3f; a.coneDeg = 90f;
                a.damage = 45f; a.lungeDistance = 2.2f; a.parryPostureMultiplier = 1.5f;
                // SILHOUETTE, MEASURED: tilt 88 deg, length 0.90 bh, tip 0.12 bh right of centre at
                // 1.17 bh UP -- a vertical MAST over a body lifted and arched back. At 2.2x scale that is
                // the tallest shape in the game, the height of the arena gate.
                // It used to play the cone-derived SWEEP fallback (cone 90) -- the slam telegraphed as a
                // horizontal swing -- and the first authored pass MEASURED at tilt 63, length 0.54 bh:
                // still a diagonal, still half a blade, still in the same family as the two slashes.
                Pose(a, new Vector3(152f, -73f, -18f), new Vector3(38f, -3f, -22f),
                     new Vector3(0f, 0.34f, -0.10f), new Vector3(-24f, 0f, 0f), 0.6f);
            });
            var bossThrust = Attack("Boss_Thrust", a =>
            {
                // unblockable: the only counter is repositioning, so it needs the longest read in the set
                a.windup = 0.85f; a.recovery = 0.9f; a.range = 4.5f; a.coneDeg = 30f;
                a.damage = 55f; a.lungeDistance = 2.9f; a.unblockable = true;
                // SILHOUETTE, MEASURED: tilt -7 deg, length 0.40 bh, tip 0.10 bh left of centre at
                // -0.17 bh -- and every one of those numbers is the opposite of the other four. It is the
                // ONLY boss pose BELOW the body's centre line, the ONLY flat one, and the ONLY short one
                // (0.40 bh against 0.80-0.90), because the blade is aimed down the camera and foreshortens
                // on purpose. The body turns 46 degrees bladed and sinks while RETREATING -- the only
                // wind-up that gets shorter and further away.
                // UNBLOCKABLE: the answer is to MOVE, not to parry, and getting it wrong costs 55. This is
                // the one pose in the whole audit that MEASURED as intended on the first pass, so it is
                // shipped UNCHANGED while the other ten were re-solved against the screen.
                // Low weapon lag on purpose -- this blade stays rigid and pointed, it does not whip.
                // This ADDS to the M_AlertTell marker (peak 3.0); it does not replace it.
                Pose(a, new Vector3(140f, -180f, 90f), new Vector3(-40f, -180f, 90f),
                     new Vector3(0f, -0.34f, -0.62f), new Vector3(0f, 46f, 0f), 0.25f);
            });

            // ---------------- Enemies ----------------
            var grunt = GetOrCreate<EnemyData>(EnemiesDir + "/Grunt.asset");
            grunt.displayName = "Grunt";
            grunt.maxHP = 60f; grunt.maxPosture = 60f; grunt.postureRegen = 6f; grunt.postureRegenDelay = 2.5f; grunt.staggerSeconds = 3f;
            grunt.moveSpeed = 5.6f; grunt.turnSpeed = 400f; grunt.aggroRange = 16f; grunt.attackRange = 2.2f;
            grunt.attackCooldown = 0.15f; grunt.parryRecoilSeconds = 0.42f; grunt.aggression = 0.9f;
            // Weight: quick to reorient but not laser-tracking, a slow deliberate step-in, and a real
            // breath after each combo. Aggression buys pressure; these keep it from reading as twitch.
            grunt.windupTurnMultiplier = 0.32f; grunt.stepSpeedMultiplier = 0.38f;
            grunt.stepAcceleration = 8f; grunt.stepDeadzone = 0.95f;
            grunt.comboBreathSeconds = 0.4f; grunt.readyDistanceMultiplier = 1.8f;
            // Spacing: fights from 3.0m and lunges the last ~0.7-1.15m as it commits.
            grunt.preferredRange = 3.0f; grunt.commitTolerance = 0.6f; grunt.repositionDeadzone = 0.4f;
            grunt.backStepSpeedMultiplier = 0.3f; grunt.strafeSpeedMultiplier = 0.34f; grunt.lungeMinDistance = 1.1f;
            grunt.soulValue = 40;
            // BODY COLOUR IS AN ALBEDO, AND IT IS THE ENEMY'S ONLY SILHOUETTE.
            // EnemyVisuals.WriteBody pushes this into a MaterialPropertyBlock, which OVERRIDES
            // M_Enemy's base colour entirely - so the material's albedo is dead for the body and this
            // is the shipped value (rule 9). Every enemy used to sit around #0A0708, ~0.004 LINEAR
            // reflectance, so a backlit enemy rendered as a flat black CUTOUT: no interior shading, no
            // readable limbs, and the wind-up "darkening" (a lerp to bodyColor * 0.45) was a change
            // from invisible to invisible. All six are now ~4x lifted and given a HUE - cool slate,
            // warm iron, violet - so an enemy separates from the warm ember-lit floor by colour as well
            // as by value. Still non-emissive, still the darkest thing in the frame: light on an enemy
            // means you deflected.
            // The numbers look high for "near-black"; they are not, because these surfaces receive
            // almost no light. Measured on a grunt at 4.5 m against a 36/255 floor: #1E1A20 renders at
            // 1.6/255 (still a cutout), #2E2836 at 4.2, #3C3446 at 8.5, #4A4256 at 14.3. The shipped
            // values land around 8-10/255 - a shape you can read, four times darker than the ground it
            // stands on. Judge an albedo by what it RENDERS as, never by the hex.
            grunt.bodyColor = Hex("#3A3340"); grunt.emission = Hex("#6A0F14") * 1.2f; grunt.scale = 1f;
            // Mixed rhythms so the player cannot settle into one deflect cadence.
            // Authored as a moveset asset; 'combos' is mirrored from it so the runtime path (which reads
            // EnemyData.combos) is byte-identical to before. Weights and ranges are additive information
            // that only takes effect once EnemyController calls EnemyData.SelectCombo.
            grunt.moveset = Moveset("Grunt_Moveset", "Grunt", new[]
            {
                Entry("jab-slash (the common rhythm)",        3f, 0f, 99f, gruntJab, gruntSlash),
                Entry("jab-jab-HEAVY (the signature bait)",   1f, 0f, 99f, gruntJab, gruntJab, gruntHeavy),
                Entry("slash-heavy (slow, slower)",           1.5f, 0f, 99f, gruntSlash, gruntHeavy),
                Entry("jab-slash-jab (fast, slow, fast)",     2f, 0f, 99f, gruntJab, gruntSlash, gruntJab),
            });
            grunt.combos = grunt.moveset.ToComboArray();
            EditorUtility.SetDirty(grunt);

            var heavy = GetOrCreate<EnemyData>(EnemiesDir + "/Heavy.asset");
            heavy.displayName = "Heavy";
            heavy.maxHP = 130f; heavy.maxPosture = 110f; heavy.postureRegen = 5f; heavy.postureRegenDelay = 3f; heavy.staggerSeconds = 3.5f;
            heavy.moveSpeed = 3.9f; heavy.turnSpeed = 260f; heavy.aggroRange = 16f; heavy.attackRange = 2.6f;
            heavy.attackCooldown = 0.3f; heavy.parryRecoilSeconds = 0.5f; heavy.aggression = 0.75f;
            // Heavier still: turns slower mid-swing (circling it is the intended counter), steps in more
            // ponderously, and takes a longer breath between phrases.
            heavy.windupTurnMultiplier = 0.22f; heavy.stepSpeedMultiplier = 0.32f;
            heavy.stepAcceleration = 5.5f; heavy.stepDeadzone = 0.95f;
            heavy.comboBreathSeconds = 0.55f; heavy.readyDistanceMultiplier = 1.7f;
            // Bigger body (1.4x), so it needs more room before its wind-up is legible.
            heavy.preferredRange = 3.6f; heavy.commitTolerance = 0.7f; heavy.repositionDeadzone = 0.45f;
            heavy.backStepSpeedMultiplier = 0.26f; heavy.strafeSpeedMultiplier = 0.28f; heavy.lungeMinDistance = 1.3f;
            heavy.soulValue = 120;
            heavy.bodyColor = Hex("#423630"); heavy.emission = Hex("#8A2A10") * 1.2f; heavy.scale = 1.4f;
            heavy.moveset = Moveset("Heavy_Moveset", "Heavy", new[]
            {
                Entry("sweep-overhead (medium, slow)",              2f, 0f, 99f, heavySweep, heavyOverhead),
                Entry("step-sweep-OVERHEAD (the long phrase)",      1.5f, 0f, 99f, heavyStep, heavySweep, heavyOverhead),
                Entry("sweep-sweep-overhead (tempo then break)",    1.5f, 0f, 99f, heavySweep, heavySweep, heavyOverhead),
                // Step openers exist to CLOSE distance, so they are gated to the far band. Chosen
                // point-blank the advancing jab looks like the enemy is walking through you.
                Entry("step-overhead (closes then commits)",        2f, 2.8f, 99f, heavyStep, heavyOverhead),
            });
            heavy.combos = heavy.moveset.ToComboArray();
            EditorUtility.SetDirty(heavy);

            var boss = GetOrCreate<BossData>(EnemiesDir + "/Boss.asset");
            boss.displayName = "THE HOLLOW WARDEN";
            boss.maxHP = 380f; boss.maxPosture = 220f; boss.postureRegen = 12f; boss.postureRegenDelay = 3f; boss.staggerSeconds = 4f;
            boss.moveSpeed = 5.5f; boss.turnSpeed = 300f; boss.aggroRange = 40f; boss.attackRange = 3.0f;
            boss.attackCooldown = 0.5f; boss.parryRecoilSeconds = 0.6f;
            // Explicitly 0: the boss keeps its authored phase pacing and is NOT swept up in the
            // grunt/heavy aggression pass. Its pressure comes from phases, not from this multiplier.
            boss.aggression = 0f;
            // The boss turns more freely than the grunts (it is the duel, you are meant to face it) but
            // still cannot track a strafe perfectly mid-swing. It fights alone, so the ready-distance and
            // step values barely matter — set explicitly anyway so the asset never carries stale defaults.
            boss.windupTurnMultiplier = 0.45f; boss.stepSpeedMultiplier = 0.35f;
            boss.stepAcceleration = 6f; boss.stepDeadzone = 0.95f;
            boss.comboBreathSeconds = 0.5f; boss.readyDistanceMultiplier = 1.6f;
            // THE duel. At 2.2x scale the old 2.1m stand-off filled the screen and the telegraph could not
            // be read at all. It now holds at 4.6m — far enough to see the whole silhouette wind up — and
            // covers that distance with a big committed lunge on the cue. This is the fix for
            // "the boss just bum rushes and only attacks up close".
            boss.preferredRange = 4.6f; boss.commitTolerance = 0.8f; boss.repositionDeadzone = 0.55f;
            boss.backStepSpeedMultiplier = 0.32f; boss.strafeSpeedMultiplier = 0.3f; boss.lungeMinDistance = 1.8f;
            boss.soulValue = 1500;
            boss.bodyColor = Hex("#40304C"); boss.emission = Hex("#7A1030") * 1.6f; boss.scale = 2.2f;
            boss.segments = 3;
            // The boss picks from its PHASE patterns, not from combos — this is only the fallback used
            // before a phase is applied. Kept as a one-combo moveset for consistency of authoring.
            boss.moveset = Moveset("Boss_Moveset", "The Hollow Warden", new[]
            {
                Entry("slash (fallback)", 1f, 0f, 99f, bossSlash),
            });
            boss.combos = boss.moveset.ToComboArray();
            boss.phases = new[]
            {
                new BossPhase
                {
                    patterns = new[]
                    {
                        new AttackCombo(bossSlash),
                        new AttackCombo(bossDoubleA, bossDoubleB),
                    },
                    windupMultiplier = 1f, speedMultiplier = 1f, accent = Hex("#7A1030") * 1.6f,
                },
                new BossPhase
                {
                    patterns = new[]
                    {
                        new AttackCombo(bossSlash),
                        new AttackCombo(bossDoubleA, bossDoubleB),
                        new AttackCombo(bossSlam),
                        new AttackCombo(bossSlash, bossSlam),
                    },
                    windupMultiplier = 0.9f, speedMultiplier = 1.05f, accent = Hex("#C0521A") * 1.8f,
                },
                new BossPhase
                {
                    patterns = new[]
                    {
                        new AttackCombo(bossDoubleA, bossDoubleB),
                        new AttackCombo(bossSlam),
                        new AttackCombo(bossThrust),
                        new AttackCombo(bossDoubleA, bossDoubleB, bossThrust),
                    },
                    windupMultiplier = 0.8f, speedMultiplier = 1.15f, accent = Hex("#6A1AB0") * 2.0f,
                },
            };
            EditorUtility.SetDirty(boss);

            // ---------------- Legendary mini-bosses ----------------
            // Three named duellists that gate the road to the Hollow Warden. They are ORDINARY
            // EnemyControllers, not BossControllers: BossController owns segments, deathIsStagger, the
            // HUD boss bar and - decisively - RaiseBossDefeated, which stops the speedrun timer and
            // clears the level. A mini-boss that ended the run is not a mini-boss.
            //
            // Every wind-up below is >= 0.45s (hard contract: cueLead 0.28 ~= 0.20 reaction + half the
            // 0.13 perfect window, so a faster wind-up would need its cue to fire before it began).
            // The Ninja is fast through DENSITY - more hits, shorter recoveries, aggression 1.0 - never
            // through a shorter tell.

            // --- Ninja: The Thirteenth Shade. Relentless, low commitment, long strings. -------------
            var ninjaCut = Attack("Ninja_Cut", a =>
            {
                // the metronome. Minimum legal wind-up, minimum recovery: the cadence you learn to ride.
                a.windup = 0.45f; a.impactDelay = 0.04f; a.strikeDuration = 0.14f; a.recovery = 0.22f;
                a.range = 3.0f; a.coneDeg = 75f; a.damage = 12f; a.lungeDistance = 0.9f; a.comboGap = 0.14f;
            });
            var ninjaCross = Attack("Ninja_Cross", a =>
            {
                // the off-beat: same tempo, wider arc, so strafing out is not an answer mid-string
                a.windup = 0.46f; a.impactDelay = 0.04f; a.strikeDuration = 0.14f; a.recovery = 0.22f;
                a.range = 3.05f; a.coneDeg = 100f; a.damage = 13f; a.lungeDistance = 0.9f; a.comboGap = 0.13f;
            });
            var ninjaRush = Attack("Ninja_Rush", a =>
            {
                // the gap-closer. Gated to the far band in the moveset so it never plays point-blank.
                a.windup = 0.48f; a.impactDelay = 0.05f; a.strikeDuration = 0.16f; a.recovery = 0.24f;
                a.range = 3.4f; a.coneDeg = 60f; a.damage = 15f; a.lungeDistance = 2.0f; a.comboGap = 0.16f;
            });
            var ninjaFall = Attack("Ninja_Fall", a =>
            {
                // the rhythm-breaker that ends a string: slower, heavier, best deflect value in the set
                a.windup = 0.62f; a.impactDelay = 0.06f; a.strikeDuration = 0.2f; a.recovery = 0.5f;
                a.range = 3.1f; a.coneDeg = 65f; a.damage = 24f; a.lungeDistance = 1.6f;
                a.comboGap = 0.2f; a.parryPostureMultiplier = 1.5f;
            });
            var ninjaReap = Attack("Ninja_Reap", a =>
            {
                // the perilous sweep. Unblockable, so riding the cadence with the parry held is punished:
                // the counter is footwork, not timing. Longest read the Shade has.
                a.windup = 0.7f; a.impactDelay = 0.06f; a.strikeDuration = 0.22f; a.recovery = 0.6f;
                a.range = 3.3f; a.coneDeg = 140f; a.damage = 28f; a.lungeDistance = 1.4f;
                a.comboGap = 0.24f; a.unblockable = true;
            });

            // --- Knight: The Iron Penitent. THE SPINNING FURNACE. -----------------------------------
            //
            // Redesigned from the slow cleave-and-overhead knight into a sustained-cadence fight. The
            // whole enemy is now one question: can you hold a parry rhythm under continuous pressure?
            // The payoff for holding it is deliberately disproportionate — a full spin deflected is
            // ~78% of his posture bar, and breaking him is a 5.0 s opening straight into the deathblow.
            //
            // Everything below still obeys the readability contract. NO wind-up is under 0.45 s; the
            // cue still fires cueLead (0.28 s) before every single impact. The spin is "fast" through a
            // TIGHT, STEADY BEAT (~0.94 s per hit, the tightest sustained cadence in the game outside
            // the Shade) and short recoveries — never through a shorter tell. A steady beat is also
            // what makes it learnable: the spin is the same interval every time, which is the whole
            // point. Speed that came from shortening the tell would be a reaction test, not a fight.
            var knightSpinUp = Attack("Knight_SpinUp", a =>
            {
                // The spool-up, and the first contact of the spin. 1.15 s of theatre: he is the heavy,
                // so he gets the longest readable wind-up in his set to announce "the cadence starts
                // NOW". The belly furnace flares through it (EnemyVisuals.eye rides Posture.Ratio) —
                // a tell that belongs to him and to nothing else in the game.
                a.windup = 1.15f; a.impactDelay = 0.05f; a.strikeDuration = 0.14f; a.recovery = 0.20f;
                a.range = 4.2f; a.coneDeg = 170f; a.damage = 24f; a.lungeDistance = 0.30f;
                a.comboGap = 0.12f; a.parryPostureMultiplier = 1.3f;
            });
            var knightSpinHit = Attack("Knight_SpinHit", a =>
            {
                // The repeating beat. 0.50 s wind-up (comfortably over the 0.45 s floor) + a 0.10 s
                // floored gap + 0.16 s of strike + a 0.18 s recovery = one hit every ~0.94 s, forever,
                // on the same interval. 170 deg cone so strafing out of a spin is not the answer;
                // 0.45 m of lunge per beat so BACKING OFF is not the answer either — the spin chases.
                a.windup = 0.50f; a.impactDelay = 0.04f; a.strikeDuration = 0.12f; a.recovery = 0.22f;
                a.range = 4.2f; a.coneDeg = 170f; a.damage = 20f; a.lungeDistance = 0.45f;
                a.comboGap = 0.12f; a.parryPostureMultiplier = 1.3f;
            });
            var knightSpinOut = Attack("Knight_SpinOut", a =>
            {
                // The exit, and the REWARD. Heavier than a beat, worth more on the deflect, and then
                // 2.2 s of authored recovery (~1.4 s after aggression) — by a wide margin the biggest
                // punish window he offers, and the reason surviving a spin is worth surviving.
                a.windup = 0.55f; a.impactDelay = 0.05f; a.strikeDuration = 0.16f; a.recovery = 2.2f;
                a.range = 4.4f; a.coneDeg = 175f; a.damage = 28f; a.lungeDistance = 0.40f;
                a.comboGap = 0.30f; a.parryPostureMultiplier = 1.6f;
            });
            var knightShove = Attack("Knight_Shove", a =>
            {
                // the only quick thing it does, and it exists purely to close distance
                a.windup = 0.6f; a.impactDelay = 0.05f; a.strikeDuration = 0.2f; a.recovery = 0.55f;
                a.range = 3.6f; a.coneDeg = 60f; a.damage = 20f; a.lungeDistance = 2.2f; a.comboGap = 0.3f;
            });
            var knightOverhead = Attack("Knight_Overhead", a =>
            {
                // Kept from the old Penitent, now a rare TEMPO BREAK rather than his bread and butter:
                // one full second of wind-up dropped into a fight whose every other beat is 0.94 s. A
                // fight that is only the spin is one-note, and this is the beat that punishes a player
                // who has stopped watching and is parrying on the metronome.
                a.windup = 1.0f; a.impactDelay = 0.07f; a.strikeDuration = 0.26f; a.recovery = 1.05f;
                a.range = 3.7f; a.coneDeg = 60f; a.damage = 46f; a.lungeDistance = 1.8f;
                a.comboGap = 0.35f; a.parryPostureMultiplier = 1.9f;
            });
            var knightVent = Attack("Knight_Vent", a =>
            {
                // THE ANTI-CAMP. The furnace vents: a wide unblockable pulse out to 8 m, gated in the
                // moveset to the far band only. It exists so that "back off and wait the spin out" is
                // not the optimal line — retreating past his reach is what SELECTS this move, and it
                // cannot be parried, only walked out of. Replaces Knight_Quake, which did the same job
                // at 4.4 m, i.e. only to a player who was already standing in the spin.
                a.windup = 1.1f; a.impactDelay = 0.08f; a.strikeDuration = 0.3f; a.recovery = 1.6f;
                a.range = 8.0f; a.coneDeg = 175f; a.damage = 34f; a.lungeDistance = 0f;
                a.comboGap = 0.45f; a.unblockable = true;
            });

            // --- Spellsword: The Ashen Chorister. Champion-Gundyr shaped. ---------------------------
            var swordArc = Attack("Spellsword_Arc", a =>
            {
                a.windup = 0.5f; a.impactDelay = 0.05f; a.strikeDuration = 0.18f; a.recovery = 0.45f;
                a.range = 4.0f; a.coneDeg = 95f; a.damage = 24f; a.lungeDistance = 1.2f; a.comboGap = 0.18f;
            });
            var swordThrust = Attack("Spellsword_Thrust", a =>
            {
                a.windup = 0.55f; a.impactDelay = 0.05f; a.strikeDuration = 0.18f; a.recovery = 0.5f;
                a.range = 4.0f; a.coneDeg = 45f; a.damage = 26f; a.lungeDistance = 2.0f; a.comboGap = 0.2f;
            });
            var swordFeint = Attack("Spellsword_Feint", a =>
            {
                // THE signature. A heavy that reads exactly like a phrase-ending overhead, then hands the
                // combo a 0.55s gap - long enough to feel like a breath - before the string continues.
                // Short recovery + long comboGap is what makes the pause a lie rather than an opening.
                a.windup = 0.9f; a.impactDelay = 0.06f; a.strikeDuration = 0.22f; a.recovery = 0.35f;
                a.range = 3.9f; a.coneDeg = 85f; a.damage = 30f; a.lungeDistance = 1.6f;
                a.comboGap = 0.55f; a.parryPostureMultiplier = 1.5f;
            });
            var swordGrasp = Attack("Spellsword_Grasp", a =>
            {
                // the grab. Unblockable, longest lunge in the game - it exists to punish the player who
                // stepped in during the feint's fake breath.
                a.windup = 0.95f; a.impactDelay = 0.07f; a.strikeDuration = 0.24f; a.recovery = 1.1f;
                a.range = 4.8f; a.coneDeg = 35f; a.damage = 58f; a.lungeDistance = 3.0f;
                a.comboGap = 0.3f; a.unblockable = true;
            });
            var swordEmberfall = Attack("Spellsword_Emberfall", a =>
            {
                // the ranged opener: a wide unblockable pulse thrown from outside melee. It cannot be
                // parried, so it forces a reposition and turns the fight into a spacing problem before
                // it is a timing one. Gated to the far band so it is never a point-blank surprise.
                a.windup = 1.0f; a.impactDelay = 0.08f; a.strikeDuration = 0.3f; a.recovery = 0.85f;
                a.range = 7.0f; a.coneDeg = 170f; a.damage = 36f; a.lungeDistance = 0f;
                a.comboGap = 0.45f; a.unblockable = true;
            });

            // --- EnemyData: The Thirteenth Shade ----------------------------------------------------
            var ninja = GetOrCreate<EnemyData>(EnemiesDir + "/Legendary_Ninja.asset");
            ninja.displayName = "THE THIRTEENTH SHADE";
            ninja.maxHP = 150f; ninja.maxPosture = 120f; ninja.postureRegen = 7f;
            ninja.postureRegenDelay = 2.5f; ninja.staggerSeconds = 3.2f;
            ninja.moveSpeed = 6.4f; ninja.turnSpeed = 430f; ninja.aggroRange = 18f; ninja.attackRange = 2.4f;
            ninja.attackCooldown = 0.1f; ninja.parryRecoilSeconds = 0.34f; ninja.aggression = 1f;
            // Maximum aggression, but the weight values keep it a duel and not a blender: it still cannot
            // track a strafe mid-swing, and comboBreathSeconds is a floor aggression may not compress away.
            ninja.windupTurnMultiplier = 0.36f; ninja.stepSpeedMultiplier = 0.5f;
            ninja.stepAcceleration = 10f; ninja.stepDeadzone = 0.95f;
            ninja.comboBreathSeconds = 0.3f; ninja.readyDistanceMultiplier = 1.8f;
            // Small and quick, so it can fight closer than the Heavy without filling the screen.
            ninja.preferredRange = 3.2f; ninja.commitTolerance = 0.7f; ninja.repositionDeadzone = 0.4f;
            ninja.backStepSpeedMultiplier = 0.4f; ninja.strafeSpeedMultiplier = 0.46f; ninja.lungeMinDistance = 1.05f;
            ninja.soulValue = 400;
            ninja.bodyColor = Hex("#2E363C"); ninja.emission = Hex("#1FBFA8") * 1.4f; ninja.scale = 0.95f;
            ninja.moveset = Moveset("Legendary_Ninja_Moveset", "The Thirteenth Shade", new[]
            {
                Entry("cut-cut-cut (the cadence you learn to ride)",   3f,   0f,   99f, ninjaCut, ninjaCut, ninjaCut),
                Entry("cut-cross-cut-FALL (ride it, then it breaks)",  2.5f, 0f,   99f, ninjaCut, ninjaCross, ninjaCut, ninjaFall),
                Entry("cut-cross-REAP (the cadence baits the sweep)",  1.2f, 0f,   99f, ninjaCut, ninjaCross, ninjaReap),
                Entry("rush-cut-cut (closes, then flurries)",          2f,   3.4f, 99f, ninjaRush, ninjaCut, ninjaCut),
                Entry("cut-cut-cross-cut-FALL (the long string)",      1f,   0f,   99f, ninjaCut, ninjaCut, ninjaCross, ninjaCut, ninjaFall),
            });
            ninja.combos = ninja.moveset.ToComboArray();
            EditorUtility.SetDirty(ninja);

            // --- EnemyData: The Iron Penitent -------------------------------------------------------
            var knight = GetOrCreate<EnemyData>(EnemiesDir + "/Legendary_Knight.asset");
            knight.displayName = "THE IRON PENITENT";
            // POSTURE 260, not 190. He now throws far more parryable hits per fight than anything else
            // in the game, so the bar has to be long enough that surviving one spin is progress rather
            // than the whole fight. With the sword (parryPostureDamage 25) a spin beat deflected is
            // 25 x 1.3 = 32.5 and the spin-out is 25 x 1.6 = 40, so a clean six-hit spin is 202.5 of
            // 260 — 78% — and SEVEN clean deflects break him from full. That is the economy: the reward
            // for holding the cadence is huge, and blocking instead of deflecting is worth exactly zero
            // enemy posture (only ParryResult.Perfect calls OnParried), so it must be real deflects.
            knight.maxHP = 260f; knight.maxPosture = 260f; knight.postureRegen = 3.5f;
            // 5.0 s of stagger: the longest in the game, and ~3.5x his own biggest recovery. This is the
            // payoff the whole design points at — break him and you have all the time you need to walk
            // in and take the deathblow (the sword's executeDamage 300 > his 260 HP, so it ends him).
            knight.postureRegenDelay = 4f; knight.staggerSeconds = 5f;
            knight.moveSpeed = 3.8f; knight.turnSpeed = 240f; knight.aggroRange = 18f; knight.attackRange = 3.4f;
            knight.attackCooldown = 0.5f; knight.parryRecoilSeconds = 0.75f; knight.aggression = 0.55f;
            // Aggression 0.55, up from 0.35. It compresses recovery (x0.64) and the combo gap, which is
            // what makes the spin a cadence instead of a series of separate swings — but deliberately
            // NOT 1.0, because the same multiplier would crush the spin-out window that is the reward.
            // He turns better during a wind-up now (0.35): a spinning thing that cannot track you at all
            // makes strafing a free answer, and the spin is supposed to be parried, not walked around.
            knight.windupTurnMultiplier = 0.35f; knight.stepSpeedMultiplier = 0.26f;
            knight.stepAcceleration = 4.5f; knight.stepDeadzone = 0.95f;
            // comboBreathSeconds is a FLOOR on every recovery, mid-combo ones included, so the old 0.85
            // made a cadence impossible: it put a near-second of dead air between every beat. At 0.18 the
            // beat closes up, and the punish window is authored where it belongs instead — on the recovery
            // of Knight_SpinOut, which ends every spin.
            knight.comboBreathSeconds = 0.18f; knight.readyDistanceMultiplier = 1.6f;
            // 1.6x scale at 4.2 m: preferredRange >= attackRange (3.4), and far enough out that a
            // spinning 3.2 m silhouette is fully on screen instead of filling it.
            knight.preferredRange = 4.2f; knight.commitTolerance = 0.8f; knight.repositionDeadzone = 0.5f;
            // lungeMinDistance 3.0, up from 1.5, and the spin's per-beat lunge cut to 0.45 m. A six-beat
            // spin that lunged 0.9 m a beat walked him from 4.2 m to 1.5 m over one phrase and left a
            // 3.2 m silhouette filling the screen, where a wind-up cannot be read at all — the exact
            // failure the preferredRange note warns about. He now cannot park closer than 3.0 m, and
            // backStep is 0.35 rather than 0.2 so he actually resets his spacing between phrases.
            knight.backStepSpeedMultiplier = 0.35f; knight.strafeSpeedMultiplier = 0.2f; knight.lungeMinDistance = 3f;
            knight.soulValue = 600;
            knight.bodyColor = Hex("#443A34"); knight.emission = Hex("#C0521A") * 1.4f; knight.scale = 1.6f;
            knight.moveset = Moveset("Legendary_Knight_Moveset", "The Iron Penitent", new[]
            {
                // The long spin is the signature and the most common thing he does: spool up, four beats
                // on the metronome, then the exit that hands you the window. ~5.8 s of held cadence.
                Entry("SPIN-UP-beat-beat-beat-beat-OUT (the cadence)", 3.5f, 0f,   99f, knightSpinUp, knightSpinHit, knightSpinHit, knightSpinHit, knightSpinHit, knightSpinOut),
                // The short spin. Same beat, fewer of them - so the length of a spin is not predictable
                // and you cannot count your way to the window without watching for the exit.
                Entry("short spin (same beat, three of them)",         2f,   0f,   99f, knightSpinUp, knightSpinHit, knightSpinHit, knightSpinOut),
                Entry("shove into the SPIN (closes, then whirls)",     2f,   3.6f, 99f, knightShove, knightSpinUp, knightSpinHit, knightSpinHit, knightSpinHit, knightSpinOut),
                // The tempo break. One 1.0 s wind-up in a fight of 0.94 s beats, to punish parrying on
                // the metronome instead of on the tell.
                Entry("OVERHEAD (the tempo break)",                    1.2f, 0f,   99f, knightOverhead),
                // Far band only: these are what a player who backed out of spin range gets instead.
                Entry("FURNACE VENT (punishes waiting it out)",        1.4f, 5f,   99f, knightVent),
                Entry("VENT-shove-SPIN (vents you back in, then spins)", 0.9f, 5.5f, 99f, knightVent, knightShove, knightSpinUp, knightSpinHit, knightSpinHit, knightSpinOut),
            });
            knight.combos = knight.moveset.ToComboArray();
            EditorUtility.SetDirty(knight);

            // --- EnemyData: The Ashen Chorister -----------------------------------------------------
            var spellsword = GetOrCreate<EnemyData>(EnemiesDir + "/Legendary_Spellsword.asset");
            spellsword.displayName = "THE ASHEN CHORISTER";
            spellsword.maxHP = 300f; spellsword.maxPosture = 210f; spellsword.postureRegen = 8f;
            spellsword.postureRegenDelay = 3f; spellsword.staggerSeconds = 3.6f;
            spellsword.moveSpeed = 5.2f; spellsword.turnSpeed = 320f; spellsword.aggroRange = 22f; spellsword.attackRange = 3f;
            spellsword.attackCooldown = 0.25f; spellsword.parryRecoilSeconds = 0.5f; spellsword.aggression = 0.7f;
            spellsword.windupTurnMultiplier = 0.3f; spellsword.stepSpeedMultiplier = 0.42f;
            spellsword.stepAcceleration = 7f; spellsword.stepDeadzone = 0.95f;
            spellsword.comboBreathSeconds = 0.45f; spellsword.readyDistanceMultiplier = 1.75f;
            // Fights from further out than anything but the Warden - it wants the ranged opener to be a
            // real option, and Emberfall is gated to that band in the moveset.
            spellsword.preferredRange = 4.3f; spellsword.commitTolerance = 0.8f; spellsword.repositionDeadzone = 0.5f;
            spellsword.backStepSpeedMultiplier = 0.36f; spellsword.strafeSpeedMultiplier = 0.34f; spellsword.lungeMinDistance = 1.6f;
            spellsword.soulValue = 900;
            spellsword.bodyColor = Hex("#3A3050"); spellsword.emission = Hex("#8A2ADF") * 1.5f; spellsword.scale = 1.5f;
            spellsword.moveset = Moveset("Legendary_Spellsword_Moveset", "The Ashen Chorister", new[]
            {
                Entry("arc-thrust (the plain rhythm it teaches you)",  2.5f, 0f,   99f, swordArc, swordThrust),
                Entry("thrust-arc-arc (pressure)",                     2f,   0f,   99f, swordThrust, swordArc, swordArc),
                Entry("FEINT...arc-thrust (the pause is a lie)",       1.8f, 0f,   99f, swordFeint, swordArc, swordThrust),
                Entry("arc-GRASP (punishes stepping in)",              1.2f, 3f,   99f, swordArc, swordGrasp),
                Entry("EMBERFALL-thrust (ranged opener, then closes)", 1.2f, 4.5f, 99f, swordEmberfall, swordThrust),
                Entry("arc-thrust-FEINT-arc (the long unreadable one)",1f,   0f,   99f, swordArc, swordThrust, swordFeint, swordArc),
            });
            spellsword.combos = spellsword.moveset.ToComboArray();
            EditorUtility.SetDirty(spellsword);

            // --- Marionette: THE PALE MARIONETTE. PROTOTYPE. ----------------------------------------
            //
            // A Mad-Clown-Puppet-shaped fight: the body whirls continuously and you deflect EVERY PASS,
            // holding a rhythm rather than reading discrete telegraphs. It is deliberately NOT wired
            // into Level_01 — it exists on a sandbox pad only. See docs/ARCHITECTURE.md.
            //
            // ===== THE ONE DESIGN PROBLEM AND ITS RESOLUTION =====================================
            // "Spin really fast" and "no wind-up below 0.45 s" cannot both be satisfied by one number,
            // because the parry cue fires cueLead (0.28 s) before impact and a faster wind-up would
            // need it to fire before the wind-up began. THE VISUAL SPIN RATE AND THE HIT CADENCE ARE
            // THEREFORE DIFFERENT QUANTITIES. The body's peak rate is ~9 revolutions per second
            // (PuppetVisuals.spinPeakMultiple, pure presentation, no timing attached); the damaging
            // passes arrive every 0.69 s, from a 0.45 s wind-up. One revolution still equals one pass:
            // the whirl is non-uniform, blurring through the back of the turn and DECELERATING into
            // the player, so the slow-down is the wind-up and the cue lands as it comes around.
            //
            // ===== THE BEAT, AND WHY IT CANNOT DRIFT =============================================
            // Unparried beat = windup(0.45) + gap(0.10) + impactDelay(0.04) + strike(0.10) = 0.69 s.
            // Parried beat   = recoil + windup(0.45) + gap(0.10) + impactDelay(0.04).
            // A deflect routes through EnemyController.OnParried -> Recover(parryRecoilSeconds x
            // lerp(1,0.55,aggression)) -> ResumeCombo, so the two are only equal if the recoil is
            // sized to STAND IN FOR THE STRIKE. Which collapses to one identity worth stating plainly,
            // because it is the thing every future retune of this fight has to preserve:
            //
            //     parryRecoilSeconds  ==  strikeDuration / lerp(1, 0.55, aggression)
            //
            // At aggression 0.62 the multiplier is 0.721, so 0.10 / 0.721 = 0.1387 — which is why the
            // number below is 0.1387 and not a round one. A fight whose tempo changes depending on
            // whether you succeeded is a fight nobody can learn, so this is derived, never felt.
            // Change strikeDuration or aggression and you MUST re-run that division;
            // MarionetteDataTests.ParriedAndUnparriedBeat_AreEqual fails loudly if you do not.
            // (Residual: a perfect parry may land up to parryPerfectWindow / 2 early, so the beat can
            // be pulled in by <= 0.065 s. Bounded, player-caused, and it rewards parrying
            // late-in-window rather than punishing anything.)
            // The gap is ALSO drift-proof by construction: NextGap floors at 0.10 s and
            // 0.12 x 0.721 = 0.087 is already UNDER the floor, so the floor is what binds and neither
            // aggression nor the parry streak (which subtracts a further 0.03 per deflect) can
            // compress it. The beat is identical on pass 1 and on pass 9 of a nine-pass phrase, which
            // is the only reason a nine-pass phrase is learnable at all. Asserted by
            // MarionetteDataTests.TheGapIsPinnedToTheFloor.
            var marSpinPass = Attack("Marionette_SpinPass", a =>
            {
                // THE BEAT, AND IT IS NOW AT THE FLOOR. 0.45 s wind-up is exactly the project-wide
                // minimum, which makes 0.69 s THE FASTEST PARRY CADENCE THIS GAME CAN LEGALLY ASK FOR.
                // That is not a tuning preference, it is arithmetic: the cue fires cueLead (0.28 s)
                // before impact, and impact is windup + impactDelay, so a wind-up under ~0.24 s would
                // need the cue to fire before the wind-up existed and the pass would stop being
                // parryable at all. 0.45 keeps a real charge phase (0.21 s of wind-up before the cue
                // even lands) on top of that. If a future session wants the passes closer together
                // than 0.69 s, the honest lever is parryPerfectWindow in PlayerStatsData — a GLOBAL
                // change affecting every enemy in the game — and not this number.
                // At 0.45 the cue lands with the body ~63 deg off and visibly slowing (was ~85 at
                // 0.50; the arrival curve is steeper now, see PuppetVisuals.Ease). 200 deg cone
                // because a thing with a 1.75 m arm span coming around at you is not something you
                // sidestep.
                a.windup = 0.45f; a.impactDelay = 0.04f; a.strikeDuration = 0.10f; a.recovery = 0.20f;
                // lungeDistance 0.60, not 0.35. THE SPIN HAS TO CHASE. Measured in the sandbox: after
                // the overhead's knockback put the player 7.4 m out, a 0.35 m pass closed the gap at
                // roughly 0.1 m per beat, so the puppet spent the rest of a seven-second phrase
                // whirling harmlessly out of reach while the player walked away. Backing off must cost
                // something, and the far-band lash cannot answer it because the moveset only reselects
                // between phrases. At 0.60 m a pass the spin walks 4.8 m over an eight-pass phrase and
                // is genuinely on top of you again by the exit. lungeMinDistance (2.8 on the data)
                // still stops it burrowing in.
                a.range = 3.9f; a.coneDeg = 200f; a.damage = 17f; a.lungeDistance = 0.60f;
                a.comboGap = 0.12f; a.parryPostureMultiplier = 1.4f;
            });
            var marSpinUp = Attack("Marionette_SpinUp", a =>
            {
                // The spool-up: the strings go taut and it starts to turn. Longest read in the SPIN, so
                // "the spin is starting" is never a surprise. Same name prefix as the beat, so
                // PuppetVisuals whirls on it too — the first revolution is the slowest one.
                // 0.80, down from 0.95. Its impactDelay and strikeDuration are deliberately IDENTICAL
                // to the pass's, which makes the spool-up -> first-pass interval
                // 0.10 + 0.04 + 0.10 + 0.45 = 0.69 s — the cadence exactly. The player is therefore
                // locked onto the metronome from beat ONE rather than having to find it on beat two;
                // the only thing the longer wind-up buys is the warning, and it should not also cost
                // the player their footing in the rhythm.
                a.windup = 0.80f; a.impactDelay = 0.04f; a.strikeDuration = 0.10f; a.recovery = 0.20f;
                a.range = 3.9f; a.coneDeg = 200f; a.damage = 18f; a.lungeDistance = 0.55f;
                a.comboGap = 0.12f; a.parryPostureMultiplier = 1.4f;
            });
            var marSpinOut = Attack("Marionette_SpinOut", a =>
            {
                // THE EXIT, AND THE OUT THAT IS NOT PARRYING. It over-rotates, unwinds, and hangs on
                // its strings for 2.0 s of authored recovery (~1.4 s after aggression) — by a wide
                // margin the biggest punish window it offers. A player who cannot hold the rhythm can
                // simply BLOCK the passes (block costs stamina and zero enemy posture, so it does not
                // progress the break) and cash the spin-out for damage instead. Two routes to the same
                // corpse: deflect it to death by posture, or tank it and out-damage it. Its 170 HP —
                // low for a duellist — is what makes the second route real.
                // windup 0.65, and the 0.21 s it adds to the beat is THE POINT. The exit arrives
                // 0.90 s after the last pass instead of 0.69 s, so a player parrying the metronome
                // presses 0.21 s early — outside parryPerfectWindow (0.13) but comfortably inside the
                // block window (0.13 + 0.12 = 0.25). Metronome play therefore BLOCKS the exit instead
                // of deflecting it: it costs you the posture and the punish, and it does not cost you
                // 26 health. That is the fight's thesis in one beat — parry the body, not the count —
                // charged at exactly the right price. Pinned by
                // MarionetteDataTests.TheExitBreaksTheMetronome_ButOnlyIntoABlock.
                a.windup = 0.65f; a.impactDelay = 0.05f; a.strikeDuration = 0.18f; a.recovery = 2.0f;
                a.range = 4.1f; a.coneDeg = 200f; a.damage = 26f; a.lungeDistance = 0.55f;
                a.comboGap = 0.30f; a.parryPostureMultiplier = 1.6f;
            });
            var marOverhead = Attack("Marionette_Overhead", a =>
            {
                // THE TEMPO BREAK. Deliberately NOT named with the spin prefix, so PuppetVisuals plays
                // it square-on with no whirl at all: the body stopping IS the tell. One 1.0 s wind-up
                // dropped into a fight of 0.76 s beats, to punish a player parrying on the metronome
                // instead of on the body.
                a.windup = 1.0f; a.impactDelay = 0.07f; a.strikeDuration = 0.24f; a.recovery = 1.0f;
                // range 3.7 = preferredRange exactly, so the overhead reaches WITHOUT relying on its
                // own lunge to bail it out. Caught by MarionetteDataTests at 3.4: it landed in play
                // only because 1.7 m of lunge closed the gap first, which is a coincidence, not a
                // design. The narrow 65 deg cone is what keeps it distinct from the 200 deg passes.
                a.range = 3.7f; a.coneDeg = 65f; a.damage = 40f; a.lungeDistance = 1.7f;
                a.comboGap = 0.35f; a.parryPostureMultiplier = 1.9f;
            });
            var marLash = Attack("Marionette_Lash", a =>
            {
                // THE ANTI-CAMP, same job as Knight_Vent. A wide unblockable string-lash out to 8 m,
                // gated in the moveset to the FAR band only. It exists so "back off and wait the spin
                // out" is never the optimal line: retreating past its reach is precisely what SELECTS
                // this move, and it cannot be parried, only walked out of.
                a.windup = 1.05f; a.impactDelay = 0.08f; a.strikeDuration = 0.3f; a.recovery = 1.5f;
                a.range = 8.0f; a.coneDeg = 175f; a.damage = 30f; a.lungeDistance = 0f;
                a.comboGap = 0.45f; a.unblockable = true;
            });

            // --- EnemyData: The Pale Marionette -----------------------------------------------------
            var marionette = GetOrCreate<EnemyData>(EnemiesDir + "/Legendary_Marionette.asset");
            marionette.displayName = "THE PALE MARIONETTE";
            // ===== THE POSTURE ECONOMY: SIX CLEAN DEFLECTS BREAK IT =============================
            // It breaks EARLY on a deflect chain rather than running a fixed number of revolutions,
            // and it does so through the ORDINARY posture system with no special case. With the sword
            // (parryPostureDamage 25) a deflected pass is 25 x 1.4 = 35, so 6 x 35 = 210 = the whole
            // bar. The spin phrase is EIGHT passes long, so a clean player breaks it two passes before
            // it would have ended on its own and a sloppy one has to survive the whole thing — the
            // player's rhythm, not a script, decides how long the spin lasts. A fixed revolution count
            // would have made skill irrelevant to the outcome, which is the opposite of the reference
            // fight, where deflecting IS the offence.
            marionette.maxHP = 170f; marionette.maxPosture = 210f; marionette.postureRegen = 6f;
            // regenDelay 3.5 s is longer than four beats, so posture never regenerates mid-spin and the
            // six deflects do not have to be consecutive within one revolution to count.
            marionette.postureRegenDelay = 3.5f; marionette.staggerSeconds = 4f;
            marionette.moveSpeed = 5f; marionette.turnSpeed = 300f; marionette.aggroRange = 18f;
            marionette.attackRange = 3.2f;
            marionette.attackCooldown = 0.35f;
            // 0.1387 x lerp(1, 0.55, 0.62) = 0.1000 s = the pass's strikeDuration exactly. See the
            // identity in the beat arithmetic above — this number is DERIVED, not tuned by feel, and
            // changing either strikeDuration or aggression means re-running the division.
            marionette.parryRecoilSeconds = 0.1387f; marionette.aggression = 0.62f;
            // Low windup turn: a whirling thing that tracked you perfectly would make the arc
            // unavoidable AND unreadable. It commits its facing and the arc is wide enough (200 deg)
            // that stepping out is a real but not free answer.
            marionette.windupTurnMultiplier = 0.28f; marionette.stepSpeedMultiplier = 0.3f;
            marionette.stepAcceleration = 6f; marionette.stepDeadzone = 0.95f;
            // comboBreathSeconds is a FLOOR on every recovery including mid-combo ones, so it must sit
            // UNDER the 0.20 s authored recovery or it would silently stretch the beat and make the
            // 0.76 s arithmetic above a lie.
            marionette.comboBreathSeconds = 0.18f; marionette.readyDistanceMultiplier = 1.6f;
            // 3.7 m at 1.15x scale: preferredRange >= attackRange, and far enough that a 1.75 m arm
            // span whirling at 4.5 rev/s is a legible silhouette rather than a screenful of noise.
            marionette.preferredRange = 3.7f; marionette.commitTolerance = 0.7f;
            marionette.repositionDeadzone = 0.45f;
            // lungeMinDistance 2.8: nine passes of 0.60 m would otherwise walk it from 3.7 m to well
            // inside contact over one phrase, exactly the failure the preferredRange note warns about.
            marionette.backStepSpeedMultiplier = 0.34f; marionette.strafeSpeedMultiplier = 0.26f;
            marionette.lungeMinDistance = 2.8f;
            marionette.soulValue = 550;
            // Bone-white body, cold porcelain glow — it is a puppet, not a furnace. The eye/lantern
            // rides Posture.Ratio like every other enemy, so it burns hotter as the break approaches.
            marionette.bodyColor = Hex("#D8D2C4"); marionette.emission = Hex("#4FE0D0") * 1.5f;
            marionette.scale = 1.15f;
            marionette.moveset = Moveset("Legendary_Marionette_Moveset", "The Pale Marionette", new[]
            {
                // THE SIGNATURE, and by far the most common thing it does: spool up, then NINE passes
                // on an unwavering 0.69 s beat, then the exit. Nine, up from eight, because the faster
                // beat would otherwise have made the phrase SHORTER — 8 x 0.69 is 5.5 s where 8 x 0.76
                // was 6.1 s — and "more parries" was the point, not "the same fight over quicker".
                // At nine the whole phrase is ~7.9 s wall-clock, within a tenth of what it was, so the
                // change reads as a denser rhythm rather than a shorter one. Nine still sits against
                // the posture economy above: six clean deflects break it, so the phrase is now THREE
                // passes longer than a perfect player needs.
                // maxRange 6 on every spin phrase, not 99. A phrase is chosen ONCE and then runs to its
                // end, so a spin selected from 8 m is nine passes of whirling at nothing — measured,
                // not theorised. 6 m is preferredRange (3.7) plus commitTolerance (0.7) plus room for
                // the player to have backed off a step, and it is under the lash's 5 m floor by enough
                // that the two bands genuinely overlap rather than leaving a dead zone.
                Entry("SPIN-UP + 9 passes + OUT (the cadence)",        4f,   0f,   6f,
                      marSpinUp, marSpinPass, marSpinPass, marSpinPass, marSpinPass, marSpinPass,
                      marSpinPass, marSpinPass, marSpinPass, marSpinPass, marSpinOut),
                // The short spin: same beat, fewer passes, so the LENGTH of a spin is not predictable
                // and you cannot count your way to the exit without watching for it. Five rather than
                // four keeps it just under the six needed to break — so the short spin is the one that
                // can NEVER be broken through, and a player who has learned to count is still made to
                // watch for the exit.
                Entry("short spin (same beat, five passes)",           2f,   0f,   6f,
                      marSpinUp, marSpinPass, marSpinPass, marSpinPass, marSpinPass, marSpinPass,
                      marSpinOut),
                // The tempo break. One square-on 1.0 s wind-up, no whirl at all.
                Entry("OVERHEAD (the tempo break)",                    1.2f, 0f,   99f, marOverhead),
                Entry("overhead into the spin",                        1f,   0f,   6f,
                      marOverhead, marSpinUp, marSpinPass, marSpinPass, marSpinPass, marSpinOut),
                // Far band only: what a player who backed out of spin range gets instead of a rest.
                Entry("STRING LASH (punishes waiting it out)",         1.6f, 5f,   99f, marLash),
                Entry("LASH into the spin (drags you back in)",        1f,   5.5f, 99f,
                      marLash, marSpinUp, marSpinPass, marSpinPass, marSpinPass, marSpinPass, marSpinOut),
            });
            marionette.combos = marionette.moveset.ToComboArray();
            EditorUtility.SetDirty(marionette);

            // ---------------- Weapons ----------------
            var sword = GetOrCreate<WeaponData>(WeaponsDir + "/Sword.asset");
            sword.displayName = "Cerulean Edge";
            sword.neon = Hex("#8FB5D9");
            sword.baseDamage = 22f; sword.postureDamage = 12f; sword.executeDamage = 300f;
            sword.strScale = 0.5f; sword.dexScale = 0.5f; sword.arcScale = 0.2f;
            sword.comboLength = 3; sword.comboMultipliers = new[] { 1f, 1f, 1.5f };
            sword.attackDuration = 0.38f; sword.hitDelay = 0.12f; sword.comboWindow = 0.35f;
            sword.hitOffset = 1.6f; sword.hitRadius = 1.1f; sword.hitStopSeconds = 0.05f;
            sword.parryWindowMultiplier = 1f; sword.parryPostureDamage = 25f; sword.pyreBonus = 0f;
            // SUPER "EMBERFALL ARC" — one enormous horizontal sweep. The sword is the generalist, so its
            // super is the plain, honest one: a single legible beat that hits everything in front of you.
            sword.superName = "Emberfall Arc"; sword.superKind = SuperKind.Cleave;
            sword.superDamage = 150f; sword.superPostureDamage = 70f;
            sword.superRadius = 5.5f; sword.superArcDeg = 170f; sword.superHits = 1;
            sword.superWindup = 0.30f; sword.superActive = 0.14f; sword.superRecover = 0.30f;
            sword.superHitStop = 0.10f; sword.superShake = 0.45f; sword.superKnockback = 2.5f;
            // VIEWMODEL SCALE (rule 9: written here or it never reaches the asset). The whole set is
            // dagger-scale now; the sword is the longest of the four at 0.32 m above the fist.
            sword.viewmodelScale = 0.52f;
            ResetPosesToDefaults(sword);
            EditorUtility.SetDirty(sword);

            var hammer = GetOrCreate<WeaponData>(WeaponsDir + "/Hammer.asset");
            hammer.displayName = "Sunbreaker";
            hammer.neon = Hex("#E0661A");
            hammer.baseDamage = 40f; hammer.postureDamage = 30f; hammer.executeDamage = 400f;
            hammer.strScale = 1f; hammer.dexScale = 0f; hammer.arcScale = 0.2f;
            hammer.comboLength = 2; hammer.comboMultipliers = new[] { 1f, 1.6f };
            hammer.attackDuration = 0.7f; hammer.hitDelay = 0.3f; hammer.comboWindow = 0.5f;
            hammer.hitOffset = 1.8f; hammer.hitRadius = 1.4f; hammer.hitStopSeconds = 0.09f;
            hammer.parryWindowMultiplier = 0.8f; hammer.parryPostureDamage = 40f; hammer.pyreBonus = 5f;
            // SUPER "SUNBREAK" — overhead into the ground, 360 degree shockwave. The longest wind-up in
            // the set and by far the biggest posture number: the hammer already trades speed for weight,
            // and its super doubles down rather than apologising for it. Nothing else knocks enemies back
            // 6 metres or shakes the camera this hard.
            hammer.superName = "Sunbreak"; hammer.superKind = SuperKind.Quake;
            hammer.superDamage = 200f; hammer.superPostureDamage = 130f;
            hammer.superRadius = 7.5f; hammer.superArcDeg = 360f; hammer.superHits = 1;
            hammer.superWindup = 0.52f; hammer.superActive = 0.12f; hammer.superRecover = 0.46f;
            hammer.superHitStop = 0.20f; hammer.superShake = 0.9f; hammer.superKnockback = 6f;
            hammer.viewmodelScale = 0.50f;   // short haft, all the volume in the head
            ResetPosesToDefaults(hammer);
            hammer.idle = new Pose(new Vector3(0.5f, -0.4f, 0.75f), new Vector3(0f, -15f, 0f));
            hammer.windup = new Pose(new Vector3(0.65f, 0.1f, 0.4f), new Vector3(-60f, -40f, 20f));
            hammer.swingEnd = new Pose(new Vector3(-0.2f, -0.6f, 0.9f), new Vector3(45f, 30f, -30f));
            hammer.parry = new Pose(new Vector3(0.05f, -0.2f, 0.6f), new Vector3(0f, 90f, 80f));
            // The hammer guards with its MASS, not its edge: the head is carried lower and further
            // out than any blade, and it is the one weapon whose volume would occlude the enemy if it
            // came up to blade height. Rolled less, so the head sits beside the frame rather than in it.
            hammer.guard = new Pose(new Vector3(0.38f, -0.22f, 0.62f), new Vector3(-6f, 34f, 48f));
            hammer.executeWindup = new Pose(new Vector3(0.5f, 0.5f, 0.4f), new Vector3(-90f, -20f, 10f));
            EditorUtility.SetDirty(hammer);

            var dagger = GetOrCreate<WeaponData>(WeaponsDir + "/Dagger.asset");
            dagger.displayName = "Rosethorn";
            dagger.neon = Hex("#5FD66A");
            dagger.baseDamage = 12f; dagger.postureDamage = 6f; dagger.executeDamage = 250f;
            dagger.strScale = 0f; dagger.dexScale = 1f; dagger.arcScale = 0.4f;
            dagger.comboLength = 4; dagger.comboMultipliers = new[] { 1f, 1f, 1f, 1.3f };
            dagger.attackDuration = 0.22f; dagger.hitDelay = 0.06f; dagger.comboWindow = 0.3f;
            dagger.hitOffset = 1.3f; dagger.hitRadius = 0.9f; dagger.hitStopSeconds = 0.03f;
            dagger.parryWindowMultiplier = 1.35f; dagger.parryPostureDamage = 18f; dagger.pyreBonus = -5f;
            // SUPER "THORNSTORM" — nine stabs into a narrow cone, fanned across it. Each hit is small;
            // the payload is POSTURE, arriving as nine separate applications, which is the dagger's whole
            // identity (it deals the least damage per swing of any weapon and wins by never stopping).
            // Narrow arc on purpose: it is a duelling super, not a crowd super.
            dagger.superName = "Thornstorm"; dagger.superKind = SuperKind.Flurry;
            dagger.superDamage = 34f; dagger.superPostureDamage = 26f;
            dagger.superRadius = 4f; dagger.superArcDeg = 70f; dagger.superHits = 9;
            dagger.superWindup = 0.14f; dagger.superActive = 0.52f; dagger.superRecover = 0.18f;
            dagger.superHitStop = 0.02f; dagger.superShake = 0.25f; dagger.superKnockback = 0.4f;
            ResetPosesToDefaults(dagger);
            dagger.idle = new Pose(new Vector3(0.4f, -0.3f, 0.6f), new Vector3(0f, -5f, 0f));
            dagger.windup = new Pose(new Vector3(0.5f, -0.2f, 0.45f), new Vector3(-10f, -40f, 20f));
            dagger.swingEnd = new Pose(new Vector3(-0.1f, -0.35f, 0.8f), new Vector3(10f, 30f, -30f));
            // The reference silhouette. It reads best in first person, so its on-screen size is the
            // target every other weapon was rebuilt against — do not change it without a playtest.
            // The needle guards TIGHT — pulled in toward the body and steeper, which is what a
            // stiletto with no guard to hide behind actually does. Also the least screen it can take.
            dagger.guard = new Pose(new Vector3(0.29f, -0.12f, 0.56f), new Vector3(-14f, 46f, 64f));
            dagger.viewmodelScale = 0.50f;
            EditorUtility.SetDirty(dagger);

            // Test weapon: slot 4, for fighting the boss quickly (F5 warps there). Very forgiving parry window.
            var dev = GetOrCreate<WeaponData>(WeaponsDir + "/DevBlade.asset");
            dev.displayName = "Oathbreaker (TEST)";
            dev.neon = Hex("#C6A6FF");   // pale violet: Rosethorn owns green, and the two must never be confused
            dev.baseDamage = 60f; dev.postureDamage = 40f; dev.executeDamage = 1000f;
            dev.strScale = 0.5f; dev.dexScale = 0.5f; dev.arcScale = 0.5f;
            dev.comboLength = 3; dev.comboMultipliers = new[] { 1f, 1f, 1.6f };
            dev.attackDuration = 0.3f; dev.hitDelay = 0.08f; dev.comboWindow = 0.4f;
            dev.hitOffset = 1.8f; dev.hitRadius = 1.4f; dev.hitStopSeconds = 0.06f;
            dev.parryWindowMultiplier = 1.6f; dev.parryPostureDamage = 60f; dev.pyreBonus = 25f;
            // SUPER "OATHBREAKER" — instant 360 nova at 12m. A test weapon exists to end an encounter so
            // the next thing can be tested, so its super has no wind-up and no falloff.
            dev.superName = "Oathbreaker"; dev.superKind = SuperKind.Nova;
            dev.superDamage = 600f; dev.superPostureDamage = 400f;
            dev.superRadius = 12f; dev.superArcDeg = 360f; dev.superHits = 1;
            dev.superWindup = 0.06f; dev.superActive = 0.10f; dev.superRecover = 0.24f;
            dev.superHitStop = 0.14f; dev.superShake = 0.7f; dev.superKnockback = 4f;
            dev.viewmodelScale = 0.53f;
            ResetPosesToDefaults(dev);
            EditorUtility.SetDirty(dev);

            // ---------------- Items (Neon White style single-use pickups) ----------------
            Item("Updraft", i =>
            {
                i.displayName = "Updraft"; i.shortLabel = "LIFT";
                i.effect = ItemEffect.Updraft;
                i.color = Hdr("#9AE07A", 5f);
                i.power = 20f;
                i.description = "Hurls you skyward. For roads that go up.";
            });
            Item("SoulLantern", i =>
            {
                i.displayName = "Soul Lantern"; i.shortLabel = "LANTERN";
                i.effect = ItemEffect.SoulLantern;
                i.color = Hdr("#E0A030", 5f);
                i.description = "Restores flesh, composure and flask in one breath.";
            });
            Item("PhantomStep", i =>
            {
                i.displayName = "Phantom Step"; i.shortLabel = "PHANTOM";
                i.effect = ItemEffect.PhantomStep;
                i.color = Hdr("#C08FFF", 5f);
                i.power = 1.35f; i.duration = 2.5f;
                i.description = "Walk as the dead do. Briefly untouchable, and swift.";
            });

            // ---------------- Singletons ----------------
            var stats = GetOrCreate<PlayerStatsData>(DataRoot + "/PlayerStats.asset");
            // These specific fields are DESIGN CONTRACTS documented in CLAUDE.md, not free-form tuning,
            // so DataFactory is authoritative for them. GetOrCreate only *creates*, which meant an asset
            // made before a retune silently kept the old values — the anti-mashing parry windows never
            // reached the game until the feature-test suite caught it. Everything else on this asset is
            // left alone so hand-tuning in the Inspector survives.
            stats.parryPerfectWindow = 0.13f;
            stats.parryLateWindow = 0.12f;
            stats.parryWhiffRecovery = 0.5f;
            stats.parrySuccessRecovery = 0.08f;
            // Responsiveness contract: buffered input, a graduated whiff penalty, and a recovery that can
            // never outlast the next cue. Without these the 0.5s mash tax also punished honest mistimes,
            // which reads as the game eating your inputs during a fast combo.
            stats.parryInputBuffer = 0.2f;
            stats.parryMistimeRecovery = 0.2f;
            stats.parryIncomingLookahead = 0.6f;
            stats.parryCueSafetyMargin = 0.04f;
            stats.parryMinRecovery = 0.05f;
            stats.blockDamageMultiplier = 0.3f;
            stats.facingConeDeg = 75f;
            stats.basePosture = 100f;
            stats.posturePerVitality = 4f;
            stats.postureRegenPerSecond = 22f;
            stats.postureRegenDelay = 1.2f;
            stats.postureStaggerSeconds = 1.5f;
            stats.blockPostureMultiplier = 0.9f;
            stats.hitPostureMultiplier = 0.5f;
            stats.staggeredDamageMultiplier = 1.6f;
            // GUARD (hold RMB) — rule 9: these three ARE the guard. A field initialiser would never
            // reach the PlayerStats asset that already exists on disk, and a guard that charges 0
            // posture is a guard that cannot be broken.
            //   chip 0     — a guard costs POSTURE, not health. That is the Sekiro contract and the
            //                reason a second bar exists. Guarding is still strictly worse than
            //                deflecting (posture + no Pyre + no enemy posture) and strictly better than
            //                being hit (no health at all).
            //   posture 1.5 — the largest multiplier in the game, against 0.9 for a timed block and 0.5
            //                for a raw hit, because posture is the ONLY price the guard charges. Three
            //                guarded hits from a 20-damage attack fill a 100-posture bar and break you,
            //                and a break is 1.5 s of 1.6x damage. Turtling loses, out loud.
            //   regen 0    — no refund while the blade is still up, or the fight is a stalemate.
            stats.guardChipDamageMultiplier = 0f;
            stats.guardPostureMultiplier = 1.5f;
            stats.guardPostureRegenMultiplier = 0f;
            // Pyre — the parry charge meter that replaced parry juice. Also a design contract: the
            // block fraction is what makes a perfect deflect worth chasing over a safe block, and an
            // asset created before this existed would otherwise ship a 0 and never light the weapon.
            stats.maxPyre = 100f;
            stats.basePyrePerPerfect = 25f;   // four clean deflects to full, before Arcane
            stats.pyreBlockFraction = 0.35f;  // a block is worth roughly a third of a deflect
            stats.pyrePerArcane = 1f;
            stats.ultSlowScale = 0.25f;
            stats.ultSlowSeconds = 1.6f;
            stats.ultBossPostureFraction = 0.5f;
            EditorUtility.SetDirty(stats);
            var table = GetOrCreate<UpgradeTable>(DataRoot + "/UpgradeTable.asset");
            EditorUtility.SetDirty(table);
            var feel = GetOrCreate<GameFeelSettings>(DataRoot + "/GameFeel.asset");
            // Rule 9: a field initialiser does nothing to an asset that already exists on disk, so the
            // shipped arbitration value is written here. 1 is deliberate — two attacks landing from
            // different angles inside the same 130 ms parry window are not simultaneously answerable.
            feel.maxSimultaneousAttackers = 1;
            // Guard impact feel (rule 9). Shorter hitstop than a deflect's 0.09 — a guard is a thud,
            // not a beat you earned — and a real shove, because the guard eats the damage and the blow
            // has to land somewhere the player can feel.
            feel.guardHitStop = 0.05f;
            feel.guardShove = 1.2f;
            EditorUtility.SetDirty(feel);

            // ---- campaign registry ----
            // Unlike every other asset here the registry is NOT reset: its contents are the campaign
            // running order, which is a human decision. We only ensure it exists and adopt any level
            // definition that is not listed yet, so exporting a new level does not silently leave it
            // out of the game.
            var registry = GetOrCreate<LevelRegistry>(DataRoot + "/LevelRegistry.asset");
            AdoptLevelDefinitions(registry);
            EditorUtility.SetDirty(registry);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DataFactory] Created/updated 25 attacks, 6 movesets, 6 enemies (3 legendary mini-bosses), 4 weapons, 3 items, PlayerStats, UpgradeTable, GameFeel, LevelRegistry under " + DataRoot);
        }

        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Adds every LevelDefinition in Assets/Data/Levels to the registry that is not already there.
        /// Additive only — existing order is preserved, because campaign order is authored, not derived.
        /// </summary>
        static void AdoptLevelDefinitions(LevelRegistry registry)
        {
            var guids = AssetDatabase.FindAssets("t:LevelDefinition", new[] { LevelsDir });
            if (guids == null || guids.Length == 0) return;

            var list = new System.Collections.Generic.List<LevelDefinition>();
            if (registry.levels != null)
                for (int i = 0; i < registry.levels.Length; i++)
                    if (registry.levels[i] != null) list.Add(registry.levels[i]);

            for (int i = 0; i < guids.Length; i++)
            {
                var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (def != null && !list.Contains(def)) list.Add(def);
            }
            registry.levels = list.ToArray();
        }

        /// <summary>Creates or rewrites a moveset asset. Reset to defaults first, like Attack()/Item().</summary>
        static EnemyMoveset Moveset(string name, string displayName, MovesetEntry[] entries)
        {
            var m = GetOrCreate<EnemyMoveset>(MovesetsDir + "/" + name + ".asset");
            var fresh = ScriptableObject.CreateInstance<EnemyMoveset>();
            EditorUtility.CopySerialized(fresh, m);
            Object.DestroyImmediate(fresh);
            m.name = name;
            m.displayName = displayName;
            m.entries = entries;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>One weighted, range-gated combo. Terse on purpose so a moveset stays readable inline.</summary>
        static MovesetEntry Entry(string label, float weight, float minRange, float maxRange, params EnemyAttackData[] hits)
        {
            return new MovesetEntry
            {
                label = label,
                weight = weight,
                minRange = minRange,
                maxRange = maxRange,
                combo = new AttackCombo(hits),
            };
        }

        /// <summary>
        /// Writes an authored wind-up silhouette onto an attack. CLAUDE.md rule 9: a pose left as a field
        /// initialiser on <see cref="WindupPose"/> would never reach the 25+ attack assets that already
        /// exist on disk, so every authored value is written here.
        ///
        /// <para>Timing is NOT touched by any of this. <c>windup</c>, <c>impactDelay</c> and the cue lead
        /// are calibrated against each other and against the parry window; a pose only changes what the
        /// body LOOKS like for a duration that was already decided.</para>
        ///
        /// <para>Attacks with no call to this fall back to the cone-derived generic pose in
        /// <c>EnemyVisuals</c>, which is why most of the roster needs no entry.</para>
        /// </summary>
        static void Pose(EnemyAttackData a, Vector3 arm, Vector3 strike, Vector3 bodyOffset,
                         Vector3 bodyEuler, float lag)
        {
            a.windupPose = new WindupPose
            {
                authored = true,
                armWindup = arm,
                armStrike = strike,
                bodyOffset = bodyOffset,
                bodyEuler = bodyEuler,
                weaponLag = lag,
            };
        }

        static EnemyAttackData Attack(string name, System.Action<EnemyAttackData> configure)
        {
            var a = GetOrCreate<EnemyAttackData>(AttacksDir + "/" + name + ".asset");
            // reset to class defaults so re-runs do not keep stale values
            var fresh = ScriptableObject.CreateInstance<EnemyAttackData>();
            EditorUtility.CopySerialized(fresh, a);
            Object.DestroyImmediate(fresh);
            a.name = name;
            a.attackName = name;
            configure(a);
            EditorUtility.SetDirty(a);
            return a;
        }

        static ItemData Item(string name, System.Action<ItemData> configure)
        {
            var a = GetOrCreate<ItemData>(ItemsDir + "/" + name + ".asset");
            // reset to class defaults so re-runs do not keep stale values
            var fresh = ScriptableObject.CreateInstance<ItemData>();
            EditorUtility.CopySerialized(fresh, a);
            Object.DestroyImmediate(fresh);
            a.name = name;
            configure(a);
            EditorUtility.SetDirty(a);
            return a;
        }

        /// <summary>Hex colour scaled to an HDR intensity, with alpha kept at 1.</summary>
        static Color Hdr(string hex, float intensity)
        {
            var c = Hex(hex) * intensity;
            c.a = 1f;
            return c;
        }

        /// <summary>Copy the class-default poses onto an existing weapon so re-runs reset any hand edits.</summary>
        static void ResetPosesToDefaults(WeaponData w)
        {
            var fresh = ScriptableObject.CreateInstance<WeaponData>();
            w.idle = fresh.idle;
            w.windup = fresh.windup;
            w.swingEnd = fresh.swingEnd;
            w.parry = fresh.parry;
            w.guard = fresh.guard;
            w.drink = fresh.drink;
            w.executeWindup = fresh.executeWindup;
            w.executeEnd = fresh.executeEnd;
            Object.DestroyImmediate(fresh);
        }

        public static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        public static Color Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            Debug.LogWarning("[DataFactory] Bad hex color " + hex);
            return Color.magenta;
        }
    }
}
