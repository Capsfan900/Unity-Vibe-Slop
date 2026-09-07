using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Makes an enemy a RANGED presence on a parkour span: while it is awake, not committed to a melee
    /// attack and the player is inside its band with a clear line, it fires a <see cref="Projectile"/>
    /// on a METRONOME of <c>EnemyData.projectileInterval</c> seconds -- a fixed beat, held (not
    /// reset) while the line is blocked, so the rhythm is something a runner can learn. Each shot leads
    /// the player's velocity (<c>projectileLead</c>) and slows its launch inside the cue distance so a
    /// bolt never arrives before its cue (<see cref="ProjectileMath.LaunchSpeed"/>). Sentries
    /// (<c>EnemyData.rangedOnly</c>) never melee, so this is their whole offence. Everything it does is read off
    /// <see cref="EnemyData"/> (rule 9); the component itself carries no tuning. Enemies whose data says
    /// <c>shootsProjectiles = false</c> keep the component and never fire, so one prefab serves both.
    ///
    /// <para>It does not touch the brain: <see cref="EnemyController"/> still chases, commits and
    /// recovers as before. The shooter only reads its state (never inside a wind-up or a strike -- one
    /// attack at a time, so the tell is never ambiguous) and adds a bolt on top.</para>
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class ProjectileShooter : MonoBehaviour
    {
        EnemyController ctrl;
        PlayerCombat combat;
        FirstPersonMotor motor;
        float nextFireAt;
        bool acquired;
        static Material boltMat;

        /// <summary>
        /// Seconds of flight a bolt keeps beyond the cue lead when the launch has to slow for a near shot.
        /// F5 of the bolt-timing plan (2026-09-06): 0.08 -> 0.16, so a near bolt shows ~0.44 s of flight
        /// instead of 0.36. The CUE LEAD itself stays a flat 0.28 s -- the plan rejects a speed-scaled cue,
        /// because that lead is a contract shared with every melee attack and making it elastic gives the
        /// loudest signal in the game a variable meaning. Lengthen the FLIGHT, never the promise.
        /// </summary>
        public const float CueMargin = 0.16f;

        /// <summary>Seconds after Awake before a sentry's very first beat can land, whatever the epoch says.</summary>
        public const float FirstBeatDelay = 1f;

        // F4, ONE BEAT PER SPAN: every sentry in a level shares one epoch, and alternate sentries sit a
        // HALF interval off it, so two perches covering one crest never argue in unison. Statics do not
        // survive a domain reload with their world, so both are re-seeded on load.
        static float spanEpoch;
        static bool spanEpochSet;
        static int spanIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetSpanBeat() { spanEpochSet = false; spanIndex = 0; }

        /// <summary>Bolts fired since spawn. Read by tests and the harness.</summary>
        public int Fired { get; private set; }

        void Awake()
        {
            ctrl = GetComponent<EnemyController>();
            if (!spanEpochSet) { spanEpoch = Time.time; spanEpochSet = true; }
            var spawnData = ctrl != null ? ctrl.data : null;
            float interval = spawnData != null ? spawnData.projectileInterval : 1.6f;
            float offset = (spanIndex++ % 2) * interval * 0.5f;
            // Never on the first frame of a spawn, and on the SPAN's grid rather than this body's (F4).
            nextFireAt = ProjectileMath.FirstBeat(spanEpoch, Time.time, interval, offset, FirstBeatDelay);
        }

        void Update()
        {
            var data = ctrl != null ? ctrl.data : null;
            if (data == null || !data.shootsProjectiles || data.projectileAttack == null) return;
            if (!ctrl.IsAlive || ctrl.Current == EnemyController.State.Idle) { acquired = false; return; }
            if (ctrl.IsStaggered || ctrl.IsCommitted || ctrl.aggroLocked) return;

            if (combat == null) combat = FindAnyObjectByType<PlayerCombat>();
            if (combat == null) return;
            if (motor == null) motor = combat.GetComponent<FirstPersonMotor>();

            Vector3 muzzle = transform.position + Vector3.up * 1.3f;
            Vector3 chest = combat.transform.position + Vector3.up * 1.2f;
            float dist = Vector3.Distance(muzzle, chest);
            // The beat is HELD, not skipped, while the player is out of band or out of sight: the next
            // shot lands on the metronome the moment they are back (ProjectileMath.NextBeat).
            bool inBand = ProjectileMath.InBand(dist, data.projectileMinRange, data.projectileMaxRange)
                          && EnemyController.HasLineOfSight(muzzle, combat.transform.position);
            if (!inBand) { acquired = false; return; }

            // F1, THE ARM-UP (bolt-timing plan 2026-09-06). A held beat that had gone stale used to fire on
            // the FIRST FRAME the line cleared -- the frame you crest a ledge or land, and two stale perches
            // covering one crest fired together. On the transition into band the beat is pushed at least
            // projectileAcquireDelay out: the sentry takes a breath, and its first shot arrives once you are
            // back on the ground and looking. This is why the band and sight checks now run every frame
            // instead of behind the beat test.
            if (!acquired)
            {
                acquired = true;
                nextFireAt = ProjectileMath.AcquireBeat(nextFireAt, Time.time, data.projectileInterval,
                                                        data.projectileAcquireDelay);
            }
            if (Time.time < nextFireAt) return;

            // Slow the launch inside the cue distance rather than going quiet: a bolt never arrives
            // before its own cue, and a sentry never stops shooting because you got close.
            float speed = ProjectileMath.LaunchSpeed(dist, data.projectileSpeed, Projectile.CueLead, CueMargin);
            // Lead a runner so the bolt meets them on the way through instead of crossing behind them.
            Vector3 vel = motor != null ? motor.Velocity : Vector3.zero;

            // F3: a perch covers a STRETCH, not a fleeing back. A bolt that would arrive from outside the
            // parry cone at a receding runner is answered by ParryMath.Evaluate with Hit before timing is
            // even considered -- not hard, impossible. Refuse to LAUNCH; the beat still advances, so the
            // skipped shot is never repaid as a burst and the sentry never reads the player's state machine.
            float cone = GameManager.I != null && GameManager.I.statsData != null
                       ? GameManager.I.statsData.facingConeDeg : 75f;
            if (!ProjectileMath.ArrivesInFront(muzzle, chest, vel, speed, cone))
            {
                nextFireAt = ProjectileMath.NextBeat(nextFireAt, Time.time, data.projectileInterval);
                return;
            }

            Vector3 target = ProjectileMath.LeadTarget(muzzle, chest, vel, speed, data.projectileLead);

            FireAt(muzzle, target, speed, data);
            nextFireAt = ProjectileMath.NextBeat(nextFireAt, Time.time, data.projectileInterval);
        }

        void FireAt(Vector3 muzzle, Vector3 target, float speed, EnemyData data)
        {
            // The root is the combat path and never leaves it. The sphere is a child because Projectile gives
            // only that visible core a small pre-cue weave; collision, cue timing and arrival keep reading root.
            var go = new GameObject("Bolt");
            go.transform.position = muzzle + (target - muzzle).normalized * 0.6f;

            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            var col = core.GetComponent<Collider>();
            if (col != null) Destroy(col);
            core.transform.SetParent(go.transform, false);
            core.transform.localScale = Vector3.one * Projectile.CoreSize;
            if (boltMat == null)
            {
                // SlashFx normalises every additive material to a peak of 1.0 -- the project's bloom
                // discipline -- and the bolt is the one documented exception (Projectile.HotCore): the
                // colour is written back OVER the normalised one, so the material glows at 1.6 while
                // every spark and streak SlashFx makes stays under the cap.
                boltMat = SlashFx.CreateAdditiveMaterial(new Color(1f, 0.55f, 0.2f, 1f));
                if (boltMat.HasProperty("_BaseColor")) boltMat.SetColor("_BaseColor", Projectile.HotCore);
                if (boltMat.HasProperty("_Color")) boltMat.SetColor("_Color", Projectile.HotCore);
            }
            var r = core.GetComponent<Renderer>();
            r.sharedMaterial = boltMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var p = go.AddComponent<Projectile>();
            p.Fire(ctrl, data, target - muzzle, speed, combat);
            Fired++;
            AudioManager.Play(Sfx.Tick, 0.5f, 1.3f, 0.05f);
        }
    }
}
