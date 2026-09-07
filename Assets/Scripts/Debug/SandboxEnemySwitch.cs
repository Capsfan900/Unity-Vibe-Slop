using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The activation switch on a sandbox enemy pad. Aim at it and press <b>F</b> and the enemy on that
    /// pad wakes up; until then it stands there and ignores you.
    ///
    /// <para><b>Why the sandbox needs this and the campaign does not.</b> In the level, aggro on sight is
    /// the design — walking into a fight is the fight. The sandbox is a workshop: six enemies stand in a
    /// row nine metres apart, and with default aggro the moment you step off the spawn pad three of them
    /// are already walking at you. You cannot look at a wind-up, time a parry, or photograph a stagger
    /// pose with two other things swinging at your back. So every pad's enemy ships
    /// <see cref="EnemyController.aggroLocked"/> — the same flag the Warden uses to sleep in its arena
    /// until the trigger fires — and this switch is that trigger, one per pad.</para>
    ///
    /// <para>It re-arms itself. <see cref="EnemySpawner"/> re-instantiates its enemy on respawn, so the
    /// switch re-locks whatever is currently standing on the pad rather than caching one controller: in
    /// a room built for repeated attempts, a switch that only worked once would be worse than no switch.</para>
    ///
    /// <para>Idiom is <see cref="WandPedestal"/>'s exactly — layer Interactable, trigger collider as a
    /// RANGE check only, look-dot to aim, <c>InputReader.InteractPressed</c> to fire — because <c>F</c>
    /// already has two consumers (the flask and the altar) and adding a third with its own rules is how
    /// this project's most-repeated input bug happens. <see cref="WandPedestal.PromptActive"/> is
    /// honoured so a switch standing near the altar never steals its press.</para>
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SandboxEnemySwitch : MonoBehaviour
    {
        [Tooltip("The pad's spawner. The switch reads Instance every frame rather than caching a " +
                 "controller, because the spawner replaces its enemy on every respawn.")]
        public EnemySpawner spawner;

        [Tooltip("Lit while the enemy is asleep, dark once it has been woken — the switch is also the " +
                 "pad's status light, so a glance down the row says which fights are live.")]
        public Renderer lamp;

        [Tooltip("Label shown on the pad. Set by SandboxBuilder from the spawner name.")]
        public string enemyName = "ENEMY";

        [Tooltip("How centred the switch must be in view: cosine of the half-angle. 0.86 is about 30 " +
                 "degrees — tighter than the wand altar's, because six of these stand in a row and a " +
                 "wide cone would offer two at once.")]
        [Range(0f, 1f)] public float lookDot = 0.86f;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly Color Armed = new Color(0.10f, 0.72f, 0.84f) * 1.6f;   // ghost teal: ready
        static readonly Color Spent = new Color(0.05f, 0.06f, 0.08f);          // dark: this one is awake

        Collider col;
        bool playerInRange;
        bool promptShown;
        bool lampArmed = true;
        MaterialPropertyBlock mpb;

        /// <summary>The enemy currently standing on this pad, or null.</summary>
        public EnemyController Enemy
        {
            get
            {
                if (spawner == null || spawner.Instance == null) return null;
                return spawner.Instance.GetComponent<EnemyController>();
            }
        }

        /// <summary>True while that enemy is still asleep — i.e. the switch has work to do.</summary>
        public bool IsArmed
        {
            get { var e = Enemy; return e != null && e.IsAlive && e.aggroLocked; }
        }

        void Awake()
        {
            col = GetComponent<Collider>();
            col.isTrigger = true;
            mpb = new MaterialPropertyBlock();
        }

        void OnDisable() { ShowPrompt(false); }

        void Update()
        {
            // Re-lock whatever is on the pad now. A freshly spawned enemy arrives with the prefab's own
            // aggroLocked (false), so this is what makes the switch survive a respawn.
            var e = Enemy;
            if (e != null && e.IsAlive && !woken.Contains(e)) e.aggroLocked = true;

            SetLamp(IsArmed);

            bool ready = GameManager.IsPlaying && playerInRange && IsArmed
                         && !WandPedestal.PromptActive && IsLookedAt();
            ShowPrompt(ready);

            if (!ready || InputReader.I == null || !InputReader.I.InteractPressed) return;
            Activate();
        }

        // Instance identity, not a bool: the spawner hands out a NEW EnemyController on every respawn,
        // and a bool would leave the replacement permanently awake.
        readonly System.Collections.Generic.HashSet<EnemyController> woken =
            new System.Collections.Generic.HashSet<EnemyController>();

        /// <summary>
        /// Wake the enemy on this pad. Public so the feature suite can drive it without synthesising a
        /// key press — the same reason <see cref="WandPedestal.Open"/> is public.
        /// </summary>
        public bool Activate()
        {
            var e = Enemy;
            if (e == null || !e.IsAlive) return false;
            woken.Add(e);
            e.aggroLocked = false;
            // The Warden sleeps behind a BossController.Activate() that also raises its bar and roars;
            // route through it rather than poking the flag, or a sandbox boss would wake up silent and
            // with no HUD.
            var boss = e as BossController;
            if (boss != null) boss.Activate();
            ShowPrompt(false);
            SetLamp(false);
            AudioManager.Play(Sfx.Click, 0.7f, 0.8f);
            return true;
        }

        /// <summary>Put the pad's enemy back to sleep — the sandbox reset path.</summary>
        public void Rearm()
        {
            woken.Clear();
            var e = Enemy;
            if (e != null) e.aggroLocked = true;
            SetLamp(true);
        }

        /// <summary>
        /// Facing test rather than a raycast, for the same reason as the wand altar: the trigger is big
        /// enough that the camera stands inside it, and a ray started inside a collider reports no hit.
        /// </summary>
        public bool IsLookedAt()
        {
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 focus = lamp != null ? lamp.bounds.center : transform.position + Vector3.up * 0.9f;
            Vector3 to = focus - cam.transform.position;
            if (to.sqrMagnitude < 0.0001f) return true;
            return Vector3.Dot(cam.transform.forward, to.normalized) >= lookDot;
        }

        void SetLamp(bool armed)
        {
            if (lamp == null || armed == lampArmed) return;
            lampArmed = armed;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.SetColor(EmissionId, armed ? Armed : Spent);
            lamp.SetPropertyBlock(mpb);
        }

        void ShowPrompt(bool show)
        {
            if (show == promptShown) return;   // edge-triggered: the prompt bus is shared
            promptShown = show;
            GameEvents.RaisePromptChanged(PromptOwner.Sandbox, show ? "[F]  WAKE  " + enemyName : "");
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerCombat>() != null) playerInRange = true;
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerCombat>() == null) return;
            playerInRange = false;
            ShowPrompt(false);
        }
    }
}
