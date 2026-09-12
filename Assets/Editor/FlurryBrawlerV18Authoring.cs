namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Stable authoring identity for the sandbox-only V18 Flurry Brawler test body. The source FBX is
    /// SHA-256 EC0328191967144DDEBEF1185124A35FA40AF7590DAE24052791B4A7D765BCC2 and its unfiltered
    /// source manifest is SHA-256 269725080628FFED78EEE3ACA01A44F6C15B6C50A054998BEE374FA24220ADB8.
    /// The project manifest is intentionally a smaller allowlist: importing an extra source take is a
    /// content change, not an accidental side effect of re-exporting the model.
    /// </summary>
    public static class FlurryBrawlerV18Authoring
    {
        public const string EnemyName = "Legendary_FlurryBrawlerV18";
        public const string ModelName = "FlurryBrawlerV18";
        public const string SourceFbxSha256 = "EC0328191967144DDEBEF1185124A35FA40AF7590DAE24052791B4A7D765BCC2";
        public const string SourceManifestSha256 = "269725080628FFED78EEE3ACA01A44F6C15B6C50A054998BEE374FA24220ADB8";

        public static readonly string[] ClipAllowlist =
        {
            "Idle", "Walk", "Run", "Jump", "AttackSwing", "AttackOverhead", "AttackStab",
            "AttackKick", "Hit", "Stagger", "Roar", "Block", "Death", "Jab2", "Dash", "Clap",
            "ShoulderCharge", "Combo2",
        };

        /// <summary>
        /// V18's approved contact profiles. Dash has no OnAttackHit event in the source manifest, so its
        /// 0.60 anchor also ensures 4b does not silently drop it from PuppetVisuals.namedClips. Combo2's
        /// single approved 0.671 source event is held explicitly so its generated multi-gesture tail
        /// never gets reinterpreted as extra gameplay contact by a future geometry heuristic.
        /// </summary>
        public static bool TryExplicitContact(string enemyName, string clipName, out float normalized)
        {
            if (enemyName == EnemyName && clipName == "Dash")
            {
                normalized = 0.60f;
                return true;
            }
            if (enemyName == EnemyName && clipName == "Combo2")
            {
                normalized = 0.671f;
                return true;
            }
            normalized = 0f;
            return false;
        }
    }
}
