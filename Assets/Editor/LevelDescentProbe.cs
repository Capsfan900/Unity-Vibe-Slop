using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>One real, hands-off slide through the shipped descent. Entry impulse is applied once;
    /// subsequent movement comes exclusively from the motor. Optional automatic parries use forecast
    /// information unavailable to a human, proving integration, never encounter fairness.</summary>
    public static class LevelDescentProbe
    {
        static FirstPersonMotor motor;
        static Health health;
        static ParryController parry;
        static PlayerCombat combat;
        static RampDef ramp;
        static readonly List<EnemySpawner> spawners = new List<EnemySpawner>();
        static readonly List<ProjectileShooter> shooters = new List<ProjectileShooter>();
        static readonly List<SurgeTurret> turrets = new List<SurgeTurret>();
        static readonly StringBuilder log = new StringBuilder();
        static bool running, withParries, launched, oldInvulnerable, reached, jumpTrial, jumpRequested;
        static float started, launchTime, nextSample, maxSpeed, worstDelta, endSlideZ;
        static int lastFrame, airborneFrames;
        static Vector3 returnPosition;
        static float returnYaw;
        static string result = "Not run";

        public static string Start(bool automaticParries, bool testJumpCancel = false)
        {
            if (running) return "Already running";
            if (!EditorApplication.isPlaying || GameManager.I == null || Time.timeScale != 1f)
                return "Requires a fresh, unpaused Play session";
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Level_01")
                return "Requires Level_01";
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            ramp = def.ramps.Single(r => r.name == "T4_Ramp_Descent");
            motor = UnityEngine.Object.FindAnyObjectByType<FirstPersonMotor>();
            health = motor.GetComponent<Health>(); parry = motor.GetComponent<ParryController>();
            combat = motor.GetComponent<PlayerCombat>();
            parry.Cancel();
            returnPosition = motor.transform.position; returnYaw = motor.transform.eulerAngles.y;
            oldInvulnerable = health.Invulnerable;
            // ReceiveAttack rejects invulnerable players before evaluating a parry.
            health.Invulnerable = !automaticParries;
            health.ResetFull();
            var surge = motor.GetComponent<ParrySurge>(); if (surge != null) surge.Clear();
            spawners.Clear(); shooters.Clear(); turrets.Clear();
            foreach (var sp in UnityEngine.Object.FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
            {
                if (!sp.name.StartsWith("Spawn_T4_Surge_")) continue;
                sp.Spawn(); spawners.Add(sp);
                var shooter = sp.Instance.GetComponent<ProjectileShooter>();
                shooter.enabled = automaticParries; shooters.Add(shooter);
                turrets.Add(sp.Instance.GetComponent<SurgeTurret>());
            }
            foreach (var bolt in UnityEngine.Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
                UnityEngine.Object.Destroy(bolt.gameObject);
            Vector3 entry = ramp.basePosition - ramp.Heading * 0.8f;
            entry.y = LevelDescentReport.SurfaceY(ramp, entry) + 0.3f;
            motor.Teleport(entry, ramp.yaw);
            var look = motor.GetComponent<PlayerLook>(); if (look != null) look.SetYaw(ramp.yaw);
            started = Time.unscaledTime; nextSample = 0f; maxSpeed = 0f; worstDelta = 0f;
            launched = reached = jumpRequested = false; jumpTrial = testJumpCancel;
            endSlideZ = float.NaN; lastFrame = -1; airborneFrames = 0;
            withParries = automaticParries; result = "Running"; log.Clear();
            log.AppendLine("Real motor descent; automaticParries=" + withParries + "; entry impulse applied ONCE.");
            running = true; EditorApplication.update += Tick;
            return result;
        }

        static void Tick()
        {
            if (!running) return;
            if (!EditorApplication.isPlaying || motor == null) { Finish("Interrupted"); return; }
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                float elapsed = Time.unscaledTime - started;
                if (InputReader.I != null && (InputReader.I.MoveAxis.sqrMagnitude > 0.001f || InputReader.I.JumpHeld))
                { Finish("INTERRUPTED: player input; this is not a movement failure"); return; }
                if (elapsed > 12f) { Finish("TIMEOUT"); return; }
                if (!launched)
                {
                    if (!motor.IsGrounded || elapsed < 0.4f) return;
                    motor.AddImpulse(ramp.Heading * motor.groundSpeed);
                    launched = motor.TrySlide(); launchTime = Time.unscaledTime;
                    if (!launched) Finish("FAIL: slide refused");
                    return;
                }
                float t = Time.unscaledTime - launchTime;
                float along = Vector3.Dot(motor.transform.position - ramp.basePosition, ramp.Heading);
                reached |= along >= ramp.run;
                maxSpeed = Mathf.Max(maxSpeed, motor.HorizontalSpeed);
                worstDelta = Mathf.Max(worstDelta, Time.unscaledDeltaTime);
                if (!motor.IsGrounded) airborneFrames++;
                if (!motor.IsSliding && float.IsNaN(endSlideZ)) endSlideZ = motor.transform.position.z;
                if (withParries && parry.Current == ParryController.State.Idle && !combat.IsStaggered &&
                    !combat.IsExecuting && !combat.IsDrinking && BoltRegistry.AnyImpactBefore(Time.time + 0.12f))
                    parry.StartParry();
                if (jumpTrial && !jumpRequested && along > ramp.run * 0.4f)
                {
                    jumpRequested = motor.TryJump();
                    if (!jumpRequested) { Finish("FAIL: jump refused"); return; }
                }
                if (jumpRequested && !motor.IsSliding)
                {
                    Finish(!motor.IsGrounded && motor.Velocity.y > 0f
                        ? "PASS: mid-ramp jump cancels slide and leaves ground"
                        : "FAIL: jump was pinned to slope");
                    return;
                }
                if (t >= nextSample)
                {
                    log.AppendLine(string.Format("t={0:0.00} pos={1} speed={2:0.00} slope={3:0.0} sliding={4} grounded={5} surge={6:0.00}",
                        t, motor.transform.position, motor.HorizontalSpeed, motor.GroundSlopeDeg,
                        motor.IsSliding, motor.IsGrounded, motor.SpeedMultiplier));
                    nextSample = t + 0.2f;
                }
                if (!motor.IsSliding && t > 0.1f)
                    Finish(reached ? "PASS: continuous slide reached run-out and ended" : "FAIL: slide ended before run-out");
            }
            catch (Exception ex) { Finish("ERROR: " + ex); }
        }

        static void Finish(string verdict)
        {
            EditorApplication.update -= Tick; running = false;
            // Getter fields survive Unity destruction; do not access transform on these references.
            int fired = shooters.Sum(s => ReferenceEquals(s, null) ? 0 : s.Fired);
            int grants = turrets.Sum(s => ReferenceEquals(s, null) ? 0 : s.SurgesGranted);
            bool everyTurret = turrets.Count == 3 && turrets.All(t => !ReferenceEquals(t, null) && t.SurgesGranted >= 1);
            if (withParries && !everyTurret && verdict.StartsWith("PASS"))
                verdict = "FAIL: slide completed but not all three turrets granted a surge";
            log.AppendLine(string.Format("{0}; maxSpeed={1:0.00}; worstFrame={2:0.000}; airborneFrames={3}; endSlideZ={4:0.00}; fired={5}; surgeGrants={6}",
                verdict, maxSpeed, worstDelta, airborneFrames, endSlideZ, fired, grants));
            log.AppendLine("Surges by turret: " + string.Join(",", turrets.Select(t => ReferenceEquals(t, null) ? "missing" : t.SurgesGranted.ToString()).ToArray()));
            log.AppendLine("Shots by turret: " + string.Join(",", shooters.Select(t => ReferenceEquals(t, null) ? "missing" : t.Fired.ToString()).ToArray()));
            result = log.ToString();
            if (EditorApplication.isPlaying && motor != null)
            {
                var surge = motor.GetComponent<ParrySurge>(); if (surge != null) surge.Clear();
                motor.Teleport(returnPosition, returnYaw);
                var look = motor.GetComponent<PlayerLook>(); if (look != null) look.SetYaw(returnYaw);
                health.Invulnerable = oldInvulnerable;
                parry.Cancel();
                foreach (var bolt in UnityEngine.Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
                    UnityEngine.Object.Destroy(bolt.gameObject);
                foreach (var sp in spawners) if (sp != null) sp.Spawn();
            }
        }

        public static string Poll() { return running ? "Running" : result; }
    }
}
