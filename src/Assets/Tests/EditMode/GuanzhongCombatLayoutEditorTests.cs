using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Tests.EditMode
{
    public sealed class GuanzhongCombatLayoutEditorTests
    {
        [Test] public void IncrementalCreatorsRetainOneFinalLayout()
        {
            AdventureSceneBuilder.RebuildCombatLayout();
            AdventureSceneBuilder.RebuildCombatLayout();
            AdventureSceneBuilder.RebuildCombatMovement();
            AdventureSceneBuilder.RebuildCombatTargetSelection();
            AdventureSceneBuilder.RebuildCombatActionPreview();
            AdventureSceneBuilder.RebuildCombatTimeline();
            SavedFormalLayoutKeepsReferencesAndSingleWriters();
        }

        [Test] public void SavedFormalLayoutKeepsReferencesAndSingleWriters()
        {
            var scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            var rects = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToArray();
            RectTransform Rect(string name) => rects.Single(r => r.name == name);
            Assert.AreEqual(1, scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Count());
            var hud = Rect("CombatHudRoot");
            Assert.IsNull(hud.GetComponent<LayoutGroup>());
            Assert.IsFalse(hud.GetComponent<Image>().raycastTarget);
            Assert.AreEqual(0, hud.GetComponent<Image>().color.a);
            Assert.IsNull(Rect("CombatActionBar").GetComponent<LayoutGroup>());
            foreach (string name in new[] { "TargetBody", "PreviewBody", "BountyBody", "PlayerSummaryBody", "CombatLogViewport" })
            {
                var scroll = Rect(name).GetComponent<ScrollRect>();
                Assert.NotNull(scroll.content); Assert.NotNull(scroll.viewport);
                Assert.NotNull(scroll.GetComponent<RectMask2D>());
                Assert.IsFalse(scroll.horizontal);
            }
            Assert.AreEqual("UICanvas", Rect("CombatTargetMarker").parent.name);
            Assert.AreEqual("UICanvas", Rect("CombatUnitHealthBars").parent.name);
            Assert.IsFalse(Rect("CombatLogPanel").gameObject.activeSelf);
            foreach (var component in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)))
            {
                if (component == null || component.GetType().Namespace != "TianZhang.Features.CombatPresentation") continue;
                var iterator = new SerializedObject(component).GetIterator();
                while (iterator.NextVisible(true))
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceInstanceIDValue != 0)
                        Assert.NotNull(iterator.objectReferenceValue, component.name + ":" + iterator.propertyPath);
            }
            var bounty = scene.GetRootGameObjects().SelectMany(r => r.GetComponents<CombatBountyTrackerView>()).Single();
            var data = new SerializedObject(bounty);
            Assert.AreSame(Rect("CombatBountyTrackerRoot").gameObject, data.FindProperty("root").objectReferenceValue);
            Assert.NotNull(data.FindProperty("summary").objectReferenceValue);
            Assert.AreEqual(1, rects.Count(r => r.name == "BountySummary"));
            Assert.AreEqual(1, rects.Count(r => r.name == "CombatActionBar"));
            var timeline = scene.GetRootGameObjects().SelectMany(r => r.GetComponents<CombatTimelineView>()).Single();
            Assert.NotNull(new SerializedObject(timeline).FindProperty("predictionRoot").objectReferenceValue);
        }
    }
}
