using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The wand altar at a level's spawn point. Aim at it and press F to open
    /// <see cref="WandSelectMenu"/>, so the loadout is a deliberate pre-run commitment (the Bloodborne
    /// firearm choice) rather than something cycled mid-fight.
    ///
    /// Trigger idiom is <see cref="ItemPickup"/>'s — layer Interactable, trigger collider, player
    /// detected via <c>GetComponentInParent</c> — but the trigger is a RANGE CHECK ONLY. Walking past
    /// must never open a menu; the player has to look at the altar and press the key.
    ///
    /// <para><b>It is a dev fixture unless <see cref="DevMenuEnabled"/> is on.</b> The altar is built
    /// into every level and sandbox spawn, but by default it does not draw, does not prompt and
    /// refuses <see cref="TryInteract"/>; the F1 test menu's "WAND PEDESTAL" button turns it on. The
    /// player keeps whatever the Player prefab's <see cref="WandController.loadout"/> gives them.</para>
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WandPedestal : MonoBehaviour
    {
        /// <summary>
        /// Master switch for every pedestal in the scene. Off: hidden (crystal, glow, plinth, trigger),
        /// no prompt, <see cref="TryInteract"/> and <see cref="Open"/> refuse. On: the full altar.
        /// Static and session-wide, like god mode — toggled from <c>TestMenu</c>, never by gameplay.
        /// </summary>
        public static bool DevMenuEnabled;

        public Transform visual;
        public float spinDegreesPerSecond = 40f;
        public float bobHeight = 0.12f;
        public float bobSpeed = 1.2f;

        [Tooltip("How centred the altar must be in view: cosine of the half-angle. 0.8 ≈ a 37° cone.")]
        [Range(0f, 1f)] public float lookDot = 0.8f;

        public string promptText = "[F]  CHOOSE WAND";

        /// <summary>
        /// True while ANY pedestal is offering its prompt. `F` is also the flask key, so the two would
        /// both fire on the same press and script execution order decides who wins. FlaskAbility reads
        /// this and stands down: while the altar is offering itself, F means "choose a wand".
        /// </summary>
        public static bool PromptActive { get; private set; }

        Collider col;
        Vector3 visualBase;
        WandController inRange;
        bool promptShown;
        bool appliedEnabled;
        bool applied;
        GameObject plinth;

        /// <summary>True while the player is inside the trigger — range only, never a reason to open.</summary>
        public bool PlayerInRange => inRange != null;

        /// <summary>True while the altar is switched off: nothing drawn, trigger off, prompt suppressed.</summary>
        public bool IsHidden => applied && !appliedEnabled;

        void Awake()
        {
            col = GetComponent<Collider>();
            col.isTrigger = true;
            if (visual != null) visualBase = visual.localPosition;
            // The builders put the stone plinth beside the trigger root, not under it (it has to stay on
            // Default so it bakes into the NavMesh). Found by the builder's naming convention.
            if (transform.parent != null)
            {
                var t = transform.parent.Find(name + "_Plinth");
                if (t != null) plinth = t.gameObject;
            }
            ApplyEnabled(DevMenuEnabled);
        }

        void OnEnable() { GameEvents.PlayerRespawned += Clear; }

        void OnDisable()
        {
            GameEvents.PlayerRespawned -= Clear;
            ShowPrompt(false);
        }

        void Clear() { inRange = null; ShowPrompt(false); }

        void Update()
        {
            if (!applied || appliedEnabled != DevMenuEnabled) ApplyEnabled(DevMenuEnabled);
            if (!appliedEnabled) return;

            if (visual != null)
            {
                // Unscaled: the altar keeps turning while its own menu holds time at zero.
                visual.Rotate(0f, spinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
                visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.unscaledTime * bobSpeed) * bobHeight);
            }

            bool menuOpen = WandSelectMenu.I != null && WandSelectMenu.I.IsOpen;
            bool ready = !menuOpen && GameManager.IsPlaying && inRange != null && IsLookedAt();
            ShowPrompt(ready);

            if (InputReader.I == null || !InputReader.I.InteractPressed) return;
            TryInteract();
        }

        /// <summary>
        /// The body of the interact press, with the input read left in <see cref="Update"/> (rule 2:
        /// InputReader is the only script that touches the Input System). Public so the feature suite
        /// can exercise the real gating — readiness, range, look direction — rather than skipping it,
        /// which is what every input-polled behaviour in this project used to do.
        /// </summary>
        public bool TryInteract()
        {
            if (!DevMenuEnabled) return false;
            bool menuOpen = WandSelectMenu.I != null && WandSelectMenu.I.IsOpen;
            if (menuOpen || !GameManager.IsPlaying || inRange == null || !IsLookedAt()) return false;
            Open(inRange);
            return true;
        }

        /// <summary>
        /// Facing test rather than a raycast: the trigger sphere is large enough that the camera stands
        /// INSIDE it, and a ray started inside a collider reports no hit — a raycast here would silently
        /// never fire. The dot product against the floating crystal is what the player actually aims at.
        /// </summary>
        public bool IsLookedAt()
        {
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 focus = visual != null ? visual.position : transform.position + Vector3.up * 1.5f;
            Vector3 to = focus - cam.transform.position;
            if (to.sqrMagnitude < 0.0001f) return true;
            return Vector3.Dot(cam.transform.forward, to.normalized) >= lookDot;
        }

        void ShowPrompt(bool show)
        {
            if (show == promptShown) return;   // edge-triggered: the prompt bus is shared with ExecuteInteractor
            promptShown = show;
            PromptActive = show;
            GameEvents.RaisePromptChanged(show ? promptText : "");
        }

        void OnTriggerEnter(Collider other)
        {
            var wands = other.GetComponentInParent<WandController>();
            if (wands != null) inRange = wands;
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<WandController>() != null) Clear();
        }

        /// <summary>
        /// Open the menu. Public so the feature suite can drive the pedestal without synthesising a
        /// key press, and so a future "use" verb can route through one place.
        /// </summary>
        public void Open(WandController wands)
        {
            if (!DevMenuEnabled || wands == null || !GameManager.IsPlaying) return;
            var menu = WandSelectMenu.I;
            if (menu == null) menu = FindAnyObjectByType<WandSelectMenu>();
            if (menu == null) return;
            ShowPrompt(false);
            menu.Open(wands);
        }

        /// <summary>
        /// Show or hide the whole altar. Renderers and lights are toggled rather than the objects
        /// deactivated so <c>visual</c>'s bob/spin state survives a round trip; the trigger collider is
        /// switched off so a hidden altar cannot even register range. The plinth IS deactivated: a
        /// visible-but-untouchable altar would be odd, an invisible wall at spawn would be worse.
        /// </summary>
        void ApplyEnabled(bool on)
        {
            applied = true;
            appliedEnabled = on;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            foreach (var l in GetComponentsInChildren<Light>(true)) l.enabled = on;
            if (col != null) col.enabled = on;
            if (plinth != null && plinth.activeSelf != on) plinth.SetActive(on);
            if (!on) Clear();   // a collider switched off fires no OnTriggerExit
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.5f, 0.75f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, 1.6f);
        }
    }
}
