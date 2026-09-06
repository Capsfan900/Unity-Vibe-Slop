using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The near-break beat (2026-09-06): every posture-shaped bar — player, boss, and the grunt
    /// world-space bar (EnemyPostureBar) — reads "one more deflect" the same way, from
    /// EnemyPostureBar.NearBreakRatio (0.8). Before this the boss posture bar had no near-break
    /// treatment at all and the player bar had no beat at all (its 0.7 was the start of the base
    /// colour lerp toward danger red, never a beat threshold).
    /// </summary>
    public class BarViewNearBreakTests
    {
        [Test]
        public void NearBreakStrength_IsZeroBelowThreshold_AndRampsToOneAtFull()
        {
            Assert.AreEqual(0f, BarView.NearBreakStrength(0f, 0.8f));
            Assert.AreEqual(0f, BarView.NearBreakStrength(0.79f, 0.8f));
            Assert.AreEqual(0f, BarView.NearBreakStrength(0.8f, 0.8f), 1e-5f,
                "at the threshold itself the beat starts from zero strength and ramps up");
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
                Assert.AreEqual(1f, bar.NearBreakStrengthValue, 1e-5f, "strength over 1 must clamp to 1");
                bar.SetNearBreak(true, -3f, 4.5f);
                Assert.AreEqual(0f, bar.NearBreakStrengthValue, 1e-5f, "negative strength must clamp to 0");

                bar.SetNearBreak(false);
                Assert.IsFalse(bar.NearBreak, "posture dropping back under threshold must stop the beat");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------- the shipped HUD prefab

        // The break/near-break language depends on these two shipped flags: the player's posture bar
        // pulses when full (the "broken" read), the boss posture bar does not. If either flipped, the
        // near-break beat would be masked (pulse wins in BarView.Update) or the break read would vanish.
        const string HudPath = "Assets/Prefabs/HUD.prefab";

        [Test]
        public void ShippedPostureBars_PulseFlagsMatchTheBreakLanguage()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if (hud == null) Assert.Ignore("HUD.prefab missing — run VibeGame1/5. Build HUD.");

            var controller = hud.GetComponentInChildren<HUDController>(true);
            Assert.IsNotNull(controller, "no HUDController on the HUD prefab.");
            Assert.IsNotNull(controller.postureBar, "the HUD has no player posture bar.");
            Assert.IsTrue(controller.postureBar.pulseWhenFull,
                "the player posture bar must pulse when full — that is the BROKEN read the near-break beat leads into.");

            var boss = hud.GetComponentInChildren<BossBarView>(true);
            Assert.IsNotNull(boss, "no BossBarView on the HUD prefab.");
            Assert.IsNotNull(boss.posture, "the boss bar has no posture bar.");
            Assert.IsFalse(boss.posture.pulseWhenFull,
                "the boss posture bar must NOT pulse when full — a pulse would mask the near-break beat, which is the whole signal in that fight.");
        }
    }
}
