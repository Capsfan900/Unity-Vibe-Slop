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
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WandPedestal : MonoBehaviour
    {
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

        /// <summary>True while the player is inside the trigger — range only, never a reason to open.</summary>
        public bool PlayerInRange => inRange != null;

        void Awake()
        {
            col = GetComponent<Collider>();
            col.isTrigger = true;
            if (visual != null) visualBase = visual.localPosition;
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
            if (visual != null)
            {
                // Unscaled: the altar keeps turning while its own menu holds time at zero.
                visual.Rotate(0f, spinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
                visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.unscaledTime * bobSpeed) * bobHeight);
            }

            bool menuOpen = WandSelectMenu.I != null && WandSelectMenu.I.IsOpen;
            bool ready = !menuOpen && GameManager.IsPlaying && inRange != null && IsLookedAt();
            ShowPrompt(ready);

            if (!ready || InputReader.I == null || !InputReader.I.InteractPressed) return;
            Open(inRange);
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
            if (wands == null || !GameManager.IsPlaying) return;
            var menu = WandSelectMenu.I;
            if (menu == null) menu = FindAnyObjectByType<WandSelectMenu>();
            if (menu == null) return;
            ShowPrompt(false);
            menu.Open(wands);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.5f, 0.75f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, 1.6f);
        }
    }
}
