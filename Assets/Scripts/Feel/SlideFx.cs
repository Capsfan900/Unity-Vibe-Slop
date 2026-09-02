using UnityEngine;

namespace VibeGame1
{
    /// <summary>
    /// The picture and the noise of a slide — and specifically of the MIDDLE of one, which is where this
    /// move was reading as nothing at all.
    ///
    /// <para><b>What was actually wrong.</b> The slide was not un-fed: <c>PlayerFeedback</c> already
    /// dropped the eye 0.55 m, pitched the dash whoosh down to 0.72 and fired a one-shot FOV kick. But a
    /// slide lasts up to 0.90 s and every one of those cues except the eye drop is an IMPULSE — the FOV
    /// kick had decayed to nothing inside about 0.4 s. So for the majority of a slide the player was
    /// 0.9 m off the floor doing 20 m/s and the world said absolutely nothing, and there was no ground
    /// contact anywhere in the feedback at all. Worse, <c>OnSlideEnded</c> was raised by the motor and
    /// <b>subscribed by nobody</b>, so standing up had no cue whatsoever.</para>
    ///
    /// <para><b>So the middle is built out of things that TRACK SPEED rather than fire once</b> — see
    /// <see cref="SlideImpulse"/> for the shapes. FOV is held (not kicked) at a value proportional to
    /// the speed you are still carrying; the scrape's gain and pitch ride the same fraction; the grit
    /// rate rides it too. The result is that a slide visibly and audibly SPENDS itself, which is exactly
    /// what the move is: you are cashing in entry speed.</para>
    ///
    /// <para><b>Ground contact was the untapped channel.</b> Grit is shed into a SCENE-LEVEL root rather
    /// than parented to the player — the same arrangement <see cref="EmberAura"/> uses and for the same
    /// reason: debris towed along by the body reads as decoration, debris left behind reads as a trail.
    /// That is most of what makes this look like scraping along a floor.</para>
    ///
    /// <para><b>Readability budget.</b> The grit ships at peak channel 0.55, well under the 1.05 bloom
    /// threshold, so the dust contributes zero bloom. The occasional spark goes through
    /// <see cref="SlashFx"/>, which normalises to a peak channel of exactly 1.0 and therefore also never
    /// crosses the threshold. Nothing in a slide can out-shout <c>EnemyVisuals.CueFlash</c>, which owns
    /// the brightness budget: light on an enemy means "you deflected", and traversal is not allowed to
    /// speak that language. Every lever here is force, motion, dirt and sound.</para>
    ///
    /// <para>Unscaled time throughout (rule 1), so a hitstop cannot freeze the dust or the camera while
    /// the player keeps moving. No per-frame allocation: fixed pools, one owned material, one owned
    /// AudioSource and one procedurally generated loop clip.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SlideFx : MonoBehaviour
    {
        [Header("Grit")]
        [Range(0, 64)] public int gritCount = 30;
        public float gritSize = 0.055f;
        public float gritLife = 0.42f;
        [Tooltip("Metres per second thrown BACKWARD along the slide, at full speed. Debris kicked out " +
                 "behind you is the read; debris thrown forward looks like an explosion under your feet.")]
        public float gritBack = 4.2f;
        public float gritUp = 1.6f;
        public float gritSpread = 1.3f;
        public float gritGravity = -14f;
        [Tooltip("Height above the feet the grit is shed from. Ankle height: it is the FLOOR you are on.")]
        public float gritHeight = 0.12f;
        [Tooltip("Dim warm grey. Peak channel is forced to 0.55 at build — dust, not fire, and far " +
                 "under the 1.05 bloom threshold so it adds no glow to the frame at all.")]
        public Color gritHue = new Color(0.68f, 0.60f, 0.50f, 1f);
        public float gritBrightness = 0.55f;

        [Header("Scrape")]
        [Tooltip("Seconds the loop fades out over when the slide ends. Cutting it dead reads as the " +
                 "sound breaking rather than as the move finishing.")]
        public float scrapeRelease = 0.12f;

        FirstPersonMotor motor;
        Transform player;

        // ---- grit pool (scene-level root; see the class note) -------------------------------------
        Transform gritRoot;
        Transform[] grit;
        Vector3[] gritVel;
        float[] gritAge;
        Material gritMat;
        int nextGrit;
        float spawnAccum, sparkAccum;

        // ---- scrape --------------------------------------------------------------------------------
        AudioSource scrape;
        AudioClip scrapeClip;
        float scrapeGain;            // what is actually on the source right now, after the release ramp

        bool running;
        float endSpeed = 8f, maxSpeed = 22f;

        /// <summary>Speed fraction being rendered this frame, 0..1. Read by tests and the harness.</summary>
        public float SpeedFraction { get; private set; }

        public void Build(FirstPersonMotor owner)
        {
            motor = owner;
            player = owner != null ? owner.transform : transform;
            if (owner != null) { endSpeed = owner.slideEndSpeed; maxSpeed = owner.slideMaxSpeed; }

            Color c = gritHue;
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m > 0.0001f) c = new Color(c.r / m * gritBrightness, c.g / m * gritBrightness, c.b / m * gritBrightness, 1f);
            // Additive, like every other particle in this project. A lit material with a dark base
            // colour is a HOLE in the frame at this size rather than a mote of dust.
            gritMat = SlashFx.CreateAdditiveMaterial(c);

            var rootGo = new GameObject("SlideGrit");
            gritRoot = rootGo.transform;

            if (gritCount > 0)
            {
                grit = new Transform[gritCount];
                gritVel = new Vector3[gritCount];
                gritAge = new float[gritCount];
                for (int i = 0; i < gritCount; i++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Grit" + i;
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

            BuildScrape();
        }

        void OnDestroy()
        {
            if (gritMat != null) Destroy(gritMat);
            if (gritRoot != null) Destroy(gritRoot.gameObject);
            if (scrapeClip != null) Destroy(scrapeClip);
        }

        // ------------------------------------------------------------------ scrape loop

        /// <summary>
        /// A seamless band-limited noise loop, synthesised here rather than added to <see cref="Sfx"/>.
        ///
        /// <para>Two reasons it is not an Sfx entry. First, <c>AudioManager</c> is a ONE-SHOT pool with
        /// no looping API, and a slide needs a voice whose gain and pitch move continuously for most of
        /// a second — retriggering a one-shot 20 times a second would eat most of the 12-source pool and
        /// starve the fight. Second, <c>Sfx</c> names are folder names and append-only (hard rule 7), so
        /// adding one is a content change, not a feedback change.</para>
        ///
        /// <para>One-pole low pass over white noise for the gravel body, a slow high pass to take the
        /// rumble out (it must not fight the music bed), and a 2048-sample crossfade of the tail into
        /// the head so the loop point is inaudible. Deterministic seed, so every run of the game — and
        /// every capture — gets the identical texture.</para>
        /// </summary>
        void BuildScrape()
        {
            const int Rate = 22050;
            const int Len = Rate;          // one second, looped
            const int Xf = 2048;

            var raw = new float[Len + Xf];
            var rng = new System.Random(1207);
            float lp = 0f, hp = 0f;
            float peak = 0.0001f;
            for (int i = 0; i < raw.Length; i++)
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (w - lp) * 0.34f;            // ~2.4 kHz corner: grit, not hiss
                hp += (lp - hp) * 0.015f;          // ~50 Hz corner, subtracted below
                float v = lp - hp;
                raw[i] = v;
                float a = v < 0f ? -v : v;
                if (a > peak) peak = a;
            }

            var data = new float[Len];
            float norm = 0.5f / peak;
            for (int i = 0; i < Len; i++)
            {
                float v = raw[i];
                if (i < Xf)
                {
                    float k = i / (float)Xf;
                    v = raw[i] * k + raw[Len + i] * (1f - k);
                }
                data[i] = v * norm;
            }

            scrapeClip = AudioClip.Create("SlideScrape", Len, 1, Rate, false);
            scrapeClip.SetData(data, 0);

            var go = new GameObject("SlideScrape");
            go.transform.SetParent(transform, false);
            scrape = go.AddComponent<AudioSource>();
            scrape.clip = scrapeClip;
            scrape.loop = true;
            scrape.playOnAwake = false;
            scrape.spatialBlend = 0f;
            scrape.volume = 0f;
        }

        // ------------------------------------------------------------------ lifecycle

        public void Begin()
        {
            running = true;
            spawnAccum = 0f;
            sparkAccum = 0f;
            if (scrape != null && !scrape.isPlaying) scrape.Play();
        }

        public void End()
        {
            running = false;
            SpeedFraction = 0f;
            if (CameraFX.I != null) CameraFX.I.FovHold(0f);
            if (CameraShake.I != null) CameraShake.I.SetRoll(0f);
        }

        void OnDisable()
        {
            End();
            if (scrape != null) { scrape.Stop(); scrape.volume = 0f; }
            scrapeGain = 0f;
            if (grit == null) return;
            for (int i = 0; i < grit.Length; i++)
            {
                if (grit[i] == null) continue;
                gritAge[i] = -1f;
                grit[i].gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>
        /// Drive the sustained layers. Called every frame by <see cref="PlayerFeedback"/> from the
        /// motor's live state rather than from the start/end events alone: the events do the one-shots,
        /// but a respawn or a disabled player can lose an end event, and a slide effect that never
        /// released the FOV hold would be a permanently wrong lens.
        /// </summary>
        public void Tick(float feelFovHold, float feelRoll, float sparkRate, float dustRate, float scrapeVolume)
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            bool sliding = running && motor != null && motor.IsSliding;
            Vector3 vel = motor != null ? motor.Velocity : Vector3.zero;
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            float speed = flat.magnitude;

            SpeedFraction = sliding ? SlideImpulse.SpeedFraction(speed, endSpeed, maxSpeed) : 0f;

            // ---- camera: held, speed-tracked. FovForSpeed is zero at the motor's slideEndSpeed by
            // construction, so the slide releases the lens continuously instead of snapping it.
            if (CameraFX.I != null)
                CameraFX.I.FovHold(sliding ? SlideImpulse.FovForSpeed(speed, endSpeed, maxSpeed, feelFovHold) : 0f);

            if (CameraShake.I != null)
            {
                float roll = 0f;
                if (sliding && InputReader.I != null && player != null)
                {
                    Vector2 m = InputReader.I.MoveAxis;
                    Vector3 wish = player.right * m.x + player.forward * m.y;
                    roll = SlideImpulse.Roll(SlideImpulse.LateralSteer(flat, wish), SpeedFraction, feelRoll);
                }
                CameraShake.I.SetRoll(roll);
            }

            // ---- scrape: gain and pitch both ride the speed. Level alone reads as distance; it is
            // PITCH that the ear reads as speed on a broadband source.
            if (scrape != null)
            {
                float master = AudioManager.I != null ? AudioManager.I.masterVolume : 1f;
                float want = sliding ? SlideImpulse.ScrapeGain(SpeedFraction, scrapeVolume) * master : 0f;
                // Attack is immediate (the commit is an event); only the release is ramped.
                scrapeGain = want > scrapeGain
                    ? want
                    : Mathf.MoveTowards(scrapeGain, want, (scrapeVolume * master) / Mathf.Max(0.01f, scrapeRelease) * dt);
                scrape.volume = scrapeGain;
                scrape.pitch = SlideImpulse.ScrapePitch(SpeedFraction);
                if (scrapeGain <= 0.0001f && scrape.isPlaying && !sliding) scrape.Stop();
            }

            // ---- grit -------------------------------------------------------------------------
            if (sliding && grit != null && speed > 0.1f)
            {
                Vector3 dir = flat / speed;
                spawnAccum += SlideImpulse.GritRate(SpeedFraction, dustRate) * dt;
                while (spawnAccum >= 1f) { spawnAccum -= 1f; SpawnGrit(dir); }

                // Real sparks, rarely, and only while the slide still has speed in it. Routed through
                // SlashFx, which normalises every effect to a peak channel of exactly 1.0 — under the
                // 1.05 bloom threshold, so even these add no glow.
                if (SpeedFraction > 0.35f)
                {
                    sparkAccum += sparkRate * dt;
                    while (sparkAccum >= 1f)
                    {
                        sparkAccum -= 1f;
                        Vector3 at = player.position + Vector3.up * gritHeight - dir * 0.25f;
                        SlashFx.Sparks(at, -dir + Vector3.up * 0.35f, new Color(1f, 0.72f, 0.35f, 1f), 4, 5f, 0.55f);
                    }
                }
            }
            else spawnAccum = 0f;

            StepGrit(dt);
        }

        void SpawnGrit(Vector3 dir)
        {
            int i = nextGrit;
            nextGrit = (nextGrit + 1) % grit.Length;

            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 at = player.position
                       + Vector3.up * gritHeight
                       + side * Random.Range(-0.32f, 0.32f)
                       - dir * Random.Range(0f, 0.35f);

            var t = grit[i];
            t.position = at;              // world space under the scene-level root: left behind, not towed
            t.localScale = Vector3.one * gritSize;
            t.rotation = Random.rotation;
            t.gameObject.SetActive(true);

            float k = 0.45f + 0.55f * SpeedFraction;
            gritVel[i] = -dir * (gritBack * k * Random.Range(0.6f, 1.2f))
                       + Vector3.up * (gritUp * Random.Range(0.4f, 1.1f))
                       + side * Random.Range(-gritSpread, gritSpread);
            gritAge[i] = 0f;
        }

        void StepGrit(float dt)
        {
            if (grit == null) return;
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
                grit[i].position += gritVel[i] * dt;
                gritVel[i] += Vector3.up * gritGravity * dt;
                // Shrink to nothing so a mote winks out rather than vanishing mid-air at full size.
                float s = gritSize * (1f - k) * (1f - k);
                grit[i].localScale = new Vector3(s, s, s);
            }
        }
    }
}
