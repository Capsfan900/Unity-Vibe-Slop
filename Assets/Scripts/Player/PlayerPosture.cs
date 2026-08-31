using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Sekiro-style player posture. A perfect deflect costs nothing; blocking late and eating hits fill
    /// the bar. When it fills the player is staggered — no parry, no attack, and incoming damage is
    /// amplified — which is the punish that makes mashing the parry button worse than not pressing.
    /// Regeneration is faster the healthier the player is (Sekiro rule).
    /// </summary>
    public class PlayerPosture : MonoBehaviour
    {
        public float Max { get; private set; } = 100f;
        public float Current { get; private set; }
        public bool IsBroken { get; private set; }
        public float Ratio => Max > 0f ? Current / Max : 0f;

        PlayerStatsData data;
        PlayerStats stats;
        Health health;
        ParryController parryCtl;

        float lastHitTime = -99f;
        float brokenUntil;

        void Awake()
        {
            stats = GetComponent<PlayerStats>();
            health = GetComponent<Health>();
            parryCtl = GetComponent<ParryController>();
        }

        void OnEnable() { GameEvents.PlayerRespawned += ResetFull; }
        void OnDisable() { GameEvents.PlayerRespawned -= ResetFull; }

        void Start()
        {
            if (GameManager.I != null) data = GameManager.I.statsData;
            if (data == null && stats != null) data = stats.data;
            RefreshMax(false);
            Current = 0f;
            IsBroken = false;
            Raise();
        }

        float ComputeMax()
        {
            if (data == null) return 100f;
            int vit = stats != null ? stats.Vitality : 0;
            return Mathf.Max(1f, data.basePosture + vit * data.posturePerVitality);
        }

        /// <summary>Recompute Max from the stat sheet (picks up Vitality level-ups automatically).</summary>
        public void RefreshMax(bool raise = true)
        {
            float m = ComputeMax();
            if (Mathf.Approximately(m, Max)) return;
            Max = m;
            Current = Mathf.Min(Current, Max);
            if (raise) Raise();
        }

        public void Add(float amount)
        {
            if (IsBroken || amount <= 0f) return;
            lastHitTime = Time.time;
            var (v, broke) = PostureMath.Apply(Current, Max, amount);
            Current = v;
            Raise();
            if (broke) Break();
        }

        public void Break()
        {
            if (IsBroken) return;
            IsBroken = true;
            Current = Max;
            brokenUntil = Time.time + (data != null ? data.postureStaggerSeconds : 1.5f);
            Raise();
            GameEvents.RaisePlayerPostureBroken();

            var feel = GameManager.I != null ? GameManager.I.feel : null;
            AudioManager.Play(Sfx.PostureBreak);
            if (CameraShake.I) CameraShake.I.Big();
            if (ScreenFlash.I) ScreenFlash.I.Flash(feel != null ? feel.hurtFlash : Color.red, 0.55f, 0.45f);
            if (CameraFX.I) CameraFX.I.VignettePulse(0.45f, 0.6f);
        }

        public void ResetFull()
        {
            IsBroken = false;
            Current = 0f;
            lastHitTime = -99f;
            Raise();
        }

        void Update()
        {
            RefreshMax();

            if (IsBroken)
            {
                if (Time.time >= brokenUntil) ResetFull();
                return;
            }

            if (Current <= 0f || data == null) return;
            if (Time.time - lastHitTime < data.postureRegenDelay) return;

            float healthRatio = health != null ? health.Ratio : 1f;
            float rate = data.postureRegenPerSecond * Mathf.Lerp(0.5f, 1.5f, healthRatio);

            // TURTLING IS NOT FREE. Holding the guard suspends regeneration (shipped multiplier 0), so
            // the posture a stance spends is not quietly refunded while the stance is still up. Without
            // this a player could hold RMB forever: guarded hits cost only posture, and posture that
            // regenerates through the guard makes the fight an unloseable, unwinnable stalemate. The
            // price of putting the blade down is what makes the deflect worth pressing for.
            if (parryCtl != null && parryCtl.IsGuarding)
            {
                float g = data.guardPostureRegenMultiplier;
                if (g <= 0f) return;
                rate *= g;
            }
            float nv = PostureMath.Regen(Current, rate, Time.deltaTime);
            if (!Mathf.Approximately(nv, Current))
            {
                Current = nv;
                Raise();
            }
        }

        void Raise() => GameEvents.RaisePlayerPostureChanged(Current, Max);
    }
}
