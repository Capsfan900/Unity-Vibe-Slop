# Session handoff — five tracks stopped mid-flight

Written when five parallel agents were stopped part-way so the session could be resumed later with a
different model. **Everything described here is UNFINISHED and UNVERIFIED unless it says otherwise.**

Last known-good commit: `af5328a` (EditMode **186 / 186**). Everything below sits on top of that as
uncommitted or newly-committed work-in-progress.

Related: [BACKLOG.md](BACKLOG.md) · [ENGINEERING-LOG.md](ENGINEERING-LOG.md) · [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md) · `.claude/skills/unity-headless/SKILL.md`

---

## Read this first

**Subagents are session-scoped.** The five that were running cannot be resumed — they are gone. What
survives is the code they had written to disk at the moment they stopped, which is what this document
describes. A new session picks the work up from the files, not from the agents.

**Two files are PARKED and invisible to Unity.** `Assets/Editor/WeaponSilhouette.cs.wip` and
`WeaponShots.cs.wip` were renamed from `.cs` because the first calls `WeaponViewmodel.FindGrip`, a helper
its author had not written yet, and the second depends on the first. The work is preserved in git and
Unity ignores the extension. **To resume: write `FindGrip` (or remove the call), rename both back to
`.cs`, and delete nothing.** They were parked rather than fixed because guessing at another author's
half-built design is how you get a plausible-looking thing that was never intended.

---

## 1. Wall running — furthest along

**Goal.** The user asked for Apex-style wall running to support stretched-out parkour between mini-bosses.
The project had wall JUMPING only (`TryWallJump`, discrete pushes, `maxWallJumps 5`,
`sameWallCosineLimit 0.85`). Sustained running along a wall is new.

**On disk.** `FirstPersonMotor.cs` **+636 lines**, `PlayerLook.cs` +60, `LevelArcAnalyzer.cs` **+474**,
`SandboxBuilder.cs` +49, plus `Assets/Editor/Tests/WallRunMechanicTests.cs` (new).

A full authored parameter set exists: `wallRunMinEntrySpeed 7`, `wallRunMaxEntryFallSpeed 9`,
`wallRunMaxApproachCos 0.55`, `wallRunMinLookAlongCos 0.30`, `wallRunMaxDuration 1.6`,
`wallRunGravityStartScale 0.10` → `wallRunGravityEndScale 0.60`, `wallRunEntryUpSpeed 3`,
`wallRunSpeedDecay 0.20`, `wallRunMinSustainSpeed 5`, `wallRunAccel 14`. The shape is clearly "entry
requires speed and intent; gravity ramps back in over the run; the loan is short" — which is the right
shape. `LevelArcAnalyzer` was extended, presumably with the wall-run modelling the level task needs.

**One known failing test, and it is a real finding — start here.**

```
WallRunMechanicTests.ASlowEntryBleedsOutBeforeTheTimerDoes
  "a minimum-speed entry rode the full clock; wallRunMinSustainSpeed is unreachable
   and therefore not doing anything."
  Expected: Decayed   But was: Expired
```

The agent's own test says the speed floor never fires: a run entered at the minimum speed reaches the
1.6 s timer instead of bleeding out. So `wallRunMinSustainSpeed = 5` is currently decorative, and every
wall run ends the same way regardless of how fast you entered it — which removes the "a slow entry gets a
short run" feedback the parameter exists to give. Whether the fix is a stronger `wallRunSpeedDecay`, a
lower entry speed, or a different decay model is **the author's design decision and was not made**; do not
guess. Note this is exactly the good case — the test was written to measure the outcome, so it caught its
own mechanic being inert rather than asserting the constant back at itself.

**To resume.** Fix the above; then verify the rule-1 requirement (every timer and
integration on `TimeScaleController.PlayerDelta`, never `Time.deltaTime`); and **test at several fixed
timesteps** — a framerate-dependent slide shipped here once (4.0 m at 500 fps, 1.8 m at 20 fps). Check
rule 9: are these values written by `PrefabFactory` onto the Player prefab, or only field initialisers?
Then check what `LevelArcAnalyzer` gained and whether it can answer "is this run possible, and where does
it land me".

---

## 2. Level restructure — NEVER STARTED, and it is the dependent one

The user asked for the level to be **stretched out for wall-running parkour between the three
mini-bosses** (`Spawn_Legendary_Ninja`, `_Knight`, `_Spellsword`, which gate progression via
`clearSpawnerName`). This was deliberately not started, because it needs wall running's real numbers and
the analyser extension above. Start it only after track 1 is verified.

The precedent to follow is `BACKLOG.md` §5b: The Ascent's wall-jump line shipped this session **without a
playtest** because `LevelArcAnalyzer` flew the real ballistic arc and proved the new fin cost the existing
`T2_L2 → T2_L3` hop two of twenty-five take-off points and zero clearance. Do the same for wall runs.

---

## 3. Slide and dash feel

**Goal.** The user: *"the movement is good but the slide and the dash dont have visual feedback or feel"*.

**On disk.** New: `Assets/Scripts/Feel/SlideFx.cs`, `DashFx.cs`, `SlideImpulse.cs`, `DashImpulse.cs`,
`Assets/Editor/Tests/DashImpulseTests.cs`. Modified: `PlayerFeedback.cs` +169, `CameraFX.cs`,
`CameraShake.cs`, `GameFeelSettings.cs` +61, and the GameFeel region of `DataFactory.cs`.

The `*Impulse` / `*Fx` split mirrors `ParryImpulse` / `ParryImpact` from earlier this session — pure
shape maths separated from the component that applies it, so curves are unit-testable. That is the right
structure.

**Key context it was given, worth preserving.** Feedback already partly existed (`OnDashed`,
`OnSlideStarted`, `OnSlideEnded` events with `PlayerFeedback` subscribed; `dashFovKick = 8` shipped), so
the question was "absent, or present and too weak to read". The slide is the harder half: it is
*sustained*, and its middle currently reads as nothing. It must not out-shout `CueFlash` — the parry work
this session deliberately added **zero brightness** for that reason.

**To resume.** Check `GameFeelSettings` additions are written by `DataFactory` and asserted (rule 9); that
VFX run on unscaled time and player motion on `PlayerDelta`; and photograph it.

---

## 4. Weapon variety

**Goal.** The user: *"make there be more than just daggers again"* — reversing an earlier request that had
turned every weapon into a dagger variant because those *"show the animations best and feel best"*.

**On disk.** `DataFactory.cs` +130 (Weapons region) and `PrefabFactory.cs` +150 (viewmodels), plus the two
**parked** capture tools. Progress note at stop: baseline confirmed, mid-way through the hammer.

**The constraint that must not be lost.** `Sword.parryPostureDamage = 25` is load-bearing:
`25 × 1.4 × 6 = 210` is exactly the Pale Marionette's posture bar, and
`MarionetteDataTests.SixCleanDeflects_BreakIt` asserts the six-deflect break. Change it and either keep
the arithmetic landing on 6 or move `maxPosture` in the same edit.

**Also:** do not simply revert to the pre-dagger shapes. Find what the dagger silhouette does well —
short readable arcs, a viewmodel that does not fill the screen, a legible contact frame — and keep it
while making the archetypes distinguishable in reach, timing and pose.

---

## 5. Eclipse sky — barely started

**Goal.** *"make the sky look like the eclipse from berserk and red instead of purple"* — an enormous
black solar disc ringed by burning light, the world drowned in red. Currently purple/void with a
starfield.

**On disk.** Only `Assets/Editor/SkyShots.cs` (a capture tool). It had just finished reading the docs when
it stopped. Effectively unstarted.

**The trap waiting for it, and the reason this one needs care.** ACES tonemapping in this project
**desaturates saturated colours toward orange above ~1.25 intensity**, so a bright saturated red comes out
orange and washed. Buy presence with area and contrast, not intensity. Also:
`RenderSettings.ambientIntensity` is a **no-op in Trilight mode** (a whole tuning session was lost to
that once) — drive the three Trilight colours instead.

**The acceptance test that matters more than the sky itself:** photograph **an enemy silhouetted against
it at combat range**. The sky is the backdrop every enemy read happens against, and a Berserk Eclipse is a
*bright* image. If silhouettes stop reading, it is not shippable however good it looks alone.

---

## 6. Settings menu

**Goal.** *"add a settings menu with sens and some graphics settings"*. There was no options UI anywhere.

**On disk.** New: `Assets/Scripts/Core/SettingsData.cs`, `SettingsStore.cs`, `SettingsApplier.cs`,
`Assets/Scripts/UI/SettingsMenu.cs`. The store/applier split is deliberate.

**The design constraint that produced that split.** Sensitivity lives on `PlayerLook.mouseSensitivity` /
`stickSensitivity`, and `PlayerLook.cs` was being edited concurrently by the wall-run track — so the
settings code **writes to those public fields from its own applier** rather than editing `PlayerLook`.
Keep that: a settings store that owns persistence and applies outward is better than a menu reaching into
gameplay classes.

**To resume.** It still needs to be reachable from the main menu **and** in-game (a settings screen you
can only open before pressing play is half a feature, since sensitivity is what you want to change the
moment you start moving). Rule 5: **never `Image.fillAmount`** — a null-sprite UGUI `Image` silently
ignores it; `BarView` drives RectTransform anchors. Rule 1: if opening settings pauses, go through
`TimeScaleController`.

---

## The state the tree was left in

**Verified after stopping: the tree COMPILES and EditMode is 215 / 216**, the single failure being the
wall-run test described in track 1. That is a deliberate stopping point, not a broken one — a new session
can open the project, run the suite, and see exactly one red test pointing at exactly one unmade design
decision.

- Two files parked as `.cs.wip` (above). Everything else compiles.
- No agent wrote to `docs/` — that was reserved to the main session throughout, which is why five
  concurrent agents never collided on documentation.
- `.claude/skills/unity-headless/SKILL.md` documents the verification workflow every one of these tracks
  used, including five traps that each cost real time. **Read it before resuming any of them.**
- The Unity MCP bridge is *not* broken. This session's client lost a startup race and never retried; the
  server has been healthy on `127.0.0.1:8090` throughout. `/mcp` → UnityMCP → Reconnect. Once connected,
  **play-mode `FeatureTests` (666 assertions) becomes available**, which none of this work has had.

## What none of this has

**Nobody has played any of it.** Not the wall running, not the weapons, not the slide and dash feel, not
the sky, not the menu. Every claim in this session is EditMode arithmetic, shipped-asset assertions and
headless captures. That is real evidence and it is not the same thing as the game being good.
