using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// Persistent, presentation-only offhand spellbook. The book is built once by
    /// <c>SpellbookFactory</c> and remains in the hand whether the player has an item or not; runtime
    /// inventory updates only choose what the fixed orb geometry SHOWS.
    ///
    /// <para>Three reads, each truthful about a different thing:</para>
    /// <list type="bullet">
    /// <item><b>The orb above the pages</b> is the FRONT carried spell — the thing E will cast. Its core
    /// is the one part of the book allowed over the bloom threshold, its glass shell carries the hue
    /// without ever blooming (so gold stays gold), and a per-spell signature rig gives it a silhouette and
    /// a motion nobody has to read a colour to recognise. With nothing carried the core dims under bloom
    /// and the rig goes away: a bright orb always means "castable".</item>
    /// <item><b>The eight rune bars</b> light one per carried slot in that spell's colour, the front one
    /// brightest, the rest dim bone. They are the queue.</item>
    /// <item><b>The small sigil on the left page</b> is the selected riposte inscription, with the same
    /// rig language at a smaller scale. It is always present, never blooms, and brightens with the riposte
    /// charge.</item>
    /// </list>
    ///
    /// <para>The book has no gameplay authority. Consumers capture an accepted cast, request its pose,
    /// then restore it. Keeping that seam here means an inventory update during a cast cannot replace,
    /// shrink, or recolour the already-committed visual.</para>
    ///
    /// <para>All persistent geometry is supplied by the prefab. This component allocates its one
    /// property block in Awake and only writes cached transforms/renderers thereafter. Motion uses
    /// <see cref="TimeScaleController.PlayerDelta"/> so a player-held prop continues through hitstop
    /// without running while the player is paused. ONE WRITER PER MATERIAL CHANNEL: this component owns
    /// every orb renderer's emission; nothing else may write it.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SpellbookVisual : MonoBehaviour
    {
        /// <summary>Ceiling on any single channel of the carried orb's core. Under M_AlertTell's 3.00 and
        /// the deathblow mark's 2.60: the book is near-field presentation, not a tell.</summary>
        public const float OrbEmissionPeak = 2.1f;
        /// <summary>Rec.709 luminance every carried spell's core is normalised to. Equal luminance is what
        /// makes a pale gold and a saturated violet read as the same brightness; max-channel
        /// normalisation pushed gold's three channels all past 1 and the tonemapper made it white.</summary>
        public const float OrbEmissionLuminance = 1.3f;
        /// <summary>Core multiplier with no carried spell: an ember of the inscription, under bloom.</summary>
        public const float OrbEmptyMultiplier = 0.42f;
        /// <summary>Core multiplier during the cast pose (the peak clamp still binds).</summary>
        public const float OrbCastMultiplier = 1.18f;
        /// <summary>Luminance of the page sigil's core. Peak-capped at 1.0: an inscription never blooms.</summary>
        public const float SigilLuminance = 0.7f;
        /// <summary>Rune bar luminance for the front (selected) carried spell, then the rest of the queue.</summary>
        public const float RuneSelectedLuminance = 0.95f;
        public const float RuneCarriedLuminance = 0.5f;
        /// <summary>Emission on an empty rune bar (times the bone colour).</summary>
        public const float RuneEmptyEmission = 0.10f;
        /// <summary>Peak channel for every rig part and rune bar. Decoration is luminous, never blooming.</summary>
        public const float DetailPeak = 1.0f;
        public const float DefaultCastSeconds = 0.32f;

        /// <summary>One signature rig: a root toggled by shape, the transforms the animation drives and
        /// the renderers whose emission this component writes. Layout per shape is documented in
        /// <c>SpellbookFactory.BuildShape</c>; <see cref="AnimateShape"/> is the only reader.</summary>
        [System.Serializable]
        public class ShapeRig
        {
            public SpellOrbShape shape;
            public Transform root;
            public Transform[] parts;
            public Renderer[] renderers;
        }

        /// <summary>A core + glass shell + one rig per shape. The carried orb and the page sigil are both
        /// one of these at different scales, so any spell can be shown on either.</summary>
        [System.Serializable]
        public class OrbRig
        {
            public Transform anchor;
            public Renderer core;
            public Renderer shell;
            public ShapeRig[] shapes;
            public float scale = 1f;
            [System.NonSerialized] public SpellOrbShape shown = SpellOrbShape.None;
            [System.NonSerialized] public bool shownValid;
        }

        [Header("Generated anchors")]
        public Transform bookRoot;
        /// <summary>Hand anchor. Its Grip* name is consumed by the existing viewmodel hand solver.</summary>
        public Transform gripAnchor;
        /// <summary>Where a later cast effect originates. Deliberately camera-left of the aim lane.</summary>
        public Transform castOrigin;
        public Transform orbAnchor;

        [Header("Generated reads")]
        public OrbRig carriedOrb;
        public OrbRig inscriptionSigil;
        public Transform runeRing;
        public Renderer[] runeRenderers;

        [Header("Generated geometry")]
        public Transform[] pagePivots;
        public Vector3[] pageRestEuler;
        public Transform[] floatingPagePivots;
        public Vector3[] floatingPageOrigins;

        [Header("Authored motion")]
        public float pageFlutterDegrees = 7f;
        public float pageFlutterSpeed = 2.2f;
        public float floatingPageRadius = 0.018f;
        public float floatingPageSpeed = 1.45f;
        public float orbBobMetres = 0.012f;
        public float orbBobSpeed = 2.15f;
        public float orbPulseScale = 0.08f;
        public float runeSpinDegreesPerSecond = 10f;
        public Vector3 castBookOffset = new Vector3(0.035f, 0.018f, 0.055f);
        public Vector3 castBookEuler = new Vector3(-8f, 10f, -4f);

        [Header("Empty-book read")]
        [ColorUsage(true, true)] public Color emptyOrbColor = new Color(0.45f, 0.35f, 0.72f, 1f);
        [ColorUsage(true, true)] public Color emptyRuneColor = new Color(0.72f, 0.66f, 0.52f, 1f);
        [Tooltip("Shell character with nothing to show. No rig, quiet glass.")]
        public SpellOrbProfile emptyProfile = new SpellOrbProfile();

        public WandData SelectedSpell { get; private set; }
        public ItemData FrontItem { get; private set; }
        public ItemData CapturedCastItem { get; private set; }
        public Color DisplayColor { get; private set; }
        public Color CapturedCastColor { get; private set; }
        public bool IsCasting { get; private set; }
        /// <summary>0..1 riposte wind-up, pushed by OffhandViewmodel. Presentation only.</summary>
        public float Charge { get; private set; }
        /// <summary>The carried queue as last published by ItemsChanged. Drives the rune bars.</summary>
        public ItemData[] HeldItems { get; private set; }
        public Vector3 CastOriginWorldPosition => castOrigin != null ? castOrigin.position : transform.position;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        static readonly int SwirlColorId = Shader.PropertyToID("_SwirlColor");
        static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        static readonly int SwirlStrengthId = Shader.PropertyToID("_SwirlStrength");
        static readonly int FlowId = Shader.PropertyToID("_Flow");
        static readonly int WobbleId = Shader.PropertyToID("_Wobble");
        static readonly int DarkId = Shader.PropertyToID("_Dark");
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int ChargeId = Shader.PropertyToID("_Charge");

        MaterialPropertyBlock mpb;
        Vector3 bookRootRestPosition;
        Quaternion bookRootRestRotation;
        Vector3 orbAnchorRestPosition;
        Vector3 orbAnchorRestScale;
        float phase;
        float castSecondsRemaining;
        float castPose;
        SpellOrbProfile capturedProfile;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            CacheAuthoredTransforms();
            DisplayColor = ResolveDisplayColor(FrontItem, SelectedSpell, emptyOrbColor);
            CapturedCastColor = DisplayColor;
            ApplyAll();
        }

        void OnEnable()
        {
            GameEvents.ItemsChanged += SetHeldItems;
        }

        void OnDisable()
        {
            GameEvents.ItemsChanged -= SetHeldItems;
        }

        /// <summary>
        /// Selects the permanent inscription. It owns the page sigil, and it is the orb's fallback hue
        /// (dimmed, rig-less) whenever the FIFO inventory is empty. No part of the book hierarchy is
        /// recreated or rescaled.
        /// </summary>
        public void SetSelectedSpell(WandData spell)
        {
            SelectedSpell = spell;
            ApplyLiveColorUnlessCasting();
        }

        /// <summary>
        /// Supplies the front (FIFO) item, if one exists. Its colour and rig take precedence over the
        /// permanent inscription because it is the object the player will spend next.
        /// </summary>
        public void SetFrontItem(ItemData item)
        {
            FrontItem = item;
            // The queue is published separately (ItemsChanged); until it arrives the front item alone is
            // the truth, so the first rune bar never lags the orb by a frame.
            if (item == null) HeldItems = null;
            else if (HeldItems == null || HeldItems.Length == 0 || HeldItems[0] != item) HeldItems = new[] { item };
            ApplyLiveColorUnlessCasting();
        }

        /// <summary>The whole carried queue, front first. Read from ItemsChanged; public for tests.</summary>
        public void SetHeldItems(ItemData[] items)
        {
            HeldItems = items;
        }

        /// <summary>0..1 riposte wind-up. The rigs spin up and the shell brightens; the core never
        /// crosses its peak clamp. Presentation only.</summary>
        public void SetCharge(float value)
        {
            Charge = Mathf.Clamp01(value);
        }

        /// <summary>Captures the current visible item/spell hue after a cast has actually been accepted.</summary>
        public void CaptureAcceptedCast()
        {
            CaptureAcceptedCast(FrontItem);
        }

        /// <summary>
        /// Captures an explicit accepted item. Passing the item that was accepted is intentional: the
        /// caller may remove it from the FIFO immediately after this call without the cast snapping to
        /// the next item in the book. A null item is the riposte: the book casts its inscription, so the
        /// orb wears the inscription's colour and rig for the length of the pose.
        /// </summary>
        public void CaptureAcceptedCast(ItemData acceptedItem)
        {
            CapturedCastItem = acceptedItem;
            CapturedCastColor = acceptedItem != null
                ? acceptedItem.color
                : ResolveDisplayColor(null, SelectedSpell, emptyOrbColor);
            capturedProfile = acceptedItem != null ? acceptedItem.orb
                : (SelectedSpell != null ? SelectedSpell.orb : emptyProfile);
        }

        /// <summary>
        /// Starts the short, visual-only cast presentation. It contains no coroutine, spawned VFX, or
        /// gameplay effect; a later combat/item caller owns those consequences.
        /// </summary>
        public void PlayCastPose(float seconds = DefaultCastSeconds)
        {
            if (CapturedCastColor.a <= 0f) CaptureAcceptedCast();
            IsCasting = true;
            castSecondsRemaining = Mathf.Max(0.01f, seconds);
            castPose = 1f;
            ApplyAll();
        }

        /// <summary>Explicit cancellation/settle path for interrupted casts and owner teardown.</summary>
        public void CancelAndRestore()
        {
            IsCasting = false;
            castSecondsRemaining = 0f;
            CapturedCastItem = null;
            capturedProfile = null;
            CapturedCastColor = ResolveDisplayColor(FrontItem, SelectedSpell, emptyOrbColor);
            ApplyLiveColorUnlessCasting();
        }

        /// <summary>Alias for integrations that finish a successful cast instead of cancelling one.</summary>
        public void RestoreAfterCast()
        {
            CancelAndRestore();
        }

        // ---- pure rules (editor tests prove these without a player scene) --------------------------

        /// <summary>
        /// The colour rule is pure so editor tests can prove the FIFO priority without a player scene.
        /// HDR source colours are preserved here; <see cref="OrbEmission"/> applies the render budget.
        /// </summary>
        public static Color ResolveDisplayColor(ItemData frontItem, WandData selectedSpell, Color fallback)
        {
            if (frontItem != null) return frontItem.color;
            if (selectedSpell != null) return selectedSpell.color;
            return fallback;
        }

        /// <summary>A castable spell is bright; an empty book is an ember. Nothing carried never blooms.</summary>
        public static float ResolveDisplayMultiplier(ItemData frontItem)
        {
            return frontItem != null ? 1f : OrbEmptyMultiplier;
        }

        /// <summary>The rig the carried orb shows: the front item's, or none. An empty orb has no silhouette.</summary>
        public static SpellOrbShape ResolveDisplayShape(ItemData frontItem)
        {
            return frontItem != null && frontItem.orb != null ? frontItem.orb.shape : SpellOrbShape.None;
        }

        /// <summary>
        /// Normalises an arbitrary item/spell colour into the carried orb's light budget BY LUMINANCE,
        /// then clamps the peak channel. Every spell lands at the same perceived brightness and keeps
        /// its hue; the clamp only binds on deep violets, and binds proportionally so they stay violet.
        /// </summary>
        public static Color OrbEmission(Color source, float multiplier = 1f)
        {
            return Emission(source, OrbEmissionLuminance * Mathf.Max(0f, multiplier), OrbEmissionPeak);
        }

        /// <summary>The page sigil's core: luminance-normalised and capped under the bloom threshold.</summary>
        public static Color SigilEmission(Color source, float charge)
        {
            return Emission(source, SigilLuminance * (1f + 0.5f * Mathf.Clamp01(charge)), DetailPeak);
        }

        /// <summary>Which rune bar is how bright: the front carried spell, the rest of the queue, or empty.</summary>
        public static float RuneLuminance(int index, int heldCount)
        {
            if (index < 0 || index >= heldCount) return 0f;
            return index == 0 ? RuneSelectedLuminance : RuneCarriedLuminance;
        }

        /// <summary>A rune bar in a carried spell's colour at the queue luminance, peak-capped at 1.0.</summary>
        public static Color RuneEmission(Color source, float luminance)
        {
            return Emission(source, luminance, DetailPeak);
        }

        /// <summary>Luminance-normalise, then scale the whole colour down if any channel exceeds the cap.</summary>
        public static Color Emission(Color source, float luminance, float peakCap)
        {
            float lum = Mathf.Max(0.0001f, SpellOrbProfile.Luminance(source));
            float k = luminance / lum;
            Color c = new Color(source.r * k, source.g * k, source.b * k, 1f);
            float max = c.maxColorComponent;
            if (max > peakCap) c = new Color(c.r * peakCap / max, c.g * peakCap / max, c.b * peakCap / max, 1f);
            return c;
        }

        // ---- internals -----------------------------------------------------------------------------

        void CacheAuthoredTransforms()
        {
            if (bookRoot != null)
            {
                bookRootRestPosition = bookRoot.localPosition;
                bookRootRestRotation = bookRoot.localRotation;
            }
            if (orbAnchor != null)
            {
                orbAnchorRestPosition = orbAnchor.localPosition;
                orbAnchorRestScale = orbAnchor.localScale;
            }
        }

        void ApplyLiveColorUnlessCasting()
        {
            if (IsCasting) return;
            DisplayColor = ResolveDisplayColor(FrontItem, SelectedSpell, emptyOrbColor);
            CapturedCastColor = DisplayColor;
            ApplyAll();
        }

        /// <summary>Writes every read once, immediately, so a change is never dark for a frame.</summary>
        void ApplyAll()
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            float breath = 1f;
            ApplyCarriedOrb(breath);
            ApplyInscriptionSigil();
            ApplyRunes(breath);
        }

        SpellOrbProfile CarriedProfile()
        {
            if (IsCasting) return capturedProfile ?? emptyProfile;
            return FrontItem != null && FrontItem.orb != null ? FrontItem.orb : emptyProfile;
        }

        SpellOrbShape CarriedShape()
        {
            if (IsCasting) return capturedProfile != null ? capturedProfile.shape : SpellOrbShape.None;
            return ResolveDisplayShape(FrontItem);
        }

        void ApplyCarriedOrb(float breath)
        {
            if (carriedOrb == null) return;
            Color hue = IsCasting ? CapturedCastColor : DisplayColor;
            float multiplier = IsCasting ? OrbCastMultiplier : ResolveDisplayMultiplier(FrontItem);
            // Charge pushes toward the cast level, never past the clamp.
            multiplier *= 1f + 0.18f * Charge;
            SpellOrbProfile profile = CarriedProfile();
            SpellOrbShape shape = CarriedShape();
            bool castable = shape != SpellOrbShape.None;
            WriteEmission(carriedOrb.core, OrbEmission(hue, breath * multiplier));
            ApplyRig(carriedOrb, profile, shape, hue, castable ? 1f : 0.5f, breath);
        }

        void ApplyInscriptionSigil()
        {
            if (inscriptionSigil == null) return;
            Color hue = SelectedSpell != null ? SelectedSpell.color : emptyOrbColor;
            SpellOrbProfile profile = SelectedSpell != null && SelectedSpell.orb != null ? SelectedSpell.orb : emptyProfile;
            WriteEmission(inscriptionSigil.core, SigilEmission(hue, Charge));
            ApplyRig(inscriptionSigil, profile, profile.shape, hue, 1f, 1f);
        }

        void ApplyRunes(float breath)
        {
            if (runeRenderers == null) return;
            int count = HeldItems != null ? HeldItems.Length : 0;
            for (int i = 0; i < runeRenderers.Length; i++)
            {
                Renderer r = runeRenderers[i];
                if (r == null) continue;
                float lum = RuneLuminance(i, count);
                Color emission;
                if (lum > 0f && HeldItems[i] != null)
                    emission = RuneEmission(HeldItems[i].color, lum * (0.9f + 0.1f * breath));
                else
                    emission = emptyRuneColor * RuneEmptyEmission;
                WriteEmission(r, emission);
            }
        }

        /// <summary>Shows one rig on an orb (toggling only on change), aligns the shell to world up,
        /// writes the shell's glass parameters and animates the shown rig's parts.</summary>
        void ApplyRig(OrbRig rig, SpellOrbProfile profile, SpellOrbShape shape, Color hue, float shellOpacity, float breath)
        {
            if (rig == null) return;
            if (profile == null) profile = emptyProfile;

            if (!rig.shownValid || rig.shown != shape)
            {
                if (rig.shapes != null)
                    for (int i = 0; i < rig.shapes.Length; i++)
                    {
                        ShapeRig s = rig.shapes[i];
                        if (s == null || s.root == null) continue;
                        bool on = s.shape == shape && shape != SpellOrbShape.None;
                        if (s.root.gameObject.activeSelf != on) s.root.gameObject.SetActive(on);
                    }
                rig.shown = shape;
                rig.shownValid = true;
            }

            Vector3 localUp = rig.anchor != null ? rig.anchor.InverseTransformDirection(Vector3.up) : Vector3.up;
            if (localUp.sqrMagnitude < 0.0001f) localUp = Vector3.up;
            Quaternion upRot = Quaternion.FromToRotation(Vector3.up, localUp.normalized);

            float chargeK = Mathf.Clamp01(Charge + castPose * 0.5f);
            if (rig.shell != null)
            {
                rig.shell.transform.localRotation = upRot;
                WriteShell(rig.shell, SpellOrbProfile.PeakNormalised(hue, DetailPeak),
                    SpellOrbProfile.PeakNormalised(profile.detail, DetailPeak), profile.shellRim,
                    profile.shellSwirl, profile.shellFlow, profile.shellWobble, profile.shellDark, chargeK, shellOpacity);
            }

            if (shape == SpellOrbShape.None || rig.shapes == null) return;
            for (int i = 0; i < rig.shapes.Length; i++)
            {
                ShapeRig s = rig.shapes[i];
                if (s != null && s.shape == shape) { AnimateShape(s, profile, upRot, rig.scale, chargeK, breath); break; }
            }
        }

        /// <summary>
        /// The eight motion signatures. Part layouts are the factory's contract (see
        /// <c>SpellbookFactory.BuildShape</c>). Every idle amplitude is small on purpose: this is the
        /// parry quadrant, and a rig that whips around reads as a wind-up.
        /// </summary>
        void AnimateShape(ShapeRig s, SpellOrbProfile p, Quaternion upRot, float scale, float chargeK, float breath)
        {
            if (s.parts == null) return;
            float speed = 1f + chargeK * 2.5f;
            float spin = phase * p.spinDegreesPerSecond * speed;
            float beat = phase * p.motionRate * speed;
            float amp = p.motionAmplitude;
            Color detail = SpellOrbProfile.PeakNormalised(p.detail, DetailPeak);
            Transform[] parts = s.parts;
            Renderer[] rends = s.renderers;
            switch (s.shape)
            {
                case SpellOrbShape.Crescent:
                {
                    // parts[0] orbit pivot (the crescent hangs off it), parts[1] the barb.
                    if (parts.Length > 0 && parts[0] != null)
                    {
                        parts[0].localRotation = upRot * Quaternion.AngleAxis(spin, Vector3.up) * Quaternion.Euler(28f, 0f, 0f);
                        parts[0].localPosition = upRot * (Vector3.up * (Mathf.Sin(beat * Mathf.PI * 2f) * amp * 0.008f * scale));
                    }
                    if (parts.Length > 1 && parts[1] != null)
                        parts[1].localRotation = Quaternion.Euler(0f, 0f, 40f + 10f * Mathf.Sin(beat * Mathf.PI * 4f) * amp);
                    WriteAll(rends, detail * (0.8f + 0.2f * breath));
                    break;
                }
                case SpellOrbShape.FanRings:
                {
                    // parts[0] flat ring, parts[1] tilted ring; they counter-rotate on their own axes.
                    float bob = Mathf.Sin(beat * Mathf.PI * 2f) * amp * 0.005f * scale;
                    if (parts.Length > 0 && parts[0] != null)
                    {
                        parts[0].localRotation = upRot * Quaternion.AngleAxis(spin, Vector3.up);
                        parts[0].localPosition = upRot * (Vector3.up * bob);
                    }
                    if (parts.Length > 1 && parts[1] != null)
                    {
                        parts[1].localRotation = upRot * Quaternion.Euler(58f, 0f, 0f) * Quaternion.AngleAxis(-spin * 0.8f, Vector3.up);
                        parts[1].localPosition = upRot * (Vector3.up * -bob);
                    }
                    WriteAll(rends, detail * (0.75f + 0.25f * breath));
                    break;
                }
                case SpellOrbShape.DiamondSeal:
                {
                    // parts[0] gem pivot (beats), parts[1] seal plate (counter-turns). A sharp attack and
                    // a slow decay: a heartbeat, not a sine, so it reads as a beat rather than a breath.
                    float cycle = Mathf.Repeat(beat, 1f);
                    float pulse = Mathf.Exp(-cycle * 5f);
                    if (parts.Length > 0 && parts[0] != null)
                    {
                        parts[0].localRotation = upRot * Quaternion.AngleAxis(spin, Vector3.up);
                        parts[0].localScale = Vector3.one * (1f + amp * 0.16f * pulse);
                    }
                    if (parts.Length > 1 && parts[1] != null)
                        parts[1].localRotation = upRot * Quaternion.AngleAxis(-spin * 0.35f, Vector3.up);
                    if (rends != null)
                        for (int i = 0; i < rends.Length; i++)
                            WriteEmission(rends[i], detail * (i == 0 ? 0.65f + 0.35f * pulse : 0.4f + 0.45f * pulse));
                    break;
                }
                case SpellOrbShape.SwordGlyph:
                {
                    // parts[0] slow orbit, parts[1] the sword tumbling end over end about its radial axis.
                    if (parts.Length > 0 && parts[0] != null)
                        parts[0].localRotation = upRot * Quaternion.AngleAxis(spin * 0.2f, Vector3.up);
                    if (parts.Length > 1 && parts[1] != null)
                        parts[1].localRotation = Quaternion.Euler(spin, 0f, 0f);
                    WriteAll(rends, detail * (0.8f + 0.2f * Mathf.Sin(beat * Mathf.PI * 2f) * amp));
                    break;
                }
                case SpellOrbShape.Molten:
                {
                    // parts[i] embers rising through the shell on staggered loops, fading and shrinking at
                    // both ends so nothing pops. Renderer i belongs to part i.
                    int n = parts.Length;
                    float radius = 0.03f * scale;
                    for (int i = 0; i < n; i++)
                    {
                        Transform e = parts[i];
                        if (e == null) continue;
                        float cycle = Mathf.Repeat(beat * (0.8f + 0.15f * i) + i / (float)n, 1f);
                        float fade = Mathf.Sin(cycle * Mathf.PI);
                        float x = Mathf.Sin(cycle * 7f + i) * 0.009f * scale * amp;
                        float z = Mathf.Cos(cycle * 5f + i * 2f) * 0.009f * scale * amp;
                        e.localPosition = upRot * new Vector3(x, Mathf.Lerp(-radius, radius * 1.3f, cycle), z);
                        e.localScale = Vector3.one * (0.15f + 0.85f * fade);
                        if (rends != null && i < rends.Length) WriteEmission(rends[i], detail * (0.05f + 0.95f * fade));
                    }
                    break;
                }
                case SpellOrbShape.SkullMist:
                {
                    // parts[0..2] wisps sinking out of the core and thinning as they go (shell material, so
                    // they fade by opacity, not by a black fleck); parts[3..] sockets and jaw are static.
                    for (int i = 0; i < 3 && i < parts.Length; i++)
                    {
                        Transform w = parts[i];
                        if (w == null) continue;
                        float cycle = Mathf.Repeat(beat * (0.7f + 0.2f * i) + i / 3f, 1f);
                        float x = Mathf.Sin(cycle * 4f + i * 2.1f) * 0.02f * scale * amp;
                        float z = Mathf.Cos(cycle * 3f + i) * 0.02f * scale * amp;
                        w.localPosition = upRot * new Vector3(x, Mathf.Lerp(0.012f, -0.062f, cycle) * scale, z);
                        w.localRotation = upRot;
                        w.localScale = Vector3.one * (0.6f + 1.0f * cycle);
                        if (rends != null && i < rends.Length)
                            WriteShell(rends[i], detail, detail, 1.4f, 0.8f, -0.4f, 0f, 0f, chargeK, (1f - cycle) * 0.7f);
                    }
                    break;
                }
                case SpellOrbShape.NeedleArcs:
                {
                    // parts[i] needles: each re-strikes on its own tick, landing tangent to the core at a
                    // hashed point, bright for the first third of the interval and dim after.
                    float radius = 0.031f * scale;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        Transform needle = parts[i];
                        if (needle == null) continue;
                        float local = beat + i * 0.37f;
                        float tick = Mathf.Floor(local);
                        float within = local - tick;
                        float h1 = Hash(tick * 12.9898f + i * 78.233f);
                        float h2 = Hash(tick * 39.346f + i * 11.135f);
                        float h3 = Hash(tick * 7.517f + i * 3.611f);
                        Vector3 dir = new Vector3(h1 * 2f - 1f, h2 * 2f - 1f, h3 * 2f - 1f);
                        if (dir.sqrMagnitude < 0.01f) dir = Vector3.up;
                        dir.Normalize();
                        Vector3 tangent = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                        needle.localPosition = dir * radius;
                        needle.localRotation = Quaternion.FromToRotation(Vector3.up, tangent);
                        bool lit = within < 0.35f;
                        needle.localScale = new Vector3(1f, (lit ? 1f : 0.55f) * (0.6f + 0.4f * amp), 1f);
                        if (rends != null && i < rends.Length) WriteEmission(rends[i], detail * (lit ? 1f : 0.18f));
                    }
                    break;
                }
                case SpellOrbShape.VoidRim:
                {
                    // parts[i] spines on a slowly turning rim, each sliding inward and brightening as it
                    // falls, shrinking to nothing at the core before it resets at the rim.
                    int n = parts.Length;
                    float outer = 0.072f * scale;
                    float inner = 0.028f * scale;
                    for (int i = 0; i < n; i++)
                    {
                        Transform spine = parts[i];
                        if (spine == null) continue;
                        float cycle = Mathf.Repeat(beat + i / (float)n, 1f);
                        Vector3 dir = upRot * (Quaternion.AngleAxis(i * (360f / n) + spin, Vector3.up) * Vector3.right);
                        spine.localPosition = dir * Mathf.Lerp(outer, inner, cycle * amp + cycle * (1f - amp) * 0.6f);
                        spine.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                        spine.localScale = Vector3.one * (1f - 0.6f * cycle);
                        if (rends != null && i < rends.Length) WriteEmission(rends[i], detail * (0.3f + 0.7f * cycle));
                    }
                    break;
                }
            }
        }

        static float Hash(float x)
        {
            float v = Mathf.Sin(x) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        void WriteAll(Renderer[] rends, Color emission)
        {
            if (rends == null) return;
            for (int i = 0; i < rends.Length; i++) WriteEmission(rends[i], emission);
        }

        void WriteEmission(Renderer r, Color emission)
        {
            if (r == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            emission.a = 1f;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionId, emission);
            mpb.SetColor(BaseColorId, Color.black);
            r.SetPropertyBlock(mpb);
        }

        void WriteShell(Renderer r, Color rim, Color swirl, float rimPower, float swirlStrength, float flow,
            float wobble, float dark, float charge, float opacity)
        {
            if (r == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(RimColorId, rim);
            mpb.SetColor(SwirlColorId, swirl);
            mpb.SetFloat(RimPowerId, Mathf.Max(0.5f, rimPower));
            mpb.SetFloat(SwirlStrengthId, Mathf.Clamp01(swirlStrength));
            mpb.SetFloat(FlowId, flow);
            mpb.SetFloat(WobbleId, Mathf.Clamp01(wobble));
            mpb.SetFloat(DarkId, Mathf.Clamp01(dark));
            mpb.SetFloat(ChargeId, Mathf.Clamp01(charge));
            mpb.SetFloat(OpacityId, Mathf.Clamp01(opacity));
            r.SetPropertyBlock(mpb);
        }

        void LateUpdate()
        {
            float dt = TimeScaleController.PlayerDelta;
            if (dt <= 0f) return;

            phase += dt;
            if (IsCasting)
            {
                castSecondsRemaining -= dt;
                if (castSecondsRemaining <= 0f) CancelAndRestore();
            }
            castPose = Mathf.MoveTowards(castPose, IsCasting ? 1f : 0f, dt * 8f);

            AnimateBook(castPose);
            AnimatePages();
            AnimateOrb();
        }

        void AnimateBook(float pose)
        {
            if (bookRoot == null) return;
            // Only the generated BookRoot poses. The prefab's root scale is never written at runtime.
            bookRoot.localPosition = Vector3.Lerp(bookRootRestPosition, bookRootRestPosition + castBookOffset, pose);
            bookRoot.localRotation = Quaternion.Slerp(bookRootRestRotation,
                bookRootRestRotation * Quaternion.Euler(castBookEuler), pose);
        }

        void AnimatePages()
        {
            if (pagePivots != null && pageRestEuler != null)
            {
                int count = Mathf.Min(pagePivots.Length, pageRestEuler.Length);
                for (int i = 0; i < count; i++)
                {
                    Transform page = pagePivots[i];
                    if (page == null) continue;
                    float flutter = Mathf.Sin(phase * pageFlutterSpeed + i * 0.91f) * pageFlutterDegrees;
                    Vector3 euler = pageRestEuler[i];
                    euler.z += flutter;
                    euler.y += Mathf.Sin(phase * (pageFlutterSpeed * 0.61f) + i) * pageFlutterDegrees * 0.22f;
                    page.localRotation = Quaternion.Euler(euler);
                }
            }

            if (floatingPagePivots == null || floatingPageOrigins == null) return;
            int floatingCount = Mathf.Min(floatingPagePivots.Length, floatingPageOrigins.Length);
            for (int i = 0; i < floatingCount; i++)
            {
                Transform page = floatingPagePivots[i];
                if (page == null) continue;
                float a = phase * (floatingPageSpeed + i * 0.13f) + i * 2.08f;
                Vector3 origin = floatingPageOrigins[i];
                page.localPosition = origin + new Vector3(Mathf.Cos(a) * floatingPageRadius,
                    Mathf.Sin(a * 1.37f) * floatingPageRadius * 0.65f,
                    Mathf.Sin(a) * floatingPageRadius * 0.5f);
                page.localRotation = Quaternion.Euler(0f, 18f * Mathf.Sin(a), 18f * Mathf.Cos(a * 1.2f));
            }
        }

        void AnimateOrb()
        {
            if (orbAnchor != null)
            {
                orbAnchor.localPosition = orbAnchorRestPosition + Vector3.back * (Mathf.Sin(phase * orbBobSpeed) * orbBobMetres);
                float pulse = 1f + Mathf.Sin(phase * orbBobSpeed * 1.6f) * orbPulseScale + castPose * 0.20f;
                orbAnchor.localScale = orbAnchorRestScale * pulse;
            }
            // The rune ring turns in the page plane, slowly: alive, not a wind-up.
            if (runeRing != null) runeRing.localRotation = Quaternion.Euler(0f, 0f, phase * runeSpinDegreesPerSecond);

            float breath = 0.92f + 0.08f * Mathf.Sin(phase * orbBobSpeed * 1.6f);
            ApplyCarriedOrb(breath);
            ApplyInscriptionSigil();
            ApplyRunes(breath);
        }
    }
}
