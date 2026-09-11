# Movement principles — what makes a system satisfying to pilot

A case study the user handed over on 2026-09-04 (a game-dev thread on satisfying movement: Tribes,
Rocket League, Celeste, Spider-Man, Just Cause, Days Gone, TrackMania), reduced to the rules that apply
HERE and mapped onto this project's systems. This is guidance for the parkour-first direction
([BACKLOG.md §0](BACKLOG.md)), not a new direction and not a list of mechanics to copy. Read it before
touching `FirstPersonMotor`, a traversal piece, a level or the movement HUD.

Related: [ARCHITECTURE.md](ARCHITECTURE.md) · [DATAFLOW.md](DATAFLOW.md) · [ENGINEERING-LOG.md](ENGINEERING-LOG.md)

---

## The rules

1. **Continuous beats discrete; a discrete input approaches continuous through DURATION.** A jump you
   can shorten by releasing (Mario) is more expressive than a fixed arc (Castlevania). Wherever a
   button fires a move, ask what HOLDING it should express.
   - *Here already:* the jump cut (release early = shorter hop), air steering, the mouse-driven look
     that yaws the body, the slide's continuous steer.
   - *Apply:* water skating steers continuously, never in steps; the balloon's two outcomes (bounce vs
     dash-through) are chosen by what you were already doing, not by a second button; a future
     charge-up (e.g. the grapple burst) may read hold length, never a menu.

2. **If a move is a heavy commitment, OWN it.** Castlevania's stiff jump works because the whole game
   is built around short-range planning of committed moves. A committed move in a fluid system must be
   deliberate and visibly announced.
   - *Here:* the slide is a committed 0.35 s (ENGINEERING-LOG: "a slide that ends when you release the
     key is a slide that ends when the frame is long"); the dash is a burst that settles; the enemy
     charge is a committed lunge. Each has a tell (legs thrown out, FOV hold, the cue flash).
   - *Apply:* never add a committed move without a read-before and a release-after.

3. **Momentum creates depth only if control stays INSTANT.** Momentum multiplies the situations a
   player can be in (grounded / airborne / carrying / bleeding) — but momentum bolted onto a
   responsive controller just feels laggy. The trick is to be instantly responsive to INTENT while
   staying committed to VELOCITY.
   - *Here:* speed is carried through soft caps and decays (`airCarryDecay`, `groundOverspeedDecay`)
     while steering and the look answer the same frame; the water floor and the slide's no-decay-on-water
     keep the carry without touching responsiveness.
   - *Apply:* any new surface or piece changes what happens to CARRIED speed, never how fast the
     player's input is answered. `LevelArcAnalyzer` should keep proving reach under the shipped decays.

4. **Fudge toward intent. The game "cheats" so the player gets what they meant** (Celeste: jump buffer,
   coyote time, corner correction; the near-miss push onto a ledge; Spider-Man's assists). Forgiveness
   that honours intent is not a gift — it is the difference between "the game did what I meant" and
   "the game stopped me on a pixel".
   - *Here already:* coyote time, the jump buffer, the wall-run entry judged on total speed and a wide
     approach cone, the slide surviving a lip (the grounded tolerance), the landing-slide.
   - *Apply (built 2026-09-05, `ForgivenessMath` + `FirstPersonMotor`):* **corner correction** — a rising
     jump whose head clips the CORNER of a ledge is nudged sideways up to 0.18 m and keeps its arc (a
     ceiling still stops it); **near-miss landing** — a falling player whose feet pass within 0.22 m under
     a ledge top they are moving toward is lifted onto it (no velocity added, never on a wall side, never
     while rising, sliding or wall running; a standing drop is a drop). Still open: near-miss onto a
     balloon or a water edge; the perfect-timing windows must be *learnable* (anchored to a physical moment: the
     wall letting go, the dash's peak, the pull landing), never frame-perfect, and a miss is the ordinary
     move, never a penalty.

5. **No shortcuts: the player controls every aspect, and mastery is visible.** Rocket League has no
   "shoot" button; the car IS the shot. Easy to pick up, and thousands of hours deep, because the same
   controls keep paying out.
   - *Here:* deflects are timing, not a button that parries; the grapple is a pull you then dash out
     of; the charge is answered by moving, not by a counter prompt.
   - *Apply:* prefer mechanics whose reward is EXPRESSION of the existing controls (a perfect wall jump
     refunding stamina) over new buttons. Do not add a "boost" button where a timed input would do.

6. **Upgrades and conditions must feel physical, not magical** (Days Gone's bike: money makes it
   stabler, surfaces change the ride). A change to movement should be felt as a change in the BODY or
   the SURFACE.
   - *Here:* water is a surface with friction off and a flow; the wall surge is a state you can see on
     the strip; stamina is a budget on the HUD.
   - *Apply:* any perfect-timing refund is felt (sound, a small punch, the bar visibly refilling), never
     only a number.

7. **Shapes.** Non-trivial movement draws shapes: circles (grapple swings), waves (a chain of balloon
   arcs), straight lines (dash, charge, water flow). A level is a sentence made of these shapes; a
   piece that draws no shape is decoration.
   - *Apply:* author balloon chains as ARCS with a rhythm (3 m apart = one launch each), water as LINES
     that turn the run, grapple targets as the pivot of a curve. When a span reads flat, ask which shape
     is missing.

8. **Consistency and predictability are freedom.** Spider-Man is freeing BECAUSE the swing is
   consistent. A move that behaves differently at different frame rates, or depending on what a
   previous move did, is not deep — it is unreliable.
   - *Here:* the motor's own clock, closed-form springs, the slide that is the same at 20 and 240 fps,
     `SlideImpulseTests` / `SlideFeelTests` frame-rate independence.
   - *Apply:* every new movement number ships with a test that runs its law at 20 / 60 / 240 fps.

9. **Take inputs away to find the game** (the superglued W key). A design tool, not a shipping rule:
   when a span feels like one played a thousand times, remove an input in the sandbox and see what the
   level asks for instead.

---

## How this changes what we build next

- **Perfect-timing refunds** (built): windows are anchored to physical moments, not frames. The predictable
  wall release is cued for 0.20 s before/after; dash-jump is cued for the final 0.10 s of its dash; burst
  remains the first 0.12 s after landing. Miss = ordinary, reward is felt on the body and the bar.
- **Balloons / water / burst** (built, unplayed): the balloon chooses its outcome from what you were
  doing (rule 1); water changes the carry, not the response (rule 3); both draw shapes (rule 7).
- **Level rework** (built, human feel pending): T1–T3 keep 10–18 m-wide landing-to-landing spaces, while
  towers, lintels, wall-run/wall-jump faces, connector ramps and the T3 balloon arc return on the expanded
  shoulders. Projectile parries still earn the first three realm approaches; hand-marked Insight flares
  offer faster, harder lines that rejoin the same course. Rules 4, 7, 8.
- **Level editor** (after): expose the pieces that draw shapes, not raw numbers (rule 7).
