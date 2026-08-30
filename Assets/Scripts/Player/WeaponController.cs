using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Melee combos with a sphere hit check in front of the camera. Attack becomes Execute when a staggered target is in reach.</summary>
    public class WeaponController : MonoBehaviour
    {
        public WeaponData[] loadout;
        public Material hitSparkMaterial;

        public int Index { get; private set; }
        public WeaponData Current => loadout != null && loadout.Length > 0 ? loadout[Mathf.Clamp(Index, 0, loadout.Length - 1)] : null;
        public bool IsAttacking { get; private set; }

        int comboIndex = -1;
        float comboWindowEnd = -99f;
        bool queued;
        Coroutine swing;

        PlayerLook look;
        PlayerStats stats;
        WeaponViewmodel viewmodel;
        ExecuteInteractor exec;
        ParryController parry;
        FlaskAbility flask;
        readonly Collider[] hits = new Collider[16];
        readonly HashSet<EnemyController> hitSet = new HashSet<EnemyController>();

        void Awake()
        {
            look = GetComponent<PlayerLook>();
            stats = GetComponent<PlayerStats>();
            viewmodel = GetComponentInChildren<WeaponViewmodel>();
            exec = GetComponent<ExecuteInteractor>();
            parry = GetComponent<ParryController>();
            flask = GetComponent<FlaskAbility>();
        }

        void Start()
        {
            Equip(0);
        }

        public void Equip(int i)
        {
            if (loadout == null || loadout.Length == 0) return;
            Index = Mathf.Clamp(i, 0, loadout.Length - 1);
            CancelAttack();
            comboIndex = -1;
            if (viewmodel != null) viewmodel.SetWeapon(Current);
            GameEvents.RaiseWeaponChanged(Current);
            AudioManager.Play(Sfx.Click, 0.6f);
        }

        void Update()
        {
            if (!GameManager.IsPlaying || Current == null) return;
            var input = InputReader.I;

            int slot = input.WeaponSlotPressed;
            if (slot >= 0 && slot < loadout.Length && slot != Index) Equip(slot);
            else if (input.NextPressed) Equip((Index + 1) % loadout.Length);
            else if (input.PrevPressed) Equip((Index - 1 + loadout.Length) % loadout.Length);

            if (input.AttackPressed)
            {
                if (exec != null && exec.TryExecute()) { CancelAttack(); return; }
                if (exec != null && exec.IsExecuting) return;
                if (flask != null && flask.IsDrinking) return;
                if (parry != null && parry.IsActive) return;

                if (IsAttacking) { queued = true; }
                else StartSwing();
            }
        }

        void StartSwing()
        {
            var w = Current;
            if (Time.time <= comboWindowEnd && comboIndex + 1 < w.comboLength) comboIndex++;
            else comboIndex = 0;
            queued = false;
            IsAttacking = true;
            if (swing != null) StopCoroutine(swing);
            swing = StartCoroutine(SwingCo(w, comboIndex));
        }

        IEnumerator SwingCo(WeaponData w, int combo)
        {
            if (viewmodel != null) viewmodel.PlayAttack(combo, w.attackDuration, w.hitDelay);
            AudioManager.Play(Sfx.Swing, 0.7f, combo % 2 == 0 ? 1f : 1.15f);
            yield return new WaitForSeconds(w.hitDelay);
            DoHit(w, combo);
            yield return new WaitForSeconds(Mathf.Max(0.01f, w.attackDuration - w.hitDelay));
            IsAttacking = false;
            swing = null;
            comboWindowEnd = Time.time + w.comboWindow;
            if (queued && combo + 1 < w.comboLength && GameManager.IsPlaying) StartSwing();
            else queued = false;
        }

        void DoHit(WeaponData w, int combo)
        {
            Transform cam = look != null && look.Cam != null ? look.Cam : transform;
            Vector3 center = cam.position + cam.forward * w.hitOffset;
            int n = Physics.OverlapSphereNonAlloc(center, w.hitRadius, hits, Layers.EnemyMask, QueryTriggerInteraction.Ignore);
            hitSet.Clear();
            bool any = false;
            float mult = stats != null ? stats.DamageMultiplier(w) : 1f;
            for (int i = 0; i < n; i++)
            {
                var e = hits[i].GetComponentInParent<EnemyController>();
                if (e == null || !e.IsAlive || hitSet.Contains(e)) continue;
                hitSet.Add(e);
                float dmg = w.baseDamage * w.ComboMultiplier(combo) * mult;
                Vector3 point = hits[i].ClosestPoint(center);
                e.Health.TakeDamage(new DamageInfo { damage = dmg, postureDamage = w.postureDamage, point = point, direction = cam.forward, source = gameObject });
                e.Posture.Add(w.postureDamage);
                SpawnSpark(point, w.neon);
                any = true;
            }
            if (any)
            {
                TimeScaleController.I.HitStop(w.hitStopSeconds);
                if (CameraShake.I) CameraShake.I.Small();
                AudioManager.Play(Sfx.Hit, 0.9f, combo == w.comboLength - 1 ? 0.8f : 1f);
            }
        }

        void SpawnSpark(Vector3 pos, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.35f;
            var r = go.GetComponent<Renderer>();
            if (hitSparkMaterial != null) r.sharedMaterial = hitSparkMaterial;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_EmissionColor", color * 4f);
            mpb.SetColor("_BaseColor", color);
            r.SetPropertyBlock(mpb);
            go.AddComponent<Spark>();
        }

        public void CancelAttack()
        {
            if (swing != null) { StopCoroutine(swing); swing = null; }
            IsAttacking = false;
            queued = false;
            if (viewmodel != null && !(parry != null && parry.IsActive)) viewmodel.Interrupt();
        }

        /// <summary>Tiny self-destructing hit spark.</summary>
        class Spark : MonoBehaviour
        {
            float t;
            void Update()
            {
                t += Time.unscaledDeltaTime;
                float k = 1f - t / 0.22f;
                transform.localScale = Vector3.one * (0.35f + (1f - k) * 0.5f) * Mathf.Max(0f, k);
                if (t >= 0.22f) Destroy(gameObject);
            }
        }
    }
}
