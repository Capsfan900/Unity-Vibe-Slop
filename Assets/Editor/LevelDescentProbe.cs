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
        static readonly HashSet<Projectile> recordedCueContacts = new HashSet<Projectile>();
        static bool running, withParries, launched, oldInvulnerable, reached, jumpTrial, jumpRequested;
        static float started, launchTime, nextSample, maxSpeed, worstDelta, endSlideZ;
        static int lastFrame, airborneFrames;
        static Vector3 returnPosition;
        static float returnYaw;
        static string result = "Not run";
        static bool opening;
        static float peakMultiplier;
        static int recordedGrants;
        static ProjectileVolleySequence openingSequence;
        static bool sequenceWasEnabled;

        /// <summary>The authored opening sequence must grant all five real turret deflects.</summary>
        public static string StartOpening(bool automaticParries)
        {
            return Start(automaticParries, false, true);
        }

        public static string Start(bool automaticParries, bool testJumpCancel = false, bool openingDescent = false)
        {
            if (running) return "Already running";
            if (!EditorApplication.isPlaying || GameManager.I == null || Time.timeScale != 1f)
                return "Requires a fresh, unpaused Play session";
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Level_01")
                return "Requires Level_01";
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            opening = openingDescent;
            peakMultiplier = 1f;
            recordedGrants = 0;
            openingSequence = opening ? UnityEngine.Object.FindAnyObjectByType<ProjectileVolleySequence>() : null;
            sequenceWasEnabled = openingSequence != null && openingSequence.enabled;
            if (openingSequence != null) openingSequence.enabled = automaticParries;
            ramp = def.ramps.Single(r => r.name == (opening ? "T0_Ramp_Descent" : "T4_Ramp_Descent"));
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
            foreach (var sp in UnityEngine.Object.FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None)
                         .Where(s => s.name.StartsWith(opening ? "Spawn_T0_Surge_" : "Spawn_T4_Surge_"))
                         .OrderBy(s => s.name))
            {
                sp.Spawn(); spawners.Add(sp);
                var shooter = sp.Instance.GetComponent<ProjectileShooter>();
                shooter.enabled = automaticParries; shooters.Add(shooter);
                turrets.Add(sp.Instance.GetComponent<SurgeTurret>());
            }
            if (openingSequence != null && automaticParries) openingSequence.Restart();
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
            recordedCueContacts.Clear();
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
                if (GameManager.I == null || GameManager.I.State == GameState.Paused)
                {
                    Finish("INTERRUPTED: stale or paused play session; not a movement failure");
                    return;
                }
                float elapsed = Time.unscaledTime - started;
                if (health != null && health.IsDead)
                {
                    Finish("FAIL: player died before the descent probe completed");
                    return;
                }
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
                peakMultiplier = Mathf.Max(peakMultiplier, motor.SpeedMultiplier);
                worstDelta = Mathf.Max(worstDelta, Time.unscaledDeltaTime);
                if (!motor.IsGrounded) airborneFrames++;
                if (!motor.IsSliding && float.IsNaN(endSlideZ)) endSlideZ = motor.transform.position.z;
                if (withParries && parry.Current == ParryController.State.Idle && !combat.IsStaggered &&
                    // Closing-rate ETA approximates a curved flight. Keep the automated input inside
                    // the perfect window with margin for that estimate and one editor update.
                    !combat.IsExecuting && !combat.IsDrinking && BoltRegistry.AnyImpactBefore(Time.time + 0.08f))
                    parry.StartParry();
                if (opening && withParries)
                {
                    foreach (var bolt in UnityEngine.Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
                        if (bolt.CueAt >= 0f && bolt.ArrivedAt >= 0f && recordedCueContacts.Add(bolt))
                            log.AppendLine(string.Format("Cue to contact: {0:0.000} s (world time)", bolt.ArrivedAt - bolt.CueAt));
                    int currentGrants = turrets.Sum(s => ReferenceEquals(s, null) ? 0 : s.SurgesGranted);
                    if (currentGrants != recordedGrants)
                    {
                        recordedGrants = currentGrants;
                        log.AppendLine(string.Format("Opening deflect {0}: t={1:0.000}, progress={2:0.00}/{3:0.00}, position={4}, multiplier={5:0.00}",
                            currentGrants, t, along, ramp.run, motor.transform.position, motor.SpeedMultiplier));
                        if (along < 0f || along > ramp.run)
                        {
                            Finish("FAIL: an opening deflect occurred outside the descent");
                            return;
                        }
                    }
                    if (reached && turrets.Count == 5 && turrets.All(s => !ReferenceEquals(s, null) && s.SurgesGranted >= 1))
                    {
                        Finish(peakMultiplier >= 1.599f ? "PASS: all five opening turrets parried on the slope; full speed boost; reached run-out"
                            : "FAIL: five parries did not sustain the full speed boost");
                        return;
                    }
                    if (reached)
                    {
                        Finish("FAIL: reached the run-out before all five deflects");
                        return;
                    }
                }
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
                if (!motor.IsSliding && t > 0.1f && !(opening && withParries && reached))
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
            int distinctGrants = turrets.Count(t => !ReferenceEquals(t, null) && t.SurgesGranted >= 1);
            int requiredGrants = opening ? 5 : 3;
            if (withParries && (turrets.Count != requiredGrants || distinctGrants < requiredGrants) && verdict.StartsWith("PASS"))
                verdict = "FAIL: slide completed but fewer than " + requiredGrants + " turrets granted a surge";
            log.AppendLine(string.Format("{0}; maxSpeed={1:0.00}; worstFrame={2:0.000}; airborneFrames={3}; endSlideZ={4:0.00}; fired={5}; surgeGrants={6}",
                verdict, maxSpeed, worstDelta, airborneFrames, endSlideZ, fired, grants));
            log.AppendLine("Surges by turret: " + string.Join(",", turrets.Select(t => ReferenceEquals(t, null) ? "missing" : t.SurgesGranted.ToString()).ToArray()));
            log.AppendLine("Shots by turret: " + string.Join(",", shooters.Select(t => ReferenceEquals(t, null) ? "missing" : t.Fired.ToString()).ToArray()));
            log.AppendLine("Peak movement multiplier: " + peakMultiplier.ToString("0.00"));
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
                if (openingSequence != null)
                {
                    openingSequence.enabled = sequenceWasEnabled;
                    openingSequence.Restart();
                }
            }
        }

        public static string Poll() { return running ? "Running" : result; }
    }
}
