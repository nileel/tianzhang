using System;
using TianZhang.Features.Adventure;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    /// <summary>Persists only the isolated fixture; never edits formal scene/build settings.</summary>
    public static class JindanFireDomainScenarioSceneBuilder
    {
        public const string ScenePath = "Assets/Tests/Scenes/JindanFireDomainScenario.unity";
        [MenuItem("天章/测试/重建赤野燎原单项场景")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("JindanFireDomainScenarioRoot");
            var controller = root.AddComponent<JindanFireDomainScenarioController>();
            var camera = new GameObject("FixtureCamera", typeof(Camera));
            camera.transform.SetParent(root.transform, false);
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(.025f, .035f, .05f);
            var canvas = SceneBuildSupport.CreateCanvas("FireDomainFixtureCanvas");
            canvas.transform.SetParent(root.transform, false);
            var eventSystem = new GameObject("FixtureEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(root.transform, false);
            var cfg = controller.Configuration;
            var grid = new GameObject("FixtureGrid", typeof(RectTransform)).GetComponent<RectTransform>();
            grid.SetParent(canvas.transform, false); grid.anchoredPosition = new Vector2(250, 20);
            var cells = new Image[cfg.cells.Length]; var labels = new Text[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                var p = cfg.cells[i].coordinate;
                var button = Button(grid, "Cell_" + p.x + "_" + p.y, "", controller, "cell:" + p.x + ":" + p.y,
                    JindanFireDomainScenarioController.BoardPosition(p), new Vector2(145, 124), out labels[i]);
                labels[i].fontSize = 19;
                cells[i] = button.GetComponent<Image>();
            }
            var unitMarkers = new RectTransform[3]; var unitLabels = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                var unit = cfg.units[i];
                var button = Button(grid, "Unit_" + unit.id, unit.id, controller, "unit:" + unit.id,
                    Vector2.zero, new Vector2(140, 28), out unitLabels[i]);
                unitLabels[i].fontSize = 17;
                button.GetComponent<Image>().color = i == 0 ? new Color(.08f, .38f, .65f)
                    : i == 1 ? new Color(.7f, .13f, .13f) : new Color(.38f, .36f, .4f);
                unitMarkers[i] = button.GetComponent<RectTransform>();
            }
            var centerButton = Button(grid, "DomainCenter", "CENTER 10", controller, "attack", Vector2.zero,
                new Vector2(140, 25), out var centerLabel);
            centerLabel.fontSize = 16; centerButton.GetComponent<Image>().color = new Color(.5f, .16f, .55f);
            var status = Text(canvas.transform, "FixtureStatus", new Vector2(-540, 190), new Vector2(650, 610), 23);
            var preview = Text(canvas.transform, "FixturePreview", new Vector2(300, -405), new Vector2(900, 150), 23);
            string[] commands = { "preview:manifest", "preview:open", "preview:cycle", "commit", "manifest", "open", "cycle",
                "extinguish", "water", "isolate", "fuel", "move", "attack", "close", "source_exit", "domain_exit", "end", "reset" };
            string[] captions = { "Preview source", "Preview domain", "Preview cycle", "Commit preview", "Manifest", "Open domain", "Next cycle",
                "Extinguish cell", "Water cell", "Isolate cell", "Remove fuel", "Move selected unit", "Attack center", "Close main",
                "Source exit", "Domain exit", "End battle", "TEST RESET" };
            for (int i = 0; i < commands.Length; i++)
                Button(canvas.transform, "Input_" + commands[i], captions[i], controller, commands[i],
                    new Vector2(-700 + (i % 3) * 205, -150 - (i / 3) * 53), new Vector2(197, 46), out _);
            controller.ConfigurePresentation(cells, labels, unitMarkers, unitLabels, centerButton.GetComponent<RectTransform>(), centerLabel, status, preview);
            controller.ResetFixture();
            EditorUtility.SetDirty(controller);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("fire_domain_scene_save_failed");
            AssetDatabase.SaveAssets();
        }

        private static Text Text(Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
        {
            var text = SceneBuildSupport.CreateText(name, parent, "", fontSize);
            text.rectTransform.anchoredPosition = position; text.rectTransform.sizeDelta = size;
            text.alignment = TextAnchor.UpperLeft; text.raycastTarget = false;
            return text;
        }
        private static Button Button(Transform parent, string name, string caption, JindanFireDomainScenarioController target,
            string command, Vector2 position, Vector2 size, out Text label)
        {
            var button = SceneBuildSupport.CreateButton(name, parent, caption, out label);
            var rect = button.GetComponent<RectTransform>(); rect.anchoredPosition = position; rect.sizeDelta = size;
            label.raycastTarget = false;
            UnityEventTools.AddStringPersistentListener(button.onClick, target.Command, command);
            return button;
        }
    }
}
