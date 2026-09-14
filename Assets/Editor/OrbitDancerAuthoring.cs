namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Stable authoring identity for the sandbox-only Orbit Dancer elite body, built the same way as
    /// <see cref="CinderJudgeAuthoring"/>. The source FBX is SHA-256
    /// 0BCBFF4DA184221F2B588772BAFA0AD314D19DF0A1E6C89A612A2C9792E28D70 and its unfiltered 21-state
    /// source manifest is SHA-256 5439F60430873743C847BDAB22A68DBD3CC7C51A2B78A567BAC19B07AB622430. The
    /// project manifest is a smaller allowlist: V18's base kit (idle, walk, run, jump, swing, stab, kick,
    /// stagger, roar, combo finisher, heavy, shoulder charge, jab2) plus the Hit and Death reactions
    /// PuppetVisuals requires, plus the two signature throws the forge generated for this body alone:
    /// DiscThrow and SpinThrow. Importing an extra source take is a content change, not a side effect.
    /// </summary>
    public static class OrbitDancerAuthoring
    {
        public const string EnemyName = "Legendary_OrbitDancer";
        public const string ModelName = "OrbitDancer";
        public const string SourceFbxSha256 = "0BCBFF4DA184221F2B588772BAFA0AD314D19DF0A1E6C89A612A2C9792E28D70";
        public const string SourceManifestSha256 = "5439F60430873743C847BDAB22A68DBD3CC7C51A2B78A567BAC19B07AB622430";

        /// <summary>The volley: the attack OrbitDancerDiscs launches three ricochet discs for.</summary>
        public const string DiscThrowAttackName = "OrbitDancer_DiscThrow";
        /// <summary>The close whirl: a real 360 contact whose release also banks two discs off the walls.</summary>
        public const string SpinThrowAttackName = "OrbitDancer_SpinThrow";
        /// <summary>The attack a disc CARRIES to PlayerCombat.ReceiveAttack (EnemyData.projectileAttack).
        /// Never in the moveset: the brain never schedules it, the disc does.</summary>
        public const string DiscAttackName = "OrbitDancer_Disc";

        public static readonly string[] ClipAllowlist =
        {
            "Idle", "Walk", "Run", "Jump", "AttackSwing", "AttackStab", "AttackKick", "Hit", "Stagger",
            "Roar", "Death", "Jab2", "ShoulderCharge", "HeavyAttack", "ComboFinisher", "DiscThrow", "SpinThrow",
        };

        /// <summary>
        /// The Dancer has NO explicit contact profile: every clip an attack names ships an OnAttackHit in
        /// the source (DiscThrow 0.478, SpinThrow 0.474, both on the right wrist), so 4b bakes the
        /// manifest's own anchors. Kept for symmetry with the other forge bodies' hooks in MiniBossFactory.
        /// </summary>
        public static bool TryExplicitContact(string enemyName, string clipName, out float normalized)
        {
            normalized = 0f;
            return false;
        }
    }
}
