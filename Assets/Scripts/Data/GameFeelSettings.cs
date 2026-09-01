using UnityEngine;

namespace VibeGame1
{
    [CreateAssetMenu(menuName = "VibeGame1/Game Feel")]
    public class GameFeelSettings : ScriptableObject
    {
        [Header("Attack arbitration")]
        [Tooltip("How many enemies may be mid-attack at once. 1 reads best; 2 is chaotic but survivable. " +
                 "Two attacks landing from different angles inside the same 130 ms window are not " +
                 "simultaneously parryable, so this is what keeps a crowd ANSWERABLE rather than unfair.")]
        [Range(1, 4)] public int maxSimultaneousAttackers = 1;

        [Header("Hitstop (realtime seconds)")]
        public float parryHitStop = 0.09f;
        [Tooltip("Hitstop on a hit taken through a HELD guard. Shorter than a deflect's: the guard is " +
                 "a thud, not a beat you earned.")]
        public float guardHitStop = 0.05f;
        public float executeHitStop = 0.14f;
        public float hitStopScale = 0.02f;

        [Header("Camera shake (amplitude, seconds)")]
        public float shakeSmallAmp = 0.06f, shakeSmallTime = 0.12f;
        public float shakeMedAmp = 0.14f, shakeMedTime = 0.2f;
        public float shakeBigAmp = 0.3f, shakeBigTime = 0.35f;

        [Header("Screen flash")]
        public Color parryFlash = new Color(0.8f, 0.9f, 1f, 1f);   // pale steel
        public float parryFlashAlpha = 0.35f;
        public Color hurtFlash = new Color(0.6f, 0f, 0.05f, 1f);   // dark blood
        public float hurtFlashAlpha = 0.4f;
        public Color ultFlash = new Color(1f, 0.5f, 0.15f, 1f);    // ember

        [Header("Post FX pulses")]
        public float parryChromatic = 0.6f;
        public float parryChromaticTime = 0.25f;
        [Tooltip("Metres of shove the player takes when a blow lands on a HELD guard. The guard eats " +
                 "the damage, so the impact has to arrive as movement or it reads as nothing happening.")]
        public float guardShove = 1.2f;
        public float dashFovKick = 8f;
        public float ultFovKick = 15f;

        [Header("Deflect impact — FORCE, never light")]
        // The deflect was already legible; what it lacked was weight. Every value below is motion,
        // time or air. Nothing here brightens the frame, because EnemyVisuals.CueFlash owns the
        // brightness budget and has to stay the loudest event on screen. See ParryImpulse / ParryImpact.
        [Tooltip("Degrees the view pitches UP on a deflect. Constant, not directional: every deflect " +
                 "you win drives your guard up. 1.6 deg at 95 deg FOV is under 2% of screen height.")]
        public float parryKickPitch = 1.6f;
        [Tooltip("Degrees the view yaws AWAY from the blow, scaled by how lateral the blow was. Zero " +
                 "for a perfectly frontal attack, which is honest — a frontal blow has no sideways force.")]
        public float parryKickYaw = 1.1f;
        [Tooltip("Degrees of roll, signed with the blow. Roll is free readability: it never moves the " +
                 "aim vector, so it can be the loudest part of the kick at no cost to the next swing.")]
        public float parryKickRoll = 1.3f;
        [Tooltip("Metres the head sinks and slides under the blow at the peak of the kick.")]
        public float parryKickOffset = 0.035f;
        [Tooltip("Kick lifetime, unscaled seconds. Must be shorter than the ~0.28 s cue lead so the " +
                 "camera is dead still again before the next 'parry now' signal.")]
        public float parryKickTime = 0.16f;
        [Tooltip("FOV delta on a deflect. NEGATIVE = punch in, pulling the enemy you just deflected " +
                 "toward the lens on the frame the world stops.")]
        public float parryFovPunch = -2.2f;

        [Tooltip("Length of the STEPPED RELEASE after the hard freeze (gap 3.4). The onset stays " +
                 "binary — that is the punctuation — but snapping from 0.02 straight back to 1.00 threw " +
                 "the moment away in one frame. 0 restores the old pure-binary hitstop exactly.")]
        public float parryHitStopRelease = 0.07f;
        [Tooltip("World scale of the first release step. The second is half way from here back to 1.")]
        [Range(0.05f, 1f)] public float parryHitStopReleaseScale = 0.45f;
        [Tooltip("Stack a bright transient over and a low body under Sfx.Parry. Spectral width, not " +
                 "volume: one clip at one pitch cannot be both sharp and heavy.")]
        public bool parryLayeredAudio = true;
    }
}
