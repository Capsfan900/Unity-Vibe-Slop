# Protected Hybrid Level Studio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a safe Unity Level Studio with protected campaign copies and a shared F10 live-edit document.

**Architecture:** Store draft metadata and cloned `LevelDefinition` assets outside shipping content, expose all objects through `LevelObjectCatalog`, and preview/edit them with native Unity handles. Campaign application is a validated transaction with conflict detection and rollback.

**Tech Stack:** Unity EditorWindow/UI Toolkit or IMGUI, SceneView Handles, Unity Undo, C# 9, JSON manifests, NUnit EditMode tests.

**Spec:** `docs/superpowers/specs/2026-09-12-level-studio-dashboard-design.md`

## Global Constraints

- Campaign originals are immutable until an explicit validated Apply transaction.
- Use the existing `LevelDefinition`, `LevelDocument`, `LevelPieceFactory`, and canonical builder.
- Drafts never ship and are ignored by git.
- Editor previews never become hand-authored campaign scene truth.
- All mapped flows update `docs/DATAFLOW.md`.

---

### Task 1: Implement atomic draft storage and recovery

**Files:**
- Create: `Assets/Editor/LevelStudio/LevelDraftStore.cs`
- Create: `Assets/Editor/LevelStudio/LevelDraftManifest.cs`
- Modify: `.gitignore`
- Test: `Assets/Editor/Tests/LevelDraftStoreTests.cs`

**Interfaces:**
- Produces: `LevelDraftStore.CreateFromCampaign(LevelDefinition)`, `Save`, `Autosave`, `Load`, `List`, `Recover`, and `Fingerprint`.

```csharp
public sealed class LevelDraft
{
    public LevelDraftManifest manifest;
    public LevelDefinition definition;
    public string assetPath;
}
public sealed class LevelDraftSummary
{
    public string draftId, displayName, sourceLevelId, savedUtc;
    public int changedObjectCount;
    public bool hasValidRecovery, sourceChanged;
}
```

- [ ] **Step 1: Write failing tests using a temporary draft root**

```csharp
[Test] public void CreateEditCancel_NeverChangesCampaignJson()
{
    string before = EditorJsonUtility.ToJson(source);
    var draft = store.CreateFromCampaign(source);
    draft.definition.displayName = "Changed";
    store.Save(draft);
    Assert.AreEqual(before, EditorJsonUtility.ToJson(source));
}
```

Also test atomic replacement, three-generation autosave rotation, corrupt-newest recovery, and fingerprint mismatch.

- [ ] **Step 2: Run the fixture and verify missing-type failures**

```powershell
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode VibeGame1.Tests.LevelDraftStoreTests
```

- [ ] **Step 3: Implement manifest and store**

```csharp
[Serializable] public sealed class LevelDraftManifest
{
    public int formatVersion = 1;
    public string draftId, displayName, sourceGuid, sourceLevelId, sourceFingerprint;
    public string createdUtc, savedUtc, autosavedUtc;
}

public sealed class LevelDraftStore
{
    public LevelDraft CreateFromCampaign(LevelDefinition source);
    public void Save(LevelDraft draft);
    public void Autosave(LevelDraft draft);
    public LevelDraft Load(string draftId);
    public IReadOnlyList<LevelDraftSummary> List();
    public LevelDraft Recover(string draftId);
    public static string Fingerprint(LevelDefinition definition);
}
```

Use explicit dependency-injected roots in tests; production root is `<project>/LevelDrafts`. Write `*.tmp`, flush/close, then atomically replace. Add `/LevelDrafts/` to `.gitignore`.

- [ ] **Step 4: Run focused tests and offline editor build**

```powershell
dotnet build Assembly-CSharp-Editor.csproj
```

- [ ] **Step 5: Commit**

```powershell
git add .gitignore Assets/Editor/LevelStudio Assets/Editor/Tests/LevelDraftStoreTests.cs
git commit -m "[Astra] Add protected level drafts and recovery"
```

### Task 2: Build the Level Studio browser and hierarchy

**Files:**
- Create: `Assets/Editor/LevelStudio/LevelStudioWindow.cs`
- Create: `Assets/Editor/LevelStudio/LevelStudioBrowser.cs`
- Create: `Assets/Editor/LevelStudio/LevelStudioSelection.cs`
- Test: `Assets/Editor/Tests/LevelStudioBrowserTests.cs`

**Interfaces:**
- Consumes: `LevelDraftStore`, `LevelObjectCatalog`.
- Produces: menu item `VibeGame1/Level Studio` and searchable `LevelStudioRow` models.

- [ ] **Step 1: Write failing pure-model tests for categories and alias search**

```csharp
[TestCase("blue squid", "T2.Sentry.02")]
[TestCase("Knight", "T2")]
[TestCase("last ramp", "T4")]
public void SearchResolvesVocabulary(string query, string expectedId)
{
    CollectionAssert.Contains(LevelStudioBrowser.Filter(model, query).Select(x => x.id), expectedId);
}
```

- [ ] **Step 2: Implement browser rows and window shell**

```csharp
[MenuItem("VibeGame1/Level Studio")]
public static void Open() => GetWindow<LevelStudioWindow>("Level Studio").Show();
```

Render Campaign Originals, Working Copies, Custom Levels, and Recovery; show Resume Last Draft, source fingerprint state, autosave time, validation status, and changed count. Opening a campaign row only calls `CreateFromCampaign`.

- [ ] **Step 3: Implement hierarchy grouping/filtering**

Group catalog records by zone, route, then type. Double-click frames the object; Shift/Ctrl preserve multi-selection. Search matches ID, friendly name, type, zone/split, data key, and aliases.

- [ ] **Step 4: Run tests and open the window through MCP**

```powershell
python .claude/skills/unity-editor/mcp_call.py --exec 'VibeGame1.EditorTools.LevelStudioWindow.Open(); return "opened";'
```

- [ ] **Step 5: Commit**

```powershell
git add Assets/Editor/LevelStudio Assets/Editor/Tests/LevelStudioBrowserTests.cs
git commit -m "[Astra] Add Level Studio browser and hierarchy"
```

### Task 3: Add complete scene editing and zone overlays

**Files:**
- Create: `Assets/Editor/LevelStudio/LevelStudioSceneTool.cs`
- Create: `Assets/Editor/LevelStudio/LevelStudioInspector.cs`
- Create: `Assets/Editor/LevelStudio/LevelStudioPreview.cs`
- Test: `Assets/Editor/Tests/LevelStudioEditingTests.cs`

**Interfaces:**
- Consumes: catalog records and current `LevelDraft`.
- Produces: `ApplyTransform`, `Duplicate`, `Delete`, `SetZoneBounds`, and preview rebuild operations, all Undo-aware.

- [ ] **Step 1: Write failing edit/Undo tests for each object family**

```csharp
[Test] public void MovingASelectedSpawnChangesOnlyItsDraftAndUndoRestoresIt()
{
    tool.ApplyTransform(spawnRecord, Matrix4x4.TRS(new Vector3(3,1,9), Quaternion.identity, Vector3.one));
    Assert.AreEqual(new Vector3(3,1,9), draft.definition.spawns[0].position);
    Undo.PerformUndo();
    Assert.AreEqual(original, draft.definition.spawns[0].position);
}
```

- [ ] **Step 2: Build preview objects through existing factories**

Create an editor-only `LevelStudioPreview` root with hide flags. Rebuild from the draft after structural edits; transform-only edits update the corresponding data and preview object in place. Never save the preview root into the campaign scene.

- [ ] **Step 3: Register native Scene view controls**

```csharp
SceneView.duringSceneGui += OnSceneGUI;
EditorGUI.BeginChangeCheck();
Vector3 p = Handles.PositionHandle(record.Position, Tools.pivotRotation == PivotRotation.Local ? record.Rotation : Quaternion.identity);
if (EditorGUI.EndChangeCheck()) ApplyPosition(record, p);
```

Support W/E/R, local/world, grid/angle snap, numeric inspector, multi-edit, box select, duplicate, copy/paste, delete, Undo/Redo, focus, hide/isolate, and overlay toggles. Zone volumes use `Handles.ScaleHandle`; route and projectile lines are presentation only.

- [ ] **Step 4: Verify every catalog kind has selection/edit coverage**

The parameterized fixture must enumerate all catalog kinds and fail if any lacks an editor adapter.

- [ ] **Step 5: Run focused/full EditMode and commit**

```powershell
git add Assets/Editor/LevelStudio Assets/Editor/Tests/LevelStudioEditingTests.cs
git commit -m "[Astra] Add complete Level Studio scene editing"
```

### Task 4: Implement validation, diff, apply, and F10 bridge

**Files:**
- Create: `Assets/Editor/LevelStudio/LevelStudioValidator.cs`
- Create: `Assets/Editor/LevelStudio/LevelDraftDiff.cs`
- Create: `Assets/Editor/LevelStudio/LevelApplyTransaction.cs`
- Modify: `Assets/Scripts/Level/LevelEditor.cs`
- Modify: `Assets/Scripts/Level/LevelDocument.cs`
- Modify: `Assets/Scripts/UI/MainMenuController.cs`
- Modify: `Assets/Editor/MainMenuBuilder.cs`
- Test: `Assets/Editor/Tests/LevelApplyTransactionTests.cs`
- Modify: `docs/LEVEL-EDITOR.md`, `docs/DATAFLOW.md`

**Interfaces:**
- Produces: `LevelValidationReport Validate(LevelDefinition)` and `ApplyResult Apply(LevelDraft)`.
- Produces: `LevelEditor.AttachDraft(string draftId)` for play-mode handoff.

```csharp
public sealed class LevelValidationReport
{
    public readonly List<LevelValidationIssue> errors = new List<LevelValidationIssue>();
    public readonly List<LevelValidationIssue> warnings = new List<LevelValidationIssue>();
    public bool HasErrors { get { return errors.Count > 0; } }
}
public sealed class ApplyResult
{
    public bool success, conflict;
    public string message, backupPath;
    public LevelValidationReport validation;
}
```

- [ ] **Step 1: Write failing tests for blockers, warnings, conflicts, rollback, and shared draft round-trip**

```csharp
[Test] public void FailedBuildRestoresOriginalAndKeepsDraft()
{
    var result = transaction.Apply(draft, failingBuilder);
    Assert.IsFalse(result.success);
    Assert.AreEqual(originalJson, EditorJsonUtility.ToJson(source));
    Assert.IsTrue(store.Exists(draft.manifest.draftId));
}
```

- [ ] **Step 2: Implement validator and readable diff**

Errors: duplicate/malformed IDs, orphan/overlap zones, broken references, invalid enemy keys, missing start/checkpoints, unreachable required traversal, and invalid arena flow. Warnings: boundary proximity, sparse light, unusual spacing, and unsupported Challenge Route. Diff groups add/remove/transform/tuning/zone by canonical ID.

- [ ] **Step 3: Implement transactional apply**

```csharp
public ApplyResult Apply(LevelDraft draft)
{
    if (LevelDraftStore.Fingerprint(source) != draft.manifest.sourceFingerprint) return ApplyResult.Conflict();
    var report = validator.Validate(draft.definition);
    if (report.HasErrors) return ApplyResult.Blocked(report);
    // snapshot -> copy serialized data -> save -> canonical build/reports -> restore snapshot on exception/failure
}
```

- [ ] **Step 4: Bridge F10 through the draft ID**

`LevelEditor.AttachDraft` loads the shared draft, edits it through `LevelDocument`, autosaves on return, and signals Level Studio to reload. It must not create another JSON lineage.

- [ ] **Step 5: Reveal the development-only editor entry after console unlock**

Add a generated `LEVEL EDITOR` row to the main menu. `MainMenuController.RefreshDeveloperRows()` shows it only when `Application.isEditor && DeveloperAccess.IsUnlocked`; clicking it opens/resumes the current draft flow. F10 and F1 remain shortcuts, and release players never receive a visible editor row.

- [ ] **Step 6: Run full verification and commit**

```powershell
dotnet build Assembly-CSharp.csproj
dotnet build Assembly-CSharp-Editor.csproj
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode
```

```powershell
git add Assets/Editor/LevelStudio Assets/Scripts/Level/LevelEditor.cs Assets/Scripts/Level/LevelDocument.cs Assets/Scripts/UI/MainMenuController.cs Assets/Editor/MainMenuBuilder.cs Assets/Editor/Tests/LevelApplyTransactionTests.cs docs/LEVEL-EDITOR.md docs/DATAFLOW.md
git commit -m "[Astra] Validate apply and bridge Level Studio to F10"
```
