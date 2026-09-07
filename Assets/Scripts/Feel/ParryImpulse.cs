using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The MATH of a deflect's impact, with no Unity objects in it, so every shape here can be unit
    /// tested instead of asserted in prose. <see cref="ParryImpact"/> is the thin layer that hands
    /// these numbers to the camera, the clock and the mixer.
    ///
    /// <para>Three separate shapes live here and they are deliberately different from each other:</para>
    /// <list type="bullet">
    ///   <item><b>The camera kick</b> (<see cref="KickCurve"/>) — a fast authored rise and a quadratic
    ///   settle. Directional, because a Perlin shake says "something happened" and a kick says
    ///   "something hit you FROM THERE". This is the single biggest missing layer on the deflect.</item>
    ///   <item><b>The hitstop</b> (<see cref="WorldScaleAt"/>) — a HARD onset and a stepped release.
    ///   See the note on gap 3.4 below; the asymmetry is the whole argument.</item>
    ///   <item><b>The sound</b> (<see cref="DeflectLayers"/>) — three voices an octave-and-a-bit apart,
    ///   because a single clip cannot be both bright and heavy.</item>
    /// </list>
    ///
    /// <para><b>Nothing here adds light.</b> <c>EnemyVisuals.CueFlash</c> and <c>Recoil</c> own the
    /// brightness budget in a fight and must stay the loudest events in the frame, so every lever in
    /// this file is FORCE (rotation, translation, FOV, time, air) rather than luminance.</para>
    ///
    /// <para><b>Nothing here touches the player's clock.</b> The hitstop requests all pass
    /// <c>affectsPlayer: false</c> through <see cref="TimeScaleController"/> (rule 1) and the camera kick
    /// runs on unscaled time, so not one millisecond of input latency is added and the 0.13 s perfect
    /// window is unmoved.</para>
    /// </summary>
    public static class ParryImpulse
    {
        // ---------------------------------------------------------------- camera kick

        /// <summary>
        /// Fraction of the kick's life spent travelling out. 0.18 of a 0.16 s kick is 29 ms — under two
        /// frames at 60 Hz. Any slower and the kick reads as a camera drift rather than as a blow; any
        /// faster and it is a single-frame teleport that the eye cannot follow back.
        /// </summary>
        public const float KickAttackFraction = 0.18f;

        /// <summary>
        /// Normalised kick envelope. Ease-OUT on the way out (most of the displacement is delivered in
        /// the first few milliseconds — that is the hit), quadratic falloff on the way back (the same
        /// shape <see cref="CameraShake"/> already uses for its Perlin channel, so the two decay
        /// together instead of one outliving the other).
        /// </summary>
        public static float KickCurve(float t01, float attackFraction)
        {
            float a = Mathf.Clamp(attackFraction, 0.01f, 0.9f);
            if (t01 <= 0f) return 0f;
            if (t01 >= 1f) return 0f;
            if (t01 < a)
            {
                float u = t01 / a;
                return 1f - (1f - u) * (1f - u);
            }
            float d = (t01 - a) / (1f - a);
            return (1f - d) * (1f - d);
        }

        /// <summary>A directional camera impulse: an euler delta and a positional offset, both local
        /// to the shake root (i.e. to the camera).</summary>
        public struct Kick
        {
            public Vector3 euler;
            public Vector3 offset;
        }

        /// <summary>
        /// Turn "the blow came from THERE" into a camera impulse.
        ///
        /// <para><paramref name="blowLocal"/> is the unit direction from the player to the attacker,
        /// expressed in CAMERA space. The camera is then driven AWAY from it: an attack from your right
        /// yaws the view left, rolls it right, and drops the head — the crossed motion (head sinks,
        /// view lifts) is what reads as absorbing a blow rather than as being nudged.</para>
        ///
        /// <para>A perfectly frontal blow has no lateral component and therefore no yaw and no roll —
        /// honestly so. Its force is carried by the pitch, the head sink and the FOV punch, which is why
        /// those three do not scale with direction.</para>
        /// </summary>
        public static Kick FromBlow(Vector3 blowLocal, float pitchDeg, float yawDeg, float rollDeg, float offsetMetres)
        {
            float lateral = 0f;
            if (blowLocal.sqrMagnitude > 1e-6f) lateral = Mathf.Clamp(blowLocal.normalized.x, -1f, 1f);

            Kick k;
            // Negative euler.x pitches the view UP in Unity. Constant, not directional: your guard is
            // driven up by every deflect you win.
            k.euler = new Vector3(-pitchDeg, -lateral * yawDeg, lateral * rollDeg);
            // Head sinks under the blow and slides away from it. No z component on purpose — a dolly
            // back would fight the FOV punch-in and the pair reads as a dolly zoom, not as an impact.
            k.offset = new Vector3(-lateral * offsetMetres, -0.35f * offsetMetres, 0f);
            return k;
        }

        // ---------------------------------------------------------------- hitstop shape

        /// <summary>
        /// Share of the release spent on the first (slower) step. Two steps, not a per-frame ramp: at
        /// 60 Hz a 0.07 s release is four frames, and four frames of a continuous curve and four frames
        /// of a two-step staircase are the same picture for a third of the cost and none of the
        /// per-frame churn on <see cref="TimeScaleController"/>.
        /// </summary>
        public const float ReleaseFirstStepFraction = 0.55f;

        /// <summary>Scale of the second release step: half way from the first step back to real time.</summary>
        public static float SecondStepScale(float releaseScale) => 0.5f * (1f + Mathf.Clamp01(releaseScale));

        /// <summary>
        /// World time scale <paramref name="t"/> seconds (unscaled) after a deflect lands.
        ///
        /// <para><b>Gap 3.4, settled asymmetrically.</b> The doc asks whether the binary freeze should
        /// become a curve. The onset stays BINARY — it is the punctuation, and a ramp INTO a freeze is
        /// a stall, because there is no frame the eye can point at and call the hit. What was actually
        /// missing is at the other end: snapping from 0.02 straight back to 1.00 discards the moment in
        /// a single frame, which is why the freeze read as a hiccup rather than as weight. So the
        /// release is stepped and the attack is not.</para>
        /// </summary>
        public static float WorldScaleAt(float t, float freeze, float freezeScale, float releaseTime, float releaseScale)
        {
            if (t < 0f) return 1f;
            if (freeze > 0f && t < freeze) return Mathf.Clamp01(freezeScale);
            if (releaseTime <= 0f) return 1f;
            float u = t - Mathf.Max(0f, freeze);
            if (u < releaseTime * ReleaseFirstStepFraction) return Mathf.Clamp01(releaseScale);
            if (u < releaseTime) return SecondStepScale(releaseScale);
            return 1f;
        }

        /// <summary>
        /// Seconds of WORLD time the whole hitstop swallows. The player keeps running at 1.0 throughout
        /// (rule 1), so this is exactly how far the rest of the fight — including the next parry cue —
        /// is pushed back in real time. It is a budget, and the tests hold it to one.
        /// </summary>
        public static float LostWorldSeconds(float freeze, float freezeScale, float releaseTime, float releaseScale)
        {
            float lost = Mathf.Max(0f, freeze) * (1f - Mathf.Clamp01(freezeScale));
            if (releaseTime > 0f)
            {
                float a = releaseTime * ReleaseFirstStepFraction;
                lost += a * (1f - Mathf.Clamp01(releaseScale));
                lost += (releaseTime - a) * (1f - SecondStepScale(releaseScale));
            }
            return lost;
        }

        // ---------------------------------------------------------------- the perfect shockwave

        /// <summary>
        /// The one place in this file that spends LIGHT rather than force, and it is here because the
        /// user asked for it: a perfect deflect used to announce itself almost entirely in TEXT — the
        /// teal <c>PERFECT</c> popup in <c>HUDController.OnParry</c> — while the world showed the same
        /// vocabulary a scraped block shows (sparks, a flash). Sekiro's whole parry read is that a
        /// deflect is *immediately* distinguishable from a block without looking at a meter; a word on
        /// the HUD is the opposite of that, because reading costs a beat the player does not have at
        /// 32 m/s.
        ///
        /// <para><b>Why a RING and nothing else.</b> Shape is the channel, not brightness. Every other
        /// outcome in <c>PlayerCombat.ReceiveAttack</c> is drawn from sparks (Blocked: 5 grey; Guard:
        /// 9 steel; Hit: 4 dark red) and a Perfect already adds a 0.85 m crescent. A closed expanding
        /// hoop is the one primitive none of the other three can produce, so its mere PRESENCE is the
        /// message — it cannot be confused with a block, and pointing away from the body rather than
        /// smearing over it means it cannot be confused with damage taken either.</para>
        ///
        /// <para><b>Why it does not bloom.</b> It goes through <c>SlashFx.Ring</c>, which normalises to
        /// 1.0, so it peaks at 0.98 after the core's white lerp — under the 1.05 threshold. The
        /// deflect's licensed bright moment is <c>EnemyVisuals.ParryGlow</c> at 3.2 ON THE ENEMY, and
        /// the whole reason that flash means "you deflected" is that nothing on the player's side
        /// competes with it. Adding a bloom here would have bought a second confirm by devaluing the
        /// first. See ANIMATION-VFX section 4 rule 9.</para>
        /// </summary>
        public static readonly Color ShockHue = new Color(0.658f, 0.902f, 0.855f);   // #A8E6DA

        /// <summary>
        /// Ring radius, metres. 0.34 → 0.62 on 2026-09-07, on the user's report that the hoop was "to
        /// small" once they could finally see it in the right place.
        ///
        /// <para>At <see cref="ShockDistance"/> from the lens and the shipped 95° vertical FOV
        /// (<c>PrefabFactory</c> line 763), a 1.24 m hoop covers <b>~54% of screen height</b> — it was
        /// ~30% before. It is still not an explosion and still the smallest wave in the game: the
        /// crescent thrown on the same frame (<c>PlayerCombat.DeflectArc</c>, radius 0.85) covers 74%,
        /// and <see cref="WeaponImpactFx.ShockwaveRadius"/>, the maul's landed-hit wave, is 1.10.</para>
        ///
        /// <para>"Minimal" was the original ask and it still holds — one thin hoop, 0.18 s, no bloom.
        /// Minimal is about how MUCH is drawn, not how small it is; a confirm the player has to hunt for
        /// is not minimal, it is quiet. It still has to survive being fired three times in four
        /// seconds, which is what keeps <see cref="ShockSeconds"/> where it is.</para>
        /// </summary>
        public const float ShockRadius = 0.62f;

        /// <summary>
        /// Life, seconds. Two frames longer than the crescent's 0.16 s so the hoop is the last thing on
        /// screen — the settle at the end of anticipation/snap/settle — and under a third of the
        /// tightest possible interval between two parryable impacts in the shipped data
        /// (min <c>comboGap</c> 0.12 + min <c>windup</c> 0.45 = 0.57 s), so a chain of perfects reads as
        /// three separate hoops rather than one smear. <c>SlashFx.Ring</c> supplies the shape: it
        /// punches out on an ease-out and its alpha falls on a square, i.e. snap then clear.
        /// </summary>
        public const float ShockSeconds = 0.18f;

        // ShockEyeHeight / ShockReach / ShockAttackerChest / ShockForward lived here until 2026-09-07.
        // They mirrored PlayerCombat.ContactPoint so the hoop would sit on the sparks -- but the hoop is
        // now born on the LOOK RAY (see ShockOrigin), so they had no readers left, and a constant whose
        // doc-comment still claims "if PlayerCombat.ContactPoint moves, this must" is worse than no
        // constant: the next reader wires it back up. The sparks and the crescent still come off the
        // real contact point; only the confirmation hoop moved to the crosshair.

        /// <summary>
        /// The ring's normal: straight back down the LOOK ray, so the hoop is square to the lens and
        /// reads as a flat wave-front seen face on wherever the player happens to be aiming.
        ///
        /// <para><b>It used to be the level direction to the attacker</b> (<c>to.y = 0</c>), which tilted
        /// the hoop away from the camera whenever the player was not looking dead level at the attacker's
        /// chest — the ring went elliptical and slid off centre exactly when the fight was most vertical.
        /// The user's report: "its just misplace and to small ... it needs to be right where the point of
        /// contact for the parry is (so essentially the crosshair)".</para>
        /// </summary>
        public static Vector3 ShockNormal(Vector3 eyePos, Vector3 attackerPos, bool haveAttacker, Vector3 forward)
        {
            Vector3 f = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            return -f;   // face the lens: the hoop is a disc on the crosshair, never an ellipse
        }

        /// <summary>
        /// Where the hoop is born: ON THE LOOK RAY, <see cref="ShockDistance"/> in front of the eye, so
        /// it renders centred on the CROSSHAIR — which is where the player's attention already is, and
        /// where they read the parry from.
        ///
        /// <para><b>The bug this replaces.</b> The old origin was
        /// <c>playerPos + up * ShockEyeHeight</c> walked toward the attacker's chest.
        /// <see cref="ShockEyeHeight"/> is a PLAYER-ROOT to contact height (1.25 m), but
        /// <c>ParryImpact.Deflect</c> passes the CAMERA transform, so the 1.25 m was added on top of the
        /// eye's own ~1.6 m and the hoop was born nearly three metres up — above the frame, or clipped at
        /// its top edge. Compounding it, the origin then tracked the ATTACKER rather than the aim, so it
        /// drifted off centre whenever the crosshair was not on their chest.</para>
        ///
        /// <para>The attacker parameters are kept in the signature deliberately: the sparks and the
        /// crescent still come off the real contact point, and a future pass may want to lean the hoop
        /// back toward it. Today it is the crosshair, because that is what the player is looking at.</para>
        /// </summary>
        public static Vector3 ShockOrigin(Vector3 eyePos, Vector3 attackerPos, bool haveAttacker, Vector3 forward)
        {
            Vector3 f = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            return eyePos + f * ShockDistance;
        }

        /// <summary>
        /// How far down the look ray the hoop sits, metres. Near enough that it is unmistakably the
        /// player's own event rather than something happening to the enemy, far enough to clear the
        /// viewmodel's swept arc so a weapon cannot poke through it.
        /// </summary>
        public const float ShockDistance = 1.05f;

        /// <summary>
        /// Fraction of SCREEN HEIGHT the finished hoop covers, at a vertical field of view of
        /// <paramref name="vFovDeg"/> and a viewing distance of <paramref name="distance"/>. Exposed so
        /// "minimal" is a measured claim rather than an adjective — see the test.
        /// </summary>
        public static float ShockScreenHeightFraction(float distance, float vFovDeg)
        {
            float halfHeight = Mathf.Max(1e-4f, distance) * Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(vFovDeg, 1f, 179f) * 0.5f);
            return (2f * ShockRadius) / (2f * halfHeight);
        }

        // ---------------------------------------------------------------- sound

        /// <summary>One voice in the deflect stack.</summary>
        public struct AudioLayer
        {
            public Sfx sfx;
            public float volume;
            public float pitch;
            public float jitter;

            public AudioLayer(Sfx s, float v, float p, float j) { sfx = s; volume = v; pitch = p; jitter = j; }
        }

        /// <summary>
        /// The two voices stacked UNDER and OVER the existing <c>Sfx.Parry</c> that
        /// <c>PlayerCombat</c> already plays at unity gain and pitch.
        ///
        /// <para>A deflect is steel arriving at speed: it needs a bright transient to read as SHARP and
        /// a low body to read as HEAVY, and one clip at one pitch cannot be both. Both layers are
        /// deliberately quiet — this is spectral width, not volume. Sfx enum names are folder names and
        /// append-only (rule 7), so this reuses existing entries rather than adding any.</para>
        /// </summary>
        public static readonly AudioLayer[] DeflectLayers =
        {
            // Bright transient. Parry pitched up more than an octave: pure attack, no body, and it is
            // what the ear hears as the edge of the sound.
            new AudioLayer(Sfx.Parry, 0.42f, 2.15f, 0.06f),
            // Low body. Block pitched down almost an octave: the mass behind the edge.
            new AudioLayer(Sfx.Block, 0.55f, 0.55f, 0.04f)
        };
    }
}
