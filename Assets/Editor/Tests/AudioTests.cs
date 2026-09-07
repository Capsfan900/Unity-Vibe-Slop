using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Pins the shape of the audio system, not how it sounds — nothing here can prove a sound is good,
    /// only that every <see cref="Sfx"/> value resolves to something, that the shipped clip folders still
    /// match the enum's names (rule 7: append-only, folder-per-name), and that the mix table (rule 9
    /// applied to audio: a code default is not a shipped level) stays exhaustive and keeps headroom for
    /// <see cref="Sfx.ParryCue"/>, the one sound that must never be masked.
    ///
    /// <para>Written 2026-09-06 alongside the audio pass that added Refuse, Detonate, Tension and Spill
    /// and found Sfx.Teleport shipping with no mix-table entry at all (silently defaulting to 0.7). This
    /// suite exists so that gap cannot recur unnoticed.</para>
    /// </summary>
    public class AudioTests
    {
        static readonly Sfx[] AllPlayable = PlayableValues();

        static Sfx[] PlayableValues()
        {
            var values = (Sfx[])Enum.GetValues(typeof(Sfx));
            // Sfx.Drone is never routed through AudioManager.Play — it is only ever the raw ambient-bed
            // fallback clip (AudioManager.Awake skips it when building the pooled library on purpose).
            var list = new System.Collections.Generic.List<Sfx>();
            foreach (var s in values) if (s != Sfx.Drone) list.Add(s);
            return list.ToArray();
        }

        // ---------------------------------------------------------------- the synthesized safety net

        [Test]
        public void EveryPlayableSfxSynthesizesANonEmptyClip()
        {
            // The whole reason ProceduralSfx exists: the project must never ship a silent event just
            // because a Resources folder is empty. This is the safety net itself, proven directly.
            foreach (var s in AllPlayable)
            {
                AudioClip clip = null;
                Assert.DoesNotThrow(() => clip = ProceduralSfx.Build(s), "Sfx." + s + " threw while synthesizing");
                Assert.IsNotNull(clip, "Sfx." + s + " synthesized a null clip");
                Assert.Greater(clip.samples, 0, "Sfx." + s + " synthesized an empty clip");
                Assert.Greater(clip.length, 0f, "Sfx." + s + " synthesized a zero-length clip");
            }
        }

        [Test]
        public void DroneIsNeverBuiltThroughTheGeneralSwitchAsAnythingButItself()
        {
            // Not routed through the pool, but it must still be buildable directly (AudioManager falls
            // back to it for the ambient bed if Resources/Audio/Music/ambient is missing).
            AudioClip clip = null;
            Assert.DoesNotThrow(() => clip = ProceduralSfx.Build(Sfx.Drone));
            Assert.IsNotNull(clip);
            Assert.Greater(clip.samples, 0);
        }

        // ---------------------------------------------------------------- folder names are the enum (rule 7)

        [Test]
        public void ShippedClipFoldersMatchAnExistingSfxNameExactly()
        {
            // Rule 7: "Sfx enum names are folder names ... append only, never reorder or rename." A stale
            // or misspelled folder after a rename attempt would silently stop matching and fall back to
            // synthesis with no error anywhere — this is the only place that would ever notice.
            string root = Path.Combine(Application.dataPath, "Resources/Audio/Sfx");
            if (!Directory.Exists(root)) { Assert.Inconclusive("No Resources/Audio/Sfx folder in this checkout."); return; }

            foreach (var dir in Directory.GetDirectories(root))
            {
                string name = new DirectoryInfo(dir).Name;
                bool isDefined = Enum.IsDefined(typeof(Sfx), name);
                Assert.IsTrue(isDefined, "Resources/Audio/Sfx/" + name + " does not match any Sfx enum member");
            }
        }

        // ---------------------------------------------------------------- the mix table (rule 9 for audio)

        [Test]
        public void TrimTableIsExhaustiveOverEveryPlayableSfx()
        {
            // A missing row is not "silent" — it takes PlayInternal's 0.7 default, which is how
            // Sfx.Teleport shipped un-mixed. Every playable value must be an AUTHORED decision.
            foreach (var s in AllPlayable)
                Assert.IsTrue(AudioManager.HasExplicitTrim(s), "Sfx." + s + " has no explicit mix-table row");
        }

        [Test]
        public void TrimValuesAreAllInASaneAudibleRange()
        {
            foreach (var s in AllPlayable)
            {
                float t = AudioManager.Trim(s);
                Assert.Greater(t, 0f, "Sfx." + s + " trims to silence");
                Assert.LessOrEqual(t, 1f, "Sfx." + s + " exceeds unity gain in the mix table");
            }
        }

        [Test]
        public void TheFourSystemsThatShippedSilentNowHaveAnAuthoredMixLevel()
        {
            // Named regression guard for the 2026-09-06 grill findings: stamina refusal, the sentry
            // detonation, the posture near-break tell and the flask punish all had zero sound.
            Assert.IsTrue(AudioManager.HasExplicitTrim(Sfx.Refuse));
            Assert.IsTrue(AudioManager.HasExplicitTrim(Sfx.Detonate));
            Assert.IsTrue(AudioManager.HasExplicitTrim(Sfx.Tension));
            Assert.IsTrue(AudioManager.HasExplicitTrim(Sfx.Spill));

            // All four can plausibly land inside the parry cue's 0.28 s lead (a near-break tick or a
            // refused dash during a fight, a detonation on a span you are mid-run through), so — unlike
            // Hurt/Parry/PostureBreak, which only ever resolve AFTER the cue's window has already had the
            // player's undivided attention — none of them may be mixed at or above Sfx.ParryCue.
            float cue = AudioManager.Trim(Sfx.ParryCue);
            Assert.Less(AudioManager.Trim(Sfx.Refuse), cue);
            Assert.Less(AudioManager.Trim(Sfx.Detonate), cue);
            Assert.Less(AudioManager.Trim(Sfx.Tension), cue);
            Assert.Less(AudioManager.Trim(Sfx.Spill), cue);

            // Tension is a passive per-enemy notification, not a combat resolution — it must be the
            // quietest of the four additions or a fight with several near-break enemies gets noisy fast.
            Assert.LessOrEqual(AudioManager.Trim(Sfx.Tension), AudioManager.Trim(Sfx.Refuse));
            Assert.LessOrEqual(AudioManager.Trim(Sfx.Tension), AudioManager.Trim(Sfx.Spill));
            Assert.LessOrEqual(AudioManager.Trim(Sfx.Tension), AudioManager.Trim(Sfx.Detonate));
            // Detonate must stay under Thunder: it happens routinely, Thunder must stay the loudest
            // thing in the game for the one ability that is supposed to feel like an event.
            Assert.Less(AudioManager.Trim(Sfx.Detonate), AudioManager.Trim(Sfx.Thunder));
        }
    }
}
