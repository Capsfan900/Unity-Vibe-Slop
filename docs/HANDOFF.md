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

Generators run this session: none (no data/prefab changes were made outside Fable's still-running pass).

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

1. **Resolve the portal/pickup question.** Ask the user (or check live) whether they killed both T1 bosses.
   If yes and the realm still didn't clear, the Seraph Lancer death-registration theory (tied to the
   ComboFinisher clip) becomes the live suspect — check whether Fable's pass already fixed it.
2. **Check on the Fable combat-designer pass** (background agent in the previous session; if this is a
   fresh session it will not be resumable — read the uncommitted diff instead). When it's done: review the
   diff against its brief and `[[combat-difficulty-direction]]`, re-run the generators it names plus both
   EditMode and FeatureTests suites, then commit as ONE `[combat-designer]` commit.
3. **Rebuild clean and ask the user before publishing.** Once the tree is clean and the pass is committed,
   rebuild Windows, confirm `build_inputs_dirty=no`, then check with the user before running
   `Tools/publish/Publish-WindowsRelease.ps1` — they explicitly asked to be asked again, not pre-approved.
4. Leftovers from 09-13/09-14, still not started: spell-orb readability capture, disc/wing SFX, V18 carry
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
