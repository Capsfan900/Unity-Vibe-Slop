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
        public float parryChromatic = 0.35f;
        public float parryChromaticTime = 0.12f;
        [Tooltip("Metres of shove the player takes when a blow lands on a HELD guard. The guard eats " +
                 "the damage, so the impact has to arrive as movement or it reads as nothing happening.")]
        public float guardShove = 1.2f;
        public float dashFovKick = 8f;
        public float ultFovKick = 15f;

        [Header("Dash — punctuation, FORCE not light")]
        // A dash was never silent: dashFovKick has shipped at 8 on the asset for as long as the asset
        // has existed. It was NON-SPECIFIC. A symmetric FOV widen says the lens changed; it does not say
        // which way you went or how far, and 0.16 s is far too short for the world to sell that on its
        // own. Everything below adds DIRECTION. Nothing below adds brightness — see dashStreakBrightness.
        [Tooltip("Degrees the view lifts on a FORWARD dash, scaled by the forward component. Zero for a " +
                 "pure strafe, honestly: a sideways dash has no pitch in it.")]
        public float dashKickPitch = 0.9f;
        [Tooltip("Degrees of roll, banking INTO the dash, scaled by its lateral component. Roll is free " +
                 "readability — it never moves the aim vector — which is why it carries most of a " +
                 "lateral dash. There is deliberately NO yaw: a dash is often the approach to a swing.")]
        public float dashKickRoll = 1.4f;
        [Tooltip("Metres the lens is left BEHIND the body at the peak of the kick, opposite the travel. " +
                 "This is the acceleration read: a camera that teleports with the body reports no " +
                 "acceleration at all, which is why a dash felt like a position edit.")]
        public float dashKickOffset = 0.06f;
        [Tooltip("Kick lifetime, unscaled seconds. Shorter than the deflect's 0.16 and shorter than the " +
                 "0.16 s dash itself, so the camera is dead still again before you land.")]
        public float dashKickTime = 0.14f;
        [Tooltip("Chromatic aberration pulse on a dash. Air distortion, and the only post-FX a dash gets.")]
        public float dashChromatic = 0.35f;
        [Tooltip("Speed lines. Camera space, never world space — a world-space streak hangs in the world " +
                 "the moment you turn the mouse, and a dash is often a turn (same conclusion WeaponTrail " +
                 "reached).")]
        [Range(0, 24)] public int dashStreakCount = 12;
        public float dashStreakSeconds = 0.22f;
        [Range(0f, 1f)] public float dashStreakAlpha = 0.85f;
        [Tooltip("Peak HDR channel of the streaks. DELIBERATELY UNDER the scene's 1.05 bloom threshold, " +
                 "so a dash contributes exactly zero bloom and can never compete with " +
                 "EnemyVisuals.CueFlash — light in this game means 'you deflected'.")]
        public float dashStreakBrightness = 0.90f;

        [Header("Slide — sustained, and the middle is the whole problem")]
        // A slide lasts up to 0.90 s and every cue it had except the eye drop was an IMPULSE: the entry
        // FOV kick had decayed to nothing inside ~0.4 s, and there was no ground contact in the package
        // at all. So these are HELD values that track actual speed (see SlideImpulse), not one-shots.
        [Tooltip("Peak SUSTAINED FOV widening, held for the whole slide and scaled by the speed still " +
                 "being carried. Reaches exactly zero at the motor's slideEndSpeed, so standing up " +
                 "never snaps the lens. Stacks on top of the entry kick.")]
        public float slideFovHold = 8f;
        [Tooltip("FOV punch when you stand up. NEGATIVE: the world closing back in as the speed goes.")]
        public float slideEndFovPunch = -2.5f;
        [Tooltip("Maximum camera roll while steering a slide, degrees, banking into the steer and scaled " +
                 "by remaining speed. Roll never moves the aim vector, so a held bank costs nothing at " +
                 "the next parry — which is exactly why it, and not pitch or yaw, is the sustained channel.")]
        public float slideRollDegrees = 3.5f;
        [Tooltip("Degrees the nose dips on the commit. Frontal and symmetric: a slide entry has no " +
                 "lateral force and inventing one would read as a stumble. Raised 1.2 -> 1.8 with the " +
                 "body pass: now that the legs are thrown out under the lens the head has to answer.")]
        public float slideKickPitch = 1.8f;
        public float slideKickTime = 0.13f;
        [Tooltip("HELD camera rumble at full slide speed, metres of lens offset. The rattle of a body on " +
                 "a floor; quadratic in speed (SlideImpulse.RumbleAmplitude) so it is gone before the " +
                 "scrape is. Over ~1 cm it stops being texture and becomes a shake.")]
        public float slideRumble = 0.006f;
        [Tooltip("Natural frequency, Hz, of the spring the eye follows the slide drop on. It used to be a " +
                 "6 m/s ramp, which is a crouch; a spring PLOPS. 4.5 Hz reaches the drop in ~0.13 s.")]
        public float slideCrouchHz = 4.5f;
        [Tooltip("Damping ratio of that spring. Under 1 the eye overshoots BELOW the slide height and " +
                 "settles up (the body arriving on the floor), and rises past neutral on stand-up. " +
                 "0.55 overshoots by ~12% — ~0.065 m of the 0.55 m drop. 1 = no overshoot.")]
        [Range(0.2f, 1f)] public float slideCrouchDamping = 0.55f;
        [Tooltip("Grit particles per second at full slide speed. Shed into a SCENE-LEVEL root so they " +
                 "are left behind rather than towed — that is most of what makes it read as a floor.")]
        public float slideDustRate = 34f;
        [Tooltip("Spark bursts per second, and only above 35% speed. Routed through SlashFx, which " +
                 "normalises to a peak channel of exactly 1.0 — still under the 1.05 bloom threshold.")]
        public float slideSparkRate = 5f;
        [Tooltip("Peak gain of the sustained scrape loop. A synthesised noise loop on its own AudioSource, " +
                 "not an Sfx entry: AudioManager is a one-shot pool with no looping API, and retriggering " +
                 "a one-shot 20x/second would eat most of the 12-voice pool and starve the fight.")]
        [Range(0f, 0.6f)] public float slideScrapeVolume = 0.22f;

        [Header("Traversal — balloons, water, the grapple burst (2026-09-04 pivot)")]
        // Three ADDITIONS to the movement kit, each a one-shot or a texture: none of them takes a held
        // camera channel, because FovHold / SetRoll / SetRumble each have exactly one writer (SlideFx)
        // and water in particular is something you slide ACROSS.
        [Tooltip("FOV widen on a balloon launch, degrees. Between a jump (2.5) and a dash (8): a launch is " +
                 "bigger than a jump and it is not a punctuation mark.")]
        public float balloonFovKick = 5f;
        [Tooltip("Degrees the nose lifts on a launch — the head answering the body going UP.")]
        public float balloonKickPitch = 1.5f;
        [Tooltip("Extra FOV widen when a dash is a grapple-exit BURST, on top of the dash's own kick. The " +
                 "burst is the biggest speed in the game (27.5 m/s), so the lens says so.")]
        public float burstFovKick = 6f;
        [Tooltip("One-shot FOV kick on ENTERING water. A kick, not a hold: the hold channels belong to the " +
                 "slide, and a slide that crosses water must not have two writers on the lens.")]
        public float waterEnterFovKick = 3f;
        [Tooltip("Spray bursts per second at the water floor speed, via SlashFx (peak 1.0, no bloom).")]
        public float waterSprayRate = 26f;
        [Tooltip("Peak gain of the water hiss loop. Its own synthesised source, like the scrape.")]
        [Range(0f, 0.6f)] public float waterHissVolume = 0.14f;

        [Header("Perfect timing — the reward for a move landed on its moment")]
        // A PERFECT (FirstPersonMotor.OnPerfect: a wall jump on the wall's last breath, a jump thrown out
        // of a dash, a grapple burst on the landing) gives stamina back. The feedback is a chime and a
        // small widen -- force and sound, never light: emission is spoken for (a deflect owns it).
        [Tooltip("FOV widen on a perfect, degrees, on top of the move's own kick. Small: the move already " +
                 "kicked the lens; this is the 'yes' on top of it.")]
        public float perfectFovKick = 3f;
        [Tooltip("Seconds the PERFECT prompt stays up. Short: it is a stamp, not a banner.")]
        public float perfectPromptSeconds = 0.6f;

        [Header("Wall run — the lean already exists; this is the catch, the feet and the LET-GO")]
        // The motor raised OnWallRunStarted / OnWallRunEnded from the start and nothing subscribed.
        // PlayerLook already banks 13° toward the face and kicks 7° away on the exit jump, so rotation
        // was never the gap. What was missing was contact (nothing said the foot met the wall), the
        // middle (no feet for up to 1.6 s of running) and the END: a run ends six ways and four of them
        // are the wall letting go, which is the cue that has to teach the exit-grace jump. See
        // WallRunImpulse / WallRunFx. Nothing here brightens anything and nothing yaws.
        [Tooltip("SUSTAINED FOV widening held for the whole run, degrees. Small on purpose — 3.5 " +
                 "against the slide's 8 — because the lean already owns the sustained channel and " +
                 "two loud held cues on one move read as one wobbly cue. Released on end; SlideFx " +
                 "and WallRunFx share CameraFX.FovHold by ORDER (slide first, wall run second), never " +
                 "at the same time.")]
        public float wallRunFovHold = 3.5f;
        [Tooltip("Metres the lens is pressed TOWARD the wall on the catch, and recovers. No rotation: " +
                 "PlayerLook's lean is the rotation, and a second one here would fight it.")]
        public float wallRunAttachOffset = 0.03f;
        [Tooltip("Catch kick lifetime, unscaled seconds. Dash-fast (20 ms attack): contact is an event.")]
        public float wallRunAttachTime = 0.12f;
        [Tooltip("Metres of wall per foot-tick. Shorter than the 2.4 m ground stride — you patter up " +
                 "a wall, you do not stride along it.")]
        public float wallRunStepDistance = 1.6f;
        [Tooltip("Gain of each wall foot-tick. UNDER the 0.55 grounded footstep: the wall patter is " +
                 "texture behind the lean, not a new voice. Pitch rides WallRunImpulse.StepPitch and " +
                 "falls as the run ages.")]
        [Range(0f, 0.6f)] public float wallRunStepVolume = 0.40f;
        [Tooltip("Degrees the view sags DOWN when the wall lets go (Expired / Decayed / Exhausted). " +
                 "The floor going out from under you; vertical and symmetric, because there is no " +
                 "lateral force in a wall giving up. Slower attack than a blow — a give, not a hit.")]
        public float wallRunDropPitch = 1.4f;
        [Tooltip("Metres the head sinks with the sag.")]
        public float wallRunDropOffset = 0.03f;
        [Tooltip("Sag lifetime, unscaled seconds. Same length as the deflect kick, so it is over well " +
                 "inside the exit-grace window and never muddies a grace jump's roll kick.")]
        public float wallRunDropTime = 0.15f;
        [Tooltip("Metres the lens drifts AWAY from where the face was when a run loses its wall " +
                 "(LostWall). Distinct from the sag: the wall did not drop you, it went away.")]
        public float wallRunLostDrift = 0.02f;
        [Tooltip("Grit shed from the foot contact per second at a full-speed run, falling with speed and " +
                 "with the run's age (WallRunImpulse.GritRate). Above the slide's 34: the wall is struck " +
                 "shorter and harder than a floor. Dust, not fire — peak channel 0.55, zero bloom.")]
        public float wallRunGritRate = 44f;
        [Tooltip("Sparks thrown on each foot-tick (every wallRunStepDistance metres). Few and discrete, " +
                 "through SlashFx.Sparks; 0 disables. They mark the STEP, the grit marks the CONTACT.")]
        [Range(0, 12)] public int wallRunStepSparks = 3;

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
