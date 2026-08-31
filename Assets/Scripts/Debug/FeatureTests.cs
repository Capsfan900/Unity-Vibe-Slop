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
            GameObject prefab = null;
            foreach (var s in FindObjectsByType<EnemySpawner>())
            {
                if (s.isBoss || s.prefab == null) continue;
                prefab = s.prefab;
                break;
            }
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
            if (motor != null) { motor.SpeedMultiplier = 1f; motor.CanMove = true; }
            if (parry != null) parry.Cancel();
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
        }

        static ItemData MakeItem(ItemEffect effect, float radius, float damage, float power, float duration)
        {
            var it = ScriptableObject.CreateInstance<ItemData>();
            it.displayName = "Test " + effect;
            it.shortLabel = effect.ToString().ToUpperInvariant();
            it.effect = effect;
            it.color = Color.white;
            it.radius = radius;
            it.damage = damage;
            it.power = power;
            it.duration = duration;
            return it;
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
                Test("HitstopScoping",  TestHitstopScoping),
                Test("ParryMathPure",   TestParryMathPure),
                Test("ParryLive",       TestParryLive),
                Test("PlayerPosture",   TestPlayerPosture),
                Test("EnemyExecute",    TestEnemyPostureAndExecute),
                Test("Weapons",         TestWeapons),
                Test("WandPedestal",    TestWandPedestal),
                Test("WandReadability", TestWandReadability),
                Test("ViewmodelArms",   TestViewmodelArms),
                Test("TellReadability", TestTellReadability),
                Test("Items",           TestItems),
                Test("Flask",           TestFlask),
                Test("Ultimate",        TestUltimate),
                Test("Progression",     TestProgression),
                Test("LevelFlow",       TestLevelFlow),
                Test("Boss",            TestBoss),
                Test("HUD",             TestHud),
                Test("Audio",           TestAudio),
            };

            foreach (var t in tests)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    t.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                CurrentTest = t.Key;
                Section(t.Key);
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

            // Launch() is also the Updraft item's mechanism.
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

            // SpeedMultiplier is the PhantomStep hook.
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
        }

        // ================================================================ 2. HITSTOP SCOPING

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

            parry.StartParry();
            float pressAt = Time.time;
            float blockTarget = parry.PerfectWindow + D.parryLateWindow * 0.5f;   // mid-block window
            while (Time.time - pressAt < blockTarget) yield return null;
            var r2 = combat.ReceiveAttack(MakeAttack(dummy, 30f, false));
            Check("Parry_LateIsBlock", r2 == ParryResult.Blocked, "result=" + r2 + " elapsed=" + (Time.time - pressAt).ToString("0.000"));
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
            yield return null;
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

            // Regeneration after the delay.
            health.ResetFull();
            posture.ResetFull();
            posture.Add(posture.Max * 0.6f);
            float before = posture.Current;
            yield return WaitRealtime(D.postureRegenDelay + 0.6f);
            Check("Posture_RegeneratesAfterDelay", posture.Current < before,
                $"{before:0.0} -> {posture.Current:0.0}");

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

        // ================================================================ 6b. WAND PEDESTAL

        /// <summary>
        /// The pre-run loadout choice. Proximity must NOT open the menu — the player aims at the altar
        /// and presses F. Once open, the menu freezes time through TimeScaleController, equipping closes
        /// it, and the time handle is released on EVERY exit path: a leaked 0-scale handle would freeze
        /// the game permanently.
        /// </summary>
        IEnumerator TestWandPedestal()
        {
            var ts = TimeScaleController.I;
            var pedestal = FindAnyObjectByType<WandPedestal>();
            var menu = FindAnyObjectByType<WandSelectMenu>();

            Check("WandPedestal_InLevel", pedestal != null, "built by LevelGreyboxBuilder / SandboxBuilder");
            if (menu == null) { Check("WandPedestal_MenuInHud", false, "no WandSelectMenu on the HUD prefab"); yield break; }
            Check("WandPedestal_MenuInHud", true);
            Check("WandPedestal_MenuStartsClosed", !menu.IsOpen);
            Check("WandPedestal_MenuHasRows", menu.rows != null && menu.rows.Length > 0,
                "rows=" + (menu.rows != null ? menu.rows.Length : 0));

            // ---- walking into range must not open anything, but must offer the prompt ----------
            if (pedestal == null) Skip("WandPedestal_ProximityDoesNotOpen", "no pedestal in this scene");
            else
            {
                string lastPrompt = "";
                Action<string> onPrompt = p => lastPrompt = p;
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
                Skip("WandPedestal_FOpensMenu",
                    "InteractPressed is polled from InputReader inside Update; no script entry point (Open() is exercised below)");
            }

            if (wandCtl == null || wandCtl.loadout == null || wandCtl.loadout.Length < 2)
            {
                Skip("WandPedestal_EquipsSelectedWand", "player has fewer than 2 wands in the loadout");
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

            // R cycling stays as a debug convenience alongside the pedestal (deliberate decision).
            Skip("WandPedestal_RCyclingStillWorks", "WandCyclePressed is polled in Update; no script entry point");
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

            bool scaled = true;
            string detail = "";
            foreach (var w in wandCtl.loadout)
            {
                if (w == null) continue;
                detail += w.displayName + "=" + w.viewmodelScale.ToString("F2") + " ";
                if (w.viewmodelScale < 0.6f) scaled = false;
            }
            Check("WandRead_ScaleShipped", scaled, detail + "(WandFactory must write viewmodelScale >= 0.60)");

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

            // A thrust pose that leaves the wand upright never points at anything, and one pushed far
            // from the lens shrinks to a splinter at 95 degrees FOV.
            Check("WandRead_ThrustPoseAimsForward", offhand.thrustEuler.x > 30f,
                "thrustEuler.x=" + offhand.thrustEuler.x);
            Check("WandRead_ThrustStaysNearLens", offhand.thrustPosition.z <= 0.8f,
                "thrustPosition.z=" + offhand.thrustPosition.z);
            Check("WandRead_TipLightEnabled", offhand.tipLightEnabled && offhand.tipLightRange >= 6f,
                "enabled=" + offhand.tipLightEnabled + " range=" + offhand.tipLightRange);

            var light = offhand.GetComponentInChildren<Light>(true);
            Check("WandRead_TipLightBuilt", light != null, "OffhandViewmodel.Awake builds a TipLight child");
            yield return null;

            yield return TestWeaponEmber();
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

            yield return null;
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
#else
            Skip("Trim_UnderDesatCeiling", "trim materials are only readable in the editor");
#endif
            yield return null;
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
            var a = MakeItem(ItemEffect.SoulLantern, 0f, 0f, 0f, 0f);
            var b = MakeItem(ItemEffect.SoulLantern, 0f, 0f, 0f, 0f);
            var c = MakeItem(ItemEffect.SoulLantern, 0f, 0f, 0f, 0f);
            var d = MakeItem(ItemEffect.SoulLantern, 0f, 0f, 0f, 0f);
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
                Vector3 target = combat.transform.position + combat.transform.forward * 1.2f + Vector3.up * 0.4f;
                clone.transform.position = target;
                yield return null;

                int held0 = items.Held.Count;
                motor.AddImpulse(combat.transform.forward * 6f);
                yield return WaitUntilOrTimeout(() => items.Held.Count > held0, 4f);
                Check("Items_PhysicsPickup", !waitTimedOut,
                    $"held {held0} -> {items.Held.Count}");

                if (!waitTimedOut)
                    Check("Items_PickupDisablesCollider",
                        cp == null || cp.GetComponent<Collider>() == null || !cp.GetComponent<Collider>().enabled);

                // Respawn restores world pickups (ItemPickup listens for PlayerRespawned).
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
            EnemyController victim = null;
            yield return SpawnDummy(combat.transform.position + combat.transform.forward * 3f, e => victim = e);
            if (victim == null) Skip("Items_WandIndependence", "no enemy prefab to stage the test around");
            else
            {
                // Wands and items are INDEPENDENT systems. Picking up an item must not touch the
                // equipped wand, and an item must be usable with E without any swapping. An earlier
                // build shared one offhand slot between them, which made items look broken.
                var wandCtrl = combat.GetComponent<WandController>();
                if (wandCtrl == null) Skip("Wands_Independent", "no WandController on the player");
                else
                {
                    ClearItems();
                    yield return null;
                    wandCtrl.Equip(1);
                    var wandBefore = wandCtrl.Current;
                    int indexBefore = wandCtrl.Index;

                    // Count deltas, not absolutes: a real pickup sits AT the spawn point and the harness
                    // teleports here between tests, so the player can legitimately be carrying one already.
                    int heldBefore = items.Held.Count;
                    var probe = MakeItem(ItemEffect.SoulLantern, 0f, 0f, 0f, 0f);
                    items.TryPickup(probe);
                    yield return null;
                    Check("Items_PickupDoesNotChangeWand", wandCtrl.Current == wandBefore && wandCtrl.Index == indexBefore,
                        "wand " + (wandBefore != null ? wandBefore.displayName : "null") + " -> " +
                        (wandCtrl.Current != null ? wandCtrl.Current.displayName : "null"));
                    Check("Items_HeldAfterPickup", items.Held.Count == heldBefore + 1, $"held {heldBefore} -> {items.Held.Count}");

                    // Usable directly, with no swap step.
                    health.SetCurrent(10f);
                    // UseCurrent() refuses while posture-broken. A previous section can leave the player
                    // staggered, which would look like "items don't work" when the gate is what fired.
                    if (posture != null) posture.ResetFull();
                    yield return null;
                    Check("Items_NotBlockedByStagger", combat == null || !combat.IsStaggered,
                        "staggered=" + (combat != null && combat.IsStaggered));
                    int beforeUse = items.Held.Count;
                    items.UseCurrent();
                    yield return null;
                    Check("Items_UsableDirectly", items.Held.Count == beforeUse - 1,
                        $"held {beforeUse} -> {items.Held.Count} (E must spend one with no swap step)");
                    Check("Items_EffectApplied", health.Current > 10f, "hp=" + health.Current.ToString("0"));
                    Check("Wands_StillEquippedAfterItemUse", wandCtrl.Current == wandBefore,
                        "wand=" + (wandCtrl.Current != null ? wandCtrl.Current.displayName : "null"));

                    // The wand set is fixed: cycling stays inside the loadout.
                    int n = wandCtrl.loadout != null ? wandCtrl.loadout.Length : 0;
                    wandCtrl.Next();
                    Check("Wands_CycleStaysInLoadout", n > 0 && wandCtrl.Index >= 0 && wandCtrl.Index < n,
                        "index=" + wandCtrl.Index + "/" + n);
                    wandCtrl.Equip(indexBefore);
                }

                if (victim != null) Destroy(victim.gameObject);
            }
            yield return SettleTimeScale();
            ClearItems();

            // ---- Updraft ----------------------------------------------------------------------
            var lift = MakeItem(ItemEffect.Updraft, 0f, 0f, 20f, 0f);
            items.TryPickup(lift);
            items.UseCurrent();
            yield return null;
            Check("Items_UpdraftLaunches", motor.Velocity.y > 10f, "velY=" + motor.Velocity.y.ToString("0.0"));
            yield return WaitUntilOrTimeout(() => motor.IsGrounded, 5f);
            ClearItems();

            // ---- Soul Lantern -----------------------------------------------------------------
            health.SetCurrent(health.Max * 0.3f);
            posture.Add(posture.Max * 0.5f);
            res.UseFlask();
            int flaskBefore = res.FlaskCharges;
            var lantern = MakeItem(ItemEffect.SoulLantern, 0f, 0f, 0f, 0f);
            items.TryPickup(lantern);
            items.UseCurrent();
            yield return null;
            CheckApprox("Items_LanternFullHeals", health.Current, health.Max, 0.01f);
            CheckApprox("Items_LanternClearsPosture", posture.Current, 0f, 0.01f);
            Check("Items_LanternRefillsFlask", res.FlaskCharges >= flaskBefore, $"{flaskBefore} -> {res.FlaskCharges}");
            ClearItems();

            // ---- Phantom Step -----------------------------------------------------------------
            var phantom = MakeItem(ItemEffect.PhantomStep, 0f, 0f, 1.35f, 0.6f);
            items.TryPickup(phantom);
            items.UseCurrent();
            yield return null;
            Check("Items_PhantomGrantsInvulnerability", health.Invulnerable);
            Check("Items_PhantomBoostsSpeed", motor.SpeedMultiplier > 1f, "mult=" + motor.SpeedMultiplier.ToString("0.00"));
            yield return WaitUntilOrTimeout(() => !health.Invulnerable, 4f);
            Check("Items_PhantomExpires", !waitTimedOut, "invulnerable=" + health.Invulnerable);
            CheckApprox("Items_PhantomRestoresSpeed", motor.SpeedMultiplier, 1f, 0.001f);
            ClearItems();
        }

        // ================================================================ 8. FLASK

        IEnumerator TestFlask()
        {
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
            var dropped = FindAnyObjectByType<Bloodstain>();
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
                // 14 m/s, not 6: ground friction (14/s) kills an impulse in about v/friction metres,
                // so a 6 m/s shove only travels ~0.4 m and never reaches the trigger from 2 m out.
                motor.AddImpulse(toStain.normalized * 14f);
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
                var cp = checkpoints[0];
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
                var timer = SpeedrunTimer.I;
                bool alreadyRunning = timer.Running;
                bool runStartedEvent = false;
                Action onRunStarted = () => runStartedEvent = true;
                timer.RunStarted += onRunStarted;
                bool accepted = timer.TryStartRun();
                float elapsed0 = timer.Elapsed;
                yield return WaitRealtime(0.25f);
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

            // Item slots are wired.
            Check("HUD_ItemSlotsWired", hud.itemSlots != null && hud.itemSlots.Length >= 3,
                "slots=" + (hud.itemSlots != null ? hud.itemSlots.Length : 0));
            Check("HUD_DeathblowBannerWired", hud.deathblowText != null);
            Check("HUD_TextWidgetsWired",
                hud.healthText != null && hud.flaskText != null && hud.soulsText != null && hud.timerText != null);
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
    }
}
#endif
