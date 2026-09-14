#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Automated play-mode feature suite. Exercises the real systems (no mocking) and produces a
    /// machine-readable PASS/FAIL report so the whole game can be verified without a human playing it.
    ///
    /// Drive it from the editor:
    ///     VibeGame1.FeatureTests.RunAll();                     // start (must already be in play mode)
    ///     VibeGame1.EditorTools.FeatureTestRunner.Poll();      // read progress / final report
    ///
    /// PlayMode NUnit tests are not available in this project (no assembly definitions, so the test
    /// runner cannot see Assembly-CSharp), which is why this is a coroutine driver rather than [UnityTest].
    /// </summary>
    public class FeatureTests : MonoBehaviour
    {
        // ---------------------------------------------------------------- public result surface

        public static bool Done;
        public static bool Running;
        public static int Passed;
        public static int Failed;
        public static int Skipped;
        public static string Report = "";
        public static string CurrentTest = "";

        static FeatureTests inst;
        static readonly StringBuilder sb = new StringBuilder();

        /// <summary>Compact one-line status for polling.</summary>
        public static string Status =>
            $"running={Running} done={Done} passed={Passed} failed={Failed} skipped={Skipped} current='{CurrentTest}'";

        public static void RunAll() => Run(null);

        /// <summary>Run only tests whose name contains <paramref name="filter"/> (case-insensitive).</summary>
        public static void Run(string filter)
        {
            if (!Application.isPlaying)
            {
                Report = "FeatureTests requires play mode.";
                Done = true;
                return;
            }

            if (inst == null)
            {
                var go = new GameObject("~FeatureTests");
                go.hideFlags = HideFlags.DontSave;
                inst = go.AddComponent<FeatureTests>();
            }

            inst.StopAllCoroutines();
            sb.Length = 0;
            Passed = Failed = Skipped = 0;
            Done = false;
            Running = true;
            CurrentTest = "";
            Report = "";
            inst.StartCoroutine(inst.RunSuite(filter));
        }

        // ---------------------------------------------------------------- assertions

        void Section(string name)
        {
            sb.AppendLine();
            sb.AppendLine("== " + name + " ==");
        }

        void Check(string name, bool condition, string detail = null)
        {
            if (condition) Passed++; else Failed++;
            string line = (condition ? "  PASS " : "  FAIL ") + name;
            if (!string.IsNullOrEmpty(detail)) line += "   [" + detail + "]";
            sb.AppendLine(line);
            if (!condition) UnityEngine.Debug.LogWarning("[FeatureTests] " + line);
        }

        void CheckApprox(string name, float actual, float expected, float tolerance)
        {
            bool ok = Mathf.Abs(actual - expected) <= tolerance;
            Check(name, ok, $"actual={actual:0.###} expected={expected:0.###} tol={tolerance:0.###}");
        }

        /// <summary>Record a feature that genuinely cannot be exercised here, with the reason.</summary>
        void Skip(string name, string reason)
        {
            Skipped++;
            sb.AppendLine("  SKIP " + name + "   [" + reason + "]");
        }

        void Note(string text) => sb.AppendLine("       " + text);

        // ---------------------------------------------------------------- waiting helpers

        bool waitTimedOut;

        /// <summary>Every wait in this suite is bounded — a hung test must never hang the editor.</summary>
        IEnumerator WaitUntilOrTimeout(Func<bool> condition, float seconds)
        {
            float end = Time.unscaledTime + Mathf.Max(0.01f, seconds);
            while (Time.unscaledTime < end && !condition()) yield return null;
            waitTimedOut = !condition();
        }

        /// <summary>Realtime wait. Scaled waits are unusable here because hitstop and slow-mo are under test.</summary>
        IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        /// <summary>Wait out any lingering hitstop so the next test starts at timeScale 1.</summary>
        IEnumerator SettleTimeScale()
        {
            yield return WaitUntilOrTimeout(
                () => TimeScaleController.I == null || TimeScaleController.I.WorldScale > 0.99f, 3f);
        }

        // ---------------------------------------------------------------- bound references

        PlayerCombat combat;
        Health health;
        PlayerPosture posture;
        PlayerResources res;
        PlayerStats stats;
        FirstPersonMotor motor;
        PlayerLook look;
        ParryController parry;
        WeaponController weapons;
        WandController wandCtl;
        ExecuteInteractor exec;
        LockOnController lockOn;
        PlayerItems items;
        FlaskAbility flask;
        UltimateAbility ult;
        HUDController hud;
        BossBarView bossBar;
        PlayerStatsData D => GameManager.I != null ? GameManager.I.statsData : null;

        bool Bind()
        {
            combat = FindAnyObjectByType<PlayerCombat>();
            if (combat == null) return false;
            health = combat.GetComponent<Health>();
            posture = combat.GetComponent<PlayerPosture>();
            res = combat.GetComponent<PlayerResources>();
            stats = combat.GetComponent<PlayerStats>();
            motor = combat.GetComponent<FirstPersonMotor>();
            look = combat.GetComponent<PlayerLook>();
            parry = combat.GetComponent<ParryController>();
            weapons = combat.GetComponent<WeaponController>();
            wandCtl = combat.GetComponent<WandController>();
            exec = combat.GetComponent<ExecuteInteractor>();
            lockOn = combat.GetComponent<LockOnController>();
            items = combat.GetComponent<PlayerItems>();
            flask = combat.GetComponent<FlaskAbility>();
            ult = combat.GetComponent<UltimateAbility>();
            hud = FindAnyObjectByType<HUDController>();
            bossBar = FindAnyObjectByType<BossBarView>();
            return true;
        }

        // ---------------------------------------------------------------- shared utilities

        void FacePoint(Vector3 worldPoint)
        {
            Vector3 d = worldPoint - combat.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) return;
            look.SetYaw(Quaternion.LookRotation(d.normalized).eulerAngles.y);
        }

        void FaceAwayFrom(Vector3 worldPoint)
        {
            FacePoint(worldPoint);
            look.SetYaw(combat.transform.eulerAngles.y + 180f);
        }

        void AimAtGrappleDummy(EnemyController target)
        {
            // Unlike melee facing, the grapple tests exercise a narrow 3D crosshair cone. A dummy
            // snapped onto the new descent sits below the player; yaw-only aiming looks over it.
            FacePoint(target.transform.position);
            Vector3 eye = look.Cam != null ? look.Cam.position : combat.transform.position + Vector3.up * 1.6f;
            Vector3 chest = target.transform.position + Vector3.up * (0.9f * Mathf.Max(0.3f, target.transform.localScale.y));
            Vector3 direction = chest - eye;
            float horizontal = new Vector2(direction.x, direction.z).magnitude;
            float pitch = -Mathf.Atan2(direction.y, horizontal) * Mathf.Rad2Deg;
            look.NudgeAim(0f, pitch - look.Pitch);
        }

        static AttackInfo MakeAttack(EnemyController attacker, float damage, bool unblockable)
        {
            return new AttackInfo
            {
                attack = null,          // null is legal: PlayerCombat guards it
                attacker = attacker,
                damage = damage,
                unblockable = unblockable
            };
        }

        /// <summary>Spawn a controllable, non-aggressive grunt near the player for combat tests.</summary>
        IEnumerator SpawnDummy(Vector3 position, Action<EnemyController> onReady)
        {
            // A plain grunt, deliberately. The spawner order FindObjectsByType returns is not stable,
            // and once the level carried Legendary_* spawners (isBoss is false on those) the "first
            // non-boss spawner" was sometimes the Ninja: an imported skinned model whose mark height and
            // bounds are its own, so Deathblow_* framing failed or passed depending on scene order.
            GameObject prefab = null, fallback = null;
            foreach (var s in FindObjectsByType<EnemySpawner>())
            {
                if (s.isBoss || s.prefab == null) continue;
                if (s.prefab.name.StartsWith("Legendary")) { if (fallback == null) fallback = s.prefab; continue; }
                if (prefab == null || s.prefab.name == "Enemy_Grunt") prefab = s.prefab;
                if (s.prefab.name == "Enemy_Grunt") break;
            }
            // 2026-09-06 split: Level_01 places only Sentry_* (parkour_enemies), so the melee Grunt is fetched
            // from the level editor's prefab library, which HudBuilder wires with every enemy key.
            if (prefab == null || prefab.name != "Enemy_Grunt")
            {
                var lib = LevelEditor.I != null ? LevelEditor.I.PrefabFor("Enemy_Grunt") : null;
                if (lib != null) prefab = lib;
            }
            if (prefab == null) prefab = fallback;
            if (prefab == null) { onReady(null); yield break; }

            // Snap onto the NavMesh before spawning. Placing a dummy blindly in front of the player can
            // drop it off a ledge, where it falls into the KillZone and DIES — the execute tests then
            // failed with "target=null" for a reason that had nothing to do with the interactor.
            if (UnityEngine.AI.NavMesh.SamplePosition(position, out var navHit, 6f, UnityEngine.AI.NavMesh.AllAreas))
                position = navHit.position;

            var go = Instantiate(prefab, position, Quaternion.identity);
            go.name = "~TestDummy";
            var e = go.GetComponent<EnemyController>();
            // Start() runs next frame and calls Init(); lock aggro so the dummy never drives its own
            // attacks and contaminates the test we are actually running.
            if (e != null) e.aggroLocked = true;
            yield return null;
            yield return null;
            if (e != null) e.aggroLocked = true;
            onReady(e);
        }

        /// <summary>Full player reset between tests so nothing leaks from one to the next.</summary>
        IEnumerator ResetPlayerState()
        {
            // The wand pedestal stands on the spawn point, so a run can begin with its menu open and
            // time held at zero. Shut it before anything else or every timed wait below stalls.
            WandSelectMenu.ForceClose();
            if (GameManager.I != null) GameManager.I.SetState(GameState.Playing);
            if (health != null) { health.Invulnerable = false; health.ResetFull(); }
            if (posture != null) posture.ResetFull();
            if (res != null) { res.RefillFlask(); res.ConsumePyre(); }
            if (motor != null) { motor.SpeedMultiplier = 1f; motor.CanMove = true; motor.wallSurgeUntil = -99f; motor.CancelPull(); }
            if (motor != null) { var st = motor.GetComponent<PlayerStamina>(); if (st != null) st.ResetFull(); }
            if (parry != null) parry.Cancel();
            if (parry != null) parry.ReleaseGuardOverride();
            if (weapons != null) weapons.CancelAttack();
            if (items != null) ClearItems();
            yield return SettleTimeScale();
        }

        void ClearItems()
        {
            // PlayerItems has no public Clear; the respawn hook does it, but raising that event has
            // wider side effects. Reflect the backing list instead — test-only, deliberately narrow.
            var f = typeof(PlayerItems).GetField("held", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.GetValue(items) is List<ItemData> list)
            {
                list.Clear();
                GameEvents.RaiseItemsChanged(Array.Empty<ItemData>());
            }
            var clear = typeof(PlayerItems).GetMethod("ClearArmedEffects",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (clear != null && items != null) clear.Invoke(items, null);
        }

        /// <summary>A throwaway item with the class-default tuning (28 m / 12 deg / 0.35 s hook, 8 s surge).</summary>
        static ItemData MakeItem(ItemEffect effect)
        {
            var it = ScriptableObject.CreateInstance<ItemData>();
            it.displayName = "Test " + effect;
            it.shortLabel = effect.ToString().ToUpperInvariant();
            it.effect = effect;
            it.color = Color.white;
            return it;
        }

        sealed class WorldEnemyState
        {
            public GameObject instance;
            public EnemyController controller;
            public bool active;
            public bool aggroLocked;
        }

        /// <summary>
        /// Guard, lock-on, deathblow, item and flask checks need a controlled attacker or target. Keep authored
        /// enemies out of those stages while leaving dummies spawned by the test itself available.
        /// Lock aggro before deactivation because an authored volley invokes its shooter directly.
        /// </summary>
        List<WorldEnemyState> IsolateWorldEnemies()
        {
            var state = new List<WorldEnemyState>();
            foreach (var spawner in FindObjectsByType<EnemySpawner>())
            {
                var instance = spawner.Instance;
                if (instance == null) continue;
                var controller = instance.GetComponent<EnemyController>();
                state.Add(new WorldEnemyState
                {
                    instance = instance,
                    controller = controller,
                    active = instance.activeSelf,
                    aggroLocked = controller != null && controller.aggroLocked
                });
                if (controller != null) controller.aggroLocked = true;
                instance.SetActive(false);
            }

            // A bolt launched before isolation is still an authored world attacker. Remove it from the
            // registry through normal destruction so it cannot land during a timing assertion.
            foreach (var bolt in FindObjectsByType<Projectile>())
                if (bolt != null) Destroy(bolt.gameObject);
            return state;
        }

        static void RestoreWorldEnemies(List<WorldEnemyState> state)
        {
            if (state == null) return;
            foreach (var saved in state)
            {
                if (saved.instance == null) continue;
                saved.instance.SetActive(saved.active);
                if (saved.controller != null) saved.controller.aggroLocked = saved.aggroLocked;
            }
        }

        static bool NeedsQuietWorld(string testName)
        {
            return testName == "ParryLive" || testName == "Guard" || testName == "PlayerPosture" ||
                   testName == "LockOn" || testName == "Deathblow" || testName == "Items" ||
                   testName == "Flask" || testName == "WandReadability";
        }

        // ---------------------------------------------------------------- suite driver

        IEnumerator RunSuite(string filter)
        {
            float t0 = Time.unscaledTime;
            WandSelectMenu.ForceClose();   // see ResetPlayerState: the spawn pedestal may have opened it
            sb.AppendLine("VibeGame1 feature test run — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            if (!Bind())
            {
                sb.AppendLine("FATAL: no PlayerCombat in the scene. Is the level built and playing?");
                Finish(t0);
                yield break;
            }
            if (D == null)
            {
                sb.AppendLine("FATAL: GameManager.statsData is null.");
                Finish(t0);
                yield break;
            }

            var tests = new List<KeyValuePair<string, Func<IEnumerator>>>
            {
                Test("Movement",        TestMovement),
                Test("SlideWallJump",   TestSlideAndWallJump),
                Test("WallRunLive",     TestWallRunLive),
                Test("HitstopScoping",  TestHitstopScoping),
                Test("Stamina",         TestStamina),
                Test("PerfectTiming",   TestPerfectTiming),
                Test("LevelEditor",     TestLevelEditor),
                Test("ParryMathPure",   TestParryMathPure),
                Test("ParryLive",       TestParryLive),
                Test("Guard",           TestGuard),
                Test("PlayerPosture",   TestPlayerPosture),
                Test("EnemyExecute",    TestEnemyPostureAndExecute),
                Test("Weapons",         TestWeapons),
                Test("WandPedestal",    TestWandPedestal),
                Test("WandReadability", TestWandReadability),
                Test("ViewmodelArms",   TestViewmodelArms),
                Test("TellReadability", TestTellReadability),
                Test("WindupPoses",     TestWindupPoses),
                Test("Deathblow",       TestDeathblowMarker),
                Test("FlaskPunish",     TestFlaskPunish),
                Test("Flare",           TestFlare),
                Test("PromptChannels",  TestPromptChannels),
                Test("LockOn",          TestLockOn),
                Test("Items",           TestItems),
                Test("Flask",           TestFlask),
                Test("Ultimate",        TestUltimate),
                Test("Progression",     TestProgression),
                Test("LevelFlow",       TestLevelFlow),
                Test("LevelStructure",  TestLevelStructure),
                Test("Legendaries",     TestLegendaries),
                Test("V18Smoke",        TestV18Smoke),
                Test("GateLoop",        TestGateLoop),
                Test("Boss",            TestBoss),
                Test("HUD",             TestHud),
                Test("MainMenu",        TestMainMenu),
                Test("Audio",           TestAudio),
            };

            foreach (var t in tests)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    t.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                CurrentTest = t.Key;
                Section(t.Key);
                List<WorldEnemyState> worldEnemies = NeedsQuietWorld(t.Key) ? IsolateWorldEnemies() : null;
                var enumerator = t.Value();
                while (true)
                {
                    // A throwing test must not take the whole suite down with it. The yield sits
                    // outside the try because C# forbids yield inside a try that has a catch.
                    try
                    {
                        if (!enumerator.MoveNext()) break;
                    }
                    catch (Exception ex)
                    {
                        Check(t.Key + " (unhandled exception)", false, ex.GetType().Name + ": " + ex.Message);
                        UnityEngine.Debug.LogException(ex);
                        break;
                    }
                    yield return enumerator.Current;
                }

                yield return ResetPlayerState();
                RestoreWorldEnemies(worldEnemies);
            }

            CurrentTest = "";
            Finish(t0);
        }

        static KeyValuePair<string, Func<IEnumerator>> Test(string name, Func<IEnumerator> body)
            => new KeyValuePair<string, Func<IEnumerator>>(name, body);

        void Finish(float t0)
        {
            sb.AppendLine();
            sb.AppendLine($"RESULT  passed={Passed}  failed={Failed}  skipped={Skipped}  " +
                          $"({Time.unscaledTime - t0:0.0}s)");
            sb.AppendLine(Failed == 0 ? "SUITE PASS" : "SUITE FAIL");
            Report = sb.ToString();
            Running = false;
            Done = true;
            UnityEngine.Debug.Log("[FeatureTests]\n" + Report);
        }

        // ================================================================ 1. MOVEMENT

        IEnumerator TestMovement()
        {
            Vector3 start = combat.transform.position;

            // Grounding: teleport to the level start and let the motor settle onto the floor.
            var spawn = LevelManager.I != null ? LevelManager.I.startSpawn : null;
            Vector3 ground = spawn != null ? spawn.position : start;
            motor.Teleport(ground + Vector3.up * 0.5f, 0f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            Check("Movement_LandsAndGrounds", !waitTimedOut, "IsGrounded=" + motor.IsGrounded);

            // Gravity sign — a positive gravity would make the whole platformer unplayable.
            Check("Movement_GravityIsNegative", motor.gravity < 0f, "gravity=" + motor.gravity);

            // The jump key is read inside FirstPersonMotor.Update from InputReader, so it cannot be
            // pressed from script. Launch() uses the identical velocity path, so we launch at the
            // jump's own takeoff speed and measure the arc.
            //
            // NOTE: with no input, InputReader.JumpHeld is false, so the motor's variable-jump
            // "jump cut" extra gravity applies for the whole ascent. The apex is therefore the
            // SHORT HOP height, jumpHeight / (1 + jumpCutGravityMultiplier) — not jumpHeight.
            // That makes this a genuine test of the variable-jump-height feature.
            float jumpV = Mathf.Sqrt(2f * -motor.gravity * motor.jumpHeight);
            float expectedShortHop = motor.jumpHeight / (1f + motor.jumpCutGravityMultiplier);
            float baseY = combat.transform.position.y;
            motor.Launch(jumpV);
            float apex = baseY;
            float end = Time.unscaledTime + 3f;
            while (Time.unscaledTime < end)
            {
                apex = Mathf.Max(apex, combat.transform.position.y);
                if (motor.IsGrounded && combat.transform.position.y <= baseY + 0.05f && apex > baseY + 0.05f) break;
                yield return null;
            }
            CheckApprox("Movement_ShortHopHeight_JumpCutApplies", apex - baseY, expectedShortHop, 0.4f);
            Note($"Full-height jump ({motor.jumpHeight:0.0}m) requires the jump key held; unreachable without input.");

            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);

            // Launch() has no shipped caller any more (the Updraft item is gone) but stays as motor API.
            motor.Launch(20f);
            yield return null;
            Check("Movement_LaunchSetsUpwardVelocity", motor.Velocity.y > 10f, "velY=" + motor.Velocity.y.ToString("0.0"));
            yield return WaitUntilOrTimeout(() => !motor.IsGrounded, 1f);
            Check("Movement_LaunchLeavesGround", !waitTimedOut, "grounded=" + motor.IsGrounded);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 5f);

            // AddImpulse is what knockback and the item tests rely on.
            Vector3 before = combat.transform.position;
            motor.AddImpulse(combat.transform.forward * 6f);
            yield return WaitRealtime(0.3f);
            Check("Movement_AddImpulseMoves",
                Vector3.Distance(before, combat.transform.position) > 0.2f,
                "moved=" + Vector3.Distance(before, combat.transform.position).ToString("0.00"));

            // Teleport must not leave residual velocity (it disables/re-enables the CharacterController).
            motor.Teleport(ground + Vector3.up * 0.2f, 90f);
            yield return null;
            CheckApprox("Movement_TeleportClearsVelocity", new Vector2(motor.Velocity.x, motor.Velocity.z).magnitude, 0f, 0.5f);
            CheckApprox("Movement_TeleportSetsYaw", Mathf.DeltaAngle(combat.transform.eulerAngles.y, 90f), 0f, 1f);

            // SpeedMultiplier is the generic item-speed hook; nothing shipped drives it today.
            motor.SpeedMultiplier = 1.35f;
            Check("Movement_SpeedMultiplierSettable", Mathf.Approximately(motor.SpeedMultiplier, 1.35f));
            motor.SpeedMultiplier = 1f;

            // ---- coyote time / jump buffer / dash ----------------------------------------------
            // Driven through the motor's public entry points (TryJump / TryDash) — the very methods
            // Update calls when InputReader reports a press, so this is the real code path.
            int jumps = 0, dashes = 0;
            Action onJump = () => jumps++;
            Action onDash = () => dashes++;
            motor.OnJumped += onJump;
            motor.OnDashed += onDash;

            motor.Teleport(ground + Vector3.up * 0.3f, 0f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);

            // Jump buffer FIRST, while no earlier press is pending: press while falling (the coyote
            // window is already gone — Launch clears lastGroundedTime) and the jump must fire on
            // landing. The window is widened so the press lands deterministically inside it; the
            // shipped 0.12s constant is asserted in the EditMode suite.
            float savedBuffer = motor.jumpBuffer;
            motor.jumpBuffer = 0.6f;
            // 14 m/s, not 8: with the jump key unheld the jump-cut gravity more than doubles the
            // descent rate, and a short hop lands before the press can be made in mid-air.
            motor.Launch(14f);
            yield return WaitUntilOrTimeout(() => !motor.IsGrounded, 1.5f);
            float launchedAt = Time.time;
            yield return WaitRealtime(0.3f);
            int jumpsBeforePress = jumps;
            motor.TryJump();
            float pressedAt = Time.time;
            yield return null; yield return null;
            bool noMidairJump = jumps == jumpsBeforePress && !motor.IsGrounded;
            yield return WaitUntilOrTimeout(() => jumps > jumpsBeforePress || motor.IsGrounded, 1.5f);
            float pressToLanding = Time.time - pressedAt, airtime = Time.time - launchedAt;
            yield return null; yield return null;
            bool firedOnLanding = jumps == jumpsBeforePress + 1;
            motor.jumpBuffer = savedBuffer;
            Check("Movement_JumpBuffer", noMidairJump && firedOnLanding,
                $"noMidairJump={noMidairJump} firedOnLanding={firedOnLanding} " +
                $"pressToLanding={pressToLanding:0.00}s airtime={airtime:0.00}s buffer=0.60");

            // Coyote: leave the ground WITHOUT jumping (a nudge does not touch lastGroundedTime),
            // then jump inside the window; repeat outside the window and it must be refused.
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 5f);
            int jumpsBeforeCoyote = jumps;
            motor.AddImpulse(Vector3.up * 4f);
            yield return WaitUntilOrTimeout(() => !motor.IsGrounded, 1.5f);
            motor.TryJump();
            yield return null; yield return null;
            bool jumpedInsideCoyote = jumps == jumpsBeforeCoyote + 1;

            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 4f);
            motor.AddImpulse(Vector3.up * 12f);
            yield return WaitUntilOrTimeout(() => !motor.IsGrounded, 1.5f);
            yield return WaitRealtime(motor.coyoteTime + 0.12f);
            bool airborneAtLatePress = !motor.IsGrounded;
            motor.TryJump();   // leaves a pending press: the default 0.12s buffer expires before landing
            yield return null; yield return null;
            bool refusedOutsideCoyote = jumps == jumpsBeforeCoyote + 1;
            Check("Movement_CoyoteTime", jumpedInsideCoyote && airborneAtLatePress && refusedOutsideCoyote,
                $"insideWindow={jumpedInsideCoyote} airborneAtLatePress={airborneAtLatePress} jumps={jumps}");

            // Dash: one on the ground, one in the air, and the air charge does not come back until
            // landing — even once the cooldown (shortened here) has elapsed.
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 6f);
            float savedCooldown = motor.dashCooldown;
            motor.dashCooldown = 0.02f;
            motor.TryDash();
            yield return null; yield return null;
            bool groundDashed = dashes == 1 && motor.HorizontalSpeed > motor.dashSpeed * 0.5f;
            yield return WaitUntilOrTimeout(() => !motor.IsDashing, 1.5f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 4f);

            motor.Launch(10f);
            yield return WaitUntilOrTimeout(() => !motor.IsGrounded, 1.5f);
            yield return WaitRealtime(0.15f);
            motor.TryDash();
            yield return null; yield return null;
            bool airDashed = dashes == 2;
            yield return WaitUntilOrTimeout(() => !motor.IsDashing, 1.5f);
            bool airborneAtSecondDash = !motor.IsGrounded;
            motor.TryDash();
            yield return null; yield return null;
            bool secondAirDashRefused = dashes == 2;
            motor.dashCooldown = savedCooldown;
            motor.OnJumped -= onJump;
            motor.OnDashed -= onDash;
            Check("Movement_DashAndAirDashOnce",
                groundDashed && airDashed && airborneAtSecondDash && secondAirDashRefused,
                $"ground={groundDashed} air={airDashed} airborneAtRetry={airborneAtSecondDash} dashes={dashes}");

            // A dash can end anywhere; put the player back on known ground for the next section.
            motor.Teleport(ground + Vector3.up * 0.3f, 0f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);

            // ---- FORGIVENESS (MOVEMENT-PRINCIPLES rule 4) -------------------------------------
            // A rig far from the level: a floor and a 2 m ledge. Falling toward the ledge with the feet
            // 0.15 m under its top must land ON it (the near-miss catch); 0.5 m under must not; falling
            // along a tall wall's side must never be lifted.
            var fFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fFloor.name = "~TestFloor_Forgive"; fFloor.layer = 0;
            fFloor.transform.position = new Vector3(400f, -60f, 200f);
            fFloor.transform.localScale = new Vector3(40f, 1f, 40f);
            var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ledge.name = "~TestLedge_Forgive"; ledge.layer = 0;
            ledge.transform.position = new Vector3(400f, -58.5f, 200f);       // top at -57.5
            ledge.transform.localScale = new Vector3(6f, 2f, 6f);
            float ledgeTop = -57.5f;
            Physics.SyncTransforms();

            // Near miss: feet 0.05 m under the top, 0.2 m before the edge (x = 397), moving +x at 6 m/s.
            // 0.5 m before it (the first cut) let gravity drop the feet under the 0.22 m band before
            // the edge arrived - a real miss, not a near one.
            motor.Teleport(new Vector3(396.8f, ledgeTop - 0.05f, 200f), 90f);
            yield return null;
            motor.AddImpulse(new Vector3(6f, 0f, 0f));
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 1.5f);
            // Caught = standing on the ledge top. The capsule (radius 0.4) can stand with its centre a little
            // before the edge at x 397, so the x check is the radius, not the edge.
            bool caught = motor.IsGrounded && combat.transform.position.y > ledgeTop - 0.05f && combat.transform.position.x > 396.6f;
            Check("Forgive_NearMissLandsOnTheLedge", caught,
                $"grounded={motor.IsGrounded} y={combat.transform.position.y:0.00} (top {ledgeTop}) x={combat.transform.position.x:0.0}");

            // A real miss: 0.5 m under the top must end on the floor, not the ledge.
            motor.Teleport(new Vector3(396.5f, ledgeTop - 0.5f, 200f), 90f);
            yield return null;
            motor.AddImpulse(new Vector3(6f, 0f, 0f));
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 2.5f);
            // A miss = never standing on the ledge top. Measured 2026-09-05: the capsule pressed into the
            // ledge's side by the impulse hovers at y -58.1 for the whole wait instead of sliding to the
            // floor, so "grounded on the floor" is not the right proof of a miss; "not on the ledge" is.
            bool onLedge = motor.IsGrounded && combat.transform.position.y > ledgeTop - 0.05f && combat.transform.position.x > 396.6f;
            bool missed = !onLedge && combat.transform.position.y < ledgeTop - 0.4f;
            Check("Forgive_AHalfMetreMissIsAMiss", missed,
                $"grounded={motor.IsGrounded} y={combat.transform.position.y:0.00} (top {ledgeTop})");

            // Falling along the ledge's side wall must never be lifted: y only ever decreases.
            motor.Teleport(new Vector3(396.6f, ledgeTop + 1.2f, 196.6f), 90f);
            yield return null;
            motor.AddImpulse(new Vector3(0f, 0f, 2f));   // sideways along the face at x ~397
            float prevY = combat.transform.position.y; bool everRose = false;
            for (int f = 0; f < 40 && !motor.IsGrounded; f++)
            {
                yield return null;
                float y = combat.transform.position.y;
                if (y > prevY + 0.001f) everRose = true;
                prevY = y;
            }
            Check("Forgive_AWallSideNeverCatches", !everRose, $"rose={everRose} y={combat.transform.position.y:0.00}");

            UnityEngine.Object.Destroy(ledge); UnityEngine.Object.Destroy(fFloor);
            motor.Teleport(ground + Vector3.up * 0.3f, 0f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
        }


        // ================================================================ 1b. SLIDE / WALL JUMP
        //
        // The two movement techs, driven through the motor's own public entry points (TrySlide /
        // TryWallJump) - the exact methods Update calls when InputReader reports a press. No synthesised
        // device, and therefore no skip.

        // ================================================================ WALL RUN, LIVE

        /// <summary>
        /// The first LIVE wall run in the suite (everything before this was WallRunMath in EditMode): a
        /// rig floor and one long face beside it, a launch along the face, and then the things a player
        /// sees — the run attaches, grit leaves the feet while it lasts, sparks mark the foot-ticks, and
        /// the grit is gone shortly after the wall lets go. A particle count is a state-machine fact,
        /// never a claim that it looks right; that is the user's.
        /// </summary>
        IEnumerator TestWallRunLive()
        {
            var rig = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rig.name = "~TestFloor_WallRun";
            rig.layer = 0;
            rig.transform.position = new Vector3(300f, -40f, 200f);
            rig.transform.localScale = new Vector3(60f, 1f, 60f);
            Vector3 pad = new Vector3(280f, -39.5f, 200f);
            motor.Teleport(pad + Vector3.up * 0.4f, 90f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            Vector3 fwd = combat.transform.forward, right = combat.transform.right;
            // One face on the RIGHT, 0.75 m off the capsule axis: inside the 0.55 m scan past the 0.4 m
            // capsule, running the length of the launch.
            var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
            face.name = "~TestWallRunFace";
            face.layer = 0;
            face.transform.position = pad + fwd * 10f + right * 0.75f + Vector3.up * 3.5f;
            face.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            face.transform.localScale = new Vector3(0.3f, 8f, 24f);
            yield return null;

            var st = motor.GetComponent<PlayerStamina>();
            if (st != null) st.ResetFull();
            // Arrive: airborne, 9 m/s along the face (over the 6 m/s entry gate), looking down the line.
            motor.Launch(3f);
            motor.AddImpulse(fwd * 9f);
            yield return WaitUntilOrTimeout(() => motor.IsWallRunning, 1.5f);
            Check("WallRunLive_Attaches", !waitTimedOut, "running=" + motor.IsWallRunning + " speed=" + motor.HorizontalSpeed.ToString("0.0")
                + " grounded=" + motor.IsGrounded + " (entry gate " + motor.wallRunMinEntrySpeed + " m/s)");
            // PlayerFeedback builds its FX objects lazily on the first dash / slide / wall run, so the
            // lookup comes AFTER the catch. SparksThrown counts from Build, which is this run or earlier.
            var fx = FindAnyObjectByType<WallRunFx>();
            Check("WallRunLive_FxExists", fx != null, "PlayerFeedback.EnsureFx builds WallRunFx on the catch");
            if (fx == null) { UnityEngine.Object.Destroy(face); UnityEngine.Object.Destroy(rig); yield break; }
            int sparks0 = 0;

            int peakGrit = 0; float ran = 0f; float t0 = Time.unscaledTime;
            var look = FindAnyObjectByType<PlayerLook>();
            float peakRoll = 0f;   // signed: the face is on the RIGHT, so the lean must be POSITIVE (head tilts left, away)
            while (motor.IsWallRunning && Time.unscaledTime - t0 < 3f)
            {
                peakGrit = Mathf.Max(peakGrit, fx.GritAlive);
                if (look != null && Mathf.Abs(look.Roll) > Mathf.Abs(peakRoll)) peakRoll = look.Roll;
                ran = Time.unscaledTime - t0;
                yield return null;
            }
            Check("WallRunLive_LeansAwayFromTheWall", look == null || peakRoll > 2f,
                "roll=" + peakRoll.ToString("0.0") + " deg with the face on the RIGHT (positive = head tilts left = away; " +
                "it shipped toward the wall once and was played as reversed)");
            Check("WallRunLive_ShedsGrit", peakGrit > 0, "peak motes alive=" + peakGrit + " over a " + ran.ToString("0.00") + " s run");
            Check("WallRunLive_GritIsAPool", peakGrit <= fx.gritCount, "alive=" + peakGrit + " pool=" + fx.gritCount);
            Check("WallRunLive_SparksOnSteps", fx.SparksThrown > sparks0,
                "sparks " + sparks0 + " -> " + fx.SparksThrown + " over " + ran.ToString("0.00") + " s (a foot-tick every "
                + (GameManager.I != null && GameManager.I.feel != null ? GameManager.I.feel.wallRunStepDistance : 1.6f) + " m)");
            Note("MEASURED wall-run particles: peak " + peakGrit + " motes, " + (fx.SparksThrown - sparks0) + " sparks, run "
                + ran.ToString("0.00") + " s, ended " + motor.LastWallRunEnd + ".");

            // The wall let go (or was jumped): the grit is left behind and winks out within its life.
            yield return WaitRealtime(fx.gritLife + 0.25f);
            Check("WallRunLive_GritClearsAfterRun", fx.GritAlive == 0, "alive=" + fx.GritAlive + " " + (fx.gritLife + 0.25f).ToString("0.00") + " s after the run");

            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 4f);
            UnityEngine.Object.Destroy(face);
            UnityEngine.Object.Destroy(rig);
            yield return null;
        }

        /// <summary>
        /// The in-game level editor, driven through its public API: enter, place a platform, save, start a
        /// new document, load the save back, find the platform rebuilt, leave. Runs over the campaign
        /// scene with the scene roots left visible (hideSceneRootsWhileEditing off) so the tests after it
        /// meet the level exactly as they did before.
        /// </summary>
        IEnumerator TestLevelEditor()
        {
            var ed = LevelEditor.I;
            Check("LevelEditor_OnHud", ed != null, "no LevelEditor on the HUD - run VibeGame1/5. Build HUD");
            if (ed == null) yield break;

            bool hid = ed.hideSceneRootsWhileEditing;
            ed.hideSceneRootsWhileEditing = false;
            Vector3 before = motor.transform.position;
            DeveloperAccess.UnlockForTests();
            try
            {
                bool entered = ed.Enter();
                yield return null;
                Check("LevelEditor_Enters", entered && ed.CurrentMode == LevelEditor.Mode.Editing && GameManager.IsEditing,
                    "entered=" + entered + " mode=" + ed.CurrentMode + " editing=" + GameManager.IsEditing);
                int start = ed.Document.platforms.Count;

                ed.SelectKind(LevelPieceKind.Platform);
                ed.SetPendingSize(6f);
                var placed = ed.Place(new Vector3(400f, 20f, 400f), Vector3.up);
                yield return null;
                Check("LevelEditor_PlacesAPlatform", placed != null && ed.Document.platforms.Count == start + 1
                    && Mathf.Abs(placed.transform.position.y - 19.5f) < 0.01f,
                    "placed=" + (placed != null) + " count=" + ed.Document.platforms.Count + (placed != null ? " y=" + placed.transform.position.y : ""));
                Check("LevelEditor_PieceIsTagged", placed != null && placed.GetComponent<LevelPiece>() != null
                    && placed.GetComponent<LevelPiece>().kind == LevelPieceKind.Platform,
                    "the factory must tag what it builds");
                Check("LevelEditor_TrimIsBuilt", placed != null && placed.transform.Find(placed.name + "_Trim_N") != null,
                    "the platform's trim bars are the factory's, same as menu item 8");

                ed.Save("~featuretest");
                string path = LevelEditor.PathFor("~featuretest");
                Check("LevelEditor_SavesJson", System.IO.File.Exists(path), path);

                ed.NewDocument("~featuretest_other");
                yield return null;
                Check("LevelEditor_NewDocumentClears", ed.Document.platforms.Count == 1 && ed.CustomRoot != null
                    && ed.CustomRoot.Find("Platform_1") == null, "count=" + ed.Document.platforms.Count);

                bool loaded = ed.Load("~featuretest");
                yield return null;
                var back = ed.CustomRoot != null ? ed.CustomRoot.Find("Platform_" + start) : null;
                Check("LevelEditor_LoadRebuildsThePlatform", loaded && ed.Document.platforms.Count == start + 1 && back != null
                    && (back.position - new Vector3(400f, 19.5f, 400f)).magnitude < 0.01f,
                    "loaded=" + loaded + " count=" + ed.Document.platforms.Count + " found=" + (back != null));

                var doc = ed.Document;
                string json = doc.ToJson();
                var round = LevelDocument.FromJson(json);
                Check("LevelEditor_JsonRoundTrip", round != null && round.platforms.Count == doc.platforms.Count
                    && round.levelId == doc.levelId, "round trip through JsonUtility");
            }
            finally
            {
                ed.Exit();
                ed.hideSceneRootsWhileEditing = hid;
                DeveloperAccess.LockForTests();
                try { System.IO.File.Delete(LevelEditor.PathFor("~featuretest")); } catch (System.Exception) { }
            }
            yield return null;
            Check("LevelEditor_ExitRestoresPlay", ed.CurrentMode == LevelEditor.Mode.Off && GameManager.IsPlaying
                && ed.CustomRoot == null && (motor.transform.position - before).magnitude < 0.5f,
                "mode=" + ed.CurrentMode + " playing=" + GameManager.IsPlaying + " pos=" + motor.transform.position);
        }

        IEnumerator TestSlideAndWallJump()
        {
            var cc = combat.GetComponent<CharacterController>();
            if (cc == null) { Check("Slide_CharacterControllerPresent", false); yield break; }
            float standH = motor.StandHeight;

            // A PURPOSE-BUILT runway, parked far outside the course. Measuring on level geometry looked
            // cheaper and was wrong three times over: the boss approach is 6 m wide, so a 16 m/s slide
            // reached its rail in 1.8 m and reported a 0.11 s slide, and its rails sat inside the wall
            // check, so "no wall in range" found one and "the same wall twice" found the other rail.
            // A measurement rig has to own its own space.
            var rig = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rig.name = "~TestFloor";
            rig.layer = 0;
            rig.transform.position = new Vector3(300f, 40f, 0f);
            rig.transform.localScale = new Vector3(60f, 1f, 60f);
            Vector3 pad = new Vector3(280f, 40.5f, 0f);
            motor.Teleport(pad + Vector3.up * 0.4f, 90f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            Check("Slide_RunwayIsGround", !waitTimedOut, "grounded=" + motor.IsGrounded + " at " + pad);

            // ---- a slide is a MOMENTUM move ---------------------------------------------------
            bool refusedIdle = !motor.TrySlide();
            Check("Slide_RefusedFromStandstill", refusedIdle && !motor.IsSliding,
                $"speed={motor.HorizontalSpeed:0.0} minEntry={motor.slideMinEntrySpeed:0.0}");

            // ---- entry boosts, and drops the collider -----------------------------------------
            motor.AddImpulse(combat.transform.forward * motor.groundSpeed);
            yield return null;
            float speedBefore = motor.HorizontalSpeed;
            bool started = motor.TrySlide();
            yield return null;
            float speedAfter = motor.HorizontalSpeed;
            Check("Slide_StartsAtRunningSpeed", started && motor.IsSliding,
                $"started={started} sliding={motor.IsSliding} speedBefore={speedBefore:0.0}");
            Check("Slide_AddsSpeed", speedAfter > speedBefore + 1f,
                $"before={speedBefore:0.0} after={speedAfter:0.0} boost={motor.slideBoost:0.0}");
            Check("Slide_LowersTheCollider", cc.height < standH - 0.4f,
                $"height={cc.height:0.00} stand={standH:0.00} slideHeight={motor.slideHeight:0.00}");

            // ---- it ENDS, and covers a measurable distance doing it ----------------------------
            Vector3 slideFrom = combat.transform.position;
            float slideStarted = Time.unscaledTime;
            int slideFrames = 0, groundedFrames = 0;
            float worstDt = 0f;
            while (motor.IsSliding && Time.unscaledTime - slideStarted < 3f)
            {
                slideFrames++;
                if (motor.IsGrounded) groundedFrames++;
                worstDt = Mathf.Max(worstDt, TimeScaleController.PlayerDelta);
                yield return null;
            }
            waitTimedOut = motor.IsSliding;
            float slideSeconds = Time.unscaledTime - slideStarted;
            Vector3 d3 = combat.transform.position - slideFrom;
            float slideMeters = new Vector2(d3.x, d3.z).magnitude;
            Check("Slide_Ends", !waitTimedOut, $"stillSliding={motor.IsSliding} after {slideSeconds:0.00}s");
            Check("Slide_CoversGroundThenStops", slideMeters > 1.5f && slideMeters < 12f,
                $"distance={slideMeters:0.00}m over {slideSeconds:0.00}s");
            Note($"MEASURED slide: {slideMeters:0.00} m in {slideSeconds:0.00} s " +
                 $"(entry {speedAfter:0.0} m/s, floor {motor.slideEndSpeed:0.0} m/s, exit {motor.HorizontalSpeed:0.0} m/s, " +
                 $"{groundedFrames}/{slideFrames} frames grounded, worst dt {worstDt:0.000} s).");
            yield return null;
            Check("Slide_RestoresStandingHeight", Mathf.Abs(cc.height - standH) < 0.01f,
                $"height={cc.height:0.00} stand={standH:0.00}");

            // ---- THE tech: cancelling a slide into a jump keeps the slide's speed ---------------
            // If this ever regresses, sliding becomes a dodge instead of a route and every fast line
            // authored in the level stops being reachable.
            motor.Teleport(pad + Vector3.up * 0.4f, 0f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            // WAIT the cooldown out; do not zero the field. slideCooldown is read once, when a slide
            // ENDS, to stamp the next ready time - setting it to 0 afterwards changes nothing, the
            // slide is silently refused, and the report then reads as though the tech does not work.
            yield return WaitRealtime(motor.slideCooldown + 0.1f);
            motor.AddImpulse(combat.transform.forward * motor.groundSpeed);
            bool cancelSlideStarted = motor.TrySlide();   // same frame: friction never gets a bite
            Check("Slide_StartsAgainAfterCooldown", cancelSlideStarted,
                "cooldown=" + motor.slideCooldown.ToString("0.00") + "s");
            yield return null;
            float speedInSlide = motor.HorizontalSpeed;
            Vector3 jumpFrom = combat.transform.position;
            motor.TryJump();
            yield return null; yield return null;
            float speedAfterJump = motor.HorizontalSpeed;
            bool leftGround = !motor.IsGrounded || motor.Velocity.y > 1f;
            Check("Slide_JumpCancelKeepsMomentum",
                leftGround && !motor.IsSliding && speedAfterJump > motor.groundSpeed + 1f,
                $"inSlide={speedInSlide:0.0} afterJump={speedAfterJump:0.0} run={motor.groundSpeed:0.0} " +
                $"airborne={leftGround} sliding={motor.IsSliding}");

            // Measure the actual slide-jump: this number is what the level's fast lines are authored to.
            float apexY = jumpFrom.y;
            float flightEnd = Time.unscaledTime + 4f;
            yield return null;
            while (Time.unscaledTime < flightEnd)
            {
                apexY = Mathf.Max(apexY, combat.transform.position.y);
                if (motor.IsGrounded) break;
                yield return null;
            }
            Vector3 land = combat.transform.position - jumpFrom;
            Note($"MEASURED slide-jump: {new Vector2(land.x, land.z).magnitude:0.00} m horizontal, " +
                 $"apex +{apexY - jumpFrom.y:0.00} m, takeoff {speedAfterJump:0.0} m/s " +
                 $"(a run-jump at {motor.groundSpeed:0.0} m/s covers ~{motor.groundSpeed * 0.8f:0.0} m).");

            // ---- leaving the ground ends the slide, but never the momentum ---------------------
            motor.Teleport(pad + Vector3.up * 0.4f, 0f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            yield return WaitRealtime(motor.slideCooldown + 0.1f);
            motor.AddImpulse(combat.transform.forward * motor.groundSpeed);
            motor.TrySlide();
            yield return null;
            float beforeLaunch = motor.HorizontalSpeed;
            motor.Launch(8f);
            yield return null;
            Check("Slide_EndsWhenAirborneButKeepsSpeed",
                !motor.IsSliding && motor.HorizontalSpeed > beforeLaunch - 1.5f,
                $"sliding={motor.IsSliding} before={beforeLaunch:0.0} after={motor.HorizontalSpeed:0.0}");
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 5f);

            // ---- WALL JUMP --------------------------------------------------------------------
            motor.Teleport(pad + Vector3.up * 0.4f, 0f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);

            Check("WallJump_RefusedOnTheGround", !motor.TryWallJump(),
                "grounded=" + motor.IsGrounded);

            // Airborne in the open: nothing to push off, so nothing happens. This is the check that
            // stops a wall jump degrading into a free double jump.
            motor.Launch(9f);
            yield return WaitUntilOrTimeout(() => !motor.IsGrounded, 1.5f);
            yield return WaitRealtime(motor.coyoteTime + 0.1f);
            Vector3 sniffed;
            bool noWallHere = !motor.FindWall(out sniffed);
            bool refusedInOpenAir = !motor.TryWallJump();
            Check("WallJump_RefusedWithNoWallInRange", noWallHere && refusedInOpenAir,
                $"foundWall={!noWallHere} fired={!refusedInOpenAir}");
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 5f);

            // A purpose-built chimney, so this test does not depend on the level's geometry surviving
            // an edit. Destroyed at the end - a survivor would become runtime debris in the scene file.
            Vector3 c = combat.transform.position;
            var wallA = TestWall("~TestWall_A", c + combat.transform.right * 1.0f + Vector3.up * 3f);
            var wallB = TestWall("~TestWall_B", c - combat.transform.right * 1.0f + Vector3.up * 3f);
            yield return null;

            int wallJumps = 0;
            Action onWall = () => wallJumps++;
            motor.OnWallJumped += onWall;

            // one wall, twice in a row: the second must be refused or a single face is a free ladder
            motor.Teleport(c + Vector3.up * 0.4f, 0f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            UnityEngine.Object.DestroyImmediate(wallB);
            motor.Launch(7f);
            yield return WaitUntilOrTimeout(() => !motor.IsGrounded, 1.5f);
            yield return WaitRealtime(motor.coyoteTime + 0.1f);
            int before1 = wallJumps;
            bool first = motor.TryWallJump();
            yield return null;
            bool second = motor.TryWallJump();
            yield return null;
            Check("WallJump_FiresOffAWall", first && wallJumps == before1 + 1,
                $"fired={first} count={wallJumps - before1}");
            Check("WallJump_SameWallRefusedTwiceRunning", !second,
                $"secondFired={second} cosLimit={motor.sameWallCosineLimit:0.00}");
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 6f);

            // one wall jump, measured in isolation: this is the number the level's chimney is authored to
            motor.Teleport(c + Vector3.up * 0.4f, 0f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            // Launch high enough to still be airborne past coyote time, and drift INTO the wall - the
            // scan reaches ~0.5 m past the capsule, and a player hanging in the middle of a chimney is
            // not near anything. A 2 m/s hop landed before the press and measured nothing.
            motor.Launch(9f);
            motor.AddImpulse(combat.transform.right * 5f);
            yield return WaitUntilOrTimeout(() => !motor.IsGrounded, 1.5f);
            yield return WaitRealtime(motor.coyoteTime + 0.05f);
            Vector3 oneFrom = combat.transform.position;
            bool oneFired = motor.TryWallJump();
            float oneApex = combat.transform.position.y;
            float oneEnd = Time.unscaledTime + 4f;
            yield return null;
            while (Time.unscaledTime < oneEnd)
            {
                oneApex = Mathf.Max(oneApex, combat.transform.position.y);
                if (motor.IsGrounded) break;
                yield return null;
            }
            Vector3 oneD = combat.transform.position - oneFrom;
            Note($"MEASURED single wall jump: fired={oneFired}, apex +{oneApex - oneFrom.y:0.00} m, " +
                 $"{new Vector2(oneD.x, oneD.z).magnitude:0.00} m away from the wall before landing.");

            // two facing walls: a chimney chains, and is BOUNDED by maxWallJumps
            wallB = TestWall("~TestWall_B", c - combat.transform.right * 1.0f + Vector3.up * 3f);
            yield return null;
            motor.Teleport(c + Vector3.up * 0.4f, 0f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            float chainFromY = combat.transform.position.y;
            int before2 = wallJumps;
            // The chain is a test of chaining, not of the budget: the wall tests before this one have
            // been spending the bar, and a chimney push costs 12. Start from a full bar.
            var chainStamina = motor.GetComponent<PlayerStamina>();
            if (chainStamina != null) chainStamina.ResetFull();
            motor.Launch(7f);
            yield return WaitUntilOrTimeout(() => !motor.IsGrounded, 1.5f);
            yield return WaitRealtime(motor.coyoteTime + 0.1f);
            float chainApex = combat.transform.position.y;
            float chainEnd = Time.unscaledTime + 6f;
            int attempts = 0;
            while (Time.unscaledTime < chainEnd && attempts < 400)
            {
                attempts++;
                // Press at the top of the arc, the way a player does. Spamming every frame burns the
                // whole allowance inside three frames at the same height and measures nothing.
                if (motor.Velocity.y <= 0f) motor.TryWallJump();
                chainApex = Mathf.Max(chainApex, combat.transform.position.y);
                if (motor.IsGrounded) break;
                yield return null;
            }
            int chained = wallJumps - before2;
            motor.OnWallJumped -= onWall;
            Check("WallJump_ChainsBetweenFacingWalls", chained >= 2,
                $"chained={chained} of max {motor.maxWallJumps} stamina={(chainStamina != null ? chainStamina.Current.ToString("0") : "n/a")} " +
                $"canAct={motor.CanAct} grounded={motor.IsGrounded} attempts={attempts} drift={(combat.transform.position - c).magnitude:0.00}m");
            Check("WallJump_BoundedPerAirtime", chained <= motor.maxWallJumps,
                $"chained={chained} max={motor.maxWallJumps} - an unbounded chain is a free elevator");
            Note($"MEASURED wall-jump chain: {chained} pushes, +{chainApex - chainFromY:0.00} m above the " +
                 $"launch floor (up {motor.wallJumpUpSpeed:0.0} m/s, push {motor.wallJumpPushSpeed:0.0} m/s, " +
                 $"single-jump rise {motor.wallJumpUpSpeed * motor.wallJumpUpSpeed / (2f * -motor.gravity):0.00} m).");

            if (wallA != null) UnityEngine.Object.DestroyImmediate(wallA);
            if (wallB != null) UnityEngine.Object.DestroyImmediate(wallB);

            // landing must forgive the wall, or the next jump off the same face is silently dead
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 6f);
            Check("WallJump_ResetsOnLanding", motor.WallJumpsUsed == 0,
                "used=" + motor.WallJumpsUsed);

            if (rig != null) UnityEngine.Object.DestroyImmediate(rig);
            if (LevelManager.I != null) LevelManager.I.Warp("Checkpoint_1");
            yield return null;
        }

        /// <summary>A throwaway wall for the wall-jump rig. Default layer, so the motor's world mask sees it.</summary>
        static GameObject TestWall(string wallName, Vector3 center)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = wallName;
            go.layer = 0;
            go.transform.position = center;
            go.transform.localScale = new Vector3(0.3f, 8f, 8f);
            return go;
        }

        // ================================================================ 2. HITSTOP SCOPING

        // ================================================================ STAMINA

        /// <summary>
        /// The movement budget: three dashes from a full bar, the fourth REFUSED with a named event (never
        /// silently), regen that waits its delay and then comes back, and a wall run that costs. This is
        /// the contract behind "it is not clear when I have an ability".
        /// </summary>
        /// <summary>
        /// PERFECT timing, driven on the motor's own clock: a jump thrown out of a dash inside the
        /// window refunds the dash; the same jump thrown on the dash's own frame does not. The wall
        /// jump and the burst share the same laws (PerfectTimingTests) and are not staged here — a
        /// full wall run to its let-go is a 1.75 s ride the WallRunLive rig would need extending for.
        /// </summary>
        IEnumerator TestPerfectTiming()
        {
            var st = motor != null ? motor.GetComponent<PlayerStamina>() : null;
            if (st == null) { Check("Perfect_StaminaOnPlayer", false, "PlayerStamina missing"); yield break; }
            bool savedInfinite = st.Infinite;
            st.Infinite = false;
            st.ResetFull();
            yield return WaitUntilOrTimeout(() => motor.IsGrounded && !motor.IsDashing, 4f);
            Vector3 home = combat.transform.position;
            float yaw = combat.transform.eulerAngles.y;
            float savedCd = motor.dashCooldown;
            motor.dashCooldown = 0.02f;

            int perfects = 0; PerfectKind lastKind = PerfectKind.None; float lastRefund = -1f;
            System.Action<PerfectKind, float> onPerfect = (k, r) => { perfects++; lastKind = k; lastRefund = r; };
            motor.OnPerfect += onPerfect;

            // ORDINARY: dash and jump on the same frame. The jump buffer lands the jump this frame, the
            // dash fires below it in the same step: sinceDash is 0, under the minimum delay.
            float before = st.Current;
            motor.TryJump();
            motor.TryDash();
            yield return null; yield return null;
            Check("Perfect_SameFrameDashJumpIsOrdinary", perfects == 0,
                "perfects=" + perfects + " (a mashed dash+jump counted)");
            Check("Perfect_OrdinaryDashStillCosts", st.Current <= before - st.dashCost + 2f,
                "stamina " + before.ToString("0") + " -> " + st.Current.ToString("0"));
            yield return WaitUntilOrTimeout(() => !motor.IsDashing, 1.5f);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            motor.Teleport(home, yaw);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            st.ResetFull();
            yield return null;

            // PERFECT: dash, wait into the window (0.04 .. 0.16 s after the dash), then jump.
            before = st.Current;
            int dashesBefore = 0;
            System.Action onDash = () => dashesBefore++;
            motor.OnDashed += onDash;
            motor.TryDash();
            yield return null;
            Check("Perfect_DashFired", dashesBefore == 1, "dashes=" + dashesBefore);
            float mid = motor.perfectDashJumpMinDelay + motor.perfectDashJumpWindow * 0.5f;
            yield return WaitRealtime(mid);
            float afterDash = st.Current;
            motor.TryJump();
            yield return null; yield return null;
            Check("Perfect_DashJumpInsideTheWindowIsPerfect", perfects == 1 && lastKind == PerfectKind.DashJump,
                "perfects=" + perfects + " kind=" + lastKind + " (jumped " + mid.ToString("0.00") + "s after the dash)");
            Check("Perfect_DashJumpRefundsTheDash", st.Current >= afterDash + motor.perfectDashJumpRefund - 2f
                                                    && st.Current <= st.max + 0.01f,
                "stamina " + afterDash.ToString("0") + " -> " + st.Current.ToString("0") + " refund=" + lastRefund.ToString("0"));
            Check("Perfect_LastPerfectKindReads", motor.LastPerfectKind == PerfectKind.DashJump, "kind=" + motor.LastPerfectKind);

            motor.OnDashed -= onDash;
            motor.OnPerfect -= onPerfect;
            motor.dashCooldown = savedCd;
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            motor.Teleport(home, yaw);
            st.ResetFull();
            st.Infinite = savedInfinite;
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
        }

        IEnumerator TestStamina()
        {
            var st = motor != null ? motor.GetComponent<PlayerStamina>() : null;
            if (st == null) { Check("Stamina_ComponentOnPlayer", false, "PlayerStamina missing — rebuild prefabs"); yield break; }
            Check("Stamina_ComponentOnPlayer", true);
            Check("Stamina_ShippedValues", st.max == 100f && st.dashCost == 30f && st.wallRunEntryCost == 12f,
                "max=" + st.max + " dash=" + st.dashCost + " wallEntry=" + st.wallRunEntryCost);

            st.Infinite = false;
            st.ResetFull();
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 4f);
            Vector3 staminaHome = combat.transform.position;
            float staminaYaw = combat.transform.eulerAngles.y;
            float savedCd = motor.dashCooldown;
            motor.dashCooldown = 0.02f;
            int refused = 0; StaminaAction lastRefused = StaminaAction.WallJump;
            System.Action<StaminaAction> onRefused = a => { refused++; lastRefused = a; };
            GameEvents.StaminaRefused += onRefused;

            int dashes = 0;
            System.Action onDash = () => dashes++;
            motor.OnDashed += onDash;

            // Three dashes, each waited out so the cooldown is never the gate, each costing 30.
            for (int i = 0; i < 3; i++)
            {
                motor.TryDash();
                yield return null; yield return null;
                yield return WaitUntilOrTimeout(() => !motor.IsDashing, 1.5f);
                yield return WaitUntilOrTimeout(() => motor.IsGrounded, 2f);
                yield return null;
            }
            Check("Stamina_ThreeDashesFromFull", dashes == 3, "dashes=" + dashes + " stamina=" + st.Current.ToString("0"));
            // Three dashes are ~13 m of travel from wherever the previous test left us, which can be off
            // a ledge; the regen timings below assume a GROUNDED player (45/s, not the air's 18/s). Go home.
            motor.Teleport(staminaHome, staminaYaw);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            Check("Stamina_ThreeDashesCostNinety", st.Current <= 10f + 3f * st.regenPerSecondGrounded * 0.4f,
                "stamina after three dashes=" + st.Current.ToString("0") + " (delay " + st.regenDelay + "s should have held most of the spend)");

            // Spend whatever regen crept back, then the fourth press must be REFUSED, loudly.
            while (st.Current >= st.dashCost) st.TrySpend(st.dashCost, StaminaAction.Dash);
            int before = dashes; refused = 0;
            motor.TryDash();
            yield return null; yield return null;
            Check("Stamina_FourthDashRefused", dashes == before, "a dash fired on " + st.Current.ToString("0") + " stamina");
            Check("Stamina_RefusalIsNamed", refused >= 1 && lastRefused == StaminaAction.Dash,
                "refused=" + refused + " last=" + lastRefused + " (a silent refusal reads as a dropped input)");
            Check("Stamina_PipReadsFalseWhenBroke", !motor.CanDashNow, "CanDashNow=" + motor.CanDashNow);

            // Regen: nothing during the delay, then back. Grounded rate is 45/s. A fresh spend first:
            // by now the last real spend is over a second old and the delay has already lapsed.
            st.Drain(1f, 1f);
            float t0 = Time.unscaledTime; float atStart = st.Current;
            yield return WaitRealtime(st.regenDelay * 0.6f);
            Check("Stamina_RegenWaitsItsDelay", st.Current <= atStart + 2f,
                "regen started inside the delay: " + atStart.ToString("0") + " -> " + st.Current.ToString("0"));
            yield return WaitUntilOrTimeout(() => st.Current >= st.dashCost, 3f);
            Check("Stamina_ADashComesBackWithinASecondOrSo", !waitTimedOut && Time.unscaledTime - t0 < 2f,
                "took " + (Time.unscaledTime - t0).ToString("0.00") + "s to afford a dash");
            Check("Stamina_PipReadsTrueAgain", motor.CanDashNow, "CanDashNow=" + motor.CanDashNow);
            yield return WaitUntilOrTimeout(() => st.IsFull, 4f);
            Check("Stamina_RefillsToFull", st.IsFull, "current=" + st.Current.ToString("0") + " grounded=" + motor.IsGrounded
                + " (grounded regen " + st.regenPerSecondGrounded + "/s, airborne " + st.regenPerSecondAirborne + "/s)");

            // God mode is infinite stamina: a spend is free and nothing is refused.
            st.Infinite = true;
            bool freeSpend = st.TrySpend(9999f, StaminaAction.Dash);
            Check("Stamina_InfiniteNeverRefuses", freeSpend && st.IsFull);
            st.Infinite = false;

            // Drain: the wall's per-second cost, and the empty-bar signal.
            st.ResetFull();
            bool stillHas = st.Drain(st.wallRunDrainPerSecond, 1f);
            CheckApprox("Stamina_WallDrainPerSecond", st.Current, st.max - st.wallRunDrainPerSecond, 0.01f);
            Check("Stamina_DrainReportsRemaining", stillHas);
            bool empty = st.Drain(st.max * 10f, 1f);
            Check("Stamina_DrainReportsEmpty", !empty && st.Current == 0f, "current=" + st.Current);

            GameEvents.StaminaRefused -= onRefused;
            motor.OnDashed -= onDash;
            motor.dashCooldown = savedCd;
            st.ResetFull();
        }

        IEnumerator TestHitstopScoping()
        {
            var ts = TimeScaleController.I;
            if (ts == null) { Check("Hitstop_ControllerExists", false, "TimeScaleController.I is null"); yield break; }

            yield return SettleTimeScale();
            CheckApprox("Hitstop_BaselineWorldScale", ts.WorldScale, 1f, 0.001f);
            CheckApprox("Hitstop_BaselinePlayerScale", ts.PlayerScale, 1f, 0.001f);

            // The contract that makes the game feel good: hitstop freezes the WORLD, never the player.
            ts.HitStop(0.5f);
            yield return null;
            Check("Hitstop_WorldSlows", ts.WorldScale < 0.5f, "world=" + ts.WorldScale.ToString("0.###"));
            CheckApprox("Hitstop_PlayerUnaffected", ts.PlayerScale, 1f, 0.001f);
            Check("Hitstop_PlayerDeltaStillAdvances", TimeScaleController.PlayerDelta > 0f,
                "playerDelta=" + TimeScaleController.PlayerDelta.ToString("0.####"));
            Check("Hitstop_HitStopActiveFlag", ts.HitStopActive);

            yield return SettleTimeScale();
            Check("Hitstop_ExpiresAutomatically", ts.WorldScale > 0.99f, "world=" + ts.WorldScale.ToString("0.###"));

            // The other half of the contract: hitstop must not EXTEND the player either. The motor's
            // timers once ran on Time.time, which hitstop drives to ~0.02x, so a dash landed with a
            // hit kept travelling at dashSpeed for the whole freeze. The motor now keeps its own clock.
            var hsMotor = UnityEngine.Object.FindAnyObjectByType<FirstPersonMotor>();
            if (hsMotor != null)
            {
                yield return WaitUntilOrTimeout(() => hsMotor.IsGrounded, 4f);
                float savedCd = hsMotor.dashCooldown;
                hsMotor.dashCooldown = 0.02f;
                hsMotor.TryDash();
                yield return null;
                ts.HitStop(0.6f);
                float t0 = Time.unscaledTime;
                yield return WaitUntilOrTimeout(() => !hsMotor.IsDashing, 2f);
                float ran = Time.unscaledTime - t0;
                hsMotor.dashCooldown = savedCd;
                Check("Hitstop_DashEndsOnPlayerClock", ran < hsMotor.dashDuration + 0.1f,
                    "dash ran " + ran.ToString("0.###") + "s under hitstop, dashDuration=" + hsMotor.dashDuration.ToString("0.###"));
                yield return SettleTimeScale();
            }

            // A pause request DOES stop the player.
            int handle = ts.Request(0f);
            yield return null;
            CheckApprox("Pause_WorldStops", ts.WorldScale, 0f, 0.001f);
            CheckApprox("Pause_PlayerStops", ts.PlayerScale, 0f, 0.001f);
            CheckApprox("Pause_PlayerDeltaZero", TimeScaleController.PlayerDelta, 0f, 0.0001f);
            ts.Release(handle);
            yield return null;
            CheckApprox("Pause_ReleaseRestores", ts.PlayerScale, 1f, 0.001f);

            // Overlapping requests must take the minimum, not cancel each other.
            int a = ts.Request(0.5f);
            int b = ts.Request(0.2f);
            yield return null;
            CheckApprox("TimeScale_MinimumOfRequestsWins", ts.WorldScale, 0.2f, 0.01f);
            ts.Release(b);
            yield return null;
            CheckApprox("TimeScale_ReleaseFallsBackToOther", ts.WorldScale, 0.5f, 0.01f);
            ts.Release(a);
            yield return SettleTimeScale();
        }

        // ================================================================ 3a. PARRY MATH

        IEnumerator TestParryMathPure()
        {
            const float p = 0.13f, l = 0.12f;
            Check("ParryMath_EarlyIsPerfect", ParryMath.Evaluate(0f, p, l, true, false) == ParryResult.Perfect);
            Check("ParryMath_EdgeOfPerfect", ParryMath.Evaluate(p, p, l, true, false) == ParryResult.Perfect);
            Check("ParryMath_JustPastPerfectIsBlock", ParryMath.Evaluate(p + 0.001f, p, l, true, false) == ParryResult.Blocked);
            Check("ParryMath_EdgeOfLate", ParryMath.Evaluate(p + l, p, l, true, false) == ParryResult.Blocked);
            Check("ParryMath_PastLateIsHit", ParryMath.Evaluate(p + l + 0.001f, p, l, true, false) == ParryResult.Hit);
            Check("ParryMath_NegativeIsHit", ParryMath.Evaluate(-0.1f, p, l, true, false) == ParryResult.Hit);
            Check("ParryMath_NotFacingIsHit", ParryMath.Evaluate(0f, p, l, false, false) == ParryResult.Hit);
            Check("ParryMath_UnblockableIsHit", ParryMath.Evaluate(0f, p, l, true, true) == ParryResult.Hit);

            // The tuning the game actually ships with, per the feel contract in CLAUDE.md.
            CheckApprox("ParryTuning_PerfectWindow", D.parryPerfectWindow, 0.13f, 0.0001f);
            CheckApprox("ParryTuning_LateWindow", D.parryLateWindow, 0.12f, 0.0001f);
            Check("ParryTuning_WhiffRecoveryPunishesMashing", D.parryWhiffRecovery >= 0.4f,
                "whiff=" + D.parryWhiffRecovery.ToString("0.00"));
            yield break;
        }

        // ================================================================ 3b. PARRY LIVE

        IEnumerator TestParryLive()
        {
            // This test owns its input and damage preconditions. A manual held guard or F8 state
            // must not turn the no-parry control case into a guard/ignored hit when run alone.
            health.Invulnerable = false;
            parry.GuardHeld = false;
            EnemyController dummy = null;
            Vector3 pos = combat.transform.position + combat.transform.forward * 3f;
            yield return SpawnDummy(pos, e => dummy = e);
            if (dummy == null) { Check("ParryLive_DummySpawned", false, "no non-boss enemy prefab found"); yield break; }
            Check("ParryLive_DummySpawned", true);

            weapons.Equip(0);
            yield return null;

            // ---- perfect deflect -------------------------------------------------------------
            FacePoint(dummy.transform.position);
            health.ResetFull();
            posture.ResetFull();
            res.ConsumePyre();
            float enemyPosture0 = dummy.Posture.Current;
            float hp0 = health.Current;

            parry.StartParry();
            yield return null;                       // resolve one frame in => well inside the perfect window
            var r1 = combat.ReceiveAttack(MakeAttack(dummy, 25f, false));
            Check("Parry_PerfectResult", r1 == ParryResult.Perfect, "result=" + r1);
            CheckApprox("Parry_PerfectTakesNoDamage", health.Current, hp0, 0.01f);
            // PYRE: a perfect deflect stokes the meter. This is the fill rule the weapon fire and
            // the super both hang off, so it is asserted at the source rather than on the HUD.
            float pyrePerfect = res.Pyre;
            Check("Parry_PerfectStokesPyre", pyrePerfect > 0f, "pyre=" + pyrePerfect.ToString("0.0"));
            Check("Parry_PerfectBuildsEnemyPosture", dummy.Posture.Current > enemyPosture0,
                $"{enemyPosture0:0.0} -> {dummy.Posture.Current:0.0}");
            CheckApprox("Parry_PerfectCostsNoPlayerPosture", posture.Current, 0f, 0.01f);

            yield return SettleTimeScale();
            yield return WaitRealtime(D.parryWhiffRecovery + 0.2f);

            // ---- late block ------------------------------------------------------------------
            parry.Cancel();
            health.ResetFull();
            posture.ResetFull();
            FacePoint(dummy.transform.position);
            hp0 = health.Current;
            res.ConsumePyre();

            // Deterministic. Busy-waiting to the middle of the late window races the frame rate: one
            // long editor frame steps past stateEnd, ParryController.Update closes the window before
            // this coroutine resumes, and a legitimate block reads as a plain Hit. Back-date the press
            // instead — no frame boundary between here and the resolve, so elapsed is exactly the value
            // asserted on.
            parry.StartParry();
            float blockTarget = parry.PerfectWindow + D.parryLateWindow * 0.5f;   // mid-block window
            BackdateParryPress(blockTarget);
            var r2 = combat.ReceiveAttack(MakeAttack(dummy, 30f, false));
            Check("Parry_LateIsBlock", r2 == ParryResult.Blocked,
                "result=" + r2 + " elapsed=" + blockTarget.ToString("0.000") +
                " perfect=" + parry.PerfectWindow.ToString("0.000") + " late=" + parry.LateWindow.ToString("0.000"));
            if (r2 == ParryResult.Blocked)
            {
                CheckApprox("Parry_BlockReducesDamage", hp0 - health.Current, 30f * D.blockDamageMultiplier, 1f);
                CheckApprox("Parry_BlockCostsPosture", posture.Current, 30f * D.blockPostureMultiplier, 1f);
                // A block IS a successful parry, so it stokes the Pyre — but strictly less than a
                // perfect deflect, or there would be no reason to chase the tighter window.
                Check("Parry_BlockStokesPyreLessThanPerfect",
                    res.Pyre > 0f && res.Pyre < pyrePerfect - 0.01f,
                    $"block={res.Pyre:0.0} perfect={pyrePerfect:0.0} fraction={D.pyreBlockFraction:0.00}");
            }

            yield return SettleTimeScale();
            yield return WaitRealtime(D.parryWhiffRecovery + 0.2f);

            // ---- missed parry ----------------------------------------------------------------
            parry.Cancel();
            health.ResetFull();
            posture.ResetFull();
            FacePoint(dummy.transform.position);
            hp0 = health.Current;
            var r3 = combat.ReceiveAttack(MakeAttack(dummy, 20f, false));    // no parry pressed at all
            Check("Parry_NoParryIsHit", r3 == ParryResult.Hit, "result=" + r3);
            CheckApprox("Parry_HitDealsFullDamage", hp0 - health.Current, 20f, 0.5f);
            CheckApprox("Parry_HitCostsPosture", posture.Current, 20f * D.hitPostureMultiplier, 1f);

            yield return SettleTimeScale();

            // ---- unblockable ignores a perfect deflect ---------------------------------------
            parry.Cancel();
            health.ResetFull();
            posture.ResetFull();
            FacePoint(dummy.transform.position);
            hp0 = health.Current;
            parry.StartParry();
            yield return null;
            var r4 = combat.ReceiveAttack(MakeAttack(dummy, 40f, true));
            Check("Parry_UnblockableAlwaysHits", r4 == ParryResult.Hit, "result=" + r4);
            Check("Parry_UnblockableDealsDamage", hp0 - health.Current > 30f,
                "dealt=" + (hp0 - health.Current).ToString("0.0"));

            yield return SettleTimeScale();
            yield return WaitRealtime(D.parryWhiffRecovery + 0.2f);

            // ---- facing away -----------------------------------------------------------------
            parry.Cancel();
            health.ResetFull();
            posture.ResetFull();
            FaceAwayFrom(dummy.transform.position);
            hp0 = health.Current;
            parry.StartParry();
            yield return null;
            var r5 = combat.ReceiveAttack(MakeAttack(dummy, 15f, false));
            Check("Parry_FacingAwayCannotParry", r5 == ParryResult.Hit, "result=" + r5);

            yield return SettleTimeScale();
            if (dummy != null) Destroy(dummy.gameObject);
            parry.ReleaseGuardOverride();
            yield return null;
        }

        // ================================================================ 3c. GUARD (held stance)

        /// <summary>
        /// The Sekiro STANCE: hold the parry button and a blow that would have been a Hit resolves as a
        /// Blocked instead. Everything here exists to keep one ladder true —
        /// <b>deflect &gt; guard &gt; hit</b> — and to prove the three things that stop the guard from
        /// eating the game: an unblockable still goes straight through it, it never protects your back,
        /// and posture does not regenerate behind it.
        /// </summary>
        IEnumerator TestGuard()
        {
            // ---- shipped tuning (rule 9) -----------------------------------------------------
            CheckApprox("GuardTuning_ChipDamage", D.guardChipDamageMultiplier, 0f, 0.0001f);
            CheckApprox("GuardTuning_PostureMultiplier", D.guardPostureMultiplier, 1.5f, 0.0001f);
            CheckApprox("GuardTuning_RegenMultiplier", D.guardPostureRegenMultiplier, 0f, 0.0001f);
            // The ladder, asserted as an inequality rather than as three magic numbers: whatever the
            // values become, guarding must cost MORE posture than a timed block and than a raw hit, and
            // LESS health than a timed block. That is what makes the deflect worth pressing for.
            Check("GuardLadder_CostsMorePostureThanBlock", D.guardPostureMultiplier > D.blockPostureMultiplier,
                "guard=" + D.guardPostureMultiplier.ToString("0.00") + " block=" + D.blockPostureMultiplier.ToString("0.00"));
            Check("GuardLadder_CostsMorePostureThanHit", D.guardPostureMultiplier > D.hitPostureMultiplier,
                "guard=" + D.guardPostureMultiplier.ToString("0.00") + " hit=" + D.hitPostureMultiplier.ToString("0.00"));
            Check("GuardLadder_CostsLessHealthThanBlock", D.guardChipDamageMultiplier < D.blockDamageMultiplier,
                "guard=" + D.guardChipDamageMultiplier.ToString("0.00") + " block=" + D.blockDamageMultiplier.ToString("0.00"));

            EnemyController dummy = null;
            Vector3 pos = combat.transform.position + combat.transform.forward * 3f;
            yield return SpawnDummy(pos, e => dummy = e);
            if (dummy == null) { Check("Guard_DummySpawned", false, "no non-boss enemy prefab found"); yield break; }
            Check("Guard_DummySpawned", true);

            weapons.Equip(0);
            yield return null;

            // ---- a HELD guard turns a would-be Hit into a Blocked ------------------------------
            parry.Cancel();
            health.ResetFull(); posture.ResetFull(); res.ConsumePyre();
            FacePoint(dummy.transform.position);
            float hp0 = health.Current;
            float enemyPosture0 = dummy.Posture.Current;

            parry.GuardHeld = true;                       // the settable stance hook: no press at all
            Check("Guard_IsGuardingWhenHeld", parry.IsGuarding);
            var g1 = combat.ReceiveAttack(MakeAttack(dummy, 20f, false));
            Check("Guard_ConvertsHitToBlocked", g1 == ParryResult.Blocked, "result=" + g1);
            CheckApprox("Guard_TakesNoChipDamage", hp0 - health.Current, 20f * D.guardChipDamageMultiplier, 0.5f);
            CheckApprox("Guard_CostsPosture", posture.Current, 20f * D.guardPostureMultiplier, 1f);
            // Turtling must not charge the super. A guard timed nothing, so it earns nothing.
            CheckApprox("Guard_StokesNoPyre", res.Pyre, 0f, 0.01f);
            CheckApprox("Guard_BuildsNoEnemyPosture", dummy.Posture.Current, enemyPosture0, 0.01f);

            yield return SettleTimeScale();

            // ---- a well-timed press while guarding is STILL a Perfect --------------------------
            // The whole design lives here. If holding could produce a Perfect the timing game dies; if
            // holding suppressed the Perfect nobody would ever hold.
            parry.Cancel();
            parry.GuardHeld = true;
            health.ResetFull(); posture.ResetFull(); res.ConsumePyre();
            FacePoint(dummy.transform.position);
            hp0 = health.Current;
            enemyPosture0 = dummy.Posture.Current;
            parry.StartParry();
            yield return null;                            // one frame in => well inside the perfect window
            var g2 = combat.ReceiveAttack(MakeAttack(dummy, 25f, false));
            Check("Guard_TimedPressStillPerfect", g2 == ParryResult.Perfect, "result=" + g2);
            CheckApprox("Guard_PerfectStillCostsNoPosture", posture.Current, 0f, 0.01f);
            Check("Guard_PerfectStillStokesPyre", res.Pyre > 0f, "pyre=" + res.Pyre.ToString("0.0"));
            Check("Guard_PerfectStillBuildsEnemyPosture", dummy.Posture.Current > enemyPosture0,
                enemyPosture0.ToString("0.0") + " -> " + dummy.Posture.Current.ToString("0.0"));

            yield return SettleTimeScale();
            yield return WaitRealtime(D.parryWhiffRecovery + 0.2f);

            // ---- an unblockable goes STRAIGHT THROUGH the guard --------------------------------
            // The pink alert tell says "this one cannot be answered with steel — move". A guard that
            // ate it would make the loudest signal in the game a lie.
            parry.Cancel();
            parry.GuardHeld = true;
            health.ResetFull(); posture.ResetFull();
            FacePoint(dummy.transform.position);
            hp0 = health.Current;
            var g3 = combat.ReceiveAttack(MakeAttack(dummy, 40f, true));
            Check("Guard_UnblockableIgnoresGuard", g3 == ParryResult.Hit, "result=" + g3);
            Check("Guard_UnblockableDealsFullDamage", hp0 - health.Current > 30f,
                "dealt=" + (hp0 - health.Current).ToString("0.0"));

            yield return SettleTimeScale();

            // ---- the guard does not protect your back ------------------------------------------
            parry.Cancel();
            parry.GuardHeld = true;
            health.ResetFull(); posture.ResetFull();
            FaceAwayFrom(dummy.transform.position);
            hp0 = health.Current;
            var g4 = combat.ReceiveAttack(MakeAttack(dummy, 15f, false));
            Check("Guard_DoesNotProtectYourBack", g4 == ParryResult.Hit, "result=" + g4);
            CheckApprox("Guard_BackHitDealsFullDamage", hp0 - health.Current, 15f, 0.5f);

            yield return SettleTimeScale();

            // ---- posture does NOT regenerate while the guard is up -----------------------------
            parry.Cancel();
            parry.GuardHeld = true;
            health.ResetFull(); posture.ResetFull();
            FacePoint(dummy.transform.position);
            posture.Add(posture.Max * 0.4f);
            float held0 = posture.Current;
            yield return WaitRealtime(D.postureRegenDelay + 0.6f);
            CheckApprox("Guard_SuppressesPostureRegen", posture.Current, held0, 0.5f);

            parry.GuardHeld = false;                      // blade down: the refund starts
            Check("Guard_NotGuardingWhenReleased", !parry.IsGuarding);
            yield return WaitRealtime(0.5f);
            Check("Guard_PostureRegensOnceReleased", posture.Current < held0 - 1f,
                held0.ToString("0.0") + " -> " + posture.Current.ToString("0.0"));

            // ---- guarding through too much BREAKS you ------------------------------------------
            // The punishment that makes turtling a losing strategy, reached through the normal
            // PlayerPosture path so the stagger, the event and the HUD all fire as they always do.
            parry.Cancel();
            parry.GuardHeld = true;
            health.ResetFull(); posture.ResetFull();
            FacePoint(dummy.transform.position);
            bool brokeEvent = false;
            Action onBreak = () => brokeEvent = true;
            GameEvents.PlayerPostureBroken += onBreak;
            int swings = 0;
            while (!posture.IsBroken && swings < 12)
            {
                FacePoint(dummy.transform.position);      // the guard shove moves the player off the line
                combat.ReceiveAttack(MakeAttack(dummy, 30f, false));
                swings++;
                yield return null;
            }
            GameEvents.PlayerPostureBroken -= onBreak;
            Check("Guard_TurtlingBreaksPosture", posture.IsBroken, "guarded hits=" + swings);
            Check("Guard_BreakRaisesEvent", brokeEvent);
            Check("Guard_BreakStaggersPlayer", combat.IsStaggered);
            // A broken guard must STAY broken: the stance cannot be re-raised through the stagger, or
            // the punish never lands.
            Check("Guard_CannotGuardWhileStaggered", !parry.IsGuarding, "GuardHeld=" + parry.GuardHeld);

            parry.GuardHeld = false;
            parry.ReleaseGuardOverride();
            parry.Cancel();
            yield return WaitRealtime(D.postureStaggerSeconds + 0.3f);
            posture.ResetFull();
            health.ResetFull();
            yield return SettleTimeScale();

            // ---- the STANCE: a held pose that does not stand in front of the fight -------------
            var vm = combat.GetComponentInChildren<WeaponViewmodel>(true);
            if (vm == null) Skip("Guard_ViewmodelExists", "no WeaponViewmodel");
            else
            {
                Check("Guard_ViewmodelExists", true);
                parry.ReleaseGuardOverride();
                vm.PlayGuard();
                yield return null;
                Check("Guard_ViewmodelHoldsStance", vm.IsGuarding);
                vm.EndGuard();
                yield return null;
                Check("Guard_ViewmodelReleases", !vm.IsGuarding);
            }

            // Every weapon's stance must be a DIFFERENT pose from its parry flick (a held stance and a
            // momentary flick have different jobs) and must sit well off the crosshair. The failure mode
            // of this viewmodel has always been eating the frame; a stance is held for SECONDS, so a
            // pose over screen centre would occlude the enemy for the whole exchange.
            if (weapons.loadout != null)
            {
                foreach (var w in weapons.loadout)
                {
                    if (w == null) continue;
                    Check("GuardPose_DistinctFromParry_" + w.name,
                        (w.guard.pos - w.parry.pos).sqrMagnitude > 0.0001f ||
                        (w.guard.euler - w.parry.euler).sqrMagnitude > 0.01f,
                        "guard=" + w.guard.pos + " parry=" + w.parry.pos);
                    Check("GuardPose_OffTheCrosshair_" + w.name, w.guard.pos.x >= 0.20f,
                        "x=" + w.guard.pos.x.ToString("0.00"));
                    Check("GuardPose_RaisedAboveIdle_" + w.name, w.guard.pos.y > w.idle.pos.y,
                        "guardY=" + w.guard.pos.y.ToString("0.00") + " idleY=" + w.idle.pos.y.ToString("0.00"));
                }
            }

            // ---- ONE MOTION INTO GUARD ---------------------------------------------------------
            // Reported from play: "it needs to not point forward first when trying to guard, it needs
            // to be one smooth motion into guard." The entry used to route through WeaponData.parry -
            // a flick that lives almost on the crosshair - so the blade shot forward and came back.
            //
            // The criterion is geometric and it is the viewer's: measure the blade tip's ANGLE OFF THE
            // CAMERA AXIS every frame of the blend. If any frame swings the tip closer to the crosshair
            // than BOTH endpoints, the motion is still travelling through a waypoint. This is the same
            // question as "is it one smooth motion", asked in a way a machine can answer every run.
            if (vm != null && weapons.Current != null)
            {
                parry.ReleaseGuardOverride();
                parry.GuardHeld = false;
                vm.EndGuard();
                yield return WaitRealtime(0.35f);

                float idleAngle = TipAngleOffAxis(vm);
                float worstRise = 999f;
                // Driven through the BUTTON, not the viewmodel: ParryController owns GuardWanted and
                // rewrites it every frame, so poking the viewmodel directly tests a path the game never
                // takes. (Learned the hard way - the first version of this test did exactly that and the
                // mid-swing case silently measured idle.)
                parry.GuardHeld = true;
                yield return null;
                float t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < 0.30f)
                {
                    worstRise = Mathf.Min(worstRise, TipAngleOffAxis(vm));
                    yield return null;
                }
                float guardAngle = TipAngleOffAxis(vm);
                float floorAngle = Mathf.Min(idleAngle, guardAngle);
                Check("GuardEntry_NeverSwingsAcrossTheView", worstRise >= floorAngle - 2f,
                    "worst=" + worstRise.ToString("0.0") + "deg idle=" + idleAngle.ToString("0.0") +
                    "deg guard=" + guardAngle.ToString("0.0") + "deg (a frame nearer the crosshair than " +
                    "both endpoints means the blade is still routing through the parry flick)");

                // And the same on the way out: the release is one settle, not a flick.
                float worstFall = 999f;
                parry.GuardHeld = false;
                yield return null;
                t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < 0.30f)
                {
                    worstFall = Mathf.Min(worstFall, TipAngleOffAxis(vm));
                    yield return null;
                }
                Check("GuardRelease_NeverSwingsAcrossTheView", worstFall >= floorAngle - 2f,
                    "worst=" + worstFall.ToString("0.0") + "deg floor=" + floorAngle.ToString("0.0") + "deg");

                // MID-SWING INTO GUARD is the case a real fight hits most often. The recovery leg of the
                // arc must land IN the stance, so the blade never returns to idle and set off again.
                weapons.TryAttack();
                yield return WaitRealtime(weapons.Current.hitDelay + 0.02f);
                parry.GuardHeld = true;                   // guard raised MID-ARC, as a player would
                yield return WaitRealtime(weapons.Current.attackDuration + 0.45f);
                Check("GuardEntry_SwingEndsInTheStance", vm.IsGuarding,
                    "a swing that ends with the button down must settle INTO the guard, not into idle");
                float settled = TipAngleOffAxis(vm);
                Check("GuardEntry_SwingSettlesToTheGuardAngle", Mathf.Abs(settled - guardAngle) < 6f,
                    "afterSwing=" + settled.ToString("0.0") + "deg guard=" + guardAngle.ToString("0.0") + "deg");

                // THE IMPACT KICK MUST ALSO DRIVE AWAY FROM THE FIGHT. A guarded hit shoves the stance,
                // and the first version of that shove pulled the blade 0.10 m toward the lens with a
                // yaw that swung the point INWARD - at the peak of the kick the tip sat on the enemy,
                // during the one beat the player most needs to see them. Measured, not eyeballed: the
                // kick's peak must be FURTHER off the camera axis than the stance it starts from.
                parry.GuardHeld = true;
                yield return null;
                yield return WaitRealtime(0.2f);
                float stanceAngle = TipAngleOffAxis(vm);
                vm.GuardImpact();
                float peakOut = 0f;
                float t1 = Time.unscaledTime;
                while (Time.unscaledTime - t1 < 0.06f)
                {
                    peakOut = Mathf.Max(peakOut, TipAngleOffAxis(vm));
                    yield return null;
                }
                Check("GuardImpact_KicksAwayFromTheFight", peakOut >= stanceAngle - 0.5f,
                    "peak=" + peakOut.ToString("0.0") + "deg stance=" + stanceAngle.ToString("0.0") +
                    "deg (a kick that swings the blade TOWARD the crosshair covers the enemy on the " +
                    "exact beat the player needs to read them)");

                parry.GuardHeld = false;
                yield return null;
                vm.EndGuard();
                yield return SettleTimeScale();
            }

            parry.ReleaseGuardOverride();
            parry.Cancel();
            if (dummy != null) Destroy(dummy.gameObject);
            yield return null;
        }

        /// <summary>
        /// Angle between the camera's forward axis and the line to the held blade's tip. Small = the
        /// weapon is near the crosshair and in front of the fight; large = it is out at the edge of the
        /// frame where a viewmodel belongs. The one number that answers "is the guard in my way".
        /// </summary>
        static float TipAngleOffAxis(WeaponViewmodel vm)
        {
            var cam = Camera.main;
            if (cam == null || vm == null) return 999f;
            Vector3 to = vm.TipWorldPosition - cam.transform.position;
            if (to.sqrMagnitude < 1e-6f) return 999f;
            return Vector3.Angle(cam.transform.forward, to);
        }

        // ================================================================ 4. PLAYER POSTURE

        IEnumerator TestPlayerPosture()
        {
            posture.ResetFull();
            health.ResetFull();
            yield return null;

            Check("Posture_StartsEmpty", posture.Current <= 0.01f, "current=" + posture.Current.ToString("0.0"));
            Check("Posture_MaxFromStats", posture.Max > 0f, "max=" + posture.Max.ToString("0.0"));

            posture.Add(posture.Max * 0.5f);
            CheckApprox("Posture_AddAccumulates", posture.Ratio, 0.5f, 0.02f);

            // Break
            bool brokenEventFired = false;
            Action onBreak = () => brokenEventFired = true;
            GameEvents.PlayerPostureBroken += onBreak;
            posture.Add(posture.Max);
            yield return null;
            GameEvents.PlayerPostureBroken -= onBreak;

            Check("Posture_BreaksAtMax", posture.IsBroken);
            Check("Posture_BreakRaisesEvent", brokenEventFired);
            Check("Posture_BreakSetsStaggered", combat.IsStaggered);
            Check("Posture_StaggeredCountsAsBusy", combat.IsBusy);

            // Staggered damage amplification — the actual punish.
            parry.Cancel();
            health.ResetFull();
            EnemyController dummy = null;
            yield return SpawnDummy(combat.transform.position + combat.transform.forward * 3f, e => dummy = e);
            if (dummy != null)
            {
                FacePoint(dummy.transform.position);
                float hp0 = health.Current;
                combat.ReceiveAttack(MakeAttack(dummy, 10f, false));
                CheckApprox("Posture_StaggeredAmplifiesDamage", hp0 - health.Current,
                    10f * D.staggeredDamageMultiplier, 1f);
                Destroy(dummy.gameObject);
            }
            else Skip("Posture_StaggeredAmplifiesDamage", "no enemy prefab to act as attacker");

            // Auto-recovery after the stagger window.
            yield return WaitUntilOrTimeout(() => !posture.IsBroken, D.postureStaggerSeconds + 2f);
            Check("Posture_StaggerEndsAutomatically", !waitTimedOut, "stillBroken=" + posture.IsBroken);
            CheckApprox("Posture_ResetsToZeroAfterStagger", posture.Current, 0f, 0.01f);

            // Regeneration after the delay. PlayerPosture measures both the delay and the regen step in
            // SCALED time, so a fixed REALTIME wait races any hitstop still ringing from the staggered-
            // damage assert above — the delay simply has not elapsed yet and the test reads "no regen".
            // Settle the clock, then wait on the condition with a bound instead of on the wall clock.
            yield return SettleTimeScale();
            health.ResetFull();
            posture.ResetFull();
            posture.Add(posture.Max * 0.6f);
            float before = posture.Current;
            // Watch for a FRAME-OVER-FRAME fall rather than for a net fall from `before`. A net
            // comparison silently depends on nothing else touching posture for the whole wait, and a
            // live enemy landing one hit in that window pushes the total back above the starting value
            // - the assert then reads "posture never regenerates" for a reason that is not regen.
            bool regenSeen = false;
            float prev = posture.Current;
            float deadline = Time.unscaledTime + D.postureRegenDelay + 3f;
            while (Time.unscaledTime < deadline && !regenSeen)
            {
                yield return null;
                if (posture.Current < prev - 0.0001f) regenSeen = true;
                prev = posture.Current;
            }
            Check("Posture_RegeneratesAfterDelay", regenSeen,
                $"{before:0.0} -> {posture.Current:0.0} delay={D.postureRegenDelay:0.00}");

            // Respawn clears it (PlayerPosture subscribes to PlayerRespawned).
            posture.Add(posture.Max * 0.5f);
            GameEvents.RaisePlayerRespawned();
            yield return null;
            CheckApprox("Posture_ResetOnRespawn", posture.Current, 0f, 0.01f);
        }

        // ================================================================ 5. ENEMY POSTURE + EXECUTE

        IEnumerator TestEnemyPostureAndExecute()
        {
            EnemyController dummy = null;
            Vector3 spot = combat.transform.position + combat.transform.forward * 2.4f;
            yield return SpawnDummy(spot, e => dummy = e);
            if (dummy == null) { Check("Execute_DummySpawned", false, "no enemy prefab"); yield break; }

            FacePoint(dummy.transform.position);
            yield return null;

            Check("EnemyPosture_ConfiguredFromData", dummy.Posture.Max > 0f, "max=" + dummy.Posture.Max.ToString("0"));
            Check("EnemyPosture_StartsEmpty", dummy.Posture.Current <= 0.01f);

            dummy.Posture.Add(dummy.Posture.Max * 0.5f);
            CheckApprox("EnemyPosture_Accumulates", dummy.Posture.Ratio, 0.5f, 0.02f);

            dummy.Posture.Add(dummy.Posture.Max);
            yield return null;
            Check("EnemyPosture_BreaksAtMax", dummy.Posture.IsBroken);
            Check("Enemy_EntersStaggeredState", dummy.IsStaggered, "state=" + dummy.Current);

            // ExecuteInteractor scans every frame; give it one.
            yield return WaitUntilOrTimeout(() => exec.Target == dummy, 2f);
            Check("Execute_InteractorFindsStaggeredTarget", !waitTimedOut,
                "target=" + (exec.Target != null ? exec.Target.name : "null"));

            int souls0 = SoulsWallet.I != null ? SoulsWallet.I.Souls : 0;
            // The wand must be OFF cooldown going in, or this riposte comes out as the melee fallback
            // and the cooldown assertions below measure the wrong thing.
            if (wandCtl != null) wandCtl.ResetCooldown();
            bool wandReadyBefore = wandCtl == null || wandCtl.WandReady;
            bool started = exec.TryExecute();
            Check("Execute_Starts", started);
            if (started)
            {
                yield return WaitUntilOrTimeout(() => !exec.IsExecuting, exec.duration + 3f);
                Check("Execute_Completes", !waitTimedOut);

                // WAND COOLDOWN. Firing the riposte must put the wand on cooldown, and the cooldown
                // must NEVER be able to refuse a future deathblow — it degrades the riposte to the
                // melee execute instead. That distinction is the whole reason a 9 s cooldown is safe
                // next to a 5 s boss deathblow window. See docs/ENGINEERING-LOG.md.
                if (wandCtl == null || wandCtl.Current == null)
                    Skip("Wand_RiposteStartsCooldown", "no wand equipped");
                else
                {
                    Check("Wand_RiposteStartsCooldown",
                        wandReadyBefore && !wandCtl.WandReady && wandCtl.CooldownRemaining > 0f,
                        $"{wandCtl.Current.displayName} cooldown={wandCtl.Current.cooldown:0.0}s " +
                        $"remaining={wandCtl.CooldownRemaining:0.0}s readyBefore={wandReadyBefore}");
                    Check("Wand_CooldownFractionTracks",
                        wandCtl.CooldownFraction > 0f && wandCtl.CooldownFraction <= 1f,
                        "fraction=" + wandCtl.CooldownFraction.ToString("0.00"));
                    wandCtl.ResetCooldown();
                    Check("Wand_ResetClearsCooldown", wandCtl.WandReady,
                        "remaining=" + wandCtl.CooldownRemaining.ToString("0.00"));
                }

                Check("Execute_KillsNormalEnemy", dummy == null || !dummy.IsAlive,
                    dummy == null ? "destroyed" : "state=" + dummy.Current);
                if (SoulsWallet.I != null)
                    Check("Execute_AwardsSouls", SoulsWallet.I.Souls > souls0,
                        $"{souls0} -> {SoulsWallet.I.Souls}");
                Check("Execute_RestoresPlayerControl", !health.Invulnerable && motor.CanMove);
            }

            yield return SettleTimeScale();
            if (dummy != null) Destroy(dummy.gameObject);
            yield return null;
        }

        // ================================================================ 5b. DEATHBLOW MARKER

        /// <summary>
        /// The Sekiro read: a posture break puts a MARKER ON THE ENEMY, and only an attack press aimed at
        /// a marked enemy comes out as a deathblow. Everything here is about the player being able to tell
        /// the difference — the suite previously proved the deathblow fired and was completely blind to
        /// whether anything told the player it was about to. See docs/ENGINEERING-LOG.md.
        /// </summary>
        /// <summary>
        /// The flask interrupt (2026-09-06): the forced path. A dummy between phrases, told to punish,
        /// must be in Windup THIS frame with a cue still owed cueLead before impact, and the counter must
        /// say so. The dice (flaskPunishChance) are EditMode data; this proves the state transition.
        /// </summary>
        IEnumerator TestFlaskPunish()
        {
            EnemyController e = null;
            yield return SpawnDummy(combat.transform.position + combat.transform.forward * 3f, x => e = x);
            if (e == null) { Skip("FlaskPunish", "no dummy"); yield break; }
            int before = e.FlaskPunishes;
            bool started = e.PunishFlaskNow(3f);
            Check("FlaskPunish_Starts", started, "PunishFlaskNow refused on a fresh dummy");
            Check("FlaskPunish_IsAWindup", e.Current == EnemyController.State.Windup, "state=" + e.Current);
            Check("FlaskPunish_Counted", e.FlaskPunishes == before + 1);
            Check("FlaskPunish_CueStillOwed", e.NextCueTime > Time.time - 0.01f, "the punish must keep the tell: cue at " + e.NextCueTime + " now " + Time.time);
            bool again = e.PunishFlaskNow(3f);
            Check("FlaskPunish_NeverInsideAWindup", !again, "one attack at a time");
            UnityEngine.Object.Destroy(e.gameObject);
            yield return null;
        }

        /// <summary>
        /// The sentry FLARE loop (2026-09-06): a broken sentry detonates (dies, throws a flare), and a DASH
        /// at the glowing flare pulls the player to it and tosses them up. Forced through the public
        /// entry points; the aim cone is a FlareGrapple.FindTarget matter the sandbox proves by hand.
        /// </summary>
        /// <summary>
        /// The two prompt channels (2026-09-06). A momentary flash ("PERFECT") is drawn over a standing cue
        /// ("GRAPPLE  [DASH]") and then HANDS IT BACK — before the split, one PERFECT erased a live cue for
        /// good, because every standing writer is edge-triggered and never re-raises the same string.
        /// </summary>
        IEnumerator TestPromptChannels()
        {
            var view = FindAnyObjectByType<PromptView>();
            if (view == null) { Skip("PromptChannels", "no PromptView in the HUD; run 5. Build HUD"); yield break; }

            GameEvents.RaisePromptChanged("");
            GameEvents.RaisePromptFlash("", 0f);
            yield return null;

            GameEvents.RaisePromptChanged("GRAPPLE  [DASH]");
            yield return null;
            Check("Prompt_StandingShows", view.Current == "GRAPPLE  [DASH]", "current=" + view.Current);

            GameEvents.RaisePromptFlash("PERFECT", 0.25f);
            yield return null;
            Check("Prompt_FlashDrawsOverTheStandingCue", view.Current == "PERFECT", "current=" + view.Current);

            yield return WaitRealtime(0.35f);
            Check("Prompt_StandingCueComesBack", view.Current == "GRAPPLE  [DASH]",
                "current=" + view.Current + " -- a flash must hand the standing cue back, not eat it");

            GameEvents.RaisePromptChanged("");
            yield return null;
            Check("Prompt_EmptyStandingClears", view.Current.Length == 0, "current=" + view.Current);

            // ---- the owner key (2026-09-06) --------------------------------------------------------
            // The slot has many writers and all of them are edge-triggered, so a writer going quiet must
            // not be able to blank a cue it never wrote. Only the holder — or nobody — may clear.
            GameEvents.RaisePromptChanged(PromptOwner.Grapple, "GRAPPLE  [DASH]");
            yield return null;
            Check("Prompt_OwnerTakesTheLine",
                view.Current == "GRAPPLE  [DASH]" && view.StandingOwner == PromptOwner.Grapple,
                "current=" + view.Current + " owner=" + view.StandingOwner);

            GameEvents.RaisePromptChanged(PromptOwner.Execute, "");
            yield return null;
            Check("Prompt_AnotherOwnersClearIsIgnored", view.Current == "GRAPPLE  [DASH]",
                "current=" + view.Current + " -- a writer going quiet must not blank another writer's cue");

            GameEvents.RaisePromptChanged("");
            yield return null;
            Check("Prompt_AnonymousClearIsIgnoredWhileOwned", view.Current == "GRAPPLE  [DASH]",
                "current=" + view.Current);

            GameEvents.RaisePromptChanged(PromptOwner.Grapple, "");
            yield return null;
            Check("Prompt_TheHolderMayClear", view.Current.Length == 0,
                "current=" + view.Current + " owner=" + view.StandingOwner);
        }

        IEnumerator TestFlare()
        {
            var sentryPf = LevelEditor.I != null ? LevelEditor.I.PrefabFor("pshooter_enemy01") : null;
            if (sentryPf == null) { Skip("Flare", "no pshooter_enemy01 in the level editor library; run 4 / 5"); yield break; }
            yield return ResetPlayerState();
            Vector3 pos = combat.transform.position + combat.transform.forward * 6f;
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var navHit, 6f, UnityEngine.AI.NavMesh.AllAreas)) pos = navHit.position;
            var go = Instantiate(sentryPf, pos, Quaternion.LookRotation(-combat.transform.forward));
            go.name = "~TestSentry";
            var e = go.GetComponent<EnemyController>();
            var burst = go.GetComponent<SentryBurst>();
            if (e != null) e.aggroLocked = true;
            yield return null; yield return null;
            Check("Flare_SentryCarriesBurst", burst != null);
            if (burst == null) { Destroy(go); yield break; }

            int flaresBefore = SentryFlare.Live.Count;
            // 2026-09-06 (user): a parkour enemy never waits to be finished -- a posture break IS the detonation
            // (deferred one frame so a same-frame grapple finish can claim the body), and it dies on the spot.
            e.Posture.Break();
            yield return null; yield return null;
            Check("Flare_BreakDetonates", !e.IsAlive, "state=" + e.Current);
            Check("Flare_BreakThrowsAFlare", SentryFlare.Live.Count == flaresBefore + 1 && burst.LastFlare != null,
                "live=" + SentryFlare.Live.Count);

            // The exception: a finish by the hook (break + execute in one frame, then the killing blow) throws no flare.
            var go2 = Instantiate(sentryPf, pos + Vector3.right * 2.5f, Quaternion.LookRotation(-combat.transform.forward));
            go2.name = "~TestSentry2";
            var e2 = go2.GetComponent<EnemyController>();
            if (e2 != null) e2.aggroLocked = true;
            yield return null; yield return null;
            int flaresMid = SentryFlare.Live.Count;
            e2.Posture.Break();
            e2.BeginExecuted(combat.transform);
            e2.Health.TakeDamage(new DamageInfo { damage = 999999f, source = combat.gameObject, isExecute = true });
            yield return null; yield return null;
            Check("Flare_ExecuteFinishThrowsNothing", !e2.IsAlive && SentryFlare.Live.Count == flaresMid,
                "alive=" + e2.IsAlive + " live=" + SentryFlare.Live.Count + " (was " + flaresMid + ")");
            Destroy(go2);
            var flare = burst.LastFlare;
            if (flare == null) yield break;
            yield return WaitRealtime(0.4f);
            Check("Flare_Rises", flare != null && flare.transform.position.y > pos.y + 1.5f,
                "y=" + (flare != null ? flare.transform.position.y.ToString("0.0") : "gone") + " from " + pos.y.ToString("0.0"));
            Check("Flare_Glows", flare != null && flare.Grappleable && flare.Glow > 0.8f, "glow=" + (flare != null ? flare.Glow.ToString("0.00") : "gone"));

            var grapple = combat.GetComponent<FlareGrapple>();
            Check("Flare_PlayerCarriesGrapple", grapple != null);
            if (grapple == null || flare == null) yield break;
            int tossesBefore = grapple.Tosses;
            bool started = grapple.GrappleNow(flare);
            Check("Flare_GrappleStarts", started && motor.IsPulling, "started=" + started + " pulling=" + motor.IsPulling);
            yield return WaitUntilOrTimeout(() => !motor.IsPulling, 2f);
            yield return null;
            Check("Flare_TossesUp", grapple.Tosses == tossesBefore + 1 && motor.Velocity.y > 8f,
                "tosses=" + grapple.Tosses + " velY=" + motor.Velocity.y.ToString("0.0"));
            Check("Flare_IsSpentByTheUse", flare == null, "a used flare is gone");

            // ---- the raised toss and its BOUNDED hangtime (2026-09-06, the user) ------------------
            // The toss leaves at tossUpSpeed and opens a hang window; the rise is never scaled, so the
            // apex is tossUpSpeed^2/2g (5.4 m at 18 against -30) and the seconds are spent on the FALL.
            Check("Flare_TossLeavesAtTheRaisedSpeed", motor.Velocity.y > grapple.tossUpSpeed - 3f,
                "velY=" + motor.Velocity.y.ToString("0.0") + " toss=" + grapple.tossUpSpeed.ToString("0.0"));
            Check("Flare_HangWindowOpens", motor.IsHanging && motor.HangRemaining > grapple.tossHangSeconds - 0.5f,
                "hanging=" + motor.IsHanging + " left=" + motor.HangRemaining.ToString("0.00"));
            float tossY = combat.transform.position.y;
            float peakY = tossY;
            float riseTimer = 0f;
            // DIAGNOSTIC (2026-09-06). This check reported 4.3 m once, against a motor that measures 5.36 m
            // for the same Launch(18, 2) driven by hand in play mode -- and passed at 5.3 m on every run
            // since, so that red was staging noise, not lost height. The fields stay because they make the
            // ONE failure mode that would be real self-evident: the jump cut is suspended only WHILE
            // hanging, so a window that shuts at v1 finishes the rise under
            // gravity * (1 + jumpCutGravityMultiplier) and lands near 4.3 m. hangClosedAtVy = -999 means
            // the window stayed open the whole way up and the shortfall was measurement, not the motor.
            float vyAtHangClose = -999f;
            float hangCloseAfter = -1f;
            while (riseTimer < 0.9f)
            {
                peakY = Mathf.Max(peakY, combat.transform.position.y);
                if (vyAtHangClose < -900f && !motor.IsHanging)
                {
                    vyAtHangClose = motor.Velocity.y;
                    hangCloseAfter = riseTimer;
                }
                riseTimer += Time.unscaledDeltaTime;
                yield return null;
            }
            // 4.5 m is the old toss (a nominal 3.3, a felt 1.9 once the jump cut ate it) plus a margin:
            // a pass that quietly loses the height fails here, not in a playtest.
            Check("Flare_TossRisesHigherThanAJump", peakY - tossY > 4.5f,
                "rise=" + (peakY - tossY).ToString("0.0") + " m from y=" + tossY.ToString("0.0")
                + " hangClosedAtVy=" + vyAtHangClose.ToString("0.0") + " after=" + hangCloseAfter.ToString("0.00")
                + "s peakAbs=" + peakY.ToString("0.0"));

            // Still in the air a second and a half later, unless the floor legally ended the window.
            yield return WaitRealtime(0.7f);
            Check("Flare_HangSurvivesTheFall", motor.IsGrounded || motor.IsHanging,
                "grounded=" + motor.IsGrounded + " hanging=" + motor.IsHanging
                + " left=" + motor.HangRemaining.ToString("0.00") + " velY=" + motor.Velocity.y.ToString("0.0"));
            // ...and a float is a float: nothing like a normal 1.6 s fall (-48 m/s) is on the clock.
            Check("Flare_HangIsAFloatNotAFall", motor.IsGrounded || motor.Velocity.y > -20f,
                "velY=" + motor.Velocity.y.ToString("0.0"));

            // A dash is a deliberate air action and SPENDS the window. Conditional on the dash actually
            // firing, so a stamina refusal can never redden this.
            bool airborneBeforeDash = !motor.IsGrounded;
            motor.RequestDash();
            yield return null; yield return null;
            Check("Flare_DashSpendsTheHang", !airborneBeforeDash || !motor.IsDashing || !motor.IsHanging,
                "dashing=" + motor.IsDashing + " hanging=" + motor.IsHanging);

            // And it expires on its own: past the window there is no float left to find.
            yield return WaitRealtime(grapple.tossHangSeconds + 0.4f);
            Check("Flare_HangExpiresOnItsOwn", !motor.IsHanging && motor.HangRemaining <= 0f,
                "hanging=" + motor.IsHanging + " left=" + motor.HangRemaining.ToString("0.00"));

            yield return WaitRealtime(0.4f);
            yield return ResetPlayerState();
        }

        IEnumerator TestDeathblowMarker()
        {
            // ---- the marker is its own material, loud, and not the alert tell ------------------
#if UNITY_EDITOR
            var markMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_DeathblowMark.mat");
            var tellMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_AlertTell.mat");
            Check("Deathblow_MarkMaterialExists", markMat != null, "Assets/Materials/M_DeathblowMark.mat");
            if (markMat != null)
            {
                float peak = PeakEmission(markMat);
                Check("Deathblow_MarkBloomsHard", peak >= 1.05f * 2f,
                    "peak=" + peak.ToString("0.###") + " (a marker under the bloom threshold is a marker " +
                    "nobody sees mid-exchange)");
                if (tellMat != null)
                {
                    Color a = markMat.GetColor("_EmissionColor");
                    Color b = tellMat.GetColor("_EmissionColor");
                    // Hue separation, measured on the NORMALISED colours so brightness cannot fake it.
                    // These two markers hang in the same place and mean opposite things; if they ever
                    // converge on a hue, the player is being told "kill this" and "you are about to die"
                    // in the same colour.
                    Vector3 na = new Vector3(a.r, a.g, a.b).normalized;
                    Vector3 nb = new Vector3(b.r, b.g, b.b).normalized;
                    Check("Deathblow_HueSeparatedFromAlertTell", Vector3.Distance(na, nb) > 0.5f,
                        "mark=" + na.ToString("F2") + " tell=" + nb.ToString("F2"));
                    Check("Deathblow_QuieterThanAlertTell", PeakEmission(markMat) < PeakEmission(tellMat),
                        "mark=" + PeakEmission(markMat).ToString("0.##") + " tell=" + PeakEmission(tellMat).ToString("0.##") +
                        " (the thing that can kill YOU must out-shout the thing you can kill)");
                }

                // The glyph now rides the TORSO, which is where the lock-on dot already lives, so these
                // two must separate on their own axes and not merely on the height they sit at.
                var dotMat0 = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_LockOnDot.mat");
                if (dotMat0 != null)
                {
                    Color a = markMat.GetColor("_EmissionColor");
                    Color b = dotMat0.GetColor("_EmissionColor");
                    Vector3 na = new Vector3(a.r, a.g, a.b).normalized;
                    Vector3 nb = new Vector3(b.r, b.g, b.b).normalized;
                    // The mark itself is not drawn, but M_DeathblowMark is still the hue of the commit
                    // shatter thrown at that point, and the lock-on dot is a pale spot in the same place.
                    Check("Deathblow_HueSeparatedFromLockDot", Vector3.Distance(na, nb) > 0.5f,
                        "mark=" + na.ToString("F2") + " dot=" + nb.ToString("F2") +
                        " (this is my target must never look like this one dies now)");
                    Check("Deathblow_LouderThanLockDot",
                        PeakEmission(markMat) > 1.05f && PeakEmission(dotMat0) < 1.05f,
                        "mark=" + PeakEmission(markMat).ToString("0.##") + " dot=" + PeakEmission(dotMat0).ToString("0.##") +
                        " (bloom threshold 1.05: the glyph must cross it and the dot must not)");
                }
            }
#else
            Skip("Deathblow_MarkMaterial", "material assets are only readable in the editor");
#endif

            EnemyController dummy = null;
            Vector3 spot = combat.transform.position + combat.transform.forward * 2.4f;
            yield return SpawnDummy(spot, e => dummy = e);
            if (dummy == null) { Skip("Deathblow_DummySpawned", "no enemy prefab"); yield break; }
            FacePoint(dummy.transform.position);
            yield return null;

            var dv = dummy.GetComponentInChildren<EnemyVisuals>(true);
            GameObject mark = dv != null ? dv.deathblowMarker : null;
            Check("Deathblow_MarkerShippedOnPrefab", mark != null,
                "EnemyVisuals.deathblowMarker (rule 9: PrefabFactory must write it)");
            if (mark == null) { Destroy(dummy.gameObject); yield break; }
            Check("Deathblow_MarkerHiddenBeforeBreak", !mark.activeInHierarchy,
                "a marker up before the posture breaks is a lie");

            // ---- it is ONE SMALL FLAT GLOWING SPOT, and it is DRAWN ------------------------------
            // It started as a crossed diamond of three cubes over the head: an object in the world
            // rather than a mark on a body, and at deathblow range a solid violet mass the camera runs
            // into as it closes. Flat, small and on the chest is the whole fix, so each of those three
            // words gets an assertion.
            var markComp0 = mark.GetComponent<DeathblowMarker>();
            Check("Deathblow_MarkIsDrawn", markComp0 != null && markComp0.drawMark,
                "drawMark=" + (markComp0 != null ? markComp0.drawMark.ToString() : "no component") +
                " (rule 9: PrefabFactory must SHIP it true, not rely on a field initialiser)");
            var markRenderers = mark.GetComponentsInChildren<MeshRenderer>(true);
            Check("Deathblow_MarkIsASingleRenderer", markRenderers.Length == 1,
                "renderers=" + markRenderers.Length + " (a mark on a body is one spot, not an assembly)");
            var markFilter = mark.GetComponentInChildren<MeshFilter>(true);
            int markVerts = markFilter != null && markFilter.sharedMesh != null ? markFilter.sharedMesh.vertexCount : -1;
            Check("Deathblow_MarkIsFlat", markVerts == 4,
                "vertexCount=" + markVerts + " (4 = a quad; a cube is 24 and has depth for the camera " +
                "and the wand to run into)");
            if (markComp0 != null)
            {
                // A FIXED size is the bug this replaced. The spot stands off the chest toward the viewer
                // — it has to, or it renders inside the mesh — so it is always nearer than the body it
                // marks and grows faster than the body does as the player closes. At 0.22 m fixed it
                // filled a quarter of the frame at stabbing range while the grunt filled a fifth.
                Check("Deathblow_MarkIsAngularlySized",
                    markComp0.angularSize > 0.02f && markComp0.angularSize <= 0.15f,
                    "angularSize=" + markComp0.angularSize.ToString("0.###") +
                    " (world units per metre of distance; ~0.115 is about 5% of the frame at 95 deg FOV)");
                Check("Deathblow_MarkCannotBalloon", markComp0.maxScale <= 0.45f,
                    "maxScale=" + markComp0.maxScale.ToString("0.00") +
                    "m (a ceiling is what stops a mark on a distant boss growing into the head markers)");
                Check("Deathblow_MarkStandoffCappedByDistance",
                    markComp0.frontOffsetMaxFraction > 0f && markComp0.frontOffsetMaxFraction <= 0.5f,
                    "fraction=" + markComp0.frontOffsetMaxFraction.ToString("0.00") +
                    " (an uncapped stand-off puts the spot in the player's face when the body is on top " +
                    "of them)");
            }
            if (markRenderers.Length > 0)
            {
                var mm = markRenderers[0].sharedMaterial;
                Check("Deathblow_MarkGlows",
                    mm != null && mm.HasProperty("_EmissionColor") && PeakEmission(mm) > 1.05f,
                    "peak=" + (mm != null ? PeakEmission(mm).ToString("0.##") : "no material") +
                    " (against a near-black enemy an unlit spot is a dim decal; it has to be over the " +
                    "1.05 bloom threshold to read as a hot mark)");
                Check("Deathblow_MarkShaderIsURP",
                    mm != null && mm.shader != null && mm.shader.name.StartsWith("Universal Render Pipeline/"),
                    "shader=" + (mm != null && mm.shader != null ? mm.shader.name : "none") +
                    " (anything else renders magenta)");
            }

            // ---- a press with NO marked target must swing ---------------------------------------
            // This is the whole "it ripostes automatically" complaint stated as a test: an attack press
            // is an ATTACK unless there is something marked to deathblow.
            Check("Deathblow_NoTargetBeforeBreak", !exec.HasMarkedTarget,
                "target=" + (exec.Target != null ? exec.Target.name : "null"));
            weapons.CancelAttack();
            yield return null;
            bool pressed = weapons.TryAttack();
            yield return null;
            Check("Deathblow_UnmarkedPressSwings", pressed && weapons.IsAttacking && !exec.IsExecuting,
                "pressed=" + pressed + " swinging=" + weapons.IsAttacking + " executing=" + exec.IsExecuting);
            weapons.CancelAttack();
            yield return null;

            // ---- posture break raises the marker ------------------------------------------------
            dummy.Posture.Add(dummy.Posture.Max * 2f);
            yield return null;
            Check("Deathblow_BreakRaisesMarker", mark.activeInHierarchy,
                "staggered=" + dummy.IsStaggered + " marker=" + mark.activeInHierarchy);

            // ---- the marker clears when the window closes ---------------------------------------
            dummy.Posture.EndStagger();
            yield return null;
            Check("Deathblow_RecoveryClearsMarker", !mark.activeInHierarchy && !dummy.IsStaggered,
                "staggered=" + dummy.IsStaggered + " marker=" + mark.activeInHierarchy);

            // ---- and only a MARKED target turns a press into a deathblow -------------------------
            dummy.Posture.ResetFull();
            dummy.Posture.Add(dummy.Posture.Max * 2f);
            yield return WaitUntilOrTimeout(() => exec.HasMarkedTarget, 2f);
            Check("Deathblow_InteractorMarksBrokenEnemy", !waitTimedOut,
                "target=" + (exec.Target != null ? exec.Target.name : "null"));
            Check("Deathblow_MarkerUpWhileTargetable", mark.activeInHierarchy);

            // ---- the pose and the mark, judged at the SHIPPED standoff ---------------------------
            // Both at ordinary scale and at the 2.2x the boss is built at, because scale multiplies a
            // pose error: a lean that costs 0.9 m of clearance on a grunt costs 2 m on the Warden.
            yield return DeathblowFraming(dummy, mark, 1f);
            yield return DeathblowFraming(dummy, mark, 2.2f);
            FacePoint(dummy.transform.position);
            yield return null;

            if (wandCtl != null) wandCtl.ResetCooldown();
            bool consumed = weapons.TryAttack();
            yield return null;
            Check("Deathblow_MarkedPressExecutes", consumed && exec.IsExecuting && !weapons.IsAttacking,
                "consumed=" + consumed + " executing=" + exec.IsExecuting + " swinging=" + weapons.IsAttacking);
            // Committing spends the window: the glyph must go the instant the blow starts, or it invites
            // a second press that can never land.
            Check("Deathblow_CommitClearsMarker", !mark.activeInHierarchy,
                "marker=" + mark.activeInHierarchy);

            yield return WaitUntilOrTimeout(() => !exec.IsExecuting, exec.duration + 4f);
            Check("Deathblow_Completes", !waitTimedOut);
            yield return SettleTimeScale();
            if (dummy != null) Destroy(dummy.gameObject);
            yield return null;
        }


        /// <summary>
        /// The riposte has to be LOOKABLE-AT. Two things had made it not so: the posture-break pose
        /// pitched the body forward into the camera, and the glyph hung over the head, where a 2.2x boss
        /// puts it five metres in the air. Both are geometry, so both can be measured.
        ///
        /// <para>Measured at the real <c>ExecuteInteractor.stabStandoff</c> for the given body scale,
        /// because that is the only distance this frame is ever composed at.</para>
        /// </summary>
        IEnumerator DeathblowFraming(EnemyController dummy, GameObject mark, float bodyScale)
        {
            string tag = bodyScale > 1.5f ? "BossScale" : "GruntScale";
            if (dummy == null || mark == null) { Skip("Deathblow_Framing_" + tag, "no dummy"); yield break; }

            Vector3 s0 = dummy.transform.localScale;
            dummy.transform.localScale = s0 * bodyScale;

            // Stand exactly where the riposte actually happens. The step-in only CLOSES the gap and the
            // press must be inside ExecuteInteractor.range, so for a big body the real distance is the
            // RANGE, not stabStandoff * scale — on a 2.2x boss the standoff (4.84 m) is further out than
            // the press is even legal from, and testing there would test the easy case.
            float standoff = Mathf.Min(exec.stabStandoff * bodyScale, exec.range);
            Vector3 away = combat.transform.position - dummy.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            Vector3 stand = dummy.transform.position + away.normalized * standoff;
            stand.y = combat.transform.position.y;
            motor.Teleport(stand, 0f);
            FacePoint(dummy.transform.position);
            yield return WaitRealtime(0.35f);          // let the stagger pose settle

            Vector3 eye = look.Cam.position;

            // ---- 1. NO ENEMY GEOMETRY IN THE PLAYER FACE --------------------------------------
            // The camera near plane is 0.03. Anything inside it is clipped open and the frame fills
            // with the enemy interior; anything within a few tens of centimetres of it is a wall of
            // body where the riposte is supposed to be. Renderer bounds are world AABBs, so this is
            // conservative in the safe direction.
            float nearest = float.MaxValue;
            bool inside = false;
            string nearestName = "";
            foreach (var r in dummy.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || r.transform.IsChildOf(mark.transform)) continue;
                if (r.bounds.Contains(eye)) inside = true;
                float d = Vector3.Distance(r.bounds.ClosestPoint(eye), eye);
                if (d < nearest)
                {
                    nearest = d;
                    nearestName = r.name + "<" + r.GetType().Name + "> size=" + r.bounds.size.ToString("0.0");
                }
            }
            Check("Deathblow_StaggerPoseClearsNearPlane_" + tag,
                !inside && nearest > 0.03f,
                "nearest=" + nearest.ToString("0.00") + "m cameraInsideBody=" + inside +
                " standoff=" + standoff.ToString("0.00") + " via " + nearestName +
                " (a break that pitches the body FORWARD walks the chest through the lens)");
            // And a real margin, not just technically-not-clipping: the whole point of the standoff is
            // that the victim stays framed.
            Check("Deathblow_StaggerPoseStaysFramed_" + tag, nearest > 0.5f,
                "nearest=" + nearest.ToString("0.00") + "m via " + nearestName +
                " (under half a metre the body IS the frame)");

            // ---- 2. THE MARK IS ON THE TORSO, NOT OVERHEAD ------------------------------------
            var dm = mark.GetComponent<DeathblowMarker>();
            if (dm == null) { Skip("Deathblow_MarkOnTorso_" + tag, "no DeathblowMarker component"); }
            else
            {
                Check("Deathblow_MarkMountedOnTorso_" + tag,
                    dm.bodyHeight > 0.7f && dm.bodyHeight < 2.0f,
                    "bodyHeight=" + dm.bodyHeight.ToString("0.00") +
                    " (the alert cube is at 2.5 and the posture bar at 2.6; a glyph up there is 5 m in " +
                    "the air on a 2.2x boss and out of the frame at deathblow range)");

                // The marker must not sit at the enemy centre of mass, which is INSIDE it. Same trap
                // that hid the lock-on dot for a whole pass, and it fails silently: every assertion
                // about position and visibility passes while nothing can be seen.
                Vector3 axis = dummy.transform.position + Vector3.up * (dm.bodyHeight * dummy.transform.lossyScale.y);
                Vector3 world = dm.SurfacePoint(eye);
                // The offset is HORIZONTAL toward the eye, deliberately — lifting it vertically as well
                // would slide the mark up the chest whenever the player looks down from a ledge. So the
                // check is not the lock-on dot's "sits on the eye-to-chest ray": on a 2.2x boss the
                // sternum is 3.2 m up and that ray climbs steeply, while the mark steps straight out
                // sideways. The real invariant is that the step is TOWARD the eye and long enough to
                // clear the body.
                Vector3 outward = world - axis;
                Vector3 flatOut = new Vector3(outward.x, 0f, outward.z);
                Vector3 flatEye = eye - axis;
                flatEye.y = 0f;
                float aligned = flatOut.sqrMagnitude > 0.0001f && flatEye.sqrMagnitude > 0.0001f
                    ? Vector3.Dot(flatOut.normalized, flatEye.normalized) : 0f;
                Check("Deathblow_MarkSteppedTowardTheEye_" + tag, aligned > 0.99f,
                    "alignment=" + aligned.ToString("0.000") + " step=" + flatOut.magnitude.ToString("0.00") +
                    "m (a mark stepped anywhere but toward the viewer is still inside the body from " +
                    "somewhere the player can stand)");
                Check("Deathblow_MarkVerticallyOnTheSternum_" + tag, Mathf.Abs(outward.y) < 0.05f,
                    "dy=" + outward.y.ToString("0.000") + " (horizontal only: a vertical lift slides the " +
                    "mark up the chest as soon as the player looks down at it)");
                Check("Deathblow_MarkClearsOwnBody_" + tag,
                    flatOut.magnitude > 0.45f * dummy.transform.lossyScale.x,
                    "step=" + flatOut.magnitude.ToString("0.00") + "m capsuleRadius=" +
                    (0.45f * dummy.transform.lossyScale.x).ToString("0.00") +
                    "m (a mark left on the centre line renders inside the mesh and is never seen)");

                // ---- 3. AND IT IS NOT THE LOCK-ON DOT --------------------------------------------
                var lc = FindAnyObjectByType<LockOnController>();
                float lockH = lc != null ? lc.markerHeight : 1.05f;
                Check("Deathblow_MarkHeightSeparatedFromLockDot_" + tag,
                    Mathf.Abs(dm.bodyHeight - lockH) >= 0.3f,
                    "mark=" + dm.bodyHeight.ToString("0.00") + " lockDot=" + lockH.ToString("0.00") +
                    " (two spots on one torso: \"this is my target\" must never be confusable with " +
                    "\"this one is ready to die\", and the blast blooms at the mark as well)");
            }

            dummy.transform.localScale = s0;
            yield return null;
        }

        // ================================================================ 5b. LOCK-ON

        /// <summary>
        /// Souls target lock in first person. Covers the four things that make it a lock rather than a
        /// toggle: acquire, switch, release, and the automatic drops (death, range). Also asserts the
        /// dot's whole reason for existing - that it is the ONE combat marker held under the bloom
        /// threshold, so it can never be confused with the alert tell or the deathblow glyph.
        /// </summary>
        IEnumerator TestLockOn()
        {
            if (lockOn == null)
            {
                Check("LockOn_ControllerShippedOnPlayer", false,
                    "no LockOnController on the Player (rule 9: PrefabFactory.BuildLockOn must add it)");
                yield break;
            }
            Check("LockOn_ControllerShippedOnPlayer", true);
            Check("LockOn_MarkerShippedOnPlayer",
                lockOn.marker != null && lockOn.marker.renderers != null && lockOn.marker.renderers.Length > 0,
                "marker=" + (lockOn.marker != null) +
                " renderers=" + (lockOn.marker != null && lockOn.marker.renderers != null ? lockOn.marker.renderers.Length : 0));

            // ---- the dot is the QUIET marker ----------------------------------------------------
#if UNITY_EDITOR
            var dotMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_LockOnDot.mat");
            var tellMat2 = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_AlertTell.mat");
            var markMat2 = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_DeathblowMark.mat");
            Check("LockOn_DotMaterialExists", dotMat != null, "Assets/Materials/M_LockOnDot.mat");
            if (dotMat != null)
            {
                float peak = PeakEmission(dotMat);
                // THE POINT OF THE WHOLE COLOUR CHOICE. The tell means danger and the glyph means
                // opportunity, and both sit 2.5-3x over the 1.05 bloom threshold because they must grab
                // you. The lock dot means neither - it is up for the whole fight and has to stay
                // ignorable. Under the threshold it never blooms, and a marker that does not bloom is
                // separable from two that do even in peripheral vision.
                Check("LockOn_DotDoesNotBloom", peak > 0.4f && peak < 1.05f,
                    "peak=" + peak.ToString("0.###") + " (visible, but under the 1.05 bloom threshold)");
                Color dc = dotMat.GetColor("_EmissionColor");
                float maxc = Mathf.Max(dc.r, Mathf.Max(dc.g, dc.b));
                float minc = Mathf.Min(dc.r, Mathf.Min(dc.g, dc.b));
                // Desaturated on purpose: every saturated slot in the palette already means something.
                Check("LockOn_DotIsDesaturated", maxc > 0.0001f && (maxc - minc) / maxc < 0.15f,
                    "rgb=" + dc.ToString("F2"));
                if (tellMat2 != null)
                    Check("LockOn_QuieterThanAlertTell", peak < PeakEmission(tellMat2) * 0.5f,
                        "dot=" + peak.ToString("0.##") + " tell=" + PeakEmission(tellMat2).ToString("0.##"));
                if (markMat2 != null)
                    Check("LockOn_QuieterThanDeathblowMark", peak < PeakEmission(markMat2) * 0.5f,
                        "dot=" + peak.ToString("0.##") + " mark=" + PeakEmission(markMat2).ToString("0.##"));
            }
#else
            Skip("LockOn_DotMaterial", "material assets are only readable in the editor");
#endif

            lockOn.Release();

            // ---- acquire -------------------------------------------------------------------------
            EnemyController a = null, b = null;
            Vector3 fwd = combat.transform.forward;
            Vector3 right = combat.transform.right;
            yield return SpawnDummy(combat.transform.position + fwd * 6f, e => a = e);
            if (a == null) { Skip("LockOn_DummySpawned", "no enemy prefab"); yield break; }
            yield return SpawnDummy(combat.transform.position + fwd * 6f + right * 5f, e => b = e);

            FacePoint(a.transform.position);
            yield return null;
            bool got = lockOn.TryLockOn();
            yield return null;
            Check("LockOn_AcquiresEnemyInView", got && lockOn.Target == a,
                "got=" + got + " target=" + (lockOn.Target != null ? lockOn.Target.name : "null"));
            Check("LockOn_DotShownOnAcquire", lockOn.marker == null || lockOn.marker.IsShown);
            if (lockOn.marker != null)
            {
                // The dot marks the CHEST, which is a point INSIDE a 0.45 m capsule, so it is pulled
                // toward the eye to clear the body. It must stay on the eye-to-chest ray (or it is
                // marking the wrong thing) and in front of the chest (or the enemy renders over it).
                Vector3 eye = look.Cam.position;
                Vector3 ray = (lockOn.TargetPoint - eye).normalized;
                Vector3 rel = lockOn.marker.transform.position - eye;
                float along = Vector3.Dot(rel, ray);
                float off = (rel - ray * along).magnitude;
                Check("LockOn_DotSitsOnLineToTargetChest", off < 0.05f, "lateral=" + off.ToString("0.000"));
                Check("LockOn_DotClearsTargetBody",
                    along < Vector3.Distance(eye, lockOn.TargetPoint) - 0.4f,
                    "along=" + along.ToString("0.00") +
                    " chest=" + Vector3.Distance(eye, lockOn.TargetPoint).ToString("0.00") +
                    " (a dot left on the chest point renders inside the capsule and is never seen)");
            }

            // ---- the assist closes the gap while the mouse is idle -------------------------------
            // No device is being driven here, so LookDelta is zero and the yield gate is fully open:
            // this measures the assist itself. What it CANNOT prove is the half that matters most -
            // that the gate shuts under a real hand. That is a playtest, not a test.
            Vector3 flat = lockOn.TargetPoint - look.Cam.position;
            flat.y = 0f;
            float desiredYaw = Quaternion.LookRotation(flat.normalized).eulerAngles.y;
            look.SetYaw(desiredYaw + 20f);
            yield return null;
            float before = Mathf.Abs(Mathf.DeltaAngle(look.Yaw, desiredYaw));
            yield return WaitRealtime(0.6f);
            float after = Mathf.Abs(Mathf.DeltaAngle(look.Yaw, desiredYaw));
            Check("LockOn_AssistRecentresTarget", after < before * 0.4f,
                "before=" + before.ToString("0.0") + " after=" + after.ToString("0.0"));

            look.SetYaw(desiredYaw);
            yield return null;
            float settled = look.Yaw;
            yield return WaitRealtime(0.3f);
            Check("LockOn_AssistHasDeadzoneInYaw", Mathf.Abs(Mathf.DeltaAngle(look.Yaw, settled)) < 1f,
                "drift=" + Mathf.DeltaAngle(look.Yaw, settled).ToString("0.00") +
                " (an assist still nudging an on-target crosshair is a jitter)");

            // ---- switch --------------------------------------------------------------------------
            if (b != null)
            {
                FacePoint(b.transform.position);
                yield return null;
                bool switched = lockOn.TryLockOn();
                yield return null;
                Check("LockOn_SwitchesToTargetUnderCrosshair", switched && lockOn.Target == b,
                    "target=" + (lockOn.Target != null ? lockOn.Target.name : "null") +
                    " (a press with the aim OFF the held target must switch, not release)");

                // ---- release: same key, still looking at what you hold ---------------------------
                FacePoint(b.transform.position);
                yield return null;
                lockOn.TryLockOn();
                yield return null;
                Check("LockOn_ReleasesWhenAimedAtHeldTarget", !lockOn.HasTarget,
                    "target=" + (lockOn.Target != null ? lockOn.Target.name : "null"));
                Check("LockOn_DotHiddenOnRelease", lockOn.marker == null || !lockOn.marker.IsShown);
                Destroy(b.gameObject);
                yield return null;
            }

            // ---- auto-drop: the target dies ------------------------------------------------------
            FacePoint(a.transform.position);
            yield return null;
            lockOn.TryLockOn();
            yield return null;
            Check("LockOn_ReacquiredForDeathTest", lockOn.Target == a);
            a.Health.TakeDamage(new DamageInfo { damage = 999999f, isExecute = true });
            yield return WaitUntilOrTimeout(() => !lockOn.HasTarget, 3f);
            Check("LockOn_DropsWhenTargetDies", !waitTimedOut,
                "target=" + (lockOn.Target != null ? lockOn.Target.name : "null") + " alive=" + a.IsAlive);
            Check("LockOn_DotHiddenAfterTargetDies", lockOn.marker == null || !lockOn.marker.IsShown);
            Destroy(a.gameObject);
            yield return null;

            // ---- auto-drop: out of range ---------------------------------------------------------
            EnemyController c = null;
            yield return SpawnDummy(combat.transform.position + fwd * 6f, e => c = e);
            if (c != null)
            {
                FacePoint(c.transform.position);
                yield return null;
                lockOn.TryLockOn();
                yield return null;
                Check("LockOn_ReacquiredForRangeTest", lockOn.Target == c);
                // The agent owns the transform; disable it or the dummy snaps straight back.
                var nav = c.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (nav != null) nav.enabled = false;
                c.transform.position = look.Cam.position + look.Cam.forward * (lockOn.dropRange + 12f);
                yield return WaitUntilOrTimeout(() => !lockOn.HasTarget, 2f);
                Check("LockOn_DropsWhenTargetLeavesRange", !waitTimedOut,
                    "dropRange=" + lockOn.dropRange +
                    " dist=" + Vector3.Distance(look.Cam.position, c.transform.position).ToString("0.0"));
                Destroy(c.gameObject);
                yield return null;
            }

            lockOn.Release();
            yield return null;
        }

        // ================================================================ 6. WEAPONS

        IEnumerator TestWeapons()
        {
            if (weapons.loadout == null || weapons.loadout.Length == 0)
            {
                Check("Weapons_LoadoutPopulated", false, "loadout empty");
                yield break;
            }
            Check("Weapons_LoadoutPopulated", weapons.loadout.Length >= 3, "count=" + weapons.loadout.Length);

            for (int i = 0; i < weapons.loadout.Length; i++)
            {
                weapons.Equip(i);
                yield return null;
                Check("Weapons_Equip_" + i, weapons.Index == i && weapons.Current == weapons.loadout[i],
                    "name=" + (weapons.Current != null ? weapons.Current.displayName : "null"));
            }

            // Every weapon must carry sane combo data or StartSwing indexes off the end.
            foreach (var w in weapons.loadout)
            {
                if (w == null) { Check("Weapons_NoNullEntries", false); continue; }
                Check("Weapons_ComboLengthValid_" + w.displayName,
                    w.comboLength > 0 && w.comboMultipliers != null && w.comboMultipliers.Length >= w.comboLength,
                    $"len={w.comboLength} mults={(w.comboMultipliers != null ? w.comboMultipliers.Length : 0)}");
                Check("Weapons_PositiveDamage_" + w.displayName, w.baseDamage > 0f);
            }

            // Parry window scales per weapon — the dev blade is the forgiving one.
            WeaponData widest = null;
            foreach (var w in weapons.loadout)
                if (w != null && (widest == null || w.parryWindowMultiplier > widest.parryWindowMultiplier)) widest = w;
            if (widest != null)
            {
                int idx = Array.IndexOf(weapons.loadout, widest);
                weapons.Equip(idx);
                yield return null;
                CheckApprox("Weapons_ParryWindowScalesWithWeapon",
                    parry.PerfectWindow, D.parryPerfectWindow * widest.parryWindowMultiplier, 0.001f);
                Check("Weapons_WidestIsMoreForgiving", widest.parryWindowMultiplier >= 1f,
                    widest.displayName + " mult=" + widest.parryWindowMultiplier.ToString("0.00"));
            }

            // Damage scaling from stats.
            weapons.Equip(0);
            var wep = weapons.Current;
            int str0 = stats.Strength;
            float m0 = stats.DamageMultiplier(wep);
            stats.Strength = str0 + 20;
            float m1 = stats.DamageMultiplier(wep);
            stats.Strength = str0;
            Check("Weapons_DamageScalesWithStats", wep.strScale <= 0f || m1 > m0,
                $"mult {m0:0.###} -> {m1:0.###} (strScale={wep.strScale})");

            // A real swing against a real enemy.
            EnemyController dummy = null;
            yield return SpawnDummy(combat.transform.position + combat.transform.forward * 2f, e => dummy = e);
            if (dummy != null)
            {
                FacePoint(dummy.transform.position);
                float ehp0 = dummy.Health.Current;
                float ep0 = dummy.Posture.Current;
                // DoHit is private and driven by the swing coroutine; reproduce the same public effect
                // to confirm damage + posture wiring, then note the input-gated part.
                var mi = typeof(WeaponController).GetMethod("DoHit", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mi != null)
                {
                    mi.Invoke(weapons, new object[] { weapons.Current, 0 });
                    yield return null;
                    Check("Weapons_SwingDamagesEnemy", dummy.Health.Current < ehp0,
                        $"{ehp0:0.0} -> {dummy.Health.Current:0.0}");
                    Check("Weapons_SwingBuildsEnemyPosture", dummy.Posture.Current > ep0,
                        $"{ep0:0.0} -> {dummy.Posture.Current:0.0}");
                }
                else Skip("Weapons_SwingDamagesEnemy", "DoHit not found by reflection");
                Destroy(dummy.gameObject);
            }
            else Skip("Weapons_SwingDamagesEnemy", "no enemy prefab");

            yield return SettleTimeScale();

            // Combo advance: pressing attack again mid-swing queues the next step, which starts as soon
            // as the current one ends. Driven through WeaponController.TryAttack — the method Update
            // calls on InputReader input. comboIndex is private; nothing public exposes the step.
            var comboField = typeof(WeaponController).GetField("comboIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            weapons.Equip(weapons.Index);   // resets comboIndex to -1 so the first step is deterministic
            yield return null;
            bool swingStarted = weapons.TryAttack() && weapons.IsAttacking;
            int firstStep = comboField != null ? (int)comboField.GetValue(weapons) : -1;
            bool pressQueued = weapons.TryAttack();
            yield return WaitUntilOrTimeout(
                () => comboField != null && (int)comboField.GetValue(weapons) == 1, 3f);
            int secondStep = comboField != null ? (int)comboField.GetValue(weapons) : -1;
            Check("Weapons_ComboAdvanceOnRepeatedInput",
                swingStarted && pressQueued && firstStep == 0 && secondStep == 1 && weapons.Current.comboLength > 1,
                $"step {firstStep} -> {secondStep} (comboLength={weapons.Current.comboLength})");
            weapons.CancelAttack();
            yield return SettleTimeScale();
        }

        // ================================================================ 6b. INSCRIPTION ALTAR

        /// <summary>
        /// The pre-run loadout choice. Proximity must NOT open the menu — the player aims at the altar
        /// and presses F. Once open, the menu freezes time through TimeScaleController, equipping closes
        /// it, and the time handle is released on EVERY exit path: a leaked 0-scale handle would freeze
        /// the game permanently.
        /// </summary>
        IEnumerator TestWandPedestal()
        {
            bool devMenuWas = WandPedestal.DevMenuEnabled;
            DeveloperAccess.UnlockForTests();
            var ts = TimeScaleController.I;
            var pedestal = FindAnyObjectByType<WandPedestal>();
            var menu = FindAnyObjectByType<WandSelectMenu>();

            Check("WandPedestal_InLevel", pedestal != null, "built by LevelGreyboxBuilder / SandboxBuilder");
            if (menu == null)
            {
                Check("WandPedestal_MenuInHud", false, "no WandSelectMenu on the HUD prefab");
                DeveloperAccess.LockForTests();
                yield break;
            }
            Check("WandPedestal_MenuInHud", true);
            Check("WandPedestal_MenuStartsClosed", !menu.IsOpen);
            Check("WandPedestal_MenuHasRows", menu.rows != null && menu.rows.Length > 0,
                "rows=" + (menu.rows != null ? menu.rows.Length : 0));

            // ---- the altar is a DEV FIXTURE: off by default, hidden and inert ----------------------
            // The test menu's "INSCRIPTION ALTAR" button is the only switch. Driven through the same
            // TestMenu body the button calls, then the flag directly, so both the wiring and the gate
            // are proven. The flag is restored at the end of this test whatever it was before.
            var testMenu = FindAnyObjectByType<TestMenu>();
            if (testMenu == null) Skip("WandPedestal_TestMenuToggleFlipsFlag", "no TestMenu on the HUD prefab");
            else
            {
                Check("WandPedestal_TestMenuButtonWired", testMenu.wandPedestalButton != null,
                    "HudBuilder.BuildTestMenu must build wandPedestalButton");
                bool flagBefore = WandPedestal.DevMenuEnabled;
                if (testMenu.wandPedestalButton != null) testMenu.wandPedestalButton.onClick.Invoke();
                else testMenu.ToggleWandPedestal();
                bool flipped = WandPedestal.DevMenuEnabled != flagBefore;
                string label = testMenu.wandPedestalButton != null
                    ? testMenu.wandPedestalButton.GetComponentInChildren<TMPro.TMP_Text>().text : "";
                Check("WandPedestal_TestMenuToggleFlipsFlag", flipped, flagBefore + " -> " + WandPedestal.DevMenuEnabled);
                Check("WandPedestal_TestMenuLabelTracksFlag",
                    label == (WandPedestal.DevMenuEnabled ? "INSCRIPTION ALTAR: ON" : "INSCRIPTION ALTAR: OFF"), "label='" + label + "'");
                testMenu.ToggleWandPedestal();
                Check("WandPedestal_TestMenuToggleFlipsBack", WandPedestal.DevMenuEnabled == flagBefore);
            }

            if (pedestal == null) Skip("WandPedestal_HiddenWhenDevMenuOff", "no pedestal in this scene");
            else
            {
                WandPedestal.DevMenuEnabled = false;
                yield return null;   // the pedestal applies the flag on its next Update
                Check("WandPedestal_HiddenWhenDevMenuOff", pedestal.IsHidden, "hidden=" + pedestal.IsHidden);
                bool anyDrawn = false;
                foreach (var r in pedestal.GetComponentsInChildren<Renderer>(true)) if (r.enabled) anyDrawn = true;
                Check("WandPedestal_NoRenderersWhenDevMenuOff", !anyDrawn);
                Check("WandPedestal_TriggerOffWhenDevMenuOff", !pedestal.GetComponent<Collider>().enabled);

                string offPrompt = "";
                Action<string, string> onOffPrompt = (o, p) => offPrompt = p;
                GameEvents.PromptChanged += onOffPrompt;
                Vector3 standOff = pedestal.transform.position - Vector3.forward * 2f + Vector3.up * 1.2f;
                motor.Teleport(standOff, 0f);
                FacePoint(pedestal.transform.position + Vector3.up * 1.5f);
                yield return null;
                yield return null;
                Check("WandPedestal_NoRangeWhenDevMenuOff", !pedestal.PlayerInRange, "inRange=" + pedestal.PlayerInRange);
                Check("WandPedestal_NoPromptWhenDevMenuOff", offPrompt.Length == 0 && !WandPedestal.PromptActive, "prompt='" + offPrompt + "'");
                Check("WandPedestal_InteractRefusedWhenDevMenuOff", !pedestal.TryInteract() && !menu.IsOpen);
                pedestal.Open(wandCtl);
                yield return null;
                Check("WandPedestal_OpenRefusedWhenDevMenuOff", !menu.IsOpen);
                GameEvents.PromptChanged -= onOffPrompt;
            }

            // Everything below exercises the altar as it behaves ONCE ENABLED.
            WandPedestal.DevMenuEnabled = true;
            yield return null;
            if (pedestal != null)
            {
                Check("WandPedestal_ShownWhenDevMenuOn", !pedestal.IsHidden && pedestal.GetComponent<Collider>().enabled);
                // Teleport again: the trigger came back this frame and OnTriggerEnter needs a fresh overlap.
                motor.Teleport(pedestal.transform.position - Vector3.forward * 6f + Vector3.up * 1.2f, 0f);
                yield return null;
            }

            // ---- walking into range must not open anything, but must offer the prompt ----------
            if (pedestal == null) Skip("WandPedestal_ProximityDoesNotOpen", "no pedestal in this scene");
            else
            {
                string lastPrompt = "";
                Action<string, string> onPrompt = (o, p) => lastPrompt = p;
                GameEvents.PromptChanged += onPrompt;

                Vector3 stand = pedestal.transform.position - Vector3.forward * 2f + Vector3.up * 1.2f;
                motor.Teleport(stand, 0f);
                FacePoint(pedestal.transform.position + Vector3.up * 1.5f);
                yield return WaitUntilOrTimeout(() => pedestal.PlayerInRange, 2f);

                if (waitTimedOut)
                {
                    Skip("WandPedestal_ProximityDoesNotOpen", "player never registered inside the pedestal trigger");
                    Skip("WandPedestal_PromptWhileLookingAt", "player never registered inside the pedestal trigger");
                }
                else
                {
                    Check("WandPedestal_PlayerInRange", pedestal.PlayerInRange);
                    Check("WandPedestal_ProximityDoesNotOpen", !menu.IsOpen,
                        "range alone must never open the menu — F does");
                    Check("WandPedestal_LooksAtWhenAimed", pedestal.IsLookedAt());
                    yield return null;
                    Check("WandPedestal_PromptWhileLookingAt", lastPrompt.Length > 0, "prompt='" + lastPrompt + "'");

                    // Look away: the prompt must clear and the menu must still be shut.
                    FaceAwayFrom(pedestal.transform.position);
                    yield return null;
                    yield return null;
                    Check("WandPedestal_NotLookedAtWhenTurnedAway", !pedestal.IsLookedAt());
                    Check("WandPedestal_PromptClearsWhenLookingAway", lastPrompt.Length == 0, "prompt='" + lastPrompt + "'");
                    Check("WandPedestal_StillClosedAfterLookingAway", !menu.IsOpen);
                }

                GameEvents.PromptChanged -= onPrompt;

                // The press itself, through the same TryInteract() body Update calls. It must REFUSE
                // while looking away (we are still turned away from the previous check) and succeed
                // once aimed at the altar — proving the gating, not just that Open() works.
                Check("WandPedestal_InteractRefusedWhenLookingAway", !pedestal.TryInteract() && !menu.IsOpen);
                FacePoint(pedestal.transform.position + Vector3.up * 1.5f);
                yield return null;
                bool opened = pedestal.TryInteract();
                yield return null;
                Check("WandPedestal_FOpensMenu", opened && menu.IsOpen, "TryInteract opened=" + opened);
                menu.Close();
                yield return null;
            }

            if (wandCtl == null || wandCtl.loadout == null || wandCtl.loadout.Length < 2)
            {
                Skip("WandPedestal_EquipsSelectedWand", "player has fewer than 2 wands in the loadout");
                WandPedestal.DevMenuEnabled = devMenuWas;
                DeveloperAccess.LockForTests();
                yield break;
            }

            // ---- the interaction the F press routes into ---------------------------------------
            yield return SettleTimeScale();
            if (pedestal != null) pedestal.Open(wandCtl); else menu.Open(wandCtl);
            yield return null;

            Check("WandPedestal_OpensMenu", menu.IsOpen);
            Check("WandPedestal_PausesGameState", GameManager.I.State == GameState.Paused,
                "state=" + GameManager.I.State);
            Check("WandPedestal_TakesTimeHandle", menu.TimeHandle >= 0, "handle=" + menu.TimeHandle);
            if (ts != null) CheckApprox("WandPedestal_TimeStopsWhileOpen", ts.WorldScale, 0f, 0.001f);

            // Equip the NEXT wand: proves the row index reaches WandController.Equip.
            int before = wandCtl.Index;
            int target = (before + 1) % wandCtl.loadout.Length;
            menu.Select(target);
            yield return null;

            Check("WandPedestal_EquipsSelectedWand", wandCtl.Index == target, before + " -> " + wandCtl.Index);
            Check("WandPedestal_ClosesOnSelect", !menu.IsOpen);
            Check("WandPedestal_ReleasesTimeHandleOnSelect", menu.TimeHandle < 0, "handle=" + menu.TimeHandle);
            Check("WandPedestal_RestoresPlayingState", GameManager.I.State == GameState.Playing,
                "state=" + GameManager.I.State);
            yield return SettleTimeScale();
            if (ts != null) CheckApprox("WandPedestal_TimeResumesAfterSelect", ts.WorldScale, 1f, 0.001f);

            // Closing without choosing must also release the handle and keep the current wand.
            menu.Open(wandCtl);
            yield return null;
            Check("WandPedestal_ReopensAfterClose", menu.IsOpen);
            int kept = wandCtl.Index;
            menu.Close();
            yield return null;
            Check("WandPedestal_CloseKeepsCurrentWand", wandCtl.Index == kept, "index=" + wandCtl.Index);
            Check("WandPedestal_ReleasesTimeHandleOnClose", menu.TimeHandle < 0, "handle=" + menu.TimeHandle);
            yield return SettleTimeScale();
            if (ts != null) CheckApprox("WandPedestal_TimeResumesAfterClose", ts.WorldScale, 1f, 0.001f);

            // Idempotent close: a second Close must not release a handle it no longer owns.
            menu.Close();
            yield return null;
            Check("WandPedestal_DoubleCloseIsSafe", !menu.IsOpen && menu.TimeHandle < 0);

            // R cycling stays as a debug convenience alongside the pedestal (deliberate decision),
            // so it has to keep working after the pedestal has equipped something.
            var wandsForCycle = combat.GetComponent<WandController>();
            int wandCount = wandsForCycle != null && wandsForCycle.loadout != null ? wandsForCycle.loadout.Length : 0;
            if (wandCount < 2)
                Skip("WandPedestal_RCyclingStillWorks", "fewer than two wands in the loadout to cycle between");
            else
            {
                int idxBefore = wandsForCycle.Index;
                bool cycled = wandsForCycle.TryCycle();
                yield return null;
                Check("WandPedestal_RCyclingStillWorks",
                    cycled && wandsForCycle.Index != idxBefore,
                    "index " + idxBefore + " -> " + wandsForCycle.Index + " of " + wandCount);
            }

            // Leave the altar the way we found it (off, unless the user had switched it on).
            WandPedestal.DevMenuEnabled = devMenuWas;
            DeveloperAccess.LockForTests();
            yield return null;
        }

        // ================================================================ 6c. WAND READABILITY

        /// <summary>
        /// The riposte's PRESENTATION contract, guarding CLAUDE.md rule 9: every value here lives on a
        /// ScriptableObject or a prefab, so editing the field initialiser alone changes nothing that
        /// ships. The riposte was once invisible in first person for exactly this class of reason — a
        /// 0.5 viewmodel scale and a 1.25m stab standoff that put the camera inside the victim.
        ///
        /// This proves the numbers reached the assets. It cannot prove the riposte looks good; only a
        /// screenshot does that.
        /// </summary>
        IEnumerator TestWandReadability()
        {
            if (wandCtl == null || wandCtl.loadout == null || wandCtl.loadout.Length == 0)
            {
                Skip("WandRead_ScaleShipped", "no wand loadout on the player");
                yield break;
            }

            // Rule 9 again: cooldown is a new field, so every existing wand asset deserialised it as 0
            // until WandFactory was re-run. A 0 here is not "no cooldown by design", it is "the tuning
            // never shipped" — and the heavier wands must genuinely wait longer or the cooldown is not
            // a trade-off, just a delay.
            float minCd = float.MaxValue, maxCd = 0f;
            string cdDetail = "";
            foreach (var w in wandCtl.loadout)
            {
                if (w == null) continue;
                cdDetail += w.displayName + "=" + w.cooldown.ToString("0.0") + "s ";
                if (w.cooldown < minCd) minCd = w.cooldown;
                if (w.cooldown > maxCd) maxCd = w.cooldown;
            }
            Check("Wand_CooldownShipped", minCd >= 1f, cdDetail + "(WandFactory must write cooldown)");
            Check("Wand_CooldownsDiffer", maxCd >= minCd * 1.5f,
                cdDetail + "(a heavy wand must wait meaningfully longer than a light one)");

            if (exec == null) Skip("WandRead_StandoffFramesVictim", "no ExecuteInteractor");
            else
                Check("WandRead_StandoffFramesVictim", exec.stabStandoff >= 2f,
                    "stabStandoff=" + exec.stabStandoff + " (under 2m the camera ends up inside the victim)");

            var offhand = combat != null ? combat.GetComponentInChildren<OffhandViewmodel>(true) : null;
            if (offhand == null) { Skip("WandRead_ThrustPoseAimsForward", "no OffhandViewmodel"); yield break; }

            Check("Spellbook_PrefabShipped", offhand.spellbookPrefab != null,
                "SpellbookFactory must run before PrefabFactory.BuildPlayer");
            var book = offhand.DisplayedInstance != null
                ? offhand.DisplayedInstance.GetComponent<SpellbookVisual>() : null;
            Check("Spellbook_PersistentOpenModel", book != null && book.bookRoot != null && book.castOrigin != null,
                "displayed=" + (offhand.DisplayedInstance != null ? offhand.DisplayedInstance.name : "null"));
            Check("Spellbook_PagesFlow", book != null && book.pagePivots != null && book.pagePivots.Length >= 8
                && book.floatingPagePivots != null && book.floatingPagePivots.Length >= 3);
            Check("WandRead_TipLightEnabled", offhand.tipLightEnabled && offhand.tipLightRange >= 6f,
                "enabled=" + offhand.tipLightEnabled + " range=" + offhand.tipLightRange);

            var light = offhand.GetComponentInChildren<Light>(true);
            Check("WandRead_TipLightBuilt", light != null, "OffhandViewmodel.Awake builds a TipLight child");
            yield return null;

            yield return TestWeaponEmber();
            yield return TestWeaponTrail();
        }

        /// <summary>
        /// The weapon fire. Like WandReadability this is a PRESENTATION contract and it cannot prove the
        /// fire looks good — only a screenshot does that. What it can prove is that the fire is wired to
        /// the meter at all, that it goes out when the meter is empty, and that the intensities stayed
        /// inside the caps that keep it from eating the frame (the failure this project keeps shipping).
        /// </summary>
        IEnumerator TestWeaponEmber()
        {
            var ember = combat != null ? combat.GetComponent<WeaponEmber>() : null;
            if (ember == null)
            {
                Check("Ember_AttachedToPlayer", false, "PlayerCombat.Awake must AddComponent<WeaponEmber>()");
                yield break;
            }
            Check("Ember_AttachedToPlayer", true);

            // Readability caps. EnergyGlow at charge 1.0 adds ~0.85 emission plus a 1.2 tip boost, which
            // blows out under this project's bloom; the ember must stay well below that.
            Check("Ember_IntensitiesCapped",
                ember.glowChargeAtFull <= 0.7f && ember.lightIntensityAtFull <= 2.5f && ember.lightRangeAtFull <= 4f,
                $"glowCharge={ember.glowChargeAtFull:0.00} lightIntensity={ember.lightIntensityAtFull:0.00} " +
                $"lightRange={ember.lightRangeAtFull:0.0}");

            // Empty meter: the weapon must read as UNLIT, not as faintly warm.
            res.ConsumePyre();
            yield return WaitRealtime(0.6f);
            bool coldWhenEmpty = !ember.IsBurning && ember.Charge <= ember.deadzone + 0.01f;
            Check("Ember_ColdAtZeroPyre", coldWhenEmpty, "charge=" + ember.Charge.ToString("0.000"));

            // Partial meter: the fire must sit partway, not snap to full.
            res.AddPyre(res.MaxPyre * 0.5f);
            yield return WaitRealtime(0.6f);
            float mid = ember.Charge;
            Check("Ember_TracksPyrePartially", mid > 0.3f && mid < 0.75f, "charge=" + mid.ToString("0.000"));

            // Full meter: alight.
            res.AddPyre(res.MaxPyre);
            yield return WaitRealtime(0.6f);
            Check("Ember_BlazesAtFullPyre", ember.IsBurning && ember.Charge > 0.9f && ember.Charge > mid,
                $"charge={ember.Charge:0.000} (was {mid:0.000})");

            res.ConsumePyre();
            yield return null;
        }

        /// <summary>
        /// The swing trail. Like the fire this is a PRESENTATION contract — only a frame sequence can
        /// say whether the arc reads — but three things ARE testable and all three have failed before:
        /// that it shipped on the prefab at all (rule 9), that its brightness stayed inside the band
        /// that does not out-shout the combat markers, and that it is open for the STRIKE ONLY. The
        /// last is the important one: the trail is the player's read on the active window, so a trail
        /// that runs through the wind-up is a lie about when the weapon is dangerous.
        /// </summary>
        IEnumerator TestWeaponTrail()
        {
            var vm = combat != null ? combat.GetComponentInChildren<WeaponViewmodel>(true) : null;
            var trail = vm != null ? vm.GetComponent<WeaponTrail>() : null;
            if (trail == null)
            {
                Check("Trail_ShippedOnViewmodel", false,
                    "PrefabFactory must AddComponent<WeaponTrail>() on ViewmodelRoot");
                yield break;
            }
            Check("Trail_ShippedOnViewmodel", true);

            // Readability band. The alert tell is 3.00 and the deathblow mark 2.60 because they are
            // alarms; this is flourish and must stay well under both, and under the ~1.25 where ACES
            // desaturates a saturated hue toward orange.
            Check("Trail_BrightnessCapped", trail.brightness <= 1.25f && trail.brightness >= 0.8f,
                "brightness=" + trail.brightness.ToString("0.00"));
            Check("Trail_WidthCapped", trail.headWidth <= 0.05f && trail.headWidth > 0f,
                "headWidth=" + trail.headWidth.ToString("0.000") + "m at ~0.5m from the lens");
            Check("Trail_FadeIsShort", trail.fadeSeconds > 0f && trail.fadeSeconds <= 0.2f,
                "fade=" + trail.fadeSeconds.ToString("0.00") + "s (a trail that outlives the recovery " +
                "stops meaning 'the hitbox is live')");

            // STRETCHED so the phases can be sampled: the shipped strike leg is 0.044-0.14 s, which is
            // a couple of frames. The pose path is a normalised lerp, so a stretched attack walks the
            // identical phases - it just gives the sampler somewhere to stand.
            yield return SettleTimeScale();
            trail.Clear();
            vm.PlayAttack(0, 1.2f, 0.5f);          // windup 0.50s, strike 0.24s, then hold + recovery
            yield return WaitRealtime(0.25f);
            bool quietInWindup = !trail.IsEmitting;
            // Observe the whole expected strike band instead of one instant. A busy editor can deliver
            // a single long frame across t=0.65, making a correct coroutine look as if it never opened.
            float observeUntil = Time.unscaledTime + 0.55f;
            bool liveInStrike = false;
            while (Time.unscaledTime < observeUntil)
            {
                if (trail.IsEmitting) liveInStrike = true;
                yield return null;
            }
            yield return WaitRealtime(0.25f);      // t >= 1.05s: past the strike, into follow-through
            bool quietAfter = !trail.IsEmitting;

            Check("Trail_SilentDuringWindup", quietInWindup, "the wind-up is not dangerous yet");
            Check("Trail_LiveDuringStrike", liveInStrike, "the ribbon must span the active window");
            Check("Trail_ClosesAfterStrike", quietAfter, "and must close with it");

            var wc = combat != null ? combat.GetComponent<WeaponController>() : null;
            if (wc != null && wc.Current != null)
            {
                Color want = SlashFx.NormaliseColor(wc.Current.neon);
                Color got = trail.Hue;
                float d = Mathf.Abs(want.r - got.r) + Mathf.Abs(want.g - got.g) + Mathf.Abs(want.b - got.b);
                Check("Trail_CarriesWeaponHue", d < 0.05f,
                    "want=" + want.ToString("F2") + " got=" + got.ToString("F2") +
                    " (each weapon's neon is its identity; a white trail makes all four the same weapon)");
            }
            else Skip("Trail_CarriesWeaponHue", "no equipped weapon");

            vm.Interrupt();
            yield return null;
        }

        // ================================================================ 6b. VIEWMODEL ARMS

        /// <summary>
        /// The shipped arm rig. Everything here lives serialized on Player.prefab, so a code default is
        /// not a shipped value (rule 9) — these assertions are what catch a stale prefab.
        /// </summary>
        IEnumerator TestViewmodelArms()
        {
            var vm = combat != null ? combat.GetComponentInChildren<WeaponViewmodel>(true) : null;
            if (vm == null) { Skip("Arms_Exist", "no WeaponViewmodel"); yield break; }

            Check("Arms_HandShipped", vm.hand != null && vm.grip != null,
                "hand=" + (vm.hand != null) + " grip=" + (vm.grip != null));
            Check("Arms_SolverShipped", vm.arm != null && vm.arm.wrist != null &&
                                        vm.arm.upperArm != null && vm.arm.forearm != null,
                "ViewmodelArm with both bones and a wrist target");
            if (vm.hand == null || vm.grip == null || vm.arm == null) yield break;

            Check("Arms_HandHasGeometry", vm.hand.GetComponentsInChildren<Renderer>(true).Length >= 6,
                "boxes=" + vm.hand.GetComponentsInChildren<Renderer>(true).Length);

            bool casts = false;
            foreach (var r in vm.hand.GetComponentsInChildren<Renderer>(true))
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casts = true;
            Check("Arms_NoShadowCasting", !casts, "viewmodel geometry sits inside the player");

            // THE POINT OF THE RIG: the weapon hangs off the hand, not beside it.
            Check("Arms_WeaponParentedToGrip", vm.grip.childCount > 0,
                "grip children=" + vm.grip.childCount + " (SetWeapon must instantiate into grip)");
            // ...and the hand slid onto the hilt without moving the weapon: the two offsets cancel.
            Vector3 cancel = vm.hand.localPosition + vm.grip.localPosition;
            Check("Arms_GripOffsetCancels", cancel.magnitude < 0.001f,
                "hand+grip=" + cancel.ToString("F4") + " (a non-zero sum has moved the weapon on screen)");

            // The shoulder must stay off the camera axis or the near-plane cut lands on screen.
            Check("Arms_ShoulderOffAxis", Mathf.Abs(vm.arm.shoulderLocal.x) >= 0.15f && vm.arm.shoulderLocal.y <= -0.25f,
                "shoulderLocal=" + vm.arm.shoulderLocal.ToString("F2"));
            Check("Arms_BonesStretchNotClamp", vm.arm.maxStretch >= 1.2f && vm.arm.upperLength > 0.2f && vm.arm.foreLength > 0.2f,
                "upper=" + vm.arm.upperLength + " fore=" + vm.arm.foreLength + " maxStretch=" + vm.arm.maxStretch);

            // ViewmodelArm solves after viewmodel posing in LateUpdate. Sampling from this coroutine's
            // Update phase sees a hand written this frame against bones from the previous frame, a gap
            // that cannot appear in the rendered image.
            yield return new WaitForEndOfFrame();
            Check("Arms_Solved", vm.arm.Solved, "ViewmodelArm.LateUpdate must run every frame");

            // The forearm has to END at the wrist. Anything else is a floating hand.
            Vector3 tip = vm.arm.forearm.position + vm.arm.forearm.forward * (vm.arm.forearm.localScale.z * 0.5f);
            Check("Arms_ForearmReachesWrist", Vector3.Distance(tip, vm.arm.wrist.position) < 0.02f,
                "gap=" + Vector3.Distance(tip, vm.arm.wrist.position).ToString("F4"));

            // And the arm has to FOLLOW the pose: swing, then re-check both.
            Vector3 before = vm.hand.position;
            vm.PlayAttack(0, 0.4f, 0.12f);
            yield return WaitRealtime(0.12f);
            Check("Arms_HandFollowsPose", Vector3.Distance(before, vm.hand.position) > 0.03f,
                "hand moved " + Vector3.Distance(before, vm.hand.position).ToString("F3") + "m into the windup");
            // MEASURE AT END OF FRAME. This coroutine runs in Update, and so does the attack coroutine
            // that writes the pose — but the arm is solved in LateUpdate. Sampling here reads a hand that
            // has already moved this frame against an arm that has not been solved yet, and reports a
            // 3-4 cm "gap" that never exists on screen, because rendering happens after LateUpdate.
            yield return new WaitForEndOfFrame();
            tip = vm.arm.forearm.position + vm.arm.forearm.forward * (vm.arm.forearm.localScale.z * 0.5f);
            Check("Arms_ArmTracksMidSwing", Vector3.Distance(tip, vm.arm.wrist.position) < 0.02f,
                "gap=" + Vector3.Distance(tip, vm.arm.wrist.position).ToString("F4"));
            vm.Interrupt();

            var offhand = combat.GetComponentInChildren<OffhandViewmodel>(true);
            if (offhand == null) { Skip("Arms_OffhandRig", "no OffhandViewmodel"); yield break; }
            Check("Arms_OffhandRig", offhand.hand != null && offhand.grip != null && offhand.arm != null,
                "hand=" + (offhand.hand != null) + " grip=" + (offhand.grip != null) + " arm=" + (offhand.arm != null));
            if (offhand.arm != null)
                // The left arm rig must NOT hang off the posed offhand root, or the shoulder travels with
                // the hand and the arm can never bend.
                Check("Arms_OffhandShoulderIsCameraFixed",
                    !offhand.arm.transform.IsChildOf(offhand.transform),
                    "OffhandArmRig parent=" + (offhand.arm.transform.parent != null ? offhand.arm.transform.parent.name : "none"));
            yield return null;
        }

        // ================================================================ 6c. TELL READABILITY

        // ---------------------------------------------------------------------------------------
        // WIND-UP SILHOUETTES (gap 3.5). An attack's anticipation has to be a distinct SHAPE, not a
        // distinct set of numbers in an asset.
        //
        // WHY THIS TEST MEASURES INSTEAD OF ASSERTING THE AUTHORED EULERS. A WindupPose authors a
        // SHOULDER angle, but the hand pivot trails it by weaponLag and the whole body is rotated and
        // offset underneath both, so the blade's actual orientation is the product of three rotations.
        // Every one of the eleven poses shipped before this test was authored with a comment describing
        // a shape it did not make: an "overhead mast" that resolved to a short horizontal stub, a
        // "pure horizontal sweep" and a "pure vertical overhead" that both resolved to the same steep
        // diagonal. Asserting the authored numbers would have passed on all of them. So this
        // instantiates the real prefab, drives the real rig through the real pose maths, and measures
        // what the blade DOES.
        //
        // The four channels are the ones a silhouette is actually read by:
        //   tilt  - the blade's angle in the plane the player sees (0 = level bar, +-90 = upright mast)
        //   proj  - how much of the blade survives foreshortening (1 = full length across the view,
        //           0 = pointed straight down the camera and invisible)
        //   tipX/tipY - where the high end of the blade sits, in body-heights from the body's centre
        // Two poses are DISTINCT when any one channel separates them by more than a threshold that was
        // set from photographs, not from taste.
        // ---------------------------------------------------------------------------------------

        struct BladeShape
        {
            public bool valid;
            public float tilt;      // degrees, wrapped to (-90, 90]; 0 = level, +-90 = upright
            public float proj;      // 0-1, length surviving foreshortening
            public float tipX;      // body-heights right of the body centre
            public float tipY;      // body-heights above the body centre
        }

        const float TiltSep = 35f;    // degrees of blade angle that read as "a different shape"
        const float ProjSep = 0.35f;  // foreshortening difference (a thrust vs a swing)
        const float TipXSep = 0.55f;  // body-heights of horizontal displacement
        const float TipYSep = 0.50f;  // body-heights of height difference

        /// <summary>
        /// Drive a real instance of <paramref name="prefab"/> into one attack's wind-up peak (the pose
        /// CueFlash freezes on: <c>armWindup * 1.12</c>) and measure the blade's silhouette in the
        /// enemy's own view plane. The instance is created INACTIVE so no Awake, no AI and no NavMesh
        /// spawn runs; the transforms are still live and still at rest, which is exactly what is needed.
        /// </summary>
        static BladeShape MeasureWindup(GameObject prefab, EnemyAttackData atk)
        {
            var s = new BladeShape();
            if (prefab == null || atk == null) return s;
            var go = Instantiate(prefab);
            go.SetActive(false);
            try
            {
                var vis = go.GetComponentInChildren<EnemyVisuals>(true);
                if (vis == null || vis.armPivot == null || vis.weapon == null) return s;
                Transform root = go.transform, arm = vis.armPivot, blade = vis.weapon.transform;
                Transform lunge = vis.lungeRoot, hand = vis.weaponPivot;
                var p = atk.windupPose;
                if (p == null) return s;

                Vector3 w = p.armWindup * 1.12f;                    // the held cue peak
                float lag = hand != null ? Mathf.Clamp01(p.weaponLag) : 0f;
                if (lunge != null)
                {
                    lunge.localPosition += p.bodyOffset;
                    lunge.localRotation *= Quaternion.Euler(p.bodyEuler);
                }
                arm.localRotation *= Quaternion.Euler(w);
                if (hand != null) hand.localRotation *= Quaternion.Euler(w * lag);

                Vector3 sc = blade.lossyScale;
                Vector3 axis = (sc.y >= sc.x && sc.y >= sc.z) ? Vector3.up
                             : ((sc.z >= sc.x) ? Vector3.forward : Vector3.right);
                Quaternion inv = Quaternion.Inverse(root.rotation);
                Vector3 dir = inv * (blade.rotation * axis);
                Vector3 centre = inv * (blade.position - root.position);
                float half = 0.5f * Vector3.Scale(axis, sc).magnitude;
                Vector3 t1 = centre + dir * half, t2 = centre - dir * half;
                Vector3 tip = t1.y >= t2.y ? t1 : t2;

                var cc = go.GetComponent<CapsuleCollider>();
                float bodyH = cc != null && cc.height > 0.1f ? cc.height : 2f;

                float tilt = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                if (tilt > 90f) tilt -= 180f;
                if (tilt < -90f) tilt += 180f;

                s.valid = true;
                s.tilt = tilt;
                s.proj = new Vector2(dir.x, dir.y).magnitude;
                s.tipX = tip.x / bodyH;
                s.tipY = tip.y / bodyH;
                return s;
            }
            finally { DestroyImmediate(go); }
        }

        /// <summary>Which channel separates two silhouettes, and by how many thresholds. >= 1 is distinct.</summary>
        static float Separation(BladeShape a, BladeShape b, out string channel)
        {
            float dTilt = Mathf.Abs(Mathf.DeltaAngle(a.tilt * 2f, b.tilt * 2f)) * 0.5f;
            float dProj = Mathf.Abs(a.proj - b.proj);
            float dX = Mathf.Abs(a.tipX - b.tipX);
            float dY = Mathf.Abs(a.tipY - b.tipY);
            float sTilt = dTilt / TiltSep, sProj = dProj / ProjSep, sX = dX / TipXSep, sY = dY / TipYSep;
            channel = "tilt"; float best = sTilt;
            if (sProj > best) { best = sProj; channel = "foreshortening"; }
            if (sX > best) { best = sX; channel = "side"; }
            if (sY > best) { best = sY; channel = "height"; }
            channel += $" (dTilt={dTilt:0}deg dProj={dProj:0.00} dX={dX:0.00} dY={dY:0.00})";
            return best;
        }

        void CheckDistinct(string tag, string an, BladeShape a, string bn, BladeShape b)
        {
            if (!a.valid || !b.valid) { Skip(tag, "could not measure " + an + " / " + bn); return; }
            string channel;
            float sep = Separation(a, b, out channel);
            Check(tag, sep >= 1f,
                $"{an} vs {bn}: separated {sep:0.00}x on {channel}. " +
                $"{an}=(tilt {a.tilt:0}, proj {a.proj:0.00}, tip {a.tipX:0.00}/{a.tipY:0.00}) " +
                $"{bn}=(tilt {b.tilt:0}, proj {b.proj:0.00}, tip {b.tipX:0.00}/{b.tipY:0.00})");
        }

        IEnumerator TestWindupPoses()
        {
            // The prefabs the level actually spawns, plus the boss, so this tests what ships.
            // BY NAME, not by scale. The first pass picked the prefabs by EnemyData.scale and got the
            // Ashen Chorister (scale 1.5) instead of Enemy_Heavy, then reported its five DELIBERATELY
            // unauthored attacks as failures. The legendaries are content: they ride the cone-derived
            // fallback on purpose, and the last check in this test is what proves that still works.
            GameObject gruntPf = null, heavyPf = null, bossPf = null;
            foreach (var sp in FindObjectsByType<EnemySpawner>())
            {
                if (sp.prefab == null) continue;
                if (sp.prefab.GetComponentInChildren<BossController>(true) != null) { bossPf = sp.prefab; continue; }
                if (sp.prefab.name == "Enemy_Grunt") gruntPf = sp.prefab;
                else if (sp.prefab.name == "Enemy_Heavy") heavyPf = sp.prefab;
            }
            if (LevelEditor.I != null)
            {
                if (gruntPf == null) gruntPf = LevelEditor.I.PrefabFor("Enemy_Grunt");
                if (heavyPf == null) heavyPf = LevelEditor.I.PrefabFor("Enemy_Heavy");
            }
            Check("Windup_CoreMovesetsFound", gruntPf != null && heavyPf != null && bossPf != null,
                $"grunt={(gruntPf != null ? gruntPf.name : "null")} heavy={(heavyPf != null ? heavyPf.name : "null")} " +
                $"boss={(bossPf != null ? bossPf.name : "null")}");
            var liveBoss = FindAnyObjectByType<BossController>();
            if (bossPf == null && liveBoss != null) bossPf = liveBoss.gameObject;

            // ---- every authored pose is actually authored, and its strike RESOLVES the wind-up -----
            var shapes = new Dictionary<string, BladeShape>();
            Action<GameObject, EnemyData> sweep = (pf, d) =>
            {
                if (pf == null || d == null) return;
                var atks = new List<EnemyAttackData>();
                var combos = d.ResolveCombos();
                if (combos != null)
                    foreach (var c in combos)
                    { if (c == null || c.hits == null) continue; foreach (var h in c.hits) if (h != null && !atks.Contains(h)) atks.Add(h); }
                var bd = d as BossData;
                if (bd != null && bd.phases != null)
                    foreach (var ph in bd.phases)
                    { if (ph == null || ph.patterns == null) continue;
                      foreach (var c in ph.patterns) { if (c == null || c.hits == null) continue;
                        foreach (var h in c.hits) if (h != null && !atks.Contains(h)) atks.Add(h); } }
                foreach (var a in atks)
                {
                    var p = a.windupPose;
                    // Only the three CORE movesets are swept, and every one of their attacks is
                    // authored. An attack outside them is allowed to have no pose at all — see the
                    // fallback check at the end of this test.
                    Check("Windup_" + a.name + "_Authored", p != null && p.authored,
                        "a core-moveset attack must carry its own silhouette");
                    if (p == null || !p.authored) continue;
                    // The strike has to RESOLVE the wind-up. Identical (or near-identical) angles mean
                    // the swing does not visibly travel, and the two beats read as one held pose.
                    float travel = Quaternion.Angle(Quaternion.Euler(p.armWindup), Quaternion.Euler(p.armStrike));
                    Check("Windup_" + a.name + "_StrikeResolves", travel >= 40f,
                        $"windup->strike travels {travel:0}deg");
                    if (!shapes.ContainsKey(a.name)) shapes[a.name] = MeasureWindup(pf, a);
                }
            };
            sweep(gruntPf, DataOf(gruntPf));
            sweep(heavyPf, DataOf(heavyPf));
            sweep(bossPf, bossPf != null ? DataOf(bossPf) : null);

            Func<string, BladeShape> S = n => shapes.ContainsKey(n) ? shapes[n] : new BladeShape();

            // ---- the pairs whose confusion costs the player -----------------------------------
            // 1. The Grunt's opener against its rhythm-breaker. Mistaking the 0.72 s heavy for the
            //    0.45 s jab throws the parry early and gives up the best deflect value in the moveset.
            CheckDistinct("Windup_GruntJabVsHeavy", "Grunt_Jab", S("Grunt_Jab"), "Grunt_Heavy", S("Grunt_Heavy"));

            // 2. Vertical answer vs horizontal answer. These two shipped as the SAME steep diagonal.
            CheckDistinct("Windup_HeavyOverheadVsSweep", "Heavy_Overhead", S("Heavy_Overhead"),
                          "Heavy_Sweep", S("Heavy_Sweep"));

            // 3. THE UNBLOCKABLE. Boss_Thrust's answer is to MOVE, not to parry, and getting it wrong
            //    costs 55. It gets a second, independent read alongside the pink M_AlertTell marker and
            //    the red cue tint - this asserts the POSE half of that, against every other boss attack.
            string[] others = { "Boss_Slash", "Boss_DoubleSlash_A", "Boss_DoubleSlash_B", "Boss_Slam" };
            foreach (var o in others)
                CheckDistinct("Windup_ThrustVs" + o.Replace("Boss_", ""), "Boss_Thrust", S("Boss_Thrust"), o, S(o));

            // 4. The boss's two openers must not rhyme with each other either.
            CheckDistinct("Windup_BossSlashVsSlam", "Boss_Slash", S("Boss_Slash"), "Boss_Slam", S("Boss_Slam"));
            CheckDistinct("Windup_BossSlashVsDoubleA", "Boss_Slash", S("Boss_Slash"),
                          "Boss_DoubleSlash_A", S("Boss_DoubleSlash_A"));

            // ---- AN UNAUTHORED ATTACK STILL GETS A POSE ---------------------------------------
            // 25+ attack assets exist and most are content that never needed its own silhouette, so the
            // cone-derived fallback has to keep working. This drives it through the real Telegraph path
            // rather than reading a field, because the fallback lives inside EnemyVisuals.PoseFor.
            if (gruntPf == null) { Skip("Windup_UnauthoredFallsBack", "no grunt prefab"); yield break; }
            EnemyController host = null;
            foreach (var e in FindObjectsByType<EnemyController>())
                if (e.IsAlive && e.gameObject.activeInHierarchy) { host = e; break; }
            if (host == null) { Skip("Windup_UnauthoredFallsBack", "no live enemy to borrow a spot from"); yield break; }

            var dummy = Instantiate(gruntPf, host.transform.position, Quaternion.identity);
            dummy.name = "WindupFallbackDummy";
            yield return null;
            yield return null;                       // Start(): the palette and the rig bind here
            var dc = dummy.GetComponent<EnemyController>();
            if (dc != null) dc.enabled = false;
            var dagent = dummy.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (dagent != null) dagent.enabled = false;
            foreach (var col in dummy.GetComponentsInChildren<Collider>(true)) col.enabled = false;

            var dvis = dummy.GetComponentInChildren<EnemyVisuals>(true);
            if (dvis == null || dvis.armPivot == null) { Skip("Windup_UnauthoredFallsBack", "no arm rig"); Destroy(dummy); yield break; }

            var fake = ScriptableObject.CreateInstance<EnemyAttackData>();
            fake.attackName = "UnauthoredProbe";
            fake.windup = 0.5f;
            fake.coneDeg = 110f;                     // wide cone -> the cone-derived SWEEP fallback
            // fake.windupPose.authored stays FALSE. That is the whole point.
            Check("Windup_ProbeIsUnauthored", !fake.windupPose.authored);

            Quaternion rest = dvis.armPivot.localRotation;
            dvis.Telegraph(fake, 0.5f);
            float until = Time.unscaledTime + 0.6f;
            while (Time.unscaledTime < until) yield return null;
            float moved = Quaternion.Angle(rest, dvis.armPivot.localRotation);
            Check("Windup_UnauthoredFallsBack", moved >= 30f,
                $"an attack with no authored pose still reared {moved:0}deg — the cone-derived fallback is alive");

            dvis.ClearTelegraph();
            Destroy(dummy);
            DestroyImmediate(fake);
            yield return null;
        }

        /// <summary>
        /// The unblockable tell and the navigational trims must live on SEPARATE materials, because they
        /// want opposite intensities: a trim held under the ACES desaturation ceiling stays a saturated
        /// hue, while the tell has to bloom hard enough to be read in ~0.45 s. They were the same key
        /// (M_NeonRed) once; dropping the trim under the bloom threshold silently killed the tell.
        /// This test is the tripwire for anyone re-merging them.
        /// </summary>
        IEnumerator TestTellReadability()
        {
            // Peak channel above which ACES starts washing a saturated hue toward white/orange. A
            // navigational trim must stay under it; the combat tell is supposed to be way over.
            const float DesatCeiling = 1.25f;

            // Real shipped bloom threshold, read off the volume profile rather than assumed.
            float bloomThreshold = -1f;
            foreach (var v in FindObjectsByType<UnityEngine.Rendering.Volume>())
            {
                if (v.sharedProfile == null) continue;
                UnityEngine.Rendering.Universal.Bloom b;
                if (v.sharedProfile.TryGet(out b) && b.active) { bloomThreshold = b.threshold.value; break; }
            }
            Check("Tell_BloomThresholdFound", bloomThreshold > 0f, "threshold=" + bloomThreshold.ToString("0.###"));
            if (bloomThreshold <= 0f) bloomThreshold = 1.05f;

            // ---- the shipped prefab actually points at the tell material ----------------------
            GameObject enemyPrefab = null;
            foreach (var sp in FindObjectsByType<EnemySpawner>())
            {
                if (sp.prefab == null) continue;
                enemyPrefab = sp.prefab;
                break;
            }
            if (enemyPrefab == null) { Skip("Tell_PrefabFound", "no EnemySpawner with a prefab"); yield break; }

            var ev = enemyPrefab.GetComponentInChildren<EnemyVisuals>(true);
            var marker = ev != null ? ev.alertMarker : null;
            var markerRend = marker != null ? marker.GetComponent<Renderer>() : null;
            Check("Tell_AlertMarkerShipped", markerRend != null && markerRend.sharedMaterial != null,
                "prefab=" + enemyPrefab.name);
            if (markerRend == null || markerRend.sharedMaterial == null) yield break;

            Material tell = markerRend.sharedMaterial;
            string tellName = tell.name;
            Check("Tell_UsesDedicatedMaterial", tellName.StartsWith("M_AlertTell"),
                "alert marker material=" + tellName + " (a trim key here means navigation and the " +
                "combat tell have been merged again)");

            float tellPeak = PeakEmission(tell);
            Check("Tell_BloomsHard", tellPeak >= bloomThreshold * 2f,
                "peak=" + tellPeak.ToString("0.###") + " threshold=" + bloomThreshold.ToString("0.###") +
                " (the tell must be unmistakable against a near-black frame)");

            // ---- every navigational trim stays UNDER the ceiling -------------------------------
#if UNITY_EDITOR
            float loudestTrim = 0f;
            string[] trimKeys = { "M_NeonCyan", "M_NeonYellow", "M_NeonRed", "M_NeonPink" };
            foreach (var key in trimKeys)
            {
                var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + key + ".mat");
                if (m == null) { Skip("Trim_" + key + "_Exists", "material missing"); continue; }
                float peak = PeakEmission(m);
                if (peak > loudestTrim) loudestTrim = peak;
                Check("Trim_" + key + "_UnderDesatCeiling", peak <= DesatCeiling,
                    "peak=" + peak.ToString("0.###") + " ceiling=" + DesatCeiling.ToString("0.###") +
                    " (over this the tonemapper eats the hue and the tile loses its identity)");
            }
            Check("Tell_LouderThanEveryTrim", tellPeak > loudestTrim * 2f,
                "tell=" + tellPeak.ToString("0.###") + " loudest trim=" + loudestTrim.ToString("0.###"));
            var tellAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_AlertTell.mat");
            Check("Tell_MaterialAssetExists", tellAsset != null, "Assets/Materials/M_AlertTell.mat");

            // ---- the world the tell has to be read AGAINST ------------------------------------
            // Ambient and the structural albedos were lifted ~4x (see ENGINEERING-LOG, "Outside the
            // trims and the eclipse, the frame was black"). Two failure directions are guarded here:
            // sliding BACK to a near-black world makes everything a cutout again, and lifting FURTHER
            // eventually closes the gap the tell needs. Rule 9: these are shipped values, so they are
            // asserted, not just written in ProjectSetup.cs.
            // NOT multiplied by ambientIntensity on purpose: that field is a NO-OP in Trilight mode
            // (Unity applies it to Skybox ambient only), so the colour is the whole value. Asserting
            // the product would silently pass a build where someone "raised the ambient" with a knob
            // that does nothing.
            float ambEq = Lum(RenderSettings.ambientEquatorColor.linear);
            // Floor 0.15: the pre-fix value (#4E3325 at intensity 1) measures 0.040 and fails loudly;
            // the shipped #7A5540 x 1.35 measures ~0.209, so there is room to tune without tripping it.
            Check("Lighting_EquatorLitsVerticals", ambEq >= 0.15f,
                "equator linear lum=" + ambEq.ToString("0.####") +
                " (Trilight lights by NORMAL - this term alone lights every wall, pillar, and every " +
                "BACKLIT enemy torso the player is looking at)");

            float brightestStructural = 0f;
            string[] structuralKeys = { "M_Ground", "M_Stone", "M_Platform", "M_Enemy" };
            foreach (var key in structuralKeys)
            {
                var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + key + ".mat");
                if (m == null) { Skip("Lighting_" + key + "_Exists", "material missing"); continue; }
                float lum = Lum(m.GetColor("_BaseColor").linear);
                if (lum > brightestStructural) brightestStructural = lum;
                // ~0.010 linear is already darker than coal. Below that, ambient x albedo is a double
                // zero and no amount of grading recovers detail that was never rendered.
                Check("Lighting_" + key + "_AlbedoFloor", lum >= 0.010f,
                    "linear lum=" + lum.ToString("0.####") + " floor=0.01");
            }
            Check("Lighting_TellClearsTheWorld", tellPeak >= brightestStructural * 20f,
                "tell=" + tellPeak.ToString("0.###") + " brightest structural albedo=" +
                brightestStructural.ToString("0.####") +
                " (the tell must still be unmistakable against the lifted background)");
#else
            Skip("Trim_UnderDesatCeiling", "trim materials are only readable in the editor");
#endif
            yield return null;
        }

        /// <summary>Rec.709 relative luminance of an already-LINEAR colour.</summary>
        static float Lum(Color linear)
        {
            return linear.r * 0.2126f + linear.g * 0.7152f + linear.b * 0.0722f;
        }

        /// <summary>Brightest emission channel of a material, or 0 if it does not emit.</summary>
        static float PeakEmission(Material m)
        {
            if (m == null || !m.HasProperty("_EmissionColor")) return 0f;
            Color c = m.GetColor("_EmissionColor");
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }

        // ================================================================ 7. ITEMS

        IEnumerator TestItems()
        {
            ClearItems();
            yield return null;

            // ---- capacity + FIFO --------------------------------------------------------------
            var a = MakeItem(ItemEffect.Rebound);
            var b = MakeItem(ItemEffect.Rebound);
            var c = MakeItem(ItemEffect.Rebound);
            var d = MakeItem(ItemEffect.Rebound);
            a.displayName = "A"; b.displayName = "B"; c.displayName = "C"; d.displayName = "D";

            Check("Items_Pickup1", items.TryPickup(a));
            Check("Items_Pickup2", items.TryPickup(b));
            Check("Items_Pickup3", items.TryPickup(c));
            Check("Items_CapacityIsThree", items.IsFull, "held=" + items.Held.Count);
            Check("Items_RejectsWhenFull", !items.TryPickup(d));
            Check("Items_FifoCurrentIsFirst", items.Current == a, "current=" + (items.Current != null ? items.Current.displayName : "null"));

            items.UseCurrent();
            yield return null;
            Check("Items_UseConsumesFront", items.Current == b, "current=" + (items.Current != null ? items.Current.displayName : "null"));
            Check("Items_UseReducesCount", items.Held.Count == 2, "held=" + items.Held.Count);
            ClearItems();
            yield return null;

            // ---- real physics pickup ----------------------------------------------------------
            // This MUST go through OnTriggerEnter: IgnoreLayerCollision(Interactable, Player) once
            // silently suppressed every trigger in the game and a direct SendMessage hid the bug.
            // Pick an UNCOLLECTED template. A pickup the player has already walked over (there is one
            // at the spawn point) has its collider and renderers disabled, and Instantiate clones that
            // disabled state — the clone could then never fire a trigger and the test failed for a
            // reason that had nothing to do with the feature.
            ItemPickup template = null;
            foreach (var p in FindObjectsByType<ItemPickup>())
            {
                if (p.item == null) continue;
                var pc = p.GetComponent<Collider>();
                if (pc == null || !pc.enabled) continue;
                template = p;
                break;
            }

            if (template == null) Skip("Items_PhysicsPickup", "no ItemPickup with an ItemData in the scene");
            else
            {
                Check("Items_LayerCollisionAllowsTriggers",
                    !Physics.GetIgnoreLayerCollision(Layers.Interactable, Layers.Player),
                    "ignored=" + Physics.GetIgnoreLayerCollision(Layers.Interactable, Layers.Player));

                var clone = Instantiate(template.gameObject);
                clone.name = "~TestPickup";
                var cp = clone.GetComponent<ItemPickup>();
                // Belt and braces: guarantee the clone starts collectable regardless of template state.
                var cloneCol = clone.GetComponent<Collider>();
                if (cloneCol != null) cloneCol.enabled = true;
                foreach (var r in clone.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                // Placed just inside contact range (pickup trigger ~1.2m + CC radius ~0.4m) and then
                // walked into, so the collection genuinely goes through OnTriggerEnter.
                // The direction is SWEPT, not assumed: the level start has the wand pedestal plinth
                // 2 m in front of the spawn, so a blind forward shove walks the player into a wall and
                // the clone is never reached — the same staging failure the bloodstain test hit, and
                // the reason this check failed in a full run but passed in isolation. Require a clear
                // path AND ground at the destination; triggers are ignored so the clone's own trigger
                // does not read as a wall.
                Vector3 pickDir = combat.transform.forward;
                // 2.4 m, not 1.2: the pickup trigger is ~1.2 m and the player capsule ~0.4 m, so a clone
                // dropped at 1.2 m is ALREADY overlapping and is collected on the first frame — held0
                // then reads 1, the walk has nothing left to collect, and the check fails as "held 1 -> 1"
                // while the feature works perfectly. Spawn outside the trigger and walk in.
                const float pickWalk = 2.4f;
                for (int step = 0; step < 8; step++)
                {
                    Vector3 probe = Quaternion.Euler(0f, step * 45f, 0f) * combat.transform.forward;
                    probe.y = 0f; probe.Normalize();
                    bool blocked = Physics.Raycast(combat.transform.position + Vector3.up * 0.6f, probe,
                                                   pickWalk + 0.6f, ~0, QueryTriggerInteraction.Ignore);
                    bool ground = Physics.Raycast(combat.transform.position + probe * pickWalk + Vector3.up * 1.5f,
                                                  Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
                    if (!blocked && ground) { pickDir = probe; break; }
                }
                Vector3 target = combat.transform.position + pickDir * pickWalk + Vector3.up * 0.4f;
                clone.transform.position = target;
                yield return null;
                // Clear AFTER the clone has settled: staging this test can itself collect something.
                ClearItems();
                yield return null;

                int held0 = items.Held.Count;
                // 20 m/s crosses the ~0.8 m gap into contact before neutral-input ground friction
                // removes the impulse. 14 m/s stopped at ~0.76 m in the full suite: close enough to
                // look correct in a trace, but still outside the real trigger.
                motor.AddImpulse(pickDir * 20f);
                yield return WaitUntilOrTimeout(() => items.Held.Count > held0, 4f);
                Check("Items_PhysicsPickup", !waitTimedOut,
                    $"held {held0} -> {items.Held.Count}");

                if (!waitTimedOut)
                    Check("Items_PickupDisablesCollider",
                        cp == null || cp.GetComponent<Collider>() == null || !cp.GetComponent<Collider>().enabled);

                // Respawn restores world pickups (ItemPickup listens for PlayerRespawned).
                // Move the collected test prop clear before restoring it: an overlapping player would
                // collect it again on the next physics tick. Preserve the player's staging for the
                // grapple checks below; the actual checkpoint teleport is tested by LevelFlow.
                clone.transform.position += Vector3.up * 50f;
                GameEvents.RaisePlayerRespawned();
                yield return null;
                if (cp != null && cp.GetComponent<Collider>() != null)
                    Check("Items_RestoredOnRespawn", cp.GetComponent<Collider>().enabled);

                if (clone != null) Destroy(clone);
                yield return null;
            }

            ClearItems();
            yield return ResetPlayerState();

            // ---- Items and wands are independent ----------------------------------------------
            // No enemy is involved here. Keeping this behind SpawnDummy once allowed the exact
            // viewmodel regression to be silently skipped when unrelated enemy staging failed.
            var wandCtrl = combat.GetComponent<WandController>();
            if (wandCtrl == null) Skip("Wands_Independent", "no WandController on the player");
            else
            {
                // Wands and items are INDEPENDENT systems. Picking up an item must not touch the
                // equipped wand, and an item must be usable with E without any swapping. An earlier
                // build shared one offhand slot between them, which made items look broken.
                ClearItems();
                yield return null;
                wandCtrl.Equip(1);
                var wandBefore = wandCtrl.Current;
                int indexBefore = wandCtrl.Index;
                var offhandView = combat.GetComponentInChildren<OffhandViewmodel>(true);
                var modelBefore = offhandView != null ? offhandView.DisplayedInstance : null;
                Vector3 scaleBefore = modelBefore != null ? modelBefore.transform.localScale : Vector3.zero;
                Check("Wands_VisibleModelExists", modelBefore != null,
                    "offhand=" + (offhandView != null) + " model=" + (modelBefore != null ? modelBefore.name : "null"));
                var bookBefore = modelBefore != null ? modelBefore.GetComponent<SpellbookVisual>() : null;
                Check("Spellbook_VisibleModelMatchesInscription",
                    bookBefore != null && bookBefore.SelectedSpell == wandBefore &&
                    Mathf.Abs(modelBefore.transform.localScale.x - 1f) < 0.0001f,
                    "model=" + (modelBefore != null ? modelBefore.name : "null") +
                    " inscription=" + (wandBefore != null ? wandBefore.displayName : "null"));

                int heldBefore = items.Held.Count;
                var probe = MakeItem(ItemEffect.Rebound);
                items.TryPickup(probe);
                yield return null;
                Check("Items_PickupDoesNotChangeWand", wandCtrl.Current == wandBefore && wandCtrl.Index == indexBefore,
                    "wand " + (wandBefore != null ? wandBefore.displayName : "null") + " -> " +
                    (wandCtrl.Current != null ? wandCtrl.Current.displayName : "null"));
                Check("Items_PickupPreservesWandModel",
                    offhandView != null && offhandView.DisplayedInstance == modelBefore &&
                    modelBefore != null && (modelBefore.transform.localScale - scaleBefore).sqrMagnitude < 0.000001f,
                    "model=" + (offhandView != null && offhandView.DisplayedInstance != null
                        ? offhandView.DisplayedInstance.name : "null"));
                Check("Items_HeldAfterPickup", items.Held.Count == heldBefore + 1,
                    $"held {heldBefore} -> {items.Held.Count}");

                // Die while the item is still held: the regression left its tiny model visible for
                // the death delay. Respawn clears inventory but must not rebuild the persistent wand.
                GameEvents.RaisePlayerDied();
                yield return null;
                Check("DeathWithItemPreservesWandModel",
                    offhandView != null && offhandView.DisplayedInstance == modelBefore &&
                    modelBefore != null && (modelBefore.transform.localScale - scaleBefore).sqrMagnitude < 0.000001f);
                GameEvents.RaisePlayerRespawned();
                yield return null;
                Check("RespawnPreservesWandModel",
                    offhandView != null && offhandView.DisplayedInstance == modelBefore &&
                    modelBefore != null && (modelBefore.transform.localScale - scaleBefore).sqrMagnitude < 0.000001f);
                Check("RespawnClearsItems", items.Held.Count == 0, "held=" + items.Held.Count);

                // Usable directly, with no swap step. Restage after the respawn deliberately cleared it.
                items.TryPickup(probe);
                if (posture != null) posture.ResetFull();
                yield return null;
                Check("Items_NotBlockedByStagger", combat == null || !combat.IsStaggered,
                    "staggered=" + (combat != null && combat.IsStaggered));
                int beforeUse = items.Held.Count;
                items.UseCurrent();
                yield return null;
                Check("Items_UsableDirectly", items.Held.Count == beforeUse - 1,
                    $"held {beforeUse} -> {items.Held.Count} (E must spend one with no swap step)");
                Check("Items_EffectApplied", motor.IsReboundArmed, "rebound=" + motor.IsReboundArmed);
                Check("Wands_StillEquippedAfterItemUse", wandCtrl.Current == wandBefore,
                    "wand=" + (wandCtrl.Current != null ? wandCtrl.Current.displayName : "null"));
                Check("Items_UsePreservesWandModel",
                    offhandView != null && offhandView.DisplayedInstance == modelBefore &&
                    modelBefore != null && (modelBefore.transform.localScale - scaleBefore).sqrMagnitude < 0.000001f);

                // The wand set is fixed: cycling stays inside the loadout.
                int n = wandCtrl.loadout != null ? wandCtrl.loadout.Length : 0;
                wandCtrl.Next();
                Check("Wands_CycleStaysInLoadout", n > 0 && wandCtrl.Index >= 0 && wandCtrl.Index < n,
                    "index=" + wandCtrl.Index + "/" + n);
                wandCtrl.Equip(indexBefore);
            }
            yield return SettleTimeScale();
            ClearItems();

            // ---- Grapple: hook, pull, deathblow ---------------------------------------------------
            // Driven through UseCurrent, the very entry the E key uses, so a refusal that keeps the
            // item and a spend that fires are both the real code path.
            yield return ResetPlayerState();
            EnemyController prey = null;
            yield return SpawnDummy(combat.transform.position + combat.transform.forward * 15f, e => prey = e);
            if (prey == null) Skip("Items_Grapple", "no enemy prefab to hook");
            else
            {
                if (lockOn != null) lockOn.Release();   // exercise the crosshair search, not the lock
                AimAtGrappleDummy(prey);
                yield return null;
                var hook = MakeItem(ItemEffect.Grapple);
                items.TryPickup(hook);
                var found = items.FindGrappleTarget(hook);
                Check("Items_GrappleFindsTarget", found == prey,
                    "found=" + (found != null ? found.name : "null") + " dist=" +
                    Vector3.Distance(combat.transform.position, prey.transform.position).ToString("0.0"));

                int heldBefore = items.Held.Count;
                items.UseCurrent();
                yield return null;
                Check("Items_GrappleConsumed", items.Held.Count == heldBefore - 1, $"held {heldBefore} -> {items.Held.Count}");
                Check("Items_GrapplePulls", motor.IsPulling || exec.IsExecuting, "pulling=" + motor.IsPulling);
                yield return WaitUntilOrTimeout(() => !motor.IsPulling, 2f);
                Check("Items_GrapplePullEnds", !waitTimedOut, "pulling=" + motor.IsPulling);

                float standoff = exec.stabStandoff * (prey != null && prey.data != null ? Mathf.Max(0.6f, prey.data.scale) : 1f);
                if (prey != null)
                {
                    Vector3 flat = prey.transform.position - combat.transform.position; flat.y = 0f;
                    Check("Items_GrappleArrivesAtStandoff", flat.magnitude <= standoff + 1f,
                        $"dist={flat.magnitude:0.00} standoff={standoff:0.00}");
                }
                // The arrival runs ExecuteInteractor's own coroutine (wand riposte or melee execute);
                // the victim is dead once it finishes. A Unity-null prey means the corpse was cleaned up.
                yield return WaitUntilOrTimeout(() => !exec.IsExecuting && (prey == null || !prey.IsAlive || prey.Health.IsDead), 4f);
                Check("Items_GrappleExecutes", prey == null || !prey.IsAlive || prey.Health.IsDead,
                    "alive=" + (prey != null && prey.IsAlive) + " executing=" + exec.IsExecuting);
                yield return SettleTimeScale();
                if (prey != null) Destroy(prey.gameObject);
                yield return null;
            }

            // ---- Grapple onto a big enemy that is not open: posture, not a kill --------------------
            yield return ResetPlayerState();
            EnemyController big = null;
            yield return SpawnDummy(combat.transform.position + combat.transform.forward * 12f, e => big = e);
            if (big == null) Skip("Items_GrappleBig", "no enemy prefab to hook");
            else
            {
                // PlayerItems.IsBig keys off the Legendary_ prefix (as LockOnMarker does), so a renamed
                // grunt walks the mini-boss path without needing a Legendary prefab in the scene.
                big.name = "Legendary_~TestDummy";
                if (lockOn != null) lockOn.Release();
                AimAtGrappleDummy(big);
                yield return null;
                float postureBefore = big.Posture.Current;
                var hookBig = MakeItem(ItemEffect.Grapple);
                items.TryPickup(hookBig);
                items.UseCurrent();
                yield return null;
                yield return WaitUntilOrTimeout(() => !motor.IsPulling, 2f);
                yield return null;
                Check("Items_GrappleBigSurvives", big != null && big.IsAlive && !exec.IsExecuting,
                    "alive=" + (big != null && big.IsAlive) + " executing=" + exec.IsExecuting);
                if (big != null)
                {
                    CheckApprox("Items_GrappleBigTakesPosture", big.Posture.Current - postureBefore,
                        big.Posture.Max * hookBig.grappleBigPostureFraction, big.Posture.Max * 0.05f);
                    Check("Items_GrappleBigNotStaggered", !big.IsStaggered, "state=" + big.Current);
                    float standoff = exec.stabStandoff * (big.data != null ? Mathf.Max(0.6f, big.data.scale) : 1f);
                    Vector3 flat = big.transform.position - combat.transform.position; flat.y = 0f;
                    Check("Items_GrappleBigLandsAtStandoff", flat.magnitude <= standoff + 1f,
                        $"dist={flat.magnitude:0.00} standoff={standoff:0.00}");
                    Destroy(big.gameObject);
                }
                yield return null;
            }

            // ---- Grapple with nothing to hook: refused and KEPT ------------------------------------
            look.SetYaw(look.Yaw);
            yield return ResetPlayerState();
            if (lockOn != null) lockOn.Release();
            var noHook = MakeItem(ItemEffect.Grapple);
            // A 1 m reach: nothing can be inside it, so this exercises the refusal, not the search.
            noHook.grappleRange = 1f;
            items.TryPickup(noHook);
            Check("Items_GrappleNoTargetFound", items.FindGrappleTarget(noHook) == null);
            items.UseCurrent();
            yield return null;
            Check("Items_GrappleNoTargetNotConsumed", items.Held.Count == 1 && items.Current == noHook, "held=" + items.Held.Count);
            Check("Items_GrappleNoTargetNoPull", !motor.IsPulling);
            ClearItems();

            // ---- Rebound + Deflect Sigil ---------------------------------------------------------
            yield return ResetPlayerState();
            var rebound = MakeItem(ItemEffect.Rebound);
            rebound.reboundExitMultiplier = 1.18f;
            rebound.reboundBonusSpeed = 3f;
            items.TryPickup(rebound);
            items.UseCurrent();
            yield return null;
            Check("Items_ReboundConsumedToArm", items.Held.Count == 0 && motor.IsReboundArmed,
                "held=" + items.Held.Count + " armed=" + motor.IsReboundArmed);

            // A duplicate activation is refused and retained until the first armed effect is spent.
            var duplicate = MakeItem(ItemEffect.Rebound);
            items.TryPickup(duplicate);
            items.UseCurrent();
            Check("Items_ReboundDuplicateKept", items.Held.Count == 1 && motor.IsReboundArmed,
                "held=" + items.Held.Count + " armed=" + motor.IsReboundArmed);
            ClearItems();
            yield return null;

            var sigil = MakeItem(ItemEffect.DeflectSigil);
            sigil.deflectSigilBonusStacks = 2;
            sigil.deflectSigilImpulse = 5f;
            items.TryPickup(sigil);
            items.UseCurrent();
            yield return null;
            Check("Items_SigilConsumedToArm", items.Held.Count == 0 && items.DeflectSigilArmed,
                "held=" + items.Held.Count + " armed=" + items.DeflectSigilArmed);
            GameEvents.RaiseParryResolved(ParryResult.Blocked);
            Check("Items_SigilSurvivesBlock", items.DeflectSigilArmed);
            GameEvents.RaiseParryResolved(ParryResult.Hit);
            Check("Items_SigilSurvivesMiss", items.DeflectSigilArmed);
            GameEvents.RaiseParryResolved(ParryResult.Perfect);
            yield return null;
            Check("Items_SigilSpendsOnPerfect", !items.DeflectSigilArmed);
            ClearItems();
        }

        // ================================================================ 8. FLASK

        IEnumerator TestFlask()
        {
            // Items ends with several real traversal effects and can leave the player airborne. Stage
            // the timing-sensitive drink on the current checkpoint floor so a fall death cannot heal
            // through Respawn while this test is waiting to prove the interrupted drink stayed inert.
            Transform flaskStage = LevelManager.I != null && LevelManager.I.Current != null
                ? LevelManager.I.Current.spawnPoint
                : LevelManager.I != null ? LevelManager.I.startSpawn : null;
            if (flaskStage != null)
                motor.Teleport(flaskStage.position, flaskStage.eulerAngles.y);
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);

            res.RefillFlask();
            yield return null;
            int max = res.MaxFlask;
            Check("Flask_RefillFillsToMax", res.FlaskCharges == max, $"{res.FlaskCharges}/{max}");

            bool used = res.UseFlask();
            Check("Flask_UseConsumesCharge", used && res.FlaskCharges == max - 1, "charges=" + res.FlaskCharges);

            // Drain and confirm it refuses at zero.
            while (res.FlaskCharges > 0) res.UseFlask();
            Check("Flask_RefusesWhenEmpty", !res.UseFlask(), "charges=" + res.FlaskCharges);

            // Heal amount comes from the stat sheet.
            res.RefillFlask();
            health.SetCurrent(health.Max * 0.25f);
            float hp0 = health.Current;
            health.Heal(stats.FlaskHeal);
            CheckApprox("Flask_HealAmountMatchesStats", health.Current - hp0, stats.FlaskHeal, 0.01f);

            // Interrupt is a safe no-op when not drinking.
            flask.Interrupt();
            Check("Flask_InterruptSafeWhenIdle", !flask.IsDrinking);

            // The real drink, through FlaskAbility.TryDrink — the method Update calls on InputReader
            // input. A hit taken mid-drink (resolved through PlayerCombat.ReceiveAttack, as all combat
            // is) must interrupt it and lose the charge; an uninterrupted drink must heal.
            res.RefillFlask();
            health.Invulnerable = false;
            health.SetCurrent(health.Max * 0.4f);
            yield return null;
            bool drinkStarted = flask.TryDrink() && flask.IsDrinking;
            float hpAtDrink = health.Current;

            EnemyController attacker = null;
            yield return SpawnDummy(combat.transform.position + combat.transform.forward * 2.5f, e => attacker = e);
            bool interruptedByHit = false, noHealAfterInterrupt = false;
            if (attacker != null)
            {
                FaceAwayFrom(attacker.transform.position);   // not facing + no parry => ParryResult.Hit
                combat.ReceiveAttack(MakeAttack(attacker, 1f, false));
                yield return null;
                interruptedByHit = !flask.IsDrinking;
                yield return WaitRealtime(D.flaskDrinkSeconds + 0.3f);
                noHealAfterInterrupt = health.Current <= hpAtDrink + 0.01f;
                Destroy(attacker.gameObject);
            }

            res.RefillFlask();
            health.SetCurrent(health.Max * 0.4f);
            yield return null;
            float hpBeforeFullDrink = health.Current;
            bool secondDrinkStarted = flask.TryDrink();
            yield return WaitUntilOrTimeout(() => !flask.IsDrinking, D.flaskDrinkSeconds + 2f);
            bool healed = health.Current > hpBeforeFullDrink + 0.5f;

            Check("Flask_DrinkCoroutineAndInterruptOnHit",
                drinkStarted && interruptedByHit && noHealAfterInterrupt && secondDrinkStarted && healed,
                $"started={drinkStarted} interrupted={interruptedByHit} noHeal={noHealAfterInterrupt} " +
                $"completed={secondDrinkStarted} healed={healed}");
            yield return SettleTimeScale();
        }

        // ================================================================ 9. PYRE + SUPER ATTACK

        IEnumerator TestUltimate()
        {
            res.ConsumePyre();
            Check("Super_StartsNotFull", !res.PyreFull, "pyre=" + res.Pyre.ToString("0.0"));

            // The gate. A super may only fire on a FULL bar — this is the whole economy.
            bool refusedWhenEmpty = !ult.TrySuper();
            Check("Super_RefusedBelowFullPyre", refusedWhenEmpty && !ult.IsActive,
                $"fired={!refusedWhenEmpty} active={ult.IsActive} pyre={res.Pyre:0.0}");

            res.AddPyre(res.MaxPyre);
            Check("Super_FullPyreGate", res.PyreFull, "pyre=" + res.Pyre.ToString("0.0"));

            res.ConsumePyre();
            CheckApprox("Super_ConsumeEmptiesPyre", res.Pyre, 0f, 0.01f);

            // Pyre clamps at max — a perfect-parry streak must not overflow the bar.
            res.AddPyre(res.MaxPyre * 5f);
            CheckApprox("Super_PyreClampsAtMax", res.Pyre, res.MaxPyre, 0.01f);
            res.ConsumePyre();

            // Rule 9: every super is authored on the WeaponData asset, so a weapon whose numbers never
            // reached the asset has no super at all and Q would silently do nothing.
            var sw = weapons != null ? weapons.Current : null;
            Check("Super_WeaponCarriesSuperData",
                sw != null && sw.superDamage > 0f && sw.superRadius > 0f && sw.superHits >= 1
                    && !string.IsNullOrEmpty(sw.superName),
                sw == null ? "no weapon" : $"{sw.displayName}: {sw.superName} kind={sw.superKind} " +
                    $"dmg={sw.superDamage} posture={sw.superPostureDamage} r={sw.superRadius} hits={sw.superHits}");

            Check("Super_SlowMoDoesNotAffectPlayer", D.ultSlowScale > 0f && D.ultSlowScale < 1f,
                "slow=" + D.ultSlowScale.ToString("0.00"));

            // The real thing, through UltimateAbility.TrySuper — the method Update calls on InputReader
            // input. A full bar must fire, damage a nearby enemy and be spent.
            EnemyController target = null;
            yield return SpawnDummy(combat.transform.position + combat.transform.forward * 2f, e => target = e);
            bool ultFired = false, enemyHurt = false, pyreSpent = false;
            if (target != null)
            {
                float hp0 = target.Health.Current;
                res.AddPyre(res.MaxPyre);
                ultFired = ult.TrySuper();
                yield return WaitUntilOrTimeout(
                    () => target == null || !target.IsAlive || target.Health.Current < hp0, 3f);
                enemyHurt = target == null || !target.IsAlive || target.Health.Current < hp0;
                pyreSpent = !res.PyreFull;
                yield return WaitUntilOrTimeout(() => !ult.IsActive, D.ultSlowSeconds + 4f);
                if (target != null) Destroy(target.gameObject);
            }
            Check("Super_FiresAndSpendsPyre", ultFired && enemyHurt && pyreSpent,
                $"fired={ultFired} enemyDamaged={enemyHurt} pyreSpent={pyreSpent} " +
                (target == null ? "(no enemy prefab to spawn)" : ""));
            yield return SettleTimeScale();
            res.ConsumePyre();
        }

        // ================================================================ 10. PROGRESSION

        IEnumerator TestProgression()
        {
            if (SoulsWallet.I == null) { Check("Progression_WalletExists", false); yield break; }
            Check("Progression_WalletExists", true);

            // Souls on kill.
            EnemyController victim = null;
            yield return SpawnDummy(combat.transform.position + combat.transform.forward * 3f, e => victim = e);
            if (victim == null) Skip("Progression_SoulsOnKill", "no enemy prefab");
            else
            {
                int s0 = SoulsWallet.I.Souls;
                int expected = victim.data != null ? victim.data.soulValue : 0;
                victim.Health.TakeDamage(new DamageInfo { damage = 99999f, isExecute = true });
                yield return null;
                Check("Progression_SoulsOnKill", SoulsWallet.I.Souls >= s0 + expected,
                    $"{s0} -> {SoulsWallet.I.Souls} (expected +{expected})");
                if (victim != null) Destroy(victim.gameObject);
            }

            // Spend / earn arithmetic.
            int before = SoulsWallet.I.Souls;
            SoulsWallet.I.Add(500);
            Check("Progression_AddSouls", SoulsWallet.I.Souls == before + 500);
            Check("Progression_SpendSucceedsWhenAffordable", SoulsWallet.I.TrySpend(200));
            Check("Progression_SpendFailsWhenTooExpensive", !SoulsWallet.I.TrySpend(SoulsWallet.I.Souls + 1));

            // Upgrade cost curve.
            var table = FindAnyObjectByType<LevelUpMenu>() != null ? FindAnyObjectByType<LevelUpMenu>().table : null;
            if (table == null) Skip("Progression_UpgradeCostCurve", "LevelUpMenu.table not assigned");
            else
            {
                Check("Progression_UpgradeCostCurve", table.Cost(1) > table.Cost(0),
                    $"lvl0={table.Cost(0)} lvl1={table.Cost(1)}");
                CheckApprox("Progression_BaseCost", table.Cost(0), table.baseCost, 0.5f);
            }

            // Levelling Vitality must raise max HP and posture.
            int vit0 = stats.Vitality;
            float maxHp0 = health.Max;
            float maxPost0 = posture.Max;
            stats.Increase(StatType.Vitality);
            yield return null;
            posture.RefreshMax();
            Check("Progression_VitalityRaisesMaxHP", health.Max > maxHp0, $"{maxHp0:0} -> {health.Max:0}");
            Check("Progression_VitalityRaisesMaxPosture", posture.Max > maxPost0, $"{maxPost0:0} -> {posture.Max:0}");
            stats.Vitality = vit0;
            stats.Apply(true);
            yield return null;

            // Death drop. TakeAll() and SpawnBloodstain() both run SYNCHRONOUSLY from Health.OnDied,
            // so the wallet and the stain are asserted here, before the respawn. Asserting the empty
            // wallet *after* the respawn is wrong: the player respawns at the checkpoint, and when they
            // died on it — as they do here, with no checkpoint activated yet — they land on their own
            // stain and immediately recover the souls through its trigger. That is correct behaviour.
            SoulsWallet.I.Add(300);
            int carried = SoulsWallet.I.Souls;
            health.Invulnerable = false;
            health.TakeDamage(new DamageInfo { damage = 999999f });
            Check("Progression_SoulsLostOnDeath", SoulsWallet.I.Souls == 0, "souls=" + SoulsWallet.I.Souls);
            // Any stain, not the first found: an earlier section's stain can still be in the scene
            // (the stain is only recovered by touching it), and FindAnyObjectByType picked that one.
            Bloodstain dropped = null;
            foreach (var st in FindObjectsByType<Bloodstain>())
                if (st != null && st.amount == carried) { dropped = st; break; }
            if (dropped == null) dropped = FindAnyObjectByType<Bloodstain>();
            Check("Progression_BloodstainCarriesSouls", dropped != null && dropped.amount == carried,
                dropped == null
                    ? "no bloodstain spawned — LevelManager.bloodstainPrefab unassigned?"
                    : $"stain={dropped.amount} expected={carried}");

            yield return WaitUntilOrTimeout(() => GameManager.I.State == GameState.Playing && !health.IsDead, 6f);
            Check("Progression_RespawnsAfterDeath", !waitTimedOut, "state=" + GameManager.I.State);

            // Recovery, by REAL PHYSICS. A fresh stain is dropped near the respawned player through the
            // public LevelManager entry point (which also clears whatever the death above left), then
            // walked into — never SendMessage("OnTriggerEnter"), because a suppressed layer-collision
            // pair once disabled every trigger in the game and a direct call would have hidden it.
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 3f);
            Vector3 foot = combat.transform.position;

            // Pick a direction that is actually walkable: the spawn point has furniture around it (the
            // wand pedestal, for one), and a stain dropped into a wall can never be reached. Require a
            // clear path AND ground under the target; triggers are ignored so the stain's own collider
            // and nearby checkpoints do not read as walls.
            Vector3 dir = combat.transform.forward;
            const float walk = 2f;
            for (int a = 0; a < 8; a++)
            {
                Vector3 d = Quaternion.Euler(0f, a * 45f, 0f) * combat.transform.forward;
                d.y = 0f; d.Normalize();
                bool blocked = Physics.Raycast(foot + Vector3.up * 0.6f, d, walk + 0.4f,
                                               ~0, QueryTriggerInteraction.Ignore);
                bool ground = Physics.Raycast(foot + d * walk + Vector3.up * 1.5f, Vector3.down, 4f,
                                              ~0, QueryTriggerInteraction.Ignore);
                if (!blocked && ground) { dir = d; break; }
            }
            Vector3 stainPos = foot + dir * walk;
            LevelManager.I.SpawnBloodstain(stainPos, 250);
            yield return null;
            var stain = FindAnyObjectByType<Bloodstain>();
            if (stain == null)
                Check("Progression_BloodstainRecovery", false, "LevelManager.SpawnBloodstain produced no stain");
            else
            {
                FacePoint(stain.transform.position);
                yield return null;
                int s0 = SoulsWallet.I.Souls;
                Vector3 toStain = stain.transform.position - combat.transform.position;
                toStain.y = 0f;
                // 20 m/s crosses the ~0.6 m gap into contact before neutral-input ground friction
                // removes the impulse. 14 m/s stopped just outside this real trigger in a full run.
                motor.AddImpulse(toStain.normalized * 20f);
                yield return WaitUntilOrTimeout(() => SoulsWallet.I.Souls > s0, 4f);
                Check("Progression_BloodstainRecovery", !waitTimedOut,
                    $"souls {s0} -> {SoulsWallet.I.Souls} (walked {toStain.magnitude:0.0}m into the trigger)");
            }
        }

        // ================================================================ 11. LEVEL FLOW

        IEnumerator TestLevelFlow()
        {
            if (LevelManager.I == null) { Check("Level_ManagerExists", false); yield break; }
            Check("Level_ManagerExists", true);

            var checkpoints = FindObjectsByType<Checkpoint>();
            Check("Level_CheckpointsExist", checkpoints.Length > 0, "count=" + checkpoints.Length);

            if (checkpoints.Length > 0)
            {
                // Not blindly checkpoints[0]: SetCheckpoint is a no-op on the checkpoint that is already
                // current, and FindObjectsByType has no order. When the first one found was the one the
                // player had already touched (or an earlier test had set), the heal and refill never
                // ran and both checks went red for a reason that had nothing to do with checkpoints.
                var cp = checkpoints[0];
                foreach (var candidate in checkpoints)
                    if (candidate != LevelManager.I.Current) { cp = candidate; break; }
                health.SetCurrent(health.Max * 0.3f);
                while (res.FlaskCharges > 0) res.UseFlask();

                LevelManager.I.SetCheckpoint(cp);
                yield return null;
                Check("Level_CheckpointBecomesCurrent", LevelManager.I.Current == cp);
                Check("Level_CheckpointActivates", cp.Activated);
                CheckApprox("Level_CheckpointHeals", health.Current, health.Max, 0.01f);
                Check("Level_CheckpointRefillsFlask", res.FlaskCharges == res.MaxFlask,
                    $"{res.FlaskCharges}/{res.MaxFlask}");

                // Respawn returns to that checkpoint and resets enemies.
                EnemySpawner spawner = null;
                foreach (var s in FindObjectsByType<EnemySpawner>())
                    if (!s.isBoss && s.Instance != null) { spawner = s; break; }
                GameObject instanceBefore = spawner != null ? spawner.Instance : null;

                int deaths0 = LevelManager.I.DeathCount;
                LevelManager.I.Respawn();
                yield return null;

                Check("Level_RespawnIncrementsDeaths", LevelManager.I.DeathCount == deaths0 + 1,
                    $"{deaths0} -> {LevelManager.I.DeathCount}");
                if (cp.spawnPoint != null)
                    Check("Level_RespawnMovesToCheckpoint",
                        Vector3.Distance(combat.transform.position, cp.spawnPoint.position) < 2f,
                        "dist=" + Vector3.Distance(combat.transform.position, cp.spawnPoint.position).ToString("0.00"));
                if (spawner != null)
                    Check("Level_RespawnResetsEnemies", spawner.Instance != instanceBefore,
                        "spawner re-instantiated=" + (spawner.Instance != instanceBefore));
            }

            // Kill zone / void fall.
            var kill = FindAnyObjectByType<KillZone>();
            if (kill == null) Skip("Level_KillZone", "no KillZone in the scene");
            else
            {
                health.Invulnerable = false;
                health.ResetFull();
                var box = kill.GetComponent<Collider>();
                Vector3 inside = box != null ? box.bounds.center : kill.transform.position;
                motor.Teleport(inside + Vector3.up * 0.2f, 0f);
                yield return WaitUntilOrTimeout(() => health.IsDead || GameManager.I.State == GameState.Dead, 4f);
                Check("Level_KillZoneKills", !waitTimedOut, "dead=" + health.IsDead);
                yield return WaitUntilOrTimeout(() => GameManager.I.State == GameState.Playing, 6f);
                Check("Level_RecoversAfterKillZone", !waitTimedOut, "state=" + GameManager.I.State);
            }

            // Speedrun timer.
            if (SpeedrunTimer.I == null) Skip("Level_SpeedrunTimer", "no SpeedrunTimer in the scene");
            else
            {
                Check("Level_SpeedrunTimerPresent", true, "elapsed=" + SpeedrunTimer.I.Elapsed.ToString("0.0"));
                Check("Level_TimerFormat", SpeedrunTimer.Format(65.5f) == "01:05.50",
                    "got=" + SpeedrunTimer.Format(65.5f));
                // Start, through SpeedrunTimer.TryStartRun — the method Update calls on the first
                // movement input. Idempotent, so a run already ticking must refuse a second start.
                // The timer only accrues while the state is Playing or Dead, and the kill-zone assert
                // directly above leaves the state machine mid-transition (and the spawn pedestal can be
                // holding a menu open, which pauses). Pin both, then wait on the CONDITION rather than
                // on a fixed slice of wall clock.
                WandSelectMenu.ForceClose();
                if (GameManager.I != null) GameManager.I.SetState(GameState.Playing);
                yield return null;
                var timer = SpeedrunTimer.I;
                bool alreadyRunning = timer.Running;
                bool runStartedEvent = false;
                Action onRunStarted = () => runStartedEvent = true;
                timer.RunStarted += onRunStarted;
                bool accepted = timer.TryStartRun();
                float elapsed0 = timer.Elapsed;
                yield return WaitUntilOrTimeout(() => timer.Elapsed > elapsed0, 2f);
                timer.RunStarted -= onRunStarted;
                bool startBehaviour = alreadyRunning ? (!accepted && !runStartedEvent)
                                                     : (accepted && runStartedEvent);
                Check("Level_TimerStartsOnFirstInput",
                    startBehaviour && timer.Running && !timer.Finished && timer.Elapsed > elapsed0,
                    $"alreadyRunning={alreadyRunning} accepted={accepted} event={runStartedEvent} " +
                    $"elapsed {elapsed0:0.00} -> {timer.Elapsed:0.00}");
            }
        }

        // ================================================================ 12. BOSS

        IEnumerator TestBoss()
        {
            var boss = FindAnyObjectByType<BossController>();
            if (boss == null)
            {
                Check("Boss_Exists", false, "no BossController — is Spawn_Boss in the level?");
                yield break;
            }
            Check("Boss_Exists", true);
            Check("Boss_StartsAggroLocked", !boss.Activated);

            int hpEvents = 0, postureEvents = 0;
            Action<float, float, int> onHp = (c, m, s) => hpEvents++;
            Action<float, float> onPost = (c, m) => postureEvents++;
            bool defeated = false;
            Action onDefeat = () => defeated = true;
            GameEvents.BossHealthChanged += onHp;
            GameEvents.BossPostureChanged += onPost;
            GameEvents.BossDefeated += onDefeat;

            boss.Activate();
            yield return null;
            Check("Boss_Activates", boss.Activated);
            Check("Boss_HasThreeSegments", boss.SegmentsLeft == 3, "segments=" + boss.SegmentsLeft);
            Check("Boss_HealthEventsReachHUD", hpEvents > 0, "events=" + hpEvents);
            Check("Boss_PostureEventsReachHUD", postureEvents > 0, "events=" + postureEvents);
            Check("Boss_DeathIsStagger", boss.Health.deathIsStagger);

            // Lightning only staggers a boss — it must never delete one.
            Vector3 near = boss.transform.position - boss.transform.forward * 3f;
            near.y = boss.transform.position.y + 0.2f;
            motor.Teleport(near, 0f);
            FacePoint(boss.transform.position);
            yield return null;
            float bossHp0 = boss.Health.Current;
            yield return SettleTimeScale();

            // Drive all three deathblow segments deterministically.
            int segments0 = boss.SegmentsLeft;
            for (int i = 0; i < segments0 + 1 && boss != null && boss.IsAlive; i++)
            {
                int segBefore = boss.SegmentsLeft;
                int phaseBefore = boss.Phase;

                boss.Health.TakeDamage(new DamageInfo { damage = 999999f });
                yield return null;
                Check($"Boss_Seg{i}_ZeroHpDoesNotKill", boss.IsAlive, "alive=" + boss.IsAlive);
                Check($"Boss_Seg{i}_ZeroHpBreaksPosture", boss.Posture.IsBroken, "broken=" + boss.Posture.IsBroken);
                Check($"Boss_Seg{i}_SegmentNotConsumedWithoutExecute", boss.SegmentsLeft == segBefore,
                    $"{segBefore} -> {boss.SegmentsLeft}");

                boss.Health.TakeDamage(new DamageInfo { damage = 1000f, isExecute = true });
                yield return null;

                if (segBefore > 1)
                {
                    Check($"Boss_Seg{i}_ExecuteRemovesSegment", boss.SegmentsLeft == segBefore - 1,
                        $"{segBefore} -> {boss.SegmentsLeft}");
                    Check($"Boss_Seg{i}_PhaseAdvances", boss.Phase >= phaseBefore,
                        $"phase {phaseBefore} -> {boss.Phase}");
                    Check($"Boss_Seg{i}_HealsForNextSegment", boss.Health.Current > 0f,
                        "hp=" + boss.Health.Current.ToString("0"));
                }
                else
                {
                    Check("Boss_FinalExecuteDefeats", defeated, "defeated=" + defeated);
                    break;
                }
            }

            Check("Boss_DefeatedEventFired", defeated);
            if (SpeedrunTimer.I != null)
                Check("Boss_DefeatStopsTimer", SpeedrunTimer.I.Finished, "finished=" + SpeedrunTimer.I.Finished);

            GameEvents.BossHealthChanged -= onHp;
            GameEvents.BossPostureChanged -= onPost;
            GameEvents.BossDefeated -= onDefeat;

            yield return SettleTimeScale();
            // Put the arena back so a later manual play session is not left bossless.
            if (LevelManager.I != null) LevelManager.I.ResetEnemies();
            yield return null;
        }

        // ================================================================ 13. HUD

        IEnumerator TestHud()
        {
            if (hud == null) { Check("HUD_ControllerExists", false); yield break; }
            Check("HUD_ControllerExists", true);

            // Every bar is asserted on the ANCHOR span, not fillAmount: a UGUI Image with a null
            // sprite silently ignores type=Filled/fillAmount and renders permanently full. That bug
            // shipped once and froze every bar in the game, so this is the regression guard.
            yield return CheckBarTracks("HUD_HealthBar", hud.healthBar, () =>
            {
                health.SetCurrent(health.Max * 0.5f);
            }, 0.5f);

            health.ResetFull();
            yield return null;

            yield return CheckBarTracks("HUD_PyreBar", hud.pyreBar, () =>
            {
                res.ConsumePyre();
                res.AddPyre(res.MaxPyre * 0.4f);
            }, 0.4f);

            res.ConsumePyre();
            yield return null;

            yield return CheckBarTracks("HUD_PostureBar", hud.postureBar, () =>
            {
                posture.ResetFull();
                posture.Add(posture.Max * 0.6f);
            }, 0.6f);

            posture.ResetFull();
            yield return null;

            // Boss bars.
            if (bossBar == null) Skip("HUD_BossBars", "no BossBarView");
            else
            {
                GameEvents.RaiseBossHealthChanged(30f, 100f, 2);
                GameEvents.RaiseBossPostureChanged(70f, 100f);
                yield return null;
                CheckBarAnchor("HUD_BossHealthBar", bossBar.health, 0.3f);
                CheckBarAnchor("HUD_BossPostureBar", bossBar.posture, 0.7f);
            }

            // The book owns spell selection; the duplicate bottom hotbar is gone.
            Check("HUD_ItemHotbarRemoved", hud.itemSlots != null && hud.itemSlots.Length == 0,
                "slots=" + (hud.itemSlots != null ? hud.itemSlots.Length : 0));
            Check("HUD_DeathblowBannerWired", hud.deathblowText != null);
            Check("HUD_TextWidgetsWired",
                hud.healthText != null && hud.flaskText != null && hud.soulsText != null && hud.timerText != null);

            // ---- top-left status strip: held items + active effects ------------------------------
            // One row per carried item (front one marked), one per running effect with a countdown,
            // plus the persistent two-line run contract in scored levels. The strip reads the live motor for
            // the countdown, so a surge ended by hand must clear only its effect row next frame.
            var strip = hud.statusStrip;
            Check("HUD_StatusStripWired", strip != null && strip.text != null,
                "HudBuilder must build StatusStrip and assign HUDController.statusStrip");
            if (strip != null)
            {
                ClearItems();
                motor.ClearItemMovementBonuses();
                motor.SpeedMultiplier = 1f;
                bool godWas = health.Invulnerable;
                health.Invulnerable = false;
                yield return null;
                yield return null;
                bool hasRunContract = LevelRunScorer.I != null &&
                    (LevelRunScorer.I.RequiredSouls > 0 || LevelRunScorer.I.RequiredRegularKills > 0);
                int persistentRows = hasRunContract ? 2 : 0;
                Check("HUD_StatusStripIdleState",
                    hasRunContract ? strip.Text.Contains("RUN ") && strip.RowCount == persistentRows : strip.IsEmpty,
                    "rows=" + strip.RowCount + " text='" + strip.Text + "'");

                var hook = MakeItem(ItemEffect.Grapple);
                hook.displayName = "Test Hook";
                var surge = MakeItem(ItemEffect.Rebound);
                surge.displayName = "Test Rebound";
                items.TryPickup(hook);
                items.TryPickup(surge);
                yield return null;
                Check("HUD_StatusStripShowsHeldItem", strip.Text.Contains("TEST HOOK"), "text='" + strip.Text + "'");
                Check("HUD_StatusStripOneRowPerItem", strip.RowCount == persistentRows + 2, "rows=" + strip.RowCount);
                Check("HUD_StatusStripMarksCurrentFirst",
                    strip.Text.IndexOf("> TEST HOOK", StringComparison.Ordinal) >= 0
                    && strip.Text.IndexOf("TEST HOOK", StringComparison.Ordinal) < strip.Text.IndexOf("TEST REBOUND", StringComparison.Ordinal),
                    "text='" + strip.Text + "'");

                // Spend Rebound (the hook is in front, so pull it out of the way first).
                ClearItems();
                items.TryPickup(surge);
                if (posture != null) posture.ResetFull();
                items.UseCurrent();
                yield return null;
                yield return null;
                Check("HUD_StatusStripShowsReboundArmed",
                    motor.IsReboundArmed && strip.Text.Contains("REBOUND ARMED"),
                    "armed=" + motor.IsReboundArmed + " text='" + strip.Text + "'");
                Check("HUD_StatusStripReboundRowNotAnItemRow", !strip.Text.Contains("TEST REBOUND"), "text='" + strip.Text + "'");

                var testMenu = hud.GetComponent<TestMenu>();
                if (testMenu != null && testMenu.statusEffectsButton != null)
                {
                    // This is a developer-menu control. Exercise the real wired button with the same
                    // session capability a trusted tester must grant; the suite's LevelEditor and
                    // WandPedestal sections deliberately restore the locked state when they finish.
                    DeveloperAccess.UnlockForTests();
                    StatusStripView.StatusEffectsVisible = true;
                    testMenu.statusEffectsButton.onClick.Invoke();
                    yield return null;
                    Check("HUD_StatusToggleHidesOnlyEffects",
                        !strip.Text.Contains("REBOUND ARMED") && (!hasRunContract || strip.Text.Contains("RUN ")),
                        "text='" + strip.Text + "'");
                    testMenu.statusEffectsButton.onClick.Invoke();
                    yield return null;
                    Check("HUD_StatusToggleRestoresLiveEffect", strip.Text.Contains("REBOUND ARMED"),
                        "text='" + strip.Text + "'");
                    DeveloperAccess.LockForTests();
                }
                else Check("HUD_StatusToggleButtonWired", false, "generated HUD has no live status toggle button");

                motor.ClearItemMovementBonuses();
                yield return null;
                yield return null;
                Check("HUD_StatusStripClearsWhenReboundEnds", !strip.Text.Contains("REBOUND ARMED") && strip.RowCount == persistentRows,
                    "rows=" + strip.RowCount + " text='" + strip.Text + "'");

                health.Invulnerable = true;
                yield return null;
                yield return null;
                Check("HUD_StatusStripShowsGodMode", strip.Text.Contains("GOD MODE"), "text='" + strip.Text + "'");
                health.Invulnerable = false;
                motor.SpeedMultiplier = 1.5f;
                yield return null;
                yield return null;
                Check("HUD_StatusStripShowsSpeedMultiplier", strip.Text.Contains("SPEED x1.5") && !strip.Text.Contains("GOD MODE"),
                    "text='" + strip.Text + "'");
                motor.SpeedMultiplier = 1f;
                health.Invulnerable = godWas;
                yield return null;
                yield return null;
                Check("HUD_StatusStripSettlesAfterEffects", godWas || strip.RowCount == persistentRows,
                    "rows=" + strip.RowCount + " text='" + strip.Text + "'");
                StatusStripView.StatusEffectsVisible = true;
                ClearItems();
            }
        }

        // ================================================================ MAIN MENU
        //
        // The suite runs in the LEVEL scene, so the menu cannot be loaded mid-run without ending the
        // run. Everything here therefore asserts on the built ASSETS (scene list + MainMenu.prefab)
        // and on a clone parented under an INACTIVE holder — no Awake, no second EventSystem, no
        // full-screen canvas over the game view, no cursor unlock.
        //
        // The one thing worth guarding above all: the row count must track LevelRegistry, not a
        // constant. A menu with a hardcoded list is the exact failure this feature exists to avoid.

        IEnumerator TestMainMenu()
        {
            // ---- the game boots into the menu -------------------------------------------------
            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;
            Check("MainMenu_BuildHasScenes", sceneCount >= 2, "count=" + sceneCount);

            string first = sceneCount > 0 ? UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(0) : "";
            Check("MainMenu_IsBuildIndexZero",
                first.EndsWith("/MainMenu.unity", StringComparison.OrdinalIgnoreCase),
                "buildIndex0=" + first);

            bool levelInBuild = false;
            for (int i = 0; i < sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i)
                        .EndsWith("/Level_01.unity", StringComparison.OrdinalIgnoreCase))
                    levelInBuild = true;
            Check("MainMenu_LevelStillInBuild", levelInBuild, "Level_01 must survive the reindex");

#if UNITY_EDITOR
            var sceneAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Scenes/MainMenu.unity");
            Check("MainMenu_SceneAssetExists", sceneAsset != null, "Assets/Scenes/MainMenu.unity");

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MainMenu.prefab");
            Check("MainMenu_PrefabExists", prefab != null, "Assets/Prefabs/MainMenu.prefab");

            var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelRegistry>("Assets/Data/LevelRegistry.asset");
            Check("MainMenu_RegistryExists", registry != null, "Assets/Data/LevelRegistry.asset");

            if (prefab != null)
            {
                var asset = prefab.GetComponent<MainMenuController>();
                Check("MainMenu_ControllerOnPrefab", asset != null);
                Check("MainMenu_CanvasOnPrefab", prefab.GetComponent<Canvas>() != null);
                Check("MainMenu_RaycasterOnPrefab", prefab.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null,
                    "no raycaster = a menu you cannot click");
                Check("MainMenu_HasEventSystem",
                    prefab.GetComponentInChildren<UnityEngine.EventSystems.EventSystem>(true) != null);

                if (asset != null)
                {
                    Check("MainMenu_TitlePanelWired", asset.titlePanel != null);
                    Check("MainMenu_LevelPanelWired", asset.levelPanel != null);
                    Check("MainMenu_PlayButtonWired", asset.playButton != null);
                    Check("MainMenu_LevelSelectButtonWired", asset.levelSelectButton != null);
                    Check("MainMenu_QuitButtonWired", asset.quitButton != null);
                    Check("MainMenu_BackButtonWired", asset.backButton != null);
                    Check("MainMenu_SandboxRowWired", asset.sandboxRow != null && asset.sandboxRow.button != null);
                    Check("MainMenu_RegistryAssigned", asset.registry != null);

                    int expected = registry != null ? registry.Ordered().Length : -1;
                    Check("MainMenu_RowCountMatchesRegistry",
                        asset.rows != null && expected >= 0 && asset.rows.Length == expected,
                        "rows=" + (asset.rows != null ? asset.rows.Length : -1) + " registry=" + expected);
                }
            }

            // ---- the row count TRACKS the registry, it is not a constant -----------------------
            // Drive the real Refresh() with a synthetic three-level registry. A hardcoded list, or a
            // list that only ever shows what the prefab was built with, fails here.
            if (prefab != null)
            {
                var holder = new GameObject("FeatureTests_MainMenuProbe");
                holder.SetActive(false);                      // inactive parent: no Awake, no side effects
                var clone = Instantiate(prefab, holder.transform);
                var menu = clone.GetComponent<MainMenuController>();

                var fake = ScriptableObject.CreateInstance<LevelRegistry>();
                fake.initiallyUnlocked = 1;
                fake.levels = new LevelDefinition[3];
                for (int i = 0; i < 3; i++)
                {
                    var d = ScriptableObject.CreateInstance<LevelDefinition>();
                    d.levelId = "featuretest_fake_" + i;      // never a real id: no saved progress to inherit
                    d.displayName = "Fake Level " + i;
                    d.sceneName = "Level_01";
                    d.parTime = 100f + i;
                    d.orderIndex = i;
                    fake.levels[i] = d;
                }

                if (menu == null)
                {
                    Check("MainMenu_ProbeHasController", false);
                }
                else
                {
                    menu.registry = fake;
                    menu.Refresh();

                    Check("MainMenu_RowsGrowWithRegistry", menu.rows != null && menu.rows.Length >= 3,
                        "rows=" + (menu.rows != null ? menu.rows.Length : -1) + " (registry has 3)");
                    Check("MainMenu_AllRegistryLevelsVisible", menu.VisibleRowCount == 3,
                        "visible=" + menu.VisibleRowCount);

                    if (menu.rows != null && menu.rows.Length >= 3)
                    {
                        Check("MainMenu_RowShowsDisplayName",
                            menu.rows[1].title != null && menu.rows[1].title.text.Contains("FAKE LEVEL 1"),
                            "title=" + (menu.rows[1].title != null ? menu.rows[1].title.text : "null"));
                        Check("MainMenu_RowShowsPar",
                            menu.rows[0].meta != null && menu.rows[0].meta.text.Contains("PAR"),
                            "meta=" + (menu.rows[0].meta != null ? menu.rows[0].meta.text : "null"));
                        Check("MainMenu_RowShowsBest",
                            menu.rows[0].meta != null && menu.rows[0].meta.text.Contains("BEST"),
                            "meta=" + (menu.rows[0].meta != null ? menu.rows[0].meta.text : "null"));

                        // initiallyUnlocked = 1: the first is playable, the rest read as LOCKED rather
                        // than silently vanishing.
                        Check("MainMenu_FirstLevelUnlocked",
                            menu.rows[0].button != null && menu.rows[0].button.interactable);
                        Check("MainMenu_LaterLevelLocked",
                            menu.rows[2].button != null && !menu.rows[2].button.interactable);
                        Check("MainMenu_LockedRowSaysLocked",
                            menu.rows[2].status != null && menu.rows[2].status.text == "LOCKED",
                            "status=" + (menu.rows[2].status != null ? menu.rows[2].status.text : "null"));
                    }

                    Check("MainMenu_PlayTargetsFirstUnlocked", menu.PlayTargetSceneName == "Level_01",
                        "target='" + menu.PlayTargetSceneName + "'");
                }

                for (int i = 0; i < fake.levels.Length; i++) Destroy(fake.levels[i]);
                Destroy(fake);
                Destroy(holder);
            }
#else
            Skip("MainMenu_PrefabAssertions", "editor only (AssetDatabase)");
#endif

            // ---- a level entered from the menu still gets ghost racing --------------------------
            // GhostRacing bootstraps with [RuntimeInitializeOnLoadMethod(AfterSceneLoad)], which fires
            // ONCE per application start. Booting into MainMenu means that one shot lands on a scene
            // with no SpeedrunTimer, so it must also hook sceneLoaded or every level reached from the
            // menu silently has no recorder, no ghost and no leaderboard.
            Check("MainMenu_GhostRacingPresentInLevel", FindAnyObjectByType<GhostRacing>() != null,
                "no GhostRacing in the level scene");

            // ---- pause -> menu releases its time handle ----------------------------------------
            // A leaked 0-scale handle across a scene load is the classic "the next level starts
            // frozen" bug. PrepareForSceneChange is everything ReturnToMainMenu does before the load.
            var pause = FindAnyObjectByType<PauseMenu>();
            if (pause == null)
            {
                Check("MainMenu_PauseMenuPresent", false, "no PauseMenu in the level scene");
            }
            else
            {
                Check("MainMenu_PauseHasMainMenuButton", pause.mainMenuButton != null,
                    "a menu you cannot get back to is half a feature");

                pause.Open();
                yield return null;
                Check("MainMenu_PauseHoldsHandle", pause.TimeHandle >= 0, "handle=" + pause.TimeHandle);
                CheckApprox("MainMenu_PauseFreezesWorld", TimeScaleController.I.WorldScale, 0f, 0.01f);

                pause.PrepareForSceneChange();
                yield return null;
                Check("MainMenu_ReturnReleasesHandle", pause.TimeHandle < 0, "handle=" + pause.TimeHandle);
                Check("MainMenu_ReturnClosesPanel", !pause.IsOpen);
                CheckApprox("MainMenu_TimeRunningAfterReturn", TimeScaleController.I.WorldScale, 1f, 0.01f);
                Check("MainMenu_CursorUnlockedForMenu", Cursor.lockState == CursorLockMode.None,
                    "lockState=" + Cursor.lockState);

                // Put the level back the way the suite found it.
                if (GameManager.I != null) GameManager.I.SetState(GameState.Playing);
                yield return null;
            }
        }

        IEnumerator CheckBarTracks(string name, BarView bar, Action mutate, float expectedRatio)
        {
            if (bar == null) { Check(name, false, "bar not assigned on HUDController"); yield break; }
            mutate();
            yield return null;
            yield return null;
            CheckBarAnchor(name, bar, expectedRatio);
        }

        void CheckBarAnchor(string name, BarView bar, float expectedRatio)
        {
            if (bar == null) { Check(name, false, "bar null"); return; }
            if (bar.fill == null) { Check(name, false, "bar.fill null"); return; }
            float anchor = bar.fill.rectTransform.anchorMax.x;
            bool ok = Mathf.Abs(anchor - expectedRatio) <= 0.05f;
            Check(name, ok, $"anchorMax.x={anchor:0.###} expected={expectedRatio:0.###} value={bar.Value:0.###}");
        }

        // ================================================================ 14. AUDIO

        IEnumerator TestAudio()
        {
            var am = AudioManager.I;
            if (am == null) { Check("Audio_ManagerExists", false); yield break; }
            Check("Audio_ManagerExists", true);

            // The library is private; reflect it so we can prove every Sfx resolved to a clip
            // (either a real CC0 file under Resources/Audio/Sfx/<Name>/ or the synthesized fallback).
            var f = typeof(AudioManager).GetField("library", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { Skip("Audio_EverySfxHasAClip", "AudioManager.library not reachable by reflection"); }
            else
            {
                var lib = f.GetValue(am) as Dictionary<Sfx, AudioClip[]>;
                if (lib == null) Skip("Audio_EverySfxHasAClip", "library was null");
                else
                {
                    var missing = new List<string>();
                    foreach (Sfx s in Enum.GetValues(typeof(Sfx)))
                    {
                        if (s == Sfx.Drone) continue;   // music, not an SFX folder
                        if (!lib.TryGetValue(s, out var clips) || clips == null || clips.Length == 0 || clips[0] == null)
                            missing.Add(s.ToString());
                    }
                    Check("Audio_EverySfxHasAClip", missing.Count == 0,
                        missing.Count == 0 ? "all resolved" : "missing: " + string.Join(",", missing));
                }
            }

            // Music: one of the two crossfade sources should be playing.
            bool musicPlaying = false;
            foreach (var src in am.GetComponentsInChildren<AudioSource>())
                if (src.loop && src.isPlaying && src.clip != null) musicPlaying = true;
            Check("Audio_MusicPlaying", musicPlaying);

            // A one-shot must not throw.
            bool threw = false;
            try { AudioManager.Play(Sfx.Parry, 0.01f); }
            catch { threw = true; }
            Check("Audio_PlayOneShotDoesNotThrow", !threw);

            yield return null;
        }

        // ================================================================ LEVEL STRUCTURE
        //
        // Cheap asserts against a bad rebuild of Level_01. Everything here is authored in
        // Assets/Data/Levels/Level_01_Level.asset and built by LevelDefinitionBuilder, so a failure
        // here means the DATA or the BUILDER, never something a play session did.

        static readonly string[] LegendarySpawnNames =
        {
            "Spawn_Legendary_Ninja", "Spawn_Legendary_Knight", "Spawn_Legendary_Spellsword",
            // The fourth mini realm at the foot of the T4 descent (2026-09-13). Placeholder occupant.
            "Spawn_Legendary_V18Grappler"
        };

        static EnemySpawner FindSpawner(string spawnerName)
        {
            foreach (var s in FindObjectsByType<EnemySpawner>())
                if (s.name == spawnerName) return s;
            return null;
        }

        static EnemySpawner FindFinalBossSpawner()
        {
            foreach (var s in FindObjectsByType<EnemySpawner>())
                if (s.isBoss) return s;
            return null;
        }

        static BossArenaTrigger FindBossArena()
        {
            foreach (var a in FindObjectsByType<BossArenaTrigger>())
                if (a.clearSpawner == null) return a;
            return null;
        }

        IEnumerator TestLevelStructure()
        {
            if (LevelManager.I == null) { Check("Structure_LevelManagerExists", false); yield break; }
            Check("Structure_LevelManagerExists", true);

            // ---- four named checkpoints, one per section ---------------------------------------
            var checkpoints = new Dictionary<string, Checkpoint>();
            foreach (var c in FindObjectsByType<Checkpoint>()) checkpoints[c.name] = c;
            for (int i = 1; i <= 4; i++)
            {
                string n = "Checkpoint_" + i;
                Check("Structure_" + n, checkpoints.ContainsKey(n),
                    "present=" + string.Join(",", new List<string>(checkpoints.Keys).ToArray()));
            }

            // ---- F5 and the test menu's "Boss Arena" both go through Warp("Checkpoint_4") -------
            var bossArena = FindBossArena();
            Check("Structure_BossArenaExists", bossArena != null);
            var solarPortals = FindObjectsByType<SolarArenaPortal>();
            // Four mini realms (T1, T2, T3 and the T4 grappler realm) plus the Warden's.
            Check("Structure_FiveSolarArenaPortals", solarPortals.Length == 5,
                "count=" + solarPortals.Length);
            foreach (var portal in solarPortals)
            {
                Check("Structure_" + portal.name + "_Wired",
                    portal.arena != null && portal.realmEntry != null && portal.realmBoundsCenter != null &&
                    portal.GetComponent<SphereCollider>() != null && portal.GetComponent<SphereCollider>().isTrigger,
                    "arena=" + (portal.arena != null) + " entry=" + (portal.realmEntry != null));
                EnemySpawner realmSpawner = portal.IsFinalBossPortal
                    ? FindFinalBossSpawner()
                    : (portal.arena != null ? portal.arena.clearSpawner : null);
                // The enemy stands 6 m up-range of the cell's centre on a 30 m floor: anything within half
                // the floor radius is "in the realm"; anything further is the builder failing to move it.
                Check("Structure_" + portal.name + "_EnemyInRealm",
                    realmSpawner != null && portal.realmBoundsCenter != null &&
                    Vector3.Distance(realmSpawner.transform.position, portal.realmBoundsCenter.position) < 15f,
                    "spawner=" + (realmSpawner != null ? realmSpawner.name : "null"));
                Check("Structure_" + portal.name + "_ReturnContract",
                    portal.IsFinalBossPortal ? portal.realmExitRoot == null : portal.realmExitRoot != null);
                var realmFloor = portal.realmBoundsCenter != null ? portal.realmBoundsCenter.Find("Floor") : null;
                var floorCollider = realmFloor != null ? realmFloor.GetComponent<MeshCollider>() : null;
                Check("Structure_" + portal.name + "_FlatPhysicalFloor",
                    floorCollider != null && floorCollider.sharedMesh != null && floorCollider.bounds.size.y <= 1.01f,
                    "wide cylinder must use a thin disc collider, never a stretched capsule");
                var ceilingSpin = portal.realmBoundsCenter != null
                    ? portal.realmBoundsCenter.GetComponent<SolarArenaVisual>() : null;
                Check("Structure_" + portal.name + "_CeilingKeepsAdditiveGlow",
                    ceilingSpin != null && Mathf.Approximately(ceilingSpin.plasmaSurfaceOpacityOverride, 0f)
                    && Mathf.Approximately(ceilingSpin.plasmaOpacityOverride, 0.20f));
                Check("Structure_" + portal.name + "_CeilingCannotTiltIntoCombat",
                    ceilingSpin != null && ceilingSpin.plasma != null &&
                    Mathf.Abs(ceilingSpin.plasmaDegreesPerSecond.x) < 0.001f &&
                    Mathf.Abs(ceilingSpin.plasmaDegreesPerSecond.z) < 0.001f,
                    "solar disc rotates around its vertical axis only");
            }
            if (checkpoints.ContainsKey("Checkpoint_4") && bossArena != null)
            {
                LevelManager.I.Warp("Checkpoint_4");
                yield return null;
                Vector3 pp = combat.transform.position;
                Vector3 ap = bossArena.transform.position;
                float d = Vector3.Distance(pp, ap);
                // Landed on the boss APPROACH: near the arena mouth, on the near side of it, at the
                // boss tile's height. Anything else and F5 has quietly stopped working.
                Check("Structure_WarpCheckpoint4LandsAtBossApproach",
                    d < 30f && pp.z < ap.z && Mathf.Abs(pp.y - ap.y) < 8f,
                    $"player={pp} arena={ap} dist={d:0.0}");
                Check("Structure_WarpCheckpoint4SetsCheckpoint",
                    LevelManager.I.Current != null && LevelManager.I.Current.name == "Checkpoint_4",
                    "current=" + (LevelManager.I.Current != null ? LevelManager.I.Current.name : "null"));
            }

            // ---- the wand altar stands at the start, or the run has no loadout ------------------
            var pedestal = FindAnyObjectByType<WandPedestal>();
            Check("Structure_WandPedestalExists", pedestal != null);
            if (pedestal != null)
            {
                Vector3 start = LevelManager.I.startSpawn != null
                    ? LevelManager.I.startSpawn.position : Vector3.zero;
                float d = Vector3.Distance(pedestal.transform.position, start);
                Check("Structure_WandPedestalAtStart", d < 10f,
                    $"pedestal={pedestal.transform.position} start={start} dist={d:0.0}");
            }

            // ---- the three legendaries resolve to real prefabs ----------------------------------
            foreach (string n in LegendarySpawnNames)
            {
                var sp = FindSpawner(n);
                Check("Structure_" + n + "_Exists", sp != null);
                if (sp == null) continue;
                Check("Structure_" + n + "_HasPrefab", sp.prefab != null);
                Check("Structure_" + n + "_NotFlaggedBoss", !sp.isBoss);
                if (sp.prefab == null) continue;
                Check("Structure_" + n + "_PrefabIsAnEnemy",
                    sp.prefab.GetComponentInChildren<EnemyController>(true) != null,
                    "prefab=" + sp.prefab.name);
                // THE guard: a mini-boss wired as a BossController would raise BossDefeated, stop the
                // speedrun timer and end the run at the FIRST legendary - three times over before the
                // real boss. Deliberately not BossControllers; see ARCHITECTURE.md.
                Check("Structure_" + n + "_IsNotABossController",
                    sp.prefab.GetComponentInChildren<BossController>(true) == null,
                    "prefab=" + sp.prefab.name);
                Check("Structure_" + n + "_SpawnedAtLevelStart", sp.Instance != null);
                if (sp.Instance != null)
                    Check("Structure_" + n + "_InstanceIsNotABossController",
                        sp.Instance.GetComponentInChildren<BossController>(true) == null);
            }

            // ---- the kill plane sits below everything you can stand on --------------------------
            var kill = FindAnyObjectByType<KillZone>();
            Check("Structure_KillZoneExists", kill != null);
            if (kill != null)
            {
                var kc = kill.GetComponent<Collider>();
                float lowest = float.MaxValue;
                string lowestName = "none";
                foreach (var r in FindObjectsByType<MeshRenderer>())
                {
                    if (r.gameObject.layer != 0) continue;               // Sky and FX live elsewhere
                    if (r.transform.root.name != "Level") continue;      // built geometry only
                    if (r.bounds.min.y < lowest) { lowest = r.bounds.min.y; lowestName = r.name; }
                }
                if (kc == null || lowest == float.MaxValue)
                    Skip("Structure_KillPlaneBelowLowestPlatform", "no kill collider or no level geometry");
                else
                    Check("Structure_KillPlaneBelowLowestPlatform", kc.bounds.max.y < lowest,
                        $"killTop={kc.bounds.max.y:0.0} lowest={lowest:0.0} ({lowestName})");
            }

            // ---- REACHABILITY -------------------------------------------------------------------
            // Every ordinary hop the course asks for, measured off the BUILT geometry rather than off
            // the numbers in the definition, and checked against the documented base envelope. The first
            // three portal approaches are deliberately different: projectile parries are the run's core
            // verb, and the no-parry line must lose there. Those gaps are proved separately below.
            //
            // Envelopes are the documented contract in docs/AUTHORING.md, which is deliberately well
            // inside the measured capability (a held run-jump covers 8.8 m; the contract says 6).
            CheckHop("Base", "Ground_Start", "T1_Stone_1", 6f, 1f);
            CheckHop("Base", "T1_Stone_1", "T1_Stone_2", 6f, 1f);
            CheckHop("Base", "T1_Stone_2", "T1_Stone_3", 6f, 1f);
            CheckHop("Base", "T1_Stone_3", "T1_Stone_4", 6f, 1f);
            CheckHop("Base", "T1_Stone_4", "T1_Causeway", 6f, 1f);
            // T1_Causeway now launches into the first portal sun. The solar fixture measures that
            // curved trigger gap directly; after the realm, the return lands on supported T2_L1.
            CheckHop("Base", "T2_L1", "T2_L2", 6f, 1.5f);
            CheckHop("Base", "T2_L2", "T2_L3", 6f, 1.5f);
            CheckHop("Base", "T2_L3", "T2_L4", 6f, 1.5f);
            CheckHop("Base", "T2_L4", "T2_L5", 6f, 1.5f);
            CheckHop("Base", "T2_L5", "T2_L6", 6f, 1.5f);
            CheckHop("Base", "T2_L6", "T2_L7", 6f, 1.5f);
            CheckHop("Base", "T2_L7", "T2_L8", 6f, 1.5f);
            CheckHop("Base", "T2_L8", "T2_L9", 6f, 1.5f);
            CheckHop("Base", "T2_L9", "T2_L10", 6f, 1.5f);
            CheckHop("Base", "T2_L10", "T2_L11", 6f, 1.5f);
            // The T2 realm returns directly onto T3_Pillar_1; there is no exterior court/entry deck.
            CheckHop("Base", "T3_Pillar_1", "T3_Pillar_2", 6f, 1.5f);
            CheckHop("Base", "T3_Pillar_2", "T3_Pillar_3", 6f, 1.5f);
            CheckHop("Base", "T3_Pillar_3", "T3_Pillar_4", 6f, 1.5f);
            CheckHop("Base", "T3_Pillar_4", "T3_Span", 6f, 1.5f);
            CheckHop("Base", "T3_Span", "T3_Step_1", 6f, 1.5f);
            CheckHop("Base", "T3_Step_1", "T3_Step_2", 6f, 1.5f);
            // T3_Step_2 launches into the third portal; its return lands on supported T4_Entry.

            // The water shoulder is an expressive feeder, not the route's mandatory skill gate. Both
            // joins stay inside the base envelope; carrying water speed is what makes it faster.
            CheckHop("Base", "T1_Stone_1", "T1_Fast_1", 6f, 1f);
            CheckHop("Base", "T1_Fast_1", "T1_Stone_4", 6f, 1f);

            // The actual skill gates are the first three portal approaches. Each stops beyond even the
            // documented 8.5 m slide-jump envelope and inside the audited two-deflect carry band.
            CheckParryPortalGap("T1", "SolarCyan", "T1_Causeway", solarPortals);
            CheckParryPortalGap("T2", "SolarGold", "T2_L11", solarPortals);
            CheckParryPortalGap("T3", "SolarAzure", "T3_Step_2", solarPortals);

            // Prove the built scene retained the widened base route under the restored vertical layer.
            // These are the widest steering/breathing decks in each section.
            CheckOpenDeck("T1_Causeway", 13.9f, 12.7f);
            CheckOpenDeck("T2_L6", 17.9f, 9.9f);
            CheckOpenDeck("T3_Span", 15.9f, 13.9f);

            // Every portal run has two real projectile sources. The no-parry line loses at the gap, so
            // deleting or rebuilding either source as a melee-only enemy would make the level impossible.
            CheckProjectileRoute("T1", new[] { "Spawn_T1_GruntA", "Spawn_T1_GruntB" });
            CheckProjectileRoute("T2", new[] { "Spawn_T2_GruntB", "Spawn_T2_GruntA" });
            CheckProjectileRoute("T3", new[] { "Spawn_T3_Grunt", "Spawn_T3_Heavy" });

            // The hybrid pass keeps the wide steering decks and restores the silhouettes and secondary
            // movement lines around them. Missing one here means a rebuild silently flattened the route.
            // The slide-under bars (T1_Fallen_Obelisk, T3_Fallen_Lintel) were retired on 2026-09-13 and
            // must NOT be in the built scene any more.
            string[] restoredTraversal =
            {
                "T1_Rail_L", "T1_Rail_R", "T1_Obelisk_W",
                "T1_Wall_Start", "T1_Wall_Causeway", "T1_Wall_Landing",
                "T2_Tower", "T2_Buttress", "T2_Wall_East", "T2_Wall_Landing_East",
                "T2_Wall_West", "T2_Wall_Landing_West",
                "T3_Obelisk_W1", "T3_Obelisk_W2", "T3_Obelisk_E1", "T3_Obelisk_E2",
                "T3_Recovery_W1", "T3_Recovery_W2", "T3_Recovery_E1", "T3_Recovery_E2",
                "T3_Wall_Pillars", "T3_Wall_Landing_S", "T3_Wall_Span",
            };
            var missingTraversal = new List<string>();
            foreach (string n2 in restoredTraversal)
                if (GameObject.Find("Level/" + n2) == null) missingTraversal.Add(n2);
            Check("Reach_HybridTraversalIsRestored", missingTraversal.Count == 0,
                "missing=" + string.Join(",", missingTraversal.ToArray()));
            Check("Reach_SlideGatesAreRetired",
                GameObject.Find("Level/T1_Fallen_Obelisk") == null && GameObject.Find("Level/T3_Fallen_Lintel") == null,
                "a retired bar is still built: rerun 8a then 8");

            var challengeAnchors = FindObjectsByType<ChallengeRouteMarker>(FindObjectsInactive.Include);
            Check("Reach_ThreeChallengeAnchorsExist", challengeAnchors.Length == 3,
                "count=" + challengeAnchors.Length);
            foreach (var marker in challengeAnchors)
                Check("Reach_Challenge_" + marker.RouteId + "_IsInvisibleAndColliderless",
                    marker.GetComponentInChildren<Collider>(true) == null &&
                    marker.GetComponentInChildren<Renderer>(true) == null,
                    "a Challenge Route anchor must never become gameplay or presentation geometry");

            int arcBalloons = 0;
            foreach (var balloon in FindObjectsByType<Balloon>(FindObjectsInactive.Include))
                if (balloon.name.StartsWith("T3_Arc_")) arcBalloons++;
            Check("Reach_T3OptionalBalloonArcRestored", arcBalloons == 4,
                "count=" + arcBalloons);

            // Leave the run where the rest of the suite expects it.
            if (checkpoints.ContainsKey("Checkpoint_1")) LevelManager.I.Warp("Checkpoint_1");
            yield return null;
        }

        /// <summary>
        /// Edge-to-edge horizontal gap and rise between two built platforms, measured off their renderer
        /// bounds — the geometry that actually exists, not the numbers someone typed into the definition.
        /// Returns false if either is missing.
        /// </summary>
        static bool HopBetween(string fromName, string toName, out float gap, out float rise)
        {
            gap = rise = 0f;
            var a = GameObject.Find("Level/" + fromName);
            var b = GameObject.Find("Level/" + toName);
            if (a == null || b == null) return false;
            var ra = a.GetComponent<Renderer>();
            var rb = b.GetComponent<Renderer>();
            if (ra == null || rb == null) return false;
            Bounds ba = ra.bounds, bb = rb.bounds;
            // Closest approach in XZ. Overlapping footprints give 0, which is right: you step across.
            float dx = Mathf.Max(0f, Mathf.Max(ba.min.x - bb.max.x, bb.min.x - ba.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(ba.min.z - bb.max.z, bb.min.z - ba.max.z));
            gap = Mathf.Sqrt(dx * dx + dz * dz);
            rise = bb.max.y - ba.max.y;
            return true;
        }

        /// <summary>One authored hop, against the envelope of the moveset it is meant to need.</summary>
        void CheckHop(string moveset, string fromName, string toName, float maxGap, float maxRise)
        {
            float gap, rise;
            if (!HopBetween(fromName, toName, out gap, out rise))
            {
                Check($"Reach_{moveset}_{fromName}_to_{toName}", false, "one of the platforms is missing");
                return;
            }
            Check($"Reach_{moveset}_{fromName}_to_{toName}", gap <= maxGap && rise <= maxRise,
                $"gap={gap:0.00}m (max {maxGap:0.0})  rise={rise:0.00}m (max {maxRise:0.0})");
        }

        void CheckParryPortalGap(string section, string theme, string approachName,
                                 SolarArenaPortal[] portals)
        {
            var approach = GameObject.Find("Level/" + approachName);
            var renderer = approach != null ? approach.GetComponent<Renderer>() : null;
            SolarArenaPortal portal = null;
            foreach (var candidate in portals)
                if (candidate != null && candidate.ThemeKey == theme) { portal = candidate; break; }
            var sphere = portal != null ? portal.GetComponent<SphereCollider>() : null;
            if (renderer == null || sphere == null)
            {
                Check("Reach_ParryGap_" + section, false,
                    "approach=" + (renderer != null) + " portal=" + (portal != null) + " sphere=" + (sphere != null));
                return;
            }

            Bounds deck = renderer.bounds;
            Vector3 center = sphere.bounds.center;
            float radius = sphere.bounds.extents.z;
            float playerCenterY = deck.max.y + motor.StandHeight * 0.5f;
            float dy = Mathf.Abs(playerCenterY - center.y);
            float chord = dy < radius ? Mathf.Sqrt(radius * radius - dy * dy) : 0f;
            float gap = center.z - chord - deck.max.z;
            Check("Reach_ParryGap_" + section,
                dy < radius && gap > 8.5f && gap >= 13.25f && gap <= 15.75f,
                $"gap={gap:0.00}m slideMax=8.50 twoDeflectBand=13.25..15.75 dy={dy:0.00} radius={radius:0.00}");
        }

        void CheckOpenDeck(string name, float minWidth, float minDepth)
        {
            var go = GameObject.Find("Level/" + name);
            var renderer = go != null ? go.GetComponent<Renderer>() : null;
            Vector3 size = renderer != null ? renderer.bounds.size : Vector3.zero;
            Check("Reach_OpenDeck_" + name,
                renderer != null && size.x >= minWidth && size.z >= minDepth,
                $"size={size} required>={minWidth:0.0}x{minDepth:0.0}");
        }

        void CheckProjectileRoute(string section, string[] spawnerNames)
        {
            var missing = new List<string>();
            foreach (string spawnerName in spawnerNames)
            {
                var spawner = FindSpawner(spawnerName);
                if (spawner == null || spawner.prefab == null ||
                    spawner.prefab.GetComponentInChildren<ProjectileShooter>(true) == null)
                    missing.Add(spawnerName);
            }
            Check("Reach_ProjectileRoute_" + section, missing.Count == 0,
                "missingOrNotProjectile=" + string.Join(",", missing.ToArray()));
        }

        /// <summary>
        /// A hop that is SUPPOSED to be out of reach of the base moveset. Without this the "optional fast
        /// line" is just a shorter route everyone takes, and the scenic route it bypasses is dead content.
        /// </summary>
        void CheckHopIsGated(string fromName, string toName, float baseMaxGap)
        {
            float gap, rise;
            if (!HopBetween(fromName, toName, out gap, out rise))
            {
                Check($"Reach_Gated_{fromName}_to_{toName}", false, "one of the platforms is missing");
                return;
            }
            Check($"Reach_Gated_{fromName}_to_{toName}", gap > baseMaxGap,
                $"gap={gap:0.00}m must exceed the base envelope {baseMaxGap:0.0}m or the tech buys nothing");
        }

        // ================================================================ GATE LOOP
        //
        // The tile-to-tile progression, end to end, for every gated arena in the level. This is the
        // loop a player spends the whole run inside, and the one thing that - if broken - either walls
        // them in forever or hands them a straight line to the boss.

        static bool AtPos(Transform t, Vector3 target)
        {
            return t != null && Vector3.Distance(t.position, target) <= 0.05f;
        }

        /// <summary>Enter an arena the way the level does - a real collider entry, not a poked flag.</summary>
        IEnumerator EnterArena(BossArenaTrigger a)
        {
            if (a.solarPortal != null)
            {
                a.solarPortal.Enter(combat);
                yield return null;
                yield return WaitUntilOrTimeout(() => AtPos(a.gate, a.gateClosedPosition), 3f);
                yield break;
            }
            var col = a.GetComponent<Collider>();
            Vector3 p = col != null ? col.bounds.center : a.transform.position;
            motor.Teleport(p, 0f);
            yield return null;
            // Teleport toggles the CharacterController, so the entry registers as a fresh OnTriggerEnter.
            yield return WaitUntilOrTimeout(() => AtPos(a.gate, a.gateClosedPosition), 3f);
        }

        IEnumerator TestGateLoop()
        {
            if (LevelManager.I == null) { Check("Gate_LevelManagerExists", false); yield break; }
            Check("Gate_LevelManagerExists", true);

            var arenas = FindObjectsByType<BossArenaTrigger>();
            var mini = new List<BossArenaTrigger>();
            BossArenaTrigger bossArena = null;
            foreach (var a in arenas)
            {
                if (a.clearSpawner != null) mini.Add(a);
                else bossArena = a;
            }
            Check("Gate_FourMiniBossArenas", mini.Count == 4, "count=" + mini.Count);
            Check("Gate_OneBossArena", bossArena != null, "arenas=" + arenas.Length);

            // Nothing is open before the player has been anywhere. If this fails the run is a straight
            // line to the boss and every gate assert below is moot.
            foreach (var a in mini)
                Check("Gate_" + a.name + "_NotClearedBeforeEntry", !a.Cleared);

            // Respawns must land at the start, not inside the arena under test.
            foreach (var c in FindObjectsByType<Checkpoint>())
                if (c.name == "Checkpoint_1") { LevelManager.I.SetCheckpoint(c); break; }

            // This section tests gates, not fights: the legendaries are real enemies and would
            // otherwise spend it hitting the tester.
            health.Invulnerable = true;

            foreach (var a in mini) yield return ExerciseMiniBossArena(a);
            if (bossArena != null) yield return ExerciseBossArena(bossArena);

            health.Invulnerable = false;

            // Leave the level playable: every enemy back, every gate back at its rest position.
            LevelManager.I.ResetEnemies();
            yield return null;
        }

        /// <summary>The whole gate loop for one mini-boss arena, from a deliberately hostile start.</summary>
        IEnumerator ExerciseMiniBossArena(BossArenaTrigger a)
        {
            string tag = "Gate_" + a.name + "_";
            var sp = a.clearSpawner;

            // ---- fresh, and with NO keeper alive yet --------------------------------------------
            // This is the "seen alive" latch under test. LevelManager.SpawnAll() runs a frame after the
            // arenas wake, so an arena that reads "no instance" as "dead" unseals itself at level start.
            a.ResetArena();
            sp.Despawn();
            yield return null;

            Check(tag + "HasAnExitGate", a.exitGate != null);
            if (a.exitGate == null) yield break;
            Check(tag + "ExitGateRestsClosed", AtPos(a.exitGate, a.exitGateClosedPosition),
                "pos=" + a.exitGate.position + " expected=" + a.exitGateClosedPosition);
            Check(tag + "EntryGateRestsOpen", AtPos(a.gate, a.gateOpenPosition),
                "pos=" + (a.gate != null ? a.gate.position.ToString() : "null"));

            yield return EnterArena(a);
            Check(tag + "EntryGateSealsOnEntry", !waitTimedOut,
                "gate=" + (a.gate != null ? a.gate.position.ToString() : "null") +
                " expected=" + a.gateClosedPosition);

            for (int i = 0; i < 10; i++) yield return null;
            Check(tag + "DoesNotOpenBeforeKeeperEverSpawns", !a.Cleared, "cleared=" + a.Cleared);
            Check(tag + "ExitStaysClosedBeforeKeeperEverSpawns", AtPos(a.exitGate, a.exitGateClosedPosition),
                "pos=" + a.exitGate.position);

            // ---- the keeper arrives; the arena stays sealed --------------------------------------
            sp.Spawn();
            yield return null;
            yield return null;
            var keeper = sp.Instance;
            Check(tag + "KeeperSpawned", keeper != null);
            if (keeper == null) yield break;
            var ke = keeper.GetComponent<EnemyController>();
            if (ke != null) ke.aggroLocked = true;
            Check(tag + "SealedWhileKeeperAlive", !a.Cleared, "cleared=" + a.Cleared);

            // ---- kill the keeper: BOTH gates drop ------------------------------------------------
            var kh = keeper.GetComponentInChildren<Health>();
            Check(tag + "KeeperHasHealth", kh != null);
            if (kh == null) yield break;
            kh.TakeDamage(new DamageInfo { damage = 999999f });
            yield return null;
            // An enemy whose death is a stagger needs the deathblow, exactly as a player would land it.
            if (!kh.IsDead) kh.TakeDamage(new DamageInfo { damage = 999999f, isExecute = true });

            // A duo realm (2026-09-14) stays sealed until its partner dies too.
            if (a.partnerSpawner != null)
            {
                yield return WaitRealtime(0.3f);
                Check(tag + "DuoSealedWhilePartnerAlive", !a.Cleared, "cleared=" + a.Cleared);
                if (a.partnerSpawner.Instance == null) { a.partnerSpawner.Spawn(); yield return null; yield return null; }
                var partner = a.partnerSpawner.Instance;
                var ph = partner != null ? partner.GetComponentInChildren<Health>() : null;
                Check(tag + "DuoPartnerHasHealth", ph != null);
                if (ph != null)
                {
                    ph.TakeDamage(new DamageInfo { damage = 999999f });
                    yield return null;
                    if (!ph.IsDead) ph.TakeDamage(new DamageInfo { damage = 999999f, isExecute = true });
                }
            }

            yield return WaitUntilOrTimeout(() => a.Cleared, 3f);
            Check(tag + "ClearsWhenKeeperDies", !waitTimedOut, "cleared=" + a.Cleared);
            yield return WaitUntilOrTimeout(
                () => AtPos(a.exitGate, a.exitGateOpenPosition) && AtPos(a.gate, a.gateOpenPosition), 3f);
            Check(tag + "BothGatesDrop", !waitTimedOut,
                "exit=" + a.exitGate.position + "/" + a.exitGateOpenPosition +
                " entry=" + (a.gate != null ? a.gate.position.ToString() : "null") + "/" + a.gateOpenPosition);

            // ---- and stays open ------------------------------------------------------------------
            yield return WaitRealtime(0.6f);
            Check(tag + "StaysOpenAfterClearing",
                a.Cleared && AtPos(a.exitGate, a.exitGateOpenPosition) && AtPos(a.gate, a.gateOpenPosition),
                "cleared=" + a.Cleared + " exit=" + a.exitGate.position);

            if (a.solarPortal != null)
            {
                Check(tag + "RealmExitAppearsAfterClear", a.solarPortal.ExitAvailable);
                yield return WaitRealtime(0.3f);
                bool returned = a.solarPortal.Exit(combat);
                yield return null;
                Vector3 actualReturn = combat.transform.position;
                Vector3 authoredReturn = a.solarPortal.WorldReturnPosition;
                float horizontalError = Vector2.Distance(
                    new Vector2(actualReturn.x, actualReturn.z),
                    new Vector2(authoredReturn.x, authoredReturn.z));
                float verticalError = Mathf.Abs(actualReturn.y - authoredReturn.y);
                Check(tag + "RealmExitReturnsBeyondGate", returned &&
                    horizontalError < 0.05f && verticalError <= 0.3f,
                    "player=" + actualReturn + " return=" + authoredReturn +
                    " horizontalError=" + horizontalError + " verticalError=" + verticalError);
            }

            // ---- dying mid-tile re-seals the arena -----------------------------------------------
            LevelManager.I.Respawn();
            yield return null;
            Check(tag + "ResealsOnDeath", !a.Cleared, "cleared=" + a.Cleared);
            Check(tag + "ExitGateBackUpOnDeath", AtPos(a.exitGate, a.exitGateClosedPosition),
                "pos=" + a.exitGate.position + " expected=" + a.exitGateClosedPosition);
            Check(tag + "EntryGateBackDownOnDeath", AtPos(a.gate, a.gateOpenPosition),
                "pos=" + (a.gate != null ? a.gate.position.ToString() : "null"));
            Check(tag + "KeeperBackOnDeath", sp.Instance != null);
        }

        /// <summary>The boss arena is the other half of the same component: it seals, and never reopens.</summary>
        IEnumerator ExerciseBossArena(BossArenaTrigger a)
        {
            const string tag = "Gate_Boss_";
            Check(tag + "HasNoClearSpawner", a.clearSpawner == null);
            Check(tag + "HasNoExitGate", a.exitGate == null,
                a.exitGate != null ? "exitGate=" + a.exitGate.name : "null");

            var boss = FindAnyObjectByType<BossController>();
            Check(tag + "BossExists", boss != null);
            if (boss == null) yield break;
            Check(tag + "BossStartsAsleep", !boss.Activated, "activated=" + boss.Activated);

            a.ResetArena();
            yield return null;
            yield return EnterArena(a);
            Check(tag + "EntryGateSealsOnEntry", !waitTimedOut,
                "gate=" + (a.gate != null ? a.gate.position.ToString() : "null") +
                " expected=" + a.gateClosedPosition);
            Check(tag + "WakesTheBoss", boss.Activated, "activated=" + boss.Activated);

            // Removing the arena's occupant is exactly what opens a mini-boss arena. Here it must do
            // nothing at all. (Executing the boss for real would raise BossDefeated and stop the run
            // timer before the Boss section gets to assert on it, so the instance is removed instead.)
            foreach (var s in FindObjectsByType<EnemySpawner>())
                if (s.isBoss) s.Despawn();
            yield return WaitRealtime(0.8f);
            Check(tag + "NeverClears", !a.Cleared, "cleared=" + a.Cleared);
            Check(tag + "GateStaysSealed", AtPos(a.gate, a.gateClosedPosition),
                "gate=" + (a.gate != null ? a.gate.position.ToString() : "null") +
                " expected=" + a.gateClosedPosition);
        }

        // ================================================================ LEGENDARY MINI-BOSSES
        //
        // Two of the three now carry an IMPORTED body (Assets/Enemies/*.fbx) instead of primitives, so
        // these guard the two ways that swap can silently break a fight: a renderer that is not URP
        // (magenta, or no telegraph at all because _BaseColor is not a property the shader has), and a
        // null EnemyVisuals binding, which is invisible at build time and only shows up as a missing
        // telegraph mid-exchange. The rest guards TUNING: an imported body must not have moved a number.

        static EnemyData DataOf(GameObject prefab)
        {
            if (prefab == null) return null;
            var c = prefab.GetComponentInChildren<EnemyController>(true);
            return c != null ? c.data : null;
        }

        void CheckLegendaryBody(string tag, GameObject prefab)
        {
            if (prefab == null) { Check(tag + "_PrefabExists", false); return; }
            Check(tag + "_PrefabExists", true);

            var v = prefab.GetComponentInChildren<EnemyVisuals>(true);
            Check(tag + "_HasEnemyVisuals", v != null);
            if (v == null) return;

            // All seven bindings. EnemyVisuals null-guards each of them at runtime, which is exactly why
            // a null one is silent: the enemy simply stops telegraphing and nothing is logged.
            Check(tag + "_Visuals_body", v.body != null);
            Check(tag + "_Visuals_eye", v.eye != null);
            Check(tag + "_Visuals_weapon", v.weapon != null);
            Check(tag + "_Visuals_lungeRoot", v.lungeRoot != null);
            Check(tag + "_Visuals_armPivot", v.armPivot != null);
            Check(tag + "_Visuals_weaponPivot", v.weaponPivot != null);
            Check(tag + "_Visuals_alertMarker", v.alertMarker != null);
            Check(tag + "_Visuals_deathblowMarker", v.deathblowMarker != null);
            // The two markers must never be the same object: they sit in the same place on screen and
            // mean opposite things.
            Check(tag + "_AlertIsNotDeathblow", v.alertMarker != v.deathblowMarker);

            if (v.body != null)
            {
                var m = v.body.sharedMaterial;
                Check(tag + "_BodyHasMaterial", m != null);
                if (m != null)
                    Check(tag + "_BodyMaterialIsURP",
                        m.shader != null && m.shader.name.StartsWith("Universal Render Pipeline/"),
                        "shader=" + (m.shader != null ? m.shader.name : "null"));
            }
            // Physics belongs to the ROOT, never the imported art (model-swap contract rule 3).
            Check(tag + "_ColliderOnRoot", prefab.GetComponent<CapsuleCollider>() != null);
            Check(tag + "_AgentOnRoot", prefab.GetComponent<UnityEngine.AI.NavMeshAgent>() != null);
            Check(tag + "_HealthOnRoot", prefab.GetComponent<Health>() != null);
            Check(tag + "_PostureOnRoot", prefab.GetComponent<Posture>() != null);
            Check(tag + "_HasPostureBar", prefab.GetComponentInChildren<EnemyPostureBar>(true) != null);
            Check(tag + "_IsNotABossController", prefab.GetComponentInChildren<BossController>(true) == null);
        }

        /// <summary>Every wind-up in a moveset must clear the readability floor: the parry cue fires
        /// cueLead (0.28 s) before impact, so a wind-up under 0.45 s has no room to telegraph.</summary>
        void CheckMovesetWindups(string tag, EnemyData d)
        {
            if (d == null || d.combos == null) { Skip(tag + "_AllWindupsReadable", "no combos"); return; }
            float worst = 99f; string worstName = "none";
            foreach (var c in d.combos)
            {
                if (c == null || c.hits == null) continue;
                foreach (var h in c.hits)
                {
                    if (h == null) continue;
                    if (h.windup < worst) { worst = h.windup; worstName = h.attackName; }
                }
            }
            Check(tag + "_AllWindupsReadable", worst >= 0.45f,
                $"shortest={worstName} windup={worst:0.###}");
        }

        IEnumerator TestLegendaries()
        {
            var spellswordSpawner = FindSpawner("Spawn_Legendary_Spellsword");
            var knightSpawner = FindSpawner("Spawn_Legendary_Knight");
            GameObject spellsword = spellswordSpawner != null ? spellswordSpawner.prefab : null;
            GameObject knight = knightSpawner != null ? knightSpawner.prefab : null;

            // 2026-09-13 boss roster: Level_01's T2/T3 realms now seat the Cinder Judge and the Orbit Dancer
            // (LevelDefinitionAuthoring.SeatBossRoster); the Chorister and Penitent live on as Sandbox pads,
            // where this suite still pins their bodies and tuning.
            if ((spellsword != null && spellsword.name != "Legendary_Spellsword") ||
                (knight != null && knight.name != "Legendary_Knight"))
            {
                Skip("Legendaries_LegacyBodies", "this scene's realms seat the 2026-09-13 roster; run in Sandbox.unity");
                yield break;
            }

            CheckLegendaryBody("Legendary_Spellsword", spellsword);
            CheckLegendaryBody("Legendary_Knight", knight);

            // ---- THE ASHEN CHORISTER: body swap ONLY. These are the shipped numbers, and the whole
            // point of the swap was that not one of them moved. Hard rule 9 in assert form.
            var sd = DataOf(spellsword);
            if (sd == null) Skip("Chorister_TuningUnchanged", "no EnemyData on the prefab");
            else
            {
                CheckApprox("Chorister_Aggression", sd.aggression, 0.7f, 0.001f);
                CheckApprox("Chorister_PreferredRange", sd.preferredRange, 4.3f, 0.001f);
                CheckApprox("Chorister_AttackRange", sd.attackRange, 3f, 0.001f);
                CheckApprox("Chorister_Scale", sd.scale, 1.5f, 0.001f);
                CheckApprox("Chorister_MaxHP", sd.maxHP, 300f, 0.001f);
                CheckApprox("Chorister_MaxPosture", sd.maxPosture, 210f, 0.001f);
                CheckApprox("Chorister_ComboBreath", sd.comboBreathSeconds, 0.45f, 0.001f);
                CheckApprox("Chorister_StaggerSeconds", sd.staggerSeconds, 3.6f, 0.001f);
                Check("Chorister_PreferredRangeAtLeastAttackRange", sd.preferredRange >= sd.attackRange,
                    $"preferred={sd.preferredRange} attack={sd.attackRange}");
                Check("Chorister_MovesetName",
                    sd.moveset != null && sd.moveset.name == "Legendary_Spellsword_Moveset",
                    "moveset=" + (sd.moveset != null ? sd.moveset.name : "null"));
                CheckMovesetWindups("Chorister", sd);
            }

            // ---- THE IRON PENITENT: the spin. A cadence fight, so what has to hold is that every beat
            // is still readable, that the spin phrase exists at all, and that breaking him actually pays.
            var kd = DataOf(knight);
            if (kd == null) { Skip("Penitent_Spin", "no EnemyData on the prefab"); yield break; }

            CheckMovesetWindups("Penitent", kd);
            Check("Penitent_PreferredRangeAtLeastAttackRange", kd.preferredRange >= kd.attackRange,
                $"preferred={kd.preferredRange} attack={kd.attackRange}");

            // The spin phrase: spool-up, at least two beats on the metronome, then the exit.
            bool foundSpin = false, foundSteadyBeat = false;
            float longestRecovery = 0f;
            if (kd.combos != null)
            {
                foreach (var c in kd.combos)
                {
                    if (c == null || c.hits == null) continue;
                    int beats = 0; bool up = false, outHit = false;
                    foreach (var h in c.hits)
                    {
                        if (h == null) continue;
                        if (h.recovery > longestRecovery) longestRecovery = h.recovery;
                        if (h.attackName == "Knight_SpinUp") up = true;
                        else if (h.attackName == "Knight_SpinHit") beats++;
                        else if (h.attackName == "Knight_SpinOut") outHit = true;
                    }
                    if (up && outHit && beats >= 2) { foundSpin = true; if (beats >= 3) foundSteadyBeat = true; }
                }
            }
            Check("Penitent_HasSpinPhrase", foundSpin,
                "expected a combo of SpinUp + >=2 SpinHit + SpinOut");
            Check("Penitent_HasSustainedSpin", foundSteadyBeat,
                "expected at least one spin of >=3 beats, or the cadence is never long enough to learn");

            // comboBreathSeconds floors EVERY recovery, mid-combo ones included. If it is not well under
            // the beat there is dead air between the spin's hits and it stops being a cadence.
            Check("Penitent_BreathAllowsACadence", kd.comboBreathSeconds <= 0.25f,
                $"comboBreath={kd.comboBreathSeconds:0.###}");

            // THE REWARD. Breaking his posture has to open a window that dwarfs anything he gives you
            // for merely surviving a phrase, or the whole design is just a wall you outlast.
            Check("Penitent_StaggerBeatsEveryRecovery", kd.staggerSeconds > longestRecovery,
                $"stagger={kd.staggerSeconds:0.##} longestRecovery={longestRecovery:0.##}");
            Check("Penitent_StaggerIsABigOpening", kd.staggerSeconds >= longestRecovery * 2f,
                $"stagger={kd.staggerSeconds:0.##} longestRecovery={longestRecovery:0.##}");

            // The parry economy, spelled out so a retune that quietly makes him unbreakable fails here.
            // Only ParryResult.Perfect calls OnParried, so a block is worth ZERO enemy posture: these are
            // deflects, not survival. Sword parryPostureDamage is 25.
            var sword = weapons != null ? weapons.Current : null;
            if (sword == null) Skip("Penitent_BreaksInASaneNumberOfDeflects", "no weapon equipped");
            else
            {
                float perBeat = sword.parryPostureDamage * 1.3f;    // Knight_SpinHit multiplier
                int deflects = Mathf.CeilToInt(kd.maxPosture / Mathf.Max(0.01f, perBeat));
                Check("Penitent_BreaksInASaneNumberOfDeflects", deflects >= 5 && deflects <= 12,
                    $"deflects={deflects} (posture={kd.maxPosture} perBeat={perBeat:0.#} weapon={sword.name})");
            }

            yield return null;
        }

        // ================================================================ FLURRY BRAWLER V18 SMOKE
        //
        // This is deliberately a FILTERED Sandbox fixture, not another generic Legendary test.  V18 is
        // a comparison body on its own Sandbox pad and its unusual presentation has four failure modes
        // that asset arithmetic cannot catch: a normal hit interrupting the committed Clap, an answered
        // Clap losing its one touchdown accent, a travelling clip moving the body twice, and Combo2's
        // authored tail being replaced by recovery.  The real EnemyController still owns every attack;
        // reflection merely selects a known data attack so random moveset choice cannot make this smoke
        // test flaky.

        static readonly BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        EnemyAttackData V18Attack(EnemyController enemy, string attackName)
        {
            if (enemy == null || enemy.data == null || enemy.data.moveset == null ||
                enemy.data.moveset.entries == null) return null;
            foreach (var entry in enemy.data.moveset.entries)
            {
                if (entry == null || entry.combo == null || entry.combo.hits == null) continue;
                foreach (var hit in entry.combo.hits)
                    if (hit != null && hit.name == attackName) return hit;
            }
            return null;
        }

        bool BeginV18Windup(EnemyController enemy, EnemyAttackData attack)
        {
            var begin = typeof(EnemyController).GetMethod("BeginWindup", PrivateInstance);
            if (begin == null || enemy == null || attack == null) return false;
            begin.Invoke(enemy, new object[] { attack, 0f });
            return enemy.Current == EnemyController.State.Windup && enemy.CurrentAttack == attack;
        }

        static bool AnimatorShows(Animator animator, string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return false;
            return animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) ||
                   (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(stateName));
        }

        static int NearbyLiveRings(Vector3 point, float radius)
        {
            // SlashFx is pooled, so a hierarchy search misses retired effects and a name search would
            // confuse another pooled primitive.  The private Kind is inspection-only: V18 still emits
            // through the real public SlashFx.Ring path.
            var kind = typeof(SlashFx).GetField("kind", PrivateInstance);
            if (kind == null) return -1;
            int count = 0;
            float sqr = radius * radius;
            foreach (var fx in Resources.FindObjectsOfTypeAll<SlashFx>())
            {
                if (fx == null || !fx.gameObject.scene.IsValid() || !fx.gameObject.activeInHierarchy) continue;
                object value = kind.GetValue(fx);
                if (value == null || value.ToString() != "Ring") continue;
                if ((fx.transform.position - point).sqrMagnitude <= sqr) count++;
            }
            return count;
        }

        List<WorldEnemyState> IsolateWorldEnemiesExcept(EnemySpawner keepSpawner)
        {
            var state = new List<WorldEnemyState>();
            foreach (var spawner in FindObjectsByType<EnemySpawner>())
            {
                if (spawner == keepSpawner || spawner.Instance == null) continue;
                var instance = spawner.Instance;
                var controller = instance.GetComponent<EnemyController>();
                state.Add(new WorldEnemyState
                {
                    instance = instance,
                    controller = controller,
                    active = instance.activeSelf,
                    aggroLocked = controller != null && controller.aggroLocked
                });
                if (controller != null) controller.aggroLocked = true;
                instance.SetActive(false);
            }
            foreach (var bolt in FindObjectsByType<Projectile>())
                if (bolt != null) Destroy(bolt.gameObject);
            return state;
        }

        IEnumerator TestV18Smoke()
        {
            var spawner = FindSpawner("Spawn_Legendary_FlurryBrawlerV18");
            if (spawner == null || spawner.prefab == null)
            {
                Skip("V18_SandboxSpawner", "open Assets/Scenes/Sandbox.unity; V18 is a Sandbox-only fixture");
                yield break;
            }

            Check("V18_SandboxPrefab", spawner.prefab.name == "Legendary_FlurryBrawlerV18",
                "prefab=" + spawner.prefab.name);
            Vector3 playerStart = combat.transform.position;
            float playerYaw = look != null ? look.Yaw : combat.transform.eulerAngles.y;
            List<WorldEnemyState> quiet = null;
            Action<ParryResult> onParry = null;
            int blocked = 0, perfect = 0, comboHits = 0;

            try
            {
                quiet = IsolateWorldEnemiesExcept(spawner);
                spawner.Spawn();                         // real shipped Sandbox spawner, never a copied prefab
                yield return WaitUntilOrTimeout(() => spawner.Instance != null &&
                    spawner.Instance.GetComponent<EnemyController>() != null, 3f);
                var enemy = spawner.Instance != null ? spawner.Instance.GetComponent<EnemyController>() : null;
                var visuals = enemy != null ? enemy.GetComponentInChildren<FlurryBrawlerV18Visuals>(true) : null;
                Check("V18_Spawned", enemy != null && visuals != null);
                if (enemy == null || visuals == null) yield break;

                enemy.aggroLocked = true;
                yield return null;                       // EnemyController.Start binds Health/player/presentation
                var clap = V18Attack(enemy, "BrawlerV18_LevitateClap");
                var shoulder = V18Attack(enemy, "BrawlerV18_ShoulderCharge");
                var combo2 = V18Attack(enemy, "BrawlerV18_Combo2");
                Check("V18_AuthoredClap", clap != null);
                Check("V18_AuthoredShoulder", shoulder != null);
                Check("V18_AuthoredCombo2", combo2 != null);
                if (clap == null || shoulder == null || combo2 == null) yield break;

                onParry = result =>
                {
                    if (result == ParryResult.Blocked) blocked++;
                    else if (result == ParryResult.Perfect) perfect++;
                    else if (result == ParryResult.Hit) comboHits++;
                };
                GameEvents.ParryResolved += onParry;

                // Keep the target inside the actual cone/range. The brawler remains aggro-locked, so
                // only the deliberately reflected BeginWindup drives it.
                Vector3 forward = spawner.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                forward.Normalize();
                motor.Teleport(enemy.transform.position + forward * 2.2f,
                    Quaternion.LookRotation(-forward).eulerAngles.y);
                enemy.transform.rotation = Quaternion.LookRotation(forward);
                FacePoint(enemy.transform.position);

                // A normal player hit during a committed Clap is presentation-only. It must leave the
                // same data attack and the staged touchdown intact; then a held guard resolves exactly
                // the scheduled one contact and its one ring.
                int ringsBefore = NearbyLiveRings(enemy.transform.position + Vector3.up * 0.08f, 1f);
                float clapGround = visuals.lungeRoot != null ? visuals.lungeRoot.localPosition.y : 0f;
                Check("V18_ClapForce", BeginV18Windup(enemy, clap));
                yield return WaitRealtime(0.20f);
                enemy.Health.TakeDamage(new DamageInfo { damage = 1f, source = combat.gameObject });
                bool pendingAfterDamage = (bool?)typeof(FlurryBrawlerV18Visuals)
                    .GetField("clapImpactPending", PrivateInstance)?.GetValue(visuals) == true;
                Check("V18_ClapDamageKeepsCommit", enemy.IsCommitted && enemy.CurrentAttack == clap &&
                    enemy.NextImpactTime < float.MaxValue && pendingAfterDamage);
                parry.GuardHeld = true;
                yield return WaitUntilOrTimeout(() => blocked == 1 || enemy.Current == EnemyController.State.Recover,
                    4f);
                yield return null;
                int ringsAfterBlock = NearbyLiveRings(enemy.transform.position + Vector3.up * 0.08f, 1f);
                Check("V18_ClapBlockedOnce", blocked == 1, "resolved=" + blocked);
                Check("V18_ClapBlockedTouchdown", visuals.lungeRoot == null ||
                    Mathf.Abs(visuals.lungeRoot.localPosition.y - clapGround) <= 0.04f,
                    "y=" + (visuals.lungeRoot != null ? visuals.lungeRoot.localPosition.y.ToString("0.###") : "none"));
                if (ringsBefore >= 0 && ringsAfterBlock >= 0)
                    Check("V18_ClapBlockedOneRing", ringsAfterBlock == ringsBefore + 1,
                        "before=" + ringsBefore + " after=" + ringsAfterBlock);
                else Skip("V18_ClapBlockedOneRing", "SlashFx pool kind unavailable");
                parry.Cancel();
                parry.ReleaseGuardOverride();

                // A tap parry follows the identical real PlayerCombat path. ClearTelegraph/Recoil may
                // run before this visual's Update on the contact frame, so the preserved pending accent
                // is the observable contract rather than execution order. Let the preceding pooled ring
                // retire first: comparing simultaneous active totals across two contacts can read 1 -> 1
                // even when the second contact correctly rents a new ring as the first one returns.
                yield return WaitUntilOrTimeout(
                    () => NearbyLiveRings(enemy.transform.position + Vector3.up * 0.08f, 1f) == 0, 1f);
                int ringsBeforePerfect = NearbyLiveRings(enemy.transform.position + Vector3.up * 0.08f, 1f);
                Check("V18_ClapParryForce", BeginV18Windup(enemy, clap));
                yield return WaitUntilOrTimeout(() => enemy.Current == EnemyController.State.Strike &&
                    enemy.NextImpactTime - Time.time <= 0.06f, 4f);
                parry.GuardHeld = false;
                parry.StartParry();
                yield return WaitUntilOrTimeout(() => perfect == 1 || enemy.Current == EnemyController.State.Recover,
                    2f);
                yield return null;
                int ringsAfterPerfect = NearbyLiveRings(enemy.transform.position + Vector3.up * 0.08f, 1f);
                Check("V18_ClapPerfectOnce", perfect == 1, "resolved=" + perfect);
                Check("V18_ClapPerfectTouchdown", visuals.lungeRoot == null ||
                    Mathf.Abs(visuals.lungeRoot.localPosition.y - clapGround) <= 0.04f,
                    "y=" + (visuals.lungeRoot != null ? visuals.lungeRoot.localPosition.y.ToString("0.###") : "none"));
                if (ringsBeforePerfect >= 0 && ringsAfterPerfect >= 0)
                    Check("V18_ClapPerfectOneRing", ringsAfterPerfect == ringsBeforePerfect + 1,
                        "before=" + ringsBeforePerfect + " after=" + ringsAfterPerfect);
                else Skip("V18_ClapPerfectOneRing", "SlashFx pool kind unavailable");
                parry.Cancel();
                parry.ReleaseGuardOverride();

                // Shoulder owns one data lunge. Run is the readable approach, ShoulderCharge is the
                // contact beat, and collider/root travel must stay close to the authored 4.12 m rather
                // than animation root motion adding a second copy.
                motor.Teleport(enemy.transform.position + forward * 7f,
                    Quaternion.LookRotation(-forward).eulerAngles.y);
                enemy.transform.rotation = Quaternion.LookRotation(forward);
                FacePoint(enemy.transform.position);
                Vector3 shoulderStart = enemy.transform.position;
                Check("V18_ShoulderForce", BeginV18Windup(enemy, shoulder));
                yield return WaitRealtime(0.12f);
                Check("V18_ShoulderRunApproach", AnimatorShows(visuals.animator, visuals.clipRun));
                yield return WaitUntilOrTimeout(() => enemy.Current == EnemyController.State.Strike &&
                    enemy.NextImpactTime - Time.time <= 0.18f, 4f);
                Check("V18_ShoulderContactClip", AnimatorShows(visuals.animator, visuals.shoulderChargeClip));
                yield return WaitUntilOrTimeout(() => enemy.Current == EnemyController.State.Recover, 2f);
                float shoulderTravel = Vector3.ProjectOnPlane(enemy.transform.position - shoulderStart, Vector3.up).magnitude;
                Check("V18_ShoulderSingleTravel", shoulderTravel >= shoulder.lungeDistance * 0.70f &&
                    shoulderTravel <= shoulder.lungeDistance * 1.25f,
                    "travel=" + shoulderTravel.ToString("0.##") + " authored=" + shoulder.lungeDistance.ToString("0.##"));

                // Combo2 is a standalone one-contact attack. Its presentation remains on the contact
                // clip during the post-impact tail even though EnemyController has already entered
                // recovery, and only one actual PlayerCombat resolution may have occurred.
                motor.Teleport(enemy.transform.position + forward * 2.2f,
                    Quaternion.LookRotation(-forward).eulerAngles.y);
                enemy.transform.rotation = Quaternion.LookRotation(forward);
                FacePoint(enemy.transform.position);
                int comboBefore = comboHits;
                Check("V18_Combo2Force", BeginV18Windup(enemy, combo2));
                yield return WaitUntilOrTimeout(() => comboHits == comboBefore + 1 ||
                    enemy.Current == EnemyController.State.Recover, 4f);
                yield return null;
                Check("V18_Combo2SingleContact", comboHits == comboBefore + 1,
                    "hits=" + (comboHits - comboBefore));
                Check("V18_Combo2TailHeld", AnimatorShows(visuals.animator, visuals.comboClip));
            }
            finally
            {
                if (onParry != null) GameEvents.ParryResolved -= onParry;
                if (parry != null) { parry.Cancel(); parry.ReleaseGuardOverride(); }
                if (spawner != null) spawner.Despawn();
                RestoreWorldEnemies(quiet);
                if (motor != null) motor.Teleport(playerStart, playerYaw);
            }
        }

        /// <summary>
        /// Rewind the live parry press so the next Resolve() sees exactly <paramref name="elapsed"/>
        /// seconds of the window used. Test-only and deliberately narrow: it moves the clock the
        /// controller reads, not a single line of the logic it runs. Without it the late-block assert
        /// races the frame rate - see Parry_LateIsBlock.
        /// </summary>
        void BackdateParryPress(float elapsed)
        {
            var t = typeof(ParryController);
            var press = t.GetField("pressTime", BindingFlags.NonPublic | BindingFlags.Instance);
            var end = t.GetField("stateEnd", BindingFlags.NonPublic | BindingFlags.Instance);
            if (press == null || end == null) return;
            float at = Time.time - elapsed;
            press.SetValue(parry, at);
            end.SetValue(parry, at + parry.PerfectWindow + parry.LateWindow);
        }
    }
}
#endif
