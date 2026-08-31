# Backlog

Requested but not yet built. Ordered by the user's stated priority. When one is completed, move it into
`ENGINEERING-LOG.md` (if it involved a non-obvious problem) and update `DATAFLOW.md` + `ARCHITECTURE.md`.

---

## 1. Per-system dataflow maps, kept current
**Requested:** *"a markdown dataflow mapping to go along with every game system and how its working so long
horizon planning does not get mucked up by how things work or what is happening in the repo. these mappings and
docs need to be updated every time something is added."*

Started in `DATAFLOW.md`. Remaining work: fill in a map for every system listed there that is still marked TODO,
and add the standing rule to the session protocol so it is never skipped.

---

## Known gaps carried from the verification report
- ~~7 feature-test skips from input-gated behaviour.~~ **Closed** — `FirstPersonMotor.TryJump/TryDash`,
  `WeaponController.TryAttack`, `FlaskAbility.TryDrink`, `UltimateAbility.TryUltimate` and
  `SpeedrunTimer.TryStartRun` are public entry points that `Update` calls with the polled input. The two
  remaining `WandPedestal` skips (`InteractPressed`, `WandCyclePressed`) want the same treatment.
- The landing fight (Heavy + 2 Grunts) needs a human playtest now that attack arbitration is in.
- `MaxSimultaneousAttackers` is a static with no Inspector exposure; resets to 1 on domain reload.
- `WandFactory` menu step is `3b` and runs inside `0. Rebuild Everything`, but is not covered by a test.
