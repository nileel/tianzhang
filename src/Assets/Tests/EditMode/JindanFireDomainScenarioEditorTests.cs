using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Features.Adventure;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TianZhang.Tests.EditMode
{
    public sealed class JindanFireDomainScenarioEditorTests
    {
        [Test]
        public void E01_BuildTwiceReopenAndResolveSerializedConfigurationAndReferences()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var build = EditorBuildSettings.scenes.Select(s => s.path + ":" + s.enabled).ToArray();
            try
            {
                JindanFireDomainScenarioSceneBuilder.Build();
                JindanFireDomainScenarioSceneBuilder.Build();
                var scene = EditorSceneManager.OpenScene(JindanFireDomainScenarioSceneBuilder.ScenePath);
                var root = scene.GetRootGameObjects().Single(r => r.name == "JindanFireDomainScenarioRoot");
                var controller = root.GetComponentsInChildren<JindanFireDomainScenarioController>(true).Single();
                Assert.AreEqual(JsonUtility.ToJson(new FireDomainConfig()), JsonUtility.ToJson(controller.Configuration));
                Assert.AreEqual(19, controller.CellMarkerCount); Assert.AreEqual(3, controller.UnitMarkerCount);
                var serialized = new SerializedObject(controller);
                foreach (string name in new[] { "cellMarkers", "cellLabels", "unitMarkers", "unitLabels" })
                {
                    var p = serialized.FindProperty(name);
                    for (int i = 0; i < p.arraySize; i++)
                        Assert.IsNotNull(p.GetArrayElementAtIndex(i).objectReferenceValue, name + i);
                }
                foreach (string name in new[] { "domainCenter", "centerLabel", "stateText", "previewText" })
                    Assert.IsNotNull(serialized.FindProperty(name).objectReferenceValue, name);
                Assert.AreEqual(19, root.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Cell_")));
                Assert.AreEqual(1, root.GetComponentsInChildren<Camera>(true).Length);
                Assert.IsFalse(root.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c != null &&
                    (c.GetType().Name == "GameBootstrap" || c.GetType().Name == "AdventureSceneInstaller"
                    || c is JindanFireFerryLineScenarioController)));
                var paths = new[]
                {
                    "Assets/Scripts/Modules/Features/Adventure/JindanFireDomainScenarioState.cs",
                    "Assets/Scripts/Modules/Features/Adventure/JindanFireDomainScenarioController.cs",
                    "Assets/Scripts/Editor/JindanFireDomainScenarioSceneBuilder.cs",
                    "Assets/Tests/EditMode/JindanFireDomainScenarioEditorTests.cs",
                    "Assets/Tests/PlayMode/JindanFireDomainScenarioPlayModeTests.cs",
                    JindanFireDomainScenarioSceneBuilder.ScenePath
                };
                foreach (var path in paths)
                {
                    Assert.IsTrue(File.Exists(path + ".meta"));
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    Assert.IsNotEmpty(guid); Assert.AreEqual(path, AssetDatabase.GUIDToAssetPath(guid));
                }
                Assert.AreEqual(6, paths.Select(path => AssetDatabase.AssetPathToGUID(path)).Distinct().Count());
                CollectionAssert.AreEqual(build, EditorBuildSettings.scenes.Select(s => s.path + ":" + s.enabled));
                Assert.AreEqual(4, EditorBuildSettings.scenes.Count(s => s.enabled));
                Assert.IsFalse(EditorBuildSettings.scenes.Any(s => s.path == JindanFireDomainScenarioSceneBuilder.ScenePath));
                TestContext.WriteLine("E01 persisted defaults, 19 cells, 3 units, references, 6 GUIDs and 4 formal scenes: PASS");
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        [Test]
        public void E02_PreviewIsPureDeterministicAndSnapshotsAreDetached()
        {
            var state = new JindanFireDomainScenarioState(new FireDomainConfig());
            string before = JsonUtility.ToJson(state.Capture());
            Assert.AreEqual(0, state.Capture().propagation);
            var a = state.Preview("manifest"); var b = state.Preview("manifest");
            CollectionAssert.AreEqual(a.Nodes, b.Nodes); Assert.AreEqual(6, a.Spirit);
            Assert.AreEqual(before, JsonUtility.ToJson(state.Capture()));
            var snapshot = state.Capture(); snapshot.spirit = 0; snapshot.cells[0].fuel = 0;
            Assert.AreEqual(before, JsonUtility.ToJson(state.Capture()));
            Assert.IsTrue(state.Commit(a));
            Assert.AreEqual(8, state.Capture().propagation);
            string beforeOpen = JsonUtility.ToJson(state.Capture());
            var open = state.Preview("open"); Assert.AreEqual(7, open.Spirit); Assert.AreEqual(3, open.Tail);
            state.Preview("open"); Assert.AreEqual(beforeOpen, JsonUtility.ToJson(state.Capture()));
            Assert.IsTrue(state.Commit(open)); Assert.AreEqual(47, state.Capture().spirit);
        }
    }
}
