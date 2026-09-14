namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Stable authoring identity for the Cinder Judge elite body, built the same way as
    /// <see cref="FlurryBrawlerV18Authoring"/>. Since 2026-09-13 the source is the <c>cinder_judge_v2</c>
    /// retrofit (same raw mesh, cached limb map and rig as v1 — Blender measurements identical — with
    /// ShieldBash and ShieldRaise appended for the magic shield). The source FBX is SHA-256
    /// 3EB62843E9B642C2ABFECEB8B90A815CBA700E9F3537ED347C06D1EC6650891E and its unfiltered source
    /// manifest is SHA-256 59995B87C78F575182964FF20B8300B152FFE2F6626E446FB38A1DDA599202E5. The project
    /// manifest is a smaller allowlist: the user's move list plus Hit/Death and the two shield takes.
    /// </summary>
    public static class CinderJudgeAuthoring
    {
        public const string EnemyName = "Legendary_CinderJudge";
        public const string ModelName = "CinderJudge";
        public const string SourceFbxSha256 = "3EB62843E9B642C2ABFECEB8B90A815CBA700E9F3537ED347C06D1EC6650891E";
        public const string SourceManifestSha256 = "59995B87C78F575182964FF20B8300B152FFE2F6626E446FB38A1DDA599202E5";

        /// <summary>The storm's attack asset name: the one attack CinderJudgeStorm ticks for.</summary>
        public const string StormAttackName = "CinderJudge_StormJudgement";
        /// <summary>The shield raise (a held stance) and its bash, read by CinderJudgeShield.</summary>
        public const string ShieldRaiseAttackName = "CinderJudge_ShieldRaise";
        public const string ShieldBashAttackName = "CinderJudge_ShieldBash";

        public static readonly string[] ClipAllowlist =
        {
            "Idle", "Walk", "Run", "Jump", "AttackSwing", "AttackStab", "AttackKick", "Hit", "Stagger",
            "Roar", "Death", "Jab2", "ShoulderCharge", "HeavyAttack", "ComboFinisher", "ShieldBash", "ShieldRaise",
        };

        /// <summary>
        /// The Judge's one explicit contact profile. Roar carries no OnAttackHit in the source (it is a
        /// roar, not a strike), but the Storm Judgement attack names it as its wind-up performance, so
        /// 4b must bake it into PuppetVisuals.namedClips or the validator reports a clip with no contact
        /// frame. 0.40 is the source's own OnRoar moment: the point the roar is fully open, which is where
        /// the charge tell peaks. The storm's damage is never scheduled off this anchor -- EnemyController
        /// owns the one impact and CinderJudgeStorm owns the ticks after it.
        /// </summary>
        public static bool TryExplicitContact(string enemyName, string clipName, out float normalized)
        {
            if (enemyName == EnemyName && clipName == "Roar")
            {
                normalized = 0.40f;
                return true;
            }
            // ShieldBash: the manifest's #auto put OnAttackHit at 0.13 (after a re-roll), but the measured
            // arm span peaks at mid-clip (Blender span 0.24/0.30/0.24 at 25/50/75%): the shove lands at 0.50.
            if (enemyName == EnemyName && clipName == "ShieldBash")
            {
                normalized = 0.50f;
                return true;
            }
            // ShieldRaise is a stance, not a strike: the "contact" is the frame the arms are fully up, where
            // the dome is complete and deflecting (0.85).
            if (enemyName == EnemyName && clipName == "ShieldRaise")
            {
                normalized = 0.85f;
                return true;
            }
            normalized = 0f;
            return false;
        }
    }
}
