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
