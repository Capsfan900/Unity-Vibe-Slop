# Parry Choreography Recorder and Module Generator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record a designer's intended traversal/parry rhythm and deterministically generate one editable best-fit projectile encounter module.

**Architecture:** Upgrade `PlayerTimingCapture` to an Idle/Primed/Recording state machine with automatic JSON export, add simple data-built recording stages, and keep the deterministic solver editor-only. The solver consumes recorded movement/beats and existing EnemyData/projectile flight laws, then emits a working-copy module with explicit per-beat error.

**Tech Stack:** Unity runtime capture, New Input System through `InputReader`, C# 9, JSON, NUnit, existing `ProjectileFlightMath`, `LevelObjectCatalog`, and Level Studio draft APIs.

**Spec:** `docs/superpowers/specs/2026-09-12-level-studio-dashboard-design.md`

## Global Constraints

- `InputReader` is the only runtime script touching the Input System.
- `0` has meaning only after the developer-gated recorder is primed.
- Stopping always atomically exports one JSON format to `timing-captures`.
- The solver uses existing parkour-projectile enemy types and never changes combat resolution.
- Generated modules are working copies, never direct campaign writes.

---

### Task 1: Prime and toggle one-format capture

**Files:**
- Modify: `Assets/InputSystem_Actions.inputactions`
- Modify: `Assets/Scripts/Core/InputReader.cs`
- Modify: `Assets/Scripts/UI/DeveloperConsole.cs`
- Modify: `Assets/Scripts/Debug/PlayerTimingCapture.cs`
- Modify: `Assets/Editor/Tests/PlayerTimingCaptureTests.cs`
- Create: `Assets/Editor/Tests/InputActionsTests.cs`
- Modify: `docs/DATAFLOW.md`

**Interfaces:**
- Produces: `PlayerTimingCapture.State`, `Prime`, `ToggleCapture`, `StopAndExport`, and `LastExportPath`.
- Produces: `InputReader.TimingCaptureTogglePressed` backed by `<Keyboard>/0`.

- [ ] **Step 1: Write failing state-machine and binding tests**

```csharp
[Test] public void PrimeThenZeroStarts_SecondZeroStopsAndExports()
{
    Assert.AreEqual(CaptureState.Primed, harness.Prime());
    Assert.AreEqual(CaptureState.Recording, harness.Toggle());
    Assert.AreEqual(CaptureState.Primed, harness.Toggle());
    Assert.That(harness.LastExportPath, Does.EndWith(".json"));
    Assert.IsTrue(File.Exists(harness.LastExportPath));
}
```

Assert `TimingCaptureToggle` is bound to `<Keyboard>/0` and no other script references `Keyboard.current` for this feature.

- [ ] **Step 2: Run focused tests and verify they fail**

```powershell
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode VibeGame1.Tests.PlayerTimingCaptureTests VibeGame1.Tests.InputActionsTests
```

- [ ] **Step 3: Implement the state machine and console policy**

```csharp
public enum CaptureState { Idle, Primed, Recording }
public static string Prime();
public static string ToggleCapture();
public static string StopAndExport();
```

`timing prime` is canonical; `timing start` calls `Prime` as a compatibility alias. While primed, `Update` consumes `InputReader.I.TimingCaptureTogglePressed`. First press creates a fresh take; second press stops and atomically exports, then returns to Primed.

- [ ] **Step 4: Add persistent HUD state without inventing another screen**

Use the existing prompt/status event lane to show `PARRY RECORDING` only while recording and the exported filename briefly after stop. No gameplay timing or time scale changes.

- [ ] **Step 5: Run focused tests and offline builds**

```powershell
dotnet build Assembly-CSharp.csproj
dotnet build Assembly-CSharp-Editor.csproj
```

- [ ] **Step 6: Commit**

```powershell
git add Assets/InputSystem_Actions.inputactions Assets/Scripts/Core/InputReader.cs Assets/Scripts/UI/DeveloperConsole.cs Assets/Scripts/Debug/PlayerTimingCapture.cs Assets/Editor/Tests docs/DATAFLOW.md
git commit -m "[Astra] Prime parry recording and auto-export with zero"
```

### Task 2: Capture authoring-grade path and desired beats

**Files:**
- Create: `Assets/Scripts/Debug/ParryCaptureSchema.cs`
- Modify: `Assets/Scripts/Debug/PlayerTimingCapture.cs`
- Test: `Assets/Editor/Tests/PlayerTimingCaptureTests.cs`

**Interfaces:**
- Produces: versioned `ParryCaptureFile`, `TraversalSample`, and `DesiredParryBeat` JSON schema.

- [ ] **Step 1: Write failing schema tests**

```csharp
[Test] public void DesiredBeatCarriesMotionSurfaceLookAndVocabulary()
{
    var beat = capture.desiredBeats.Single();
    Assert.AreEqual("T0", beat.zoneId);
    Assert.AreEqual("Ramp", beat.surfaceType);
    Assert.Greater(beat.lookDirection.sqrMagnitude, .9f);
    Assert.Greater(beat.playerSpeed, 0f);
}
```

- [ ] **Step 2: Implement version 2 schema**

```csharp
[Serializable] public sealed class DesiredParryBeat
{
    public int ordinal;
    public float captureSeconds;
    public Vector3 position, velocity, lookDirection;
    public float playerSpeed;
    public string surfaceType, zoneId, splitName;
    public bool grounded, sliding, wallRunning, dashing, pulling;
}
```

Keep observed projectile events for diagnosis. Each human parry press always creates a desired beat, even when no projectile exists. Determine surface by the motor's grounded contact/raycast and known traversal components; use `Unknown` rather than guessing.

- [ ] **Step 3: Make export atomic and singular**

Write `timing-<UTC>.json.tmp`, close, then replace/rename to `.json`. Remove the normal need for manual export while retaining a read-only/status compatibility response. A failed export keeps the take and reports the exception.

- [ ] **Step 4: Run JSON round-trip and cap tests, then commit**

```powershell
git add Assets/Scripts/Debug/ParryCaptureSchema.cs Assets/Scripts/Debug/PlayerTimingCapture.cs Assets/Editor/Tests/PlayerTimingCaptureTests.cs
git commit -m "[Astra] Capture desired parry beats and traversal context"
```

### Task 3: Add simple regenerable recording stages

**Files:**
- Create: `Assets/Editor/ParryRecordingStageFactory.cs`
- Create via generator: `Assets/Data/Levels/Recording/Recording_*.asset`
- Test: `Assets/Editor/Tests/ParryRecordingStageTests.cs`
- Modify: `docs/AUTHORING.md`, `docs/TOOLING.md`

**Interfaces:**
- Produces: Flat Walkway, Downhill Ramp, Ice/Water Slideway, and Mixed Traversal `LevelDefinition` templates.

- [ ] **Step 1: Write failing geometry and identity tests**

```csharp
[TestCase("Recording_Flat", "Platform")]
[TestCase("Recording_Downhill", "Ramp")]
[TestCase("Recording_Ice", "Water")]
[TestCase("Recording_Mixed", "Mixed")]
public void RecordingStageHasOneClearZoneAndNoShooters(string key, string surface)
{
    var def = Load(key);
    Assert.AreEqual(1, def.zones.Length);
    Assert.IsFalse(def.spawns.Any(s => s.prefabKey.StartsWith("pshooter_")));
}
```

- [ ] **Step 2: Implement idempotent factory menu**

```csharp
[MenuItem("VibeGame1/10. Build Parry Recording Stages")]
public static void CreateAll()
{
    CreateFlat(); CreateDownhill(); CreateIce(); CreateMixed();
    AssetDatabase.SaveAssets();
}
```

Keep geometry minimal, long enough for stable acceleration and several beats, with one zone, start, safe bounds, and unobstructed lateral perch envelopes.

- [ ] **Step 3: Generate, read back assets, run tests, and commit**

```powershell
python .claude/skills/unity-editor/mcp_call.py --exec 'VibeGame1.EditorTools.ParryRecordingStageFactory.CreateAll(); return "ok";'
```

```powershell
git add Assets/Editor/ParryRecordingStageFactory.cs Assets/Data/Levels/Recording Assets/Editor/Tests/ParryRecordingStageTests.cs docs/AUTHORING.md docs/TOOLING.md
git commit -m "[Astra] Add base parry recording stages"
```

### Task 4: Solve and emit one deterministic best-fit module

**Files:**
- Create: `Assets/Editor/LevelStudio/ParryModuleSolver.cs`
- Create: `Assets/Editor/LevelStudio/ParryModuleDefinition.cs`
- Create: `Assets/Editor/LevelStudio/ParryModuleReport.cs`
- Test: `Assets/Editor/Tests/ParryModuleSolverTests.cs`
- Modify: `Assets/Editor/LevelStudio/LevelStudioWindow.cs`
- Modify: `docs/DATAFLOW.md`

**Interfaces:**
- Produces: `ParryModuleResult Solve(ParryCaptureFile capture, LevelDefinition stage, ParrySolverSettings settings)`.
- Produces: `ApplyToDraft(ParryModuleResult, LevelDraft, string zoneId)`.

```csharp
public sealed class ParryModuleResult
{
    public ParryModuleDefinition module;
    public ParryModuleReport report;
    public bool success;
}
public sealed class ParryBeatFit
{
    public int ordinal;
    public string enemyKey, objectId;
    public float timeErrorSeconds, spatialErrorMetres, viewAngleDegrees, confidence;
    public bool visible, satisfied;
    public string failure;
}
```

- [ ] **Step 1: Write fixed-fixture tests for determinism and no dropped beats**

```csharp
[Test] public void SameCaptureProducesByteEquivalentModule()
{
    var a = solver.Solve(capture, stage, settings);
    var b = solver.Solve(capture, stage, settings);
    Assert.AreEqual(JsonUtility.ToJson(a.module), JsonUtility.ToJson(b.module));
    Assert.AreEqual(capture.desiredBeats.Count, a.report.beats.Count);
}
```

Add fixtures for flat singles, downhill alternating shots, ice high-speed contacts, a valid Heavy triple, and an unsatisfied beat reported as an error.

- [ ] **Step 2: Implement deterministic candidate generation**

For each beat, sample legal lateral perch candidates at fixed distances/angles relative to the recorded look and motion vector. Reject range, LOS, blocker, cue-margin, route-obstruction, and zone-bound violations using existing projectile flight math. Group three beats into a Heavy phrase only when their spacing fits the shipped Heavy cadence; otherwise use autonomous Sentry/Surge candidates.

- [ ] **Step 3: Implement stable scoring and selection**

```csharp
score = timeErrorSeconds * 100f
      + spatialErrorMetres * 10f
      + viewAngleDegrees * 0.2f
      + obstructionPenalty
      + enemyCountPenalty;
```

Sort ties by enemy key, beat ordinal, and quantized candidate position. Emit each desired beat with predicted contact, time/spatial error, visibility, chosen enemy, and confidence. Never omit an unsatisfied beat.

- [ ] **Step 4: Add Generate Module to Level Studio**

Select one capture, preview its path/beats, run Solve, show the report, and add the result only to the open draft. Rerun Selected Beats preserves unselected manual edits.

- [ ] **Step 5: Run solver, encounter, arc, and full EditMode tests; commit**

```powershell
python Tools/level_arc_offline.py --after --sight
```

```powershell
git add Assets/Editor/LevelStudio Assets/Editor/Tests/ParryModuleSolverTests.cs docs/DATAFLOW.md
git commit -m "[Astra] Generate best-fit projectile modules from parry runs"
```
