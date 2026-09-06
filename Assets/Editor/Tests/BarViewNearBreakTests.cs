using NUnit.Framework;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The near-break beat (2026-09-06): every posture-shaped bar — player, boss, and the grunt
    /// world-space bar (EnemyPostureBar) — reads "one more deflect" the same way, from
    /// EnemyPostureBar.NearBreakRatio (0.8). Before this the boss posture bar had no near-break
    /// treatment at all and the player bar used a different, undocumented threshold (0.7).
    /// </summary>
    public class BarViewNearBreakTests
    {
        [Test]
        public void NearBreakStrength_IsZeroBelowThreshold_AndRampsToOneAtFull()
        {
            Assert.AreEqual(0f, BarView.NearBreakStrength(0f, 0.8f));
            Assert.AreEqual(0f, BarView.NearBreakStrength(0.79f, 0.8f));
            Assert.AreEqual(0f, BarView.NearBreakStrength(0.8f, 0.8f), 1e-5f, "at the threshold itself the beat should just be starting, not silent");
            Assert.AreEqual(1f, BarView.NearBreakStrength(1f, 0.8f), 1e-5f, "a full bar is the hardest beat");
            Assert.Greater(BarView.NearBreakStrength(0.9f, 0.8f), BarView.NearBreakStrength(0.85f, 0.8f),
                "closer to break must beat harder, not just on/off");
        }

        [Test]
        public void SharedThreshold_MatchesEnemyPostureBar()
        {
            // The whole point: player and boss bars use the SAME constants the grunt world-space bar
            // does, not a re-typed number that can drift.
            Assert.AreEqual(0.8f, EnemyPostureBar.NearBreakRatio);
            Assert.Greater(EnemyPostureBar.NearBreakHz, 2f);
            Assert.Less(EnemyPostureBar.NearBreakHz, 8f);
        }

        [Test]
        public void SetNearBreak_TogglesTheFlagAndClampsStrength()
        {
            var go = new GameObject("BarViewNearBreakTest");
            try
            {
                var bar = go.AddComponent<BarView>();
                Assert.IsFalse(bar.NearBreak, "a fresh bar must not start mid-beat");

                bar.SetNearBreak(true, 5f, 4.5f); // strength over 1 must clamp
                Assert.IsTrue(bar.NearBreak);

                bar.SetNearBreak(false);
                Assert.IsFalse(bar.NearBreak, "posture dropping back under threshold must stop the beat");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
