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
    /// four-beat strings on a 0.73 s beat, eight attacks on eight clips. Hard rule 9 in assert form:
    /// every test below reads the SHIPPED asset, never a C# field initialiser.
    ///
    /// <para>What is particular to this body, and therefore what these tests exist to hold:</para>
    /// <list type="number">
    /// <item><b>The beat is the design.</b> A string is only a parry problem if every hit inside it
    /// arrives on an interval a human can find, so <see cref="EveryBeatInsideEveryStringClearsTheFloor"/>
    /// recomputes the interval the way <c>EnemyController.NextGap</c> does — including the deflect
    /// streak, which SHORTENS the gap — and holds it above the 0.69 s parry contract floor
    /// (ARCHITECTURE → The Pale Marionette).</item>
    /// <item><b>The economy is arithmetic, not vibes.</b> The posture bar is sized so that the signature
    /// string deflected clean plus one more beat breaks it, and nothing less does.</item>
    /// <item><b>The art's travel is data.</b> The sidecar's <c>forward_m</c> is SOURCE travel and on this
    /// body it is 1.18-1.24× under the rig on the attack clips and 0.90× over it on the locomotion
    /// clips — no blanket correction is legal. Every <c>lungeDistance</c> is held to the Hips travel
    /// sampled off the imported clip, so the data and the art cannot drift apart.</item>
    /// <item><b>Every attack names a clip the model actually ships</b>, with its own baked length and
    /// contact frame. A generated clip is unreachable without the name, and a name the model does not
    /// carry falls back to the canonical swing with a warning nobody reads.</item>
    /// </list>
    /// </summary>
    public class FlurryBrawlerDataTests
    {
        const float WindupFloor = 0.45f;    // no enemy attack wind-up may go below this
        const float CueLead = 0.28f;        // EnemyController.cueLead
        const float BeatFloor = 0.69f;      // the parry contract's floor — the Marionette's whole fight
        const float SwordParryPosture = 25f;// WeaponData Sword.parryPostureDamage, the calibration constant
        const string Fbx = "Assets/Enemies/FlurryBrawler.fbx";

        static readonly string[] AttackNames =
        {
            "Brawler_Jab", "Brawler_Cross", "Brawler_Uppercut", "Brawler_OneTwo",
            "Brawler_Flurry", "Brawler_Hammerfist", "Brawler_Kick", "Brawler_Charge",
        };

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
        /// The interval between one hit of a combo and the next, exactly as EnemyController produces it:
        /// the follow-up's wind-up, plus the gap (scaled by aggression, shortened by the deflect streak,
        /// floored at 0.10), plus its impact delay and its strike.
        /// </summary>
        static float Beat(EnemyAttackData from, EnemyAttackData to, float aggression, int parryStreak)
        {
            float gap = Mathf.Max(0.1f, from.comboGap * Mathf.Lerp(1f, 0.45f, aggression) - parryStreak * 0.03f);
            return to.windup + gap + to.impactDelay + to.strikeDuration;
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
            Assert.AreEqual(1f, d.scale, 0.001f, "its read is width, not height.");
            Assert.AreEqual(480, d.soulValue);
            Assert.AreEqual(16f, d.aggroRange, 0.001f);
            Assert.Less(d.moveSpeed, 5.8f,
                "the Halberdier's 5.8 m/s chase is that fight's superlative; this one closes with the shoulder.");
            Assert.GreaterOrEqual(d.preferredRange, d.attackRange,
                "preferred=" + d.preferredRange + " attack=" + d.attackRange);
            Assert.Greater(d.lungeMinDistance, 0.85f,
                "the 3.90 m charge stops lungeMinDistance short; under 0.85 m it ends inside the player " +
                "(enemy radius 0.45 + player capsule 0.4) and reads as a collision bug.");
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
                    (d.preferredRange + d.commitTolerance) + " — it would whiff when committed.");
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
        public void TheEconomyIsTheSignatureStringPlusOneBeat()
        {
            // With the sword (parryPostureDamage 25) a deflected jab or cross is 28.75 and the flurry is
            // 50. Deflect the whole signature and it is not enough; one more clean beat of anything is.
            var d = Data();
            var jab = Atk("Brawler_Jab"); var cross = Atk("Brawler_Cross"); var flurry = Atk("Brawler_Flurry");
            float signature = SwordParryPosture *
                (jab.parryPostureMultiplier + cross.parryPostureMultiplier +
                 jab.parryPostureMultiplier + flurry.parryPostureMultiplier);
            Assert.Less(signature, d.maxPosture,
                "the signature string alone (" + signature + ") already breaks the " + d.maxPosture +
                " bar; then holding the phrase is not the skill the fight is asking for.");
            float cheapestNextBeat = SwordParryPosture * jab.parryPostureMultiplier;
            Assert.GreaterOrEqual(signature + cheapestNextBeat, d.maxPosture,
                "the signature plus one more clean beat (" + (signature + cheapestNextBeat) +
                ") does not break " + d.maxPosture + "; the break is out of reach of a whole clean phrase.");

            // And the flurry is the biggest single deflect in the fight — the payoff for the hit that is
            // hardest to hold, at the end of the longest string.
            foreach (var h in EveryHit())
                if (!h.unblockable)
                    Assert.LessOrEqual(h.parryPostureMultiplier, flurry.parryPostureMultiplier,
                        h.name + " pays more posture than the FLURRY does.");
        }

        [Test]
        public void ThePunishIsTheEndOfTheFlurry_AndTheStaggerIsBigger()
        {
            var d = Data();
            float longest = 0f; string who = "";
            foreach (var h in EveryHit())
                if (h.recovery > longest) { longest = h.recovery; who = h.name; }
            Assert.AreEqual("Brawler_Flurry", who,
                "the biggest opening is " + who + "; the design says it is the end of the flurry, so the " +
                "reward for holding the whole phrase is the chance to answer.");
            // Effective, at the shipped aggression — the number the player actually gets (AUTHORING §3:
            // "write the raw number for the effective opening you want, and assert the effective one").
            float effective = Mathf.Max(longest * Mathf.Lerp(1f, 0.35f, d.aggression), d.comboBreathSeconds);
            Assert.Greater(effective, 0.8f,
                "the flurry's punish window is " + effective.ToString("F2") + " s in play; a sword swing " +
                "is 0.44 s, so under ~0.8 s there is no window at all.");
            Assert.Greater(d.staggerSeconds, longest * 1.5f,
                "stagger " + d.staggerSeconds + "s is not a big enough step up from the " + longest + "s recovery.");
        }

        [Test]
        public void TheTwoUnblockables_AnswerTurtlingAndKiting()
        {
            Assert.IsTrue(Atk("Brawler_Kick").unblockable, "the kick is the designated anti-turtle.");
            Assert.IsTrue(Atk("Brawler_Charge").unblockable, "the charge is the designated anti-kiting.");
            int n = 0;
            foreach (var h in EveryHit()) if (h.unblockable) n++;
            Assert.AreEqual(2, n, "exactly two unblockables: one for each way of refusing the fight.");

            // The charge is only ever thrown from outside the melee band. Point-blank, 3.90 m of travel
            // is a body check that ends on top of the player and reads as a bug.
            var charge = Atk("Brawler_Charge");
            var d = Data();
            bool found = false;
            foreach (var e in d.moveset.entries)
            {
                if (e.combo == null || e.combo.hits == null) continue;
                foreach (var h in e.combo.hits)
                    if (h == charge)
                    {
                        found = true;
                        Assert.GreaterOrEqual(e.minRange, 3.6f,
                            "'" + e.label + "' can pick the charge from " + e.minRange + " m.");
                        Assert.Greater(e.minRange, d.preferredRange + d.commitTolerance,
                            "'" + e.label + "' can pick the charge from inside the commit band.");
                    }
            }
            Assert.IsTrue(found, "no moveset entry throws the charge at all.");
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
            // Eight attacks, eight animations. Two attacks on one clip is two attacks with one
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
            // The sidecar is SOURCE travel and lies in both directions on this body (1.18-1.24× under on
            // the attack clips, 0.90× over on Walk/Run), so the shipped lunge is held to the travel the
            // IMPORTED clip actually carries. FORWARD only: ApplyLunge has no lateral channel and cannot
            // retreat, so a clip that shuffles sideways (Burst4: dx -0.21, dz +0.02) or steps back must
            // ship a lunge of 0 and let CompensateTravel keep the mesh over the collider.
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
        public void TheChargeIsTheLongestCloseThisBodyHas()
        {
            var charge = Atk("Brawler_Charge");
            foreach (var h in EveryHit())
                if (h != charge)
                    Assert.Less(h.lungeDistance, charge.lungeDistance,
                        h.name + " closes " + h.lungeDistance + " m, as far as the charge (" +
                        charge.lungeDistance + "); the charge is this fight's answer to distance.");
            Assert.Greater(charge.lungeDistance, 3f,
                "the charge no longer covers the gap it was measured at (3.90 m of Hips travel).");
        }

        [Test]
        public void TheTravelRoot_KeepsTheMeshOverTheCollider()
        {
            // The clip's Hips travel stays in the pose (Unity's root-node extraction is deliberately not
            // used on these Generic rigs) and PuppetVisuals cancels its XZ every LateUpdate. Sampled here
            // through the SHIPPED prefab on the two clips that move most — including Burst4, whose drift
            // is LATERAL and would otherwise walk the body out of the capsule mid-flurry.
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
                foreach (var name in new[] { "ShoulderCharge", "Burst4" })
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
        public void TheBodyIsLiftedOutOfTheFloor_AndSitsOverItsCapsule()
        {
            // The measurement that would have been silently wrong if it had been assumed: this mesh
            // spans y -0.23..2.13, so without the lift it stands buried to the ankle. And the mesh
            // centre is z +0.24 while every bone is between 0.00 and 0.24, so the body is shifted back
            // under the capsule that gets hit (the Halberdier's -0.22, measured again here).
            var pv = Puppet();
            var model = pv.animator.transform;
            Assert.AreEqual(0.23f, model.localPosition.y, 0.001f,
                "the model's lift is " + model.localPosition.y + "; 0.23 m is what puts its boots on the " +
                "collider base rather than through it. Rebuild with 4b.");
            Assert.AreEqual(-0.20f, model.localPosition.z, 0.001f,
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
            Assert.AreEqual(24, clips.Length,
                "the manifest ships 24 clips and " + clips.Length + " were split — run VibeGame1/4a.");
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
