# Level Studio and Human Tooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute the approved Level Studio, shared vocabulary, parry choreography, Challenge Route, and human dashboard design as independently verifiable increments.

**Architecture:** Four linked plans separate shared data foundations, the hybrid editor, the recorder/generator, and documentation tooling. The foundation lands first; Level Studio and recorder work may then proceed in parallel; dashboard integration follows their stable interfaces and owns the final project-wide audit.

**Tech Stack:** Unity 6000.5.10f1, C# 9, Unity Editor APIs, New Input System, NUnit EditMode/FeatureTests, Python 3 dashboard generator.

**Spec:** `docs/superpowers/specs/2026-09-12-level-studio-dashboard-design.md`

## Global Constraints

- Follow `AGENTS.md`, especially data authoring, `InputReader`, regeneration, shipped-value, and DATAFLOW rules.
- Use the user's open Unity editor through MCP; do not run a headless editor or a second project copy.
- Tag before each implementation batch and keep each reviewed lane revertible.
- Preserve all user-owned untracked audio, portraits, route captures, and local settings.
- Do not publish a new friend build unless the user separately requests it after human acceptance.

---

### Task 1: Shared level foundation

**Files:**
- Execute: `docs/superpowers/plans/2026-09-12-level-foundation-plan.md`

**Interfaces:**
- Produces stable object metadata, `ZoneDef`, `LevelObjectCatalog`, and `ChallengeRouteDef` for every later plan.

- [ ] **Step 1: Execute both tasks in the foundation plan test-first**

```powershell
Get-Content docs/superpowers/plans/2026-09-12-level-foundation-plan.md
```

- [ ] **Step 2: Gate on shipped Level 1 migration and focused/full tests**

```powershell
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode VibeGame1.Tests.LevelVocabularyTests VibeGame1.Tests.ChallengeRouteMarkerTests
python Tools/level_arc_offline.py --after --sight
```

### Task 2: Protected hybrid Level Studio

**Files:**
- Execute: `docs/superpowers/plans/2026-09-12-level-studio-plan.md`

**Interfaces:**
- Consumes Task 1 catalog/zones/routes.
- Produces `LevelDraftStore`, Level Studio, validation/apply, and the shared F10 draft bridge.

- [ ] **Step 1: Execute all four Level Studio tasks with review after each commit**

```powershell
Get-Content docs/superpowers/plans/2026-09-12-level-studio-plan.md
```

- [ ] **Step 2: Gate on protected-original, recovery, editor coverage, and apply-rollback fixtures**

```powershell
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode VibeGame1.Tests.LevelDraftStoreTests VibeGame1.Tests.LevelStudioBrowserTests VibeGame1.Tests.LevelStudioEditingTests VibeGame1.Tests.LevelApplyTransactionTests
```

### Task 3: Parry choreography and best-fit modules

**Files:**
- Execute: `docs/superpowers/plans/2026-09-12-parry-choreography-plan.md`

**Interfaces:**
- Consumes Task 1 metadata and Task 2 draft insertion APIs.
- Produces primed recording, four data-built stages, versioned JSON captures, deterministic solver/report, and draft modules.

- [ ] **Step 1: Execute all four choreography tasks with review after each commit**

```powershell
Get-Content docs/superpowers/plans/2026-09-12-parry-choreography-plan.md
```

- [ ] **Step 2: Gate on recorder, stage, solver, projectile encounter, and arc suites**

```powershell
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode VibeGame1.Tests.PlayerTimingCaptureTests VibeGame1.Tests.ParryRecordingStageTests VibeGame1.Tests.ParryModuleSolverTests
python Tools/level_arc_offline.py --after --sight
```

### Task 4: Dashboard and documentation integration

**Files:**
- Execute: `docs/superpowers/plans/2026-09-12-dashboard-docs-plan.md`

**Interfaces:**
- Consumes all stable current APIs and shipped metadata.
- Produces categorized navigation, Archive, documentation-health audit, derived Level Map, human guides, and safe timing-folder action.

- [ ] **Step 1: Execute all five dashboard/documentation tasks with review after each commit**

```powershell
Get-Content docs/superpowers/plans/2026-09-12-dashboard-docs-plan.md
```

- [ ] **Step 2: Run dashboard tests and build the final page**

```powershell
py -3 -m unittest Tools.dashboard.test_dashboard -v
py -3 Tools/dashboard/build_dashboard.py
```

### Task 5: Final integration gate

**Files:**
- Modify: `docs/VERIFICATION-REPORT.md`
- Modify: `docs/HANDOFF.md`

**Interfaces:**
- Consumes all four completed subplans.
- Produces an evidence-backed completion record and exact resume state.

- [ ] **Step 1: Compile runtime/editor assemblies**

```powershell
dotnet build Assembly-CSharp.csproj
dotnet build Assembly-CSharp-Editor.csproj
```

- [ ] **Step 2: Run full EditMode from a saved, stopped editor**

```powershell
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode
```

- [ ] **Step 3: Run Health Check, Level Arc, Projectile Encounter, and fresh Level 1 FeatureTests**

```powershell
python .claude/skills/unity-editor/mcp_call.py --menu VibeGame1/Health Check
python Tools/level_arc_offline.py --after --sight
```

Before FeatureTests, explicitly prove `GameManager.I != null` and `Time.timeScale == 1`; preserve the intentional Sandbox-only skip.

- [ ] **Step 4: Perform the six human acceptance actions from the spec and record any unperformed action honestly**

```text
Level 1 draft/load/edit/undo/play; F10 round-trip/recovery; blocked then disposable successful apply;
four recording surfaces; repeatable module generation; dashboard-only workflow discovery.
```

- [ ] **Step 5: Update verification/handoff and commit the integration record**

```powershell
git add docs/VERIFICATION-REPORT.md docs/HANDOFF.md
git commit -m "[Astra] Complete Level Studio and human tooling verification"
```
