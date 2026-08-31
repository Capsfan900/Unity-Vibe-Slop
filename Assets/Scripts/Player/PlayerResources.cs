using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The Pyre meter and flask charges.
    ///
    /// <para><b>Pyre</b> is the parry charge meter. It fills on every SUCCESSFUL parry — a perfect
    /// deflect stokes it fully, a block only <see cref="PlayerStatsData.pyreBlockFraction"/> of that,
    /// because surviving a hit is not the same as mastering it. It drives the fire on the weapon
    /// (<see cref="WeaponEmber"/>) and, at full, unlocks the weapon's super attack.</para>
    ///
    /// <para><b>It does not decay.</b> This is a speedrun platformer: fights are separated by long
    /// stretches of parkour, and a bar that bleeds out between arenas would punish the traversal the
    /// game is built around. The bar has exactly one sink — the super attack spends it in full — and
    /// exactly one reset: death.</para>
    /// </summary>
    public class PlayerResources : MonoBehaviour
    {
        public float Pyre { get; private set; }
        public float MaxPyre { get; private set; } = 100f;
        public int FlaskCharges { get; private set; }
        public int MaxFlask { get; private set; } = 3;

        public bool PyreFull => Pyre >= MaxPyre - 0.01f;

        /// <summary>0..1. The single read every presentation layer uses (HUD bar, weapon fire).</summary>
        public float PyreRatio => MaxPyre > 0f ? Mathf.Clamp01(Pyre / MaxPyre) : 0f;

        void OnEnable() { GameEvents.PlayerRespawned += OnRespawned; }
        void OnDisable() { GameEvents.PlayerRespawned -= OnRespawned; }

        void Start()
        {
            var gm = GameManager.I;
            // Guarded: a PlayerStats asset written before Pyre existed deserialises maxPyre as 0, which
            // would make PyreFull permanently true and hand out a free super every frame. Rule 9 in its
            // most dangerous form — a missing value that fails OPEN.
            if (gm != null && gm.statsData != null && gm.statsData.maxPyre > 0f) MaxPyre = gm.statsData.maxPyre;
            GameEvents.RaisePyreChanged(Pyre, MaxPyre);
            GameEvents.RaiseFlaskChanged(FlaskCharges, MaxFlask);
        }

        /// <summary>The fire goes out when you do. Nothing else empties the bar but the super.</summary>
        void OnRespawned() => ConsumePyre();

        public void AddPyre(float amount)
        {
            Pyre = Mathf.Clamp(Pyre + amount, 0f, MaxPyre);
            GameEvents.RaisePyreChanged(Pyre, MaxPyre);
        }

        public void ConsumePyre()
        {
            Pyre = 0f;
            GameEvents.RaisePyreChanged(0f, MaxPyre);
        }

        public bool UseFlask()
        {
            if (FlaskCharges <= 0) return false;
            FlaskCharges--;
            GameEvents.RaiseFlaskChanged(FlaskCharges, MaxFlask);
            return true;
        }

        public void RefillFlask()
        {
            FlaskCharges = MaxFlask;
            GameEvents.RaiseFlaskChanged(FlaskCharges, MaxFlask);
        }

        public void SetMaxFlask(int max, bool refill)
        {
            int delta = max - MaxFlask;
            MaxFlask = max;
            if (refill) FlaskCharges = max;
            else if (delta > 0) FlaskCharges = Mathf.Min(max, FlaskCharges + delta);
            GameEvents.RaiseFlaskChanged(FlaskCharges, MaxFlask);
        }
    }
}
