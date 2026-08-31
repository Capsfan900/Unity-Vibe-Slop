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
                e.Health.TakeDamage(new DamageInfo
                {
                    damage = w.superDamage * mult,
                    source = gameObject,
                    point = e.transform.position,
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
        }

        /// <summary>
        /// The one place <see cref="SuperKind"/> matters: how the blow is drawn.
        ///
        /// <para>Built from <see cref="SlashFx.Beam"/> rather than <see cref="SlashFx.Ring"/>. `Ring`
        /// draws a hoop 0.045 m thick, which is right for a 1–2 m grounded impact and invisible at the
        /// 5–12 m reach a super covers — from the centre, the far side of a 7.5 m ring is a 4.5 cm wire
        /// seven metres away, i.e. sub-pixel. A fan of beams radiating from the player is the same
        /// silhouette, stays legible at any radius, and is anchored to the player, which is the rule
        /// the riposte blast had to learn: an effect must be visibly authored by the thing that fired
        /// it. See docs/ENGINEERING-LOG.md.</para>
        /// </summary>
        void Fx(WeaponData w, Color hue, Vector3 origin, Vector3 flat, int index, int hits)
        {
            Vector3 hub = transform.position + Vector3.up * 0.8f;

            switch (w.superKind)
            {
                case SuperKind.Cleave:
                    // The sweep: beams fanned across the arc, plus the crescent on top for the edge.
                    Fan(hub, flat, hue, w.superArcDeg, w.superRadius, 9, 0.05f, 0.22f);
                    SlashFx.Arc(origin + flat * 1.2f, flat, hue, w.superRadius * 0.55f, w.superArcDeg, 0.22f, Vector3.up);
                    SlashFx.Sparks(origin + flat * 1.6f, flat, hue, 16, 10f, 40f);
                    break;

                case SuperKind.Flurry:
                {
                    // One small stab per hit, walked across the cone so the burst reads as many blows
                    // rather than one wide one.
                    float t = hits > 1 ? (float)index / (hits - 1) : 0.5f;
                    Vector3 fan = Quaternion.AngleAxis(Mathf.Lerp(-w.superArcDeg * 0.5f, w.superArcDeg * 0.5f, t), Vector3.up) * flat;
                    SlashFx.Beam(origin + flat * 0.4f, origin + fan * w.superRadius, hue, 0.045f, 0.12f);
                    SlashFx.Flare(origin + fan * w.superRadius * 0.9f, hue, 0.25f, 0.10f);
                    break;
                }

                case SuperKind.Quake:
                    // A ground shockwave: thick spokes out to the full radius, low to the floor but not
                    // coplanar with it, plus debris thrown straight up at the impact point.
                    Fan(transform.position + Vector3.up * 0.35f, flat, hue, 360f, w.superRadius, 14, 0.09f, 0.30f);
                    SlashFx.Flare(transform.position + flat * 1.4f + Vector3.up * 0.6f, hue, 0.8f, 0.22f);
                    SlashFx.Sparks(transform.position + flat * 1.2f + Vector3.up * 0.2f, Vector3.up, hue, 22, 9f, 70f);
                    break;

                case SuperKind.Nova:
                    // Same spokes, but thrown up and out of the horizontal plane so the detonation reads
                    // as a sphere rather than a puddle.
                    Fan(hub, flat, hue, 360f, w.superRadius, 16, 0.07f, 0.28f);
                    Fan(hub, flat, hue, 360f, w.superRadius * 0.7f, 8, 0.06f, 0.26f, 0.55f);
                    SlashFx.Flare(origin + flat * 1.5f, hue, 0.9f, 0.22f);
                    break;
            }
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
