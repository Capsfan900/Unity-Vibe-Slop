using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// THE FLARE GRAPPLE (2026-09-06, replaces SentryDash). While a <see cref="SentryFlare"/> still glows,
    /// aiming near it and pressing DASH pulls the player to it along the hook's arc and, on arrival,
    /// TOSSES them upward (motor.Launch: an entry point, never a velocity write -- rule 10). The flare is
    /// spent by the use. It is meant to be used creatively: a flare thrown over a gap is a bridge, one
    /// thrown up a wall is a lift, and one you leave alone is nothing.
    ///
    /// <para>Runs before the motor (execution order) so the press that starts a pull is never also an
    /// ordinary dash. Input is read only through <see cref="InputReader"/> (rule 2). Numbers are written
    /// by PrefabFactory (rule 9).</para>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class FlareGrapple : MonoBehaviour
    {
        [Tooltip("Metres. A glowing flare further than this is not a target.")]
        public float range = 30f;
        [Tooltip("Degrees off the aim inside which a flare is the target. Generous: it is a moving point in the sky.")]
        public float coneDeg = 20f;
        [Tooltip("Seconds of pull to the flare. The hook's 0.35.")]
        public float pullSeconds = 0.35f;
        [Tooltip("Metres per second straight up on arrival: the toss. 14 is a balloon-and-a-bit.")]
        public float tossUpSpeed = 14f;
        [Tooltip("Lens punch on the toss, degrees.")]
        public float tossFovKick = 8f;
        [Tooltip("Colour of the line and the burst.")]
        public Color hue = new Color(0.85f, 0.7f, 1f);
        [Tooltip("Chromatic pulse on the toss: a g-force read, distinct from the ordinary FovKick punch.")]
        public float tossChroma = 0.16f;
        [Tooltip("Seconds the toss chroma pulse takes to decay.")]
        public float tossChromaSeconds = 0.25f;
        [Tooltip("Radius of the expanding ring drawn under the player at the moment of the toss, metres.")]
        public float tossRingRadius = 1.4f;

        /// <summary>The flare a DASH press would fly to right now, or null. HUD and tests.</summary>
        public SentryFlare Target { get; private set; }
        /// <summary>Times the player has been tossed by a flare. Tests.</summary>
        public int Tosses { get; private set; }

        PlayerLook look;
        FirstPersonMotor motor;
        ExecuteInteractor exec;
        OffhandViewmodel offhand;
        string lastPrompt = "";
        bool execHadTarget;
        SentryFlare inFlight;

        void Awake()
        {
            look = GetComponent<PlayerLook>();
            motor = GetComponent<FirstPersonMotor>();
            exec = GetComponent<ExecuteInteractor>();
            offhand = GetComponentInChildren<OffhandViewmodel>(true);
        }

        void OnEnable() { if (motor != null) motor.OnPullEnded += HandlePullEnded; }
        void OnDisable() { if (motor != null) motor.OnPullEnded -= HandlePullEnded; }

        void Update()
        {
            Target = null;
            if (!GameManager.IsPlaying || motor == null) { Prompt(""); return; }
            if (motor.IsPulling || (exec != null && exec.IsExecuting)) { Prompt(""); return; }

            Target = FindTarget();

            bool execHas = exec != null && exec.Target != null;
            if (execHadTarget && !execHas) lastPrompt = "";
            execHadTarget = execHas;
            Prompt(Target != null && !execHas ? "GRAPPLE  <alpha=#99>[DASH]" : "");

            if (Target != null && InputReader.I != null && InputReader.I.DashPressed)
            {
                if (GrappleNow(Target)) Prompt("");
            }
        }

        /// <summary>Nearest-to-the-crosshair glowing flare with a clear world line, or null.</summary>
        public SentryFlare FindTarget()
        {
            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position + Vector3.up * 1.6f;
            Vector3 fwd = look != null ? look.AimForward : transform.forward;
            int mask = motor != null ? motor.WorldMask : ~((1 << Layers.Player) | (1 << Layers.Enemy) | (1 << Layers.Interactable));
            SentryFlare best = null;
            float bestAngle = coneDeg;
            for (int i = 0; i < SentryFlare.Live.Count; i++)
            {
                var f = SentryFlare.Live[i];
                if (f == null || !f.Grappleable) continue;
                Vector3 to = f.transform.position - eye;
                float dist = to.magnitude;
                if (dist > range || dist < 0.5f) continue;
                float angle = Vector3.Angle(fwd, to);
                if (angle > bestAngle) continue;
                if (Physics.Raycast(eye, to / dist, dist, mask, QueryTriggerInteraction.Ignore)) continue;
                bestAngle = angle;
                best = f;
            }
            return best;
        }

        /// <summary>Pull to <paramref name="f"/> now. Public so tests can prove the path without an aim.</summary>
        public bool GrappleNow(SentryFlare f)
        {
            if (f == null || !f.Grappleable || motor == null || motor.IsPulling) return false;
            if (exec != null && exec.IsExecuting) return false;
            inFlight = f;
            Vector3 from = offhand != null ? offhand.TipWorldPosition : transform.position;
            ItemVfx.GrappleLine(from, f.transform.position, hue);
            if (offhand != null) offhand.PlayUse();
            if (CameraFX.I != null) CameraFX.I.FovKick(6f);
            AudioManager.Play(Sfx.Dash, 0.8f, 1.15f);
            // The pull is aimed a hair under the flare so the body arrives BELOW it and the toss carries
            // through the light, which is the picture the mechanic is selling.
            motor.BeginPull(f.transform.position + Vector3.down * 0.6f, pullSeconds);
            return true;
        }

        void HandlePullEnded(bool arrived)
        {
            if (inFlight == null) return;
            var f = inFlight;
            inFlight = null;
            // Arrived OR cut short by a lintel: the toss still happens. A pull that ends in nothing is a
            // dropped input; a pull that ends in a lift is always a move.
            motor.Launch(tossUpSpeed);
            Tosses++;
            if (CameraFX.I != null)
            {
                CameraFX.I.FovKick(tossFovKick);
                // A g-force read distinct from an ordinary FovKick: the toss is the ONE moment the motor
                // hands the player vertical speed they did not ask for with WASD, and the kick alone reads
                // the same as a dash. This is the "you left the ground hard" cue.
                CameraFX.I.ChromaticPulse(tossChroma, tossChromaSeconds);
            }
            if (CameraShake.I != null) CameraShake.I.Small();
            AudioManager.Play(Sfx.Teleport, 0.9f, 1.05f, 0.03f);
            // A ring under the feet, expanding as the body leaves it: sells "thrown FROM here" the way the
            // grapple line sells "pulled TO there" -- the toss otherwise has no visual anchored to the
            // player at all, only sparks left behind at the (already-consumed) flare.
            SlashFx.Ring(transform.position, Vector3.up, hue, tossRingRadius, tossChromaSeconds);
            if (f != null) f.Consume();
        }

        void Prompt(string p)
        {
            if (p == lastPrompt) return;
            lastPrompt = p;
            GameEvents.RaisePromptChanged(p);
        }
    }
}
