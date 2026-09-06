# Session protocol

How to start and end a working session on `vibegame1` without re-deriving what past sessions learned,
and without burning context re-reading the codebase.

Related: [ARCHITECTURE.md](ARCHITECTURE.md) · [TOOLING.md](TOOLING.md) · [ENGINEERING-LOG.md](ENGINEERING-LOG.md)

---

## Starting a session

1. **Read `CLAUDE.md`.** It loads automatically and is deliberately small. It is an index, not a manual.
2. **Read exactly one `docs/` file** — the one matching the task:
   | Task | Read |
   |---|---|
   | Something is behaving strangely | [ENGINEERING-LOG.md](ENGINEERING-LOG.md) **first** |
   | Building or running a tool | [TOOLING.md](TOOLING.md) |
   | Changing systems or combat | [ARCHITECTURE.md](ARCHITECTURE.md) |
   | Networking / backend | [multiplayer-system-design.md](multiplayer-system-design.md) |
   | "Is X actually working?" | [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md) |
   | Picking up where the last chat stopped | [HANDOFF.md](HANDOFF.md) **first** |
   | Sandbox scene | [../Assets/Scenes/README_Sandbox.md](../Assets/Scenes/README_Sandbox.md) |
   Do not read all of them "for context".
3. **Confirm the Unity Editor is open on this project.** No MCP tool works otherwise. If tools time out
   or return `no_unity_session`, check this before debugging anything.
4. **Run `VibeGame1/Health Check`.** Ten seconds, and it catches most environment breakage immediately.
5. **Never assume the scene is populated.** The scene is disposable and has been wiped before. If the
   hierarchy looks empty, run `VibeGame1/0. Rebuild Everything` — that is the normal recovery, not an
   emergency.

## While working

- **Compile after every batch of script edits** (`refresh_unity`, then read the console). A single
  compile error puts the editor into Safe Mode, which presents as "the project won't open and the scene
  is blank". Do not let errors accumulate.
- **Exit play mode before any editor generator.** The level and sandbox builders destroy before they
  rebuild and now refuse to run in play mode; other editor code may not be so careful.
- **Adding or changing a system means updating its map in [DATAFLOW.md](DATAFLOW.md) in the same change.**
  Not at the end of the session — in the same change. A map that lies is worse than no map, and this is
  the step that gets skipped when a session runs long.
- **Prefer the generators over hand-editing the scene.** Anything done by hand is lost on the next
  rebuild unless it lives under `Level_Manual` / `Sandbox_Manual`.
- **Verify with the tools, not by eye:** `Health Check` → EditMode `run_tests` → `FeatureTests`.
  Remember `DebugHarness` proves the state machine, never the *feel*.

## Working with agents: code lanes first, one editor pass per batch

The Unity editor is a single shared resource and the slowest step in any change (generators ~2 min, the
full EditMode suite ~4 min, a fresh feature session ~1.5 min, and it waits whenever the user is in play
mode). Learned on 2026-09-04/05, when five agents each wanted their own pass:

- **Code agents never touch the editor.** They write scripts, data-in-code and tests, prove them with the
  offline `dotnet build` of both assemblies (temp csproj copies for new files), and hand back the list of
  generator steps their change needs.
- **One editor agent runs one pass for the whole batch**: refresh → the generators the batch needs → Health
  Check → save → `Run Quick EditMode Tests` (the full suite only when a level asset or a motor tuning field
  changed) → one fresh feature session → screenshots, looked at. The pass costs the same for one change or five.
- **Never enter play mode while `is_playing` is true** — the user is at the controls; wait.
- **Never split a lane mid-flight** across a file both halves need; the split on 2026-09-05 cost a full pass
  when one lane reverted work the other assumed existed.

## Ending a session

1. **Append to [ENGINEERING-LOG.md](ENGINEERING-LOG.md)** any non-obvious problem you solved, as
   Symptom → Root cause → Fix → Invariant. This is the highest-value thing you can leave behind: it is
   what stops the next session re-deriving or re-breaking it.
2. **Update the docs you invalidated.** New tool → [TOOLING.md](TOOLING.md). New system or changed
   contract → [ARCHITECTURE.md](ARCHITECTURE.md). New hard rule → the rules table in `CLAUDE.md`.
3. **Keep `CLAUDE.md` lean.** If an addition does not belong in the first context window of *every*
   future session, it belongs in `docs/`.
4. **State what is unverified.** Say plainly what was not compiled, not play-tested, or not feel-tested.
   A confident "done" on unverified work costs the next session more than an honest caveat. If the test
   results moved, update [VERIFICATION-REPORT.md](VERIFICATION-REPORT.md) and the counts in `CLAUDE.md`.
5. **Leave the project compiling.** Never end on a Safe Mode state.
6. **Rewrite [HANDOFF.md](HANDOFF.md).** One page, overwritten every session: what is in flight, what is
   uncommitted, which editor steps still need running, and what the next session should do first. It is
   the only file here that describes a moment rather than the project.

---

## Token discipline

The reason this structure exists. `CLAUDE.md` is paid for at the start of *every* session; `docs/` files
are paid for only when read.

- **Index, don't inline.** Depth goes in `docs/`. `CLAUDE.md` stays an index with pointers.
- **Read narrowly.** One `docs/` file for the task at hand. Never read the whole codebase to answer a
  narrow question — `grep` for the symbol.
- **Let tools answer questions.** `Health Check` and `FeatureTests` report project state far more cheaply
  than reading files and reasoning about them. A failing assertion names the file for you.
- **Trust the log.** If behaviour is strange, check [ENGINEERING-LOG.md](ENGINEERING-LOG.md) before
  investigating from scratch — most strange behaviour here has a known cause.
- **Delegate wide reads.** Searches spanning many files belong in a subagent that returns the conclusion,
  not the file contents.
- **Don't re-explore what a doc already states.** If a doc is wrong, fix the doc — do not work around it
  silently.
