using NUnit.Framework;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using VibeGame1.EditorTools;

namespace VibeGame1.Tests
{
    /// <summary>
    /// Guards the physical, local-only records board requested for the Level 1 spawn.  These are data and
    /// presentation contracts: the board must remain regenerable, non-gameplay geometry, and distinct from
    /// the intentionally hidden screen-space GhostHud table.
    /// </summary>
    public class WorldLeaderboardTests
    {
        LevelDefinition definition;

        [SetUp]
        public void LoadDefinition()
        {
            var asset = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionAuthoring.Level01);
            Assert.IsNotNull(asset, "Level_01 asset missing");
            definition = Object.Instantiate(asset);
        }

        [TearDown]
        public void DisposeDefinition()
        {
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void ApplyingLevelOneTwiceKeepsOneEnabledBoardBehindTheSpawn()
        {
            LevelDefinitionAuthoring.Apply(definition);
            string once = JsonUtility.ToJson(definition.worldLeaderboard);
            LevelDefinitionAuthoring.Apply(definition);

            var board = definition.worldLeaderboard;
            Assert.IsNotNull(board, "Level 1 must author a physical local-records board");
            Assert.IsTrue(board.enabled);
            Assert.AreEqual(once, JsonUtility.ToJson(board),
                "reapplying Level 1 authoring must not drift or duplicate the board configuration");
            Assert.That(board.rowCount, Is.InRange(1, Leaderboard.DisplayCount));
            Assert.IsFalse(string.IsNullOrWhiteSpace(board.name));

            Vector3 runForward = Quaternion.Euler(0f, definition.playerStartYaw, 0f) * Vector3.forward;
            Assert.Less(Vector3.Dot(board.position - definition.playerStart, runForward), 0f,
                "the board belongs behind the runner at spawn, not in the opening route");
        }

        [Test]
        public void WorldBoardFormatterUsesTruthfulEmptyStateAndCapsRows()
        {
            string empty = WorldLeaderboardView.FormatRows(null, Leaderboard.DisplayCount);
            StringAssert.Contains("NO RUNS YET", empty);
            StringAssert.Contains("FINISH THE LEVEL", empty);

            var entries = new[]
            {
                new RunEntry { timeMs = 61234, deaths = 0 },
                null,
                new RunEntry { timeMs = 72345, deaths = 1 },
                new RunEntry { timeMs = 83456, deaths = 3, verified = true }
            };
            string rows = WorldLeaderboardView.FormatRows(entries, 2);
            StringAssert.Contains("01", rows);
            StringAssert.Contains("02", rows);
            StringAssert.DoesNotContain("03", rows);
            StringAssert.Contains(SpeedrunTimer.Format(61.234f), rows);
            StringAssert.Contains("1 DEATH", rows);
            StringAssert.DoesNotContain("VERIFIED", rows,
                "a capped row must not leak later leaderboard metadata into the display");
        }

        [Test]
        public void WorldBoardViewUsesLocalWordingAndTheAuthoredPresentationValues()
        {
            var root = new GameObject("WorldLeaderboardViewTest");
            var heading = root.AddComponent<TextMeshPro>();
            var rows = new GameObject("Rows").AddComponent<TextMeshPro>();
            var footer = new GameObject("Footer").AddComponent<TextMeshPro>();
            rows.transform.SetParent(root.transform, false);
            footer.transform.SetParent(root.transform, false);
            try
            {
                var board = new WorldLeaderboardDef
                {
                    rowCount = 3,
                    size = new Vector2(11f, 6f),
                    backingMaterialKey = "Stone",
                    glowMaterialKey = "NeonCyan"
                };
                var view = root.AddComponent<WorldLeaderboardView>();
                view.Configure("The Hollow Ascent", board, heading, rows, footer);

                Assert.AreEqual(3, view.MaxRows);
                Assert.AreEqual(board.size, view.BoardSize);
                Assert.AreEqual(board.backingMaterialKey, view.BackingMaterialKey);
                Assert.AreEqual(board.glowMaterialKey, view.GlowMaterialKey);
                StringAssert.Contains("LOCAL BEST RUNS", heading.text);
                StringAssert.Contains("THE HOLLOW ASCENT", heading.text);
                StringAssert.Contains("SAVED ON THIS DEVICE", footer.text);
                StringAssert.Contains("NO RUNS YET", rows.text);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void GeneratedWorldBoardIsSkyOnlyAndHasNoColliders()
        {
            LevelDefinitionAuthoring.Apply(definition);
            var parent = new GameObject("WorldLeaderboardHierarchyTest");
            try
            {
                var build = typeof(LevelDefinitionBuilder).GetMethod("BuildWorldLeaderboard",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(build, "the regenerable level builder must own the world-board geometry");
                build.Invoke(null, new object[] { definition, parent.transform, new LevelPieceContext() });

                var board = parent.transform.Find(definition.worldLeaderboard.name);
                Assert.IsNotNull(board, "enabled authored board was not generated beneath Level");
                foreach (var transform in board.GetComponentsInChildren<Transform>(true))
                {
                    Assert.AreEqual(Starfield.SkyLayer, transform.gameObject.layer,
                        transform.name + " must stay outside the Default-only NavMesh bake");
                    Assert.IsEmpty(transform.GetComponents<Collider>(),
                        transform.name + " must be presentation-only and never block a route");
                }

                var view = board.GetComponent<WorldLeaderboardView>();
                Assert.IsNotNull(view);
                var fullBoard = new RunEntry[Leaderboard.DisplayCount];
                for (int i = 0; i < fullBoard.Length; i++)
                    fullBoard[i] = new RunEntry { timeMs = 3599999 - i, deaths = 999, verified = true };

                view.RowsText.text = WorldLeaderboardView.FormatRows(fullBoard, Leaderboard.DisplayCount);
                view.RowsText.ForceMeshUpdate();
                float preferredHeight = view.RowsText.GetPreferredValues(
                    view.RowsText.text, view.RowsText.rectTransform.rect.width, 10000f).y;
                Assert.LessOrEqual(preferredHeight, view.RowsText.rectTransform.rect.height,
                    "all eight longest-form rows must fit above the footer without overflow");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void LegacyScreenSpaceGhostBoardRemainsHidden()
        {
            var root = new GameObject("GhostHudDefaults");
            try
            {
                var ghostHud = root.AddComponent<GhostHud>();
                Assert.IsFalse(ghostHud.BoardVisible,
                    "the physical local board must not restore the removed screen-space BEST RUNS table");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
