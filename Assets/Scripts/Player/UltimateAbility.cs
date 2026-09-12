using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace VibeGame1
{
    /// <summary>
    /// The SUPER ATTACK. Spend a full Pyre bar (Q) and the equipped weapon unleashes its own payoff —
    /// there is one per weapon, authored entirely as data on <see cref="WeaponData"/>, so a new weapon
    /// ships with a super without a line of new code here.
    ///
    /// <para>The class name is historical: this used to be a single weapon-agnostic "Overdrive" burst.
    /// It was REPOINTED rather than replaced, so the <c>Q</c> binding, the Player prefab wiring and
    /// <see cref="GameEvents.UltimateUsed"/> all survive. A fifth parallel ability would have meant two
    /// competing full-bar payoffs on the same meter.</para>
    ///
    /// <para>Rule 1: the world crawls, the player does not — the time request is
    /// <c>affectsPlayer:false</c> and every wait here is realtime, so the hitstop on the impact frame
    /// cannot freeze the swing that caused it.</para>
    /// </summary>
    public class UltimateAbility : MonoBehaviour
    {
        public Material ringMaterial;
        public bool IsActive { get; private set; }

        // ---- preserved electrical prototype --------------------------------------------------------
        // Consts, not serialized fields: these are feel constants, not content, so rule 9 does not apply
        // and there is nothing on the Player prefab to go stale.
        //
        // Player Pyre no longer calls BoltCo. Its complete electrical flight remains here, and the public
        // PyreArc / LightningEffect helpers remain intact, for the requested future enemy attack.
        const float BoltSecondsPerMetre = 0.022f;
        const float BoltMinSeconds = 0.06f;
        const float BoltMaxSeconds = 0.20f;
        /// <summary>Ember orange. The Pyre is fire and the blade has been burning with it since the
        /// first deflect (Feel/WeaponEmber.cs); the bolt is that charge leaving the weapon.</summary>
        static readonly Color EmberHue = new Color(1f, 0.45f, 0.12f);

        PlayerResources resources;
        PlayerStats stats;
        PlayerCombat combat;
        PlayerLook look;
        WeaponController weapons;
        WeaponViewmodel viewmodel;
        readonly Collider[] buf = new Collider[48];
        readonly HashSet<EnemyController> seen = new HashSet<EnemyController>();

        void Awake()
        {
            resources = GetComponent<PlayerResources>();
            stats = GetComponent<PlayerStats>();
            combat = GetComponent<PlayerCombat>();
            look = GetComponent<PlayerLook>();
            weapons = GetComponent<WeaponController>();
            viewmodel = GetComponentInChildren<WeaponViewmodel>(true);
        }

        void Update()
        {
            if (!GameManager.IsPlaying || IsActive) return;
            if (InputReader.I == null || !InputReader.I.UltimatePressed) return;
            TrySuper();
        }

        /// <summary>
        /// One Q press: spend a full Pyre bar and unleash the equipped weapon's super, or refuse
        /// (already running, bar not full, no weapon, executing, staggered). <see cref="Update"/> calls
        /// this when <see cref="InputReader"/> reports the press — input is still read only there.
        /// Returns whether the super fired.
        /// </summary>
        public bool TrySuper()
        {
            if (IsActive) return false;
            if (resources == null || !resources.PyreFull) { AudioManager.Play(Sfx.Click, 0.4f, 0.6f); return false; }
            var w = weapons != null ? weapons.Current : null;
            if (w == null) return false;
            if (combat != null && (combat.IsExecuting || combat.IsStaggered)) return false;
            StartCoroutine(SuperCo(w));
            return true;
        }

        /// <summary>Old name, kept so existing callers and tests keep working.</summary>
        public bool TryUltimate() { return TrySuper(); }

        IEnumerator SuperCo(WeaponData w)
        {
            IsActive = true;
            var d = GameManager.I.statsData;
            var feel = GameManager.I.feel;
            resources.ConsumePyre();
            GameEvents.RaiseUltimateUsed();

            float total = w.superWindup + w.superActive + w.superRecover;

            // affectsPlayer:false — the world crawls, you do not. That is the power fantasy.
            int handle = TimeScaleController.I != null
                ? TimeScaleController.I.Request(d.ultSlowScale, Mathf.Max(total, d.ultSlowSeconds), false)
                : -1;

            Color hue = w.neon;
            if (CameraFX.I) { CameraFX.I.FovKick(feel != null ? feel.ultFovKick : -10f); CameraFX.I.ChromaticPulse(0.45f, total); }
            // Deliberately a tint, not a wash. 0.55 alpha over a bloom-heavy frame destroyed the very
            // thing it was punctuating once already — see docs/ENGINEERING-LOG.md.
            if (ScreenFlash.I) ScreenFlash.I.Flash(hue, 0.22f, 0.22f);
            AudioManager.Play(Sfx.Ultimate);

            // The hand plays the weapon's own swing across windup+active, so the super reads as THIS
            // weapon rather than as a generic screen effect.
            if (viewmodel != null) viewmodel.PlayAttack(0, w.superWindup + w.superActive, w.superWindup);

            yield return WaitRealtime(w.superWindup);

            int hits = Mathf.Max(1, w.superHits);
            float gap = hits > 1 ? w.superActive / hits : 0f;
            for (int i = 0; i < hits; i++)
            {
                ApplyHit(w, d, hue, i, hits);
                if (gap > 0f && i < hits - 1) yield return WaitRealtime(gap);
            }

            if (TimeScaleController.I != null)
                TimeScaleController.I.HitStop(w.superHitStop, feel != null ? feel.hitStopScale : 0.02f);
            if (CameraShake.I) CameraShake.I.Add(w.superShake, 0.4f);

            yield return WaitRealtime(w.superRecover);
            if (handle >= 0 && TimeScaleController.I != null) TimeScaleController.I.Release(handle);
            IsActive = false;
        }

        static IEnumerator WaitRealtime(float seconds)
        {
            if (seconds <= 0f) yield break;
            yield return new WaitForSecondsRealtime(seconds);
        }

        /// <summary>
        /// One application of the super's hit shape. Geometry comes entirely from the weapon's data —
        /// <c>superRadius</c> is the reach and <c>superArcDeg</c> the full width (360 = everything) —
        /// so <see cref="SuperKind"/> only chooses the presentation.
        /// </summary>
        void ApplyHit(WeaponData w, PlayerStatsData d, Color hue, int index, int hits)
        {
            Vector3 origin = transform.position + Vector3.up * 1.0f;
            Vector3 fwd = look != null ? look.AimForward : transform.forward;
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
            if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;
            flat.Normalize();

            Fx(w, hue, origin, flat, index, hits);

            seen.Clear();
            float mult = stats != null ? stats.DamageMultiplier(w) : 1f;
            float half = Mathf.Clamp(w.superArcDeg, 1f, 360f) * 0.5f;
            Vector3 tip = WeaponTip(origin + flat * 0.8f);

            int n = Physics.OverlapSphereNonAlloc(transform.position, w.superRadius, buf, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var e = buf[i].GetComponentInParent<EnemyController>();
                if (e == null || !e.IsAlive || seen.Contains(e)) continue;

                Vector3 to = e.transform.position - transform.position;
                to.y = 0f;
                if (half < 180f && to.sqrMagnitude > 0.0001f && Vector3.Angle(flat, to) > half) continue;
                seen.Add(e);

                Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : flat;
                // Damage lands just after the broad hot edge reaches the contact beat. The target set,
                // damage, posture and shove remain the weapon-authored super contract.
                StartCoroutine(FireSlashContactCo(e, tip, hue, w.superDamage * mult, w, d, hits, dir));
            }
        }

        IEnumerator FireSlashContactCo(EnemyController e, Vector3 from, Color hue, float damage,
                                       WeaponData w, PlayerStatsData d, int hits, Vector3 dir)
        {
            // Realtime, like the existing Pyre cadence: the hot edge visibly leaves the hand before
            // the body reacts, while every victim remains in the same authored hit beat.
            yield return new WaitForSecondsRealtime(FireSlashFx.DefaultSeconds * 0.32f);
            if (e == null || !e.IsAlive) yield break;

            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position + Vector3.up;
            Vector3 point = e.DeathblowPoint(eye);
            Color fire = Color.Lerp(EmberHue, SlashFx.NormaliseColor(hue), 0.16f);
            Vector3 back = from - point;
            if (back.sqrMagnitude < 0.0001f) back = -dir;
            SlashFx.Flare(point, fire, 0.62f, 0.14f);
            SlashFx.Sparks(point, (back.normalized + Vector3.up * 0.32f).normalized, fire, 9, 8f, 45f);
            AudioManager.Play(Sfx.Execute, 0.32f, 1.12f);

            e.Health.TakeDamage(new DamageInfo
            {
                damage = damage,
                source = gameObject,
                point = point,
                direction = dir,
            });
            bool boss = e is BossController;
            e.Posture.Add(boss ? e.Posture.Max * d.ultBossPostureFraction / Mathf.Max(1, hits)
                               : w.superPostureDamage);
            e.OnParried(0f);
            Shove(e, dir, w.superKnockback);
        }

        /// <summary>
        /// Preserved travelling-bolt prototype. Player Pyre no longer calls this path; keep it available
        /// as the electrical-flight reference for a future enemy ability.
        ///
        /// <para>This is the difference between a super and an area query with sparks on top. The blow
        /// already started at the tip (<see cref="Fx"/> runs the energy up the blade) and the enemy
        /// already took the damage — what was missing was the middle: something visibly crossing the gap
        /// between the two, so the hand, the flight and the body falling over are one causal chain rather
        /// than three effects that happened at once.</para>
        ///
        /// <para>It detonates on <c>DeathblowPoint</c> — the victim's chest <b>surface</b> facing the
        /// player, not its centre of mass. A burst drawn at the centre renders inside the mesh and is
        /// never seen; that trap has now cost this project three separate systems (see
        /// docs/ENGINEERING-LOG.md).</para>
        ///
        /// <para>Realtime throughout (rule 1). The super already holds the world at
        /// <c>ultSlowScale</c> with <c>affectsPlayer:false</c>, so a scaled flight would crawl to a stop
        /// inside the very slow-motion it is meant to be showing off.</para>
        /// </summary>
        IEnumerator BoltCo(EnemyController e, Vector3 from, Color hue, float damage,
                           WeaponData w, PlayerStatsData d, int hits, Vector3 dir)
        {
            if (e == null) yield break;

            // Blended toward ember orange: this is the PYRE spending itself, and Pyre is fire — the
            // weapon has been visibly burning with it since the first deflect (Feel/WeaponEmber.cs).
            // Half-blended rather than pure flame so a bolt still carries which weapon threw it.
            Color fire = Color.Lerp(hue, EmberHue, 0.5f);

            Vector3 eye = look != null && look.Cam != null ? look.Cam.position : transform.position + Vector3.up;
            Vector3 to = e.DeathblowPoint(eye);
            float flight = Mathf.Clamp(Vector3.Distance(from, to) * BoltSecondsPerMetre, BoltMinSeconds, BoltMaxSeconds);

            // The muzzle: the bolt tears out of the weapon before it is anywhere.
            SlashFx.Flare(from, fire, 0.42f, 0.10f);

            // The braided bolt bundle and the mist flowing along it, weapon tip into the victim.
            // Cast once, up front: it lives 0.34 s and the bolt's own flight is 0.06-0.20 s, so it is
            // still crackling when the detonation lands rather than being a separate second event.
            PyreArc.Cast(from, to, fire, 1f);

            float t = 0f;
            Vector3 at = from;
            while (t < flight)
            {
                float k = t / flight;
                // Eased IN: a bolt leaves fast and arrives faster. A linear crossing reads as a floating
                // ball rather than as something thrown.
                Vector3 target = e != null ? e.DeathblowPoint(eye) : to;
                at = Vector3.Lerp(from, target, k * (2f - k));
                SlashFx.Flare(at, fire, Mathf.Lerp(0.34f, 0.52f, k), 0.09f);   // head plus its own tail
                // The single tube streak is gone: PyreArc above already draws the channel, and a beam
                // over a braided bundle just fills the gaps between the strands and reads as a laser
                // sight again. See docs/ENGINEERING-LOG.md.
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (e == null || !e.IsAlive) yield break;
            to = e.DeathblowPoint(eye);

            // ---- impact ---------------------------------------------------------------------------
            // On the SURFACE facing the player, so it is not drawn inside the body it is hitting.
            Vector3 back = from - to;
            if (back.sqrMagnitude < 0.0001f) back = -dir;
            SlashFx.Flare(to, fire, 0.85f, 0.18f);
            SlashFx.Sparks(to, (back.normalized + Vector3.up * 0.4f).normalized, fire, 14, 9f, 55f);
            SlashFx.Ring(to, back.normalized, fire, 0.9f, 0.20f);
            AudioManager.Play(Sfx.Execute, 0.35f, 1.25f);

            e.Health.TakeDamage(new DamageInfo
            {
                damage = damage,
                source = gameObject,
                point = to,
                direction = dir,
            });

            // A boss's posture bar is a different scale to a grunt's, so it takes a FRACTION of its
            // own bar rather than a flat number that would either do nothing or trivialise it.
            bool boss = e is BossController;
            e.Posture.Add(boss ? e.Posture.Max * d.ultBossPostureFraction / Mathf.Max(1, hits)
                               : w.superPostureDamage);
            e.OnParried(0f);
            Shove(e, dir, w.superKnockback);
        }

        /// <summary>
        /// Draws Pyre as a weapon-originated fire sheet using the weapon's authored hit geometry.
        ///
        /// <para><b>THE BLAST LEAVES THE WEAPON.</b> Every element below is anchored to
        /// <see cref="WeaponViewmodel.TipWorldPosition"/> — the sword's point, the hammer's head, the
        /// dagger's tip — exactly as <see cref="WandController"/> anchors the riposte blast to the wand's
        /// tip. Drawn from the player's own transform instead (which is what this did) a super reads as
        /// an effect happening TO the player rather than one they authored: the hand swings, and
        /// something unrelated goes off around their navel. See docs/ENGINEERING-LOG.md.</para>
        ///
        /// <para><see cref="FireSlashFx"/> draws a broad hot edge and torn ember fringe, rather than the
        /// old electrical bundle. Radius, arc and multi-hit progress still come directly from data.</para>
        /// </summary>
        void Fx(WeaponData w, Color hue, Vector3 origin, Vector3 flat, int index, int hits)
        {
            Vector3 tip = WeaponTip(origin + flat * 0.8f);

            // The muzzle: on the FIRST beat of every super, the weapon itself flashes and the energy
            // runs up the blade from the fist. One shared gesture across all four kinds, so whatever
            // the shape, the player always sees the blow start in their own hand.
            if (index == 0)
            {
                SlashFx.Beam(GripPoint(tip), tip, hue, 0.05f, 0.16f);
                SlashFx.Flare(tip, hue, 0.42f, 0.14f);
            }

            // Every weapon keeps its authored hit count, radius and arc. Pyre now renders those exact
            // numbers as one torn sheet of flame per hit instead of sending electrical bundles at each
            // victim. Multi-hit weapons walk the hot edge across the same arc.
            float progress = hits > 1 ? (float)index / (hits - 1) : 0.5f;
            Color fire = Color.Lerp(EmberHue, SlashFx.NormaliseColor(hue), 0.16f);
            float life = Mathf.Clamp(w.superActive > 0f ? w.superActive : FireSlashFx.DefaultSeconds,
                                     FireSlashFx.MinSeconds, FireSlashFx.MaxSeconds);
            FireSlashFx.Play(tip, flat, fire, w.superRadius, w.superArcDeg, progress, life);
            SlashFx.Sparks(tip, flat, fire, index == 0 ? 14 : 7, 9f, 38f);
        }

        /// <summary>
        /// Where the blast comes out of the weapon. Falls back to <paramref name="fallback"/> (a point
        /// out in front of the player) only when there is no viewmodel at all — a headless test rig or
        /// a weaponless player — never to the player's own transform, which is the read this whole
        /// method exists to avoid.
        /// </summary>
        Vector3 WeaponTip(Vector3 fallback)
        {
            if (viewmodel == null) viewmodel = GetComponentInChildren<WeaponViewmodel>(true);
            if (viewmodel == null || viewmodel.CurrentModel == null) return fallback;
            return viewmodel.TipWorldPosition;
        }

        /// <summary>The fist on the hilt, for the run-up beam. Falls back just short of the tip.</summary>
        Vector3 GripPoint(Vector3 tip)
        {
            if (viewmodel == null) return tip;
            Vector3 g = viewmodel.GripWorldPosition;
            return (g - tip).sqrMagnitude > 0.0004f ? g : tip;
        }

        /// <summary>
        /// The floor under the hammer head. A raycast so the quake lands on the surface actually being
        /// struck (a platform, a stair) rather than at a guessed height; if nothing is hit — mid-air
        /// super over a pit — it falls back to the player's own feet, which is still the ground plane
        /// the damage sphere uses.
        /// </summary>
        Vector3 GroundUnder(Vector3 tip, Vector3 flat)
        {
            Vector3 probe = tip + flat * 0.5f;
            RaycastHit hit;
            if (Physics.Raycast(probe + Vector3.up * 0.5f, Vector3.down, out hit, 6f,
                                ~(Layers.PlayerMask | Layers.EnemyMask), QueryTriggerInteraction.Ignore))
                return hit.point;
            return new Vector3(probe.x, transform.position.y - 0.9f, probe.z);
        }

        /// <summary>
        /// <paramref name="count"/> beams from <paramref name="hub"/>, spread evenly across
        /// <paramref name="arcDeg"/> around <paramref name="forward"/>. <paramref name="rise"/> tilts
        /// them out of the horizontal plane.
        /// </summary>
        static void Fan(Vector3 hub, Vector3 forward, Color hue, float arcDeg, float radius,
                        int count, float width, float seconds, float rise = 0f)
        {
            count = Mathf.Clamp(count, 1, 20);
            bool full = arcDeg >= 359f;
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float)i / (full ? count : count - 1) : 0.5f;
                float a = full ? t * 360f : Mathf.Lerp(-arcDeg * 0.5f, arcDeg * 0.5f, t);
                Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * forward;
                if (rise > 0f) dir = (dir + Vector3.up * rise).normalized;
                SlashFx.Beam(hub, hub + dir * radius, hue, width, seconds);
            }
        }

        /// <summary>
        /// Push an enemy through its NavMeshAgent so it stays on the mesh — the same idiom
        /// <see cref="WandController"/> uses. Enemies are agent-driven and have no impulse API.
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
    }
}
