# Handoff — boss AI pass shipped, realm portal fixed (2026-09-14)

## Current state

`master` is clean (apart from the preserved user-owned files below). This session committed:

- `493263a` `[combat-designer]` Fable's reviewed pass: `PuppetVisuals.ClipStartDelay` (clip late-start, fixes
  the ComboFinisher clamp warning), reflected javelins aim at `shooter.BodyPoint(1.2)`, tempo/aggression
  pass on six duo bosses (wind-ups untouched). Tag `pre-boss-tempo-2026-09-14` sits before it.
- `c269833` `[Astra]` **Realm exit never reopening:** `BossArenaTrigger.Update` short-circuited on the main
  spawner, so a duo partner killed FIRST (destroyed 1.5 s later) was never latched as seen-alive and the
  arena never cleared. Both spawners are now evaluated every frame. Likely also the "can't pick up an item
  in the realm" report (pickups sit inside realm bubbles). **Unplayed.**
- `162c40a` `[Astra]` Fable's spec S1-S5, with the user's taste calls. Full spec + decisions:
  [BOSS-AI-SPEC-2026-09-14.md](BOSS-AI-SPEC-2026-09-14.md).
  - S1 duo arbitration refuses a commit while any bolt/disc/javelin lands within 0.9 s.
  - S2 phrase chaining (`EnemyController.TryChainPhrase` / `ShouldChainPhrase`): aggression roll, recovery
    <= 1.0, player in band, max 2 phrases back to back.
  - S3 `EnemyData.stanceFreesPartner` (Cinder Judge only): the Aegis raise holds no slot, V18 presses.
  - S4 `BrawlerV18_Grab` is now RED.
  - S5 Lancer 4 javelins (strike 3.85, liveCap 5); Dancer whirl 3 discs / 4 bounces; Judge Aegis = raise, bash, Heavy.
  - Not done by user choice: V18 higher suplex. Skipped (low): S6 Marionette tempo, S7 whiff false-positive.

## Verification (this session, against `162c40a`)

- Full EditMode 1361/1361 · FeatureTests 797/0/2 · Projectile Encounter Report PASS.
- Generators run: `3 -> 3b -> 4 -> 4b`. They re-serialise every Legendary animator controller, `Player.prefab`,
  `VM_Spellbook.prefab` and `pshooter_enemy01.prefab` in a new order with identical content (balanced
  numstat); that churn was discarded with `git checkout`, not committed.
- Health Check and Level Arc Report not re-run (no level geometry changed).

**Human-only, still open:** kill the T1 partner first and confirm the exit opens; feel of phrase chaining;
Lancer's 4-javelin verdict; Dancer's 3-disc whirl; Judge shield-into-dive; V18 attacking during the Judge's
shield in T4; the red V18 grab.

## Next action

1. User playtest of the list above; retune from their notes.
2. Rebuild Windows clean (`build_inputs_dirty=no`), then **ask the user** before
   `Tools/publish/Publish-WindowsRelease.ps1` — they asked to be asked, not pre-approved.
3. Leftovers, not started: spell-orb readability capture, disc/wing SFX, V18 carry aim assist, boss-intro
   follow-ups (music stingers, deathblow kill-cam + slow-mo, "VICTORY ACHIEVED" banner — undecided).

## Working rules learned

- **Play-mode domain reloads** happen when the MCP bridge reconnects (e.g. after a `/model` switch). Always
  read `FeatureTestRunner.Start()`'s RETURN value — it refuses with an ERROR when `GameManager.I` is null.
  Stop and re-enter play mode, then start again.
- **`mcp_call.py` takes bare tool names** (`execute_code`, not `mcp__UnityMCP__execute_code`). A Bash loop
  over it polls the feature suite without burning context.
- `LockOn_AssistRecentresTarget` is a known flake (real-time wait); rerun before believing it.
- Running `Create Data` alone clears `viewmodelPrefab` on `Items/{DeflectSigil,Grapple,Rebound}` — always `3 -> 3b -> 4`.
- **Never `git stash` with the editor open. Ask before stopping play mode you did not start.**
- A subagent's report lives only in the transcript; save any spec it produces to `docs/` the same session.

## Preserved user-owned files

Do not stage, remove, overwrite, or relocate:

- `.claude/settings.local.json`
- `Assets/Resources/Audio/Radio/Level_01/FineArt & jazza's dance party - Eyes Wide Shut.mp3` and `.meta`
- `Portraits/`
- all `RouteShots/` files and directories
- `output/` (untracked)
- `%SystemDrive%/` (untracked, unknown origin — leave it)
- `Tools/dashboard/__pycache__/build_dashboard.cpython-312.pyc` (build artefact; do not commit)
