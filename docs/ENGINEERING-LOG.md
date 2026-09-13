# Engineering log

## 2026-09-12 — A generated viewmodel can be structurally correct and still read as the wrong object

**Symptom.** The first persistent spellbook build passed its prefab, persistence and aim-lane tests, but a
live player-eye capture still looked like two dark blocks holding one flat beige ball.

**Root cause.** The parchment relied entirely on eclipse lighting, while the “halo” was a larger opaque
sphere surrounding an opaque core. Nested opaque geometry has no visible separation; structural assertions
cannot prove material readability or silhouette.

**Fix.** Give parchment restrained self-light below bloom, add fixed ink strokes to the upper leaves, and
replace the outer sphere with eight separated spell-tinted rune bars. The book remains generated data,
camera-left and allocation-free at runtime. `SpellbookVisualTests` pins the fixed renderer/page budgets; the
player-eye screenshot owns the presentation proof.

**Invariant.** A generated prop needs both structural tests and a live camera read. Never call nested opaque
geometry a halo, and never rely on level lighting alone for a persistent near-lens information surface.

## 2026-09-12 — Sentry placement is a four-constraint solve, not a perch coordinate

**Symptom.** Moving the upper T2 blue Sentry “forward” fixed one route report but put its perch inside the
gold portal silhouette and made the L9 bolt cross the L10 terrace. Earlier positions also lost the fastest
27.5 m/s interception sample.

**Root cause.** Visibility, collision clearance, parry-facing direction and predicted projectile contact
were being evaluated one at a time. A position can pass any three and still fail the encounter.

**Fix.** Solve all four against the generated boxes and shipped projectile math. The upper perch ships at
world `(21,20,185)`, yaw `230°`, covering L9: visible from L8/L9, 5.7° off the outgoing route bearing,
clear of L10, 13.26 m outside the gold sun surface (required 11.95), with two valid 27.5 m/s contact samples.

**Invariant.** Every parkour shooter placement must simultaneously prove eye-to-muzzle visibility, an
unblocked in-band bolt, no forced look-away, and valid contacts at all audited route speeds. Solar/exterior
clearance is part of the same solve wherever a portal is nearby.

## 2026-09-12 — A traversal trigger is not permission to replace ordinary movement

**Symptom.** The water/ice sheet threw ordinary runners around and made jumps feel slippery even when the
player had not committed to a slide. The intended reward — extra speed while sliding — instead behaved
like a global surface-physics mode.

**Root cause.** `FirstPersonMotor` entered the no-friction `WaterStep` path for every grounded water touch,
added the volume's flow conveyor, and suppressed ordinary carry decay while airborne in the trigger. The
trigger is deliberately taller than the rendered sheet for reliable stay refreshes, so normal footsteps and
hops were enough to inherit all three behaviours.

**Fix.** Only `slideOnGround && inWater` may call `WaterStep` or add `WaterFlow`. A non-sliding grounded
player follows the ordinary ground law, and every airborne player follows ordinary carry and soft-cap decay.
`TouchWater` / `InWater` remain intact for trigger safety and presentation. `PivotMovementTests` pins the
slide-only wording and the water-slide floor at 20, 60 and 240 fps.

**Invariant.** Environmental contact may report context; it must not silently become a second movement
controller. Water/ice is a slide reward, not passive slipperiness. Any future surface effect that changes
velocity must be entered through an explicit motor state and must prove the ordinary run/jump paths remain
unchanged.

## 2026-09-11 — Raw batch probes can lose the Hub licensing context while the live editor is healthy

**Symptom.** A disposable `Unity.exe -batchmode -nographics -quit -createProject` probe repeatedly
reported `Connection to channel LicenseClient-tyler refused`, waited for licensing initialization, then
registered zero built-in packages. Compilation subsequently failed on `UnityEngine.AnimationModule` /
`Animator`, which made the failure look like a missing engine module rather than an authentication-path
failure.

**Root cause.** The raw process was launched directly and did not carry the Hub bootstrap used by a
Hub-authenticated editor (`-useHub`, Hub/licensing IPC channels and Hub-issued session credentials). Updating
Hub aligned the installed Licensing Client version and refreshed entitlements, but did not make this raw
launch path reliable. A later guarded probe through a Hub-authenticated live editor executed Unity normally;
the official CLI likewise reported this project's Pipeline server reachable when run with access to the
user-scoped Hub credential store and Pipeline token. A restricted automation sandbox can falsely report the
CLI unavailable because those two stores are intentionally readable only by the signed-in user.

**Invariant.** Do not diagnose `Registered 0 packages` followed by a missing built-in module as an asset or
package defect until licensing initialization is proven. Do not retry the raw `-createProject` probe, copy
licensing binaries or edit token/license files. Drive the already-open Hub-authenticated editor through MCP.
The retained `Tools/unity-cli/Invoke-VibeGame.ps1` / `unity command eval` pilot is an optional short diagnostic,
not the base workflow: its five-second Pipeline window has timed out on healthy editor work, including a
later preflight. Run it from the normal user context only when comparing transport reachability; the Pipeline
descriptor is deliberately protected by a user-only ACL.

## 2026-09-11 — Player-reachable diagnostics need one capability, not scattered build symbols

**Symptom.** Release builds compiled some debug keys out, left F10 behind a public `editor unlock` phrase,
shipped a visible Sandbox row, and exposed the fourth test weapon through ordinary slot input. Each surface
answered a different question, so “the public build has no cheats” could not be proven as one policy.

**Fix.** `DeveloperAccess` owns one process-local grant derived from a normalized console passphrase and a
committed SHA-256 digest. `InputReader` gates F1/F5-F10, slot 4 and R; the console gates timing commands;
`TestMenu` gates opening and public toggles; the main menu hides and rejects Sandbox/custom rows; and a
locked `SandboxController` disables itself. Level-editor entry, Sandbox controller/wake-switch,
wand-pedestal and scripted-diagnostic entry points also re-check the gate, so direct component calls do not
bypass the input policy. The
diagnostic code remains in release builds so a trusted tester can unlock it, while locked help and the
settings INFO card do not advertise privileged commands; locked console input and echoes are masked.

**Invariant.** The Backquote console is the only player-reachable door. Adding a diagnostic input, menu,
scene row, capture or mutation requires `DeveloperAccess.IsUnlocked`; build symbols alone are not the
authority. The grant is never persisted. A digest inside a client is deterrence, not authentication.

The sandbox pad wake switches and timing recorder enforce the capability at their own public entry points.
Hiding a menu row, disabling only the root controller, or gating only the console dispatcher is not enough:
a separately attached player-reachable component or direct capture/export call must remain inert too.

## 2026-09-11 — Unity CLI drives the existing editor; it does not license a second editor

The project pins `com.unity.pipeline` `0.7.0-exp.1` behind tag `pre-unity-cli-pilot-2026-09-11` and keeps
the third-party MCP bridge as fallback. `Tools/unity-cli/Invoke-VibeGame.ps1` uses `unity command eval`
against the explicit project path. `unity test`, `unity build` and `unity run` may start another Editor and
remain prohibited against the active working copy. Experimental tooling is retained only after direct
state/preflight/test/build checks agree with the established MCP evidence.

The pilot also established a narrower reliability boundary. Short CLI evaluations are fast and useful, but
Pipeline's current package can drop its port descriptor around domain reloads and applies a five-second
main-thread response window even when the outer CLI timeout is much larger. One mini-boss generator returned
HTTP 400 for that reason and nevertheless finished writing its prefab and Animator controller; `state` then
briefly alternated between reachable and "No Pipeline instance found." Always verify the asset or returned
state, and use MCP for completion/readback when an operation crosses a reload or performs substantial asset
generation. A transport timeout is not evidence that the editor rolled the operation back.

Build output is transactional: Unity writes beside the deliverable under `.staging`, metadata is stamped
there, and only a successful traceable build replaces the old directory. A failed build preserves the last
known-good copy instead of deleting it before `BuildPipeline.BuildPlayer` starts.

## 2026-09-10 — An openness pass must not erase the movement vocabulary

**Symptom.** Level 1 had more steering room, but the wall runs, wall jumps, tower/chimney, slide lintels,
recovery posts, connector ramps and T3 balloon arc had vanished. The course read flatter and offered fewer
ways to express the movement kit.

**Root cause.** `ApplyOpenProjectileCourse` did not merely widen the named decks. It carried an explicit
`OpenCourseClutter` deletion list containing every one of those traversal pieces, while `T3Arc` and `Ramps`
were replaced with empty arrays. Re-running `8a` therefore deterministically removed them from shipped data.

**Fix.** Keep the broad `OpenCourseDecks`, but regenerate the removed pieces from the public,
idempotent `HybridCourseStructures` table on the expanded outer shoulders. Restore four measured balloons
and five supported connector ramps. Author three colliderless, glowing `INSIGHT` hand markers as
`LevelDefinition.insightRoutes`; their source sentries and entry/rejoin boxes identify optional flare
shortcuts without adding triggers or changing combat. Add opt-in local player/projectile timing capture so
future cadence adjustments can use the player's real slide/parry trace.

**Invariant.** “Open” means preserving a wide readable base lane, not deleting alternate movement. A
secondary wall/balloon/flare line stays outside that lane and rejoins before the arena. Geometry, marker,
route anchors and shipped assets must all be regenerated from data and asserted after the build.

## 2026-09-10 — A water sheet on two coplanar decks needs one unambiguous owner

**Symptom.** `T1_Water_Fast` was fully contained by its shoulder deck, but the traversal audit reported it
on `T1_Stone_2` and outside that deck.

**Root cause.** The widened shoulder overlaps the main landing at the same surface height. The sheet centre
sat exactly on the main landing's east edge, so the deterministic first supporting-box lookup selected that
smaller deck before reaching the shoulder that contains the full sheet.

**Fix.** Move only the sheet centre 0.1 m east. It remains inset on all four edges of `T1_Fast_1`, while its
centre now belongs to that deck alone. The level test pins both the owning deck and full containment.

**Invariant.** Where coplanar traversal decks overlap, a surface effect's centre must lie strictly inside
one intended owner; do not depend on platform-array order to decide which support an audit or builder finds.

## Adding an opening must not skip the original level (2026-09-07)

**Symptom.** Level 1 spawned at the new descent immediately before the boss, bypassing the original
course. **Root cause.** The first follow-up made `ApplyDescent` own `playerStart` despite that pass
authoring the final encounter. **Fix.** Retain the final descent and prepend a separate
`ApplyOpeningDescent` before Ground_Start; only the opening pass sets the crest spawn and pedestal.
**Invariant.** Validate route order as well as spawn-on-platform: loading from the actual main menu
must put the original course ahead of the player. Tests pin two descents and unchanged older content.

## Feature-suite trigger reach and flask respawn contamination (2026-09-07)

**Symptom.** Neutral-input full runs repeated pickup, bloodstain recovery and interrupted-flask
failures after the ramp-start follow-up. Read-only InputReader observation confirmed no movement or
jump input throughout one complete run.

**Root cause evidence.** The suite had already warped to the old checkpoint: trigger tests ran at
`(0,0.05,3)`, not the new ramp. At capped 60 fps their single 14 m/s impulse stopped at z3.76,
short of the pickup at z5.4 and stain at z5.0. The flask section inherited an airborne player from
Items, fell into a kill zone, then respawned at full health during the interrupted-drink wait.
That respawn, not a completed flask drink, violated its no-heal assertion. Local evidence:
`TestResults/descent/full-position-trace.txt`.

**Invariant.** A physics-trigger check must actually move the capsule through the trigger, and a
healing check must begin on stable footing without inherited velocity. Keep these corrections in
the harness; do not retune movement, trigger sizes or healing to accommodate test staging.

**Fix.** The two test-only entry impulses are 20 m/s. The flask test uses the existing motor teleport
to the current checkpoint (start spawn fallback), then waits for grounded contact before drinking.
The real trigger, inventory, wallet and interrupted-heal assertions remain unchanged.

## 2026-09-07 — Two draw paths, and a prefab asset is never active

**Symptom A.** Removing the HUD's BEST RUNS pane would not have removed BEST RUNS. `GhostHud` writes
its table into the HUD pane when it finds one and otherwise falls back to drawing
`"<b>BEST RUNS</b>" + table` on its own runtime canvas — so deleting the pane REVERTS the screen to the
older loose block instead of clearing it. **Fix.** The builder stops emitting the pane and `GhostHud`
ships `BoardVisible` false, closing both paths; the leaderboard data and the ghost delta are untouched.
**Invariant.** Before deleting a readout, find every writer. A fallback renderer is a second writer.

**Symptom B.** `SettingsPrefabTests` failed asserting `WeaponTwirl` sits under a `WeaponViewmodel`,
while the two components were verifiably on the SAME GameObject of the shipped prefab.
**Cause.** A prefab ASSET is not in a scene, so every object in it reports `activeInHierarchy == false`,
and the no-argument `GetComponentInParent<T>()` skips inactive objects — it returns null even for a
component on the same object. `GetComponentInChildren<T>(true)` was already correct elsewhere in the
same file, which is why only this assertion failed. **Fix.** `GetComponentInParent<T>(true)`.
**Invariant.** Every `GetComponent*` call against an asset loaded with `LoadAssetAtPath` needs the
`includeInactive` overload. A shipped-value test that reads the prefab wrongly reports a false failure,
which costs more than no test at all.

**Symptom C.** `TheFirstRampIsWideAndFullySupported` failed on an overlap of `0.1999993` against a bare
`>= 0.2f`. The authored overlap IS exactly 0.2 m; `16.30f + 3.90f` lands on `20.199999` in float. The
identical assertion on the ramp's other end passed only by rounding luck. **Invariant.** A geometry
assertion on an exactly-authored boundary needs the file's own 0.001 epsilon on BOTH sides, or it is a
coin toss that fails the day the number is met precisely.

## 2026-09-07 - A progress gate is a floor, not a schedule (a regression I shipped and reverted)

**Symptom.** The user asked for "more runway before the first enemy so you can have more time to parry"
on the T0 opening. I raised `memberProgressGates[0]` from `0f` to `14f` - the one authored number
standing between the crest and the first bolt - and it read as correct on paper. Playing it, the user
reported the opposite of an improvement: *"the logic for those was somewhat working and making so I
could parry them when sliding but now its off"*, and *"the turret is not aggroing soon enough (the
first one on the left)"*.

**Cause.** A gate is a FLOOR on where a beat may happen, in a SEQUENTIAL volley where each member also
waits on the previous shot resolving plus `recoveryGap`. Raising the first gate therefore did not
insert runway in front of the ladder - it deleted the first beat's approach and pushed every later beat
further down the slide, so the rhythm the player had learned came apart. The turret also read as
"not aggroing" because it was awake (inside its 32 m `WakeRange`) but forbidden to fire.

**Fix.** Reverted to `0f`. Runway at the top is bought by LENGTHENING THE RAMP ABOVE `progressOrigin`,
which leaves every beat's relationship to its own turret untouched.

**Invariant.** Do not buy space at the start of a sequenced encounter by delaying its first beat. Move
the geometry the encounter sits on. And note the shape of the error: the change was verified by the
live probe (five parries, full 1.60x) and STILL broke the feel - the probe parries on a forecast, so it
proves the ladder is completable, never that its rhythm is learnable by a human. `OpeningTurretTests`
now pins the first gate at 0 with this reasoning attached.

## 2026-09-07 - A control path is not a binding path (the rebind that only ever gave you F11)

**Symptom.** The user: *"i can bind the twirl to anything other than f11 for some reason"*. Every
rebind appeared to fail and the flourish stayed on its default key.

**Cause.** The interactive rebind read `op.selectedControl.path`. `InputControl.path` is a **runtime**
path - `/Keyboard/f11`, leading slash, no device brackets (its own doc example is
`"/gamepad/leftStick/x"`). A **binding** path, the only thing `ApplyBindingOverride` can resolve, is
`<Keyboard>/f11`. `SettingsData.SanitizeBindingPath` rejects the former on purpose, so the completion
handler took its "unusable path" branch, re-applied the saved value and called the CANCEL callback.
Every key behaved identically; the default was the only reachable binding.

**Fix.** Read `action.bindings[0].effectivePath` instead. The operation has already applied its own
override by the time `OnComplete` runs, and what it wrote is the canonical binding path.

**Invariant.** `InputControl.path` and `InputBinding.effectivePath` are different namespaces and only
one of them is an override. `SettingsDataTests.OnlyABindingPathSurvivesSanitising_NotARuntimeControlPath`
pins both shapes. More generally: a validator that silently downgrades a bad value to "use the default"
hides the bug that produced it - this one turned a format error into a plausible-looking no-op, and it
took a player noticing that one specific key "worked" to surface it.

Institutional memory for `vibegame1`. Every non-obvious problem that cost real time, and the invariant
that stops it recurring. **Read this before debugging anything weird** — there is a good chance it is
already in here.

Newest first. When you solve something non-obvious, add an entry at the top in the same shape:
**Symptom → Root cause → Fix → Invariant**.

Related: [ARCHITECTURE.md](ARCHITECTURE.md) · [TOOLING.md](TOOLING.md) · [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md) · [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md)

---

## 2026-09-08 — Solar breathing room must translate route sections as rigid groups

**Symptom.** Enlarged portal suns still intersected T2's optional west wall-run geometry and T3's first
wall, even after obsolete court geometry was removed. Moving one ledge three metres only exposed the next
overlap and risked changing the authored movement line.

**Root cause.** The original route was packed around smaller arena footprints. A visual-radius clearance
audit measures every platform, torch and gate against every sun, so it correctly found whole clusters—not
one bad mesh—inside the new 1.5 m breathing-room envelope.

**Fix.** `LevelDefinitionAuthoring` normalizes any prior section offset, runs its absolute authoring passes,
then moves all T2 content +15 m in Z and all T3/T4/boss-route content +19 m. Gates, triggers, checkpoints,
pickups, ramps, spawns, torches, balloons, water and portal returns move with their owning route. Historical
legendary/boss SpawnDefs remain fixed because the builder relocates those live instances into remote realms.

**Invariant.** Fix a clustered landmark collision at the composition level. A route section moves as one
rigid authored group; never buy portal clearance by shrinking the landmark or nudging isolated movement
pieces. Applying the authoring pass twice must serialize identically, and the exhaustive sun-clearance test
must examine all route boxes, torches and gates.

## 2026-09-08 — The dashboard must follow the repository contract and nested data layout

**Symptom.** The dashboard presented `CLAUDE.md` as the project contract, showed no current enemies, and
reported a blank branch with zero changes even in a dirty working tree.

**Root cause.** Its document list predated tool-neutral `AGENTS.md`; its roster scanned only flat
`Assets/Data/Enemies`, `Movesets` and `Attacks` folders; and Git rejected the repository ownership context
when the generator ran without the project's safe-directory setting. All three failures silently returned
plausible empty data.

**Fix.** The dashboard now leads with `AGENTS.md`, discovers every plain-Markdown specialist brief and shared
workflow, recursively indexes nested content assets while retaining their relative paths, and supplies the
repo-local safe-directory setting to read-only Git subprocesses. The regenerated page sees 14 enemies and
the real branch/change history.

**Invariant.** A project dashboard is a projection of current repository truth, not a second hand-maintained
schema. Recursively discover tool-neutral contracts and content, preserve relative paths through GUID joins,
and treat an empty Git result as an error condition to investigate rather than valid project state.

## A modal dialog in Unity deadlocks the MCP bridge and looks exactly like a lost bridge

**2026-09-07.** `SandboxBuilder.Build()` was called over MCP. It returned `success:false, message:null`,
and from that moment **every** MCP call — `execute_code`, `read_console`, even the `editor/state` resource —
returned `Unity session not ready for '<tool>' (ping not answered); please retry`. That is the same string
the bridge produces when it has genuinely lost its startup handshake, so the obvious next move is the
documented fix for that (save a script to force a domain reload, or Tools → MCP Bootstrap → Reconnect
Bridge) — and it would have done nothing here. Polling for two and a half minutes never cleared it.

**Root cause.** `SandboxBuilder.cs:67` calls `EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()`.
The generators that must run before it (`DataFactory` → `PrefabFactory` → `HudBuilder`) leave the open scene
dirty, so Unity put up a modal **"Scene(s) Have Been Modified"** window and blocked its own main thread
waiting for a click. The bridge answers pings on that thread, so it goes silent — the editor is not broken,
it is waiting for a human.

**How to tell the two apart in one call, without guessing.** Measure Unity's CPU over ten seconds: a
compiling or baking editor burns CPU, an editor blocked on a modal sits at ~0. Then enumerate its top-level
windows — `EnumWindows` filtered to the editor's PID via `GetWindowThreadProcessId` — and read the titles.
A window called "Scene(s) Have Been Modified" (or any other dialog) IS the answer. Note that
`Get-Process ... Responding` reports **True** for a modal-blocked editor, so it proves nothing here.

**Fix.** The dialog window can be forced visible with `SetWindowPos(hwnd, HWND_TOPMOST, …)` and
screenshotted with `CopyFromScreen` to read its buttons before clicking anything — worth doing, because a
blind Enter could hit "Don't Save". Escape did not dismiss it. Clicking **Save** freed the bridge and the
generator ran to completion.

**Invariant.** **Save the scene before running any generator that changes scenes** (7 `SandboxBuilder`, and
9 `MainMenuBuilder` for the same reason) and the dialog never appears. And when every MCP call says
`ping not answered`, **look at Unity's windows before you touch the bridge** — the reconnect procedure is
for a bridge that is actually down, and running it against a modal wastes the time the modal is already
costing.

---

## The photograph tools leave the editor in an empty `Untitled` scene

**2026-09-07.** After `WeaponShots.Shoot()` the user reported the editor "showing an untitled view" and
"no cameras rendering". Both are true and neither is damage: the capture tools open a scratch scene to shoot
into, and an empty scene has no camera, so the Game view says exactly that.

**Root cause.** Nothing is wrong. `Assets/Scenes/Level_01.unity` was never touched, and hard rule 4 means
there is nothing hand-authored to lose in the scratch scene either.

**Invariant.** **Reopen the working scene after any `Photograph …` / `Film …` tool** —
`EditorSceneManager.OpenScene("Assets/Scenes/Level_01.unity", OpenSceneMode.Single)` — rather than leaving
the user looking at an empty Game view and wondering what a session just deleted. Confirm it with a camera
count in the same call.

---

## The feature suite run against a paused world reports 49 plausible failures

**2026-09-07.** A play-mode feature run reported **720 passed / 49 failed / 2 skipped**. The failure list
looked like a serious multi-system regression: Hitstop, TimeScale, Pause, every Stamina check, every Flare
check, WallRunLive, Posture regen, combo advance, the wand pedestal, the trail, lock-on assist. Nothing had
changed in any of those systems.

**Root cause.** `Time.timeScale` was **0** for the whole run — the world was paused. Every one of the 49 is
a timing test reading a stopped clock, and they fail in a cascade that reads exactly like real breakage:
`Hitstop_BaselineWorldScale [actual=0 expected=1]`, `Stamina_ThreeDashesFromFull [dashes=0 stamina=100]`,
`Flare_TossesUp [velY=0.0]`, `WallRunLive_ShedsGrit [peak motes alive=0 over a 0.00 s run]`. With the clock
stopped nothing can move, so the suite measures nothing and reports it as failure. `Time.timeScale` read
back as `1` immediately after the run, which is what confirmed it.

The session had followed the known domain-reload rule — enter play, exit, re-enter, and check
`GameManager.I != null` — and that check passed. It is necessary and **not sufficient**: it proves the
session is warm, not that the world is running.

**Fix.** Assert the clock in the same call that starts the suite, so a paused world refuses to produce a
report at all:

    return UnityEngine.Time.timeScale < 0.99f
        ? "REFUSED: world is paused (timeScale=" + UnityEngine.Time.timeScale + ")"
        : VibeGame1.EditorTools.FeatureTestRunner.Start();

**Invariant. A feature-suite result is only evidence if `Time.timeScale == 1` at the moment it started.**
Check the clock, not just the singleton. And when a run comes back with a large, broad failure set that
spans unrelated systems, suspect the harness before the game — read two or three failure payloads first:
if they all say `0`, `0.00 s` or `unchanged`, nothing ran.

A second, dumber cost from the same run: a poll loop written as `grep -qi "running"` matches
`running=False` as happily as `running=True`, so it never terminates. Match `running=False` explicitly.

---

## `execute_menu_item` over MCP reports success and does nothing

**2026-09-06.** The surge-turret pass was verified by running `VibeGame1/3. Create Data` and
`VibeGame1/4. Build Prefabs` through the MCP `execute_menu_item` tool. Both returned
`success: true`, the console showed no error, and `pshooter_enemy03.asset` did not exist afterwards.
`AssetDatabase.FindAssets("t:EnemyData")` listed the same eleven enemies as before.

**Root cause.** Not established. The bridge logs
`[ExecuteMenuItem] Handling menu item command` and then the item's body never runs — the tool's own
message is honest about this ("Attempted to execute menu item ... Check Unity logs"), so its success
flag means *the request was delivered*, not *the menu item ran*. It is a fire-and-forget dispatch with
no result channel.

**Fix.** Invoke the generator directly instead, which is synchronous and returns:

    var t = System.Type.GetType("VibeGame1.EditorTools.DataFactory, Assembly-CSharp-Editor");
    t.GetMethod("CreateAll").Invoke(null, null);
    UnityEditor.AssetDatabase.SaveAssets();
    UnityEditor.AssetDatabase.Refresh();

**Invariant. A generator is not run until you have read back what it was supposed to produce.**
Drive the rebuild pipeline from `execute_code` against `VibeGame1.EditorTools.<Class>.<Method>()`, and
end every step by loading the asset it creates and asserting a field on it. `execute_menu_item`
returning `success` is not evidence of anything.

Related trap: `read_console` with a `filter_text` can return a single entry that is tens of thousands
of characters long — `Health Check` logs all 1744 warnings as ONE message. Capture the log through
`Application.logMessageReceived` inside `execute_code` and slice out the section you want, rather than
paging the console.

---

## A pane the runtime resizes needs stretched children, and the y under it needs a named constant

**2026-09-06.** BEST RUNS moved out of its own column into the top-right stack, under the radio, and now
ships COLLAPSED (title strip, the personal best, a "+N MORE" line) and grows to the full table for four
seconds whenever the board changes. Two traps. (1) `BestRunsText` was authored with `Rect(...)` at the
full table's height; a pane whose height the runtime changes has no layout group, so the fixed rect kept
drawing eight rows out of the bottom of a 108 px glass — `Truncate` clips to the TEXT rect, not to the
pane. It is stretched to the glass on all four sides now (`HudBuilder.StretchInto`). (2) The stack below
BEST RUNS (hint at -12, the 700-tall level-editor panel at -48) is only on a 1080 canvas because
`BestRunsBottom` is -272; that is why the collapsed height is 108 and not "whatever looks right" — radio
116 + gap 16 + 108 lands on exactly the y the pieces under it were tuned against.

**Invariant.** A child of a pane the runtime resizes is anchored to the pane, never sized by a literal.
Anything positioned relative to another pane hangs off that pane's named constant, and the constant is
the one a test asserts.

## Moving enemy files into family folders: AssetDatabase.MoveAsset, never delete-and-recreate

**2026-09-06.** The `parkour_enemies` / `souls_enemies` split moved ten data assets, ten movesets, fifteen
scripts and six tests. Every move went through `AssetDatabase.MoveAsset` from an `execute_code` snippet,
so each `.meta` (and so each GUID) travelled with its file and every prefab's component and data
reference survived; the generators were then re-run and Health Check read clean. Two traps met on the way:
`"
"` inside an `execute_code` JSON string arrives as a literal newline and fails the C# 6 compile
("Newline in constant"), so build multi-line results with a separator instead; and the
`8. Build Level From Definition` menu item shows a modal "select a definition" dialog when nothing is
selected in the Project window, which blocks the main thread and kills the bridge until a human clicks it.
Rebuild the level by code: `LevelDefinitionBuilder.Build(def)` or `BuildCanonicalHeadless()`.

**Invariant.** A file move in this project is an `AssetDatabase.MoveAsset` (or a Project-window drag), and
a generator a session drives is one that never opens a dialog.

## A pane's glass is not its root, so hiding the glass leaves a black frame

**2026-09-06.** Play: "a random black menu below the best runs menu that does nothing." `HudBuilder.Pane()`
returns the GLASS transform (content parents under it), not the group root that holds the shadow, sheen and
edge-light layers. `BuildLevelEditor` wired `ed.panel = pane.gameObject`, so `LevelEditor.Awake` hid the
glass and its buttons while the shadow and sheen stayed on screen as an empty black pane. The BEST RUNS
pane already did it right (`best.parent.gameObject`). Same pass: that pane was 196 tall for a 200-tall
eight-row table, so the board spilled out of its glass; it is now sized from `Leaderboard.DisplayCount`
(`HudBuilder.BestRunsHeight`) and the hint and editor panel hang off `BestRunsBottom`.

**Invariant.** Anything that toggles a pane toggles `Pane(...).parent`. A pane that holds a list is sized
from the list's count, never a literal.

## A span shooter that chases is a melee enemy with a gun it forgets to use

**2026-09-06.** Play: "the parkour enemies stop shooting too early, the rhythm is bad, the detection is bad."
Three causes, all in the wiring rather than the numbers. (1) The shooter only fired while the brain was
awake, and the brain woke at `aggroRange` (14 m) with a single chest-to-chest linecast -- so a perch
30 m up the span sat idle through half its band, and a runner whose chest passed behind a rail was
invisible. (2) The band's near edge was 10 m so the cue lead held; once inside it the shooter went
quiet and the brain walked off the perch to melee, which is the "stops shooting" the player felt.
(3) The interval carried a ±15% jitter and every fire reset the clock from `Time.time`, so a held
shot shifted the beat: there was no rhythm to learn.

**Fix.** `EnemyData.rangedOnly` makes a SENTRY: Chase is Stop + FaceTarget, no commit, no sleep.
Wake range for a shooter is `max(aggroRange, projectileMaxRange)` and sight is the best of three
lines (head, chest, feet), shared with the shooter through the static `EnemyController.HasLineOfSight`.
The near edge drops to 3 m and `ProjectileMath.LaunchSpeed` slows the launch inside 11.5 m so the
flight is always cue lead + 0.08 s. The beat is `ProjectileMath.NextBeat` -- previous beat plus
interval, re-anchored only after a silence longer than a beat -- and `LeadTarget` aims 80% of the
player's flat velocity ahead.

**Invariant.** A bolt is never owed its cue before it exists: assert `TimeToImpact(minRange,
LaunchSpeed(minRange, ...)) > CueLead` on the shipped data, not `minRange / speed`. A shooter's
rhythm is a grid, never `now + interval`.

## A wind-up pose cannot be computed, only photographed

**Symptom.** Eleven per-attack wind-up poses were authored into `EnemyAttackData.windupPose`, each with a
careful comment saying what shape it made — "PURE VERTICAL, no yaw, nothing horizontal at all", "PURE
HORIZONTAL, deliberately no pitch", "the exact mirror of hit A", "the boss REARS, arm past vertical, the
tallest shape in the game". The assets shipped, the tests were green, and the enemies still all looked
like they were doing the same thing.

Measured on screen from the player's eye at real fighting distance, **ten of the eleven made a different
shape than their comment claimed**:

| Authored intent | What the player actually saw |
|---|---|
| `Heavy_Overhead` "pure vertical" | tilt **74°**, and only **0.51** of a blade long |
| `Heavy_Sweep` "pure horizontal" | tilt **63°** — the same steep diagonal, in the same place, 11° apart |
| `Grunt_Heavy` "a mast a body-height above the head" | tilt **−3°**, length **0.38** — a short horizontal stub at head height, *smaller* than the jab it is meant to contrast with |
| `Boss_Slam` "past vertical, the tallest in the game" | tilt 63°, length 0.54 |
| `Boss_DoubleSlash_B` "the exact mirror of A" | tilt 71° vs A's 72° — the same lean, not the mirror |

Four of the boss's five wind-ups were one shape. The two Heavy attacks with **opposite correct answers**
— dodge-vertical versus dodge-horizontal — were indistinguishable.

**Root cause. `armWindup` is not the blade's angle; it is the first of three rotations.** The blade's
on-screen orientation is

```
bodyEuler (at LungeRoot)  ×  armWindup × 1.12 (at the shoulder)  ×  armWindup × 1.12 × weaponLag (at the hand)
```

then perspective. `weaponLag` exists so the hand *trails* the shoulder and the blade whips — which means
the same authored shoulder Euler produces a different blade angle at every lag value, and the body yaw
underneath it rotates the whole result again. Hand algebra ("−100 keeps it in the view plane", "the arm's
−56 yaw cancels the body's +46") is confident, plausible, and wrong by tens of degrees.

Two secondary effects made it worse, and both are invisible until you look at a frame:

- **Foreshortening eats the pose.** A blade cocked back over the head points *away* from a first-person
  player, and a 1.0-length blade renders as a 0.38 stub. The biggest, most-damaging attack drew the
  smallest mark on screen.
- **The lighting turns with the blade.** Rotating the same blade away from the key light took it from
  bright tan to near-black on a near-black enemy. `Heavy_Overhead` was both the shortest *and* the
  darkest shape in its own moveset.

**Fix.** Stop deriving, start measuring. `FrameFilm.RunWindups(dir, prefabName, distance)` stages a fresh
enemy, switches off its brain, its agent and its colliders, parks the player square in front at a chosen
fighting distance and films the `_mid` and the `_peak` (post-`CueFlash`, arm frozen) of every attack in
the moveset. A solver then searched shoulder-Euler space against the **measured** on-screen signature —
blade angle, foreshortened length, and where the tip sits in body-heights — for a target silhouette per
attack. Every shipped value is a solver result that was then photographed and looked at.

**Invariant.** **Author the silhouette, solve for the Euler; never the reverse.** A wind-up pose is
specified as a shape on screen (angle, length, tip position, in body-heights) and the numbers in
`DataFactory` are whatever produces it. A pose comment that describes an intent rather than a measurement
is a comment that will be wrong. `FeatureTests → WindupPoses` re-measures all eleven on the real prefabs
and fails if a pair that matters stops being separable — it deliberately does **not** assert the authored
Eulers, because asserting those would have passed on every single one of the broken poses.

**Two smaller traps paid for in the same session.**

- **A boss shoves the camera off the mark.** A 2.2× body's capsule overlaps the stand point, so a capture
  that parks the player once at the top of the run photographs the arena from 52 m away by the fifth
  attack — while still logging `dist=5.50` from staging. Re-park before *every* frame, and switch the
  prop's colliders off: a photo model does not need them.
- **A boss does not attack out of `combos`.** `BossData.phases[].patterns` is where its attacks live;
  `combos` holds a one-entry fallback used only before a phase is applied. A sweep that enumerates
  `ResolveCombos()` alone finds one of the boss's five wind-ups and silently reports the other four as
  not existing.

---

## The swing trail hung in mid-air, detached from the blade that drew it

**Symptom.** The first weapon trail read, frame by frame, as a bright streak floating to the right of the
screen with nothing attached to it — the blade was already down and left, and the ribbon just sat there
for five frames before fading. On the sword the gap between the blade tip and the ribbon's head was about
150 px.

**Root cause.** Two separate faults that looked like one.

1. **A frame of lag at the head.** The trail samples the tip in `LateUpdate`; the swing loop ends in
   `Update`. So the newest point in the ribbon was always one frame behind the end of the arc — and on a
   weapon whose strike leg is three frames long, one frame of arc is a hand's width on screen.
2. **It dimmed in place instead of retracting.** Fading only the alpha and the width leaves the ribbon at
   full LENGTH for its entire dissolve, so the detached streak stays exactly as long as it was while it
   dies.

**Fix.** `WeaponTrail.EndStrike()` takes one final sample before it closes, and `AttackCo` applies the
`swingEnd` pose immediately *before* calling it, so that sample lands on the true end of the arc. During
the fade the ribbon **retracts from the tail** (`count → ceil(peak × fade)`), so it closes toward where
the blade left off rather than hanging at full length.

**Invariant.** *A trail sampled in `LateUpdate` from an animation driven in `Update` is one frame short by
construction — the phase that closes it must take the last sample itself, and the caller must apply the
final pose first. And a trail dies by getting SHORTER, not only dimmer: a ribbon that keeps its length
while it fades sits over the fight for an extra beat.*

---

## The trail left the screen at full width, because the taper was linear

**Symptom.** The first ribbon ran off the right edge of the frame still several pixels wide and at full
brightness — a wire strung across the image rather than a sweep behind a blade.

**Root cause.** A `LineRenderer`'s `startWidth`/`endWidth` is a **linear** ramp, and the ribbon held 18
samples, so the oldest third of it was still at ~30% width where it left the frame. The obvious
alternative — fading alpha along the ribbon with `colorGradient` — does nothing here: **URP/Unlit ignores
vertex colour**, so there is no per-vertex alpha channel at all.

**Fix.** 12 samples, and an explicit `widthCurve` with a fast decay (`1 → 0.5 @0.3 → 0.18 @0.65 → 0.03`)
driven by `widthMultiplier` so the whole-ribbon fade still scales it.

**Invariant.** *On a URP/Unlit line, WIDTH is the only head-to-tail channel there is. Reach for
`widthCurve`, never `colorGradient`, and never assume a linear ramp reads as a taper — it does not.*

---

## Growing the embers turned them into black slivers, and some of them were ghosts

**Symptom.** The Pyre embers were made larger and stretched into sparks (12 mm cube → 12 × 68 mm streak),
and dark slivers appeared around the weapon and in the sky — holes in the frame where a spark should be.

**Root cause, part one: a lit black box.** The embers borrowed the **blade's** URP/Lit material and were
drawn by writing `_EmissionColor` over a black `_BaseColor`. Any ember past the bright part of its life
was therefore a black, unlit object in front of a lit sky. At 12 mm nobody could see that; at 68 mm it is
a stripe. *A shape too small to see is also too small to be visibly wrong.*

**Root cause, part two: half of them were not real.** `WeaponEmber` builds its pool in `Awake`. A **domain
reload during play mode** (another agent recompiling) wipes the component's non-serialized fields but
leaves the ember `GameObject`s in the scene — so they freeze mid-flight, wearing whatever material and
colour they last had, and never animate again. Several of the "dark slivers" being diagnosed were these
orphans from a previous assembly.

**Fix.** The embers own an **additive** URP/Unlit material (`SlashFx.CreateAdditiveMaterial`) and are
drawn by writing `_BaseColor` with alpha — additive can only ever *add* light, so a dim spark is faint
instead of black. The brightness ramp also went from `(1−k)²` to `(1−k)^0.45` so a spark stays hot for
almost all of its life and then goes out. And every judgement was re-made from a **freshly restarted**
play session.

**Invariant.** *A particle that can be dim must be ADDITIVE, never a lit surface with emission on a black
base — the lit version is a hole in the frame the moment it is big enough to see. And a play session that
has survived a domain reload cannot be trusted for a VFX screenshot: pooled objects built in `Awake` are
still on screen but are no longer being driven by anything.*

---

## A CharacterController moving 0.8 m in one frame is not grounded, and the slide died of it

**Symptom.** The slide covered **4.0 m at 500 fps and 1.8 m at 20 fps** from the same press, and the short
one always ended at exactly 0.12 s — `coyoteTime`, to three decimal places. The feature suite reported it as
a working slide with a small number; only comparing two framerates showed it was the *same bug* both times,
just less of it.

**Root cause.** Two compounding facts about `CharacterController`. First, **resizing it drops its ground
contact until the next `Move`**, so the frame after a slide started reported `isGrounded == false` and the
airborne branch cancelled the slide outright. Second, and worse, **a mostly-horizontal sweep on a flat floor
does not re-establish contact**: at 16 m/s and 20 fps the controller moves 0.8 m sideways and 0.1 m down in
one step, the sweep grazes the floor, and `isGrounded` stays false indefinitely. The slide branch — which is
where friction and the end conditions live — therefore never ran, so the slide neither bled speed nor
reached its floor; it simply sat there until the coyote window expired and the airborne branch killed it.
`slideHeight` was also exactly `2 * (radius + skinWidth)` = 0.9 m, which degenerates the capsule to a sphere
and makes grazing contact even weaker.

**Fix.** Three things, in order of importance. The slide branch now runs on `slideOnGround = sliding &&
(IsGrounded || within coyoteTime)` rather than on `IsGrounded` alone, so a one-frame contact flicker cannot
stall it — and the same tolerance means a lip, a platform seam or a kerb no longer eats a slide. `TrySlide`
reseats the controller with one tiny downward `Move` immediately after shrinking it. `slideHeight` went
0.9 → **1.0 m**, keeping a real cylindrical section. Verified across **20 / 30 / 60 / 144 / 400 fps**:
3.75 / 3.97 / 3.94 / 3.97 / 3.98 m.

**Invariant.** **Measure movement at more than one framerate before believing it.** Anything that resizes a
`CharacterController`, or moves it fast and horizontally, must not treat `isGrounded` as authoritative for a
single frame — gate on coyote time, not on the flag. And keep a controller's height comfortably above
`2 * (radius + skinWidth)`; at exactly that value it is a sphere and its ground contact is fragile.

---

## A slide that ends when you release the key is a slide that ends when the frame is long

**Symptom.** Every scripted slide lasted 0.00 s and covered 4 cm, while the same code played fine by hand.

**Root cause.** The slide ended on key-release after a `slideMinDuration`, and `Time.deltaTime` is clamped to
`maximumDeltaTime` (0.333 s) — so **one long frame is longer than the minimum**, and the release check fired
on the very first update. It also made the mechanic untestable in principle: a test calls `TrySlide()` and
holds no key, so the slide it starts is always cancelled immediately.

**Fix.** The release-cancel was removed outright. A slide is a **committed** 0.35 s of speed that ends on its
speed floor or its duration cap; you cancel it by jumping or dashing out, which is the tech anyway.

**Invariant.** **A mechanic whose lifetime depends on a key being held cannot be driven by the `Try*` entry
points**, and every input-gated behaviour in this project is required to have one. If a design needs
hold-to-continue, the hold has to be state the entry point can set, not a poll of the device.

---

## Measuring movement on level geometry measures the level, not the movement

**Symptom.** The slide/wall-jump tests reported a 1.8 m slide, "no wall in range" *finding* a wall, and "the
same wall twice" being allowed. Three different mechanics apparently broken, all at once.

**Root cause.** The test runway was the boss approach, which is 6 m wide with rails at x = ±3.1. A 16 m/s
slide reached the far rail in 1.8 m, and both rails sat inside the wall scan's ~0.9 m reach — so the "open
air" check found one rail and the "same wall" check found the other. In the sandbox the same code measured
3.99 m and behaved perfectly.

**Fix.** The test builds its own rig — a 60 x 60 plate parked at (300, 40, 0), far outside the course, with
its own walls — and destroys it afterwards. The wall-jump measurement also had to `Launch` high enough to
still be airborne past coyote time and drift *into* the wall; a 2 m hop landed before the press.

**Invariant.** **A measurement rig owns its own space.** Never measure a movement capability against shipped
geometry: you are measuring the geometry. Level geometry is for *reachability* assertions
(`CheckHop` off the built renderer bounds), which is a different question and belongs in a different test.

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

## Four weapons photographed as one weapon, stacked on top of each other

**Symptom.** Rebuilding the whole loadout at dagger scale, a comparison shot of each weapon was taken by
looping `wc.Equip(i)` and `ViewmodelCapture.Shoot(...)` in one `execute_code` call. Slot 1 was clean;
slots 2, 3 and 4 came back with the *previous* weapon still in the frame, orange hammer band and violet
kris overlapping. It reads as a broken prefab, and the first instinct was to go looking at the geometry.

**Root cause.** `WeaponViewmodel.SetWeapon` releases the old model with `Destroy(instance)`, which Unity
defers to the **end of the frame**. Equipping and rendering inside the same frame therefore renders both
models. Nothing was wrong with any prefab.

**Second trap, on the retry.** Splitting the capture across one MCP call per weapon fixed the stacking
and broke something worse: the other agent working in the project recompiled, which dropped play mode,
and three of the four "weapons" were photographed in **edit mode** — an empty course, no arms, no
weapon. The tell was that the three PNGs had *byte-identical* sizes.

**Fix.** `ViewmodelCapture.LoadoutTour(dir, slowFactor)` — equip, **let a tick pass**, shoot the idle,
then play the swing and burst across it, for all four slots, driven from one `EditorApplication.update`
sequence so nothing can recompile between the equip and the shutter. `slowFactor` stretches the swing
only: the pose path is a normalised lerp, so a 3× swing walks exactly the same poses, and the editor's
~8 Hz tick actually samples the dagger's 0.22 s arc more than twice.

**Invariants.** **A `Destroy` is not a disappearance until the frame ends** — never swap a model and
render it in the same frame. And **a multi-call capture is a capture with a recompile in the middle**:
check `EditorApplication.isPlaying` *and* `GameManager.I != null` in the same call that takes the frame,
or drive the whole sequence from one call. Identical PNG file sizes across supposedly different subjects
means you photographed the same thing every time.

---

## A hammer that is dagger-sized cannot tell you it is a hammer by being long

**Symptom / decision, not a bug.** The whole loadout was taken to dagger scale because the arms made
long blades read as poles across the frame. Four short blades are four weapons the player cannot tell
apart, and a hammer shaped like a dagger actively *lies* about what it does.

**What the shapes carry now.** Length was given up as the differentiator and replaced by mass and edge:
a wide knobbed **cross** (sword), a **top-heavy** blocky mass head with the thickest grip in the set
(hammer), the thinnest **needle** (dagger), and a wavy **serrated** blade with twin rings in a colour no
other weapon uses (dev). All four sit in a 0.27–0.32 m on-screen band, so nothing is told by size.

**The honest cost.** Reach information is gone from the viewmodel — though it was never really there:
`hitOffset` / `hitRadius` are camera-space (1.3–1.8 m) and no viewmodel has ever been longer than about
0.6 m, so the model was never a range cue. What *is* genuinely weaker is the pre-swing weight read: a
long haft used to imply a slow swing before it started. That now rests entirely on the head's volume and
on `attackDuration`. If a playtest says the hammer feels like a fast weapon, the fix is the head's mass
and the wind-up pose — **not** the numbers, which were deliberately left untouched.

---

## Rigged forge FBXs: an animated model imports as one useless take

**Symptom.** `Assets/Enemies/PaleMarionette.fbx` ships fifteen animations. Unity imported it with a
single clip called `Take 001`, 21.6 s long, playing every animation back to back. Nothing named `Walk`
or `AttackSwing` existed to play, so an `Animator` pointed at it just ran the whole reel.

**Root cause.** enemy-forge concatenates every animation onto ONE timeline and ships the frame ranges
separately, as `<model>.clips.json`. Unity has no idea that file exists. Compounding it,
`EnemyForgeImporter` (the tool's own vendored `AssetPostprocessor`) sets `importAnimation = false` and
`animationType = Human` on first import — so the take was not even read, and `importedTakeInfos` came
back empty.

**Fix.** `Assets/Editor/ForgeClipSplitter.cs` + **VibeGame1 -> 4a. Split Forge Animation Clips**: reads
the manifest and writes `ModelImporter.clipAnimations`, forcing `Generic`. Typing those ranges into the
Rig inspector by hand would have "worked" and then evaporated on the next reimport, silently.

**Invariant.** The manifest is the source of truth for clip ranges and **nothing may write
`clipAnimations` except that menu item**. An animated forge model is not usable until 4a has run, and
4a must be re-run after any re-export. Also: `takeName` on a `ModelImporterClipAnimation` that does not
match a real take imports the clip **empty**, with no error anywhere and the enemy standing in its bind
pose — so read the name from `importer.importedTakeInfos[0]`, never hard-code `"Take 001"`.

---

## The obvious clip for a spin was the wrong clip — measure the pose, do not read the name

**Symptom.** The Pale Marionette's spin passes played `AttackSwing`, the clip whose name says "this is
an attack". Screenshots of the whirl showed a thin turning stick. The whole silhouette the fight is
built on — the wide-armed clown puppet — was missing, and the revolution barely read as a revolution.

**Root cause.** `AttackSwing` tucks the arms in to swing. Sampling the `LeftHand`/`RightHand`
separation at 25/50/75 % of every clip in the model gave: `AttackSwing` **0.38 / 0.76 / 1.02 m**,
`AttackOverhead` 0.11 / 1.55 / 1.16, `IdleCombat` 1.24 flat, `Roar` **2.04 / 1.95 / 2.03** with the
hands at 1.37 m. `Roar` is the only clip that HOLDS the arms out for its whole length.

**Fix.** `ModelSpec.spinClip = "Roar"`, played as a pose rather than as a roar, and
`ForgeClipSplitter.ReadHitNormalizedTime` falls back to the manifest's `OnRoar` when a clip has no
`OnAttackHit`. That is safe here precisely because the arms are out for the whole clip, so no single
frame is "the contact" and the anchor only chooses which part of the hold is on screen.

**Invariant.** For a whirl or any pose-driven move, **pick the clip by measuring the rig, not by
reading the clip name.** A clip named for its verb tells you nothing about its silhouette. The
measurement is four lines of `AnimationClip.SampleAnimation` plus a bone-distance read and it is worth
running before committing to any clip choice.

---

## A whirl that never stopped made the tempo break invisible

**Symptom.** `Marionette_Overhead` is a 1.0 s wind-up dropped into a fight of 0.76 s beats, and it
exists to punish parrying on the metronome. In the sandbox it did not read as a break at all. The
overhead *clip* played square-on, but the body kept rotating underneath it at the idle drift rate, so
every frame of the wind-up looked exactly like another pass coming around.

**Root cause.** `PuppetVisuals.UnwindToSquare()` only cleared `passInFlight`, which handed the yaw to
the free-spin drift instead of driving it to zero. There was no "the whirl is OFF" state at all.

**Fix.** An explicit `squaring` flag, set by every non-spin `Telegraph`, by `Settle`, by
`ClearTelegraph` and by `Slump(true)`, and cleared only by `BeginPass`. While squaring the phase is
driven FORWARD to the nearest alignment (never reversed — a puppet reversing its spin reads as a
second, different move) and then holds with a plus/minus 5 degree wobble. Verified by capture: the
overhead's whole wind-up, strike and recovery now hold yaw between 355 and 5 degrees, and the next spin
pass sweeps 359 -> 65 -> 121 -> 169 immediately after.

**Invariant.** **If a body's motion is the tell, the absence of that motion has to be a state you can
name.** "Not currently attacking" is not the same as "deliberately still", and the difference is the
whole read of a tempo break.

---

## A spin phase integrated forward will drift; re-derive it every beat

**Symptom / risk.** The Marionette's whole design is that the player learns one interval. Anything that
lets the body's rotation and the data's impact time diverge — a dropped frame, a hitstop, a deflect
that shortens the recovery — turns the arrival into a lie, and the player is timing off a body that is
no longer where the maths thinks it is.

**Fix.** `PuppetVisuals.BeginPass` does not advance a running angle. Every beat it reads the CURRENT
yaw, computes the arc to the next alignment, and interpolates that arc against the data's own
time-to-impact. Nothing accumulates because nothing is accumulated. Measured over five consecutive
passes at ~8 Hz the yaw ladder was identical to the degree:
`16 -> 100 -> 178 -> 245 -> 292 -> 320 -> 338 -> 352 -> 6`.

**The other half is in the DATA.** An unparried beat is `windup + gap + impactDelay + strike`, but a
parried one is `parryRecoilSeconds x lerp(1, 0.55, aggression) + windup + gap + impactDelay` — a
different expression, so the two are equal only by construction. `Marionette.parryRecoilSeconds` is
**0.167** because 0.167 x 0.719 = 0.120, which is the 0.12 s strike it replaces, giving a 0.760 s
parried beat against a 0.760 s unparried one. It is a derived number, not a felt one.

**Invariant.** For any enemy whose fight is a rhythm: **the parried and unparried beat must be equal,
and the visual phase must be re-derived from the data clock rather than integrated.** If you retune
`aggression`, re-derive `parryRecoilSeconds`. (The residual is bounded: a perfect parry may land up to
`parryPerfectWindow / 2` = 0.065 s early, which pulls the next beat in by that much.)

---

## A spinning enemy that will not chase can be walked away from

**Symptom.** Measured, not theorised. `Marionette_Overhead`'s knockback put the player 7.4 m out. The
puppet then spent the remaining ~7 s of its eight-pass phrase whirling at nothing, closing at roughly
0.1 m per beat, while the player stood and watched. The far-band `Marionette_Lash` could not answer it
because a moveset entry is selected ONCE per phrase.

**Fix.** Two changes, both in data. `Marionette_SpinPass.lungeDistance` 0.35 -> **0.60**, so the spin
walks ~4.8 m over a phrase and is on top of you again by the exit (`lungeMinDistance 2.8` still stops
it burrowing in). And every spin entry is range-gated to `maxRange 6` instead of 99, so a spin is never
*started* from outside its own reach and the far-band lash gets selected instead.

**Invariant.** **A moveset entry is chosen once and then runs to its end**, so range gating is about
where a phrase may BEGIN, not where it stays legal. Any long phrase needs either enough lunge to hold
its own spacing for its whole length, or a `maxRange` that stops it being picked from somewhere it can
never reach.

---

## An Animator on the same transform as a code-driven spin is two writers on one channel

**Symptom / avoided.** `PuppetVisuals` writes the whirl as a local yaw. The obvious place for it was
the model root — the transform the `Animator` sits on.

**Root cause.** `ForgeClipSplitter` imports the clips with `keepOriginalOrientation`, so the take's root
curves stay in the clips and the Animator writes the model's own local rotation every frame. A whirl
written to the same transform would be overwritten or would overwrite, depending on script execution
order, and the failure mode is a spin that stutters or silently does nothing with a clean console.

**Fix.** `MiniBossFactory.WireAnimatedBody` inserts a dedicated empty `SpinRoot` between `LungeRoot`
and the model. Three transforms, three owners: `LungeRoot` = the base class's lean and lunge,
`SpinRoot` = the whirl, `Model` = the Animator.

**Invariant.** **One transform, one writer.** Before driving a transform from code, check whether an
`Animator`, a `NavMeshAgent` or a base-class coroutine already owns it — and if so, insert a parent
rather than sharing.

---

## A held guard that could produce a Perfect would have killed the timing game

**Symptom / avoided.** The obvious way to add a Sekiro guard is to make the stance a *state* that
`ParryController.Resolve` branches on: guarding? then evaluate leniently. Every version of that is the
same bug — the moment holding the button widens, extends or short-circuits the window, the correct play
becomes "hold RMB and press vaguely", and a game whose entire design is a 0.13 s window has no reason to
exist.

**Root cause.** The guard and the deflect are the same button, so it is very easy to write them as one
decision. They are not one decision. They are two rungs of a ladder and the ladder must stay monotone.

**Fix.** `ParryMath.Evaluate` gained a `guarding` argument that is read **last**. The windows are
evaluated exactly as before; only a result that already came out `Hit` is upgraded to `Blocked`. Nothing
about a hold can reach the Perfect branch. `ParryController.Resolve` passes `elapsed = float.MaxValue`
when the window is not open, which falls through the same path.

**Invariant.** **Timing first, stance second, and the stance may only ever upgrade a Hit.** Any future
defensive option (a second guard, a shield, a parry-ring item) is added the same way: after the windows,
never inside them. `FeatureTests > Guard_TimedPressStillPerfect` and `Guard_ConvertsHitToBlocked` are
the pair that pins it — breaking either one means the ladder has collapsed.

---

## Free posture regeneration turns a guard into an unloseable stalemate

**Symptom / avoided.** With chip damage at 0 (the Sekiro contract) a guarded hit costs *only* posture.
If posture keeps regenerating while the blade is up, the arithmetic of an ordinary grunt exchange is:
guarded hit +30 posture, ~1.3 s of enemy cooldown × 22/s regen = −29. The bar never fills. The player
takes no damage, deals no damage, and the fight runs forever.

**Root cause.** `PlayerPosture.Update` regenerated on a timer that knew nothing about defensive state.
Every posture cost in the game up to then came with damage attached, so the timer alone was enough.

**Fix.** `PlayerPosture.Update` returns before regenerating while `ParryController.IsGuarding`
(`guardPostureRegenMultiplier` 0, shipped from `DataFactory`). Turtling now fills the bar in three
20-damage hits and the break — 1.5 s at 0.4× speed, no parry, 1.6× damage — is reachable by standing
still, which is the point.

**Invariant.** **Any defensive option that costs only posture must also suspend posture regeneration
while it is active**, or it is free. If you add chip damage back, this can be relaxed to a multiplier
rather than a hard stop — but the two numbers are one decision, not two.

---

## A stance is on screen for seconds; a flick is on screen for 0.25 s

**Symptom / avoided.** `WeaponData.parry` sits at local x `0.05` — essentially on the crosshair — and
that is fine, because it exists for the length of the parry window. Reusing it as the held guard would
have parked a weapon and a gauntleted fist over the centre of the frame for as long as the player kept
the button down: precisely over the enemy they are guarding against, and over the cue flash that is the
only signal telling them when to deflect.

**Root cause.** The two poses look like the same pose ("blade up, across the body") and differ only in
how long they are held. Duration is the design constraint, and it is invisible in the pose data.

**Fix.** A separate `WeaponData.guard` pose, authored per weapon in `DataFactory` and held right of
centre (x `0.29`–`0.38`) and above idle. `WeaponViewmodel.PlayGuard/EndGuard` hold and release it, and
the stance survives an attack, a parry flick and a weapon swap — each re-enters it on the way out,
because dropping to idle when the parry window closed made a held guard flinch back to the hip every
quarter second.

**Invariant.** **A pose that is HELD must be authored separately from the momentary pose it resembles,
and must clear screen centre.** `FeatureTests > GuardPose_OffTheCrosshair_*` asserts `guard.pos.x >=
0.20` for every weapon in the loadout. This project has already paid for the other choice once — the
riposte that rendered as a black screen.

---

## Feedback for a hit that deals no damage has to be force, not light

**Symptom / avoided.** A guarded hit costs 0 health. With no flash, no shake and no shove it reads as
the attack having simply missed, and the player learns nothing about the posture they just spent.

**Root cause.** Every other outcome in `ReceiveAttack` announces itself with damage. The guard is the
first one that does not.

**Fix.** `PlayerCombat.GuardImpact`: `guardHitStop` 0.05, a `guardShove` of 1.2 m off the line
(grounded-only, no upward component — same rule as a clean hit, because launching the player off a 6 m
walkway over a pit turns every blow into a fall), nine dull grey-steel sparks struck half-way between
the blade tip and the incoming line, and `WeaponViewmodel.GuardImpact` kicking the stance back and
recovering it.

**Measured, after getting it wrong once.** The first kick was `(+0.05, -0.05, -0.10)` with a `-7 deg`
yaw. Pulling the blade 0.10 m toward the lens magnified it and the yaw swung the point *inward*, so at
the peak of the kick the tip crossed screen centre and sat on the enemy - during the one beat the player
most needs to see them. The kick now drives **down and out** with no yaw at all:
`(+0.07, -0.09, -0.02)`, `(+14, 0, -10) deg`. Shipped angles off the camera axis, measured on the real
viewmodel: idle `32.9 deg`, stance `9.4 deg`, **kick peak `18.5 deg`** - the shove moves the blade
*away* from the crosshair, never across it. `FeatureTests > GuardImpact_KicksAwayFromTheFight` fails if
that inequality ever inverts.

**Invariant.** **The guard borrows none of the deflect’s vocabulary.** No pale-steel screen flash, no
chromatic pulse, a duller spark colour and a shorter hitstop. Enemies no longer glow at all, so the
deflect is the only bright event in a fight; a guard that looked like one would make the two outcomes
indistinguishable at exactly the moment the player is deciding whether to keep holding or start pressing.

---

## Raising the guard pointed the blade forward first, because the press went through the flick

**Symptom.** Reported from play, not from a test: "it needs to not point forward first when trying to
guard, it needs to be one smooth motion into guard." Holding RMB read as *present the weapon, then
guard* - the blade shot out and came back before settling into the stance.

**Root cause.** Three separate things, all of which produce the same two-stage motion, and the first was
the one actually firing:

1. **A waypoint.** The guard and the parry are the same button, so a press always ran
   `PlayParry` first and only reached `PlayGuard` when the window closed 0.25 s later.
   `WeaponData.parry` sits at local x `0.05`, z `0.60` - inward and forward, almost on the crosshair.
   So the authored path was idle (right, back) -> flick (centre, FORWARD) -> guard (right, up). The
   forward excursion was not a bug in the blend; it was a pose on the path.
2. **Euler interpolation.** Interpolating `Vector3` euler angles between two perfectly good poses
   routinely swings the weapon through an orientation nobody authored, and "pointing forward" is a
   very common one.
3. **Blending from the authored idle rather than the live transform**, which teleports the weapon home
   before it starts travelling and reads as a hitch.

**Fix.** No waypoint: `StartParry` calls `PlayGuard()` directly when the button is down and only falls
back to the flick for a press made with the button already released. `GuardCo` / `GuardReleaseCo` blend
from `model.localPosition` / `model.localRotation` - the LIVE transform - with `Quaternion.Slerp` to the
target, so the rotation takes the shortest arc. And `AttackCo` retargets its **recovery leg** at
`data.guard` whenever `WeaponViewmodel.GuardWanted` is set, so coming out of a swing with the button
down travels from wherever the arc left the blade straight into the stance instead of landing in idle
and raising afterwards. Rise **0.08 s**, release 0.16 s.

**Invariant.** **A held pose is reached by ONE blend from the live transform, never via another pose.**
Three rules fall out of it, and all three are cheap to check: no intermediate pose on the path; slerp
the rotation rather than lerping eulers; start from where the transform actually is. And the test for
it is visual, not an assertion - film the entry and look for any frame where the weapon is further from
the body than *both* the start and the end pose. There should not be one.

**Note.** `WeaponViewmodel.GuardWanted` (the raw button) is deliberately separate from
`ParryController.IsGuarding` (the mechanical stance, which a swing suppresses). Swinging drops your
guard's *protection*; it must not interrupt the blade's *journey* back to the stance.

---

## An eased curve whose peak silently saturated, so half its Inspector range was decorative

**Symptom.** The brief was "make the spin way faster". The Pale Marionette's whirl speed is
`PuppetVisuals.spinPeakMultiple`, a `[Range(1, 4)]` field. Raising it from 2.5 toward 4 would have
changed nothing on screen, and nothing would have said so.

**Root cause.** The arrival curve was `w·(1-(1-k)³) + (1-w)·k` with `w = clamp01((peak - 1) / 2)`.
That reaches `w = 1` at `peak = 3`. Every authored value from 3 up to the slider's own maximum of 4
therefore produced a byte-identical curve — a quarter of the exposed range was inert. The clamp was
correct for the formula and the formula was correct for `peak ≤ 3`; nobody had checked that the
*slider* and the *maths* agreed about their limits.

**Fix.** A form where both endpoints are exact rather than emergent:
`f(k) = m(1-(1-k)^p) + tail·k`, with `m = 1 - tail` and `p = (peak - tail) / m`. That gives
`f(0)=0`, `f(1)=1`, `f'(0)=peak` and `f'(1)=tail`, so `spinPeakMultiple` and the new
`spinTailMultiple` mean literally what their names say, as multiples of the average rate, at any
value. It reduces **exactly** to the old cubic at the old values (both give `m=0.75, p=3`,
max divergence measured at 0.00e+00 over 501 samples), so the retune is a widening of the reachable
range and not a silent change of shape underneath the numbers that already shipped.

**Invariant.** **An exposed range must be a reachable range.** If a serialized field has a
`[Range]`, some test must show that the top of it differs from the middle of it.
`PuppetSpinTests.Ease_ActuallyRespondsAboveThreeX` compares the measured slope at peak 3 against
peak 4.5 and asserts the top of the slider is live.

**And note how it is asserted.** Every test in that file measures the curve — numeric derivatives,
monotonicity, endpoint values — and never asserts the value that was passed in. Asserting the input
would have passed on the broken version, which is the same failure that cost this project eleven
wind-up poses: *an authored angle is not an on-screen angle*, and an authored peak is not an
on-screen peak.

---

## The whirl now has a frame-rate guard, and it clamps the PEAK rather than the phase

**Symptom / risk.** At `peak 4.5` the body's fastest instant is 3306 °/s — 9.2 revolutions a second.
That is 55° per frame at 60 fps and **110° per frame at 30**. Rotation only reads as rotation while
the per-frame step stays under about half the silhouette's rotational symmetry period; a humanoid with
its arms out is roughly 2-fold symmetric, so past ~90° a frame the spin stops looking like a spin and
starts looking like random orientation. The fight's whole readability rests on the body arriving, so
on a slow machine the headline feature would have destroyed the thing it exists to serve.

**Fix.** `PuppetVisuals.ResolvePeak` lowers `spinPeakMultiple` against the *measured* frame time so the
step never exceeds `maxDegPerFrame` (75°). Resolved **once per pass**, not per frame — re-solving every
frame would let a single frame-time spike bend the arrival curve mid-revolution. The frame time is
smoothed off `Time.unscaledDeltaTime`, so a hitstop is not mistaken for a stutter.

**The part worth remembering: it clamps the peak, never the phase.** Clamping the phase is the obvious
implementation and it is wrong. The phase is re-derived every beat precisely so the body is square-on
to the player at the impact instant; a clamped phase would lag its own schedule and arrive late,
destroying the one invariant the whirl has. Lowering the peak keeps the arrival exact and spends the
frame budget by making the turn more uniform — which is the right thing to lose, because on a machine
that cannot render the blur, the blur was never going to be seen.

**Invariant.** **A performance guard may degrade how something looks, never when it happens.**
Anything that anchors to a gameplay instant is off limits to a frame-rate adaptation.

---

## The fastest legal parry cadence is 0.69 s, and it is arithmetic rather than taste

**Symptom.** "Make the parry more frequent" has a hard ceiling, and it is worth writing down so the
next session does not go looking for room that is not there.

**The constraint.** The cue fires `cueLead` (0.28 s) before impact, and impact is
`windup + impactDelay`. A wind-up under ~0.24 s would need its cue to fire *before the wind-up began*,
at which point the attack is not parryable at all — and the project floor is 0.45 s, which leaves a
real charge phase on top of that (0.21 s of wind-up before the cue lands). The mid-combo beat is
`windup + gap + impactDelay + strike`, `gap` floors at 0.10 in `NextGap`, and a strike shorter than
~0.10 s stops reading as a blow. So **0.45 + 0.10 + 0.04 + 0.10 = 0.69 s** is the floor, and the
Marionette now sits exactly on it.

**The only remaining lever is global.** `parryPerfectWindow` (0.13) and `parryLateWindow` (0.12) live
on `PlayerStatsData` and apply to **every enemy in the game**. There is no per-attack or per-enemy
window multiplier, and adding one is a real design decision, not a tuning change — it would mean the
player's most fundamental timing is no longer one learnable quantity.

**Invariant.** **`MarionetteDataTests.TheBeatSitsExactlyOnTheParryContractsFloor` is deliberately
brittle.** It asserts the wind-up *equals* 0.45, not that it is above it. If a playtest says the
cadence is too fast to hold for nine passes, the fix is to raise the wind-up and change that test and
the `DataFactory` comment together — deliberately, knowing the floor has been left. What must never
happen is the beat drifting back up unnoticed.

**Second invariant, and the one that actually breaks things.** The parried and unparried beats must be
equal, which reduces to
`parryRecoilSeconds == strikeDuration / lerp(1, 0.55, aggression)`. Three separate fields, in two
different assets, and no compiler will ever connect them. Change any one and re-run the division.

---

## The alias guard fixed orientation aliasing and CREATED an area flicker at 30 fps

**Symptom.** Found by photographing the whirl (`SpinFilm`), not by reasoning about it. At 60 fps the
Marionette's arrival is beautifully smooth — the silhouette grows 73% → 98% of its widest over the 18
frames after the cue, with a worst frame-to-frame area change of **×1.11** and a mean of **×1.02**. At
30 fps the same arrival measures **×1.60**, and the frames show why: the body's silhouette collapses to
**38%** of its widest exactly at the cue.

**Root cause, and it is a hole in my own reasoning.** `maxDegPerFrame` bounds ORIENTATION aliasing: past
~90° per frame a 2-fold-symmetric shape stops reading as rotation. That argument silently assumes the
silhouette is roughly the same size at every yaw. This body is strongly **anisotropic** — wide from the
front and back, close to a vertical sliver edge-on — so its on-screen AREA collapses twice per
revolution, and the failure mode at speed is *flicker*, which begins well below 90°/frame. Worse, the
guard *caused* the 30 fps case: by flattening the curve it moved the edge-on instant later, out of the
blur (where nobody is meant to be reading) and onto the cue (where everybody is).

At 60 fps both edge-on instants (yaw ~265° and ~96°, areas 42% and 44%) fall **before** the cue, so the
player's read window contains only the body opening up. That is not luck so much as it is untested —
nothing pins it, and a future change to the peak, the tail or the pass length could walk the sliver into
the cue at 60 fps too.

**Status: documented, not fixed.** 60 fps is the target and the project has enormous headroom (main
thread measured at 1.18 ms). The honest fix is not obvious — one revolution per pass means the body
*must* pass edge-on twice, so guaranteeing where that lands would mean anchoring the arc to the cue
instead of to the impact, and the impact anchor is the invariant that makes the cadence undriftable.
Not worth trading for a 30 fps case until someone has actually played at 30 fps.

**Invariant.** **A per-frame ANGULAR bound does not bound per-frame SHAPE.** For any anisotropic
silhouette, measure the rendered area as well as the rotation, and measure it *across the window the
player has to read* rather than across the whole motion — the two give opposite verdicts here, and only
the second one matters. `SpinFilm` reports both.

**And the general lesson, again.** The degrees-per-frame bound was principled, arithmetic-clean, and
passed every test I wrote for it. It took rendering thirty-one frames and counting pixels to find that it
measured the wrong quantity. This is the third time in this project that photographing the result
contradicted a confident derivation about it.

---

## Emission on an enemy was already spoken for, so the fire had to be a floor and not a lamp

**Symptom / risk.** The brief was "make these types of enemies glow and emanate fire/energy". The
obvious implementation — push emission onto the body renderers — collides head-on with a rule
`EnemyVisuals` states in its own header: *light on an enemy means "you deflected", never "an attack
is happening"*. `WriteBody` is documented as the single writer of `_BaseColor` and `_EmissionColor`
precisely so that a deflect is the one and only moment an enemy emits light. A permanently glowing enemy
does not just add a look; it destroys the meaning of the loudest reward signal in the game.

**Resolution: a FLOOR and a SPIKE are different things, and can coexist.** `EmberAura` never writes a
property block. It calls `EnemyVisuals.SetAura(colour, amount)` and the existing single writer folds it
in — the same arrangement `WeaponEmber` already has with `EnergyGlow` on the player's blade. Two
properties make it safe: the aura ships far under the parry glow (0.22 against 3.2), and it is passed
through the same `chargeDark` as everything else. That second one turned out to be the good part rather
than the concession: **a body on fire now visibly INHALES on a wind-up**, the fire drawn in as it charges
and flooding back on the strike. The aura reinforces the telegraph instead of washing it out.

**Invariant.** **Before adding a channel to an enemy, find out what that channel already MEANS.** The
readability language here is small and each part of it is load-bearing; a new effect that borrows an
occupied channel is not additive, it is a redefinition. Ask the owner for a slot instead.

---

## The forge rig's bones and its silhouette disagree by three quarters of a metre

**Symptom.** The Ember Revenant's mesh spans y −0.17 to 1.96 — a two-metre figure. Its **head bone is at
y 1.20**, and the entire skeleton fits between 0.96 (Hips) and 1.20 (Head). The top 0.76 m is shoulder
spikes and hood with no bones in it at all.

**Why that is dangerous rather than merely odd.** Every previous forge model had a skeleton that roughly
filled its silhouette, so `ModelSpec` pivots were sanity-checked against the bounds without anyone
noticing they were doing it. On this body, the deathblow glyph placed at the documented "0.74 of height"
lands at y 1.41 — **three tenths of a metre above the head bone**, floating in the hood, marking nothing.
The eye would have gone the same way. Neither failure produces an error; they produce an enemy that is
subtly, silently wrong to look at.

**Fix, and the tool that should have existed already.** `VibeGame1/Probe Forge Models`
(`Editor/ForgeModelProbe.cs`) reports mesh bounds, the full bone list, per-clip hand separation, and a
suggested `ModelSpec` derived **from the bones**. `docs/AUTHORING.md` has said "pick the clip by
measuring, not by name" since the Marionette — whose spin clip was chosen exactly that way, by sampling
arm span and discovering that the obviously-named `AttackSwing` tucks the arms to 0.76 m while `Roar`
holds 2.0 m throughout. But that measurement was made ad hoc through the MCP bridge and never became a
tool, so the next model was always going to skip it.

**Invariant.** **A pivot comes from the SKELETON, never from the bounding box.** The two agree only on
bodies without large unrigged decoration, and nothing warns you when they stop agreeing.

---

## Unity's first `cam.Render()` in batch mode is a lie

**Symptom.** The first headless portrait of the Ember Revenant came back as a **flat orange silhouette**
against a dark ground — exactly what a broken material looks like. Every subsequent frame in the same
run was correct: a dark charcoal body with a small glowing eye.

**Root cause.** In `-batchmode`, the first `Camera.Render()` returns before the render pipeline has
finished setting itself up, and produces a frame with wrong lighting and wrong colour. It is not a
material bug, an asset bug or a pipeline misconfiguration; it is a warm-up artefact.

**Fix.** `EnemyPortrait` renders once into the void before believing anything. Worth propagating: the
earlier `SpinFilm` captures used to judge the Marionette's whirl had the same flaw, so their first frames
were suspect too.

**Invariant.** **Discard the first rendered frame of any headless capture.** And more generally — the
reason this one was caught rather than acted on — when a capture disagrees with everything else you know
about the asset, suspect the CAMERA before the asset.

---

## Generated assets live in git, so a scratch-copy workflow has to copy them back

**Symptom.** Verification this session ran in a throwaway copy of the project (`robocopy` of `Assets` +
`Packages` + `ProjectSettings`, Unity rebuilds its own `Library`), because the MCP bridge was down and the
editor was busy. Three separate failures came out of that arrangement, all the same shape:

1. **`/PURGE` deletes what only exists in the copy.** Data assets generated by `DataFactory` in the scratch
   project were wiped by the next sync, and the tests that read them failed claiming the enemy did not
   exist — which was true, of the copy, and false of the work.
2. **A new script gets a DIFFERENT `.meta` GUID in each project.** A prefab built in the scratch copy
   referenced the scratch GUID for `EmberAura`. Copied back, that is a **missing script**: present in the
   YAML, values intact, `GetComponent` returns null at runtime, nothing in the console.
3. **The clip split lives in the FBX's `.meta`.** `4a. Split Forge Animation Clips` writes
   `ModelImporter.clipAnimations`; a `/PURGE` sync restored the unsplit meta and the animator silently
   built from a model with no named clips.

**The rule that resolves all three.** `Assets/Data/**`, `Assets/Prefabs/**`, `Assets/Animation/**` and the
FBX `.meta`s are **committed artefacts**, not build output. A scratch copy is a place to RUN the
generators, and its output has to be copied back into the real project — with its `.meta`, so GUIDs
travel with it — before the next sync destroys it.

**Invariant.** **A test that reads a shipped asset is worth more than a screenshot of it.**
`RevenantDataTests.ThePrefabActuallyCarriesTheAura` resolves the component TYPE rather than grepping the
YAML, which is the only check that catches a missing script — a text diff of the prefab looks perfect in
exactly that case, and the runtime failure is silent.

---

## A deflect was missing weight, not clarity — and gap 3.4 was two questions, not one

**Symptom.** The parry was "pretty good": hitstop, shake, flash, chromatic pulse, sparks, an enemy
emission spike, audio. Everything needed to *read* the deflect was there. What was absent was the sense
of being hit by something. The instinct in that situation is to turn up the light, and it is the wrong
instinct here: `EnemyVisuals` documents that `CueFlash` must stay the loudest event in the frame, and
light on an enemy means "you deflected", never "an attack is happening". Every lever added is therefore
**force** — rotation, translation, FOV, time, spectral width — and not one of them brightens a pixel.

**Root cause 1: the camera shake was omnidirectional by construction.** `CameraShake` was pure Perlin
noise, which can tell you that something happened and can never tell you what. It now carries two
channels that sum: `Add` (noise, "something happened") and `Kick` (an authored *directional* impulse,
"something hit you, from there"). The kick's envelope, `ParryImpulse.KickCurve`, eases out on the way and
falls off quadratically on the return — at the shipped 0.16 s life, **82% of the displacement lands
inside the first frame**. That is the load-bearing number: a kick that arrives over three frames is a
camera drift and reads as a bug. The amplitudes are small on purpose (1.6° pitch, ±1.1° yaw, ±1.3° roll,
3.5 cm) because ShakeRoot sits between the look pivot and the lens and *is* the aim source; roll is the
largest component precisely because roll cannot move the aim vector.

**Root cause 2, and the real answer to gap 3.4: hitstop is two questions.** `ANIMATION-VFX.md` asked
whether to replace the binary freeze with a curve and correctly suspected the crisp version was doing a
job. It is — but only at one end. A ramp *into* the freeze removes the single frame the eye can point at
and call the hit, which is what this game's whole deflect is built on, so **the onset stays binary**, and
a test samples every quarter-frame from contact to 0.090 s to keep it that way. The waste was at the
other end: snapping from 0.02 straight back to 1.00 discarded the moment in one frame. The release is now
a two-step staircase (0.45 for ~2.3 frames, then 0.725 for ~1.9), issued as two extra
`TimeScaleController` requests that are invisible under the freeze because overlapping requests resolve
to the *smallest* scale — which is why this needed no edit to `PlayerCombat` or to the controller.

**Invariant.** **Gap 3.4 is settled asymmetrically: do not ramp the onset.** Setting
`parryHitStopRelease = 0` restores the old behaviour bit-exactly, and a test proves that escape hatch
works.

**The budget that makes it safe.** The player runs at 1.0 throughout (rule 1), so the added slow costs
**29.8 ms of world time** and shifts *everything* — the enemy, the swing, the next parry cue — by the
same 29.8 ms. No relative timing changes, and the 0.13 s perfect window is untouched. `ParryImpactTests`
holds the whole freeze under one perfect window and the release tail to 15–40 ms, and one test
independently replays the min-wins overlap resolution to prove the modelled staircase and the shipped
staircase are the same object. **Invariant: if you add a hitstop layer, add its cost to that budget
test.**

**The cheapest cue in the package was already written.** `WeaponViewmodel.GuardImpact` runs on
`PlayerDelta`, so during a deflect's freeze the blade is the one thing in the world still moving. It was
only ever wired to the held guard; the viewmodel did nothing at all on a Perfect. It must be called
**after** `EnterRecovery()`, because `EndParry` re-asserts the stance and re-raising the guard on top of
a kickback eats it entirely.

---

## The Pyre discharge: what a single beam cannot say, and what a capture said in one frame

`SlashFx.Beam` was built to answer "where did that come from", and it does — but a beam is a *tube*, and
a tube is a laser sight. The Pyre needed something that reads as CHARGE BEING DELIVERED, which is three
separate claims: it has an author (it starts at the weapon), it has a destination (it stops inside the
body, not near it), and it has *substance* crossing the gap. `LightningEffect.Bundle` and `PyreMist`
split those: five braided jagged channels give the moment its spine and direction, and a misty flow
seeded along the same channel gives it volume and duration. Neither works alone — bolts alone are a
flashbulb, mist alone has no author.

**Invariant: any effect that is supposed to read as one object acting on another must terminate EXACTLY
on both endpoints.** `FillStrand` writes `points[0] = from` and `points[n-1] = to` literally rather than
trusting the lerp, and tapers lateral deviation by `sin(k·π)` so nothing is clipped at the ends; a channel
that merely passes near the wand tip and near the chest reads as an unrelated spark, and at 60 fps that
failure is invisible.

**A burst emitted from the source is a clump, not a flow.** The first `PyreMist` fired all its motes out
of a cone at the weapon tip. Photographed at mid-flight that is a *ball* with a gap on either side of it
travelling up the span — the exact opposite of a stream. The fix is arithmetic, not tuning: each mote is
seeded at its own fraction `k` of the way along and given lifetime `Flight·(1-k)`, so the whole channel is
populated on frame one, every mote still arrives at the victim, and the far end empties first — the flow
visibly drains INTO the body. **Invariant: a directed particle stream is defined by where its particles
START, not by how fast they are thrown.**

**HDR content colour plus additive blending equals white, and this project ships everything at
2.4–2.6×.** The first capture of the bundle was five identical *pure white* wires; the Stormneedle's blue
was nowhere on screen, because every channel of `#7FD4FF × 2.6` clips. `LightningEffect.Strike` has had
this property since it was written and nobody noticed, because a storm being white is fine. A five-strand
rope being white is not — the braid is invisible. `FringeOf(c) = c × 0.34` is the same core-plus-fringe
trick `SlashFx` applies to every primitive, promoted to bundle mode: the spine keeps the hot HDR value
and blows out the bloom, the braid keeps the *hue* at an intensity that does not clip. **Invariant: in an
additive effect with more than one element, at most ONE of them may be drawn at full HDR intensity — the
rest carry the colour.**

**And a real build bug found by inspection, now fixed.** `ProjectSetup.EnsureAlwaysIncludedShader`
registered only `Universal Render Pipeline/Unlit` and `Sprites/Default`. `DeathMist` and `PyreMist`
resolve `Universal Render Pipeline/Particles/Unlit` by `Shader.Find` at RUNTIME, and no asset references
it — so a player build strips it. The failure mode is not magenta: a `ParticleSystemRenderer` whose
material has a null shader draws **nothing**, silently. The death dissolve and the Pyre mist would have
been absent from a shipped build while looking perfect in the editor. **Invariant: any shader reached
only by `Shader.Find` must be in Always Included Shaders, and nothing in this project builds a player, so
no test will ever catch the next one.**

---

## A jump arc is geometry, not a playtest

**Symptom.** BACKLOG §5b specified a buttress fin for The Ascent down to the centimetre and could not
ship it. The blocking sentence was: "`CheckHop` measures box-to-box gaps and would *not* catch an
obstruction in the middle of the arc, so this needs a real play test." The Ascent is a 20 m tower and the
obvious home for wall jumping, and it had no authored line for want of one jump nobody had time to try.

**Root cause.** `FeatureTests.CheckHop` compares the closest XZ distance between two `Renderer.bounds`
against a reachability envelope. That is a *necessary* condition and it is blind to everything between
the two boxes: a pillar standing halfway along a 4 m hop measures as zero gap and passes. But the level
is a `LevelDefinition` — a list of axis-aligned boxes with known centres and sizes — and the player's
flight is ballistic with constants that live on `Player.prefab`. Whether an arc is obstructed was never a
question that needed a human. It was a question nobody had written down as arithmetic.

**Fix.** `Assets/Editor/LevelArcAnalyzer.cs`: pure, static, scene-free. `SweepArc` integrates the real
trajectory at 2 ms steps — ≤4.5 cm of travel at the motor's top speed, so a 0.6 m fin cannot be tunnelled
— and measures the exact distance from the capsule's spine to every box using the closed form for a
vertical segment against an AABB. `AnalyzeHop` fans 1800 arcs across take-off points, aim points, speeds
and held/cut jumps, and reports how many arrive clean, from how many distinct take-off spots, and what
the nearest obstruction was. `ClimbChimney` mirrors the motor's wall-jump rules — `wallCheckDistance`,
`sameWallCosineLimit`, `maxWallJumps`, the preserved along-wall momentum. **Every constant is READ off
the shipped `Player.prefab`;** a movement number duplicated into an analyser is a number that will
silently stop being true.

**Four things it caught that arithmetic alone did not.** (1) A uniform take-off grid over the 26 m
`T1_Arena` puts four samples 8 m apart and misses the 6 m doorway into The Ascent entirely, reporting a
*walk* as impossible — take-off points must be sampled in the band facing the target, which is also where
a player actually stands. (2) The motor's wall scan falls back to `transform.forward` when velocity is
negligible, and it must: pressed against a chimney wall the `CharacterController` has already zeroed
horizontal velocity, so a scan requiring velocity finds nothing and every chimney stalls two pushes up.
(3) A search that returns as soon as the wall jumps are spent throws away the final unpowered segment —
which is the segment that decides where you land, i.e. every route worth having. (4) A fin flush with a
ledge's face **walls the slot off everywhere it stands**: the chimney's mouth is past the fin's END, so
sampling entries "inside the slot" reports a climbable chimney as unenterable.

**And one that only a picture could catch.** `LevelRouteShots` rebuilds the level and photographs it from
the player's real eye. Its first run silently showed the OLD scene: `EditorSceneManager.OpenScene` in
Single mode unloads unused assets, and a `LevelDefinition` held only by a local is "unused" — the
reference survives as Unity's fake-null and `LevelDefinitionBuilder` refuses with "Null LevelDefinition"
in the log while the frames come back looking perfectly plausible. **Load the asset AFTER opening the
scene**, and read the builder's own success line before believing a frame.

**Invariant.** Any geometry added to `Level_01_Level.asset` must keep every hop in
`LevelArcReport.BaseRoute` clean from at least three distinct take-off points, and
`LevelArcClearanceTests` is what says so. `CheckHop` still measures the envelope; this measures the space
in between. Neither replaces a human: they prove a line EXISTS, never that it feels good.

---

## The alert tell is already the size of the deathblow mark — bloom decides that, not geometry

The backlog said the tell's next lever was the 0.25 m cube's size: "past peak 3.0 you only buy white". A
pre-render derivation agreed — 0.25 m at the grunt's 3 m `preferredRange` subtends 0.080 rad against
`DeathblowMarker`'s 0.115 rad `angularSize`, so the tell looked like 0.69× the mark linearly and half its
area. **The rendered frame says otherwise.** Photographing the shipped grunt through `SampleSceneProfile`
at 1920×1080 / FOV 95 and diffing tell-on against tell-off, the 0.25 m cube covers a 47 × 60 px bbox at
3 m — 5.6% of frame height, matching the mark's 57 px. The bloom halo, which the derivation ignored, is
most of the tell's on-screen area and closes the gap on its own. Emission peak 3.00 does not just make it
whiter; it makes it *bigger*.

The measurement also found the real failure mode, which is framing, not size. The tell rides 2.5 m up
while the camera sits at 1.6 m looking down at the torso, so it climbs toward the top of the frame as the
enemy closes. **At 1.5 m it is clipped by the top edge** — gap 0 px, and zero pixels above 90% peak,
meaning the saturated core is off-screen and only the skirt of the halo is left. Growing the cube makes
that arrive sooner: 0.25 → 0.40 drops the gap at 2 m from 125 px to 83 px. And the airspace is already
taken — the world posture bar sits at 2.6 and a 0.25 cube's top edge is at 2.625.

**Invariant: the tell's size is not the lever, and the numbers behind that live in
`Assets/Editor/Tests/AlertTellFramingTests.cs`.** If it ever needs more presence, the lever is angular
sizing (as `DeathblowMarker` uses, so it stops shrinking with distance — it is 22 × 26 px at 6 m) or
lowering it, and either needs a play test. **Third time this project has had a confident derivation
contradicted by the rendered frame: measure, then photograph.**

---

## The level's light count was never the cost — the per-object limit is

"42 point lights, no performance measurement taken" was two things wrong at once. There are **55**
additional point lights (43 with `FlickerLight`), and the count was never the interesting number.
`VibeGame1/Audit Level Lights` (`Assets/Editor/LightAudit.cs`) measures it statically over 68
camera-height probe points at the spawn, the checkpoints, the spawners and the torches:

- **Zero additional lights cast shadows.** The one shadow caster in the scene is the directional key
  light, so the most expensive kind of light cost is entirely absent.
- The torches are 6.00 m apart at their closest with a 9 m range, so they barely overlap: **1.6 lights on
  average actually reach a probe** (worst 6) against a URP `AdditionalLightsPerObjectLimit` of 4, and
  **only 1 probe of 68 exceeds that limit**. Almost nothing is being culled having contributed nothing.
- `FlickerLight`'s 42 m cull leaves **21.1 of 55 enabled** on average (worst 24, Checkpoint_4), so it
  drops ~62%. What survives is gather-and-sort work plus 43 `Update()`s strided by 2, not shading.

**Invariant: the number to watch when adding lights is the count of lights REACHING one point, not the
total.** Adding torches stays cheap until a cluster pushes past 4 reaching lights. No frame cost is
claimed here — `PerfProbe` needs play mode and this was measured statically.

---

## On an imported body the wind-up pose has only one visible channel

**Symptom.** THE EMBER REVENANT shipped with four deliberately different attacks — a 95° cut, a 40°
thrust, the game's biggest 0.95 s punish, and an unblockable kick — and no authored `WindupPose`, so all
four rode the cone-derived fallback. Photographed from the player's eye at 3.40 m, the four wind-up peaks
measured **IoU 1.00 against each other**: not similar, pixel-identical. Four attacks, one picture.

**Root cause, and it is not the fallback's fault.** The fallback varies only `armWindup` and holds one
generic body channel for every attack. On the primitives that is fine — the arm carries a blade. On an
IMPORTED forge model it carries nothing: `MiniBossFactory.BuildModelBody` deliberately builds EMPTY arm
pivots (the auto-rig's bones sit inside the silhouette, so driving them at a ±136° telegraph tears the
mesh) and `EnemyVisuals.weapon` is a 3 cm spark marker, not a blade. **So the fallback's only varying
channel is invisible and its only visible channel is constant.**

**Fix.** Author all four poses into `bodyOffset` / `bodyEuler`, which move the whole skinned mesh through
`LungeRoot`, and give each attack one channel of its own — measured, never derived:

| | measured at 3.40 m, at the frozen cue peak |
|---|---|
| `Revenant_Slash` | width **0.58** body-heights against the resting 0.90, level, area 0.74 — the only pose *narrower* than the body stands. A wide cut is wound from a coil. |
| `Revenant_Stab` | height **1.19**, area **1.10**, crown **0.00** — the biggest and nearest shape, growing without rising. |
| `Revenant_Overhead` | centre **+0.19**, crown **+0.16** — the only wind-up in the moveset that goes UP. |
| `Revenant_Kick` | axis **27°** off vertical — the only tilted body in the game — and the lowest (−0.21 / −0.23). |

Worst pair now IoU 0.43; the unblockable's worst is 0.32. Two smaller things paid for on the way: the
overhead first measured *smaller* than the slash it towers over (pitching a body back foreshortens it
exactly as it foreshortens a blade — `Heavy_Overhead`'s failure in a new costume), and a pure roll on the
kick read as toppling rather than loading a leg until 16° of yaw put a hip behind the lean.

**Invariant.** **Before authoring a pose for an imported body, find out which channel is visible on it.**
`armWindup` drives nothing the player sees on any forge model. And measure the WHOLE BODY, not the blade
— `FeatureTests.MeasureWindup` measures `EnemyVisuals.weapon`, which on these bodies is a 3 cm cube.
`Assets/Editor/PoseSilhouette.cs` rasterises the real geometry through the player's real camera with no
GPU and no play mode, so the same measurement backs both the capture tool and the EditMode regression —
which fails **6 of its 9 cases** the moment the poses are unauthored.

**Three capture traps, all found the hard way.** `EditorSceneManager.NewScene` runs an unused-asset sweep
that destroys `ScriptableObject`s nothing in a scene references — a moveset's attacks held only by a local
`List` die under it, so **open the scene first**. `EnemyVisuals.Setup` **throws** in edit mode (it
dereferences the `EmissiveFlash` and property blocks cached in `Awake`), so paint the property block by
hand. And **film from +Z**: the forge models face +Z and a camera on −Z photographs the BACK, silently
inverting every forward/back reading — a thrust that drives at you measures as a retreat.

---

## Two of the four attack animations were unreachable, and nothing warned

**Symptom.** Found while measuring the poses above, not by reading the code. `EmberRevenant.fbx` ships
`AttackSwing`, `AttackOverhead`, `AttackStab` and `AttackKick`; all four import, split, and appear as
states in the generated animator. **Two of them never played.**

**Root cause.** `PuppetVisuals.PlayAttackClip` had exactly two attack slots:

```csharp
string clip = spin ? clipSpin : (atk.unblockable || atk.windup >= 0.9f) ? clipHeavy : clipAttack;
```

So the slash and the thrust both played `AttackSwing`, and the overhead and the kick both played
`AttackOverhead` — the kick because it is the moveset's *unblockable*, which the first branch catches. The
failure is completely silent: every clip exists, every state is valid, and a perfectly reasonable clip
plays. Nothing is null and nothing logs.

**It also made a comment in `DataFactory` false.** `Revenant_Stab`'s cone was justified in writing by a
real measurement — `AttackStab` is the only clip in the set whose hands CLOSE (0.81 / 0.19 / 0.24 m of
separation) — while that clip could not run. **A measured premise does not make a claim true if the thing
measured is not the thing running.** The comment is annotated rather than deleted, as a marker.

**Fix.** `PuppetVisuals.ClipFor` resolves the stab and the kick **by asset-name suffix, before the
unblockable heuristic**, and each carries its own baked contact anchor and length (stab 0.92 s, kick
1.08 s, against the swing's 1.17 s) so the clip is stretched onto its own contact frame rather than
another clip's. Matching on the name follows the `spinAttackPrefix` precedent instead of adding a field to
`EnemyAttackData`: the forge exports the same four canonical attack clips for every model, so the mapping
is a property of the PIPELINE and belongs with the other clip bindings, not on 25+ content assets.

**Invariant.** **Order the clip selection by name before heuristics.** An unblockable-first test will
always swallow the kick, and `RevenantDataTests.TheStabAndTheKickPlayTheirOwnClips` exists so a future
simplification cannot quietly restore it. More generally: when a lookup has fewer slots than the content
has cases, the overflow does not error — it aliases onto a neighbour and looks fine.

---

## The slide was fed, not silent — its middle was empty, and the asset was lying about all of it

*"The slide and the dash don't have visual feedback or feel."* Neither was unfed. The dash shipped an 8°
FOV kick, a chromatic pulse and a whoosh; the slide shipped a 6° kick, a 0.55 m eye drop and the same
whoosh pitched down. Two different problems hid under one complaint.

**The dash was non-specific.** A symmetric FOV widen says "the lens changed"; it cannot say which way you
went or how far, and 0.16 s is too short for the world to sell it. Fix: a DIRECTIONAL camera kick
(`DashImpulse.FromDash` — lens left 0.06 m *opposite* the travel and catching up, 0.9° pitch scaled by
the forward component, 1.4° roll banking into the lateral one, **never yaw**, because a dash is usually
the approach to a swing and a yaw kick drags the reticle off the target for exactly the 0.14 s you are
lining it up) plus 12 camera-space speed lines that flow radially for a lunge and sideways for a strafe.
World-space streaks were rejected for the reason `WeaponTrail` already found: turn the mouse and the
ribbon stays in the world.

**The slide's middle was empty.** Every cue except the eye drop was an *impulse*: `CameraFX.FovKick` has a
0.18 s time constant and a full-speed slide lives ~0.5 s. For most of the move you were 0.9 m off the
floor at 20 m/s and nothing said so — and `OnSlideEnded` was raised by the motor and **subscribed by
nobody**. Fix: every sustained channel is a function of the SPEED BEING CARRIED, not of time
(`SlideImpulse.SpeedFraction` maps the motor's 8→22 m/s band to 0→1). FOV is HELD (`CameraFX.FovHold`, a
new channel summed with the kick) at `slideFovHold × speedFraction`, built to be **exactly zero at
`slideEndSpeed`** so stand-up cannot snap the lens. Roll is HELD (`CameraShake.SetRoll`, a third channel)
banking into the steer. Grit is shed into a scene-level root so it is left behind, and a synthesised
scrape loop rides gain *and pitch* on the same fraction — pitch is what the ear reads as speed on
broadband noise.

**Rule 9 bit anyway, and in a new way.** `DataFactory` wrote all 17 new tunables, but `Create Data` was
never re-run, so the on-disk `GameFeel.asset` had none of them and every value ran off code defaults.
Worse: the test asserting "the asset carries the shipped values" **passed**, because a YAML field that is
missing deserialises to the field initialiser — which equalled the factory value. **An asset-value test
only proves the asset once the asset has actually been regenerated. After `Create Data`, grep the YAML for
the new key; do not trust the green.**

**Invariants.** `FovHold` and `SetRoll` each have exactly ONE writer (`SlideFx.Tick`, every frame, 0 when
not sliding) — a second holder will fight it. Every held channel eases and snaps to exactly zero when
released, because ShakeRoot sits between the look pivot and the lens. Nothing in either package brightens
the frame: streaks 0.90, grit 0.55, sparks ≤ 1.0, all under the 1.05 bloom threshold — `CueFlash` still
owns light. `SlideImpulseTests` runs the motor's real decay law (`hv *= 1 − 2·dt`) closed-loop at 20, 60
and 240 fps and asserts the picture at any given speed is identical across all three.

---

## Settings menu: one panel, two entry points, an applier instead of a menu that reaches into gameplay

**Shape.** `SettingsData` (POCO, clamps, labels) → `SettingsStore` (PlayerPrefs, `Changed` event) →
`SettingsApplier` (self-bootstrapping `DontDestroyOnLoad`, pushes onto `PlayerLook`, `CameraFX`,
`QualitySettings`, `Screen`, the URP volume) ← `SettingsMenu` (edits `SettingsStore.Current`, saves on every
notch). `SettingsPanelKit` in `HudBuilder.cs` is the ONE emitter of the panel, used by both `HudBuilder`
(pause path) and `MainMenuBuilder` (title path); a test asserts the two prefabs match row for row.
Sensitivity is written onto `PlayerLook`'s public fields from the applier — `PlayerLook.cs` is never edited
by settings code, and that is the intended architecture, not a workaround.

**Gotchas, each now an invariant:**
- `PausePressed` is `WasPressedThisFrame` — true for the WHOLE frame. Re-enabling `PauseMenu` inside
  `SettingsMenu.Close()` let one ESC close settings AND toggle pause, depending on script order. The
  re-enable is deferred one frame in `SettingsMenu.Update`.
- `CameraFX.Start` captures `cam.fieldOfView` into `baseFov`, so an FOV applied on `sceneLoaded` is
  overwritten a frame later. The applier applies on load AND once more via `Invoke(…, 0f)`.
- `QualitySettings.SetQualityLevel` loads that level's own vSync; set `vSyncCount` AFTER it.
- `Application.targetFrameRate` is ignored while vsync is on. The row greys out and says so rather than
  offering a dead control.
- Bloom goes through `Volume.profile` (runtime clone), never `sharedProfile` — writing the shared profile
  from play mode dirties the asset on disk. Authored intensity is cached once per profile instance and the
  setting is authored × scale, so re-applying never compounds and a re-authored profile moves the
  player's 100 % with it.
- `Screen.SetResolution` is a no-op in the editor (the Game view owns it). Skipped there, logged once,
  the row says "applies in a build".
- `Mathf.Clamp` passes NaN/∞ through. A NaN sensitivity in prefs blanks the view; `Clamp()` sanitises.
- Tests swap `SettingsStore.KeyPrefix` to `vg1.test.settings.` so the suite can never overwrite the
  developer's own sensitivity.
- A UGUI `Slider` drives its fill by anchors, not `fillAmount` — that is why the sliders are stock
  Sliders, and the prefab test asserts the fill Image is not `Type.Filled` (rule 5).

---

## A guard pose cannot be eyeballed — the maul's head sat on the crosshair twice

Restoring the weapon archetypes ("more than just daggers") kept the three properties the dagger pass
actually earned — held poses never cross the crosshair, never cover more than a corner of the frame, tip
stays in frame — and enforced them by rasterising the real prefab through the player's own camera
(`WeaponSilhouette`, asserted in `WeaponSilhouetteTests`). **Length was never what broke the frame; POSE
was**, so length is free again: 0.32 m needle → 0.62 m sword → 0.71 m maul, with the dagger untouched as
the control (zero diff after a full regeneration).

Two authored guards that read fine in the mind's eye measured **16.1% and 6.3% crosshair-disc coverage**
on screen, and a hand-tuned "fix" made one of them WORSE — where a weapon's mass lands on a canted line
depends on grip height, extent and lens distance in ways intuition does not track. The invariant: **a held
pose ships only with a measurement, and iterating one pose per Unity launch is the wrong loop** —
`WeaponGuardSweep` mutates the loaded asset in memory and measures hundreds of candidates in one batch.
The sweep also overturned the intuitive fix: the 0.50 m kris must NOT copy the 0.32 m dagger's flat roll
(that lays its blade across the disc); it belongs to the sword's regime.

Two edit-mode traps fixed on the way: `WeaponViewmodel.SetWeapon` called `Destroy()`, which in edit mode
logs an error and leaves the old model parented — every capture after the first photographed two weapons
stacked (now `DestroyImmediate` outside play mode). And `Sword.parryPostureDamage` stays 25;
`WeaponSilhouetteTests.SixDeflectEconomy_TheSwordStaysAt25` now guards the Marionette arithmetic from
inside weapon-land, so the next weapon pass trips over the constraint where it will be looking.

**And rule 9, again, in its purest form.** The previous agent's `DataFactory` / `PrefabFactory` work was
complete on disk and had **never reached an asset**: `Sword.asset` still carried the dagger-era 0.38 s
swing and 1.6 m reach. Code that describes a weapon is not a weapon until `Create Data` has run.

---

## The Eclipse: a bright sky that made enemies read better, not worse

**Symptom.** Asked for "the eclipse from Berserk, red instead of purple". The trap list was long: ACES
desaturates saturated colour toward orange above ~1.25, `ambientIntensity` is a no-op in Trilight, and
the sky is the backdrop every silhouette is read against — a bright reference image on a contrast-driven
melee game.

**What shipped.** Disc 19° → **38°**, pitch 13° → **22°** (bottom limb 3° above the horizon). Dome
`#1A0407`/`#4A0A0D`, blood halo 2.3× the disc, near-black disc `#0D0304`, white-hot rim `#FFD9A8`. Fog
`#1A0708`, Trilight sky `#6B4045×1.35`, equator `#82503A×1.35`, ground `#1F1010×1.35`, key `#C9542E`.
`Starfield` is now **two submeshes**: the whole red field on a material tinted exactly 1.0, the rim alone
on `_Color = 1.35`.

**Why it worked.** Vertex colours clamp at 1.0, so a field on a 1.0 tint *cannot* cross the 1.05 bloom
threshold or the desat knee however it is tuned; only the near-white rim does, and pushing near-white
toward white is what burning looks like. Measured after grading: sky hue **1–2°, saturation 74%** (was
285°, violet). Every ambient hue moved at **matched luminance** (equator .209 → .204, sky .130 → .135), so
bodies render at the same 8–10/255 they were tuned for. Silhouette at 4 m: Weber **90%** (was 89%); a head
overlapping the disc: 83% (was 88%) — a dark body on mid-red reads *better* than dark on violet.

**Invariants.** **Buy sky presence with area and contrast, never field intensity** — put anything that must
bloom on its own material and keep the field physically unable to. **Change hue at matched luminance**:
compute linear luminance before and after and hold the enemy-readability floor. And the trap that cost a
round of captures: **the shipped eclipse SIZE is data on `LevelDefinition.sky`**, in
`Level_01_Level.asset`. Changing `Starfield`'s defaults changes the legacy greybox and nothing the
pipeline actually builds. Three copies of the number exist (`Starfield.DefaultEclipse*`, `SkyDef`'s field
initialisers, and the asset) and they must be kept equal.

**Residual, honestly.** An enemy standing *high on a platform* directly over the disc centre is
dark-on-dark; the rim only helps at the limb. The disc sits above eye level so combat-range reads are
against the corona, but it is the one thing to watch in a playtest.

---

## Wall run: the sustain floor that could never fire (rule 9, twice)

`wallRunSpeedDecay` shipped at 0.20. With `minEntry 7`, `minSustain 5`, `maxDuration 1.6` the floor is
reachable only for a decay in `(ln(7/5)/1.6, ln(11/5)/1.6) = (0.21, 0.49)`; 0.20 is *below* it, so every run
ended on the clock and `wallRunMinSustainSpeed` was decorative. The author's own test caught it because it
measured the OUTCOME (which end reason fired) rather than asserting the constant. Fixed at 0.35 — a
minimum-speed entry now bleeds out at 0.96 s, a sprint entry rides the full 1.6 s — and the window itself
is asserted by `WallRunTunablesTests.BothEndConditionsAreReachable_TheDecayWindow`.
**Invariant: a parameter that gates an end condition needs a test that reaches that end condition.**

Second finding, and the third time today in this shape: `PrefabFactory` wrote all 19 wall-run fields but
`Player.prefab` had never been rebuilt, so the committed asset carried none of them and the motor ran on
C# initialisers — and so did the analyser, which reads the prefab. **Rule 9's failure mode is not only
"initialiser changed, asset didn't"; it is also "factory changed, asset didn't".** Rebuild after editing a
factory, and pin the asset with a test (`ThePrefabCarriesTheShippedWallRunTuning`).

---

## Wall-run geometry: author against `longest`, not `best`

`AnalyzeWallRunGap.best` is the widest-clearance arriving route, which in practice is the one that hops off
the wall at 0.2 s. The first sandbox gauntlet was "reachable" only that way — and its first draft was not
reachable at all (the corner sat across every exit line; real gap 7.9 m, not the documented 12.5).
`WallRunVerdict.longest` was added: the arriving route with the most time on the wall. **A pad that only
`best` reaches is a wall jump wearing a costume.**

The exit throws you ALONG the wall (+4 tangent, 7 out, 10 up); it does not carry you sideways. So landings
go **down the line**, past the face's end, within ~0.5 m of its plane: 1 m further out halves the longest
arriving run, 8 m out never arrives. A full-duration sprint run covers **13.5–17.6 m of wall** and nets
−2.24 m of height; a minimum-speed entry covers 5.7 m and nets +1.08 m before it bleeds out. Those are the
numbers a wall has to be sized against.

---

## The motor's timers ran on the world clock (rule 1, the other direction)

**Symptom.** None reported — found by review. Every `FirstPersonMotor` timer (`dashUntil`, `slideEndsAt`,
coyote, jump buffer, the three cooldowns) compared against `Time.time`, while displacement integrated on
`TimeScaleController.PlayerDelta`. Hitstop drives `Time.timeScale` to 0.02, so `Time.time` effectively
stops: a dash that landed a hit kept travelling at `dashSpeed` for the whole freeze, a slide's duration
cap grew by the hitstop length, and coyote / jump-buffer windows were wider under hitstop than without.
Rule 1 says hitstop must never *freeze* the player; this was hitstop *extending* the player.

**Fix.** A motor-local clock: `now += dt` at the top of `Update`, and every timer measured against
`now`. `Time.unscaledTime` would have been wrong too — it ignores the pause (`PlayerScale = 0`).
Asserted by `Hitstop_DashEndsOnPlayerClock` (a dash under a 0.6 s hitstop ends in 0.162 s).

**Invariant.** Anything that moves the player and anything that *times* the player read the same clock.
A timer on `Time.time` next to an integration on `PlayerDelta` is a bug even when nothing looks wrong.

Three smaller motor findings from the same review, all fixed: the stagger-cancels-wall-run check sat
*after* the run consumed the frame and returned, so it never fired (moved before the run branch); a
dash press with the air dash already spent cancelled the run without dashing (the cancel now uses the
dash's own gate); and `Teleport` left a buffered jump, a dash request, `airDashUsed` and a stale
coyote window alive across a warp, so a jump pressed just before F5 fired at the spawn.

---

## The feature suite's dummy was whichever spawner came first

**Symptom.** `Deathblow_StaggerPose*` and `Deathblow_MarkHeightSeparatedFromLockDot` failed on some runs
and not others with no code change: `nearest=0.00 m cameraInsideBody=True`, `mark=1.28 lockDot=1.05`.

**Root cause.** `SpawnDummy` took the first non-boss `EnemySpawner` that `FindObjectsByType` returned.
`isBoss` is false on the `Legendary_*` spawners, and once the level carried them the "first" spawner was
sometimes the Ninja — an imported skinned model with its own mark height (1.28) and bounds. The tests
were measuring a different body on different runs. `Progression_BloodstainCarriesSouls` was flaky for
the sibling reason: `FindAnyObjectByType<Bloodstain>` found an earlier section's stain.

**Fix.** The dummy is `Enemy_Grunt` when a grunt spawner exists, never a Legendary; the stain check
searches all stains for the one carrying the amount. The framing assertions now name the renderer that
came nearest, so the next failure says *what* was in the lens.

**What this leaves open.** The Ninja genuinely fails the framing test at both scales when it is the
dummy: at the shipped stand-off its stagger pose puts a renderer inside the camera. That is a real
finding about the mini-boss riposte frame, logged in `BACKLOG.md`, not fixed here.

**Invariant.** A test that picks "the first X in the scene" is a test whose subject changes when the
level does. Pick by name.

---

## The guard entry crossed the view once the blades got their length back

**Symptom.** `GuardEntry_NeverSwingsAcrossTheView`: `worst=9.8° idle=17.0° guard=12.7°`. Present since
the four weapon lengths landed; the last green run predates them.

**Root cause.** The entry is a single position lerp plus a quaternion slerp from idle to guard — one
motion, no waypoint, exactly as designed. But the sword's yaw sweeps −14° → 38° during the blend and on a
0.62 m blade that sweep carries the *tip* a few degrees nearer the crosshair than either endpoint. The
dagger never showed it because its tip is 0.32 m from the grip.

**Fix.** The grip bows outward (camera-right) on a half-sine, `GuardArcOut = 0.07 m`, during the rise
and the release. Still one motion, still no waypoint; the worst frame is now 12.0° against a 10.7°
floor. **Invariant:** a blend that is "one motion" in grip space is not necessarily one motion in tip
space. Measure the tip.

---

## Movement retune: the wall run that only worked sometimes, and speed with no ceiling

**Symptom (from play, 2026-09-03).** "Wall running, jumping and sliding don't feel that good. The wall run
only works sometimes. It's not clear when I have an ability or how much stamina is left. I can dash and
move endlessly at a constant speed — there needs to be momentum, so you don't just fly off the map."

**Root causes, each one a rule that was silently refusing the player.**
- Entry waited out coyote time (0.12 s after leaving the ground): the same approach attached or not
  depending on where the ledge was.
- Speed was judged on the *tangential projection*: a sprint-jump at 45° toward a wall is 7.8 m/s along it
  and 7.8 m/s into it, and the 0.55 approach gate (33°) refused it anyway. Both refusals looked like nothing.
- The look gate (0.30, ~72°) refused a run while the head was turned to line up the next jump.
- A slide still "sliding" through coyote after leaving the ground blocked entry outright.
- Every seam between two wall boxes ended the run (`LostWall`) — most of what read as glitching.
- A jump pressed a few frames after the loan expired was a whiff, so the player learned to bail early.
- A dash set 22 m/s and the air kept it forever; a slide minted +5 on every press, so slide-jump-slide-jump
  was a free constant 16 m/s. There was no budget and no UI, so "can I dash" was a hidden cooldown.

**Fix.** Research first (Source `gamemovement.cpp`, Titanfall 2 wall-run, Apex slide cap and slide-hop
fatigue, Doom Eternal's dash pips), then: entry on *total* speed (6) with the whole velocity redirected down
the run; 53° approach; only looking backwards refuses; no coyote wait; a slide yields to the wall;
`wallRunLostGrace` 0.15 s and `wallRunExitGrace` 0.15 s; the wall's top speed 13.75 (1.25× sprint — the
wall is faster than the floor); 1.75 s, sustain 4, decay kept at 0.35 (0.30 failed
`ASlowEntryLosesRealTimeAndASprintDoesNot`: a 6 m/s entry has to lose a quarter of the clock to read as
shorter). Momentum: `DecayExcess` — exponential drag on the excess over `airSoftCap` 17.6 in the air and
over a sprint on the ground, hard clamp 27.5; slide boost fades with carried speed and with chained slides.
Stamina: `PlayerStamina` (dash 30, wall-run 12 + 22/s, wall jump 12; 45/s grounded, 18/s airborne, 0.45 s
delay), `StaminaView` (segmented bar, DASH/AIR/WALL pips, named red refusal), F8 = infinite.

**Invariants.** Entry gates judge intent, never geometry-luck. A burst is a decay, not a cruise. Every
refusal is named on screen. A full bar always affords a full run, because the analyser assumes it.

**UNPLAYED as of writing.** Every number above is from arithmetic, the test suites and the research brief.
The next human session is the verification.

---

## Build Sandbox leaves the sandbox open, and the feature suite runs wherever it is pointed

**Symptom.** 52 feature failures in one run: every `LevelStructure` platform "missing", zero checkpoints,
zero arenas, four `LockOn_*` and one slide check red. Nothing in the diff touched any of them.

**Root cause.** `7. Build Sandbox Scene` opens and saves `Sandbox.unity` and leaves it as the active
scene. `FeatureTestRunner.Start()` runs on whatever scene is open. Reopening `Level_01.unity` and
rerunning gave 695 / 2, and the two were a test bug and the known pickup flake.

**Invariant.** After any generator that opens a scene, load `Assets/Scenes/Level_01.unity` before
entering play mode for the suite, and read `active_scene` off `editor/state` first. A green suite on
the wrong scene is not a green suite; a red one is not a regression.

---

## Launch lost a frame of ground friction, and the checkpoint test picked the one already lit

**Symptom (2026-09-03, first suite run after the movement retune).** Three reds, none in the retune:
`Slide_EndsWhenAirborneButKeepsSpeed` (`before=15.9 after=14.3`, tolerance 1.5), and
`Level_CheckpointHeals` / `Level_CheckpointRefillsFlask` (`actual=30 expected=100`, flask `0/3`). The
previous run had two different reds (`Stamina_RegenWaitsItsDelay`, `Items_PhysicsPickup`) and this one
had neither — order- and framerate-dependent, which is the signature of staging, not of the game.

**Root cause 1 — the motor.** `Launch()` clears `IsGrounded` and sets `vel.y` upward *between* frames, but
the movement step opens with `IsGrounded = cc.isGrounded`, which is still last frame's answer because the
controller has not moved yet. So the frame after a launch ran the *grounded* branch with no stick input:
`groundFriction` 14/s on 15.9 m/s. At 60 fps that is 3.7 m/s (the test had always been marginal); on an
unfocused editor running 140 fps frames it was 1.6, just over the tolerance. The in-game jump never hit
this because it sets `vel.y` inside the step, before `cc.Move`.

**Fix 1.** `IsGrounded = cc.isGrounded && !(!wasGrounded && vel.y > 0f)` — a body that was already
airborne and is rising is not grounded, whatever the controller remembers.

**Root cause 2 — the test.** `LevelManager.SetCheckpoint` is deliberately a no-op on the checkpoint that
is already current (a trigger you stand in must not heal you every re-entry). The test took
`FindObjectsByType<Checkpoint>()[0]` — no defined order — and when that happened to be the current one,
nothing healed. **Fix 2.** The test picks the first checkpoint that is *not* current.

**Invariants.** Anything that sets the player airborne from outside the step sets `vel.y > 0` and
`IsGrounded = false` together, and the step trusts that pair over `cc.isGrounded`. A test that calls a
guarded setter stages the state the guard checks.

Also that day: **the headless `-batchmode` copy of the project was retired** at the user's request. The
skill is now `.claude/skills/unity-editor` and drives only the open editor; if none is running, ask.
One trap from the removal: a Bash shell left `cd`'d inside the skill folder locked it, so `git mv`
failed with *Permission denied* until the shell moved out. Run `mcp_call.py` from the project root.

---

## The slide-jump that fast machines refused, and weight for a movement that had none

**Symptom (from play, 2026-09-03 evening).** "The wall running and movement feels better but the movement
needs more weight — I can still just shoot off a wall or ledge." "There needs to be more control in the
air, like CS:GO surfing." "You need to be able to slide-jump."

**Root cause of the slide-jump.** The suite had been reporting it for a while without anyone reading it as a
bug: the first slide measured `1.66 m in 0.12 s, 0/80 frames grounded` against a documented 4.0 m in 0.35 s,
and the verification report filed it under "only after a scripted teleport, unexplained". 0.12 s is the
coyote window. The grounded branch pins `vel.y = -2` and the final `cc.Move(vel * dt)` therefore moves the
controller **0.004 m down per frame at 500 fps** — inside its 0.05 m skin width — so PhysX reports no
contact, `CharacterController.isGrounded` flickers off, the slide runs on coyote tolerance, dies when it
expires, and a jump pressed more than 0.12 s into a slide is refused *on a player standing on the floor*.
It was never a teleport quirk: the hand-driven probe that measured 3.97 m had the Game view focused and
vsync'd at 60–144 fps, where the pin moves 0.014–0.033 m and clears the skin. The user's greybox scene
runs hundreds of fps unfocused, so for them the slide-jump was simply broken.

**Fix.** `groundSnapDistance` 0.12: while grounded and not rising, the final Move's vertical displacement is
at least that far down — **displacement only**, the sweep stops at the floor, so on flat ground it costs
nothing, on a step down it follows the step, and at a ledge it is one frame of extra drop. Twice the skin
width and under `stepOffset` 0.4, both asserted in `AirFeelTests`.

**Weight and air control, in the same pass.** Three new pure laws on the motor, all written by
`PrefabFactory` and pinned by `AirFeelTests` (rule 9), all read by `LevelArcAnalyzer` off the prefab so every
route is re-judged: `fallGravityMultiplier` 1.5 (rise at −30, fall at −45: `jumpHeight` still means 2.4 m,
the way down is heavy); `airCarryDecay` 0.8/s on the airborne excess over a sprint, applied *before* the
soft cap (a 17.6 wall exit is 15.4 at 0.5 s and 13.9 at 1 s — spent, not glided); `LandingSpeedFactor`
(soft 16, hard 26, loss 0.35: a flat jump lands at ~14.7 and is free, a 7.5 m drop costs 35%, applied
before that frame's slide press so the landing-slide still keeps a run alive). And `AirSteer` at
120°/s: the airborne velocity *turns* toward the stick with its speed untouched while the stick is within
90° of travel; Source's `AirAccelerate` then adds up to a run's worth along the stick, which is what a stick
held back brakes with. Steer, accelerate, carry decay, soft cap, in that order.

**Invariants.** Anything that must register ground contact moves the controller by a *distance* that clears
the skin, never by a speed times a frame. A test that reports "0/N frames grounded" is a bug report, not a
flake. Gravity asymmetry lives in one pure function that the motor and the analyser both call.

**UNPLAYED as of writing.** Every number is a starting value chosen by arithmetic. The first things to
feel: does a wall exit now arc and land rather than sail, can you carve a 17 m/s exit onto a landing with
the stick, and does the slide-jump fire every time on the user's machine.

---

## The wall-run lean shipped toward the wall

**Symptom (from play, 2026-09-03 evening).** "It seems like you accidentally reversed the way you lean when
wall running."

**Root cause.** Two sign conventions met without a test between them. `WallSide(n)` returns +1 for a face on
the player's RIGHT; `PlayerLook.SetRollBias` documents positive as "rolls the camera's up vector to the
LEFT". The motor called `SetRollBias(-WallSide(n) * wallRunCameraRoll)`, so a right-hand wall tilted the head
right — into the face. Every number was right and every test passed, because no test ever asked which way
13° pointed. Titanfall's convention, and what the inner ear expects when a wall is holding you up, is the
head tilting **off** the wall.

**Fix.** `SetRollBias(WallSide(n) * wallRunCameraRoll)`. The exit kick keeps its sign, so it is now a snap
back through level toward the face as the bias releases — a "let go", not more of the lean.
`WallRunLive_LeansAwayFromTheWall` stages a face on the right and asserts the roll is positive.

**Invariant.** A camera effect with a side has a live test that names the side. "13°" is not a spec; "13°
away from a face on the right" is.

---

## The EditMode runner hangs Unity behind a save dialog when the open scene is dirty

**Symptom (2026-09-03, twice).** `run_tests` over the MCP bridge returns a job that never starts:
`"status":"failed", "error":"Test job failed to initialize (tests did not start within timeout)"`, and from
that moment every bridge call answers `Unity did not respond to 'get_editor_state' within 2.0s`. The editor
window title reads **`vibegame1 - Untitled`** and Windows reports the process as **not responding**. It looks
exactly like a crash and is not one.

**Root cause.** The EditMode runner opens a NEW, untitled scene to run the tests in. If the currently open
scene has unsaved changes, Unity raises a modal *"Save changes before opening a new scene?"* dialog. A modal
dialog blocks Unity's main thread, so the bridge stops answering and the test job never starts. Nothing can
dismiss it from a session - it needs a human click. The scene gets dirtied by ordinary work: a prefab
rebuild, a generator, or `manage_scene load` after either.

**Fix.** Save the open scene before calling `run_tests`: `manage_scene` with `{"action":"save"}` - and note
that it FAILS with *"Cannot save an untitled scene"* once you are already stuck, which is itself the
diagnostic. Better, check `editor/state` first: if `active_scene.path` is empty you are already behind the
dialog.

**Invariant.** **Save the scene before running EditMode tests over the bridge.** A test job that "failed to
initialize" plus a bridge that stops answering is a modal dialog, not a dead editor - check the window title
for `Untitled` before assuming anything worse.

---

## The forge's sidecar travel is source travel, not rig travel

**Symptom.** The Argent Halberdier's manifest says its `Thrust` travels `forward_m: 0.898` and its
`ShoulderCharge` `3.546`. Sampling the Hips bone across the same clips on the shipped FBX gave **1.17 m**
and **4.5 m** — every travelling clip about 1.3× the sidecar, consistently (slam 0.78 vs 0.63, leap 2.33
vs 1.88, kick 0.32 vs 0.25).

**Root cause.** The `root` block records the motion model's SOURCE path; the tool scales it onto the rig
at export and never rewrites the sidecar. Its own README says as much in passing — "a Lunge with 1.05 m
of source travel moves the Animator 1.30 m on a 1.86 m rig" — and the field's comment ("metres at the
character's scale") is simply wrong. A `lungeDistance` copied from the sidecar would have shipped every
lunge 25 % short of the animation the player watches.

**Fix.** The lunges were written from the measured rig travel, and `HalberdierDataTests.
EveryLungeIsTheClipsOwnTravel` holds them to the IMPORTED clip — the Hips bone's forward travel,
sampled on the FBX at the clip's start and end — within 0.15 m, with a non-travelling clip required to
ship a lunge of 0. The measurement itself is a tool now: `Tools/measure_forge_fbx.py` runs in
the forge's own Blender venv with no editor at all.

**Invariant.** **A distance the art carries is read off the clip, never off a sidecar.** The manifest is
authoritative for frame ranges and event fractions; for anything metric, measure the asset that ships.

---

## Generated clips broke the "canonical four" premise — the clip name moved onto the attack

**Symptom.** Nine generated attack clips imported, split, and listed in the Halberdier's animator, and
none of them could ever play: `PuppetVisuals.ClipFor` maps an attack onto the forge's four canonical
clips (swing, heavy, `_Stab`, `_Kick`) and a `HalberdSweep` matches none of them, so every attack would
have played `AttackSwing`. The same silent failure the Revenant had, from the opposite direction.

**Root cause.** The Revenant fix chose an asset-name suffix over a field on `EnemyAttackData` on the
grounds that "the forge exports the same four attack clips for every model, so the mapping is a property
of the pipeline". That was true of authored clips and stopped being true the day `forge.py --motion`
shipped a clip that exists for one character alone. A per-character clip is content, and content is data.

**Fix.** `EnemyAttackData.clip`, resolved before every heuristic, against a table `4b` bakes onto the
prefab from every manifest clip carrying `OnAttackHit` — each with its own length and contact frame, so
the clip still bends to the attack's clock. `MiniBossFactory` errors at build time on an attack whose
clip the model does not ship, and the test forbids two attacks sharing one clip on this body.

**The root-motion decision made at the same time — and the design that did not survive the editor.**
Six of the clips TRAVEL; the tool's own contract applies that as root motion through a component that
moves the agent. This project does not let a clip own the transform — the NavMeshAgent moves the enemy
and the parry contract's reach is `range + lungeDistance` — so the first design had `4a` set Hips as the
motion node, leave XZ un-baked on the travelling clips, and let the Animator discard the extracted travel
(`applyRootMotion = false`). Measured in the live editor it did not do that, three ways: with only
`motionNodeName` set the clips reported an `averageSpeed` but `hasRootMotion` was false and the Hips
still walked 1.24 m in the pose; with the avatar's root bone set to a path (`EnemyRig/Hips`) the avatar
failed and **the model imported with zero clips**, silently; with the root bone set to `Hips` and root
motion applied, Unity moved the Hips' WHOLE transform onto the model root — XZ, the leap's 0.3 m lift and
the sweep's 16° of yaw — and the bake flags kept none of it in the pose. Discarding that would have
discarded the leap and the body turn with it. So NO root node is set, the Hips travel stays in the clip
like every other bone, `4b` inserts a `TravelRoot` between `SpinRoot` and the model, and
`PuppetVisuals.CompensateTravel` moves it by minus the Hips' XZ drift from its bind position in
`LateUpdate`. The mesh stays over its collider (drift under 5 cm at 50 % and 100 % of the thrust, the
charge and the leap, and the leap still lifts 0.3 m), and the distance ships as data. Legacy models with
no `root` block keep exactly their old import flags and get no `TravelRoot`.

**Invariant.** **When a lookup's premise is about what a tool exports, re-check it when the tool changes.**
And: a clip never moves an enemy — the art's travel is data, held to the art by a test. And: **on a
Generic forge rig, never set a root node**; a wrong path imports no clips and a right one moves the
whole pelvis. Cancel travel in a transform of your own.

---

## The MCP bridge lost a race at startup and nothing retried

**Symptom.** The editor was open and responsive, port 8090 was listening, and every bridge call answered
`no_unity_session`; the instances resource reported zero. `Editor.log` showed the HTTP transport failing
to connect ~40 s before the uvx server finished starting, then silence.

**Root cause.** `McpBootstrap` runs once per editor session (a `SessionState` flag) and the package's own
auto-start handler does not retry after a failed handshake. One lost race left the bridge down for the
whole session, with the only remedy a human click on the Tools menu.

**Fix.** `Assets/Editor/McpReconnect.cs`: `[InitializeOnLoad]`, retries `Bridge.StartAsync()` on every
domain reload when the bridge is down and the server is reachable, and exposes **Tools → MCP Bootstrap →
Reconnect Bridge**. Since the editor reloads its domain on every script save, the next compile heals it.
Meanwhile the work went ahead without the bridge: the model was measured in Blender
(`Tools/measure_forge_fbx.py`) and the C# was compiled offline with `dotnet build` on the Unity-generated
csproj files, which catch the same errors the editor's console would.

**Invariant.** **A bridge failure is not a stop.** Check `instance_count`, save a script or use the menu,
and if it stays down fall back to Blender for measurement and `dotnet build` for compile errors.

---

## A slide you could not see yourself in — and an eye that crouched instead of arriving

**Symptom (from play, 2026-09-04).** *"Make it so I can see my feet when sliding, and make the slide feel more
satisfying."* Looking down mid-slide showed floor. The rest of the package (FOV hold, roll, grit, scrape) was
all there and all correct, and the move still read as a fast crouch.

**Root cause.** Two absences. There was **no player body at all** — the prefab's only renderers were the arm
rigs under the camera — so the one thing every slide in every game is read by, the boots out in front, did not
exist. And the eye's drop was a `MoveTowards` at 6 m/s: it descended and stopped. A body dropping onto a floor
*arrives* — it goes a little past and settles — and a lens that never overshoots never says "weight".

**Fix.** `PlayerBody` (under the root, so it yaws with you and stays level when you look down): a gait on the
player's own clock, an air tuck, a wall-run lean, a landing dip, and a slide pose built from the FRAME rather than
from anatomy — eye at 1.05 m, half-FOV 47.5°, so the hips sink to 0.40 and lead the head by 0.25 m and the
leading boot lands at z 1.15, 41° below the horizon, in frame. The legs are THROWN there on a closed-form spring
(6 Hz, ζ 0.6: ~0.10 s, 9% past the pose) and yawed to the velocity, so steering the slide shows the legs going
where you go. The eye now arrives on the same kind of spring (4.5 Hz, ζ 0.55: ~12% below the slide height, then
up past neutral on stand-up), the lens carries a held 6 mm rattle that is quadratic in speed, the commit dip went
1.2° → 1.8° to answer the legs, and stand-up plants with a knee bend and a quiet Land.

**What was measured vs. what was not.** The geometry is arithmetic and `SlideFeelTests` holds it (bounds under
1.35, boot inside the frame, spring frame-rate independence at 20/60/240 fps). Whether it *feels* satisfying is a
human sliding in the sandbox looking down — nothing here can prove that.

**Invariants.** A sustained camera effect gets a HELD channel with one writer (`FovHold`, `SetRoll`, now
`SetRumble`); an arrival gets a spring, not a ramp, and the spring is closed-form so it cannot become
frame-rate-dependent; and the body writes only its own transforms — never the pivot, the ShakeRoot, the collider
or the motor.

---

## An offline `dotnet build` cannot see a new file, and the editor csproj hides that behind a project reference

**Symptom (2026-09-04).** With the editor owned by another agent, code was verified with
`dotnet build Assembly-CSharp.csproj`. A new runtime file (`WaterVolume.cs`) compiled fine in the runtime
build, then the EDITOR build failed with `CS0246: WaterVolume could not be found` — in `FirstPersonMotor.cs`, a
file that had not been touched by that build.

**Root cause.** Unity regenerates the csproj files only on its own asset refresh, so a file written from outside
is not a `<Compile Include>` yet. The runtime build was passing only because the check script added the
unlisted files to a temporary copy; the editor csproj carries `<ProjectReference Include="Assembly-CSharp.csproj">`
BY NAME, so it rebuilt the runtime assembly from the ORIGINAL csproj, without the new file.

**Fix.** The offline check (scratchpad `offline_build.py`) writes a patched temp copy of BOTH csproj files, points
the editor copy's project reference at the runtime temp copy, keeps the runtime temp alive until the editor build
finishes, then deletes both. Worth keeping as a tool if the editor is ever shared again.

**Invariant.** An offline compile of the editor assembly is only valid if the runtime csproj it references also
carries every new file. "Build succeeded" on the runtime alone proves nothing about the editor build.

---

## Water is a stay-refreshed touch, not an Enter/Exit pair

**Symptom / avoided.** The obvious water volume keeps a bool on Enter and clears it on Exit.

**Root cause.** `FirstPersonMotor.Teleport` disables the CharacterController to move it; a disabled
collider sends no `OnTriggerExit`. A player warped out of the yard's water lane (F1 → MOVEMENT YARD, a respawn,
a checkpoint warp) would still be "in water" — no friction, a 14.85 m/s floor — on the next staircase, with
nothing in the console.

**Fix.** `WaterVolume.OnTriggerStay` → `motor.TouchWater(volume)` refreshes `waterUntil = now + waterGrace`
(0.15 s) on the motor clock; `InWater` is `now < waterUntil`; `Teleport` clears it besides.

**Invariant.** **Any state a trigger grants the player expires on its own.** Never rely on `OnTriggerExit`
from a CharacterController.

---

---

## Aggression scaling turned the punish window into a beat

**Symptom (from play, 2026-09-04).** *"He needs to use his charge when you get too far and then combo his
attacks on you and be aggressive."* The Halberdier held at 4 m, threw its charge no more often than a
sweep, and a player who backed off got a breath.

**Root cause.** Two data facts. The charge shared its weight with the leap and a plain sweep across the
far band, and its range (3.6) whiffed from the far half of its own band once the lunge stopped
`lungeMinDistance` short. And `aggression` was 0.55 — but raising it alone would have quietly halved every
opening: `EnemyController` plays recovery × lerp(1, 0.35, A), so at 0.85 the heavy's 1.6 s "biggest punish"
becomes 0.72 s, and the test that called it the biggest punish only ever compared raw numbers.

**Fix.** Charge entries at 5 m to the aggro edge weighing 11 against 1.2, two of them chaining into the
sweep pair or the thrust; three-hit strings as the close-band default; aggression 0.85 with the raw
recoveries rewritten for it (heavy 2.4 → 1.08 s in play) and the charge's range raised to 4.4 so it lands
from anywhere its band can pick it. `HalberdierBehaviourTests` asserts the *effective* opening, the
charge's weight share, the openers, and that no tell got shorter.

**Invariant.** **Assert what the controller plays, not what the asset says.** Any number `EnemyController`
scales by aggression is asserted after the scaling. And: raising aggression is a retune of every recovery in
the moveset, not one field.

---

## A sprite made during a build is not an asset, so the prefab keeps nothing

**Symptom / avoided.** The obvious way to give a code-built HUD rounded panes is `Sprite.Create` on a
`Texture2D` painted at build time. It works in the editor session that built it and ships a prefab whose
every `Image.sprite` is a missing reference: the sprite was never saved, so the panes come back as flat
quads the next time the prefab loads.

**Fix.** `UiSprites` writes each generated sprite as a PNG under `Assets/UI/Generated/`, imports it
synchronously as a single 9-sliced Sprite (uncompressed, no mips, border set from the corner radius plus
the feather) and hands the ASSET back to the builder. The PNG is rewritten only when its bytes change, so
a rebuild with unchanged generators leaves the asset (and its GUID) alone.

**Invariant.** **Anything a built prefab references must be an asset on disk before the prefab is saved.**
A generator that creates textures, sprites, meshes or materials in memory has to `CreateAsset` / write and
import them first, or the prefab silently references nothing.

---

## Generated strike clips do not strike — and a hit at 4 m with a 1.7 m blade is the same bug twice

**Symptom (from play, 2026-09-04).** *"The animations don't line up with the attack hitboxes."* THE ARGENT
HALBERDIER's sweeps, thrust, slam and heavy played their generated clips with the contact frame timed
exactly on the data's impact — and still nothing visibly connected.

**Root cause, measured.** The halberd TIP (the RightHand-weighted vertex 0.80 m from the joint), sampled
through every attack clip at 5 % steps: the four AUTHORED strikes whip it from −1.4 m to +1.3 m at 36–86 m/s
with the strike on the manifest's contact frame; the GENERATED "strike" clips move it at 1–8 m/s —
`HalberdSweep` drifts, `HalberdBackswing` and `Thrust` barely stir, `OverheadSlam` and `HeavyWindup` END
with the blade behind the body. The motion model was asked for a halberd sweep and produced a body that
shifts its weight. Compounding it, the attacks landed at 3.8–4.7 m (+0.5 slack) while the tip's whole reach
from the hips is ~1.7 m at scale 1.15 — so even a striking clip would have hit with the blade two metres
short. And `EnemyController` only ever attacked inside `preferredRange + commitTolerance`, so the far-band
charge entries (5 m+) could never be selected at all: he walked in and threw a sweep.

**Fix.** Sweep / thrust / slam map to the authored `AttackSwing` / `AttackStab` / `AttackOverhead`; the
backswing and the heavy, with no striking clip left, are removed rather than doubled onto a shared one
(MOVEMENT-PRINCIPLES rule 2 — a tell with no blow behind it is exactly the bug); the slam inherits the punish
window. Ranges came down to the blade (2.7–2.9 m for the cuts + a 1.0–1.3 m step from the cue, kick 3.3,
spin 3.6, charge 3.0 after its 4.7 m), the commit band to 3.2 + 0.4. `MiniBossFactory.MeasureContactFraction`
measures every generated clip's tip at `4b` and logs "NO STRIKE" for one under 10 m/s. `PuppetVisuals`
returns the Animator to ×1 at the impact (`recoverySpeed`) and holds the follow-through, instead of running
the ×0.55 wind-up scale through the whole swing and snapping to idle 0.25 s after the blow.
`EnemyController` gained a FAR-BAND COMMIT: outside the band it attacks if and only if
`EnemyMoveset.HasEligible(dist)` — an entry whose band contains the distance — so the charge fires from 5 m
to the aggro edge and nothing without a closer is ever thrown from range.

**Invariants.** **A clip is an attack only if the weapon moves like one — measure the tip, do not trust the
prompt that generated it.** **An attack's range is the weapon's reach plus the step the data gives it**, never
"where he stands plus a bit"; a hit that lands with nothing touching the player is a mismatch, whatever the
timing says. **A moveset entry authored for a distance the controller never commits from is a lie** — the
far-band commit exists so the bands mean what they say.

---

## A jump thrown out of a dash was a dud, so the perfect dash-jump could never fire

**Symptom (2026-09-04, feature suite).** `Perfect_DashJumpInsideTheWindowIsPerfect` and its two siblings red: a
jump 0.10 s into a ground dash registered no perfect, refunded nothing, and `LastPerfectKind` read None.

**Root cause — three, stacked.** The whole ground/air section of `FirstPersonMotor.Update`, the jump block
included, lives in the `else` of `if (IsDashing)`: a jump pressed inside a dash was not examined until the
dash ended. By then the 22 m/s sweep had lifted the CharacterController off a flat floor on the dash's first
frame (the same fast-sweep trap the slide hit), so coyote had lapsed and the buffered press died. And had it
fired, the dash branch's `vel.y = 0` would have undone it on the next frame. A dud, silently, and nobody had
noticed because a dud jump mid-dash looks like "the dash is committed". The window's maths was right; the
move it judged did not exist. (The first fix tried — making coyote accept a grounded dash — changed nothing,
because the block it lived in never ran during a dash.)

**Fix.** The jump-out is fired FROM the dash branch: a dash that began on the ground (`dashFromGround`) fires a
buffered jump at any point in its flight, the jump ENDS the dash (`dashUntil = now`) so its vertical speed
survives, and the dash's horizontal speed is already in `hv` that frame and settles as carried momentum like
every other burst. The perfect judgement is unchanged and happens there.

**Invariant.** **Judge a timing window on a move that can actually happen.** A window test must be preceded by
a plain "the move fires at all" check (`Perfect_DashFired` exists; the jump-out needed its own). And: a branch
that overwrites a velocity component every frame owns that component — anything else that writes it inside
that branch's lifetime must end the branch first.

---

## A balloon chain laid to the yard's spacing sails clean over the second orb

**Symptom (2026-09-05, caught in simulation before it shipped).** The T3 balloon arc was first laid to the
sandbox yard's proven chain — 5.5 m per link, 1.2 m of rise. Flown with the motor's own laws, every
orb-to-orb link MISSED by 2.1 m: the player went over the next orb, not through it.

**Root cause.** The yard chain was laid before the pop gained its float (0.45 s at 0.55 gravity) and the
carry cap (9 m/s). A pop with the float apexes ~3.5 m above the orb about 5 m out, so an orb only 1.2 m
higher is a metre and a half under the player's feet at the crossing. "Proven in the yard" was proven for a
different pop.

**Fix.** Orbs ~5 m across and ~3 m UP; `LevelTraversalAnalyzer.AnalyzeChain` flies every link with the
shipped `launchCarryCap` / `launchFloatSeconds` / `launchGravityScale` read off `Player.prefab`, and
`LevelTraversalTests` holds both the geometry band (2.4–3.3 m rise) and the flown chain. The yard's own
chain (`SandboxBuilder.BalloonChain*`, `MovementYardTests`) is outside this lane and still carries the old
1.2 m rise — it should be re-flown with the same analyser.

**Invariant.** **A traversal piece's spacing is derived from the flown pop, never copied from another
level.** Any change to a pop's numbers on the prefab re-judges every chain through the analyser.

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
| The first-person torso blocks the view | From play, 2026-09-04: `PlayerBody`'s chest cube 0.26 m under the 1.6 m lens filled the bottom of the frame on any look-down, and on a slide (eye 1.05 m, hips pushed forward) it sat in front of the legs. Fix: the torso renderers are `ShadowCastingMode.ShadowsOnly` — the player still casts a whole-body shadow, only the legs are drawn. Apex and Titanfall draw no first-person torso either. `SlideFeelTests` pins torso = shadow-only, legs = drawn. |
| A magenta Pyre bar | The fire is a hand-written UGUI shader (`Assets/Shaders/UI/FireBar.shader`). Magenta on the bar means the shader failed to compile in THIS Unity/URP — read the console's "Shader error in 'VibeGame1/UI/FireBar'" line; the C# builds cannot catch it. `HudExtensions.ApplyPyreFire` logs an error and leaves the plain bar if `Shader.Find` returns null. |
| A dash carried through a balloon overshot the next orb by a storey | Measured 2026-09-05 with the yard chain: a 22 m/s air dash through `Yard_Balloon_1` re-armed the dash but kept its speed, and the float window carried the player 16 m past orb 2. The chain is meant to be pop → aim → dash. Fix: `RearmDash` now ENDS the dash at the orb and both paths trim the carry to `launchCarryCap` (9 m/s, `TraversalMath.Launch(vel, up, cap)`), so the re-armed dash is the reach and the carry is only ever steerable. `PivotMovementTests.APopTrimsTheCarryToTheCap_SoTheNextOrbIsAimable`. |
| A white disc under the player's feet | The lock-on marker. `LockOnMarker.Show` cached "what I last set" starting from `false`, so Awake's `Show(false)` returned early and the metre-wide `DotCore` sphere stayed enabled at the player's origin until a target was acquired. Invisible for the whole project because nothing gave a reason to look down until `PlayerBody` (2026-09-04). Fixed with an `applied` flag; `LockOnMarkerTests.TheFirstHideActuallyHides` pins it. Lesson: a "skip if unchanged" cache is only valid after the first write. |
| A rebuilt animator controller reads NULL on the prefab in the same session | `PuppetAnimatorFactory.Build` deletes and recreates the `.controller`; until `AssetDatabase.ImportAsset(path, ForceUpdate)` runs on the controller and the prefab, `runtimeAnimatorController` on the freshly built prefab resolves to null in that editor session and a test reading it fails for no real reason. Force-import both after `4b` before testing. |
| Naive brace-balance checks lie | A regex `{`/`}` counter reports imbalance on *every* file here (interpolated strings, chars). Do not use it as a compile proxy — it produced 78 false positives once. |
| Do not `SetActive(false)` the viewmodel | `WeaponController.Awake` caches `GetComponentInChildren<WeaponViewmodel>()` — **active-only**. Hiding the viewmodel root for a screenshot and then reloading the scene leaves that cache null, and every `Equip()` silently stops swapping the weapon model. Hide `Renderer.enabled` instead. |
| Enemy standoff distance | `agent.stoppingDistance = attackRange * 0.7` parks the 2.2×-scale boss ~2.1 m from the camera, too close to read in first person. Known, not yet changed. |
| Blender measures a forge FBX when the bridge is down | `Tools/measure_forge_fbx.py`, run with the forge tool's own venv (`ai_skelly_tool/.venv/Scripts/python.exe`), prints bounds, bone heads, facing slices, per-clip arm span and per-clip Hips travel in **Unity axes**: `unity = (-bl.x, bl.z, -bl.y)`. Import with `ignore_leaf_bones=False` or Blender drops the hands, toes and head. The forge places every bone on the drawing's z = 0 plane, so read facing off the mesh (feet, head, extremities), never the skeleton. |
| `Sword.parryPostureDamage = 25` is arithmetic, not tuning | 25 × 1.4 (`Marionette_SpinPass.parryPostureMultiplier`) × 6 = 210, exactly the Pale Marionette's `maxPosture`, and FeatureTests' Knight beat counts it at ×1.3. `MarionetteDataTests.SixCleanDeflects_BreakIt` asserts the six-deflect break against `Sword.asset`; `WeaponSilhouetteTests.SixDeflectEconomy_TheSwordStaysAt25` guards it from the weapon side. A weapon pass that touches this number silently re-tunes two boss fights — keep the arithmetic landing on 6 or move `maxPosture` in the same edit. |

---

## Running generator 3 alone silently nulls every item's viewmodel

**Symptom (2026-09-06).** A one-line tuning change in `DataFactory` was applied the correct way — edit the
initialiser, re-run `3. Create Data`, read the shipped asset back off disk (rule 9). The asset was right.
But `git diff` afterwards showed two files nobody had touched:

```
-  viewmodelPrefab: {fileID: 5387924563961540692, guid: e637fe0a881c1514dbfbf7c11212cdb9, type: 3}
+  viewmodelPrefab: {fileID: 0}
```

`Grapple.asset` and `WallSurge.asset` had lost their held-item models. Nothing logged, nothing failed, and
the value the change was actually about was correct — so a session that diffed only the file it edited, or
committed with `git commit -am`, would have shipped the player holding nothing.

**Root cause.** `viewmodelPrefab` is a ScriptableObject field that `DataFactory` (step 3) creates and
`PrefabFactory` (step 4) fills in — the prefab it points at does not exist until step 4 builds it. Step 3
rewrites the asset from its initialisers, so running it alone always leaves the field null. It is not a
bug in either generator; it is the pipeline order being load-bearing in a direction that is invisible from
the asset the change was about.

**Invariant.** **Steps 3 and 4 are one operation.** Any run of `3. Create Data` is followed by
`4. Build Prefabs` before the tree is diffed or committed, and the check is `git status` over the WHOLE
tree — not the asset the change was aimed at. This is the same failure family as rule 9: what a generator
writes is not what you told it to write, and the only proof is reading the result back.

**Corollary, same session.** `PrefabFactory.BuildAll` re-serialises `Player.prefab` and the enemy prefabs
with reordered `fileID`s and identical content — a ~1300-line diff that means nothing. Check whether a
prefab diff is substantive before committing it (`git diff -U0 | grep -v fileID | sort | uniq -c`: churn
shows as equal `+`/`-` counts of identical lines). Revert pure churn so a real prefab change is visible in
the history.

---

## 2026-09-07 — A repeated level pass detached the boss doorway from its arena

**Symptom.** The full EditMode suite found the final descent overlapping `Wall_Boss_S_L` after the
level authoring generator ran twice. The boss arena itself was correctly at its new lower position.

**Root cause.** `Apply` first resets the south walls through its absolute `Reshapes` table. The later
descent pass translates boss objects relative to `Boss_Arena`; on repeat, that anchor is already at the
destination, so the translation is zero and the two reset walls remain at the original location.

**Fix.** `ApplyDescent` places those walls at final absolute coordinates after translating the arena.
Regression tests check the shipped doorway against the arena floor and boundary, exercise migration from
the original arena position, and compare repeated authoring results.

**Invariant.** An anchor-based translation is idempotent only when earlier passes preserve every child's
coordinate frame. A child reset by an earlier absolute pass needs a final absolute placement too.

## 2026-09-07 — A long downhill ramp outlasted the dry slide's contract

**Symptom.** The authored 48 m descent could not support one continuous slide under shipped friction 2,
end speed 8 and duration 0.9 s. Gravity's small slope contribution did not overcome the friction, and the
duration expired regardless. At 20 fps, 22 m/s also crosses 1.1 m horizontally per frame: a 1:4 slope drops
0.275 m, more than the ordinary 0.12 m ground snap.

**Fix, explicitly approved.** A slide moving downhill while actually grounded on a walkable slope
skips dry friction and renews its duration, adding gravity up to the existing slide speed cap. Flat,
uphill and airborne branches retain their original slope-then-friction arithmetic. Only that grounded
downhill branch extends the displacement to follow its contacted plane; vertical velocity stays owned by
the existing gravity/jump logic. Leaving the hill restores ordinary expiry and friction with a fresh tail,
as leaving water already does. Setting `slideSlopeAccel` to zero disables the whole addition.

**Invariant.** A sustained slope needs both a speed/duration contract and controller contact at the
lowest supported frame rate. A cached coyote normal cannot sustain an airborne slide; ground snap cannot
override jump velocity. Pure tests cover the arithmetic; actual collider behavior requires the live probe.

## 2026-09-07 — Generated solar realms must preserve physics and authoring identity

**Symptom.** Review of the new boss-realm builder found a primitive cylinder used as a wide, thin floor,
and a glowing ceiling disc rotating on all axes. The exporter also initially lost the new realm data.

**Root cause.** Unity's cylinder primitive has capsule collision: scaling it to a broad disc does not
make its collider flat. A continuously tilting ceiling disc eventually sweeps down through combat.
Scene export reconstructs ArenaDef rather than retaining new optional fields automatically, while
remote spawner positions differ intentionally from the historical authored exterior anchors.

**Fix.** The stationary floor explicitly uses a MeshCollider sharing the cylinder mesh. The solar disc
rotates around Y only. SolarRealmPlacement retains historical spawn/pickup coordinates for export,
and the portal carries a deep-copied SolarRealmDef that the exporter preserves. Generated feature
checks guard the floor shape and the ceiling's axis; exporter tests guard the complete definition.

**Invariant.** Verify the collider separately from the visible primitive. A rotating decoration must
remain outside playable space for its entire rotation. Optional generated realm data must survive
export to a fresh definition; rebuilding a scene must not turn remote runtime placement into authored
route geometry. Live Unity verification for this change is recorded in VERIFICATION-REPORT.md.

## 2026-09-07 — An opening volley must prove the fifth contact, not just five launches

**Symptom.** The first arrangement earned four parries. Moving its overhead platform closer made
all five connect, but the fifth arrived after the player's first jump and one speed stack had decayed.

**Fix.** The upper pair now shares the front terrace at z 15. Two actual motor/projection runs earn
five distinct grants and the existing 1.60x multiplier before the first jump. The late-ramp row and
global projectile/boost numbers stay unchanged. The probe sorts spawners before reporting their
counts and fails on death, rather than letting a subsequent respawn contaminate the trajectory.

**Invariant.** A range/LOS proof does not establish moving interception. Verify actual contact gaps
and the resulting multiplier. A sequenced row also needs a finite deadline for each later member,
even if the runner never enters its firing band; the first member alone waits passively for approach.

## 2026-09-07 — New opening enemies can contaminate controlled feature tests

**Symptom.** Adding the opening row caused unrelated guard, lock-on, flask and deathblow checks to
see real turret attacks/targets during their staged dummy encounters. A pickup restoration check
also collected the freshly restored item again because the tester still overlapped its trigger.

**Fix.** FeatureTests temporarily hides authored enemy instances and locks their aggro for the Guard,
LockOn, Deathblow, Items and Flask groups, then restores their prior state. Test-created dummies stay
available; previously launched bolts are cleared. The collected test pickup moves clear before its
restore event, preserving the player's staging for subsequent grapple checks. Portal return checks
compare horizontal placement tightly while allowing the normal short vertical settle onto the deck.

**Invariant.** A controlled combat assertion must have a controlled attacker set. Keep separate live
level proofs for the actual encounter; do not retune gameplay to satisfy a contaminated harness.

## 2026-09-07 — Long descents need firing gates and height-aware perch design

**Symptom.** Moving the five opening turrets along a much longer hill exposed premature readiness
windows. The final overhead bolt fired but flew over the descending player, then looped behind them.

**Root cause.** Range alone does not locate a beat along a route. Starting a later member's deadline
when the prior bolt resolves consumes that window while the runner is still far away. Projectile
lead intentionally ignores vertical velocity; equal-height upper perches therefore have very
different interception angles as the player descends. Static frontal LOS cannot prove contact.

**Fix.** Optional authored progress gates hold each member before its finite readiness window starts.
The rear overhead terrace descends with the route, linked to the front terrace by stepped beams.
The probe now requires every real deflect on the slope, full 1.60 boost and reaching the run-out;
it no longer continues onto the first span with additional movement assistance.

**Invariant.** Record moving contacts, not merely launches. Test the saved scene after rebuilding its
NavMesh: moving a spawner temporarily can still project its enemy onto the old baked perch height.
The final motor clamp remains 27.5 m/s, although an impulse can be observed above it before the next
motor tick. Neither projectile flight, the motor, nor parry rewards needed retuning for this fix.

## 2026-09-07 — A larger arena decoration should not silently enlarge its trigger

**Symptom.** Old court corners protruded beyond the new sun surfaces.

**Fix.** A separate optional visualRadius encloses measured floor, wall, pillar, gate and torch bounds
with at least 1.5 m margin, while the existing portal radii, retries and returns retain their behavior.
Zero visualRadius falls back to exteriorRadius, and the definition survives export/rebuild.

**Invariant.** Size enclosing scenery against complete bounds, not floor width alone. Decorative size
and interaction range are separate authored values when changing one would break a tested return point.
## 2026-09-07 — Crosshair tests must aim down slopes

**Symptom.** Seven grapple feature assertions failed after lengthening the opening, despite unchanged
item and grapple systems. The first failure was target acquisition; consumption/pull/execute then
failed as consequences.

**Root cause.** SpawnDummy snaps onto the NavMesh. Along the new descent this places the target below
the player, while the harness's FacePoint sets yaw only and resets pitch to zero. The actual grapple
uses a 12-degree 3D crosshair cone, so looking horizontally legitimately finds no target downhill.

**Fix.** Only the two grapple dummy stages now aim at the chest using PlayerLook.NudgeAim, including
pitch. The existing range, sightline, consumption, pull and execution checks stay intact; the harness
returns pitch to neutral afterward. Gameplay aiming and grapple code are unchanged.

**Invariant.** A crosshair test must aim in all three dimensions. Melee-facing helpers that discard
height cannot stand in for camera aim on slopes.

## 2026-09-07 — Cloud coverage from the crest and solar shell opacity

**Symptom.** The cloud ocean looked like a small patch from the raised opening; old court details
showed through the suns. **Cause.** The fixed cloud bounds were sized for the lower route, global
36..140 m fog erased distant bank detail, and additive solar blending could not obscure geometry.
**Fix.** A 900x1400 m sea, 90x144 grid, stays beyond the 300 m camera range around the route.
Cloud-only 80..280 m haze retains rolling detail. Premultiplied solar blending with surface opacity
.92 obscures exterior courts. Zero surface opacity exactly preserves additive corona/interior light;
the inner ceiling explicitly retains its .20 emission multiplier and zero surface opacity.
**Invariant.** Validate atmosphere from the highest shipped spawn; opacity must control destination
attenuation, not merely increase additive brightness. Shared interior materials need explicit overrides.

## 2026-09-07 — Fast targets, projectile forecasts and sequence ownership

**Symptom.** Turret contacts and shot timing could feel inconsistent while descending quickly.
**Cause.** Endpoint-only contact could skip a moving target between frames. Distance/bolt-speed ETA
ignored the target's closing speed. A missed sequence bolt held the next member until its six-second
lifetime ended; reconfiguring a completed sequence retained the old completed index.
**Fix.** Relative swept-sphere contact covers both bodies' motion with teleport/hitstop history guards.
Relative closing ETA drives cue and registry without changing lead, speed or homing. The opening alone
has a 1.25 s incoming-shot deadline. Reset/reconfiguration retires only the owned incoming bolt,
preserves reflected payoff, and resets the member index. The larger 120x14 m opening spreads the
same five turrets along progress 22/47/71/96/113 m.
**Invariant.** A reflected bolt has resolved its incoming sequence obligation and must finish returning.
Collision continuity cannot bridge a teleport. Frame-dependent contact fixes must preserve near misses.

**Verification trap.** The live automatic probe initially reported three/four grants. A frame trace
showed contacts were real: the fifth Active-to-Blocked interval was exactly .130 s, on the perfect
window boundary. Current closing ETA approximates curved flight, so the editor-only input forecast
now uses .08 s instead of .12 s. Gameplay parry windows remain unchanged. Avoid MCP calls while a
timing run is active: one 978 ms editor stall invalidated the first 30 fps trial; an uninterrupted
rerun granted all five, full 1.60x, with worst frame 36 ms. Automated parries prove integration,
not human fairness, and cue-to-contact times remain estimates during turning and hitstop.

## 2026-09-08 — A finite cloud sea cannot cover a raised camera's horizon

**Symptom.** Widening the opening exposed a hard pale waterline and a repeating row of black,
planet-like shapes across the distant horizon. Stars and nebulae remained visible through fully hazed
cloud banks.

**Root cause.** The 300 m camera plane clips the finite cloud grid below the geometric horizon from the
36 m crest; increasing its rectangular bounds does not change that angle. The cloud shader converged
RGB into fog but retained transparency, while `Starfield.BuildHorizonSilhouette` drew 64 unrelated,
fully opaque spires directly across the gap. Remote combat realms were not the repeating row.

**Fix.** The existing sky mesh draws a continuous, fog-coloured lower atmosphere after its decorative
layers, opaque below -3 degrees and clear by +2 degrees. Distant cloud haze now converges opacity to one,
and the ruin ring is subdued to alpha 0.25 at its body and 0.12 at its crest. No renderer, material or
light was added.

**Invariant.** Inspect a raised route at every yaw and match background haze to `unity_FogColor` in the
active colour space. Do not try to close an angular far-plane gap by enlarging a finite plane.

**Related solar silhouette.** The first exterior sun is roughly 249 m from the new start. URP fog had
already converged its RGB at that distance, but `SolarArena.shader` retained 0.92 destination opacity,
leaving a flat fog-coloured disc against the brighter dome. The shared final fade now remains intact
through 65% fog transmittance and smoothly reaches zero with full fog, releasing shell and corona
together. Fogging a transparent object's colour without fogging its occlusion is not disappearance.

## 2026-09-08 — The widened atmosphere exposed two multiplicative black floors

**Symptom.** The new horizon coverage removed the gap, but the fog formed a dark belt that did not blend
into the scenery, and unlit faces on the larger ruins collapsed nearly to black.

**Root cause.** `Sprites/Default` multiplies vertex RGB by alpha in its fragment path; the first lower-sky
mesh supplied already-premultiplied RGB, so partial haze was multiplied twice. After correcting that,
measurements showed a second independent floor: healthy Trilight probe values multiplied by `M_Ground`'s
old albedo produced only ~.0029 linear luminance before mortar/occlusion, while fog was .0117 and a nearby
cloud bank ~.1053. Contrast/exposure changes could not recover information that never entered the frame.

**Fix.** Horizon RGB is straight, authored sky/eclipses are converted into the active rendering space, and
the atmosphere grades from -7 to +18 degrees before the opaque eclipse disc and rim. The shared fog target
is `#20344D`; Ground/Stone/Platform bases are `#36404F`/`#424D5F`/`#586579`, with platform emission unchanged.
No light, post-process, tell colour, geometry or renderer budget changed.

**Invariant.** Diagnose the scene in linear light: inspect sky compositing and ambient-times-albedo before
retuning grading. A healthy probe times a near-zero surface remains near zero.

## 2026-09-08 — Weapon polish faults were stale pooled state and hot-path hierarchy work

**Symptom.** A reused glint could begin at an old contact, hitstop packed the ribbon with duplicate points,
and active swings performed avoidable managed work even though the visible effect budget was already small.

**Root cause.** Pooled `SlashFx` renderers waited until their first update for positions. `WeaponTrail`
treated stationary/sub-2.5 mm samples as new geometry, and `WeaponViewmodel.TipWorldPosition` called the
array-returning `GetComponentsInChildren<Renderer>(true)` on every active-frame read.

**Fix.** Spawn initializes all primitive positions immediately; the flare is one narrow four-point star;
sub-threshold trail movement accumulates while the old tail dissolves, with exact final contact forced.
Disabling clears the camera sibling. The viewmodel caches its chosen renderer per held model and reads live
bounds thereafter. The maul finisher stays below the deflect size. No renderer/material/light/buffer was added.

**Invariant.** A pooled VFX request must be visually valid in the request frame. Finite trail history is
spent only on visible displacement, and a per-frame presentation property must not enumerate a hierarchy.

## 2026-09-08 — Solar entry audio must follow successful state transition

**Symptom.** The cinematic sphere cut had no authored sound, and a cleared sphere could still execute its
entry path even though its visual crossing wash had been disarmed.

**Root cause.** The portal used no dedicated audio event, and `Update` checked `arena.Cleared` while
`Enter` did not, splitting visual and transport authority.

**Fix.** `Enter` rejects a cleared arena, then dispatches append-only `Sfx.SolarWarp` exactly once only
after `BeginFight` and teleport succeed, beside the same-frame `SolarTransition.Cut`. The clip is generated
once and pooled.

**Invariant.** Presentation for a state transition fires downstream of the successful transition, and
every gate that visually disarms the interaction must disarm its public entry path too.

## 2026-09-09 — Section translation cannot repair cramped local topology

**Symptom.** Moving the post-first-miniboss route farther apart improved the skyline, but T2/T3 still felt
like the same cramped structures with more empty road between them. The first T3 shortcut could be cleared
as a jump, its landing intruded on the widened span, and the solar membranes still competed with nearby
geometry.

**Root cause.** A rigid section offset preserves every local gap and footprint, including the bad ones.
The previous wall-run report also derived one launch rectangle from stale literals, so it could certify a
route different from the authored wall. Sphere clearance was being treated as a centre-distance problem
instead of checking the actual floor/wall/pillar/gate/torch bounds around the membrane.

**Fix.** T2 is locally rebuilt as a roughly 28 m helix with larger terraces. T3 now uses broad pillar
terraces, a 9.6 × 26 m span, a 23 m first wall and an isolated landing that creates a true 13.5 m gap
(about one second of wall time); its alternate arc has four balloons. Only after those local relationships
are correct do the sections move as units: T2 +38 m, T3 +70 m, T4/boss +96 m. All gates, checkpoints,
pickups, perches, water, balloons, encounter windows and kill bounds derive or move with the same data.
The span report derives launch rectangles from current wall faces.

**Invariant.** Scale a level in two passes: author local movement topology first, then translate the entire
section. Reports must derive their probes from shipped geometry, and sphere isolation is measured against
the bounds of every nearby structure, not an authoring anchor.

## 2026-09-09 — Current-position homing silently undid correct projectile lead

**Symptom.** Projectile encounters passed at base run speed but missed or arrived outside the authored
route at 17.6–27.5 m/s. Launch lead looked correct in isolation, yet high-speed crossing runners received
late, rearward or no parry contacts. A three-shot burst made that drift compound.

**Root cause.** The initial shot led the player, but every homing frame steered back toward the player's
stale current chest. Forecasting used range/muzzle approximations rather than first contact from the real
spawned root, and launch-frame cadence said nothing about when two curved flights would reach the player.
Encounter ownership also had no data-level route boundary, so a geometrically valid shot could belong to
the wrong part of a stacked or branching course.

**Fix.** `ProjectileFlightMath` solves the exact constant-velocity intercept and uses that moving target in
both forecast and runtime capped homing. It integrates at 120 Hz, sweeps projectile and player spheres from
the real projectile root, and searches for the fastest speed that still preserves the 0.28 s cue plus
0.16 s margin. `ProjectileEngagementWindowDef` bounds player presence and predicted contact along authored
route corridors. Every emission repeats life, band, LOS, facing, blocker, flight and window validation.
Bursts reserve predicted contacts rather than launch times, cancel without catch-up, and the Heavy Sentry
ships three contacts 0.42 s apart followed by 2.4 s quiet. The generic report proves all campaign windows
at 11 / 17.6 / 27.5 m/s.

**Invariant.** Aim, runtime homing, cue ETA and verification must consume one contact model. Sequence data
may decide ownership and geometry, never waive a normal firing gate. Cadence is an arrival contract; a
cancelled phrase creates silence, not debt.

## 2026-09-09 — A parry-count promise must use the strongest shipped weapon

**Symptom.** The Heavy Sentry was described as breaking after five parries, but its 250 posture actually
broke sooner with the Dev Blade. EditMode fixture shots also intermittently targeted a scene player instead
of the test player and left `ProjectileShooter` uninitialized.

**Root cause.** The tuning comment assumed 40 posture, while the shipped Dev Blade applies 60 × 1.2 = 72
per parry; three parries plus two reflected returns total 316. Separately, `AddComponent` in EditMode does
not guarantee the runtime lifecycle/order the fixture assumed, and a global player lookup was ambiguous.

**Fix.** The generated Heavy Sentry ships 330 posture, so five strongest-blade parries are required while
three reflected 45-damage bolts still kill its 130 HP. The fixture invokes `Awake`, binds its own
`PlayerCombat`, sets layers explicitly and calls `Physics.SyncTransforms`. The body is now a blade-free,
12-renderer stone reliquary with three recessed apertures and no lights or particles.

**Invariant.** Balance statements are arithmetic over generated assets and the strongest legal loadout.
Tests that exercise lifecycle-bound components must create an unambiguous world and explicitly establish
the lifecycle state they depend on.

## 2026-09-09 — A pooled-object cap cannot trust a static counter across editor reloads

**Symptom.** Six late full-suite VFX tests could not spawn any `SlashFx`, although inspection found zero
live effect objects. Focused runs passed, making the failure look order-dependent.

**Root cause.** Unity destroyed the pooled GameObjects across an editor scene/domain transition while the
managed `live` counter survived at its hard cap of 28. The pool already skipped destroyed object
references, but the separate counter had no equivalent recovery path. The behavior suite had a second
isolation mismatch: `WandReadability` sampled a scaled weapon animation using realtime waits while live
level enemies were still allowed to trigger hitstop.

**Fix.** `SlashFx` clears pool state at subsystem registration and, only when cap pressure occurs, recounts
genuinely active/counted instances before deciding to shed an effect. The steady-state path remains O(1)
and allocation-free; 28 real live effects still enforce the cap. `WandReadability` now suspends unrelated
world enemies like the other timing-sensitive feature sections. A dedicated stale-counter test pins the
reload recovery.

**Invariant.** A managed counter of scene objects needs a reload boundary and a cheap exceptional-path
reconciliation. A test comparing realtime with a scaled system must own every possible time-scale writer
in its world.

## 2026-09-09 — A weighted random test cannot assert one sample is guaranteed

**Symptom.** A full 980-test run failed only `ASignatureOnCooldownIsNeverChosen_TheFillerIs`, while focused
runs and the selector implementation were correct. The assertion expected a 100:1 weighted signature to
win one sample before and after cooldown.

**Root cause.** Weight changes probability, not eligibility. The filler remains a legal 1/101 outcome, so
an unseeded one-sample assertion had a real intermittent failure mode and also perturbed Unity's global RNG.

**Fix.** The test saves and restores `UnityEngine.Random.state`, uses a fixed seed, samples the eligible
periods repeatedly, and keeps the cooldown period strict: every choice during cooldown must be the filler.
It now tests the selector's contract rather than demanding a particular random draw.

**Invariant.** Tests of weighted selection either control the random stream or assert a statistical/
eligibility property across enough deterministic samples; they never equate a nonzero weight with a
guaranteed single result.

## 2026-09-09 — Route-audit corridors must not become invisible shooter triggers

**Symptom.** The blue squid sentries and other projectile enemies fired only in odd patches of the route,
then stayed silent. The older autonomous behavior was more reliable in ordinary encounters.

**Root cause.** The reusable encounter pass built a runtime `ProjectileVolleySequence` for every authored
route-audit group. That turned narrow, synthetic engagement corridors into mandatory live firing gates and
gave each ordinary shooter only one owned phrase, even though those windows were authored to prove contact
geometry rather than define what the player must stand inside.

**Fix.** Every campaign shooter keeps one bounded route-audit record, but a runtime coordinator is now built
only when the record has explicit member progress gates. The five-beat T0 opening retains its ordered volley;
T1-T4 sentries are autonomous again and repeat their normal range, LOS, facing, obstruction and cue-safety
loop. The runtime coordinator does not consume audit windows. A shipped-data test pins that split.

**Invariant.** An authoring corridor is evidence, not an invisible trigger. Runtime sequence ownership must
be explicit in data; ordinary sentries continue firing whenever their normal readable combat gates pass.

## 2026-09-09 — Enemy targets must survive lifecycle discontinuities

**Symptom.** An enemy whose player reference was unavailable at `Start`, replaced by respawn, or cleared by
an in-play domain reload could remain permanently inert even after a valid player existed.

**Root cause.** `EnemyController` searched for `PlayerCombat` exactly once in `Start`, while `Update` returned
immediately whenever the non-serialized target was null. Nothing could ever reopen acquisition.

**Fix.** `Start` and `Update` share `EnsurePlayerTarget`, which retains a valid target and retries the lookup
only while the references are missing or inconsistent. A focused EditMode regression test clears both target
references and proves they are restored together.

**Invariant.** A scene-owned AI target is a recoverable reference, not a one-shot startup assumption. Missing
targets may pause a brain, but they cannot permanently disable it once the player exists again.

## 2026-09-09 — A traversal bolt cannot use player-contact clearance as world clearance

**Symptom.** The ordinary blue `pshooter_enemy01` sentries were awake and had line of sight in tight
parkour, yet rejected almost every shot as blocked. The ramp Surge Turrets were already readable and must
not be retuned.

**Root cause.** The conservative 1 m `SphereCast` used to prove a flight path doubles as a player-contact
radius. On a sentry deliberately tucked beside rails and landing lips, that broad volume grazed nearby
route geometry even when the actual predicted bolt line was clear. Two ordinary sentries sharing a beat
also advanced together after an arrival-spacing refusal, preserving their tie and starving the second one.

**Fix.** `EnemyData.projectileAllowTightRouteShots` is true only for the ordinary blue traversal sentry.
It linecasts the exact forecast path instead of broad-clearing the player's contact radius; range, LOS,
solid walls, frontal arrival, contact forecast and cue safety still apply. Heavy Sentries and Surge Turrets
retain the 1 m sweep. A rejected autonomous blue shot retries at the first safe contact slot rather than a
full shared beat.

**Invariant.** Tight-route support narrows the obstruction shape; it never waives combat readability.
Arrival reservations are handed off by predicted contact time, never by component Update order. Keep the
tight-route policy false for Heavy and Surge, and keep departure-support exemption false for Surge.

## 2026-09-09 — A broad Heavy forecast may leave its own support, never ignore the world

**Symptom.** The T3 Heavy stayed in `BlockedFlight` from valid shipped route positions and never began its
three-shot phrase, even after ordinary blue sentries were restored.

**Root cause.** Its intentionally conservative 1 m forecast swept against `T3_Perch_E`, the collider it
was standing on, before the bolt could clear the muzzle. Disabling broad clearance would have erased the
Heavy's safety identity and risked changing the already-correct Surge Turrets.

**Fix.** `projectileIgnoreDepartureSupport` is authored only on the Heavy. The shooter detects the exact
collider below its root and ignores only that collider's radius-only hits during the initial departure.
The actual centreline, a muzzle inside geometry, sibling colliders, a saturated eight-hit buffer, and any
support contact after departure reject the emission. Every follow-up still re-plans and revalidates. The
shipped T3 Heavy was then observed emitting its complete three-shot phrase from the route; a temporary
solid blocker still rejected the shot.

**Invariant.** A launch-support exception is collider-specific, departure-only, and fail-closed. It must
never be copied onto Surge Turrets or widened into permission to shoot through the support itself.

## 2026-09-09 — A parry impact must answer the strike the player saw

**Symptom.** The deflect already read, but its force package could be displaced from the crosshair and a
tap parry lacked the weapon's physical answer. A longer chromatic veil also risked masking the next cue.

**Root cause.** The impact package was passed the player root rather than the rendered eye, and feedback
trusted the attacker's old position even for a bolt whose travel direction was now the truthful source.
Weapon kickback reused held-guard state, so a tap had no recoil.

**Fix.** `ParryImpact` resolves one feedback eye from `CameraFX`, then `Camera.main`, then the player root;
the hoop and directional kick share it. `ParryController` derives melee feedback from the attacker and bolt
feedback opposite `AttackInfo.incomingDirection`, the same rule as combat facing. `WeaponViewmodel.DeflectImpact`
starts at the live pose for both tap and hold, while guarded hits keep their separate thud. The authored
chromatic contact is 0.35 for 0.12 s.

**Invariant.** Presentation source direction must equal combat source direction, and a Perfect's feedback
must originate from the camera that rendered it. Do not turn a perfect's recoil into a guard-only effect or
let post FX cover the next readable cue.

The same Perfect also grants the shared speed ladder after `attacker.OnParried`. The opening
`SurgeTurret` is deliberately excluded because its override already grants one stack with its dedicated
1.4 s decay; every other attacker uses the player-authored 2.0 s decay. Keep that exclusion beside combat
resolution so a turret bolt can never double-pay.

## 2026-09-09 — Run completion is a frozen authored contract, not an inferred boss clear

**Symptom.** A boss death alone could not express the requested run objective: all three sub-bosses and the
main boss, four distinct regular encounters, plus a D-to-S split reward. Treating every enemy instance as
credit also leaves respawn farming and duplicated ghost/progression decisions.

**Root cause.** `BossDefeated`, wallet balance and ghost recording had separate legacy listeners but no
run-local adjudicator. Instance identity changes on respawn, so it cannot represent one authored encounter.

**Fix.** `LevelRunScorer` begins with the run timer, credits each authored `EnemySpawner` name once,
closes only the current ordered split endpoint, adds the data-authored grade bonus, then freezes one
`LevelRunResult` on boss defeat. `Level_01` is authored at 3560 run souls (the three sub-bosses, Warden,
and four 40-soul regulars), four distinct regular spawners,
Ninja/Knight/Spellsword/Warden splits and D/C/B/A/S bonuses 0/25/50/75/100. On a scored level,
`LevelRunEvaluated` decides HUD, progression and ghost handling: success records/saves; failure reports
the missing gates, records no completion and discards the ghost.

**Invariant.** Count authored spawner names exactly once; a later split killed early cannot skip order.
`LevelRunEvaluated` is immutable terminal authority for scored levels. A legacy level with an empty run
contract keeps its direct boss-clear behavior.

## 2026-09-09 — Effect visibility must not hide run requirements

**Symptom.** The requested top-left status presentation needed a readable parry-speed stack and a developer
toggle without making held items or mandatory run objectives disappear.

**Root cause.** The strip previously had only generic transient player reads. A broad visibility toggle
would conflate inventory, temporary effects and level requirements.

**Fix.** `StatusStripView` reads `ParrySurge.Stacks` as `SPEED SURGE xN`, and reads the scorer for a
persistent RUN / FOES / SPLITS row. The F1 developer menu flips session-only
`StatusStripView.StatusEffectsVisible`; live views rebuild immediately. The switch suppresses only active
effect rows, while held items and run progress remain visible.

**Invariant.** The HUD observes status and scoring; it never writes speed, stacks or run credit. A developer
effect preference is not a way to hide inventory or a completion gate.

## 2026-09-09 — Tight-route occlusion must not restart a traversal sentry's thought

**Symptom.** The ordinary blue squids fired only in odd pockets or appeared passive on stacked parkour,
even though the already-tuned ramp turrets felt correct.

**Root cause.** Every one-frame rail or ledge LOS loss cleared acquisition, charging the full 0.7 s delay
again. A transient forecast rejection then discarded another complete 1.6 s beat. Backpedalling also used
movement velocity as a facing proxy, so looking directly at a squid while moving away could suppress it.

**Fix.** Only the authored tight-route blue sentry preserves completed acquisition across brief occlusion,
retries a rejected plan after 0.08 s and uses actual flat look-facing. The emission itself still revalidates
range, LOS, solid obstruction, frontal arrival, contact and cue safety. Heavy Sentries and Surge Turrets
retain their conservative behavior and all existing tuning.

**Invariant.** A glimpse interruption may delay a blue shot, but it must not make the sentry repay its whole
acquisition. Fast retry is permission to reconsider, never permission to fire through a failed gate.

## 2026-09-09 — Projectile forecasts must use the same clock as player motion

**Symptom.** A bolt could steer correctly yet lose or delay its cue around parry hitstop.

**Root cause.** Runtime steering read motor velocity, while the cue fallback divided player-clock movement
by scaled `Time.deltaTime`. During a 0.02 world scale that manufactured roughly 50x target speed and could
turn a valid incoming contact into a separating/no-contact forecast.

**Fix / invariant.** `ProjectileMath.ForecastTargetVelocity` now owns both reads: authoritative motor
velocity, otherwise transform displacement over `TimeScaleController.PlayerDelta`. Never compare a player-
clock displacement with world-clock delta.

## 2026-09-09 — God mode protects health; it does not mute parry

**Symptom.** During F8 testing a valid blue-bolt parry appeared to stop working.

**Root cause.** `PlayerCombat.ReceiveAttack` returned `None` for invulnerability before evaluating the parry.

**Fix / invariant.** Invulnerability ignores damage outcomes but allows `Perfect` to complete its normal
deflect, reflection, posture and movement-reward path. A debug survival switch must not disable the mechanic
being tested. `ExecuteInteractor.IsExecuting` remains an early rejection before parry resolution because
deathblow invulnerability owns a sealed presentation, not a debug playtest state.

## 2026-09-09 — Fast combat UI reinforces the decision at its point of use

**Symptom.** The top-left run contract was one dense line, its maximum status stack could exceed its panel,
and a world-space projectile cue lacked restrained central reinforcement during high-speed traversal.

**Fix.** The run contract is now a primary `RUN earned/required` row plus a quieter FOES/SPLITS context row;
the strip is 184 px, sized for all eight shipped rows. `ProjectileThreatView` draws four low-alpha brackets around
the crosshair only after the existing projectile cue fires and only inside its 0.28 s action window.

**Invariant.** Persistent macro information belongs in the edge hierarchy; immediate action information may
briefly reinforce at the reticle. The HUD observes `BoltRegistry` and never becomes aim assist or early warning.

## 2026-09-09 — Flat-ground stick velocity is not projectile target descent

**Symptom.** The lone T3 Heavy could emit once, then remain at `BlockedFlight`; in other starts it never
emitted. Its three-shot phrase looked random despite valid range, line of sight and authored timing.

**Root cause.** `FirstPersonMotor` intentionally keeps about -2 m/s Y velocity while grounded so the
controller stays attached to a deck. Projectile planning treated that contact residue as a real fall and
forecast the target through the platform. After a parry, `EnemyController.Recover` also ran the melee
`Reposition` path for `rangedOnly` enemies, letting the fixed sentry drift off its authored pad.

**Fix.** `ProjectileMath.GroundAwareTargetVelocity` is shared by launch planning, runtime steering and
runtime contact/cue prediction. It clears negative Y only on flat grounded support; airborne descent,
upward movement and grounded ramp motion are preserved. Recover now stops locomotion for `rangedOnly`
enemies while melee enemies retain Reposition.

**Invariant.** Normalize the motor's flat-ground contact residue at every projectile forecast boundary,
never globally. A ramp is motion, an airborne fall is motion, and a ranged-only perch is not a melee lane.

## 2026-09-09 — A spawn leaderboard is level content, not a restored HUD pane

**Symptom.** The requested large glowing leaderboard behind the Level 1 spawn needed to survive rebuilds
without bringing back the previously removed top-right BEST RUNS screen UI or polluting the NavMesh.

**Fix.** Optional `LevelDefinition.worldLeaderboard` data is authored, built and exported with the level.
`WorldLeaderboardView` observes the existing local leaderboard and labels it truthfully. The generated root
and every descendant use the Sky layer and have no colliders; the exporter captures its marker/config before
skipping the root from generic platform export.

**Invariant.** Physical world presentation and screen HUD presentation are separate consumers of the same
model. `GhostHud.BoardVisible` ships false. A generated display must be data-owned, round-trippable,
non-colliding and excluded from navigation geometry.

## 2026-09-10 â€” A three-contact Heavy phrase cannot be planned as three unrelated one-shots

**Symptom.** The Heavy Reliquary commonly fired one or two bolts, then appeared to bug out for its full
2.4-second cooldown. Two requested Heavies placed directly on the opening-ramp seam reproduced 2/3 and 0/3
emissions at maximum route speed even though a stationary test had previously produced repeated bursts.

**Root cause.** Every follow-up repeated the correct safety checks, but any single-frame LOS, facing, contact
or swept-clearance rejection immediately destroyed the entire phrase. A parry changes player velocity, so
the next forecast can legitimately need a frame to settle. The inherited 32 m range also armed a full-speed
runner too late. Finally, a seam-side muzzle moved behind the runner's predicted third contact and correctly
failed the 75-degree parry cone; this was a placement error, not an AI error.

**Fix.** Multi-contact planning preserves paid acquisition, retries initial rejections after 0.08 s, and
re-plans transient follow-up failures inside the existing finite deadline. Life, target and range loss still
cancel immediately; persistent failures time out with no catch-up. Heavy arrival readability uses the same
actual player look direction as the blue traversal sentry. The shipped Heavy range is 48 m. The opening pair
is data-authored on flanking pads at (-10,0.1,6)/(10,0.1,6), after the five unchanged Surge Turrets in one
seven-member coordinator. A live moving probe resolved 3/3 + 3/3 with no cancellations.

**Invariant.** Once a readable multi-contact phrase begins, transient re-planning may delay its next contact
but never silently erase it. Every emitted follow-up still passes range, LOS, facing, cue/contact and solid-
flight validation. Fix an unanswerable predicted bearing with placement before widening the parry cone.

## 2026-09-10 — A Heavy burst is an incoming-answer transaction, not reflected damage

**Symptom.** A Heavy could emit three bolts yet die from old reflected-damage arithmetic, carry partial
progress between phrases, or start its next cooldown at the final launch while the player was still answering
that bolt. Sequence-owned Heavies and autonomous Heavies consequently disagreed about when another burst was legal.

**Root cause.** `phraseActive` described emission only. Projectiles had no immutable phrase/ordinal identity
and no exactly-once incoming outcome, so the shooter could not distinguish three ordered Perfects from stale
returns, expiry or cancellation. `projectileInterval` was scheduled from emission time.

**Fix.** Every emitted bolt receives `(phraseId, ordinal)` and reports its incoming result once. A strict
Heavy phrase accepts ordered Perfect 0/1/2; any other outcome invalidates it, and Perfect 2 kills through the
ordinary `Health` path. Heavy returns do zero health/posture damage. Its 0.90 s rest begins at the final incoming
resolution and is enforced by both autonomous and sequence paths. `usesPosture=false` makes posture calls inert
and generated Heavy/Surge prefabs omit duel UI; melee health damage remains legal.

**Invariant.** A multi-shot requirement is scored from immutable incoming obligations. Never infer it from
return damage, never let a stale callback mutate a newer phrase, and never start its rest before the player has
answered the final shot.

## 2026-09-10 — A mathematically valid Perfect is still a black box without an actionable cue

**Symptom.** Perfect jumps worked in tests but players could not tell what action or moment produced them.
The wall check used a hidden maximum-duration threshold, and dash-jump accepted nearly the whole dash without
ever announcing when to press Space.

**Root cause.** The reward was documented after the fact (`PERFECT`) instead of taught before the input. A
wall can release early through decay, stamina or lost contact, so a fixed elapsed time was not its real moment.

**Fix.** The motor predicts clock/decay/stamina wall release and opens a 0.20 s pre/post hybrid window with
`WALL EXIT [SPACE]`; unexpected contact loss retains only post-release forgiveness. Dash-jump now exposes a
0.10 s window from 0.06–0.16 s and raises `DASH JUMP [SPACE]` at its opening. Successful wall timing adds a
capped +2 m/s tangent and refunds stamina; misses remain ordinary moves.

**Invariant.** A precision movement reward needs a cue naming the existing input at the real physical moment.
Do not widen an invisible timer to compensate for missing communication.

## 2026-09-10 — A final-ramp shooter must actually be beside the ramp

**Symptom.** The three last-ramp Surge turrets fired inconsistently and their projectiles were difficult to
read while descending. One appeared valid in the broad encounter record but rarely participated in the slope.

**Root cause.** `Spawn_T4_Surge_3` was eight metres beyond the 48 m ramp on the run-out, while all three sat on
the same side. The route contract audited a broad start/end corridor and did not pin physical ramp progress.

**Fix.** The three single-shot turrets now alternate sides at ramp progress 16/32/44 m, with 0.5 m between each
pad and the ramp edge. Their shared type receives presentation-only 1.35× core, 1.30× trail and 1.25× cue scale;
logical hit radius, timing and flight are unchanged. A dedicated placement test pins progress and clearance.

**Invariant.** Audit a ramp encounter in ramp-local coordinates. A broad route window cannot prove that a
perch is on the slope, and visual enlargement must never enlarge the advertised collision contract.

## 2026-09-10 — A fast chained ramp needs one firing-order owner

**Symptom.** All three final-ramp Surge Turrets were individually valid and correctly placed, but live
descent probes still saw runs such as 1/1/0 shots. The later turret could report `FacingAway` after the runner
had already passed its useful intercept window.

**Root cause.** Static encounter auditing proved that each turret had a safe contact somewhere in its route
window, but sequence advancement transferred turret A's personal 1.1-second refire cooldown to turret B.
After the first speed payout that delay cost about 27 metres, so the second source became eligible only after
the player had passed it. A separate predictor inconsistency could also re-derive facing from a cue-slowed
speed with the old two-step lead instead of the accepted 120 Hz contact path.

**Fix.** `T4_SurgeRoute` remains data-authored from the same three globally configured single-shot Surge
Turrets, but now owns ordered ramp-progress gates at 0/14/28 m through one `ProjectileVolleySequence`.
Each member still performs the normal range, LOS, facing, obstruction and cue-safe flight checks before it
fires; the sequence changes ownership and order, not projectile combat mechanics. Ramp-local perches at
24/36/48 m stay ahead of their respective contact windows, so the normal forward parry cone remains honest.
Only the 0.11-second inter-member recovery transfers; each shooter's rest is enforced when that same shooter
is selected again. T4's authored first-member arm-up is zero because the approach already exposes the row;
the opening tutorial retains its enemy-authored 0.7-second breath. Facing now consumes the terminal direction
from the already accepted flight forecast.

**Invariant.** Per-member geometry proof is not temporal proof for a chained high-speed encounter. When
several one-shot sources form one required rhythm, author one explicit order in level data, never transfer
one member's personal refire clock to another, and validate facing from the same path that will actually fly.

## 2026-09-11 — Item inventory must never replace the persistent wand model

**Symptom.** After picking up or spending an item, and around the death/respawn flow, the equipped wand could
collapse into what looked like a tiny toothpick even though the equipped wand name and index were unchanged.

**Root cause.** `WandController` correctly treated the offhand as a persistent wand and documented itself as
its sole visual owner, but `PlayerItems.Broadcast` still contained an older shared-slot implementation. Every
inventory broadcast destroyed the correctly scaled wand and spawned the current item's much smaller pickup
model in its place. That tiny model remained visible through the death delay; respawn itself correctly rebuilt
the wand. The existing independence test asserted only the wand data and missed the rendered object entirely.

**Fix.** `PlayerItems.Broadcast` now publishes inventory state only. It retains the `OffhandViewmodel`
reference solely to animate item use and originate effects from the visible wand tip. `WandController` is the
only code allowed to replace the held model. Feature verification pins the actual instance reference and local
scale across pickup, successful use, death and respawn.

**Invariant.** Data independence is not visual independence. If a system claims exclusive ownership of a
persistent viewmodel, test the rendered instance across every unrelated lifecycle event—not just its selected
data asset.

## 2026-09-12 — Downhill travel is not a void, and a homing plan is not a wall exemption

**Symptom.** Playtesters died unpredictably while jumping down the opening ramp, and some projectile deaths
appeared to come through the ramp's obstacles.

**Root cause.** `PlayerDeath` inferred a void solely from being nine metres below the last grounded sample.
The opening ramp itself drops 36 metres, so an ordinary downhill jump or a low-frame-rate contact gap could
cross that threshold while the player was still directly above the course. Separately, a projectile proved
its forecast path only at launch; homing could bend the live path into newly intervening solid geometry, but
runtime contact tested only the moving player.

**Fix.** The void check now requires both the existing unsupported vertical drop and the absence of a solid
route surface in a downward sphere probe. Incoming bolts linecast every travelled segment against the same
world mask as the motor and deterministically resolve whichever comes first: solid geometry or the swept
player contact. Projectile damage and parry timing are unchanged.

**Invariant.** A stale grounded height is an early-fall heuristic, not proof that the course disappeared.
Likewise, launch-time clearance does not authorize a live homing curve through later geometry; order world
and player contacts within the frame and resolve only the earliest one.

## 2026-09-12 — A reserved key must be protected on capture and on load

**Symptom.** The customizable keybind page said console and developer keys were reserved, but only Escape
and Backquote were blocked during interactive capture. A valid edited PlayerPrefs JSON blob could also apply
overrides to actions the settings page never exposes.

**Root cause.** The UI contract was enforced as a one-off completion check rather than as a policy on every
input boundary. Unity's complete override JSON can name any binding in the action asset, not only the row that
started the listen.

**Fix.** `InputReader.IsReservedBindingPath` owns the console/developer key list. Interactive completion
rejects it, and saved JSON is filtered after loading so only the exact 24 exposed action/binding pairs survive;
all reserved paths and overrides on hidden actions are removed before play.

**Invariant.** Treat persisted binding JSON as external input. A control reservation is real only when both
interactive capture and settings restoration enforce the same allowlist.

## 2026-09-13 — Developer unlock must refresh dynamic level rows

**Symptom.** Unlocking the developer menu while Level Select was open revealed the static Sandbox row but
not the four parry-recording stages.

**Root cause.** The unlock event refreshed only the prebuilt developer rows. Custom and recording rows are
created dynamically by the full menu refresh, so they remained absent until Level Select was closed and
reopened. The recording scenes also must first be generated by menu step 10.

**Fix.** `DeveloperAccess.Changed` now invokes `MainMenuController.Refresh`, the existing shared path that
updates campaign, static developer, custom and recording rows. Menu step 10 produces the four playable
recording scenes consumed by those rows.

**Invariant.** A capability change refreshes every view derived from that capability, including dynamic
collections; do not update only the prebuilt controls.

## 2026-09-13 — A recording-stage spawn belongs to its surface

**Symptom.** Entering the Downhill Ramp recording stage immediately dropped the player to their death.

**Root cause.** Every recording preset inherited the flat stage's `(0, 1.5, -46)` spawn, but the downhill
ramp begins at about `y = 8`. Its spawn was also one metre behind the ramp edge.

**Fix.** The stage factory derives the downhill spawn from the authored ramp: one metre inside its entry and
1.2 metres above the sloped surface. An asset test pins the longitudinal, lateral and vertical relationship.

**Invariant.** A reusable stage may share a default spawn only while that spawn is validated against the
stage's actual traversal surface.
