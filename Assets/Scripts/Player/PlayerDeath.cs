using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    public class PlayerDeath : MonoBehaviour
    {
        [Tooltip("Kept short on purpose: a speedrun platformer must not punish a fall with dead air.")]
        public float respawnDelay = 0.35f;

        [Header("Void fall")]
        [Tooltip("Falling this far below the last grounded position kills instantly, instead of waiting for the kill plane far below the level.")]
        public float voidFallDistance = 9f;

        Health health;
        FirstPersonMotor motor;
        WeaponController weapons;
        WeaponViewmodel viewmodel;
        bool dying;

        const float SupportProbeStart = 0.25f;
        const float SupportProbeRadius = 0.2f;

        void Awake()
        {
            health = GetComponent<Health>();
            motor = GetComponent<FirstPersonMotor>();
            weapons = GetComponent<WeaponController>();
            viewmodel = GetComponentInChildren<WeaponViewmodel>();
        }

        void OnEnable() { health.OnDied += OnDied; }
        void OnDisable() { health.OnDied -= OnDied; }

        void Update()
        {
            // Waiting for the y=-25 kill plane cost ~1.7s of silent falling from the arena. Fail fast instead.
            if (dying || health == null || health.IsDead || motor == null) return;
            if (!GameManager.IsPlaying || motor.IsGrounded) return;
            bool supportBelow = HasRouteSurfaceBelow();
            if (!ShouldTriggerVoidFall(transform.position.y, motor.LastGroundedPosition.y,
                                       voidFallDistance, motor.IsGrounded, supportBelow)) return;

            if (health.Invulnerable)
            {
                // god mode: don't die, just put us back on solid ground
                if (LevelManager.I != null) LevelManager.I.Respawn();
                return;
            }
            health.TakeDamage(new DamageInfo { damage = 99999f, source = gameObject });
        }

        /// <summary>
        /// A downhill jump can legitimately descend more than the void threshold while remaining directly
        /// above a ramp. Ground contact is deliberately tolerant of seams and low frame rates, so a stale
        /// grounded sample alone cannot prove that the player has left the course.
        /// </summary>
        bool HasRouteSurfaceBelow()
        {
            int mask = motor != null
                ? motor.WorldMask
                : ~((1 << Layers.Player) | (1 << Layers.Enemy) | (1 << Layers.Interactable));
            float distance = Mathf.Max(0.01f, voidFallDistance) + SupportProbeStart;
            return Physics.SphereCast(transform.position + Vector3.up * SupportProbeStart,
                                      SupportProbeRadius, Vector3.down, out _, distance, mask,
                                      QueryTriggerInteraction.Ignore);
        }

        public static bool ShouldTriggerVoidFall(float currentY, float lastGroundedY, float threshold,
                                                 bool grounded, bool supportBelow)
        {
            return !grounded && !supportBelow &&
                   currentY < lastGroundedY - Mathf.Max(0.01f, threshold);
        }

        void OnDied()
        {
            if (dying) return;
            StartCoroutine(DieCo());
        }

        IEnumerator DieCo()
        {
            dying = true;
            GameManager.I.SetState(GameState.Dead);
            if (weapons != null) weapons.CancelAttack();
            if (viewmodel != null) viewmodel.Interrupt();
            if (motor != null) motor.CanMove = false;

            int souls = SoulsWallet.I != null ? SoulsWallet.I.TakeAll() : 0;
            if (souls > 0 && LevelManager.I != null)
                LevelManager.I.SpawnBloodstain(motor != null ? motor.LastGroundedPosition : transform.position, souls);

            if (ScreenFlash.I) ScreenFlash.I.Flash(new Color(0.6f, 0f, 0.1f), 0.85f, respawnDelay);
            if (CameraFX.I) CameraFX.I.VignettePulse(0.5f, respawnDelay);
            AudioManager.Play(Sfx.Death);
            GameEvents.RaisePlayerDied();

            yield return new WaitForSecondsRealtime(respawnDelay);

            if (motor != null) motor.CanMove = true;
            if (LevelManager.I != null) LevelManager.I.Respawn();
            else { health.ResetFull(); GameManager.I.SetState(GameState.Playing); }
            dying = false;
        }
    }
}
