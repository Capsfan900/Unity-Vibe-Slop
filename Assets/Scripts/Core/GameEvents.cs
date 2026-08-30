using System;

namespace VibeGame1
{
    /// <summary>Static event bus. Gameplay raises, HUD listens. Keeps UI out of gameplay code.</summary>
    public static class GameEvents
    {
        public static event Action<float, float> PlayerHealthChanged;
        public static event Action<float, float> JuiceChanged;
        public static event Action<int, int> FlaskChanged;
        public static event Action<int> SoulsChanged;
        public static event Action<WeaponData> WeaponChanged;
        public static event Action<ParryResult> ParryResolved;
        public static event Action<float> PlayerDamaged;
        public static event Action PlayerDied;
        public static event Action PlayerRespawned;
        public static event Action<Checkpoint> CheckpointReached;
        public static event Action<EnemyController> EnemyKilled;
        public static event Action<BossController> BossStarted;
        public static event Action<float, float, int> BossHealthChanged;
        public static event Action<float, float> BossPostureChanged;
        public static event Action BossDefeated;
        public static event Action<string> PromptChanged;
        public static event Action UltimateUsed;

        public static void RaisePlayerHealthChanged(float c, float m) => PlayerHealthChanged?.Invoke(c, m);
        public static void RaiseJuiceChanged(float v) => JuiceChanged?.Invoke(v, 100f);
        public static void RaiseFlaskChanged(int c, int m) => FlaskChanged?.Invoke(c, m);
        public static void RaiseSoulsChanged(int s) => SoulsChanged?.Invoke(s);
        public static void RaiseWeaponChanged(WeaponData w) => WeaponChanged?.Invoke(w);
        public static void RaiseParryResolved(ParryResult r) => ParryResolved?.Invoke(r);
        public static void RaisePlayerDamaged(float d) => PlayerDamaged?.Invoke(d);
        public static void RaisePlayerDied() => PlayerDied?.Invoke();
        public static void RaisePlayerRespawned() => PlayerRespawned?.Invoke();
        public static void RaiseCheckpointReached(Checkpoint c) => CheckpointReached?.Invoke(c);
        public static void RaiseEnemyKilled(EnemyController e) => EnemyKilled?.Invoke(e);
        public static void RaiseBossStarted(BossController b) => BossStarted?.Invoke(b);
        public static void RaiseBossHealthChanged(float c, float m, int seg) => BossHealthChanged?.Invoke(c, m, seg);
        public static void RaiseBossPostureChanged(float c, float m) => BossPostureChanged?.Invoke(c, m);
        public static void RaiseBossDefeated() => BossDefeated?.Invoke();
        public static void RaisePromptChanged(string s) => PromptChanged?.Invoke(s);
        public static void RaiseUltimateUsed() => UltimateUsed?.Invoke();

        /// <summary>Clear all subscribers (domain reload safety when Enter Play Mode options disable reload).</summary>
        public static void ClearAll()
        {
            PlayerHealthChanged = null; JuiceChanged = null; FlaskChanged = null; SoulsChanged = null;
            WeaponChanged = null; ParryResolved = null; PlayerDamaged = null; PlayerDied = null;
            PlayerRespawned = null; CheckpointReached = null; EnemyKilled = null; BossStarted = null;
            BossHealthChanged = null; BossPostureChanged = null; BossDefeated = null; PromptChanged = null;
            UltimateUsed = null;
        }
    }
}
