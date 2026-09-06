using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Finds staggered enemies in front of the player and performs the critical attack (deathblow).</summary>
    public class ExecuteInteractor : MonoBehaviour
    {
        public float range = 3.5f;
        public float coneDeg = 40f;
        public float duration = 0.55f;

        [Tooltip("Seconds of melee commit before the wand is drawn. The wand's own windup/recover " +
                 "then set the rest of the riposte's rhythm, so different wands feel different.")]
        public float wandCommit = 0.18f;

        [Header("Step-in")]
        [Tooltip("How close the player ends up to the victim when the stab lands. Scaled by the enemy's " +
                 "EnemyData.scale so the Boss is not stabbed from inside its own chest. Must leave the " +
                 "victim FRAMED: at 95 degrees FOV anything under ~2m puts the camera inside a black " +
                 "silhouette and the whole riposte becomes invisible.")]
        public float stabStandoff = 2.2f;

        [Tooltip("Metres per second the player closes the gap over the wind-up. Fast enough to cover the " +
                 "execute range, slow enough that the step reads as a lunge rather than a teleport.")]
        public float stepInSpeed = 7f;

        /// <summary>
        /// The posture-broken enemy this press would deathblow, or null. Non-null exactly when the
        /// player is looking at a MARKED enemy inside <see cref="range"/> and <see cref="coneDeg"/>;
        /// while it is null an attack press is an ordinary swing.
        /// </summary>
        public EnemyController Target { get; private set; }
        /// <summary>True when a press right now would come out as a deathblow rather than a swing.</summary>
        public bool HasMarkedTarget { get { return Target != null && !IsExecuting; } }
        public bool IsExecuting { get; private set; }

        /// <summary>Arc violet, matching M_DeathblowMark, so the commit burst reads as THAT GLYPH
        /// breaking rather than as a generic white flash.</summary>
        static readonly Color MarkHue = new Color(0.48f, 0.17f, 1f);

        Health health;
        FirstPersonMotor motor;
        WeaponController weapons;
        WandController wands;
        WeaponViewmodel viewmodel;
        PlayerLook look;
        CharacterController cc;
        readonly Collider[] buf = new Collider[16];
        string lastPrompt = "";
        bool lastDeathblow;

        void Awake()
        {
            health = GetComponent<Health>();
            motor = GetComponent<FirstPersonMotor>();
            weapons = GetComponent<WeaponController>();
            wands = GetComponent<WandController>();
            viewmodel = GetComponentInChildren<WeaponViewmodel>();
            look = GetComponent<PlayerLook>();
            cc = GetComponent<CharacterController>();
        }

        void Update()
        {
            if (IsExecuting) return;
            Target = null;
            if (GameManager.IsPlaying)
            {
                int n = Physics.OverlapSphereNonAlloc(transform.position, range, buf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                Vector3 fwd = look != null ? look.AimForward : transform.forward;
                for (int i = 0; i < n; i++)
                {
                    var e = buf[i].GetComponentInParent<EnemyController>();
                    if (e == null || !e.IsStaggered) continue;
                    Vector3 to = e.transform.position + Vector3.up * 0.8f * e.transform.localScale.y - (look != null && look.Cam ? look.Cam.position : transform.position);
                    float dist = to.magnitude;
                    if (Vector3.Angle(fwd, to) > coneDeg) continue;
                    if (dist < best) { best = dist; Target = e; }
                }
            }
            // A boss deathblow gets the big banner; regular enemies get the small prompt.
            //
            // The wand cooldown never blocks the deathblow — it downgrades it to the melee execute —
            // so the prompt SAYS SO rather than the blast quietly not happening. A player who fires a
            // Voidspine and then sees a plain EXECUTE two seconds later would otherwise conclude the
            // wand was broken.
            bool deathblow = Target is BossController;
            bool wandCooling = wands != null && wands.Current != null && !wands.WandReady;
            // Names the INPUT, not just the verb. The old prompt said "EXECUTE" and left the player to
            // discover that their ordinary attack button had quietly changed meaning — which is exactly
            // how a deliberate press comes to feel automatic.
            string prompt = Target != null && !deathblow
                ? (wandCooling ? "DEATHBLOW  <alpha=#99>[ATTACK]  WAND " + wands.CooldownRemaining.ToString("0.0") + "s"
                               : "DEATHBLOW  <alpha=#99>[ATTACK]")
                : "";
            if (prompt != lastPrompt) { lastPrompt = prompt; GameEvents.RaisePromptChanged(prompt); }
            if (deathblow != lastDeathblow) { lastDeathblow = deathblow; GameEvents.RaiseDeathblowReady(deathblow); }
        }

        /// <summary>
        /// The frame the player commits. Its whole job is to make the press UNMISTAKABLY not a swing,
        /// on the same frame it is pressed and before any of the deathblow's own timing has run.
        ///
        /// <para>The glyph over the enemy's head shatters — a flare and a spray of violet sparks thrown
        /// off exactly where the marker was, plus a beam running from the weapon to it — and the audio
        /// is the low execute stinger rather than <see cref="Sfx.Swing"/>. Previously the commit was a
        /// swing whoosh and no visual at all, so a deathblow started out looking and sounding identical
        /// to the attack the player thought they were throwing. That is the whole of the "it ripostes
        /// automatically" complaint: the press WAS deliberate, but nothing on screen said so.</para>
        /// </summary>
        void CommitCue(EnemyController e)
        {
            if (e == null) return;
            // Asks the ENEMY where its glyph is rather than recomputing a height here. The marker now
            // rides the sternum and is pushed off the body surface toward the eye every frame, so a
            // constant local height would have put the shatter inside the body — invisible, which is
            // the same trap that hid the lock-on dot for a whole pass.
            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position;
            Vector3 mark = e.DeathblowPoint(eye);
            SlashFx.Flare(mark, MarkHue, 0.75f, 0.16f);
            SlashFx.Sparks(mark, Vector3.up, MarkHue, 14, 8f, 120f);
            if (viewmodel != null) SlashFx.Beam(viewmodel.TipWorldPosition, mark, MarkHue, 0.05f, 0.14f);
            if (CameraFX.I != null) CameraFX.I.ChromaticPulse(0.3f, 0.18f);
        }

        /// <summary>
        /// One attack press against a MARKED enemy. Returns false — leaving
        /// <see cref="WeaponController.TryAttack"/> to swing normally — whenever there is no marked
        /// target, which is every press outside a posture break.
        /// </summary>
        public bool TryExecute()
        {
            if (Target == null || IsExecuting) return false;
            StartCoroutine(ExecuteCo(Target));
            return true;
        }

        /// <summary>
        /// Deathblow a specific enemy NOW, without the look-cone scan. The Grapple item's arrival
        /// calls this: it has already put the player at stand-off, and the enemy has already been
        /// staggered, so the ordinary "is it marked and in front of me" gate would only re-derive what
        /// the pull just guaranteed. Runs the SAME coroutine as a pressed deathblow — wand riposte or
        /// melee execute, RiposteLanded, hitstop, the lot — so there is one execute path, not two.
        /// Refused while an execute is running, and for a dead or unstaggered enemy.
        /// </summary>
        public bool ExecuteNow(EnemyController e)
        {
            if (e == null || IsExecuting || !e.IsAlive || !e.IsStaggered) return false;
            Target = e;
            StartCoroutine(ExecuteCo(e));
            return true;
        }

        IEnumerator ExecuteCo(EnemyController e)
        {
            IsExecuting = true;
            health.Invulnerable = true;
            if (motor != null) motor.CanMove = false;
            CommitCue(e);              // BEFORE BeginExecuted: it reads the marker's position
            e.BeginExecuted(transform);
            GameEvents.RaisePromptChanged("");
            lastPrompt = "";
            GameEvents.RaiseDeathblowReady(false);
            lastDeathblow = false;

            var w = weapons != null ? weapons.Current : null;
            float dmg = w != null ? w.executeDamage : 300f;
            // A cooling wand degrades to the melee deathblow below rather than refusing the input.
            // Refusing would be a soft-lock: the boss deathblow window is 5 s and Voidspine's cooldown
            // is 9 s, so a strict gate would make the boss unkillable through no fault of the player.
            var wand = wands != null && wands.WandReady ? wands.Current : null;

            if (wand != null)
            {
                // Riposte: brief melee commit, then the wand is cocked and driven into the victim. The
                // wand's windup and recover drive the rest of the timing, so each wand has its own cadence.
                //
                // The player closes the gap over exactly the same span, so the body arrives under the wand
                // on the impact frame. Without this the stab animation plays out at whatever range the
                // deathblow prompt happened to fire from (up to `range`, 3.5m) and the wand jabs at air.
                StartCoroutine(StepInCo(e, wandCommit + wand.windup));

                if (viewmodel != null) viewmodel.PlayExecute(wandCommit);
                AudioManager.Play(Sfx.Execute, 0.55f, 0.62f);   // NOT Sfx.Swing: the press must not sound like a swing
                yield return new WaitForSecondsRealtime(wandCommit);

                // WandController raises RiposteLanded and applies all damage — do not duplicate here.
                yield return wands.FireRiposte(e, dmg);
            }
            else
            {
                // No wand equipped: the original melee deathblow, with the same step-in so a wandless
                // deathblow also lands in contact rather than at arm's length.
                StartCoroutine(StepInCo(e, duration * 0.55f));
                if (viewmodel != null) viewmodel.PlayExecute(duration);
                AudioManager.Play(Sfx.Execute, 0.55f, 0.62f);   // NOT Sfx.Swing: the press must not sound like a swing

                yield return new WaitForSecondsRealtime(duration * 0.55f);

                var feel = GameManager.I.feel;
                if (e != null && e.IsAlive)
                {
                    // Announce the riposte BEFORE the killing damage, so anything keyed off it (the
                    // any riposte listener) can still read the victim's position and state.
                    GameEvents.RaiseRiposteLanded(e);
                    e.Health.TakeDamage(new DamageInfo { damage = dmg, isExecute = true, source = gameObject, point = e.transform.position, direction = transform.forward });
                }
                TimeScaleController.I.HitStop(feel.executeHitStop, feel.hitStopScale);
                if (CameraShake.I) CameraShake.I.Big();
                // Matches the wand stab's arc: punch in on contact, ease back out as the blow finishes.
                if (CameraFX.I) { CameraFX.I.ChromaticPulse(1f, 0.35f); CameraFX.I.FovKick(-12f); }
                StartCoroutine(FovSettleCo(0.16f));
                if (ScreenFlash.I) ScreenFlash.I.Flash(w != null ? w.neon : Color.white, 0.5f, 0.2f);
                AudioManager.Play(Sfx.Execute);

                yield return new WaitForSecondsRealtime(duration * 0.45f);
            }

            if (e != null) e.EndExecuted();
            if (viewmodel != null) viewmodel.ClearOverride();   // belt and braces if the blast was cut short
            health.Invulnerable = false;
            if (motor != null) motor.CanMove = true;
            IsExecuting = false;
        }

        /// <summary>
        /// Walks the player into stabbing range over <paramref name="seconds"/>, arriving as the wand
        /// reaches full extension.
        ///
        /// <para>Deliberately NOT FirstPersonMotor.Teleport: Teleport pushes a yaw through PlayerLook,
        /// which would wrench the player's aim mid-execute. It also is not a raw transform write —
        /// everything goes through CharacterController.Move so walls, ledges and step offsets still stop
        /// us, and the motor's own Move in the same frame composes with it the way Unity intends.</para>
        ///
        /// <para>Horizontal only. Adding a vertical component here would fight the motor's gravity, and a
        /// deathblow on a walkway over a pit is not the place to start arguing about who owns Y.</para>
        ///
        /// <para>Runs alongside the riposte rather than before it, so it costs no extra time and cannot
        /// change the execute's cadence. Unscaled, so the hitstop on the impact frame does not strand the
        /// player mid-step.</para>
        /// </summary>
        IEnumerator FovSettleCo(float hold)
        {
            float t = 0f;
            while (t < hold) { t += Time.unscaledDeltaTime; yield return null; }
            if (CameraFX.I != null) CameraFX.I.FovKick(5f);
        }

        IEnumerator StepInCo(EnemyController e, float seconds)
        {
            if (cc == null || e == null) yield break;

            float standoff = stabStandoff * (e.data != null ? Mathf.Max(0.6f, e.data.scale) : 1f);
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                if (e == null || !cc.enabled) yield break;

                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > standoff && dist > 0.001f)
                    cc.Move(to / dist * Mathf.Min(dist - standoff, stepInSpeed * Time.unscaledDeltaTime));

                yield return null;
            }
        }
    }
}
