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

        [MenuItem("VibeGame1/3. Create Data")]
        public static void CreateAll()
        {
            EnsureFolder(DataRoot);
            EnsureFolder(AttacksDir);
            EnsureFolder(EnemiesDir);
            EnsureFolder(WeaponsDir);

            // ---------------- Attacks ----------------
            var gruntSlash = Attack("Grunt_Slash", a =>
            {
                a.windup = 0.55f; a.impactDelay = 0.05f; a.strikeDuration = 0.2f; a.recovery = 0.7f;
                a.range = 2.4f; a.coneDeg = 70f; a.damage = 20f; a.lungeDistance = 0.5f;
            });
            var heavyOverhead = Attack("Heavy_Overhead", a =>
            {
                a.windup = 0.85f; a.recovery = 1.0f; a.range = 2.8f; a.coneDeg = 60f;
                a.damage = 35f; a.lungeDistance = 0.8f; a.comboGap = 0.25f;
            });
            var heavySweep = Attack("Heavy_Sweep", a =>
            {
                a.windup = 0.6f; a.recovery = 0.6f; a.range = 2.8f; a.coneDeg = 110f;
                a.damage = 25f; a.lungeDistance = 0.4f;
            });
            var bossSlash = Attack("Boss_Slash", a =>
            {
                a.windup = 0.5f; a.recovery = 0.6f; a.range = 3.2f; a.coneDeg = 80f;
                a.damage = 30f; a.lungeDistance = 0.8f;
            });
            var bossDoubleA = Attack("Boss_DoubleSlash_A", a =>
            {
                a.windup = 0.45f; a.recovery = 0.7f; a.range = 3.2f; a.coneDeg = 80f;
                a.damage = 25f; a.comboGap = 0.15f; a.lungeDistance = 0.7f;
            });
            var bossDoubleB = Attack("Boss_DoubleSlash_B", a =>
            {
                a.windup = 0.3f; a.recovery = 0.7f; a.range = 3.2f; a.coneDeg = 80f;
                a.damage = 25f; a.lungeDistance = 0.7f;
            });
            var bossSlam = Attack("Boss_Slam", a =>
            {
                a.windup = 0.9f; a.recovery = 1.1f; a.range = 3.6f; a.coneDeg = 90f;
                a.damage = 45f; a.lungeDistance = 1.5f; a.parryPostureMultiplier = 1.5f;
            });
            var bossThrust = Attack("Boss_Thrust", a =>
            {
                a.windup = 0.7f; a.recovery = 0.9f; a.range = 4.5f; a.coneDeg = 30f;
                a.damage = 55f; a.lungeDistance = 2.0f; a.unblockable = true;
            });

            // ---------------- Enemies ----------------
            var grunt = GetOrCreate<EnemyData>(EnemiesDir + "/Grunt.asset");
            grunt.displayName = "Grunt";
            grunt.maxHP = 60f; grunt.maxPosture = 60f; grunt.postureRegen = 6f; grunt.postureRegenDelay = 2.5f; grunt.staggerSeconds = 3f;
            grunt.moveSpeed = 4.5f; grunt.turnSpeed = 360f; grunt.aggroRange = 14f; grunt.attackRange = 2.2f;
            grunt.attackCooldown = 0.4f; grunt.parryRecoilSeconds = 0.5f;
            grunt.soulValue = 40;
            grunt.bodyColor = Hex("#0A0708"); grunt.emission = Hex("#6A0F14") * 1.2f; grunt.scale = 1f;
            grunt.combos = new[] { new AttackCombo(gruntSlash) };
            EditorUtility.SetDirty(grunt);

            var heavy = GetOrCreate<EnemyData>(EnemiesDir + "/Heavy.asset");
            heavy.displayName = "Heavy";
            heavy.maxHP = 130f; heavy.maxPosture = 110f; heavy.postureRegen = 5f; heavy.postureRegenDelay = 3f; heavy.staggerSeconds = 3.5f;
            heavy.moveSpeed = 3.2f; heavy.turnSpeed = 240f; heavy.aggroRange = 14f; heavy.attackRange = 2.6f;
            heavy.attackCooldown = 0.6f; heavy.parryRecoilSeconds = 0.6f;
            heavy.soulValue = 120;
            heavy.bodyColor = Hex("#0A0708"); heavy.emission = Hex("#8A2A10") * 1.2f; heavy.scale = 1.4f;
            heavy.combos = new[] { new AttackCombo(heavyOverhead, heavySweep), new AttackCombo(heavySweep) };
            EditorUtility.SetDirty(heavy);

            var boss = GetOrCreate<BossData>(EnemiesDir + "/Boss.asset");
            boss.displayName = "THE HOLLOW WARDEN";
            boss.maxHP = 380f; boss.maxPosture = 220f; boss.postureRegen = 12f; boss.postureRegenDelay = 3f; boss.staggerSeconds = 4f;
            boss.moveSpeed = 5.5f; boss.turnSpeed = 300f; boss.aggroRange = 40f; boss.attackRange = 3.0f;
            boss.attackCooldown = 0.5f; boss.parryRecoilSeconds = 0.6f;
            boss.soulValue = 1500;
            boss.bodyColor = Hex("#0D0612"); boss.emission = Hex("#7A1030") * 1.6f; boss.scale = 2.2f;
            boss.segments = 3;
            boss.combos = new[] { new AttackCombo(bossSlash) };
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

            // ---------------- Weapons ----------------
            var sword = GetOrCreate<WeaponData>(WeaponsDir + "/Sword.asset");
            sword.displayName = "Cerulean Edge";
            sword.neon = Hex("#8FB5D9");
            sword.baseDamage = 22f; sword.postureDamage = 12f; sword.executeDamage = 300f;
            sword.strScale = 0.5f; sword.dexScale = 0.5f; sword.arcScale = 0.2f;
            sword.comboLength = 3; sword.comboMultipliers = new[] { 1f, 1f, 1.5f };
            sword.attackDuration = 0.38f; sword.hitDelay = 0.12f; sword.comboWindow = 0.35f;
            sword.hitOffset = 1.6f; sword.hitRadius = 1.1f; sword.hitStopSeconds = 0.05f;
            sword.parryWindowMultiplier = 1f; sword.parryPostureDamage = 25f; sword.juiceBonus = 0f;
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
            hammer.parryWindowMultiplier = 0.8f; hammer.parryPostureDamage = 40f; hammer.juiceBonus = 5f;
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
            dagger.parryWindowMultiplier = 1.35f; dagger.parryPostureDamage = 18f; dagger.juiceBonus = -5f;
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
            dev.parryWindowMultiplier = 1.6f; dev.parryPostureDamage = 60f; dev.juiceBonus = 25f;
            dev.viewmodelScale = 0.5f;
            ResetPosesToDefaults(dev);
            EditorUtility.SetDirty(dev);

            // ---------------- Singletons ----------------
            var stats = GetOrCreate<PlayerStatsData>(DataRoot + "/PlayerStats.asset");
            EditorUtility.SetDirty(stats);
            var table = GetOrCreate<UpgradeTable>(DataRoot + "/UpgradeTable.asset");
            EditorUtility.SetDirty(table);
            var feel = GetOrCreate<GameFeelSettings>(DataRoot + "/GameFeel.asset");
            EditorUtility.SetDirty(feel);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DataFactory] Created/updated 8 attacks, 3 enemies, 4 weapons, PlayerStats, UpgradeTable, GameFeel under " + DataRoot);
        }

        // ---------------------------------------------------------------------------------

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
