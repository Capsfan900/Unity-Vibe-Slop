using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The solar portal crossing: the membrane that parts on the approach, the wash that closes over the
    /// eye, and the cut that hides the teleport frame. Every number the effect uses is a const in
    /// <see cref="SolarTransition"/> — there is no serialized copy that could drift from it — so these
    /// tests pin the CURVES and the guarantees rather than a shipped field.
    /// </summary>
    public class SolarTransitionTests
    {
        const string LevelPath = "Assets/Data/Levels/Level_01_Level.asset";

        static SolarRealmDef[] ShippedPortals()
        {
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelPath);
            Assert.IsNotNull(def, "Level_01 definition missing at " + LevelPath);
            var portals = def.arenas.Where(a => a.solarRealm != null && a.solarRealm.enabled)
                                    .Select(a => a.solarRealm).ToArray();
            Assert.IsTrue(portals.Length > 0, "the level should still ship solar portals");
            return portals;
        }

        // ---------------- the whole point: the inside of the shell is never drawn ----------------

        [Test]
        public void TheShellIsGoneBeforeTheCameraCanReachIt()
        {
            // A Cull Back sphere renders nothing from inside, so the raw behaviour was a POP: an opaque
            // sun one frame, the bare court the next. This is the structural replacement — the fade
            // reaches exactly zero OUTSIDE the drawn surface, so there is no camera position at or
            // inside the surface at which the shell contributes a single pixel.
            Assert.Greater(SolarTransition.MembraneClear, 0f,
                "the shell must clear BEFORE the surface, not at it");

            foreach (var portal in ShippedPortals())
            {
                float r = portal.visualRadius > 0f ? portal.visualRadius : portal.exteriorRadius;
                for (float d = 0f; d <= r + 0.0001f; d += 0.25f)
                    Assert.AreEqual(0f, SolarTransition.ShellFade(d, r), 0f,
                        portal.themeMaterialKey + ": shell still drawn at " + d + " m inside r=" + r);
                Assert.AreEqual(0f, SolarTransition.ShellFade(r + SolarTransition.MembraneClear, r), 0f);
                Assert.AreEqual(1f, SolarTransition.ShellFade(r + SolarTransition.MembraneBand, r), 0f,
                    "the sun must still be a solid ball on the run-up");
            }
        }

        [Test]
        public void TheShellFadeIsMonotonicAndSmoothAcrossTheBand()
        {
            const float r = 22f;
            // Seed from the first sample, not from a sentinel: ShellFade(r, r) is 0 at the surface, so
            // a -1f seed reports a 1.0 "step" on the very first comparison and fails a 0.06 limit before
            // the dissolve has been sampled twice.
            float previous = SolarTransition.ShellFade(r, r);
            for (float d = r; d <= r + SolarTransition.MembraneBand + 2f; d += 0.1f)
            {
                float k = SolarTransition.ShellFade(d, r);
                Assert.GreaterOrEqual(k, previous - 1e-5f, "fade must never dip while backing away");
                Assert.LessOrEqual(k - previous, 0.06f, "no visible step in the dissolve at 0.1 m granularity");
                previous = k;
            }
        }

        // ---------------- the wash ----------------

        [Test]
        public void TheWashIsContinuousAtTheSurfaceAndPeaksAtTheTrigger()
        {
            foreach (var portal in ShippedPortals())
            {
                float r = portal.visualRadius > 0f ? portal.visualRadius : portal.exteriorRadius;
                float trigger = portal.exteriorRadius;
                Assert.Greater(r, trigger + SolarTransition.MembraneClear,
                    portal.themeMaterialKey + ": the drawn sun must enclose its own trigger");

                Assert.AreEqual(0f, SolarTransition.Wash(r + SolarTransition.MembraneBand, r, trigger), 1e-4f,
                    "nothing on the eye until the membrane band");
                Assert.AreEqual(SolarTransition.MembraneWash,
                                SolarTransition.Wash(r, r, trigger), 1e-3f,
                    "the wash must have picked up exactly where the shell finished parting");
                Assert.AreEqual(SolarTransition.WashMax,
                                SolarTransition.Wash(trigger, r, trigger), 1e-3f,
                    "the eye is nearly covered by the time the transport fires");

                // Seam continuity: a step here would read as the screen flicking darker mid-dive.
                float outside = SolarTransition.Wash(r + 0.02f, r, trigger);
                float inside = SolarTransition.Wash(r - 0.02f, r, trigger);
                Assert.Less(Mathf.Abs(outside - inside), 0.01f, "no step at the surface seam");
            }
        }

        [Test]
        public void TheCutIsASmallStepNotAJump()
        {
            // "Instantly cut" must not mean "the frame explodes". The wash is already at 0.92 when the
            // trigger fires, so the cover is a 0.08 step the eye reads as a snap, not a flashbang.
            Assert.LessOrEqual(1f - SolarTransition.WashMax, 0.10f);
            Assert.GreaterOrEqual(SolarTransition.WashMax, 0.85f);
        }

        [Test]
        public void TheDescentStaysFlyableUntilTheLastFewMetres()
        {
            // The player crosses the drawn surface 10-13 m before the transport. If the wash closed
            // linearly they would fly that distance blind and could miss a 12 m trigger entirely.
            const float r = 22f, trigger = 12f;
            Assert.Less(SolarTransition.Wash(17f, r, trigger), 0.5f, "half way in, still readable");
            Assert.Greater(SolarTransition.Wash(13f, r, trigger), 0.7f, "one metre out, nearly closed");

            float previous = SolarTransition.Wash(r + SolarTransition.MembraneBand, r, trigger);
            for (float d = r + SolarTransition.MembraneBand; d >= trigger; d -= 0.1f)
            {
                float w = SolarTransition.Wash(d, r, trigger);
                Assert.GreaterOrEqual(w, previous - 1e-5f, "the wash must only ever close as you descend");
                previous = w;
            }
        }

        // ---------------- the hold: a clock AND a frame budget ----------------

        [Test]
        public void TheHoldNeedsBothTheClockAndTheFrameBudget()
        {
            const float hold = SolarTransition.HoldSeconds;
            const int frames = SolarTransition.MinHoldFrames;

            Assert.IsTrue(ScreenFlash.CurtainHolding(0f, 0, hold, frames), "nothing elapsed");
            Assert.IsTrue(ScreenFlash.CurtainHolding(10f, frames - 1, hold, frames),
                "a hitch can burn the whole wall-clock hold inside ONE Update; the frame budget is what " +
                "guarantees the destination was actually drawn under cover");
            Assert.IsTrue(ScreenFlash.CurtainHolding(hold * 0.5f, 999, hold, frames),
                "a 240 Hz machine must still hold long enough for the eye");
            Assert.IsFalse(ScreenFlash.CurtainHolding(hold, frames, hold, frames), "both met, reveal may start");
            Assert.GreaterOrEqual(frames, 3, "two frames is one frame of margin; three is the floor");
        }

        [Test]
        public void TheRevealStartsOpaqueAndFinishesClear()
        {
            const float rev = SolarTransition.RevealSeconds;
            Assert.AreEqual(1f, ScreenFlash.CurtainReveal(0f, rev), 1e-4f);
            Assert.AreEqual(0f, ScreenFlash.CurtainReveal(rev, rev), 1e-4f);
            Assert.AreEqual(0f, ScreenFlash.CurtainReveal(rev * 4f, rev), 1e-4f);
            // Ease-out: half way through the reveal the frame is already mostly clear, and what is left
            // is a thinning theme tint rather than a wall.
            Assert.Less(ScreenFlash.CurtainReveal(rev * 0.5f, rev), 0.30f);
            Assert.Greater(SolarTransition.RevealSeconds, SolarTransition.HoldSeconds,
                "the cover is the instant half; the reveal is the smooth half");
        }

        [Test]
        public void TheCoverIsAppliedInsideTheCallNotOnTheNextUpdate()
        {
            // This is the guarantee that hides the teleport. SolarArenaPortal.Enter teleports and then
            // calls SolarTransition.Cut in the SAME call; Unity renders no frame inside a callback, so a
            // cover written synchronously here cannot be one frame late.
            var go = new GameObject("FlashProbe", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            try
            {
                var img = go.GetComponent<Image>();
                var flash = go.AddComponent<ScreenFlash>();
                flash.image = img;
                flash.Curtain(SolarTransition.HotTint("SolarCyan"), SolarTransition.SettleTint("SolarCyan"),
                              SolarTransition.HoldSeconds, SolarTransition.MinHoldFrames,
                              SolarTransition.RevealSeconds, SolarTransition.ColourSettleFraction);
                Assert.AreEqual(1f, img.color.a, 1e-4f, "the cover must be on screen before Update runs");
                Assert.IsTrue(flash.CurtainActive);
                flash.ClearCurtain();
                Assert.IsFalse(flash.CurtainActive, "a portal reset must be able to drop a cut on the spot");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void CutSurvivesAMissingHud()
        {
            Assert.DoesNotThrow(() => SolarTransition.Cut("SolarGold"));
        }

        [Test]
        public void EntryAnnouncesOnlySuccessfulTransportOnceThroughTheExistingPool()
        {
            // Inactive gameplay objects avoid running a scene or subscribing player lifecycle events.
            // Seed only the motor's native controller and the audio pool so this exercises the real
            // Enter -> Teleport -> AudioManager.Play path without starting music or generating a level.
            var root = new GameObject("SolarAudioEntryProbe");
            root.SetActive(false);
            var sourceGo = new GameObject("SolarAudioSourceProbe");
            var previousManager = AudioManager.I;
            var singleton = typeof(AudioManager).GetProperty("I");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                        System.Reflection.BindingFlags.NonPublic;
            AudioClip clip = null;
            try
            {
                var manager = root.AddComponent<AudioManager>();
                singleton.SetValue(null, manager);
                var source = sourceGo.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.volume = 0f; // tests inspect dispatch, listening belongs to the playtest
                var pool = new AudioSource[12];
                for (int i = 0; i < pool.Length; i++) pool[i] = source;
                typeof(AudioManager).GetField("pool", flags).SetValue(manager, pool);
                var library = (System.Collections.Generic.Dictionary<Sfx, AudioClip[]>)
                    typeof(AudioManager).GetField("library", flags).GetValue(manager);
                clip = ProceduralSfx.Build(Sfx.SolarWarp);
                library.Add(Sfx.SolarWarp, new[] { clip });
                var next = typeof(AudioManager).GetField("next", flags);

                var portal = root.AddComponent<SolarArenaPortal>();
                var playerGo = new GameObject("PlayerProbe");
                playerGo.transform.SetParent(root.transform);
                var player = playerGo.AddComponent<PlayerCombat>();
                Assert.IsFalse(portal.Enter(null));
                Assert.IsFalse(portal.Enter(player), "missing arena");
                var arenaGo = new GameObject("ArenaProbe", typeof(BoxCollider));
                arenaGo.transform.SetParent(root.transform);
                portal.arena = arenaGo.AddComponent<BossArenaTrigger>();
                // A mini-boss spawner prevents the fixture from waking a real scene's final boss.
                portal.arena.clearSpawner = arenaGo.AddComponent<EnemySpawner>();
                Assert.IsFalse(portal.Enter(player), "missing destination");
                var destination = new GameObject("DestinationProbe");
                destination.transform.SetParent(root.transform);
                destination.transform.position = new Vector3(200f, 12f, 300f);
                portal.realmEntry = destination.transform;
                Assert.IsFalse(portal.Enter(player), "missing motor");
                Assert.AreEqual(0, next.GetValue(manager), "rejected attempts must stay silent");

                var motor = playerGo.AddComponent<FirstPersonMotor>();
                typeof(FirstPersonMotor).GetField("cc", flags)
                    .SetValue(motor, playerGo.GetComponent<CharacterController>());
                var lastTeleport = typeof(SolarArenaPortal).GetField("lastTeleportAt", flags);
                lastTeleport.SetValue(portal, Time.unscaledTime);
                Assert.IsFalse(portal.Enter(player), "debounced crossing");
                Assert.AreEqual(0, next.GetValue(manager));

                lastTeleport.SetValue(portal, Time.unscaledTime - 1f);
                Assert.IsTrue(portal.Enter(player));
                Assert.AreEqual(destination.transform.position, player.transform.position);
                Assert.AreEqual(1, next.GetValue(manager), "exactly one pooled dispatch per success");
                Assert.AreEqual(1f, source.pitch, 0f, "the cinematic sound must not jitter in duration");
                Assert.IsFalse(portal.Enter(player), "duplicate collider callback");
                Assert.AreEqual(1, next.GetValue(manager), "duplicates must not layer the warp");
                lastTeleport.SetValue(portal, Time.unscaledTime - 1f);
                Assert.IsTrue(portal.Enter(player), "a later valid entry still announces itself");
                Assert.AreEqual(2, next.GetValue(manager));
                Assert.AreSame(pool, typeof(AudioManager).GetField("pool", flags).GetValue(manager));
                Assert.AreSame(clip, library[Sfx.SolarWarp][0], "crossing reuses the preloaded clip");

                // The visual already disarms cleared suns. Enter must share that invariant even when
                // called directly by a collider/debug harness after the debounce has expired.
                typeof(BossArenaTrigger).GetField("cleared", flags).SetValue(portal.arena, true);
                lastTeleport.SetValue(portal, Time.unscaledTime - 1f);
                player.transform.position = new Vector3(5f, 3f, 9f);
                Vector3 beforeClearedEntry = player.transform.position;
                Assert.IsFalse(portal.Enter(player), "a cleared sun cannot restart its transport");
                Assert.AreEqual(beforeClearedEntry, player.transform.position,
                    "cleared entry must leave the player on the exterior route");
                Assert.AreEqual(2, next.GetValue(manager), "cleared entry must not announce a warp");
            }
            finally
            {
                singleton.SetValue(null, previousManager);
                Object.DestroyImmediate(sourceGo);
                Object.DestroyImmediate(root);
                if (clip != null) Object.DestroyImmediate(clip);
            }
        }

        // ---------------- colour ----------------

        [Test]
        public void TheCutNeverBloomsBecauseItIsOnTheHudCanvas()
        {
            foreach (string key in new[] { "SolarCyan", "SolarGold", "SolarAzure", "SolarGhost", "unknown" })
            {
                foreach (var c in new[] { SolarTransition.HotTint(key), SolarTransition.SettleTint(key),
                                          SolarTransition.WashTint(key, 0.42f), SolarTransition.WashTint(key, 1f) })
                {
                    Assert.LessOrEqual(c.r, 1f, key); Assert.LessOrEqual(c.g, 1f, key); Assert.LessOrEqual(c.b, 1f, key);
                    Assert.GreaterOrEqual(c.r, 0f, key); Assert.GreaterOrEqual(c.g, 0f, key); Assert.GreaterOrEqual(c.b, 0f, key);
                }
            }
        }

        [Test]
        public void TheCutResolvesIntoTheColourOfTheRoomItOpensOn()
        {
            // Same four values LevelDefinitionBuilder gives the realm's point light. The bleach resolves
            // to the light the player is about to be standing in, which is what makes it read as one move.
            Assert.AreEqual(new Color(0.21f, 0.86f, 0.93f), SolarTransition.SettleTint("SolarCyan"));
            Assert.AreEqual(new Color(0.85f, 0.68f, 0.16f), SolarTransition.SettleTint("SolarGold"));
            Assert.AreEqual(new Color(0.20f, 0.42f, 1f), SolarTransition.SettleTint("SolarAzure"));
            Assert.AreEqual(new Color(0.25f, 0.88f, 0.48f), SolarTransition.SettleTint("SolarGhost"));

            foreach (string key in new[] { "SolarCyan", "SolarGold", "SolarAzure", "SolarGhost" })
            {
                Color hot = SolarTransition.HotTint(key), settle = SolarTransition.SettleTint(key);
                float hotMin = Mathf.Min(hot.r, Mathf.Min(hot.g, hot.b));
                float settleMin = Mathf.Min(settle.r, Mathf.Min(settle.g, settle.b));
                Assert.Greater(hotMin, settleMin + 0.3f, key + ": the cover must read as an overexposure");
                Assert.Greater(hotMin, 0.7f, key + ": every channel lifted, so the cut bleaches rather than gels");
            }
        }

        [Test]
        public void EveryShippedPortalThemeHasItsOwnCut()
        {
            // Cyan is the fallback, so "does it differ from the fallback" cannot be the test. Assert the
            // shipped key is one this table actually knows: an unrecognised theme would silently make
            // every cut cyan and the cut would stop saying which realm you are entering.
            string[] known = { "SolarCyan", "SolarGold", "SolarAzure", "SolarGhost" };
            foreach (var portal in ShippedPortals())
                Assert.Contains(portal.themeMaterialKey, known,
                    "SolarTransition.SettleTint has no entry for this theme; it would fall through to cyan");

            var seen = ShippedPortals().Select(p => SolarTransition.SettleTint(p.themeMaterialKey)).ToArray();
            Assert.AreEqual(seen.Length, seen.Distinct().Count(), "each sun must cut in its own colour");
        }

        // ---------------- the shader channel the dissolve drives ----------------

        [Test]
        public void TheSolarMaterialsShipTheCrossingFadeAtOne()
        {
            foreach (string name in new[] { "M_SolarCyan", "M_SolarGold", "M_SolarAzure", "M_SolarGhost",
                                            "M_SolarCorona" })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + name + ".mat");
                Assert.IsNotNull(mat, name + " missing — run VibeGame1/2. Create Materials");
                Assert.IsTrue(mat.HasProperty("_Fade"),
                    name + " has no _Fade: reimport Assets/Shaders/SolarArena.shader");
                Assert.AreEqual(1f, mat.GetFloat("_Fade"), 1e-4f,
                    name + " must ship fully drawn; only SolarArenaVisual's per-renderer block moves it");
            }
        }
    }
}
