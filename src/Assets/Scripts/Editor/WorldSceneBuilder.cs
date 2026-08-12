using TianZhang.Bootstrap;
using TianZhang.Features.WorldMap;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    public static class WorldSceneBuilder
    {
        [MenuItem("天章/场景/重建主世界")]
        public static void Build()
        {
            GameObject root = SceneBuildSupport.BeginScene("WorldRoot", new Color(0.04f, 0.08f, 0.1f));
            WorldSceneInstaller installer = root.AddComponent<WorldSceneInstaller>();
            WorldMapController controller = root.AddComponent<WorldMapController>();
            WorldMapView view = root.AddComponent<WorldMapView>();
            Canvas canvas = SceneBuildSupport.CreateCanvas();
            GameObject panel = SceneBuildSupport.CreatePanel("WorldMapPanel", canvas.transform, new Vector2(0.05f, 0.08f), new Vector2(0.42f, 0.92f));
            SceneBuildSupport.AddVerticalLayout(panel);
            SceneBuildSupport.CreateText("WorldTitle", panel.transform, "主世界", 32);
            string[] names = { "江左天域", "关陇玄域", "陇西雷域", "中州天域" };
            var buttons = new Button[names.Length];
            var labels = new Text[names.Length];
            for (int i = 0; i < names.Length; i++)
                buttons[i] = SceneBuildSupport.CreateButton("WorldNodeButton_" + i, panel.transform, names[i], out labels[i]);
            Text selected = SceneBuildSupport.CreateText("SelectedWorldNodeText", panel.transform, string.Empty, 22);
            Text description = SceneBuildSupport.CreateText("SelectedWorldNodeDescription", panel.transform, string.Empty, 17);
            Button enter = SceneBuildSupport.CreateButton("EnterLocationButton", panel.transform, "进入地点", out _);
            SceneBuildSupport.SetObjects(view, "nodeButtons", buttons);
            SceneBuildSupport.SetObjects(view, "nodeButtonLabels", labels);
            SceneBuildSupport.SetObject(view, "selectedNodeText", selected);
            SceneBuildSupport.SetObject(view, "selectedNodeDescription", description);
            SceneBuildSupport.SetObject(view, "enterLocationButton", enter);
            SceneBuildSupport.SetObject(installer, "languageTable", SceneBuildSupport.RequireAsset<TextAsset>("Assets/DataConfig/Language.csv"));
            SceneBuildSupport.SetObject(installer, "controller", controller);
            SceneBuildSupport.SetObject(installer, "view", view);
            SceneBuildSupport.Save(SceneBuildSupport.WorldScenePath);
        }
    }
}
