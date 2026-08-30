namespace VibeGame1
{
    /// <summary>Physics layer indices. Created by Editor/ProjectSetup.cs.</summary>
    public static class Layers
    {
        public const int Player = 6;
        public const int Enemy = 7;
        public const int Interactable = 8;

        public static int PlayerMask => 1 << Player;
        public static int EnemyMask => 1 << Enemy;
    }
}
