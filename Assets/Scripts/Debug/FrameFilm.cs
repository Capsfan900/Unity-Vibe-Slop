using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// TEMPORARY visual-verification helper: films the player camera EVERY GAME FRAME through a scripted
    /// deathblow, then writes the frames out afterwards.
    ///
    /// <para><b>Why this exists next to <c>ViewmodelCapture</c>.</b> That one shoots from
    /// <c>EditorApplication.update</c>, which ticks at roughly 8 Hz in the background — fine for posing a
    /// viewmodel, useless for a riposte. The whole beat is 0.6-1.0 s, so an editor-tick capture samples
    /// it about six times and lands on the commit and the corpse while missing the stab and the blast
    /// entirely, which is exactly what happened on the first pass.</para>
    ///
    /// <para><b>And why it buffers rather than writing as it goes.</b> Every beat in the riposte is on
    /// REALTIME waits (rule 1: hitstop must not freeze the player), so anything that slows the frame rate
    /// down does not slow the riposte down with it — it just means fewer frames across the same second.
    /// Encoding a PNG per frame costs enough to halve the sample count it is trying to raise. So frames
    /// go into memory at a modest resolution and are written once the beat is over.</para>
    /// </summary>
    public class FrameFilm : MonoBehaviour
    {
        public const int Width = 1024;
        public const int Height = 576;

        public static string Log = "";
        public static bool Done;

        static FrameFilm inst;

        string dir;
        string prefix;
        int cap;
        readonly List<Texture2D> buffer = new List<Texture2D>();
        readonly List<string> names = new List<string>();

        static void L(string s) { Log += s + " "; }

        public static string Status
        {
            get { return "done=" + Done + " frames=" + (inst != null ? inst.buffer.Count : 0) + " " + Log; }
        }

        /// <summary>Film one whole parry-to-riposte against the named enemy already in the level.</summary>
        public static void Run(string directory, string enemyName)
        {
            if (inst == null)
            {
                var go = new GameObject("FrameFilm");
                inst = go.AddComponent<FrameFilm>();
            }
            Log = "";
            Done = false;
            inst.dir = directory;
            inst.StopAllCoroutines();
            inst.buffer.Clear();
            inst.names.Clear();
            inst.cap = 0;
            Directory.CreateDirectory(directory);
            inst.StartCoroutine(inst.Scenario(enemyName));
        }

        /// <summary>
        /// Film the three ENTRY paths into the held guard, EVERY RENDERED FRAME, through the real
        /// runtime code - no hand-driven poses. The blend is 0.08 s, which is about five frames at
        /// 60 Hz; an editor-tick capture at ~8 Hz samples it less than once, and a hand-driven
        /// reproduction is contaminated by LateUpdate's idle drift the moment the coroutine is stopped.
        /// This is the only honest way to see the PATH rather than the endpoints.
        /// </summary>
        public static void RunGuardEntry(string directory)
        {
            if (inst == null)
            {
                var go = new GameObject("FrameFilm");
                inst = go.AddComponent<FrameFilm>();
            }
            Log = "";
            Done = false;
            inst.dir = directory;
            inst.StopAllCoroutines();
            inst.buffer.Clear();
            inst.names.Clear();
            inst.cap = 0;
            Directory.CreateDirectory(directory);
            inst.StartCoroutine(inst.GuardEntryScenario());
        }

        /// <summary>
        /// Film ONE REAL SWING per weapon, EVERY RENDERED FRAME, at the shipped attack speed.
        ///
        /// <para>The swing trail is open for the strike leg only — 0.044 s on the dagger, about three
        /// frames at 60 Hz — and <c>ViewmodelCapture.LoadoutTour</c> shoots from the ~8 Hz editor tick,
        /// so it samples that window less than once and would report "there is no trail". Anything that
        /// judges the arc, the follow-through hold or the wind-up easing has to be filmed from
        /// LateUpdate at the real frame rate. Buffers are flushed per weapon: four weapons' worth of
        /// 1024x576 frames at once is a quarter of a gigabyte.</para>
        /// </summary>
        public static void RunSwings(string directory)
        {
            if (inst == null)
            {
                var go = new GameObject("FrameFilm");
                inst = go.AddComponent<FrameFilm>();
            }
            Log = "";
            Done = false;
            inst.dir = directory;
            inst.StopAllCoroutines();
            inst.buffer.Clear();
            inst.names.Clear();
            inst.cap = 0;
            Directory.CreateDirectory(directory);
            inst.StartCoroutine(inst.SwingScenario());
        }

        /// <summary>
        /// Film every WIND-UP POSE in one enemy's moveset, from the player's eye, at a real fighting
        /// distance, in the real lighting.
        ///
        /// <para><b>Why measuring beats algebra here.</b> A <c>WindupPose</c> authors a shoulder Euler,
        /// but the hand pivot trails it by <c>weaponLag</c> and the whole body is rotated underneath both
        /// — so the blade's on-screen angle is the product of three rotations, not the one that was
        /// typed. Hand-computed "this points up" is routinely wrong by tens of degrees. The only honest
        /// way to tune a silhouette is to photograph it.</para>
        ///
        /// <para>Two frames are kept per attack: <c>_mid</c> at ~55% of the wind-up, and <c>_peak</c>
        /// immediately after <c>CueFlash</c>, which is where the arm hitches past full extension and
        /// FREEZES. The peak frame is the one the player's parry decision is actually made on.</para>
        /// </summary>
        public static void RunWindups(string directory, string enemyName, float distance)
        {
            if (inst == null)
            {
                var go = new GameObject("FrameFilm");
                inst = go.AddComponent<FrameFilm>();
            }
            Log = "";
            Done = false;
            inst.dir = directory;
            inst.StopAllCoroutines();
            inst.buffer.Clear();
            inst.names.Clear();
            inst.cap = 0;
            Directory.CreateDirectory(directory);
            inst.StartCoroutine(inst.WindupScenario(enemyName, distance));
        }

        IEnumerator WindupScenario(string enemyName, float distance)
        {
            var combat = FindAnyObjectByType<PlayerCombat>();
            if (combat == null) { L("noPlayer"); Done = true; yield break; }
            var motor = combat.GetComponent<FirstPersonMotor>();
            var look = combat.GetComponent<PlayerLook>();
            var health = combat.GetComponent<Health>();
            var parry = combat.GetComponent<ParryController>();

            WandSelectMenu.ForceClose();
            if (health != null) health.Invulnerable = true;
            if (parry != null) { parry.ReleaseGuardOverride(); parry.GuardHeld = false; }

            // Borrow a standing spot the level already proved navigable and flat (see Scenario), then
            // switch EVERY enemy off: a second body wandering into frame ruins a silhouette comparison.
            EnemyController host = null;
            float bestD = float.MaxValue;
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            {
                if (!e.IsAlive || !e.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(e.transform.position, combat.transform.position);
                if (d < bestD) { bestD = d; host = e; }
            }
            if (host == null) { L("noHostSpot"); Done = true; yield break; }
            Vector3 spot = host.transform.position;
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                e.gameObject.SetActive(false);

            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + enemyName + ".prefab");
#endif
            if (prefab == null) { L("noPrefab:" + enemyName); Done = true; yield break; }

            var model = Instantiate(prefab, spot, Quaternion.identity);
            model.name = "PoseModel";
            var victim = model.GetComponent<EnemyController>();
            if (victim == null) { L("noController"); Done = true; yield break; }
            yield return null;                       // Start() runs here: it is where Setup(data) binds
            yield return null;                       // the shipped body colour into the property block

            var pres = model.GetComponentInChildren<IEnemyPresentation>();
            if (pres == null || victim.data == null) { L("noVisuals"); Done = true; yield break; }

            // A photo shoot, not a fight: the brain and the feet are switched off so the body holds
            // still and the only thing moving is the pose being measured.
            victim.enabled = false;
            var agent = model.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            // And it must not TOUCH the player. A 2.2x boss's capsule overlaps the stand point, and the
            // depenetration shoves the camera out from under it between poses no matter how often the
            // player is re-parked. A prop does not need colliders.
            foreach (var col in model.GetComponentsInChildren<Collider>(true)) col.enabled = false;

            // Park the player square in front at the requested fighting distance and aim at the chest.
            Vector3 away = combat.transform.position - spot;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            away.Normalize();
            float scale = Mathf.Max(0.6f, victim.data.scale);
            Vector3 stand = spot + away * distance;
            stand.y = spot.y + 0.05f;
            motor.Teleport(stand, 0f);
            model.transform.rotation = Quaternion.LookRotation(-away);
            yield return null;
            motor.Teleport(stand, 0f);               // the first teleport can be re-grounded
            yield return null;

            // RE-PARK BEFORE EVERY POSE, not once at the top. A big body (the boss is 2.2x) overlaps
            // the stand point with its own collider and shoves the player off the mark over the seconds a
            // full moveset takes to film, so a run that logged dist=5.50 at staging photographed the
            // arena from twenty metres away. Every frame in this film is taken from the same spot.
            System.Action park = () =>
            {
                motor.Teleport(stand, 0f);
                look.SetYaw(Quaternion.LookRotation(-away).eulerAngles.y);
                Vector3 aimAt = spot + Vector3.up * (1.1f * scale);
                Vector3 eyeAt = look.Cam != null ? look.Cam.position : combat.transform.position;
                Vector3 d2 = aimAt - eyeAt;
                float flat2 = new Vector2(d2.x, d2.z).magnitude;
                if (flat2 > 0.01f) look.NudgeAim(0f, -Mathf.Atan2(d2.y, flat2) * Mathf.Rad2Deg);
            };
            park();
            yield return null;
            L("model=" + enemyName + " dist=" +
              Vector3.Distance(combat.transform.position, spot).ToString("0.00") + " scale=" + scale.ToString("0.0"));

            // Every DISTINCT attack in the moveset, in moveset order.
            var seen = new List<EnemyAttackData>();
            var combos = victim.data.ResolveCombos();
            if (combos != null)
                foreach (var c in combos)
                {
                    if (c == null || c.hits == null) continue;
                    foreach (var a in c.hits)
                        if (a != null && !seen.Contains(a)) seen.Add(a);
                }
            // A BOSS DOES NOT ATTACK OUT OF `combos`. It picks from its PHASE patterns; `combos` holds a
            // one-entry fallback used only before a phase is applied. Enumerating combos alone films one
            // of the boss's five wind-ups and silently reports the other four as not existing.
            var bossData = victim.data as BossData;
            if (bossData != null && bossData.phases != null)
                foreach (var ph in bossData.phases)
                {
                    if (ph == null || ph.patterns == null) continue;
                    foreach (var c in ph.patterns)
                    {
                        if (c == null || c.hits == null) continue;
                        foreach (var a in c.hits)
                            if (a != null && !seen.Contains(a)) seen.Add(a);
                    }
                }
            if (seen.Count == 0) { L("noAttacks"); Done = true; yield break; }

            for (int i = 0; i < seen.Count; i++)
            {
                var atk = seen[i];
                string tag = (i + 1).ToString("00") + "_" + atk.name;
                pres.ClearTelegraph();
                pres.Strike(0f, 0.12f);              // return the arm to rest so each pose starts equal
                yield return Wait(0.5f);
                park();
                yield return null;
                park();                              // the first teleport can be re-grounded

                pres.Telegraph(atk, atk.windup);
                yield return Wait(atk.windup * 0.55f);
                Film(tag + "_mid", 2);
                yield return Wait(Mathf.Max(0.05f, atk.windup * 0.45f - 0.03f));
                pres.CueFlash(atk.unblockable);      // the arm hitches past full extension and FREEZES
                yield return null;
                Film(tag + "_peak", 3);
                yield return Wait(0.30f);
                Flush();
                L(tag + ":wu=" + atk.windup.ToString("0.00") +
                  ":authored=" + (atk.windupPose != null && atk.windupPose.authored));
            }

            pres.ClearTelegraph();
            if (health != null) health.Invulnerable = false;
            Done = true;
            L("WINDUPS DONE");
        }

        IEnumerator SwingScenario()
        {
            var combat = FindAnyObjectByType<PlayerCombat>();
            if (combat == null) { L("noPlayer"); Done = true; yield break; }
            var vm = combat.GetComponentInChildren<WeaponViewmodel>(true);
            var wc = combat.GetComponent<WeaponController>();
            var parry = combat.GetComponent<ParryController>();
            if (vm == null || wc == null) { L("noViewmodel"); Done = true; yield break; }

            WandSelectMenu.ForceClose();
            if (parry != null) { parry.ReleaseGuardOverride(); parry.GuardHeld = false; }
            vm.EndGuard();

            string[] tag = { "1_sword", "2_hammer", "3_dagger", "4_devblade" };
            for (int i = 0; i < 4; i++)
            {
                wc.Equip(i);
                yield return Wait(0.6f);            // let the swap settle and the idle return finish
                var w = wc.Current;
                float dur = w != null ? w.attackDuration : 0.38f;
                float hit = w != null ? w.hitDelay : 0.12f;
                prefix = tag[i];
                // Enough frames for wind-up + strike + hold + recovery, plus the trail's dissolve.
                Film(tag[i], Mathf.RoundToInt((dur + 0.25f) * 60f));
                vm.PlayAttack(0, dur, hit);
                yield return Wait(dur + 0.45f);
                Flush();
                L(tag[i] + ":dur=" + dur.ToString("0.00") + ":hit=" + hit.ToString("0.00"));
            }
            Done = true;
        }

        IEnumerator GuardEntryScenario()
        {
            var combat = FindAnyObjectByType<PlayerCombat>();
            if (combat == null) { L("noPlayer"); Done = true; yield break; }
            var vm = combat.GetComponentInChildren<WeaponViewmodel>(true);
            var wc = combat.GetComponent<WeaponController>();
            var parry = combat.GetComponent<ParryController>();
            if (vm == null || wc == null || parry == null) { L("noViewmodel"); Done = true; yield break; }

            WandSelectMenu.ForceClose();
            parry.ReleaseGuardOverride();
            parry.GuardHeld = false;
            vm.EndGuard();
            wc.Equip(0);
            yield return Wait(0.6f);

            // A. IDLE -> GUARD, driven exactly as a button press drives it.
            Film("A_idle_to_guard", 14);
            parry.GuardHeld = true;
            parry.StartParry();                       // the real press path: must go straight to stance
            yield return Wait(0.6f);

            // B. MID-SWING -> GUARD. The case a real fight hits most often, and the one most likely to
            // look broken: the recovery leg of the arc has to land IN the stance, not in idle.
            Film("B_swing_to_guard", 40);
            parry.GuardHeld = false;
            vm.EndGuard();
            yield return Wait(0.25f);
            wc.TryAttack();
            yield return Wait(0.10f);
            parry.GuardHeld = true;                   // guard raised mid-arc
            yield return Wait(1.2f);

            // C. GUARD -> RELEASE.
            Film("C_release", 20);
            yield return Wait(0.15f);
            parry.GuardHeld = false;
            yield return Wait(0.8f);

            parry.ReleaseGuardOverride();
            Flush();
            Done = true;
        }

        void Film(string namePrefix, int frames) { prefix = namePrefix; cap = frames; }

        void LateUpdate()
        {
            if (cap <= 0) return;
            cap--;
            var cam = Camera.main;
            if (cam == null) return;

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 1;
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prevTarget;
            rt.Release();
            Destroy(rt);

            buffer.Add(tex);
            names.Add(prefix + "_" + buffer.Count.ToString("000") + ".png");
        }

        void Flush()
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                File.WriteAllBytes(Path.Combine(dir, names[i]), buffer[i].EncodeToPNG());
                Destroy(buffer[i]);
            }
            L("wrote=" + buffer.Count);
            buffer.Clear();
            names.Clear();
        }

        IEnumerator Wait(float s) { float e = Time.unscaledTime + s; while (Time.unscaledTime < e) yield return null; }

        IEnumerator Scenario(string enemyName)
        {
            var combat = FindAnyObjectByType<PlayerCombat>();
            if (combat == null) { L("noPlayer"); Done = true; yield break; }

            var motor = combat.GetComponent<FirstPersonMotor>();
            var look = combat.GetComponent<PlayerLook>();
            var exec = combat.GetComponent<ExecuteInteractor>();
            var wands = combat.GetComponent<WandController>();
            var lockOn = combat.GetComponent<LockOnController>();
            var health = combat.GetComponent<Health>();

            WandSelectMenu.ForceClose();
            if (wands != null) wands.ResetCooldown();
            if (health != null) health.Invulnerable = true;

            // ---- pick a stage, then put a FRESH body on it ---------------------------------------
            // Two lessons paid for here. A blind spawn in front of the player lands on whatever NavMesh
            // is nearest, which on a platformer course is routinely a different ledge — one whole run
            // photographed an empty platform. And an enemy the level has already been fighting cannot
            // be reliably staggered on demand: caught inside a committed combo it goes straight back to
            // Windup on the next frame, so the press comes out as an ordinary swing.
            //
            // So: borrow an existing enemy's standing spot (guaranteed navigable, flat and framed),
            // switch that enemy off, and instantiate a clean one of the requested kind there.
            EnemyController host = null;
            float bestD = float.MaxValue;
            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            {
                if (!e.IsAlive || !e.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(e.transform.position, combat.transform.position);
                if (d < bestD) { bestD = d; host = e; }
            }
            if (host == null) { L("noHostSpot"); Done = true; yield break; }
            Vector3 spot = host.transform.position;
            host.gameObject.SetActive(false);

            var prefab = Resources.Load<GameObject>("__none__");
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + enemyName + ".prefab");
#endif
            if (prefab == null) { L("noPrefab:" + enemyName); Done = true; yield break; }

            var go = Instantiate(prefab, spot, Quaternion.identity);
            go.name = "FilmVictim";
            EnemyController victim = go.GetComponent<EnemyController>();
            if (victim == null) { L("noController"); Done = true; yield break; }
            yield return null;      // let Start() run: it is where the posture events are wired
            yield return null;
            L("staged:" + enemyName + "@" + spot.ToString("F0"));

            // ---- 1. approach: stand where the step-in will park us --------------------------------
            Frame(combat, motor, look, exec, victim, 0.9f);
            yield return Wait(0.4f);
            Frame(combat, motor, look, exec, victim, 0.9f);   // again: the first teleport can be re-grounded
            yield return null;
            L("player@" + combat.transform.position.ToString("F1") +
              " dist=" + Vector3.Distance(combat.transform.position, victim.transform.position).ToString("0.00") +
              " active=" + victim.gameObject.activeInHierarchy);
            Film("01_approach", 3);
            yield return Wait(0.35f);

            // ---- 2. lock on, so the pale chest dot is up alongside the violet mark ----------------
            if (lockOn != null) lockOn.TryLockOn();
            Film("02_lockon", 4);
            yield return Wait(0.3f);

            // ---- 3. THE WHOLE BEAT, IN ONE UNBROKEN FILM ------------------------------------------
            // Break, pose settle, mark up, press, thrust, contact, blast, recovery, death — filmed as
            // one continuous run rather than as separate takes. Two reasons. Gaps between takes lose
            // exactly the frames that matter, and more importantly the stagger is on a REAL timer:
            // EnemyController leaves State.Staggered on its own schedule, so a leisurely capture with
            // waits between takes had the window close before the press every single time
            // (Posture.HoldStagger extends the posture, not the enemy's state machine).
            // ResetFull then Break rather than Add: Add is a no-op while already broken and its result
            // depends on where the bar happened to be, and a capture that silently does not break the
            // posture photographs a fight instead of a deathblow.
            // Break, then KEEP breaking until the state machine is actually in Staggered. A single
            // Break is not enough: an enemy caught mid-combo can be posture-broken and still come out
            // of a scheduled Windup/Strike, and a capture that presses at that moment films an ordinary
            // swing instead of a deathblow. HoldStagger then parks the window open for the whole film.
            float guard = Time.unscaledTime + 8f;
            while (!victim.IsStaggered && Time.unscaledTime < guard)
            {
                // ONLY between swings. An enemy caught inside a committed combo goes back to
                // Windup/Strike on the very next frame — HandleBroken's SetState(Staggered) is
                // overwritten by the attack that was already in flight — so breaking mid-combo
                // produces a broken posture on an enemy that is still swinging, and the press that
                // follows comes out as an ordinary attack. Waiting for a gap is the whole fix.
                if (victim.Current == EnemyController.State.Idle ||
                    victim.Current == EnemyController.State.Chase ||
                    victim.Current == EnemyController.State.Recover)
                {
                    victim.Posture.ResetFull();
                    victim.Posture.Break();
                    victim.Posture.HoldStagger(60f);
                }
                yield return null;
            }
            L("staggeredAt=" + victim.IsStaggered);
            Film("03_beat", 190);
            yield return Wait(0.45f);              // pose settles, mark comes up — all of it filmed

            Frame(combat, motor, look, exec, victim, 0.9f);
            yield return null;
            L("preExec target=" + (exec.Target != null ? exec.Target.name : "null") +
              " staggered=" + victim.IsStaggered + " dist=" +
              Vector3.Distance(combat.transform.position, victim.transform.position).ToString("0.00"));
            bool ok = exec.TryExecute();
            L("execute=" + ok);
            yield return Wait(4f);

            Film("06_after", 0);
            if (health != null) health.Invulnerable = false;
            Flush();
            Done = true;
            L("SCENARIO DONE");
        }

        /// <summary>Park the player at the shipped standoff for this body and aim at its mark.</summary>
        static void Frame(PlayerCombat combat, FirstPersonMotor motor, PlayerLook look,
                          ExecuteInteractor exec, EnemyController e, float extra)
        {
            if (e == null) return;
            float scale = e.data != null ? Mathf.Max(0.6f, e.data.scale) : 1f;
            Vector3 away = combat.transform.position - e.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            // The step-in only ever CLOSES the gap, and the press itself has to happen inside
            // ExecuteInteractor.range (3.5 m). On a 2.2x boss stabStandoff*scale is 4.84 m — further
            // than the press is legal from — so the real riposte distance for a big body is the RANGE,
            // not the standoff, and that is the tighter framing the pose has to survive.
            float standDist = Mathf.Min(exec.stabStandoff * scale + extra, exec.range - 0.4f);
            Vector3 stand = e.transform.position + away.normalized * standDist;
            stand.y = e.transform.position.y + 0.05f;
            motor.Teleport(stand, 0f);

            // Aim where ExecuteInteractor's own cone test looks — up * 0.8 * scale — not at the mark.
            // On a 2.2x boss the sternum is 3.2 m in the air and 1.8 m off the centre line, so aiming at
            // it points the camera ~50 degrees up, outside the 40-degree execute cone, and the press is
            // refused. A player looks at the body; so does this.
            Vector3 eye = look.Cam != null ? look.Cam.position : combat.transform.position;
            Vector3 aim = e.transform.position + Vector3.up * (0.8f * e.transform.localScale.y);
            Vector3 d = aim - combat.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f) look.SetYaw(Quaternion.LookRotation(d.normalized).eulerAngles.y);
            // SetYaw zeroes pitch, so tip onto the mark afterwards.
            eye = look.Cam != null ? look.Cam.position : combat.transform.position;
            Vector3 v = aim - eye;
            float flat = new Vector2(v.x, v.z).magnitude;
            if (flat > 0.01f) look.NudgeAim(0f, -Mathf.Atan2(v.y, flat) * Mathf.Rad2Deg);
        }
    }
}
