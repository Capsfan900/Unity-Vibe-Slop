using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// TEMPORARY visual-verification helper: renders the player camera to a RenderTexture once per
    /// editor tick and writes numbered PNGs. Driving it from EditorApplication.update rather than from
    /// a coroutine means play-mode timing is untouched, so frames captured during a riposte are the
    /// frames the player would actually see.
    /// </summary>
    public static class ViewmodelCapture
    {
        static int frame, total;
        static string dir, prefix;
        static bool running;
        public static string Log = "";

        public static string Status { get { return "running=" + running + " frame=" + frame + "/" + total + " " + Log; } }

        public static void Burst(string directory, string namePrefix, int count)
        {
            Directory.CreateDirectory(directory);
            dir = directory; prefix = namePrefix; total = count; frame = 0; Log = "";
            if (!running) { EditorApplication.update += Tick; running = true; }
        }

        public static void Stop()
        {
            if (running) EditorApplication.update -= Tick;
            running = false;
        }

        static void Tick()
        {
            if (frame >= total) { Stop(); Log += " done"; return; }
            Shoot(Path.Combine(dir, prefix + frame.ToString("00") + ".png"));
            frame++;
        }

        // ---- scripted tour ------------------------------------------------------------------------
        // One call sets the whole sequence going on EditorApplication.update. Driving it from the editor
        // tick rather than from a chain of MCP calls matters: any script edit anywhere in the project
        // recompiles and DROPS PLAY MODE, so a capture split across several calls routinely photographs
        // an edit-mode scene with no weapon in it.

        class Step
        {
            public int wait;        // ticks to idle before this step
            public Action act;      // state change to trigger
            public string name;     // file prefix, null = no capture
            public int count = 1;   // consecutive ticks to capture
        }

        static List<Step> steps;
        static int stepIndex, waited, shotsTaken;
        static string tourDir;
        static bool touring;

        public static string TourStatus
        {
            get { return "touring=" + touring + " step=" + stepIndex + "/" + (steps != null ? steps.Count : 0) + " " + Log; }
        }

        public static void Tour(string directory)
        {
            Directory.CreateDirectory(directory);
            tourDir = directory;
            Log = "";
            steps = BuildTour();
            stepIndex = 0; waited = 0; shotsTaken = 0;
            if (!touring) { EditorApplication.update += TourTick; touring = true; }
        }

        public static void StopTour()
        {
            if (touring) EditorApplication.update -= TourTick;
            touring = false;
        }

        static void TourTick()
        {
            if (!EditorApplication.isPlaying) { Log += " LEFT-PLAY-MODE@" + stepIndex; StopTour(); return; }
            if (steps == null || stepIndex >= steps.Count) { Log += " done"; StopTour(); return; }

            Step s = steps[stepIndex];
            if (waited < s.wait) { waited++; return; }
            if (shotsTaken == 0 && s.act != null)
            {
                try { s.act(); }
                catch (Exception e) { Log += " ERR:" + e.Message; }
            }
            if (s.name != null)
            {
                Shoot(Path.Combine(tourDir, s.name + "_" + shotsTaken + ".png"));
                shotsTaken++;
                if (shotsTaken < s.count) return;
            }
            stepIndex++; waited = 0; shotsTaken = 0;
        }

        static Step S(int wait, Action act, string name, int count)
        {
            var s = new Step(); s.wait = wait; s.act = act; s.name = name; s.count = count; return s;
        }

        /// <summary>
        /// Every viewmodel state, in one pass. Each state is captured over several consecutive ticks
        /// because an editor tick is not exactly a game frame — a single shot lands wherever it lands.
        /// </summary>
        static List<Step> BuildTour()
        {
            var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
            var off = UnityEngine.Object.FindAnyObjectByType<VibeGame1.OffhandViewmodel>();
            var wc = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponController>();
            float dur = wc != null && wc.Current != null ? wc.Current.attackDuration : 0.38f;
            float hit = wc != null && wc.Current != null ? wc.Current.hitDelay : 0.12f;

            var list = new List<Step>();
            list.Add(S(10, null, "01_idle", 1));
            list.Add(S(2, delegate { vm.PlayAttack(0, dur, hit); }, "02_combo0", 8));
            list.Add(S(18, delegate { vm.PlayAttack(1, dur, hit); }, "03_combo1", 8));
            list.Add(S(18, delegate { vm.PlayAttack(2, dur, hit); }, "04_combo2", 8));
            list.Add(S(20, delegate { vm.PlayParry(0.3f); }, "05_parry", 4));
            list.Add(S(8, delegate { vm.EndParry(); }, null, 1));
            list.Add(S(10, delegate { vm.PlayDrink(1.2f); }, null, 1));
            list.Add(S(14, null, "06_drink", 4));
            list.Add(S(40, delegate { vm.PlayExecute(1.2f); }, null, 1));
            list.Add(S(16, null, "07_execute_windup", 3));
            list.Add(S(16, null, "08_execute_slam", 4));
            list.Add(S(30, delegate { if (off != null) { off.Raise(true); off.PlayCharge(0.9f, Color.white); } }, null, 1));
            list.Add(S(12, null, "09_wand_raise", 3));
            list.Add(S(20, delegate { if (off != null) off.PlayThrust(0.5f, 0.25f, 0.4f); }, null, 1));
            list.Add(S(8, null, "10_wand_cocked", 3));
            list.Add(S(16, null, "11_wand_thrust", 4));
            list.Add(S(30, delegate { if (off != null) off.Raise(false); }, "12_settle", 1));
            return list;
        }

        /// <summary>
        /// Walk the WHOLE LOADOUT in one editor-driven pass: for each weapon slot, equip it, let a frame
        /// pass (the previous viewmodel is destroyed at END of frame, so equipping and shooting in the
        /// same frame photographs TWO weapons stacked on each other), shoot the idle, then play the
        /// swing and burst across it. Written for the "are these four weapons still distinguishable"
        /// question, which needs the same camera, the same lighting and the same frame budget for all
        /// four or the comparison is worthless.
        /// </summary>
        /// <param name="slowFactor">Playback stretch for the swing only. The editor tick is ~8 Hz and the
        /// dagger's swing is 0.22 s, so at 1x a burst samples it twice. The pose path is a normalised
        /// lerp, so a stretched swing walks the same poses — it just gets photographed more often.</param>
        public static void LoadoutTour(string directory, float slowFactor = 3f)
        {
            Directory.CreateDirectory(directory);
            tourDir = directory;
            Log = "";
            steps = BuildLoadoutTour(slowFactor);
            stepIndex = 0; waited = 0; shotsTaken = 0;
            if (!touring) { EditorApplication.update += TourTick; touring = true; }
        }

        static List<Step> BuildLoadoutTour(float slow)
        {
            var list = new List<Step>();
            string[] tag = { "1_sword", "2_hammer", "3_dagger", "4_devblade" };
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                list.Add(S(4, delegate
                {
                    var wc = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponController>();
                    if (wc != null) wc.Equip(slot);
                }, null, 1));
                list.Add(S(4, null, tag[i] + "_idle", 2));
                list.Add(S(2, delegate
                {
                    var wc = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponController>();
                    var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                    if (wc == null || vm == null || wc.Current == null) return;
                    vm.PlayAttack(0, wc.Current.attackDuration * slow, wc.Current.hitDelay * slow);
                }, tag[i] + "_swing", 8));
                list.Add(S(6, null, null, 1));
            }
            return list;
        }

        // ---- guard stance tour ---------------------------------------------------------------------

        /// <summary>
        /// The HELD guard, on all four weapons, plus a guarded impact. Same construction rule as
        /// <see cref="LoadoutTour"/> and for the same reason: equip, LET A FRAME PASS (the previous
        /// model is destroyed at end of frame, so equipping and shooting together photographs two
        /// weapons stacked), then raise the stance and shoot it.
        ///
        /// <para>The question this answers is not "does it look nice" but "can I still see the fight" —
        /// a stance is held for SECONDS, so anything of it over the crosshair occludes the enemy for a
        /// whole exchange. Judge the frames against the idle shots, not against each other.</para>
        /// </summary>
        public static void GuardTour(string directory)
        {
            Directory.CreateDirectory(directory);
            tourDir = directory;
            Log = "";
            steps = BuildGuardTour();
            stepIndex = 0; waited = 0; shotsTaken = 0;
            if (!touring) { EditorApplication.update += TourTick; touring = true; }
        }

        static List<Step> BuildGuardTour()
        {
            var list = new List<Step>();
            string[] tag = { "1_sword", "2_hammer", "3_dagger", "4_devblade" };
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                string name = tag[i];
                list.Add(S(4, delegate
                {
                    var vm0 = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                    if (vm0 != null) vm0.EndGuard();
                    var wc = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponController>();
                    if (wc != null) wc.Equip(slot);
                }, null, 1));
                list.Add(S(6, null, name + "_a_idle", 1));
                list.Add(S(2, delegate
                {
                    var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                    if (vm != null) vm.PlayGuard();
                }, null, 1));
                list.Add(S(5, null, name + "_b_guard", 2));
                list.Add(S(1, delegate
                {
                    var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                    if (vm != null) vm.GuardImpact();
                }, name + "_c_impact", 4));
                list.Add(S(6, null, name + "_d_recovered", 1));
                list.Add(S(2, delegate
                {
                    var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                    if (vm != null) vm.EndGuard();
                }, name + "_e_release", 3));
                list.Add(S(6, null, null, 1));
            }
            return list;
        }

        /// <summary>
        /// The three ENTRY paths into the stance, filmed densely enough to see the path and not just the
        /// endpoints: idle to guard, mid-swing to guard, and guard to release. The question is not "is
        /// the end pose right" but "does the blade travel through anywhere it should not" - if any frame
        /// shows the weapon extended FORWARD, or further from the body than both the start and the end
        /// pose, the motion is still routing through a waypoint.
        ///
        /// <para>The blend is 0.08 s and the editor tick is ~8 Hz, so the rise is stretched by driving
        /// the pose manually rather than by calling PlayGuard and hoping to catch it: each step here
        /// nudges the model one fraction of the way and shoots it.</para>
        /// </summary>
        public static void GuardEntryTour(string directory)
        {
            Directory.CreateDirectory(directory);
            tourDir = directory;
            Log = "";
            steps = BuildGuardEntryTour();
            stepIndex = 0; waited = 0; shotsTaken = 0;
            if (!touring) { EditorApplication.update += TourTick; touring = true; }
        }

        static List<Step> BuildGuardEntryTour()
        {
            var list = new List<Step>();

            // --- A. idle -> guard, sampled along the blend -------------------------------------
            list.Add(S(4, delegate
            {
                var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                var wc = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponController>();
                if (vm != null) vm.EndGuard();
                if (wc != null) wc.Equip(0);
            }, null, 1));
            list.Add(S(8, delegate
            {
                var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                if (vm != null) vm.Interrupt();
            }, "A0_idle", 1));
            for (int i = 1; i <= 5; i++)
            {
                float k = i / 5f;
                list.Add(S(1, MakeBlend(k, false), "A" + i + "_rise", 1));
            }
            list.Add(S(2, delegate
            {
                var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                if (vm != null) vm.PlayGuard();
            }, "A6_settled", 1));

            // --- B. mid-swing -> guard ---------------------------------------------------------
            list.Add(S(4, delegate
            {
                var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                var wc = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponController>();
                if (vm == null || wc == null || wc.Current == null) return;
                vm.EndGuard();
                vm.GuardWanted = true;
                // Stretched so the editor tick can sample the arc AND its recovery leg.
                vm.PlayAttack(0, wc.Current.attackDuration * 6f, wc.Current.hitDelay * 6f);
            }, "B0_swing", 6));
            list.Add(S(0, null, "B1_recover_into_guard", 8));
            list.Add(S(4, null, "B2_settled", 1));

            // --- C. guard -> release ----------------------------------------------------------
            list.Add(S(2, delegate
            {
                var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                if (vm != null) { vm.GuardWanted = false; vm.PlayGuard(); }
            }, "C0_guard", 1));
            for (int i = 1; i <= 5; i++)
            {
                float k = i / 5f;
                list.Add(S(1, MakeBlend(k, true), "C" + i + "_release", 1));
            }
            list.Add(S(4, delegate
            {
                var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                if (vm != null) vm.EndGuard();
            }, "C6_idle", 2));
            return list;
        }

        /// <summary>
        /// Reproduces one sample of the shipped blend by hand: same live-transform start, same
        /// Quaternion.Slerp, same easing. Driving it manually is the only way to photograph a 0.08 s
        /// motion on an 8 Hz editor tick, and it is honest because it runs the identical maths.
        /// </summary>
        static Action MakeBlend(float k, bool release)
        {
            return delegate
            {
                var vm = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponViewmodel>();
                var wc = UnityEngine.Object.FindAnyObjectByType<VibeGame1.WeaponController>();
                if (vm == null || wc == null || wc.Current == null || vm.model == null) return;
                var target = release ? wc.Current.idle : wc.Current.guard;
                var start = release ? wc.Current.guard : wc.Current.idle;
                float e = release ? (k * k * (3f - 2f * k)) : (1f - (1f - k) * (1f - k));
                vm.Interrupt();
                vm.model.localPosition = Vector3.Lerp(start.pos, target.pos, e);
                vm.model.localRotation = Quaternion.Slerp(
                    Quaternion.Euler(start.euler), Quaternion.Euler(target.euler), e);
            };
        }

        // A riposte is NOT captured from here. The editor tick runs at roughly 8 Hz in the
        // background and the whole beat is 0.6-1.0 s, so this would sample it about six times and land
        // on the commit and the corpse while missing the stab and the blast. Use the runtime
        // Assets/Scripts/Debug/FrameFilm.cs, which captures in LateUpdate and gets every rendered frame.

        public static void Shoot(string path)
        {
            var cam = Camera.main;
            if (cam == null) { Log += " noCam"; Stop(); return; }
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 1;
            var prev = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
