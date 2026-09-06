using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// THE ARGENT HALBERDIER — the third ai_skelly_tool body, and the first whose attacks are GENERATED,
    /// per-character clips (<c>forge.py --motion</c>) rather than the four canonical ones. Hard rule 9 in
    /// assert form: every test reads the SHIPPED assets, never a C# default.
    ///
    /// <para>What is new about this body, and therefore what these tests exist to hold:</para>
    /// <list type="number">
    /// <item><b>Every attack names its clip</b> (<c>EnemyAttackData.clip</c>), and the prefab's baked
    /// clip table must carry that clip with its own length and contact frame — or the clip is stretched
    /// onto another clip's anchor and lands its blow at the wrong moment.</item>
    /// <item><b>The art's travel is data.</b> The tool bakes a thrust's or a charge's pelvis path onto
    /// the Hips bone; the importer extracts it as root motion, the Animator discards it, and the same
    /// distance ships in <c>lungeDistance</c>. <see cref="EveryLungeIsTheClipsOwnTravel"/> reads the
    /// travel back off the imported clip and holds the data to it, so neither can drift alone.</item>
    /// <item><b>The import is what the splitter says it is</b>: Generic, Hips as the motion node, XZ
    /// baked into the pose on every clip except the travelling ones. A stray reimport that lost any of
    /// that would put the mesh a metre ahead of its collider with nothing in the console.</item>
    /// </list>
    /// </summary>
    public class HalberdierDataTests
    {
        const float WindupFloor = 0.45f;   // no enemy attack wind-up may go below this
        const float CueLead = 0.28f;       // EnemyController.cueLead
        const string Fbx = "Assets/Enemies/ArgentHalberdier.fbx";

        static readonly string[] AttackNames =
        {
            "Halberdier_Sweep", "Halberdier_Thrust", "Halberdier_Slam",
            "Halberdier_Charge", "Halberdier_Kick", "Halberdier_Leap", "Halberdier_Spin",
            // Halberdier_Backswing and Halberdier_Heavy were REMOVED on 2026-09-04: their generated clips
            // never struck (the tip moved at 1-8 m/s and ended behind the body). See DataFactory.
        };

        static EnemyData Data() =>
            AssetDatabase.LoadAssetAtPath<EnemyData>(EnemyPaths.Data("Legendary_Halberdier"));
        static EnemyAttackData Atk(string n) =>
            AssetDatabase.LoadAssetAtPath<EnemyAttackData>("Assets/Data/Attacks/" + n + ".asset");
        static GameObject Prefab() =>
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Legendary_Halberdier.prefab");
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

        [Test]
        public void Assets_Exist()
        {
            Assert.IsNotNull(Data(), "Legendary_Halberdier.asset missing — run VibeGame1/3. Create Data");
            foreach (var n in AttackNames)
                Assert.IsNotNull(Atk(n), n + ".asset missing — run VibeGame1/3. Create Data");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx),
                Fbx + " missing — it is committed source art (docs/AUTHORING.md → Importing a forge model)");
            Assert.IsTrue(System.IO.File.Exists("Assets/Enemies/ArgentHalberdier.clips.json"),
                "the clip manifest is missing beside the FBX; nothing can split the take without it.");
            Assert.IsNotNull(Prefab(), "prefab missing — run VibeGame1/4a then 4b");
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
            Assert.GreaterOrEqual(d.preferredRange, d.attackRange,
                "preferred=" + d.preferredRange + " attack=" + d.attackRange);
            foreach (var h in EveryHit())
                Assert.GreaterOrEqual(h.range + h.lungeDistance, d.preferredRange + d.commitTolerance,
                    h.name + ": reach " + (h.range + h.lungeDistance) + " cannot cover the commit band " +
                    (d.preferredRange + d.commitTolerance) + " — it would whiff when committed.");
        }

        [Test]
        public void ReachIsTheCharge_NotWhereHeStands()
        {
            // REACH is still the lesson, but it moved: he no longer holds further out than the others
            // (a blade with ~1.7 m of reach cannot hit from 4 m -- 2026-09-04), he CLOSES further than
            // anything else can. The charge's lunge must be the longest in the whole attack roster, and
            // the commit band must sit at the blade: preferredRange + commitTolerance under 4 m.
            var charge = Atk("Halberdier_Charge");
            Assert.IsNotNull(charge);
            foreach (var guid in AssetDatabase.FindAssets("t:EnemyAttackData", new[] { "Assets/Data/Attacks" }))
            {
                var other = AssetDatabase.LoadAssetAtPath<EnemyAttackData>(AssetDatabase.GUIDToAssetPath(guid));
                if (other == null || other == charge) continue;
                Assert.Greater(charge.lungeDistance, other.lungeDistance,
                    other.name + " lunges " + other.lungeDistance + " m, as far as or further than the charge (" +
                    charge.lungeDistance + "); the charge is supposed to be the longest close in the game.");
            }
            Assert.Less(Data().preferredRange + Data().commitTolerance, 4f,
                "the commit band reaches " + (Data().preferredRange + Data().commitTolerance) +
                " m; the halberd's tip reaches ~1.7 m, so cuts from there land on nothing.");
        }

        [Test]
        public void EveryAttackNamesAClipTheModelShips_WithItsOwnContactFrame()
        {
            var pv = Puppet();
            Assert.IsNotNull(pv, "no PuppetVisuals on the prefab.");
            foreach (var h in EveryHit())
            {
                Assert.IsFalse(string.IsNullOrEmpty(h.clip),
                    h.name + " names no clip. Every attack on this body is a generated clip; without a " +
                    "name it falls back to the canonical AttackSwing and the art the tool made is unreachable.");
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
                // And the baked length must be the clip's real length, or the anchor lands elsewhere.
                Assert.AreEqual(ClipNamed(h.clip).length, pv.namedClipLengths[i], 0.01f,
                    h.clip + ": the baked length disagrees with the imported clip — stale prefab.");
            }
        }

        [Test]
        public void NoTwoAttacksShareAClip()
        {
            // The whole point of this body. Seven attacks, seven animations: if two collapse onto one clip
            // the silhouette stops telling them apart, which is the Revenant's failure in a new costume.
            var used = new Dictionary<string, string>();
            foreach (var h in EveryHit())
            {
                if (used.ContainsKey(h.clip))
                    Assert.Fail(h.name + " and " + used[h.clip] + " both play '" + h.clip + "'.");
                used[h.clip] = h.name;
            }
        }

        [Test]
        public void EveryClipSpeedFitsInsideTheClamp()
        {
            // PuppetVisuals scales the clip so its contact frame lands on the data's impact, but only
            // within minClipSpeed..maxClipSpeed; outside that it clamps and LOGS, and the contact no
            // longer lines up. Computed here exactly as PlayAttackClip does.
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
            // The tool's root-motion contract, honoured as data. A clip the manifest marks as
            // travelling walks the Hips forward inside the pose; this project moves the AGENT by
            // lungeDistance and cancels the pose's travel (PuppetVisuals.CompensateTravel), so the two
            // numbers must agree -- and a clip the manifest says stays put must ship a lunge of 0. The
            // lunge is straight ahead, so it is the FORWARD component that is held; the charge also
            // drifts 1.5 m sideways in the art and the lunge system has no lateral channel. A clip that
            // travels BACKWARD (the heavy steps back on its wind-up) gets 0: ApplyLunge cannot retreat.
            foreach (var h in EveryHit())
            {
                var clip = ClipNamed(h.clip);
                if (clip == null) continue;   // reported elsewhere
                bool travels = ForgeClipSplitter.ClipTravels(Fbx, h.clip);
                float forward = SampledHipsForwardTravel(clip);
                // An AUTHORED clip (no root block in the manifest: AttackSwing / Stab / Overhead) is
                // rotation-only, so its step INTO the cut is the data's own lunge, exactly as on the
                // Revenant -- any value is honest. Only generated clips bind the data to the art.
                if (!ForgeClipSplitter.ClipIsGenerated(Fbx, h.clip)) continue;
                if (travels)
                {
                    Assert.Greater(Mathf.Abs(forward), 0.2f,
                        h.clip + " is marked root.motion in the manifest but its Hips barely move (" +
                        forward.ToString("F2") + " m); the clip imported wrong or the manifest lies.");
                    Assert.AreEqual(Mathf.Max(0f, forward), h.lungeDistance, 0.15f,
                        h.name + ": lungeDistance " + h.lungeDistance + " but '" + h.clip + "' walks the Hips " +
                        forward.ToString("F2") + " m forward. The art and the data have drifted apart.");
                }
                else
                {
                    Assert.AreEqual(0f, h.lungeDistance, 0.001f,
                        h.name + ": '" + h.clip + "' does not travel in the art, so the enemy must not either.");
                    Assert.Less(Mathf.Abs(forward), 0.25f,
                        h.clip + " is not marked as travelling yet its Hips move " + forward.ToString("F2") + " m.");
                }
            }
        }

        [Test]
        public void TheTravelRoot_KeepsTheMeshOverTheCollider()
        {
            // Unity's own Generic root-node extraction was tried and measured: it moves the Hips' whole
            // transform onto the model root -- lift and yaw included -- so it was dropped for a
            // dedicated TravelRoot that PuppetVisuals writes from the Hips' XZ drift. This samples the
            // two biggest travelling clips through the SHIPPED prefab and asserts the Hips end up where
            // they started, in the prefab root's space.
            var p = (GameObject)PrefabUtility.InstantiatePrefab(Prefab());
            try
            {
                var pv = p.GetComponentInChildren<PuppetVisuals>(true);
                Assert.IsNotNull(pv.travelRoot, "no TravelRoot on the prefab; rebuild with 4b.");
                Assert.IsNotNull(pv.hipsBone, "hipsBone unbound.");
                Assert.AreNotSame(pv.travelRoot, pv.spinRoot, "TravelRoot must be its own transform.");
                Assert.AreNotSame(pv.travelRoot, pv.animator.transform, "TravelRoot must not be the Animator's transform.");
                Assert.IsFalse(pv.animator.applyRootMotion, "applyRootMotion must stay OFF; the travel is cancelled, not applied.");

                var model = pv.animator.gameObject;
                pv.CompensateTravel();
                Vector3 rest = p.transform.InverseTransformPoint(pv.hipsBone.position);
                foreach (var name in new[] { "Thrust", "ShoulderCharge", "LeapSlam" })
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
                // ...and the lift is still there: a leap that stays on the floor has lost its art.
                var leap = ClipNamed("LeapSlam");
                float yMin = 99f, yMax = -99f;
                for (int i = 0; i <= 10; i++)
                {
                    leap.SampleAnimation(model, leap.length * i / 10f);
                    float y = p.transform.InverseTransformPoint(pv.hipsBone.position).y;
                    yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
                }
                Assert.Greater(yMax - yMin, 0.3f,
                    "LeapSlam's Hips only span " + (yMax - yMin).ToString("F2") + " m of height; the lift is gone.");
            }
            finally { Object.DestroyImmediate(p); }
        }

        [Test]
        public void TheImportIsGeneric_WithNoRootNode_AndTheSkinItWasPreviewedWith()
        {
            var importer = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(ModelImporterAnimationType.Generic, importer.animationType,
                "the rig is not Generic — EnemyForgeImporter's Humanoid took over. Re-run 4a.");
            // NO root node, on purpose. On a Generic rig Unity's root node moves the Hips' whole
            // transform onto the model root (lift and yaw included) and bakes nothing into the pose;
            // set to a path, the avatar fails and the model imports with zero clips. See ForgeClipSplitter.
            Assert.IsTrue(string.IsNullOrEmpty(importer.motionNodeName),
                "motionNodeName is '" + importer.motionNodeName + "'; the travel must stay in the pose.");
            var so = new SerializedObject(importer);
            var rootBone = so.FindProperty("m_HumanDescription.m_RootMotionBoneName");
            Assert.IsTrue(rootBone == null || string.IsNullOrEmpty(rootBone.stringValue),
                "the avatar has a root motion bone; the Hips would be glued to the model root.");
            Assert.AreEqual(ModelImporterSkinWeights.Custom, importer.skinWeights, "skin weights not Custom.");
            Assert.AreEqual(8, importer.maxBonesPerVertex,
                "the tool's skin keeps up to 8 influences per vertex; 4 renormalised is a rougher skin.");

            var clips = importer.clipAnimations;
            Assert.Greater(clips.Length, 15, "fewer than 16 clips split — the generated attacks are missing.");
            foreach (var c in clips)
            {
                Assert.IsFalse(c.lockRootRotation, c.name + ": lockRootRotation must stay off (legacy flags).");
                Assert.IsTrue(c.keepOriginalOrientation && c.keepOriginalPositionY && c.keepOriginalPositionXZ,
                    c.name + ": keepOriginal* flags changed.");
                Assert.AreEqual(0, c.events.Length,
                    c.name + " carries AnimationEvents; the project writes none (timing is data-driven).");
            }
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx))
            {
                var c = o as AnimationClip;
                if (c == null || c.name.StartsWith("__")) continue;
                Assert.IsFalse(c.empty, c.name + " imported EMPTY (take-name mismatch).");
                Assert.Greater(c.length, 0.3f, c.name + " is " + c.length + " s; a 1.00 s clip is the empty signature.");
            }
        }

        [Test]
        public void ThePrefab_IsAnimatedAndFullyBound_AndTheAnimatorDoesNotApplyRootMotion()
        {
            var p = Prefab();
            Assert.IsNull(p.GetComponent<BossController>(), "a mini-boss must never carry BossController.");
            Assert.IsNotNull(p.GetComponent<EnemyController>());

            var pv = p.GetComponentInChildren<PuppetVisuals>(true);
            Assert.IsNotNull(pv, "no PuppetVisuals — the animated presentation is not wired.");
            Assert.IsNotNull(pv.animator, "PuppetVisuals.animator is null.");
            Assert.IsNotNull(pv.animator.runtimeAnimatorController,
                "no AnimatorController — run VibeGame1/4a. Split Forge Animation Clips, then 4b.");
            Assert.IsFalse(pv.animator.applyRootMotion,
                "applyRootMotion is ON: the clip would move the model on top of the lunge, doubling every " +
                "thrust and charge. The travel is data (lungeDistance); TravelRoot cancels the pose's.");
            Assert.IsTrue(string.IsNullOrEmpty(pv.spinAttackPrefix),
                "a spin prefix ('" + pv.spinAttackPrefix + "') would whirl the body under Halberdier_Spin; " +
                "this body's spin is entirely in the clip.");

            Assert.IsNotNull(pv.body); Assert.IsNotNull(pv.eye); Assert.IsNotNull(pv.weapon);
            Assert.IsNotNull(pv.lungeRoot); Assert.IsNotNull(pv.armPivot);
            Assert.IsNotNull(pv.weaponPivot); Assert.IsNotNull(pv.alertMarker);
            Assert.IsNotNull(pv.deathblowMarker);

            // The first textured forge body: its own URP material carrying the albedo, and a near-white
            // EnemyData tint so the texture actually shows through EnemyVisuals' per-frame _BaseColor.
            var mat = pv.body.sharedMaterial;
            StringAssert.StartsWith("Universal Render Pipeline/", mat.shader.name, "a non-URP shader renders magenta.");
            Assert.IsNotNull(mat.GetTexture("_BaseMap"),
                "the body material has no base map; the silver-and-gold albedo the tool painted is not on it.");
            Assert.IsTrue(mat.IsKeywordEnabled("_EMISSION"),
                "_EMISSION is off on the body material, so the parry flash cannot light it.");
            var bc = Data().bodyColor;
            Assert.Greater(Mathf.Min(bc.r, Mathf.Min(bc.g, bc.b)), 0.8f,
                "bodyColor " + Data().bodyColor + " is not near-white; it multiplies into the albedo and " +
                "would tint the silver plate.");
            Assert.Greater(Data().emission.maxColorComponent, 1.05f, "the accent is under the bloom threshold.");
        }

        [Test]
        public void TheHeavyIsTheBiggestPunishWindow_AndTheStaggerIsBigger()
        {
            var d = Data();
            float longest = 0f; string who = "";
            foreach (var h in EveryHit())
                if (h.recovery > longest) { longest = h.recovery; who = h.name; }
            Assert.AreEqual("Halberdier_Slam", who,
                "the biggest opening is now " + who + "; the slam inherited the punish lesson from the " +
                "removed heavy (its generated clip never struck).");
            Assert.Greater(d.staggerSeconds, longest * 1.5f,
                "stagger " + d.staggerSeconds + "s is not a big enough step up from the " + longest + "s recovery.");
        }

        [Test]
        public void TheTwoUnblockables_AnswerTurtlingAndKiting()
        {
            Assert.IsTrue(Atk("Halberdier_Kick").unblockable, "the kick is the designated anti-turtle.");
            Assert.IsTrue(Atk("Halberdier_Charge").unblockable, "the charge is the designated anti-kiting.");
            int n = 0;
            foreach (var h in EveryHit()) if (h.unblockable) n++;
            Assert.AreEqual(2, n, "exactly two unblockables: one for each way of refusing the fight.");

            // The charge is only ever thrown from the far band. Point-blank, a 4.5 m lunge is a body
            // check that ends inside the player and reads as a collision bug.
            var ms = Data().moveset;
            Assert.IsNotNull(ms);
            var charge = Atk("Halberdier_Charge");
            bool found = false;
            foreach (var e in ms.entries)
            {
                if (e.combo == null || e.combo.hits == null) continue;
                foreach (var h in e.combo.hits)
                    if (h == charge)
                    {
                        found = true;
                        Assert.GreaterOrEqual(e.minRange, 5f,
                            "'" + e.label + "' can pick the charge from " + e.minRange + " m.");
                    }
            }
            Assert.IsTrue(found, "no moveset entry throws the charge at all.");
        }

        [Test]
        public void EveryGeneratedClipAnchorIsMeasured_NotGuessed()
        {
            // The manifest's OnAttackHit on a GENERATED clip is the tool's guess. From play: "the
            // animations don't line up with the attack hitboxes". 4b measures the contact (a limb's
            // furthest forward reach of the pelvis) and bakes THAT; this recomputes it the same way.
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
            Assert.Greater(measured, 0, "no generated clip was checked; every Halberdier attack should be one.");
        }

        [Test]
        public void TheFollowThroughRunsAtTheAuthoredRate()
        {
            // The wind-up scale lands the contact frame; it must not carry through the swing's recovery,
            // or every attack is a slow-motion swing that snaps to idle a quarter-second after the blow.
            var pv = Puppet();
            Assert.GreaterOrEqual(pv.recoverySpeed, 1f, "the follow-through plays slower than authored.");
            Assert.Greater(pv.followThroughSeconds, 0.25f,
                "the attack clip is handed back to locomotion " + pv.followThroughSeconds + " s after the blow; " +
                "the follow-through never plays.");
        }
    }
}
