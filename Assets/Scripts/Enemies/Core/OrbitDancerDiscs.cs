using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// ORBIT STORM's launcher: the Orbit Dancer's signature. While the brain is in its Strike state for
    /// one of the two throw attacks, this component waits for the brain's own impact frame (the clip's
    /// release, <see cref="EnemyController.NextImpactTime"/>) and throws <see cref="BouncingDisc"/>s from
    /// the right hand: the volley (<see cref="discThrowAttack"/>) is one disc straight at the player and
    /// two BANKED off the nearest walls to either side; the whirl (<see cref="spinThrowAttack"/>) is a
    /// real 360 melee contact the brain lands itself, plus two banked discs on the release.
    ///
    /// <para><b>Every disc is a <see cref="Projectile"/></b>: it resolves through
    /// <see cref="PlayerCombat.ReceiveAttack"/> carrying <c>EnemyData.projectileAttack</c> (hard rule 3),
    /// it can be Blocked or Perfect-parried, and a Perfect flies it back into the Dancer for
    /// <c>parriedProjectileDamage</c> / <c>parriedProjectilePosture</c> on the existing reflect path.
    /// Nothing here touches the player.</para>
    ///
    /// <para><b>The bank shot is a mirror.</b> A horizontal probe ray to the side finds a WALL (a normal
    /// with little vertical component); the player's chest is mirrored across that wall's plane and the
    /// disc is aimed at the mirror image, so its first bounce brings it back through the player from the
    /// side. A side with no wall inside <see cref="bankRange"/> falls back to a plain fan shot at
    /// <see cref="fanDeg"/>, which flies off and expires: honest, never a curve.</para>
    ///
    /// <para><b>One combat clock.</b> The launch is keyed to the brain's impact time, read once per throw
    /// as <c>CinderJudgeStorm</c> reads it; the disc flight is then the bolt's own clock. Scaled time
    /// throughout: hitstop freezes the discs with the world. Discs are recalled when the Dancer dies or
    /// is posture-broken (incoming ones only: a reflected disc still lands its punish), on player
    /// respawn, and when this component is disabled.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OrbitDancerDiscs : MonoBehaviour
    {
        [Header("Orbit Storm (rule 9: every value is written by MiniBossFactory)")]
        [Tooltip("The volley attack. Its brain contact is a no-op (range 0, cone 0); the discs are the attack.")]
        public string discThrowAttack = "OrbitDancer_DiscThrow";
        [Tooltip("The close whirl: the brain lands a 360 contact and the release banks discs.")]
        public string spinThrowAttack = "OrbitDancer_SpinThrow";
        [Tooltip("Discs per volley: one direct, the rest banked left/right alternately.")]
        public int volleyCount = 3;
        [Tooltip("Discs per whirl release: all banked, no direct one (you are already in her reach).")]
        public int whirlCount = 2;
        [Tooltip("Discs alive at once, any thrower. A throw launches only up to the room left under it.")]
        public int liveCap = 4;
        [Tooltip("Walls a disc may reflect off; the next one shatters it.")]
        public int maxBounces = 3;
        [Tooltip("Seconds a disc lives, bounces or not (Projectile.maxLife).")]
        public float discLifetime = 4f;
        [Tooltip("Visual spin of the disc mesh, degrees per second.")]
        public float spinDegPerSec = 900f;
        [Tooltip("Disc mesh diameter, metres. The logical hit radius stays Projectile.hitRadius.")]
        public float discDiameter = 0.60f;
        [Tooltip("Seconds over Projectile.CueLead the shortest flight must give (ProjectileMath.LaunchSpeed).")]
        public float launchMargin = 0.12f;
        [Tooltip("Fallback fan half-angle, degrees, when a side has no wall to bank off.")]
        public float fanDeg = 30f;
        [Tooltip("How far the bank probe looks for a wall, metres.")]
        public float bankRange = 20f;
        [Tooltip("Probe yaws off the player bearing, tried in order, degrees. The first wall wins.")]
        public float[] bankProbeDeg = { 40f, 65f, 90f };
        [Tooltip("Largest |normal.y| that still counts as a WALL. A floor or ceiling is never a bank.")]
        public float maxBankIncidenceY = 0.35f;
        [Tooltip("Bone the discs leave from; falls back to muzzleFallbackHeight up the body.")]
        public string muzzleBone = "RightHand";
        public float muzzleFallbackHeight = 1.1f;
        public float pingVolume = 0.55f;
        public float pingRange = 24f;
        [ColorUsage(true, true)] public Color rimColor = new Color(0.36f, 1.25f, 1.15f, 1f);

        /// <summary>This Dancer's discs still alive.</summary>
        public int LiveOwned
        {
            get
            {
                int n = 0;
                for (int i = 0; i < owned.Count; i++) if (owned[i] != null && !owned[i].IsSpent) n++;
                return n;
            }
        }
        /// <summary>Throws whose release frame came due. Tests and the harness.</summary>
        public int Throws { get; private set; }
        /// <summary>Discs the last release actually launched (after the live cap).</summary>
        public int LastLaunched { get; private set; }
        /// <summary>The brain's impact time for the current throw, or MaxValue between throws.</summary>
        public float ImpactTime { get; private set; } = float.MaxValue;
        /// <summary>Bank shots found on the last release (0..count): how many discs will come back off a wall.</summary>
        public int LastBanked { get; private set; }

        EnemyController controller;
        PlayerCombat playerCombat;
        FirstPersonMotor motor;
        Transform muzzle;
        readonly List<BouncingDisc> owned = new List<BouncingDisc>();
        bool armed, fired;

        static Material rimMat, bladeMat;
        static Color rimMatColor;

        // ---------------------------------------------------------------- pure arithmetic (tests)

        /// <summary>How many of <paramref name="wanted"/> discs may launch with <paramref name="liveNow"/> already alive under <paramref name="cap"/>.</summary>
        public static int LaunchCount(int wanted, int liveNow, int cap)
        {
            if (wanted <= 0) return 0;
            return Mathf.Clamp(Mathf.Min(wanted, cap - liveNow), 0, wanted);
        }

        /// <summary>The mirror image of <paramref name="point"/> across the plane through <paramref name="planePoint"/> with <paramref name="normal"/>.</summary>
        public static Vector3 MirrorAcrossPlane(Vector3 point, Vector3 planePoint, Vector3 normal)
        {
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            float d = Vector3.Dot(point - planePoint, n);
            return point - 2f * d * n;
        }

        /// <summary>A surface is a bankable WALL when its normal is nearly horizontal.</summary>
        public static bool IsBankable(Vector3 normal, float maxIncidenceY)
        {
            if (normal.sqrMagnitude < 1e-6f) return false;
            return Mathf.Abs(normal.normalized.y) <= maxIncidenceY;
        }

        /// <summary>
        /// The fallback fan: slot 0 is dead centre, odd slots swing +fan, +2 fan..., even slots the mirror.
        /// Yaw only; the vertical component of the bearing is kept so the disc still flies at chest height.
        /// </summary>
        public static Vector3 FanDirection(Vector3 toTarget, int slot, float fanDeg)
        {
            Vector3 d = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.forward;
            if (slot <= 0) return d;
            int step = (slot + 1) / 2;
            float sign = (slot % 2 == 1) ? 1f : -1f;
            return (Quaternion.Euler(0f, sign * step * fanDeg, 0f) * d).normalized;
        }

        /// <summary>
        /// The speed a disc leaves at for a path of <paramref name="pathLength"/> metres (muzzle to
        /// wall to player, or muzzle to player): the data speed, slowed near in so the flight is never
        /// shorter than the cue lead plus the margin -- the sentries' own rule.
        /// </summary>
        public static float PathSpeed(float pathLength, float speed, float cueLead, float margin)
        {
            return ProjectileMath.LaunchSpeed(pathLength, speed, cueLead, margin);
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
            bool throwing = controller.Current == EnemyController.State.Strike
                         && atk != null && IsThrow(atk.name);
            if (!throwing) { Disarm(); return; }

            if (!armed)
            {
                armed = true;
                fired = false;
                // The brain publishes the release on its first Strike frame (struck is still false).
                float impact = controller.NextImpactTime;
                ImpactTime = impact < float.MaxValue ? impact : Time.time;
            }
            if (!fired && Time.time >= ImpactTime)
            {
                fired = true;
                bool whirl = atk.name == spinThrowAttack;
                Release(whirl ? whirlCount : volleyCount, whirl);
            }
        }

        public bool IsThrow(string attackName)
        {
            return attackName == discThrowAttack || attackName == spinThrowAttack;
        }

        void Disarm()
        {
            armed = false;
            fired = false;
            ImpactTime = float.MaxValue;
        }

        void Prune()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] == null || owned[i].IsSpent) owned.RemoveAt(i);
        }

        /// <summary>Discs still coming at the player vanish (death, posture break); a reflected one still lands.</summary>
        public void RecallIncoming()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                var d = owned[i];
                if (d == null) { owned.RemoveAt(i); continue; }
                if (d.IsIncoming) { Destroy(d.gameObject); owned.RemoveAt(i); }
            }
        }

        /// <summary>Every disc this Dancer threw is gone (player respawn, component disabled).</summary>
        public void RecallAll()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                var d = owned[i];
                if (d != null) Destroy(d.gameObject);
            }
            owned.Clear();
        }

        // ---------------------------------------------------------------- the release

        void Release(int wanted, bool whirl)
        {
            Throws++;
            int count = LaunchCount(wanted, BouncingDisc.LiveCount, liveCap);
            LastLaunched = count;
            LastBanked = 0;
            if (count <= 0) return;

            EnsurePlayer();
            if (playerCombat == null || controller.data == null) return;

            Vector3 from = MuzzlePoint();
            Vector3 chest = playerCombat.transform.position + Vector3.up * 1.2f;
            Vector3 velocity = motor != null
                ? ProjectileMath.GroundAwareTargetVelocity(motor.Velocity, motor.IsGrounded, motor.GroundNormal)
                : Vector3.zero;
            int mask = motor != null
                ? motor.WorldMask
                : ~((1 << Layers.Player) | (1 << Layers.Enemy) | (1 << Layers.Interactable));
            float speed = controller.data.projectileSpeed;
            float lead = controller.data.projectileLead;

            for (int i = 0; i < count; i++)
            {
                // The whirl has no direct disc: you are already inside her reach, the whirl itself is the
                // contact, and both discs go out to the walls to come back from the sides.
                int slot = whirl ? i + 1 : i;
                Vector3 dir;
                float pathLength;
                if (slot == 0)
                {
                    Vector3 aim = ProjectileMath.LeadTarget(from, chest, velocity, speed, lead);
                    dir = aim - from;
                    pathLength = dir.magnitude;
                }
                else
                {
                    float side = (slot % 2 == 1) ? 1f : -1f;
                    if (TryBank(from, chest, side, mask, out dir, out pathLength))
                    {
                        LastBanked++;
                    }
                    else
                    {
                        dir = FanDirection(chest - from, slot, fanDeg);
                        pathLength = Vector3.Distance(from, chest) + bankRange;   // full speed: it is leaving
                    }
                }
                float launch = PathSpeed(pathLength, speed, Projectile.CueLead, launchMargin);
                SpawnDisc(from, dir, launch, pathLength / Mathf.Max(0.01f, launch));
            }

            // The release: a hand's worth of teal sparks and the light swing. Under the bloom budget;
            // the disc's own cue flare is the bright thing, 0.28 s before it lands.
            SlashFx.Sparks(from, controller.transform.forward + Vector3.up * 0.2f, rimColor, 8, 5f, 50f);
            AudioManager.Play(Sfx.SwingLight, 0.7f, 1.3f, 0.05f);
        }

        /// <summary>
        /// Find a wall to <paramref name="side"/> (+1 right, -1 left of the bearing to the chest) and aim
        /// so the first bounce comes back through the chest. The mirror aim is re-probed so the disc is
        /// known to reach THAT wall (a finite wall can be missed by a mirror computed on its plane).
        /// </summary>
        bool TryBank(Vector3 from, Vector3 chest, float side, int mask, out Vector3 dir, out float pathLength)
        {
            dir = Vector3.zero;
            pathLength = 0f;
            Vector3 bearing = chest - from;
            bearing.y = 0f;
            if (bearing.sqrMagnitude < 1e-4f) bearing = controller.transform.forward;
            bearing.Normalize();
            if (bankProbeDeg == null) return false;

            for (int p = 0; p < bankProbeDeg.Length; p++)
            {
                Vector3 probe = Quaternion.Euler(0f, side * bankProbeDeg[p], 0f) * bearing;
                RaycastHit hit;
                if (!Physics.Raycast(from, probe, out hit, bankRange, mask, QueryTriggerInteraction.Ignore)) continue;
                if (!IsBankable(hit.normal, maxBankIncidenceY)) continue;

                Vector3 mirror = MirrorAcrossPlane(chest, hit.point, hit.normal);
                Vector3 aim = mirror - from;
                if (aim.sqrMagnitude < 1e-4f) continue;
                RaycastHit confirm;
                if (!Physics.Raycast(from, aim.normalized, out confirm, bankRange, mask, QueryTriggerInteraction.Ignore)) continue;
                if (confirm.collider != hit.collider || !IsBankable(confirm.normal, maxBankIncidenceY)) continue;

                dir = aim.normalized;
                pathLength = Vector3.Distance(from, confirm.point) + Vector3.Distance(confirm.point, chest);
                return true;
            }
            return false;
        }

        void SpawnDisc(Vector3 from, Vector3 dir, float speed, float contactSeconds)
        {
            EnsureMaterials();

            var go = new GameObject("Disc");
            go.transform.position = from;

            // The rim IS the Projectile's "Core": Projectile flares and whitens it at the cue and the trail
            // is built from its material, so the disc speaks the bolt's cue language in teal. A dark
            // obsidian blade sits inside it, so at rest it reads as a black disc with a lit edge.
            var core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            core.name = "Core";
            StripCollider(core);
            core.transform.SetParent(go.transform, false);
            core.transform.localScale = new Vector3(discDiameter, discDiameter * 0.02f, discDiameter);
            var rimR = core.GetComponent<Renderer>();
            rimR.sharedMaterial = rimMat;
            rimR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rimR.receiveShadows = false;

            var blade = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            blade.name = "Blade";
            StripCollider(blade);
            blade.transform.SetParent(core.transform, false);
            blade.transform.localScale = new Vector3(0.84f, 2.6f, 0.84f);   // narrower, thicker: the rim shows
            var bladeR = blade.GetComponent<Renderer>();
            bladeR.sharedMaterial = bladeMat;
            bladeR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bladeR.receiveShadows = false;

            var disc = go.AddComponent<BouncingDisc>();
            disc.maxBounces = maxBounces;
            disc.spinDegPerSec = spinDegPerSec;
            disc.maxLife = discLifetime;
            disc.rimColor = rimColor;
            disc.pingVolume = pingVolume;
            disc.pingRange = pingRange;
            disc.listener = playerCombat != null ? playerCombat.transform : null;
            disc.Fire(controller, controller.data, dir, speed, playerCombat, contactSeconds);
            owned.Add(disc);
        }

        static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        }

        void EnsureMaterials()
        {
            if (rimMat == null || rimMatColor != rimColor)
            {
                rimMat = SlashFx.CreateAdditiveMaterial(rimColor);
                // CreateAdditiveMaterial normalises to a 1.0 peak; the rim is meant to bloom softly
                // (1.25 peak: over the 1.05 threshold, well under the sentry bolt's 1.6, which stays the
                // brightest thing in the game).
                if (rimMat.HasProperty("_BaseColor")) rimMat.SetColor("_BaseColor", rimColor);
                if (rimMat.HasProperty("_Color")) rimMat.SetColor("_Color", rimColor);
                rimMatColor = rimColor;
            }
            if (bladeMat == null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                bladeMat = lit != null ? new Material(lit) : new Material(rimMat);
                bladeMat.name = "OrbitDancerDiscBlade";
                var obsidian = new Color(0.06f, 0.06f, 0.08f, 1f);
                if (bladeMat.HasProperty("_BaseColor")) bladeMat.SetColor("_BaseColor", obsidian);
                if (bladeMat.HasProperty("_Color")) bladeMat.SetColor("_Color", obsidian);
                if (bladeMat.HasProperty("_Smoothness")) bladeMat.SetFloat("_Smoothness", 0.65f);
                if (bladeMat.HasProperty("_Metallic")) bladeMat.SetFloat("_Metallic", 0.6f);
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
