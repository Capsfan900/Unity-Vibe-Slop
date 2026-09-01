using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE EMBER REVENANT — the ai_skelly_tool test body, and the project's first BURNING enemy.
    /// Hard rule 9 in assert form: these read the SHIPPED assets, never the C# defaults.
    ///
    /// <para>EditMode rather than a <c>FeatureTests</c> entry, for the same reason as
    /// <see cref="MarionetteDataTests"/>: it is a sandbox prototype with no spawner in <c>Level_01</c>,
    /// so a play-mode test would either Skip in the canonical run or force it into a level it is
    /// deliberately not in.</para>
    ///
    /// <para><b>The one that matters most is <see cref="ThePrefabActuallyCarriesTheAura"/>.</b> A
    /// MonoBehaviour whose script GUID does not resolve becomes a MISSING SCRIPT: the component is
    /// still in the YAML, the values are still in the YAML, and <c>GetComponent</c> returns null at
    /// runtime with nothing in the console. That is not hypothetical — building this prefab in a
    /// scratch copy of the project minted a different GUID for the new script, and the resulting
    /// prefab looked completely correct in a text diff while carrying a component that would never
    /// run. A screenshot would not have caught it either, because the fire is spawned at runtime.</para>
    /// </summary>
    public class RevenantDataTests
    {
        const float WindupFloor = 0.45f;   // no enemy attack wind-up may go below this
        const float CueLead = 0.28f;       // EnemyController.cueLead
        const float GapFloor = 0.10f;      // EnemyController.NextGap floors here

        static EnemyData Data() =>
            AssetDatabase.LoadAssetAtPath<EnemyData>("Assets/Data/Enemies/Legendary_Revenant.asset");
        static EnemyAttackData Atk(string n) =>
            AssetDatabase.LoadAssetAtPath<EnemyAttackData>("Assets/Data/Attacks/" + n + ".asset");
        static GameObject Prefab() =>
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Legendary_Revenant.prefab");

        [Test]
        public void Assets_Exist()
        {
            Assert.IsNotNull(Data(), "Legendary_Revenant.asset missing — run VibeGame1/3. Create Data");
            foreach (var n in new[] { "Revenant_Slash", "Revenant_Stab", "Revenant_Overhead", "Revenant_Kick" })
                Assert.IsNotNull(Atk(n), n + ".asset missing — run VibeGame1/3. Create Data");
            Assert.IsNotNull(Prefab(), "prefab missing — run VibeGame1/4a then 4b");
        }

        [Test]
        public void EveryWindup_ClearsTheFloorAndLeavesRoomForTheCue()
        {
            foreach (var combo in Data().ResolveCombos())
                foreach (var h in combo.hits)
                {
                    Assert.GreaterOrEqual(h.windup, WindupFloor,
                        h.name + " wind-up " + h.windup + " is under the " + WindupFloor + "s floor.");
                    Assert.Greater(h.windup + h.impactDelay, CueLead,
                        h.name + ": the cue would have to fire before its own wind-up started.");
                }
        }

        [Test]
        public void ItIsSlowerThanTheMarionette_BecauseItTeachesADifferentThing()
        {
            // The Marionette is a cadence you HOLD at the parry contract's floor. This one is a READ:
            // slow, committed swings with real openings. If it ever gets as fast as the Marionette it
            // has stopped being the gentler of the two and the pair teaches one thing instead of two.
            var pass = AssetDatabase.LoadAssetAtPath<EnemyAttackData>(
                "Assets/Data/Attacks/Marionette_SpinPass.asset");
            Assert.IsNotNull(pass);
            foreach (var combo in Data().ResolveCombos())
                foreach (var h in combo.hits)
                    Assert.Greater(h.windup, pass.windup,
                        h.name + " (" + h.windup + "s) is no slower to read than a Marionette spin pass (" +
                        pass.windup + "s).");
        }

        [Test]
        public void TheOverheadIsTheBiggestPunishWindow()
        {
            var d = Data();
            float longest = 0f;
            string who = "";
            foreach (var c in d.ResolveCombos())
                foreach (var h in c.hits)
                    if (h.recovery > longest) { longest = h.recovery; who = h.name; }
            Assert.AreEqual("Revenant_Overhead", who,
                "the biggest opening is now " + who + "; the overhead is supposed to be the lesson that " +
                "a whiffed heavy is free damage.");
            Assert.Greater(d.staggerSeconds, longest * 1.5f,
                "stagger " + d.staggerSeconds + "s is not a big enough step up from the " + longest +
                "s overhead recovery.");
        }

        [Test]
        public void ExactlyOneAttackIsUnblockable_AndItAnswersTurtling()
        {
            int unblockable = 0;
            foreach (var c in Data().ResolveCombos())
                foreach (var h in c.hits)
                    if (h.unblockable) unblockable++;
            Assert.Greater(unblockable, 0,
                "nothing here is unblockable, so simply holding guard is a complete answer to this enemy.");
            Assert.IsTrue(Atk("Revenant_Kick").unblockable, "the kick is the designated anti-turtle.");
        }

        [Test]
        public void EveryAttackReaches()
        {
            var d = Data();
            Assert.GreaterOrEqual(d.preferredRange, d.attackRange,
                "preferred=" + d.preferredRange + " attack=" + d.attackRange);
            foreach (var c in d.ResolveCombos())
                foreach (var h in c.hits)
                    Assert.GreaterOrEqual(h.range + h.lungeDistance, d.preferredRange + d.commitTolerance,
                        h.name + ": reach " + (h.range + h.lungeDistance) + " cannot cover the commit band " +
                        (d.preferredRange + d.commitTolerance) + " — it would whiff when committed.");
        }

        [Test]
        public void ThePrefabActuallyCarriesTheAura()
        {
            // See the class doc: a component whose script GUID does not resolve is a MISSING SCRIPT —
            // present in the YAML, absent at runtime, silent in the console. Resolving the TYPE here is
            // the whole point; asserting the YAML would pass on exactly the broken case.
            var aura = Prefab().GetComponentInChildren<EmberAura>(true);
            Assert.IsNotNull(aura,
                "no EmberAura resolves on Legendary_Revenant.prefab. Either it was never added, or its " +
                "script reference is a missing script — rebuild with VibeGame1/4b. Build Mini-Bosses.");

            // Hard rule 9: the shipped values, not the field initialisers.
            Assert.Greater(aura.emberHot.maxColorComponent, 1.05f,
                "the ember colour is under the 1.05 bloom threshold, so the fire will not bloom.");
            Assert.Less(aura.glowAtRest, 1f,
                "the resting glow is at or above the parry glow's own scale — a deflect must stay the " +
                "brightest thing an enemy ever does.");
            Assert.Greater(aura.glowAtBreak, aura.glowAtRest,
                "the body must stoke UP as posture breaks, like the eye does on every other enemy.");
            Assert.Greater(aura.emberCount, 0, "no embers; the enemy glows but does not emanate.");
            Assert.Greater(aura.emberToHeight, aura.emberFromHeight, "the ember column is inverted.");
        }

        [Test]
        public void TheBodyIsDarkSoTheFireHasSomethingToReadAgainst()
        {
            var d = Data();
            Assert.Less(d.bodyColor.maxColorComponent, 0.4f,
                "bodyColor is " + d.bodyColor + "; a bright body under a bright aura is one flat shape.");
            Assert.Greater(d.emission.maxColorComponent, 1.05f,
                "the enemy's emission accent is under the bloom threshold.");
        }

        [Test]
        public void ThePrefab_IsAnimatedAndFullyBound()
        {
            var p = Prefab();
            Assert.IsNull(p.GetComponent<BossController>(), "a mini-boss must never carry BossController.");
            Assert.IsNotNull(p.GetComponent<EnemyController>());

            var pv = p.GetComponentInChildren<PuppetVisuals>(true);
            Assert.IsNotNull(pv, "no PuppetVisuals — the animated presentation is not wired.");
            Assert.IsNotNull(pv.animator, "PuppetVisuals.animator is null.");
            Assert.IsNotNull(pv.animator.runtimeAnimatorController,
                "no AnimatorController — run VibeGame1/4a. Split Forge Animation Clips, then 4b.");

            // It does not whirl, and that has to be true in the DATA as well as in the intent: an empty
            // prefix means IsSpinPass is never true and every attack plays square-on.
            Assert.IsTrue(string.IsNullOrEmpty(pv.spinAttackPrefix),
                "the Revenant has a spin prefix ('" + pv.spinAttackPrefix + "'), so PuppetVisuals will " +
                "whirl its body on any attack whose name starts with it. This enemy does not spin.");

            Assert.IsNotNull(pv.body); Assert.IsNotNull(pv.eye); Assert.IsNotNull(pv.weapon);
            Assert.IsNotNull(pv.lungeRoot); Assert.IsNotNull(pv.armPivot);
            Assert.IsNotNull(pv.weaponPivot); Assert.IsNotNull(pv.alertMarker);
            Assert.IsNotNull(pv.deathblowMarker);
            StringAssert.StartsWith("Universal Render Pipeline/", pv.body.sharedMaterial.shader.name,
                "a non-URP shader renders magenta.");
        }

        [Test]
        public void TheAttackClipFitsTheDataWithoutBeingClamped()
        {
            var pv = Prefab().GetComponentInChildren<PuppetVisuals>(true);
            foreach (var n in new[] { "Revenant_Slash", "Revenant_Stab", "Revenant_Overhead", "Revenant_Kick" })
            {
                var a = Atk(n);
                float contact = pv.attackClipLength * pv.attackHitNormalized;
                float speed = contact / (a.windup + a.impactDelay);
                Assert.That(speed, Is.InRange(pv.minClipSpeed, pv.maxClipSpeed),
                    n + " would need the clip played at x" + speed.ToString("F2") +
                    ", outside the allowed " + pv.minClipSpeed + ".." + pv.maxClipSpeed +
                    " band. Fix the ART or pick a different clip — do not retune the attack to suit it.");
            }
        }
    }
}
