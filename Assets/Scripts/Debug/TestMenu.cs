using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1
{
    /// <summary>
    /// Developer overlay (F1). A button-driven superset of <see cref="DebugKeys"/>: warp, grant items,
    /// swap weapons, restore/break the player, wipe or reset enemies, plus a live state readout.
    /// Present in playtest builds for trusted diagnosis, but inert until the command console grants
    /// <see cref="DeveloperAccess"/> for this process.
    /// </summary>
    public class TestMenu : MonoBehaviour
    {
        [Header("Panel")]
        public GameObject panel;
        public TMP_Text readout;
        public Button closeButton;

        [Header("Warp")]
        public Button warpStartButton;
        public Button warpCheckpoint1Button;
        public Button warpCheckpoint2Button;
        public Button warpBossButton;
        public Button warpYardButton;   // sandbox only: the movement yard east of the arena

        [Header("Items")]
        public ItemData[] items;
        public Button[] itemButtons;

        [Header("Weapons")]
        public Button[] weaponButtons;

        [Header("Player")]
        public Button fullRestoreButton;
        public Button godModeButton;
        public Button giveSoulsButton;
        public Button breakPostureButton;
        [Tooltip("Flips StatusStripView.StatusEffectsVisible. Held item rows remain visible.")]
        public Button statusEffectsButton;
        [Tooltip("Flips WandPedestal.DevMenuEnabled. Label is rewritten to the live state on every refresh.")]
        public Button wandPedestalButton;
        public Button armMovementButton;

        [Header("Enemies")]
        public Button killNearbyButton;
        public Button staggerNearbyButton;
        public Button resetEnemiesButton;

        [Header("Level editor")]
        public Button levelEditorButton;
        [Tooltip("Opens the settings screen straight onto its INFO card (the key reference).")]
        public Button infoButton;

        static readonly Vector3 ArenaEntrance = new Vector3(0f, 28.2f, 296f);
        const float NearbyRadius = 40f;

        readonly StringBuilder sb = new StringBuilder();
        int timeHandle = -1;
        bool open;
        float fps = 60f;

        void Start()
        {
            if (panel != null) panel.SetActive(false);

            Wire(closeButton, Close);
            Wire(warpStartButton, WarpStart);
            Wire(warpCheckpoint1Button, () => WarpCheckpoint("Checkpoint_1"));
            Wire(warpCheckpoint2Button, () => WarpCheckpoint("Checkpoint_2"));
            Wire(warpBossButton, WarpBossArena);
            Wire(warpYardButton, WarpMovementYard);

            if (itemButtons != null)
                for (int i = 0; i < itemButtons.Length; i++)
                {
                    int index = i;                       // capture per-iteration
                    Wire(itemButtons[i], () => GiveItem(index));
                }

            if (weaponButtons != null)
                for (int i = 0; i < weaponButtons.Length; i++)
                {
                    int index = i;
                    Wire(weaponButtons[i], () => EquipWeapon(index));
                }

            Wire(fullRestoreButton, FullRestore);
            Wire(godModeButton, ToggleGodMode);
            Wire(levelEditorButton, OpenLevelEditor);
            Wire(infoButton, OpenInfo);
            Wire(giveSoulsButton, GiveSouls);
            Wire(breakPostureButton, BreakPosture);
            Wire(statusEffectsButton, ToggleStatusEffects);
            Wire(wandPedestalButton, ToggleWandPedestal);
            Wire(armMovementButton, ToggleArmMovement);
            Wire(killNearbyButton, KillNearby);
            Wire(staggerNearbyButton, StaggerNearby);
            Wire(resetEnemiesButton, ResetEnemies);

            RefreshButtons();
        }

        static void Wire(Button b, System.Action action)
        {
            if (b != null && action != null) b.onClick.AddListener(() => action());
        }

        void Update()
        {
            var input = InputReader.I;
            if (input != null)
            {
                if (input.TestMenuPressed)
                {
                    if (open) Close();
                    else if (GameManager.IsPlaying) Open();
                }
                else if (open && input.PausePressed) Close();
            }

            if (open)
            {
                fps = Mathf.Lerp(fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.08f);
                RefreshReadout();
            }
        }

        public void Open()
        {
            if (open || !DeveloperAccess.IsUnlocked) return;
            open = true;
            if (GameManager.I != null) GameManager.I.SetState(GameState.Paused);   // also unlocks the cursor
            if (TimeScaleController.I != null) timeHandle = TimeScaleController.I.Request(0f);
            if (panel != null) panel.SetActive(true);
            RefreshButtons();
            RefreshReadout();
        }

        void OpenInfo()
        {
            Close();
            if (SettingsMenu.I != null) SettingsMenu.I.OpenInfo();
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            if (panel != null) panel.SetActive(false);
            if (timeHandle >= 0 && TimeScaleController.I != null) { TimeScaleController.I.Release(timeHandle); timeHandle = -1; }
            if (GameManager.I != null) GameManager.I.SetState(GameState.Playing);
        }

        // ---- lookups -----------------------------------------------------------------------------

        static PlayerCombat Player() => FindAnyObjectByType<PlayerCombat>();
        static T OnPlayer<T>() where T : Component
        {
            var p = Player();
            return p != null ? p.GetComponent<T>() : null;
        }

        // ---- warp --------------------------------------------------------------------------------

        void WarpStart()
        {
            var lm = LevelManager.I;
            if (lm == null) return;

            var motor = OnPlayer<FirstPersonMotor>();
            var look = OnPlayer<PlayerLook>();
            var spawn = lm.startSpawn;
            if (spawn != null && motor != null)
            {
                motor.Teleport(spawn.position, spawn.eulerAngles.y);
                if (look != null) look.SetYaw(spawn.eulerAngles.y);
            }
            else lm.Respawn();
        }

        void WarpCheckpoint(string checkpointName)
        {
            if (LevelManager.I != null) LevelManager.I.Warp(checkpointName);
        }

        /// <summary>Sandbox only. In the campaign scene there is no SandboxController and the button does nothing.</summary>
        void WarpMovementYard()
        {
            var sandbox = FindAnyObjectByType<SandboxController>();
            if (sandbox != null) sandbox.WarpToMovementYard();
            else Debug.Log("[TestMenu] No SandboxController in this scene - the movement yard is in Sandbox.unity.");
        }

        void WarpBossArena()
        {
            if (LevelManager.I != null) LevelManager.I.Warp("Checkpoint_4");
            var motor = OnPlayer<FirstPersonMotor>();
            var look = OnPlayer<PlayerLook>();
            if (motor != null) motor.Teleport(ArenaEntrance, 0f);
            if (look != null) look.SetYaw(0f);
        }

        // ---- grants ------------------------------------------------------------------------------

        void GiveItem(int index)
        {
            if (items == null || index < 0 || index >= items.Length) return;
            var item = items[index];
            if (item == null) return;

            var inventory = FindAnyObjectByType<PlayerItems>();
            if (inventory != null) inventory.TryPickup(item);
            RefreshButtons();
        }

        void EquipWeapon(int index)
        {
            var weapons = OnPlayer<WeaponController>();
            if (weapons == null || weapons.loadout == null) return;
            if (index < 0 || index >= weapons.loadout.Length) return;
            weapons.Equip(index);
            RefreshButtons();
        }

        // ---- player ------------------------------------------------------------------------------

        void FullRestore()
        {
            var p = Player();
            if (p == null) return;
            if (p.Health != null) p.Health.ResetFull();

            var posture = p.GetComponent<PlayerPosture>();
            if (posture != null) posture.ResetFull();

            var res = p.GetComponent<PlayerResources>();
            if (res != null) { res.RefillFlask(); res.AddPyre(1000f); }
        }

        void ToggleGodMode()
        {
            var p = Player();
            if (p == null || p.Health == null) return;
            p.Health.Invulnerable = !p.Health.Invulnerable;
        }

        void GiveSouls()
        {
            if (SoulsWallet.I != null) SoulsWallet.I.Add(1000);
        }

        /// <summary>
        /// Switches only temporary-effect rows in the top-left strip. This is session-static like the
        /// other F1 developer switches, and StatusStripView immediately rebuilds every live instance.
        /// </summary>
        public void ToggleStatusEffects()
        {
            if (!DeveloperAccess.IsUnlocked) return;
            StatusStripView.StatusEffectsVisible = !StatusStripView.StatusEffectsVisible;
            RefreshButtons();
        }

        /// <summary>
        /// The wand altar at spawn is hidden and inert by default; this is the one place it is switched
        /// on. Every WandPedestal applies the flag on its next Update, so the altar appears (or vanishes)
        /// the moment the menu closes. Public so the feature suite can drive the same body the button does.
        /// </summary>
        public void ToggleWandPedestal()
        {
            if (!DeveloperAccess.IsUnlocked) return;
            WandPedestal.DevMenuEnabled = !WandPedestal.DevMenuEnabled;
            RefreshButtons();
        }

        /// <summary>
        /// Switches the viewmodel movement channel off and on live (BACKLOG 2b). The user asked for this
        /// the day the channel was built: it is a FEEL change they may not want, and a feel change you
        /// cannot turn off mid-run cannot be judged against the version without it.
        /// </summary>
        public void ToggleArmMovement()
        {
            if (!DeveloperAccess.IsUnlocked) return;
            MovementPose.Enabled = !MovementPose.Enabled;
            RefreshButtons();
        }

        void BreakPosture()
        {
            var posture = OnPlayer<PlayerPosture>();
            if (posture != null) posture.Add(9999f);
        }

        // ---- enemies -----------------------------------------------------------------------------

        void KillNearby()
        {
            var p = Player();
            if (p == null) return;
            foreach (var e in FindObjectsByType<EnemyController>())
            {
                if (e == null || !e.IsAlive || e.Health == null) continue;
                if (Vector3.Distance(e.transform.position, p.transform.position) > NearbyRadius) continue;
                e.Health.TakeDamage(new DamageInfo
                {
                    damage = 99999f,
                    isExecute = true,
                    source = gameObject,
                    point = e.transform.position,
                    direction = Vector3.down
                });
            }
        }

        void StaggerNearby()
        {
            var p = Player();
            if (p == null) return;
            foreach (var e in FindObjectsByType<EnemyController>())
            {
                if (e == null || !e.IsAlive || e.Posture == null) continue;
                if (Vector3.Distance(e.transform.position, p.transform.position) > NearbyRadius) continue;
                e.Posture.Break();
            }
        }

        /// <summary>Close this menu and open the in-game level editor (F10 does the same from play).</summary>
        void OpenLevelEditor()
        {
            Close();
            if (LevelEditor.I != null) LevelEditor.I.Toggle();
            else Debug.LogWarning("[TestMenu] No LevelEditor on the HUD; run VibeGame1/5. Build HUD.");
        }

        void ResetEnemies()
        {
            if (LevelManager.I != null) LevelManager.I.ResetEnemies();
        }

        // ---- ui refresh --------------------------------------------------------------------------

        void RefreshButtons()
        {
            var inventory = FindAnyObjectByType<PlayerItems>();
            if (itemButtons != null)
                for (int i = 0; i < itemButtons.Length; i++)
                {
                    var b = itemButtons[i];
                    if (b == null) continue;
                    var item = items != null && i < items.Length ? items[i] : null;
                    var text = b.GetComponentInChildren<TMP_Text>();
                    if (text != null) text.text = item != null ? item.displayName.ToUpperInvariant() : "-";
                    b.interactable = item != null && inventory != null && !inventory.IsFull;
                }

            var weapons = OnPlayer<WeaponController>();
            if (weaponButtons != null)
                for (int i = 0; i < weaponButtons.Length; i++)
                {
                    var b = weaponButtons[i];
                    if (b == null) continue;
                    bool has = weapons != null && weapons.loadout != null && i < weapons.loadout.Length && weapons.loadout[i] != null;
                    var text = b.GetComponentInChildren<TMP_Text>();
                    if (text != null) text.text = has ? weapons.loadout[i].displayName.ToUpperInvariant() : "-";
                    b.interactable = has;
                }

            if (wandPedestalButton != null)
            {
                var text = wandPedestalButton.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = WandPedestal.DevMenuEnabled ? "INSCRIPTION ALTAR: ON" : "INSCRIPTION ALTAR: OFF";
            }

            if (statusEffectsButton != null)
            {
                var text = statusEffectsButton.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = StatusStripView.StatusEffectsVisible ? "STATUS EFFECTS: ON" : "STATUS EFFECTS: OFF";
            }

            if (armMovementButton != null)
            {
                var text = armMovementButton.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = MovementPose.Enabled ? "ARM MOVEMENT: ON" : "ARM MOVEMENT: OFF";
            }
        }

        void RefreshReadout()
        {
            if (readout == null) return;

            sb.Clear();
            sb.Append("<b>LIVE STATE</b>\n\n");
            sb.Append("FPS            ").Append(fps.ToString("F0")).Append('\n');

            var p = Player();
            if (p != null)
            {
                if (p.Health != null)
                {
                    sb.Append("HP             ").Append(p.Health.Current.ToString("F0")).Append(" / ").Append(p.Health.Max.ToString("F0"));
                    if (p.Health.Invulnerable) sb.Append("   <b>[GOD]</b>");
                    sb.Append('\n');
                }

                var posture = p.GetComponent<PlayerPosture>();
                if (posture != null)
                {
                    sb.Append("POSTURE        ").Append(posture.Current.ToString("F0")).Append(" / ").Append(posture.Max.ToString("F0"));
                    if (p.IsStaggered) sb.Append("   <b>[BROKEN]</b>");
                    sb.Append('\n');
                }

                var res = p.GetComponent<PlayerResources>();
                if (res != null)
                {
                    sb.Append("PYRE           ").Append(res.Pyre.ToString("F0")).Append(" / ").Append(res.MaxPyre.ToString("F0")).Append('\n');
                    sb.Append("FLASK          ").Append(res.FlaskCharges).Append(" / ").Append(res.MaxFlask).Append('\n');
                }

                var weapons = p.GetComponent<WeaponController>();
                if (weapons != null && weapons.Current != null)
                    sb.Append("WEAPON         ").Append(weapons.Current.displayName).Append('\n');

                var inventory = p.GetComponent<PlayerItems>();
                if (inventory != null)
                {
                    sb.Append("ITEMS          ");
                    if (inventory.Held.Count == 0) sb.Append("(empty)");
                    else
                        for (int i = 0; i < inventory.Held.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(inventory.Held[i] != null ? inventory.Held[i].shortLabel : "?");
                        }
                    sb.Append('\n');
                }

                sb.Append("POS            ").Append(p.transform.position.ToString("F1")).Append('\n');
            }
            else sb.Append("<b>no player found</b>\n");

            sb.Append("SOULS          ").Append(SoulsWallet.I != null ? SoulsWallet.I.Souls : 0).Append('\n');
            sb.Append("WAND ALTAR     ").Append(WandPedestal.DevMenuEnabled ? "ON" : "OFF  (dev fixture; toggle above)").Append('\n');

            if (LevelManager.I != null)
                sb.Append("DEATHS         ").Append(LevelManager.I.DeathCount).Append('\n');

            if (SpeedrunTimer.I != null)
                sb.Append("TIMER          ").Append(SpeedrunTimer.Format(SpeedrunTimer.I.Elapsed)).Append('\n');

            var boss = FindAnyObjectByType<BossController>();
            sb.Append('\n').Append("<b>BOSS</b>\n");
            if (boss != null)
            {
                sb.Append("SEGMENTS       ").Append(boss.SegmentsLeft).Append('\n');
                sb.Append("PHASE          ").Append(boss.Phase).Append('\n');
                if (boss.Health != null)
                    sb.Append("HP             ").Append(boss.Health.Current.ToString("F0")).Append(" / ").Append(boss.Health.Max.ToString("F0")).Append('\n');
                if (boss.Posture != null)
                    sb.Append("POSTURE        ").Append(boss.Posture.Current.ToString("F0")).Append(" / ").Append(boss.Posture.Max.ToString("F0")).Append('\n');
                sb.Append("STATE          ").Append(boss.Current).Append('\n');
            }
            else sb.Append("(not spawned)\n");

            sb.Append('\n').Append("<b>TIME</b>\n");
            sb.Append("Time.timeScale ").Append(Time.timeScale.ToString("F2")).Append('\n');
            if (TimeScaleController.I != null)
            {
                sb.Append("WORLD          ").Append(TimeScaleController.I.WorldScale.ToString("F2")).Append('\n');
                sb.Append("PLAYER         ").Append(TimeScaleController.I.PlayerScale.ToString("F2")).Append('\n');
            }

            readout.text = sb.ToString();
        }
    }
}
