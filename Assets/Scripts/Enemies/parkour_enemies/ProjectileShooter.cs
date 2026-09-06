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
        static Material boltMat;

        /// <summary>Seconds of flight a bolt keeps beyond the cue lead when the launch has to slow for a near shot.</summary>
        public const float CueMargin = 0.08f;

        /// <summary>Bolts fired since spawn. Read by tests and the harness.</summary>
        public int Fired { get; private set; }

        void Awake()
        {
            ctrl = GetComponent<EnemyController>();
            nextFireAt = Time.time + 1f;   // never on the first frame of a spawn
        }

        void Update()
        {
            var data = ctrl != null ? ctrl.data : null;
            if (data == null || !data.shootsProjectiles || data.projectileAttack == null) return;
            if (!ctrl.IsAlive || ctrl.IsStaggered || ctrl.IsCommitted || ctrl.aggroLocked) return;
            if (ctrl.Current == EnemyController.State.Idle) return;
            if (Time.time < nextFireAt) return;

            if (combat == null) combat = FindAnyObjectByType<PlayerCombat>();
            if (combat == null) return;
            if (motor == null) motor = combat.GetComponent<FirstPersonMotor>();

            Vector3 muzzle = transform.position + Vector3.up * 1.3f;
            Vector3 chest = combat.transform.position + Vector3.up * 1.2f;
            float dist = Vector3.Distance(muzzle, chest);
            // The beat is HELD, not skipped, while the player is out of band or out of sight: the next
            // shot lands on the metronome the moment they are back (ProjectileMath.NextBeat).
            if (!ProjectileMath.InBand(dist, data.projectileMinRange, data.projectileMaxRange)) return;
            if (!EnemyController.HasLineOfSight(muzzle, combat.transform.position)) return;

            // Slow the launch inside the cue distance rather than going quiet: a bolt never arrives
            // before its own cue, and a sentry never stops shooting because you got close.
            float speed = ProjectileMath.LaunchSpeed(dist, data.projectileSpeed, Projectile.CueLead, CueMargin);
            // Lead a runner so the bolt meets them on the way through instead of crossing behind them.
            Vector3 vel = motor != null ? motor.Velocity : Vector3.zero;
            Vector3 target = ProjectileMath.LeadTarget(muzzle, chest, vel, speed, data.projectileLead);

            FireAt(muzzle, target, speed, data);
            nextFireAt = ProjectileMath.NextBeat(nextFireAt, Time.time, data.projectileInterval);
        }

        void FireAt(Vector3 muzzle, Vector3 target, float speed, EnemyData data)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Bolt";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.position = muzzle + (target - muzzle).normalized * 0.6f;
            go.transform.localScale = Vector3.one * Projectile.CoreSize;
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
            var r = go.GetComponent<Renderer>();
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
