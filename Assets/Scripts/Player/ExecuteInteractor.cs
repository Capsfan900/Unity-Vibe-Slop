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

        public EnemyController Target { get; private set; }
        public bool IsExecuting { get; private set; }

        Health health;
        FirstPersonMotor motor;
        WeaponController weapons;
        WeaponViewmodel viewmodel;
        PlayerLook look;
        readonly Collider[] buf = new Collider[16];
        string lastPrompt = "";

        void Awake()
        {
            health = GetComponent<Health>();
            motor = GetComponent<FirstPersonMotor>();
            weapons = GetComponent<WeaponController>();
            viewmodel = GetComponentInChildren<WeaponViewmodel>();
            look = GetComponent<PlayerLook>();
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
            string prompt = Target != null ? "EXECUTE" : "";
            if (prompt != lastPrompt) { lastPrompt = prompt; GameEvents.RaisePromptChanged(prompt); }
        }

        public bool TryExecute()
        {
            if (Target == null || IsExecuting) return false;
            StartCoroutine(ExecuteCo(Target));
            return true;
        }

        IEnumerator ExecuteCo(EnemyController e)
        {
            IsExecuting = true;
            health.Invulnerable = true;
            if (motor != null) motor.CanMove = false;
            e.BeginExecuted(transform);
            if (viewmodel != null) viewmodel.PlayExecute(duration);
            GameEvents.RaisePromptChanged("");
            lastPrompt = "";
            AudioManager.Play(Sfx.Swing, 0.8f, 0.7f);

            yield return new WaitForSecondsRealtime(duration * 0.55f);

            var feel = GameManager.I.feel;
            var w = weapons != null ? weapons.Current : null;
            float dmg = w != null ? w.executeDamage : 300f;
            if (e != null && e.IsAlive)
            {
                e.Health.TakeDamage(new DamageInfo { damage = dmg, isExecute = true, source = gameObject, point = e.transform.position, direction = transform.forward });
            }
            TimeScaleController.I.HitStop(feel.executeHitStop, feel.hitStopScale);
            if (CameraShake.I) CameraShake.I.Big();
            if (CameraFX.I) { CameraFX.I.ChromaticPulse(1f, 0.35f); CameraFX.I.FovKick(-6f); }
            if (ScreenFlash.I) ScreenFlash.I.Flash(w != null ? w.neon : Color.white, 0.5f, 0.2f);
            AudioManager.Play(Sfx.Execute);

            yield return new WaitForSecondsRealtime(duration * 0.45f);

            if (e != null) e.EndExecuted();
            health.Invulnerable = false;
            if (motor != null) motor.CanMove = true;
            IsExecuting = false;
        }
    }
}
