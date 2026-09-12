using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VibeGame1;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// The in-game level editor (docs/LEVEL-EDITOR.md). What has to stay true:
    /// the document round-trips through JSON with every piece type; the grid and size maths is what the
    /// doc says; the RUNTIME piece factory builds the same objects, by name and position, that the
    /// editor-time builder (menu item 8) builds from the same data; and the HUD prefab carries the editor
    /// with its library filled (rule 9).
    /// </summary>
    public class LevelEditorTests
    {
        static LevelDocument Sample()
        {
            var d = LevelDocument.NewDefault("test level");
            d.platforms.Add(new PlatformDef { name = "Step", center = new Vector3(4f, 0.5f, 6f), size = new Vector3(4f, 1f, 4f), materialKey = "Platform", trim = true, trimMaterialKey = "NeonPink" });
            d.spawns.Add(new SpawnDef { name = "Spawn_Grunt", prefabKey = "Enemy_Grunt", position = new Vector3(0f, 0.3f, 6f), yaw = 180f });
            d.pickups.Add(new PickupDef { name = "Pickup_Grapple", itemKey = "Grapple", position = new Vector3(2f, 1.2f, 2f) });
            d.checkpoints.Add(new CheckpointDef { name = "Checkpoint_1", position = new Vector3(0f, 0f, 10f), spawnOffset = new Vector3(0f, 0.2f, -2f) });
            d.torches.Add(new TorchDef { name = "Torch_0", basePosition = new Vector3(-6f, 0f, -6f) });
            d.balloons.Add(new BalloonDef { name = "Balloon_0", position = new Vector3(0f, 3f, 0f), launchSpeed = 14f, respawnSeconds = 2.5f, radius = 0.6f });
            d.waters.Add(new WaterDef { name = "Water_0", center = new Vector3(0f, 0.02f, -4f), size = new Vector3(6f, 0.04f, 6f), flowDirection = Vector3.forward, flowSpeed = 6f });
            return d;
        }

        [Test]
        public void TheDocumentRoundTripsThroughJson_WithEveryPieceType()
        {
            var d = Sample();
            var back = LevelDocument.FromJson(d.ToJson());
            Assert.IsNotNull(back);
            Assert.AreEqual(d.levelId, back.levelId);
            Assert.AreEqual(2, back.platforms.Count); Assert.AreEqual(1, back.spawns.Count); Assert.AreEqual(1, back.pickups.Count);
            Assert.AreEqual(1, back.checkpoints.Count); Assert.AreEqual(1, back.torches.Count);
            Assert.AreEqual(1, back.balloons.Count, "balloons must survive the round trip");
            Assert.AreEqual(1, back.waters.Count, "water must survive the round trip");
            Assert.AreEqual(d.platforms[1].center, back.platforms[1].center);
            Assert.AreEqual(d.waters[0].flowDirection, back.waters[0].flowDirection);
            Assert.AreEqual(d.balloons[0].launchSpeed, back.balloons[0].launchSpeed);
            Assert.AreEqual(d.playerStart, back.playerStart);
        }

        [Test]
        public void AnEmptyJsonListNeverComesBackNull()
        {
            var back = LevelDocument.FromJson("{\"levelId\":\"x\"}");
            Assert.IsNotNull(back.platforms); Assert.IsNotNull(back.balloons); Assert.IsNotNull(back.waters);
            Assert.AreEqual(0, back.PieceCount);
        }

        [Test]
        public void TheDocumentMirrorsADefinitionBothWays()
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                Sample().CopyTo(def);
                Assert.AreEqual(2, def.platforms.Length); Assert.AreEqual(1, def.balloons.Length); Assert.AreEqual(1, def.waters.Length);
                var again = LevelDocument.FromDefinition(def);
                Assert.AreEqual(2, again.platforms.Count); Assert.AreEqual("Step", again.platforms[1].name);
                Assert.AreEqual(def.playerStartYaw, again.playerStartYaw);
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void GridSnap_IsTheDocsGrid()
        {
            Assert.AreEqual(new Vector3(2f, 0f, -3f), LevelEditorMath.Snap(new Vector3(2.4f, 0.2f, -3.3f), 1f));
            Assert.AreEqual(new Vector3(2.5f, 0f, -3.5f), LevelEditorMath.Snap(new Vector3(2.4f, 0.2f, -3.3f), 0.5f));
            Assert.AreEqual(new Vector3(2.4f, 0.2f, -3.3f), LevelEditorMath.Snap(new Vector3(2.4f, 0.2f, -3.3f), 0f), "grid 0 = free");
            Assert.AreEqual(1f, LevelEditorMath.GridFor(LevelPieceKind.Platform));
            Assert.AreEqual(1f, LevelEditorMath.GridFor(LevelPieceKind.Water));
            Assert.AreEqual(0.5f, LevelEditorMath.GridFor(LevelPieceKind.Balloon));
            Assert.AreEqual(0.5f, LevelEditorMath.GridFor(LevelPieceKind.Spawn));
        }

        [Test]
        public void SnapWithHysteresis_HoldsTheCellOnABoundary()
        {
            // The jank: an aim resting on the line between two cells flickered the preview between them
            // every frame. With a quarter-cell of hysteresis the previous cell is kept until the raw point
            // is clearly into the next one.
            float g = 1f, f = 0.25f;
            var prev = new Vector3(2f, 0f, 0f);
            // on the boundary (2.5): plain snap would round to 3 (banker's aside), hysteresis keeps 2
            var held = LevelEditorMath.SnapWithHysteresis(new Vector3(2.5f, 0f, 0f), prev, true, g, f);
            Assert.AreEqual(2f, held.x, 1e-4f, "on the boundary the previous cell must be kept");
            // a hair past the boundary still keeps it (2.7 is within 0.75 of 2)
            Assert.AreEqual(2f, LevelEditorMath.SnapWithHysteresis(new Vector3(2.7f, 0f, 0f), prev, true, g, f).x, 1e-4f);
            // clearly into the next cell (2.8 > 2 + 0.75) switches
            Assert.AreEqual(3f, LevelEditorMath.SnapWithHysteresis(new Vector3(2.8f, 0f, 0f), prev, true, g, f).x, 1e-4f);
            // without a previous cell it is a plain snap; grid 0 is free placement
            Assert.AreEqual(3f, LevelEditorMath.SnapWithHysteresis(new Vector3(2.8f, 0f, 0f), prev, false, g, f).x, 1e-4f);
            Assert.AreEqual(2.8f, LevelEditorMath.SnapWithHysteresis(new Vector3(2.8f, 0f, 0f), prev, true, 0f, f).x, 1e-4f);
        }

        [Test]
        public void TheEase_IsTheSameAtAnyFrameRate()
        {
            // MOVEMENT-PRINCIPLES rule 8: the fly and the preview ease by a time constant, so the fraction
            // of the way they travel in 0.2 s is the same whether that is 4 frames or 48.
            float tau = 0.12f;
            float x20 = 0f, x240 = 0f;
            for (int i = 0; i < 4; i++) x20 += (1f - x20) * LevelEditorMath.EaseFactor(tau, 0.05f);
            for (int i = 0; i < 48; i++) x240 += (1f - x240) * LevelEditorMath.EaseFactor(tau, 0.05f / 12f);
            Assert.AreEqual(x20, x240, 0.002f, "the ease depends on the frame rate");
            Assert.AreEqual(1f - Mathf.Exp(-0.2f / tau), x20, 0.002f, "and it is the closed-form exponential");
            Assert.AreEqual(1f, LevelEditorMath.EaseFactor(0f, 0.016f), 1e-5f, "a zero time constant snaps");
        }

        [Test]
        public void TheHudShipsTheControlNumbers_NotTheInitialisers()
        {
            // Rule 9. The prefab's LevelEditor carries the eases, the hysteresis and the grab hold.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HUD.prefab");
            if (prefab == null) Assert.Ignore("HUD.prefab not built");
            var ed = prefab.GetComponent<LevelEditor>();
            if (ed == null) Assert.Ignore("HUD.prefab predates the editor");
            string yaml = System.IO.File.ReadAllText("Assets/Prefabs/HUD.prefab");
            if (!yaml.Contains("snapHysteresis:")) Assert.Ignore("HUD.prefab predates the control rework - run 5. Build HUD");
            Assert.Greater(ed.flyEase, 0f); Assert.Greater(ed.previewEase, 0f);
            Assert.That(ed.snapHysteresis, Is.InRange(0.1f, 0.5f));
            Assert.That(ed.grabHoldSeconds, Is.InRange(0.1f, 0.35f), "a grab hold must be short enough to feel like a drag, long enough not to eat a click");
            if (yaml.Contains("undoDepth:"))
            {
                Assert.That(ed.surfaceDebounceFrames, Is.InRange(2, 6), "the aim surface debounce is a few frames: enough to kill edge flicker, not enough to feel late");
                Assert.GreaterOrEqual(ed.undoDepth, 20, "Ctrl+Z must hold a real session of edits");
                Assert.That(ed.decalCells, Is.InRange(3, 25));
                Assert.IsNotNull(ed.readout, "the numeric readout is on the panel");
                Assert.IsNotNull(ed.undoButton); Assert.IsNotNull(ed.placeHereButton);
            }
            Assert.IsTrue(ed.enterCursorFree, "the panel must work on the first click: the editor opens with the cursor free");
            Assert.Greater(ed.groundSize, 50f, "without a ground the aim has nothing to land on");
        }

        [Test]
        public void TheSizeLadder_StepsThroughThePresetsThenStretches()
        {
            Assert.AreEqual(4f, LevelEditorMath.StepSize(2f, 1)); Assert.AreEqual(6f, LevelEditorMath.StepSize(4f, 1));
            Assert.AreEqual(8f, LevelEditorMath.StepSize(6f, 1)); Assert.AreEqual(10f, LevelEditorMath.StepSize(8f, 1), "past the ladder: +2 m");
            Assert.AreEqual(40f, LevelEditorMath.StepSize(40f, 1), "capped");
            Assert.AreEqual(6f, LevelEditorMath.StepSize(8f, -1)); Assert.AreEqual(2f, LevelEditorMath.StepSize(4f, -1));
            Assert.AreEqual(1f, LevelEditorMath.StepSize(2f, -1)); Assert.AreEqual(1f, LevelEditorMath.StepSize(1f, -1), "floor at 1 m");
            Assert.AreEqual(90f, LevelEditorMath.RotateStep(0f, 1)); Assert.AreEqual(0f, LevelEditorMath.RotateStep(270f, 1));
            Assert.AreEqual(270f, LevelEditorMath.RotateStep(0f, -1), "Shift+T: the back-step, a reversible turn");
            Assert.AreEqual(0f, LevelEditorMath.RotateStep(90f, -1));
            Assert.AreEqual(180f, LevelEditorMath.RotateStep(270f, -1));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), LevelEditorMath.CenterForTop(new Vector3(1f, 2.5f, 3f), new Vector3(4f, 1f, 4f)));
        }

        [Test]
        public void FileNames_AreSafe()
        {
            Assert.AreEqual("my_level-2", LevelEditorMath.SafeFileName("my level-2"));
            Assert.AreEqual("custom", LevelEditorMath.SafeFileName("   "));
            Assert.AreEqual("a_b", LevelEditorMath.SafeFileName("..a/b.."));
            Assert.AreEqual("custom", LevelEditorMath.SafeFileName(null));
        }

        [Test]
        public void TheRuntimeFactory_BuildsWhatTheEditorBuilderBuilds()
        {
            // The whole point of one factory: the same document built with the editor context (menu
            // item 8) and with a runtime-shaped context (plain Instantiate, no statics) must produce the
            // same object names at the same positions. Prefab-based pieces need the prefabs (4. Build
            // Prefabs) - skip cleanly if they are not there yet rather than fail on a fresh checkout.
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Balloon.prefab") == null)
                Assert.Ignore("Assets/Prefabs/Balloon.prefab missing - run VibeGame1/4. Build Prefabs, then re-run.");

            var doc = Sample();
            var a = new GameObject("~A").transform;
            var b = new GameObject("~B").transform;
            try
            {
                var editorCtx = EditorContext.Default();
                var runtimeCtx = new LevelPieceContext
                {
                    materials = editorCtx.materials, prefabs = editorCtx.prefabs, items = editorCtx.items,
                    instantiate = prefab => Object.Instantiate(prefab), markStatic = null,
                };
                var ca = LevelPieceFactory.BuildDocument(doc, a, editorCtx);
                var cb = LevelPieceFactory.BuildDocument(doc, b, runtimeCtx);
                Assert.AreEqual(ca.ToString(), cb.ToString(), "the two contexts built different counts");

                var mapA = new Dictionary<string, Vector3>();
                foreach (var t in a.GetComponentsInChildren<Transform>(true)) if (t != a) mapA[Path(t, a)] = t.position;
                int compared = 0;
                foreach (var t in b.GetComponentsInChildren<Transform>(true))
                {
                    if (t == b) continue;
                    string path = Path(t, b);
                    Assert.IsTrue(mapA.ContainsKey(path), "runtime built '" + path + "' which the editor build did not");
                    Assert.Less((mapA[path] - t.position).magnitude, 0.001f, path + " is at a different position");
                    compared++;
                }
                Assert.Greater(compared, 20, "too few objects compared: " + compared);
                Assert.IsNotNull(a.Find("Ground_Trim_N") ?? a.Find("Ground/Ground_Trim_N"), "the ground's trim bars");
                Assert.IsNotNull(b.GetComponentInChildren<WaterVolume>(true), "water volume (runtime)");
                Assert.IsNotNull(b.GetComponentInChildren<Balloon>(true), "balloon (runtime)");
                Assert.IsNotNull(b.GetComponentInChildren<EnemySpawner>(true), "spawner (runtime)");
                foreach (var piece in b.GetComponentsInChildren<LevelPiece>(true))
                    Assert.IsTrue(piece.index >= 0, piece.name + " has no index");
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
            }
        }

        static string Path(Transform t, Transform root)
        {
            var s = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        [Test]
        public void TheHudCarriesTheEditor_WithItsLibraryFilled()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HUD.prefab");
            Assert.IsNotNull(hud, "HUD.prefab missing - run VibeGame1/5. Build HUD");
            var ed = hud.GetComponent<LevelEditor>();
            if (ed == null) Assert.Ignore("HUD.prefab has no LevelEditor yet - run VibeGame1/5. Build HUD on this builder, then re-run.");

            Assert.IsNotNull(ed.panel, "panel"); Assert.IsNotNull(ed.pieceList); Assert.IsNotNull(ed.status);
            Assert.IsNotNull(ed.nameField, "name field"); Assert.IsNotNull(ed.saveButton); Assert.IsNotNull(ed.loadButton);
            Assert.IsNotNull(ed.playButton); Assert.IsNotNull(ed.exitButton);
            Assert.AreEqual(ed.materialKeys.Length, ed.materials.Length);
            Assert.AreEqual(ed.prefabKeys.Length, ed.prefabs.Length);
            for (int i = 0; i < ed.materialKeys.Length; i++) Assert.IsNotNull(ed.materials[i], "library material " + ed.materialKeys[i]);
            for (int i = 0; i < ed.prefabKeys.Length; i++) Assert.IsNotNull(ed.prefabs[i], "library prefab " + ed.prefabKeys[i]);
            Assert.Greater(ed.spawnKeys.Length, 1);
            Assert.Greater(ed.items.Length, 0);
            // rule 9: the shipped numbers, not the initialisers
            Assert.AreEqual(12f, ed.flySpeed, 1e-4f); Assert.AreEqual(11f, ed.balloonLaunchSpeed, 1e-4f);   // Balloon.prefab (2026-09-05: 14 -> 11)
            Assert.AreEqual(6f, ed.wallFaceHeight, 1e-4f); Assert.AreEqual(6f, ed.waterFlowSpeed, 1e-4f);
            Assert.IsTrue(ed.hideSceneRootsWhileEditing);
            var menu = hud.GetComponent<TestMenu>();
            Assert.IsNotNull(menu != null ? menu.levelEditorButton : null, "the F1 menu's LEVEL EDITOR button");
            var console = hud.GetComponent<DeveloperConsole>();
            Assert.IsNotNull(console, "the HUD must carry the release-accessible command console");
            Assert.IsNotNull(console.panel); Assert.IsNotNull(console.output); Assert.IsNotNull(console.input);
        }

        [Test]
        public void TheMainMenuHasACustomTemplateRow()
        {
            var menu = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MainMenu.prefab");
            if (menu == null) Assert.Ignore("MainMenu.prefab missing - run VibeGame1/9. Build Main Menu.");
            var c = menu.GetComponent<MainMenuController>();
            Assert.IsNotNull(c);
            if (c.customTemplate == null || c.customTemplate.root == null)
                Assert.Ignore("no custom template row yet - run VibeGame1/9. Build Main Menu on this builder, then re-run.");
            Assert.IsFalse(c.customTemplate.root.activeSelf, "the template stays hidden; clones are made per saved file");
            Assert.IsNotNull(c.customTemplate.button);
        }
    
        [Test]
        public void ThePanelAndTheWheelShareOneKind_AndTheHighlightLeavesNoTrace()
        {
            // From play, 2026-09-05: the panel's piece list was text, so clicking it changed nothing. Now every
            // kind is a button wired to SelectKind, which writes the SAME field the wheel and keys write.
            var go = new GameObject("editor");
            try
            {
                var ed = go.AddComponent<LevelEditor>();
                ed.SelectKind(LevelPieceKind.Water);
                Assert.AreEqual(LevelPieceKind.Water, ed.CurrentKind, "SelectKind did not reach the field placement reads.");
                ed.SelectKind(LevelPieceKind.Balloon);
                Assert.AreEqual(LevelPieceKind.Balloon, ed.CurrentKind);

                // The aimed-piece highlight is a property block on ONE renderer, cleared exactly when the aim moves on.
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(go.transform, false);
                var piece = cube.AddComponent<LevelPiece>();
                var r = cube.GetComponent<Renderer>();
                ed.Highlight(piece);
                Assert.IsTrue(r.HasPropertyBlock(), "the aimed piece was not tinted.");
                ed.Highlight(null);
                Assert.IsFalse(r.HasPropertyBlock(), "the tint outlived the aim: that is the 'flashing map' shape of bug.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheSizeStepsFromThePiece_NotFromThePendingSize()
        {
            // From play ("the size buttons half work"): aiming at a 2 m slab with 8 m pending and pressing +
            // used to make it 10 m. It steps from the slab's own size: 2 -> 4, 8 -> 6, and clamps at both ends.
            Assert.AreEqual(4f, LevelEditorMath.StepSizeFrom(2f, 1), 1e-4f);
            Assert.AreEqual(6f, LevelEditorMath.StepSizeFrom(8f, -1), 1e-4f);
            Assert.AreEqual(40f, LevelEditorMath.StepSizeFrom(40f, 1), 1e-4f, "clamped at the top");
            Assert.AreEqual(1f, LevelEditorMath.StepSizeFrom(1f, -1), 1e-4f, "clamped at the bottom");
            Assert.AreEqual(3f, LevelEditorMath.StepSizeFrom(3f, 0), 1e-4f);
        }

        [Test]
        public void TheUndoStack_IsBounded_AndAnEditForgetsTheRedoBranch()
        {
            var u = new LevelUndoStack(3);
            u.Push("a"); u.Push("b"); u.Push("c"); u.Push("d");
            Assert.AreEqual(3, u.UndoCount, "bounded: the oldest snapshot falls off");
            Assert.AreEqual("d", u.Undo("e")); Assert.AreEqual(1, u.RedoCount);
            Assert.AreEqual("e", u.Redo("d")); Assert.AreEqual(0, u.RedoCount);
            Assert.AreEqual("d", u.Undo("e"));
            u.Push("x");
            Assert.AreEqual(0, u.RedoCount, "a new edit after an undo forgets the redo branch");
            Assert.AreEqual("x", u.Undo("y")); Assert.AreEqual("c", u.Undo("x")); Assert.AreEqual("b", u.Undo("c"));
            Assert.IsNull(u.Undo("b"), "an empty stack returns null, never throws");
        }

        [Test]
        public void TheAimSurface_IsHeldUntilANewOneHasBeenSeenForTheDebounce()
        {
            // The ray flicks between a slab top (y 1) and the ground (y 0) around the slab's edge. One or
            // two frames of the other surface keep the old height; three in a row switch.
            var f = new AimSurfaceFilter(3);
            Assert.AreEqual(1f, f.Filter(1f, 0.25f), 1e-4f);
            Assert.AreEqual(1f, f.Filter(0f, 0.25f), 1e-4f);
            Assert.AreEqual(1f, f.Filter(1f, 0.25f), 1e-4f, "a flick back resets the count");
            Assert.AreEqual(1f, f.Filter(0f, 0.25f), 1e-4f);
            Assert.AreEqual(1f, f.Filter(0f, 0.25f), 1e-4f);
            Assert.AreEqual(0f, f.Filter(0f, 0.25f), 1e-4f, "three frames on the ground and the preview follows");
            Assert.AreEqual(0f, f.Filter(0.1f, 0.25f), 1e-4f, "jitter within the tolerance is the same surface");
        }

        [Test]
        public void TheArrowNudge_MovesAlongTheGridAxisNearestTheLook()
        {
            // Looking roughly along +z, "up" is +z one cell and "right" is +x; looking along -x, "up" is -x.
            Vector3 d = LevelEditorMath.NudgeDelta(new Vector2(0f, 1f), new Vector3(0.2f, -0.5f, 0.8f), 1f, false);
            Assert.AreEqual(new Vector3(0f, 0f, 1f), d);
            d = LevelEditorMath.NudgeDelta(new Vector2(1f, 0f), new Vector3(0.2f, -0.5f, 0.8f), 1f, false);
            Assert.AreEqual(new Vector3(1f, 0f, 0f), d);
            d = LevelEditorMath.NudgeDelta(new Vector2(0f, 1f), new Vector3(-0.9f, 0f, 0.1f), 0.5f, false);
            Assert.AreEqual(new Vector3(-0.5f, 0f, 0f), d);
            d = LevelEditorMath.NudgeDelta(new Vector2(0f, -1f), Vector3.forward, 1f, true);
            Assert.AreEqual(new Vector3(0f, -1f, 0f), d, "with Ctrl the up/down keys move along Y");
        }

        [Test]
        public void TheInputActionsAsset_HasEditorAndConsoleBindings()
        {
            // docs/handoffs/level-editor-controls-handoff.md: the eyedropper (`EditorPick`) and a `delete`
            // binding on `EditorDelete` (alongside `X`), so Delete lives on Delete like every other 3D editor.
            string path = System.IO.Path.Combine(Application.dataPath, "InputSystem_Actions.inputactions");
            Assert.IsTrue(System.IO.File.Exists(path), path);
            string json = System.IO.File.ReadAllText(path);
            var asset = UnityEngine.InputSystem.InputActionAsset.FromJson(json);
            try
            {
                Assert.IsNotNull(asset.FindActionMap("Player", true).FindAction("EditorPick", false), "EditorPick action is missing");
                Assert.IsNotNull(asset.FindActionMap("Player", true).FindAction(InputReader.ConsoleToggleActionName, false), "ConsoleToggle action is missing");
                Assert.IsNotNull(asset.FindActionMap("Player", true).FindAction(InputReader.ConsoleSubmitActionName, false), "ConsoleSubmit action is missing");
            }
            finally { Object.DestroyImmediate(asset); }

            Assert.IsTrue(json.Contains("\"name\": \"EditorPick\""), "EditorPick action is missing");
            Assert.IsTrue(json.Contains("\"path\": \"<Keyboard>/delete\""), "the Delete key alias for EditorDelete is missing");
            Assert.IsTrue(json.Contains("\"path\": \"<Keyboard>/backquote\""), "the console must open on Backquote");
            Assert.IsTrue(json.Contains("\"path\": \"<Keyboard>/enter\""), "the console must submit on Enter");
        }

        [Test]
        public void EveryBuildF10_RequiresTheSharedSessionCapability()
        {
            DeveloperAccess.LockForTests();
            try
            {
                Assert.IsFalse(InputReader.LevelEditorShortcutAllowed(false, false),
                    "an ordinary release build must reject F10");
                Assert.IsFalse(InputReader.LevelEditorShortcutAllowed(true, false),
                    "editor and development builds must not bypass the console capability");

                var help = DeveloperConsole.ExecuteCommand("  HELP  ");
                Assert.IsFalse(help.clear);
                Assert.AreEqual(DeveloperConsole.HelpText, help.message);
                Assert.IsFalse(InputReader.LevelEditorSessionUnlocked, "help must not grant access");

                var rejectedLegacy = DeveloperConsole.ExecuteCommand("editor    unlock");
                Assert.IsFalse(InputReader.LevelEditorSessionUnlocked,
                    "the old public phrase must never grant developer access");
                StringAssert.Contains("LOCKED", rejectedLegacy.message);

                DeveloperAccess.UnlockForTests();
                Assert.IsTrue(InputReader.LevelEditorSessionUnlocked);
                Assert.IsTrue(InputReader.LevelEditorShortcutAllowed(false, InputReader.LevelEditorSessionUnlocked));

                Assert.IsTrue(DeveloperConsole.ExecuteCommand("clear").clear);
                Assert.IsFalse(DeveloperConsole.ExecuteCommand("editor please").clear);
            }
            finally { DeveloperAccess.LockForTests(); }
        }
    }
}
