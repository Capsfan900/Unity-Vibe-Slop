# Verification report

What is actually proven about `vibegame1`, how it was proven, and — just as important — what is **not**.

Run date: this session. Reproduce with the commands in [TOOLING.md](TOOLING.md).

Related: [TOOLING.md](TOOLING.md) · [ENGINEERING-LOG.md](ENGINEERING-LOG.md) · [ARCHITECTURE.md](ARCHITECTURE.md)

---

## Results

| Suite | Scope | Result |
|---|---|---|
| EditMode tests | Pure functions — `ParryMath`, `PostureMath`, `UpgradeMath` | **20 / 20 pass** |
| `FeatureTests` | Behavioural, real systems in play mode | **265 passed · 0 failed · 2 skipped — SUITE PASS** (~21 s) |

Reproduce:

```csharp
// EditMode — works while the editor is unfocused
// MCP run_tests, mode: EditMode

// Behavioural — enter play mode first, let a few frames elapse
VibeGame1.EditorTools.FeatureTestRunner.Start();
VibeGame1.EditorTools.FeatureTestRunner.Poll();       // appends the full report when done
```

Per-assertion detail lives in the live report (`FullReport()`); every assertion logs actual vs expected,
so a failure is diagnosable from the report text without re-running.

---

## Coverage by section

Skips are listed where they occur and explained in [Known gaps](#known-gaps) below. The seven
input-gated skips are closed; the remaining ones are noted there. Sections added since this table was
written (WandPedestal, WandReadability) are covered by the live report.

| # | Section | What it proves | Skips |
|---:|---|---|---:|
| 1 | Movement | Grounding, gravity, variable jump height (jump-cut), `Launch()`, `AddImpulse`, `Teleport` clears velocity **and sets facing**, `SpeedMultiplier`, **coyote time, jump buffer, dash + air-dash-once** | — |
| 2 | HitstopScoping | Hitstop drops `WorldScale` but leaves `PlayerScale` at 1 and `PlayerDelta` advancing; pause zeroes both; minimum-of-requests wins; release restores | — |
| 3 | ParryMathPure | Window boundaries, negative elapsed, not-facing, unblockable, and that the shipped tuning **is** 0.13 / 0.12 / 0.5 | — |
| 4 | ParryLive | Real deflects against a live enemy: perfect (no damage, FULL Pyre gain, enemy posture, no player posture cost), block stokes Pyre at a fraction of perfect, late block, missed parry, unblockable, facing-away | — |
| 5 | PlayerPosture | Accumulates, breaks at max, raises the event, staggers, amplifies damage ×1.6, auto-recovers, regenerates after delay, resets on respawn | — |
| 6 | EnemyExecute | Enemy posture configured from data, accumulates, breaks, enters Staggered; `ExecuteInteractor` acquires, executes, kills, awards souls, restores control | — |
| 7 | Weapons | All four equip; combo lengths and multipliers align; damage scales with stats; per-weapon parry window multiplier applies; a swing damages and builds posture; **a repeated press advances the combo** | — |
| 8 | Items | Pickup, capacity 3, FIFO order, **real-physics trigger pickup**, restore on respawn, and all four effects incl. the Stormcall arm→riposte flow | — |
| 9 | Flask | Refill, consume, refuse when empty, heal amount matches stats, **the real drink heals and a hit interrupts it (charge lost)** | — |
| 10 | Pyre + super | Refused below full, full-bar gate, consumption, clamping, the weapon carries shipped super data (rule 9), slow-mo does not slow the player, **the real super fires, damages a nearby enemy and spends the bar** | — |
| 11 | Progression | Souls on kill, spend/afford rules, cost curve, Vitality raises max HP **and** max posture, death empties the wallet and drops a stain carrying every soul, recovery by real physics | — |
| 12 | LevelFlow | Checkpoint activation heals and refills, respawn returns and resets enemies, kill zone kills, timer format, **the timer starts and ticks** | — |
| 13 | Boss | Activation, aggro lock, 3 segments, HP 0 breaks posture without killing, only `isExecute` consumes a segment, phases advance, heal between segments, defeat fires and stops the timer, lightning staggers but cannot kill | — |
| 14 | HUD | Every bar's fill tracks its value, asserted on `fill.rectTransform.anchorMax.x`; item slots, deathblow banner and text widgets wired | — |
| 15 | Audio | Every `Sfx` enum member resolves a clip (real or synthesized); music playing; one-shots do not throw | — |

Two assertions are deliberate **regression guards** for bugs that already shipped once:

- HUD bars assert on `anchorMax.x`, never `fillAmount` — a null-sprite `Image` silently ignores `fillAmount`.
- Item pickup and bloodstain recovery **walk into the trigger with real physics** and explicitly assert
  `!Physics.GetIgnoreLayerCollision(Interactable, Player)` — a direct `SendMessage("OnTriggerEnter", …)`
  is how that bug stayed hidden.

---

## Bugs the suite caught this session

Five real defects, four of them in shipping code. All are now in
[ENGINEERING-LOG.md](ENGINEERING-LOG.md) with their invariants.

### 1. The anti-mashing parry retune never reached the game

**Symptom.** `ParryTuning_*` failed: the asset reported perfect `0.15` / late `0.20` / whiff `0.25`,
while the C# defaults said `0.13` / `0.12` / `0.5`.

**Root cause.** `DataFactory.GetOrCreate` only **creates** singleton assets. `Assets/Data/PlayerStats.asset`
already existed, so it silently kept its pre-retune serialized values. The retune existed only as a
changed field initialiser, which a deserialized asset never reads.

**Fix.** `DataFactory` is now authoritative for the documented **design-contract** fields on `PlayerStats`
(parry windows, posture constants), rewriting them on every run while leaving every other field alone so
Inspector tuning still survives.

**Invariant.** *A code default is not a shipped value — an existing ScriptableObject silently wins.*
Changing a field initialiser on a type that already has an asset changes nothing until the asset is
rewritten.

### 2. `FirstPersonMotor.Teleport` did not set facing

**Symptom.** `Movement_TeleportSetsYaw` — teleporting with `yaw = 0` left the player facing 90°.

**Root cause.** `PlayerLook` owns yaw and rewrites `transform.rotation` every frame, so the rotation set
inside `Teleport` was overwritten on the next `Update`. It only appeared to work because the callers that
mattered (`LevelManager.Respawn`) happened to also call `PlayerLook.SetYaw`.

**Fix.** `Teleport` now pushes the yaw into `PlayerLook` itself.

**Invariant.** If a component owns a transform channel every frame, writing that channel from elsewhere
is a no-op. Push the value through the owner.

### 3. `ParryMath` boundary was float-fragile

**Symptom.** `ParryMath_EdgeOfLate` failed — a press landing exactly on the block boundary resolved as a
full **Hit**.

**Root cause.** The caller's `perfect + late` was constant-folded at compile time; `Evaluate` computed the
same sum at runtime. The two differed in the last bit, so an exact-boundary press fell outside the window.

**Fix.** A `1e-4f` epsilon on both comparisons, resolving boundaries in the **player's** favour.

**Invariant.** Never compare accumulated float timings for exact inclusion. On a timing boundary, favour
the player.

### 4. Aggro-locked enemies woke up when parried or damaged

**Symptom.** Found while diagnosing a contaminated parry test — the "inert" test dummy was attacking.

**Root cause.** `OnParried` routes the enemy into `State.Recover`, and `Recover` transitioned to `Chase`
**without checking `aggroLocked`**. Any locked enemy that was parried or damaged woke up and began
attacking. This reaches live gameplay: the boss is aggro-locked until its arena trigger fires, and the
Stormcall discharge calls `OnParried` on the boss directly — so a boss could start fighting with no boss
bar, no music cue and no gate.

**Fix.** Leaving `Recover` now honours `aggroLocked`, falling back to `Idle` instead of `Chase`.

**Invariant.** Every path *out* of a transient state must re-check the gating flags that kept the enemy
asleep, not just the path *in*.

### 5. Two test-side defects (recorded because they are easy to repeat)

- **Cloning a collected pickup.** The physics-pickup test instantiated the first `ItemPickup` it found.
  A pickup the player has already walked over has its collider and renderers disabled, and `Instantiate`
  copies that state — so the clone could never fire a trigger. It now selects a template with an enabled
  collider and force-enables the clone. The bug appeared only once a pickup was placed *at spawn*.
- **Parry sub-cases contaminating each other.** Once blocking genuinely cost posture (bug 1's fix), the
  accumulated posture from earlier sub-cases broke the player mid-section, and every later measurement
  came back ×1.6 — the `staggeredDamageMultiplier`. Sub-cases now reset posture between measurements.

---

## Known gaps

### Closed: the 7 input-gated skips

All seven skips shared **one architectural cause** — the behaviour was reachable only from `InputReader`
inside an `Update()`, with no public entry point a test could call. They are now closed by adding small
public entry points that `Update` itself calls with the polled input:

| Was skipped | Section | Entry point that closed it |
|---|---|---|
| Coyote time | Movement | `FirstPersonMotor.TryJump()` |
| Jump buffer | Movement | `FirstPersonMotor.TryJump()` |
| Dash / air-dash-once | Movement | `FirstPersonMotor.TryDash()` |
| Weapon combo advance on repeated input | Weapons | `WeaponController.TryAttack()` |
| Flask drink coroutine + interrupt-on-hit | Flask | `FlaskAbility.TryDrink()` (interrupt driven through `PlayerCombat.ReceiveAttack`) |
| Ultimate AoE burst | Ultimate | `UltimateAbility.TryUltimate()` |
| Timer starts on first input | LevelFlow | `SpeedrunTimer.TryStartRun()` |

Behaviour is unchanged: `Update` still polls `InputReader` (it remains the only script touching the Input
System) and simply delegates its body to the new method, which re-applies its own gates and takes no input.
Simulating `InputSystem` state events was rejected — the risk of a compile failure blocking the whole
project outweighed the coverage. This is also the first slice of the de-singletoning / decoupling work the
[multiplayer design](multiplayer-system-design.md) lists as a prerequisite: a network command stream can
call the same Try* methods.

Two test-side notes, both deliberate and both annotated in `FeatureTests.cs`:

- The jump-buffer assertion widens `jumpBuffer` to 0.6 s and the dash assertion shortens `dashCooldown`
  to 0.02 s for the duration of the check, then restores them. The *shipped* constants are asserted in the
  EditMode suite; what the play-mode check proves is the mechanism (a press past the coyote window fires on
  landing; the air-dash charge does not return until you land, even once the cooldown is up).
- `Weapons_ComboAdvanceOnRepeatedInput` reads the private `comboIndex` by reflection — nothing public
  exposes the combo step.

### Closed: the bloodstain "regression" that never was

`Progression_SoulsLostOnDeath` failed and `Progression_BloodstainRecovery` skipped together. The shipping
code was **correct**: death takes the whole wallet and drops a stain carrying every soul — both were
verified live. The test asserted the empty wallet *after* the respawn, and since no checkpoint has been
activated by that point in the suite, the player respawns exactly where they died, lands on their own
stain and recovers the souls through its trigger. The stain was then gone, so the recovery check skipped.
The test now asserts the drop synchronously (it happens inside `Health.OnDied`) and stages the recovery
walk deliberately. See [ENGINEERING-LOG.md](ENGINEERING-LOG.md).

### Still open

- Two `WandPedestal` skips (`FOpensMenu`, `RCyclingStillWorks`) are the *same* class of gap for
  `InteractPressed` / `WandCyclePressed` and want the same remedy: `TryInteract()` / `TryCycle()`.
- The landing fight (Heavy + 2 Grunts) still needs a human playtest.

---

## What this does and does not prove

**Proven.** The systems are *correct*. State machines advance as designed, damage and posture arithmetic
matches the data assets, the boss deathblow sequence is airtight, every HUD bar tracks its value, items
behave to spec, and the shipped tuning is the intended tuning. Four real bugs were found by running it.

**Not proven — and not provable this way — is whether the game feels good.**

`FeatureTests` and `DebugHarness` parry on a **state transition**: they have frame-perfect knowledge of
when a strike lands, which no human has. A green suite says the deflect *resolves* correctly for a
perfectly-timed press. It says nothing about whether a human can time that press from the on-screen cue.

### Needs a human playtest

1. **Is the new enemy aggression fun or oppressive?** Grunts are at `aggression 0.9`, Heavies `0.75`, and
   a deflect no longer ends a combo. The specific encounter to watch is the **landing fight: a Heavy plus
   two Grunts**, all relentless, against a **75° parry facing cone** — you cannot deflect two attackers
   from opposite sides. Per-enemy pressure is probably right; that *encounter* may need thinning or
   spacing. This is the single most likely thing to be wrong.
2. **Does `cueLead = 0.28 s` read correctly?** The maths says reactions of 0.15–0.28 s land in the Perfect
   band. Whether the cue *reads* as "press now" at speed is a different question. The `[Parry]` console
   logs print elapsed ms vs the window and will distinguish "the window is wrong" from "the player is
   early".
3. **Is the arm telegraph legible?** The rig rears back through the wind-up, hitches past extension on the
   cue, and whips through on the strike. Whether that silhouette reads in first person — especially with
   the boss parked ~2.1 m from the camera (a known standoff-distance issue) — is a visual judgement.
4. **Do the Stormcall arm→riposte stakes land?** Arming and *then* having to earn the discharge is a
   deliberate tension. Only play tells you whether it is exciting or just a delay.

Do not report a green suite as "feel verified".
