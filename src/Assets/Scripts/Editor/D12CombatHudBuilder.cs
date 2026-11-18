using System;
using System.Linq;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Editor
{
    /// <summary>Applies the approved V3 layout to existing Adventure UI only. No battlefield roots are rebuilt.</summary>
    public static class D12CombatHudBuilder
    {
        [MenuItem("天章/场景/D12 统一战斗HUD")]
        public static void Rebuild()
        {
            EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
            Apply();
            SceneBuildSupport.Save(SceneBuildSupport.AdventureScenePath);
            EditorSceneManager.OpenScene(SceneBuildSupport.AdventureScenePath, OpenSceneMode.Single);
        }

        public static void Apply()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path) && scene.path != SceneBuildSupport.AdventureScenePath) throw new InvalidOperationException("D12 requires AdventureScene.");
            Transform canvas = scene.GetRootGameObjects().Single(go => go.name == "UICanvas").transform;
            GameObject owner = scene.GetRootGameObjects().Single(go => go.name == "AdventureRoot");
            if (!canvas.GetComponentsInChildren<RectTransform>(true).Any(r => r.name == "JindanDemoCombatRoot")) return;
            var bar = owner.GetComponent<CombatActionBarView>();
            var jindan = owner.GetComponent<JindanDemoView>();
            Transform hud = Find(canvas, "CombatHudRoot");
            Transform hotbar = Find(hud, "CombatHotbar");
            Rect(hotbar, 190, 28, 1540, 170);
            Transform actions = Find(hud, "CombatActionBar");
            Stretch(actions);
            Transform viewport = Find(hud, "ActionViewport");
            Rect(viewport, 12, 10, 1300, 150);
            viewport.GetComponent<ScrollRect>().enabled = false;
            viewport.GetComponent<RectMask2D>().enabled = false;
            Transform row = Find(hud, "ActionContent");
            Stretch(row);
            row.GetComponent<ContentSizeFitter>().enabled = false;
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            Button fourth = CloneSlot(hud, row, "ThirdArtButton", "FourthArtButton");
            Button fifth = CloneSlot(hud, row, "ThirdArtButton", "FifthArtButton");
            Button secondDivine = CloneSlot(hud, row, "DivineButton", "SecondDivineButton");
            SceneBuildSupport.SetObject(bar, "fourthArtButton", fourth);
            SceneBuildSupport.SetObject(bar, "fifthArtButton", fifth);
            SceneBuildSupport.SetObject(bar, "secondDivineButton", secondDivine);
            Find(hud, "MoveButton").SetParent(row, false);
            Find(hud, "MovementControls").gameObject.SetActive(false);
            foreach (string name in new[] { "CombatTurnText", "CombatTurnPortrait", "CombatLocationText" }) Find(hud, name).gameObject.SetActive(false);
            foreach (string name in new[] { "MoveButton", "BasicAttackButton", "ArtButton", "SecondArtButton", "ThirdArtButton", "FourthArtButton", "FifthArtButton", "DivineButton", "SecondDivineButton" })
                Find(hud, name).SetAsLastSibling();

            BuildJindan(canvas, hud, row, owner, jindan, bar);
            Find(hud, "GuardButton").SetAsLastSibling();
            var bag = Panel(hud, "CombatBagRoot");
            Rect(bag.transform, 1230, 210, 470, 190);
            string[] bagItems = { "SwapSpellButton", "SwapEquipmentButton", "UseItemButton" };
            for (int i = 0; i < bagItems.Length; i++)
            {
                Transform item = Find(hud, bagItems[i]); item.SetParent(bag.transform, false);
                Rect(item, 12 + i * 150, 10, 146, 166);
            }
            bag.SetActive(false);
            Button bagButton = Button(row, "BagButton", "行囊\n换术 / 装备");
            bagButton.transform.SetAsLastSibling();
            Image icon = Image(bagButton.transform, "ActionIcon", "icon_equipment");
            Rect(icon.transform, 42, 98, 36, 36);
            foreach (Button button in row.GetComponentsInChildren<Button>(true)) Slot(button);
            Transform movement = Find(hud, "MovementStatus");
            Rect(movement, 4, 0, 112, 34); movement.GetComponent<Text>().fontSize = 20;
            Rect(Find(hud, "MoveButton").GetComponentInChildren<Text>().transform, 4, 35, 112, 34);
            var wait = Find(hud, "WaitButton");
            Rect(wait, 1328, 34, 196, 102);
            wait.GetComponentInChildren<Text>().fontSize = 28;
            Transform log = Find(hud, "ToggleLogButton"); log.SetParent(hud, false); Rect(log, 1752, 876, 132, 52);
            Transform statusBody = Find(hud, "ActionStatusBody");
            statusBody.SetParent(hud, false); Rect(statusBody, 600, 204, 720, 54);
            Transform details = Panel(hud, "CombatSlotDetailsRoot").transform;
            Rect(details, 570, 206, 780, 66);
            Text detailsText = Label(details, "CombatSlotDetails", "", 22); Stretch(detailsText.transform, 12);
            SceneBuildSupport.SetObject(bar, "slotContainer", row);
            SceneBuildSupport.SetObject(bar, "bagButton", bagButton);
            SceneBuildSupport.SetObject(bar, "bagRoot", bag);
            SceneBuildSupport.SetObject(bar, "slotDetails", detailsText);
            SceneBuildSupport.SetObject(bar, "defaultAbilityIcon", CombatCompactUiAssets.Sprite("icon_divine"));
            SceneBuildSupport.SetObject(bar, "commandInput", owner.GetComponent<CombatCommandInput>());
            SceneBuildSupport.SetObject(bar, "movementView", owner.GetComponent<CombatMovementView>());
            BuildHeader(canvas, hud, owner);
            // Retain original preview contents and command behavior until V3 step 3.
            Rect(Find(canvas, "CombatActionPreviewRoot"), 1030, 276, 620, 276);
            canvas.Find("JindanConflictRoot")?.SetAsLastSibling();
            canvas.Find("AdventureWithdrawalConfirmation")?.SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void BuildJindan(Transform canvas, Transform hud, Transform row, GameObject owner,
            JindanDemoView view, CombatActionBarView bar)
        {
            Transform combat = Find(canvas, "JindanDemoCombatRoot");
            Rect(combat, 650, 276, 620, 346);
            Transform body = Find(combat, "JindanCombatBody"); Rect(body, 0, 0, 620, 346);
            var data = new SerializedObject(view);
            var bindings = data.FindProperty("buttons");
            Button fireTab = canvas.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "fire_tab");
            if (fireTab == null)
            {
                fireTab = Button(row, "fire_tab", "长明");
                int index = bindings.arraySize++;
                var binding = bindings.GetArrayElementAtIndex(index);
                binding.FindPropertyRelative("key").stringValue = "fire_tab";
                binding.FindPropertyRelative("button").objectReferenceValue = fireTab;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (string key in new[] { "earth_tab", "tide_tab", "fire_tab", "scale_tab", "visibility_tab", "flight_tab" })
            {
                Button button = Find(canvas, key).GetComponent<Button>();
                button.transform.SetParent(row, false); button.transform.SetAsLastSibling();
                Image(button.transform, "ActionIcon", key == "flight_tab" ? "icon_move" : "icon_divine");
                Slot(button);
            }
            Find(combat, "JindanCombatHeader").gameObject.SetActive(false);
            Transform fire = Child(body, "FireActions"); Rect(fire, 0, 0, 620, 240);
            foreach (var item in new[] { ("fire_preview", "展开长明", 16f), ("fire_close", "收势 · 关闭主域", 318f) })
            {
                Transform button = Find(canvas, item.Item1); button.SetParent(fire, false);
                Rect(button, item.Item3, 184, 286, 50); button.GetComponentInChildren<Text>().text = item.Item2;
            }
            Transform quoteViewport = Find(combat, "CombatQuoteViewport"); Rect(quoteViewport, 16, 242, 588, 92);
            GameObject detailPanel = Panel(hud, "JindanDetailsRoot");
            Rect(detailPanel.transform, 28, 250, 630, 520);
            Transform oldDetails = Find(canvas, "CombatDetailsViewport"); oldDetails.SetParent(detailPanel.transform, false);
            Stretch(oldDetails, 16);
            Button toggle = Find(canvas, "toggle_panel").GetComponent<Button>(); toggle.transform.SetParent(hud, false);
            Rect(toggle.transform, 1598, 876, 140, 52); toggle.GetComponentInChildren<Text>().text = "能力详情";
            detailPanel.SetActive(false);
            GameObject status = Panel(hud, "JindanStatusRoot");
            Rect(status.transform, 28, 564, 540, 284);
            status.GetComponent<Image>().color = new Color(1, 1, 1, .85f);
            status.GetComponent<Image>().raycastTarget = false;
            Text statusText = Label(status.transform, "JindanPersistentStatus", "", 24);
            Stretch(statusText.transform, 12); statusText.alignment = TextAnchor.UpperLeft;
            status.SetActive(false);
            SceneBuildSupport.SetObject(view, "fireActions", fire.gameObject);
            SceneBuildSupport.SetObject(view, "statusRoot", status);
            SceneBuildSupport.SetObject(view, "statusText", statusText);
            SceneBuildSupport.SetObject(view, "detailsRoot", detailPanel);
            SceneBuildSupport.SetObject(view, "actionBar", bar);
            combat.gameObject.SetActive(false);
        }

        private static void BuildHeader(Transform canvas, Transform hud, GameObject owner)
        {
            Transform card = Find(hud, "CombatResourceCard"); Rect(card, 28, 868, 540, 184);
            foreach (string resource in new[] { "Health", "Spirit", "ActionPoint" })
            {
                var track = Find(card, "Combat" + resource + "Bar").GetComponent<RectTransform>();
                track.sizeDelta = new Vector2(370, 4);
                Stretch(track.Find("Fill"));
                track.Find("Value").GetComponent<RectTransform>().sizeDelta = new Vector2(370, 36);
            }
            var name = Find(card, "PlayerSummaryBody").GetComponent<RectTransform>();
            name.offsetMax = new Vector2(-20, -16);
            Rect(Find(hud, "CombatBountyTrackerRoot"), 1370, 944, 514, 108);
            Transform rail = Find(canvas, "CombatTimelineRoot"); Rect(rail, 600, 952, 720, 100);
            Transform trackRoot = Find(rail, "TimelineTrack"); Rect(trackRoot, 0, 0, 720, 100);
            Find(rail, "TimelineTick").gameObject.SetActive(false);
            for (int i = 0; i < 3; i++)
            {
                Transform entry = Find(rail, "TimelineEntry" + i); Rect(entry, i * 240, 0, 232, 100);
                Text label = entry.GetComponentInChildren<Text>(); Rect(label.transform, 4, 56, 224, 36); label.fontSize = 21;
                Rect(entry.Find("Identity"), 10, 4, 48, 48);
                Rect(entry.Find("SelectionFrame"), 10, 4, 48, 48);
                Rect(entry.Find("CurrentMarker"), 2, 4, 4, 48);
                Rect(entry.Find("TimelineCt"), 68, 6, 146, 40);
            }
            Rect(Find(rail, "TimelineFocus"), 180, -170, 360, 160);
            var timeline = new SerializedObject(owner.GetComponent<CombatTimelineView>());
            timeline.FindProperty("horizontal").boolValue = true;
            timeline.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Button CloneSlot(Transform scope, Transform parent, string source, string name)
        {
            Transform existing = scope.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            if (existing != null) return existing.GetComponent<Button>();
            GameObject clone = UnityEngine.Object.Instantiate(Find(scope, source).gameObject, parent);
            clone.name = name; return clone.GetComponent<Button>();
        }

        private static void Slot(Button button)
        {
            var size = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 82; size.preferredWidth = 120; size.minHeight = size.preferredHeight = 148;
            size.flexibleWidth = size.flexibleHeight = 0;
            CombatCompactUiAssets.Button(button);
            Text label = button.GetComponentInChildren<Text>(true);
            Rect(label.transform, 4, 0, 112, 76); label.fontSize = 21; label.lineSpacing = .85f;
            label.rectTransform.anchorMax = Vector2.right;
            label.rectTransform.offsetMin = Vector2.zero; label.rectTransform.offsetMax = new Vector2(0, 76);
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            Image icon = Image(button.transform, "ActionIcon", null); Rect(icon.transform, 38, 82, 44, 44);
            Text hint = Label(button.transform, "Shortcut", "", 20); Rect(hint.transform, 4, 112, 40, 36);
            Text cooldown = Label(button.transform, "Cooldown", "", 19); Rect(cooldown.transform, 40, 118, 76, 28);
            Image underline = Image(button.transform, "SelectedUnderline", null);
            underline.color = new Color(1, .78f, .35f); Rect(underline.transform, 24, 74, 72, 3);
            underline.gameObject.SetActive(false);
            foreach (string name in new[] { "SelectedFrame", "FocusFrame" })
            {
                Image frame = Image(button.transform, name, "frame_selected"); Stretch(frame.transform);
                frame.type = UnityEngine.UI.Image.Type.Sliced;
                frame.color = name == "FocusFrame" ? Color.white : new Color(1, .78f, .35f);
                frame.gameObject.SetActive(false);
            }
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        }

        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        internal static void RemoveRelocatedJindanUi(Transform canvas)
        {
            // These nodes used to live below JindanDemoCombatRoot, which the old builder replaces.
            string[] names = { "flight_tab", "scale_tab", "earth_tab", "tide_tab", "visibility_tab", "fire_tab", "toggle_panel", "JindanDetailsRoot", "JindanStatusRoot" };
            foreach (string name in names)
                foreach (Transform node in canvas.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray())
                    UnityEngine.Object.DestroyImmediate(node.gameObject);
        }
        private static Transform Child(Transform parent, string name)
        {
            Transform value = parent.Find(name);
            if (value == null) { value = new GameObject(name, typeof(RectTransform)).transform; value.SetParent(parent, false); }
            return value;
        }
        private static GameObject Panel(Transform parent, string name)
        {
            Transform value = Child(parent, name);
            if (value.GetComponent<Image>() == null) value.gameObject.AddComponent<Image>();
            CombatCompactUiAssets.Panel(value.gameObject); return value.gameObject;
        }
        private static Button Button(Transform parent, string name, string caption)
        {
            Button value = parent.Find(name)?.GetComponent<Button>() ?? SceneBuildSupport.CreateButton(name, parent, caption, out _);
            CombatCompactUiAssets.Button(value); return value;
        }
        private static Image Image(Transform parent, string name, string sprite)
        {
            Transform value = Child(parent, name);
            var image = value.GetComponent<Image>() ?? value.gameObject.AddComponent<Image>();
            if (sprite != null) image.sprite = CombatCompactUiAssets.Sprite(sprite);
            image.raycastTarget = false; return image;
        }
        private static Text Label(Transform parent, string name, string text, int size)
        {
            Text label = parent.Find(name)?.GetComponent<Text>() ?? SceneBuildSupport.CreateText(name, parent, text, size);
            label.font = SettlementSceneBuilder.UiFont; label.fontSize = size; label.raycastTarget = false;
            label.color = new Color32(239, 233, 211, 255); return label;
        }
        private static void Rect(Transform target, float x, float y, float width, float height)
        {
            var rect = (RectTransform)target; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
        }
        private static void Stretch(Transform target, float margin = 0)
        {
            var rect = (RectTransform)target; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * margin; rect.offsetMax = Vector2.one * -margin;
        }
    }
}
