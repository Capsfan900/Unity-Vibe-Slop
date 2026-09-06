using TMPro;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The movement budget on screen: a bar segmented into dash-sized ticks, three ability pips (DASH,
    /// AIR, WALL) that light when the ability is actually available RIGHT NOW, and a refusal flash that
    /// names what you could not afford. Built by <c>HudBuilder</c>, driven from <c>GameEvents</c> plus a
    /// per-frame read of the motor for the pips (a pip is "can I do it this frame", which is stamina AND
    /// cooldown AND the air-dash charge, and only the motor knows all three).
    ///
    /// <para><b>Readability rules.</b> The bar sits at 45% alpha while full and idle so it never competes
    /// with the health/posture read, and snaps to full alpha the moment it is spent or refused. Unscaled
    /// time throughout: this is UI, and it must keep animating through hitstop and pause.</para>
    /// </summary>
    public class StaminaView : MonoBehaviour
    {
        public BarView bar;
        public TMP_Text label;
        public TMP_Text dashPip, airPip, wallPip;
        public CanvasGroup group;

        static readonly Color PipOn = new Color(0.55f, 0.95f, 1f, 1f);
        static readonly Color PipOff = new Color(1f, 1f, 1f, 0.22f);
        static readonly Color Refuse = new Color(1f, 0.25f, 0.2f, 1f);
        const float IdleAlpha = 0.45f;

        FirstPersonMotor motor;
        PlayerStamina stamina;
        float refusedUntil;
        float lastSpendAt = -99f;
        float lastRatio = 1f;

        void OnEnable()
        {
            GameEvents.StaminaChanged += OnChanged;
            GameEvents.StaminaRefused += OnRefused;
            if (label != null) label.text = "STAMINA";
        }

        void OnDisable()
        {
            GameEvents.StaminaChanged -= OnChanged;
            GameEvents.StaminaRefused -= OnRefused;
        }

        void OnChanged(float c, float m)
        {
            float r = m > 0f ? Mathf.Clamp01(c / m) : 0f;
            if (r < lastRatio - 1e-4f) lastSpendAt = Time.unscaledTime;
            lastRatio = r;
            if (bar != null) bar.Set(r);
        }

        void OnRefused(StaminaAction what)
        {
            refusedUntil = Time.unscaledTime + 0.8f;
            if (bar != null) bar.Flash(Refuse, 0.3f);
            if (label != null)
            {
                label.text = what == StaminaAction.Dash ? "NO STAMINA  -  DASH"
                           : what == StaminaAction.WallRun ? "NO STAMINA  -  WALL RUN"
                           : "NO STAMINA  -  WALL JUMP";
                label.color = Refuse;
            }
        }

        void Update()
        {
            if (motor == null) motor = FindAnyObjectByType<FirstPersonMotor>();
            if (stamina == null && motor != null) stamina = motor.GetComponent<PlayerStamina>();

            float now = Time.unscaledTime;
            if (label != null && now >= refusedUntil && label.text != "STAMINA")
            {
                label.text = "STAMINA";
                label.color = new Color(1f, 1f, 1f, 0.6f);
            }

            bool full = stamina == null || stamina.IsFull;
            bool busy = now - lastSpendAt < 1.2f || now < refusedUntil;
            float wantAlpha = full && !busy ? IdleAlpha : 1f;
            if (group != null)
                group.alpha = Mathf.MoveTowards(group.alpha, wantAlpha, Time.unscaledDeltaTime * (wantAlpha > group.alpha ? 12f : 2f));

            if (motor == null) return;
            Pip(dashPip, motor.CanDashNow);
            Pip(airPip, !motor.AirDashUsed);
            Pip(wallPip, motor.CanWallRunNow);
        }

        static void Pip(TMP_Text t, bool on)
        {
            if (t == null) return;
            Color want = on ? PipOn : PipOff;
            if (t.color != want) t.color = want;
        }
    }
}
