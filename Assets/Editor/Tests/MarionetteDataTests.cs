using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Hard rule 9 in assert form for THE PALE MARIONETTE: a field initialiser does nothing to an asset
    /// that already exists on disk, so these read the SHIPPED `.asset` files, not the C# defaults.
    ///
    /// <para>EditMode rather than a <c>FeatureTests</c> entry on purpose. The Marionette is a sandbox
    /// prototype and has no spawner in <c>Level_01</c>, so a play-mode test would either Skip in the
    /// canonical run or force the enemy into a level it is deliberately not in. These assertions are
    /// about the numbers on disk, which <see cref="AssetDatabase"/> can read directly and headlessly.</para>
    ///
    /// <para>The arithmetic below is the fight. If one of these fails, the fight has changed shape and
    /// the comments in <c>DataFactory</c> and <c>docs/ARCHITECTURE.md</c> have become fiction.</para>
    /// </summary>
    public class MarionetteDataTests
    {
        const string DataDir = "Assets/Data/Enemies";
        const string AttackDir = "Assets/Data/Attacks";

        // Shared constants the fight is tuned against. Read from the shipped assets where possible.
        const float CueLead = 0.28f;        // EnemyController.cueLead
        const float WindupFloor = 0.45f;    // no enemy attack wind-up may go below this
        const float GapFloor = 0.10f;       // EnemyController.NextGap floors here

        static EnemyData Data() => AssetDatabase.LoadAssetAtPath<EnemyData>(DataDir + "/Legendary_Marionette.asset");
        static EnemyAttackData Atk(string n) => AssetDatabase.LoadAssetAtPath<EnemyAttackData>(AttackDir + "/" + n + ".asset");

        [Test]
        public void Assets_Exist()
        {
            Assert.IsNotNull(Data(), "Legendary_Marionette.asset missing — run VibeGame1/3. Create Data");
            foreach (var n in new[] { "Marionette_SpinUp", "Marionette_SpinPass", "Marionette_SpinOut",
                                      "Marionette_Overhead", "Marionette_Lash" })
                Assert.IsNotNull(Atk(n), n + ".asset missing — run VibeGame1/3. Create Data");
        }

        [Test]
        public void EveryWindup_IsAtOrAboveTheFloor()
        {
            var d = Data();
            Assert.IsNotNull(d);
            foreach (var combo in d.ResolveCombos())
                foreach (var h in combo.hits)
                    Assert.GreaterOrEqual(h.windup, WindupFloor,
                        h.name + " wind-up " + h.windup + " is under the " + WindupFloor +
                        "s floor — the parry cue would have to fire before the wind-up started.");
        }

        [Test]
        public void EveryWindup_LeavesRoomForTheCue()
        {
            // The cue fires cueLead before impact, and impact is (windup end + impactDelay). The cue
            // must therefore land INSIDE the wind-up, or FireCue() is forced to fire immediately at
            // BeginWindup and the telegraph has no charge phase at all.
            foreach (var combo in Data().ResolveCombos())
                foreach (var h in combo.hits)
                    Assert.Greater(h.windup + h.impactDelay, CueLead,
                        h.name + ": cue at impact-" + CueLead + " would precede its own wind-up.");
        }

        [Test]
        public void ParriedAndUnparriedBeat_AreEqual()
        {
            // THE load-bearing number. A rhythm fight whose tempo changes depending on whether you
            // succeeded is a fight nobody can learn.
            //   unparried = windup + gap + impactDelay + strike        (Windup -> Strike -> next Windup)
            //   parried   = recoil + windup + gap + impactDelay        (OnParried -> Recover -> ResumeCombo)
            // with recoil = parryRecoilSeconds * Mathf.Lerp(1, 0.55, aggression)  [EnemyController.OnParried]
            var d = Data();
            var pass = Atk("Marionette_SpinPass");

            float gap = Mathf.Max(GapFloor, pass.comboGap * Mathf.Lerp(1f, 0.45f, d.aggression));
            float unparried = pass.windup + gap + pass.impactDelay + pass.strikeDuration;
            float recoil = d.parryRecoilSeconds * Mathf.Lerp(1f, 0.55f, d.aggression);
            float parried = recoil + pass.windup + gap + pass.impactDelay;

            Assert.AreEqual(unparried, parried, 0.005f,
                "beat drifts on a deflect: unparried=" + unparried.ToString("F3") +
                "s parried=" + parried.ToString("F3") + "s. parryRecoilSeconds must be " +
                (pass.strikeDuration / Mathf.Lerp(1f, 0.55f, d.aggression)).ToString("F4") +
                " for strikeDuration " + pass.strikeDuration + " at aggression " + d.aggression +
                " — see docs/ENGINEERING-LOG.md.");
        }

        [Test]
        public void TheBeatSitsExactlyOnTheParryContractsFloor()
        {
            // The cadence is deliberately the FASTEST this game can legally ask for, and that is a
            // statement about the parry contract rather than a taste call: the cue fires cueLead
            // before impact, so a wind-up short enough to push the passes closer together would need
            // its cue to fire before the wind-up began, and the pass would stop being parryable.
            //
            // This test is therefore INTENTIONALLY brittle. If a playtest says 0.69 s is too fast to
            // hold for nine passes, the fix is to raise the wind-up AND change this test and the
            // DataFactory comment together — deliberately, and knowing you have left the floor. What
            // must never happen is the beat drifting back up without anybody noticing.
            var d = Data();
            var pass = Atk("Marionette_SpinPass");

            Assert.AreEqual(WindupFloor, pass.windup, 0.001f,
                "the spin pass wind-up is " + pass.windup + ", not the " + WindupFloor +
                "s floor. The whole point of the cadence is that it sits ON the floor.");

            float gap = Mathf.Max(GapFloor, pass.comboGap * Mathf.Lerp(1f, 0.45f, d.aggression));
            float beat = pass.windup + gap + pass.impactDelay + pass.strikeDuration;
            Assert.AreEqual(0.69f, beat, 0.005f,
                "the spin beat is " + beat.ToString("F3") + "s; the design and every doc say 0.69s.");
        }

        [Test]
        public void TheGapIsPinnedToTheFloor()
        {
            // Half of why the beat cannot drift. NextGap is
            //   max(0.10, comboGap * lerp(1, 0.45, aggression) - parryStreak * 0.03)
            // so as long as the FIRST term is already under the floor, neither aggression nor a
            // growing parry streak can compress the gap, and the ninth pass of a phrase arrives on
            // exactly the same interval as the first. If comboGap were ever raised above the floor
            // this silently becomes false and the fight speeds up the better you play — which is the
            // single most disorienting thing a rhythm enemy can do.
            var d = Data();
            var pass = Atk("Marionette_SpinPass");
            float unfloored = pass.comboGap * Mathf.Lerp(1f, 0.45f, d.aggression);
            Assert.Less(unfloored, GapFloor,
                "comboGap " + pass.comboGap + " x " + Mathf.Lerp(1f, 0.45f, d.aggression).ToString("F3") +
                " = " + unfloored.ToString("F3") + "s is ABOVE the " + GapFloor +
                "s floor, so the gap is no longer pinned and the beat now shortens with the parry streak.");
        }

        [Test]
        public void TheSpoolUp_HandsOffOnTheCadence()
        {
            // The player must be able to lock onto the metronome from beat ONE. The spool-up is
            // allowed a longer wind-up — it is the warning that the cadence is starting — but the
            // interval from ITS blow to the first pass's blow has to be the cadence itself, which
            // means its impactDelay and strikeDuration have to match the pass's.
            var d = Data();
            var up = Atk("Marionette_SpinUp");
            var pass = Atk("Marionette_SpinPass");

            float upGap = Mathf.Max(GapFloor, up.comboGap * Mathf.Lerp(1f, 0.45f, d.aggression));
            float passGap = Mathf.Max(GapFloor, pass.comboGap * Mathf.Lerp(1f, 0.45f, d.aggression));
            float handoff = up.strikeDuration + upGap + pass.windup + pass.impactDelay;
            float beat = pass.strikeDuration + passGap + pass.windup + pass.impactDelay;

            Assert.AreEqual(beat, handoff, 0.01f,
                "spool-up hands off after " + handoff.ToString("F3") + "s but the cadence is " +
                beat.ToString("F3") + "s, so the player has to find the rhythm on beat two.");
            Assert.Greater(up.windup, pass.windup,
                "the spool-up must still be the longest wind-up in the spin, or nothing warns that " +
                "the cadence is about to start.");
        }

        [Test]
        public void TheExitBreaksTheMetronome_ButOnlyIntoABlock()
        {
            // The spin-out arrives LATE on purpose, so a player parrying the count rather than the
            // body presses early on it. Both halves of that are load-bearing:
            //   - the stretch must EXCEED parryPerfectWindow, or metronome play deflects the exit for
            //     free and the fight never asks anyone to watch the body;
            //   - it must stay INSIDE perfect + late, or metronome play eats a 26-damage blow, which
            //     punishes an otherwise reasonable read far harder than the lesson is worth.
            // Getting a block instead of a deflect is exactly the right price: it costs the posture
            // progress and the punish window, and it costs no health.
            var d = Data();
            var stats = AssetDatabase.LoadAssetAtPath<PlayerStatsData>("Assets/Data/PlayerStats.asset");
            Assert.IsNotNull(stats, "PlayerStats.asset missing — run VibeGame1/3. Create Data");

            var pass = Atk("Marionette_SpinPass");
            var exit = Atk("Marionette_SpinOut");
            float gap = Mathf.Max(GapFloor, pass.comboGap * Mathf.Lerp(1f, 0.45f, d.aggression));

            // Both measured blow-to-blow, so the two are directly comparable.
            float beat = pass.strikeDuration + gap + pass.windup + pass.impactDelay;
            float exitBeat = pass.strikeDuration + gap + exit.windup + exit.impactDelay;
            float stretch = exitBeat - beat;

            Assert.Greater(stretch, stats.parryPerfectWindow,
                "the exit arrives only " + stretch.ToString("F3") + "s late, inside the " +
                stats.parryPerfectWindow + "s perfect window — parrying on the metronome would deflect " +
                "it for free and the fight would never make anyone watch the body.");
            Assert.Less(stretch, stats.parryPerfectWindow + stats.parryLateWindow,
                "the exit arrives " + stretch.ToString("F3") + "s late, past the " +
                (stats.parryPerfectWindow + stats.parryLateWindow) + "s block window — a metronome " +
                "press would take the full hit rather than blocking it.");
        }

        [Test]
        public void ComboBreath_DoesNotStretchTheBeat()
        {
            // comboBreathSeconds floors EVERY recovery, mid-combo ones included. Above the authored
            // recovery it silently inserts dead air between passes and the beat above becomes a lie.
            var d = Data();
            Assert.LessOrEqual(d.comboBreathSeconds, Atk("Marionette_SpinPass").recovery,
                "comboBreathSeconds " + d.comboBreathSeconds + " exceeds the pass recovery and will stretch the beat.");
        }

        [Test]
        public void SixCleanDeflects_BreakIt()
        {
            // With the sword (parryPostureDamage 25) and the pass multiplier 1.4, a deflected pass is 35.
            var sword = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Data/Weapons/Sword.asset");
            Assert.IsNotNull(sword, "Sword.asset missing");
            float perPass = sword.parryPostureDamage * Atk("Marionette_SpinPass").parryPostureMultiplier;
            int deflects = Mathf.CeilToInt(Data().maxPosture / perPass);
            Assert.AreEqual(6, deflects,
                "expected exactly 6 clean deflects to break it; got " + deflects +
                " (posture=" + Data().maxPosture + " perPass=" + perPass + ")");
        }

        [Test]
        public void TheSpinPhraseIsLongerThanTheDeflectsNeededToBreakIt()
        {
            // The whole tension: a clean player breaks it BEFORE the phrase ends, a sloppy one has to
            // survive the whole thing. If the longest phrase were <= 6 hits the spin would always end
            // on its own schedule and the player's rhythm would not decide anything.
            int longest = 0;
            foreach (var c in Data().ResolveCombos())
                if (c.hits.Length > longest) longest = c.hits.Length;
            Assert.Greater(longest, 6, "longest phrase is " + longest + " hits; 6 clean deflects break it.");
        }

        [Test]
        public void TheSpinOut_IsTheBiggestPunishWindowThatIsNotAStagger()
        {
            // The out that is not parrying: a player who blocks rather than deflects still gets paid,
            // on the exit. And the stagger must dwarf even that, or breaking it is not the goal.
            var d = Data();
            float longestRecovery = 0f;
            foreach (var c in d.ResolveCombos())
                foreach (var h in c.hits)
                    if (h.recovery > longestRecovery) longestRecovery = h.recovery;

            Assert.AreEqual(Atk("Marionette_SpinOut").recovery, longestRecovery, 0.001f,
                "something other than the spin-out now has the longest recovery.");
            Assert.Greater(d.staggerSeconds, longestRecovery * 1.5f,
                "stagger " + d.staggerSeconds + "s is not a big enough step up from the " +
                longestRecovery + "s spin-out window.");
        }

        [Test]
        public void RetreatingIsAnsweredByTheFarBand()
        {
            // Backing off and waiting must not be strictly optimal. Two halves: an unblockable gated to
            // the far band that PUNISHES the retreat, and spin phrases gated so they are never started
            // from outside their own reach (a phrase is selected once and then runs to its end).
            var d = Data();
            Assert.IsNotNull(d.moveset);

            bool farUnblockable = false, spinGated = true;
            foreach (var e in d.moveset.entries)
            {
                bool isSpin = false, hasUnblockable = false;
                foreach (var h in e.combo.hits)
                {
                    if (h == null) continue;
                    if (h.name.StartsWith("Marionette_Spin")) isSpin = true;
                    if (h.unblockable) hasUnblockable = true;
                }
                if (hasUnblockable && e.minRange >= 5f) farUnblockable = true;
                if (isSpin && e.minRange < 5f && e.maxRange > 8f) spinGated = false;
            }
            Assert.IsTrue(farUnblockable, "no unblockable gated to minRange >= 5 — waiting it out would be free.");
            Assert.IsTrue(spinGated, "a spin phrase can be selected from beyond its own reach; it would whirl at nothing.");
        }

        [Test]
        public void PreferredRange_CoversAttackRange_AndEveryAttackReaches()
        {
            var d = Data();
            Assert.GreaterOrEqual(d.preferredRange, d.attackRange,
                "preferred=" + d.preferredRange + " attack=" + d.attackRange);
            // The impact test runs AFTER the lunge has travelled (FireCue commits the travel, DoImpact
            // measures at impact), so reach is range + lungeDistance. Stated that way rather than as a
            // bare `range >= preferredRange` because the latter is false for several shipped enemies
            // whose long lunges are exactly the point — but an attack that clears the bar ONLY because
            // of its lunge is a coincidence, so the two halves are asserted separately.
            foreach (var c in d.ResolveCombos())
                foreach (var h in c.hits)
                {
                    Assert.GreaterOrEqual(h.range + h.lungeDistance, d.preferredRange + d.commitTolerance,
                        h.name + ": reach " + (h.range + h.lungeDistance) + " cannot cover the commit band " +
                        (d.preferredRange + d.commitTolerance) + " — it would whiff when committed.");
                    Assert.GreaterOrEqual(h.range, d.attackRange,
                        h.name + " range " + h.range + " is under the enemy's own attackRange " + d.attackRange + ".");
                }
        }

        [Test]
        public void ThePrefab_IsAnimatedAndFullyBound()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Legendary_Marionette.prefab");
            Assert.IsNotNull(p, "prefab missing — run VibeGame1/4b. Build Mini-Bosses");

            // Not a BossController. That class raises BossDefeated, which stops the speedrun timer.
            Assert.IsNull(p.GetComponent<BossController>(), "a mini-boss must never carry BossController.");
            Assert.IsNotNull(p.GetComponent<EnemyController>());

            var pv = p.GetComponentInChildren<PuppetVisuals>(true);
            Assert.IsNotNull(pv, "no PuppetVisuals — the animated presentation is not wired.");
            Assert.IsNotNull(pv.animator, "PuppetVisuals.animator is null.");
            Assert.IsNotNull(pv.animator.runtimeAnimatorController,
                "no AnimatorController — run VibeGame1/4a then 4b.");
            Assert.IsNotNull(pv.spinRoot, "no spinRoot — the whirl would not run.");
            Assert.AreNotSame(pv.spinRoot, pv.animator.transform,
                "spinRoot must NOT be the Animator's own transform: the generic clips keep their root " +
                "curves, so that would be two writers on one channel.");

            // All seven inherited EnemyVisuals bindings, plus the URP material. A null one is silent.
            Assert.IsNotNull(pv.body); Assert.IsNotNull(pv.eye); Assert.IsNotNull(pv.weapon);
            Assert.IsNotNull(pv.lungeRoot); Assert.IsNotNull(pv.armPivot);
            Assert.IsNotNull(pv.weaponPivot); Assert.IsNotNull(pv.alertMarker);
            Assert.IsNotNull(pv.deathblowMarker);
            StringAssert.StartsWith("Universal Render Pipeline/", pv.body.sharedMaterial.shader.name,
                "a non-URP shader renders magenta.");
        }

        [Test]
        public void TheWhirlSpeedIsShippedOnThePrefab_NotLeftToAFieldInitialiser()
        {
            // Hard rule 9, for the three numbers that decide how fast the thing actually looks. These
            // used to rely on PuppetVisuals' C# field initialisers, which meant the prefab carried
            // whatever the initialiser said on the day it was last built and editing the initialiser
            // afterwards changed nothing, silently. MiniBossFactory.WireAnimatedBody now writes them.
            var pv = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Legendary_Marionette.prefab")
                        .GetComponentInChildren<PuppetVisuals>(true);
            Assert.IsNotNull(pv);

            Assert.Greater(pv.spinPeakMultiple, 3f,
                "spinPeakMultiple is " + pv.spinPeakMultiple + ". Anything at or below 3 is the OLD " +
                "cubic's saturation point and the fight is back to the slow whirl — rebuild with " +
                "VibeGame1/4b. Build Mini-Bosses.");
            Assert.That(pv.spinTailMultiple, Is.InRange(0.05f, 0.5f),
                "spinTailMultiple " + pv.spinTailMultiple + " — at 0 the body stops dead on the " +
                "alignment and the arrival reads as a separate pose beat; above ~0.5 there is not " +
                "enough deceleration left for the slowdown to BE the wind-up.");
            Assert.Greater(pv.maxDegPerFrame, 0f,
                "the alias guard is disabled; on a 30 fps machine the whirl will strobe into noise.");
        }

        [Test]
        public void ThePeakWhirlRate_IsFastButStillReadsAsRotation()
        {
            // The user-facing claim ("way faster") and the perceptual ceiling, in one place.
            // One revolution per pass, travelled in (windup + impactDelay), with the peak instant at
            // spinPeakMultiple x the average. Above roughly 90 deg per rendered frame a 2-fold
            // symmetric silhouette stops reading as rotation at all, so the guard has to leave real
            // headroom at 60 fps or it would be doing nothing.
            var pv = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Legendary_Marionette.prefab")
                        .GetComponentInChildren<PuppetVisuals>(true);
            var pass = Atk("Marionette_SpinPass");

            float passSeconds = pass.windup + pass.impactDelay;
            float average = 360f / passSeconds;
            float peak = average * pv.spinPeakMultiple;

            Assert.Greater(peak, 2500f,
                "peak whirl is " + peak.ToString("F0") + " deg/s. The brief was 'way faster'; the " +
                "version this replaced peaked at ~1350 deg/s.");
            Assert.Less(peak * (1f / 60f), 90f,
                "at 60 fps the body steps " + (peak / 60f).ToString("F0") + " deg per frame, past the " +
                "~90 deg alias threshold for a 2-fold symmetric silhouette — the spin would read as " +
                "random orientation rather than as rotation, on a machine hitting the target frame rate.");

            // And the guard must be slack at 60 fps: if it were binding there it would be flattening
            // the curve on ordinary hardware rather than only rescuing slow machines.
            Assert.AreEqual(pv.spinPeakMultiple,
                PuppetVisuals.ResolvePeak(pv.spinPeakMultiple, pv.spinTailMultiple, average, 1f / 60f, pv.maxDegPerFrame),
                0.001f, "the alias guard is clamping the peak at 60 fps, so the shipped value is a lie.");
        }

        [Test]
        public void TheAttackClipIsScaledOntoTheData_NotTheOtherWayRound()
        {
            var pv = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Legendary_Marionette.prefab")
                        .GetComponentInChildren<PuppetVisuals>(true);
            var pass = Atk("Marionette_SpinPass");

            // The speed PuppetVisuals will pick for a spin pass, computed the same way it does.
            float contact = pv.spinClipLength * pv.spinHitNormalized;
            float speed = contact / (pass.windup + pass.impactDelay);
            Assert.That(speed, Is.InRange(pv.minClipSpeed, pv.maxClipSpeed),
                "the spin clip would have to be played at x" + speed.ToString("F2") +
                " to land its contact on the data's impact, outside the allowed " +
                pv.minClipSpeed + ".." + pv.maxClipSpeed + " band. Fix the ART, not the attack.");

            // And the clip the whirl uses must be the one whose name the data's prefix will match.
            StringAssert.StartsWith(pv.spinAttackPrefix, pass.name,
                "the spin pass asset name no longer matches PuppetVisuals.spinAttackPrefix, so the " +
                "whirl would never start and the passes would play square-on.");
        }
    }
}
