using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// SKYFALL SUPLEX: V18's signature (2026-09-13 boss roster). A telegraphed two-handed grab. It is an
    /// ordinary blue, cued attack resolved by <see cref="PlayerCombat.ReceiveAttack"/>:
    /// <list type="bullet">
    /// <item><b>Perfect</b> — deflected like any blow (the brain recoils). No grab.</item>
    /// <item><b>Dodged / out of reach</b> — the brain's DoImpact never reaches the player. No grab.</item>
    /// <item><b>Blocked or Hit</b> — caught. V18 lifts off the floor carrying the player
    /// (<see cref="FirstPersonMotor.BeginCarry"/>; the motor still owns every velocity write), hangs at the
    /// top, then hurls the player down and outward onto the floor in front of him
    /// (<see cref="FirstPersonMotor.EndCarry"/>). The slam on landing is a second <see cref="AttackInfo"/>
    /// through <see cref="PlayerCombat.ReceiveAttack"/>, unblockable.</item>
    /// </list>
    /// The lift writes only <see cref="grabRoot"/> (inserted above SpinRoot by MiniBossFactory, the Judge's
    /// StormRoot pattern). Death, stagger or leaving Strike aborts: the player is dropped where they are.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class V18Grapple : MonoBehaviour
    {
        public enum GrabPhase { None, Lifting, Holding, Descending }

        [Header("Skyfall Suplex (rule 9: every value is written by MiniBossFactory)")]
        public string grabAttack = "BrawlerV18_Grab";
        [Tooltip("The landing slam's attack asset: damage, unblockable. Never scheduled by the brain.")]
        public EnemyAttackData slamAttack;
        [Tooltip("Written ONLY by this component: the body's lift during the suplex.")]
        public Transform grabRoot;
        public float maxGrabDistance = 3.2f;
        public float liftHeight = 11f;
        public float liftSeconds = 0.9f;
        public float holdSeconds = 0.35f;
        public float descendSeconds = 0.5f;
        [Tooltip("Metres per second straight DOWN the player leaves the hands at.")]
        public float throwDownSpeed = 14f;
        [Tooltip("Horizontal metres in front of V18's feet the throw aims to land the player.")]
        public float throwOutDistance = 6f;
        [Tooltip("Where the player's FEET hang under the hands, in V18's local frame on GrabRoot.")]
        public Vector3 holdLocal = new Vector3(0f, -0.1f, 1.15f);
        [Tooltip("Seconds after the throw the slam still waits for ground contact before giving up.")]
        public float slamTimeout = 3f;

        public GrabPhase Phase { get; private set; }
        public int Grabs { get; private set; }
        public int Throws { get; private set; }
        public int Slams { get; private set; }

        EnemyController controller;
        PlayerCombat player;
        FirstPersonMotor motor;
        Transform holdAnchor;
        float phaseAt, groundY, slamUntil = -1f;
        bool slamPending;

        /// <summary>Initial velocity that drops a body from <paramref name="from"/> onto height
        /// <paramref name="groundY"/> at horizontal <paramref name="target"/>, leaving at
        /// <paramref name="downSpeed"/> straight down under gravity magnitude <paramref name="g"/>. Pure.</summary>
        public static Vector3 ThrowVelocity(Vector3 from, Vector3 target, float groundY, float downSpeed, float g)
        {
            float h = Mathf.Max(0.1f, from.y - groundY);
            float vd = Mathf.Max(0f, downSpeed);
            g = Mathf.Max(0.1f, g);
            float t = (-vd + Mathf.Sqrt(vd * vd + 2f * g * h)) / g;
            Vector3 flat = target - from;
            flat.y = 0f;
            Vector3 horizontal = flat / Mathf.Max(0.05f, t);
            return new Vector3(horizontal.x, -vd, horizontal.z);
        }

        void Awake() { controller = GetComponent<EnemyController>(); }
        void OnEnable() { GameEvents.ParryResolved += OnParryResolved; GameEvents.PlayerRespawned += Abort; }
        void OnDisable() { GameEvents.ParryResolved -= OnParryResolved; GameEvents.PlayerRespawned -= Abort; Abort(); }

        void OnParryResolved(ParryResult result)
        {
            if (Phase != GrabPhase.None || controller == null || !controller.IsAlive) return;
            if (result != ParryResult.Hit && result != ParryResult.Blocked) return;
            var atk = controller.CurrentAttack;
            if (atk == null || atk.name != grabAttack || controller.Current != EnemyController.State.Strike) return;
            if (player == null) player = FindAnyObjectByType<PlayerCombat>();
            if (player == null || player.Health == null || player.Health.Current <= 0f) return;
            if ((player.transform.position - transform.position).sqrMagnitude > maxGrabDistance * maxGrabDistance) return;
            motor = player.GetComponent<FirstPersonMotor>();
            if (motor == null || grabRoot == null) return;
            BeginGrab();
        }

        void BeginGrab()
        {
            if (holdAnchor == null)
            {
                holdAnchor = new GameObject("GrabHold").transform;
                holdAnchor.SetParent(grabRoot, false);
            }
            holdAnchor.localPosition = holdLocal;
            groundY = transform.position.y;
            motor.BeginCarry(holdAnchor, Vector3.zero);
            Phase = GrabPhase.Lifting;
            phaseAt = Time.time;
            Grabs++;
            SlashFx.Ring(transform.position + Vector3.up * 0.05f, Vector3.up, new Color(0.7f, 0.65f, 0.6f, 0.6f), 2.2f, 0.4f);
            SlashFx.Sparks(transform.position, Vector3.up, new Color(0.8f, 0.75f, 0.7f, 1f), 14, 7f, 70f);
            if (CameraShake.I != null) CameraShake.I.Small();
            AudioManager.Play(Sfx.Dash, 1f, 0.7f);
        }

        void Update()
        {
            if (slamPending) WatchSlam();
            if (Phase == GrabPhase.None || grabRoot == null) return;

            bool brainHolds = controller != null && controller.IsAlive && !controller.IsStaggered
                           && controller.Current == EnemyController.State.Strike;
            if (!brainHolds && Phase != GrabPhase.Descending) { Abort(); return; }

            float t = Time.time - phaseAt;
            switch (Phase)
            {
                case GrabPhase.Lifting:
                {
                    float k = Mathf.Clamp01(t / Mathf.Max(0.05f, liftSeconds));
                    SetLift(liftHeight * (k * k * (3f - 2f * k)));
                    if (k >= 1f) { Phase = GrabPhase.Holding; phaseAt = Time.time; }
                    break;
                }
                case GrabPhase.Holding:
                    SetLift(liftHeight);
                    if (t >= holdSeconds) Throw();
                    break;
                case GrabPhase.Descending:
                {
                    float k = Mathf.Clamp01(t / Mathf.Max(0.05f, descendSeconds));
                    SetLift(liftHeight * (1f - k * k));
                    if (k >= 1f)
                    {
                        SetLift(0f);
                        Phase = GrabPhase.None;
                        SlashFx.Ring(transform.position + Vector3.up * 0.05f, Vector3.up, new Color(0.7f, 0.65f, 0.6f, 0.6f), 1.6f, 0.3f);
                        AudioManager.Play(Sfx.Land, 0.9f, 0.8f);
                    }
                    break;
                }
            }
        }

        void Throw()
        {
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            Vector3 target = transform.position + fwd.normalized * throwOutDistance;
            float g = motor != null ? Mathf.Abs(motor.gravity) : 30f;
            Vector3 v = ThrowVelocity(motor.transform.position, target, groundY, throwDownSpeed, g);
            motor.EndCarry(v);
            Throws++;
            slamPending = slamAttack != null;
            slamUntil = Time.time + slamTimeout;
            Phase = GrabPhase.Descending;
            phaseAt = Time.time;
            SlashFx.Sparks(holdAnchor.position, -fwd + Vector3.up, new Color(0.9f, 0.85f, 0.8f, 1f), 12, 8f, 60f);
            if (CameraFX.I != null) CameraFX.I.FovKick(10f);
            AudioManager.Play(Sfx.Dash, 1f, 1.1f);
        }

        void WatchSlam()
        {
            if (motor == null || player == null) { slamPending = false; return; }
            if (!motor.IsGrounded && Time.time < slamUntil) return;
            slamPending = false;
            if (!motor.IsGrounded || controller == null) return;
            Slams++;
            SlashFx.Ring(player.transform.position + Vector3.up * 0.05f, Vector3.up, new Color(0.9f, 0.5f, 0.3f, 0.8f), 2.4f, 0.35f);
            if (CameraShake.I != null) CameraShake.I.Big();
            player.ReceiveAttack(new AttackInfo
            {
                attack = slamAttack,
                attacker = controller,
                damage = slamAttack.damage,
                unblockable = slamAttack.unblockable
            });
        }

        void SetLift(float y)
        {
            Vector3 p = grabRoot.localPosition;
            p.y = y;
            grabRoot.localPosition = p;
        }

        void Abort()
        {
            if (motor != null && motor.IsCarried) motor.EndCarry(Vector3.zero);
            slamPending = false;
            if (grabRoot != null) SetLift(0f);
            Phase = GrabPhase.None;
        }
    }
}
