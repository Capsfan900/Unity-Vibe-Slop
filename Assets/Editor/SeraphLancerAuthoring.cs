namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Stable authoring identity for the Seraph Lancer elite body, built the same way as
    /// <see cref="OrbitDancerAuthoring"/> and <see cref="CinderJudgeAuthoring"/>. The source FBX is SHA-256
    /// 59C7B1375EF319AFC705656DF92692FFA22690C540DF4E82D96935255B67ECFA and its unfiltered 21-state
    /// source manifest is SHA-256 55E0B5D7DA28803ADF2920C53E63A48517650CB366BE4A0F722E4486FBEEC6E5. The
    /// project manifest is a smaller allowlist: V18's base kit (idle, walk, run, jump, swing, stab, kick,
    /// stagger, roar, combo finisher, heavy, shoulder charge, jab2) plus the Hit and Death reactions
    /// PuppetVisuals requires, plus the two clips the forge generated for this body alone: JavelinThrow
    /// (the signature's release) and HoverHold (the held aerial pose between throws). Importing an extra
    /// source take is a content change, not a side effect.
    /// </summary>
    public static class SeraphLancerAuthoring
    {
        public const string EnemyName = "Legendary_SeraphLancer";
        public const string ModelName = "SeraphLancer";
        public const string SourceFbxSha256 = "59C7B1375EF319AFC705656DF92692FFA22690C540DF4E82D96935255B67ECFA";
        public const string SourceManifestSha256 = "55E0B5D7DA28803ADF2920C53E63A48517650CB366BE4A0F722E4486FBEEC6E5";

        /// <summary>SKY VERDICT: the one schedule SeraphLancerJavelins throws javelins for. Its brain
        /// contact is a no-op (range 0, cone 0); the javelins are the attack.</summary>
        public const string SkyVerdictAttackName = "SeraphLancer_SkyVerdict";
        /// <summary>The attack a JAVELIN carries to PlayerCombat.ReceiveAttack (EnemyData.projectileAttack).
        /// Never in the moveset: the brain never schedules it, the javelin does.</summary>
        public const string JavelinAttackName = "SeraphLancer_Javelin";

        public static readonly string[] ClipAllowlist =
        {
            "Idle", "Walk", "Run", "Jump", "AttackSwing", "AttackStab", "AttackKick", "Hit", "Stagger",
            "Roar", "Death", "Jab2", "ShoulderCharge", "HeavyAttack", "ComboFinisher", "JavelinThrow", "HoverHold",
        };

        /// <summary>
        /// The Lancer has NO explicit contact profile: the one generated clip an attack names ships an
        /// OnAttackHit in the source (JavelinThrow 0.696, right wrist), so 4b bakes the manifest's own
        /// anchor (measured on the imported clip, as every generated clip is). Jump and HoverHold are
        /// presentation clips staged by SeraphLancerVisuals and are named by no attack, so they need no
        /// anchor. Kept for symmetry with the other forge bodies' hooks in MiniBossFactory.
        /// </summary>
        public static bool TryExplicitContact(string enemyName, string clipName, out float normalized)
        {
            normalized = 0f;
            return false;
        }
    }
}
