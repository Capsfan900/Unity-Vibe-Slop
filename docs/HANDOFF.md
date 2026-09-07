# Handoff — state of play

**Rewritten at the end of every session; describes a moment, not the project.** Read this first when picking
up where the last chat stopped, then [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md).

Last session: **2026-09-07** (Opus 5), third session that day. Two commits, landed, tree clean.
**Both halves of the build pipeline are now proven, and the feature suite has been re-run against the
slimmed project.** No gameplay code changed this session — it was verification and diagnosis.

## What happened

**`aa72712` — the WebGL build exists.** It had never been cut; it was the last unproven half of the
pipeline. `BuildRunner.Preflight()` came back clean (`windowsSupported=True webglSupported=True
stripping=High dirtyScenes=0`, scene list `MainMenu → Level_01 → Sandbox`), and the build ran in **4:56**
— far under the 10–20 min an IL2CPP-to-WASM build is usually braced for.

**The number that matters is 30.8 MB, and it is already gzipped.** Unity ships the `.unityweb` files
pre-compressed (they carry the `1f8b` magic), so that is the wire size a playtester on a link actually
waits for, not a pre-compression figure. Uncompressed the payload is 58.7 MB: `wasm` 31.0 → 8.7,
`data` 27.4 → 22.0, `framework` 0.3 → 0.1, plus a 48 KB uncompressed loader. Compare the Windows build at
95.6 MB. Three warnings, all benign — IL2CPP splitting three large TextMeshPro methods into their own
`.cpp` files. Zero errors.

**Proven as far as it can be without a browser.** All four `Build/` assets plus `index.html` serve
**HTTP 200 at full length** from a plain static server, which also exercises the gzip decompression
fallback `BuildRunner` enables — the thing that lets a gzipped build load from a server sending no
`Content-Encoding` header, GitHub Pages included. The `Playtest` template's click-to-play pointer-lock gate
and progress bar are present in the served `index.html`. **Nobody has opened it in a browser**, so the WASM
has never been instantiated and the gate has never been clicked. That is the one remaining WebGL unknown.

Two side effects of a WebGL build, both real, both now handled. `ProjectSettings.asset` goes dirty because
`BuildRunner` enforces `webGLTemplate: PROJECT:Playtest`, `webGLCompressionFormat: 1` and
`webGLDecompressionFallback: 1` — by design, so a build never depends on what someone last clicked. And
**Burst spills `Data/lib_burst_generated.{cpp,wasm}` into the project root**, written relative to the
working directory; referenced nowhere in `Assets/`, now covered by `/[Dd]ata/` in `.gitignore`.

**`1edf04b` — the feature suite passes after the package removal, and backlog 5c is narrowed.**

`777 / 777, 0 failed, 0 skipped` in 60.7 s, on `Level_01`, with **both** guards asserted in the same call
that started it (`warm=True timeScale=1 playing=True`). That closes the last inherited-not-re-run claim:
removing six packages and raising managed stripping to `High` regressed nothing behavioural, as EditMode
722/722 had already suggested.

**Backlog 5c (the Legendary Ninja riposte framing) is narrowed, not fixed**, and the two obvious suspects
are ruled out. All seven `Legendary_*` prefabs were probed at rest — in the editor, no play mode — at the
standoff the test actually uses (`Min(stabStandoff × scale, range)`: 2.20 m at ×1, 3.50 m at ×2.2):

| Body | ×1.0 | ×2.2 | nearest renderer |
|---|---|---|---|
| Halberdier | 1.13 m | **1.15 m** (tightest) | `EnemyMesh` |
| Revenant | 1.36 | 1.64 | `EnemyMesh` |
| Spellsword | 1.55 | 2.06 | `EnemyMesh` |
| Knight / Drillmaster | 1.58 | 2.15 | `EnemyMesh` |
| **Ninja** | **1.86** | **2.75** | **`Body`** |
| Marionette | 1.98 | 3.01 | `EnemyMesh` |

Nothing is inside the camera and nothing is near the 0.5 m framing floor — **and the Ninja is the
second-roomiest of the seven.** So the reported `nearest=0.00 m, cameraInsideBody=True` is not resting
geometry. The stagger pose is out too: `EnemyVisuals.StaggerEuler` is `-13°` and `StaggerSag` is `-0.14` in
z, leaning the body **back** from the lens — that fix already landed. **The failure is live-only.**

Leading candidate: `DeathblowFraming` computes its stand point from `dummy.transform.position` and only
*then* waits 0.35 s realtime for the pose to settle, so a body still closing distance during that wait ends
up nearer than the standoff it was measured for. Worth noting the Ninja is the only one of the seven whose
nearest renderer is a primitive `Body` rather than a forge `EnemyMesh` — it is built down a different path.

**A live repro needs a deliberate spawn.** `Level_01` has **zero** enemies in play mode — the parkour pivot
removed the filler spawns — so this cannot be reproduced by entering play mode on the shipped level. That
is why it is still open.

Also corrected in `BACKLOG.md`: `markHeight = 1.28` is the **Drillmaster's** spec, not the Ninja's, and only
five of the seven bodies carry an explicit `markHeight` in `MiniBossFactory` (the rest fall back to the
`1.45` default at line 180). Confirm ownership before moving any of them.

## State of the tree

- **Committed and clean** at `1edf04b`.
- **Tags**: `pre-buildpipeline-2026-09-07` at `23b53a3`, plus `pre-distribution-2026-09-07` and
  `pre-weapons-2026-09-06`. No new tag this session — neither commit touches gameplay, so there is nothing
  a revert would need to isolate.
- **Generators re-run since the last code change: none were needed.** No `DataFactory`, material or prefab
  change has landed since 2026-09-05; this session touched two docs, `.gitignore` and `ProjectSettings`
  only. Steps 2/3/4 remain as the 2026-09-05 session left them.
- **Expect `ProjectSettings.asset` and `Assets/Settings/*` to go dirty after any build.** URP shader-prefilter
  churn is bookkeeping Unity rewrites, not a change anyone made; `git restore Assets/Settings ProjectSettings`
  is safe when the diff is only prefilter flags. (`git checkout --` is blocked by the permission classifier
  here; `git restore` is not.)
- **One worktree still deliberately in place**, unchanged across four handoffs now:
  `.claude/worktrees/agent-ac943965c5ea99158`, holding `d3b6245 "Span 4: the Warden causeway"`, which
  exists on no other branch. See "Open questions".

## Verification

| Suite | Result | When |
|---|---|---|
| `FeatureTests`, play mode | **777 / 777, 0 failed, 0 skipped** (60.7 s) | 2026-09-07, **run this session**, both guards asserted |
| EditMode, full | 722 / 722, 0 failed (218 s) | 2026-09-07 earlier session — **inherited**, but taken after the same package removal |
| `Health Check` | 0 errors, 1748 known warnings | 2026-09-07 earlier session — **inherited** |
| Windows build boots | **PASS** — reaches the menu, clean `Player.log` | 2026-09-07 earlier session — **inherited** |
| WebGL build | **BUILT** — 30.8 MB gzipped, 4:56, 0 errors; all assets serve HTTP 200 | 2026-09-07, **run this session** |
| WebGL build *runs* | **NEVER OPENED IN A BROWSER** | — |

**What is still unproven.** The WebGL build has never been instantiated by a browser, so the WASM, the
pointer-lock gate and the frame rate are all unexercised — serving proves only that the bytes are reachable
and correctly laid out. And **nothing in the weapon pass has been played by a human**, unchanged across
four handoffs; it is still the only thing that can settle whether Rosethorn at 9 base damage reads as a
breaker or just as weak.

When reading `Health Check`'s output, filter the console — an unfiltered read of its 1748 warnings costs a
large chunk of context for no information. The result line is what matters.

## Do first next session

1. **Open the WebGL build in a browser.** `cd Builds/WebGL && python -m http.server 8123 --bind 127.0.0.1`,
   then `http://127.0.0.1:8123` — `file://` will not work. This is a five-minute job that closes the last
   unknown in the whole distribution pipeline, and it needs a human with a browser. Watch for: the WASM
   instantiating at all, the click-to-play gate taking pointer lock, and whether it holds frame rate.
2. **Get the two blocked decisions out of the user** (both below). They have now been carried across two
   handoffs, and the F10 one blocks publishing anything to a playtester.
3. **Pick up backlog 5c with a live repro** — spawn a `Legendary_Ninja` in the **sandbox** (not `Level_01`,
   which has no enemies), break its posture, and read the `Deathblow_StaggerPoseClearsNearPlane_*` failure
   message; it names the offending renderer. The measurement table above tells you what it is *not*, which
   is most of the work.

## Open questions for the user

1. **The Resolution row** — `SettingsMenu.cs:439-442` destroys the `0/0` "native" sentinel: cycling the row
   once writes a concrete pair that persists forever. The user's instruction was *"it just needs to detect
   native and use that, nothing else, don't overcomplicate those"*. Two shapes: **delete the row** (native
   becomes unloseable, display-mode row kept — the recommendation) or **add a "Native" entry at index 0**
   that writes `0/0` back. Touches a Fable system, so it needs the user's word in-session. **Asked twice,
   not yet answered.**
2. **F10 in a shipped build** — `LevelEditor.cs` gates only its EXPORT button, so a playtester who presses
   F10 gets the fly-cam editor over their run. One `#if UNITY_EDITOR || DEVELOPMENT_BUILD` around the key
   read fixes it. The real question is whether trusted testers *should* have it. **This blocks publishing.**
3. **GitHub Pages needs one-time clicks only the user can make**: Settings → Pages → Deploy from a branch →
   `gh-pages` → `/ (root)`. The branch need not exist first. Result:
   `https://capsfan900.github.io/Unity-Vibe-Slop/`.
4. **The unmerged Span 4 worktree** — `d3b6245` is real level work on a branch never merged, in a worktree
   far behind master (it deletes 126k lines relative to master, so a merge needs care, not a fast-forward).
   Cherry-pick, redo on master, or drop?
5. **Two Fable retunes waiting on a yes/no**: per-weapon camera kick (`WeaponController` calls
   `CameraShake.I.Small()` for every weapon, so a maul hit shakes exactly as hard as a needle flick — named
   by the combat lane as the largest remaining weight gap), and the surge decay
   (`pshooter_enemy03.parrySurgeSeconds` 2.0 s, tuned against the *old* spiral spacing; `BACKLOG.md` has the
   shape).

## In flight

Nothing running. No subagents were used this session.

**A task list was rebuilt from `BACKLOG.md` this session and is worth reconstructing** — task lists are
session-local and do not survive a `/clear`. The 14 items, with the four marked `[NEEDS USER]` being
questions 1, 2, 4 and 5 above: re-run the feature suite (**done**), commit the WebGL build (**done**),
the Resolution row, the F10 gate, Legendary Ninja framing, per-weapon camera kick, per-weapon hit reaction
(`EnemyVisuals.HitFlash` is a fixed tint pop, so an 88-damage finisher and a 9-damage jab read
identically), the swing arc (`WeaponViewmodel.cs:268` lerps position linearly; `GuardArc` at line 412 is
the fix pattern already in this project), the Sunbreaker hue (`Hammer.neon` sits ~5° from
`Projectile.HotCore` — the player's weapon should not share the bolt's colour in a parry game), arms
reacting to movement (backlog 2b), the surge decay, the sandbox's stale warm ambient
(`SandboxBuilder.cs:222` — the workshop lies about how enemies read in the campaign), the stale palette
section at `ARCHITECTURE.md:833`, and the Span 4 worktree.

---

## Restore phrase

**Paste this into a fresh session verbatim. It is the whole boot sequence.**

    Read docs/HANDOFF.md, then CLAUDE.md. This is vibegame1: melee-only first-person parry
    speedrun platformer, namespace VibeGame1, Unity 6000.5.10f1, C# 9, URP.

    State: clean at 1edf04b. BOTH build targets are now proven - Windows boots, and WebGL
    was cut 2026-09-07 at 30.8 MB gzipped in 4:56 with 0 errors, every asset serving
    HTTP 200. Feature suite 777/777 was re-run this session with both guards asserted;
    EditMode 722/722 and Health Check 0 errors are inherited from earlier the same day.
    Tag pre-buildpipeline-2026-09-07 sits at 23b53a3.

    Do this first, in order:
    1. Confirm the Unity editor is open on this project (PowerShell: Get-Process Unity).
       Everything goes through the user's open editor - there is no headless copy. Drive it
       with .claude/skills/unity-editor/mcp_call.py, run from the project root.
    2. Open the WebGL build in a browser - the last unknown in the whole pipeline.
       cd Builds/WebGL && python -m http.server 8123 --bind 127.0.0.1, then hit
       http://127.0.0.1:8123 (file:// will not work). Check the WASM instantiates, the
       click-to-play gate takes pointer lock, and it holds frame rate.
    3. Rebuild the task list from docs/BACKLOG.md - task lists are session-local and do not
       survive a /clear. HANDOFF's "In flight" section lists the 14 items and which four
       are blocked on the user.

    Five rules that override instinct:
    - A cold build drops the MCP websocket, so execute_code returns success:false with a null
      message for a build that SUCCEEDED. Read Builds/last-build.txt. Run
      BuildRunner.Preflight() first when anything looks wrong; it checks everything and
      builds nothing.
    - execute_menu_item over MCP returns success:true and does nothing. Use execute_code and
      read back the asset every step is supposed to produce.
    - Generators 3 and 4 are ONE operation. Running 3 alone silently nulls every item's
      viewmodelPrefab. Check git status over the whole tree, not the file you edited.
    - Rule 9: a code default is not a shipped value. Changing a field initialiser does nothing
      to a ScriptableObject that already exists. Write it in DataFactory, re-run, read it back.
    - A feature-suite result is only evidence if Time.timeScale == 1 when it started.
      GameManager.I != null proves the session is warm, NOT that the world is running - a
      paused run reports ~49 plausible failures across unrelated systems.

    Level_01 has ZERO enemies in play mode since the parkour pivot. Any enemy-facing test
    needs a deliberate spawn, in the sandbox. This is why backlog 5c is still open.

    Never smoke-test a build with -screen-width/-screen-height flags: Unity PERSISTS them to
    HKCU:\Software\vibegame1\vibegame1, so they poison every later flagless launch.

    Model boundary: an Opus session ADDS features and does not rewrite or retune a system
    Fable wrote unless the user says so in that session. Subagents REFINE only - they never
    invent a mechanic, input, resource or screen; they propose it and stop. The lead owns the
    editor, commits one commit per worker pass, and tags before a batch.

    Two decisions are blocked on the user and have been carried across two handoffs: the
    settings-menu Resolution row (delete it vs a "Native" entry at index 0) and whether
    playtesters should have F10. The F10 one blocks publishing anything.

    Then ask me what to work on.
