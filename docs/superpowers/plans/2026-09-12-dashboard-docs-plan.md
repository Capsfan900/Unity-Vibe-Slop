# Human Development Dashboard and Documentation Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the dashboard's flat document wall with task categories, current-vs-archive separation, shared level vocabulary, and actionable human editing/debugging guidance.

**Architecture:** Keep Markdown as source and extend the Python generator with explicit categorization, structured audit results, derived level inventory, and an allowlisted localhost folder action. Current guides follow one practical template; historical documents remain unchanged but archived.

**Tech Stack:** Python 3, generated HTML/CSS/JavaScript, Markdown sources, `unittest`, localhost `http.server`.

**Spec:** `docs/superpowers/specs/2026-09-12-level-studio-dashboard-design.md`

## Global Constraints

- The dashboard remains local, dependency-free, and useful offline.
- Archive is collapsed by default but remains searchable.
- Level inventory derives from shipped data/metadata, not a duplicate hand-maintained list.
- The folder-opening endpoint binds only to `127.0.0.1` and accepts no arbitrary path.
- Historical documents are labelled, not rewritten to pretend history used current terminology.

---

### Task 1: Categorize documents and collapse Archive

**Files:**
- Modify: `Tools/dashboard/build_dashboard.py`
- Create: `Tools/dashboard/test_dashboard.py`

**Interfaces:**
- Produces: `categorize_doc(path) -> str` and category-annotated document payloads.

- [ ] **Step 1: Write failing category tests**

```python
def test_current_and_history_are_separated(self):
    self.assertEqual("Level Building", dashboard.categorize_doc("docs/LEVEL-EDITOR.md"))
    self.assertEqual("Archive", dashboard.categorize_doc("docs/plans/old-plan.md"))
    self.assertEqual("Archive", dashboard.categorize_doc("docs/ENGINEERING-LOG.md"))
```

- [ ] **Step 2: Run and verify failure**

```powershell
py -3 -m unittest Tools.dashboard.test_dashboard -v
```

- [ ] **Step 3: Add explicit category rules and grouped navigation**

```python
CATEGORIES = ("Start Here", "Level Building", "Combat & Enemies", "Systems",
              "Testing & Debugging", "Tools & Distribution", "Archive")

def categorize_doc(path):
    p = path.replace("\\", "/")
    if p.startswith("docs/plans/") or p.startswith("docs/handoffs/") or p.endswith("ENGINEERING-LOG.md"):
        return "Archive"
    # exact current-doc routing follows before conservative fallback
```

Render categories as accessible collapsible groups; Archive starts closed. Preserve global search across closed groups and show category badges on results.

- [ ] **Step 4: Run tests, build dashboard, inspect generated category markup**

```powershell
py -3 -m unittest Tools.dashboard.test_dashboard -v
py -3 Tools/dashboard/build_dashboard.py
rg -n "Start Here|Level Building|Archive" Tools/dashboard/out/index.html
```

- [ ] **Step 5: Commit**

```powershell
git add Tools/dashboard/build_dashboard.py Tools/dashboard/test_dashboard.py
git commit -m "[Astra] Group dashboard documentation by human task"
```

### Task 2: Add documentation usefulness audit

**Files:**
- Modify: `Tools/dashboard/build_dashboard.py`
- Modify: `Tools/dashboard/test_dashboard.py`
- Create: `docs/HUMAN-DEVELOPMENT-GUIDE.md`

**Interfaces:**
- Produces: `audit_docs(docs, repo_root) -> list[AuditFinding]` with severity, code, path, and message.

- [ ] **Step 1: Write failing audit tests with temporary Markdown fixtures**

```python
def test_audit_finds_broken_link_and_current_insight_term(self):
    findings = dashboard.audit_docs([
        {"path":"docs/X.md", "category":"Level Building", "text":"[bad](missing.md) Insight route"}
    ], ROOT)
    self.assertIn("broken-local-link", {f["code"] for f in findings})
    self.assertIn("obsolete-current-term", {f["code"] for f in findings})
```

- [ ] **Step 2: Implement deterministic audit rules**

Check broken relative links, uncategorized files, obsolete terms in current docs, duplicate current titles, missing required guide headings, and backtick source paths that do not exist. Exempt Archive from terminology failures. Render findings on a dedicated Documentation Health view with counts and direct document links.

- [ ] **Step 3: Write the practical entry guide**

`docs/HUMAN-DEVELOPMENT-GUIDE.md` must contain these exact sections: Purpose, Common Tasks, Where Data Lives, Safe Editing, Regeneration, Verification, Debugging Symptoms, and Rollback. Link Level Studio, vocabulary, authoring, combat/enemy data, tests, dashboard, distribution, and parry choreography.

- [ ] **Step 4: Run audit/tests and commit**

```powershell
py -3 -m unittest Tools.dashboard.test_dashboard -v
py -3 Tools/dashboard/build_dashboard.py
```

```powershell
git add Tools/dashboard docs/HUMAN-DEVELOPMENT-GUIDE.md
git commit -m "[Astra] Audit docs for useful human workflows"
```

### Task 3: Derive Level Map vocabulary and object inventory

**Files:**
- Modify: `Tools/dashboard/build_dashboard.py`
- Modify: `Tools/dashboard/test_dashboard.py`
- Modify: `docs/LEVEL-VOCABULARY.md`
- Modify: `docs/LEVEL-EDITOR.md`

**Interfaces:**
- Produces: level-map payload with zones, splits, aliases, canonical IDs, types, and placed data keys.

- [ ] **Step 1: Write failing fixture tests against a small LevelDefinition YAML**

```python
def test_level_map_groups_objects_under_zone(self):
    level = dashboard.parse_level_asset(FIXTURE)
    self.assertEqual("Knight", level["zones"]["T2"]["split"])
    self.assertEqual("pshooter_enemy01", level["objects"]["T2.Sentry.02"]["dataKey"])
```

- [ ] **Step 2: Parse the shipped metadata and render zone cards**

Read `zones` and object metadata from `Assets/Data/Levels/*_Level.asset`; join enemy aliases from the current vocabulary convention table. Render bounds, split, description, platforms, enemies, routes, checkpoints, IDs, and aliases. A parser failure is an audit error, not an empty map.

- [ ] **Step 3: Replace duplicate inventory JSON with convention documentation**

Keep examples and alias definitions in `LEVEL-VOCABULARY.md`; remove the hand-maintained placed-object inventory once the shipped-data parser is proven. Document the prompt grammar: `<zone ID/name> / <canonical object ID or alias> / <requested change>`.

- [ ] **Step 4: Build, inspect Level 1 output, and commit**

```powershell
py -3 Tools/dashboard/build_dashboard.py
rg -n "T2.*Helix Tower|T2.Sentry" Tools/dashboard/out/index.html
```

```powershell
git add Tools/dashboard docs/LEVEL-VOCABULARY.md docs/LEVEL-EDITOR.md
git commit -m "[Astra] Derive dashboard level vocabulary from shipped data"
```

### Task 4: Add actionable parry workflow and safe folder opening

**Files:**
- Create: `docs/PARRY-CHOREOGRAPHY.md`
- Modify: `Tools/dashboard/build_dashboard.py`
- Modify: `Tools/dashboard/test_dashboard.py`
- Modify: `docs/TOOLING.md`

**Interfaces:**
- Produces: copy-command buttons and POST `/actions/open-timing-captures` in served mode.

- [ ] **Step 1: Write failing endpoint allowlist tests**

```python
def test_only_timing_capture_action_is_allowed(self):
    self.assertEqual(dashboard.timing_capture_dir(), dashboard.resolve_action("open-timing-captures"))
    with self.assertRaises(ValueError): dashboard.resolve_action("../../Windows")
```

- [ ] **Step 2: Implement the localhost-only action**

Subclass `SimpleHTTPRequestHandler`; accept only the exact POST path, resolve the known LocalLow timing-captures directory, create it if absent, and open it with `explorer.exe`. Bind the server explicitly to `127.0.0.1`. Static output shows a copyable absolute path and never attempts local execution.

- [ ] **Step 3: Write the recorder/generator guide**

Document: select one of four recording stages; unlock; `timing prime`; press `0`; run and tap intended empty-air parry beats; press `0` to auto-export; locate JSON; generate one best-fit module; read per-beat time/spatial/visibility/confidence; adjust in Level Studio; rerun selected beats; play through F10; validate/apply.

- [ ] **Step 4: Run tests, serve locally, click the allowlisted button, and commit**

```powershell
py -3 -m unittest Tools.dashboard.test_dashboard -v
py -3 Tools/dashboard/build_dashboard.py --serve --open
```

```powershell
git add docs/PARRY-CHOREOGRAPHY.md docs/TOOLING.md Tools/dashboard
git commit -m "[Astra] Document and expose parry choreography workflow"
```

### Task 5: Final documentation and integration audit

**Files:**
- Modify: current Markdown files reported by the audit
- Modify: `docs/VERIFICATION-REPORT.md`, `docs/HANDOFF.md`

**Interfaces:**
- Consumes all prior plan outputs.
- Produces a zero-error Documentation Health report; warnings must be explained in Verification Report.

- [ ] **Step 1: Run the dashboard audit and fix only current actionable documentation**

```powershell
py -3 Tools/dashboard/build_dashboard.py
py -3 -m unittest Tools.dashboard.test_dashboard -v
```

- [ ] **Step 2: Run project-wide verification from a fresh editor state**

```powershell
dotnet build Assembly-CSharp.csproj
dotnet build Assembly-CSharp-Editor.csproj
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode
python Tools/level_arc_offline.py --after --sight
```

Run fresh-session FeatureTests only after confirming `GameManager.I != null` and `Time.timeScale == 1`; run Health Check and Projectile Encounter Report.

- [ ] **Step 3: Perform the human acceptance checklist and record honest gaps**

Open/create a Level 1 draft, edit and undo objects, recover autosave, exercise a blocked and successful apply on a disposable copy, record all four stage types, generate twice for deterministic equality, and use the dashboard to locate every workflow. Any step not manually performed remains labelled human-only.

- [ ] **Step 4: Update handoff/verification and commit integration**

```powershell
git add docs Tools/dashboard Assets
git commit -m "[Astra] Verify Level Studio and human development workflow"
```
