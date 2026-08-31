# Engineering log

Institutional memory for `vibegame1`. Every non-obvious problem that cost real time, and the invariant
that stops it recurring. **Read this before debugging anything weird** — there is a good chance it is
already in here.

Newest first. When you solve something non-obvious, add an entry at the top in the same shape:
**Symptom → Root cause → Fix → Invariant**.

Related: [ARCHITECTURE.md](ARCHITECTURE.md) · [TOOLING.md](TOOLING.md) · [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md) · [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md)

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
| Enemy standoff distance | `agent.stoppingDistance = attackRange * 0.7` parks the 2.2×-scale boss ~2.1 m from the camera, too close to read in first person. Known, not yet changed. |
