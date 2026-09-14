using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>
    /// Creates (or overwrites) the wand ScriptableObjects used by the riposte.
    /// Idempotent, following the same GetOrCreate pattern as <see cref="DataFactory"/>.
    ///
    /// Split out of DataFactory deliberately so wand tuning can be re-run on its own without
    /// resetting weapon, enemy and item balance.
    /// </summary>
    public static class WandFactory
    {
        const string WandsDir = "Assets/Data/Wands";

        [MenuItem("VibeGame1/3b. Create Wands")]
        public static void CreateAll()
        {
            DataFactory.EnsureFolder(WandsDir);

            // Emberlance — the reliable default. Fast enough to keep the exchange moving, and the
            // highest single-target payoff, so it is the correct pick when you just want the kill.
            Wand("Emberlance", w =>
            {
                w.shortLabel = "EMBER";
                w.description = "A gold-cored rod that spits a single lance of fire. Fast, and it does not share.";
                w.color = Hdr("#FFD98A", 2.6f);
                w.viewmodelScale = 0.72f;
                w.kind = WandKind.Bolt;
                w.damage = 220f; w.blastRadius = 0f; w.splashDamage = 0f;
                w.windup = 0.20f; w.recover = 0.22f; w.cooldown = 3.5f;   // the quick one: back before the next stagger
                w.knockback = 3.0f; w.hitStop = 0.10f; w.shake = 0.30f;
                // SPELLBOOK SIGIL (2026-09-13). Inscriptions show on the page sigil (and on the orb for the
                // length of a riposte). Emberlance: a MOLTEN shell - heat wobble, the inner field RISING,
                // six embers climbing through it. The one rig whose motion is upward.
                w.orb.shape = SpellOrbShape.Molten;
                w.orb.detail = Hdr("#FF6A1A", 1f);          // ember, under bloom: fire on the page, not a tell
                w.orb.spinDegreesPerSecond = 0f;
                w.orb.motionRate = 0.55f; w.orb.motionAmplitude = 0.8f;
                w.orb.shellRim = 1.8f; w.orb.shellSwirl = 0.70f; w.orb.shellFlow = 1.0f;
                w.orb.shellWobble = 0.6f; w.orb.shellDark = 0f;
            });

            // Gravecall — slow and committal, but it clears a crowd. The trade is real: the long
            // windup is time you are standing still with enemies still swinging.
            Wand("Gravecall", w =>
            {
                w.shortLabel = "GRAVE";
                w.description = "Vents a cloud of grave-rot. Slow to bring to bear, but nothing nearby is spared.";
                w.color = Hdr("#7FE04A", 2.4f);
                w.viewmodelScale = 0.82f;
                w.kind = WandKind.Scatter;
                w.damage = 120f; w.blastRadius = 7.0f; w.splashDamage = 90f;
                w.windup = 0.38f; w.recover = 0.34f; w.cooldown = 7.0f;   // crowd clear is worth a long wait
                w.knockback = 5.0f; w.hitStop = 0.14f; w.shake = 0.40f;
                // Gravecall: SKULL MIST - two dark sockets and a jaw on the core, three wisps SINKING out
                // of it, the inner field drifting down. Green like Rebound, but rot sinks and a fan spins.
                w.orb.shape = SpellOrbShape.SkullMist;
                w.orb.detail = Hdr("#C8FFB0", 1f);
                w.orb.spinDegreesPerSecond = 0f;
                w.orb.motionRate = 0.30f; w.orb.motionAmplitude = 0.6f;
                w.orb.shellRim = 2.4f; w.orb.shellSwirl = 0.60f; w.orb.shellFlow = -0.6f;
                w.orb.shellWobble = 0.15f; w.orb.shellDark = 0f;
            });

            // Stormneedle — middling everything, but the arc reaches further than any other wand.
            Wand("Stormneedle", w =>
            {
                w.shortLabel = "STORM";
                w.description = "The charge leaps from throat to throat. Three others feel it before it dies.";
                w.color = Hdr("#7FD4FF", 2.6f);
                w.viewmodelScale = 0.66f;
                w.kind = WandKind.Chain;
                w.damage = 150f; w.blastRadius = 9.0f; w.splashDamage = 70f; w.chainTargets = 3;
                w.windup = 0.28f; w.recover = 0.26f; w.cooldown = 5.5f;   // middling, like everything else about it
                w.knockback = 1.5f; w.hitStop = 0.12f; w.shake = 0.34f;
                // Stormneedle: NEEDLE ARCS - four needles that re-strike tangent to the core seven times a
                // second, white-hot for a third of each interval. The only stochastic motion in the set;
                // a crackle is not a spin. 7 Hz is the idle ceiling on purpose.
                w.orb.shape = SpellOrbShape.NeedleArcs;
                w.orb.detail = Hdr("#FFFFFF", 1f);
                w.orb.spinDegreesPerSecond = 0f;
                w.orb.motionRate = 7f; w.orb.motionAmplitude = 0.6f;
                w.orb.shellRim = 3.2f; w.orb.shellSwirl = 0.25f; w.orb.shellFlow = 0f;
                w.orb.shellWobble = 0f; w.orb.shellDark = 0f;
            });

            // Voidspine — the heaviest cadence in the set. Highest damage and it pierces, but the
            // windup is long enough that it is a genuine commitment.
            Wand("Voidspine", w =>
            {
                w.shortLabel = "VOID";
                w.description = "Opens a seam in the air and drives it through whatever stands in the line.";
                w.color = Hdr("#B87AFF", 2.4f);
                w.viewmodelScale = 0.70f;
                w.kind = WandKind.Lance;
                w.damage = 260f; w.blastRadius = 14.0f; w.splashDamage = 110f;
                w.windup = 0.46f; w.recover = 0.40f; w.cooldown = 9.0f;   // the heaviest hit in the set pays the longest tax
                w.knockback = 6.0f; w.hitStop = 0.16f; w.shake = 0.45f;
                // Voidspine: VOID RIM - the shell darkens its centre so the core reads as pulled inward,
                // and eight spines fall from a slowly turning rim into it. Violet like the Deflect Sigil,
                // but a rim of spines falling in is not a gem beating, and they never share an anchor.
                w.orb.shape = SpellOrbShape.VoidRim;
                w.orb.detail = Hdr("#7A3CFF", 1f);
                w.orb.spinDegreesPerSecond = 20f;
                w.orb.motionRate = 0.45f; w.orb.motionAmplitude = 0.7f;
                w.orb.shellRim = 1.6f; w.orb.shellSwirl = 0.40f; w.orb.shellFlow = 0f;
                w.orb.shellWobble = 0f; w.orb.shellDark = 0.85f;
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WandFactory] Created/updated 4 wands under " + WandsDir);
        }

        /// <summary>
        /// Reset to class defaults, then apply the tuning. Matches DataFactory's behaviour: a re-run
        /// is authoritative, so a field removed from the table here goes back to its code default
        /// rather than silently keeping a stale Inspector value.
        /// </summary>
        static WandData Wand(string name, System.Action<WandData> configure)
        {
            var asset = DataFactory.GetOrCreate<WandData>($"{WandsDir}/{name}.asset");

            // Preserve the viewmodel prefab — PrefabFactory assigns it, and it must survive a re-run.
            var prefab = asset.viewmodelPrefab;

            var fresh = ScriptableObject.CreateInstance<WandData>();
            EditorUtility.CopySerialized(fresh, asset);
            Object.DestroyImmediate(fresh);

            asset.name = name;
            asset.displayName = name;
            asset.viewmodelPrefab = prefab;
            configure(asset);

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static Color Hdr(string hex, float intensity)
        {
            var c = DataFactory.Hex(hex) * intensity;
            c.a = 1f;
            return c;
        }
    }
}
