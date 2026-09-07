using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The swing arc, drawn as a ribbon behind the blade tip — and ONLY during the strike leg of the
    /// attack, so the trail doubles as the player's read on the active window.
    ///
    /// <para><b>Why this exists.</b> The roster spans 0.32 m (needle) to 0.72 m (maul) of weapon above
    /// the fist and this project has no motion blur, so a 0.22 s arc is a dozen frames of a small object
    /// moving fast and nothing smears. The trail is the standard device for making that arc legible.</para>
    ///
    /// <para><b>And it carries the weapon's WEIGHT.</b> Width and fade are bent around the authored
    /// values by <see cref="WeaponImpactFx.Mass"/> — see the block of constants below. Before that the
    /// maul and the needle drew the same 30 mm streak dying over the same 0.11 s, which made the one
    /// channel guaranteed to be on screen at contact say nothing about what was in the hand.</para>
    ///
    /// <para><b>LOCAL SPACE, NOT WORLD SPACE — this is the whole reason there is no <c>TrailRenderer</c>
    /// here.</b> A <c>TrailRenderer</c> emits its points in world space, which is correct for a sword in
    /// the world and wrong for a viewmodel: turning the mouse while swinging would leave the ribbon
    /// hanging in the world and drag it across the screen. Points are recorded in CAMERA space instead
    /// (the parent of the viewmodel root), so the arc is exactly the arc the hand described and a
    /// 180° flick during a swing does not smear the frame.</para>
    ///
    /// <para><b>Readability caps.</b> Peak channel 1.15: over the 1.05 bloom threshold so it glows, under
    /// the ~1.25 ACES ceiling where saturated colours desaturate toward orange, and far under the alert
    /// tell (3.00) and deathblow mark (2.60) — those are alarms, this is flourish. Additive URP/Unlit via
    /// <see cref="SlashFx.CreateAdditiveMaterial"/>: a <c>Standard</c> shader renders magenta here.</para>
    ///
    /// <para>Unscaled time for the fade (rule: effects keep running while hitstop holds the world), but
    /// SAMPLING is driven by the swing itself, so a swing frozen mid-arc by hitstop freezes the ribbon's
    /// head with it and only the tail keeps dissolving.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponTrail : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("Ribbon samples. The strike leg is ~0.05 s (3 frames at 60), so each frame contributes " +
                 "several interpolated points or the 'arc' is a two-segment elbow.")]
        [Range(4, 48)] public int maxPoints = 12;
        [Tooltip("Interpolated points inserted between last frame's tip and this frame's. Smooths the " +
                 "ribbon without waiting for frames that a 3-frame window does not have.")]
        [Range(1, 4)] public int subdivisions = 3;
        [Tooltip("Width at the head, in metres, at the blade. The viewmodel sits ~0.5 m from the lens " +
                 "where the screen is ~0.58 m tall, so this is ~5% of screen height — a blade edge, not a banner.")]
        public float headWidth = 0.030f;
        [Tooltip("Width at the oldest sample, as a fraction of headWidth. The taper is a CURVE, not a " +
                 "linear ramp: the first pass ramped linearly over 18 samples and the ribbon was still " +
                 "several pixels wide and full brightness where it left the screen, which reads as a " +
                 "wire strung across the frame rather than as a sweep behind the blade.")]
        [Range(0f, 0.5f)] public float tailFraction = 0.03f;

        [Header("Timing")]
        [Tooltip("Seconds the ribbon lingers after the strike window closes. It must outlive the swing " +
                 "or a 3-frame arc is gone before the eye lands on it — but not by much, or the trail " +
                 "stops meaning 'the hitbox is live'.")]
        public float fadeSeconds = 0.11f;

        [Header("Colour")]
        [Tooltip("Peak HDR channel. Above the 1.05 bloom threshold so it glows; under ~1.25 where ACES " +
                 "desaturates saturated colour toward orange. NEVER raise toward the alert tell (3.00).")]
        public float brightness = 1.15f;

        // ---------------------------------------------------------------- weight, in the ribbon
        //
        // THE RIBBON USED TO BE THE SAME RIBBON FOR EVERY WEAPON. headWidth and fadeSeconds are
        // serialised once by PrefabFactory and the component never knew which weapon was swinging, so a
        // 0.86 s maul and a 0.22 s needle drew an identical 30 mm streak that died in an identical
        // 0.11 s. That is the one channel in the whole swing that is guaranteed to be on screen at the
        // moment of contact, and it was saying nothing about the weapon in the hand.
        //
        // These are MULTIPLIERS on the authored values, not replacements: rule 9 keeps the shipped
        // numbers in PrefabFactory, and the mass curve only bends them around it. Both curves are fitted
        // so the SWORD — mass 0.34, the generalist everything else is compared against — lands within
        // 0.5% of the values that shipped. The reference does not move; the two ends spread around it.
        //
        //   dagger  x0.60 -> 0.018 m,  0.088 s   a whip: gone inside its own 0.116 s follow-through
        //   sword   x1.00 -> 0.030 m,  0.110 s   unchanged, deliberately
        //   hammer  x1.76 -> 0.053 m,  0.152 s   a slab that hangs a beat, which IS the follow-through

        /// <summary>Width multiplier at the light end of <see cref="WeaponImpactFx.Mass"/>.</summary>
        public const float WidthScaleLight = 0.60f;
        /// <summary>Width multiplier at the heavy end. Capped so the hammer's ribbon stays a blade edge
        /// (~9% of screen height at the viewmodel's ~0.5 m) and never becomes a banner across the frame;
        /// FeatureTests' Trail_WidthCapped guards the authored value, WeaponImpactVfxTests the product.</summary>
        public const float WidthScaleHeavy = 1.76f;
        /// <summary>Fade multiplier at the light end. The needle's whole post-strike leg is 0.116 s, so
        /// the flat 0.11 s ribbon was still on screen as the next flick began; 0.088 s is honest.</summary>
        public const float FadeScaleLight = 0.80f;
        /// <summary>Fade multiplier at the heavy end. 0.152 s still dies inside the maul's 0.288 s
        /// post-strike leg, so the ribbon never claims a hitbox that has closed.</summary>
        public const float FadeScaleHeavy = 1.38f;

        public static float WidthScale(float mass) =>
            Mathf.Lerp(WidthScaleLight, WidthScaleHeavy, Mathf.Clamp01(mass));
        public static float FadeScale(float mass) =>
            Mathf.Lerp(FadeScaleLight, FadeScaleHeavy, Mathf.Clamp01(mass));

        float widthScale = 1f;
        float fadeScale = 1f;

        /// <summary>Head width actually drawn this swing — the authored width times the mass curve.</summary>
        public float CurrentHeadWidth { get { return headWidth * widthScale; } }
        /// <summary>Fade actually used this swing.</summary>
        public float CurrentFadeSeconds { get { return Mathf.Max(0.01f, fadeSeconds * fadeScale); } }

        WeaponViewmodel viewmodel;
        LineRenderer line;
        Transform space;          // the transform whose local space the ribbon is recorded in
        Material mat;
        Vector3[] points;
        int count;
        Vector3 lastTip;
        bool haveLast;
        bool emitting;
        float fade;               // 1 while emitting, decays to 0 after
        int peak;                 // points at the moment the strike closed; the retraction runs off this
        Color hue = Color.white;
        bool hueSet;

        /// <summary>True while the strike window is open and the ribbon is recording. Read by tests.</summary>
        public bool IsEmitting { get { return emitting; } }
        /// <summary>Points currently in the ribbon. 0 when nothing is drawn. Read by tests.</summary>
        public int PointCount { get { return count; } }
        /// <summary>The hue the ribbon is currently drawn in — the equipped weapon's neon, normalised.</summary>
        public Color Hue { get { return hue; } }

        void Awake()
        {
            viewmodel = GetComponent<WeaponViewmodel>();
            if (viewmodel == null) viewmodel = GetComponentInChildren<WeaponViewmodel>(true);
            points = new Vector3[Mathf.Max(4, maxPoints)];
            Build();
        }

        void OnEnable() { GameEvents.WeaponChanged += OnWeaponChanged; }
        void OnDisable() { GameEvents.WeaponChanged -= OnWeaponChanged; }
        void OnDestroy() { if (mat != null) Destroy(mat); }

        void Build()
        {
            // Record in the CAMERA's space, not the viewmodel root's: the root carries sway and bob, and
            // a ribbon recorded in a bobbing frame swims while you run.
            space = transform.parent != null ? transform.parent : transform;

            mat = SlashFx.CreateAdditiveMaterial(Color.white);
            line = SlashFx.CreateLine(space, "SwingTrail", 0, headWidth, headWidth * tailFraction, false, mat);
            line.useWorldSpace = false;
            // Fast decay near the head. Additive URP/Unlit ignores vertex colour, so there is no
            // per-vertex alpha to fade along the ribbon — WIDTH is the only head-to-tail channel there
            // is, and it has to do the whole job of "this end is older".
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.3f, 0.5f),
                new Keyframe(0.65f, 0.18f), new Keyframe(1f, tailFraction));
            line.widthMultiplier = headWidth;
            line.transform.localPosition = Vector3.zero;
            line.transform.localRotation = Quaternion.identity;
            line.transform.localScale = Vector3.one;
            line.enabled = false;
        }

        void OnWeaponChanged(WeaponData w)
        {
            hue = w != null ? SlashFx.NormaliseColor(w.neon) : Color.white;
            hueSet = w != null;
            ApplyMass(w);
            Clear();
        }

        /// <summary>Bend the authored width and fade around the equipped weapon's mass. One source of
        /// truth for "how heavy is this thing": <see cref="WeaponImpactFx.Mass"/>, derived from the
        /// shipped attackDuration ladder, so the ribbon and the impact can never disagree.</summary>
        void ApplyMass(WeaponData w)
        {
            float m = WeaponImpactFx.Mass(w);
            widthScale = WidthScale(m);
            fadeScale = FadeScale(m);
        }

        /// <summary>
        /// Open the strike window. Called from <see cref="WeaponViewmodel"/> at the START of the swing
        /// leg — never at the start of the attack, because the wind-up is the phase where nothing is
        /// dangerous yet and a trail on it would say the opposite.
        /// </summary>
        public void BeginStrike()
        {
            // The weapon may have been equipped before this component subscribed (Equip runs in Start),
            // in which case no WeaponChanged was ever heard and the ribbon would be a generic white.
            if (!hueSet)
            {
                var wc = GetComponentInParent<WeaponController>();
                if (wc != null && wc.Current != null)
                {
                    hue = SlashFx.NormaliseColor(wc.Current.neon);
                    hueSet = true;
                    ApplyMass(wc.Current);
                }
            }
            Clear();
            emitting = true;
            fade = 1f;
        }

        /// <summary>
        /// Close the strike window; the ribbon then dissolves over <see cref="fadeSeconds"/>.
        ///
        /// <para>ONE LAST SAMPLE FIRST, and it is not optional. Sampling happens in LateUpdate but the
        /// swing loop ends in Update, so without this the newest point in the ribbon is a whole frame
        /// behind the end of the arc — and on a fast weapon a frame of arc is a hand's width on screen.
        /// The first pass shipped that gap and the ribbon hung DETACHED from the blade, which reads as
        /// a streak with no author. The caller must apply the end pose before calling this.</para>
        /// </summary>
        public void EndStrike()
        {
            if (emitting) Sample();
            emitting = false;
            peak = count;
        }

        /// <summary>Drop the ribbon immediately. Used on weapon swap and interrupt.</summary>
        public void Clear()
        {
            count = 0;
            haveLast = false;
            emitting = false;
            fade = 0f;
            peak = 0;
            if (line != null) { line.positionCount = 0; line.enabled = false; }
        }

        void LateUpdate()
        {
            // LateUpdate, and on the same GameObject as the viewmodel: the attack coroutine writes the
            // pose in the Update phase, so sampling here always reads the pose actually rendered.
            if (line == null || viewmodel == null) return;

            if (emitting) { Sample(); peak = count; }
            else if (fade > 0f)
            {
                fade -= Time.unscaledDeltaTime / CurrentFadeSeconds;
                // RETRACT FROM THE TAIL, the way a real trail dies: the oldest end catches up to where
                // the blade left off instead of the whole ribbon dimming in place. A ribbon that only
                // dims stays the same length for its whole dissolve and sits over the fight for an
                // extra beat; one that retracts reads as the arc closing.
                count = Mathf.Min(count, Mathf.CeilToInt(peak * Mathf.Clamp01(fade)));
            }

            if (fade <= 0f || count < 2)
            {
                if (line.enabled) { line.enabled = false; line.positionCount = 0; }
                if (fade <= 0f) { count = 0; haveLast = false; }
                return;
            }

            Draw();
        }

        void Sample()
        {
            Vector3 tip = space.InverseTransformPoint(viewmodel.TipWorldPosition);
            if (!haveLast) { Push(tip); lastTip = tip; haveLast = true; return; }

            // Sub-sample toward the new tip. A 3-frame window cannot produce a curve on its own; these
            // fill the ribbon so the taper and the fade have something to run along.
            for (int i = 1; i <= subdivisions; i++)
                Push(Vector3.Lerp(lastTip, tip, i / (float)subdivisions));
            lastTip = tip;
        }

        /// <summary>Newest first: index 0 is the head, so the width taper and alpha run tip-to-tail.</summary>
        void Push(Vector3 p)
        {
            int n = Mathf.Min(count + 1, points.Length);
            for (int i = n - 1; i > 0; i--) points[i] = points[i - 1];
            points[0] = p;
            count = n;
        }

        void Draw()
        {
            if (line.positionCount != count) line.positionCount = count;
            for (int i = 0; i < count; i++) line.SetPosition(i, points[i]);
            line.widthMultiplier = CurrentHeadWidth * Mathf.Clamp01(fade);

            // Square the fade: the last third of the dissolve is nearly gone, which is what stops a
            // dying trail from sitting on the enemy for an extra beat. Matches SlashFx.
            float a = Mathf.Clamp01(fade); a *= a;
            Color c = hue * brightness;
            c.a = a;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (!line.enabled) line.enabled = true;
        }
    }
}
