---
name: dashboard
description: Build and open the vibegame1 development dashboard - one local web page with every doc (architecture, dataflow, engineering log, backlog, handoff, verification), the git change log, the working tree, the latest EditMode and feature-suite results, and the system map. Use when the user says /dashboard, "spin up the dashboard", "show me the project status page", or wants the docs as a web page.
---

# /dashboard

Regenerate the page from the repo's current truth and open it. Read-only: no Unity, no network needed
(markdown renders through a CDN script when online and falls back to plain text offline).

```powershell
python Tools/dashboard/build_dashboard.py --open
```

That writes `Tools/dashboard/out/index.html` (gitignored) and opens it in the default browser. To serve it
instead (a URL the user can keep open and refresh), run in the background:

```powershell
python Tools/dashboard/build_dashboard.py --serve --open
```

and tell the user the address (`http://127.0.0.1:8765/`). Re-run the build to refresh the numbers after a
test run or a doc change; the served copy picks the new file up on reload.

What is on it: status tiles (EditMode from the runner's `TestResults.xml`, the feature suite as recorded in
`docs/VERIFICATION-REPORT.md`, uncommitted files, last commit, log-entry and backlog counts), the handoff
page, the last engineering-log entries, code counts; a Change log view (commits, uncommitted files, every
log entry); a Tests view (per suite, failures, skips, slowest); a Systems view (DATAFLOW's maps, the backlog
sections, a derived Level Map drawn from `Level_01_Level.asset` in the shipped level/zone/object vocabulary,
not hand-authored); every doc rendered under categorized navigation (Start Here, Level Building, Combat &
Enemies, Systems, Testing & Debugging, Tools & Distribution, Archive); a documentation-health/audit view that
flags uncategorized docs, duplicate titles, obsolete terminology and broken local links/paths; and a search
box over all of them. `AGENTS.md` is the primary contract shown; `CLAUDE.md` is labelled only as an adapter.
Plain-Markdown specialist briefs and shared workflows are discovered too, so the dashboard remains useful
from any coding-agent harness. The dedicated **Agent contract** view links that one source of truth, all
specialist briefs, and all shared workflows without treating any harness adapter as authoritative.

In-game, the F1 test menu's **DEV DASHBOARD** button opens `Tools/dashboard/out/index.html` directly (editor
only) so a build does not need to be rebuilt to see it — it just needs the page regenerated first.

If `TestResults.xml` is missing the tiles say so — run the EditMode suite in the editor first (see the
`unity-editor` skill: `VibeGame1.EditorTools.QuickTestRunner.RunQuick()` / `.RunFull()`). Do not edit the
generator to add a doc: drop a `.md` under `docs/` and it is picked up and categorized automatically.
`Tools/test_level_arc_offline.py` proves the offline arc parser the dashboard's Level Map reuses, without
Unity — useful to re-run after touching `LevelDefinitionAuthoring.cs` or the dashboard's own arc parsing.
