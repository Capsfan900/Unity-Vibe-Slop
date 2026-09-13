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
        const string EnemiesDir = EnemyPaths.Souls;   // the duels; parkour data is EnemyPaths.Parkour
        const string WeaponsDir = DataRoot + "/Weapons";
        const string ItemsDir = DataRoot + "/Items";
        const string MovesetsDir = EnemyPaths.SoulsMovesets;
        const string LevelsDir = DataRoot + "/Levels";

        [MenuItem("VibeGame1/3. Create Data")]
        public static void CreateAll()
        {
            EnsureFolder(DataRoot);
            EnsureFolder(AttacksDir);
            EnsureFolder(EnemyPaths.Parkour);
            EnsureFolder(EnemyPaths.Souls);
            EnsureFolder(WeaponsDir);
            EnsureFolder(ItemsDir);
            EnsureFolder(EnemyPaths.ParkourMovesets);
            EnsureFolder(EnemyPaths.SoulsMovesets);
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
            var grunt = GetOrCreate<EnemyData>(EnemyPaths.Data("Grunt"));
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
                EntryCd("jab-jab-HEAVY (the signature bait)", 1f, 0f, 99f, 5f, gruntJab, gruntJab, gruntHeavy),
                Entry("slash-heavy (slow, slower)",           1.5f, 0f, 99f, gruntSlash, gruntHeavy),
                Entry("jab-slash-jab (fast, slow, fast)",     2f, 0f, 99f, gruntJab, gruntSlash, gruntJab),
            });
            grunt.combos = grunt.moveset.ToComboArray();
            // PARKOUR-SECTION ENEMIES SHOOT (2026-09-05 pivot: enemies are a route, not filler). A bolt
            // every 1.6 s from 10-30 m at 32 m/s -- a 0.31-0.94 s flight that IS the tell, cued 0.28 s
            // out like every attack. Retuned the same day from play ("so slow I have to stop almost and
            // parry it"): 18 m/s was a 0.8 s crawl from mid-band that you WAITED for; 32 m/s arrives from
            // 15 m in 0.47 s, so the deflect happens at a run. The near edge moved 6 -> 10 m because the
            // cue is 9 m out at this speed and a bolt must never arrive before its own cue. A perfect
            // deflect throws it back for 30 damage / 40 posture on the shooter and buys the player 9 m/s
            // along their look (a run at 11 becomes 20, bleeding back toward the 17.6 air soft cap):
            // deflect while aiming at the next ledge and the enemy has just launched you.
            // souls_enemies (2026-09-06 split): the pill Grunt is the MELEE trainer again. It keeps the bolt
            // data authored below only as the SOURCE the sentry copies from; it never shoots.
            grunt.flaskPunishChance = 0.35f;
            grunt.shootsProjectiles = false;
            grunt.projectileAttack = Attack("Projectile_Bolt", a =>
            {
                a.windup = 0.5f; a.impactDelay = 0f; a.strikeDuration = 0.05f; a.recovery = 0.2f;
                a.range = 30f; a.coneDeg = 20f; a.damage = 12f; a.lungeDistance = 0f;
                a.comboGap = 0.2f; a.parryPostureMultiplier = 1.2f;
            });
            // SENTRIES (2026-09-06, from play: "they stop shooting too early, the rhythm is bad, the
            // detection is bad"). rangedOnly: a perch enemy never melees, never leaves the perch, and
            // wakes at the far edge of the band (30 m) with a three-line sight check, not at 14 m melee
            // aggro with one. The near edge drops 10 -> 3 m: inside 11.5 m the LAUNCH slows so the flight
            // is always cue lead + 0.08 s, so a runner is shot at all the way in and past. The beat is a
            // fixed 1.6 s metronome (no +-15% jitter), held while the line is blocked, and each shot leads
            // 80% of the player's velocity so a runner meets it instead of outrunning it.
            grunt.rangedOnly = false; grunt.projectileLead = 1.0f; grunt.projectileHomingDegPerSec = 180f;
            // 2026-09-06 (user): faster, and it never misses -- full lead plus 180 deg/s homing. 40 m/s from 15 m
            // is 0.37 s; the launch still slows inside 14.4 m so the cue is never owed before the bolt exists.
            grunt.projectileInterval = 1.6f; grunt.projectileSpeed = 40f;
            grunt.projectileBurstCount = 1; grunt.projectileBurstInterval = 0.42f;
            // BOLT TIMING (2026-09-06 plan, from play: "the projectile just comes in at a bad time ... the
            // placement and timing of the shots needs to work with the game as well so the player can
            // actually make the parrys while moving fast"). Two shipped numbers move.
            // F1, the ARM-UP: the beat is HELD while the line is blocked, so a shooter that has not seen you
            // for seconds owes a shot and fires it on the FIRST FRAME the line clears -- the frame you crest a
            // ledge or land. 0.7 s is one cue lead plus a landing; capped at one interval by AcquireBeat.
            grunt.projectileAcquireDelay = 0.7f;
            // F5, a LONGER MINIMUM FLIGHT up close: the near edge 3 -> 6 m. With ProjectileShooter.CueMargin
            // 0.16 a near bolt now shows ~0.44 s of flight instead of 0.36. The cue LEAD is untouched at
            // 0.28 s -- it is a contract shared with every melee attack, and the plan rejects making it
            // elastic. Still well inside the melee bodies' 3.6 m reach, so a sentry is never toothless.
            grunt.projectileMinRange = 6f; grunt.projectileMaxRange = 32f;
            // P1 (combat plan 2026-09-06): a reflected bolt OPENS a sentry, it does not kill it. At 30 damage
            // two reflects were exactly the Grunt's 60 HP, and Health.TakeDamage runs before Posture.Add, so
            // the body died on the frame it would have staggered and the sentry dash never had a target.
            // Invariant (ProjectileTests): ceil(maxPosture / parriedPosture) * parriedDamage < maxHP.
            // 2026-09-06 (user): a parkour enemy is KILLED by its own reflected bolts -- two for this one (60 HP).
            // The flare is thrown on death (SentryBurst) and is optional traversal, never the way to finish it.
            grunt.parriedProjectileDamage = 30f; grunt.parriedProjectilePosture = 40f;
            grunt.parrySpeedGain = 9f;
            EditorUtility.SetDirty(grunt);

            var heavy = GetOrCreate<EnemyData>(EnemyPaths.Data("Heavy"));
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
                EntryCd("step-sweep-OVERHEAD (the long phrase)",    1.5f, 0f, 99f, 6f, heavyStep, heavySweep, heavyOverhead),
                Entry("sweep-sweep-overhead (tempo then break)",    1.5f, 0f, 99f, heavySweep, heavySweep, heavyOverhead),
                // Step openers exist to CLOSE distance, so they are gated to the far band. Chosen
                // point-blank the advancing jab looks like the enemy is walking through you.
                Entry("step-overhead (closes then commits)",        2f, 2.8f, 99f, heavyStep, heavyOverhead),
            });
            heavy.combos = heavy.moveset.ToComboArray();
            // The heavy shoots too, a touch slower and harder: the same bolt data, a longer interval.
            // 28 m/s from 10 m is a 0.36 s flight -- still a tell you answer at a run.
            heavy.flaskPunishChance = 0.4f;
            heavy.shootsProjectiles = false;
            heavy.projectileAttack = grunt.projectileAttack;
            heavy.rangedOnly = false; heavy.projectileLead = 1.0f; heavy.projectileHomingDegPerSec = 150f;
            heavy.projectileInterval = 2.4f; heavy.projectileSpeed = 36f;
            heavy.projectileBurstCount = 1; heavy.projectileBurstInterval = 0.42f;
            heavy.projectileAcquireDelay = 0.7f;               // F1, the arm-up (see the Grunt above)
            heavy.projectileMinRange = 6f; heavy.projectileMaxRange = 32f;   // F5, the longer near flight
            heavy.parriedProjectileDamage = 45f; heavy.parriedProjectilePosture = 50f;   // 3 reflects kill: 135 >= 130 HP
            heavy.parrySpeedGain = 9f;
            EditorUtility.SetDirty(heavy);

            // ---- parkour_enemies: THE SENTRIES (2026-09-06 split) ----------------------------------------
            // pshooter_enemy01 / pshooter_enemy02 are the Grunt and the Heavy's tuning copied whole (CopySerialized,
            // so a retune of the pill guys carries over) and then flipped to the span role: rangedOnly,
            // shootsProjectiles, no flask interrupt (a route, not a duel), and a violet body so a perch
            // never reads as a melee enemy. Level_01's perches spawn THESE; the sandbox pads keep the melee
            // originals. Everything projectile-side lives in Enemies/parkour_enemies.
            var sentryGrunt = GetOrCreate<EnemyData>(EnemyPaths.Data("pshooter_enemy01"));
            EditorUtility.CopySerialized(grunt, sentryGrunt);
            sentryGrunt.name = "pshooter_enemy01";
            sentryGrunt.displayName = "Sentry";
            sentryGrunt.shootsProjectiles = true; sentryGrunt.rangedOnly = true; sentryGrunt.flaskPunishChance = 0f;
            // One bolt remains one learnable beat. Write this after CopySerialized so a future source-enemy
            // retune cannot silently turn the basic span sentry into a phrase.
            sentryGrunt.projectileBurstCount = 1; sentryGrunt.projectileBurstInterval = 0.42f;
            // The pale ghost is a traversal tool placed inside tight parkour. Its exact predicted path
            // must stay clear, but the 1 m PLAYER-CONTACT sweep must not turn a nearby ledge into permanent
            // silence; the runtime bolt itself has never collided with world geometry.
            sentryGrunt.projectileAllowTightRouteShots = true;
            sentryGrunt.projectileIgnoreDepartureSupport = false;
            sentryGrunt.projectileVisualScale = 1.10f; sentryGrunt.projectileTrailScale = 1.10f;
            sentryGrunt.projectileCueScale = 1.10f;
            sentryGrunt.usesPosture = true; sentryGrunt.isTurret = false;
            sentryGrunt.perfectBurstParriesToDestroy = 0;
            // THE GHOST (2026-09-06, user-directed body redesign; see PrefabFactory.BuildGhostBody).
            // bodyColor is written into the SHELL's _BaseColor by EnemyVisuals, so it must be the same
            // value as M_SentryGhost's albedo or the shell and the hem would be two different colours.
            // emission is NOT a body glow — EnemyVisuals normalises it into `accent`, which tints the
            // parry flash 25%. It used to be violet #5A2BD0, which both squatted on the flare's "use
            // this" hue and dragged a quarter of the deflect flash off bone-white; a pale cold near-white
            // leaves EnemyVisuals.ParryGlow reading as the steel-on-steel it is meant to be.
            sentryGrunt.bodyColor = Hex("#A9C2DA"); sentryGrunt.emission = Hex("#BFD8F2") * 0.9f;
            EditorUtility.SetDirty(sentryGrunt);

            var sentryHeavy = GetOrCreate<EnemyData>(EnemyPaths.Data("pshooter_enemy02"));
            EditorUtility.CopySerialized(heavy, sentryHeavy);
            sentryHeavy.name = "pshooter_enemy02";
            sentryHeavy.displayName = "Heavy Sentry";
            sentryHeavy.shootsProjectiles = true; sentryHeavy.rangedOnly = true; sentryHeavy.flaskPunishChance = 0f;
            // The dark reliquary asks for a rapid THREE-PARRY phrase. 0.40 is contact cadence, not launch
            // cadence: cue 0.28 + perfect recovery 0.08 + 0.04 s of honest slack. projectileInterval 0.90
            // is the quiet cooldown and starts from the final incoming answer, not the third emission.
            sentryHeavy.projectileBurstCount = 3; sentryHeavy.projectileBurstInterval = 0.40f;
            sentryHeavy.projectileInterval = 0.90f;
            sentryHeavy.perfectBurstParriesToDestroy = 3;
            sentryHeavy.usesPosture = false; sentryHeavy.isTurret = true;
            sentryHeavy.projectileAllowTightRouteShots = false;
            // A full phrase needs two follow-up contacts while a 27.5 m/s runner is still crossing its
            // answerable approach. The inherited 32 m band was barely one second of route coverage and
            // made a bottom-of-ramp pair wake too late; 48 m announces the phrase without changing speed,
            // cue, cadence or any of the conservative LOS/flight-clearance checks.
            sentryHeavy.projectileMaxRange = 48f;
            // The 1 m broad sweep used to graze T3_Perch_E before a bolt could leave its own muzzle,
            // permanently silencing the Heavy. Ignore only that detected standing support during departure;
            // every other broad obstruction remains conservative.
            sentryHeavy.projectileIgnoreDepartureSupport = true;
            sentryHeavy.projectileVisualScale = 1.20f; sentryHeavy.projectileTrailScale = 1.20f;
            sentryHeavy.projectileCueScale = 1.15f;
            // The shipped DevBlade is the upper bound: 60 * 1.2 = 72 immediate posture, plus 50 on
            // each reflected return. Two complete exchanges are 244; the third parry reaches 316.
            // At 330 the deathblow prompt therefore cannot replace the third projectile answer, while
            // the third 45-damage return still kills its 130 HP body. This is sequencing safety.
            sentryHeavy.maxPosture = 1f; sentryHeavy.postureRegen = 0f;
            sentryHeavy.parriedProjectileDamage = 0f; sentryHeavy.parriedProjectilePosture = 0f;
            // The Heavy Sentry's dedicated reliquary body is a different creature from the pale ghost;
            // its dark cold slate also keeps the body out of the flare's tell hue. It remains the darkest
            // thing on a perch, so the pale ghost and broad heavy separate by shape and value at a glance.
            sentryHeavy.bodyColor = Hex("#25303F"); sentryHeavy.emission = Hex("#8FB6E0") * 1.0f;
            EditorUtility.SetDirty(sentryHeavy);

            // ---- pshooter_enemy03: THE SURGE TURRET (2026-09-06, the user's ask) -------------------------
            // "a small little circle shaped turret that just shoots the player and dies in one hit but
            // boosts the player's speed when they parry."
            //
            // It is a target, not a duel: it lives on a long descending ramp, in a row, and the player slides
            // past parrying as they go. Copied from pshooter_enemy01 so a retune of the sentries' bolt carries
            // over, then flipped to the target role.
            var turret = GetOrCreate<EnemyData>(EnemyPaths.Data("pshooter_enemy03"));
            EditorUtility.CopySerialized(sentryGrunt, turret);
            turret.name = "pshooter_enemy03";
            turret.displayName = "Surge Turret";
            turret.shootsProjectiles = true; turret.rangedOnly = true; turret.flaskPunishChance = 0f;
            // Opening rows are sequenced one TURRET at a time. Each member owns one bolt, never a hidden
            // sub-phrase, so the level-authored five-member ladder remains five distinct reads.
            turret.projectileBurstCount = 1; turret.projectileBurstInterval = 0.42f;
            // CopySerialized inherits the ordinary ghost's tight-route policy. The ramp turret is already
            // tuned correctly and keeps the conservative broad clearance forecast unchanged.
            turret.projectileAllowTightRouteShots = false;
            turret.projectileIgnoreDepartureSupport = false;
            // The final descent is read at maximum lateral/vertical screen motion. Make this enemy type's
            // existing warm attack larger and broader without changing its collision, path or timing.
            turret.projectileVisualScale = 1.35f; turret.projectileTrailScale = 1.30f;
            turret.projectileCueScale = 1.25f;
            turret.usesPosture = false; turret.isTurret = true;
            turret.perfectBurstParriesToDestroy = 0;

            // ONE HIT, FROM ANYTHING. 1 HP: a swing, a reflected bolt, a wand, a riposte -- every damage
            // source in the game does at least 1. Not 0 (Health treats a zero-max body as a divide it has
            // never been asked to do) and not 10 (the dagger's chip on a passing swing would leave it up).
            turret.maxHP = 1f;
            // ...and NO posture game. Posture is deliberately out of reach: the largest single parry in the
            // game is the dev blade's 60 x a 1.5 parryPostureMultiplier = 90, so 200 can never be broken by
            // one deflect. Without this the turret would stagger and raise a DEATHBLOW glyph for the ~0.3 s
            // the reflected bolt is in the air -- a duel prompt on a body that is already dead. It also
            // means no posture break, so SentryBurst's flare never fires; the prefab does not carry one.
            turret.maxPosture = 1f; turret.postureRegen = 0f; turret.postureRegenDelay = 99f;
            turret.staggerSeconds = 0.1f;
            // It never moves. moveSpeed is dead weight on a rangedOnly body (EnemyController.Chase stops the
            // locomotion outright) but is written small so nothing that reads it draws a walking turret.
            turret.moveSpeed = 0f; turret.turnSpeed = 300f;
            // The band. It must be shooting at you from the top of a ramp you are still sliding down, so it
            // wakes at 36 m (further than the sentries' 32: on a descent you SEE it long before you reach it,
            // and a turret that only starts firing at 32 m gives you one bolt instead of three) and keeps
            // firing to 2.5 m so the last one on the row is still parriable as you go past it.
            turret.aggroRange = 36f; turret.attackRange = 2.5f;
            turret.projectileMinRange = 2.5f; turret.projectileMaxRange = 36f;
            // FASTER BEAT than a sentry's 1.6 s. This body has 1 HP and exists for one exchange: the row is
            // the encounter, so each turret has to put a bolt in front of you inside the ~1 s you are within
            // its arc at slide speed. 1.1 s. Not 0.7: two bolts overlapping in flight from the same turret
            // gives two cues 0.28 s apart and the parry stops being one clean read.
            turret.projectileInterval = 1.1f;
            // The arm-up (F1) is inherited at 0.7 s and written explicitly so it is a decision, not a copy:
            // a turret wakes at 36 m on a descent you can see all the way down, so it acquires you long
            // before you are in its arc and the breath costs it nothing. Its near edge stays 2.5 m (NOT the
            // sentries' new 6 m): the last turret on a row must still be parriable as you slide past it.
            turret.projectileAcquireDelay = 0.7f;
            // Same bolt, same tell, same cue lead as every other bolt in the game -- deliberately NOT a new
            // projectile. 36 m/s (the Heavy Sentry's speed rather than the Grunt's 40) because the turret
            // shoots from further out and the flight must stay readable across the extra 4 m. 240 deg/s of
            // homing, up from 180, because the player is DESCENDING past it at speed on a slope: the bolt has
            // to turn down as well as across, or the one parry this body exists for is never offered.
            turret.projectileSpeed = 36f; turret.projectileLead = 1.0f; turret.projectileHomingDegPerSec = 240f;
            // A single reflect kills it (1 HP), which is the point: the parry IS the kill. Kept at 30 rather
            // than lowered to 1 so a stray reflect off a neighbouring sentry still reads the same everywhere.
            turret.parriedProjectileDamage = 30f; turret.parriedProjectilePosture = 40f;
            // The deflect impulse itself stays at the sentries' 9 m/s: the punch is a shared feel contract
            // and this enemy's own reward is the SURGE below, not a bigger shove.
            turret.parrySpeedGain = 9f;

            // ---- THE SURGE. FirstPersonMotor.SpeedMultiplier, via SurgeTurret -> ParrySurge. -------------
            // 0.12 per deflect on a groundSpeed of 11 is +1.32 m/s a parry: felt on the very first one, but
            // not a jolt that throws your landing. Not 0.20 -- one lucky parry would then be worth more than
            // the four that follow it, and the ramp stops being the reward.
            turret.parrySurgeStep = 0.12f;
            // Five stacks, ceiling x1.60 (17.6 m/s). Five so a row reads as a LADDER you are climbing -- five
            // separate confirmations, five audible steps -- rather than a switch that flips on the second
            // turret. Not 8 (x1.96): past ~x1.6 the level's jump arcs and the motor's air control stop being
            // something a human can aim, and the run-out at the bottom of a ramp becomes a coin flip.
            turret.parrySurgeMaxStacks = 5;
            // One stack falls every 1.4 s of not parrying (was 1.5 s; retuned 2026-09-08 for the wider
            // current route). Grant RESETS the drop timer, so any player inside a turret's 1.1 s bolt
            // metronome never decays at all -- the timer only governs the run-out after the last turret.
            // 1.4 leaves 0.3 s of real slack over that beat, enough for an honest late contact, while the
            // full x1.60 ladder now clears in 7.0 s rather than carrying an extra half-second into the next
            // route beat. This deliberately stays a simple timer: no distance rule or new mechanic.
            turret.parrySurgeSeconds = 1.4f;

            // Cheap: it dies to a touch and it is meant to be taken in rows of five or more.
            turret.soulValue = 15;
            // THE READ. It must be unmistakable from pshooter_enemy01 (a tall pale ghost) and 02 (a big dark
            // pill) at 30 m in a moving frame. It separates on all three axes at once: SHAPE (a small sphere
            // in a ring -- the only round silhouette among enemies), SIZE (0.55 scale, roughly half the
            // height of either sentry) and VALUE (mid steel, between the ghost's near-white and the heavy's
            // near-black). The hue stays cold like every other body: warm means a combat tell or it means
            // fire (ANIMATION-VFX section 4), and this thing is read WHILE an amber bolt crosses it.
            turret.bodyColor = Hex("#6F7C8A"); turret.emission = Hex("#A8C4DC") * 0.9f;
            turret.scale = 0.55f;
            EditorUtility.SetDirty(turret);

            var boss = GetOrCreate<BossData>(EnemyPaths.Data("Boss"));
            boss.displayName = "THE HOLLOW WARDEN";
            boss.maxHP = 380f; boss.maxPosture = 220f; boss.postureRegen = 12f; boss.postureRegenDelay = 3f; boss.staggerSeconds = 4f;
            boss.moveSpeed = 5.5f; boss.turnSpeed = 300f; boss.aggroRange = 40f; boss.attackRange = 3.0f;
            boss.attackCooldown = 0.5f; boss.parryRecoilSeconds = 0.6f;
            // Explicitly 0: the boss keeps its authored phase pacing and is NOT swept up in the
            // grunt/heavy aggression pass. Its pressure comes from phases, not from this multiplier.
            boss.aggression = 0f;
            boss.flaskPunishChance = 0.7f;   // the Warden reads the flask; its phases are already its aggression curve
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
            var ninja = GetOrCreate<EnemyData>(EnemyPaths.Data("Legendary_Ninja"));
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
                EntryCd("cut-cross-REAP (the cadence baits the sweep)", 1.2f, 0f,  99f, 6f, ninjaCut, ninjaCross, ninjaReap),
                Entry("rush-cut-cut (closes, then flurries)",          2f,   3.4f, 99f, ninjaRush, ninjaCut, ninjaCut),
                EntryCd("cut-cut-cross-cut-FALL (the long string)",    1f,   0f,   99f, 7f, ninjaCut, ninjaCut, ninjaCross, ninjaCut, ninjaFall),
            });
            ninja.combos = ninja.moveset.ToComboArray();
            ninja.flaskPunishChance = 0.75f;   // the Shade is the one that WILL be on you when you drink
            EditorUtility.SetDirty(ninja);

            // --- EnemyData: The Iron Penitent -------------------------------------------------------
            var knight = GetOrCreate<EnemyData>(EnemyPaths.Data("Legendary_Knight"));
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
                // COOLDOWNS (2026-09-06): the tempo break and the vent are surprises, so they cannot come
                // twice running -- the report's "never spam a heavy" rule, as data.
                EntryCd("OVERHEAD (the tempo break)",                  1.2f, 0f,   99f, 7f, knightOverhead),
                // Far band only: these are what a player who backed out of spin range gets instead.
                EntryCd("FURNACE VENT (punishes waiting it out)",      1.4f, 5f,   99f, 5f, knightVent),
                EntryCd("VENT-shove-SPIN (vents you back in, then spins)", 0.9f, 5.5f, 99f, 8f, knightVent, knightShove, knightSpinUp, knightSpinHit, knightSpinHit, knightSpinOut),
            });
            knight.combos = knight.moveset.ToComboArray();
            knight.flaskPunishChance = 0.6f;   // drinking inside 18 m of the Penitent is a decision
            EditorUtility.SetDirty(knight);

            // --- EnemyData: The Ashen Chorister -----------------------------------------------------
            var spellsword = GetOrCreate<EnemyData>(EnemyPaths.Data("Legendary_Spellsword"));
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
                EntryCd("arc-GRASP (punishes stepping in)",            1.2f, 3f,   99f, 6f, swordArc, swordGrasp),
                EntryCd("EMBERFALL-thrust (ranged opener, then closes)", 1.2f, 4.5f, 99f, 5f, swordEmberfall, swordThrust),
                EntryCd("arc-thrust-FEINT-arc (the long unreadable one)", 1f, 0f,  99f, 8f, swordArc, swordThrust, swordFeint, swordArc),
            });
            spellsword.combos = spellsword.moveset.ToComboArray();
            spellsword.flaskPunishChance = 0.5f;
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
            var marionette = GetOrCreate<EnemyData>(EnemyPaths.Data("Legendary_Marionette"));
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
                EntryCd("OVERHEAD (the tempo break)",                  1.2f, 0f,   99f, 7f, marOverhead),
                Entry("overhead into the spin",                        1f,   0f,   6f,
                      marOverhead, marSpinUp, marSpinPass, marSpinPass, marSpinPass, marSpinOut),
                // Far band only: what a player who backed out of spin range gets instead of a rest.
                EntryCd("STRING LASH (punishes waiting it out)",       1.6f, 5f,   99f, 5f, marLash),
                Entry("LASH into the spin (drags you back in)",        1f,   5.5f, 99f,
                      marLash, marSpinUp, marSpinPass, marSpinPass, marSpinPass, marSpinPass, marSpinOut),
            });
            marionette.combos = marionette.moveset.ToComboArray();
            marionette.flaskPunishChance = 0.6f;
            EditorUtility.SetDirty(marionette);

            // --- Revenant: THE EMBER REVENANT. PROTOTYPE. ------------------------------------------
            //
            // The test body for the ai_skelly_tool pipeline, and the first BURNING enemy. Sandbox pad
            // only, like the Marionette -- it is in no LevelDefinition and no LevelRegistry.
            //
            // ===== WHAT IT IS FOR =================================================================
            // Two things the project had not answered:
            //   1. Can an enemy be lit from inside without wrecking the readability language? Emission
            //      on an enemy already MEANS "you deflected". The answer is EmberAura: a dim constant
            //      floor far under the parry spike, modulated by the same chargeDark as everything
            //      else, so a burning body still visibly INHALES on a wind-up. See EnemyVisuals.
            //   2. Does a second forge model drop in without bespoke code? It does; the only new code
            //      is the aura, which is a presentation component any enemy can take.
            //
            // ===== THE FIGHT ======================================================================
            // A long-limbed thing with a heavy blade. Where the Marionette is a metronome you hold,
            // this is a READ: slow, enormous, committed swings with real openings between them, so it
            // is the tutorial for "watch the body" rather than "hold the beat". Everything is well
            // clear of the wind-up floor because nothing here is trying to be fast.
            //
            // ===== THE WIND-UP SILHOUETTES ========================================================
            // MEASURED, at 3.40 m (preferredRange), from the player's eye (y 1.60, 95 deg FOV), on the
            // frozen CUE PEAK -- VibeGame1/Photograph Wind-up Silhouettes. Numbers below are on-screen
            // measurements, never the Eulers on the line above them: an authored angle is not an
            // on-screen angle, and this project has already shipped ten poses whose comments were wrong.
            //
            // TWO THINGS ARE DIFFERENT ON THIS BODY, and they decide the whole shape of these poses.
            //   1. THE ARM CHANNEL IS INVISIBLE HERE. An imported forge model gets EMPTY arm pivots
            //      (MiniBossFactory.BuildModelBody -- the auto-rig's bones sit inside the silhouette,
            //      so driving them at a telegraph pose tears the mesh) and its EnemyVisuals.weapon is a
            //      3 cm spark marker, not a blade. armWindup/armStrike below still swing the cue-spark
            //      origin and are kept honest for the day this body gets a real prop, but NOTHING the
            //      player sees comes from them. The silhouette is carried entirely by bodyOffset and
            //      bodyEuler, which move the whole skinned mesh through LungeRoot.
            //   2. THE CLIP CANNOT TELL THESE FOUR APART EITHER. PuppetVisuals has two attack clip
            //      slots, so slash and stab BOTH play AttackSwing and overhead and kick BOTH play
            //      AttackOverhead (the kick because it is unblockable, the overhead because its wind-up
            //      is >= 0.9). The FBX ships AttackStab and AttackKick and nothing plays them.
            // Before these poses, all four attacks were measured at IoU 1.00 against each other --
            // pixel-identical on screen, because the only channel the fallback moves is the body and it
            // moves it the same way for every attack. The four wind-ups were literally one shape.
            var revSlash = Attack("Revenant_Slash", a =>
            {
                // The bread-and-butter cut. 0.62 s is generous on purpose: this enemy exists to be
                // read, and a player who cannot yet hold the Marionette's 0.69 s cadence should be
                // able to deflect this one on sight.
                a.windup = 0.62f; a.impactDelay = 0.05f; a.strikeDuration = 0.16f; a.recovery = 0.55f;
                a.range = 3.4f; a.coneDeg = 95f; a.damage = 20f; a.lungeDistance = 0.9f;
                a.comboGap = 0.22f; a.parryPostureMultiplier = 1.3f;
                // SILHOUETTE, MEASURED: width 0.58 body-heights against the resting 0.90, level
                // (axis -1 deg), covering 0.74 of the resting area, centre 0.06 bh to the player's left.
                // THE NARROW ONE -- the body turns 38 deg and COILS, which is what a 95 deg horizontal
                // cut is wound from. It is the only wind-up that makes this enemy thinner than it
                // stands, and that shrink is the read: the wider the cut, the tighter the coil.
                Pose(a, new Vector3(-18f, -84f, -52f), new Vector3(24f, 74f, 30f),
                     new Vector3(0.10f, -0.10f, -0.18f), new Vector3(0f, 38f, 0f), 0.45f);
            });
            var revStab = Attack("Revenant_Stab", a =>
            {
                // The thrust. The forge clip brings both hands together and drives forward -- measured,
                // not assumed: AttackStab samples 0.81/0.19/0.24 m of hand separation, the only clip in
                // the set that CLOSES. Narrow cone to match what the animation actually does, so
                // stepping aside is a real answer to this one specifically.
                //
                // NOTE: when this comment was first written it was justifying a clip that NEVER PLAYED.
                // PuppetVisuals had two attack slots (swing / heavy) and AttackStab was unreachable, so
                // the measurement was real and the conclusion drawn from it was fiction. Fixed in
                // PuppetVisuals.ClipFor, which now resolves the stab and the kick by name BEFORE the
                // unblockable heuristic. Left here as a marker: a measured premise does not make a
                // claim true if the thing measured is not the thing running.
                a.windup = 0.55f; a.impactDelay = 0.04f; a.strikeDuration = 0.12f; a.recovery = 0.5f;
                a.range = 3.9f; a.coneDeg = 40f; a.damage = 24f; a.lungeDistance = 1.5f;
                a.comboGap = 0.2f; a.parryPostureMultiplier = 1.45f;
                // SILHOUETTE, MEASURED: height 1.19 bh and area 1.10 -- the BIGGEST and NEAREST shape
                // of the four, because the body drives 0.42 m at the camera while the top of the head
                // stays exactly where it was (dTop 0.00). It grows without rising: that is what
                // "it is coming down the middle" looks like, and it is the opposite read to the
                // overhead, which rises without growing.
                // Bladed the other way from the slash (-48 deg against +38), so the two attacks that
                // open a combo together lean off opposite shoulders.
                Pose(a, new Vector3(-64f, -14f, 0f), new Vector3(22f, 10f, 0f),
                     new Vector3(-0.06f, -0.08f, 0.42f), new Vector3(2f, -48f, 0f), 0.3f);
            });
            var revOverhead = Attack("Revenant_Overhead", a =>
            {
                // THE PUNISH WINDOW. A full-body overhead with 1.4 s of recovery -- by a wide margin
                // the biggest opening any enemy in the game offers, because this is where a new player
                // learns that a whiffed heavy is free damage.
                a.windup = 0.95f; a.impactDelay = 0.07f; a.strikeDuration = 0.22f; a.recovery = 1.4f;
                a.range = 3.6f; a.coneDeg = 70f; a.damage = 38f; a.lungeDistance = 1.3f;
                a.comboGap = 0.3f; a.parryPostureMultiplier = 1.8f;
                // SILHOUETTE, MEASURED: centre 0.19 bh UP and crown 0.16 bh up -- the ONLY wind-up in
                // the moveset that goes up at all; every other one drops. Squared to the player (no
                // yaw) and reared 14 deg, area 0.88.
                // The rear used to be 22 deg and 0.30 m of retreat, and it MEASURED 0.74 area, 0.90
                // height -- the biggest-damage attack drawing the smallest mark, which is exactly the
                // failure logged against Heavy_Overhead. Pitching back foreshortens a body the same way
                // it foreshortens a blade. Less lean and more lift buys the height without the shrink.
                Pose(a, new Vector3(-142f, 6f, -30f), new Vector3(66f, 0f, 18f),
                     new Vector3(0f, 0.40f, 0.06f), new Vector3(-14f, 0f, 0f), 0.55f);
            });
            var revKick = Attack("Revenant_Kick", a =>
            {
                // THE ANTI-TURTLE, and the only unblockable it has. A long-legged shove that answers a
                // player who simply holds guard: the pink alert tell means "steel will not answer this
                // one", and the honest reply is to step out of a 55 deg cone.
                a.windup = 0.7f; a.impactDelay = 0.05f; a.strikeDuration = 0.18f; a.recovery = 0.9f;
                a.range = 3.2f; a.coneDeg = 55f; a.damage = 18f; a.lungeDistance = 1.1f;
                a.comboGap = 0.28f; a.unblockable = true;
                // SILHOUETTE, MEASURED: axis 27 deg off vertical -- the ONLY tilted body in the game;
                // no other pose in any moveset rolls at all. Also the LOWEST (centre 0.21 bh down, crown
                // 0.23 bh down) and, after the roll spreads it, 0.94 bh wide. Its worst IoU against a
                // sibling is 0.32; against the overhead it is 0.10, which is as far apart as two poses
                // of one body get.
                // A canted, sunken, twisted body is what a long-legged shove is loaded from, and it is
                // the one shape here that cannot be mistaken for a sword coming.
                // The 16 deg of yaw is not decoration. At a pure 22 deg roll and no turn the frame read
                // as a body TOPPLING sideways rather than one loading a leg; the turn puts a hip behind
                // the lean and costs only 2 deg of measured tilt.
                // This ADDS to the pink M_AlertTell marker and the red cue tint; it does not replace
                // them. The answer to this attack is to MOVE, and it gets three separate reads.
                Pose(a, new Vector3(-40f, 34f, 18f), new Vector3(46f, -30f, -22f),
                     new Vector3(0.04f, -0.28f, 0.06f), new Vector3(10f, -16f, 18f), 0.35f);
            });

            var revenant = GetOrCreate<EnemyData>(EnemyPaths.Data("Legendary_Revenant"));
            revenant.displayName = "THE EMBER REVENANT";
            // Softer than the Marionette in every direction: more HP, less posture, slower. It is a
            // punching bag with a good silhouette, which is exactly what a test body should be.
            revenant.maxHP = 240f; revenant.maxPosture = 160f; revenant.postureRegen = 8f;
            revenant.postureRegenDelay = 2.5f; revenant.staggerSeconds = 4.5f;
            revenant.moveSpeed = 3.4f; revenant.turnSpeed = 220f; revenant.aggroRange = 20f;
            revenant.attackRange = 3.2f;
            revenant.attackCooldown = 0.75f;
            // parryRecoilSeconds x lerp(1, 0.55, 0.38) = 0.35 x 0.829 = 0.29 s. It is NOT held to the
            // Marionette's beat identity because this fight is not a cadence -- a visible stumble after
            // a deflect is the reward here, not a metronome that must not drift.
            revenant.parryRecoilSeconds = 0.35f; revenant.aggression = 0.38f;
            revenant.windupTurnMultiplier = 0.35f; revenant.stepSpeedMultiplier = 0.35f;
            revenant.stepAcceleration = 5f; revenant.stepDeadzone = 1f;
            revenant.comboBreathSeconds = 0.35f; revenant.readyDistanceMultiplier = 1.5f;
            // preferredRange 3.4 MUST stay >= attackRange 3.2. It was 3.1 and the test caught it: an
            // enemy that stands nearer than the distance it decides to attack from is permanently
            // inside its own commit band, so it never settles at a readable distance and every wind-up
            // starts on top of the player. Every attack still covers preferredRange + commitTolerance
            // (4.1 m) once its lunge is counted -- slash reaches 4.3, stab 5.4, overhead 4.9, kick 4.3.
            revenant.preferredRange = 3.4f; revenant.commitTolerance = 0.7f;
            revenant.repositionDeadzone = 0.5f;
            revenant.backStepSpeedMultiplier = 0.3f; revenant.strafeSpeedMultiplier = 0.28f;
            revenant.lungeMinDistance = 2.2f;
            revenant.soulValue = 400;
            // Charcoal body so the fire inside it has something to read against. EmberAura supplies the
            // glow; this is the UNLIT colour, and it is dark on purpose -- a bright body with a bright
            // aura is one flat shape.
            revenant.bodyColor = Hex("#2A2320"); revenant.emission = Hex("#FF7A1E") * 2.2f;
            // 1.0: the forge mesh already stands 2.13 m to the tips of its shoulder spikes. Scaling it
            // up would put the blade through the camera at preferredRange.
            revenant.scale = 1f;
            revenant.moveset = Moveset("Legendary_Revenant_Moveset", "The Ember Revenant", new[]
            {
                Entry("slash",                           3f,   0f,  6f, revSlash),
                Entry("slash, slash",                    2f,   0f,  6f, revSlash, revSlash),
                // The read: a wide cut, then the thrust down the middle. Two different cones back to
                // back, so sidestepping the first puts you in front of the second.
                Entry("slash into THRUST",               2f,   0f,  7f, revSlash, revStab),
                EntryCd("OVERHEAD (the big punish)",     1.5f, 0f,  6f, 6f, revOverhead),
                Entry("thrust from range",               1.5f, 3.5f, 8f, revStab),
                EntryCd("KICK (unblockable, anti-turtle)", 1.2f, 0f,  5f, 5f, revKick),
                EntryCd("slash into the kick",           1f,   0f,  5f, 5f, revSlash, revKick),
            });
            revenant.combos = revenant.moveset.ToComboArray();
            revenant.flaskPunishChance = 0.6f;
            EditorUtility.SetDirty(revenant);

            // --- Halberdier: THE ARGENT HALBERDIER. PROTOTYPE. ------------------------------------
            //
            // The third ai_skelly_tool body (output/a_towering_swift), and the first to ship GENERATED,
            // per-character clips (forge.py --motion) beside the four canonical AttackSwing/Overhead/
            // Stab/Kick every model carries. Sandbox pad only (x 0.75), like the other two prototypes;
            // in no LevelDefinition and no LevelRegistry.
            //
            // ===== WHAT IT IS FOR =================================================================
            // One question: does a forge model whose animation is its OWN drop in without the pipeline
            // learning its name? Two things had to change for the answer to be yes:
            //   1. EnemyAttackData.clip. A generated clip has no canonical name the pipeline could map
            //      (PuppetVisuals.ClipFor knows swing / heavy / _Stab / _Kick / the spin prefix), so the
            //      attack names it, and MiniBossFactory bakes every attack clip's length and contact
            //      frame onto the prefab so the clip still bends to the data.
            //   2. Root motion. The tool bakes a thrust's or a charge's pelvis travel onto the Hips
            //      bone. Its own Unity contract applies that as root motion through a component that
            //      moves the agent; this project does not let a clip move an enemy (the parry contract
            //      is data-driven), so the travel stays in the clip, PuppetVisuals cancels its XZ on a
            //      TravelRoot so the mesh stays over its collider, and the distance goes here, into
            //      lungeDistance, as data. HalberdierDataTests holds every lunge to the sampled clip.
            //
            // ===== GENERATED STRIKE CLIPS DO NOT STRIKE (2026-09-04, from play) ====================
            // "The animations don't line up with the attack hitboxes." Measured by sampling the halberd
            // TIP (the RightHand-weighted vertex 0.80 m out) through every clip at 5% steps: the four
            // AUTHORED strikes whip the tip from -1.4 m to +1.3 m at 36-86 m/s with the strike exactly
            // on the manifest's contact frame; the GENERATED "strike" clips move it at 1-8 m/s --
            // HalberdSweep drifts, HalberdBackswing and Thrust barely stir, OverheadSlam and HeavyWindup
            // END with the blade BEHIND him. Only Kick, LeapSlam, SpinSweep and ShoulderCharge have real
            // body action. So the five strikes were playing a wind-up with no blow in it, and the blow
            // landed at 3.8-4.7 m while the tip's whole reach from the hips is ~1.7 m at scale 1.15:
            // the hit arrived with the blade two metres short of the player.
            //
            // The fix is the mapping, not the numbers on the clips: sweep / thrust / slam now play the
            // AUTHORED AttackSwing / AttackStab / AttackOverhead, which do strike; the backswing and
            // the heavy, which had no striking clip left, are GONE rather than doubled onto a shared one
            // (MOVEMENT-PRINCIPLES rule 2: own the commitment -- a 0.5 s tell with no blow behind it is
            // exactly the bug). Seven attacks, seven animations. MiniBossFactory measures every
            // generated clip's tip at 4b and says "NO STRIKE" in the console for any that should not be
            // an attack; the four that keep their generated clips are the four it passes.
            //
            // ===== RANGES ARE THE BLADE'S =========================================================
            // Ranges came down from 3.8-4.7 to 2.6-3.0 for the three cuts (tip reach ~1.7 m + the
            // player's 0.4 m capsule + the impact test's own 0.5 m slack), 2.4 for the kick, 3.6 for the
            // all-round spin. The step INTO a cut is data (lungeDistance 1.0-1.3, run from the cue
            // like every lunge) because the authored strikes are rotation-only; the generated
            // travellers keep the clip's own sampled Hips travel: ShoulderCharge 4.70, LeapSlam 2.40,
            // Kick 0.32 (the sidecar's forward_m is ~1.3x smaller: it records SOURCE motion, the tool
            // scales it to the rig -- measure the clip, not the sidecar). The commit band is now
            // preferredRange 3.2 + commitTolerance 0.4 = 3.6 m and every attack's range + lunge covers
            // it; lungeMinDistance 1.2 so a 2.8 m cut actually arrives.
            //
            // ===== THE FIGHT ======================================================================
            // REACH THROUGH THE CHARGE. The Marionette is a cadence you hold and the Revenant a body
            // you read; this one answers DISTANCE. Inside 3.6 m it is three-hit strings by default
            // (sweep, thrust, SLAM is the signature: wide, narrow, then the punish); the kick for a
            // player who turtles inside the halberd; the spin for one circling behind. From 5 m out
            // the CHARGE is near-certain (EnemyController's far-band commit fires it, 2026-09-04) and
            // two of its entries chain straight into the string, so backing off buys a shoulder and
            // then a phrase, never a breath. The slam's recovery is the punish window.
            //
            // ===== AGGRESSIVE =====================================================================
            // EnemyController scales by aggression: recovery x lerp(1, 0.35), cooldown x lerp(1, 0.3),
            // combo gaps x lerp(1, 0.45), step-in x lerp(0.5, 1), and >= 0.5 keeps him swinging through
            // a deflect. At 0.85: recoveries x0.45, cooldown 0.3 -> 0.12 s, gaps x0.53. Nothing here
            // touches a wind-up: every tell is still >= 0.45 s and the cue still fires 0.28 s before
            // impact. Speed comes from DENSITY, never from a shorter tell. Every recovery below is
            // written for the aggressive enemy -- the slam's 2.2 s raw is 0.99 s in play.
            //
            // ===== WIND-UP SILHOUETTES: NOT AUTHORED, ON PURPOSE ===================================
            // Each attack plays a different clip, so the clip is the silhouette and the cone-derived
            // body lean is the fallback. Author a pose only if a photograph shows two aliasing.
            var halSweep = Attack("Halberdier_Sweep", a =>
            {
                // The bread and butter: the authored AttackSwing -- a real horizontal cut, tip at
                // 40+ m/s through the contact frame. 120 deg because a halberd sweep IS wide; stepping
                // aside is not the answer to this one, deflecting is. A 1.0 m step into it from the cue.
                a.clip = "AttackSwing";
                a.windup = 0.60f; a.impactDelay = 0.05f; a.strikeDuration = 0.16f; a.recovery = 0.55f;
                a.range = 2.9f; a.coneDeg = 120f; a.damage = 22f; a.lungeDistance = 1.0f;
                a.comboGap = 0.20f; a.parryPostureMultiplier = 1.3f;
            });
            var halThrust = Attack("Halberdier_Thrust", a =>
            {
                // Down the middle: the authored AttackStab, the one clip in the set whose hands CLOSE
                // and drive forward. Narrow cone to match, so a sidestep is a real answer to THIS one,
                // which is what makes sweep-then-thrust a read rather than a rhythm. The quick tell of
                // the set (0.50 s, clear of the floor) and the longest step (1.3 m): it comes at you.
                a.clip = "AttackStab";
                a.windup = 0.50f; a.impactDelay = 0.04f; a.strikeDuration = 0.12f; a.recovery = 0.55f;
                a.range = 2.7f; a.coneDeg = 36f; a.damage = 26f; a.lungeDistance = 1.3f;
                a.comboGap = 0.20f; a.parryPostureMultiplier = 1.45f;
            });
            var halSlam = Attack("Halberdier_Slam", a =>
            {
                // THE PUNISH WINDOW: the authored AttackOverhead, a full-body chop with a step in.
                // Recovery 2.2 RAW = 0.99 s in play at aggression 0.85 (x0.45) -- the biggest opening
                // this enemy offers, and HalberdierBehaviourTests holds the EFFECTIVE number above 0.9 s.
                // It inherited the role from the removed heavy, whose clip never struck.
                a.clip = "AttackOverhead";
                a.windup = 0.95f; a.impactDelay = 0.06f; a.strikeDuration = 0.20f; a.recovery = 2.2f;
                a.range = 2.8f; a.coneDeg = 60f; a.damage = 38f; a.lungeDistance = 1.1f;
                a.comboGap = 0.30f; a.parryPostureMultiplier = 1.8f;
            });
            var halCharge = Attack("Halberdier_Charge", a =>
            {
                // THE ANSWER TO DISTANCE, unblockable. "He needs to use his charge when you get too
                // far and then combo his attacks on you." A polearm fighter whose reply to a player
                // backing out of its band is to lower a shoulder and cover 4.7 m -- the clip's own
                // travel, the biggest lunge in the game -- and it is a COMBO OPENER. Gated to >= 5 m so
                // it is never thrown point-blank; EnemyController's far-band commit throws it from
                // anywhere the moveset's bands say (5 m to the aggro edge). Pink alert tell: steel does
                // not answer this, moving does.
                //
                // range 3.0: the shoulder connects at <= 3.5 m after the travel. The lunge stops
                // lungeMinDistance (1.2) short, so from 5 m the body ends 1.2 m out and from 8 m 3.3 m
                // out -- picked further than ~8.2 m it closes and whiffs, then the phrase continues
                // from wherever it stopped. It was 4.4, which "landed" from 9.5 m with the body 4.8 m
                // away: a hit with nothing touching the player, the complaint in a new costume.
                a.clip = "ShoulderCharge";
                a.windup = 0.80f; a.impactDelay = 0.06f; a.strikeDuration = 0.20f; a.recovery = 1.2f;
                a.range = 3.0f; a.coneDeg = 40f; a.damage = 24f; a.lungeDistance = 4.70f;   // the clip's travel
                a.comboGap = 0.26f; a.unblockable = true;
            });
            var halKick = Attack("Halberdier_Kick", a =>
            {
                // THE ANTI-TURTLE, unblockable: the generated Kick (the foot really goes 0.71 m
                // forward at the contact frame). Short, low, and it shoves. 2.4 m + the 0.32 m step:
                // it lands only on a player who is standing in the halberd's shadow.
                a.clip = "Kick";
                a.windup = 0.70f; a.impactDelay = 0.05f; a.strikeDuration = 0.16f; a.recovery = 0.85f;
                a.range = 3.3f; a.coneDeg = 50f; a.damage = 16f; a.lungeDistance = 0.32f;   // the clip's travel
                a.comboGap = 0.28f; a.unblockable = true;
            });
            var halLeap = Attack("Halberdier_Leap", a =>
            {
                // The far-band alternative to the charge: a leaping slam that visibly leaves the ground
                // (the lift is in the pose, the collider stays down) and covers 2.4 m. Long wind-up:
                // you see it coming from across the arena.
                a.clip = "LeapSlam";
                a.windup = 1.0f; a.impactDelay = 0.07f; a.strikeDuration = 0.22f; a.recovery = 1.5f;
                a.range = 2.8f; a.coneDeg = 75f; a.damage = 34f; a.lungeDistance = 2.40f;   // the clip's travel
                a.comboGap = 0.30f; a.parryPostureMultiplier = 1.6f;
            });
            var halSpin = Attack("Halberdier_Spin", a =>
            {
                // All-round: the generated SpinSweep, arms out (1.53 m span at the quarter mark),
                // 300 deg so it catches a player circling behind it. NOTE the name: PuppetVisuals'
                // spinAttackPrefix is EMPTY on this body, so this does not drive the whirl -- the whole
                // spin is in the clip. Stays put (the clip does not travel), so its reach is the range.
                a.clip = "SpinSweep";
                a.windup = 0.75f; a.impactDelay = 0.05f; a.strikeDuration = 0.20f; a.recovery = 0.90f;
                a.range = 3.7f; a.coneDeg = 300f; a.damage = 24f; a.lungeDistance = 0f;
                a.comboGap = 0.25f; a.parryPostureMultiplier = 1.4f;
            });

            var halberdier = GetOrCreate<EnemyData>(EnemyPaths.Data("Legendary_Halberdier"));
            halberdier.displayName = "THE ARGENT HALBERDIER";
            halberdier.maxHP = 230f; halberdier.maxPosture = 200f; halberdier.postureRegen = 7f;
            halberdier.postureRegenDelay = 3f;
            // Stagger 4.2 s: more than 1.5x the slam's 2.2 s raw recovery, so a broken posture is a
            // bigger reward than a whiffed slam (HalberdierDataTests holds the ratio).
            halberdier.staggerSeconds = 4.2f;
            // SWIFT. 5.8 m/s is the fastest chase in the roster (the Marionette is 5.0): a band he
            // fights from is only a threat if he can re-establish it.
            halberdier.moveSpeed = 5.8f; halberdier.turnSpeed = 320f; halberdier.aggroRange = 18f;
            halberdier.attackRange = 3.0f;
            halberdier.attackCooldown = 0.3f;
            halberdier.parryRecoilSeconds = 0.3f; halberdier.aggression = 0.85f;
            // windupTurnMultiplier stays LOW: a committed swing that tracked you would make the
            // sidestep (the answer to the thrust and the charge) stop working at exactly the moment the
            // aggression makes it matter most.
            halberdier.windupTurnMultiplier = 0.28f; halberdier.stepSpeedMultiplier = 0.55f;
            halberdier.stepAcceleration = 7f; halberdier.stepDeadzone = 0.95f;
            // 0.22 s of breath between phrases, the floor aggression cannot compress away.
            halberdier.comboBreathSeconds = 0.22f; halberdier.readyDistanceMultiplier = 1.4f;
            // The commit band is the BLADE's: 3.2 + 0.4 = 3.6 m, and every cut's range + step covers
            // it. "Reach" is no longer where he stands -- it is the 4.7 m he closes when you leave.
            halberdier.preferredRange = 3.2f; halberdier.commitTolerance = 0.4f;
            halberdier.repositionDeadzone = 0.45f;
            halberdier.backStepSpeedMultiplier = 0.3f; halberdier.strafeSpeedMultiplier = 0.3f;
            // 1.2 (the roster's usual): a 2.8 m cut has to arrive. It was 1.6 for the charge's sake,
            // and the charge's range now accounts for the stop instead.
            halberdier.lungeMinDistance = 1.2f;
            halberdier.soulValue = 450;
            // NEAR-WHITE body: the first forge model with an albedo texture (silver plate, gold trim),
            // and EnemyVisuals multiplies _BaseColor into it every frame -- so the tint has to be white
            // for the texture to show, and the wind-up sink still darkens it. Cold silver-blue accent
            // for the cue and the parry flash, over the bloom threshold.
            halberdier.bodyColor = Hex("#F2EFE8"); halberdier.emission = Hex("#8FD3FF") * 1.8f;
            // 1.15: "towering". The forge normalises every model to ~1.9 m; this one is 1.86 to the
            // horns and the whole point of it is height, so it ships at 2.14 m like the Marionette.
            halberdier.scale = 1.15f;
            // THE PHRASES. Inside the band, strings by default -- sweep, thrust, SLAM is the signature
            // (wide, narrow, then the punish) -- with the single cuts, the spin and the kick as the
            // tempo breaks. From 5 m out the CHARGE is the answer to distance, almost every time (11 of
            // 12.2 weight against the leap's 1.2, so the far band is not one animation), and two of its
            // three entries chain straight into pressure: charge, sweep, thrust / charge, slam. The
            // opener bands stop at 8 m because past ~8.2 m the shoulder cannot reach after its 4.7 m
            // (see the charge); the plain charge runs to the aggro edge as a CLOSER that may whiff and
            // still leaves him on top of you.
            halberdier.moveset = Moveset("Legendary_Halberdier_Moveset", "The Argent Halberdier", new[]
            {
                Entry("sweep, thrust, SLAM (the signature string)",       4f,   0f,   3.6f, halSweep, halThrust, halSlam),
                Entry("thrust, sweep, thrust",                            1.5f, 0f,   3.6f, halThrust, halSweep, halThrust),
                Entry("sweep, thrust (the fast pair)",                    2f,   0f,   3.6f, halSweep, halThrust),
                Entry("sweep",                                            1.2f, 0f,   3.6f, halSweep),
                Entry("thrust down the middle",                           1.2f, 0f,   3.6f, halThrust),
                EntryCd("OVERHEAD SLAM (the punish)",                     0.8f, 0f,   3.6f, 6f, halSlam),
                EntryCd("the SPIN (all round)",                           1f,   0f,   3.6f, 7f, halSpin),
                EntryCd("KICK (unblockable, anti-turtle)",                1.2f, 0f,   3.0f, 5f, halKick),
                Entry("CHARGE into sweep, THRUST (unblockable opener)",   5f,   5f,   8f,   halCharge, halSweep, halThrust),
                Entry("CHARGE into SLAM (unblockable opener)",            4f,   5f,   8f,   halCharge, halSlam),
                Entry("SHOULDER CHARGE (unblockable, to the aggro edge)", 2f,   5f,   18f,  halCharge),
                EntryCd("LEAP SLAM from range",                           1.2f, 4.5f, 8f, 6f, halLeap),
            });
            halberdier.combos = halberdier.moveset.ToComboArray();
            halberdier.flaskPunishChance = 0.6f;
            EditorUtility.SetDirty(halberdier);

            // ---- THE DRILLMASTER: the showcase duellist (2026-09-06) ----------------------------------
            // Sandbox pad only (x 14, z -26, the second row). One body that carries every soulslike
            // combat feature the report brought in, tuned so each one is SEEN inside one fight:
            //   * per-move cooldowns on every signature (EntryCd) -- nothing heavy comes twice running;
            //   * a DELAYED overhead: 1.25 s wind-up in a 0.5 s fight, the tempo break that eats rhythm parrying;
            //   * a feint whose pause is a lie (0.95 s) and an unblockable kick for the turtle;
            //   * flaskPunishChance 1.0 -- drink inside 18 m and it WILL come, every 4 s at most;
            //   * posture 150 with the near-break beat from 80%: four clean deflects and the bar is beating.
            // Knight silhouette at 1.3x, slate body, cold blue accent so it is never mistaken for the Penitent.
            var drillJab = Attack("Drill_Jab", a =>
            {
                a.windup = 0.5f; a.impactDelay = 0.05f; a.strikeDuration = 0.15f; a.recovery = 0.6f;
                a.range = 3.0f; a.coneDeg = 60f; a.damage = 16f; a.lungeDistance = 0.6f; a.comboGap = 0.2f;
            });
            var drillCross = Attack("Drill_Cross", a =>
            {
                a.windup = 0.55f; a.impactDelay = 0.05f; a.strikeDuration = 0.16f; a.recovery = 0.7f;
                a.range = 3.0f; a.coneDeg = 65f; a.damage = 18f; a.lungeDistance = 0.7f; a.comboGap = 0.22f;
                a.parryPostureMultiplier = 1.2f;
            });
            var drillOverhead = Attack("Drill_DelayedOverhead", a =>
            {
                // THE DELAYED ATTACK. The cue still fires 0.28 s before impact -- it punishes a player
                // parrying on the 0.5 s metronome, never one watching the blade. 1.6 s of recovery is the
                // reward for reading it.
                a.windup = 1.25f; a.impactDelay = 0.08f; a.strikeDuration = 0.2f; a.recovery = 1.6f;
                a.range = 3.2f; a.coneDeg = 50f; a.damage = 34f; a.lungeDistance = 0.8f; a.comboGap = 0.3f;
                a.parryPostureMultiplier = 1.6f;
            });
            var drillFeint = Attack("Drill_Feint", a =>
            {
                a.windup = 0.95f; a.impactDelay = 0.04f; a.strikeDuration = 0.12f; a.recovery = 0.4f;
                a.range = 2.8f; a.coneDeg = 60f; a.damage = 14f; a.lungeDistance = 0.5f; a.comboGap = 0.15f;
            });
            var drillKick = Attack("Drill_Kick", a =>
            {
                a.windup = 0.7f; a.impactDelay = 0.05f; a.strikeDuration = 0.15f; a.recovery = 0.9f;
                a.range = 2.6f; a.coneDeg = 45f; a.damage = 20f; a.lungeDistance = 0.9f; a.comboGap = 0.3f;
                a.unblockable = true;
            });
            var drillLunge = Attack("Drill_Lunge", a =>
            {
                a.windup = 0.8f; a.impactDelay = 0.05f; a.strikeDuration = 0.18f; a.recovery = 0.9f;
                a.range = 3.2f; a.coneDeg = 40f; a.damage = 24f; a.lungeDistance = 4.5f; a.comboGap = 0.25f;
            });

            var drill = GetOrCreate<EnemyData>(EnemyPaths.Data("Legendary_Drillmaster"));
            drill.displayName = "THE DRILLMASTER";
            drill.maxHP = 220f; drill.maxPosture = 150f; drill.postureRegen = 4f; drill.postureRegenDelay = 2.5f; drill.staggerSeconds = 4f;
            drill.moveSpeed = 4.0f; drill.turnSpeed = 260f; drill.aggroRange = 18f; drill.attackRange = 3.0f;
            drill.attackCooldown = 0.4f; drill.parryRecoilSeconds = 0.6f; drill.aggression = 0.6f;
            drill.windupTurnMultiplier = 0.3f; drill.stepSpeedMultiplier = 0.3f;
            drill.stepAcceleration = 5f; drill.stepDeadzone = 0.95f;
            drill.comboBreathSeconds = 0.25f; drill.readyDistanceMultiplier = 1.6f;
            drill.preferredRange = 3.8f; drill.commitTolerance = 0.8f; drill.repositionDeadzone = 0.5f;
            drill.backStepSpeedMultiplier = 0.3f; drill.strafeSpeedMultiplier = 0.3f; drill.lungeMinDistance = 1.4f;
            drill.soulValue = 500;
            drill.bodyColor = Hex("#2E3A44"); drill.emission = Hex("#4FB3FF") * 1.2f; drill.scale = 1.3f;
            drill.flaskPunishChance = 1.0f;
            drill.shootsProjectiles = false; drill.rangedOnly = false;
            drill.moveset = Moveset("Legendary_Drillmaster_Moveset", "The Drillmaster", new[]
            {
                Entry("jab-cross (the rhythm)",                          3f,   0f,   99f, drillJab, drillCross),
                Entry("jab-jab-cross (the longer rhythm)",               2f,   0f,   99f, drillJab, drillJab, drillCross),
                EntryCd("FEINT...cross (the pause is a lie)",            1.5f, 0f,   99f, 5f, drillFeint, drillCross),
                EntryCd("DELAYED OVERHEAD (the tempo break)",            1.5f, 0f,   99f, 7f, drillOverhead),
                EntryCd("jab-KICK (unblockable, anti-turtle)",           1.2f, 0f,   99f, 6f, drillJab, drillKick),
                EntryCd("LUNGE (closes from range)",                     2f,   4.5f, 9f,  4f, drillLunge),
                EntryCd("lunge-jab-DELAYED OVERHEAD (closes, then breaks tempo)", 1f, 4.5f, 9f, 8f, drillLunge, drillJab, drillOverhead),
            });
            drill.combos = drill.moveset.ToComboArray();
            EditorUtility.SetDirty(drill);

            // --- FlurryBrawler: THE FLURRY BRAWLER. PROTOTYPE. -------------------------------------
            //
            // The fourth ai_skelly_tool body and the roster's first FLURRY enemy. Sandbox pad only
            // (x -22, z -26, the second row); in no LevelDefinition and no LevelRegistry, like the
            // Marionette, the Revenant and the Halberdier.
            //
            // ===== ONE SENTENCE ===================================================================
            // An unarmed pressure fighter whose punch-strings come in a LADDER the player can hear --
            // two, four, eight -- so the reward for holding the beat one rung longer is a punish
            // window one rung bigger, and holding the longest rung clean breaks it outright.
            //
            // ===== WHAT CHANGED, AND WHY (v15 body, 2026-09-07) ===================================
            // The FBX was re-exported with 32 clips against the old 24. Eight are new: Hook, Slam,
            // Burst8, UppercutLeft, UppercutAlt, Clap, Combo1, Combo2. FIVE of them earn a slot below
            // and three do not -- the arithmetic that refuses each one is in
            // FlurryBrawlerDataTests.TheClipsThisFightRefuses_AreRefusedByArithmetic, not in a
            // comment, so a later pass cannot quietly adopt one without redoing it.
            //
            // The re-export also MOVED the body's travel, and that is not cosmetic:
            //   * ShoulderCharge USED to walk the Hips 3.90 m and was this fight's answer to distance.
            //     In v15 it does not move at all -- Blender start->end AND across the whole path reads
            //     fwd -0.00..+0.00 -- and its fists peak at 5-8 m/s, the slowest of any attack clip on
            //     the body. It is now a slow stationary lean with no strike in it, so it is DROPPED.
            //   * Slam is the only clip in v15 that both travels (1.48 m forward, 0.89 m right, with a
            //     sidecar airborne window) and carries an OnAttackHit. So Brawler_Charge keeps its
            //     name, its job and its unblockable and moves onto that clip, with the measured travel.
            //   * Jab1 no longer steps: 0.01 m forward against the 0.20 m it used to carry. Its lunge
            //     goes to 0 and its range goes up 0.15 m to cover the commit band instead.
            // Rule: the clip owns the travel and the data follows it. Never the other way round.
            //
            // ===== THE LADDER (the new design) ====================================================
            // Rung 1  ONE-TWO   Burst2   2 punches of art   0.50 s tell   x1.3 deflect   0.35 s opening
            // Rung 2  FLURRY    Burst4   4 punches          0.72 s tell   x2.0           0.86 s opening
            // Rung 3  BARRAGE   Burst8   8 punches          0.86 s tell   x2.6           1.10 s opening
            // Same beat, longer hold, bigger payoff, bigger punish. The player learns the rung from the
            // LENGTH OF THE TELL and knows how long to stay on the beat before the window opens. The
            // LOAD (UppercutAlt) is the announcer in front of rung 3: 1.05 s of rising arm, the longest
            // tell on the body, and it means eight beats are coming.
            //
            // ===== THE ECONOMY: ONE PHRASE BREAKS THE BAR =========================================
            // With the sword (parryPostureDamage 25, the calibration constant) the bar is 160 and:
            //   jab, cross, jab, FLURRY          = 25 x 5.45 = 136.25  -- a whole clean phrase, NOT a break
            //   ... plus any one more clean beat = 165.00              -- so it takes a phrase and a bit
            //   jab, cross, LOAD, BARRAGE        = 25 x 6.45 = 161.25  -- THE ONLY PHRASE THAT BREAKS IT
            // Exactly one of the eighteen entries below reaches the bar on its own, and it is the
            // longest climb in the fight. That is the whole design in one line: hold the top rung
            // clean and the bar breaks in your hand. 190 HP stays low on purpose -- block is worth zero
            // posture, so a player who cannot hold the beat still has the damage route.
            //
            // ===== THE BEAT, AND WHY IT CANNOT SHORTEN ============================================
            // A hit inside a combo arrives windup + gap + impactDelay + strikeDuration after the last,
            // where gap = max(0.10, comboGap x lerp(1, 0.45, aggression) - parryStreak x 0.03). At
            // aggression 0.80 the jab's 0.20 gap scales to 0.112, and ONE deflect takes it under the
            // 0.10 floor -- so the fastest beat is 0.732 s cold and 0.72 s for a player on a streak,
            // and it can never go below that however well the fight is going. Both clear the 0.69 s
            // parry contract floor, which stays the Pale Marionette's superlative.
            //
            // ===== RANGES ARE A FIST'S, NOT A BLADE'S =============================================
            // Measured on the v15 rig: RightHand rests at (0.35, 0.84, 0.00) and Jab1 throws it 1.06 m
            // forward of the Hips -- the longest reach of the three straight punches. With the player's
            // 0.4 m capsule and DoImpact's 0.5 m slack the honest reach is ~1.9-2.2 m. Every range is
            // 2.30-2.60 and the commit band is preferredRange 2.0 + commitTolerance 0.3 = 2.3, so every
            // attack must reach 2.30 with its own lunge or it whiffs when committed (EveryAttackReaches).
            // This enemy fights INSIDE the Halberdier's band, close enough that its shoulders fill the
            // frame, which is the whole read.
            //
            // ===== WIND-UP SILHOUETTES: NOT AUTHORED, ON PURPOSE ==================================
            // Twelve attacks, twelve clips: the clip is the silhouette and the cone-derived lean is the
            // fallback. Author a pose only if a photograph shows two aliasing -- and the three uppercut
            // variants are the trio most likely to, which is the first thing to look at in play.
            var brJab = Attack("Brawler_Jab", a =>
            {
                // THE BEAT. Lead hand, 0.46 s tell -- one hundredth over the 0.45 floor, because the
                // whole fight is this interval repeated and there is no room above it. Small damage: a
                // jab that hurt would make the string lethal rather than demanding.
                // v15: the clip no longer steps in (0.01 m of Hips travel against the old 0.20), so the
                // lunge is 0 and the RANGE carries the commit band instead. That is the honest edit --
                // a lunge the art does not perform is the body sliding.
                a.clip = "Jab1";
                a.windup = 0.46f; a.impactDelay = 0.04f; a.strikeDuration = 0.12f; a.recovery = 0.45f;
                a.range = 2.35f; a.coneDeg = 45f; a.damage = 11f; a.lungeDistance = 0f;
                a.comboGap = 0.20f; a.parryPostureMultiplier = 1.15f;
            });
            var brCross = Attack("Brawler_Cross", a =>
            {
                // The rear hand, thrown across the body: a WIDER cone than the jab (65 vs 45) so
                // sidestepping the jab does not also answer the cross. The clip steps BACKWARD 0.10 m,
                // and ApplyLunge has no reverse channel, so it ships 0.
                a.clip = "Jab2";
                a.windup = 0.48f; a.impactDelay = 0.04f; a.strikeDuration = 0.12f; a.recovery = 0.50f;
                a.range = 2.35f; a.coneDeg = 65f; a.damage = 14f; a.lungeDistance = 0f;
                a.comboGap = 0.20f; a.parryPostureMultiplier = 1.15f;
            });
            var brUpper = Attack("Brawler_Uppercut", a =>
            {
                // THE RIGHT ENDER. The rising blow that closes the jab-jab-cross string. Half a beat
                // slower than the jab and a narrower cone: it comes UP the middle, so it is the one hit
                // in the string a player crowding inside the guard eats first.
                a.clip = "Uppercut";
                a.windup = 0.55f; a.impactDelay = 0.05f; a.strikeDuration = 0.14f; a.recovery = 0.70f;
                a.range = 2.35f; a.coneDeg = 50f; a.damage = 19f; a.lungeDistance = 0f;
                a.comboGap = 0.22f; a.parryPostureMultiplier = 1.4f;
            });
            var brUpperL = Attack("Brawler_UppercutLeft", a =>
            {
                // THE MIRROR, and the NARROWEST cone in the fight (40 deg). Its job is not to be a
                // second uppercut -- it is the hit that punishes a player who has learned to answer
                // every ender by drifting to the same side. Step off the centre line and this one
                // genuinely misses, where the 50 deg right uppercut still catches you. Faster and
                // cheaper than the right so it can also sit INSIDE a string rather than only ending
                // one. Clip UppercutLeft: left wrist, and its hop lands AFTER the blow rather than
                // before it, so the two uppercuts do not share a rhythm either.
                a.clip = "UppercutLeft";
                a.windup = 0.50f; a.impactDelay = 0.05f; a.strikeDuration = 0.13f; a.recovery = 0.55f;
                a.range = 2.35f; a.coneDeg = 40f; a.damage = 16f; a.lungeDistance = 0f;
                a.comboGap = 0.20f; a.parryPostureMultiplier = 1.25f;
            });
            var brLoad = Attack("Brawler_UppercutLoad", a =>
            {
                // THE ANNOUNCER. 1.05 s of rising arm -- the longest tell on this body -- and it is
                // thrown for one reason: it is what comes in front of the BARRAGE. See the long rise
                // and eight beats are coming; miss it and you are holding a rhythm you did not know
                // was going to run that far. UppercutAlt's contact sits dead centre of a 2.13 s clip,
                // so at this wind-up the art plays at x0.96 -- almost its authored rate, the least
                // stretched attack in the set.
                a.clip = "UppercutAlt";
                a.windup = 1.05f; a.impactDelay = 0.06f; a.strikeDuration = 0.18f; a.recovery = 0.90f;
                a.range = 2.40f; a.coneDeg = 55f; a.damage = 22f; a.lungeDistance = 0f;
                a.comboGap = 0.34f; a.parryPostureMultiplier = 1.55f;
            });
            var brOneTwo = Attack("Brawler_OneTwo", a =>
            {
                // LADDER RUNG 1. ONE ATTACK, TWO PUNCHES OF ART. Burst2 throws a lead hand and then
                // drives a right straight through the contact frame; the first punch is the
                // anticipation and the second is the blow, so the tell is a punch rather than a pose.
                // The cue still fires 0.28 s before the blow that counts, which is the only thing the
                // parry rides on (ARCHITECTURE -> the Marionette: "the parry rides entirely on the cue
                // flash"). v15 shortened the clip to 0.58 s with its contact at 0.65, so it plays at
                // x0.69 rather than v14's x1.00 -- the tell reads a shade heavier, which suits the
                // bottom of a ladder.
                a.clip = "Burst2";
                a.windup = 0.50f; a.impactDelay = 0.05f; a.strikeDuration = 0.14f; a.recovery = 0.60f;
                a.range = 2.40f; a.coneDeg = 55f; a.damage = 17f; a.lungeDistance = 0f;
                a.comboGap = 0.20f; a.parryPostureMultiplier = 1.3f;
            });
            var brFlurry = Attack("Brawler_Flurry", a =>
            {
                // LADDER RUNG 2. Four punches of art, one blow, a 110 deg cone you cannot walk around,
                // and 1.80 s of raw recovery -- 0.86 s in play. Until v15 this was the fight's only
                // real opening; it is now the MIDDLE one, which is the point of building a ladder.
                a.clip = "Burst4";
                a.windup = 0.72f; a.impactDelay = 0.05f; a.strikeDuration = 0.24f; a.recovery = 1.80f;
                a.range = 2.60f; a.coneDeg = 110f; a.damage = 24f; a.lungeDistance = 0f;
                a.comboGap = 0.30f; a.parryPostureMultiplier = 2.0f;
            });
            var brBarrage = Attack("Brawler_Barrage", a =>
            {
                // LADDER RUNG 3, and the biggest opening in the fight. Burst8: eight punches of art,
                // the widest arc the body has (1.60 m against Burst4's 0.30), a crouch and a hop
                // inside it, and one blow at 0.43 of a 2.42 s clip. 2.30 s of raw recovery is 1.10 s
                // in play -- two and a half sword swings, against the flurry's one and a half -- and
                // x2.6 is the biggest deflect payoff any mini-boss pays. It is expensive on purpose:
                // it is the end of the longest thing this fight ever asks you to hold.
                a.clip = "Burst8";
                a.windup = 0.86f; a.impactDelay = 0.06f; a.strikeDuration = 0.22f; a.recovery = 2.30f;
                a.range = 2.60f; a.coneDeg = 120f; a.damage = 32f; a.lungeDistance = 0f;
                a.comboGap = 0.32f; a.parryPostureMultiplier = 2.6f;
            });
            var brClap = Attack("Brawler_Clap", a =>
            {
                // THE ANSWER TO CIRCLING. windupTurnMultiplier is 0.30 on purpose -- a string that
                // tracked you would kill circling, which is the honest answer to a wide flurry -- so
                // the fight needs ONE hit that circling does not answer. Both arms swing in and close
                // (the clip's sweep is horizontal with right -0.91: a lateral clap, not a punch), and
                // at 150 deg it is the widest cone in the game. You deflect it or you are out of the
                // band; walking around it is not a third option.
                a.clip = "Clap";
                a.windup = 0.65f; a.impactDelay = 0.06f; a.strikeDuration = 0.20f; a.recovery = 1.10f;
                a.range = 2.55f; a.coneDeg = 150f; a.damage = 21f; a.lungeDistance = 0f;
                a.comboGap = 0.28f; a.parryPostureMultiplier = 1.9f;
            });
            var brHammer = Attack("Brawler_Hammerfist", a =>
            {
                // THE TEMPO BREAK, and the one attack on an AUTHORED clip chosen for exactly that:
                // AttackOverhead is 0.92 s of full-body chop where every punch clip here is under
                // 0.60, so the break is visible in the silhouette and not only in the count. 0.95 s of
                // wind-up dropped into a 0.46 s rhythm is what punishes a player parrying the beat
                // instead of the body. The step into it is data (0.55 m): the authored clip is
                // rotation-only, exactly as on the Halberdier's three authored cuts.
                a.clip = "AttackOverhead";
                a.windup = 0.95f; a.impactDelay = 0.06f; a.strikeDuration = 0.20f; a.recovery = 1.40f;
                a.range = 2.45f; a.coneDeg = 70f; a.damage = 34f; a.lungeDistance = 0.55f;
                a.comboGap = 0.30f; a.parryPostureMultiplier = 1.8f;
            });
            var brKick = Attack("Brawler_Kick", a =>
            {
                // THE ANTI-TURTLE, unblockable. Blocking a flurry is the obvious cheese -- it costs the
                // enemy no posture, so a turtled player could sit inside the string forever. This is
                // the price of that and only that: short, cheap, and it lands on nobody who is moving.
                a.clip = "AttackKick";
                a.windup = 0.70f; a.impactDelay = 0.05f; a.strikeDuration = 0.16f; a.recovery = 0.95f;
                a.range = 2.45f; a.coneDeg = 55f; a.damage = 16f; a.lungeDistance = 0.45f;
                a.comboGap = 0.28f; a.unblockable = true;
            });
            var brCharge = Attack("Brawler_Charge", a =>
            {
                // THE ANSWER TO DISTANCE, unblockable, and a COMBO OPENER -- the same job it has always
                // had, on the only clip in v15 that can still do it.
                //
                // THE CLIP CHANGED, AND THAT IS THE WHOLE STORY. ShoulderCharge carried 3.90 m of Hips
                // travel in v14; in v15 it carries none (Blender, over the full path and not just
                // start->end: fwd -0.00..+0.00) and its fists peak at 5-8 m/s. Slam is the only v15
                // clip that both TRAVELS and strikes: 1.48 m forward, 0.89 m right, a sidecar airborne
                // window, contact at 0.31 of a 1.79 s clip. So this is a leap-in now, not a shoulder
                // run, and it closes a metre and a half rather than four.
                //
                // 1.55 is a DELIBERATE half-step above the Blender number. Three figures exist for one
                // motion and they never agree:
                //   clips.json forward_m  1.222  (SOURCE travel, before the export scales it)
                //   Blender on the FBX    1.48   (Tools/measure_forge_fbx.py, start->end Hips XZ)
                //   Unity's imported clip  ?     (what EveryLungeIsTheClipsOwnTravel actually reads)
                // On the Halberdier, Unity read 6% over Blender (3.68 -> 3.90). 1.55 sits between 1.48
                // and 1.57, so it is inside the test's 0.15 m tolerance whichever way the import lands.
                // If the test still fails it PRINTS Unity's number -- paste that here and re-run 3.
                a.clip = "Slam";
                a.windup = 0.62f; a.impactDelay = 0.05f; a.strikeDuration = 0.20f; a.recovery = 1.10f;
                a.range = 2.60f; a.coneDeg = 45f; a.damage = 22f; a.lungeDistance = 1.55f;
                a.comboGap = 0.26f; a.unblockable = true;
            });

            var brawler = GetOrCreate<EnemyData>(EnemyPaths.Data("Legendary_FlurryBrawler"));
            brawler.displayName = "THE FLURRY BRAWLER";
            // 190 HP: low for a duellist on purpose (the Marionette's 170 is the precedent), because the
            // block-and-punish route has to be a real way to win for a player who cannot hold the beat.
            // 160 posture is the ladder's top rung plus its two set-up beats -- see the economy above.
            brawler.maxHP = 190f; brawler.maxPosture = 160f; brawler.postureRegen = 6f;
            brawler.postureRegenDelay = 3f;
            // Stagger 4.0 s: 1.7x the barrage's 2.30 s recovery, so breaking it is still unmistakably a
            // bigger prize than catching the end of the longest phrase.
            brawler.staggerSeconds = 4f;
            // 5.2 m/s: quick, but NOT the Halberdier's 5.8 -- that body's whole claim is the chase. This
            // one closed with a 3.9 m charge until v15 took the travel out of the clip; it now closes
            // with a 1.5 m leap and with its feet, which makes the walk-in part of the read.
            brawler.moveSpeed = 5.2f; brawler.turnSpeed = 340f; brawler.aggroRange = 16f;
            brawler.attackRange = 1.9f;
            brawler.attackCooldown = 0.25f;
            brawler.parryRecoilSeconds = 0.28f; brawler.aggression = 0.80f;
            // LOW windup turn: a string that tracked you would make circling -- the answer to the wide
            // flurry -- stop working at exactly the moment volume makes it matter. The CLAP is the one
            // hit that answers circling instead, which is why it can afford to be this wide.
            brawler.windupTurnMultiplier = 0.30f; brawler.stepSpeedMultiplier = 0.60f;
            brawler.stepAcceleration = 8f; brawler.stepDeadzone = 0.90f;
            // 0.35 s of breath, the floor aggression may not compress. Bigger than the Halberdier's
            // 0.22 because a four-beat string with no breath after it is a wall, not a phrase.
            brawler.comboBreathSeconds = 0.35f; brawler.readyDistanceMultiplier = 1.5f;
            brawler.preferredRange = 2.0f; brawler.commitTolerance = 0.3f;
            brawler.repositionDeadzone = 0.40f;
            // It CIRCLES rather than backs off: strafe 0.45 against the roster's 0.3, backstep 0.35.
            brawler.backStepSpeedMultiplier = 0.35f; brawler.strafeSpeedMultiplier = 0.45f;
            // 0.9 m: the leap-in stops that far short, which clears both capsules (enemy 0.45 + player
            // 0.4). From the near edge of its 2.6 m band a 1.55 m leap ends at 1.05 m, so the floor
            // never binds in practice -- it is the guard, not the design.
            brawler.lungeMinDistance = 0.9f;
            brawler.soulValue = 480;
            // NEAR-WHITE body: it ships an albedo texture like the Halberdier, and EnemyVisuals
            // multiplies _BaseColor into it every frame, so the tint has to stay white or it stains the
            // paint. ACID GREEN accent, over the bloom threshold and a hue nothing else in the roster
            // owns (the Ninja and the Marionette are teal, the Halberdier and the Drillmaster cold
            // blue, the Revenant and the Penitent ember, the Chorister violet).
            brawler.bodyColor = Hex("#F1EDE4"); brawler.emission = Hex("#B6FF3C") * 1.9f;
            // 1.0. The v15 body measures 1.18 m across the shoulders and 1.95 m tall in its rest pose
            // (v14's 2.02 m width was a T-posed bind; this export rests with the arms down). It reads
            // by CLOSENESS and by how much of the frame the strings fill at 2.0 m, not by height, and
            // a brawler that towers stops being a brawler.
            brawler.scale = 1f;
            brawler.flaskPunishChance = 0.7f;
            brawler.shootsProjectiles = false; brawler.rangedOnly = false;
            // THE PHRASES. Inside 3.4 m it is STRINGS, and the ladder runs through them: one-two, then
            // the flurry, then LOAD + BARRAGE at the top on an 11-13 s cooldown so the climb stays an
            // event. The tempo breaks are the hammerfist and the clap, both cooled so neither becomes
            // the rhythm; the kick is gated to 2.8 m because it only answers a player standing inside
            // the guard. From 2.6 m the LEAP-IN is the answer to distance and two of its three entries
            // chain straight into pressure. Its band tops out at 4.5 m -- range 2.60 + travel 1.55 +
            // DoImpact's 0.5 m slack is 4.65, so it is never thrown from a distance it cannot cover.
            brawler.moveset = Moveset("Legendary_FlurryBrawler_Moveset", "The Flurry Brawler", new[]
            {
                Entry("jab, cross (the beat)",                            3f,   0f,   3.4f, brJab, brCross),
                Entry("jab, cross, jab, FLURRY (the signature string)",   3.5f, 0f,   3.4f, brJab, brCross, brJab, brFlurry),
                Entry("jab, jab, cross, UPPERCUT (the right ender)",      2f,   0f,   3.4f, brJab, brJab, brCross, brUpper),
                Entry("cross, jab, UPPERCUT-LEFT (the mirror ender)",     2f,   0f,   3.4f, brCross, brJab, brUpperL),
                Entry("one-two (rung 1: the fast pair)",                  1.5f, 0f,   3.4f, brOneTwo),
                Entry("cross, one-two",                                   1.2f, 0f,   3.4f, brCross, brOneTwo),
                EntryCd("FLURRY alone (rung 2: the punish window)",       1f,   0f,   3.4f, 5f,  brFlurry),
                EntryCd("LOAD, BARRAGE (rung 3: the top of the ladder)",  1.6f, 0f,   3.4f, 11f, brLoad, brBarrage),
                EntryCd("jab, cross, LOAD, BARRAGE (the full climb -- deflected clean this IS the break)",
                                                                          1.2f, 0f,   3.4f, 13f, brJab, brCross, brLoad, brBarrage),
                EntryCd("jab, jab, HAMMERFIST (fast, fast, SLOW)",        1.5f, 0f,   3.4f, 7f,  brJab, brJab, brHammer),
                EntryCd("HAMMERFIST (the tempo break)",                   1f,   0f,   3.4f, 6f,  brHammer),
                EntryCd("CLAP (you cannot walk around this one)",         1.4f, 0f,   3.4f, 6f,  brClap),
                EntryCd("jab, cross, CLAP (the circle closes)",           1f,   0f,   3.4f, 8f,  brJab, brCross, brClap),
                EntryCd("KICK (unblockable, anti-turtle)",                1.2f, 0f,   2.8f, 5f,  brKick),
                EntryCd("cross into the KICK",                            1f,   0f,   2.8f, 5f,  brCross, brKick),
                Entry("LEAP-IN into jab, cross (unblockable opener)",     4f,   2.6f, 4.5f, brCharge, brJab, brCross),
                Entry("LEAP-IN into the FLURRY",                          3f,   2.6f, 4.5f, brCharge, brFlurry),
                Entry("LEAP-IN (the close)",                              1.5f, 2.6f, 4.5f, brCharge),
            });
            brawler.combos = brawler.moveset.ToComboArray();
            EditorUtility.SetDirty(brawler);

            // --- FlurryBrawlerV18: ADDITIVE SANDBOX TEST BODY. ------------------------------------
            // This does not replace or retune Legendary_FlurryBrawler (the shipped v15 prototype above).
            // It exists to judge the v18 export's tightly filtered animation vocabulary in a clean pad.
            // Source FBX SHA-256: EC0328191967144DDEBEF1185124A35FA40AF7590DAE24052791B4A7D765BCC2.
            // Source manifest SHA-256: 269725080628FFED78EEE3ACA01A44F6C15B6C50A054998BEE374FA24220ADB8.
            var br18Swing = Attack("BrawlerV18_Swing", a =>
            {
                a.clip = "AttackSwing";
                a.windup = 0.55f; a.impactDelay = 0.05f; a.strikeDuration = 0.20f; a.recovery = 0.75f;
                a.range = 2.45f; a.coneDeg = 70f; a.damage = 18f; a.lungeDistance = 0f;
                a.comboGap = 0.22f; a.parryPostureMultiplier = 1.15f;
            });
            var br18Overhead = Attack("BrawlerV18_Overhead", a =>
            {
                a.clip = "AttackOverhead";
                a.windup = 0.95f; a.impactDelay = 0.08f; a.strikeDuration = 0.28f; a.recovery = 1.00f;
                a.range = 2.55f; a.coneDeg = 70f; a.damage = 34f; a.lungeDistance = 0f;
                a.comboGap = 0.30f; a.parryPostureMultiplier = 1.8f;
            });
            var br18Stab = Attack("BrawlerV18_Stab", a =>
            {
                a.clip = "AttackStab";
                a.windup = 0.55f; a.impactDelay = 0.05f; a.strikeDuration = 0.16f; a.recovery = 0.70f;
                a.range = 2.45f; a.coneDeg = 40f; a.damage = 20f; a.lungeDistance = 0f;
                a.comboGap = 0.22f; a.parryPostureMultiplier = 1.4f;
            });
            var br18Kick = Attack("BrawlerV18_Kick", a =>
            {
                a.clip = "AttackKick";
                a.windup = 0.65f; a.impactDelay = 0.05f; a.strikeDuration = 0.22f; a.recovery = 0.90f;
                a.range = 2.45f; a.coneDeg = 55f; a.damage = 18f; a.lungeDistance = 0f;
                a.comboGap = 0.28f; a.unblockable = true;
            });
            var br18Jab2 = Attack("BrawlerV18_Jab2", a =>
            {
                a.clip = "Jab2";
                a.windup = 0.50f; a.impactDelay = 0.05f; a.strikeDuration = 0.16f; a.recovery = 0.70f;
                a.range = 2.35f; a.coneDeg = 65f; a.damage = 14f; a.lungeDistance = 0f;
                a.comboGap = 0.20f; a.parryPostureMultiplier = 1.15f;
            });
            var br18Dash = Attack("BrawlerV18_Dash", a =>
            {
                // Dash has no OnAttackHit event in the source; 4b bakes its explicit 0.60 profile.
                a.clip = "Dash";
                a.windup = 0.60f; a.impactDelay = 0.05f; a.strikeDuration = 0.22f; a.recovery = 1.00f;
                a.range = 2.60f; a.coneDeg = 45f; a.damage = 22f; a.lungeDistance = 1.77f;
                a.comboGap = 0.26f; a.unblockable = true;
            });
            var br18Shoulder = Attack("BrawlerV18_ShoulderCharge", a =>
            {
                a.clip = "ShoulderCharge";
                a.windup = 1.00f; a.impactDelay = 0.08f; a.strikeDuration = 0.28f; a.recovery = 1.10f;
                a.range = 2.70f; a.coneDeg = 50f; a.damage = 30f; a.lungeDistance = 4.12f;
                a.comboGap = 0.32f; a.unblockable = true;
            });
            var br18Clap = Attack("BrawlerV18_LevitateClap", a =>
            {
                // The LungeRoot rises during the two-second Idle hold. Strike begins 0.20 s before the
                // impact and EnemyVisuals' first 40% of the 0.50 s strike returns it to base: touchdown
                // therefore lands exactly on the one data-scheduled blast, never on an AnimationEvent.
                a.clip = "Clap";
                a.windup = 2.00f; a.impactDelay = 0.20f; a.strikeDuration = 0.30f; a.recovery = 1.50f;
                a.range = 3.60f; a.coneDeg = 180f; a.damage = 28f; a.lungeDistance = 0f;
                a.comboGap = 0.35f; a.parryPostureMultiplier = 2.0f;
                Pose(a, Vector3.zero, Vector3.zero, new Vector3(0f, 1.60f, 0f), Vector3.zero, 0.45f);
            });
            var br18Combo2 = Attack("BrawlerV18_Combo2", a =>
            {
                // The source performance contains several gestures, but this TEST declares exactly one
                // contact. EnemyController owns that one impact; no AnimationEvent creates extra hits.
                a.clip = "Combo2";
                a.windup = 1.50f; a.impactDelay = 0.05f; a.strikeDuration = 0.20f; a.recovery = 4.60f;
                a.range = 2.50f; a.coneDeg = 70f; a.damage = 20f; a.lungeDistance = 0f;
                a.comboGap = 0.25f; a.parryPostureMultiplier = 1.25f; a.unblockable = false;
            });

            var brawler18 = GetOrCreate<EnemyData>(EnemyPaths.Data(FlurryBrawlerV18Authoring.EnemyName));
            brawler18.displayName = "THE FLURRY BRAWLER V18 (TEST)";
            // Initial comparison holds the v15 vitals exactly. Everything else remains a separate asset.
            brawler18.maxHP = 190f; brawler18.maxPosture = 160f; brawler18.postureRegen = 6f;
            brawler18.postureRegenDelay = 3f; brawler18.staggerSeconds = 4f;
            brawler18.moveSpeed = 5.2f; brawler18.turnSpeed = 340f; brawler18.aggroRange = 18f;
            brawler18.attackRange = 1.9f; brawler18.attackCooldown = 0.25f;
            brawler18.parryRecoilSeconds = 0.28f; brawler18.aggression = 0.80f;
            brawler18.windupTurnMultiplier = 0.30f; brawler18.stepSpeedMultiplier = 0.60f;
            brawler18.stepAcceleration = 8f; brawler18.stepDeadzone = 0.90f;
            brawler18.comboBreathSeconds = 0.35f; brawler18.readyDistanceMultiplier = 1.5f;
            brawler18.preferredRange = 2.0f; brawler18.commitTolerance = 0.3f;
            brawler18.repositionDeadzone = 0.40f;
            brawler18.backStepSpeedMultiplier = 0.35f; brawler18.strafeSpeedMultiplier = 0.45f;
            brawler18.lungeMinDistance = 0.9f; brawler18.soulValue = 480;
            brawler18.bodyColor = Hex("#F1EDE4"); brawler18.emission = Hex("#B6FF3C") * 1.9f;
            brawler18.scale = 1f; brawler18.flaskPunishChance = 0.7f;
            brawler18.shootsProjectiles = false; brawler18.rangedOnly = false;
            brawler18.moveset = Moveset("Legendary_FlurryBrawlerV18_Moveset", "The Flurry Brawler V18 Test", new[]
            {
                Entry("jab-two into swing",                                  3f,   0f,   3.4f, br18Jab2, br18Swing),
                Entry("stab",                                                1.4f, 0f,   3.4f, br18Stab),
                EntryCd("OVERHEAD tempo break",                              1.2f, 0f,   3.4f, 6f, br18Overhead),
                EntryCd("KICK anti-turtle",                                  1.2f, 0f,   2.8f, 5f, br18Kick),
                EntryCd("LEVITATE CLAP wide blast",                          1.1f, 0f,   3.8f, 8f, br18Clap),
                Entry("DASH close",                                          2.6f, 2.6f, 4.7f, br18Dash),
                EntryCd("SHOULDER CHARGE far close",                         2.4f, 4.4f, 7.1f, 7f, br18Shoulder),
                EntryCd("COMBO2 held performance (one contact)",              0.45f, 0f, 2.7f, 8f, br18Combo2),
            });
            brawler18.combos = brawler18.moveset.ToComboArray();
            EditorUtility.SetDirty(brawler18);

            // ---------------- Weapons ----------------
            //
            // THREE ARCHETYPES, ONE LADDER. The dagger pass collapsed every weapon into one silhouette
            // and one tempo because a long blade at 95° FOV filled the frame. Length was never what
            // broke the frame — POSE was (see PrefabFactory.BuildWeaponViewmodels), so the framing rules
            // are now enforced by measurement and the numbers are free to spread again. The ladder is
            // deliberately a clean doubling on every axis, so the difference is felt and not just read:
            //
            //             extent above fist   reach (offset+radius)   swing      combo
            //   Rosethorn      0.32 m              1.30 + 0.90         0.22 s      4
            //   Cerulean Edge  0.62 m              2.10 + 1.15         0.44 s      3
            //   Verdigris      0.72 m              2.50 + 1.70         0.86 s      2
            //
            // Every step is roughly x2 in swing time and +0.4-0.8 m of reach. A player who swaps
            // weapons should notice inside one swing, without reading a stat.
            //
            // THAT LADDER IS SHAPE. THIS IS THE JOB (2026-09-06). A ladder alone is not a roster: with
            // the old numbers the sword and the maul had the SAME throughput (73.9 vs 72.2 health/s,
            // 36.4 vs 39.5 posture/s), so the maul was a slow sword with more reach, and the needle was
            // last on BOTH — the starting-weapon failure, applied to the third slot. The fix is not a
            // fourth rung, it is a CROSSING: the two posture channels now run in OPPOSITE directions to
            // health damage, so each weapon wins one column outright and loses another outright.
            //
            //                    health/s   HIT-posture/s   deflect-posture   parry window   reach
            //   Rosethorn          44.0         59.1              18             x1.35       2.20 m
            //   Cerulean Edge      73.9         36.4              25             x1.00       3.25 m
            //   Verdigris          81.6         30.2              40             x0.75       4.20 m
            //
            //   ROSETHORN  — THE BREAKER. Kills slowest of anything in the game and BREAKS fastest, by
            //                a wide margin: it is the only weapon whose swings take a Grunt's posture
            //                down before its health (5 hits to break, ~7 to kill), so with the needle
            //                the deathblow is the kill, not a bonus. Widest parry window (x1.35) and
            //                the cheapest commitment in the game (0.22 s, contact at 0.06) — which is
            //                also what makes it the speedrun weapon: it takes the least time out of a
            //                line. It pays for all of it with pyreBonus -5 and the lowest deflect
            //                posture. Deletion test: lose the needle and nothing else breaks by hitting.
            //   CERULEAN EDGE — THE INSTRUMENT. Deliberately unchanged by this pass, to the number. It
            //                is the middle of every column, and its parryPostureDamage 25 is the
            //                calibration constant two boss fights are built on. Its job is to be the
            //                thing the other two are deviations from, and a generalist that keeps
            //                moving is a real job in a speedrun platformer.
            //   SUNBREAKER — THE CRUSHER, and the DEFLECT weapon. Health-damage crown (52 a swing, 88
            //                on the finisher — twice a sword hit) and the longest reach, so it kills a
            //                span sentry without leaving the line. Worst hit-posture in the set: a maul
            //                caves a body in, it does not out-fence it. It breaks through the PARRY
            //                instead — 40 a deflect, the crown, behind the narrowest window in the game
            //                (x0.75). The two ways of breaking an enemy are now split across two
            //                weapons, and both of them are parry-first.
            var sword = GetOrCreate<WeaponData>(WeaponsDir + "/Sword.asset");
            sword.displayName = "Cerulean Edge";
            sword.neon = Hex("#8FB5D9");
            sword.baseDamage = 26f; sword.postureDamage = 16f; sword.executeDamage = 300f;
            sword.strScale = 0.5f; sword.dexScale = 0.5f; sword.arcScale = 0.2f;
            sword.comboLength = 3; sword.comboMultipliers = new[] { 1f, 1.15f, 1.6f };
            sword.attackDuration = 0.44f; sword.hitDelay = 0.15f; sword.comboWindow = 0.36f;
            sword.hitOffset = 2.1f; sword.hitRadius = 1.15f; sword.hitStopSeconds = 0.06f;
            // parryPostureDamage STAYS AT 25. It is load-bearing arithmetic, not a tuning knob: the Pale
            // Marionette's 210 posture is exactly six clean deflects at 25 x 1.4, MarionetteDataTests
            // asserts it against THIS weapon, and FeatureTests' Knight beat counts it at x1.3. Changing
            // the sword's blade is a presentation change; changing this number would silently re-tune
            // two boss fights.
            sword.parryWindowMultiplier = 1f; sword.parryPostureDamage = 25f; sword.pyreBonus = 0f;
            // SUPER "EMBERFALL ARC" — one enormous horizontal sweep. The sword is the generalist, so its
            // super is the plain, honest one: a single legible beat that hits everything in front of you.
            sword.superName = "Emberfall Arc"; sword.superKind = SuperKind.Cleave;
            sword.superDamage = 150f; sword.superPostureDamage = 70f;
            sword.superRadius = 5.5f; sword.superArcDeg = 170f; sword.superHits = 1;
            sword.superWindup = 0.30f; sword.superActive = 0.14f; sword.superRecover = 0.30f;
            sword.superHitStop = 0.10f; sword.superShake = 0.45f; sword.superKnockback = 2.5f;
            // VIEWMODEL SCALE (rule 9: written here or it never reaches the asset). 1.355 m of prefab
            // above the grip x 0.46 = 0.62 m above the fist — roughly twice the dagger.
            sword.viewmodelScale = 0.46f;
            ResetPosesToDefaults(sword);
            // A LONG BLADE IS FRAMED BY CANT, NOT BY SHRINKING IT. The class-default idle holds the
            // weapon almost vertical; at 0.62 m that puts the point out of the top of the frame and a
            // steel bar up the right-hand side. Rolled 34° and pushed away from the lens instead, the
            // same blade lies diagonally across the lower-right corner: the tip stays in frame, the
            // crosshair stays clear, and the sword still reads as a SWORD because the whole length of
            // it is visible at once. Measured, not guessed — WeaponSilhouetteTests asserts all three.
            sword.idle = new Pose(new Vector3(0.50f, -0.42f, 0.66f), new Vector3(8f, -14f, 34f));
            sword.windup = new Pose(new Vector3(0.62f, -0.06f, 0.44f), new Vector3(-34f, -56f, 26f));
            sword.swingEnd = new Pose(new Vector3(-0.30f, -0.50f, 0.80f), new Vector3(26f, 44f, -46f));
            sword.parry = new Pose(new Vector3(0.10f, -0.16f, 0.62f), new Vector3(0f, 82f, 74f));
            // The guard: a big diagonal across the lower right. More roll than the dagger's, because a
            // longer blade needs a shallower angle to keep its point inside the frame, and pushed a
            // further 0.12 m out so the quillons do not sit on the lens.
            sword.guard = new Pose(new Vector3(0.36f, -0.16f, 0.72f), new Vector3(-10f, 38f, 46f));
            sword.executeWindup = new Pose(new Vector3(0.52f, 0.34f, 0.46f), new Vector3(-74f, -28f, 18f));
            EditorUtility.SetDirty(sword);

            var hammer = GetOrCreate<WeaponData>(WeaponsDir + "/Hammer.asset");
            // RENAMED 2026-09-07, and the rename is the FIX -- see the colour note below. Was "Sunbreaker".
            hammer.displayName = "Verdigris";
            // HIT-READ PASS (2026-09-07): was Hex("#E0661A"), hue ~23 deg -- only ~5 deg from
            // Projectile.HotCore (the enemy bolt, ~28 deg). In a parry game the bolt is the single most
            // important thing to read and the player's own weapon must not compete with it, exactly the
            // >=45 deg rule WandDataTests already holds the wand set to. The bolt does not move (it is a
            // gameplay read other systems depend on); the WEAPON moves.
            // The band that is both >=45 deg from the bolt (28) AND still reads warm is narrow: true
            // amber/gold sits at 40-55 deg, inside the forbidden 45 deg radius around the bolt on both
            // sides (a magenta-red escape collides with the unblockable cue's ~356-358 deg red family
            // instead). Hex("#A8D12E") sits at hue ~75 deg: ~47 deg clear of the bolt and ~51 deg clear
            // of Rosethorn's green (~126 deg).
            //
            // NAME PASS (2026-09-07, the user's call). The hue above is forced -- there is NO warm hue
            // that clears the bolt, so the >=45 deg rule and the word "sun" cannot both be kept. The
            // previous pass kept the name and claimed #A8D12E "still reads as Sunbreaker"; photographed
            // under the real pipeline it reads OLIVE-LIME, and a weapon called the Sunbreaker looking
            // like a garden tool is worse than a weapon with a different name. So the NAME moved instead
            // of the colour: #A8D12E is almost exactly the green that grows on corroded bronze, and the
            // maul's head is a blocky brass mass -- as "Verdigris" the colour reads as age on metal,
            // which is intentional, rather than as an amber that missed. Do not "fix" this back to gold
            // without moving Projectile.HotCore first; the bolt owns warm.
            hammer.neon = Hex("#A8D12E");
            // THE CRUSHER. baseDamage 46 -> 52 takes the health-damage crown outright (81.6/s against
            // the sword's 73.9, and 88.4 on the 1.7x finisher — twice a sword hit, in one legible beat),
            // and postureDamage 34 -> 26 gives up the hit-posture column entirely (30.2/s, last in the
            // set). A maul caves a body in; it does not out-fence it. The maul still breaks people —
            // through parryPostureDamage 40, the crown, bought with the narrowest parry window in the
            // game (x0.75). So breaking-by-hitting and breaking-by-deflecting are now two different
            // weapons instead of the same one, and the sword sits between them at 16 / 25.
            hammer.baseDamage = 52f; hammer.postureDamage = 26f; hammer.executeDamage = 400f;
            hammer.strScale = 1f; hammer.dexScale = 0f; hammer.arcScale = 0.2f;
            hammer.comboLength = 2; hammer.comboMultipliers = new[] { 1f, 1.7f };
            // COMMITTED. 0.86 s is nearly four dagger swings and the contact frame does not arrive until
            // 0.40 s — you are holding the maul over your head for longer than a Marionette wind-up, and
            // there is no taking it back. That is the trade the reach and the 1.7x finisher pay for.
            hammer.attackDuration = 0.86f; hammer.hitDelay = 0.40f; hammer.comboWindow = 0.55f;
            // hitStopSeconds 0.11 -> 0.085. Still the heaviest freeze of any weapon and still 2.8x the
            // needle's 0.03 (sword 0.06), so the weight ladder is intact — but 0.11 was LONGER than
            // GameFeel.parryHitStop 0.09, which quietly made an ordinary maul swing the biggest beat in
            // the game. The deflect is the thing this project is about and it must own the longest
            // freeze; nothing routine may out-punctuate it. It is also 25 ms less world-stop per hit on
            // a run clock, on the weapon a speedrunner swings at a sentry in passing.
            hammer.hitOffset = 2.5f; hammer.hitRadius = 1.7f; hammer.hitStopSeconds = 0.085f;
            hammer.parryWindowMultiplier = 0.75f; hammer.parryPostureDamage = 40f; hammer.pyreBonus = 5f;
            // SUPER "SUNBREAK" — overhead into the ground, 360 degree shockwave. The longest wind-up in
            // the set and by far the biggest posture number: the hammer already trades speed for weight,
            // and its super doubles down rather than apologising for it. Nothing else knocks enemies back
            // 6 metres or shakes the camera this hard.
            // "Bronzefall": the Quake IS a fall, and it echoes the sword's "Emberfall Arc". Was "Sunbreak".
            hammer.superName = "Bronzefall"; hammer.superKind = SuperKind.Quake;
            hammer.superDamage = 200f; hammer.superPostureDamage = 130f;
            hammer.superRadius = 7.5f; hammer.superArcDeg = 360f; hammer.superHits = 1;
            hammer.superWindup = 0.52f; hammer.superActive = 0.12f; hammer.superRecover = 0.46f;
            hammer.superHitStop = 0.20f; hammer.superShake = 0.9f; hammer.superKnockback = 6f;
            // 1.42 m of haft above the grip x 0.50 = 0.71 m above the fist: the longest weapon in the
            // set, and the only one where the mass is at the FAR end rather than in the hand.
            hammer.viewmodelScale = 0.50f;
            ResetPosesToDefaults(hammer);
            // Carried LOW and canted well out. A maul held anywhere near vertical parks a head the size
            // of the enemy's own in the upper frame; slung down-right at 30° it hangs in the corner and
            // the player looks over it. The head being 0.71 m from the fist is what does the work —
            // it is far from the lens, so it is big without being close.
            hammer.idle = new Pose(new Vector3(0.52f, -0.55f, 0.72f), new Vector3(8f, -18f, 30f));
            // The biggest anticipation in the game: the head goes right up over the shoulder and behind
            // the frame, so the 0.40 s before contact is spent looking at an empty screen with a shadow
            // coming down through it.
            hammer.windup = new Pose(new Vector3(0.58f, 0.08f, 0.36f), new Vector3(-70f, -42f, 18f));
            hammer.swingEnd = new Pose(new Vector3(-0.24f, -0.74f, 0.88f), new Vector3(58f, 32f, -34f));
            hammer.parry = new Pose(new Vector3(0.08f, -0.24f, 0.66f), new Vector3(0f, 86f, 76f));
            // The hammer guards with its MASS, not its edge: the haft is braced across the body and the
            // head kept out of the way. SWEPT, not felt: the first two authored braces read fine in the
            // mind's eye and put the HEAD ON THE CROSSHAIR on screen (16.1% then 14.3% of the disc —
            // WeaponSilhouette caught both), because where a maul's mass lands on the canted line cannot
            // be eyeballed. This pose comes out of WeaponGuardSweep: 648 candidates, 50 clean, this one
            // chosen for keeping the brace lowest (most mass-like) of the survivors. Measures crosshair
            // 0.0%, tip (+0.10,+0.16) above the disc, cover 1.0%.
            hammer.guard = new Pose(new Vector3(0.44f, -0.16f, 0.80f), new Vector3(-10f, 46f, 42f));
            hammer.executeWindup = new Pose(new Vector3(0.54f, 0.52f, 0.40f), new Vector3(-96f, -22f, 8f));
            EditorUtility.SetDirty(hammer);

            // ROSETHORN IS THE CONTROL. Not one number in this block changed when the set got its
            // lengths back, and that is on purpose: the dagger is what the player said reads and feels
            // best, so it stays the fixed point the other three are measured against. If a future pass
            // finds the sword or the maul unreadable, the fault is in that weapon's pose or geometry —
            // it is never a reason to shrink this one to match, and it is never a reason to change it.
            var dagger = GetOrCreate<WeaponData>(WeaponsDir + "/Dagger.asset");
            dagger.displayName = "Rosethorn";
            dagger.neon = Hex("#5FD66A");
            // ...AND THE ROLE IS NOT THE SILHOUETTE. Everything that paragraph is about — pose,
            // geometry, viewmodelScale, tempo, reach, comboLength, parryPostureDamage — is untouched
            // and is pinned by WeaponSilhouetteTests.RosethornIsTheControl. What moves here is the one
            // thing the control was never about: the needle's JOB. It was last in health/s AND last in
            // posture/s, i.e. it had no column of its own.
            //   baseDamage 12 -> 9      the worst killer in the game, on purpose
            //   postureDamage 6 -> 13   the best BREAKER in the game, by 1.6x over the sword
            // 4 hits in 0.88 s: 44.0 health/s against 59.1 posture/s. On a 60/60 Grunt that is 5 hits
            // to the break and ~7 to the kill, so the needle is the only weapon that reaches the
            // deathblow first — it is the one weapon that plays the deflect-and-break game this
            // project is built on with its OFFENCE too; the other two simply kill. Its super already agreed with this (Thornstorm's payload is nine
            // separate posture applications); now the light attack does too.
            // executeDamage 250 -> 320: a breaker's deathblow has to actually kill the toughest thing
            // it can break, and 250 sits UNDER the Iron Penitent's 260 HP and the Chorister's 300.
            dagger.baseDamage = 9f; dagger.postureDamage = 13f; dagger.executeDamage = 320f;
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
            dev.attackDuration = 0.32f; dev.hitDelay = 0.09f; dev.comboWindow = 0.4f;
            dev.hitOffset = 1.9f; dev.hitRadius = 1.45f; dev.hitStopSeconds = 0.06f;
            dev.parryWindowMultiplier = 1.6f; dev.parryPostureDamage = 60f; dev.pyreBonus = 25f;
            // SUPER "OATHBREAKER" — instant 360 nova at 12m. A test weapon exists to end an encounter so
            // the next thing can be tested, so its super has no wind-up and no falloff.
            dev.superName = "Oathbreaker"; dev.superKind = SuperKind.Nova;
            dev.superDamage = 600f; dev.superPostureDamage = 400f;
            dev.superRadius = 12f; dev.superArcDeg = 360f; dev.superHits = 1;
            dev.superWindup = 0.06f; dev.superActive = 0.10f; dev.superRecover = 0.24f;
            dev.superHitStop = 0.14f; dev.superShake = 0.7f; dev.superKnockback = 4f;
            // 1.085 m above the grip x 0.46 = 0.50 m: deliberately BETWEEN the dagger and the sword, so
            // the test blade never gets mistaken for either at a glance.
            dev.viewmodelScale = 0.46f;
            ResetPosesToDefaults(dev);
            dev.idle = new Pose(new Vector3(0.46f, -0.36f, 0.64f), new Vector3(4f, -12f, 26f));
            dev.windup = new Pose(new Vector3(0.58f, -0.14f, 0.46f), new Vector3(-26f, -52f, 26f));
            dev.swingEnd = new Pose(new Vector3(-0.24f, -0.44f, 0.80f), new Vector3(18f, 40f, -42f));
            // SWEPT, and the sweep overturned the intuition: rolling the kris flatter "like the dagger"
            // (62-72°) lays its wavy blade ACROSS the crosshair disc (6-14% covered in every such
            // candidate), because a 0.50 m blade at high roll crosses centre height where the 0.32 m
            // needle has already ended. The kris belongs to the sword regime — modest roll, point
            // carried up. From WeaponGuardSweep (432 candidates, 144 clean): crosshair 0.0%, tip
            // (+0.04,+0.19) above the disc, cover 0.7%.
            dev.guard = new Pose(new Vector3(0.34f, -0.08f, 0.62f), new Vector3(-14f, 46f, 52f));
            EditorUtility.SetDirty(dev);

            // ---------------- Items (Neon White style single-use pickups) ----------------
            // Wall Surge is retired: it paid out on use and flattened wall skill expression.
            Item("Grapple", i =>
            {
                i.displayName = "Grapple"; i.shortLabel = "HOOK";
                i.effect = ItemEffect.Grapple;
                i.color = Hdr("#5AF2FF", 5f);   // neon cyan: the line, the tine, the HUD slot
                i.grappleRange = 28f;
                i.grappleConeDeg = 12f;
                i.grappleSeconds = 0.35f;
                i.grappleBigPostureFraction = 0.35f;
                i.grapplePerfectWindow = 0.13f;
                i.description = "Hook a foe. Against a turret, cross its incoming bolt on the Hook timing: " +
                                "the bolt deflects, the turret breaks, and your next airborne dash-jump is empowered.";
            });
            Item("Rebound", i =>
            {
                i.displayName = "Rebound"; i.shortLabel = "REBOUND";
                i.effect = ItemEffect.Rebound;
                i.color = Hdr("#7BFFB2", 4f);
                i.reboundExitMultiplier = 1.18f;
                i.reboundBonusSpeed = 3f;
                i.description = "Arm your next successful airborne dash or wall jump. That exit refreshes " +
                                "your air dash and carries a stronger, capped burst.";
            });
            Item("DeflectSigil", i =>
            {
                i.displayName = "Deflect Sigil"; i.shortLabel = "SIGIL";
                i.effect = ItemEffect.DeflectSigil;
                i.color = Hdr("#D6A2FF", 4.5f);
                i.deflectSigilBonusStacks = 2;
                i.deflectSigilImpulse = 5f;
                i.description = "Arm until your next Perfect. Blocks and misses do not spend it; the Perfect " +
                                "adds two speed stacks and a stronger forward impulse.";
            });
            Item("BladeThrow", i =>
            {
                i.displayName = "Blade Throw"; i.shortLabel = "BLADE";
                i.effect = ItemEffect.BladeThrow;
                i.color = Hdr("#FFB347", 4f);   // ember amber: distinct from Hook cyan, Rebound green, Sigil violet
                // 24 m/s x 1.1 s = 26.4 m reach, under the Hook's 28 m envelope Level_01 already tolerates.
                i.bladeSpeed = 24f;
                i.bladeGravity = 8f;
                i.bladeFlightSeconds = 1.1f;
                i.bladeLodgeSeconds = 3.5f;
                i.bladeSpinDegreesPerSecond = 1080f;
                i.bladeRecallRange = 30f;
                i.bladeRecallConeDeg = 25f;
                i.bladePullSeconds = 0.35f;
                i.bladePostureMultiplier = 1.5f;
                // 2026-09-13 (the user): "the sword is tiny and hard to see, it needs to be much bigger".
                // 1.6 gave a 1.1 m sword; 4.5 is ~3.2 m, readable across a span.
                i.bladeModelScale = 4.5f;
                i.description = "Throw your sword. Aim at it and DASH at any point in its flight - or where it " +
                                "bites - to be pulled to it. You are unarmed until it is back in your hand.";
            });
            AssetDatabase.DeleteAsset(ItemsDir + "/WallSurge.asset");

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
            // Every perfect parry participates in the same readable speed ladder. The opening Surge
            // Turret keeps its own tighter 1.4 s run-out; ordinary blue sentries fire every 1.6 s, so
            // their general ladder needs 2.0 s to reward consecutive clean contacts without becoming
            // permanent traversal speed.
            stats.generalParrySurgeStep = 0.12f;
            stats.generalParrySurgeMaxStacks = 5;
            stats.generalParrySurgeSeconds = 3.2f;
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
            // The deflect's chromatic veil is a brief contact accent, never a screen-covering haze.
            // The hitstop, force package, audio and enemy flash carry the weight; this clears while the
            // next cue is still legible.
            feel.parryChromatic = 0.35f;
            feel.parryChromaticTime = 0.12f;
            // Deflect impact (rule 9). Every one of these is FORCE — rotation, translation, FOV, time,
            // spectral width. Nothing here brightens the frame: EnemyVisuals.CueFlash owns the
            // brightness budget and must remain the loudest event on screen. ParryImpulse holds the
            // shapes and ParryImpactTests holds them to their budgets.
            feel.parryKickPitch = 1.6f;    // deg, up, constant — every deflect drives the guard up
            feel.parryKickYaw = 1.1f;      // deg, away from the blow, scaled by its lateral component
            feel.parryKickRoll = 1.3f;     // deg, with the blow; roll never moves the aim vector
            feel.parryKickOffset = 0.035f; // m, head sinks and slides
            feel.parryKickTime = 0.16f;    // s, dead still again well before the ~0.28 s cue lead
            feel.parryFovPunch = -2.2f;    // deg, punch IN on the frame the world stops
            // Gap 3.4, settled asymmetrically: the ONSET stays binary (it is the punctuation) and only
            // the RELEASE is stepped. 0.07 s at 0.45 then 0.725 costs 0.030 s of world time, which is
            // 23% of the 0.13 s perfect window and shifts the whole world — cue included — uniformly.
            feel.parryHitStopRelease = 0.07f;
            feel.parryHitStopReleaseScale = 0.45f;
            feel.parryLayeredAudio = true;
            // Dash and slide feel (rule 9). The dash was never silent — dashFovKick has shipped at 8 on
            // this asset all along — it was NON-SPECIFIC: a symmetric FOV widen cannot say which way you
            // went. These add DIRECTION to the dash and a sustained middle to the slide, and like the
            // deflect package above, not one of them brightens the frame. See DashImpulse / SlideImpulse.
            feel.dashKickPitch = 0.9f;      // deg, view lifts on a forward surge; zero on a pure strafe
            feel.dashKickRoll = 1.4f;       // deg, banks into a lateral dash; roll never moves the aim
            feel.dashKickOffset = 0.06f;    // m, lens left behind by the body — the acceleration read
            feel.dashKickTime = 0.14f;      // s, still again before the 0.16 s dash has finished
            feel.dashChromatic = 0.35f;
            feel.dashStreakCount = 12;      // camera space, never world space (WeaponTrail's lesson)
            feel.dashStreakSeconds = 0.22f;
            feel.dashStreakAlpha = 0.85f;
            feel.dashStreakBrightness = 0.90f;  // UNDER the 1.05 bloom threshold: a dash adds zero bloom
            // The slide is the harder half. Its middle had nothing in it because every cue it owned was
            // an impulse on a move that lasts 0.90 s; all of these are HELD and track actual speed.
            feel.slideFovHold = 8f;         // deg, sustained, reaching exactly 0 at the motor's end speed
            feel.slideEndFovPunch = -2.5f;  // deg, the world closing back in as the speed goes
            feel.slideRollDegrees = 3.5f;   // deg, banking into the steer
            feel.slideKickPitch = 1.8f;     // deg, nose dips on the commit (1.2 before the body pass)
            feel.slideKickTime = 0.13f;
            // The body pass (PlayerBody + the slide's WEIGHT). The eye now arrives on a spring and the
            // lens carries the floor's rattle; both are held channels with one writer, like the roll.
            feel.slideRumble = 0.006f;      // m, held lens rattle at full speed, quadratic in speed
            feel.slideCrouchHz = 4.5f;      // Hz, the eye PLOPS onto the slide height in ~0.13 s
            feel.slideCrouchDamping = 0.55f;// ~12% overshoot: below the slide height, then settles up
            feel.slideDustRate = 34f;       // grit/s at full speed, shed into a scene-level root
            feel.slideSparkRate = 5f;       // spark bursts/s above 35% speed, via SlashFx (peak 1.0)
            feel.slideScrapeVolume = 0.22f; // synthesised loop on its own source, not an Sfx entry
            // The pivot's traversal pieces (rule 9). One-shots and textures only: no held lens channel.
            feel.balloonFovKick = 5f;       // deg, a launch is bigger than a jump (2.5), smaller than a dash (8)
            feel.balloonKickPitch = 1.5f;   // deg, the nose lifts as the body goes up
            feel.burstFovKick = 6f;         // deg, ON TOP of the dash kick: the grapple burst is the fastest thing in the game
            feel.waterEnterFovKick = 3f;    // deg, one-shot on entering water; the slide owns every held channel
            feel.waterSprayRate = 26f;      // spray bursts/s at the water floor speed, via SlashFx
            feel.waterHissVolume = 0.14f;   // its own synthesised loop, an octave over the scrape
            // Perfect timing (rule 9). The reward is stamina (on the motor); this is only the "yes".
            feel.perfectFovKick = 3f;       // deg, on top of the move's own kick
            feel.perfectPromptSeconds = 0.6f;
            // Wall run feel (rule 9). The lean (PlayerLook, 13° in, 7° kick out on the jump) was never
            // missing; the catch, the feet and the LET-GO were. Small on purpose — the lean owns the
            // sustained channel — and every value is force or air. See WallRunImpulse / WallRunFx.
            feel.wallRunFovHold = 3.5f;        // deg, held for the run; slide's is 8, the lean is loud enough
            feel.wallRunAttachOffset = 0.03f;  // m, lens pressed toward the face on the catch
            feel.wallRunAttachTime = 0.12f;    // s, dash-fast: contact is an event
            feel.wallRunStepDistance = 1.6f;   // m of wall per foot-tick; ground stride is 2.4
            feel.wallRunStepVolume = 0.40f;    // under the 0.55 grounded footstep — texture, not a voice
            feel.wallRunDropPitch = 1.4f;      // deg DOWN when the wall lets go (Expired/Decayed/Exhausted)
            feel.wallRunDropOffset = 0.03f;    // m, head sinks with the sag
            feel.wallRunDropTime = 0.15f;      // s, over inside the exit-grace window
            feel.wallRunLostDrift = 0.02f;     // m, lens drifts AWAY from where the face was (LostWall)
            feel.wallRunGritRate = 44f;        // motes/s off the foot contact at full speed; falls with speed and age
            feel.wallRunStepSparks = 3;        // sparks per foot-tick, discrete; the grit is the contact, the sparks are the step
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
        /// The parry recording stages under <see cref="RecordingLevelsDir"/> are tooling, never campaign
        /// levels: they are skipped, and removed if an earlier run adopted them.
        /// </summary>
        static void AdoptLevelDefinitions(LevelRegistry registry)
        {
            var guids = AssetDatabase.FindAssets("t:LevelDefinition", new[] { LevelsDir });
            if (guids == null || guids.Length == 0) return;

            var list = new System.Collections.Generic.List<LevelDefinition>();
            if (registry.levels != null)
                for (int i = 0; i < registry.levels.Length; i++)
                    if (registry.levels[i] != null && !IsRecordingStage(registry.levels[i])) list.Add(registry.levels[i]);

            for (int i = 0; i < guids.Length; i++)
            {
                var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (def != null && !IsRecordingStage(def) && !list.Contains(def)) list.Add(def);
            }
            registry.levels = list.ToArray();
        }

        const string RecordingLevelsDir = LevelsDir + "/Recording";

        static bool IsRecordingStage(LevelDefinition def)
        {
            string path = AssetDatabase.GetAssetPath(def);
            return !string.IsNullOrEmpty(path) && path.Replace('\\', '/').StartsWith(RecordingLevelsDir + "/");
        }

        /// <summary>Creates or rewrites a moveset asset. Reset to defaults first, like Attack()/Item().</summary>
        static EnemyMoveset Moveset(string name, string displayName, MovesetEntry[] entries)
        {
            var m = GetOrCreate<EnemyMoveset>(EnemyPaths.Moveset(name));
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
        /// <summary>An entry with a per-move cooldown: a signature that never comes twice running.</summary>
        static MovesetEntry EntryCd(string label, float weight, float minRange, float maxRange, float cooldown, params EnemyAttackData[] hits)
        {
            var e = Entry(label, weight, minRange, maxRange, hits);
            e.cooldown = cooldown;
            return e;
        }

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
