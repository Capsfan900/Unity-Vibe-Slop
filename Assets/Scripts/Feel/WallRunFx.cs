using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The MATH of a wall run's feedback, with no Unity objects in it — the <see cref="DashImpulse"/> /
    /// <see cref="SlideImpulse"/> arrangement. <see cref="WallRunFx"/> is the thin layer that hands these
    /// numbers to the camera, and <c>PlayerFeedback</c> owns the audio.
    ///
    /// <para><b>What a wall run already had, and what it did not.</b> The motor raised
    /// <c>OnWallRunStarted</c> / <c>OnWallRunEnded</c> and nothing subscribed. The ROTATION was never
    /// missing: <c>PlayerLook</c> banks the camera 13° toward the face for the whole run and kicks it
    /// 7° away on the exit jump, and that lean is most of what sells the move. What was missing was
    /// everything else — the catch, the feet, the lens, and above all the END. A run ends six different
    /// ways and the player could not tell any of them apart, which matters because four of them
    /// (expired, decayed, exhausted, lost the face) are the wall LETTING GO and the correct response is
    /// the exit-grace jump, while the other two are already something you did.</para>
    ///
    /// <para><b>So the end is where the specificity lives.</b> A jump gets nothing extra — the exit roll
    /// kick is already the loudest thing that can happen and stacking on it would blur it. A run that
    /// expires, decays or exhausts gets a short DOWNWARD sag: the floor going out from under you. A
    /// run that loses its face gets a lateral drift AWAY from where the wall was. Landing and cancels
    /// hand off to the cue that caused them.</para>
    ///
    /// <para><b>Force, not light.</b> Nothing here brightens anything, nothing yaws (a wall run is very
    /// often an approach), and the sustained FOV hold is small — 3.5° against the slide's 8 — because
    /// the lean already owns the sustained channel and two loud sustained cues on one move read as one
    /// wobbly cue.</para>
    /// </summary>
    public static class WallRunImpulse
    {
        /// <summary>Attack fraction for the CATCH — the same 20 ms yank a dash uses, because contact
        /// is an event and anything slower reads as drifting into the wall.</summary>
        public const float KickAttackFraction = DashImpulse.KickAttackFraction;

        /// <summary>
        /// Attack fraction for the let-go SAG. Deliberately slower than a blow's: 0.30 of a 0.16 s kick
        /// is ~50 ms of travel, a give rather than a hit. The wall did not strike you; it stopped
        /// holding you. This is also what keeps the sag distinct from the exit roll kick, which is fast
        /// because a jump is something you did.
        /// </summary>
        public const float DropAttackFraction = 0.30f;

        /// <summary>
        /// The catch. The lens is pressed TOWARD the wall (opposite its normal) by
        /// <paramref name="offsetMetres"/> and recovers — the body meeting the face. No rotation at all:
        /// <c>PlayerLook</c>'s lean is the rotation, and a second one here would fight it.
        ///
        /// <para><paramref name="normalLocal"/> is the wall normal in CAMERA space (x right, z forward),
        /// flattened, because a wall run is a horizontal contact by construction. A degenerate normal
        /// produces a zero kick rather than a guess.</para>
        /// </summary>
        public static ParryImpulse.Kick AttachKick(Vector3 normalLocal, float offsetMetres)
        {
            ParryImpulse.Kick k = default(ParryImpulse.Kick);
            Vector3 n = new Vector3(normalLocal.x, 0f, normalLocal.z);
            if (n.sqrMagnitude < 1e-6f) return k;
            k.offset = -n.normalized * offsetMetres;
            return k;
        }

        /// <summary>
        /// Playback rate of the wall patter as the run ages, <paramref name="elapsedFraction"/> in 0..1
        /// of the motor's <c>wallRunMaxDuration</c>. Starts bright and FALLS as the loan is called in,
        /// so the ear hears the wall getting heavier before the let-go — the same shape as the motor's
        /// t² gravity ramp. Sits above 1 throughout: a wall is struck shorter and harder than a floor.
        /// </summary>
        public static float StepPitch(float elapsedFraction)
        {
            return Mathf.Lerp(1.30f, 1.12f, Mathf.Clamp01(elapsedFraction));
        }

        /// <summary>
        /// Grit shed per second off the foot contact: <paramref name="ratePerSecond"/> at full speed on a
        /// fresh run, linear in <paramref name="speedFraction"/> (a run bleeding out sheds less), and
        /// falling to 40% as <paramref name="elapsedFraction"/> reaches the end of the loan - the same
        /// direction as <see cref="StepPitch"/>, so the eye and the ear agree the wall is getting heavier.
        /// Pure, clamped, never negative.
        /// </summary>
        public static float GritRate(float speedFraction, float elapsedFraction, float ratePerSecond)
        {
            if (ratePerSecond <= 0f) return 0f;
            float s = Mathf.Clamp01(speedFraction);
            float age = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(elapsedFraction));
            return ratePerSecond * s * age;
        }

        /// <summary>Ends that are the wall letting go, and get the downward sag.</summary>
        public static bool EndsWithDrop(WallRunEnd why)
        {
            return why == WallRunEnd.Expired || why == WallRunEnd.Decayed || why == WallRunEnd.Exhausted;
        }

        /// <summary>Exhaustion alone gets a thud under the sag: stamina ran out ON the wall, which is the
        /// one ending the player needs to learn to hear, because the fix is a different line.</summary>
        public static bool EndsWithThud(WallRunEnd why)
        {
            return why == WallRunEnd.Exhausted;
        }

        /// <summary>
        /// Turn "how the run ended" into a camera impulse. Returns false when the ending gets nothing
        /// extra here: <c>Jumped</c> (the exit roll kick already exists in <c>PlayerLook</c>),
        /// <c>Landed</c> (<c>OnLanded</c> fires its own cue) and <c>Cancelled</c> (the dash or stagger
        /// that cancelled it owns the frame).
        ///
        /// <para>Expired / Decayed / Exhausted: the view pitches DOWN by <paramref name="dropPitchDeg"/>
        /// (positive euler.x is down in Unity) and the head sinks <paramref name="dropOffsetMetres"/>.
        /// Vertical and symmetric — there is no lateral force in the wall giving up.</para>
        ///
        /// <para>LostWall: no pitch; the lens drifts <paramref name="lostDriftMetres"/> AWAY from where
        /// the face was, along its normal. The wall did not drop you, it went away.</para>
        /// </summary>
        public static bool EndKick(WallRunEnd why, Vector3 normalLocal,
                                   float dropPitchDeg, float dropOffsetMetres, float lostDriftMetres,
                                   out ParryImpulse.Kick k)
        {
            k = default(ParryImpulse.Kick);
            if (EndsWithDrop(why))
            {
                k.euler = new Vector3(dropPitchDeg, 0f, 0f);
                k.offset = new Vector3(0f, -dropOffsetMetres, 0f);
                return true;
            }
            if (why == WallRunEnd.LostWall)
            {
                Vector3 n = new Vector3(normalLocal.x, 0f, normalLocal.z);
                if (n.sqrMagnitude < 1e-6f || lostDriftMetres <= 0f) return false;
                k.offset = n.normalized * lostDriftMetres;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The SUSTAINED half of a wall run's feel: a small held FOV widen and the patter of feet on the
    /// face. The one-shots (catch, let-go) live in <c>PlayerFeedback</c>'s event handlers; this is
    /// ticked from <c>PlayerFeedback.Update</c> every frame off the motor's live state, for the same
    /// reason <see cref="SlideFx"/> is — a respawn or a disabled player can swallow the end event, and
    /// a held FOV that never released is a wrong lens for the rest of the run.
    ///
    /// <para><b>The FOV hold channel has one writer at a time, and this is the second candidate.</b>
    /// <see cref="SlideFx.Tick"/> writes <c>CameraFX.FovHold</c> unconditionally every frame (0 when
    /// not sliding). So <c>PlayerFeedback.Update</c> ticks SlideFx FIRST and this SECOND, and this
    /// only writes while a run is live plus one release frame. A slide and a wall run are mutually
    /// exclusive in the motor (one is grounded, the other airborne), so the two never hold at once;
    /// the ordering is what stops the slide's zero from clobbering a live wall-run hold.</para>
    ///
    /// <para>Unscaled time (rule 1), no per-frame allocation, no owned Unity objects — the audio goes
    /// through the one-shot pool and the camera through the existing singletons.</para>
    /// </summary>
    /// <para><b>Particles (2026-09-03 evening, "add particles for the wall running").</b> The wall was
    /// the one contact in the game that shed nothing. Grit now leaves the FOOT CONTACT - the point on
    /// the face beside the feet, not the camera - thrown back down the line and a little off the wall,
    /// into a scene-level root so it is left behind rather than towed (the slide's arrangement).
    /// Rate rides <see cref="WallRunImpulse.GritRate"/>: speed and the age of the loan, so a run that
    /// is about to let go visibly thins out. Each foot-tick additionally throws a few discrete
    /// <see cref="SlashFx.Sparks"/>: the grit is the contact, the sparks are the step. Dust, not fire -
    /// peak channel <see cref="GritBrightness"/>, zero bloom, additive like every particle here.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WallRunFx : MonoBehaviour
    {
        /// <summary>Peak colour channel of a grit mote. Under the 1.05 bloom threshold by a wide margin.</summary>
        public const float GritBrightness = 0.55f;

        [Header("Grit")]
        [Range(0, 64)] public int gritCount = 40;
        public float gritSize = 0.05f;
        public float gritLife = 0.38f;
        [Tooltip("Speed thrown BACK down the run line, scaled by the run's speed fraction.")]
        public float gritBack = 3.4f;
        [Tooltip("Speed thrown OFF the face, along its normal. Small: it sprays past you, not at you.")]
        public float gritOff = 0.9f;
        public float gritSpread = 0.9f;
        public float gritGravity = -12f;
        [Tooltip("Height above the feet the grit leaves the face. Ankle-to-shin: it is the FEET on the wall.")]
        public float gritHeight = 0.28f;
        [Tooltip("Warm stone grey. Peak channel is forced to GritBrightness at build.")]
        public Color gritHue = new Color(0.66f, 0.62f, 0.56f, 1f);
        public Color sparkHue = new Color(1f, 0.74f, 0.40f, 1f);

        FirstPersonMotor motor;
        Transform player;
        float capsuleRadius = 0.4f;

        // ---- grit pool (scene-level root, like SlideGrit) ---------------------------------------
        Transform gritRoot;
        Transform[] grit;
        Vector3[] gritVel;
        float[] gritAge;
        Material gritMat;
        int nextGrit;
        float spawnAccum;

        /// <summary>Motes alive this frame. Read by the feature suite.</summary>
        public int GritAlive { get; private set; }
        /// <summary>Sparks thrown since Build. Read by the feature suite.</summary>
        public int SparksThrown { get; private set; }
        bool running;        // Begin() seen, End() not yet
        bool releasing;      // write FovHold(0) exactly once after the run ends
        Vector3 normalLocal; // wall normal in camera space, cached at Begin: the motor zeroes
                             // WallRunNormal before it raises OnWallRunEnded
        Vector3 lastPos;
        float stepAccum;
        float maxDuration = 1.6f;

        /// <summary>0..1 of the motor's max duration this frame; 0 when not running. Drives the
        /// patter's pitch. Read by the harness.</summary>
        public float ElapsedFraction { get; private set; }

        /// <summary>Camera-space normal of the face the last run was on. Valid inside the end handler,
        /// when the motor's own <c>WallRunNormal</c> has already gone to zero.</summary>
        public Vector3 NormalLocal { get { return normalLocal; } }

        public void Build(FirstPersonMotor owner)
        {
            motor = owner;
            player = owner != null ? owner.transform : transform;
            if (owner != null) maxDuration = Mathf.Max(0.01f, owner.wallRunMaxDuration);
            lastPos = player.position;
            var cc = owner != null ? owner.GetComponent<CharacterController>() : null;
            if (cc != null) capsuleRadius = cc.radius;

            Color c = gritHue;
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m > 0.0001f) c = new Color(c.r / m * GritBrightness, c.g / m * GritBrightness, c.b / m * GritBrightness, 1f);
            gritMat = SlashFx.CreateAdditiveMaterial(c);

            var rootGo = new GameObject("WallRunGrit");
            gritRoot = rootGo.transform;
            if (gritCount > 0)
            {
                grit = new Transform[gritCount];
                gritVel = new Vector3[gritCount];
                gritAge = new float[gritCount];
                for (int i = 0; i < gritCount; i++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "WallGrit" + i;
                    var col = go.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                    var r = go.GetComponent<Renderer>();
                    r.sharedMaterial = gritMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    go.transform.SetParent(gritRoot, false);
                    go.transform.localScale = Vector3.one * gritSize;
                    go.SetActive(false);
                    grit[i] = go.transform;
                    gritAge[i] = -1f;
                }
            }
        }

        void OnDestroy()
        {
            if (gritMat != null) Destroy(gritMat);
            if (gritRoot != null) Destroy(gritRoot.gameObject);
        }

        public void Begin(Vector3 wallNormalLocal)
        {
            running = true;
            releasing = false;
            normalLocal = wallNormalLocal;
            stepAccum = 0f;
            spawnAccum = 0f;
            lastPos = player != null ? player.position : Vector3.zero;
        }

        public void End()
        {
            if (!running) return;
            running = false;
            releasing = true;
            ElapsedFraction = 0f;
        }

        void OnDisable()
        {
            running = false;
            releasing = false;
            ElapsedFraction = 0f;
            stepAccum = 0f;
            if (CameraFX.I != null) CameraFX.I.FovHold(0f);
        }

        /// <summary>
        /// Drive the held layers. Returns true on a frame a foot lands on the wall, so the caller can
        /// voice it and bob the eye; the accumulator is distance along the wall, like the grounded
        /// footsteps, so the patter stays in step with actual speed.
        /// </summary>
        public bool Tick(float feelFovHold, float stepDistance)
        {
            return Tick(feelFovHold, stepDistance, 44f, 3);
        }

        /// <summary>
        /// As above, plus the particles: grit off the foot contact at
        /// <see cref="WallRunImpulse.GritRate"/>(<paramref name="gritRate"/>) and
        /// <paramref name="stepSparks"/> sparks on each foot-tick.
        /// </summary>
        public bool Tick(float feelFovHold, float stepDistance, float gritRate, int stepSparks)
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return false;

            // The motor ended the run and the event was lost (respawn, disable): end from live state.
            if (running && motor != null && !motor.IsWallRunning) End();

            bool live = running && motor != null && motor.IsWallRunning && GameManager.IsPlaying;

            if (CameraFX.I != null)
            {
                if (live) CameraFX.I.FovHold(feelFovHold);
                else if (releasing) { releasing = false; CameraFX.I.FovHold(0f); }
            }
            else releasing = false;

            ElapsedFraction = live ? Mathf.Clamp01(motor.WallRunElapsed / maxDuration) : 0f;

            bool stepped = false;
            if (live && player != null)
            {
                // Full 3D path length, not flattened: the feet follow the wall up and back down.
                stepAccum += (player.position - lastPos).magnitude;
                if (stepAccum >= stepDistance)
                {
                    stepAccum -= stepDistance;
                    stepped = true;
                }
            }
            else stepAccum = 0f;
            if (player != null) lastPos = player.position;

            // ---- particles ----------------------------------------------------------------------
            if (live && player != null && grit != null)
            {
                Vector3 n = motor.WallRunNormal;
                Vector3 run = motor.WallRunDirection;
                float top = Mathf.Max(0.1f, motor.wallRunTopSpeed);
                float speedFraction = Mathf.Clamp01(motor.HorizontalSpeed / top);
                Vector3 contact = player.position + Vector3.up * gritHeight - n * capsuleRadius;

                spawnAccum += WallRunImpulse.GritRate(speedFraction, ElapsedFraction, gritRate) * dt;
                while (spawnAccum >= 1f) { spawnAccum -= 1f; SpawnGrit(contact, n, run, speedFraction); }

                if (stepped && stepSparks > 0)
                {
                    SlashFx.Sparks(contact, n * 0.5f - run + Vector3.up * 0.3f, sparkHue, stepSparks, 4.5f, 0.5f);
                    SparksThrown += stepSparks;
                }
            }
            else spawnAccum = 0f;
            StepGrit(dt);
            return stepped;
        }

        void SpawnGrit(Vector3 contact, Vector3 n, Vector3 run, float speedFraction)
        {
            int i = nextGrit;
            nextGrit = (nextGrit + 1) % grit.Length;

            var t = grit[i];
            t.position = contact + Vector3.up * Random.Range(-0.12f, 0.16f) - run * Random.Range(0f, 0.3f);
            t.localScale = Vector3.one * gritSize;
            t.rotation = Random.rotation;
            t.gameObject.SetActive(true);

            float k = 0.45f + 0.55f * speedFraction;
            gritVel[i] = -run * (gritBack * k * Random.Range(0.6f, 1.2f))
                       + n * (gritOff * Random.Range(0.5f, 1.2f))
                       + Vector3.up * Random.Range(-gritSpread, gritSpread) * 0.6f;
            gritAge[i] = 0f;
        }

        void StepGrit(float dt)
        {
            int alive = 0;
            if (grit == null) { GritAlive = 0; return; }
            for (int i = 0; i < grit.Length; i++)
            {
                if (gritAge[i] < 0f) continue;
                gritAge[i] += dt;
                float k = gritAge[i] / gritLife;
                if (k >= 1f)
                {
                    gritAge[i] = -1f;
                    grit[i].gameObject.SetActive(false);
                    continue;
                }
                alive++;
                grit[i].position += gritVel[i] * dt;
                gritVel[i] += Vector3.up * gritGravity * dt;
                float s = gritSize * (1f - k) * (1f - k);
                grit[i].localScale = new Vector3(s, s, s);
            }
            GritAlive = alive;
        }
    }
}
