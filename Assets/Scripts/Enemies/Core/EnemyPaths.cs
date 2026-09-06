namespace VibeGame1
{
    /// <summary>
    /// The two enemy FAMILIES and where their data lives (2026-09-06 split, the user's ask: "any enemy
    /// meant to accommodate the parkour section is separated and mostly shoots projectiles").
    ///
    /// <para><b>parkour_enemies</b> — sentries on the spans: <c>Sentry_*</c>. Never melee, hold a perch,
    /// fire parriable bolts; a deflect is a boost, a stagger is a dash target. Code: <c>Enemies/parkour_enemies</c>
    /// (Projectile, ProjectileShooter, ProjectileMath) plus <c>Player/FlareGrapple</c> (a broken sentry detonates into a flare you grapple).</para>
    /// <para><b>souls_enemies</b> — the duels: Grunt, Heavy, the Warden (Boss) and every <c>Legendary_*</c>.
    /// Melee movesets with cooled signatures, the flask interrupt, posture and the deathblow. Code:
    /// <c>Enemies/souls_enemies</c> (BossController) on the shared brain in <c>Enemies/Core</c>.</para>
    ///
    /// <para>Everything shared (EnemyController, EnemyVisuals, Posture, the marker, the bar) stays in Core
    /// and in the one namespace: folders carry the taxonomy, the C# does not. A name decides the family:
    /// <c>Sentry_</c> is parkour, anything else is souls. Paths go through here so a move is one edit.</para>
    /// </summary>
    public static class EnemyPaths
    {
        public const string Root = "Assets/Data/Enemies";
        public const string Parkour = Root + "/parkour_enemies";
        public const string Souls = Root + "/souls_enemies";
        public const string MovesetsRoot = "Assets/Data/Movesets";
        public const string ParkourMovesets = MovesetsRoot + "/parkour_enemies";
        public const string SoulsMovesets = MovesetsRoot + "/souls_enemies";

        public const string ParkourPrefix = "Sentry_";

        /// <summary>A data or prefab name that belongs to the parkour family.</summary>
        public static bool IsParkourName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith(ParkourPrefix);
        }

        /// <summary>"Grunt" → Assets/Data/Enemies/souls_enemies/Grunt.asset; "Sentry_Grunt" → parkour_enemies/…</summary>
        public static string Data(string name)
        {
            return (IsParkourName(name) ? Parkour : Souls) + "/" + name + ".asset";
        }

        /// <summary>"Grunt_Moveset" → Assets/Data/Movesets/souls_enemies/Grunt_Moveset.asset.</summary>
        public static string Moveset(string name)
        {
            return (IsParkourName(name) ? ParkourMovesets : SoulsMovesets) + "/" + name + ".asset";
        }
    }
}
