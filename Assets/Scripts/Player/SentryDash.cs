using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// THE SENTRY DASH (2026-09-06, from play: "when you stagger a parkour enemy you can dash to them
    /// like with the hook, but it makes a teleport sound"). A staggered sentry (EnemyData.rangedOnly --
    /// the shooters on the spans) inside <see cref="range"/> and <see cref="coneDeg"/> of the aim,
    /// with a clear world line, turns the DASH press into a pull: the player is flown to deathblow
    /// stand-off and the execute runs, exactly the Grapple item's arrival, spent on nothing. So the
    /// loop on a span is deflect - deflect - dash - kill, and the kill lands you at the next perch.
    ///
    /// <para>Runs before the motor (execution order) so the press that starts a pull is never also
    /// an ordinary dash: the motor's Update returns early while <c>IsPulling</c>. Input is still read
    /// only through <see cref="InputReader"/> (hard rule 2). All numbers are written by PrefabFactory
    /// (rule 9). The pull itself is <see cref="PlayerItems.DashTo"/>: one pull-and-execute path.</para>
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SentryDash : MonoBehaviour
    {
        [Tooltip("Metres. A staggered sentry further than this is not a dash target.")]
        public float range = 30f;
        [Tooltip("Degrees off the aim inside which a staggered sentry is the target. Wider than the hook's " +
                 "12: the target is glowing and open, the press is a reaction, not an aim.")]
        public float coneDeg = 18f;
        [Tooltip("Seconds of pull. The hook's 0.35.")]
        public float pullSeconds = 0.35f;
        [Tooltip("Colour of the line and the flare. Bone-white violet: a blink, not the hook's cyan.")]
        public Color hue = new Color(0.85f, 0.75f, 1f);

        /// <summary>The staggered sentry a dash press would fly to right now, or null. HUD and tests.</summary>
        public EnemyController Target { get; private set; }

        PlayerItems items;
        PlayerLook look;
        FirstPersonMotor motor;
        ExecuteInteractor exec;
        readonly Collider[] buf = new Collider[48];
        string lastPrompt = "";
        bool execHadTarget;

        void Awake()
        {
            items = GetComponent<PlayerItems>();
            look = GetComponent<PlayerLook>();
            motor = GetComponent<FirstPersonMotor>();
            exec = GetComponent<ExecuteInteractor>();
        }

        void Update()
        {
            Target = null;
            if (!GameManager.IsPlaying || items == null || motor == null) { Prompt(""); return; }
            if (motor.IsPulling || (exec != null && exec.IsExecuting)) { Prompt(""); return; }

            Target = FindTarget();

            // The deathblow prompt (ExecuteInteractor) wins when it has a target: at stab range the
            // ATTACK press is the answer, not a pull. When it drops its target it writes "" over ours,
            // so re-raise on that edge.
            bool execHas = exec != null && exec.Target != null;
            if (execHadTarget && !execHas) lastPrompt = "";
            execHadTarget = execHas;
            Prompt(Target != null && !execHas ? "DASH  <alpha=#99>[DASH]" : "");

            if (Target != null && InputReader.I != null && InputReader.I.DashPressed)
            {
                if (items.DashTo(Target, pullSeconds, hue)) Prompt("");
            }
        }

        /// <summary>Nearest-to-the-crosshair staggered sentry with a clear world line, or null.</summary>
        public EnemyController FindTarget()
        {
            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position + Vector3.up * 1.6f;
            Vector3 fwd = look != null ? look.AimForward : transform.forward;
            int n = Physics.OverlapSphereNonAlloc(transform.position, range, buf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            EnemyController best = null;
            float bestAngle = coneDeg;
            int mask = motor != null ? motor.WorldMask : ~((1 << Layers.Player) | (1 << Layers.Enemy) | (1 << Layers.Interactable));
            for (int i = 0; i < n; i++)
            {
                var e = buf[i].GetComponentInParent<EnemyController>();
                if (e == null || e == best || !IsSentry(e) || !e.IsAlive || !e.IsStaggered) continue;
                Vector3 p = e.transform.position + Vector3.up * (0.9f * Mathf.Max(0.3f, e.transform.localScale.y));
                Vector3 to = p - eye;
                float dist = to.magnitude;
                if (dist > range || dist < 0.01f) continue;
                float angle = Vector3.Angle(fwd, to);
                if (angle > bestAngle) continue;
                if (Physics.Raycast(eye, to / dist, dist, mask, QueryTriggerInteraction.Ignore)) continue;
                bestAngle = angle;
                best = e;
            }
            return best;
        }

        /// <summary>A span shooter: rangedOnly data. The Warden and the legendaries are duels, never a blink.</summary>
        public static bool IsSentry(EnemyController e)
        {
            return e != null && e.data != null && e.data.rangedOnly && !(e is BossController);
        }

        void Prompt(string p)
        {
            if (p == lastPrompt) return;
            lastPrompt = p;
            GameEvents.RaisePromptChanged(p);
        }
    }
}
