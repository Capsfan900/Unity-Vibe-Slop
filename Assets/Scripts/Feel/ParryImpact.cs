using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The extra layers a deflect lands with, on top of what <c>PlayerCombat</c> already fires
    /// (hitstop, Perlin shake, chromatic pulse, screen flash, sparks, arc, enemy recoil, Sfx.Parry).
    ///
    /// <para><b>Why a separate class.</b> Almost everything here is FORCE — a directional camera kick,
    /// an FOV punch, a stepped hitstop release, two extra audio voices, a blade kickback. The deflect
    /// was already legible; what it was missing is weight, and weight added as brightness would have
    /// taken the frame away from <c>EnemyVisuals.CueFlash</c>, which has to stay the loudest thing on
    /// screen or the player stops being able to see the attack coming.</para>
    ///
    /// <para><b>The one exception, added 2026-09-07 on the user's ask</b> (<i>"the perfect parry needs
    /// to have a minimal shockwave visual to know it was performed, over the words"</i>):
    /// <see cref="Shockwave"/>. It is SHAPE, not luminance — a closed hoop is a primitive no other
    /// parry outcome can produce, and it runs through <c>SlashFx</c>, which normalises to 1.0, so it
    /// peaks at 0.98 and never crosses the 1.05 bloom threshold. The cue flash keeps the frame.</para>
    ///
    /// <para><b>Rule 1.</b> Every time request routes through <see cref="TimeScaleController"/> with
    /// <c>affectsPlayer: false</c>; the camera runs on unscaled time. The player's clock, their input
    /// and the 0.13 s perfect window are untouched.</para>
    ///
    /// <para>All shapes live in <see cref="ParryImpulse"/> so they are unit tested rather than trusted.</para>
    /// </summary>
    public static class ParryImpact
    {
        /// <summary>
        /// Fire the impact package. Called from <see cref="ParryController.NotifyDeflected"/>, which is
        /// itself called by <c>PlayerCombat</c> on the frame a Perfect resolves — so this lands on the
        /// same frame as the flash, the sparks and the enemy recoil, not a frame after them.
        /// </summary>
        /// <param name="playerRoot">Anything on the player; used only as a fallback when no camera exists.</param>
        /// <param name="sourceDirection">World direction FROM the player toward the attack source.</param>
        public static void Deflect(Transform playerRoot, Vector3 sourceDirection, bool haveSource)
        {
            // Resolve once. The perfect hoop is a screen-space confirmation, so its eye has to be the
            // actual rendered eye, not the player root at the character's feet. Reusing this transform
            // for the kick also guarantees its directional force agrees with what the player saw.
            Transform eye = ResolveFeedbackEye(playerRoot);
            // Before the settings guard on purpose: the confirmation that a Perfect happened is the one
            // layer that must never be contingent on an unrelated asset having loaded.
            Shockwave(eye);

            var feel = GameManager.I != null ? GameManager.I.feel : null;
            if (feel == null) return;

            HitStopRelease(feel);
            CameraKick(eye, sourceDirection, haveSource, feel);
            FovPunch(feel);
            Layers(feel);
        }

        /// <summary>
        /// The single camera-selection seam for a deflect. CameraFX owns the actual gameplay camera;
        /// Camera.main is the safe standalone/test fallback; the player root is only the final fallback.
        /// Keeping the selection here stops a hoop and a kick ever using different eyes.
        /// </summary>
        public static Transform ResolveFeedbackEye(Transform fallback)
        {
            if (CameraFX.I != null && CameraFX.I.cam != null) return CameraFX.I.cam.transform;
            var main = Camera.main;
            return main != null ? main.transform : fallback;
        }

        /// <summary>
        /// The world's answer to "was that a PERFECT?". A single expanding hoop on the blow line, at the
        /// same contact point the sparks and the crescent already use, so the three read as one event.
        ///
        /// <para><b>It cannot lie.</b> This method is reachable only from
        /// <see cref="ParryController.NotifyDeflected"/>, which <c>PlayerCombat.ReceiveAttack</c> calls
        /// in the <c>ParryResult.Perfect</c> branch and nowhere else. A hoop on screen means a perfect
        /// deflect resolved, with no second path to it.</para>
        ///
        /// <para><b>Teal, matching the word it is replacing.</b> <c>HUDController</c> prints
        /// <c>PERFECT</c> in <c>#A8E6DA</c> and <see cref="ParryImpulse.ShockHue"/> is the same colour,
        /// so the HUD and the world are one language rather than two. It is also ~138° of hue from the
        /// enemy bolt's amber and ~106° from the sentry flare's violet, which is what keeps "answer
        /// this" and "use this" untouched — the hoop claims neither.</para>
        ///
        /// <para>All numbers live in <see cref="ParryImpulse"/> (rule 9) and are pinned by
        /// <c>ParryImpactTests</c>.</para>
        /// </summary>
        static void Shockwave(Transform eye)
        {
            Vector3 pos = eye != null ? eye.position : Vector3.zero;
            Vector3 fwd = eye != null ? eye.forward : Vector3.forward;
            SlashFx.Ring(ParryImpulse.ShockOrigin(pos, Vector3.zero, false, fwd),
                         ParryImpulse.ShockNormal(pos, Vector3.zero, false, fwd),
                         ParryImpulse.ShockHue,
                         ParryImpulse.ShockRadius,
                         ParryImpulse.ShockSeconds);
        }

        /// <summary>
        /// The tail of the freeze. <c>PlayerCombat</c> has already asked for the hard freeze
        /// (<c>parryHitStop</c> at <c>hitStopScale</c>) on this same frame; these two requests overlap
        /// it and, because <see cref="TimeScaleController"/> resolves overlapping requests by taking the
        /// SMALLEST scale, they are invisible until the freeze expires and then form the staircase
        /// <see cref="ParryImpulse.WorldScaleAt"/> describes. That composition is the reason this can be
        /// added without editing the freeze or the controller.
        /// </summary>
        static void HitStopRelease(GameFeelSettings feel)
        {
            if (TimeScaleController.I == null) return;
            float rt = feel.parryHitStopRelease;
            if (rt <= 0f) return;

            float freeze = Mathf.Max(0f, feel.parryHitStop);
            float first = rt * ParryImpulse.ReleaseFirstStepFraction;
            TimeScaleController.I.HitStop(freeze + first, Mathf.Clamp01(feel.parryHitStopReleaseScale));
            TimeScaleController.I.HitStop(freeze + rt, ParryImpulse.SecondStepScale(feel.parryHitStopReleaseScale));
        }

        /// <summary>
        /// The directional kick. Until now a deflect shook the camera with Perlin noise, which is
        /// omnidirectional by construction: it told you something happened and nothing about what. This
        /// is an authored impulse pointed away from the blade that actually hit you.
        /// </summary>
        static void CameraKick(Transform eye, Vector3 sourceDirection, bool haveSource, GameFeelSettings feel)
        {
            if (CameraShake.I == null) return;

            Vector3 blowLocal = Vector3.forward;
            if (haveSource && sourceDirection.sqrMagnitude > 1e-6f)
            {
                blowLocal = eye != null
                    ? eye.InverseTransformDirection(sourceDirection.normalized)
                    : sourceDirection.normalized;
            }

            var k = ParryImpulse.FromBlow(blowLocal, feel.parryKickPitch, feel.parryKickYaw,
                                          feel.parryKickRoll, feel.parryKickOffset);
            CameraShake.I.Kick(k.euler, k.offset, feel.parryKickTime);
        }

        /// <summary>
        /// A punch IN, not out. Narrowing the FOV for a sixth of a second pulls the enemy you just
        /// deflected toward the lens on the exact frame the world stops — which is also the frame you
        /// most need to see them, so the punch and the readability requirement point the same way for
        /// once. <c>CameraFX</c> springs it back on unscaled time.
        /// </summary>
        static void FovPunch(GameFeelSettings feel)
        {
            if (CameraFX.I == null || Mathf.Approximately(feel.parryFovPunch, 0f)) return;
            CameraFX.I.FovKick(feel.parryFovPunch);
        }

        /// <summary>Bright transient over, low body under, the Sfx.Parry that PlayerCombat already plays.</summary>
        static void Layers(GameFeelSettings feel)
        {
            if (!feel.parryLayeredAudio) return;
            var layers = ParryImpulse.DeflectLayers;
            for (int i = 0; i < layers.Length; i++)
                AudioManager.Play(layers[i].sfx, layers[i].volume, layers[i].pitch, layers[i].jitter);
        }
    }
}
