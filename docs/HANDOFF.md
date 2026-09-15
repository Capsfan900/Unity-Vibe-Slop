# Handoff — duo realms follow-up + boss AI review (2026-09-14/15)

## Current state

`master` is at `b8dcf5c` (`[Astra] Handoff: duo-realm bugs investigated, boss AI review in flight`).
**Nothing from Fable's pass is committed yet.**

**Mid-session policy change, apply going forward:** the user said Fable-tier should do judgment/design
only, never implementation typing — see `[[fable-brain-only]]` in memory. Fable's still-running pass was
redirected mid-flight: finish only what's already started to a clean stopping point, then switch to a
written spec (no more code) for the rest of its brief; a cheaper (sonnet-tier) worker implements the spec
next, Fable reviews the diff after. **Also:** the user pushed back on agent token cost generally ("shave
down on the agent contracts... they eat tokens and not help") — decision was to keep the `.claude/agents/*`
brief files as-is but tighten delegation habit: prefer `fork` over a fresh agent whenever conversation
context matters, reserve fresh agents for genuinely context-independent work, see the reaffirmed
`[[token-cost-inline-first]]`. No AGENTS.md change — user chose behavior-only, not a project-doc edit.

This session did no direct code edits. It ran three side investigations and one review/correction pass,
all as background agents, in response to the user reporting live bugs while playing the just-shipped duo
realms:

- **T1 exit portal not reopening** and **can't pick up an item in the realm** — both investigated (read-only,
  no edits). Likely the **same root cause**: `BossArenaTrigger` only reopens the realm exit once **both**
  T1 duo bosses (Seraph Lancer + Thirteenth Shade) are dead, and 5 of 12 level pickups — including
  `Pickup_Boss_Hook` — sit inside the realm bubbles, so an uncleared realm reads as "can't pick up" too.
  **Not yet confirmed** — the user has not said whether they killed both T1 bosses or just one. Second
  candidate: Seraph Lancer's `ComboFinisher` animation clip was clamped out of its allowed range (a live
  Unity console warning), which could mean it doesn't cleanly register a kill. See "Open questions" below.
- **Fable combat-designer pass, still running at handoff time (background agent, not a subtask I can
  resume from a new session).** Directive: review + correct the new duo-realm bosses' AI/movesets
  (Seraph Lancer, Orbit Dancer, Cinder Judge, V18 Grappler, Argent Halberdier, Ember Revenant, Pale
  Marionette) starting from the Seraph Lancer clip-clamp warning, **plus** a design direction the user gave
  mid-pass and I relayed to it: movesets need more fluid transitions between attacks, more aggression
  overall, and bosses should be "absurd but fair/doable" — see `[[combat-difficulty-direction]]` in memory,
  this applies to all future boss/enemy work, not just this pass.
  - **As of this handoff it is still editing** — working tree currently has uncommitted changes across 17
    files (138 insertions / 62 deletions), including `Assets/Editor/DataFactory.cs`, `PuppetVisuals.cs`,
    `Projectile.cs`, three `souls_enemies` test files, `PuppetSpinTests.cs`, and — because it edited
    `DataFactory.cs` — the regenerated `.asset` data it writes: `Legendary_{CinderJudge,FlurryBrawlerV18,
    Halberdier,OrbitDancer,Revenant,SeraphLancer}.asset`, `CinderJudge_StormJudgement.asset`, and
    `DeflectSigil/Grapple/Rebound.asset`. Regenerating shipped assets from a `DataFactory` edit is the
    project's correct pattern (hard rule 9), not a mistake — but it means the diff is real gameplay data,
    not just code. **Do not commit yet.** Per the project's worker-pass rule, the lead reviews the full
    diff, re-runs the generators the report names plus both test suites, then makes ONE commit prefixed
    `[combat-designer]` with a `Model: Fable 5.1 (Claude Code)` trailer (and note the lead model too).
    Run `git diff --stat` first to see the current size before assuming this list is still accurate.
  - If a new session opens and this agent is gone (background agents don't survive a session boundary),
    the uncommitted diff above is its unfinished work — read it, decide whether to finish it yourself or
    ask the user, then commit or discard deliberately. Don't leave it uncommitted indefinitely.
- **A Windows build was run and succeeded** (`Builds/Windows/vibegame1.exe`, 112.5 MB, 43 s, 3 scenes, 0
  compile errors, 1 warning) — but it was built from the **dirty** tree above (Fable's in-progress edits),
  so `build-info.txt` shows `git_dirty=YES`. **Publishing to GitHub is deliberately blocked and the user
  chose to be asked again once the tree is clean** — do not tag/push/create a GitHub Release without
  checking with the user first, even once the tree is clean.

**Fable's pass reported back complete** (report received, not yet independently verified by the lead):
- **A1 (fixed):** root cause of the `ComboFinisher` clip-clamp warning was a shared timing bug in
  `PuppetVisuals.PlayAttackClip` (attack strings with a short run-in start the clip too early at the clamp
  floor). Fix holds the pose and starts the clip late so contact still lands on the blow. New
  `PuppetVisuals.ClipStartDelay`, test `PuppetSpinTests.AClipWithAShortRunInStartsLateAndStillLandsItsContactOnTheBlow`.
- **A2 (fixed):** Seraph Lancer's reflected javelin flew under his hover-lifted body (`Projectile.cs`
  aimed at root+1.2m, `SeraphLancerVisuals` lifts 3m) — now uses `shooter.BodyPoint(1.2f)`.
- **A3 (traced, not fixed — new lead for the portal bug):** NOT the boss AI. `BossArenaTrigger`'s
  `sawPartnerAlive` latch (`BossArenaTrigger.cs:107-118`) only latches if `partnerSpawner.Instance != null`
  is observed *after* `triggered` — if the T1 duo partner spawner has no live instance at that exact
  moment, the realm never clears even with both bosses dead. **This supersedes the earlier "did you kill
  both bosses" theory as the more precise next thing to check** — still don't know if the user killed
  both; either way this latch-timing bug is worth checking directly in `BossArenaTrigger.cs`.
- **A4 (finished, was already in-flight before the redirect):** a tempo/aggression data pass across all 7
  new bosses via `DataFactory.cs` (wind-ups untouched — the parry contract — only recovery/combo-gap/
  cooldown/press-on tightened). Full per-boss numbers are in Fable's report text above this doc entry in
  session scrollback; if that's gone, re-ask Fable's agent (name `a39f6769566585318`, if still alive) to
  restate table A4.
- **Gotcha surfaced, worth remembering:** running `3. Create Data` alone (without `3b`/`4` after it)
  clears `viewmodelPrefab` on `DeflectSigil/Grapple/Rebound.asset`. Fable restored those 3 files from HEAD
  this time — verify they're still intact (`git diff Assets/Data/Items/`) before assuming the tree is
  clean, and always run Create Data as part of `3 -> 3b -> 4` or `0. Rebuild Everything`, never alone.
- **B: a full spec (S1-S7) for a cheaper model to implement next**, ranked, covering: duo arbitration
  ignoring in-flight projectiles (S1, high), chaining attack phrases for fluid/aggressive transitions (S2,
  high — this is the direct answer to the user's "more fluid, more aggressive" direction), whether the
  Judge's shield/storm stances should stop blocking the duo partner (S3), whether V18's grab should be red
  (unblockable) since it currently punishes a Block which breaks the "never punish a guard on a blue" rule
  (S4), concrete "absurd" spectacle escalations for each boss (S5), Marionette tempo math (S6, low), a
  harmless false-positive in whiff scoring (S7, ignore unless reported). Full detail is in Fable's report
  in scrollback — if starting a fresh session, ask Fable's agent to restate section B verbatim before
  implementing, don't reconstruct from memory.
- **Before committing (not done yet):** re-run Full EditMode Tests + Level_01 FeatureTests (Fable only ran
  Quick EditMode: 1317/1317, both assemblies compile). No generator re-run needed for A1-A4; S5(i)/(iii)
  will need `Projectile Encounter Report` re-run if implemented (live-cap changes only).

Generators run this session: `3. Create Data` (by Fable, as part of A4 — see gotcha above). Level Arc
Report, Projectile Encounter Report and Health Check earlier this session were run against the pre-Fable
tree (`4723f60`) and are now stale for the changed files; re-run is only required for S5(i)/(iii) per above.

## Verification (2026-09-14, this session)

Re-ran the three checks flagged as outstanding in the previous handoff, against the `4723f60` tree
(before Fable's pass started):

- **Health Check:** 0 errors, 3338 warnings — all the same pre-existing "may be wired at runtime"
  HUD.prefab noise, nothing new.
- **Level Arc Report:** `VERDICT: every authored traversal has a clean arc.`
- **Projectile Encounter Report:** `VERDICT: PASS` (every sequence READY at all three route speeds).

Not re-run since: EditMode/FeatureTests suites (last known-good numbers are the previous session's
1357/1357 EditMode, 797/0/2 FeatureTests — inherited, not re-verified this session), and nothing has been
re-run against Fable's in-progress edits yet — that's required before committing them.

**Human-only, still open:**
- Whether both T1 duo bosses were actually killed (see below).
- Duo difficulty/fairness in general — untested since the shrink.
- Whether the 20 m ceiling crowds the Lancer's hover or the Judge's storm.

## Next action

1. **Fix the actual portal bug now identified:** `BossArenaTrigger.cs:107-118`'s `sawPartnerAlive` latch —
   check whether it's null-observed too early for the T1 duo partner spawner. This is a small, targeted fix
   (implementation, not design judgment — a cheaper model can do it) once confirmed. Still worth asking the
   user whether they killed both T1 bosses, as a second data point.
2. **Review and commit Fable's finished A1/A2/A3(trace)/A4 pass:**
   - Verify: `git diff --stat` (expect ~17 files: PuppetVisuals.cs, Projectile.cs, DataFactory.cs, 3 test
     files, PuppetSpinTests.cs, DATAFLOW.md, 7 regenerated `.asset` files under Data/Attacks and
     Data/Enemies/souls_enemies), confirm `Assets/Data/Items/{DeflectSigil,Grapple,Rebound}.asset` were
     NOT left with cleared `viewmodelPrefab` (Fable says it restored them from HEAD).
   - Run Full EditMode Tests + Level_01 FeatureTests (only Quick EditMode 1317/1317 has been run so far).
   - Commit as ONE `[combat-designer]` commit, trailer `Model: Fable 5.1 (Claude Code), lead: <session
     model> (Claude Code)`.
   - Human playtest still needed: the Lancer's Jab2/Swing/Finisher string feel, in Sandbox.
3. **Implement spec B (S1-S7)** with a cheaper model once A/A4 is committed — S2 (chained attack phrases)
   is the direct answer to the user's "more fluid, more aggressive" request and should probably go first;
   S3/S4/S5 need the user's taste call per Fable's own notes (see report in scrollback / ask
   `a39f6769566585318` to restate section B if it's gone). Fable reviews the implementer's diff after.
4. **Rebuild clean and ask the user before publishing.** Once the tree is clean and both passes are
   committed, rebuild Windows, confirm `build_inputs_dirty=no`, then check with the user before running
   `Tools/publish/Publish-WindowsRelease.ps1` — they explicitly asked to be asked again, not pre-approved.
5. Leftovers from 09-13/09-14, still not started: spell-orb readability capture, disc/wing SFX, V18 carry
   aim assist, the boss-intro follow-ups not yet picked (music stingers, deathblow/kill-cam + slow-mo,
   "VICTORY ACHIEVED" banner — see previous handoff, still undecided).

## Open questions for the user

- Did you kill **both** T1 duo bosses (Seraph Lancer + Thirteenth Shade), or just one, before the exit
  didn't reopen and the item wouldn't pick up? This determines whether there's a real bug left to fix.

## Working rules learned

- **Inline first:** worker lanes cost more tokens than they save. Delegate only large, independent work.
- **Offline `dotnet build` fails in this shell** (NuGet `path1` null). Compile through Unity instead: refresh, then
  poll `isCompiling` and a reflection probe for the new symbol.
- **Never `git stash` with the editor open.**
- **Ask before stopping play mode.** The user may be playing.
- **`execute_code` calls need an explicit `"action"` field** (e.g. `{"action":"execute","code":"..."}`) —
  omitting it fails validation even though other tools like `manage_editor` also gate on `"action"`.
- **The `session-handoff` skill is currently disabled for model invocation** (`skillOverrides` in
  `.claude/settings.local.json`, same as `dashboard` per CLAUDE.md) — this handoff was written by following
  `.claude/skills/session-handoff/SKILL.md` manually. Worth re-enabling if this keeps happening.

## Preserved user-owned files

Do not stage, remove, overwrite, or relocate:

- `.claude/settings.local.json`
- `Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and `.meta`
- `Portraits/`
- all `RouteShots/` files and directories
- `output/` (untracked)
- `Tools/dashboard/__pycache__/build_dashboard.cpython-312.pyc` (build artefact; do not commit)
