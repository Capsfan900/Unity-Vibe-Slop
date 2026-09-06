# Code Tree

A file-level map of `Assets/Scripts`, grouped by folder (system). One line each — what it owns, not how.
Regenerate by hand when folders are added; for how systems *flow* into each other, see `docs/DATAFLOW.md`.
For module ownership, event bus and singletons, see `docs/ARCHITECTURE.md`.

```
Assets/Scripts/
├── Core/                     Framework: input, timing, camera, settings, global events
│   ├── GameManager.cs            Top-level singleton: game state, scene flow, references other managers
│   ├── GameEvents.cs             Static event bus other systems subscribe to / raise
│   ├── InputReader.cs            ONLY script touching the New Input System; exposes actions to everything else
│   ├── TimeScaleController.cs    ONLY writer of Time.timeScale; exposes PlayerDelta so hitstop never freezes the player
│   ├── ViewCamera.cs             Player camera rig; hookup point for lock-on and camera FX
│   ├── Layers.cs                 Physics layer constants (Player/Enemy/Interactable)
│   ├── SettingsData.cs           Serializable settings payload
│   ├── SettingsStore.cs          Load/save settings to disk
│   └── SettingsApplier.cs        Applies loaded settings to live systems (audio, sensitivity, graphics)
│
├── Data/                     ScriptableObject definitions (content is data, not code — see docs/AUTHORING.md)
│   ├── EnemyData.cs               Base enemy stats/config asset
│   ├── EnemyMoveset.cs            Set of attacks an enemy archetype can use
│   ├── EnemyAttackData.cs         One attack's timing/damage/telegraph definition
│   ├── BossData.cs                Boss-specific config extending enemy data
│   ├── WeaponData.cs              Player melee weapon definition
│   ├── WandData.cs                Player ranged/wand definition
│   ├── ItemData.cs                Consumable/utility item definition
│   ├── PlayerStatsData.cs         Player base stats asset
│   ├── UpgradeTable.cs            Level-up/upgrade curve data
│   ├── LevelDefinition.cs         One level's authored data asset (pieces, spawns, layout)
│   ├── LevelRegistry.cs           List of levels shown in the main menu / used by builders
│   └── GameFeelSettings.cs        Tunable feel constants (screenshake, hitstop, etc.)
│
├── Player/                   Player motor, combat, targeting, items, viewmodel
│   ├── FirstPersonMotor.cs        Movement body: only place velocity is decided; traversal pieces call into it, never write velocity
│   ├── TraversalMath.cs           Pure math for traversal (jump arcs, dashes, etc.), tested independent of the motor
│   ├── PlayerBody.cs              Physical body/legs representation, capsule + animation root
│   ├── PlayerLook.cs              Camera look rotation, applies lock-on assist
│   ├── LockOnController.cs        Lock-on target acquisition/switching control law
│   ├── LockOnMarker.cs            UI reticle tracking the locked target
│   ├── PlayerCombat.cs            ONLY entry point attacks resolve through (ReceiveAttack)
│   ├── ParryController.cs         Parry/deflect input window and state machine
│   ├── ParryMath.cs (Combat/)     — see Combat/ below
│   ├── ForgivenessMath.cs         Pure math for parry/dodge forgiveness windows
│   ├── PerfectMath.cs             Pure math for "perfect" timing bonuses
│   ├── PlayerPosture.cs           Player posture/stagger meter
│   ├── PlayerStamina.cs           Stamina resource for dodge/sprint/attacks
│   ├── PlayerResources.cs         Aggregates player resources (health/stamina/etc.) for HUD & systems
│   ├── PlayerStats.cs             Runtime stats derived from PlayerStatsData + upgrades
│   ├── PlayerDeath.cs             Death sequence, respawn/restore flow
│   ├── PlayerItems.cs             Inventory/consumable item usage (item key, E to use)
│   ├── FlaskAbility.cs            Healing flask consumable ability
│   ├── UltimateAbility.cs         Ultimate/special ability logic
│   ├── FlareGrapple.cs            DASH at a glowing sentry flare: pull to it, get tossed up
│   ├── WeaponController.cs        Melee weapon swing/combo state machine
│   ├── WeaponViewmodel.cs         First-person weapon mesh/animation driver
│   ├── ViewmodelArm.cs            Arm rig driver for viewmodel poses
│   ├── OffhandViewmodel.cs        Off-hand (wand/shield) viewmodel driver
│   ├── WandController.cs         Wand aiming/casting logic
│   └── ExecuteInteractor.cs      Detects/executes finishing-blow interactions on staggered enemies
│
├── Combat/                   Shared combat math and low-level effects (enemy + player)
│   ├── DamageInfo.cs              Struct carrying one hit's damage/source/type
│   ├── Health.cs                  Generic HP component used by player & enemies
│   ├── Posture.cs                 Generic posture/stagger meter component
│   ├── PostureMath.cs             Pure posture damage/regen math
│   ├── ParryMath.cs               Pure parry timing/impulse math (tested by FeatureTests)
│   └── EmissiveFlash.cs           Hit-flash material effect
│
├── Enemies/                  Two families (EnemyPaths, 2026-09-06)
│   ├── Core/                 Shared brain: EnemyController, EnemyVisuals, locomotion, marker, posture bar, EnemyPaths
│   ├── parkour_enemies/      Sentries on the spans: Projectile, ProjectileShooter, ProjectileMath, SentryBurst, SentryFlare, FlareMath (Sentry_* data)
│   └── souls_enemies/        The duels: BossController (Grunt, Heavy, Warden, Legendary_* data)
│   ├── EnemyController.cs        Per-enemy state machine (aggro, attack selection, telegraph)
│   ├── BossController.cs         Boss-specific state machine/phases
│   ├── IEnemyLocomotion.cs       Locomotion interface (nav vs. scripted movers)
│   ├── NavMeshLocomotion.cs      NavMesh-driven locomotion implementation
│   ├── IEnemyPresentation.cs     Presentation interface (animation/visual hookup)
│   ├── EnemyVisuals.cs           Standard enemy animation/visual driver
│   ├── PuppetVisuals.cs          Marionette/puppet-specific visual driver
│   ├── EnemyWeaponTrail.cs       Weapon trail VFX for enemy attacks
│   ├── EnemyPostureBar.cs        World-space posture bar above an enemy
│   ├── EnemySpawner.cs           Spawns enemies into a level/encounter
│   ├── DeathblowMarker.cs        Marks/enables execute prompts on staggered enemies
│   ├── EmberAura.cs              Ambient VFX aura for fire-type enemies
│   ├── Projectile.cs             Projectile behavior/lifetime
│   ├── ProjectileMath.cs         Pure projectile trajectory math
│   └── ProjectileShooter.cs      Fires configured projectiles from an enemy
│
├── Level/                    Level structure, traversal pieces, level flow
│   ├── LevelManager.cs           Level lifecycle: load, checkpoint, completion
│   ├── LevelPiece.cs             Base traversal/greybox piece component
│   ├── LevelPieceFactory.cs      Builds level pieces from data; shared by campaign builder and in-game editor
│   ├── LevelDocument.cs          Serialized level layout used by the in-game editor / export
│   ├── LevelEditor.cs            In-game level editor (F10): pick/place/rotate/delete, PLAY/EXPORT
│   ├── LevelProgress.cs          Tracks player progress through a level (splits, completion)
│   ├── SpeedrunTimer.cs          Speedrun clock/UI timing
│   ├── Checkpoint.cs             Checkpoint trigger/respawn point
│   ├── BossArenaTrigger.cs       Enters boss arena / locks the fight
│   ├── KillZone.cs               Instant-death volume (falling off level)
│   ├── ItemPickup.cs             World item pickup trigger
│   ├── WandPedestal.cs           Wand pickup/selection pedestal
│   ├── WaterVolume.cs            Water traversal piece; calls motor's TouchWater entry point
│   └── Balloon.cs                Launcher traversal piece; calls motor's Launch entry point
│
├── Progression/              Meta-progression: souls, upgrades, death recovery
│   ├── SoulsWallet.cs             Currency (souls) tracking
│   ├── UpgradeMath.cs             Pure stat-upgrade curve math
│   ├── LevelUpMenu.cs             Upgrade selection UI
│   └── Bloodstain.cs              Death-location soul recovery marker
│
├── Ghost/                    Run recording and ghost-replay racing
│   ├── RunRecorder.cs             Records player run as GhostData
│   ├── GhostData.cs               Serializable recorded run
│   ├── RunStore.cs                Saves/loads runs to disk
│   ├── GhostPlayer.cs             Replays a GhostData as a ghost avatar
│   ├── GhostRacing.cs             Manages ghost playback vs. live run
│   ├── Leaderboard.cs             Best-time leaderboard tracking
│   └── GhostHud.cs                Ghost delta/split HUD display
│
├── Feel/                     Game feel: camera FX, VFX, audio, procedural motion
│   ├── AudioManager.cs            Central SFX/music playback (Sfx enum → Resources/Audio/Sfx)
│   ├── ProceduralSfx.cs           Procedurally generated/varied sfx triggers
│   ├── CameraFX.cs                Camera post-effects (impact zoom, etc.)
│   ├── CameraShake.cs             Camera shake driver
│   ├── ParryImpact.cs             Parry hit VFX/feedback
│   ├── ParryImpulse.cs            Parry timing impulse feel (camera/weapon kick)
│   ├── DashFx.cs / DashImpulse.cs Dash visual + motion feel
│   ├── SlideFx.cs / SlideImpulse.cs  Slide visual + motion feel
│   ├── WallRunFx.cs               Wall-run visual feedback
│   ├── WaterFx.cs                 Water traversal VFX
│   ├── SlashFx.cs                 Melee slash VFX
│   ├── WeaponTrail.cs / WeaponEmber.cs  Player weapon trail/ember VFX
│   ├── ItemVfx.cs                Item use VFX
│   ├── PlayerFeedback.cs          Aggregates hit/damage feedback cues
│   ├── DeathMist.cs               Death screen VFX
│   ├── EnergyGlow.cs / FlickerLight.cs / LightningEffect.cs  Ambient/prop VFX
│   ├── PyreArc.cs                 Pyre/launcher arc VFX (matches jump-arc math)
│   ├── SkyFollower.cs / Starfield.cs  Skybox/background VFX
│
├── UI/                       HUD and menus
│   ├── HUDController.cs          Top-level HUD orchestration
│   ├── BarView.cs                 Base resource bar (drives RectTransform anchors, never fillAmount)
│   ├── FireBarView.cs / FluidBarView.cs / StaminaView.cs / BossBarView.cs  Specific resource bars
│   ├── StatusStripView.cs        Status effect icon strip
│   ├── ItemSlotView.cs           Item slot HUD widget
│   ├── PromptView.cs             Contextual interact/prompt text
│   ├── ScreenFlash.cs            Full-screen flash feedback
│   ├── ControlsInfo.cs           Controls display panel
│   ├── PauseMenu.cs / SettingsMenu.cs / WandSelectMenu.cs  In-game menus
│   └── MainMenuController.cs     Main menu scene controller
│
└── Debug/                    Editor-only / dev-build tooling (see docs/TOOLING.md)
    ├── DebugHarness.cs            Scripted fight/feature runs (Run("parry")/("boss")/("death"))
    ├── FeatureTests.cs            Play-mode feature test suite (asserts shipped-asset values)
    ├── DebugKeys.cs               Dev key bindings (F1/F5/F6/F7/F8/F10, dev blade)
    ├── TestMenu.cs                In-game F1 test menu
    ├── FrameFilm.cs               Frame-by-frame capture/analysis tool
    ├── SandboxController.cs      Sandbox scene driver
    └── SandboxEnemySwitch.cs     Swaps enemy types in the sandbox scene
```

## Editor-only code (`Assets/Editor/`)

Not shown above — factories, builders and tests run only in the Unity Editor (`ProjectSetup`,
`MaterialFactory`, `DataFactory`, `PrefabFactory`, `LevelGreyboxBuilder`, `HudBuilder`, `MainMenuBuilder`,
`MiniBossFactory`, `ForgeClipSplitter`, `SandboxBuilder`, EditMode `Tests/`). See the rebuild pipeline
table in `CLAUDE.md` and `docs/TOOLING.md`.
