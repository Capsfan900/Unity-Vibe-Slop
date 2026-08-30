using System.Collections;
using TMPro;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Subscribes to GameEvents and pushes values into the HUD widgets built by Editor/HudBuilder.</summary>
    public class HUDController : MonoBehaviour
    {
        public BarView healthBar;
        public BarView juiceBar;
        public TMP_Text healthText;
        public TMP_Text flaskText;
        public TMP_Text soulsText;
        public TMP_Text timerText;
        public TMP_Text weaponText;
        public TMP_Text parryPopup;
        public TMP_Text centerText;
        public TMP_Text hintText;
        public GameObject juiceReadyLabel;

        Coroutine popup;
        Coroutine center;
        int perfectStreak;

        void OnEnable()
        {
            GameEvents.PlayerHealthChanged += OnHealth;
            GameEvents.JuiceChanged += OnJuice;
            GameEvents.FlaskChanged += OnFlask;
            GameEvents.SoulsChanged += OnSouls;
            GameEvents.WeaponChanged += OnWeapon;
            GameEvents.ParryResolved += OnParry;
            GameEvents.PlayerDied += OnDied;
            GameEvents.PlayerRespawned += OnRespawned;
            GameEvents.CheckpointReached += OnCheckpoint;
            GameEvents.BossDefeated += OnBossDefeated;
            GameEvents.UltimateUsed += OnUltimate;
            GameEvents.BossStarted += OnBossStarted;
        }

        void OnDisable()
        {
            GameEvents.PlayerHealthChanged -= OnHealth;
            GameEvents.JuiceChanged -= OnJuice;
            GameEvents.FlaskChanged -= OnFlask;
            GameEvents.SoulsChanged -= OnSouls;
            GameEvents.WeaponChanged -= OnWeapon;
            GameEvents.ParryResolved -= OnParry;
            GameEvents.PlayerDied -= OnDied;
            GameEvents.PlayerRespawned -= OnRespawned;
            GameEvents.CheckpointReached -= OnCheckpoint;
            GameEvents.BossDefeated -= OnBossDefeated;
            GameEvents.UltimateUsed -= OnUltimate;
            GameEvents.BossStarted -= OnBossStarted;
        }

        void Start()
        {
            if (parryPopup != null) parryPopup.alpha = 0f;
            if (centerText != null) centerText.alpha = 0f;
            if (juiceReadyLabel != null) juiceReadyLabel.SetActive(false);
            if (hintText != null) hintText.text = "WASD move   SPACE jump   SHIFT dash   LMB attack   RMB parry   F flask   Q ultimate   1/2/3 weapons  4 test blade   TAB level up\n<alpha=#88>F5 warp to boss   F6 restore   F7 +souls   F8 god mode";
        }

        void Update()
        {
            if (timerText != null && SpeedrunTimer.I != null) timerText.text = SpeedrunTimer.Format(SpeedrunTimer.I.Elapsed);
        }

        void OnHealth(float c, float m)
        {
            if (healthBar != null) healthBar.Set(m > 0 ? c / m : 0f);
            if (healthText != null) healthText.text = $"{Mathf.CeilToInt(c)} / {Mathf.CeilToInt(m)}";
        }

        void OnJuice(float v, float max)
        {
            if (juiceBar != null) juiceBar.Set(v / max);
            if (juiceReadyLabel != null) juiceReadyLabel.SetActive(v >= max - 0.01f);
        }

        void OnFlask(int c, int m)
        {
            if (flaskText != null) flaskText.text = $"FLASK  {c} / {m}";
        }

        void OnSouls(int s)
        {
            if (soulsText != null) soulsText.text = $"SOULS  {s}";
        }

        void OnWeapon(WeaponData w)
        {
            if (weaponText == null || w == null) return;
            weaponText.text = w.displayName.ToUpperInvariant();
            weaponText.color = w.neon.maxColorComponent > 1f ? w.neon / w.neon.maxColorComponent : w.neon;
        }

        void OnParry(ParryResult r)
        {
            string s = null; Color c = Color.white;
            switch (r)
            {
                case ParryResult.Perfect: perfectStreak++; s = perfectStreak > 1 ? $"PERFECT x{perfectStreak}" : "PERFECT"; c = new Color(0.3f, 1f, 1f); break;
                case ParryResult.Blocked: perfectStreak = 0; s = "BLOCK"; c = new Color(1f, 0.85f, 0.3f); break;
                case ParryResult.Hit: perfectStreak = 0; break;
            }
            if (s != null) ShowPopup(s, c);
        }

        void ShowPopup(string s, Color c)
        {
            if (parryPopup == null) return;
            if (popup != null) StopCoroutine(popup);
            popup = StartCoroutine(PopupCo(s, c));
        }

        IEnumerator PopupCo(string s, Color c)
        {
            parryPopup.text = s;
            parryPopup.color = c;
            float t = 0f;
            while (t < 0.6f)
            {
                float k = t / 0.6f;
                parryPopup.alpha = 1f - k * k;
                parryPopup.transform.localScale = Vector3.one * (1.4f - 0.4f * Mathf.Min(1f, k * 6f));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            parryPopup.alpha = 0f;
        }

        void ShowCenter(string s, Color c, float seconds)
        {
            if (centerText == null) return;
            if (center != null) StopCoroutine(center);
            center = StartCoroutine(CenterCo(s, c, seconds));
        }

        IEnumerator CenterCo(string s, Color c, float seconds)
        {
            centerText.text = s;
            centerText.color = c;
            float t = 0f;
            while (t < seconds)
            {
                float k = t / seconds;
                centerText.alpha = Mathf.Min(1f, k * 8f) * (1f - Mathf.Pow(k, 4f));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            centerText.alpha = 0f;
        }

        void OnDied() { perfectStreak = 0; ShowCenter("YOU DIED", new Color(1f, 0.15f, 0.25f), 1.8f); }
        void OnRespawned() { ShowCenter("", Color.white, 0.01f); }
        void OnCheckpoint(Checkpoint c) { ShowCenter("CHECKPOINT", new Color(0.3f, 1f, 1f), 1.2f); }
        void OnUltimate() { ShowCenter("OVERDRIVE", new Color(0.3f, 1f, 1f), 1.2f); }
        void OnBossStarted(BossController b) { ShowCenter(b != null && b.data != null ? b.data.displayName.ToUpperInvariant() : "BOSS", new Color(1f, 0.2f, 0.4f), 2f); }

        void OnBossDefeated()
        {
            string time = SpeedrunTimer.I != null ? SpeedrunTimer.Format(SpeedrunTimer.I.Elapsed) : "";
            ShowCenter($"LEVEL CLEAR\n{time}", new Color(1f, 0.9f, 0.3f), 8f);
            if (GameManager.I != null) StartCoroutine(WinCo());
        }

        IEnumerator WinCo()
        {
            yield return new WaitForSecondsRealtime(2f);
            if (GameManager.I != null && GameManager.I.State == GameState.Playing) GameManager.I.SetState(GameState.Won);
            if (GameManager.I != null) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }
    }
}
