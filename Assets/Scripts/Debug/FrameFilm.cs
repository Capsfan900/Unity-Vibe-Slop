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
