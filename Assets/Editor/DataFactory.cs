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
            });
            var gruntSlash = Attack("Grunt_Slash", a =>
            {
                a.windup = 0.5f; a.impactDelay = 0.05f; a.strikeDuration = 0.2f; a.recovery = 0.35f;
                a.range = 2.7f; a.coneDeg = 70f; a.damage = 18f; a.lungeDistance = 0.75f; a.comboGap = 0.2f;
            });
            var gruntHeavy = Attack("Grunt_Heavy", a =>
            {
                // the rhythm-breaker that ends a chain: slower, hits harder, best deflect value
                a.windup = 0.72f; a.impactDelay = 0.06f; a.strikeDuration = 0.22f; a.recovery = 0.5f;
                a.range = 2.8f; a.coneDeg = 60f; a.damage = 26f; a.lungeDistance = 1.15f;
                a.comboGap = 0.22f; a.parryPostureMultiplier = 1.3f;
            });
            var heavyStep = Attack("Heavy_Step", a =>
            {
                // an advancing jab so the Heavy can close distance mid-combo instead of whiffing
                a.windup = 0.5f; a.impactDelay = 0.05f; a.strikeDuration = 0.18f; a.recovery = 0.3f;
                a.range = 3.0f; a.coneDeg = 70f; a.damage = 20f; a.lungeDistance = 1.4f; a.comboGap = 0.18f;
            });
            var heavyOverhead = Attack("Heavy_Overhead", a =>
            {
                a.windup = 0.75f; a.recovery = 0.5f; a.range = 3.2f; a.coneDeg = 60f;
                a.damage = 32f; a.lungeDistance = 1.3f; a.comboGap = 0.22f; a.parryPostureMultiplier = 1.3f;
            });
            var heavySweep = Attack("Heavy_Sweep", a =>
            {
                a.windup = 0.55f; a.recovery = 0.35f; a.range = 3.1f; a.coneDeg = 110f;
                a.damage = 23f; a.lungeDistance = 0.8f; a.comboGap = 0.2f;
            });
            var bossSlash = Attack("Boss_Slash", a =>
            {
                a.windup = 0.5f; a.recovery = 0.6f; a.range = 3.9f; a.coneDeg = 80f;
                a.damage = 30f; a.lungeDistance = 1.5f;
            });
            var bossDoubleA = Attack("Boss_DoubleSlash_A", a =>
            {
                a.windup = 0.45f; a.recovery = 0.7f; a.range = 3.9f; a.coneDeg = 80f;
                a.damage = 25f; a.comboGap = 0.15f; a.lungeDistance = 1.3f;
            });
            var bossDoubleB = Attack("Boss_DoubleSlash_B", a =>
            {
                // was 0.3 — at/below human visual reaction time, so the second hit was unparryable
                a.windup = 0.45f; a.recovery = 0.7f; a.range = 3.9f; a.coneDeg = 80f;
                a.damage = 25f; a.lungeDistance = 1.3f;
            });
            var bossSlam = Attack("Boss_Slam", a =>
            {
                a.windup = 0.9f; a.recovery = 1.1f; a.range = 4.3f; a.coneDeg = 90f;
                a.damage = 45f; a.lungeDistance = 2.2f; a.parryPostureMultiplier = 1.5f;
            });
            var bossThrust = Attack("Boss_Thrust", a =>
            {
                // unblockable: the only counter is repositioning, so it needs the longest read in the set
                a.windup = 0.85f; a.recovery = 0.9f; a.range = 4.5f; a.coneDeg = 30f;
                a.damage = 55f; a.lungeDistance = 2.9f; a.unblockable = true;
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
            grunt.bodyColor = Hex("#0A0708"); grunt.emission = Hex("#6A0F14") * 1.2f; grunt.scale = 1f;
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
            heavy.bodyColor = Hex("#0A0708"); heavy.emission = Hex("#8A2A10") * 1.2f; heavy.scale = 1.4f;
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
            boss.bodyColor = Hex("#0D0612"); boss.emission = Hex("#7A1030") * 1.6f; boss.scale = 2.2f;
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

            // --- Knight: The Iron Penitent. Slow, enormous, wide punish windows. --------------------
            var knightShove = Attack("Knight_Shove", a =>
            {
                // the only quick thing it does, and it exists purely to close distance
                a.windup = 0.6f; a.impactDelay = 0.05f; a.strikeDuration = 0.2f; a.recovery = 0.55f;
                a.range = 3.6f; a.coneDeg = 60f; a.damage = 20f; a.lungeDistance = 2.2f; a.comboGap = 0.3f;
            });
            var knightCleave = Attack("Knight_Cleave", a =>
            {
                a.windup = 0.8f; a.impactDelay = 0.06f; a.strikeDuration = 0.24f; a.recovery = 0.8f;
                a.range = 3.8f; a.coneDeg = 105f; a.damage = 34f; a.lungeDistance = 1.2f;
                a.comboGap = 0.32f; a.parryPostureMultiplier = 1.4f;
            });
            var knightOverhead = Attack("Knight_Overhead", a =>
            {
                // one full second of wind-up. Deflecting it is most of a posture bar; eating it is most of yours.
                a.windup = 1.0f; a.impactDelay = 0.07f; a.strikeDuration = 0.26f; a.recovery = 1.05f;
                a.range = 3.7f; a.coneDeg = 60f; a.damage = 46f; a.lungeDistance = 1.8f;
                a.comboGap = 0.35f; a.parryPostureMultiplier = 1.9f;
            });
            var knightQuake = Attack("Knight_Quake", a =>
            {
                // unblockable, near-omnidirectional, the longest recovery of any attack in the game:
                // the whole move is "get out, then take your free hits".
                a.windup = 1.1f; a.impactDelay = 0.08f; a.strikeDuration = 0.28f; a.recovery = 1.4f;
                a.range = 4.4f; a.coneDeg = 160f; a.damage = 52f; a.lungeDistance = 0.9f;
                a.comboGap = 0.4f; a.unblockable = true;
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
            ninja.bodyColor = Hex("#07090A"); ninja.emission = Hex("#1FBFA8") * 1.4f; ninja.scale = 0.95f;
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
            knight.maxHP = 260f; knight.maxPosture = 190f; knight.postureRegen = 4f;
            knight.postureRegenDelay = 3.5f; knight.staggerSeconds = 4.2f;
            knight.moveSpeed = 3.4f; knight.turnSpeed = 210f; knight.aggroRange = 18f; knight.attackRange = 2.9f;
            knight.attackCooldown = 0.5f; knight.parryRecoilSeconds = 0.75f; knight.aggression = 0.35f;
            // Deliberately the LEAST aggressive non-boss in the game. Aggression compresses recovery and
            // cooldown, and this enemy's entire design is the size of the gap after it swings.
            knight.windupTurnMultiplier = 0.16f; knight.stepSpeedMultiplier = 0.26f;
            knight.stepAcceleration = 4.5f; knight.stepDeadzone = 0.95f;
            knight.comboBreathSeconds = 0.85f; knight.readyDistanceMultiplier = 1.6f;
            // 1.6x scale: bigger than the Heavy, smaller than the Warden, and it stands at 4.0m so the
            // whole silhouette is on screen when the overhead starts.
            knight.preferredRange = 4f; knight.commitTolerance = 0.8f; knight.repositionDeadzone = 0.5f;
            knight.backStepSpeedMultiplier = 0.2f; knight.strafeSpeedMultiplier = 0.2f; knight.lungeMinDistance = 1.5f;
            knight.soulValue = 600;
            knight.bodyColor = Hex("#0A0708"); knight.emission = Hex("#C0521A") * 1.4f; knight.scale = 1.6f;
            knight.moveset = Moveset("Legendary_Knight_Moveset", "The Iron Penitent", new[]
            {
                Entry("cleave-OVERHEAD (the bread and butter)",        3f,   0f,   99f, knightCleave, knightOverhead),
                Entry("OVERHEAD (single, huge, free punish after)",    1.5f, 0f,   99f, knightOverhead),
                Entry("cleave-cleave-OVERHEAD (two, then the break)",  2f,   0f,   99f, knightCleave, knightCleave, knightOverhead),
                Entry("shove-cleave (closes, then commits)",           2f,   3.8f, 99f, knightShove, knightCleave),
                Entry("shove-QUAKE (unblockable - run, then punish)",  1f,   3.4f, 99f, knightShove, knightQuake),
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
            spellsword.bodyColor = Hex("#0B0610"); spellsword.emission = Hex("#8A2ADF") * 1.5f; spellsword.scale = 1.5f;
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
            ResetPosesToDefaults(hammer);
            hammer.idle = new Pose(new Vector3(0.5f, -0.4f, 0.75f), new Vector3(0f, -15f, 0f));
            hammer.windup = new Pose(new Vector3(0.65f, 0.1f, 0.4f), new Vector3(-60f, -40f, 20f));
            hammer.swingEnd = new Pose(new Vector3(-0.2f, -0.6f, 0.9f), new Vector3(45f, 30f, -30f));
            hammer.parry = new Pose(new Vector3(0.05f, -0.2f, 0.6f), new Vector3(0f, 90f, 80f));
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
            EditorUtility.SetDirty(dagger);

            // Test weapon: slot 4, for fighting the boss quickly (F5 warps there). Very forgiving parry window.
            var dev = GetOrCreate<WeaponData>(WeaponsDir + "/DevBlade.asset");
            dev.displayName = "Oathbreaker (TEST)";
            dev.neon = Hex("#7FFF9A");
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
            dev.viewmodelScale = 0.5f;
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
