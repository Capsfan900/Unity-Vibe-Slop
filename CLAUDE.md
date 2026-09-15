# vibegame1

@AGENTS.md

---

## Claude Code specifics

Project rules live in **AGENTS.md** (tool-neutral). This file holds only what is specific to Claude Code.

### The user's standing direction (2026-09-13/14)

- **One campaign level** — `Level_01` "The Hollow Ascent" (zones T0–T4) — polished with Level Studio and parry
  choreography. Level 2 is not the next step. Name places as zone then object ID.
- **The user records runs** (`timing prime`, key `0`, Level Studio → Generate Module) and judges feel.
- **Windows exe only.** Leave the four gated Legendary duels and the Warden in place (no cutting or reordering).
- **Git:** work on `master`; ignore (never delete) `.worktrees/*` and `.claude/worktrees/*`. Never stage the
  "Preserved user-owned files" listed in `docs/HANDOFF.md`.
- **Fable plans, frontier leads implement, cheaper models type:** Fable-tier agents do judgment and math as
  read-only specs saved to `docs/`; the lead implements core systems; Sonnet workers take fenced presentation/data lanes.

### Skills and agents

- `unity-editor` skill: any test, generator, menu item or capture (the editor must be open).
- `session-handoff`, `astra-engineering-company`, `dashboard` are **disabled for auto-invocation** in
  `.claude/settings.local.json` (user-owned). When one is needed, Read its `.claude/skills/<name>/SKILL.md`
  and follow it. The context watcher hook points at the handoff file the same way.
- Specialist agents (`.claude/agents/*.md`, listed in AGENTS.md) are read at session start: a new or edited brief is
  not selectable until restart — use `general-purpose` with the brief inlined meanwhile. Pick a worker's tier with the
  Agent tool's `model`; record the model that actually ran in the commit `Model:` trailer.
- A subagent's report exists only in the transcript: save any spec it returns to `docs/` in the same session.

### MCP

Prefer `mcp__UnityMCP__*`; if they did not connect, `python .claude/skills/unity-editor/mcp_call.py <tool> '<json>'`
from the project root (bare tool names). Traps: a **modal dialog** deadlocks the bridge (looks like a lost session;
check Unity's windows, `SaveOpenScenes()` before scene-changing generators); a generator runs the **previously
compiled** assembly until the new symbol resolves by reflection; MCP `run_tests` cannot start this suite (use
`QuickTestRunner`); the bridge's safety check blocks `AssetDatabase.DeleteAsset` (remove a file you generated with `git rm`).
