# Engineering log

Institutional memory for `vibegame1`. Every non-obvious problem that cost real time, and the invariant
that stops it recurring. **Read this before debugging anything weird** — there is a good chance it is
already in here.

Newest first. When you solve something non-obvious, add an entry at the top in the same shape:
**Symptom → Root cause → Fix → Invariant**.

Related: [ARCHITECTURE.md](ARCHITECTURE.md) · [TOOLING.md](TOOLING.md) · [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md) · [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md)

---

## A matte material cannot show curvature, so no amount of light will shape it

**Symptom.** Enemies rendered as flat cutouts against the dark. The ambient pass had already lifted a
backlit grunt from a measured 0.0 to 8.8 against a 36.3 floor — the silhouette was there, but the inside
of it had no tone at all, and raising ambient further only greyed the whole frame.

**Root cause.** `MaterialFactory.Configure` set `_Smoothness = 0` **and** `_SPECULARHIGHLIGHTS_OFF` on
every material unconditionally, for the flat neon look. With both, a surface has only a diffuse term.
This course is deliberately backlit, so the side of an enemy facing the player receives no key light —
diffuse alone therefore renders one uniform value across the whole torso no matter how the geometry
curves. There was no term left in the shading that *could* describe shape.

**Fix.** Smoothness became per-`Spec` (default 0, so the neon shapes are unchanged) and the specular
keyword is only forced off when a spec actually asks to be matte. `M_Enemy` ships at **0.34**: enough
for the ambient sky term to skim a shoulder, not enough to look wet.

**Invariant.** Reach for the **shading model** before reaching for more light. If a surface looks flat
and adding light only makes it a brighter flat, the missing thing is a specular term, not lumens. And
note `M_Enemy`'s base colour is overridden per-enemy by `EnemyData.bodyColor` through a
`MaterialPropertyBlock` — smoothness is not, so it applies to every enemy at once.

---

## A suite that "waits and nothing happens" is one leaked time handle, not 24 bugs

**Symptom.** A full run reported 24 failures spread across Weapons, Arms, LockOn, Items, Flask,
Progression, LevelFlow and every GateLoop arena. Read individually they look like eight unrelated
systems breaking at once. The next run of the same build reported **518 passed / 1 failed**.

**Root cause.** Every one of those failures is a check that needs *time to pass*:
`Arms_HandFollowsPose [hand moved 0.000m]`, `LockOn_AssistRecentresTarget [before=20.0 after=20.0]`,
`Items_PhantomExpires [invulnerable=True]`, `Level_KillZoneKills [dead=False]`, and gates that never
lerped. Something held a `TimeScaleController` request at 0 partway through the run. The suite kept
marching because `WaitUntilOrTimeout` is measured in **realtime**, so a frozen world does not hang the
run — it produces a long, plausible-looking list of unrelated failures instead.

**Fix.** Diagnose by shape before reading the list: if the failures are all "waited and nothing
changed", suspect one frozen clock, not many broken systems. Sampling `Time.timeScale` *after* the run
proves nothing — the handle is released by the suite's own teardown, so it reads 1 by the time you look.

**Invariant.** **Failure count is not failure diversity.** Before investigating N failures, ask whether
one shared resource — the time scale, the game state, a wiped static — explains all of them. Related:
"A play session that has survived a domain reload is not a play session", below.

---

## Importing the first real enemy models: four traps between the FBX and a fight

**Symptom.** Two of the legendary mini-bosses were swapped from procedural primitives to models exported
by `enemy-forge` (`Assets/Enemies/AshenChorister.fbx`, `IronPenitent.fbx`). Nothing about the swap failed
loudly. It failed in four quiet ways instead, each of which would have shipped.

**Root cause.**

1. **The forge's own `AssetPostprocessor` never ran.** `EnemyForgeImporter` was copied into
   `Assets/Editor/` in the *same* refresh that first imported the two FBXs. Unity imports assets before
   the new editor assembly exists, so `OnPreprocessModel` was never called — and because the postprocessor
   guards on `importer.importSettingsMissing`, a later reimport is a no-op by design. The models came in
   as **Generic**, not the Humanoid the script intends. (Generic is the right answer here anyway; see the
   invariant.)
2. **"Unity axes" is not a facing.** The forge exports pre-normalised — feet at y = 0, crown at y = 2 —
   but which way the figure looks is not part of that promise. Both models turned out to face **+Z**,
   which is what we wanted, but the first attempt to *deduce* it from vertex colours got the wraith
   backwards: the hood's teal trim wraps the *back* of the hood too, so "where are the cyan vertices"
   answered a different question than "where is the face".
3. **The auto-rig does not follow the art.** Both FBXs ship a 21-bone Unity-named humanoid skeleton, and
   it is placed by proportion, not by silhouette: the Chorister's `RightUpperArm` hangs *inside* the robe
   while the visible art holds a scythe across both shoulders. Binding `EnemyVisuals.armPivot` to that
   bone and driving it to the telegraph poses (up to ±136°) tears the mesh.
4. **A cadence enemy that lunges every beat walks into your face.** Unrelated to the import, but found by
   it: the Iron Penitent's redesigned spin is a six-hit phrase, and at the first-pass `lungeDistance 0.9`
   per beat it travelled 4.2 m → 1.5 m over one phrase. A 3.2 m silhouette at 1.5 m has no readable
   wind-up at all.

**Fix.**

1. Set the rig type explicitly rather than relying on the postprocessor having run. Generic is correct:
   nothing here is animated by an `Animator` — `EnemyVisuals` drives plain transforms — so a Humanoid
   avatar buys nothing and a tentacle-skirted wraith would fail to produce a valid one.
2. Verify facing by **rendering** the model from ±X/±Z on a clean background and looking at it. Four
   orthographic captures cost one `execute_code` call and settle it; the vertex-colour heuristic cost
   three and got it wrong.
3. Do **not** drive the imported skeleton. `armPivot` / `weaponPivot` are empty transforms parented under
   the model, and `EnemyVisuals.weapon` is a 3 cm non-shadowing marker cube at the blade/fist so
   `WeaponPoint()` still throws the cue sparks from the right place. The wind-up then reads through
   `LungeRoot`'s whole-body lean plus the base-colour sink-and-snap, exactly as it does on the primitives.
4. `lungeMinDistance` 1.5 → 3.0 and the spin's per-beat lunge 0.9 → 0.45.

**Invariant.** **An imported model is art, not a rig.** Assume its skeleton is decorative until you have
proved otherwise, keep every gameplay pivot as an empty transform you own, and keep colliders, the
`NavMeshAgent` and `EnemyData.scale` on the prefab root (model-swap contract rule 3) — the Chorister
hovers by lifting the *mesh* 0.10 m inside `Visual`, never by touching `agent.baseOffset`. And **verify a
model's axes by looking at a render, never by reasoning about its vertices**: the mesh will happily
answer a question you did not ask.

Also worth knowing: `PrefabUtility.UnpackPrefabCompletely` does not exist. The call is
`PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction)`.

---

## Three feature tests failed intermittently: all three raced the frame, none was a game bug

**Symptom.** `Parry_LateIsBlock` (`result=Hit elapsed=0.333`), `Posture_RegeneratesAfterDelay` and
`Level_TimerStartsOnFirstInput` each failed roughly one run in several and passed on a re-run. Nothing in
the shipping code changed between a red run and a green one.

**Root cause.** Three different races, one shape — each test measured *wall clock* against a system that
does not run on it.

1. **`Parry_LateIsBlock`** busy-waited `while (Time.time - pressAt < perfect + late * 0.5f)` to land in
   the middle of the block window. `ParryController.Update` closes the window at `pressTime + perfect +
   late` = 0.25 s, and `Update` runs *before* the coroutine resumes. One long editor frame — a GC spike,
   an asset import, another agent's compile — steps clean past the whole 0.25 s window, so the resolve
   happens in `Recovery` and a legitimate block reads as a plain `Hit`. The logged `elapsed=0.333` was
   the tell: it is past the entire window, not merely past `perfect`.
2. **`Posture_RegeneratesAfterDelay`** waited a fixed **realtime** span while `PlayerPosture` measures
   both `postureRegenDelay` and its regen step in **scaled** `Time.time` / `Time.deltaTime`. Any hitstop
   still ringing from the staggered-damage assert immediately above means the delay has not elapsed when
   the wait ends. Worse, the assert was `posture.Current < before` — a *net* comparison that silently
   assumes nothing else touches posture for the whole wait, and a single hit from a live enemy pushes the
   total back above the starting value (observed: `60.0 -> 92.3`).
3. **`Level_TimerStartsOnFirstInput`** waited 0.25 s and asserted `Elapsed` had grown. `SpeedrunTimer`
   only accrues while `GameManager.State` is `Playing` or `Dead`, and the kill-zone assert directly above
   it leaves the state machine mid-transition (and the spawn wand pedestal can be holding a menu open,
   which pauses).

**Fix.** Make each measurement independent of frame length.

1. Back-date the live press by reflection (`BackdateParryPress`) so `elapsed` is exactly the value
   asserted on, with no frame boundary between setting it and resolving. It moves the clock the
   controller reads, not one line of the logic it runs.
2. Settle the time scale first, then watch for a **frame-over-frame** fall rather than a net one — that
   is what "regenerates" actually means, and it is immune to a top-up mid-wait.
3. Pin `GameState.Playing` and force the wand menu closed, then wait on the *condition* with a bound
   instead of on a slice of wall clock.

**Invariant.** **A play-mode test must never assume its own frame length.** If the system under test runs
on scaled time, a realtime wait races it (and vice versa); if the assertion is "X changed", wait on that
condition with a timeout rather than sleeping and hoping. And where a window is only a few frames wide,
set the clock the system reads rather than trying to arrive inside it — a busy-wait to the middle of a
0.25 s window is a coin flip on a bad frame, and it flips against you exactly when the machine is busy,
which is exactly when a suite is most likely to be running.

---

## The lock-on dot was invisible: a marker on the centre of mass is inside the body

**Symptom.** Lock-on acquired correctly, the marker reported `IsShown = true`, the dot was at exactly the
right world position, and it could not be seen at any distance in any screenshot.

**Root cause.** The dot marks the target's **chest**, which is the centre of a 0.45 m-radius capsule. It
was rendering *inside* the enemy it was marking. Nothing in the state was wrong — every assertion about
position and visibility passed — which is why it read as "the emission is too low" for a while. It was
not; it was behind 0.45 m of enemy.

**Fix.** `LockOnMarker.Track` pulls the dot `frontOffset` (0.75 m) along the direction to the camera,
capped at 30% of the distance so it cannot end up in the player's face point-blank. The alternative — a
depth-test-off overlay material — was rejected: more fragile, and it would make the dot shine through
walls, when being occluded by real cover is behaviour we want.

**Invariant.** **A world-space marker placed at an object's centre of mass is inside it.** Every other
marker in this project floats above the head and so never hit this. Anything drawn *on* a body must be
lifted off the surface toward the eye, and the test must assert that: `LockOn_DotClearsTargetBody` checks
the dot is on the eye-to-chest ray *and* short of the chest, not merely "at the right place".

---

## Lock-on folds target switching into the lock key, because the scroll wheel was already taken

**Symptom.** None — caught before shipping, by dumping the bindings before choosing a key.

**Root cause.** The two Souls conventions are middle mouse to lock and the scroll wheel (or a right-stick
flick) to switch. Middle mouse was free. The scroll wheel is **not**: `Previous` and `Next` are bound to
`<Mouse>/scroll/down` and `<Mouse>/scroll/up` for weapon cycling, and `WeaponController` polls them every
frame with no gate. Binding target-switching there would have reproduced the `F` flask-versus-altar
collision exactly — one input, two consumers, both firing.

**Fix.** One key, three verbs, disambiguated by where the player is aiming: not locked, acquire; locked
and still looking at the target (within 14 degrees), release; locked and looking elsewhere, switch. No
second binding, and nothing to learn beyond "point at what you want".

**Invariant.** **Dump the whole action map before choosing a key.** A three-line script over the
`.inputactions` JSON prints every action and every path in one go. Two consumers of one input is this
project's most-repeated input bug and the Input System will never warn you about it.

---

## An aim assist must spend only the frames the player is not using

**Symptom.** N/A — a design constraint, recorded because it is the thing to preserve if lock-on is ever
retuned.

**Root cause.** This is a first-person parry game with a 0.13 s perfect window, so the mouse is the parry
hand. Any assist that scales, filters, damps or overrides the look delta is input lag on the one input
the whole game rests on, and it will be felt long before it is diagnosed.

**Fix.** `PlayerLook.Update` applies the player's mouse untouched, exactly as before. `LockOnController`
runs in `LateUpdate` — after that — and *adds* a correction through the single sanctioned door,
`PlayerLook.NudgeAim`. The correction is multiplied by a yield gate that reaches zero as soon as the
mouse is genuinely moving, so an assist frame and a player frame never overlap; and past 62 degrees off
the target the lock **drops** rather than pulling back.

**Invariant.** **An assist adds, it never subtracts.** It gets the still frames and nothing else. And
because the correction is player-driven timing, it integrates `TimeScaleController.PlayerDelta`, never
`Time.deltaTime` (rule 1) — on scaled time it would stall during the hitstop of the very parry it exists
to help you land, which reads as a dropped frame at the worst possible moment.

---

## Outside the trims and the eclipse, the frame was black — and it was not the tonemapper

**Symptom.** Three separate passes hit the same wall from three directions and each blamed a different
thing. A riposte photographed the enemy as a **flat black cutout** with no interior shading (measured:
**0/255** on the body). Distance shots of the four tiles were near-total black — only the emissive trim
outlines said geometry was there. Material captures showed structural walls and pillars with effectively
no tonal range. The natural suspects — ACES, the bloom threshold, colour grading — were all behaving
correctly.

**Root cause.** Two multiplicative near-zeros, and neither of them lives in post.

1. **Ambient was `AmbientMode.Trilight`, and Trilight lights by surface NORMAL.** The sky term only
   reaches *up-facing* faces, the ground term only *down-facing* ones, and **everything vertical is lit by
   the equator term alone** — which was `#4E3325`, about **0.040 linear luminance**. In a first-person
   platformer, vertical faces are not a minority case: walls, pillars, obelisks, risers and enemy torsos
   are the surfaces the player actually aims at. Worse, the course is deliberately **backlit** — the key
   directional shines from behind the boss arena toward the player — so an enemy the player is facing
   receives *no key light at all* on the side that faces them. The equator term was the only thing
   rendering every enemy in the game.
2. **The structural albedos were below anything physical.** `M_Ground #151011` is **~0.008 linear
   reflectance** — darker than coal — and it covers most of the structural surface area. `M_Stone #1E1819`
   was ~0.013. Enemy bodies were `#0A0708`, ~**0.004 linear**.

Multiply them and you get ~6/255 for a wall and 0/255 for an enemy. There was no detail going *into* the
tonemapper, so no grading change could bring any out. Vignette 0.34 and film grain 0.35 then finished off
what little range survived — and in first person the vignette crushes hardest exactly where the platform
you are about to land on sits, at the bottom edge of the frame.

**Two traps made the first attempt at the fix a silent no-op**, and both are the real lesson here:

- **`RenderSettings.ambientIntensity` does nothing in Trilight (or Flat) mode.** It scales *Skybox*
  ambient only. Setting it to 1.35, and then to 2.7, rendered **pixel-for-pixel identically** to 1.0 —
  measured, not assumed. In Trilight the multiplier has to live in the colours, which are HDR:
  `Hex("#7A5540") * 1.35f` is the only form of the knob that does anything.
- **`M_Enemy`'s albedo is dead for enemy bodies.** `EnemyVisuals.WriteBody` pushes `EnemyData.bodyColor`
  into a `MaterialPropertyBlock`, which overrides `_BaseColor` on every body renderer. Changing
  `MaterialFactory`'s `M_Enemy` and re-running `2. Create Materials` changed nothing on screen; the
  shipped value was in `DataFactory`, and it needed `3. Create Data`.

**Fix.** Lift both sides of the multiplication; touch nothing in post that affects emissives.

| Knob | Was | Now |
|---|---|---|
| ambient equator | `#4E3325` @ intensity 1 | `#7A5540 × 1.35` |
| ambient sky | `#2B3654` | `#3E4A6B × 1.35` |
| ambient ground | `#0E0B12` | `#191424 × 1.35` |
| `ambientIntensity` | 1.0 | 1.0 (documented as a no-op) |
| key directional | 0.85 | 1.05 |
| `M_Ground` | `#151011` | `#262023` |
| `M_Stone` | `#1E1819` | `#3A3134` |
| `M_Platform` | `#48423D` | `#56504A` |
| `EnemyData.bodyColor` | all ≈ `#0A0708` | `#3A3340` / `#423630` / `#40304C` / `#2E363C` / `#443A34` / `#3A3050` |
| vignette | 0.34 | 0.27 |
| film grain | 0.35 | 0.26 |

Measured at identical camera positions, before → after: ground **23 → 36/255**, backlit grunt at 4.5 m
**0 → ~9/255**, gate emissive **154.1 → 155.2**, alert tell **204 → 211**. The world came up; the
emissives did not move. An intermediate equator of ×2.4 was tried and **rejected** — it put the ground at
53/255, which is a lit room, not a dark one.

**Invariant.** **Trilight ambient lights by normal, and vertical surfaces are most of a platformer.**
The equator term is not a minor third of the ambient budget — in first person, with a backlit course, it
*is* the budget, and it is the only light on every enemy the player is facing. Alongside it:
**a near-black ambient multiplied by a near-black albedo is a double zero** — when a scene reads as black,
measure the *linear* product of ambient × albedo before touching grading, because no tonemapper can
recover contrast that was never rendered. And **judge an albedo by what it renders as, not by the hex**:
these "mid grey" enemy values render at 8–10/255. `FeatureTests.TestTellReadability` now asserts an
equator floor (deliberately *not* multiplied by `ambientIntensity`, so a build that "raised the ambient"
with the no-op knob fails), a 0.010-linear floor on every structural albedo, and that the alert tell still
clears the brightest structural albedo by 20×.

---

## The level round-tripped perfectly and lost the sky and the wand altar

**Symptom.** `Export Current Level To Definition` → `Build Level From Definition` reported success and a
matching platform count. A hierarchy diff of the two scenes was clean except for six objects: the whole
`Sky` (starfield dome + eclipse) and all of `WandPedestal_Start`. The level looked *nearly* right, which is
worse than looking wrong — the missing altar means no wand can be chosen, and that is invisible until you
play it.

**Root cause.** `LevelDefinition` had no field for either. The exporter walks the direct children of
`Level` and classifies them (kill zone, spawner, checkpoint, torch group, pickup group, …, else "anything
that renders a mesh is geometry"). `Sky` renders on the Sky layer and `WandPedestal_Start` is an empty
trigger root, so **both fell through every branch and were silently discarded**. The pedestal's *plinth*
survived only because it happens to be a plain box. A schema that cannot express something exports it as
nothing, and reports the same success either way.

**Fix.** Added `SkyDef sky` and `PedestalDef[] pedestals` to `LevelDefinition`, taught both the exporter and
`LevelDefinitionBuilder` about them (including skipping `<pedestal>_Plinth` on export, or the next build
stacks two plinths), and re-ran the round trip to a clean diff.

**Invariant.** **A clean round-trip diff is the acceptance test for a data migration, not the export's own
log line.** Snapshot the scene before and after and diff it object by object; an exporter cannot warn about
a category it does not know exists.

---

## The mini-boss arena was already open when the run started

**Symptom.** Building the four-tile level, every mini-boss exit gate sat in its open position from frame
one, so the gates gated nothing.

**Root cause.** `BossArenaTrigger` decides an arena is cleared when its `EnemySpawner.Instance` is gone.
`LevelManager.SpawnAll()` runs in `Start()`, so for the first frames `Instance` is legitimately `null` —
indistinguishable from "the enemy died". The same shape appears whenever "the thing is absent" is used as a
proxy for "the thing is finished".

**Fix.** Latch on having *seen* it alive: the trigger returns "not cleared" until `Instance` has been
non-null once, and only then treats null-or-`Health.IsDead` as death. `ResetArena()` clears the latch.

**Invariant.** **Absence is not completion.** Any "is it done yet" check that reads emptiness must first
observe non-emptiness, or it fires before the thing it is watching has been created.

---

## Two level builders, one level, and no way to tell which one you got

**Symptom.** Latent, caught before it bit: after the four-tile rework was authored as data,
`VibeGame1/6. Build Level` — which is also step 6 of **Rebuild Everything** — still built the *old*
hard-coded three-section course. Any routine full rebuild would have silently reverted the rework, with a
success message.

**Root cause.** `LevelGreyboxBuilder` (literal coordinates) and `LevelDefinitionBuilder` (data) were parallel
implementations of the same output, sharing nothing but a comment promising they agreed.

**Fix.** `LevelGreyboxBuilder.Build()` now loads `Assets/Data/Levels/Level_01_Level.asset` and forwards to
`LevelDefinitionBuilder.Build()` whenever it exists. The literal course is still there as
`BuildHardcoded()` — reachable, still the reference implementation, no longer reachable by accident.

**Invariant.** **Two generators that can produce the same artefact must not both be reachable.** Make one
the front door and have it delegate; a comment claiming they match is not a mechanism.

---

## The first-person hand read as a slab with a blade stuck through it

**Symptom.** The new viewmodel hand had a palm, four fingers, a thumb, a knuckle band and a cuff — ten
boxes, plenty for a greybox — and on screen it was one featureless dark rectangle with a sword sticking
out of it. Zooming a debug camera onto the hand showed the same thing: a cube.

**Root cause.** Depth ordering, not detail. The fingers were authored on **+Z**, i.e. on the far side of
the hilt from the camera, and the palm plate on −Z facing the lens. In first person the camera only ever
sees the −Z face of the hand, so every piece that says "this is gripping something" was hidden behind the
one piece that says nothing. Adding more boxes would not have helped; every one of them was behind the
palm.

**Fix.** Fingers moved to −Z **in front of** the hilt (and given the lighter trim material), palm pushed
behind it on +Z, knuckle band dropped as redundant, cuff darkened so it stops being the brightest thing
on the hand. The hilt now passes visibly *between* the fingers and the palm.

**Invariant.** **For a first-person prop, which face points at the lens matters more than how many boxes
it has.** Author the detail on the −Z side of the hand and let the weapon pass between the fingers and
the palm; occlusion is what reads as a grip. A closed silhouette with the interesting geometry behind it
is indistinguishable from a cube at 95° FOV.

---

## A generator "ran successfully" and produced the old geometry

**Symptom.** Edited `PrefabFactory`, called `PrefabFactory.BuildAll()` through MCP, got
`Code executed successfully` and `[PrefabFactory] All prefabs built.` — and the rebuilt prefab still had
the *previous* build's children. Repeating the rebuild changed nothing. It looked like the prefab was
being written from a cached copy.

**Root cause.** Unrelated code elsewhere in the project (another in-flight refactor) did not compile, so
Unity kept the **last good assembly** loaded. `execute_code` binds against whatever assembly is loaded, so
it cheerfully ran the *old* `PrefabFactory` and reported success. Nothing in the call's result mentions
compilation — the only evidence is in the console, which had a dozen CS1061s from files nobody in this
task had touched.

**Fix.** `read_console` for errors *before* trusting any generator run, and confirm the new code is live by
asserting a value only the new code produces (a new child name, a new material colour) rather than by
reading the "success" message.

**Invariant.** **A green `execute_code` result proves the editor ran something, not that it ran YOUR code.**
Unity keeps the last compiling assembly, so with a broken project every generator silently rebuilds the
previous version. Check the console for compile errors first; if the project does not compile, no
regeneration is trustworthy. The same rule catches the sibling symptom — a *play-mode* capture taken after
an edit is often taken in **edit mode**, because the recompile dropped play mode; `WeaponViewmodel.data`
and its weapon instance are runtime-only, so an edit-mode Player shows a hand collapsed at the camera
origin and no viewmodel at all. Verify `EditorApplication.isPlaying` in the same call that grabs the frame.

---

## A cooldown on the riposte would have made the boss unkillable

**Symptom / trap.** The wand was given a per-wand cooldown (3.5–9 s). The obvious implementation —
refuse `TryExecute` while the wand is cooling — soft-locks the boss fight, and does so *silently*: the
boss's deathblow window is 5 s and only `isExecute` damage removes a segment, so a player holding
Voidspine (9 s) who ripostes segment one can never open segment two. Every cooldown in the set is
longer than the window.

**Root cause.** The riposte is two things wearing one name: a **gameplay** deathblow (the only way to
remove a boss segment) and a **presentation/damage** wand blast. Gating the second gates the first.

**Fix.** The cooldown gates the blast only. `ExecuteInteractor` resolves
`wand = wands.WandReady ? wands.Current : null` and the existing no-wand melee execute path handles it
— the deathblow always lands, it is just plain. The `EXECUTE` prompt appends the remaining seconds so
the downgrade is announced rather than discovered.

**Invariant.** **Never put a cooldown, cost or resource in front of the boss deathblow.** Anything that
gates the riposte must degrade it, not refuse it, and must say so through `PromptChanged` — a riposte
that quietly comes out as melee reads as a broken wand. More generally: before gating an action, check
whether it is also the *only* path through some other system's state machine.

---

## Charge that reads only on the HUD is charge the player does not feel

**Symptom / trap.** The Pyre meter fills on every successful parry and unlocks the super at full. Shown
only as a bottom-left bar, it is invisible during the exact moment it matters — a parry exchange, when
the player's eyes are locked on the enemy's cue flash at the centre of the screen.

**Root cause.** The HUD is where you *confirm* a resource, not where you *feel* one.

**Fix.** `Feel/WeaponEmber.cs` puts the meter on the weapon: the blade heats toward ember orange, sheds
more embers, and lights a small point light, all in proportion to charge. The bar became the
confirmation.

**The trap inside the fix.** Two of them, both already in this log in other forms:

1. **Emission is owned.** `EnergyGlow` rewrites `_EmissionColor` on the blade every `LateUpdate`, so a
   `MaterialPropertyBlock` write from the ember survives one frame. It calls `SetTint`/`SetCharge`
   instead. ONE WRITER PER MATERIAL CHANNEL.
2. **A stock primitive material has `_EMISSION` off**, so the pooled ember cubes silently rendered as
   grey boxes until they were given the blade's own material. A property-block write to a keyword that
   is not enabled is a no-op with no error — the same shape as the `Image.fillAmount` bug.

**Invariant.** **A resource the player spends in combat must be legible on the thing they are looking
at.** And when escalating a viewmodel effect, escalate *rate and motion*, not brightness: this project
has twice shipped a viewmodel effect bright enough to destroy the frame it was punctuating (the black
-screen riposte, `ScreenFlash` at 0.55). `EnergyGlow` charge is capped at 0.55, the ember light at 1.5
intensity / 2.4 m range on a squared ramp. Verify with a screenshot at zero, partial and full charge —
`FeatureTests` is blind to all of it.

---

## The riposte was invisible: the step-in put the camera inside the victim

**Symptom.** *"The wand and magic need to be more visible in the FPS view — it just looks like an explosion,
you can't see the wand or anything."* The riposte fired correctly and every test passed, but on screen it was
a white blast with no visible source.

**Root cause.** Four independent faults, all of them presentation, and all invisible to the test suite:

1. **`ExecuteInteractor.stabStandoff = 1.25`.** The grunt's body is a capsule of radius 0.45, so the lunge
   parked the camera 0.8 m from its surface. At 95° FOV that is a wall of unlit black filling the whole
   frame. The riposte's own step-in was hiding the riposte. This was the dominant cause.
2. **The blast was drawn on the victim.** `SlashFx` sparks and a flare at the enemy's chest, plus a
   `LightningEffect` that strikes from 18 m above — nothing anywhere connected the effect to the wand.
3. **The wand had no light of its own.** The shaft uses a dark material, the world is near-black, and the
   scene lights the enemy as a silhouette. Only the emissive tip survived; the prop read as a stick.
4. **The punctuation was drowning the subject.** `ScreenFlash` at 0.55 alpha over a bloom-heavy frame, plus
   `ChromaticPulse(1.0)`, shredded the frame into RGB confetti — including the wand.

Also contributing: `viewmodelScale 0.5` (a splinter at 95° FOV) and a thrust pose that kept the wand
*upright* while pushing it to z = 1.02, so it never pointed at anything and shrank as it committed.

**Fix.** `stabStandoff` 2.2 (framing the victim is now an explicit contract, not a side effect). New
`SlashFx.Beam(from, to, …)` primitive, fired tip → contact on every discharge, plus a muzzle flare at the
tip. A point light on `OffhandViewmodel` that rides `TipWorldPosition`, ramps with the charge and blows out
on the discharge — it lights both the wand and the victim. Flash down to 0.20/0.16 s, chroma to 0.4. Thrust
pose pitched 58° forward and held near the lens; per-wand `viewmodelScale` 0.66–0.82. Hold beat raised from
≤0.16 s to 0.16–0.28 s so the pose survives long enough to read at 60 fps.

**Invariant.** **A riposte you cannot frame is a riposte you cannot see.** Any step-in, lunge or camera move
that closes to under ~2 m on a body-sized collider at 95° FOV is a presentation bug regardless of how good
the effects are. And **every element of a blast must be anchored to the thing that fired it** — an effect
drawn only on the victim has no author. Verify with a screenshot; `FeatureTests` and `DebugHarness` prove the
state machine and are blind to all of this. `FeatureTests > WandReadability` guards the shipped numbers
(CLAUDE.md rule 9 — the poses and the standoff live on the Player *prefab*, so `PrefabFactory` writes them
explicitly and editing the field initialiser alone changes nothing).

---

## `F` opens the wand altar AND drinks a flask

**Symptom / trap.** The wand pedestal was specified to open on `F`. `F` was already bound to `Heal`, so a
single press would open the menu *and* start a flask drink, with script execution order deciding whether the
charge was spent. The stock `Interact` action's gamepad binding (`buttonNorth`) collided with `Ultimate` the
same way.

**Root cause.** Two actions in the same map may share a physical control; nothing warns you, and both fire.

**Fix.** `WandPedestal.PromptActive` is true while any altar is offering its prompt; `FlaskAbility.Update`
returns early on it, so the press deterministically belongs to the altar. `Interact`'s gamepad binding was
moved to the free `dpad/down`.

**Invariant.** Before binding a key, grep the `.inputactions` for that control — the Player map already
double-books `buttonEast` (Crouch/Dash) and `leftStickPress` (Sprint/WandCycle). If a share is unavoidable,
resolve it in code with an explicit priority flag, never by hoping for an execution order.

---

## A UI alpha is composited in LINEAR space, so "subtle" is five times smaller than it looks

**Symptom.** The first main menu was a mud-brown striped backdrop. The bands behind the title were
authored at alpha `0.045` of the ember gold `#D9891A` over a near-black `#06040A` ground — a whisper on
paper. On screen they read as flat `#4A2D0A` stripes with hard edges, and the level-select rows, authored
at `0.045` white, came out as mid-grey slabs.

**Root cause.** The project renders in **linear colour space**. A UGUI `Image` colour is converted
sRGB → linear, the alpha blend happens in linear, and the result is re-encoded to sRGB for display.
`0.045 x linear(0.85) = 0.031` linear re-encodes to about `0.19` sRGB — roughly **5x** the value the
number suggests. The darker the ground, the worse the discrepancy, and this project's ground is nearly
black everywhere.

**Fix.** Menu backdrop bands are `0.0035`–`0.0045`; the level row's resting tint is `0.008` white, its
hover `0.10` of the row accent. Nested bands of falling alpha stand in for the gradient sprite this
project does not have.

**Invariant.** In this project a UI "whisper" alpha is ~`0.005`, not ~`0.05`. Any new low-alpha overlay
must be looked at in the Game view — a screenshot, not the Scene view — before it is called subtle.

---

## A `[RuntimeInitializeOnLoadMethod]` bootstrap fires once per APPLICATION, not once per scene

**Symptom.** After the game was changed to boot into `MainMenu.unity` (build index 0), a level started
from the menu had no ghost, no run recorder and no leaderboard. Opening `Level_01.unity` directly still
worked perfectly, so the feature looked fine in every existing test.

**Root cause.** `GhostRacing.Bootstrap` is `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`, which runs
**once**, after the first scene of the run loads. It correctly declines to build itself in a scene with no
`SpeedrunTimer` — and with a menu at index 0, that one shot always lands on the menu. Nothing then ever
re-checked.

**Fix.** `Bootstrap` now subscribes to `SceneManager.sceneLoaded` and calls the same `TryCreate()` on every
load. `TryCreate` was already idempotent (it bails if a `GhostRacing` exists), so the first scene is not
double-created.

**Invariant.** Any self-bootstrapping system that keys off "am I in a level?" must re-check on
`sceneLoaded`, not only at application start. Booting into a menu broke that assumption for every such
system at once.

---

## The pause menu's time handle must be released BEFORE the scene load, not by destruction

**Symptom.** (Caught while adding *Return to menu*.) Loading a scene straight from an open pause menu can
leave the next scene running at `Time.timeScale = 0`.

**Root cause.** `PauseMenu` holds one `TimeScaleController.Request(0f)` handle while it is open. Destroying
the old scene destroys `TimeScaleController`, whose `OnDestroy` does restore `Time.timeScale = 1` — but
that is destruction *order* doing the work, which is not something to rely on for a thing the player can
feel, and the new scene's own controller may already have been created.

**Fix.** `PauseMenu.ReturnToMainMenu()` calls `PrepareForSceneChange()` first: `Close()` (which releases
the handle on every exit path) plus the cursor unlock, and only then `SceneManager.LoadScene`. The split
exists so `FeatureTests > MainMenu` can assert the handle is released without leaving the level scene.

**Invariant.** Release every time handle before a scene load, explicitly. Never let teardown order be the
mechanism.

---

## A menu that can open itself freezes every scripted run

**Symptom.** A trap found while building the wand pedestal: any UI that opens without input and takes a
`TimeScaleController.Request(0f)` leaves `FeatureTests` and `DebugHarness` waiting on a frozen clock, with no
error and no visible cause. The pedestal's first version auto-opened on trigger enter at the spawn point and
did exactly this.

**Root cause.** Automated drivers assume a run begins in `GameState.Playing` with time running.

**Fix.** The shipped pedestal no longer opens on proximity at all — it needs look-at plus `F`. As
belt-and-braces, `WandSelectMenu.ForceClose()` (idempotent, releases the handle) is still called at the top of
`FeatureTests.RunSuite`, in `FeatureTests.ResetPlayerState` and in `DebugHarness.Run`, so no driver can inherit
an open menu.

**Invariant.** Any UI that takes a 0-scale time handle must expose a static force-close, and every automated
entry point must call it before scripting anything.

---

## A changed code default never reaches an existing ScriptableObject

**Symptom.** The anti-mashing parry retune was written, compiled and documented — and the game still ran
the old windows. `PlayerStats.asset` reported perfect `0.15` / late `0.20` / whiff `0.25` while
`PlayerStatsData.cs` clearly said `0.13` / `0.12` / `0.5`. Nothing errored. Caught by `FeatureTests`.

**Root cause.** `DataFactory.GetOrCreate<T>` only **creates** singleton assets; if the `.asset` exists it
is returned untouched. A field initialiser only runs when an instance is constructed, never when one is
deserialized — so the asset kept its serialized values forever.

**Fix.** `DataFactory` now rewrites the documented **design-contract** fields on `PlayerStats` (parry
windows, posture constants) on every run, while leaving all other fields alone so Inspector tuning
survives. `FeatureTests` asserts the shipped values, so this cannot silently drift again.

**Invariant.** **A code default is not a shipped value.** Changing a field initialiser on a type that
already has an asset changes nothing. Either rewrite the field in `DataFactory` or delete the asset. If a
value is a design contract, assert it in `FeatureTests`.

---

## Writing a transform channel that another component owns is a no-op

**Symptom.** `FirstPersonMotor.Teleport(pos, yaw)` moved the player but left them facing the old
direction.

**Root cause.** `PlayerLook` owns yaw and rewrites `transform.rotation` from its own internal value every
`Update`, so the rotation set inside `Teleport` was overwritten one frame later. It appeared to work only
because the caller that mattered — `LevelManager.Respawn` — also happened to call `PlayerLook.SetYaw`.

**Fix.** `Teleport` pushes the yaw into `PlayerLook` itself, so every caller gets correct facing.

**Invariant.** If a component drives a transform channel every frame, that component is the only valid
place to set it. Push the value through the owner rather than writing the transform directly.

---

## Exact float boundaries in parry timing resolved against the player

**Symptom.** A press landing exactly on the block-window boundary resolved as a full **Hit** instead of a
Block. `ParryMath_EdgeOfLate` failed while the logic read as obviously correct.

**Root cause.** The caller's `perfect + late` was **constant-folded at compile time**; `ParryMath.Evaluate`
computed the same sum at runtime. The two results differed in the last bit, so `elapsed <= perfect + late`
was false for a value that was supposed to be exactly on the boundary.

**Fix.** A `1e-4f` epsilon on both window comparisons in `ParryMath.Evaluate`, deliberately resolving
boundaries in the **player's** favour.

**Invariant.** Never test accumulated float timings for exact inclusion. Add an epsilon, and when a
player-facing timing sits on a boundary, round in the player's favour.

---

## Aggro-locked enemies woke up when parried or damaged

**Symptom.** A test dummy created with `aggroLocked = true` attacked anyway, contaminating the parry
measurements around it.

**Root cause.** `OnParried` (and damage) routes an enemy into `State.Recover`. The transition *out* of
`Recover` went straight to `Chase` **without re-checking `aggroLocked`** — the flag was only consulted on
the `Idle → Chase` path. Any locked enemy that was touched woke up permanently.

This reached live gameplay: the boss is aggro-locked until `BossArenaTrigger` fires, and the Stormcall
discharge calls `OnParried` on the boss directly. A boss could therefore start fighting with no boss bar,
no music cue and no gate.

**Fix.** Leaving `Recover` now honours `aggroLocked`, falling back to `Idle` instead of `Chase`.

**Invariant.** Every path *out* of a transient state must re-check the gating flags that put the entity to
sleep — not just the path *in*. Gate on exit, not only on entry.

---

## Two test-authoring traps worth not repeating

**Cloning an already-collected pickup.** The physics-pickup test instantiated the first `ItemPickup` it
found in the scene. A pickup the player has already walked over has its collider and renderers
**disabled**, and `Instantiate` copies that state — so the clone could never fire a trigger and the test
failed for a reason unrelated to the feature. It surfaced only once a pickup was placed at the spawn
point, where the player collects it immediately. The test now requires a template with an enabled
collider and force-enables the clone.

**Sub-cases contaminating each other once a cost became real.** As soon as blocking genuinely cost
posture, accumulated posture from earlier sub-cases broke the player mid-section and every later
measurement came back exactly **×1.6** — the `staggeredDamageMultiplier`. The numbers looked like a
damage bug; the cause was missing isolation.

**Invariant.** Reset shared state between *sub-cases*, not just between tests. When a measured value is
off by a clean multiplier, suspect a state flag before suspecting the arithmetic. Never clone a
scene object whose enabled-state is part of its behaviour without normalising it first.

---

## Unity halts play mode when the Editor window loses focus

**Symptom.** Play mode "runs" but `Time.frameCount` stays at 1 and `Time.time` at 0. Scripted play-mode
verification hangs forever; the editor state resource reports `phase: playmode_transition` with a status
that goes minutes stale. Looks exactly like a deadlock or a wedged editor.

**Root cause.** Unity does not tick the player loop while the Editor is unfocused unless "Run In
Background" is on. Every automated play-mode test driven over MCP therefore stalls, because driving the
editor remotely means the window is never focused.

**Fix.** `Application.runInBackground = true` in `GameManager.Awake` (runtime, authoritative) and
`PlayerSettings.runInBackground = true` in `ProjectSetup.Run()` (project setting, persisted).

**Invariant.** Do not remove either line. If play mode appears frozen at frame 1, check focus and this
setting before suspecting a deadlock.

---

## `ref` on an `in` parameter → whole project stuck in Safe Mode

**Symptom.** "The project won't open", "the scene is blank". Unity opens to an empty scene with no
level, and the MCP bridge never connects.

**Root cause.** `Health.TakeDamage(in DamageInfo d)` was called as `TakeDamage(ref d)`. Passing `ref` to
an `in` parameter is a **C# 12** feature; Unity 6 compiles gameplay code as **C# 9**, so this is
`error CS9194`. Any compile error in `Assembly-CSharp` puts the editor into **Safe Mode**, and Safe Mode
deliberately does not load scenes — which presents to a user as catastrophic data loss.

**Fix.** Call it by value: `TakeDamage(d)`. An `in` parameter accepts a value argument.

**Invariant.** Target **C# 9**. No `ref`-to-`in`, no file-scoped namespaces, no `required` members, no
list patterns. A blank scene plus a project that "won't open" almost always means one compile error —
read `Editor.log` for `error CS` rather than assuming the project is damaged:
```bash
grep -oE "Assets[\\/][^ ]+\.cs\([0-9]+,[0-9]+\): error CS[0-9]+: .*" \
  "$LOCALAPPDATA/Unity/Editor/Editor.log" | sort -u | tail -20
```

---

## The campaign level got built inside the sandbox scene

**Symptom.** `Sandbox.unity` opened with the four-tile campaign course sitting inside the 60 x 60 arena:
a `Level` root alongside `Sandbox`, sixteen spawners instead of six (`Spawn_T1_GruntA` at z = 48 next to
`Spawn_Grunt` at z = -18), and two Players' worth of geometry. It had been **saved** that way.

**Root cause.** `LevelGreyboxBuilder.Build()` builds into whatever scene is **active**. It never opens
`Level_01.unity` itself - `SandboxBuilder` does open its own scene, but the level builder trusts the
editor's current scene. Run `VibeGame1/6. Build Level` while the sandbox happens to be the open scene
and the course lands in the sandbox, and the builder's own `SaveScene` persists it. Nothing errors:
both builders report success, because both did exactly what they were asked.

**Fix.** `SandboxBuilder.ClearGeneratedRoots` now destroys a stray `Level` root as well as its own, so a
single `VibeGame1/7. Build Sandbox Scene` repairs the scene. Exact-name match only - `Level_Manual` and
`Sandbox_Manual` are still never touched.

**Invariant.** A builder that writes into "the active scene" is a builder that will eventually write
into the wrong one. Check the open scene before running `6. Build Level`, and prefer leaving
`Level_01.unity` open when you are done in the sandbox. If a generated root that belongs to another
builder turns up in your scene, delete it in the builder rather than by hand - a hand deletion that is
never saved is silently undone the next time play mode exits.

---

## The level builder wiped the scene and could not save it

**Symptom.** `SampleScene.unity` (now `Level_01.unity`) on disk ended up with **zero** GameObjects. The level was genuinely
lost once.

**Root cause.** `LevelGreyboxBuilder.Build()` destroys the `Level`, `Player`, `Managers` and `HUD` roots
*first*, then rebuilds. In play mode `EditorSceneManager.MarkSceneDirty` / `SaveOpenScenes` throw
`InvalidOperationException: This cannot be used during play mode` — after the destruction, before the
save. Exiting play mode then discarded the rebuilt scene, leaving nothing.

**Fix.** A play-mode guard as the **first statement** of `Build()` in both `LevelGreyboxBuilder.cs` and
`SandboxBuilder.cs` (search `PLAY MODE GUARD`), logging an error and returning.

**Invariant.** Any editor routine that destroys before it rebuilds must refuse to run in play mode.
Nothing in the scene is hand-authored — the level is fully regenerable — so recovery is
`VibeGame1/0. Rebuild Everything`, not a restore from backup. Hand-placed objects belong under a
`Level_Manual` / `Sandbox_Manual` root, which the builders never touch.

---

## `Physics.IgnoreLayerCollision` also suppresses trigger callbacks

**Symptom.** Item pickups, checkpoints and bloodstains never fired. Silent — no error, no warning.

**Root cause.** `ProjectSetup` set `Physics.IgnoreLayerCollision(Interactable, Player, true)` on the
reasoning that Interactable is trigger-only and never needs physical contact. But the ignore matrix
gates **`OnTriggerEnter` as well as collision**, so it disabled every interaction in the game.

Worse, the bug was invisible to the test suite, because tests invoked `OnTriggerEnter` directly via
`SendMessage` instead of walking into the trigger.

**Fix.** `Physics.IgnoreLayerCollision(Layers.Interactable, Layers.Player, false)`. Interactable colliders
are all `isTrigger`, so leaving the pair enabled blocks no movement and costs nothing.

**Invariant.** Never ignore a layer pair that needs triggers. Pickup/checkpoint/bloodstain tests **must**
move the player into the collider and let physics fire; a direct `SendMessage("OnTriggerEnter", …)` is
not a test, it is a way to hide this exact bug. `FeatureTests` asserts
`!Physics.GetIgnoreLayerCollision(Interactable, Player)` explicitly.

---

## Every HUD bar rendered permanently full

**Symptom.** Health, parry juice, player posture, boss health and boss posture bars never moved. The
boss looked invulnerable and unkillable — it was taking damage the whole time.

**Root cause.** UGUI `Image` **ignores `type = Filled` and `fillAmount` entirely when `sprite == null`**.
`Image.OnPopulateMesh` falls through to a plain quad. The bars were built with no sprite, so every
`fillAmount` write was silently discarded.

**Fix.** `BarView` drives the fill's `RectTransform` anchors instead
(`anchorMin = (0,0)`, `anchorMax = (ratio,1)`, zeroed offsets), which is sprite-independent and
pivot-independent. `HudBuilder` also assigns the builtin `UI/Skin/UISprite.psd` as belt and braces.
The fill `Image` is disabled below ratio 0.001 so an empty bar cannot render as a sliver.

**Invariant.** Never rely on `Image.fillAmount` in this project. `FeatureTests` asserts on
`BarView.fill.rectTransform.anchorMax.x` as the regression guard. A "the enemy is invulnerable" report
should make you check the *bar* before the damage code.

---

## An `AudioSource` created and played in the same `Awake` never starts

**Symptom.** The ambient music source reported `isPlaying == false` forever, despite a valid loaded clip,
volume > 0, an `AudioListener` present, and `Play()` having been called.

**Root cause.** Calling `Play()` on an `AudioSource` that was `AddComponent`'d earlier in the *same*
`Awake` during scene load silently fails.

**Fix.** Create sources in `Awake`, start them in `Start()`. `AudioManager` also runs a watchdog in
`Update` that restarts the music if it stops unexpectedly (device change, focus loss).

**Invariant.** Never `AddComponent<AudioSource>()` and `Play()` in the same `Awake`.

---

## Runtime `Shader.Find` gets stripped from player builds

**Symptom.** Works in the editor, silently invisible in a build. Affects `LightningEffect`, which builds
its additive material at runtime from `Shader.Find("Universal Render Pipeline/Unlit")`.

**Root cause.** Unity strips shaders no material asset references. No `.mat` in the project used
URP/Unlit, so it was not in the build.

**Fix.** `ProjectSetup.EnsureAlwaysIncludedShader("Universal Render Pipeline/Unlit")` adds it to
Graphics Settings → Always Included Shaders.

**Invariant.** Any shader obtained via `Shader.Find` at runtime must be in Always Included Shaders or
referenced by a material asset.

---

## The parry cue lead must exceed human reaction time

**Symptom.** A "press now" cue that fires too close to the impact makes *correct* reactions score as
late blocks or clean hits — the mechanic feels broken while the code is provably correct.

**Root cause.** The player reacts ~0.20 s after the cue. The parry is Perfect only when the press lands
within `perfectWindow` **before** impact. With `cueLead = 0.18 s`, a 0.20 s reaction presses at
`impact + 0.02` — after the hit, every time.

**Fix.** `cueLead = 0.28 s` ≈ reaction (0.20) + half the perfect window (0.065). Reactions of
0.15–0.28 s land in the Perfect band; faster presses read as an early Block, slower ones miss.
Serialized on `EnemyController` so it is tunable without a recompile.

**Invariant.** `cueLead ≈ 0.20 + perfectWindow / 2`. If you retune `parryPerfectWindow`, retune
`cueLead` with it. Also: no enemy attack windup below **0.45 s**, or the cue would need to fire before
the windup starts. See the feel contracts in [ARCHITECTURE.md](ARCHITECTURE.md).

---

## PlayMode NUnit tests are unavailable in this project

**Symptom.** You will reach for `run_tests` with `mode: PlayMode` and find no tests.

**Root cause.** There are **no assembly definitions**. Gameplay code lives in `Assembly-CSharp`, and an
asmdef-based test assembly cannot reference `Assembly-CSharp`. EditMode tests work only because
`Assembly-CSharp-Editor` already references nunit.

**Fix / current state.** Pure logic is covered by EditMode tests in `Assets/Editor/Tests/`
(`ParryMath`, `PostureMath`, `UpgradeMath`). Everything behavioural is covered by `FeatureTests`, a
play-mode coroutine suite driven over MCP — see [TOOLING.md](TOOLING.md).

**Invariant.** Do not add asmdefs casually to "enable PlayMode tests" — it is a project-wide refactor
that also forces an asmdef on `Assets/Editor`. If it is ever done, do it deliberately and on its own.

---

## Hitstop must never freeze the player

**Symptom.** Every sword connection stuttered the run in a game built around flow.

**Root cause.** Hitstop dropped global `Time.timeScale` to 0.02, and `FirstPersonMotor` read
`Time.deltaTime`.

**Fix.** `TimeScaleController` requests carry an `affectsPlayer` flag. Hitstop and the ultimate's
slow-mo pass `false`. The motor reads `TimeScaleController.PlayerDelta`, which ignores those requests
but still honours a pause.

**Invariant.** **Never read `Time.deltaTime` for player movement.** Use `TimeScaleController.PlayerDelta`.

---

## A green test can be measuring the game putting itself back the way it was

**Symptom.** `Progression_SoulsLostOnDeath` failed with `souls=760` — the exact total the player was
carrying before they died — and `Progression_BloodstainRecovery` skipped alongside it with "no bloodstain
spawned". It read like a hard progression regression: souls neither dropped nor recoverable.

**Root cause.** The shipping code was correct the whole time. Verified live in play mode: `TakeAll()`
emptied the wallet to 0 and `LevelManager.SpawnBloodstain` dropped a stain carrying all 300+ souls —
both run *synchronously* from `Health.OnDied`. The test then waited for the respawn before sampling the
wallet. No checkpoint has been activated by that point in the suite, so `Respawn()` returns the player to
the start spawn — 1.15 m from the stain, which has a 1 m trigger radius. The player landed on their own
bloodstain, walked into it and got every soul back before the assertion ran. The stain was consumed, so
the recovery check found nothing to walk into and skipped. Dying on your own checkpoint and getting your
souls straight back is correct, intended behaviour.

A second, unrelated staging failure surfaced while fixing it: the rewritten recovery walk dropped its
stain 2 m along the player's forward axis and the player never moved a centimetre. The **wand pedestal
plinth**, newly built at the start spawn, was standing exactly there. A raycast sweep confirmed 0°/45°/315°
were all blocked by `WandPedestal_Start_Plinth`.

**Fix.** Assert the drop where it actually happens — immediately after `TakeDamage`, before the respawn
(`Progression_SoulsLostOnDeath` + the new `Progression_BloodstainCarriesSouls`). Stage the recovery
deliberately afterwards: drop a fresh stain through the public `LevelManager.SpawnBloodstain`, pick the
walk direction by raycasting eight compass points for a clear path *and* ground, and shove the player
along it at 14 m/s (ground friction of 14/s stops an impulse in roughly `v/friction` metres, so the old
6 m/s shove only travelled ~0.4 m and could never cross 2 m).

**Invariant.** **Sample a state change at the moment it happens, not after the game has had a chance to
undo it.** When a drop, a spend or a loss is applied synchronously, assert it synchronously; a value
re-read after a respawn, a reload or a trigger volume is measuring the aftermath, not the event. And
**never assume the level geometry around a test fixture is empty** — probe for a clear path with a
raycast, because level builders keep putting new furniture at the spawn point.

---

## One material was both navigational trim and a combat tell

**Symptom.** Fixing the level's tile colours broke the unblockable telegraph. Tile 3's crimson trim was
rendering *orange* and was indistinguishable from the ember boss court, so `M_NeonRed` was dropped from
`#FF2A10 x 2.2` to `#FF1010 x 1.10`. That is the right value for a trim — and it silently took the
enemy "Alert" cube down with it, because the alert cube used the same material. The player gets roughly
**0.45 s** to read that cube; it went from an HDR block that blew out the tonemapper to something barely
above the 1.05 bloom threshold. Nothing failed, nothing logged, and no test noticed.

**Root cause.** `M_NeonRed` was serving two jobs with **opposite requirements**. A navigational trim must
stay *under* the ACES desaturation ceiling or it loses the hue that carries the tile's identity; a combat
tell must be *well over* the bloom threshold or it cannot be read in reaction time. One number cannot
satisfy both, so tuning for either silently detunes the other.

**Fix.** Split the key. `M_AlertTell` (`#FF0A28 x 3.00`, peak channel 3.00, ~2.9x the shipped 1.05 bloom
threshold) is a dedicated combat-tell material; `PrefabFactory` and `MiniBossFactory` point the Alert cube
at it, and `M_NeonRed` keeps the trim value untouched. The tell's hue carries a **blue lift** rather than
being a pure red: ACES pushes a saturated pure red toward orange as it brightens — the exact trap that hit
the trim — and orange is the boss court's colour, so the tell desaturates toward hot pink-white instead,
a hue nothing else in the palette occupies. `FeatureTests.TestTellReadability` now asserts the alert
marker's material is not a trim key, that its peak emission is at least 2x the *live* bloom threshold read
off the volume profile, and that every navigational trim key stays at or under the 1.25 desaturation
ceiling.

**Invariant.** **A material that serves both navigation and a combat tell will always be tuned in two
opposite directions.** Anything the player must read *fast* gets its own material key, separate from
anything the player reads *slowly* for orientation — and the split is enforced by a test, because a
detuned tell is invisible in the diff, invisible in the console and only shows up as the game feeling
unfair.

---

## The deathblow was already a button press and still felt automatic

**Symptom.** *"Instead of riposting automatically it needs to be the player hitting again to riposte,
like in Sekiro when you see the red dot on an enemy whose stance you broke."*

**Root cause.** The press was never automatic. `WeaponController.TryAttack` has always called
`ExecuteInteractor.TryExecute()` before starting a swing, and `TryExecute` refuses unless a posture-broken
enemy is inside 3.5 m and a 40 degrees cone — which is exactly Sekiro's binding. Nothing in the parry path
triggered it. What was missing was every single thing that tells a player a press was *theirs*:

1. **No marker on the enemy.** A posture break showed a slumped pose, a world posture bar and — off at the
   edge of the frame — the word `EXECUTE`. During an exchange the player's eyes are locked on the enemy's
   cue flash at the centre of the screen, so the only signal was in the one place they were not looking.
   This is the same lesson as the Pyre bar (*"charge that reads only on the HUD"*), one system over.
2. **The press was pixel- and audio-identical to a swing** for its first 0.18 s: the same `Sfx.Swing`
   whoosh, no visual at all, and the melee commit pose. By the time the cinematic step-in made it obvious,
   the player had already concluded the game had decided for them.
3. **The prompt named the verb, not the input.** `EXECUTE` does not say *which button*, so nothing
   connected the word on screen to the attack the player was about to throw anyway.

**Fix.** `EnemyVisuals.deathblowMarker` — an arc violet-blue glyph (`M_DeathblowMark`) that spins and
breathes over the broken enemy, raised by `Slump(true)` on the break and dropped by `HandleStaggerEnded`,
`BeginExecuted` and `Die`, so the marker's lifetime is exactly the window's. `ExecuteInteractor.CommitCue`
fires on the press frame: the glyph shatters into violet sparks, a beam runs from
`WeaponViewmodel.TipWorldPosition` to it, and the audio is `Sfx.Execute` at 0.62 pitch instead of
`Sfx.Swing`. The prompt now reads `DEATHBLOW [ATTACK]`. No timing, cone, range or `stabStandoff` changed.

**Invariant.** **A deliberate input that produces no distinct feedback is indistinguishable from an
automatic one.** If a button changes meaning in context, the world must say so *before* the press (a
marker on the thing, not a word on the HUD) and the press itself must feel different *on the frame it
lands*. `FeatureTests > Deathblow` now asserts both directions: an unmarked press swings, a marked press
executes, the break raises the glyph and the window closing drops it.

---

## A "violet" marker rendered magenta, and read as the alert tell it had to differ from

**Symptom.** `M_DeathblowMark` was authored as `#7A2BFF * 2.60` and reasoned about as violet. On screen,
the deathblow glyph and the unblockable alert cube were the same pink — the two markers that hang over the
same head and mean opposite things.

**Root cause.** `#7A2BFF * 2.60` is `(1.24, 0.44, 2.60)`. **Both** red and blue are over 1.0, so both clip
to full and the hue collapses to magenta. The mistake was reasoning about the hex string, which is a
direction, rather than about the scaled colour, which is what the tonemapper sees. This is the third
appearance of the same family of trap in this project (the trim that rendered orange, the tell that had to
carry a blue lift) and the first one where the *dominance ratio* rather than the peak was the problem.

**Fix.** Bring the non-dominant channels down until the hue survives. `#3A18FF * 2.60` =
`(0.59, 0.24, 2.60)` fixed the magenta but rendered light ORCHID, still close enough to the tell's pink to
hesitate over in a side-by-side capture. `#2A0BFF * 2.60` = `(0.43, 0.11, 2.60)` is where it becomes an
unmistakable blue-violet. Peak (and therefore bloom) is unchanged at 2.60 throughout — this was never
about brightness. Verified by rendering all three hues in one frame; arithmetic got it wrong twice.

**Invariant.** **In an HDR emission, only the dominant channel may exceed 1.0.** Everything above 1.0 pins
at full, so a second clipped channel silently rewrites the hue — the brighter you push a colour, the fewer
channels are allowed to carry it. And two markers that share a location on screen must differ on **hue,
silhouette and motion**, not on hue alone, because hue is the axis the tonemapper is most likely to take
away from you.

---

## The posture break collapsed the enemy INTO the camera, because +X pitch is "toward the player"

**Symptom.** *"the enemy fall towards the player and clips into them."* At deathblow range the frame
filled edge to edge with enemy geometry and the riposte — the wand, the stab, the blast — happened
somewhere behind a wall of body.

**Root cause.** `EnemyVisuals.Slump(true)` played `TiltCo(28f, ...)`: a **+28 degree pitch about local
X**, which over a 2 m body pivoting at the feet walks the chest about 0.9 m along the body's own forward
axis. That axis points at the player, always — the enemy faces them during a fight and `BeginExecuted`
explicitly turns it to face them again on the press frame. The stagger arm made it worse one bone
further out (`+38` about X swung a 1.35 m weapon down the camera's throat), and `DieCo` finished the job
with `Euler(85, 0, 20)` — a corpse pitching *forward* through the lens on the exact frame the player is
meant to be watching the blast. Scale multiplies all of it: the boss is 2.2x.

**Fix.** The pose **buckles** instead: `StaggerEuler (-13, 0, 9)` leans BACK and rolls, `StaggerSag
(0, -0.20, -0.14)` drops and shifts the weight onto the back foot, `StaggerArm (-26, 0, 34)` throws the
guard back and OUT rather than forward. `DieCo` falls backward (`-78`) and slides away. Every component
is signed away from the camera. The read is unchanged — Sekiro's broken posture is a heavy hunch that
holds roughly in place, and holding in place is exactly what keeps the victim framed; a fall to the
ground would hide the body behind its own arena floor at this camera height, and a fall *backwards*
would move the deathblow target away mid-commit.

**Invariant.** **In a first-person game, a pose on an enemy that faces you has a sign, and the sign is
the whole design.** Anything positive about local X is *toward the lens*. `FeatureTests >
Deathblow_StaggerPoseClearsNearPlane / StaysFramed` measure the nearest enemy renderer bound to the eye
at the shipped standoff, at 1x and at 2.2x, and they are the reason a pose regression cannot ship
quietly — measured 1.57 m clear at grunt scale and 2.06 m at boss scale.

**And the standoff you must test at is not the one you think.** `stabStandoff` is 2.2 m x enemy scale,
so 4.84 m on the boss — but `StepInCo` only ever CLOSES the gap and the press has to be inside
`ExecuteInteractor.range` (3.5 m). On any body over ~1.6x scale the real riposte distance is therefore
the RANGE, not the standoff, and the standoff never applies. Testing at 4.84 m would have tested the
easy case.

---

## The deathblow mark: a blob over the head, then a blob on the chest, and the size was ANGULAR all along

**Symptom.** Three verdicts on the same object. *"the repost symbol needs appear on the body not as an
icon above their head"* — then, once it was on the body, *"it needs to be a flat glowing spot and not a
3d shape right now its a purple blob that gets in the way of the camera."* Flattening it to a 0.22 m
quad did not fix that: it still photographed as a violet card filling a quarter of the frame.

**Root cause.** Two separate mistakes wearing one complaint.

The first is placement. At local y 2.30 the glyph rode above the body capsule, which on a 2.2x boss is
**five metres in the air** — out of the frame entirely at deathblow range, the one moment it has to be
readable. Moving it to the sternum fixed that.

The second is the one that survived flattening, and it is not obvious: **the mark cannot sit on the
body.** A marker at a body's centre of mass renders inside the mesh (the logged lock-on dot trap), so
the spot has to stand `surfaceOffset` metres *off* the chest toward the viewer. That means it is always
**nearer to the camera than the enemy it marks**, and the closer the player gets, the bigger that
relative difference is. A fixed-size quad therefore grows *faster than the enemy does* as the player
closes. Measured at stabbing range: the grunt's 0.9 m body spanned about a fifth of the frame while a
0.22 m mark standing 0.80 m in front of it spanned a quarter. The blob was not authored too big; it was
authored at a fixed size, which is a different bug with the same photograph.

**Fix.** Constant **angular** size, exactly as `LockOnMarker` already does one system over:
`angularSize` 0.115 world units per metre of distance (~5% of the frame at 95 degrees FOV), clamped
`[0.10, 0.42]`, with the quad authored at 1.0 and `DeathblowMarker` driving the root's scale each frame.
`surfaceOffset` also came down from 0.80 to 0.58 (capsule radius 0.45 plus a small margin; the imported
models measure their own depth plus 0.13) and is capped at 35% of the distance to the eye, so it cannot
end up in the player's face point-blank. And the whole question of it fouling the *stab* is moot for a
different reason worth writing down: `BeginExecuted` calls `SetDeathblowReady(false)` on the press
frame, so the spot is gone before the melee commit and long before the wand reaches the body.

**Invariant.** **A world-space marker that stands off its target has to be sized by angle, not by
metres.** The offset that makes it visible is the same offset that makes it balloon, and the two are
inseparable — you cannot fix the second by shrinking, only by decoupling size from distance. And a
marker that exists to say *press now* should die on the press: after that the commit feedback owns those
pixels, and anything still drawn there is in the way of the shot.

---

## The blast was drawn inside the body it was blowing up

**Symptom.** *"you should be able to see the player stabbing the enemy with the wand and the explosion."*
The wand thrust played, the damage landed, and the explosion was a dim smear.

**Root cause.** `WandController.FireRiposte` drew every victim-side element at
`origin + Vector3.up * (0.95 * scale)` — the enemy's **centre of mass**, which is 0.45 m inside a capsule.
The flare, the sparks and the far end of the beam were all rendering *inside the mesh*. This is the third
appearance of the identical trap in this project: the lock-on dot spent a whole pass invisible at a
chest point with every assertion passing. And `ExecuteInteractor.CommitCue` had the same class of bug from
the other direction — a hardcoded `const float MarkHeight = 2.30f` that had to be kept in step with the
factory by hand.

**Fix.** One source of truth. `EnemyController.DeathblowPoint(eye)` returns the sternum stepped onto the
**surface** facing the viewer, and the marker, the commit shatter and the blast all read it — so the
three land on the same pixels instead of near each other. `SlashFx.Ring` at that point, oriented square
to the eye, gives the blast a shockwave without touching `ScreenFlash` (still 0.20) or the chromatic
pulse (still 0.4). The thrust pose was also re-aimed to finish near screen centre
(`thrustPosition (-0.07, -0.09, 0.80)`, `thrustEuler (66, -6, 4)`, written in `PrefabFactory` — rule 9):
a viewmodel wand can never physically reach 2.2 m, so the stab is sold by the tip visually *landing on*
the chest, which only happens if it travels inward rather than finishing off to the left where it rests.

**Invariant.** **Anything drawn at a body's centre of mass is inside it.** Compute the point once, on the
surface, facing the eye, and let every consumer ask for it — a constant height copied into three files
will drift, and it will drift silently because nothing about it can fail an assertion.

---

## A play session that has survived a domain reload is not a play session

**Symptom.** Hours of it. An enemy would not stagger; `Posture.IsBroken` went true and
`EnemyController.Current` stayed `Idle`; `ExecuteInteractor.Target` was permanently null so every
scripted deathblow came out as an ordinary swing; `GameManager.I` was null while a `GameManager`
component sat enabled in the scene. Each symptom looked like a different bug in a different system.

**Root cause.** Every one of them was the same thing: a **script recompile while play mode is running**.
Unity reloads the domain, which wipes every `static` — singletons, `GameEvents` subscriptions, cached
`I` references — *without* re-running `Awake`/`Start` on the objects that set them. So `GameManager.I`
is null forever, `Posture.OnBroken` has lost `EnemyController`'s subscription (leaving only
`EnemyPostureBar`, which resubscribes in `OnEnable`), and `GameManager.IsPlaying` is false, which is what
was silently making `ExecuteInteractor.Update` find nothing. This is the play-mode twin of the logged
"Unity keeps serving the last good assembly while the tree is red" trap, and it is worse, because
nothing is red and nothing is logged.

**Fix.** After **any** script edit, exit and re-enter play mode before trusting a single reading. The
verification checklist gets one more line: `GameManager.I != null` is the cheap canary for "these
statics are real", and every scripted capture and harness run should assert it before doing anything
else.

**Invariant.** **A static that survived a domain reload is a lie.** If a play-mode measurement disagrees
with the code in front of you, check whether the session has been recompiled under it before debugging
anything else — and never diagnose a gameplay system from a session that has.

---

## An editor-tick capture cannot photograph a beat that lasts one second

**Symptom.** Frame-by-frame captures of the riposte kept landing on the commit frame and then on the
corpse, with the stab and the explosion — the entire thing under review — missing.

**Root cause.** `ViewmodelCapture` shoots from `EditorApplication.update`, which ticks at roughly 8 Hz
with the editor in the background. The riposte is 0.6-1.0 s. That is about six samples for the whole
beat, and the two that mattered fell in the gaps.

**Fix.** `Assets/Scripts/Debug/FrameFilm.cs` — a runtime component that captures in `LateUpdate`, so it
gets **every rendered frame**, and buffers the textures in memory rather than encoding PNGs as it goes.
The buffering is the load-bearing part: every beat in the riposte is on **realtime** waits (rule 1), so
slowing the frame rate down does not slow the riposte down with it, it just means fewer frames across
the same second — a per-frame `EncodeToPNG` costs enough to halve the sample count it exists to raise.

**Invariant.** **Match the sampling rate to the thing being sampled, and check it before trusting the
film.** Also worth keeping: film from a body the level already placed rather than a blind spawn (a
NavMesh sample in a platformer lands on whatever ledge is nearest — one whole run photographed an empty
platform), and stage a **fresh** enemy rather than one the level has been fighting, because an enemy
caught inside a committed combo returns to `Windup` on the next frame and cannot be reliably staggered.

---

## Smaller traps worth knowing

| Trap | Detail |
|---|---|
| `Sfx` enum order | Enum member names are folder names under `Resources/Audio/Sfx/`. **Append only, never reorder or rename** — renaming silently drops back to the synthesized fallback. |
| `DataFactory` re-run resets tuning | `VibeGame1/3. Create Data` rewrites weapon/enemy/attack assets to the values coded in `DataFactory.cs`. Inspector tweaks you want to keep must be written back into that file. |
| Torch lights hijacked as the sun | Scene builders that look up "the directional light" must filter on `LightType.Directional`; torches are point lights and get picked up otherwise. Clear generated roots *before* resolving environment objects. |
| Deprecated find overloads | Use `FindObjectsByType<T>()` / `FindAnyObjectByType<T>()`. The `FindObjectsSortMode` overloads and `FindFirstObjectByType` are obsolete in Unity 6 and produce warnings. |
| Volume overrides need persisting | `VolumeProfile.Add<T>()` components must also be `AssetDatabase.AddObjectToAsset`'d or they do not survive a reload. |
| Scene file may serialize binary | `SampleScene.unity` (now `Level_01.unity`) has been observed written as binary despite `serializationMode = ForceText`. It loads correctly; it just is not diffable. Verify content with `strings`, or by reopening and counting roots — not with `grep`. |
| MCP `execute_code` is C# 6 | The dynamic-code compiler is stricter/older than the project's C# 9. Avoid local functions, `$"{x:F0}"` inside lambdas, and interpolation edge cases; keep snippets plain. |
| Naive brace-balance checks lie | A regex `{`/`}` counter reports imbalance on *every* file here (interpolated strings, chars). Do not use it as a compile proxy — it produced 78 false positives once. |
| Do not `SetActive(false)` the viewmodel | `WeaponController.Awake` caches `GetComponentInChildren<WeaponViewmodel>()` — **active-only**. Hiding the viewmodel root for a screenshot and then reloading the scene leaves that cache null, and every `Equip()` silently stops swapping the weapon model. Hide `Renderer.enabled` instead. |
| Enemy standoff distance | `agent.stoppingDistance = attackRange * 0.7` parks the 2.2×-scale boss ~2.1 m from the camera, too close to read in first person. Known, not yet changed. |
