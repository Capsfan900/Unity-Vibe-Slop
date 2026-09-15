using System;

namespace VibeGame1
{
    /// <summary>Static event bus. Gameplay raises, HUD listens. Keeps UI out of gameplay code.</summary>
    public static class GameEvents
    {
        public static event Action<float, float> PlayerHealthChanged;
        /// <summary>The Pyre meter changed: (current, max). Fires on every successful parry and on spend.</summary>
        public static event Action<float, float> PyreChanged;
        /// <summary>Wand cooldown ticked: (secondsRemaining, totalSeconds). 0 remaining = ready.</summary>
        public static event Action<float, float> WandCooldownChanged;
        public static event Action<int, int> FlaskChanged;
        public static event Action<int> SoulsChanged;
        public static event Action<WeaponData> WeaponChanged;
        public static event Action<WandData> WandChanged;
        public static event Action<ParryResult> ParryResolved;
        public static event Action<float> PlayerDamaged;
        public static event Action PlayerDied;
        public static event Action PlayerRespawned;
        public static event Action<Checkpoint> CheckpointReached;
        public static event Action<EnemyController> EnemyKilled;
        public static event Action<BossController> BossStarted;
        /// <summary>A mini-boss solar realm was entered (2026-09-14): the name card, camera hold and duo bars key off it.</summary>
        public static event Action<BossArenaTrigger> RealmFightStarted;
        public static event Action<float, float, int> BossHealthChanged;
        public static event Action<float, float> BossPostureChanged;
        public static event Action BossDefeated;
        /// <summary>
        /// The standing prompt line, as (owner, text). The owner key was added 2026-09-06 because this slot
        /// has SEVERAL writers and had no owner: the grapple, the deathblow, the surge countdown, the wand
        /// altar, the sandbox switch, the level editor and the dev keys all write it, every one of them
        /// EDGE-TRIGGERED. So a writer clearing with "" blanked whatever another writer had standing there,
        /// and — because the blanked writer's own string had not changed — it never re-raised. See
        /// <see cref="PromptOwner"/> and PromptView.AcceptsStandingWrite.
        /// </summary>
        public static event Action<string, string> PromptChanged;
        /// <summary>A momentary prompt (text, seconds) drawn over the standing one. See RaisePromptFlash.</summary>
        public static event Action<string, float> PromptFlash;
        public static event Action UltimateUsed;
        public static event Action<float, float> PlayerPostureChanged;
        public static event Action PlayerPostureBroken;
        /// <summary>True when a staggered enemy is in deathblow range (drives the big HUD banner).</summary>
        public static event Action<bool> DeathblowReady;
        /// <summary>Fires whenever the carried item list changes (pickup, use, respawn reset).</summary>
        public static event Action<ItemData[]> ItemsChanged;
        public static event Action<ItemData> ItemPickedUp;
        public static event Action<ItemData> ItemUsed;
        /// <summary>A riposte (deathblow/critical attack) landed on this enemy.</summary>
        public static event Action<EnemyController> RiposteLanded;
        /// <summary>Movement budget (PlayerStamina). (current, max). The HUD bar and the ability pips read this.</summary>
        public static event Action<float, float> StaminaChanged;
        /// <summary>A movement ability was refused for lack of stamina. The HUD flashes the bar and names it.</summary>
        public static event Action<StaminaAction> StaminaRefused;
        /// <summary>Run-local souls, distinct regular kills, or split progress changed.</summary>
        public static event Action<LevelRunScorer> RunScoreChanged;
        /// <summary>An ordered run split closed and received its data-authored grade bonus.</summary>
        public static event Action<RunSplitResult> SplitGraded;
        /// <summary>The boss ended a run and its score requirements were adjudicated.</summary>
        public static event Action<LevelRunResult> LevelRunEvaluated;

        public static void RaisePlayerHealthChanged(float c, float m) => PlayerHealthChanged?.Invoke(c, m);
        public static void RaisePyreChanged(float v, float max) => PyreChanged?.Invoke(v, max);
        public static void RaiseWandCooldownChanged(float remaining, float total) => WandCooldownChanged?.Invoke(remaining, total);
        public static void RaiseFlaskChanged(int c, int m) => FlaskChanged?.Invoke(c, m);
        public static void RaiseSoulsChanged(int s) => SoulsChanged?.Invoke(s);
        public static void RaiseWeaponChanged(WeaponData w) => WeaponChanged?.Invoke(w);
        public static void RaiseWandChanged(WandData w) => WandChanged?.Invoke(w);
        public static void RaiseParryResolved(ParryResult r) => ParryResolved?.Invoke(r);
        public static void RaisePlayerDamaged(float d) => PlayerDamaged?.Invoke(d);
        public static void RaisePlayerDied() => PlayerDied?.Invoke();
        public static void RaisePlayerRespawned() => PlayerRespawned?.Invoke();
        public static void RaiseCheckpointReached(Checkpoint c) => CheckpointReached?.Invoke(c);
        public static void RaiseEnemyKilled(EnemyController e) => EnemyKilled?.Invoke(e);
        public static void RaiseBossStarted(BossController b) => BossStarted?.Invoke(b);
        public static void RaiseRealmFightStarted(BossArenaTrigger a) => RealmFightStarted?.Invoke(a);
        public static void RaiseBossHealthChanged(float c, float m, int seg) => BossHealthChanged?.Invoke(c, m, seg);
        public static void RaiseBossPostureChanged(float c, float m) => BossPostureChanged?.Invoke(c, m);
        public static void RaiseBossDefeated() => BossDefeated?.Invoke();
        /// <summary>
        /// An UNOWNED standing write. Kept for callers that own the line outright for as long as they are
        /// alive; an unowned cue may be cleared by anyone. Prefer the two-argument form.
        /// </summary>
        public static void RaisePromptChanged(string s) => PromptChanged?.Invoke(PromptOwner.Anonymous, s);

        /// <summary>
        /// A standing cue written by <paramref name="owner"/> (a <see cref="PromptOwner"/> key). A non-empty
        /// string takes the line; an empty one clears it ONLY if this owner still holds it, so one writer
        /// going quiet can never blank another writer's live cue.
        /// </summary>
        public static void RaisePromptChanged(string owner, string s) => PromptChanged?.Invoke(owner ?? PromptOwner.Anonymous, s);

        /// <summary>
        /// A momentary prompt shown OVER the standing one for <paramref name="seconds"/>, then gone —
        /// "PERFECT", "NO TARGET". 2026-09-06: these used to be written to the same single channel as the
        /// standing cues ("DEATHBLOW [ATTACK]", "GRAPPLE [DASH]"), which are EDGE-TRIGGERED — a writer only
        /// re-raises when its own string changes — so one PERFECT erased a live cue until the player looked
        /// away and back. PromptView keeps the two apart and restores the standing cue when the flash ends.
        /// </summary>
        public static void RaisePromptFlash(string s, float seconds) => PromptFlash?.Invoke(s, seconds);
        public static void RaiseUltimateUsed() => UltimateUsed?.Invoke();
        public static void RaisePlayerPostureChanged(float c, float m) => PlayerPostureChanged?.Invoke(c, m);
        public static void RaisePlayerPostureBroken() => PlayerPostureBroken?.Invoke();
        public static void RaiseDeathblowReady(bool ready) => DeathblowReady?.Invoke(ready);
        public static void RaiseItemsChanged(ItemData[] items) => ItemsChanged?.Invoke(items);
        public static void RaiseItemPickedUp(ItemData i) => ItemPickedUp?.Invoke(i);
        public static void RaiseItemUsed(ItemData i) => ItemUsed?.Invoke(i);
        public static void RaiseRiposteLanded(EnemyController e) => RiposteLanded?.Invoke(e);
        public static void RaiseStaminaChanged(float c, float m) => StaminaChanged?.Invoke(c, m);
        public static void RaiseStaminaRefused(StaminaAction a) => StaminaRefused?.Invoke(a);
        public static void RaiseRunScoreChanged(LevelRunScorer scorer) => RunScoreChanged?.Invoke(scorer);
        public static void RaiseSplitGraded(RunSplitResult result) => SplitGraded?.Invoke(result);
        public static void RaiseLevelRunEvaluated(LevelRunResult result) => LevelRunEvaluated?.Invoke(result);

        /// <summary>Clear all subscribers (domain reload safety when Enter Play Mode options disable reload).</summary>
        public static void ClearAll()
        {
            PlayerHealthChanged = null; PyreChanged = null; WandCooldownChanged = null; FlaskChanged = null; SoulsChanged = null;
            WeaponChanged = null; WandChanged = null; ParryResolved = null; PlayerDamaged = null; PlayerDied = null;
            PlayerRespawned = null; CheckpointReached = null; EnemyKilled = null; BossStarted = null; RealmFightStarted = null;
            BossHealthChanged = null; BossPostureChanged = null; BossDefeated = null; PromptChanged = null;
            UltimateUsed = null; PlayerPostureChanged = null; PlayerPostureBroken = null; DeathblowReady = null;
            ItemsChanged = null; ItemPickedUp = null; ItemUsed = null; RiposteLanded = null;
            StaminaChanged = null;
            StaminaRefused = null;
            PromptFlash = null;
            RunScoreChanged = null;
            SplitGraded = null;
            LevelRunEvaluated = null;
        }
    }
}
