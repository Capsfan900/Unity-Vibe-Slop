using UnityEngine;

namespace VibeGame1
{
    /// <summary>Parry Juice and flask charges.</summary>
    public class PlayerResources : MonoBehaviour
    {
        public float Juice { get; private set; }
        public float MaxJuice { get; private set; } = 100f;
        public int FlaskCharges { get; private set; }
        public int MaxFlask { get; private set; } = 3;

        public bool JuiceFull => Juice >= MaxJuice - 0.01f;

        void Start()
        {
            var gm = GameManager.I;
            if (gm != null && gm.statsData != null) MaxJuice = gm.statsData.maxJuice;
            GameEvents.RaiseJuiceChanged(Juice / MaxJuice * 100f);
            GameEvents.RaiseFlaskChanged(FlaskCharges, MaxFlask);
        }

        public void AddJuice(float amount)
        {
            Juice = Mathf.Clamp(Juice + amount, 0f, MaxJuice);
            GameEvents.RaiseJuiceChanged(Juice / MaxJuice * 100f);
        }

        public void ConsumeJuice()
        {
            Juice = 0f;
            GameEvents.RaiseJuiceChanged(0f);
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
