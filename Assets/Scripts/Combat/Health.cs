using System;
using UnityEngine;

namespace VibeGame1
{
    public class Health : MonoBehaviour
    {
        [SerializeField] float max = 100f;

        /// <summary>Boss rule: reaching 0 HP does not kill, it raises OnZeroHealth (posture break). Only execute damage kills.</summary>
        public bool deathIsStagger;
        public bool Invulnerable;

        public float Max => max;
        public float Current { get; private set; }
        public bool IsDead { get; private set; }
        public float Ratio => max > 0 ? Current / max : 0f;

        public event Action<DamageInfo> OnDamaged;
        public event Action OnDied;
        public event Action OnZeroHealth;
        public event Action<float, float> OnChanged;

        void Awake() { Current = max; }

        public void SetMax(float m, bool keepRatio)
        {
            float r = max > 0 ? Current / max : 1f;
            max = Mathf.Max(1f, m);
            Current = keepRatio ? max * r : Mathf.Min(Current, max);
            OnChanged?.Invoke(Current, max);
        }

        public void TakeDamage(in DamageInfo d)
        {
            if (IsDead) return;
            if (Invulnerable && !d.isExecute) return;

            if (d.isExecute && deathIsStagger)
            {
                Current = 0f;
                OnDamaged?.Invoke(d);
                OnChanged?.Invoke(Current, max);
                IsDead = true;
                OnDied?.Invoke();
                return;
            }

            if (deathIsStagger && Current <= 0f) return; // waiting for the deathblow

            Current = Mathf.Max(0f, Current - d.damage);
            OnDamaged?.Invoke(d);
            OnChanged?.Invoke(Current, max);

            if (Current <= 0f)
            {
                if (deathIsStagger) OnZeroHealth?.Invoke();
                else { IsDead = true; OnDied?.Invoke(); }
            }
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Current = Mathf.Min(max, Current + amount);
            OnChanged?.Invoke(Current, max);
        }

        public void ResetFull()
        {
            IsDead = false;
            Current = max;
            OnChanged?.Invoke(Current, max);
        }

        public void SetCurrent(float v)
        {
            Current = Mathf.Clamp(v, 0f, max);
            OnChanged?.Invoke(Current, max);
        }
    }
}
