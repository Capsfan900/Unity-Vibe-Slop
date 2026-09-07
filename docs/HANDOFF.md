# Handoff — state of play

**Rewritten at the end of every session; describes a moment, not the project.** Read this first when picking
up where the last chat stopped, then [SESSION-PROTOCOL.md](SESSION-PROTOCOL.md).

Last session: **2026-09-06, evening** (Opus 5, three subagent passes, all landed and verified).

## What happened

Three passes, each its own commit so each reverts alone. Newest first.

1. **`5885b9a` level-designer — Level_01 opens up.** The level ran like a corridor set. A sliding player's
   eye sits 0.8 m above the deck, so the shipped 1.2 m rails put the whole world above the eyeline: the
   causeway, the bridge and the boss approach — the three longest straights, and the three places you most
   want to be sliding — were slid **blind, in a trench**. Rails drop to **0.65 m**. The causeway widens
   3 m to 5 m **westward only**, so the wall-run corridor and its 1.0 m standoff do not move. The T2 spiral's
   pads grow, which shrinks its gaps from 4.0-5.1 m to **2.1-3.2 m** against a 4.5 m reach ceiling. Twelve
   route beacons light the deck you are leaving and the one you are arriving on.
   Reshapes are absolute centre/size applied **by name**, so the pass is idempotent and creates and destroys
   nothing — proven by running `8a` twice and diffing the printed summary.
   **The lesson, which cost that pass two regressions: a shorter gap is not automatically a better hop.**
   The real number in `AnalyzeHop` is clean launch points out of 25 sampled take-off spots, and growing a
   *departing* deck moves those samples. `T1_Rail_L` had been written standing ON the widened deck instead of
   flush outside its edge (5/20 to 4/16); `T2_L8` grew east, adding deck on the wrong side of the tower
   (10/25 to 8/25), fixed by growing the **landing** deck instead. `Tools/level_arc_offline.py` had the same
   rail typo as the authoring table, so it agreed with itself and the bug survived a run — it now parses the
   tables straight out of `LevelDefinitionAuthoring.cs` rather than retyping them.
2. **`8d852bc` + `e3f431a` vfx-art-team — fog (plan item A5).** The plan asked for `fogStartDistance` ~30;
   the pass found a colour bug worth more. `fogColor` was `#060D18`, the sky dome's **zenith** — not its
   horizon band (`#13233F`, 4.3x brighter). A first-person game reads forward and slightly down, so distant
   surfaces converged **darker than the sky behind them**: extinction, not haze. Now `#0E1C34`, which sits
   above a shadowed stone face, so distance *lightens* dark geometry. `fogStartDistance` 45 to **36** (the
   quoted 25 m dome radius is a lie by 6 m — the eclipse halo's corners reach 31.2 m), `fogEndDistance`
   240 to **170**.
3. **`b03e336` enemy-designer — `pshooter_enemy03`, the surge turret.** A small round turret built to be
   parried, not fought. 1 HP; posture 200 with no regen, deliberately out of reach so it never opens a duel
   over a body already dead. Each deflected bolt gives +0.12 speed, 5 stacks, ceiling **x1.60**, one stack
   falling every 2 s. Drives `FirstPersonMotor.SpeedMultiplier`, which already scales ground speed, air
   accel, overspeed decay, wall-run top speed and the water floor — no motor change, and `StatusStripView`
   already renders it, so no new HUD. `SurgeTurret` subclasses `EnemyController` and overrides the virtual
   `OnParried`; nothing in `Enemies/Core`, `Projectile`, `PlayerCombat` or the motor was touched.
   Sandbox: a row of three at z -26, each behind its own wake switch.
4. **`a92e811` engineering log — a menu item run over MCP can report success and do nothing.** See below;
   this one will bite the next session if it is not read.

## State of the tree

- **Committed and clean** at `e3f431a`. Nothing uncommitted, nothing stashed, no half-edits.
- Revert points: each pass is one commit — `5885b9a` (level), `8d852bc` (fog), `b03e336` (turret).
  `b00da22` is the last commit before this session.
- **Generators re-run since the last code change, by this session:** `1` (both scenes), `3`, `4`, `7`,
  `8a` (twice, for the idempotency proof), `8`, `Rebuild NavMesh`, `Health Check`. Nothing is stale.

## THE TRAP THAT COST THIS SESSION TIME — read before driving the editor

**`execute_menu_item` over MCP returns `success: true`, logs no error, and does not run the menu item.**
Two generators "succeeded" and produced no asset. Its success flag means the request was delivered, not that
anything ran. Drive the pipeline from `execute_code` instead, and **read back the asset each step is supposed
to produce**:

```csharp
var t = System.Type.GetType("VibeGame1.EditorTools.DataFactory, Assembly-CSharp-Editor");
t.GetMethod("CreateAll").Invoke(null, null);
UnityEditor.AssetDatabase.SaveAssets(); UnityEditor.AssetDatabase.Refresh();
```

Second trap, same family: `Health Check` logs all 1744 warnings as **one** console entry, so a filtered
`read_console` hands back tens of thousands of characters. Capture it via `Application.logMessageReceived`
inside `execute_code` and slice out the `ERRORS` section. Both are in `docs/ENGINEERING-LOG.md`.

## Verification

| Suite | Result | When | Who ran it |
|---|---|---|---|
| EditMode, **full** | **701 / 701**, 0 failed, 0 skipped, 238 s | 2026-09-06 evening, after all three passes | this session |
| Health Check | no errors (1744 warnings, the usual TMP/HUD null baseline) | same | this session |
| Feature suite, play mode | **STALE — never run this session** | last run 2026-09-06 daytime, before all of this | inherited |

The full run includes all 138 `LevelLines` fixtures against the reworked level, which is the category that
matters for the openness pass. **The feature suite is the gap: nothing from this session has been run in
play mode.** Re-run it fresh (check `GameManager.I != null` first — a recompiled session is not a fresh one).

**Nothing from this session has been played by a human.** Specifically unproven:

- Whether a **0.65 m rail** still reads as a rail at speed or as a trip hazard.
- Whether **12 beacons** make the route legible two moves ahead.
- Whether a spiral with every gap at **2.1-3.2 m** against a 4.5 m ceiling is safer but **less exciting** —
  this is the most likely thing to be wrong, and it is cheap to walk back (the table is absolute values).
- Whether **x1.60** on the surge is exhilarating or uncontrollable.
- Whether **38% fog at 87 m** is atmosphere or murk. Measured before/after captures are in `RouteShots/fog/`:
  the sky is pixel-identical and dark pixels lift .0893 to .0966 at 91 m, but the change touches only **0.8%**
  of that frame — most of any frame is fog-immune sky or geometry inside 36 m. Linear fog here is a
  route-preview instrument, not an atmosphere one. `FogEndDistance` is the single constant to nudge.

## Do first next session

1. **Play it.** Sandbox for the turret row (parry-run-parry-run down the three pads, then stand still and
   watch the status strip walk back down); Level_01 for the openness pass — slide the causeway, climb the
   spiral, and judge the rails and the beacons. Everything above is reasoned and tested, not felt.
2. **Re-run the feature suite in play mode** and update `docs/VERIFICATION-REPORT.md`. It is the only suite
   that has not seen this session's work.
3. **Settle `parrySurgeSeconds`** — see the open question below. One number in `DataFactory`, then re-run
   generator `3` and re-assert it in the tests (CLAUDE.md rule 9: a code default is not a shipped value).

## Open questions for the user

1. **The 2 s stack decay on `pshooter_enemy03` is probably now wrong.** It was tuned blind against the OLD
   spiral spacing. The rework dropped hop-to-hop travel to ~0.35-0.5 s, so a player who parries one bolt at
   the bottom of The Ascent still holds stacks four hops later, and x1.60 becomes trivially maintainable
   rather than something you fight to keep. The level pass suggests **1.2 s**, or tying decay to distance
   travelled rather than time. Left alone deliberately — it is a lead call across two passes' work.
2. **`T2_L9` to `T2_L10` was taken from 25/25 to 24/25** on a hop whose gap is now 0.71 m (effectively a step
   across), bought for +3 on the spiral's hardest hop. Trimming `T2_L9` to 5x4 returns 25/25 but drops
   `T2_L8` to `T2_L9` back to no better than shipped. Current call: keep the trade.
3. **Is the spiral now too easy?** Every gap 2.1-3.2 m against a 4.5 m ceiling. Safer, possibly duller.
4. **`SandboxBuilder`'s ambient is still the warm pre-cold-pass set** (`#7A5540` equator vs `#3F5E88` in
   `ProjectSetup`, at `SandboxBuilder.cs:222`), so the workshop lies about how enemies read in the campaign.
   Its own pass; not attempted.
5. **`ARCHITECTURE.md:833` is broadly stale** — still describes the blood-red palette with the pre-cold-pass
   ambient table, and 834 is an orphaned fragment describing the cold one. Only the fog clause was corrected.

## In flight

Nothing. All three subagents completed and their work is committed and verified. No agent is still running.
