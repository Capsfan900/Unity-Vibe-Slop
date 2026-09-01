using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE WANDS — hard rule 9 in assert form. <c>WandFactory</c> runs as step <c>3b</c> inside
    /// <c>0. Rebuild Everything</c> and until now nothing asserted a single one of its outputs, so a
    /// drifted field initialiser, a hand-edit in the Inspector or a half-finished re-run would all have
    /// shipped silently.
    ///
    /// <para>Every assertion below reads the SHIPPED <c>.asset</c> on disk, never the C# default. A
    /// value written in <c>WandFactory.cs</c> is a wish; a value in <c>Assets/Data/Wands/*.asset</c> is
    /// what the game loads.</para>
    ///
    /// <para>EditMode rather than a <c>FeatureTests</c> entry because these are numbers on disk that
    /// <see cref="AssetDatabase"/> can read headlessly — no play mode, no spawned enemy, no wand
    /// actually fired. What is asserted is chosen deliberately: the fields another system dereferences
    /// (a null viewmodel prefab empties the offhand), the fields with arithmetic behind them (the hold
    /// clamp, the cooldown/damage trade), and every field where a silent <b>zero</b> disables a whole
    /// blast shape without an error anywhere. A Scatter wand with <c>splashDamage = 0</c> compiles,
    /// loads, fires, draws its full VFX, and hurts nobody but the ripostee.</para>
    /// </summary>
    public class WandDataTests
    {
        const string WandsDir = "Assets/Data/Wands";

        /// <summary>The four names <c>PrefabFactory</c> hardcodes into <c>WandController.loadout</c>.</summary>
        static readonly string[] Shipped = { "Emberlance", "Gravecall", "Stormneedle", "Voidspine" };

        // WandController.FireRiposte: hold = Mathf.Clamp(wand.recover * 0.7f, 0.16f, 0.28f)
        const float HoldFactor = 0.7f;
        const float HoldFloor = 0.16f;
        const float HoldCeiling = 0.28f;

        // WandController.FireLance: length = Mathf.Max(2f, wand.blastRadius)
        const float LanceLengthFloor = 2f;

        // The shipped bloom threshold (ProjectSetup / the volume profile). Anything under it never blooms.
        const float BloomThreshold = 1.05f;

        // M_AlertTell peaks at 3.00 and is the loudest thing in the game on purpose — it means "steel
        // will not answer this one". Nothing else may reach it.
        const float AlertTellPeak = 3.00f;

        static WandData W(string n) => AssetDatabase.LoadAssetAtPath<WandData>(WandsDir + "/" + n + ".asset");

        static WandData[] All()
        {
            var a = new WandData[Shipped.Length];
            for (int i = 0; i < Shipped.Length; i++) a[i] = W(Shipped[i]);
            return a;
        }

        static float Peak(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

        // ---------------------------------------------------------------------------------------
        // Existence and identity
        // ---------------------------------------------------------------------------------------

        [Test]
        public void EveryShippedWand_ExistsOnDisk()
        {
            foreach (var n in Shipped)
                Assert.IsNotNull(W(n), n + ".asset missing under " + WandsDir +
                    " — PrefabFactory hardcodes this exact path into WandController.loadout, so a missing " +
                    "file becomes a NULL loadout slot, and a null slot silently degrades every riposte on " +
                    "it to the bare melee execute with no error anywhere. Fix: VibeGame1/3b. Create Wands.");
        }

        [Test]
        public void TheFolderHoldsExactlyTheShippedFour()
        {
            // HudBuilder sizes the wand-select panel from FindAssets("t:WandData") in this folder, while
            // PrefabFactory fills the loadout from the four names above. A fifth asset left in the folder
            // adds a menu row that can never be selected; a missing one leaves a row-count mismatch.
            var guids = AssetDatabase.FindAssets("t:WandData", new[] { WandsDir });
            Assert.AreEqual(Shipped.Length, guids.Length,
                "found " + guids.Length + " WandData assets in " + WandsDir + ", expected " + Shipped.Length +
                ". HudBuilder builds one menu row per asset here but PrefabFactory only loads the four it " +
                "names, so any extra is an unselectable row and any missing one is a dead slot.");
        }

        [Test]
        public void IdentityFields_AreFilledAndUnique()
        {
            var seenLabel = new System.Collections.Generic.HashSet<string>();
            var seenName = new System.Collections.Generic.HashSet<string>();
            foreach (var n in Shipped)
            {
                var w = W(n);
                Assert.IsNotNull(w, n);
                Assert.AreEqual(n, w.name, "asset file " + n + ".asset carries m_Name=" + w.name +
                    " — WandFactory sets asset.name from the file name, so this means a hand-rename.");
                Assert.IsNotEmpty(w.displayName, n + " has an empty displayName — WandSelectMenu prints " +
                    "displayName.ToUpperInvariant() as the row title, so the row would be blank.");
                Assert.IsNotEmpty(w.shortLabel, n + " has an empty shortLabel — this is the HUD's only " +
                    "name for the equipped wand.");
                Assert.IsNotEmpty(w.description, n + " has an empty description — the select menu's whole " +
                    "job is telling you what the trade is before you commit to it.");
                Assert.IsTrue(seenName.Add(w.displayName), "duplicate displayName " + w.displayName +
                    " — two menu rows would read identically.");
                Assert.IsTrue(seenLabel.Add(w.shortLabel), "duplicate shortLabel " + w.shortLabel +
                    " — the HUD could not tell you which wand you hold.");
            }
        }

        [Test]
        public void EveryWand_KeepsItsViewmodelPrefab()
        {
            // WandFactory deliberately preserves viewmodelPrefab across a re-run (PrefabFactory assigns
            // it). If that preservation ever breaks, the field goes null and OffhandViewmodel.ShowWand
            // has nothing to show: the hand is EMPTY for the whole riposte, and the discharge is anchored
            // to the tip of a wand that is not there. This is the exact failure the "wands must READ in
            // the first-person view" backlog item was about.
            foreach (var n in Shipped)
            {
                var w = W(n);
                Assert.IsNotNull(w, n);
                Assert.IsNotNull(w.viewmodelPrefab, n + ".viewmodelPrefab is null — the offhand renders " +
                    "nothing and the blast looks sourceless. Fix: run VibeGame1/4. Build Prefabs AFTER " +
                    "3b (PrefabFactory is what writes this field back).");
            }
        }

        [Test]
        public void EveryWand_HasAViewmodelScaleThatCanBeSeen()
        {
            // 0.5 was already found to be "a splinter at 95 deg FOV" (WandData's own tooltip). A zero
            // here is an invisible wand with no error; the band is the readable range that survived.
            foreach (var n in Shipped)
            {
                var w = W(n);
                Assert.Greater(w.viewmodelScale, 0.55f, n + ".viewmodelScale=" + w.viewmodelScale +
                    " — under ~0.55 the wand is a splinter at the shipped FOV, and 0 is invisible with " +
                    "no warning at all.");
                Assert.Less(w.viewmodelScale, 1.0f, n + ".viewmodelScale=" + w.viewmodelScale +
                    " — at 1.0 the offhand fills the frame and hides the enemy you are impaling.");
            }
        }

        // ---------------------------------------------------------------------------------------
        // Silent zeroes: every blast shape must actually do its shape
        // ---------------------------------------------------------------------------------------

        [Test]
        public void NoBlastShape_IsSilentlyDisabled()
        {
            foreach (var n in Shipped)
            {
                var w = W(n);
                switch (w.kind)
                {
                    case WandKind.Bolt:
                        // "Fast, and it does not share." Bolt takes no branch in FireRiposte's switch, so
                        // a radius or splash here is dead data that reads as a bug in the tuning table.
                        Assert.AreEqual(0f, w.blastRadius, 0.0001f, n + " is a Bolt with blastRadius=" +
                            w.blastRadius + " — Bolt takes no branch in WandController's switch, so this " +
                            "value does nothing and lies to whoever tunes it next.");
                        Assert.AreEqual(0f, w.splashDamage, 0.0001f, n + " is a Bolt with splashDamage=" +
                            w.splashDamage + " — same: unreachable, and it contradicts 'it does not share'.");
                        break;

                    case WandKind.Scatter:
                        Assert.Greater(w.blastRadius, 0f, n + " is a Scatter with blastRadius=0 — " +
                            "OverlapSphereNonAlloc with radius 0 finds nobody, so the crowd-clear wand " +
                            "hits exactly one enemy while still paying its long windup and cooldown.");
                        Assert.Greater(w.splashDamage, 0f, n + " is a Scatter with splashDamage=0 — " +
                            "WandController.Splash early-returns on <= 0, so every bystander is found, " +
                            "shoved, and takes ZERO damage.");
                        break;

                    case WandKind.Chain:
                        Assert.Greater(w.blastRadius, 0f, n + " is a Chain with blastRadius=0 — the search " +
                            "sphere finds no further targets, so the arc has nowhere to jump.");
                        Assert.Greater(w.splashDamage, 0f, n + " is a Chain with splashDamage=0 — the arc " +
                            "still draws, jump for jump, and does nothing.");
                        Assert.GreaterOrEqual(w.chainTargets, 1, n + " is a Chain with chainTargets=" +
                            w.chainTargets + " — jumps = Min(chainTargets, candidates), so 0 means no chain.");
                        break;

                    case WandKind.Lance:
                        // FireLance floors the length at 2 m. A 0 here would not error — it would quietly
                        // become a 2 m pierce, which is barely past the victim, so the clamp must never be
                        // the thing supplying the shipped value.
                        Assert.Greater(w.blastRadius, LanceLengthFloor, n + " is a Lance with blastRadius=" +
                            w.blastRadius + " — FireLance floors the pierce length at " + LanceLengthFloor +
                            " m, so anything at or under that is the CLAMP shipping the value, not the " +
                            "tuning table, and the pierce stops inside the first body.");
                        Assert.Greater(w.splashDamage, 0f, n + " is a Lance with splashDamage=0 — " +
                            "everything on the line past the ripostee takes nothing.");
                        break;
                }
            }
        }

        [Test]
        public void EveryWand_HasAPositiveCooldown()
        {
            // CooldownFraction is `cooldownTotal > 0 ? ... : 0`, so a zero total pins the HUD bar at
            // empty forever AND removes the wand's whole cost — it becomes free on every deathblow.
            foreach (var n in Shipped)
                Assert.Greater(W(n).cooldown, 0f, n + ".cooldown=" + W(n).cooldown +
                    " — WandController.CooldownFraction returns 0 when the total is 0, so the HUD bar " +
                    "never moves and the wand fires on every single riposte for free.");
        }

        [Test]
        public void EveryCadenceBeat_IsPositive()
        {
            foreach (var n in Shipped)
            {
                var w = W(n);
                Assert.Greater(w.windup, 0f, n + ".windup=0 — WaitForSecondsRealtime(0) skips the raise " +
                    "entirely and the discharge happens on the same frame as the lunge.");
                Assert.Greater(w.recover, 0f, n + ".recover=0 — no settle, and the hold below collapses.");
                Assert.Greater(w.hitStop, 0f, n + ".hitStop=0 — the impact has no weight at all.");
                Assert.Greater(w.shake, 0f, n + ".shake=0 — likewise.");
            }
        }

        // ---------------------------------------------------------------------------------------
        // The arithmetic behind the cadence
        // ---------------------------------------------------------------------------------------

        [Test]
        public void TheStabHold_ClearsTheLegibilityFloorAndUsesItsWholeBand()
        {
            // hold = Clamp(recover * 0.7, 0.16, 0.28) is the beat the wand spends BURIED in the victim.
            // It was raised off 0.08-0.16 because ten frames at 60fps is under the threshold at which a
            // pose reads at all. Two things are asserted:
            //   1. every wand clears the floor (guaranteed by the clamp, so really: the raw value is not
            //      so far under it that the clamp is doing all the work for the whole set), and
            //   2. the set actually SPANS the band, because the design claim is "derived from the wand's
            //      own recover so a heavy wand lingers". If every wand clamps to the same number that
            //      claim is fiction and recover has no effect on the pose at all.
            float min = float.MaxValue, max = float.MinValue;
            foreach (var n in Shipped)
            {
                float hold = Mathf.Clamp(W(n).recover * HoldFactor, HoldFloor, HoldCeiling);
                min = Mathf.Min(min, hold);
                max = Mathf.Max(max, hold);
                Assert.GreaterOrEqual(hold, HoldFloor - 0.0001f, n);
                Assert.LessOrEqual(hold, HoldCeiling + 0.0001f, n);
            }
            Assert.Greater(max - min, 0.05f,
                "every wand's stab hold landed within " + (max - min).ToString("0.###") + " s of every " +
                "other (" + min.ToString("0.###") + "-" + max.ToString("0.###") + " s). The clamp band is " +
                HoldFloor + "-" + HoldCeiling + " s, i.e. recover in " +
                (HoldFloor / HoldFactor).ToString("0.###") + "-" + (HoldCeiling / HoldFactor).ToString("0.###") +
                " s; outside it the clamp ships the value and 'a heavy wand lingers' stops being true.");
        }

        [Test]
        public void EveryCooldown_OutlastsItsOwnRiposte()
        {
            // The cooldown is charged at the START of the shot precisely so the discharge is part of the
            // wait rather than added on top. If cooldown <= windup + recover that stops being a cost at
            // all: the wand is ready again before the player has control back.
            foreach (var n in Shipped)
            {
                var w = W(n);
                float committed = w.windup + w.recover;
                Assert.Greater(w.cooldown, committed, n + ": cooldown " + w.cooldown +
                    " s does not outlast its own riposte (windup " + w.windup + " + recover " + w.recover +
                    " = " + committed.ToString("0.###") + " s). The cooldown is charged at the start of " +
                    "the shot, so this makes the wand free.");
            }
        }

        [Test]
        public void TheHeaviestWindup_PaysTheLongestTax()
        {
            // The stated contract in WandFactory's comments: "the quick one: back before the next
            // stagger" through to "the heaviest hit in the set pays the longest tax". That is only a real
            // trade if windup order and cooldown order agree. If they invert, one wand strictly dominates
            // another and the loadout choice collapses to a single correct answer.
            var all = All();
            System.Array.Sort(all, (a, b) => a.windup.CompareTo(b.windup));
            for (int i = 1; i < all.Length; i++)
                Assert.Greater(all[i].cooldown, all[i - 1].cooldown,
                    all[i].displayName + " has a longer windup than " + all[i - 1].displayName +
                    " (" + all[i].windup + " vs " + all[i - 1].windup + ") but a shorter-or-equal cooldown (" +
                    all[i].cooldown + " vs " + all[i - 1].cooldown + "). Slower AND cheaper is a strictly " +
                    "dominated wand — nobody would ever pick it, and the pedestal choice stops being one.");
        }

        [Test]
        public void NoWand_StrictlyDominatesAnother()
        {
            // The general form of the test above. A wand that is at least as good on damage, splash,
            // radius and every cadence beat, and strictly better on one, makes its rival dead content.
            var all = All();
            foreach (var a in all)
                foreach (var b in all)
                {
                    if (a == b) continue;
                    bool weaklyBetter =
                        a.damage >= b.damage && a.splashDamage >= b.splashDamage &&
                        a.blastRadius >= b.blastRadius && a.windup <= b.windup &&
                        a.recover <= b.recover && a.cooldown <= b.cooldown;
                    bool strictlyBetterSomewhere =
                        a.damage > b.damage || a.splashDamage > b.splashDamage ||
                        a.blastRadius > b.blastRadius || a.windup < b.windup ||
                        a.recover < b.recover || a.cooldown < b.cooldown;
                    Assert.IsFalse(weaklyBetter && strictlyBetterSomewhere,
                        a.displayName + " dominates " + b.displayName + " on every axis — " +
                        b.displayName + " is dead content and the wand pedestal has one right answer.");
                }
        }

        // ---------------------------------------------------------------------------------------
        // The trades the descriptions promise
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Emberlance_IsTheHighestSustainedSingleTargetPayoff()
        {
            // "The reliable default ... the correct pick when you just want the kill." That is a claim
            // about damage per second of cooldown, not raw damage (Voidspine hits harder once).
            var ember = W("Emberlance");
            float best = ember.damage / ember.cooldown;
            foreach (var w in All())
            {
                if (w == ember) continue;
                float rate = w.damage / w.cooldown;
                Assert.Greater(best, rate, "Emberlance's single-target rate " + best.ToString("0.#") +
                    " dmg/s is no longer the best in the set — " + w.displayName + " is at " +
                    rate.ToString("0.#") + ". WandFactory calls Emberlance the reliable default; if that " +
                    "is false the comment and the pedestal both lie.");
            }
        }

        [Test]
        public void Gravecall_TradesSingleTargetForTheCrowd()
        {
            // The trade must be real in both directions: worse than Emberlance one-on-one, better than it
            // the moment three others are standing close.
            var grave = W("Gravecall");
            var ember = W("Emberlance");
            Assert.Less(grave.damage, ember.damage,
                "Gravecall's single-target damage " + grave.damage + " is not below Emberlance's " +
                ember.damage + " — the crowd wand would also be the duel wand.");
            Assert.Greater(grave.cooldown, ember.cooldown, "Gravecall pays no more than Emberlance in time.");

            const int Bystanders = 3;
            float graveTotal = grave.damage + grave.splashDamage * Bystanders;
            float emberTotal = ember.damage;
            Assert.Greater(graveTotal, emberTotal,
                "against " + Bystanders + " bystanders Gravecall deals " + graveTotal + " to Emberlance's " +
                emberTotal + " — the long windup buys nothing and the crowd wand is never worth taking.");
        }

        [Test]
        public void Stormneedle_HasTheLongestReach()
        {
            // "the arc reaches further than any other wand" is the ONLY axis Stormneedle wins on;
            // everything else about it is middling by design. Except the Lance, whose blastRadius is a
            // pierce LENGTH along a line and not a search radius, so it is not the same quantity.
            var storm = W("Stormneedle");
            foreach (var w in All())
            {
                if (w == storm || w.kind == WandKind.Lance) continue;
                Assert.Greater(storm.blastRadius, w.blastRadius,
                    "Stormneedle's search radius " + storm.blastRadius + " m is not the longest — " +
                    w.displayName + " reaches " + w.blastRadius + " m. Reach is the only thing " +
                    "Stormneedle is best at; without it, it is middling at everything and never picked.");
            }
        }

        [Test]
        public void Voidspine_HitsHardestAndCommitsHardest()
        {
            var voidsp = W("Voidspine");
            foreach (var w in All())
            {
                if (w == voidsp) continue;
                Assert.Greater(voidsp.damage, w.damage, "Voidspine no longer has the highest single hit.");
                Assert.Greater(voidsp.windup, w.windup, "Voidspine's windup is no longer the longest — " +
                    "the heaviest hit must be the most committal, or it is simply the best wand.");
                Assert.Greater(voidsp.knockback, w.knockback, "Voidspine no longer shoves hardest.");
            }
        }

        // ---------------------------------------------------------------------------------------
        // Colour: you identify your wand by it, and the blast has to bloom
        // ---------------------------------------------------------------------------------------

        [Test]
        public void EveryWandColour_BloomsButNeverReachesTheAlertTell()
        {
            // The colour drives PyreArc, the muzzle flare, the ring and the screen flash. Under the bloom
            // threshold the whole discharge renders as flat geometry against a near-black frame. At or
            // above the alert tell's 3.00 it starts competing with the one signal that means "steel will
            // not answer this" — the same rule WeaponTrail is held to.
            foreach (var n in Shipped)
            {
                float peak = Peak(W(n).color);
                Assert.Greater(peak, BloomThreshold, n + ".color peaks at " + peak.ToString("0.###") +
                    ", under the shipped bloom threshold " + BloomThreshold + " — the discharge would not " +
                    "bloom at all and the riposte's biggest beat renders as dull geometry.");
                Assert.Less(peak, AlertTellPeak, n + ".color peaks at " + peak.ToString("0.###") +
                    ", at or over M_AlertTell's " + AlertTellPeak + " — nothing may be as loud as the " +
                    "unblockable tell.");
                Assert.AreEqual(1f, W(n).color.a, 0.0001f, n + ".color alpha is not 1 — several of the " +
                    "VFX paths multiply straight through it.");
            }
        }

        [Test]
        public void EveryWandColour_IsTellableFromEveryOther()
        {
            // The wand you hold is identified in the offhand and in the discharge by hue alone. Hue is
            // the right measure rather than RGB distance: HDR intensity varies between wands, and after
            // the ACES tonemapper two bright colours an equal RGB distance apart can read as the same
            // washed-out white if their hues are close.
            const float MinHueDeg = 45f;
            foreach (var a in Shipped)
                foreach (var b in Shipped)
                {
                    if (a == b) continue;
                    float ha = Hue(W(a).color), hb = Hue(W(b).color);
                    float d = Mathf.Abs(ha - hb);
                    if (d > 180f) d = 360f - d;
                    Assert.GreaterOrEqual(d, MinHueDeg, a + " (" + ha.ToString("0") + " deg) and " + b +
                        " (" + hb.ToString("0") + " deg) are only " + d.ToString("0") +
                        " deg apart in hue — under " + MinHueDeg + " they read as the same wand mid-fight, " +
                        "and the colour is the only thing that identifies which one is in your hand.");
                }
        }

        /// <summary>Hue in degrees, normalised out of the HDR intensity first.</summary>
        static float Hue(Color c)
        {
            float p = Mathf.Max(0.0001f, Peak(c));
            float h, s, v;
            Color.RGBToHSV(new Color(c.r / p, c.g / p, c.b / p, 1f), out h, out s, out v);
            return h * 360f;
        }
    }
}
