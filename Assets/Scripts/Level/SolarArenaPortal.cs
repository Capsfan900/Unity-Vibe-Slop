using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Same-scene transport for one solar boss realm. The exterior sphere only moves the player;
    /// <see cref="BossArenaTrigger"/> remains the owner of gate closure, clear detection and reset.
    /// Keeping the realm in the level scene preserves the timer, checkpoint, bloodstain and boss events.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class SolarArenaPortal : MonoBehaviour
    {
        public BossArenaTrigger arena;
        [Tooltip("Authored realm data copied onto the built portal so scene export is lossless.")]
        public SolarRealmDef definition;
        public Transform realmBoundsCenter;
        public float realmContainmentRadius = 30f;
        public Transform realmEntry;
        public Transform worldRetry;
        public Transform worldReturn;
        public GameObject realmExitRoot;
        public bool hasReturn;

        const float TeleportDebounce = 0.25f;
        /// <summary>How long a mini realm's entry holds the camera on its occupants while they stand still.</summary>
        public const float IntroHoldSeconds = 1.6f;
        public const float IntroFovPushDegrees = 14f;
        /// <summary>Squared look delta (mouse counts / stick) that hands the camera back to the player mid-intro.</summary>
        public const float IntroReleaseLookSq = 4f;
        Coroutine intro;
        readonly System.Collections.Generic.List<EnemyController> introLocked = new System.Collections.Generic.List<EnemyController>();
        float lastTeleportAt = -99f;
        bool exitShown;
        PlayerCombat occupant;
        SolarArenaVisual visual;

        /// <summary>The shipped theme key, used only to colour the crossing.</summary>
        public string ThemeKey
        {
            get
            {
                return definition != null && !string.IsNullOrEmpty(definition.themeMaterialKey)
                    ? definition.themeMaterialKey : "SolarCyan";
            }
        }

        public bool IsFinalBossPortal { get { return arena != null && arena.clearSpawner == null; } }
        public bool ExitAvailable { get { return hasReturn && arena != null && arena.Cleared; } }
        public Vector3 RealmEntryPosition { get { return realmEntry != null ? realmEntry.position : Vector3.zero; } }
        public Vector3 WorldReturnPosition { get { return worldReturn != null ? worldReturn.position : Vector3.zero; } }

        public static SolarArenaPortal FindFinalBossPortal()
        {
            foreach (var portal in FindObjectsByType<SolarArenaPortal>())
                if (portal != null && portal.IsFinalBossPortal) return portal;
            return null;
        }

        void Awake()
        {
            var col = GetComponent<SphereCollider>();
            col.isTrigger = true;
            SetExit(false);

            // Hand the presentation lane the two numbers it cannot derive: how far in the transport
            // fires, and which sun this is. Everything else about the crossing (the membrane band, the
            // wash curve, the cut) lives in SolarTransition, so no serialized value can go stale.
            visual = GetComponent<SolarArenaVisual>();
            if (visual != null)
            {
                var scale = transform.lossyScale;
                float uniform = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                visual.crossingTriggerRadius = col.radius * Mathf.Max(0.0001f, uniform);
                visual.crossingThemeKey = ThemeKey;
                visual.crossingArmed = false;
            }
        }

        void Update()
        {
            SetExit(ExitAvailable);
            // A cleared sun no longer transports, so it no longer closes over the eye. This is what
            // keeps the wash from sitting on the screen at the return point, which is INSIDE the
            // drawn shell (15-16 m against a 22-31 m visual radius on every shipped portal).
            if (visual != null) visual.crossingArmed = arena != null && !arena.Cleared;
        }

        void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<PlayerCombat>();
            if (player != null) Enter(player);
        }

        /// <summary>Public entry for the F5/debug harness and behaviour tests.</summary>
        public bool Enter(PlayerCombat player)
        {
            if (player == null || arena == null || realmEntry == null) return false;
            // A cleared sun is inert, matching the disarmed approach wash and open return path.
            if (arena.Cleared) return false;
            if (Time.unscaledTime - lastTeleportAt < TeleportDebounce) return false;
            if (player.GetComponent<FirstPersonMotor>() == null) return false;
            if (!arena.BeginFight(player)) return false;

            if (!Teleport(player, realmEntry)) return false;
            // Fired AFTER the transport, inside the same call. Unity renders no frame between the two,
            // so the cover cannot be late; and because it is downstream of the teleport it can never
            // play for a crossing that did not happen.
            SolarTransition.Cut(ThemeKey);
            AudioManager.Play(Sfx.SolarWarp, 1f, 1f, 0f);
            lastTeleportAt = Time.unscaledTime;
            occupant = player;
            if (!IsFinalBossPortal)
            {
                EndIntro();
                if (isActiveAndEnabled) intro = StartCoroutine(IntroHold(player));
                GameEvents.RaiseRealmFightStarted(arena);
            }
            return true;
        }

        /// <summary>Called by the realm-side exit trigger after the mini-boss clear latch opens.</summary>
        public bool Exit(PlayerCombat player)
        {
            if (player == null || !ExitAvailable || worldReturn == null) return false;
            if (Time.unscaledTime - lastTeleportAt < TeleportDebounce) return false;

            if (!Teleport(player, worldReturn)) return false;
            SolarTransition.Cut(ThemeKey);
            lastTeleportAt = Time.unscaledTime;
            occupant = null;
            return true;
        }

        public void ResetPortal()
        {
            // LevelManager.Respawn follows ResetEnemies with its own checkpoint teleport. A standalone
            // ResetEnemies (debug/tests) has no such second step, so rescue an occupant to the safe side
            // of the sun while the world is still Playing. Never replace the checkpoint on a real death.
            bool respawnWillPlacePlayer = GameManager.I != null && GameManager.I.State == GameState.Dead;
            if (occupant != null && !respawnWillPlacePlayer && worldRetry != null && OccupantIsInsideRealm())
                Teleport(occupant, worldRetry);
            occupant = null;
            lastTeleportAt = -99f;
            EndIntro();
            SetExit(false);
            // A reset undoes a crossing; a cover armed for it must not outlive it.
            if (ScreenFlash.I != null) ScreenFlash.I.ClearCurtain();
        }

        /// <summary>
        /// The souls "boss reveal": the occupants hold (aggroLocked) while the view is pulled onto them.
        /// Runs after PlayerLook.Update each frame and steers through NudgeAim, the sanctioned camera API.
        /// </summary>
        System.Collections.IEnumerator IntroHold(PlayerCombat player)
        {
            introLocked.Clear();
            LockOccupant(arena.clearSpawner);
            LockOccupant(arena.partnerSpawner);
            var look = player != null ? player.GetComponent<PlayerLook>() : null;
            float end = Time.unscaledTime + IntroHoldSeconds;
            bool steering = true;
            while (Time.unscaledTime < end)
            {
                yield return null;
                if (!steering || look == null || look.Cam == null || introLocked.Count == 0) continue;
                // The player's hand always wins: any look input ends the pull and the push-in at once, so the
                // reveal never reads as a lock-on fighting the mouse (user, 2026-09-14). The occupants still hold.
                if (InputReader.I != null && InputReader.I.LookDelta.sqrMagnitude > IntroReleaseLookSq)
                {
                    steering = false;
                    if (CameraFX.I != null) CameraFX.I.FovHold(0f);
                    continue;
                }
                Vector3 focus = Vector3.zero;
                foreach (var e in introLocked) if (e != null) focus += e.transform.position;
                focus = focus / introLocked.Count + Vector3.up * 1.6f;
                Vector3 to = focus - look.Cam.position;
                float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                float k = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
                look.NudgeAim(Mathf.DeltaAngle(look.Yaw, wantYaw) * k, (wantPitch - look.Pitch) * k);
                // A slow push-in on the pair. CameraFX.FovHold's only other writer is the slide, and the
                // player arrives standing, so the channel is free for the hold's duration.
                if (CameraFX.I != null) CameraFX.I.FovHold(-IntroFovPushDegrees);
            }
            EndIntro();
        }

        void LockOccupant(EnemySpawner spawner)
        {
            var inst = spawner != null ? spawner.Instance : null;
            var enemy = inst != null ? inst.GetComponent<EnemyController>() : null;
            if (enemy == null || enemy.aggroLocked) return;
            enemy.aggroLocked = true;
            introLocked.Add(enemy);
        }

        void EndIntro()
        {
            if (intro != null) { StopCoroutine(intro); intro = null; }
            if (introLocked.Count > 0 && CameraFX.I != null) CameraFX.I.FovHold(0f);
            foreach (var e in introLocked) if (e != null) e.aggroLocked = false;
            introLocked.Clear();
        }

        static bool Teleport(PlayerCombat player, Transform destination)
        {
            if (player == null || destination == null) return false;
            var motor = player.GetComponent<FirstPersonMotor>();
            if (motor == null) return false;
            float yaw = destination.eulerAngles.y;
            motor.Teleport(destination.position, yaw);
            // FirstPersonMotor currently forwards yaw itself. Keep the explicit owner call here too:
            // older prefabs and test doubles can omit that cached reference, while PlayerLook rewrites
            // transform rotation on its next Update.
            var look = player.GetComponent<PlayerLook>();
            if (look != null) look.SetYaw(yaw);
            return true;
        }

        bool OccupantIsInsideRealm()
        {
            if (occupant == null || realmBoundsCenter == null) return false;
            Vector3 delta = occupant.transform.position - realmBoundsCenter.position;
            float horizontalSq = delta.x * delta.x + delta.z * delta.z;
            float radius = Mathf.Max(1f, realmContainmentRadius + 2f);
            return horizontalSq <= radius * radius && Mathf.Abs(delta.y) <= radius;
        }

        void SetExit(bool show)
        {
            if (exitShown == show && (realmExitRoot == null || realmExitRoot.activeSelf == show)) return;
            exitShown = show;
            if (realmExitRoot != null) realmExitRoot.SetActive(show);
        }
    }

}
