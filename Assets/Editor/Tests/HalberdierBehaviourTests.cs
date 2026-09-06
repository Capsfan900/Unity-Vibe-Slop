using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE ARGENT HALBERDIER's BEHAVIOUR after the 2026-09-04 play report: "he needs to use his charge
    /// when you get too far and then combo his attacks on you and be aggressive", and "efficient, not
    /// lag-causing VFX to make his attacks more visually appealing". Hard rule 9 in assert form: every
    /// test reads the shipped assets.
    ///
    /// <para>The aggression numbers are held as EFFECTIVE values — what <c>EnemyController</c> actually
    /// plays after its aggression scaling (recovery × lerp(1, 0.35, A), cooldown × lerp(1, 0.3, A),
    /// gap × lerp(1, 0.45, A)) — because a raw number that looks like an opening can be compressed into a
    /// beat by the same aggression that makes the rest of the fight fast.</para>
    /// </summary>
    public class HalberdierBehaviourTests
    {
        const float GapFloor = 0.10f;      // EnemyController.NextGap floors here

        static EnemyData Data() =>
            AssetDatabase.LoadAssetAtPath<EnemyData>("Assets/Data/Enemies/Legendary_Halberdier.asset");
        static EnemyAttackData Atk(string n) =>
            AssetDatabase.LoadAssetAtPath<EnemyAttackData>("Assets/Data/Attacks/" + n + ".asset");
        static GameObject Prefab() =>
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Legendary_Halberdier.prefab");

        static float A => Mathf.Clamp01(Data().aggression);
        static float EffectiveRecovery(EnemyAttackData h) =>
            Mathf.Max(h.recovery * Mathf.Lerp(1f, 0.35f, A), Data().comboBreathSeconds);
        static float EffectiveGap(EnemyAttackData h) =>
            Mathf.Max(GapFloor, h.comboGap * Mathf.Lerp(1f, 0.45f, A));

        static IEnumerable<MovesetEntry> Entries()
        {
            var ms = Data().moveset;
            Assert.IsNotNull(ms, "no moveset on Legendary_Halberdier — run VibeGame1/3. Create Data");
            foreach (var e in ms.entries)
                if (e != null && e.combo != null && e.combo.hits != null && e.combo.hits.Length > 0) yield return e;
        }

        static bool Contains(MovesetEntry e, EnemyAttackData a)
        {
            foreach (var h in e.combo.hits) if (h == a) return true;
            return false;
        }

        [Test]
        public void HeIsAggressive_ButNoTellGotShorter()
        {
            var d = Data();
            Assert.GreaterOrEqual(d.aggression, 0.8f, "aggression " + d.aggression + " — he is meant to press.");
            Assert.GreaterOrEqual(d.aggression, 0.5f, "under 0.5 EnemyController resets after a deflect instead of pressing on.");
            // Speed comes from density, never from the tell: the parry contract is untouched.
            foreach (var e in Entries())
                foreach (var h in e.combo.hits)
                    Assert.GreaterOrEqual(h.windup, 0.45f, h.name + " wind-up " + h.windup + " is under the floor.");
            Assert.Less(d.windupTurnMultiplier, 0.35f,
                "a committed swing that tracks the player makes the sidestep stop working exactly when " +
                "the aggression makes it matter.");
            Assert.Greater(d.comboBreathSeconds, 0.15f, "a phrase must still read as a phrase with a breath after it.");
        }

        [Test]
        public void TheChargeIsTheAnswerToDistance()
        {
            var charge = Atk("Halberdier_Charge");
            var leap = Atk("Halberdier_Leap");
            Assert.IsNotNull(charge); Assert.IsNotNull(leap);

            float chargeWeightFar = 0f, otherWeightFar = 0f, maxFar = 0f;
            MovesetEntry heaviestFar = null;
            bool coversAggroEdge = false;
            foreach (var e in Entries())
            {
                if (e.minRange < 5f) continue;   // the far band starts where he loses reach
                if (e.combo.hits[0] == charge)
                {
                    chargeWeightFar += e.weight;
                    if (e.maxRange >= Data().aggroRange - 0.01f) coversAggroEdge = true;
                }
                else otherWeightFar += e.weight;
                if (e.weight > maxFar) { maxFar = e.weight; heaviestFar = e; }
            }
            Assert.IsNotNull(heaviestFar, "no far-band entry at all.");
            Assert.AreSame(charge, heaviestFar.combo.hits[0],
                "the heaviest far-band entry is '" + heaviestFar.label + "', not a charge.");
            Assert.Greater(chargeWeightFar, otherWeightFar * 3f,
                "from the far band the charge weighs " + chargeWeightFar + " against " + otherWeightFar +
                " of everything else; it is supposed to be near-certain.");
            Assert.IsTrue(coversAggroEdge, "no charge entry reaches the aggro edge (" + Data().aggroRange +
                " m); a player far enough away would just be walked at.");

            // The leap keeps a small share so the far band is not one animation.
            bool leapFar = false;
            foreach (var e in Entries()) if (e.minRange >= 4f && Contains(e, leap)) leapFar = true;
            Assert.IsTrue(leapFar, "the leap slam is no longer thrown from range.");
        }

        [Test]
        public void TheChargeOpensIntoPressure()
        {
            var charge = Atk("Halberdier_Charge");
            int openers = 0;
            foreach (var e in Entries())
            {
                if (e.combo.hits[0] != charge || e.combo.hits.Length < 2) continue;
                openers++;
                // Closing the gap must flow into the string: the follow-up starts before the charge's
                // (effective) recovery could have ended, i.e. as a combo hit, not as a new decision.
                Assert.Less(EffectiveGap(charge), EffectiveRecovery(charge),
                    "'" + e.label + "': the gap after the charge (" + EffectiveGap(charge) +
                    " s) is no shorter than its recovery; the follow-up would not read as a combo.");
                foreach (var h in e.combo.hits)
                    Assert.IsFalse(h != charge && h.unblockable,
                        "'" + e.label + "' chains the charge into another unblockable — two pink tells " +
                        "in a row is a wall, not a phrase.");
            }
            Assert.GreaterOrEqual(openers, 2, "fewer than two entries chain the charge into a follow-up.");
        }

        [Test]
        public void TheChargeCanLandFromItsWholeBand()
        {
            // The lunge stops lungeMinDistance short and DoImpact allows range + 0.5 m: from the far
            // end of a charge entry's band, the body must still arrive inside the impact test.
            var d = Data();
            var charge = Atk("Halberdier_Charge");
            foreach (var e in Entries())
            {
                if (e.combo.hits[0] != charge) continue;
                // Past ~8 m the charge is a CLOSER: it covers its 4.7 m, stops short, and the phrase
                // carries on from wherever he stopped. Inside 8 m the shoulder must connect.
                float pickAt = Mathf.Min(e.maxRange, 8f);
                float arrives = Mathf.Max(d.lungeMinDistance, pickAt - charge.lungeDistance);
                Assert.LessOrEqual(arrives, charge.range + 0.5f,
                    "'" + e.label + "' picked at " + pickAt + " m ends the lunge " + arrives.ToString("F2") +
                    " m out, beyond the impact test's " + (charge.range + 0.5f) + " m — a whiff from its own band.");
            }
        }

        [Test]
        public void EveryPhraseIsBounded_AndMostOfTheCloseBandIsAString()
        {
            float stringWeight = 0f, singleWeight = 0f;
            foreach (var e in Entries())
            {
                float total = 0f;
                foreach (var h in e.combo.hits) total += h.windup + h.impactDelay + h.strikeDuration + EffectiveGap(h);
                Assert.LessOrEqual(total, 6f, "'" + e.label + "' runs " + total.ToString("F2") + " s before its first breath.");
                if (e.minRange < 5f)
                {
                    if (e.combo.hits.Length >= 2) stringWeight += e.weight; else singleWeight += e.weight;
                }
            }
            // By WEIGHT, not by count: the singles exist as tempo breaks, but the default pick inside
            // the band has to be a string or 'combo his attacks on you' is a label, not a behaviour.
            Assert.Greater(stringWeight, singleWeight,
                "close-band strings weigh " + stringWeight + " against " + singleWeight + " of single hits.");
        }

        [Test]
        public void TheSlamIsStillAPunishAfterTheAggressionScaling()
        {
            // The slam inherited the punish from the removed heavy (2026-09-04: its generated clip
            // never struck). Same law: the EFFECTIVE opening, after aggression, above 0.9 s.
            var heavy = Atk("Halberdier_Slam");
            float eff = EffectiveRecovery(heavy);
            Assert.GreaterOrEqual(eff, 0.9f,
                "the heavy's opening in play is " + eff.ToString("F2") + " s at aggression " + A +
                "; under 0.9 s it is a beat, not a punish.");
            foreach (var e in Entries())
                foreach (var h in e.combo.hits)
                    if (h != heavy)
                        Assert.Less(EffectiveRecovery(h), eff,
                            h.name + "'s effective recovery (" + EffectiveRecovery(h).ToString("F2") +
                            ") is not under the heavy's (" + eff.ToString("F2") + ").");
        }

        [Test]
        public void TheControllerCanCommitFromTheFarBand()
        {
            // EnemyController only ever attacked inside preferredRange + commitTolerance, so a moveset
            // entry authored for 5-18 m could never fire: he walked in and threw a sweep. The far-band
            // commit (2026-09-04) attacks from outside the band IF AND ONLY IF the moveset has an entry
            // whose range band contains the distance -- EnemyMoveset.HasEligible, never the selector's
            // fallback-to-anything. So: eligible at 7 m (the charge), at 3 m (the strings), NOT past the
            // aggro edge, and the band itself sits under where the charge takes over.
            var d = Data();
            var ms = d.moveset;
            Assert.IsTrue(ms.HasEligible(7f), "nothing is eligible at 7 m; the charge cannot fire from range.");
            Assert.IsTrue(ms.HasEligible(3f), "nothing is eligible at 3 m.");
            Assert.IsFalse(ms.HasEligible(d.aggroRange + 1f), "an entry reaches past the aggro edge.");
            Assert.Less(d.preferredRange + d.commitTolerance, 5f,
                "the near commit band overlaps the charge band; the charge would be thrown from inside the blade's reach.");
        }

        [Test]
        public void TheBladeTrail_IsWiredAndUnderTheBloomCap()
        {
            var p = Prefab();
            Assert.IsNotNull(p, "prefab missing — run VibeGame1/4b. Build Mini-Bosses");
            var trail = p.GetComponentInChildren<EnemyWeaponTrail>(true);
            if (trail == null)
                Assert.Ignore("no EnemyWeaponTrail on Legendary_Halberdier.prefab yet — run VibeGame1/4b. " +
                              "Build Mini-Bosses, then this test proves the wiring.");

            Assert.IsNotNull(trail.bladeBone, "no blade bone.");
            Assert.AreEqual("RightHand", trail.bladeBone.name, "the trail rides the wrong bone.");
            Assert.IsNotNull(trail.animator, "no Animator bound; the trail cannot read the clip.");
            Assert.Greater(Vector3.Distance(trail.bladeBaseLocal, trail.bladeTipLocal), 0.15f,
                "base and tip are on top of each other; the strip would be a line.");
            Assert.Greater(trail.attackClips.Length, 0, "no attack clips; the trail would never show.");
            Assert.AreEqual(trail.attackClips.Length, trail.attackHits.Length, "attack clip/hit tables differ in length.");
            var pv = p.GetComponentInChildren<PuppetVisuals>(true);
            CollectionAssert.AreEqual(pv.namedClips, trail.attackClips,
                "the trail's clip table is not the puppet's; its window would key off a different contact frame.");
            Assert.LessOrEqual(trail.hue.maxColorComponent, 1.0f + 1e-4f,
                "the trail hue peaks at " + trail.hue.maxColorComponent + " — over the 1.05 bloom cap a swing " +
                "would glow like a deflect.");
            Assert.LessOrEqual(trail.contactSparks, 6, "the contact sparks rival the parry's ten.");
            Assert.That(trail.leadIn + trail.tail, Is.InRange(0.15f, 0.6f), "the contact window is not a swing.");
        }

        [Test]
        public void TheContactWindow_IsAroundTheContactFrame_NotTheWindup()
        {
            // Pure: the window that decides when the strip draws.
            Assert.IsTrue(EnemyWeaponTrail.WindowContains(0.50f, 0.55f, 0.22f, 0.12f));
            Assert.IsTrue(EnemyWeaponTrail.WindowContains(0.66f, 0.55f, 0.22f, 0.12f));
            Assert.IsFalse(EnemyWeaponTrail.WindowContains(0.10f, 0.55f, 0.22f, 0.12f), "the wind-up must not draw.");
            Assert.IsFalse(EnemyWeaponTrail.WindowContains(0.90f, 0.55f, 0.22f, 0.12f), "the recovery must not draw.");
            Assert.IsTrue(EnemyWeaponTrail.WindowContains(1.50f, 0.55f, 0.22f, 0.12f), "normalised time past 1 wraps.");
        }
    }
}
