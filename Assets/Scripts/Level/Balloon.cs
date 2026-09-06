using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// A floating orb that launches the player straight up when touched — or, for a player who is
    /// DASHING through it, lets the dash carry on and re-arms it. Neon White's balloon demon, as a
    /// traversal piece rather than an enemy: the user's words were "a floating orb that gives you a
    /// boost and allow you to dash through it".
    ///
    /// <para><b>Touch, not hit.</b> The weapon's hit test is <c>OverlapSphereNonAlloc</c> on the Enemy
    /// layer with triggers ignored, so a swing cannot see this orb; the trigger is the whole interface
    /// for v1. That is also what Neon White's bounce is — you go THROUGH the balloon.</para>
    ///
    /// <para><b>The launch is the motor's.</b> <see cref="FirstPersonMotor.Launch(float)"/> replaces the
    /// vertical speed and resets the air kit (rule 1: it runs on the motor clock and touches nothing
    /// else); this component never writes a velocity. A dash-through calls
    /// <see cref="FirstPersonMotor.RearmDash"/> instead.</para>
    ///
    /// <para>Pops hide the orb and respawn it after <see cref="respawnSeconds"/> with a scale-in, on
    /// UNSCALED time so a hitstop cannot hold a balloon closed. Every number here is written by
    /// <c>PrefabFactory.BuildBalloon</c> / <c>LevelDefinitionBuilder</c> (rule 9).</para>
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class Balloon : MonoBehaviour
    {
        [Tooltip("Upward speed the player leaves with, m/s. REPLACES the vertical speed (a capped jump). " +
                 "14 against gravity -30 is a 3.3 m rise — one storey, the height a level is authored against.")]
        public float launchSpeed = 14f;
        [Tooltip("Seconds before the orb is back after a pop.")]
        public float respawnSeconds = 2.5f;
        [Tooltip("Trigger radius, metres. Generous: a balloon you can miss by 10 cm at 20 m/s is a balloon " +
                 "that reads as broken.")]
        public float radius = 0.6f;
        [Tooltip("The visible orb, scaled in on respawn.")]
        public Transform visual;
        [Tooltip("Idle bob, metres and Hz. Unscaled time.")]
        public float bobHeight = 0.15f;
        public float bobHz = 0.8f;
        [Tooltip("Colour of the pop's sparks. Under the bloom cap on the orb itself; the sparks route " +
                 "through SlashFx, which normalises to a peak of 1.0.")]
        public Color popColor = new Color(1f, 0.76f, 0.29f, 1f);
        [Tooltip("Seconds the respawn scale-in takes.")]
        public float respawnScaleSeconds = 0.25f;

        /// <summary>True while popped and waiting to come back. Read by tests and the harness.</summary>
        public bool IsPopped { get; private set; }
        /// <summary>How many times this orb has popped since the scene loaded / last respawn reset.</summary>
        public int PopCount { get; private set; }

        SphereCollider col;
        Vector3 visualBase;
        float respawnAt;
        float scaleInT;

        void Awake()
        {
            col = GetComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = radius;
            if (visual != null) visualBase = visual.localPosition;
        }

        void OnEnable() { GameEvents.PlayerRespawned += Restore; }
        void OnDisable() { GameEvents.PlayerRespawned -= Restore; }

        void Restore()
        {
            IsPopped = false;
            scaleInT = 1f;
            if (col != null) col.enabled = true;
            if (visual != null) { visual.gameObject.SetActive(true); visual.localScale = Vector3.one; }
        }

        void Update()
        {
            float t = Time.unscaledTime;
            if (IsPopped)
            {
                if (t >= respawnAt)
                {
                    IsPopped = false;
                    scaleInT = 0f;
                    if (col != null) col.enabled = true;
                    if (visual != null) { visual.gameObject.SetActive(true); visual.localScale = Vector3.zero; }
                }
                return;
            }
            if (visual == null) return;
            if (scaleInT < 1f)
            {
                scaleInT = Mathf.Min(1f, scaleInT + Time.unscaledDeltaTime / Mathf.Max(0.01f, respawnScaleSeconds));
                // Ease-out with a hair of overshoot: an orb that inflates back reads as alive.
                float s = 1f - (1f - scaleInT) * (1f - scaleInT);
                visual.localScale = Vector3.one * (s * (1f + 0.12f * Mathf.Sin(scaleInT * Mathf.PI)));
            }
            visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(t * bobHz * 2f * Mathf.PI) * bobHeight);
        }

        void OnTriggerEnter(Collider other)
        {
            if (IsPopped) return;
            var motor = other.GetComponentInParent<FirstPersonMotor>();
            if (motor == null) return;
            Pop(motor);
        }

        /// <summary>The pop. Public so tests and the harness can drive it without a physics step.</summary>
        public void Pop(FirstPersonMotor motor)
        {
            if (IsPopped || motor == null) return;
            IsPopped = true;
            PopCount++;
            respawnAt = Time.unscaledTime + Mathf.Max(0.05f, respawnSeconds);
            if (col != null) col.enabled = false;
            if (visual != null) visual.gameObject.SetActive(false);

            if (TraversalMath.DashesThrough(motor.IsDashing)) motor.RearmDash();
            else motor.Launch(launchSpeed, true);   // the POP trims the carry: pop -> aim -> dash

            // The pop itself. Sparks through SlashFx (peak channel 1.0, under the 1.05 bloom threshold);
            // Jump pitched up for the burst and a soft Land for the skin of the orb going. Rule 7: no
            // new Sfx entry.
            SlashFx.Sparks(transform.position, Vector3.up, popColor, 14, 6f, 1f);
            SlashFx.Ring(transform.position, Vector3.up, popColor, radius * 1.6f, 0.22f);
            AudioManager.Play(Sfx.Jump, 0.7f, 1.3f, 0.06f);
            AudioManager.Play(Sfx.Land, 0.3f, 1.25f, 0.05f);
        }
    }
}
