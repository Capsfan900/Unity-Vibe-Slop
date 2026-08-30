using UnityEngine;

namespace VibeGame1
{
    public class SoulsWallet : MonoBehaviour
    {
        public static SoulsWallet I { get; private set; }
        public int Souls { get; private set; }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
        }

        void Start() { GameEvents.RaiseSoulsChanged(Souls); }

        public void Add(int amount)
        {
            if (amount <= 0) return;
            Souls += amount;
            GameEvents.RaiseSoulsChanged(Souls);
        }

        public bool TrySpend(int amount)
        {
            if (amount > Souls) return false;
            Souls -= amount;
            GameEvents.RaiseSoulsChanged(Souls);
            return true;
        }

        public int TakeAll()
        {
            int s = Souls;
            Souls = 0;
            GameEvents.RaiseSoulsChanged(Souls);
            return s;
        }
    }
}
