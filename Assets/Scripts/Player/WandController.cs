using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace VibeGame1
{
    /// <summary>
    /// The magic wand fired as part of the riposte. Bloodborne's firearms are the model: a small
    /// loadout of wands, each with its own cadence and blast shape, used only at the deathblow rather
    /// than fired freely.
    ///
    /// <see cref="ExecuteInteractor"/> drives this — it commits the lunge, then hands off to
    /// <see cref="FireRiposte"/> for the raise, discharge and settle.
    /// </summary>
    public class WandController : MonoBehaviour
    {
        public WandData[] loadout;

        public int Index { get; private set; }
        public WandData Current =>
            loadout != null && loadout.Length > 0 ? loadout[Mathf.Clamp(Index, 0, loadout.Length - 1)] : null;

        // ---- cooldown -------------------------------------------------------------------------
        // The wand is a resource now, not a free rider on every deathblow. It gates the BLAST only:
        // ExecuteInteractor falls back to the melee execute while this is running, so a cooldown can
        // never lock a player out of a boss deathblow window. Unscaled, like every other riposte beat.
        float readyAt;
        float cooldownTotal;
        float lastBroadcast = -1f;

        /// <summary>True when the wand can be fired. Always true with no wand equipped.</summary>
        public bool WandReady => Current == null || Time.unscaledTime >= readyAt;
        public float CooldownRemaining => Mathf.Max(0f, readyAt - Time.unscaledTime);
        public float CooldownTotal => cooldownTotal;
        /// <summary>1 = just fired, 0 = ready. Drives the HUD bar.</summary>
        public float CooldownFraction => cooldownTotal > 0f ? Mathf.Clamp01(CooldownRemaining / cooldownTotal) : 0f;

        /// <summary>Clear the cooldown (respawn, debug restore, wand swap at the altar).</summary>
        public void ResetCooldown()
        {
            readyAt = 0f;
            Broadcast(true);
        }

        void Broadcast(bool force)
        {
            float rem = CooldownRemaining;
            // Only when the tenth of a second the HUD can actually show has changed.
            float q = Mathf.Round(rem * 10f) / 10f;
            if (!force && Mathf.Approximately(q, lastBroadcast)) return;
            lastBroadcast = q;
            GameEvents.RaiseWandCooldownChanged(rem, cooldownTotal);
        }

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        WeaponViewmodel viewmodel;
        OffhandViewmodel offhandView;
        PlayerLook look;
        readonly Collider[] buf = new Collider[32];
        readonly RaycastHit[] rayBuf = new RaycastHit[32];
        readonly HashSet<EnemyController> seen = new HashSet<EnemyController>();
        readonly List<Vector3> arcPoints = new List<Vector3>();
        MaterialPropertyBlock mpb;

        void Awake()
        {
            viewmodel = GetComponentInChildren<WeaponViewmodel>();
            offhandView = GetComponentInChildren<OffhandViewmodel>(true);
            look = GetComponent<PlayerLook>();
            mpb = new MaterialPropertyBlock();
        }

        public void Equip(int i)
        {
            if (loadout == null || loadout.Length == 0) return;
            Index = Mathf.Clamp(i, 0, loadout.Length - 1);
            AudioManager.Play(Sfx.Click, 0.5f, 1.2f);
            ShowInOffhand();
        }

        void Start() { ShowInOffhand(); }

        // Dying already costs the run its Pyre; carrying a half-spent wand cooldown back to a
        // checkpoint would be a second, invisible punishment.
        void OnEnable() { GameEvents.PlayerRespawned += ResetCooldown; }
        void OnDisable() { GameEvents.PlayerRespawned -= ResetCooldown; }

        void Update()
        {
            // Wands are a FIXED set. Picking up an item must never change which wand is equipped, and an
            // item is never held in this hand — R only ever cycles between the wands themselves.
            if (!GameManager.IsPlaying || InputReader.I == null) return;
            Broadcast(false);
            if (InputReader.I.WandCyclePressed) TryCycle();
        }

        /// <summary>
        /// The body of the cycle press, with the input read left in <see cref="Update"/> (rule 2:
        /// InputReader is the only script that touches the Input System). Public so the feature suite
        /// can exercise cycling instead of skipping it.
        /// </summary>
        public bool TryCycle()
        {
            if (!GameManager.IsPlaying) return false;
            Next();
            return true;
        }

        /// <summary>The offhand always displays the equipped wand. Nothing else is ever put in it.</summary>
        void ShowInOffhand()
        {
            if (offhandView != null) offhandView.ShowWand(Current);
            GameEvents.RaiseWandChanged(Current);
        }

        public void Next()
        {
            if (loadout == null || loadout.Length == 0) return;
            Index = (Index + 1) % loadout.Length;
            AudioManager.Play(Sfx.Click, 0.5f, 1.2f);
            ShowInOffhand();
        }

        public void Prev()
        {
            if (loadout == null || loadout.Length == 0) return;
            Index = (Index - 1 + loadout.Length) % loadout.Length;
            AudioManager.Play(Sfx.Click, 0.5f, 1.2f);
            ShowInOffhand();
        }

        /// <summary>
        /// Raise the wand, charge, and blast the staggered target. <paramref name="baseDamage"/> is the
        /// equipped weapon's executeDamage; the wand's own damage is added on top.
        ///
        /// Only the riposte target takes <c>isExecute</c> damage — that is what satisfies the boss
        /// deathblow rule. Splash, chain and pierce damage on bystanders is ordinary damage.
        /// </summary>
        public IEnumerator FireRiposte(EnemyController target, float baseDamage)
        {
            var wand = Current;

            // Charged at the START of the shot, not the end: the cooldown covers the discharge itself,
            // so a slow wand's own cadence is part of the wait rather than being added on top of it.
            if (wand != null)
            {
                cooldownTotal = Mathf.Max(0f, wand.cooldown);
                readyAt = Time.unscaledTime + cooldownTotal;
                Broadcast(true);
            }

            // No wand equipped: behave exactly like the old melee execute so nothing breaks.
            if (wand == null)
            {
                if (target != null && target.IsAlive)
                {
                    GameEvents.RaiseRiposteLanded(target);
                    target.Health.TakeDamage(new DamageInfo
                    {
                        damage = baseDamage,
                        isExecute = true,
                        source = gameObject,
                        point = target.transform.position,
                        direction = transform.forward,
                    });
                }
                var fb = GameManager.I != null ? GameManager.I.feel : null;
                if (TimeScaleController.I != null)
                    TimeScaleController.I.HitStop(fb != null ? fb.executeHitStop : 0.14f);
                if (CameraShake.I != null) CameraShake.I.Big();
                AudioManager.Play(Sfx.Execute);
                yield break;
            }

            // The wand is buried in the victim for this long before it is pulled back out. Derived from
            // the wand's own recover so a heavy wand lingers, but clamped: too short and the stab reads
            // as a poke, too long and the riposte stops feeling fast.
            // Raised from 0.08–0.16s: at 60fps the old ceiling was ten frames, which is under the
            // threshold at which a pose reads at all. The whole riposte is only 0.6–1.0s, so this is
            // the one beat that has to be legible, and it costs no extra time — the withdraw simply
            // starts later inside the same recover window.
            float hold = Mathf.Clamp(wand.recover * 0.7f, 0.16f, 0.28f);

            // ---- raise + charge --------------------------------------------------------------
            // The wand lives in the OFFHAND full time now, so there is nothing to summon — just raise
            // the hand already holding it and build the charge at its tip. Overriding the weapon hand
            // here is what made the blast look sourceless: the wand appeared and vanished with the shot.
            //
            // This beat is now the COCK of a stab rather than a stationary charge: the hand draws back
            // over exactly wand.windup and snaps to full extension on the frame damage lands below, so
            // the riposte reads as the wand going INTO the enemy instead of an explosion in front of it.
            // The cadence is unchanged — same windup, same recover — only the pose differs.
            if (offhandView != null) offhandView.PlayThrust(wand.windup, hold, wand.recover);
            else if (viewmodel != null)
            {
                viewmodel.ShowOverride(wand.viewmodelPrefab, wand.viewmodelScale);
                Tint(viewmodel.OverrideInstance, wand.color);
                viewmodel.PlayWandRaise(wand.windup);
            }
            AudioManager.Play(Sfx.Heal, 0.5f, 1.6f);   // a rising charge whine

            if (CameraFX.I != null) CameraFX.I.ChromaticPulse(0.35f, wand.windup);
            yield return new WaitForSecondsRealtime(wand.windup);

            // ---- discharge -------------------------------------------------------------------
            // Announced BEFORE the damage so listeners can still read the
            // victim's position and state. This is the single raise point for the wand path.
            if (target != null) GameEvents.RaiseRiposteLanded(target);

            Vector3 origin = target != null ? target.transform.position : transform.position;
            float vScale = target != null && target.data != null ? Mathf.Max(0.25f, target.data.scale) : 1f;
            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position + Vector3.up;
            // THE CONTACT POINT IS THE MARK, and the mark is on the SURFACE of the chest, not at its
            // centre. `origin + up * 0.95` was the middle of the body: every flare and spark drawn there
            // rendered INSIDE the enemy and was never seen — the same trap that hid the lock-on dot for
            // a whole pass. Asking the enemy for its own glyph position also guarantees the shatter, the
            // bolt and the blast all land on the same pixels rather than near each other.
            // The blast helpers below still use `origin` so radii and chain distances are untouched.
            Vector3 contact = target != null ? target.DeathblowPoint(eye) : origin + Vector3.up * (0.95f * vScale);

            // NOTE: no PlayFire() here. PlayThrust owns the whole animation and they share the same
            // coroutine slot — firing a recoil now would cancel the hold and yank the wand back out on
            // the impact frame, which is the exact opposite of what the stab is for.
            Vector3 fireDir = look != null ? look.AimForward : transform.forward;

            if (offhandView != null)
            {
                // THE SOURCE. Everything here is anchored to the wand's own tip, because a blast drawn
                // only on the victim is exactly what read as "an explosion, you can't see the wand".
                Vector3 tip = offhandView.TipWorldPosition;

                offhandView.MuzzleFlash();                            // the tip lights the victim
                SlashFx.Flare(tip, wand.color, 0.34f, 0.13f);         // muzzle glint AT the wand

                // THE DISCHARGE. A braided BUNDLE of jagged bolts plus a misty flow riding the same
                // channel, both anchored at the wand tip and both terminating in the victim — see
                // Feel/PyreArc.cs. This replaced a single SlashFx.Beam, which is a straight tube: it
                // answered "where did that come from" but read as a laser sight, not as a discharge.
                PyreArc.Cast(tip, contact, wand.color, vScale);

                // A lance keeps going past the victim, so the bundle does too — the pierce is visible
                // as one continuous channel from the hand rather than as damage that happens off screen.
                if (wand.kind == WandKind.Lance)
                    PyreArc.Cast(contact, contact + fireDir * Mathf.Max(2f, wand.blastRadius), wand.color, vScale * 0.8f);

                // Sparks spray back along the beam toward the player: the discharge is happening at the
                // far end of an arm, inside a body, not floating in mid-air.
                Vector3 back = tip - contact;
                if (back.sqrMagnitude < 0.0001f) back = -fireDir;
                SlashFx.Sparks(contact, (back.normalized + Vector3.up * 0.35f).normalized, wand.color, 10, 8f, 30f);
                SlashFx.Flare(contact, wand.color, 0.45f, 0.16f);

                // The blast BLOOMING out of the wound, facing the player. Presence bought from geometry
                // and the tip light rather than from ScreenFlash — 0.55 alpha white-outs the frame and
                // takes the wand with it (ScreenFlash stays at 0.20, chroma at 0.4). A ring squarely
                // across the view is the one shape that reads as an expanding shockwave in first person.
                Vector3 faceN = eye - contact;
                if (faceN.sqrMagnitude < 0.0001f) faceN = -fireDir;
                SlashFx.Ring(contact, faceN.normalized, wand.color, Mathf.Max(0.7f, 0.85f * vScale), 0.22f);
            }

            seen.Clear();
            arcPoints.Clear();

            if (target != null && target.IsAlive)
            {
                seen.Add(target);
                target.Health.TakeDamage(new DamageInfo
                {
                    damage = baseDamage + wand.damage,
                    isExecute = true,
                    source = gameObject,
                    point = origin,
                    direction = fireDir,
                });
                Shove(target, fireDir, wand.knockback);
            }

            switch (wand.kind)
            {
                case WandKind.Scatter: FireScatter(wand, origin); break;
                case WandKind.Chain: FireChain(wand, origin); break;
                case WandKind.Lance: FireLance(wand, origin, fireDir); break;
                // Bolt: single target, nothing further.
            }

            // The chain and the pierce are drawn as MORE BUNDLES, jumping from the contact point out to
            // each further victim. LightningEffect.Strike used to be called here, which drops bolts out
            // of the SKY onto each position — a storm. That is the right shape for a thrown item and
            // the wrong one for a wand: nothing about it left the wand, so a chain read as unrelated
            // weather happening near the enemies rather than as the charge leaping throat to throat.
            // Lance is not listed: its pierce is already drawn above as one continuous bundle straight
            // through the victim, and a second pass here would double-draw the same line.
            if (wand.kind == WandKind.Chain)
            {
                Vector3 chainFrom = contact;
                for (int i = 0; i < arcPoints.Count; i++)
                {
                    Vector3 p = arcPoints[i] + Vector3.up * 0.9f;
                    PyreArc.Cast(chainFrom, p, wand.color, 0.8f);
                    // Each jump starts where the last one landed, so the arc is one path with a
                    // direction, not a starburst from the first body.
                    chainFrom = p;
                }
            }

            var feel = GameManager.I != null ? GameManager.I.feel : null;
            if (TimeScaleController.I != null)
                TimeScaleController.I.HitStop(wand.hitStop, feel != null ? feel.hitStopScale : 0.02f);
            if (CameraShake.I != null) CameraShake.I.Add(wand.shake, 0.3f);
            // Punch IN hard on contact — the world lurches toward the thing you just impaled — then back
            // out over the hold as the wand withdraws. FovKick assigns rather than accumulates, so the
            // second call is what turns a single jolt into an arc.
            // Chromatic aberration at 1.0 shredded the whole frame into RGB confetti — including the
            // wand — so the effect that was meant to sell the impact was destroying the thing the
            // player needed to see. 0.4 still lurches; it no longer smears.
            if (CameraFX.I != null) { CameraFX.I.ChromaticPulse(0.4f, 0.3f); CameraFX.I.FovKick(-13f); }
            StartCoroutine(FovSettleCo(hold));
            // Likewise the flash: a 0.55-alpha wash over a bloom-heavy frame white-outs the wand it is
            // supposed to be highlighting. Down to a tint, and short. The tip light carries the moment.
            if (ScreenFlash.I != null) ScreenFlash.I.Flash(wand.color, 0.20f, 0.16f);
            AudioManager.Play(Sfx.Execute);
            if (viewmodel != null) viewmodel.PlayWandFire(wand.recover);

            // ---- settle ----------------------------------------------------------------------
            yield return new WaitForSecondsRealtime(wand.recover);
            if (viewmodel != null) viewmodel.ClearOverride();
        }

        /// <summary>
        /// The back half of the stab's camera arc. Runs alongside the riposte rather than inside it so
        /// the FireRiposte cadence (windup then recover, nothing else) stays exactly as it was.
        /// </summary>
        IEnumerator FovSettleCo(float hold)
        {
            float t = 0f;
            while (t < hold) { t += Time.unscaledDeltaTime; yield return null; }
            if (CameraFX.I != null) CameraFX.I.FovKick(6f);
        }

        // ---- blast shapes --------------------------------------------------------------------

        void FireScatter(WandData wand, Vector3 origin)
        {
            int n = Physics.OverlapSphereNonAlloc(origin, wand.blastRadius, buf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var e = Resolve(buf[i]);
                if (e == null) continue;
                Splash(wand, e, (e.transform.position - origin).normalized);
            }
        }

        void FireChain(WandData wand, Vector3 origin)
        {
            int n = Physics.OverlapSphereNonAlloc(origin, wand.blastRadius, buf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);

            // Nearest-first so the arc reads as jumping outward rather than picking at random.
            var candidates = new List<EnemyController>();
            for (int i = 0; i < n; i++)
            {
                var e = Resolve(buf[i]);
                if (e != null) candidates.Add(e);
            }
            candidates.Sort((a, b) =>
                (a.transform.position - origin).sqrMagnitude.CompareTo((b.transform.position - origin).sqrMagnitude));

            int jumps = Mathf.Min(wand.chainTargets, candidates.Count);
            for (int i = 0; i < jumps; i++)
            {
                var e = candidates[i];
                arcPoints.Add(e.transform.position);
                Splash(wand, e, (e.transform.position - origin).normalized);
            }
        }

        void FireLance(WandData wand, Vector3 origin, Vector3 dir)
        {
            // Pierces from the player, through the ripostee, and onward. blastRadius is the length.
            Vector3 start = transform.position + Vector3.up * 1.0f;
            Vector3 through = (origin - start);
            if (through.sqrMagnitude > 0.001f) dir = through.normalized;

            float length = Mathf.Max(2f, wand.blastRadius);
            int n = Physics.SphereCastNonAlloc(start, 0.6f, dir, rayBuf, length, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var e = Resolve(rayBuf[i].collider);
                if (e == null) continue;
                Splash(wand, e, dir);
            }
            arcPoints.Add(start + dir * length);
        }

        // ---- helpers -------------------------------------------------------------------------

        /// <summary>Alive enemy from a collider, skipping anything already hit by this discharge.</summary>
        EnemyController Resolve(Collider c)
        {
            if (c == null) return null;
            var e = c.GetComponentInParent<EnemyController>();
            if (e == null || !e.IsAlive || seen.Contains(e)) return null;
            seen.Add(e);
            return e;
        }

        /// <summary>Bystander damage. Never isExecute — only the ripostee earns the deathblow.</summary>
        void Splash(WandData wand, EnemyController e, Vector3 dir)
        {
            if (wand.splashDamage <= 0f) return;
            e.Health.TakeDamage(new DamageInfo
            {
                damage = wand.splashDamage,
                source = gameObject,
                point = e.transform.position,
                direction = dir,
            });
            Shove(e, dir, wand.knockback * 0.6f);
        }

        /// <summary>
        /// Push an enemy through its NavMeshAgent so it stays on the mesh. Enemies have no impulse
        /// API of their own and are agent-driven, so this is the only safe way to move them.
        /// </summary>
        static void Shove(EnemyController e, Vector3 dir, float metres)
        {
            if (metres <= 0f || e == null) return;
            var agent = e.GetComponent<NavMeshAgent>();
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            agent.Move(dir.normalized * metres);
        }

        /// <summary>
        /// ONE WRITER PER MATERIAL CHANNEL. An <see cref="EnergyGlow"/> on the model rewrites
        /// `_EmissionColor` every LateUpdate, so a property block set here would survive exactly one
        /// frame before being overwritten — route the hue through the glow instead. The direct write is
        /// the fallback for models built without one.
        /// </summary>
        void Tint(GameObject instance, Color color)
        {
            if (instance == null) return;

            var glow = instance.GetComponent<EnergyGlow>();
            if (glow != null)
            {
                glow.Collect();       // freshly instantiated: bind its Seg*/Tip*/Float* parts
                glow.SetTint(color);
                return;
            }

            if (mpb == null) mpb = new MaterialPropertyBlock();
            foreach (var r in instance.GetComponentsInChildren<Renderer>())
            {
                // Only the emissive tip is tinted; the shaft keeps its own dark material.
                if (!r.gameObject.name.StartsWith("Tip")) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(EmissionId, color);
                mpb.SetColor(BaseColorId, Color.black);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
