using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Pure, data-only coverage for the Signature Sigil (souls-AI-accuracy spec, 2026-09-14 section 4):
    /// the shape a special attack's tell draws is DERIVED from the shipped attack data, never authored
    /// and never a new field. Nothing here touches a scene, a prefab or a MonoBehaviour — <see cref="SigilShape.For"/>
    /// and <see cref="EnemySigilFx"/>'s pure statics are testable without an instance, matching this
    /// project's <c>PuppetVisuals.ResolveRate</c> pattern.
    /// </summary>
    public class EnemySigilTests
    {
        static EnemyAttackData[] AllShippedAttacks()
        {
            var guids = AssetDatabase.FindAssets("t:EnemyAttackData", new[] { "Assets/Data/Attacks" });
            var list = new System.Collections.Generic.List<EnemyAttackData>();
            foreach (var g in guids)
            {
                var a = AssetDatabase.LoadAssetAtPath<EnemyAttackData>(AssetDatabase.GUIDToAssetPath(g));
                if (a != null) list.Add(a);
            }
            return list.ToArray();
        }

        [Test]
        public void ShapeForEveryShippedAttackIsDerivedNotAuthored()
        {
            var attacks = AllShippedAttacks();
            Assert.Greater(attacks.Length, 0, "no EnemyAttackData assets — run VibeGame1/3. Create Data.");

            foreach (var atk in attacks)
            {
                var shape = SigilShape.For(atk);

                // The prose rule, checked directly: a blue (blockable), no-lunge, short-windup jab/swing/
                // stab/finisher never draws a sigil. The base tell (silhouette + audio) is enough for it.
                bool blueNoLungeShortWindup = !atk.unblockable && atk.lungeDistance < 1.5f && atk.windup < 0.9f && atk.coneDeg < 300f;   // a spin is a RING
                if (blueNoLungeShortWindup)
                    Assert.AreEqual(SigilShape.Kind.None, shape,
                        atk.name + ": a blockable, no-lunge, sub-0.9s-windup attack must not draw a sigil");

                // No-contact stances draw nothing on the floor (they get a hand charge instead).
                if (atk.range <= 0f)
                    Assert.AreEqual(SigilShape.Kind.None, shape,
                        atk.name + ": range <= 0 is a no-contact stance; it must not draw a floor sigil");
            }
        }

        [Test]
        public void TheLaneLengthIsLungePlusReachPlusSlack()
        {
            Assert.AreEqual(2.0f + 1.0f + 0.5f, EnemySigilFx.LaneLength(2.0f, 1.0f), 1e-4f);
            Assert.AreEqual(3.4f + 4.7f + 0.5f, EnemySigilFx.LaneLength(3.4f, 4.7f), 1e-4f);
            // A zero (or missing) lunge still measures the reach plus the same 0.5 m slack `allow` uses.
            Assert.AreEqual(2.9f + 0.5f, EnemySigilFx.LaneLength(2.9f, 0f), 1e-4f);
        }

        [Test]
        public void AnUnblockableSigilIsAlwaysRed()
        {
            // Regardless of the body's own hue -- gold, teal, ember, bone -- an unblockable attack's
            // sigil is the same alert red everywhere else in this game means "move, do not parry".
            Assert.AreEqual(EnemySigilFx.AlertRed, EnemySigilFx.HueFor(true, Color.white));
            Assert.AreEqual(EnemySigilFx.AlertRed, EnemySigilFx.HueFor(true, new Color(1f, 0.82f, 0.29f) * 1.8f));
            Assert.AreEqual(EnemySigilFx.AlertRed, EnemySigilFx.HueFor(true, new Color(0.25f, 0.75f, 0.62f) * 1.3f));

            // A blockable attack's hue is the body's own emission, normalised.
            var emission = new Color(1f, 0.16f, 0.16f) * 1.8f;
            Assert.AreEqual(SlashFx.NormaliseColor(emission), EnemySigilFx.HueFor(false, emission));
        }

        [Test]
        public void NoSigilMaterialExceedsOne()
        {
            Assert.LessOrEqual(EnemySigilFx.AlertRed.maxColorComponent, 1.0f);

            // Every shipped EnemyData's emission, through the same HueFor a body's sigil actually uses.
            var guids = AssetDatabase.FindAssets("t:EnemyData", new[] { "Assets/Data/Enemies" });
            Assert.Greater(guids.Length, 0, "no EnemyData assets — run VibeGame1/3. Create Data.");
            foreach (var g in guids)
            {
                var d = AssetDatabase.LoadAssetAtPath<EnemyData>(AssetDatabase.GUIDToAssetPath(g));
                if (d == null) continue;
                Color blockable = EnemySigilFx.HueFor(false, d.emission);
                Color unblockable = EnemySigilFx.HueFor(true, d.emission);
                Assert.LessOrEqual(blockable.maxColorComponent, 1.0f + 1e-4f, d.name + " (blockable sigil hue)");
                Assert.LessOrEqual(unblockable.maxColorComponent, 1.0f + 1e-4f, d.name + " (unblockable sigil hue)");
            }
        }
    }
}
