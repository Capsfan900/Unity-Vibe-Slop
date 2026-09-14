using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// AEGIS OF JUDGEMENT: the Cinder Judge's magic shield (2026-09-13 boss roster). While the brain runs
    /// the shield-raise attack (and the wind-up of the bash that follows it) a translucent ember dome stands
    /// in front of him, and every player melee blow into it is REFUSED through
    /// <see cref="IPlayerHitDeflector"/>: nothing lands, the player recoils and pays guard posture.
    ///
    /// <para><b>How it breaks.</b> A Perfect parry of the shield BASH, or a thrown blade (Blade Throw)
    /// striking the dome, shatters it: the Judge takes a chunk of posture and is thrown into Recover through
    /// his own <see cref="EnemyController.OnParried"/> — the punish window. Executes and wand ripostes never
    /// ask the shield (see <see cref="PlayerHitDeflection"/>), so an opened Judge is always finishable.</para>
    ///
    /// <para>The active test is the brain's own state, like <see cref="CinderJudgeStorm"/>: death, stagger or
    /// an execute take the brain out of the shield attacks and the dome drops on the next frame.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CinderJudgeShield : MonoBehaviour, IPlayerHitDeflector
    {
        [Header("Aegis of Judgement (rule 9: every value is written by MiniBossFactory)")]
        public string raiseAttack = "CinderJudge_ShieldRaise";
        public string bashAttack = "CinderJudge_ShieldBash";
        [Tooltip("Guard posture the player pays for swinging into the raised shield.")]
        public float recoilPosture = 18f;
        [Tooltip("Fraction of the Judge's max posture taken when the shield shatters.")]
        public float shatterPostureFraction = 0.35f;
        [Tooltip("Seconds after a shatter during which the shield cannot deflect, even if the brain is still in a shield attack.")]
        public float brokenSeconds = 2.5f;
        [Tooltip("Dome radius, metres, centred in front of the chest.")]
        public float domeRadius = 1.25f;
        [Tooltip("Metres in front of the Judge's root the dome sits.")]
        public float domeForward = 0.7f;
        [Tooltip("Dome centre height above the feet.")]
        public float domeHeight = 1.35f;
        [ColorUsage(true, true)] public Color domeColor = new Color(1f, 0.55f, 0.18f, 1f) * 0.55f;

        /// <summary>True while the dome is up and deflecting.</summary>
        public bool ShieldUp { get; private set; }
        public int Deflects { get; private set; }
        public int Shatters { get; private set; }

        EnemyController controller;
        Transform dome;
        Renderer domeRenderer;
        MaterialPropertyBlock mpb;
        float brokenUntil = -99f;
        float domeScale;
        float flashUntil;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Is a shield attack in a state that holds the dome up? Pure, for the tests.</summary>
        public static bool HoldsShield(bool alive, EnemyController.State state, string attackName, string raise, string bash)
        {
            if (!alive || string.IsNullOrEmpty(attackName)) return false;
            if (attackName == raise) return state == EnemyController.State.Windup || state == EnemyController.State.Strike;
            if (attackName == bash) return state == EnemyController.State.Windup;
            return false;
        }

        void Awake() { controller = GetComponent<EnemyController>(); }
        void OnEnable() { GameEvents.ParryResolved += OnParryResolved; }
        void OnDisable() { GameEvents.ParryResolved -= OnParryResolved; ShieldUp = false; if (dome != null) dome.gameObject.SetActive(false); }

        void Update()
        {
            if (controller == null) return;
            var atk = controller.CurrentAttack;
            ShieldUp = Time.time >= brokenUntil
                    && HoldsShield(controller.IsAlive && !controller.IsStaggered, controller.Current,
                                   atk != null ? atk.name : null, raiseAttack, bashAttack);
            UpdateDome(Time.deltaTime);
        }

        public bool TryDeflectPlayerHit(PlayerHitKind kind, Vector3 point, Vector3 direction, out float posture)
        {
            posture = 0f;
            if (!ShieldUp) return false;
            if (kind == PlayerHitKind.ThrownBlade)
            {
                Shatter(point);
                return true;   // the blade broke the shield; it does not also add posture
            }
            Deflects++;
            posture = recoilPosture;
            flashUntil = Time.time + 0.15f;
            SlashFx.Ring(point, -direction, domeColor * 2f, domeRadius * 0.9f, 0.22f);
            SlashFx.Sparks(point, -direction + Vector3.up * 0.3f, new Color(1f, 0.6f, 0.2f, 1f), 10, 6f, 50f);
            AudioManager.Play(Sfx.Block, 1f, 1.25f, 0.04f);
            return true;
        }

        void OnParryResolved(ParryResult result)
        {
            if (result != ParryResult.Perfect || controller == null || !controller.IsAlive) return;
            var atk = controller.CurrentAttack;
            if (atk == null || atk.name != bashAttack) return;
            var player = FindAnyObjectByType<PlayerCombat>();
            if (player == null || (player.transform.position - transform.position).sqrMagnitude > (atk.range + 1.5f) * (atk.range + 1.5f)) return;
            Shatter(transform.position + Vector3.up * domeHeight + transform.forward * domeForward);
        }

        void Shatter(Vector3 at)
        {
            if (Time.time < brokenUntil) return;
            brokenUntil = Time.time + Mathf.Max(0.1f, brokenSeconds);
            Shatters++;
            ShieldUp = false;
            SlashFx.Ring(at, transform.forward, new Color(1f, 0.6f, 0.2f, 1f), domeRadius * 1.6f, 0.35f);
            SlashFx.Sparks(at, transform.forward + Vector3.up * 0.5f, new Color(1f, 0.55f, 0.15f, 1f), 24, 9f, 140f);
            SlashFx.Flare(at, new Color(1f, 0.7f, 0.3f, 1f), 1.6f, 0.2f);
            if (CameraShake.I != null) CameraShake.I.Small();
            AudioManager.Play(Sfx.PostureBreak, 0.9f, 1.2f, 0.03f);
            // His own recoil path: Recover, telegraph cleared, then the posture chunk (a break overrides the recoil).
            float chunk = controller.Posture != null ? controller.Posture.Max * Mathf.Clamp01(shatterPostureFraction) : 0f;
            controller.OnParried(chunk);
        }

        void UpdateDome(float dt)
        {
            float target = ShieldUp ? 1f : 0f;
            domeScale = Mathf.MoveTowards(domeScale, target, dt / 0.18f);
            if (domeScale <= 0.001f)
            {
                if (dome != null && dome.gameObject.activeSelf) dome.gameObject.SetActive(false);
                return;
            }
            EnsureDome();
            if (!dome.gameObject.activeSelf) dome.gameObject.SetActive(true);
            dome.position = transform.position + Vector3.up * domeHeight + transform.forward * domeForward;
            float breathe = 1f + 0.04f * Mathf.Sin(Time.time * 6f);
            dome.localScale = Vector3.one * (domeRadius * 2f * domeScale * breathe);
            if (mpb == null) mpb = new MaterialPropertyBlock();
            domeRenderer.GetPropertyBlock(mpb);
            float flash = Time.time < flashUntil ? 2.2f : 1f;
            mpb.SetColor(BaseColorId, domeColor * flash * domeScale);
            domeRenderer.SetPropertyBlock(mpb);
        }

        void EnsureDome()
        {
            if (dome != null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "AegisDome";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            dome = go.transform;
            domeRenderer = go.GetComponent<Renderer>();
            domeRenderer.sharedMaterial = SlashFx.CreateAdditiveMaterial(domeColor);
            domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            domeRenderer.receiveShadows = false;
        }

        void OnDestroy()
        {
            if (dome != null) Destroy(dome.gameObject);
        }
    }
}
