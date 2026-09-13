using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>Melee combos with a sphere hit check in front of the camera. Attack becomes Execute when a staggered target is in reach.</summary>
    public class WeaponController : MonoBehaviour
    {
        public WeaponData[] loadout;
        /// <summary>LEGACY, unused since the hit confirm moved to <see cref="WeaponImpactFx"/>: the old
        /// spark was a lit sphere that needed a material of its own. <c>PrefabFactory</c> still assigns
        /// it, so the field stays until that assignment is retired in the same pass.</summary>
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
        PlayerCombat combat;
        readonly Collider[] hits = new Collider[16];
        readonly HashSet<EnemyController> hitSet = new HashSet<EnemyController>();

        void Awake()
        {
            look = GetComponent<PlayerLook>();
            stats = GetComponent<PlayerStats>();
            // includeInactive: the default overload skips disabled objects, so hiding the viewmodel root
            // (a menu, a cutscene, a test) and reloading the scene left this null — and Equip() then
            // silently stopped swapping weapon models with nothing logged.
            viewmodel = GetComponentInChildren<WeaponViewmodel>(true);
            exec = GetComponent<ExecuteInteractor>();
            parry = GetComponent<ParryController>();
            flask = GetComponent<FlaskAbility>();
            combat = GetComponent<PlayerCombat>();
        }

        void OnEnable() { GameEvents.PlayerPostureBroken += CancelAttack; }
        void OnDisable() { GameEvents.PlayerPostureBroken -= CancelAttack; }

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
            // No swapping the sword that is currently in the air (BladeThrow).
            if (slot >= 0 && slot < loadout.Length && slot != Index && !ThrownBlade.IsAway) Equip(slot);

            if (input.AttackPressed) TryAttack();
        }

        /// <summary>
        /// One attack press: execute a staggered target if one is in reach, otherwise start a swing or
        /// queue the next combo step. <see cref="Update"/> calls this when <see cref="InputReader"/>
        /// reports the press — input is still read only there. Returns whether the press was consumed.
        /// </summary>
        public bool TryAttack()
        {
            if (Current == null) return false;
            if (ThrownBlade.IsAway) return false;   // the sword is in the air: nothing to swing or execute with
            if (combat != null && combat.IsStaggered) return false;
            if (exec != null && exec.TryExecute()) { CancelAttack(); return true; }
            if (exec != null && exec.IsExecuting) return false;
            if (flask != null && flask.IsDrinking) return false;
            if (parry != null && parry.IsActive) return false;

            if (IsAttacking) queued = true;
            else StartSwing();
            return true;
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
            AudioManager.Play(WeaponAudio.SwingSfx(w), 0.7f, combo % 2 == 0 ? 1f : 1.15f);
            yield return new WaitForSeconds(w.hitDelay);
            DoHit(w, combo);
            yield return new WaitForSeconds(Mathf.Max(0.01f, w.attackDuration - w.hitDelay));
            IsAttacking = false;
            swing = null;
            comboWindowEnd = Time.time + w.comboWindow;
            bool staggered = combat != null && combat.IsStaggered;
            if (queued && combo + 1 < w.comboLength && GameManager.IsPlaying && !staggered) StartSwing();
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
                // Presentation only — every number, shape and colour lives in WeaponImpactFx, which
                // draws it through the pooled, 1.0-normalised SlashFx primitives. The finisher flag is
                // the SAME real state the audio below already pitches down for.
                WeaponImpactFx.Hit(point, cam.forward, w, combo == w.comboLength - 1);
                any = true;
            }
            if (any)
            {
                TimeScaleController.I.HitStop(w.hitStopSeconds);
                if (CameraShake.I) CameraShake.I.Small();
                AudioManager.Play(WeaponAudio.HitSfx(w), 0.9f, combo == w.comboLength - 1 ? 0.8f : 1f);
            }
        }

        public void CancelAttack()
        {
            if (swing != null) { StopCoroutine(swing); swing = null; }
            IsAttacking = false;
            queued = false;
            if (viewmodel != null && !(parry != null && parry.IsActive)) viewmodel.Interrupt();
        }

    }
}
