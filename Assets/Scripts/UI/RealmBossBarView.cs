using System.Collections;
using TMPro;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A mini-boss realm's presentation (2026-09-14): the souls name card on entry and one health bar per
    /// occupant, so a duo realm shows both. Keyed off <see cref="GameEvents.RealmFightStarted"/>; reads each
    /// occupant's <see cref="Health"/> every frame and hides once the arena clears or the player respawns.
    /// The Warden keeps its own <see cref="BossBarView"/>.
    /// </summary>
    public class RealmBossBarView : MonoBehaviour
    {
        [Header("Health bars (one slot per occupant)")]
        public GameObject root;
        public GameObject[] slots = new GameObject[2];
        public TMP_Text[] slotNames = new TMP_Text[2];
        public BarView[] slotHealth = new BarView[2];

        [Header("Name card")]
        public CanvasGroup card;
        public TMP_Text cardText;
        public float cardSeconds = 3.2f;

        BossArenaTrigger arena;
        readonly Health[] healths = new Health[2];
        Coroutine cardCo;
        float hideAt = -1f;

        void OnEnable()
        {
            GameEvents.RealmFightStarted += OnStarted;
            GameEvents.PlayerRespawned += Hide;
        }

        void OnDisable()
        {
            GameEvents.RealmFightStarted -= OnStarted;
            GameEvents.PlayerRespawned -= Hide;
        }

        void Start() { Hide(); }

        void OnStarted(BossArenaTrigger a)
        {
            arena = a;
            hideAt = -1f;
            var names = new System.Collections.Generic.List<string>();
            var spawners = new[] { a != null ? a.clearSpawner : null, a != null ? a.partnerSpawner : null };
            for (int i = 0; i < 2; i++)
            {
                var inst = spawners[i] != null ? spawners[i].Instance : null;
                healths[i] = inst != null ? inst.GetComponentInChildren<Health>() : null;
                var enemy = inst != null ? inst.GetComponent<EnemyController>() : null;
                string name = enemy != null && enemy.data != null ? enemy.data.displayName : "";
                bool show = healths[i] != null;
                if (i < slots.Length && slots[i] != null) slots[i].SetActive(show);
                if (i < slotNames.Length && slotNames[i] != null) slotNames[i].text = name;
                if (i < slotHealth.Length && slotHealth[i] != null) slotHealth[i].Set(show ? healths[i].Ratio : 0f);
                if (show && !string.IsNullOrEmpty(name)) names.Add(name);
            }
            if (root != null) root.SetActive(names.Count > 0);
            if (cardText != null && names.Count > 0)
            {
                cardText.text = string.Join("\n<size=60%>&</size>\n", names.ToArray());
                if (cardCo != null) StopCoroutine(cardCo);
                cardCo = StartCoroutine(Card());
            }
        }

        void Update()
        {
            if (arena == null) return;
            for (int i = 0; i < 2; i++)
                if (healths[i] != null && i < slotHealth.Length && slotHealth[i] != null)
                    slotHealth[i].Set(healths[i].IsDead ? 0f : healths[i].Ratio);
            if (arena.Cleared && hideAt < 0f) hideAt = Time.unscaledTime + 1.5f;
            if (hideAt >= 0f && Time.unscaledTime >= hideAt) Hide();
        }

        IEnumerator Card()
        {
            // Fade in fast, hold, fade out slow: the Souls title-card envelope.
            float t = 0f;
            while (t < cardSeconds)
            {
                float k = t / cardSeconds;
                if (card != null) card.alpha = Mathf.Min(1f, k * 6f) * Mathf.Clamp01((1f - k) * 3f);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (card != null) card.alpha = 0f;
            cardCo = null;
        }

        void Hide()
        {
            arena = null;
            hideAt = -1f;
            healths[0] = healths[1] = null;
            if (root != null) root.SetActive(false);
            if (cardCo != null) { StopCoroutine(cardCo); cardCo = null; }
            if (card != null) card.alpha = 0f;
        }
    }
}
