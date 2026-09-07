# Handoff — state of play

**Rewritten at the end of every session; describes a moment, not the project.** Read this first when picking
up where the last chat stopped, then [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md).

Last session: **2026-09-06, late** (Opus 5). Four commits, all landed. **The weapon pass is committed but
NOT fully verified — see "The one thing you must do first".**

## What happened

Newest first. Each pass is one commit, so each reverts alone.

1. **`79e5a1f` vfx-art-team + audio-engineer — the weapon look and sound.** Two lanes in ONE commit because
   both had to edit `WeaponController.DoHit`; they cannot be split. The find worth knowing: the melee hit
   confirm was **the loudest effect in the game**. `SpawnSpark` drew an unpooled sphere at `neon * 4`, so a
   routine hammer chip rendered at **3.51** — above the unblockable alert tell (3.00), the deathblow mark
   (2.60) and the deflect body flash (3.20) that is meant to be the loudest moment in the game. Everything
   else goes through `SlashFx`, which caps at 1.0; the one effect that skipped it out-shouted every alarm.
   Now `WeaponImpactFx`: pooled, capped, sparks thrown back out of the wound, a shockwave for the maul
   alone, every number interpolated from the shipped `attackDuration` ladder (no new data field).
   `M_WeaponCore` went `#08070C` to `#2B313C` — the weapon's mass had been **3.6x darker than the glove
   holding it**. On the audio side all three weapons played the same clash and the same connect sound;
   `WeaponAudio` now reads the shipped `hitStopSeconds` into light/mid/heavy bands, so a future weapon gets
   weight-appropriate sound from its own numbers. Four synthesized voices, no new files, no `Sfx` reorder.
2. **`d0b8b7a` combat-designer — the roster becomes a CROSSING, not a ladder.** The two posture channels now
   run opposite to health damage, so each weapon wins one column outright and loses one outright.
   Rosethorn (dagger) is the **breaker**: worst killer, best breaker, its deathblow IS its kill
   (`baseDamage` 12 to 9, `postureDamage` 6 to 13, `executeDamage` 250 to **320** because 250 sat *under*
   Iron Penitent 260 — the breaker could break what it then could not cash). Sunbreaker (hammer) is the
   **crusher and the deflect weapon**: gives up hit-posture (34 to 26), breaks through the parry at 40 a
   deflect (`baseDamage` 46 to 52, `hitStopSeconds` 0.11 to **0.085** because 0.11 EXCEEDED
   `GameFeel.parryHitStop` 0.09 — an ordinary swing was the biggest beat in the game). Cerulean Edge
   (sword) untouched to the number: it is the calibration reference the Marionette and Iron Penitent fights
   are built on.
3. **`e387856` engineering log — generator 3 alone nulls every item viewmodel.** Read it. It cost this
   session a near-miss and it will bite again.
4. **`c3c5f87` the surge decay settles at 1.5 s**, and the flare toss red is named as noise. The reasoning
   the two previous passes both missed: `ParrySurge.Grant` **resets** the drop timer, so this number never
   governs a player still inside a turret's 1.1 s bolt metronome — it governs the **run-out after the last
   turret**. At 2 s that run-out was 10 s, long enough to carry a full x1.60 ladder out of the span that
   earned it. 1.2 s (the level pass's suggestion) leaves only 0.1 s of slack over the metronome, so a parry
   landing a fraction late bleeds a stack and the ladder reads as random. 1.5 s gives a 7.5 s run-out and
   0.4 s of slack.

## The one thing you must do first

**Re-run the EditMode suite and the feature suite, and update `docs/VERIFICATION-REPORT.md`.**

The weapon pass is committed on **721/722 EditMode**, and the single red was a bad assertion in the VFX
lane's own new test (it asserted every weapon's hue normalises to *exactly* 1.0; `SlashFx.Normalise` is a
**ceiling**, so Rosethorn's `#5FD66A` correctly stays at 0.839). That assertion is fixed in `79e5a1f`, but
**the fix has not been re-run** — the user entered play mode as the re-run started, so the job is
untrustworthy and the scene save was refused. Assume nothing; run it.

The feature suite has not been run against the weapon pass at all. Neither has `Health Check`.

## State of the tree

- **Committed and clean** at the handoff commit. Nothing uncommitted, nothing stashed.
- **Tag `pre-weapons-2026-09-06`** sits immediately before the weapon batch. `git reset --hard` to it undoes
  the whole weapon pass in one move; `git revert 79e5a1f` or `git revert d0b8b7a` undoes a single lane.
- **Generators re-run since the last code change:** 2, 3, 4 (3 and 4 together), values read back off disk.
  `Health Check` and both suites are the gap.

## Two traps, both of which cost this session real time

1. **Generator 3 alone silently nulls every item's `viewmodelPrefab`** — those links are written by step 4.
   The asset you were editing looks perfectly correct, so a `git commit -am` ships the player holding
   nothing. **Steps 3 and 4 are ONE operation**, and the check is `git status` over the whole tree, not the
   file you aimed at. Full write-up in `docs/ENGINEERING-LOG.md`.
2. **The FIRST feature-suite run after a domain reload is a lie.** A run started right after a recompile
   reported **16 failures** across Guard, Posture, Weapons, WandPedestal, Deathblow and LockOn, and took
   108 s. Stopping play mode, re-entering and re-running gave **776/1** in 63 s, then **777/777**. The cold
   session steals frames and every timing-sensitive check fails. `GameManager.I != null` is necessary but
   **not sufficient** — stop, re-enter, and only trust the second run.

## Verification

| Suite | Result | When |
|---|---|---|
| Feature suite, play mode | **777 / 777 / 0 skipped**, 64 s | before the weapon pass |
| EditMode, full | **701 / 701** | before the weapon pass |
| EditMode, full | **721 / 722** — the one red fixed in `79e5a1f`, **fix unverified** | during the weapon pass |
| Feature suite vs the weapon pass | **NEVER RUN** | — |
| Health Check vs the weapon pass | **NEVER RUN** | — |

**Nothing in the weapon pass has been played by a human.** Specifically unproven: whether the dagger at 9
base damage feels like a breaker or just feels weak (it takes ~27 swings to kill a Revenant if you refuse
the deathblow — intentional, but it only works if breaking reliably leads to a cashed execute); whether the
hammer's mechanical creak reads as tension or as input latency; whether the new hit confirm reads at all now
that it is no longer the brightest thing on screen.

## Open questions for the user

1. **Per-weapon camera kick.** `WeaponController` calls `CameraShake.I.Small()` for every weapon, so a maul
   hit shakes exactly as hard as a needle flick. Named by the combat lane as the single largest remaining
   weight gap. One line.
2. **The Sunbreaker is amber, and amber is the bolt.** `Hammer.neon` sits about 5 degrees of hue from
   `Projectile.HotCore` — the colour the player is trained to read as "deflect this at 32 m/s" — and the
   trail paints it across the frame on every strike. The VFX lane recommends a red-ember
   `(0.90, 0.30, 0.16)` or an explicit written acceptance of the collision.
3. **The swing is a straight chord, not an arc.** `WeaponViewmodel.cs:268` lerps position linearly; only the
   rotation slerps, so the hand travels straight. The project already has the fix pattern (`GuardArc`, line
   412, bows the grip on a half-sine). Costs no timing and no data.
4. **Per-weapon hit reaction.** `EnemyVisuals.HitFlash` is a fixed tint pop, so an 88-damage finisher and a
   9-damage jab read identically on the enemy.
5. **Is the spiral now too easy?** Every gap 2.1-3.2 m against a 4.5 m reach ceiling. Safer, possibly
   duller. Cheap to walk back — the table is absolute values.
6. **`SandboxBuilder`'s ambient is still the warm pre-cold-pass set** (`SandboxBuilder.cs:222`), so the
   workshop lies about how enemies read in the campaign.
7. **`ARCHITECTURE.md:833` is broadly stale** — still the blood-red palette and the pre-cold-pass ambient.

## In flight

Nothing. All three weapon subagents completed and their work is committed.

---

## Restore phrase

**Paste this into a fresh session verbatim. It is the whole boot sequence.**

    Read docs/HANDOFF.md, then CLAUDE.md. This is vibegame1: melee-only first-person parry
    speedrun platformer, namespace VibeGame1, Unity 6000.5.10f1, C# 9, URP.

    State: clean at the handoff commit; tag pre-weapons-2026-09-06 sits before the weapon
    batch. A three-lane weapon pass (roles, VFX, audio) is committed but NOT fully verified.

    Do this first, in order:
    1. Confirm the Unity editor is open on this project (PowerShell: Get-Process Unity).
       Everything goes through the user's open editor - there is no headless copy. Drive it
       with .claude/skills/unity-editor/mcp_call.py, run from the project root.
    2. Run the full EditMode suite. Expect 722/722; the last run was 721/722 and the fix for
       that red is committed but unverified.
    3. Run VibeGame1/Health Check. Capture it inside execute_code - a filtered read_console
       returns tens of thousands of characters because it logs 1744 warnings as ONE entry.
    4. Run the play-mode feature suite. Enter play mode, EXIT, RE-ENTER, then run - the first
       run after a domain reload reports about 16 phantom failures. Expect 777+ and 0 failed.
    5. Update docs/VERIFICATION-REPORT.md with what you actually measured.

    Three rules that override instinct:
    - execute_menu_item over MCP returns success:true and does nothing. Use execute_code and
      read back the asset every step is supposed to produce.
    - Generators 3 and 4 are ONE operation. Running 3 alone silently nulls every item's
      viewmodelPrefab. Check git status over the whole tree, not the file you edited.
    - Rule 9: a code default is not a shipped value. Changing a field initialiser does nothing
      to a ScriptableObject that already exists. Write it in DataFactory, re-run, read it back.

    Model boundary: an Opus session ADDS features and does not rewrite or retune a system
    Fable wrote unless the user says so in that session. Subagents REFINE only - they never
    invent a mechanic, input, resource or screen; they propose it and stop. The lead owns the
    editor, commits one commit per worker pass, and tags before a batch.

    Then ask me what to work on.
