using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// SKY VERDICT's launcher: the Seraph Lancer's signature. While the brain is in its Strike state for
    /// the verdict attack, this component throws <see cref="Javelin"/>s from the right hand -- the first
    /// on the brain's own impact frame (<see cref="EnemyController.NextImpactTime"/>, the first
    /// JavelinThrow clip's release) and the rest at fixed multiples of <see cref="javelinCadence"/>
    /// after it, one per release, each aimed at where the player's chest will be. The body is in the
    /// air the whole time (<c>SeraphLancerVisuals</c> lifts it; this class never touches a transform),
    /// so every javelin leaves an AIRBORNE muzzle and comes DOWN at the player: the lesson is "look up".
    ///
    /// <para><b>Every javelin is a <see cref="Projectile"/></b>: it resolves through
    /// <see cref="PlayerCombat.ReceiveAttack"/> carrying <c>EnemyData.projectileAttack</c> (hard rule 3),
    /// it can be Blocked or Perfect-parried, and a Perfect flies it back into the Lancer for
    /// <c>parriedProjectileDamage</c> / <c>parriedProjectilePosture</c> on the existing reflect path.
    /// Nothing here touches the player. Nothing here is unblockable: he is the teaching boss.</para>
    ///
    /// <para><b>One combat clock.</b> The release schedule is keyed to the brain's impact time, read once
    /// per verdict as <c>CinderJudgeStorm</c> reads it, and the visuals stage the throw clip off the same
    /// numbers (<see cref="javelinsPerVerdict"/>, <see cref="javelinCadence"/>) so the release frame and
    /// the launch agree by construction. Scaled time throughout: hitstop freezes the javelins with the
    /// world. Incoming javelins are recalled when the Lancer dies or is posture-broken (a reflected one
    /// still lands its punish), on player respawn, and when this component is disabled.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeraphLancerJavelins : MonoBehaviour
    {
        [Header("Sky Verdict (rule 9: every value is written by MiniBossFactory)")]
        [Tooltip("The verdict attack. Its brain contact is a no-op (range 0, cone 0); the javelins are the attack.")]
        public string skyVerdictAttack = "SeraphLancer_SkyVerdict";
        [Tooltip("Javelins per verdict, one per release, the first on the brain's impact.")]
        public int javelinsPerVerdict = 3;
        [Tooltip("Seconds between releases. The visuals stage one JavelinThrow per release on this same number.")]
        public float javelinCadence = 1.10f;
        [Tooltip("Javelins alive at once, any thrower. A release launches only if there is room under it.")]
        public int liveCap = 4;
        [Tooltip("Seconds a javelin lives, either way (Projectile.maxLife).")]
        public float javelinLifetime = 5f;
        [Tooltip("Shaft length, metres. The logical hit radius stays Projectile.hitRadius.")]
        public float javelinLength = 1.6f;
        [Tooltip("Shaft diameter, metres.")]
        public float shaftDiameter = 0.05f;
        [Tooltip("The hot tip's diameter, metres: the Projectile 'Core' that flares at the cue.")]
        public float tipSize = 0.30f;
        [Tooltip("Seconds over Projectile.CueLead the shortest flight must give (ProjectileMath.LaunchSpeed).")]
        public float launchMargin = 0.12f;
        [Tooltip("Bone the javelins leave from; falls back to muzzleFallbackHeight up the body.")]
        public string muzzleBone = "RightHand";
        public float muzzleFallbackHeight = 1.6f;
        [Tooltip("Seconds a javelin that struck the world stays planted (JavelinRelic).")]
        public float relicSeconds = 1.5f;
        public float relicHoldFraction = 0.7f;
        public float stuckVolume = 0.5f;
        public float stuckRange = 24f;
        [ColorUsage(true, true)] public Color tipColor = new Color(1.45f, 1.05f, 0.40f, 1f);
        public Color shaftColor = new Color(0.86f, 0.80f, 0.62f, 1f);

        /// <summary>This Lancer's javelins still alive.</summary>
        public int LiveOwned
        {
            get
            {
                int n = 0;
                for (int i = 0; i < owned.Count; i++) if (owned[i] != null && !owned[i].IsSpent) n++;
                return n;
            }
        }
        /// <summary>Verdicts whose impact frame came due. Tests and the harness.</summary>
        public int Verdicts { get; private set; }
        /// <summary>Releases whose frame came due, across every verdict.</summary>
        public int Throws { get; private set; }
        /// <summary>Releases so far in the current verdict (0 between verdicts).</summary>
        public int ThrowsThisVerdict { get; private set; }
        /// <summary>Javelins the last release actually launched (after the live cap): 0 or 1.</summary>
        public int LastLaunched { get; private set; }
        /// <summary>The brain's impact time for the current verdict, or MaxValue between verdicts.</summary>
        public float ImpactTime { get; private set; } = float.MaxValue;
        /// <summary>When the next release is due, or MaxValue.</summary>
        public float NextThrowAt { get; private set; } = float.MaxValue;
        /// <summary>Pitch, degrees below the horizontal, of the last javelin's launch line. Tests and the harness.</summary>
        public float LastPitchDeg { get; private set; }

        EnemyController controller;
        PlayerCombat playerCombat;
        FirstPersonMotor motor;
        Transform muzzle;
        readonly List<Javelin> owned = new List<Javelin>();
        bool armed;

        static Material tipMat, shaftMat;
        static Color tipMatColor;

        // ---------------------------------------------------------------- pure arithmetic (tests)

        /// <summary>How many of <paramref name="wanted"/> javelins may launch with <paramref name="liveNow"/> already alive under <paramref name="cap"/>.</summary>
        public static int LaunchCount(int wanted, int liveNow, int cap)
        {
            if (wanted <= 0) return 0;
            return Mathf.Clamp(Mathf.Min(wanted, cap - liveNow), 0, wanted);
        }

        /// <summary>When release <paramref name="index"/> (0-based) of a verdict is due: the impact plus whole cadences.</summary>
        public static float ThrowTime(float impactTime, int index, float cadence)
        {
            return impactTime + Mathf.Max(0, index) * Mathf.Max(0.05f, cadence);
        }

        /// <summary>
        /// The speed a javelin leaves at for a straight path of <paramref name="pathLength"/> metres: the
        /// data speed, slowed near in so the flight is never shorter than the cue lead plus the margin --
        /// the sentries' own rule.
        /// </summary>
        public static float PathSpeed(float pathLength, float speed, float cueLead, float margin)
        {
            return ProjectileMath.LaunchSpeed(pathLength, speed, cueLead, margin);
        }

        /// <summary>Degrees below the horizontal a line from <paramref name="from"/> to <paramref name="to"/> pitches (negative = upward).</summary>
        public static float PitchDeg(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float flat = new Vector2(d.x, d.z).magnitude;
            return Mathf.Atan2(-d.y, Mathf.Max(0.0001f, flat)) * Mathf.Rad2Deg;
        }

        // ---------------------------------------------------------------- lifecycle

        void Awake()
        {
            controller = GetComponent<EnemyController>();
        }

        void OnEnable()
        {
            GameEvents.PlayerRespawned += RecallAll;
        }

        void OnDisable()
        {
            GameEvents.PlayerRespawned -= RecallAll;
            RecallAll();
            Disarm();
        }

        void Update()
        {
            if (controller == null || Time.timeScale <= 0f) return;
            Prune();

            if (!controller.IsAlive || controller.IsStaggered)
            {
                RecallIncoming();
                Disarm();
                return;
            }

            var atk = controller.CurrentAttack;
            bool verdict = controller.Current == EnemyController.State.Strike
                        && atk != null && atk.name == skyVerdictAttack;
            if (!verdict) { Disarm(); return; }

            if (!armed)
            {
                armed = true;
                ThrowsThisVerdict = 0;
                Verdicts++;
                // The brain publishes the release on its first Strike frame (struck is still false).
                float impact = controller.NextImpactTime;
                ImpactTime = impact < float.MaxValue ? impact : Time.time;
                NextThrowAt = ImpactTime;
            }
            // One release per frame at most: the schedule is anchored to the impact, so a dropped frame
            // delays a release by that frame and never bunches two into one (a throw is a clip, not a tick).
            if (ThrowsThisVerdict < javelinsPerVerdict && Time.time >= NextThrowAt)
            {
                Release();
                ThrowsThisVerdict++;
                NextThrowAt = ThrowsThisVerdict < javelinsPerVerdict
                    ? ThrowTime(ImpactTime, ThrowsThisVerdict, javelinCadence)
                    : float.MaxValue;
            }
        }

        void Disarm()
        {
            armed = false;
            ThrowsThisVerdict = 0;
            ImpactTime = float.MaxValue;
            NextThrowAt = float.MaxValue;
        }

        void Prune()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] == null || owned[i].IsSpent) owned.RemoveAt(i);
        }

        /// <summary>Javelins still coming at the player vanish (death, posture break); a reflected one still lands.</summary>
        public void RecallIncoming()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                var j = owned[i];
                if (j == null) { owned.RemoveAt(i); continue; }
                if (j.IsIncoming) { Destroy(j.gameObject); owned.RemoveAt(i); }
            }
        }

        /// <summary>Every javelin this Lancer threw is gone (player respawn, component disabled).</summary>
        public void RecallAll()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                var j = owned[i];
                if (j != null) Destroy(j.gameObject);
            }
            owned.Clear();
        }

        // ---------------------------------------------------------------- the release

        void Release()
        {
            Throws++;
            int count = LaunchCount(1, Javelin.LiveCount, liveCap);
            LastLaunched = count;
            if (count <= 0) return;

            EnsurePlayer();
            if (playerCombat == null || controller.data == null) { LastLaunched = 0; return; }

            Vector3 from = MuzzlePoint();
            Vector3 chest = playerCombat.transform.position + Vector3.up * 1.2f;
            Vector3 velocity = motor != null
                ? ProjectileMath.GroundAwareTargetVelocity(motor.Velocity, motor.IsGrounded, motor.GroundNormal)
                : Vector3.zero;
            float speed = controller.data.projectileSpeed;
            float lead = controller.data.projectileLead;

            Vector3 aim = ProjectileMath.LeadTarget(from, chest, velocity, speed, lead);
            Vector3 dir = aim - from;
            float pathLength = dir.magnitude;
            float launch = PathSpeed(pathLength, speed, Projectile.CueLead, launchMargin);
            LastPitchDeg = PitchDeg(from, aim);
            SpawnJavelin(from, dir, launch, pathLength / Mathf.Max(0.01f, launch));

            // The release: a hand's worth of gold sparks down the line and the light swing. Under the
            // bloom budget; the javelin's own cue flare is the bright thing, 0.28 s before it lands.
            SlashFx.Sparks(from, dir.normalized, tipColor, 8, 5f, 40f);
            AudioManager.Play(Sfx.SwingLight, 0.7f, 1.2f, 0.05f);
        }

        void SpawnJavelin(Vector3 from, Vector3 dir, float speed, float contactSeconds)
        {
            EnsureMaterials();

            var go = new GameObject("Javelin");
            go.transform.position = from;

            // The TIP is the Projectile's "Core": Projectile flares and whitens it at the cue and the
            // trail is built from its material, so the javelin speaks the bolt's cue language in gold.
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            StripCollider(core);
            core.transform.SetParent(go.transform, false);
            core.transform.localScale = Vector3.one * tipSize;
            core.transform.rotation = Javelin.Heading(dir);
            var tipR = core.GetComponent<Renderer>();
            tipR.sharedMaterial = tipMat;
            tipR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tipR.receiveShadows = false;

            // The shaft trails behind the tip along the core's +Z. Local units are the core's (tipSize),
            // and Javelin counter-scales it every frame so only the tip flares at the cue.
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            StripCollider(shaft);
            shaft.transform.SetParent(core.transform, false);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // cylinder Y -> +Z
            float u = 1f / Mathf.Max(0.01f, tipSize);
            shaft.transform.localPosition = new Vector3(0f, 0f, (-javelinLength * 0.5f + tipSize * 0.3f) * u);
            shaft.transform.localScale = new Vector3(shaftDiameter * u, javelinLength * 0.5f * u, shaftDiameter * u);
            var shaftR = shaft.GetComponent<Renderer>();
            shaftR.sharedMaterial = shaftMat;
            shaftR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shaftR.receiveShadows = false;

            var javelin = go.AddComponent<Javelin>();
            javelin.maxLife = javelinLifetime;
            javelin.relicSeconds = relicSeconds;
            javelin.relicHoldFraction = relicHoldFraction;
            javelin.stuckVolume = stuckVolume;
            javelin.stuckRange = stuckRange;
            javelin.tipColor = tipColor;
            javelin.relicShaftMaterial = shaftMat;
            javelin.relicTipMaterial = tipMat;
            javelin.listener = playerCombat != null ? playerCombat.transform : null;
            javelin.Fire(controller, controller.data, dir, speed, playerCombat, contactSeconds);
            owned.Add(javelin);
        }

        static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        }

        void EnsureMaterials()
        {
            if (tipMat == null || tipMatColor != tipColor)
            {
                tipMat = SlashFx.CreateAdditiveMaterial(tipColor);
                // CreateAdditiveMaterial normalises to a 1.0 peak; the tip is meant to bloom (1.45 peak:
                // over the 1.05 threshold, above the Dancer's 1.25 disc rim because this is the roster's
                // FIRST projectile lesson, under the sentry bolt's 1.6, which stays the brightest thing).
                if (tipMat.HasProperty("_BaseColor")) tipMat.SetColor("_BaseColor", tipColor);
                if (tipMat.HasProperty("_Color")) tipMat.SetColor("_Color", tipColor);
                tipMatColor = tipColor;
            }
            if (shaftMat == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                shaftMat = lit != null ? new Material(lit) : new Material(tipMat);
                shaftMat.name = "SeraphLancerJavelinShaft";
                if (shaftMat.HasProperty("_BaseColor")) shaftMat.SetColor("_BaseColor", shaftColor);
                if (shaftMat.HasProperty("_Color")) shaftMat.SetColor("_Color", shaftColor);
                if (shaftMat.HasProperty("_Smoothness")) shaftMat.SetFloat("_Smoothness", 0.6f);
                if (shaftMat.HasProperty("_Metallic")) shaftMat.SetFloat("_Metallic", 0.7f);
            }
        }

        Vector3 MuzzlePoint()
        {
            if (muzzle == null && !string.IsNullOrEmpty(muzzleBone))
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.name == muzzleBone) { muzzle = t; break; }
            }
            if (muzzle != null) return muzzle.position;
            return controller.BodyPoint(muzzleFallbackHeight);
        }

        void EnsurePlayer()
        {
            if (playerCombat == null) playerCombat = FindAnyObjectByType<PlayerCombat>();
            if (playerCombat != null && motor == null) motor = playerCombat.GetComponent<FirstPersonMotor>();
        }
    }
}
