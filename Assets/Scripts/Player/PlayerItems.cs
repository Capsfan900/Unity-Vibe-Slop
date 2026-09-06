using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Carries single-use pickups and spends them. FIFO: the leftmost HUD slot is the one that fires.
    ///
    /// <para>Two items exist and both are TRAVERSAL: the Grapple hooks an enemy, pulls you to it and
    /// deathblows it on arrival (Sekiro's grapple-kill, Neon White's kill-to-move), and the Wall Surge
    /// makes every wall run free, faster and attachable from any speed for a few seconds. The level is
    /// built around those two moves; nothing here heals or protects.</para>
    /// </summary>
    public class PlayerItems : MonoBehaviour
    {
        public int capacity = 3;

        readonly List<ItemData> held = new List<ItemData>();
        public IReadOnlyList<ItemData> Held => held;
        public bool IsFull => held.Count >= capacity;
        public ItemData Current => held.Count > 0 ? held[0] : null;

        /// <summary>The enemy a Grapple pull is flying toward, or null. Read by tests and the HUD.</summary>
        public EnemyController GrappleTarget { get; private set; }

        PlayerCombat combat;
        FirstPersonMotor motor;
        PlayerLook look;
        LockOnController lockOn;
        ExecuteInteractor exec;
        OffhandViewmodel offhand;
        CharacterController cc;
        readonly Collider[] buf = new Collider[48];
        Coroutine surgePrompt;

        /// <summary>Hook cyan, for the line and the commit flare when the item's own colour is unset.</summary>
        static readonly Color HookFallback = new Color(0.35f, 0.95f, 1f);

        void Awake()
        {
            combat = GetComponent<PlayerCombat>();
            motor = GetComponent<FirstPersonMotor>();
            look = GetComponent<PlayerLook>();
            lockOn = GetComponent<LockOnController>();
            exec = GetComponent<ExecuteInteractor>();
            offhand = GetComponentInChildren<OffhandViewmodel>(true);
            cc = GetComponent<CharacterController>();
        }

        void OnEnable()
        {
            GameEvents.PlayerRespawned += ClearAll;
        }

        void OnDisable()
        {
            GameEvents.PlayerRespawned -= ClearAll;
        }

        void Start() { Broadcast(); }

        void Update()
        {
            // Items are INDEPENDENT of wands: E always spends the first carried item, with no swapping
            // and no interaction with whatever wand is equipped.
            if (!GameManager.IsPlaying || InputReader.I == null) return;
            if (InputReader.I.UseItemPressed) UseCurrent();
        }

        public bool TryPickup(ItemData item)
        {
            if (item == null || IsFull) return false;
            held.Add(item);
            GameEvents.RaiseItemPickedUp(item);
            Broadcast();
            AudioManager.Play(Sfx.ItemPickup, 0.9f);
            // Deliberately faint: ItemVfx.Collect draws the eye, and a strong tint here would wash
            // out the very streaks that communicate what was picked up.
            if (ScreenFlash.I != null) ScreenFlash.I.Flash(item.color, 0.06f, 0.18f);
            return true;
        }

        /// <summary>
        /// Spend a specific carried item (the offhand slot picks which one). Returns false — and keeps
        /// the item — when the effect refuses to fire: a Grapple with nothing to hook is not spent.
        /// </summary>
        public bool Use(ItemData item)
        {
            if (item == null || !held.Contains(item)) return false;
            if (combat != null && combat.IsStaggered) return false;
            if (!Apply(item)) return false;
            held.Remove(item);
            Spent(item);
            return true;
        }

        public void UseCurrent()
        {
            if (held.Count == 0) { AudioManager.Play(Sfx.Click, 0.4f, 0.6f); return; }
            if (combat != null && combat.IsStaggered) return;

            var item = held[0];
            if (!Apply(item)) return;   // refused: still carried, nothing announced
            held.RemoveAt(0);
            Spent(item);
        }

        void Spent(ItemData item)
        {
            Broadcast();
            GameEvents.RaiseItemUsed(item);
            AudioManager.Play(Sfx.ItemUse, 0.9f);
        }

        void ClearAll()
        {
            held.Clear();
            GrappleTarget = null;
            Broadcast();
        }

        void Broadcast() => GameEvents.RaiseItemsChanged(held.ToArray());

        // ---- effects ---------------------------------------------------------------------------

        /// <summary>Fire the effect. False means "refused, do not consume".</summary>
        bool Apply(ItemData item)
        {
            switch (item.effect)
            {
                case ItemEffect.Grapple: return TryGrapple(item);
                case ItemEffect.WallSurge: DoWallSurge(item); return true;
            }
            return false;
        }

        // ---- Grapple ---------------------------------------------------------------------------

        /// <summary>
        /// A Legendary_* mini-boss or the boss. These are not killed by a hook: a grapple onto one
        /// that is not already staggered lands you at stand-off and takes a chunk of its posture, so
        /// the item stays a traversal tool against big enemies without trivialising them.
        /// </summary>
        public static bool IsBig(EnemyController e)
        {
            if (e == null) return false;
            if (e is BossController) return true;
            if (e.name.StartsWith("Legendary")) return true;
            return e.data != null && e.data.name.StartsWith("Legendary");
        }

        /// <summary>
        /// The enemy a Grapple press would hook right now, or null. The lock-on target wins when it is
        /// in range with line of sight; otherwise the live enemy nearest the crosshair inside
        /// <see cref="ItemData.grappleConeDeg"/> and <see cref="ItemData.grappleRange"/> that a world
        /// ray can reach. Public so the HUD and tests can ask without spending anything.
        /// </summary>
        public EnemyController FindGrappleTarget(ItemData item)
        {
            if (item == null) return null;
            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position + Vector3.up * 1.6f;
            Vector3 fwd = look != null ? look.AimForward : transform.forward;
            float range = Mathf.Max(1f, item.grappleRange);

            if (lockOn != null && lockOn.HasTarget && lockOn.Target != null && lockOn.Target.IsAlive)
            {
                var t = lockOn.Target;
                if (Reachable(eye, HookPoint(t), range)) return t;
            }

            int n = Physics.OverlapSphereNonAlloc(transform.position, range, buf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            EnemyController best = null;
            float bestAngle = item.grappleConeDeg;
            for (int i = 0; i < n; i++)
            {
                var e = buf[i].GetComponentInParent<EnemyController>();
                if (e == null || !e.IsAlive || e == best) continue;
                Vector3 p = HookPoint(e);
                float angle = Vector3.Angle(fwd, p - eye);
                if (angle > bestAngle) continue;
                if (!Reachable(eye, p, range)) continue;
                bestAngle = angle;
                best = e;
            }
            return best;
        }

        static Vector3 HookPoint(EnemyController e)
        {
            return e.transform.position + Vector3.up * (0.9f * Mathf.Max(0.3f, e.transform.localScale.y));
        }

        /// <summary>Inside range, and no WORLD geometry between the eye and the point. Enemies do not
        /// block a hook (the mask is the motor's, which excludes Enemy and Player), only the level.</summary>
        bool Reachable(Vector3 eye, Vector3 point, float range)
        {
            Vector3 to = point - eye;
            float dist = to.magnitude;
            if (dist > range || dist < 0.01f) return false;
            int mask = motor != null ? motor.WorldMask : ~((1 << Layers.Player) | (1 << Layers.Enemy) | (1 << Layers.Interactable));
            return !Physics.Raycast(eye, to / dist, dist, mask, QueryTriggerInteraction.Ignore);
        }

        bool TryGrapple(ItemData item)
        {
            if (motor == null || motor.IsPulling) return false;
            if (exec != null && exec.IsExecuting) return false;
            var target = FindGrappleTarget(item);
            if (target == null)
            {
                // Refused, kept. Says so: a press that does nothing with no explanation reads as broken.
                GameEvents.RaisePromptChanged("NO TARGET");
                AudioManager.Play(Sfx.Click, 0.5f, 0.7f);
                StartCoroutine(ClearPromptCo(0.6f));
                return false;
            }
            StartCoroutine(PullCo(target, item.grappleSeconds, item.color, Sfx.Dash, 0.75f, item.grappleBigPostureFraction));
            return true;
        }

        /// <summary>The transform position that puts the player at deathblow stand-off from
        /// <paramref name="e"/>, on the player's side, at the enemy's floor.</summary>
        Vector3 StandoffPoint(EnemyController e)
        {
            float standoff = (exec != null ? exec.stabStandoff : 2.2f)
                           * (e.data != null ? Mathf.Max(0.6f, e.data.scale) : 1f);
            Vector3 flat = transform.position - e.transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f)
            {
                flat = look != null ? -look.AimForward : -transform.forward;
                flat.y = 0f;
            }
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.back;
            flat.Normalize();
            // Both roots sit at the feet (player CC centre is +0.9 on a 1.8 capsule; the enemy's
            // collider is centred +1.0), so the enemy's y IS the floor the player should land on.
            return e.transform.position + flat * standoff;
        }

        IEnumerator PullCo(EnemyController e, float seconds, Color colour, Sfx sound, float pitch, float bigPostureFraction)
        {
            GrappleTarget = e;
            Color hue = colour.maxColorComponent > 0.01f ? colour : HookFallback;
            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position;

            // Commit cue: the line goes out from the hand, the glyph point on the victim flares. The
            // player has to SEE the hook connect before the pull moves them, or the pull reads as a
            // teleport with a coloured flash.
            Vector3 from = offhand != null ? offhand.TipWorldPosition : transform.position;
            ItemVfx.GrappleLine(from, e.DeathblowPoint(eye), hue);
            if (offhand != null) offhand.PlayUse();
            if (CameraFX.I != null) CameraFX.I.FovKick(10f);
            AudioManager.Play(sound, 0.8f, pitch);

            motor.BeginPull(StandoffPoint(e), seconds);

            // Ride the pull. If the victim dies or vanishes under us (a friend's super, a pit) the arc
            // still finishes: the motor owns it and the player still arrives somewhere sensible.
            while (motor.IsPulling) yield return null;
            GrappleTarget = null;

            if (e == null || !e.IsAlive) yield break;

            if (CameraShake.I != null) CameraShake.I.Small();

            bool big = IsBig(e);
            if (big && !e.IsStaggered)
            {
                // A big enemy that is not open: the hook is a posture blow and a repositioning, not a
                // kill. Enough that two hooks (or one hook and a deflect) open the deathblow.
                float chunk = e.Posture.Max * Mathf.Clamp01(bigPostureFraction);
                e.Posture.Add(chunk);
                SlashFx.Flare(e.DeathblowPoint(eye), hue, 0.6f, 0.16f);
                SlashFx.Sparks(e.DeathblowPoint(eye), Vector3.up, hue, 10, 6f, 90f);
                AudioManager.Play(Sfx.Hit, 0.8f, 0.8f);
                yield break;
            }

            // Open the victim, then run the ONE execute path. Posture.Break -> OnBroken ->
            // EnemyController.HandleBroken -> State.Staggered, synchronously, so ExecuteNow's gate
            // sees it on the same frame. ExecuteCo raises RiposteLanded (melee) or lets the wand do
            // it, applies the isExecute damage, hitstop, shake and the rest - nothing is duplicated here.
            if (!e.IsStaggered) e.Posture.Break();
            if (exec == null || !exec.ExecuteNow(e))
            {
                // No interactor, or the enemy could not be opened (already mid-execute, dead): the
                // hook still did its job as a move. Nothing else to do.
                yield break;
            }
        }

        // ---- Wall surge ------------------------------------------------------------------------

        void DoWallSurge(ItemData item)
        {
            float seconds = Mathf.Max(0.1f, item.surgeSeconds);
            if (motor != null) motor.StartWallSurge(seconds);
            Color hue = item.color.maxColorComponent > 0.01f ? item.color : Color.yellow;
            ItemVfx.Surge(transform, motor, hue, seconds);
            if (CameraFX.I != null) CameraFX.I.FovKick(6f);
            if (ScreenFlash.I != null) ScreenFlash.I.Flash(hue, 0.08f, 0.25f);
            if (surgePrompt != null) StopCoroutine(surgePrompt);
            surgePrompt = StartCoroutine(SurgePromptCo());
        }

        /// <summary>"SURGE 8s" counting down on the HUD prompt, once a second. Shares the prompt line
        /// with the deathblow prompt; a deathblow overwrite is re-asserted on the next tick.</summary>
        IEnumerator SurgePromptCo()
        {
            int last = -1;
            while (motor != null && motor.IsWallSurging)
            {
                int s = Mathf.CeilToInt(motor.WallSurgeRemaining);
                if (s != last) { last = s; GameEvents.RaisePromptChanged("SURGE " + s + "s"); }
                yield return null;
            }
            GameEvents.RaisePromptChanged("");
            surgePrompt = null;
        }

        IEnumerator ClearPromptCo(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
            // The interactor only re-raises its own prompt on change, so clear ours explicitly.
            GameEvents.RaisePromptChanged("");
        }
    }
}
