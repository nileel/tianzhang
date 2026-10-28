using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Bootstrap;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    /// <summary>Persists only the Jindan rehearsal entry and presentation in the existing Adventure scene.</summary>
    public static class JindanDemoSceneBuilder
    {
        [MenuItem("天章/场景/D5 导入并接入金丹演武")]
        public static void ImportAndRebuild()
        {
            WorldContentImporter.ImportEnvironmentProfiles();
            JindanDemoContentImporter.Import();
            Rebuild();
        }

        [MenuItem("天章/场景/接入金丹演武")]
        public static void Rebuild()
        {
            EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Install();
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
            EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
        }

        public static void Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path) && scene.path != SceneBuildSupport.AdventureScenePath)
                throw new InvalidOperationException("Jindan presentation requires the Adventure scene.");
            GameObject owner = Array.Find(scene.GetRootGameObjects(), value => value.name == "AdventureRoot");
            GameObject canvasObject = Array.Find(scene.GetRootGameObjects(), value => value.name == "UICanvas");
            if (owner == null || canvasObject == null)
                throw new InvalidOperationException("Jindan presentation owners are missing.");
            var installer = owner.GetComponent<AdventureSceneInstaller>();
            SceneBuildSupport.SetObject(owner.GetComponent<Static3DCombatUnitPresentationProvider>(), "flightLineMaterial",
                SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.SelectedMaterialPath));
            Transform canvas = canvasObject.transform;
            Transform exploration = canvas.Find("AdventurePanel");
            if (exploration == null) throw new InvalidOperationException("AdventurePanel is missing.");
            Remove(exploration, "JindanRehearsalButton");
            Button entry = SceneBuildSupport.CreateButton("JindanRehearsalButton", exploration, "金丹演武", out _);
            SettlementSceneBuilder.ApplyButtonSkin(entry);
            SceneBuildSupport.SetObject(installer, "jindanRehearsalButton", entry);

            Remove(canvas, "JindanDemoPreparationRoot");
            Remove(canvas, "JindanDemoCombatRoot");
            Remove(canvas, "JindanConflictRoot");
            Remove(owner.transform, "JindanScaleRoot");
            Remove(owner.transform, "JindanSealRoot");
            Remove(owner.transform, "JindanFireDomainRoot");
            Remove(owner.transform, "JindanEarthRoot");
            Remove(owner.transform, "JindanSurfaceRoot");
            var view = owner.GetComponent<JindanDemoView>() ?? owner.AddComponent<JindanDemoView>();
            var bindings = new List<JindanDemoView.ActionButton>();
            BuildPreparation(canvas, view, bindings);
            BuildCombat(canvas, view, bindings);
            BuildVisibility(scene, owner, canvas, installer);
            var serialized = new SerializedObject(view);
            SerializedProperty buttons = serialized.FindProperty("buttons");
            buttons.arraySize = bindings.Count;
            for (int index = 0; index < bindings.Count; index++)
            {
                SerializedProperty binding = buttons.GetArrayElementAtIndex(index);
                binding.FindPropertyRelative("key").stringValue = bindings[index].key;
                binding.FindPropertyRelative("button").objectReferenceValue = bindings[index].button;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var scaleRoot = new GameObject("JindanScaleRoot");
            scaleRoot.transform.SetParent(owner.transform, false);
            var scale = scaleRoot.AddComponent<JindanScaleView>();
            SceneBuildSupport.SetObject(scale, "supportPrefab", SceneBuildSupport.RequireAsset<GameObject>(
                GuanzhongPropSampleBuilder.AssetRoot + "/SupportStele.prefab"));
            SceneBuildSupport.SetObject(scale, "edgeMaterial", SceneBuildSupport.RequireAsset<Material>(
                VisualBaselineBuilder.SelectedMaterialPath));
            UnityEngine.Object catalog = AssetDatabase.LoadMainAssetAtPath("Assets/Data/JindanDemo/JindanDemoCatalog.asset");
            if (catalog == null) throw new InvalidOperationException("Jindan demo catalog is missing.");
            SceneBuildSupport.SetObject(installer, "jindanDemoView", view);
            SceneBuildSupport.SetObject(installer, "jindanScaleView", scale);
            var sealRoot = new GameObject("JindanSealRoot");
            sealRoot.transform.SetParent(owner.transform, false);
            var seal = sealRoot.AddComponent<JindanSealView>();
            var marker = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Single(value => value.name == "space_lock_support");
            SceneBuildSupport.SetObject(seal, "supportMarker", marker.gameObject);
            SceneBuildSupport.SetObject(seal, "edgeMaterial", SceneBuildSupport.RequireAsset<Material>(VisualBaselineBuilder.SelectedMaterialPath));
            SceneBuildSupport.SetObject(installer, "jindanSealView", seal);
            var fireRoot = new GameObject("JindanFireDomainRoot");
            fireRoot.transform.SetParent(owner.transform, false);
            var fire = fireRoot.AddComponent<JindanFireDomainView>();
            var fireMarker = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Single(value => value.name == "fire_domain_support");
            SceneBuildSupport.SetObject(fire, "supportMarker", fireMarker.gameObject);
            SceneBuildSupport.SetObject(installer, "jindanFireDomainView", fire);
            BuildEarthRamp();
            var earthRoot = new GameObject("JindanEarthRoot");
            earthRoot.transform.SetParent(owner.transform, false);
            var earth = earthRoot.AddComponent<CombatEarthView>();
            SceneBuildSupport.SetObject(earth, "wallPrefab", SceneBuildSupport.RequireAsset<GameObject>(
                GuanzhongPropSampleBuilder.AssetRoot + "/HighWall.prefab"));
            SceneBuildSupport.SetObject(earth, "rampPrefab", SceneBuildSupport.RequireAsset<GameObject>(
                "Assets/Art/Environments/Guanzhong/EarthManifest/EarthManifestRamp.prefab"));
            SceneBuildSupport.SetObject(installer, "earthView", earth);
            var surfaceRoot = new GameObject("JindanSurfaceRoot");
            surfaceRoot.transform.SetParent(owner.transform, false);
            var surfaceView = surfaceRoot.AddComponent<CombatSurfaceView>();
            SceneBuildSupport.SetObject(surfaceView, "surfacePrefab", SceneBuildSupport.RequireAsset<GameObject>(
                GuanzhongWaterbankSampleBuilder.AssetRoot + "/WaterHex.prefab"));
            SceneBuildSupport.SetObject(surfaceView, "groundMaterial", SceneBuildSupport.RequireAsset<Material>(
                GuanzhongWaterbankSampleBuilder.AssetRoot + "/MudBank.mat"));
            SceneBuildSupport.SetObject(surfaceView, "lineMaterial", SceneBuildSupport.RequireAsset<Material>(
                VisualBaselineBuilder.SelectedMaterialPath));
            var terrain = owner.transform.Find(GuanzhongDemoBattlefieldBuilder.RootName);
            if (terrain == null) terrain = scene.GetRootGameObjects().SelectMany(value => value.GetComponentsInChildren<Transform>(true))
                .Single(value => value.name == GuanzhongDemoBattlefieldBuilder.RootName);
            var overlays = terrain.GetComponentsInChildren<Transform>(true)
                .Where(value => value.name == "WaterSurface" || value.name.StartsWith("Shore_", StringComparison.Ordinal)).ToArray();
            var surfaceData = new SerializedObject(surfaceView);
            var originals = surfaceData.FindProperty("staticWaterOverlays"); originals.arraySize = overlays.Length;
            for (int i = 0; i < overlays.Length; i++) originals.GetArrayElementAtIndex(i).objectReferenceValue = overlays[i].gameObject;
            surfaceData.ApplyModifiedPropertiesWithoutUndo();
            SceneBuildSupport.SetObject(installer, "surfaceView", surfaceView);
            SceneBuildSupport.SetObject(installer, "jindanDemoCatalog", catalog);
            // The existing withdrawal dialog remains above every combat panel and receives its clicks.
            canvas.Find("AdventureWithdrawalConfirmation")?.SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void BuildEarthRamp()
        {
            const string root = "Assets/Art/Environments/Guanzhong/EarthManifest/EarthManifestRamp";
            var importer = (ModelImporter)AssetImporter.GetAtPath(root + ".fbx");
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
            var ramp = (GameObject)PrefabUtility.InstantiatePrefab(SceneBuildSupport.RequireAsset<GameObject>(root + ".fbx"));
            ramp.name = "EarthManifestRamp";
            foreach (MeshRenderer renderer in ramp.GetComponentsInChildren<MeshRenderer>())
                renderer.sharedMaterial = SceneBuildSupport.RequireAsset<Material>(GuanzhongRampSampleBuilder.AssetRoot + "/Earth.mat");
            PrefabUtility.SaveAsPrefabAsset(ramp, root + ".prefab");
            UnityEngine.Object.DestroyImmediate(ramp);
        }

        private static void BuildPreparation(Transform canvas, JindanDemoView view,
            List<JindanDemoView.ActionButton> bindings)
        {
            GameObject root = Panel(canvas, "JindanDemoPreparationRoot", false);
            Stretch(root.GetComponent<RectTransform>());
            root.GetComponent<Image>().color = new Color(0, 0, 0, .65f);
            GameObject dialog = Panel(root.transform, "JindanPreparationDialog");
            var rect = dialog.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(1280, 880);
            Label(dialog.transform, "Heading", "金丹演武 · 战前配置", 30, 32, 814, 1216, 44);
            AddButton(dialog.transform, bindings, "stage1", "第一段 · 御风飞行", 32, 742, 392, 60);
            AddButton(dialog.transform, bindings, "stage2", "第二段 · 水火与土石", 444, 742, 392, 60);
            AddButton(dialog.transform, bindings, "stage3", "第三段 · 方位定尺", 856, 742, 392, 60);
            Text details = ScrollText(dialog.transform, "PreparationDetails", 32, 166, 1216, 560, 24);
            AddButton(dialog.transform, bindings, "preset_next", "切换预设", 32, 90, 392, 60);
            AddButton(dialog.transform, bindings, "earth_toggle", "切换土支撑装配", 444, 90, 392, 60);
            AddButton(dialog.transform, bindings, "fire_side", "第二段 · 切换攻守", 856, 90, 392, 60);
            AddButton(dialog.transform, bindings, "cancel_preparation", "返回冒险", 32, 20, 594, 60);
            AddButton(dialog.transform, bindings, "enter", "进入演武", 654, 20, 594, 60);
            SceneBuildSupport.SetObject(view, "preparationRoot", root);
            SceneBuildSupport.SetObject(view, "preparationDetails", details);
            root.SetActive(false);
        }

        private static void BuildCombat(Transform canvas, JindanDemoView view,
            List<JindanDemoView.ActionButton> bindings)
        {
            // V4 design space: left of target/preview panels, right of the rail, above the hotbar.
            // The root has no raycast image, so a collapsed body exposes the battlefield below it.
            var root = new GameObject("JindanDemoCombatRoot", typeof(RectTransform));
            root.transform.SetParent(canvas, false);
            Place(root.GetComponent<RectTransform>(), 160, 220, 620, 628);
            GameObject header = Panel(root.transform, "JindanCombatHeader");
            Place(header.GetComponent<RectTransform>(), 0, 580, 620, 48);
            Label(header.transform, "Heading", "金丹演武", 24, 16, 2, 132, 44);
            AddButton(header.transform, bindings, "flight_tab", "飞行", 150, 0, 60, 48);
            AddButton(header.transform, bindings, "scale_tab", "位格", 216, 0, 60, 48);
            AddButton(header.transform, bindings, "earth_tab", "土系", 282, 0, 60, 48);
            AddButton(header.transform, bindings, "tide_tab", "水火", 348, 0, 60, 48);
            AddButton(header.transform, bindings, "visibility_tab", "显隐", 414, 0, 60, 48);
            Button toggle = AddButton(header.transform, bindings, "toggle_panel", "收起", 480, 0, 140, 48);
            GameObject body = Panel(root.transform, "JindanCombatBody");
            Place(body.GetComponent<RectTransform>(), 0, 0, 620, 580);
            Text details = ScrollText(body.transform, "CombatDetails", 16, 330, 588, 238, 22);
            Text quote = ScrollText(body.transform, "CombatQuote", 16, 242, 588, 80, 22);
            var scale = new GameObject("ScaleActions", typeof(RectTransform)); scale.transform.SetParent(body.transform, false);
            Place(scale.GetComponent<RectTransform>(), 0, 0, 620, 240);
            AddButton(scale.transform, bindings, "compress", "压缩边（1）", 16, 184, 286, 50);
            AddButton(scale.transform, bindings, "expand", "拉长边（4）", 318, 184, 286, 50);
            AddButton(scale.transform, bindings, "pick_edges", "选定 2–3 个相连格", 16, 126, 286, 50);
            AddButton(scale.transform, bindings, "pick_origin", "选择遥施空格", 318, 126, 286, 50);
            AddButton(scale.transform, bindings, "preview", "预演", 16, 68, 184, 50);
            AddButton(scale.transform, bindings, "close_effect", "关闭当前位格效果", 16, 10, 286, 50);
            AddButton(scale.transform, bindings, "swap_effect", "战中换装（土候选）", 318, 10, 286, 50);
            var flight = new GameObject("FlightActions", typeof(RectTransform)); flight.transform.SetParent(body.transform, false);
            Place(flight.GetComponent<RectTransform>(), 0, 0, 620, 240);
            AddButton(flight.transform, bindings, "flight_takeoff", "起飞 · 预演", 16, 184, 286, 50);
            AddButton(flight.transform, bindings, "flight_ascend", "升至高空 · 预演", 318, 184, 286, 50);
            AddButton(flight.transform, bindings, "flight_descend", "降至低空 · 预演", 16, 126, 286, 50);
            AddButton(flight.transform, bindings, "flight_land", "原柱降落 · 预演", 318, 126, 286, 50);
            AddButton(flight.transform, bindings, "flight_recover", "失效后续御 · 预演", 16, 10, 588, 50);
            flight.SetActive(false);
            var earth = new GameObject("EarthActions", typeof(RectTransform)); earth.transform.SetParent(body.transform,false);
            Place(earth.GetComponent<RectTransform>(),0,0,620,240);
            AddButton(earth.transform,bindings,"earth_wall","岩墙 · 选空边",16,184,286,50);
            AddButton(earth.transform,bindings,"earth_ramp","窄坡 · 选高差1",318,184,286,50);
            AddButton(earth.transform,bindings,"earth_heavy","极重",16,126,136,50);
            AddButton(earth.transform,bindings,"earth_light","失重",166,126,136,50);
            AddButton(earth.transform,bindings,"earth_anchor","镇岳",318,126,136,50);
            AddButton(earth.transform,bindings,"earth_next_target","切换目标",468,126,136,50);
            AddButton(earth.transform,bindings,"earth_preview","预演",16,68,184,50);
            AddButton(earth.transform,bindings,"earth_close","解除实例",16,10,184,50);
            AddButton(earth.transform,bindings,"earth_swap","切换装配",218,10,184,50);
            AddButton(earth.transform,bindings,"break_object","破垒 · 选构件",420,10,184,50);
            earth.SetActive(false);
            SceneBuildSupport.SetObject(view,"earthActions",earth);
            var tide = new GameObject("TideActions", typeof(RectTransform)); tide.transform.SetParent(body.transform, false);
            Place(tide.GetComponent<RectTransform>(), 0, 0, 620, 240);
            AddButton(tide.transform, bindings, "tide_rise", "涨潮 · 选水源", 16, 184, 286, 50);
            AddButton(tide.transform, bindings, "tide_ebb", "退潮 · 选湿水格", 318, 184, 286, 50);
            AddButton(tide.transform, bindings, "tide_charge", "切换普通／蓄力", 16, 126, 286, 50);
            AddButton(tide.transform, bindings, "tide_eye", "无源潮眼 · 选干格", 318, 126, 286, 50);
            AddButton(tide.transform, bindings, "tide_preview", "预演", 16, 68, 184, 50);
            AddButton(tide.transform, bindings, "tide_close", "终止水面", 16, 10, 184, 50);
            AddButton(tide.transform, bindings, "fire_preview", "展开长明", 218, 10, 184, 50);
            AddButton(tide.transform, bindings, "fire_close", "关闭火界", 420, 10, 184, 50);
            tide.SetActive(false);
            var visibility = new GameObject("VisibilityActions", typeof(RectTransform)); visibility.transform.SetParent(body.transform, false);
            Place(visibility.GetComponent<RectTransform>(), 0, 0, 620, 240);
            AddButton(visibility.transform, bindings, "visibility_deploy", "展开显隐领域 · 预演", 16, 184, 588, 50);
            AddButton(visibility.transform, bindings, "visibility_close", "关闭本域 · 预演", 16, 126, 588, 50);
            visibility.SetActive(false);
            SceneBuildSupport.SetObject(view, "visibilityActions", visibility);
            SceneBuildSupport.SetObject(view, "tideActions", tide);
            SceneBuildSupport.SetObject(view, "flightActions", flight);
            SceneBuildSupport.SetObject(view, "scaleActions", scale);
            AddButton(body.transform, bindings, "confirm", "确认施放", 218, 68, 184, 50);
            AddButton(body.transform, bindings, "cancel_targeting", "取消选格", 420, 68, 184, 50);
            SceneBuildSupport.SetObject(view, "combatRoot", root);
            SceneBuildSupport.SetObject(view, "combatBody", body);
            SceneBuildSupport.SetObject(view, "combatDetails", details);
            SceneBuildSupport.SetObject(view, "combatQuote", quote);
            SceneBuildSupport.SetObject(view, "toggleLabel", toggle.GetComponentInChildren<Text>());
            root.SetActive(false);
        }

        private static void BuildVisibility(Scene scene, GameObject owner, Transform canvas, AdventureSceneInstaller installer)
        {
            var view = owner.GetComponent<JindanVisibilityView>() ?? owner.AddComponent<JindanVisibilityView>();
            var root = Panel(canvas, "JindanConflictRoot", false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0, 0, 0, .65f);
            var panel = Panel(root.transform, "ConflictDialog");
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(760, 560);
            var details = Label(panel.transform, "ConflictDetails", "", 24, 24, 164, 712, 374);
            details.alignment = TextAnchor.UpperLeft;
            var keys = Label(panel.transform, "ConflictBindings", "", 22, 24, 122, 712, 40);
            var unused = new List<JindanDemoView.ActionButton>();
            foreach (var item in new[] { ("holdBinding", "改维持键", 24f), ("firstBinding", "改破绽键一", 266f), ("secondBinding", "改破绽键二", 508f) })
            {
                var button = AddButton(panel.transform, unused, item.Item1, item.Item2, item.Item3, 70, 228, 46);
                button.navigation = new Navigation { mode = Navigation.Mode.None }; SceneBuildSupport.SetObject(view, item.Item1, button);
            }
            foreach (var item in new[] { ("skipButton", "跳过本次", 24f), ("autoButton", "本场自动跳过", 266f), ("exitButton", "撤离演武", 508f) })
            {
                var button = AddButton(panel.transform, unused, item.Item1, item.Item2, item.Item3, 14, 228, 46);
                button.navigation = new Navigation { mode = Navigation.Mode.None }; SceneBuildSupport.SetObject(view, item.Item1, button);
            }
            SceneBuildSupport.SetObject(view, "dialogRoot", root); SceneBuildSupport.SetObject(view, "details", details);
            SceneBuildSupport.SetObject(view, "bindingsText", keys);
            foreach (var item in new[] { ("yangMarker", "reveal_support"), ("yinMarker", "conceal_support") })
                SceneBuildSupport.SetObject(view, item.Item1, scene.GetRootGameObjects().SelectMany(value => value.GetComponentsInChildren<Transform>(true))
                    .Single(value => value.name == item.Item2).gameObject);
            SceneBuildSupport.SetObject(installer, "jindanVisibilityView", view);
        }

        private static Button AddButton(Transform parent, List<JindanDemoView.ActionButton> bindings,
            string key, string caption, float x, float y, float width, float height)
        {
            Button button = SceneBuildSupport.CreateButton(key, parent, caption, out Text label);
            CombatCompactUiAssets.Button(button);
            label.font = SettlementSceneBuilder.UiFont;
            label.fontSize = 22;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            Place(button.GetComponent<RectTransform>(), x, y, width, height);
            bindings.Add(new JindanDemoView.ActionButton { key = key, button = button });
            return button;
        }

        private static Text ScrollText(Transform parent, string name, float x, float y, float width,
            float height, int fontSize)
        {
            var viewport = new GameObject(name + "Viewport", typeof(RectTransform), typeof(Image),
                typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(parent, false);
            Place(viewport.GetComponent<RectTransform>(), x, y, width, height);
            viewport.GetComponent<Image>().color = Color.clear;
            Text text = Label(viewport.transform, name, string.Empty, fontSize, 0, 0, width - 12, height);
            RectTransform content = text.rectTransform;
            content.anchorMin = Vector2.up; content.anchorMax = Vector2.one;
            content.pivot = Vector2.up; content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(-12, height);
            text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            ScrollRect scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            return text;
        }

        private static Text Label(Transform parent, string name, string value, int size,
            float x, float y, float width, float height)
        {
            Text text = SceneBuildSupport.CreateText(name, parent, value, size);
            text.font = SettlementSceneBuilder.UiFont;
            text.color = new Color32(239, 233, 211, 255);
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            Place(text.rectTransform, x, y, width, height);
            return text;
        }

        private static GameObject Panel(Transform parent, string name, bool skinned = true)
        {
            GameObject panel = SceneBuildSupport.CreatePanel(name, parent, Vector2.zero, Vector2.one);
            if (skinned) CombatCompactUiAssets.Panel(panel);
            return panel;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Remove(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
    }
}
