using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Carries single-use book spells. The wheel rotates the selected spell to the front; E casts it.
    ///
    /// <para>HOOK converts a matching turret bolt into a movement kill, REBOUND strengthens the next
    /// legal airborne exit, and DEFLECT SIGIL waits for a real Perfect before paying out. Nothing here
    /// heals or protects, and no item synthesises a combat contact.</para>
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
        public bool DeflectSigilArmed { get; private set; }
        public bool ReboundArmed { get { return motor != null && motor.IsReboundArmed; } }

        PlayerCombat combat;
        FirstPersonMotor motor;
        PlayerLook look;
        LockOnController lockOn;
        ExecuteInteractor exec;
        OffhandViewmodel offhand;
        CharacterController cc;
        readonly Collider[] buf = new Collider[48];
        ItemData activeHookItem;
        Projectile qualifiedHookProjectile;
        float hookPressedAt = -99f;
        ItemData armedSigil;

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
            GameEvents.PlayerDied += ClearArmedEffects;
            GameEvents.ParryResolved += OnParryResolved;
            ThrownBlade.Ended += OnBladeEnded;
        }

        void OnDisable()
        {
            GameEvents.PlayerRespawned -= ClearAll;
            GameEvents.PlayerDied -= ClearArmedEffects;
            GameEvents.ParryResolved -= OnParryResolved;
            ClearArmedEffects();
            ThrownBlade.Ended -= OnBladeEnded;
        }

        void Start() { Broadcast(); }

        void Update()
        {
            // The wheel rotates the carried spellbook pages; E casts the selected front page.
            if (!GameManager.IsPlaying || InputReader.I == null) return;
            if (InputReader.I.NextPressed) CycleSelection(1);
            else if (InputReader.I.PrevPressed) CycleSelection(-1);
            if (InputReader.I.UseItemPressed) UseCurrent();
        }

        /// <summary>Rotate the carried spell list so the selected spell remains the existing front-item
        /// contract consumed by E, the book, and the status strip.</summary>
        public bool CycleSelection(int direction)
        {
            if (held.Count < 2 || direction == 0) return false;
            if (direction > 0)
            {
                ItemData first = held[0];
                held.RemoveAt(0);
                held.Add(first);
            }
            else
            {
                int lastIndex = held.Count - 1;
                ItemData last = held[lastIndex];
                held.RemoveAt(lastIndex);
                held.Insert(0, last);
            }
            Broadcast();
            AudioManager.Play(Sfx.Click, 0.45f, direction > 0 ? 1.08f : 0.92f);
            return true;
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
        /// Spend a specific carried item. Returns false — and keeps the item — when the effect refuses
        /// to fire: a Grapple with nothing to hook is not spent.
        /// </summary>
        public bool Use(ItemData item)
        {
            if (item == null || !held.Contains(item)) return false;
            if (combat != null && combat.IsStaggered) return false;
            if (!Apply(item)) return false;
            if (offhand != null) offhand.PlayUse(item);
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
            if (offhand != null) offhand.PlayUse(item);
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
            activeHookItem = null;
            qualifiedHookProjectile = null;
            ClearArmedEffects();
            Broadcast();
        }

        void Broadcast()
        {
            // Inventory updates only the orb above the persistent spellbook. It never swaps a pickup
            // prefab into the hand, which is the old path that collapsed the prop into a toothpick.
            if (offhand != null) offhand.SetFrontItem(Current);
            GameEvents.RaiseItemsChanged(held.ToArray());
        }

        // ---- effects ---------------------------------------------------------------------------

        /// <summary>Fire the effect. False means "refused, do not consume".</summary>
        bool Apply(ItemData item)
        {
            switch (item.effect)
            {
                case ItemEffect.Grapple: return TryGrapple(item);
                case ItemEffect.Rebound: return TryArmRebound(item);
                case ItemEffect.DeflectSigil: return TryArmSigil(item);
                case ItemEffect.BladeThrow: return TryThrowBlade(item);
                case ItemEffect.WallSurge: return false; // retired serialized value; never reinterpret it
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
                GameEvents.RaisePromptFlash("NO TARGET", 0.6f);   // a flash: it must not erase a live GRAPPLE cue
                AudioManager.Play(Sfx.Click, 0.5f, 0.7f);
                return false;
            }
            hookPressedAt = Time.time;
            activeHookItem = item;
            qualifiedHookProjectile = null;
            StartCoroutine(PullCo(target, item.grappleSeconds, item.color, Sfx.Dash, 0.75f, item.grappleBigPostureFraction));
            return true;
        }

        /// <summary>
        /// Called only from ParryController while resolving a real projectile contact. A Hook timing
        /// source is valid for the matching turret during the active pull and only for its authored
        /// E-to-contact window. It cannot parry melee or another shooter's bolt.
        /// </summary>
        public bool TryResolveHookParry(Projectile projectile, EnemyController shooter)
        {
            if (projectile == null || !projectile.IsIncoming || shooter == null ||
                shooter != GrappleTarget || activeHookItem == null)
                return false;
            if (motor == null || !motor.IsPulling || shooter.data == null || !shooter.data.isTurret)
                return false;
            float elapsed = Time.time - hookPressedAt;
            if (elapsed < 0f || elapsed > Mathf.Max(0.01f, activeHookItem.grapplePerfectWindow)) return false;
            qualifiedHookProjectile = projectile;
            return true;
        }

        /// <summary>Completes the already-judged Hook Perfect after Projectile has paid its movement
        /// impulse. The ordinary Health path owns death, souls, respawn and all listeners.</summary>
        public void CompleteHookPerfect(Projectile projectile, EnemyController shooter)
        {
            if (projectile == null || projectile != qualifiedHookProjectile || shooter == null ||
                shooter != GrappleTarget || !shooter.IsAlive) return;
            qualifiedHookProjectile = null;
            if (motor != null) motor.PrimeHookDashJump();
            if (shooter.Health != null)
            {
                shooter.Health.TakeDamage(new DamageInfo
                {
                    damage = shooter.Health.Current + 1f,
                    point = projectile.transform.position,
                    direction = shooter.transform.position - transform.position,
                    source = gameObject,
                    isExecute = false
                });
            }
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
            bool turretHook = e != null && e.data != null && e.data.isTurret;
            Color hue = colour.maxColorComponent > 0.01f ? colour : HookFallback;
            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position;

            // Commit cue: the line goes out from the hand, the glyph point on the victim flares. The
            // player has to SEE the hook connect before the pull moves them, or the pull reads as a
            // teleport with a coloured flash.
            Vector3 from = offhand != null ? offhand.TipWorldPosition : transform.position;
            ItemVfx.GrappleLine(from, e.DeathblowPoint(eye), hue);
            if (CameraFX.I != null) CameraFX.I.FovKick(10f);
            AudioManager.Play(sound, 0.8f, pitch);

            motor.BeginPull(StandoffPoint(e), seconds, !turretHook);

            // Ride the pull. If the victim dies or vanishes under us (a friend's super, a pit) the arc
            // still finishes: the motor owns it and the player still arrives somewhere sensible.
            while (motor.IsPulling) yield return null;
            bool arrived = motor.LastPullArrived;
            GrappleTarget = null;
            activeHookItem = null;
            qualifiedHookProjectile = null;

            if (!arrived || turretHook || e == null || !e.IsAlive) yield break;

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

        // ---- Blade throw ------------------------------------------------------------------------

        /// <summary>Throw the equipped sword along the aim. Refused (kept) with no weapon, mid-pull,
        /// mid-execute, or with a blade already away. The weapon viewmodel hides until
        /// <see cref="ThrownBlade.Ended"/>; BladeRecall does the pull.</summary>
        bool TryThrowBlade(ItemData item)
        {
            if (ThrownBlade.IsAway) return false;
            if (motor != null && motor.IsPulling) return false;
            if (exec != null && exec.IsExecuting) return false;
            var weapons = GetComponent<WeaponController>();
            if (weapons == null || weapons.Current == null) return false;
            weapons.CancelAttack();

            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position + Vector3.up * 1.6f;
            Vector3 aim = look != null ? look.AimForward : transform.forward;
            int mask = motor != null ? motor.WorldMask : ~((1 << Layers.Player) | (1 << Layers.Enemy) | (1 << Layers.Interactable));
            if (ThrownBlade.Spawn(eye + aim * 0.6f, aim, weapons.Current, item, mask) == null) return false;

            var vm = GetComponentInChildren<WeaponViewmodel>(true);
            if (vm != null) vm.SetBladeAway(true);
            if (CameraFX.I != null) CameraFX.I.FovKick(4f);
            AudioManager.Play(Sfx.Dash, 0.7f, 1.35f);
            return true;
        }

        void OnBladeEnded(bool recalled)
        {
            var vm = GetComponentInChildren<WeaponViewmodel>(true);
            if (vm != null) vm.SetBladeAway(false);
            AudioManager.Play(Sfx.Click, 0.6f, recalled ? 1.1f : 0.8f);
        }

        // ---- Armed items -----------------------------------------------------------------------

        bool TryArmRebound(ItemData item)
        {
            if (motor == null || motor.IsReboundArmed) return false;
            motor.ArmRebound(item.reboundExitMultiplier, item.reboundBonusSpeed);
            GameEvents.RaisePromptFlash("REBOUND ARMED", 0.8f);
            return true;
        }

        bool TryArmSigil(ItemData item)
        {
            if (DeflectSigilArmed) return false;
            DeflectSigilArmed = true;
            armedSigil = item;
            GameEvents.RaisePromptFlash("SIGIL AWAITS A PERFECT", 0.9f);
            return true;
        }

        void OnParryResolved(ParryResult result)
        {
            if (result != ParryResult.Perfect || !DeflectSigilArmed || armedSigil == null || motor == null) return;
            ItemData sigil = armedSigil;
            DeflectSigilArmed = false;
            armedSigil = null;
            var stats = GameManager.I != null ? GameManager.I.statsData : null;
            if (stats != null)
                ParrySurge.GrantBonus(motor, Mathf.Max(1, sigil.deflectSigilBonusStacks),
                    stats.generalParrySurgeStep, stats.generalParrySurgeMaxStacks,
                    stats.generalParrySurgeSeconds);
            Vector3 aim = look != null ? look.AimForward : transform.forward;
            motor.AddImpulse(ProjectileMath.SpeedGain(aim, sigil.deflectSigilImpulse));
            GameEvents.RaisePromptFlash("SIGIL RELEASED", 0.7f);
        }

        void ClearArmedEffects()
        {
            ThrownBlade.ClearActive();   // death and reset put the sword back in the hand
            DeflectSigilArmed = false;
            armedSigil = null;
            if (motor != null) motor.ClearItemMovementBonuses();
        }
    }
}
