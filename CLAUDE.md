# vibegame1

@AGENTS.md

---

## Claude Code specifics

Everything that governs this project lives in **[AGENTS.md](AGENTS.md)**, imported above. It is
tool-neutral on purpose: Codex, Cursor, Zed, Aider and Jules read `AGENTS.md` automatically, so the same
contract reaches every model. **Put project rules THERE, not here** — a rule written only in this file is
invisible the moment the user picks up the project in another tool, and that divergence is silent.

This file holds only what is specific to running as Claude Code.

### Skills — invoke with the `Skill` tool

| Skill | When |
|---|---|
| `unity-editor` | Any test, generator, menu item or capture. **The editor must be open** — there is no headless copy. |
| `session-handoff` | The moment the context watcher hits 65%, or on any "wrap up / new session / I'm going to clear". |
| `dashboard` | Currently disabled in `.claude/settings.local.json` — drop its `skillOverrides` entry to use it. |

### Subagents — invoke with the `Agent` tool

`level-designer` · `enemy-designer` · `combat-designer` · `vfx-art-team` · `audio-engineer` ·
`ui-designer` · `editor-controls` · `unbuilt-asks`. Each brief is in `.claude/agents/<name>.md` and is
summarised in AGENTS.md.

**A new agent file is not selectable until the session restarts** — the roster is read at startup. Write
the file, then use `general-purpose` with the brief inlined for the rest of the session.

**Fence off the files you hold.** Every brief must name a do-not-touch list, because two agents editing one
file clobber each other. This has already cost this project a pass.

**Claude Code maps the company tiers onto its models** (`astra-engineering-company`): the lead is the
session model, and a worker's tier is picked with the Agent tool's `model` (`fable` / `opus` / `sonnet` /
`haiku`). Record the model that actually ran in each commit's `Model:` trailer, e.g.
`Model: Opus 5 (Claude Code)`.

**One commit per worker pass**, prefixed with the worker's role, after re-running the generators the report
names and both suites — so a regression is a single `git revert`. Tag before a batch
(`pre-<theme>-<date>`). Details in AGENTS.md under "Hard rules".

### MCP

Prefer the `mcp__UnityMCP__*` tools when they connected at startup. When they did not — or the server
reports `no_unity_session` with the editor open — skip the client entirely:

```
python .claude/skills/unity-editor/mcp_call.py <tool> '<json>'
```

Run it from the project root. `execute_code` compiles as **C# 6** — keep snippets plain.

**Three traps that have each cost a session:**

1. **A modal dialog in Unity deadlocks the bridge** and presents as `Unity session not ready … ping not
   answered`, which looks exactly like a lost bridge. Unity's CPU sits near zero while it waits. Diagnose
   by enumerating Unity's top-level windows filtered to its PID — not by retrying. `SaveOpenScenes()` from
   code before **each** scene-changing generator prevents it.
2. **A generator runs against the assembly Unity has already compiled**, not the file you just wrote.
   `AssetDatabase.Refresh()` returns before compilation starts, so an `isCompiling == false` check right
   after it passes while the OLD code is still loaded. Probe for the new symbol by reflection and loop
   until it resolves.
3. **`run_tests` cannot start this project's suite** — `Unity.PerformanceTesting`'s prebuild setup blocks
   on the Account API for 30 s while the job manager gives up at 15 s. Use
   `VibeGame1.EditorTools.QuickTestRunner.RunFull()` and poll for a new `TestResults/*.xml` on disk.
