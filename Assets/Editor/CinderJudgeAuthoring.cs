namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Stable authoring identity for the sandbox-only Cinder Judge elite body, built the same way as
    /// <see cref="FlurryBrawlerV18Authoring"/>. The source FBX is SHA-256
    /// E33A9D238114BF1A25A829A1AA47FDCD449DBE1DE0A92C4C22E142FCB9E40C58 and its unfiltered source
    /// manifest is SHA-256 CAD0809492410F0B39606E395A6F9C85EAD3150E96DAA95A14F0562DDF651FAC. The project
    /// manifest is a smaller allowlist: the user's move list (idle, walk, run, jump, swing, stab, kick,
    /// stagger, roar, combo finisher, heavy, shoulder charge, jab2) plus the Hit and Death reactions
    /// PuppetVisuals requires. Importing an extra source take is a content change, not a side effect.
    /// </summary>
    public static class CinderJudgeAuthoring
    {
        public const string EnemyName = "Legendary_CinderJudge";
        public const string ModelName = "CinderJudge";
        public const string SourceFbxSha256 = "E33A9D238114BF1A25A829A1AA47FDCD449DBE1DE0A92C4C22E142FCB9E40C58";
        public const string SourceManifestSha256 = "CAD0809492410F0B39606E395A6F9C85EAD3150E96DAA95A14F0562DDF651FAC";

        /// <summary>The storm's attack asset name: the one attack CinderJudgeStorm ticks for.</summary>
        public const string StormAttackName = "CinderJudge_StormJudgement";

        public static readonly string[] ClipAllowlist =
        {
            "Idle", "Walk", "Run", "Jump", "AttackSwing", "AttackStab", "AttackKick", "Hit", "Stagger",
            "Roar", "Death", "Jab2", "ShoulderCharge", "HeavyAttack", "ComboFinisher",
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
            normalized = 0f;
            return false;
        }
    }
}
