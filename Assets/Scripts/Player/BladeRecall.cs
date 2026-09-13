using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// RECALL to a thrown sword (2026-09-13). The <see cref="FlareGrapple"/> contract with the player's own
    /// blade as the point: while a <see cref="ThrownBlade"/> is recallable, aiming near it and pressing DASH
    /// pulls the player to it along the hook's arc - at any point in its flight, or wherever it lodged.
    /// The blade is spent at the press and the sword returns to the hand.
    ///
    /// <para>Runs one slot before FlareGrapple (-51 vs -50) so, with both a flare and the blade in view,
    /// the press goes to the blade and the flare sees <see cref="FirstPersonMotor.IsPulling"/> and bails.
    /// Movement only through <see cref="FirstPersonMotor.BeginPull(Vector3, float, bool)"/> (rule 10);
    /// input only through <see cref="InputReader"/> (rule 2). Numbers live on the BladeThrow item asset.</para>
    /// </summary>
    [DefaultExecutionOrder(-51)]
    public class BladeRecall : MonoBehaviour
    {
        /// <summary>Times the player has recalled to a blade. Tests.</summary>
        public int Recalls { get; private set; }
        /// <summary>The blade a DASH press would recall to right now, or null.</summary>
        public ThrownBlade Target { get; private set; }

        PlayerLook look;
        FirstPersonMotor motor;
        ExecuteInteractor exec;
        string lastPrompt = "";
        bool pulling;
        EnemyController pullEnemy;

        void Awake()
        {
            look = GetComponent<PlayerLook>();
            motor = GetComponent<FirstPersonMotor>();
            exec = GetComponent<ExecuteInteractor>();
        }

        void OnEnable() { if (motor != null) motor.OnPullEnded += HandlePullEnded; }
        void OnDisable() { if (motor != null) motor.OnPullEnded -= HandlePullEnded; Prompt(""); }

        void Update()
        {
            Target = null;
            if (!GameManager.IsPlaying || motor == null) { Prompt(""); return; }
            if (motor.IsPulling || (exec != null && exec.IsExecuting)) { Prompt(""); return; }

            Target = FindTarget();
            Prompt(Target != null ? "RECALL  <alpha=#99>[DASH]" : "");
            if (Target != null && InputReader.I != null && InputReader.I.DashPressed)
            {
                if (RecallNow(Target)) Prompt("");
            }
        }

        /// <summary>The active blade if it is recallable, in range, inside the cone and in world line of sight.</summary>
        public ThrownBlade FindTarget()
        {
            var b = ThrownBlade.Active;
            if (b == null || !b.Recallable || b.Item == null) return null;
            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position + Vector3.up * 1.6f;
            Vector3 fwd = look != null ? look.AimForward : transform.forward;
            Vector3 to = b.transform.position - eye;
            float dist = to.magnitude;
            if (dist < 0.5f || dist > Mathf.Max(1f, b.Item.bladeRecallRange)) return null;
            if (Vector3.Angle(fwd, to) > b.Item.bladeRecallConeDeg) return null;
            int mask = motor != null ? motor.WorldMask : ~((1 << Layers.Player) | (1 << Layers.Enemy) | (1 << Layers.Interactable));
            // Stop short of the blade itself: a lodged blade sits a hair off the surface it bit.
            if (Physics.Raycast(eye, to / dist, Mathf.Max(0f, dist - 0.4f), mask, QueryTriggerInteraction.Ignore)) return null;
            return b;
        }

        /// <summary>Pull to <paramref name="b"/> now. Public so tests can prove the path without an aim.</summary>
        public bool RecallNow(ThrownBlade b)
        {
            if (b == null || !b.Recallable || motor == null || motor.IsPulling) return false;
            if (exec != null && exec.IsExecuting) return false;
            var item = b.Item;
            Color hue = item != null && item.color.maxColorComponent > 0.01f ? item.color : new Color(1f, 0.6f, 0.25f);
            Vector3 target = b.PullTarget;
            pullEnemy = b.LodgedEnemy;
            ItemVfx.GrappleLine(transform.position + Vector3.up * 1.2f, b.transform.position, hue);
            if (CameraFX.I != null) CameraFX.I.FovKick(8f);
            // The warp: the real dash whoosh pitched down for weight, under the rising teleport shimmer.
            AudioManager.Play(Sfx.Dash, 1f, 0.82f);
            AudioManager.Play(Sfx.Teleport, 1f, 1.15f, 0.03f);
            float seconds = item != null ? Mathf.Max(0.05f, item.bladePullSeconds) : 0.35f;
            b.Consume();   // the sword is back in the hand the moment you commit
            motor.BeginPull(target, seconds, true);
            pulling = true;
            Recalls++;
            return true;
        }

        void HandlePullEnded(bool arrived)
        {
            if (!pulling) return;
            pulling = false;
            var e = pullEnemy;
            pullEnemy = null;
            if (CameraShake.I != null) CameraShake.I.Small();
            AudioManager.Play(Sfx.Dash, 0.8f, 1.2f);   // arrival snap
            SlashFx.Ring(transform.position, Vector3.up, new Color(1f, 0.7f, 0.35f), 1.2f, 0.25f);
            // Recalled into a body that is already open: the one execute path, exactly as the Hook does.
            if (arrived && e != null && e.IsAlive && e.IsStaggered && exec != null) exec.ExecuteNow(e);
        }

        void Prompt(string p)
        {
            if (p == lastPrompt) return;
            lastPrompt = p;
            GameEvents.RaisePromptChanged(PromptOwner.Recall, p);
        }
    }
}
