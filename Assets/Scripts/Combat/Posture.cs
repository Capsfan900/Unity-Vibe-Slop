using System;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Sekiro style posture. Fills on hits/parries, breaks at max, regenerates after a delay.</summary>
    public class Posture : MonoBehaviour
    {
        [SerializeField] float max = 60f;
        public float regenPerSecond = 10f;
        public float regenDelay = 1.5f;
        public float staggerSeconds = 3f;
        [Range(0f, 1f)] public float resetFraction = 0.5f;

        public float Max => max;
        public float Current { get; private set; }
        public bool IsBroken { get; private set; }
        public float Ratio => max > 0 ? Current / max : 0f;
        public bool Enabled { get; private set; } = true;

        /// <summary>Optional multiplier on regen (enemy sets it to its health fraction).</summary>
        public Func<float> RegenMultiplier;

        public event Action OnBroken;
        public event Action OnStaggerEnded;
        public event Action<float, float> OnChanged;

        float lastHitTime = -99f;
        float brokenUntil;

        public void Configure(float maxValue, float regen, float delay, float stagger, bool enabled = true)
        {
            Enabled = enabled;
            max = Mathf.Max(1f, maxValue);
            regenPerSecond = regen;
            regenDelay = delay;
            staggerSeconds = stagger;
            Current = 0f;
            IsBroken = false;
            OnChanged?.Invoke(Current, max);
        }

        public void Add(float amount)
        {
            if (!Enabled || IsBroken || amount <= 0f) return;
            lastHitTime = Time.time;
            var (v, broke) = PostureMath.Apply(Current, max, amount);
            Current = v;
            OnChanged?.Invoke(Current, max);
            if (broke) Break();
        }

        public void Break()
        {
            if (!Enabled || IsBroken) return;
            IsBroken = true;
            Current = max;
            brokenUntil = Time.time + staggerSeconds;
            OnChanged?.Invoke(Current, max);
            OnBroken?.Invoke();
        }

        public void EndStagger()
        {
            if (!IsBroken) return;
            IsBroken = false;
            Current = max * resetFraction;
            lastHitTime = Time.time;
            OnChanged?.Invoke(Current, max);
            OnStaggerEnded?.Invoke();
        }

        public void ResetFull()
        {
            IsBroken = false;
            Current = 0f;
            OnChanged?.Invoke(Current, max);
        }

        /// <summary>Extend the current stagger (used while an execute plays).</summary>
        public void HoldStagger(float seconds)
        {
            if (Enabled && IsBroken) brokenUntil = Mathf.Max(brokenUntil, Time.time + seconds);
        }

        void Update()
        {
            if (!Enabled) return;
            if (IsBroken)
            {
                if (Time.time >= brokenUntil) EndStagger();
                return;
            }
            if (Current <= 0f) return;
            if (Time.time - lastHitTime < regenDelay) return;
            float mult = RegenMultiplier != null ? RegenMultiplier() : 1f;
            float nv = PostureMath.Regen(Current, regenPerSecond * mult, Time.deltaTime);
            if (!Mathf.Approximately(nv, Current))
            {
                Current = nv;
                OnChanged?.Invoke(Current, max);
            }
        }
    }
}
