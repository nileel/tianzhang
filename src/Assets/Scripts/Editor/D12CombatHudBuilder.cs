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
            D12CombatHudAssets.Import();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path) && scene.path != SceneBuildSupport.AdventureScenePath) throw new InvalidOperationException("D12 requires AdventureScene.");
            Transform canvas = scene.GetRootGameObjects().Single(go => go.name == "UICanvas").transform;
            GameObject owner = scene.GetRootGameObjects().Single(go => go.name == "AdventureRoot");
            if (!canvas.GetComponentsInChildren<RectTransform>(true).Any(r => r.name == "JindanDemoCombatRoot")) return;
            var bar = owner.GetComponent<CombatActionBarView>();
            var jindan = owner.GetComponent<JindanDemoView>();
            Transform hud = Find(canvas, "CombatHudRoot");
            var hudRect = (RectTransform)hud;
            hudRect.anchorMin = hudRect.anchorMax = hudRect.pivot = Vector2.one * .5f;
            hudRect.anchoredPosition = Vector2.zero; hudRect.sizeDelta = new Vector2(1920, 1080);
            Image vignette = Image(hud, "CombatVignette", "vignette"); Stretch(vignette.transform); vignette.transform.SetAsFirstSibling();
            Transform hotbar = Find(hud, "CombatHotbar");
            Anchor(hotbar, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 32, 868, 140);
            D12CombatHudAssets.Surface(hotbar.GetComponent<Image>(), "dock");
            hotbar.GetComponent<Image>().raycastTarget = false;
            Transform actions = Find(hud, "CombatActionBar");
            Stretch(actions);
            Transform viewport = Find(hud, "ActionViewport");
            Rect(viewport, 26, 12, 646, 114);
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
            Find(hud, "close_tab").SetAsLastSibling();
            var bag = Panel(hud, "CombatBagRoot");
            Anchor(bag.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 172, 380, 110);
            string[] bagItems = { "SwapSpellButton", "SwapEquipmentButton", "UseItemButton" };
            for (int i = 0; i < bagItems.Length; i++)
            {
                Transform item = Find(hud, bagItems[i]); item.SetParent(bag.transform, false);
                Rect(item, 10 + i * 124, 10, 116, 90);
                D12CombatHudAssets.Button(item.GetComponent<Button>());
                item.GetComponentInChildren<Text>().fontSize = 16;
                item.Find("ActionIcon")?.gameObject.SetActive(false);
            }
            bag.SetActive(false);
            Button bagButton = Button(row, "BagButton", "行囊\n<size=14>换术 / 装备</size>");
            bagButton.GetComponentInChildren<Text>().text = "行囊\n<size=14>换术 / 装备</size>";
            bagButton.transform.SetAsLastSibling();
            Image(bagButton.transform, "ActionIcon", "icon_more");
            foreach (Button button in row.GetComponentsInChildren<Button>(true)) Slot(button);
            Transform movement = Find(hud, "MovementStatus");
            Anchor(movement, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 0, 92, 22);
            movement.GetComponent<Text>().fontSize = 14;
            Anchor(Find(hud, "MoveButton").GetComponentInChildren<Text>().transform,
                new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 23, 92, 26);
            foreach (var pair in new[] { ("MoveButton", "move"), ("GuardButton", "guard"), ("close_tab", "close") })
                Image(Find(hud, pair.Item1), "ActionIcon", "icon_" + pair.Item2);
            var wait = Find(hud, "WaitButton");
            wait.SetParent(hotbar, false);
            Anchor(wait, Vector2.right, Vector2.right, -26, 23, 144, 70);
            D12CombatHudAssets.Button(wait.GetComponent<Button>(), "end");
            wait.GetComponentInChildren<Text>().fontSize = 20;
            Transform log = Find(hud, "ToggleLogButton"); log.SetParent(hud, false);
            Anchor(log, new Vector2(.98f, .94f), Vector2.one, -58, -68, 44, 40);
            D12CombatHudAssets.Button(log.GetComponent<Button>()); log.GetComponentInChildren<Text>().text = "录";
            log.GetComponentInChildren<Text>().fontSize = 15;
            Transform statusBody = Find(hud, "ActionStatusBody");
            statusBody.SetParent(hud, false);
            Anchor(statusBody, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 181, 760, 26);
            ClearSurface(statusBody);
            Transform details = Panel(hud, "CombatSlotDetailsRoot").transform;
            Anchor(details, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 181, 760, 26);
            ClearSurface(details);
            Text detailsText = Label(details, "CombatSlotDetails", "", 16); Stretch(detailsText.transform);
            SceneBuildSupport.SetObject(bar, "slotContainer", row);
            SceneBuildSupport.SetObject(bar, "bagButton", bagButton);
            SceneBuildSupport.SetObject(bar, "bagRoot", bag);
            SceneBuildSupport.SetObject(bar, "slotDetails", detailsText);
            foreach (var pair in new[] { ("attackIcon", "attack"), ("unarmedIcon", "attack"), ("guanshijiIcon", "stone"),
                ("julingshuIcon", "gather"), ("feixingzhiIcon", "star"), ("defaultAbilityIcon", "star") })
                SceneBuildSupport.SetObject(bar, pair.Item1, D12CombatHudAssets.Sprite("icon_" + pair.Item2));
            SceneBuildSupport.SetObject(bar, "commandInput", owner.GetComponent<CombatCommandInput>());
            SceneBuildSupport.SetObject(bar, "movementView", owner.GetComponent<CombatMovementView>());
            SceneBuildSupport.SetObject(owner.GetComponent<TianZhang.Features.Adventure.AdventureHudPresenter>(), "combatReturnSprite", D12CombatHudAssets.Sprite("button"));
            BuildHeader(canvas, hud, owner);
            BuildPreview(canvas, hud, owner);
            var responsive = hud.GetComponent<D12CombatHudLayout>() ?? hud.gameObject.AddComponent<D12CombatHudLayout>();
            foreach (var pair in new[] { ("hud", "CombatHudRoot"), ("card", "CombatResourceCard"), ("portrait", "CombatPlayerPortrait"),
                ("vitals", "CombatVitals"), ("statuses", "JindanStatusRoot"), ("hotbar", "CombatHotbar"), ("viewport", "ActionViewport"),
                ("row", "ActionContent"), ("endButton", "WaitButton"), ("timeline", "CombatTimelineRoot"),
                ("ordinaryQuote", "CombatActionPreviewRoot"), ("jindanQuote", "JindanDemoCombatRoot") })
                SceneBuildSupport.SetObject(responsive, pair.Item1, Find(hud, pair.Item2).GetComponent<RectTransform>());
            SceneBuildSupport.SetObject(responsive, "jindan", jindan);
            canvas.Find("JindanConflictRoot")?.SetAsLastSibling();
            canvas.Find("AdventureWithdrawalConfirmation")?.SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void BuildJindan(Transform canvas, Transform hud, Transform row, GameObject owner,
            JindanDemoView view, CombatActionBarView bar)
        {
            Transform combat = Find(canvas, "JindanDemoCombatRoot");
            combat.SetParent(hud, false);
            Anchor(combat, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 252, 510, 78);
            Transform body = Find(combat, "JindanCombatBody"); Stretch(body);
            ClearSurface(body);
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
            if (GetButton(canvas, "close_tab") == null)
            {
                Button close = Button(row, "close_tab", "收势\n<size=14>选择实例</size>");
                data.Update(); int index = bindings.arraySize++;
                var binding = bindings.GetArrayElementAtIndex(index);
                binding.FindPropertyRelative("key").stringValue = "close_tab";
                binding.FindPropertyRelative("button").objectReferenceValue = close;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (string key in new[] { "earth_tab", "tide_tab", "fire_tab", "scale_tab", "visibility_tab", "flight_tab" })
            {
                Button button = Find(canvas, key).GetComponent<Button>();
                button.transform.SetParent(row, false); button.transform.SetAsLastSibling();
                string iconName = key switch { "fire_tab" => "fire", "scale_tab" => "scale", "visibility_tab" => "sun", "flight_tab" => "flight", "earth_tab" => "stone", _ => "gather" };
                Image(button.transform, "ActionIcon", "icon_" + iconName);
            }
            Find(combat, "JindanCombatHeader").gameObject.SetActive(false);
            Transform fire = Child(body, "FireActions");
            Find(canvas, "fire_preview").SetParent(fire, false);
            Transform closes = Child(body, "CloseActions");
            foreach (string key in new[] { "fire_close", "close_effect", "visibility_close", "tide_close", "earth_close" })
                Find(canvas, key).SetParent(closes, false);
            foreach (string groupName in new[] { "FireActions", "ScaleActions", "FlightActions", "EarthActions", "TideActions", "VisibilityActions", "CloseActions" })
            {
                Transform group = Find(body, groupName);
                Anchor(group, new Vector2(.5f, 0), new Vector2(.5f, 1), 0, -8, 402, 76);
                var grid = group.GetComponent<GridLayoutGroup>() ?? group.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(128, 32); grid.spacing = new Vector2(8, 6); grid.childAlignment = TextAnchor.UpperCenter;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
                foreach (Button option in group.GetComponentsInChildren<Button>(true))
                {
                    D12CombatHudAssets.Button(option); Text label = option.GetComponentInChildren<Text>(); label.fontSize = 15;
                    label.text = OptionName(option.name, label.text); Stretch(label.transform, 2);
                }
            }
            GameObject detailPanel = Panel(hud, "JindanDetailsRoot");
            Anchor(detailPanel.transform, new Vector2(.02f, .21f), Vector2.zero, 0, 0, 440, 400);
            Transform oldDetails = Find(canvas, "CombatDetailsViewport"); oldDetails.SetParent(detailPanel.transform, false);
            Stretch(oldDetails, 16);
            Button toggle = Find(canvas, "toggle_panel").GetComponent<Button>(); toggle.transform.SetParent(hud, false);
            Anchor(toggle.transform, new Vector2(.98f, .94f), Vector2.one, -116, -68, 44, 40);
            D12CombatHudAssets.Button(toggle); toggle.GetComponentInChildren<Text>().text = "目"; toggle.GetComponentInChildren<Text>().fontSize = 15;
            detailPanel.SetActive(false);
            GameObject status = Panel(hud, "JindanStatusRoot");
            Anchor(status.transform, new Vector2(.02f, .8f), Vector2.up, 0, 0, 440, 200); ClearSurface(status.transform);
            Text statusText = Label(status.transform, "JindanPersistentStatus", "", 17); statusText.gameObject.SetActive(false);
            Transform statusRow = Panel(status.transform, "StatusRow").transform;
            D12CombatHudAssets.Surface(statusRow.GetComponent<Image>(), "status"); statusRow.GetComponent<Image>().raycastTarget = false;
            Text statusLine = Label(statusRow, "StatusLine", "", 17); Stretch(statusLine.transform, 4);
            statusLine.alignment = TextAnchor.MiddleLeft;
            Image statusRule = Image(statusRow, "StatusRule", null); statusRule.color = new Color32(196, 173, 115, 136);
            var ruleRect = statusRule.rectTransform; ruleRect.anchorMin = Vector2.zero; ruleRect.anchorMax = Vector2.up;
            ruleRect.pivot = Vector2.zero; ruleRect.sizeDelta = new Vector2(2, 0); ruleRect.anchoredPosition = Vector2.zero;
            SceneBuildSupport.SetObject(view, "statusTemplate", statusLine);
            status.SetActive(false);
            SceneBuildSupport.SetObject(view, "fireActions", fire.gameObject);
            SceneBuildSupport.SetObject(view, "closeActions", closes.gameObject);
            SceneBuildSupport.SetObject(view, "statusRoot", status);
            SceneBuildSupport.SetObject(view, "statusText", statusText);
            SceneBuildSupport.SetObject(view, "detailsRoot", detailPanel);
            SceneBuildSupport.SetObject(view, "actionBar", bar);
            combat.gameObject.SetActive(false);
        }

        private static Button GetButton(Transform root, string name) => root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);
        private static string OptionName(string key, string fallback) => key switch
        {
            "compress" => "压缩 · 0.5", "expand" => "扩张 · 2", "pick_edges" => "选相连边", "pick_origin" => "遥施原点",
            "preview" or "earth_preview" or "tide_preview" => "取得报价", "fire_preview" => "展开长明",
            "fire_close" => "长明收势", "close_effect" => "定尺收势", "visibility_close" => "日月坛收势",
            "tide_close" => "蓄澜收势", "earth_close" => "厚载收势", "visibility_deploy" => "展开本域",
            "flight_takeoff" => "起飞", "flight_ascend" => "升至高空", "flight_descend" => "降至低空",
            "flight_land" => "降落", "flight_recover" => "续御", "tide_charge" => "普通 / 蓄力", "tide_eye" => "无源潮眼",
            _ => fallback,
        };

        private static void BuildHeader(Transform canvas, Transform hud, GameObject owner)
        {
            var resourceView = new SerializedObject(owner.GetComponent<CombatHudView>());
            resourceView.FindProperty("separateResourceNames").boolValue = true; resourceView.ApplyModifiedPropertiesWithoutUndo();
            Transform card = Find(hud, "CombatResourceCard");
            Anchor(card, new Vector2(.02f, .95f), Vector2.up, 0, 0, 440, 137);
            D12CombatHudAssets.Surface(card.GetComponent<Image>(), "identity"); card.GetComponent<Image>().raycastTarget = false;
            Transform portrait = Find(card, "CombatPlayerPortrait"); Rect(portrait, 0, 10, 96, 116);
            Transform vitals = Child(card, "CombatVitals"); Stretch(vitals);
            Transform player = Find(card, "CombatPlayerText"); player.SetParent(vitals, false);
            Text name = player.GetComponent<Text>(); name.fontSize = 23; name.alignment = TextAnchor.MiddleLeft;
            DisableLayout(player); Anchor(player, Vector2.up, Vector2.up, 0, 0, 314, 33);
            Text build = Label(vitals, "CombatBuildLabel", "", 14); build.color = new Color32(216, 189, 125, 255);
            Anchor(build.transform, Vector2.up, Vector2.up, 90, -6, 150, 26); build.alignment = TextAnchor.MiddleLeft;
            SceneBuildSupport.SetObject(owner.GetComponent<JindanDemoView>(), "buildLabel", build);
            SceneBuildSupport.SetObject(owner.GetComponent<JindanDemoView>(), "playerName", name);
            Find(card, "PlayerSummaryBody").gameObject.SetActive(false);
            string[] resources = { "Health", "Spirit", "ActionPoint" };
            string[] names = { "生命", "灵力", "AP" };
            Color32[] colors = { new Color32(163, 191, 140, 255), new Color32(145, 213, 203, 255), new Color32(217, 188, 115, 255) };
            for (int i = 0; i < resources.Length; i++)
            {
                Transform track = Find(card, "Combat" + resources[i] + "Bar"); track.SetParent(vitals, false);
                Anchor(track, Vector2.up, Vector2.up, 48, -45 - i * 27, 146, 5.6f);
                var image = track.GetComponent<Image>(); image.sprite = null; image.color = new Color32(2, 10, 16, 170);
                image.raycastTarget = false;
                Stretch(track.Find("Fill")); var fill = track.Find("Fill").GetComponent<Image>();
                fill.sprite = D12CombatHudAssets.Sprite("solid");
                fill.color = colors[i]; fill.type = UnityEngine.UI.Image.Type.Filled; fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
                Transform value = track.Find("Value") ?? vitals.Find("Combat" + resources[i] + "Value");
                value.SetParent(vitals, false); value.name = "Combat" + resources[i] + "Value";
                Anchor(value, Vector2.one, Vector2.one, 0, -35 - i * 27, 112, 24);
                value.GetComponent<Text>().fontSize = 16; value.GetComponent<Text>().alignment = TextAnchor.MiddleRight;
                Text title = Label(vitals, "ResourceName" + i, names[i], 16); title.text = names[i];
                Anchor(title.transform, Vector2.up, Vector2.up, 0, -35 - i * 27, 40, 24);
            }
            Transform bounty = Find(hud, "CombatBountyTrackerRoot");
            Anchor(bounty, new Vector2(.98f, .94f), Vector2.one, 0, 0, 350, 60); ClearSurface(bounty);
            Text objective = bounty.GetComponentInChildren<Text>(true); objective.transform.SetParent(bounty, false); DisableLayout(objective.transform);
            objective.fontSize = 19; objective.alignment = TextAnchor.UpperRight; Stretch(objective.transform);
            Transform rail = Find(canvas, "CombatTimelineRoot"); rail.SetParent(hud, false);
            Anchor(rail, new Vector2(.5f, .95f), new Vector2(.5f, 1), 0, 0, 202, 48); ClearSurface(rail);
            Transform trackRoot = Find(rail, "TimelineTrack"); Stretch(trackRoot); ClearSurface(trackRoot);
            Find(rail, "TimelineTick").gameObject.SetActive(false);
            for (int i = 0; i < 3; i++)
            {
                Transform entry = Find(rail, "TimelineEntry" + i); Rect(entry, i * 77, 0, 48, 48);
                D12CombatHudAssets.Button(entry.GetComponent<Button>(), "token");
                Text label = entry.GetComponentInChildren<Text>(); Stretch(label.transform); label.fontSize = 15;
                entry.Find("Identity").gameObject.SetActive(false); entry.Find("TimelineCt").gameObject.SetActive(false);
                foreach (string marker in new[] { "SelectionFrame", "CurrentMarker" })
                { Stretch(entry.Find(marker)); D12CombatHudAssets.Surface(entry.Find(marker).GetComponent<Image>(), "token_current"); }
                Text arrow = Label(entry, "OrderArrow", "›", 15); arrow.text = "›"; Rect(arrow.transform, 55, 0, 15, 48);
            }
            Transform focus = Find(rail, "TimelineFocus"); Rect(focus, -60, -155, 320, 140);
            D12CombatHudAssets.Surface(focus.GetComponent<Image>(), "panel");
            focus.GetComponentInChildren<Text>().fontSize = 17;
            var timeline = new SerializedObject(owner.GetComponent<CombatTimelineView>());
            timeline.FindProperty("horizontal").boolValue = true;
            timeline.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildPreview(Transform canvas, Transform hud, GameObject owner)
        {
            Transform preview = Find(canvas, "CombatActionPreviewRoot"); preview.SetParent(hud, false);
            Anchor(preview, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 214, 460, 78);
            D12CombatHudAssets.Surface(preview.GetComponent<Image>(), "panel");
            Transform heading = Find(preview, "ActionPreviewHeading"); Rect(heading, 24, 12, 58, 54);
            heading.GetComponent<Text>().fontSize = 22; heading.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
            Find(preview, "PreviewLegend").gameObject.SetActive(false);
            Transform actions = Find(preview, "PreviewActions"); DisableLayout(actions); Stretch(actions);
            var view = owner.GetComponent<CombatActionPreviewView>();
            Rect(view.ConfirmButton.transform, 240, 16, 118, 46); D12CombatHudAssets.Button(view.ConfirmButton, "confirm");
            view.ConfirmButton.GetComponentInChildren<Text>().fontSize = 18;
            Rect(view.CancelButton.transform, 380, 16, 56, 46); D12CombatHudAssets.Button(view.CancelButton, null);
            view.CancelButton.GetComponentInChildren<Text>().text = "Esc"; view.CancelButton.GetComponentInChildren<Text>().fontSize = 18;
            foreach (Button button in new[] { view.ConfirmButton, view.CancelButton }) DisableLayout(button.transform);
            Text cost = Label(preview, "CompactQuote", "", 19); Rect(cost.transform, 90, 10, 144, 58); cost.alignment = TextAnchor.MiddleLeft;
            Text prompt = Label(preview, "BoardActionPrompt", "点格锁定", 18); Rect(prompt.transform, 240, 16, 118, 46);
            Text hint = Label(preview, "ActionHint", "", 16);
            Anchor(hint.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, -36, 760, 28);
            Transform selectors = Find(preview, "PreviewSelectors"); Rect(selectors, -50, 86, 560, 38);
            foreach (Button button in selectors.GetComponentsInChildren<Button>(true)) { D12CombatHudAssets.Button(button); button.GetComponentInChildren<Text>().fontSize = 16; }
            GameObject detail = Panel(hud, "OrdinaryActionDetails");
            Anchor(detail.transform, new Vector2(.02f, .21f), Vector2.zero, 0, 0, 440, 400);
            Transform summaryBody = Find(hud, "PreviewBody"); summaryBody.SetParent(detail.transform, false); Stretch(summaryBody, 20);
            Find(detail.transform, "ActionPreviewSummary").GetComponent<Text>().fontSize = 18;
            detail.SetActive(false);
            SceneBuildSupport.SetObject(view, "compactQuote", cost);
            SceneBuildSupport.SetObject(view, "boardPrompt", prompt);
            SceneBuildSupport.SetObject(view, "actionHint", hint);
            SceneBuildSupport.SetObject(view, "detailsRoot", detail);
            SceneBuildSupport.SetObject(view, "detailsButton", Find(hud, "toggle_panel").GetComponent<Button>());
            SceneBuildSupport.SetObject(owner.GetComponent<JindanDemoView>(), "ordinaryPreview", view);
            SceneBuildSupport.SetObject(owner.GetComponent<CombatActionBarView>(), "previewView", view);
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
            size.minWidth = 68; size.preferredWidth = 83; size.minHeight = size.preferredHeight = 114;
            size.flexibleWidth = size.flexibleHeight = 0;
            D12CombatHudAssets.Button(button, null);
            Text label = button.GetComponentInChildren<Text>(true);
            Anchor(label.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 0, 100, 49);
            label.fontSize = 17; label.lineSpacing = 1; label.alignment = TextAnchor.LowerCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow; label.verticalOverflow = VerticalWrapMode.Truncate;
            Image frameBackground = Image(button.transform, "IconFrame", "slot");
            Anchor(frameBackground.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 47, 72, 72);
            frameBackground.transform.SetAsFirstSibling();
            Image icon = Image(button.transform, "ActionIcon", null);
            Anchor(icon.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 64, 38, 38);
            icon.color = button.name.EndsWith("_tab") ? new Color32(233, 197, 133, 255) : new Color32(215, 226, 213, 255);
            Text hint = Label(button.transform, "Shortcut", "", 14);
            Anchor(hint.transform, new Vector2(.5f, 0), new Vector2(0, 0), -36, 109, 28, 22);
            Text cooldown = Label(button.transform, "Cooldown", "", 17);
            Anchor(cooldown.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 50, 64, 64);
            Image shadeImage = Image(button.transform, "CooldownShade", null); shadeImage.sprite = null; shadeImage.color = new Color32(7, 17, 25, 217);
            Transform shade = shadeImage.transform;
            Anchor(shade, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 50, 64, 64); shade.SetAsLastSibling(); shade.gameObject.SetActive(false);
            cooldown.transform.SetAsLastSibling(); hint.transform.SetAsLastSibling();
            Image underline = Image(button.transform, "SelectedUnderline", null);
            underline.sprite = null; underline.color = new Color32(243, 214, 147, 255);
            Anchor(underline.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, -5, 22, 2);
            underline.gameObject.SetActive(false);
            foreach (string name in new[] { "SelectedFrame", "FocusFrame" })
            {
                Image frame = Image(button.transform, name, name == "FocusFrame" ? "focus" : "selected");
                Anchor(frame.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), 0, 47, 72, 72);
                frame.type = UnityEngine.UI.Image.Type.Simple; frame.color = Color.white;
                frame.gameObject.SetActive(false);
            }
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        }

        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        internal static void RemoveRelocatedJindanUi(Transform canvas)
        {
            // These nodes used to live below JindanDemoCombatRoot, which the old builder replaces.
            string[] names = { "flight_tab", "scale_tab", "earth_tab", "tide_tab", "visibility_tab", "fire_tab", "close_tab", "toggle_panel", "JindanDetailsRoot", "JindanStatusRoot", "JindanDemoCombatRoot" };
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
            D12CombatHudAssets.Surface(value.GetComponent<Image>(), "panel"); return value.gameObject;
        }
        private static Button Button(Transform parent, string name, string caption)
        {
            Button value = parent.Find(name)?.GetComponent<Button>() ?? SceneBuildSupport.CreateButton(name, parent, caption, out _);
            D12CombatHudAssets.Button(value); return value;
        }
        private static Image Image(Transform parent, string name, string sprite)
        {
            Transform value = Child(parent, name);
            var image = value.GetComponent<Image>() ?? value.gameObject.AddComponent<Image>();
            if (sprite != null) image.sprite = D12CombatHudAssets.Sprite(sprite);
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
        private static void Anchor(Transform target, Vector2 anchor, Vector2 pivot, float x, float y, float width, float height)
        {
            Rect(target, x, y, width, height); var rect = (RectTransform)target;
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot;
        }
        private static void ClearSurface(Transform target)
        {
            var image = target.GetComponent<Image>();
            if (image != null) { image.color = Color.clear; image.raycastTarget = false; }
        }
        private static void DisableLayout(Transform target)
        {
            foreach (var group in target.GetComponents<LayoutGroup>()) group.enabled = false;
            var fitter = target.GetComponent<ContentSizeFitter>(); if (fitter != null) fitter.enabled = false;
        }
        private static void Stretch(Transform target, float margin = 0)
        {
            var rect = (RectTransform)target; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * margin; rect.offsetMax = Vector2.one * -margin;
        }
    }
}
