using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE FLURRY BRAWLER — the fourth ai_skelly_tool body and the roster's first FLURRY enemy: unarmed,
    /// and since the v15 export (2026-09-07) a LADDER — two punches, four, eight — twelve attacks on
    /// twelve clips. Hard rule 9 in assert form: every test below reads the SHIPPED asset, never a C#
    /// field initialiser.
    ///
    /// <para>What is particular to this body, and therefore what these tests exist to hold:</para>
    /// <list type="number">
    /// <item><b>The beat is the design.</b> A string is only a parry problem if every hit inside it
    /// arrives on an interval a human can find, so <see cref="EveryBeatInsideEveryStringClearsTheFloor"/>
    /// recomputes the interval the way <c>EnemyController.NextGap</c> does — including the deflect
    /// streak, which SHORTENS the gap — and holds it above the 0.69 s parry contract floor
    /// (ARCHITECTURE → The Pale Marionette).</item>
    /// <item><b>The ladder is monotonic, and exactly one phrase breaks the bar.</b> The whole point of
    /// three rungs is that holding a longer string pays more, so
    /// <see cref="TheLadderIsMonotonic_AndTheBarrageIsTheBiggestPunish"/> and
    /// <see cref="TheEconomy_ExactlyOnePhraseBreaksTheBar"/> are the design in arithmetic. If a tuning
    /// pass flattens the ladder or lets a second phrase break the bar, the fight stops teaching
    /// anything and these say so.</item>
    /// <item><b>The art's travel is data.</b> The sidecar's <c>forward_m</c> is SOURCE travel and lies in
    /// both directions on this body, so every <c>lungeDistance</c> is held to the travel the IMPORTED
    /// clip carries. v15 is why this matters: it took ALL the travel out of <c>ShoulderCharge</c>
    /// (3.90 m in v14, none now) and out of <c>Jab1</c>, and the data had to follow.</item>
    /// <item><b>Every attack names a clip the model actually ships</b>, with its own baked length and
    /// contact frame — and the clips this fight REFUSES are refused by arithmetic, not by taste
    /// (<see cref="TheClipsThisFightRefuses_AreRefusedByArithmetic"/>), so no later pass adopts one
    /// without redoing the sum.</item>
    /// </list>
    /// </summary>
    public class FlurryBrawlerDataTests
    {
        const float WindupFloor = 0.45f;    // no enemy attack wind-up may go below this
        const float CueLead = 0.28f;        // EnemyController.cueLead
        const float BeatFloor = 0.69f;      // the parry contract's floor — the Marionette's whole fight
        const float SwordParryPosture = 25f;// WeaponData Sword.parryPostureDamage, the calibration constant
        // The longest wind-up the game ships anywhere: Drill_DelayedOverhead. Used as the ceiling when
        // asking whether a clip's contact frame could EVER be stretched onto a legal tell.
        const float LongestTellInTheGame = 1.25f;
        const string Fbx = "Assets/Enemies/FlurryBrawler.fbx";

        static readonly string[] AttackNames =
        {
            "Brawler_Jab", "Brawler_Cross", "Brawler_Uppercut", "Brawler_UppercutLeft",
            "Brawler_UppercutLoad", "Brawler_OneTwo", "Brawler_Flurry", "Brawler_Barrage",
            "Brawler_Clap", "Brawler_Hammerfist", "Brawler_Kick", "Brawler_Charge",
        };

        /// <summary>The three rungs of the ladder, in order. The fight's spine.</summary>
        static readonly string[] Ladder = { "Brawler_OneTwo", "Brawler_Flurry", "Brawler_Barrage" };

        /// <summary>Clips the model ships that this fight deliberately does NOT use. See the refusal test.</summary>
        static readonly string[] Refused = { "Hook", "Combo1", "Combo2", "ShoulderCharge" };

        static EnemyData Data() =>
            AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("Legendary_FlurryBrawler"));
        static EnemyAttackData Atk(string n) =>
            AssetDatabase.LoadAssetAtPath<EnemyAttackData>("Assets/Data/Attacks/" + n + ".asset");
        static GameObject Prefab() =>
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Legendary_FlurryBrawler.prefab");
        static PuppetVisuals Puppet() => Prefab().GetComponentInChildren<PuppetVisuals>(true);

        static IEnumerable<EnemyAttackData> EveryHit()
        {
            var seen = new HashSet<EnemyAttackData>();
            foreach (var combo in Data().ResolveCombos())
                foreach (var h in combo.hits)
                    if (h != null && seen.Add(h)) yield return h;
        }

        static AnimationClip ClipNamed(string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx))
            {
                var c = o as AnimationClip;
                if (c != null && c.name == name) return c;
            }
            return null;
        }

        /// <summary>
        /// Seconds into a clip at which its contact frame lands, computed exactly the way
        /// <c>MiniBossFactory</c> bakes it: the manifest's <c>OnAttackHit</c> for an authored clip, the
        /// measurement for a generated one. This is what <c>PuppetVisuals</c> stretches onto an attack's
        /// impact, so it is the only number the clip-speed clamp cares about.
        /// </summary>
        static float ContactSeconds(string clipName)
        {
            var clip = ClipNamed(clipName);
            if (clip == null) return 0f;
            float manifest = ForgeClipSplitter.ReadHitNormalizedTime(Fbx, clipName, 0.55f);
            string why;
            float f = ForgeClipSplitter.ClipIsGenerated(Fbx, clipName)
                ? MiniBossFactory.MeasureContactFraction(Fbx, clipName, manifest, out why)
                : manifest;
            return clip.length * Mathf.Clamp01(f);
        }

        /// <summary>
        /// The interval between one hit of a combo and the next, exactly as EnemyController produces it:
        /// the follow-up's wind-up, plus the gap (scaled by aggression, shortened by the deflect streak,
        /// floored at 0.10), plus its impact delay and its strike.
        /// </summary>
        static float Beat(EnemyAttackData from, EnemyAttackData to, float aggression, int parryStreak)
        {
            float gap = Mathf.Max(0.1f, from.comboGap * Mathf.Lerp(1f, 0.45f, aggression) - parryStreak * 0.03f);
            return to.windup + gap + to.impactDelay + to.strikeDuration;
        }

        /// <summary>Posture this phrase pays if the player deflects every deflectable hit in it, with the
        /// sword. An unblockable cannot be deflected, so it pays nothing.</summary>
        static float CleanDeflect(MovesetEntry e)
        {
            float sum = 0f;
            if (e == null || e.combo == null || e.combo.hits == null) return 0f;
            foreach (var h in e.combo.hits)
                if (h != null && !h.unblockable) sum += h.parryPostureMultiplier;
            return sum * SwordParryPosture;
        }

        [Test]
        public void Assets_Exist()
        {
            Assert.IsNotNull(Data(), "Legendary_FlurryBrawler.asset missing — run VibeGame1/3. Create Data");
            foreach (var n in AttackNames)
                Assert.IsNotNull(Atk(n), n + ".asset missing — run VibeGame1/3. Create Data");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx),
                Fbx + " missing — it is committed source art (docs/AUTHORING.md → Importing a forge model)");
            Assert.IsTrue(System.IO.File.Exists("Assets/Enemies/FlurryBrawler.clips.json"),
                "the clip manifest is missing beside the FBX; nothing can split the take without it.");
            Assert.IsNotNull(Prefab(), "prefab missing — run VibeGame1/4a then 4b");
            Assert.IsNotNull(Data().moveset, "no moveset on the shipped EnemyData.");
        }

        [Test]
        public void TheShippedNumbersAreTheDesign()
        {
            // Rule 9: these are read off the ASSET. If a field initialiser changes and this passes, the
            // asset never moved; if this fails, someone re-tuned the fight without saying so.
            var d = Data();
            Assert.AreEqual("THE FLURRY BRAWLER", d.displayName);
            Assert.AreEqual(190f, d.maxHP, 0.001f, "low HP is the block-and-punish route staying real.");
            Assert.AreEqual(160f, d.maxPosture, 0.001f, "the posture bar IS the economy — see the test below.");
            Assert.AreEqual(4f, d.staggerSeconds, 0.001f);
            Assert.AreEqual(0.80f, d.aggression, 0.001f, "every effective recovery below is written for this.");
            Assert.AreEqual(2.0f, d.preferredRange, 0.001f, "it fights INSIDE the Halberdier's band.");
            Assert.AreEqual(0.3f, d.commitTolerance, 0.001f);
            Assert.AreEqual(0.9f, d.lungeMinDistance, 0.001f);
            Assert.AreEqual(0.35f, d.comboBreathSeconds, 0.001f, "the breath after a four-beat string.");
            Assert.AreEqual(1f, d.scale, 0.001f, "its read is closeness, not height.");
            Assert.AreEqual(480, d.soulValue);
            Assert.AreEqual(16f, d.aggroRange, 0.001f);
            Assert.Less(d.moveSpeed, 5.8f,
                "the Halberdier's 5.8 m/s chase is that fight's superlative; this one closes with the leap-in.");
            Assert.GreaterOrEqual(d.preferredRange, d.attackRange,
                "preferred=" + d.preferredRange + " attack=" + d.attackRange);
            Assert.AreEqual(AttackNames.Length, CountDistinctHits(),
                "the attack roster changed size — twelve attacks on twelve clips is the shipped shape.");
        }

        static int CountDistinctHits()
        {
            int n = 0;
            foreach (var unused in EveryHit()) n++;
            return n;
        }

        [Test]
        public void EveryWindup_ClearsTheFloorAndLeavesRoomForTheCue()
        {
            foreach (var h in EveryHit())
            {
                Assert.GreaterOrEqual(h.windup, WindupFloor,
                    h.name + " wind-up " + h.windup + " is under the " + WindupFloor + "s floor.");
                Assert.Greater(h.windup + h.impactDelay, CueLead,
                    h.name + ": the cue would have to fire before its own wind-up started.");
            }
        }

        [Test]
        public void EveryAttackReaches()
        {
            var d = Data();
            foreach (var h in EveryHit())
                Assert.GreaterOrEqual(h.range + h.lungeDistance, d.preferredRange + d.commitTolerance - 0.001f,
                    h.name + ": reach " + (h.range + h.lungeDistance) + " cannot cover the commit band " +
                    (d.preferredRange + d.commitTolerance) + " — it would whiff when committed. v15 took the " +
                    "step out of Jab1, which is why the jab's RANGE carries the band now instead of its lunge.");
        }

        [Test]
        public void EveryBeatInsideEveryStringClearsTheFloor()
        {
            // THE FIGHT. A four-hit string is only a parry problem if a human can find every interval in
            // it. Recomputed the way EnemyController does, at the shipped aggression, and at a deflect
            // streak of 4 — the gap SHRINKS as the player does well, so the tightest beat this enemy can
            // ever produce is the one a player who is winning meets.
            var d = Data();
            foreach (var e in d.moveset.entries)
            {
                if (e.combo == null || e.combo.hits == null) continue;
                for (int i = 1; i < e.combo.hits.Length; i++)
                {
                    var from = e.combo.hits[i - 1];
                    var to = e.combo.hits[i];
                    if (from == null || to == null) continue;
                    foreach (int streak in new[] { 0, 4 })
                    {
                        float beat = Beat(from, to, d.aggression, streak);
                        Assert.GreaterOrEqual(beat, BeatFloor,
                            "'" + e.label + "' hit " + (i + 1) + " (" + to.name + ") arrives " +
                            beat.ToString("F3") + " s after " + from.name + " at streak " + streak +
                            " — under the " + BeatFloor + " s parry contract floor, so it is a wall, " +
                            "not a fight. See ARCHITECTURE → The Pale Marionette.");
                    }
                }
            }
        }

        [Test]
        public void TheJabBeatIsTheAdvertisedOne_AndCannotShorten()
        {
            // 0.73 s cold, 0.72 s once the gap is on its 0.10 floor — and never below that, however well
            // the player is doing. Deliberately ABOVE the Marionette's 0.69: that fight owns the floor.
            var d = Data();
            var jab = Atk("Brawler_Jab");
            Assert.AreEqual(0.732f, Beat(jab, jab, d.aggression, 0), 0.005f, "the cold jab beat moved.");
            float pressed = Beat(jab, jab, d.aggression, 8);
            Assert.AreEqual(0.72f, pressed, 0.005f,
                "the pressed beat moved; the 0.10 s gap floor is what stops a deflect streak compressing it.");
            Assert.Greater(pressed, BeatFloor,
                "the pressed beat " + pressed + " is at or under the Marionette's floor; two fights cannot " +
                "both own the fastest cadence in the game.");
        }

        [Test]
        public void TheLadderIsMonotonic_AndTheBarrageIsTheBiggestPunish()
        {
            // THE NEW DESIGN, in arithmetic. Two punches, four, eight: each rung asks the player to hold
            // the beat longer and pays them more for it, in ALL FOUR currencies at once. Flatten any one
            // of them and the escalation stops being legible — the player cannot hear a rung they are
            // not paid differently for.
            var d = Data();
            for (int i = 1; i < Ladder.Length; i++)
            {
                var lo = Atk(Ladder[i - 1]); var hi = Atk(Ladder[i]);
                Assert.Greater(hi.windup, lo.windup,
                    Ladder[i] + " does not tell longer than " + Ladder[i - 1] + "; the TELL is how the " +
                    "player knows which rung is coming and therefore how long to hold.");
                Assert.Greater(hi.damage, lo.damage, Ladder[i] + " does not hit harder than " + Ladder[i - 1]);
                Assert.Greater(hi.parryPostureMultiplier, lo.parryPostureMultiplier,
                    Ladder[i] + " does not pay more posture than " + Ladder[i - 1] + "; the longer hold " +
                    "has to buy more of the break or there is no reason to hold it.");
                Assert.Greater(hi.recovery, lo.recovery,
                    Ladder[i] + " does not open wider than " + Ladder[i - 1] + ".");
                // ...and the EFFECTIVE opening, which is the one the player actually gets (AUTHORING §3).
                float eLo = Mathf.Max(lo.recovery * Mathf.Lerp(1f, 0.35f, d.aggression), d.comboBreathSeconds);
                float eHi = Mathf.Max(hi.recovery * Mathf.Lerp(1f, 0.35f, d.aggression), d.comboBreathSeconds);
                Assert.Greater(eHi, eLo,
                    "at aggression " + d.aggression + " the openings are " + eLo.ToString("F2") + " s and " +
                    eHi.ToString("F2") + " s — the ladder is flat where the player stands.");
            }

            // The biggest opening in the fight is the top of the ladder, and it is a real window.
            float longest = 0f; string who = "";
            foreach (var h in EveryHit())
                if (h.recovery > longest) { longest = h.recovery; who = h.name; }
            Assert.AreEqual("Brawler_Barrage", who,
                "the biggest opening is " + who + "; the design says it is the end of the eight-punch " +
                "barrage, so the reward for holding the longest phrase is the chance to answer.");
            float effective = Mathf.Max(longest * Mathf.Lerp(1f, 0.35f, d.aggression), d.comboBreathSeconds);
            Assert.Greater(effective, 1.0f,
                "the barrage's punish window is " + effective.ToString("F2") + " s in play; a sword swing " +
                "is 0.44 s, so the top rung has to be worth more than one.");
            Assert.Greater(d.staggerSeconds, longest * 1.5f,
                "stagger " + d.staggerSeconds + "s is not a big enough step up from the " + longest + "s recovery.");

            // And nothing deflectable pays more posture than the top rung.
            var barrage = Atk("Brawler_Barrage");
            foreach (var h in EveryHit())
                if (!h.unblockable)
                    Assert.LessOrEqual(h.parryPostureMultiplier, barrage.parryPostureMultiplier,
                        h.name + " pays more posture than the BARRAGE does.");
        }

        [Test]
        public void TheEconomy_ExactlyOnePhraseBreaksTheBar()
        {
            // With the sword (parryPostureDamage 25) the bar is 160 and exactly ONE of the authored
            // phrases reaches it on a clean deflect: jab, cross, LOAD, BARRAGE = 161.25. That is the
            // design in one line — hold the top rung clean and the bar breaks in your hand — and it only
            // means anything while it stays unique. Two phrases that break it and the ladder is noise.
            var d = Data();
            var breakers = new List<string>();
            foreach (var e in d.moveset.entries)
            {
                float paid = CleanDeflect(e);
                if (paid >= d.maxPosture) breakers.Add(e.label + " (" + paid.ToString("F2") + ")");
            }
            Assert.AreEqual(1, breakers.Count,
                "phrases that break the " + d.maxPosture + " bar on a clean deflect: " +
                (breakers.Count == 0 ? "NONE — nothing in the fight rewards holding a whole phrase"
                                     : string.Join(" | ", breakers.ToArray())));
            StringAssert.Contains("BARRAGE", breakers[0],
                "the phrase that breaks the bar is not the top of the ladder.");

            // The older signature is deliberately NOT a break: a whole clean phrase plus one more beat.
            var jab = Atk("Brawler_Jab"); var cross = Atk("Brawler_Cross"); var flurry = Atk("Brawler_Flurry");
            float signature = SwordParryPosture *
                (jab.parryPostureMultiplier + cross.parryPostureMultiplier +
                 jab.parryPostureMultiplier + flurry.parryPostureMultiplier);
            Assert.Less(signature, d.maxPosture,
                "the signature string alone (" + signature + ") already breaks the " + d.maxPosture + " bar.");
            Assert.GreaterOrEqual(signature + SwordParryPosture * jab.parryPostureMultiplier, d.maxPosture,
                "the signature plus one more clean beat does not break " + d.maxPosture +
                "; the break is out of reach of a whole clean phrase.");
        }

        [Test]
        public void TheTwoUnblockables_AnswerTurtlingAndKiting()
        {
            Assert.IsTrue(Atk("Brawler_Kick").unblockable, "the kick is the designated anti-turtle.");
            Assert.IsTrue(Atk("Brawler_Charge").unblockable, "the leap-in is the designated anti-kiting.");
            int n = 0;
            foreach (var h in EveryHit()) if (h.unblockable) n++;
            Assert.AreEqual(2, n, "exactly two unblockables: one for each way of refusing the fight.");

            // The leap-in is only ever thrown from OUTSIDE the commit band (point-blank it is a body
            // check that ends on top of the player) and never from further than it can actually cover —
            // range + its own travel + DoImpact's 0.5 m slack. Both bounds are DERIVED, so re-tuning the
            // travel automatically re-checks the band.
            var charge = Atk("Brawler_Charge");
            var d = Data();
            float covers = charge.range + charge.lungeDistance + 0.5f;
            bool found = false;
            foreach (var e in d.moveset.entries)
            {
                if (e.combo == null || e.combo.hits == null) continue;
                foreach (var h in e.combo.hits)
                    if (h == charge)
                    {
                        found = true;
                        Assert.Greater(e.minRange, d.preferredRange + d.commitTolerance,
                            "'" + e.label + "' can pick the leap-in from inside the commit band.");
                        Assert.LessOrEqual(e.maxRange, covers + 0.001f,
                            "'" + e.label + "' can pick the leap-in from " + e.maxRange + " m, but it only " +
                            "covers " + covers.ToString("F2") + " m (range " + charge.range + " + travel " +
                            charge.lungeDistance + " + 0.5 slack). It would land in front of the player.");
                    }
            }
            Assert.IsTrue(found, "no moveset entry throws the leap-in at all.");
        }

        [Test]
        public void EveryAttackNamesAClipTheModelShips_WithItsOwnContactFrame()
        {
            var pv = Puppet();
            Assert.IsNotNull(pv, "no PuppetVisuals on the prefab.");
            foreach (var h in EveryHit())
            {
                Assert.IsFalse(string.IsNullOrEmpty(h.clip),
                    h.name + " names no clip. Without a name it falls back to the canonical AttackSwing " +
                    "and the art the tool generated for this attack is unreachable.");
                Assert.IsNotNull(ClipNamed(h.clip),
                    h.name + " names clip '" + h.clip + "' but " + Fbx + " has no such AnimationClip. " +
                    "Run VibeGame1/4a. Split Forge Animation Clips, or check the name against the manifest.");
                int i = pv.IndexOfNamedClip(h.clip);
                Assert.GreaterOrEqual(i, 0,
                    h.name + ": '" + h.clip + "' is not in the prefab's baked clip table — rebuild with 4b. " +
                    "At runtime it would fall back to the pipeline mapping with a warning.");
                Assert.Greater(pv.namedClipLengths[i], 0.05f, h.clip + " length was never baked.");
                Assert.That(pv.namedClipHits[i], Is.InRange(0.05f, 0.95f),
                    h.clip + " contact anchor " + pv.namedClipHits[i] + " is not a usable fraction of the clip.");
                Assert.AreEqual(ClipNamed(h.clip).length, pv.namedClipLengths[i], 0.01f,
                    h.clip + ": the baked length disagrees with the imported clip — stale prefab.");
            }
        }

        [Test]
        public void NoTwoAttacksShareAClip()
        {
            // Twelve attacks, twelve animations. Two attacks on one clip is two attacks with one
            // silhouette, and in a fight made of strings the silhouette is the only thing telling the
            // beats apart.
            var used = new Dictionary<string, string>();
            foreach (var h in EveryHit())
            {
                if (used.ContainsKey(h.clip))
                    Assert.Fail(h.name + " and " + used[h.clip] + " both play '" + h.clip + "'.");
                used[h.clip] = h.name;
            }
            Assert.AreEqual(AttackNames.Length, used.Count, "the attack roster changed size.");
        }

        [Test]
        public void EveryClipSpeedFitsInsideTheClamp()
        {
            // PuppetVisuals stretches the clip so its contact frame lands on the data's impact, but only
            // within minClipSpeed..maxClipSpeed; outside that it clamps and LOGS, and the punch lands at
            // a moment the animation is not throwing it. Computed exactly as PlayAttackClip does.
            //
            // This is not bookkeeping: it is the test that REFUSED the Hook clip on this body (contact at
            // 0.09 s of a 0.54 s clip needs a 0.21 s tell, and the floor is 0.45). See the refusal test.
            var pv = Puppet();
            foreach (var h in EveryHit())
            {
                int i = pv.IndexOfNamedClip(h.clip);
                if (i < 0) continue;   // reported by the test above
                float contact = pv.namedClipLengths[i] * Mathf.Clamp01(pv.namedClipHits[i]);
                float toImpact = Mathf.Max(0.05f, h.windup + h.impactDelay);
                float speed = contact / toImpact;
                Assert.That(speed, Is.InRange(pv.minClipSpeed, pv.maxClipSpeed),
                    h.name + " would need '" + h.clip + "' at x" + speed.ToString("F2") + " (contact " +
                    contact.ToString("F2") + "s onto " + toImpact.ToString("F2") + "s), outside " +
                    pv.minClipSpeed + ".." + pv.maxClipSpeed + ". Change the ATTACK's wind-up or pick " +
                    "another clip; the clamp would silently misalign the blow.");
            }
        }

        [Test]
        public void TheClipsThisFightRefuses_AreRefusedByArithmetic()
        {
            // v15 ships 32 clips and this fight uses 12 of them. FOUR are refused on purpose, and the
            // reason is a number in each case rather than a preference — so if a re-export changes the
            // number, this test fails and the decision gets made again instead of being inherited.
            var pv = Puppet();
            var named = new HashSet<string>();
            foreach (var h in EveryHit()) named.Add(h.clip);

            foreach (var n in Refused)
            {
                Assert.IsNotNull(ClipNamed(n),
                    n + " is no longer in " + Fbx + " — the refusal below is about a clip that has gone. " +
                    "Re-read the manifest and delete the entry from Refused.");
                Assert.IsFalse(named.Contains(n),
                    n + " is named by an attack now. That may well be right — but redo the arithmetic in " +
                    "this test first, because it is what says the clip could not be used.");
            }

            // HOOK — the clip-speed clamp cannot be satisfied at ANY legal wind-up. Its contact sits at
            // 0.16 of a 0.54 s clip (0.09 s in), and the slowest playback allowed is x0.4, so the tell
            // would have to be 0.21 s — under the 0.45 s wind-up floor. Shipped anyway, the visible
            // punch lands a third of a second before the blow, which is the exact bug the contact-frame
            // baking exists to prevent. THIS IS THE ONE WORTH REVISITING: its fists move at 22-25 m/s,
            // faster than anything but the jab, so if a re-export moves OnAttackHit later, or if
            // MiniBossFactory learns to measure the anchor on the VFX_Hand bones when nothing is skinned
            // to RightHand (nothing is, on this body: max weight 0.12), the Hook becomes the best
            // round-house in the roster.
            float hook = ContactSeconds("Hook");
            Assert.Less(hook / (WindupFloor + 0.08f), pv.minClipSpeed,
                "Hook's contact is now " + hook.ToString("F3") + " s in, so at the " + WindupFloor +
                " s wind-up floor it would play at x" + (hook / (WindupFloor + 0.08f)).ToString("F2") +
                " — inside the clamp. It is usable again; give it a slot.");

            // COMBO2 — the other end of the same clamp. Contact at 0.83 of a 6.5 s clip is 5.45 s in, so
            // even at the longest tell the game ships anywhere (Drill_DelayedOverhead, 1.25 s) the clip
            // would have to run at over x3.5.
            float c2 = ContactSeconds("Combo2");
            Assert.Greater(c2 / (LongestTellInTheGame + 0.08f), pv.maxClipSpeed,
                "Combo2's contact is now " + c2.ToString("F2") + " s in, which fits inside the clamp at a " +
                LongestTellInTheGame + " s tell. Reconsider it.");

            // COMBO1 — it FITS the clamp, and is still refused, on the project's own rule that a
            // generated clip is only an attack if it moves like one (AUTHORING §2b.4; ENGINEERING-LOG,
            // "Generated strike clips do not strike"). Its fists peak at 5-6 m/s against 22-31 for the
            // jabs and 14 for the uppercuts, and to fit at all it must run at nearly x3 — a five-second
            // showcase phrase compressed into one second of blur, which is Burst4's job at a third the
            // length. The measurable half of that is here; the tip speeds are in the report.
            float c1 = ContactSeconds("Combo1");
            Assert.Greater(c1 / (1.05f + 0.06f), 2.5f,
                "Combo1's contact is now " + c1.ToString("F2") + " s in; at the LOAD's 1.05 s tell it would " +
                "play at x" + (c1 / 1.11f).ToString("F2") + ", no longer a blur. Reconsider it.");

            // SHOULDERCHARGE — refused because v15 deleted the only thing it was for. It carried 3.90 m
            // of Hips travel in v14 and was this fight's answer to distance; it now carries none, so it
            // cannot hold a lunge, and Brawler_Charge moved onto Slam.
            Assert.Less(Mathf.Abs(SampledHipsForwardTravel(ClipNamed("ShoulderCharge"))), 0.1f,
                "ShoulderCharge walks the Hips again. It is the better clip for a charge if it does — " +
                "move Brawler_Charge back onto it and give Slam its own entry.");
        }

        /// <summary>The Hips' forward travel over one clip, sampled on the FBX's own hierarchy. Positive =
        /// toward +Z, the way the model faces.</summary>
        static float SampledHipsForwardTravel(AnimationClip clip)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                Transform hips = null;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Hips") hips = t;
                Assert.IsNotNull(hips, "no Hips bone in " + Fbx);
                clip.SampleAnimation(go, 0f);
                Vector3 a = go.transform.InverseTransformPoint(hips.position);
                clip.SampleAnimation(go, clip.length);
                Vector3 b = go.transform.InverseTransformPoint(hips.position);
                return b.z - a.z;
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void EveryLungeIsTheClipsOwnTravel()
        {
            // The sidecar is SOURCE travel and lies in both directions on this body, so the shipped lunge
            // is held to the travel the IMPORTED clip actually carries. FORWARD only: ApplyLunge has no
            // lateral channel and cannot retreat, so a clip that shuffles sideways or steps back (Jab2:
            // dz -0.10; Slam carries 0.89 m of lateral on top of its forward travel) ships only its
            // forward component and lets CompensateTravel keep the mesh over the collider.
            //
            // v15 moved two of these and the data followed: Jab1 0.20 -> 0 and ShoulderCharge's 3.90 m
            // off the clip entirely. If this fails, the message PRINTS Unity's own number — paste it
            // into DataFactory and re-run 3. That is how the Halberdier's 3.68 became 3.90.
            foreach (var h in EveryHit())
            {
                var clip = ClipNamed(h.clip);
                if (clip == null) continue;   // reported elsewhere
                // An AUTHORED clip (AttackSwing / Overhead / Stab / Kick) is rotation-only, so its step
                // into the blow is the data's own lunge and any value is honest.
                if (!ForgeClipSplitter.ClipIsGenerated(Fbx, h.clip)) continue;
                float forward = SampledHipsForwardTravel(clip);
                Assert.AreEqual(Mathf.Max(0f, forward), h.lungeDistance, 0.15f,
                    h.name + ": lungeDistance " + h.lungeDistance + " but '" + h.clip + "' walks the Hips " +
                    forward.ToString("F2") + " m forward. The art and the data have drifted apart.");
            }
        }

        [Test]
        public void TheLeapInIsTheLongestCloseThisBodyHas()
        {
            var charge = Atk("Brawler_Charge");
            foreach (var h in EveryHit())
                if (h != charge)
                    Assert.Less(h.lungeDistance, charge.lungeDistance,
                        h.name + " closes " + h.lungeDistance + " m, as far as the leap-in (" +
                        charge.lungeDistance + "); the leap-in is this fight's answer to distance.");
            Assert.Greater(charge.lungeDistance, 1.4f,
                "the leap-in no longer covers the gap Slam was measured at (1.48 m of Hips travel in " +
                "Blender; ~1.55 expected off Unity's import).");
        }

        [Test]
        public void TheTravelRoot_KeepsTheMeshOverTheCollider()
        {
            // The clip's Hips travel stays in the pose (Unity's root-node extraction is deliberately not
            // used on these Generic rigs) and PuppetVisuals cancels its XZ every LateUpdate. Sampled here
            // through the SHIPPED prefab on the two v15 clips that move most: Slam, which walks 1.48 m
            // forward and 0.89 m sideways and would otherwise leave the capsule entirely, and Burst8,
            // whose drift is lateral and would walk the body off you mid-barrage.
            var p = (GameObject)PrefabUtility.InstantiatePrefab(Prefab());
            try
            {
                var pv = p.GetComponentInChildren<PuppetVisuals>(true);
                Assert.IsNotNull(pv.travelRoot, "no TravelRoot on the prefab; rebuild with 4b.");
                Assert.IsNotNull(pv.hipsBone, "hipsBone unbound.");
                Assert.IsFalse(pv.animator.applyRootMotion,
                    "applyRootMotion must stay OFF; the travel is cancelled, not applied.");

                var model = pv.animator.gameObject;
                pv.CompensateTravel();
                Vector3 rest = p.transform.InverseTransformPoint(pv.hipsBone.position);
                foreach (var name in new[] { "Slam", "Burst8" })
                {
                    var clip = ClipNamed(name);
                    Assert.IsNotNull(clip, name + " missing");
                    foreach (var k in new[] { 0.5f, 1f })
                    {
                        clip.SampleAnimation(model, clip.length * k);
                        pv.CompensateTravel();
                        Vector3 at = p.transform.InverseTransformPoint(pv.hipsBone.position);
                        float driftXZ = new Vector2(at.x - rest.x, at.z - rest.z).magnitude;
                        Assert.Less(driftXZ, 0.05f,
                            name + " at " + (k * 100) + "%: the Hips are " + driftXZ.ToString("F2") +
                            " m off the collider after compensation.");
                    }
                }
            }
            finally { Object.DestroyImmediate(p); }
        }

        [Test]
        public void TheBodyStandsOnTheFloor_AndSitsOverItsCapsule()
        {
            // RE-MEASURED FOR v15, and the number that would have been silently wrong if it had been
            // inherited: the v14 mesh spanned y -0.23..2.13 and needed a 0.23 m lift to get its boots out
            // of the floor; v15 spans y -0.00..1.95 and needs NONE, so the old lift would hover it by a
            // boot's height. And the mesh centre is z +0.12 against bones at -0.01..0.02, so the body is
            // still shifted back under the capsule that gets hit — by half what it was.
            var pv = Puppet();
            var model = pv.animator.transform;
            Assert.AreEqual(0f, model.localPosition.y, 0.001f,
                "the model's lift is " + model.localPosition.y + "; the v15 mesh starts at y -0.00 so any " +
                "lift at all leaves it hovering. Rebuild with 4b.");
            Assert.AreEqual(-0.12f, model.localPosition.z, 0.001f,
                "the forward shift under the collider changed.");
        }

        [Test]
        public void ThePrefab_IsAnimatedAndFullyBound_AndCarriesNoBladeTrail()
        {
            var p = Prefab();
            Assert.IsNull(p.GetComponent<BossController>(), "a mini-boss must never carry BossController.");
            Assert.IsNotNull(p.GetComponent<EnemyController>());

            var pv = p.GetComponentInChildren<PuppetVisuals>(true);
            Assert.IsNotNull(pv, "no PuppetVisuals — the animated presentation is not wired.");
            Assert.IsNotNull(pv.animator, "PuppetVisuals.animator is null.");
            Assert.IsNotNull(pv.animator.runtimeAnimatorController,
                "no AnimatorController — run VibeGame1/4a. Split Forge Animation Clips, then 4b.");
            Assert.IsTrue(string.IsNullOrEmpty(pv.spinAttackPrefix),
                "a spin prefix ('" + pv.spinAttackPrefix + "') would whirl the body under an attack whose " +
                "clip is already turning it.");
            Assert.IsNull(p.GetComponentInChildren<EnemyWeaponTrail>(true),
                "this body is UNARMED: EnemyWeaponTrail sweeps a strip from the RightHand bone to " +
                "weaponFxPos, and half its punches are thrown with the LEFT hand. A two-handed trail is " +
                "a change to EnemyWeaponTrail and a lead call.");

            Assert.IsNotNull(pv.body); Assert.IsNotNull(pv.eye); Assert.IsNotNull(pv.weapon);
            Assert.IsNotNull(pv.lungeRoot); Assert.IsNotNull(pv.armPivot);
            Assert.IsNotNull(pv.weaponPivot); Assert.IsNotNull(pv.alertMarker);
            Assert.IsNotNull(pv.deathblowMarker);

            var mat = pv.body.sharedMaterial;
            StringAssert.StartsWith("Universal Render Pipeline/", mat.shader.name, "a non-URP shader renders magenta.");
            Assert.IsNotNull(mat.GetTexture("_BaseMap"),
                "the body material has no base map; the albedo the tool painted is not on it.");
            Assert.IsTrue(mat.IsKeywordEnabled("_EMISSION"),
                "_EMISSION is off on the body material, so the parry flash cannot light it.");
            var bc = Data().bodyColor;
            Assert.Greater(Mathf.Min(bc.r, Mathf.Min(bc.g, bc.b)), 0.8f,
                "bodyColor " + bc + " is not near-white; EnemyVisuals multiplies it into the albedo every " +
                "frame and a tint would stain the paint.");
            Assert.Greater(Data().emission.maxColorComponent, 1.05f, "the accent is under the bloom threshold.");
            // The acid green is the identity: nothing else in the roster is green, and confusing two
            // duellists at a glance is the same bug as two weapons with one silhouette.
            var e = Data().emission;
            Assert.Greater(e.g, e.r, "the accent is no longer green-dominant.");
            Assert.Greater(e.g, e.b * 2f, "the accent has drifted toward the teal the Ninja and Marionette own.");
        }

        [Test]
        public void TheImportIsGeneric_WithNoRootNode_AndEveryClipSplit()
        {
            var importer = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(ModelImporterAnimationType.Generic, importer.animationType,
                "the rig is not Generic — EnemyForgeImporter's Humanoid took over. Re-run 4a.");
            Assert.IsTrue(string.IsNullOrEmpty(importer.motionNodeName),
                "motionNodeName is '" + importer.motionNodeName + "'; the travel must stay in the pose.");
            var clips = importer.clipAnimations;
            Assert.AreEqual(32, clips.Length,
                "the v15 manifest ships 32 clips and " + clips.Length + " were split — run VibeGame1/4a. " +
                "(v14 shipped 24; the eight added are Hook, Slam, Burst8, UppercutLeft, UppercutAlt, " +
                "Clap, Combo1, Combo2.)");
            foreach (var c in clips)
                Assert.AreEqual(0, c.events.Length,
                    c.name + " carries AnimationEvents; the project writes none (timing is data-driven).");
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx))
            {
                var c = o as AnimationClip;
                if (c == null || c.name.StartsWith("__")) continue;
                Assert.IsFalse(c.empty, c.name + " imported EMPTY (take-name mismatch).");
            }
        }

        [Test]
        public void EveryGeneratedClipAnchorIsMeasured_NotGuessed()
        {
            // The manifest's OnAttackHit on a GENERATED clip is the tool's guess; 4b measures the frame
            // where a limb reaches furthest forward of the pelvis and bakes THAT. This recomputes it the
            // same way, so a stale prefab cannot ship a punch that lands off its own contact frame.
            var pv = Puppet();
            int measured = 0;
            foreach (var h in EveryHit())
            {
                if (!ForgeClipSplitter.ClipIsGenerated(Fbx, h.clip)) continue;
                int i = pv.IndexOfNamedClip(h.clip);
                if (i < 0) continue;   // reported by EveryAttackNamesAClip...
                float manifest = ForgeClipSplitter.ReadHitNormalizedTime(Fbx, h.clip, 0.55f);
                string why;
                float expected = MiniBossFactory.MeasureContactFraction(Fbx, h.clip, manifest, out why);
                Assert.AreEqual(expected, pv.namedClipHits[i], 0.011f,
                    h.clip + ": baked anchor " + pv.namedClipHits[i] + " is not what the measurement gives (" +
                    expected + ": " + why + "). Rebuild with 4b.");
                measured++;
            }
            Assert.Greater(measured, 4, "most of this enemy's attacks should be on generated clips.");
        }
    }
}
