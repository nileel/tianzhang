using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Tests.EditMode
{
    public sealed class GuanzhongCombatLayoutEditorTests
    {
        [Test] public void TimelineRebuildAndReopenKeepSingleSequence()
        {
            AdventureSceneBuilder.RebuildCombatTimeline();
            AdventureSceneBuilder.RebuildCombatTimeline();
            SavedFormalLayoutKeepsReferencesAndSingleWriters();
        }

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

        [Test] public void WithdrawalRebuildAndReopenKeepOneBoundEntry()
        {
            AdventureSceneBuilder.RebuildWithdrawalEntry();
            AdventureSceneBuilder.RebuildWithdrawalEntry();
            var scene = EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            var rects = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToArray();
            var entry = rects.Single(r => r.name == "AdventureReturnEntry");
            var panel = rects.Single(r => r.name == "AdventurePanel");
            var hud = scene.GetRootGameObjects().SelectMany(r => r.GetComponents<AdventureHudPresenter>()).Single();
            var data = new SerializedObject(hud);
            Assert.AreSame(entry.gameObject, data.FindProperty("returnEntry").objectReferenceValue);
            Assert.AreSame(panel, data.FindProperty("explorationPanel").objectReferenceValue);
            Assert.AreSame(entry.Find("ReturnButton").GetComponent<Button>(), data.FindProperty("returnButton").objectReferenceValue);
            Assert.AreSame(entry.Find("ReturnReason").GetComponent<Text>(), data.FindProperty("returnReason").objectReferenceValue);
            Assert.AreEqual("UICanvas", entry.parent.name);
            Assert.AreSame(panel.parent, entry.parent);
            Assert.IsNull(entry.GetComponent<LayoutGroup>());
            Assert.AreEqual(panel.anchorMax, entry.anchorMin);
            Assert.AreEqual(panel.anchorMax, entry.anchorMax);
            Assert.AreEqual(Vector2.up, entry.pivot);
            Assert.AreEqual(new Vector2(24, 0), entry.anchoredPosition);
            Assert.AreEqual(new Vector2(panel.rect.width, 144), entry.sizeDelta);
            Assert.IsFalse(entry.gameObject.activeSelf);
            Assert.IsFalse(rects.Single(r => r.name == "AdventureWithdrawalConfirmation").gameObject.activeSelf);
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
            var healthBars = Rect("CombatUnitHealthBars");
            Assert.AreEqual("UICanvas", healthBars.parent.name);
            Assert.IsEmpty(healthBars.GetComponentsInChildren<Text>(true), "Head health bars must stay text-free.");
            Assert.IsEmpty(healthBars.GetComponentsInChildren<LayoutGroup>(true),
                "Head health bars must not keep the text-driven layout group.");
            Assert.IsEmpty(healthBars.GetComponentsInChildren<ContentSizeFitter>(true),
                "Head health bars must not stretch with their content.");
            var healthView = scene.GetRootGameObjects().SelectMany(r => r.GetComponents<CombatUnitHealthBarView>()).Single();
            SerializedProperty healthSlots = new SerializedObject(healthView).FindProperty("bars");
            Assert.AreEqual(3, healthSlots.arraySize);
            foreach (string slot in new[] { "UnitHealthBar_0", "UnitHealthBar_1", "UnitHealthBar_2" })
            {
                var bar = (RectTransform)healthBars.Find(slot);
                Assert.NotNull(bar, slot + " must exist under the health bar container.");
                Assert.IsNull(bar.GetComponent<Image>(), slot + " must not keep the old text panel background.");
                Assert.AreEqual(new Vector2(CombatUnitHealthBarView.BarWidth, CombatUnitHealthBarView.BarHeight), bar.sizeDelta);
                Assert.AreEqual(Vector2.one * 0.5f, bar.anchorMin);
                Assert.AreEqual(Vector2.one * 0.5f, bar.anchorMax);
                Assert.AreEqual(new Vector2(0.5f, 0f), bar.pivot);
                Transform track = bar.Find("HealthTrack");
                Assert.NotNull(track, slot + " must keep its health track.");
                Assert.NotNull(track.GetComponent<Image>(), slot + " track must stay a graphic.");
                Transform fill = track.Find("HealthFill");
                Assert.NotNull(fill, slot + " must keep its health fill under the track.");
                Assert.NotNull(fill.GetComponent<Image>(), slot + " fill must stay a graphic.");
                Assert.AreEqual(2, bar.GetComponentsInChildren<Image>(true).Length,
                    slot + " must keep only its track and fill graphics.");
            }
            for (int index = 0; index < healthSlots.arraySize; index++)
            {
                SerializedProperty slot = healthSlots.GetArrayElementAtIndex(index);
                Assert.NotNull(slot.FindPropertyRelative("root").objectReferenceValue, "slot " + index + " root");
                Assert.NotNull(slot.FindPropertyRelative("healthFill").objectReferenceValue, "slot " + index + " healthFill");
                Assert.IsNull(slot.FindPropertyRelative("identityText"), "Removed head text must not stay serialized.");
                Assert.IsNull(slot.FindPropertyRelative("healthText"), "Removed head text must not stay serialized.");
                Assert.IsNull(slot.FindPropertyRelative("spiritText"), "Removed head text must not stay serialized.");
            }
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
            var actionBar = scene.GetRootGameObjects().SelectMany(r => r.GetComponents<CombatActionBarView>()).Single();
            Assert.IsFalse(rects.Any(r => r.name.StartsWith("AttackDirection_")));
            Assert.IsNull(new SerializedObject(actionBar).FindProperty("attackDirectionButtons"));
            var input = scene.GetRootGameObjects().SelectMany(r => r.GetComponents<CombatCommandInput>()).Single();
            Assert.AreSame(actionBar, new SerializedObject(input).FindProperty("actionBar").objectReferenceValue);
            var targetInput = scene.GetRootGameObjects().SelectMany(r => r.GetComponents<CombatTargetInput>()).Single();
            Assert.AreSame(targetInput, new SerializedObject(input).FindProperty("targetInput").objectReferenceValue);
            var timeline = scene.GetRootGameObjects().SelectMany(r => r.GetComponents<CombatTimelineView>()).Single();
            Assert.NotNull(new SerializedObject(timeline).FindProperty("predictionRoot").objectReferenceValue);
            var timelineData = new SerializedObject(timeline);
            Assert.AreSame(Rect("TimelineTrack"), timelineData.FindProperty("chargeTrack").objectReferenceValue);
            Assert.AreEqual(new Vector2(52f / 1920, 196f / 1080), Rect("CombatTimelineRoot").anchorMin);
            Assert.AreEqual(new Vector2(300f / 1920, 801f / 1080), Rect("CombatTimelineRoot").anchorMax);
            Assert.AreEqual(Vector2.zero, Rect("CombatTimelineRoot").offsetMin);
            Assert.AreEqual(Vector2.zero, Rect("CombatTimelineRoot").offsetMax);
            Assert.IsFalse(rects.Any(rect => rect.name == "TimelineThreshold" || rect.name == "TimelineZero"));
            Assert.AreEqual(3, timelineData.FindProperty("entryCtLabels").arraySize);
            for (int index = 0; index < 3; index++)
            {
                Assert.AreSame(Rect("TimelineTrack"), Rect("TimelineEntry" + index).parent);
                Assert.AreEqual(new Vector2(0.5f, 1), Rect("TimelineEntry" + index).pivot);
                Assert.AreEqual(new Vector2(0.5f, 1), Rect("TimelineEntry" + index).anchorMin);
                Assert.AreEqual(0, Rect("TimelineEntry" + index).anchoredPosition.x);
                Assert.NotNull(timelineData.FindProperty("entryCtLabels").GetArrayElementAtIndex(index).objectReferenceValue);
            }
        }
    }
}
