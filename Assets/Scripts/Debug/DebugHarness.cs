using System.Collections;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Scripted play-mode scenarios for verifying the combat loop without input.
    /// Run from the editor console: VibeGame1.DebugHarness.Run("parry" | "boss" | "death").
    /// </summary>
    public class DebugHarness : MonoBehaviour
    {
        static DebugHarness inst;
        public static string Log = "";
        public static bool Done;

        static void L(string s)
        {
            Log += s + "\n";
            Debug.Log("[Harness] " + s);
        }

        public static void Run(string scenario)
        {
            if (inst == null)
            {
                var go = new GameObject("DebugHarness");
                inst = go.AddComponent<DebugHarness>();
            }
            Log = "";
            Done = false;
            inst.StopAllCoroutines();
            // The wand pedestal sits on the spawn point, so a scenario can start with its menu open
            // and time frozen at zero. Close it before scripting anything.
            WandSelectMenu.ForceClose();
            switch (scenario)
            {
                case "parry": inst.StartCoroutine(inst.Wrap(inst.ParryScenario())); break;
                case "boss": inst.StartCoroutine(inst.Wrap(inst.BossScenario())); break;
                case "death": inst.StartCoroutine(inst.Wrap(inst.DeathScenario())); break;
                default: L("unknown scenario " + scenario); Done = true; break;
            }
        }

        IEnumerator Wrap(IEnumerator co)
        {
            yield return co;
            L("SCENARIO DONE");
            Done = true;
        }

        PlayerCombat player;
        FirstPersonMotor motor;
        PlayerLook look;
        ParryController parry;
        ExecuteInteractor exec;
        PlayerResources res;

        void Bind()
        {
            player = FindAnyObjectByType<PlayerCombat>();
            motor = player.GetComponent<FirstPersonMotor>();
            look = player.GetComponent<PlayerLook>();
            parry = player.GetComponent<ParryController>();
            exec = player.GetComponent<ExecuteInteractor>();
            res = player.GetComponent<PlayerResources>();
        }

        void Face(Transform target)
        {
            Vector3 d = target.position - player.transform.position; d.y = 0f;
            if (d.sqrMagnitude < 0.001f) return;
            look.SetYaw(Quaternion.LookRotation(d).eulerAngles.y);
        }

        void TeleportNear(Transform target, float dist)
        {
            Vector3 d = player.transform.position - target.position; d.y = 0f;
            if (d.sqrMagnitude < 0.01f) d = Vector3.back;
            d.Normalize();
            Vector3 pos = target.position + d * dist;
            pos.y = target.position.y + 0.1f;
            motor.Teleport(pos, 0f);
            Face(target);
        }

        /// <summary>
        /// Exact spawner name first, then a suffix match.
        ///
        /// <para>The four-tile level rework renamed every spawner (<c>Spawn_GruntA</c> →
        /// <c>Spawn_T1_GruntA</c>) and the whole harness silently answered "no grunt A" and passed — a
        /// scripted run that finds nothing to fight reports success just as loudly as one that wins.
        /// The fallback keeps the harness alive across a level rename, which is a thing that will keep
        /// happening while levels are data.</para>
        /// </summary>
        static EnemyController FindSpawned(string spawnerName)
        {
            var all = FindObjectsByType<EnemySpawner>();
            foreach (var s in all)
                if (s.name == spawnerName && s.Instance != null) return s.Instance.GetComponent<EnemyController>();

            string suffix = spawnerName.StartsWith("Spawn_") ? spawnerName.Substring(6) : spawnerName;
            foreach (var s in all)
                if (s.Instance != null && s.name.EndsWith(suffix)) return s.Instance.GetComponent<EnemyController>();

            // Last resort: match on the ENEMY KIND alone. The campaign level renamed its spawners per
            // tile (Spawn_GruntA -> Spawn_T1_GruntA), which the suffix pass above handles, but the
            // sandbox names them by kind only (Spawn_Grunt), so "GruntA" matched nothing there and the
            // harness printed "no grunt A" and exited REPORTING SUCCESS. A scene-specific naming
            // dialect must degrade to finding the right kind of enemy, never to a silent no-op.
            string kind = suffix.TrimEnd('A', 'B', 'C', 'D');
            foreach (var s in all)
                if (s.Instance != null && s.name.IndexOf(kind, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return s.Instance.GetComponent<EnemyController>();
            return null;
        }

        /// <summary>
        /// A body that actually SWINGS. 2026-09-06: the campaign level's grunt spawners now hold
        /// pshooter_enemy01 — a sentry that never melees — so <see cref="FindSpawned"/> kept returning a
        /// body and the parry scenario sat through its whole timeout proving nothing. Prefer the named
        /// spawner when it is melee, else any spawned melee body, else build one from the level editor's
        /// prefab library (the melee Enemy_Grunt lives there even on a level that places none).
        /// </summary>
        EnemyController FindMelee(string preferredSpawner)
        {
            var e = FindSpawned(preferredSpawner);
            if (IsMelee(e)) return e;

            foreach (var s in FindObjectsByType<EnemySpawner>())
            {
                var c = s.Instance != null ? s.Instance.GetComponent<EnemyController>() : null;
                if (IsMelee(c) && !(c is BossController)) return c;
            }

            var pf = LevelEditor.I != null ? LevelEditor.I.PrefabFor("Enemy_Grunt") : null;
            if (pf == null) return null;
            Vector3 pos = player.transform.position + player.transform.forward * 5f;
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 8f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
            var go = Instantiate(pf, pos, Quaternion.identity);
            go.name = "~HarnessMelee";
            L("no melee enemy in this scene; spawned " + pf.name + " at " + pos);
            return go.GetComponent<EnemyController>();
        }

        static bool IsMelee(EnemyController e)
        {
            return e != null && e.IsAlive && (e.data == null || !e.data.rangedOnly);
        }

        IEnumerator FightAutoParry(EnemyController e, bool executeWhenStaggered, float timeout, bool cheatHeal)
        {
            float end = Time.unscaledTime + timeout;
            var last = e.Current;
            int perfect = 0, blocked = 0, hit = 0;
            System.Action<ParryResult> h = r =>
            {
                if (r == ParryResult.Perfect) perfect++;
                else if (r == ParryResult.Blocked) blocked++;
                else if (r == ParryResult.Hit) hit++;
                L($"  parry result {r}  (enemy posture {e.Posture.Current:F0}/{e.Posture.Max:F0}, hp {e.Health.Current:F0}, playerHP {player.Health.Current:F0}, attack {(e.CurrentAttack ? e.CurrentAttack.attackName : "?")})");
            };
            GameEvents.ParryResolved += h;
            while (Time.unscaledTime < end && e != null && e.IsAlive)
            {
                if (player.Health.IsDead) { L("  PLAYER DIED - aborting fight"); break; }
                Face(e.transform);
                if (e.Current == EnemyController.State.Strike && last != EnemyController.State.Strike)
                {
                    parry.StartParry();
                }
                if (e.IsStaggered && executeWhenStaggered && !exec.IsExecuting)
                {
                    yield return null;
                    bool ok = exec.TryExecute();
                    L($"  stagger -> execute {(ok ? "OK" : "FAILED (target=" + (exec.Target ? exec.Target.name : "null") + ")")}");
                    if (ok) yield return new WaitForSecondsRealtime(1.5f);
                }
                if (cheatHeal)
                {
                    if (player.Health.Current < 70f) player.Health.Heal(200f);
                    var pp = player.GetComponent<PlayerPosture>();
                    if (pp != null && pp.Current > 0f) pp.ResetFull();
                }
                if (e != null) last = e.Current;
                yield return null;
            }
            GameEvents.ParryResolved -= h;
            L($"fight done: perfect={perfect} blocked={blocked} hit={hit} enemyAlive={(e != null && e.IsAlive)} playerHP={player.Health.Current:F0} pyre={res.Pyre:F0}");
        }

        IEnumerator ParryScenario()
        {
            Bind();
            yield return null;
            var grunt = FindMelee("Spawn_GruntA");
            if (grunt == null) { L("no MELEE enemy to parry (the level's grunts are sentries; no editor library either)"); yield break; }
            int souls0 = SoulsWallet.I.Souls;
            TeleportNear(grunt.transform, 4f);
            L($"teleported near {grunt.name} at {player.transform.position}");
            yield return FightAutoParry(grunt, true, 30f, false);
            L($"souls {souls0} -> {SoulsWallet.I.Souls}");

            // now a heavy with a 2-hit combo
            var heavy = FindMelee("Spawn_Heavy");
            if (heavy != null)
            {
                TeleportNear(heavy.transform, 4f);
                L($"teleported near {heavy.name}");
                yield return FightAutoParry(heavy, true, 40f, true);
            }
        }

        IEnumerator BossScenario()
        {
            Bind();
            yield return null;
            LevelManager.I.Warp("Checkpoint_4");
            yield return null;
            var boss = FindAnyObjectByType<BossController>();
            if (boss == null) { L("no boss"); yield break; }
            var portal = SolarArenaPortal.FindFinalBossPortal();
            if (portal != null) portal.Enter(player);
            else
            {
                BossArenaTrigger trig = null;
                foreach (var candidate in FindObjectsByType<BossArenaTrigger>())
                    if (candidate.clearSpawner == null) { trig = candidate; break; }
                if (trig == null) { L("no boss arena trigger"); yield break; }
                motor.Teleport(trig.transform.position, 0f);
                trig.BeginFight(player);
            }
            yield return null;
            L($"boss activated={boss.Activated} segments={boss.SegmentsLeft} phase={boss.Phase} state={boss.Current}");
            int lastSeg = boss.SegmentsLeft;
            bool defeated = false;
            System.Action onDef = () => defeated = true;
            GameEvents.BossDefeated += onDef;
            float t0 = SpeedrunTimer.I != null ? SpeedrunTimer.I.Elapsed : 0f;
            float end = Time.unscaledTime + 240f;
            while (Time.unscaledTime < end && !defeated)
            {
                yield return FightAutoParry(boss, true, 30f, true);
                if (player.Health.IsDead) { L("player died in boss fight - stopping"); break; }
                if (boss.SegmentsLeft != lastSeg) { L($"*** segment removed -> {boss.SegmentsLeft} left, phase {boss.Phase}"); lastSeg = boss.SegmentsLeft; }
                if (boss == null || !boss.IsAlive) break;
            }
            GameEvents.BossDefeated -= onDef;
            L($"boss defeated={defeated} timerRunning={(SpeedrunTimer.I != null && SpeedrunTimer.I.Running)} finished={(SpeedrunTimer.I != null && SpeedrunTimer.I.Finished)} state={GameManager.I.State}");
        }

        IEnumerator DeathScenario()
        {
            Bind();
            yield return null;
            LevelManager.I.Warp("Checkpoint_1");
            yield return null;
            SoulsWallet.I.Add(123);
            var grunt = FindSpawned("Spawn_GruntA");
            Vector3 gruntPos = grunt != null ? grunt.transform.position : Vector3.zero;
            // damage grunt so we can verify it resets
            if (grunt != null) grunt.Health.TakeDamage(new DamageInfo { damage = 30f });
            int deaths = LevelManager.I.DeathCount;
            player.Health.TakeDamage(new DamageInfo { damage = 99999f });
            L($"killed player, state={GameManager.I.State}");
            float end = Time.unscaledTime + 5f;
            while (Time.unscaledTime < end && GameManager.I.State == GameState.Dead) yield return null;
            yield return null;
            var bs = FindAnyObjectByType<Bloodstain>();
            var grunt2 = FindSpawned("Spawn_GruntA");
            L($"respawned: state={GameManager.I.State} deaths={LevelManager.I.DeathCount} pos={player.transform.position} hp={player.Health.Current} flask={res.FlaskCharges} souls={SoulsWallet.I.Souls} bloodstain={(bs ? bs.amount.ToString() + "@" + bs.transform.position : "none")} gruntReset={(grunt2 != null && grunt2 != grunt && grunt2.Health.Current == grunt2.Health.Max)}");
            if (bs != null)
            {
                motor.Teleport(bs.transform.position + Vector3.up * 0.1f, 0f);
                bs.SendMessage("OnTriggerEnter", player.GetComponent<Collider>());
                yield return null;
                L($"picked up bloodstain -> souls={SoulsWallet.I.Souls}");
            }
        }
    }
}
