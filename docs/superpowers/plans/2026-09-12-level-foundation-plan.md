# Level Foundation and Challenge Route Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add stable level-object identity, zone/split volumes, and Challenge Route data without losing any existing Level 1 content.

**Architecture:** Extend `LevelDefinition` additively, enumerate every serializable object through one catalog, and derive primary zone ownership from authoring anchors. Migrate serialized Insight route data with Unity rename metadata while deleting only its hand-shaped presentation.

**Tech Stack:** Unity 6000.5.10f1, C# 9, NUnit EditMode tests, Unity serialization, existing `LevelDefinitionBuilder` and `LevelDocument`.

**Spec:** `docs/superpowers/specs/2026-09-12-level-studio-dashboard-design.md`

## Global Constraints

- Namespace is `VibeGame1`; editor APIs remain under `VibeGame1.EditorTools`.
- New content is data and generators, never hand-authored scene YAML.
- Existing campaign assets must migrate without losing route anchors or object arrays.
- Only `InputReader` may touch the runtime Input System.
- A changed system updates `docs/DATAFLOW.md` in the same change.
- Exit play mode before generators; prove shipped values in `.asset` YAML.

---

### Task 1: Add stable object and zone metadata

**Files:**
- Modify: `Assets/Scripts/Data/LevelDefinition.cs`
- Create: `Assets/Scripts/Level/LevelObjectCatalog.cs`
- Test: `Assets/Editor/Tests/LevelVocabularyTests.cs`

**Interfaces:**
- Produces: `ZoneDef`, `LevelObjectMeta`, `LevelObjectRecord`, and `LevelObjectCatalog.Enumerate(LevelDefinition)`.
- Produces: `LevelObjectCatalog.AssignZones(LevelDefinition)` returning `ZoneAssignmentReport`.

```csharp
public sealed class LevelObjectRecord
{
    public LevelObjectKind kind;
    public int index;
    public object data;
    public LevelObjectMeta meta;
    public Vector3 anchor;
    public string typeKey;
}
public sealed class ZoneAssignmentReport
{
    public readonly List<string> errors = new List<string>();
    public readonly List<string> warnings = new List<string>();
}
```

- [ ] **Step 1: Write failing tests for containment, stable IDs, overlap, orphan, and override**

```csharp
[Test] public void ZoneAssignment_UsesAnchorAndRejectsOverlap()
{
    var def = ScriptableObject.CreateInstance<LevelDefinition>();
    def.zones = new[] {
        new ZoneDef { zoneId="T0", canonicalName="Opening", splitName="Opening", center=Vector3.zero, size=Vector3.one*10 },
        new ZoneDef { zoneId="T1", canonicalName="Causeway", splitName="Ninja", center=Vector3.right*20, size=Vector3.one*10 }
    };
    def.platforms = new[] { new PlatformDef { name="Deck", center=Vector3.zero, size=Vector3.one } };
    var report = LevelObjectCatalog.AssignZones(def);
    Assert.AreEqual("T0.Platform.01", def.platforms[0].meta.objectId);
    Assert.IsEmpty(report.errors);
}
```

- [ ] **Step 2: Run the focused test and confirm it fails because the types do not exist**

```powershell
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode VibeGame1.Tests.LevelVocabularyTests
```

- [ ] **Step 3: Add serializable metadata and zones**

```csharp
[Serializable] public class LevelObjectMeta
{
    public string objectId = "";
    public string friendlyName = "";
    public string zoneIdOverride = "";
}

[Serializable] public class ZoneDef
{
    public string zoneId = "T0";
    public string canonicalName = "Zone";
    public string splitName = "Split";
    public string[] aliases = new string[0];
    public int order;
    public Vector3 center;
    public Vector3 size = new Vector3(10f, 10f, 10f);
    public Color displayColor = Color.cyan;
}
```

Add `public ZoneDef[] zones = new ZoneDef[0];` to `LevelDefinition` and `public LevelObjectMeta meta = new LevelObjectMeta();` to every repeatable level-object definition. `LevelObjectCatalog` must yield kind, index, metadata, and the correct anchor for each array; singleton player start, kill zone, sky, and leaderboard use reserved IDs through the same catalog.

- [ ] **Step 4: Implement deterministic assignment and validation**

```csharp
public static ZoneAssignmentReport AssignZones(LevelDefinition level)
{
    // Sort zones by order/zoneId, classify each record's anchor, honour a valid explicit override,
    // report zero/multiple matches, and allocate the first unused XX suffix without changing existing IDs.
}
```

- [ ] **Step 5: Run focused tests and both offline builds**

```powershell
dotnet build Assembly-CSharp.csproj
dotnet build Assembly-CSharp-Editor.csproj
```

- [ ] **Step 6: Commit the independently passing metadata foundation**

```powershell
git add Assets/Scripts/Data/LevelDefinition.cs Assets/Scripts/Level/LevelObjectCatalog.cs Assets/Editor/Tests/LevelVocabularyTests.cs docs/DATAFLOW.md
git commit -m "[Astra] Add level zones and stable object vocabulary"
```

### Task 2: Migrate Insight data to Challenge Routes and remove hands

**Files:**
- Modify: `Assets/Scripts/Data/LevelDefinition.cs`
- Rename/Modify: `Assets/Scripts/Level/InsightRouteMarker.cs` -> `Assets/Scripts/Level/ChallengeRouteMarker.cs`
- Modify: `Assets/Editor/LevelDefinitionBuilder.cs`
- Modify: `Assets/Editor/LevelDefinitionExporter.cs`
- Modify: `Assets/Editor/LevelDefinitionAuthoring.cs`
- Rename/Modify: `Assets/Editor/Tests/InsightRouteMarkerTests.cs` -> `Assets/Editor/Tests/ChallengeRouteMarkerTests.cs`
- Modify: `Assets/Editor/Tests/LevelTraversalTests.cs`
- Modify: `docs/LEVEL-VOCABULARY.md`, `docs/AUTHORING.md`, `docs/TOOLING.md`, `docs/MOVEMENT-PRINCIPLES.md`, `docs/DATAFLOW.md`

**Interfaces:**
- Consumes: `LevelObjectMeta` from Task 1.
- Produces: `ChallengeRouteDef[] LevelDefinition.challengeRoutes` and colliderless `ChallengeRouteMarker` anchors with no renderers.

- [ ] **Step 1: Replace historical marker-shape assertions with migration and no-hand assertions**

```csharp
[Test] public void BuilderCreatesChallengeAnchorsWithoutHandGeometry()
{
    var root = BuildSampleChallengeRoute();
    Assert.AreEqual(1, root.GetComponentsInChildren<ChallengeRouteMarker>(true).Length);
    Assert.AreEqual(0, root.GetComponentsInChildren<Renderer>(true).Length);
    Assert.AreEqual(0, root.GetComponentsInChildren<Collider>(true).Length);
}
```

- [ ] **Step 2: Run the focused fixture and confirm the old hand expectation fails**

```powershell
python .claude/skills/unity-editor/mcp_call.py --run-tests EditMode VibeGame1.Tests.InsightRouteMarkerTests
```

- [ ] **Step 3: Rename serialized data safely**

```csharp
[FormerlySerializedAs("insightRoutes")]
public ChallengeRouteDef[] challengeRoutes = new ChallengeRouteDef[0];

[Serializable]
[MovedFrom(true, "VibeGame1", null, "InsightRouteDef")]
public class ChallengeRouteDef
{
    public LevelObjectMeta meta = new LevelObjectMeta();
    public string routeId = "ChallengeRoute";
    public string[] sourceSpawnerNames = new string[0];
    public Vector3 entryCenter, entrySize, rejoinCenter, rejoinSize;
}
```

Preserve the old field/class only as an obsolete deserialization adapter if Unity requires it; no current API or current documentation may call the route Insight.

- [ ] **Step 4: Replace `BuildInsightRoutes` with anchor-only `BuildChallengeRoutes`**

```csharp
static void BuildChallengeRoutes(LevelDefinition level, Transform parent)
{
    foreach (var data in level.challengeRoutes) {
        var root = new GameObject(ChallengeRouteMarker.NameFor(data.routeId));
        root.transform.SetParent(parent, false);
        root.AddComponent<ChallengeRouteMarker>().Configure(data);
    }
}
```

- [ ] **Step 5: Update authoring IDs/data and regenerate Level 1**

Use `T1_Challenge_Flare`, `T2_Challenge_Flare`, and `T3_Challenge_Flare`; retain the exact entry/rejoin vectors and source spawner names from the shipped asset.

```powershell
python .claude/skills/unity-editor/mcp_call.py --exec 'VibeGame1.EditorTools.LevelDefinitionAuthoring.Run(); return "ok";'
```

- [ ] **Step 6: Prove the shipped YAML and reports**

```powershell
rg -n "Insight|Palm|Wrist|Finger_" Assets/Data/Levels/Level_01_Level.asset Assets/Scenes/Level_01.unity
python Tools/level_arc_offline.py --after --sight
```

Expected: no current Insight IDs or generated hand parts; all three Challenge Routes retain their anchors; arc report passes.

- [ ] **Step 7: Run focused/full EditMode and commit**

```powershell
git add Assets/Scripts/Data/LevelDefinition.cs Assets/Scripts/Level/ChallengeRouteMarker.cs Assets/Editor/LevelDefinitionBuilder.cs Assets/Editor/LevelDefinitionExporter.cs Assets/Editor/LevelDefinitionAuthoring.cs Assets/Editor/Tests/ChallengeRouteMarkerTests.cs Assets/Editor/Tests/LevelTraversalTests.cs docs/LEVEL-VOCABULARY.md docs/AUTHORING.md docs/TOOLING.md docs/MOVEMENT-PRINCIPLES.md docs/DATAFLOW.md
git commit -m "[Astra] Rename challenge routes and remove hand markers"
```
