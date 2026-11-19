using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class D12PlayerStartPlayModeTests
    {
        private Mouse mouse;
        private string saves;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private readonly string output = Path.GetFullPath("Logs/d12-hud/changming");
        private static T Asset<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
#else
            throw new NotSupportedException("Production asset test requires the editor.");
#endif
        }

        [Test]
        public void TrialRoundTripRejectsInvalidSourcesWithoutReplacingLiveOwners()
        {
            var content = Asset<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset");
            var demo = Asset<JindanDemoCatalogData>("Assets/Data/JindanDemo/JindanDemoCatalog.asset");
            var runtime = new GameRuntime(); runtime.ConfigureGrowth(content.CombatRuleSet);
            runtime.BeginJindanDemoGame("沈行舟", demo);
            string json = runtime.CaptureSaveJson();
            var restored = new GameRuntime(); restored.ConfigureGrowth(content.CombatRuleSet);
            restored.RestoreSaveJson(json, content, demo);
            Assert.AreEqual(json, restored.CaptureSaveJson());
            var owner = restored.Player;
            var bad = GameSaveSerializer.Deserialize(json);
            bad.jindanDemoContentVersion = "unknown";
            Assert.Throws<ArgumentException>(() => restored.RestoreSave(bad, content, demo));
            Assert.AreSame(owner, restored.Player);
            bad = GameSaveSerializer.Deserialize(json);
            bad.cultivation.foundationPurpleMansionState.characterId = "wrong_owner";
            Assert.Throws<ArgumentException>(() => restored.RestoreSave(bad, content, demo));
            Assert.AreSame(owner, restored.Player);
            bad = GameSaveSerializer.Deserialize(json);
            bad.player.equippedSpells = new[] { "spell_feixingzhi", "spell_feixingzhi" };
            Assert.Throws<ArgumentException>(() => restored.RestoreSave(bad, content, demo));
            Assert.AreSame(owner, restored.Player);
            bad = GameSaveSerializer.Deserialize(json); bad.hasPlayer = false;
            Assert.Throws<InvalidDataException>(() => GameSaveSerializer.Serialize(bad));
            var spawned = JindanRehearsalFactory.CreateForPlayer(demo, "EARTH_GRAVITY", key => key, owner.Capture());
            var loadout = JindanRehearsalFactory.CreateLoadout(demo, spawned, "EARTH_GRAVITY");
            Assert.AreEqual(1, loadout.Seats.Count(s => s.Definition.ActorId == "player"));
            Assert.IsNull(JindanRehearsalFactory.CreateFlight(demo, spawned).Find("player"));
            Assert.AreEqual("demo_20261024_fire", demo.GetPreset(JindanDemoCatalogData.PlayerFirePresetId).foundationState.characterId,
                "Creating a player must not mutate the catalog asset.");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator CreateLoadEnterDeployWithdrawAndReenterAtBothResolutions()
        {
            Directory.CreateDirectory(output);
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>();
            saves = Path.Combine(Application.temporaryCachePath, "D12Start-" + Guid.NewGuid().ToString("N"));
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1366, 768) })
            {
                var stale = Object.FindFirstObjectByType<GameBootstrap>();
                if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
                GuanzhongCombatEquipmentPlayModeTests.SetGameSize(size.x, size.y);
                yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
                yield return null;
                Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
                var creation = Find<CharacterCreationController>();
                yield return Click(GameObject.Find("NewPlayerButton").GetComponent<Button>());
                yield return Click(GameObject.Find("JindanStartButton").GetComponent<Button>());
                Assert.IsTrue(creation.IsJindanTrial);
                GameObject.Find("SlotIdInput").GetComponent<InputField>().text = "trial" + size.x;
                GameObject.Find("CharacterNameInput").GetComponent<InputField>().text = "沈行舟";
                yield return Capture(size.x + "-creation");
                yield return Click(GameObject.Find("CreatePlayerButton").GetComponent<Button>());
                yield return SceneReady(GameplaySceneNames.World);
                var runtime = GameBootstrap.RequireRuntime();
                Assert.AreEqual("player", runtime.Player.Identity.CharacterId);
                Assert.AreEqual("沈行舟", runtime.Player.Identity.DisplayName);
                Assert.AreEqual("金丹初结", runtime.Player.Progression.RealmStage);
                Assert.Contains("spell_feixingzhi", runtime.Player.AbilityLoadout.EquippedSpells.ToArray());
                string playerJson = JsonUtility.ToJson(runtime.CaptureSave().player);
                string cultivationJson = JsonUtility.ToJson(runtime.CaptureSave().cultivation);
                Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
                Find<WorldMapController>().EnterSelectedLocation();
                yield return SceneReady(GameplaySceneNames.Settlement);
                Assert.IsTrue(GameBootstrap.RequireInstance().SaveActiveSlot().Succeeded);
                for (int entry = 0; entry < 2; entry++)
                {
                    if (entry == 1)
                    {
                        yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
                        yield return null;
                        Assert.IsTrue(Find<StartMenuSceneInstaller>().LoadPlayer("trial" + size.x).Succeeded);
                        yield return SceneReady(GameplaySceneNames.Adventure);
                        Assert.AreEqual(playerJson, JsonUtility.ToJson(runtime.CaptureSave().player));
                        Assert.AreEqual(cultivationJson, JsonUtility.ToJson(runtime.CaptureSave().cultivation));
                    }
                    else
                    {
                        Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
                        yield return SceneReady(GameplaySceneNames.Adventure);
                    }
                    var view = Find<JindanDemoView>();
                    yield return Click(view.GetButton("stage2"));
                    yield return Click(view.GetButton("enter"));
                    var encounter = Find<EncounterCoordinator>();
                    for (float end = Time.realtimeSinceStartup + 30; !encounter.AcceptsPlayerCommand && Time.realtimeSinceStartup < end;) yield return null;
                    Assert.IsTrue(encounter.AcceptsPlayerCommand);
                    Assert.AreEqual("player", encounter.ControlledActorId);
                    var session = encounter.CurrentSession;
                    Assert.IsTrue(session.Combatants.TryGet("player", out var actor));
                    Assert.IsFalse(session.Combatants.TryGet(JindanDemoCatalogData.PlayerFirePresetId, out _));
                    Assert.Contains("spell_feixingzhi", actor.EquippedArtProfileIds.ToArray());
                    var seat = session.Jindan.Seats.Single(s => s.Definition.ActorId == "player");
                    Assert.AreEqual("player:trial:demo_20261024_fire_guardian", seat.Definition.AbilityInstanceId);
                    Assert.IsNull(seat.FireDomain?.Main);
                    Assert.IsNull(session.Flight.Find("player"));
                    Assert.IsNotNull(session.ExternalResources);
                    yield return Capture(size.x + "-initial-hud");
                    D12UnifiedHudPlayModeTests.CheckSlots("changming-" + size.x);
                    Assert.IsTrue(view.GetButton("fire_tab").gameObject.activeInHierarchy);
                    Assert.IsFalse(view.GetButton("tide_tab").gameObject.activeInHierarchy);
                    Assert.IsFalse(view.GetButton("flight_tab").gameObject.activeInHierarchy);
                    Assert.IsFalse(Find<CombatActionBarView>().DivineButton.gameObject.activeInHierarchy);
                    Assert.IsFalse(Find<CombatActionBarView>().SecondArtButton.gameObject.activeInHierarchy);
                    StringAssert.Contains("守阵 · 接近阵心", Find<CombatBountyTrackerView>().Summary);
                    yield return CastFlyingStarAtEmptyCell(encounter, actor, entry == 0 ? "新建" : "读档");
                    if (entry == 0)
                    {
                        var shared = Find<CombatActionPreviewView>();
                        int beforeApproach = actor.ActionPointsRemainingUnits;
                        int mana = actor.CurrentSpirit;
                        yield return Click(view.GetButton("fire_tab"));
                        yield return Click(view.GetButton("fire_preview"));
                        Assert.IsFalse(shared.ConfirmButton.interactable);
                        StringAssert.Contains("点「移动」靠近", GameObject.Find("ActionHint").GetComponent<Text>().text);
                        var marked = Find<CombatMovementView>().Highlights.Single(pair => pair.Value.gameObject.activeSelf).Key;
                        var center = new CombatUnitPresentationHex(marked.x, marked.y);
                        Assert.AreEqual(seat.FireDomain.Support.Cell.Q, center.Q);
                        Assert.AreEqual(seat.FireDomain.Support.Cell.R, center.R);
                        var label = GameObject.Find("FireCenterLabel").GetComponent<Text>();
                        Assert.AreEqual("阵心", label.text);
                        Vector3 ground = Camera.main.WorldToViewportPoint(Find<CombatBattlefieldSurface>().Center(center));
                        Assert.Less(Vector2.Distance(label.rectTransform.anchorMin, (Vector2)ground), .001f);
                        yield return Capture(size.x + "-fire-center-approach");
                        yield return Click(shared.CancelButton);
                        Assert.IsFalse(Find<CombatMovementView>().Highlights[marked].gameObject.activeSelf);
                        Assert.AreEqual(beforeApproach, actor.ActionPointsRemainingUnits);
                        Assert.AreEqual(mana, actor.CurrentSpirit);
                        yield return MoveToMarkedCenter(encounter, actor, center);
                        yield return Click(view.GetButton("fire_tab"));
                        yield return Click(view.GetButton("fire_preview"));
                        Assert.IsTrue(shared.ConfirmButton.interactable);
                        int ap = actor.ActionPointsRemainingUnits;
                        yield return Click(Find<CombatActionBarView>().ArtButton);
                        Assert.IsNull(view.SelectedKey, "Ordinary selection must cancel a frozen self-cast quote.");
                        Assert.AreEqual("spell_feixingzhi", Find<CombatCommandInput>().Candidate.ProfileId);
                        Assert.AreEqual(ap, actor.ActionPointsRemainingUnits);
                        Assert.AreEqual(mana, actor.CurrentSpirit);
                        yield return Capture(size.x + "-spell-after-cancel");
                        yield return Click(view.GetButton("fire_tab"));
                        yield return Click(view.GetButton("fire_preview"));
                        Assert.AreEqual(mana, actor.CurrentSpirit);
                        Assert.AreEqual(ap, actor.ActionPointsRemainingUnits);
                        AssertPrice(500000, 1000);
                        yield return Capture(size.x + "-fire-confirm");
                        yield return Click(shared.ConfirmButton);
                        Assert.IsNotNull(seat.FireDomain?.Main);
                        Assert.AreEqual(mana - 1000, actor.CurrentSpirit);
                        Assert.AreEqual(ap - 500000, actor.ActionPointsRemainingUnits);
                        yield return CommandReady(encounter);
                        yield return Click(Find<CombatActionBarView>().BasicAttackButton);
                        StringAssert.Contains("中心 600/600", view.PersistentStatus);
                        StringAssert.Contains("护住阵心", Find<CombatBountyTrackerView>().Summary);
                        StringAssert.Contains("200 MP", view.PersistentStatus);
                        StringAssert.Contains("本回合不维护", view.PersistentStatus);
                        Assert.AreEqual(Find<CombatActionBarView>().BasicAttackButton, Find<CombatActionBarView>().SelectedButton);
                        yield return Capture(size.x + "-ordinary-with-domain");
                        yield return ConfirmBasicAttack(encounter, actor);
                        Assert.IsNotNull(seat.FireDomain?.Main);
                        yield return Click(view.GetButton("close_tab"));
                        yield return Click(view.GetButton("fire_close"));
                        Assert.IsTrue(shared.ConfirmButton.interactable, "Active fire offers its own close quote.");
                        yield return Capture(size.x + "-close-quote");
                        yield return Click(view.GetButton("close_tab"));
                        Assert.IsFalse(shared.ConfirmButton.gameObject.activeInHierarchy && shared.ConfirmButton.interactable,
                            "Opening close options clears the prior frozen quote.");
                        Assert.IsNotNull(seat.FireDomain?.Main, "Cancelling close keeps the domain.");
                        Assert.AreEqual(mana - 1000, actor.CurrentSpirit, "Close preview/cancel never spends MP.");
                        yield return D12UnifiedHudPlayModeTests.CheckFullLoadoutLayout(size.x);
                        yield return EndAtZeroAp(encounter, actor);
                    }
                    yield return Capture(size.x + "-entry-" + entry);
                    var controller = Find<AdventureController>();
                    controller.RequestReturn();
                    Assert.IsTrue(controller.IsWithdrawalConfirmationOpen);
                    controller.ConfirmWithdrawal();
                    yield return null;
                    Assert.IsTrue(view.GetButton("retry").gameObject.activeInHierarchy);
                    Assert.IsNull(encounter.CurrentSession);
                    Assert.AreEqual(playerJson, JsonUtility.ToJson(runtime.CaptureSave().player));
                    Assert.AreEqual(cultivationJson, JsonUtility.ToJson(runtime.CaptureSave().cultivation));
                }
                // Old schema 6 ordinary saves lack the optional trial markers and retain equipment/spells.
                yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu); yield return null;
                creation = Find<CharacterCreationController>(); creation.Open();
                Assert.IsFalse(creation.IsJindanTrial);
                Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
                creation.Submit("ordinary" + size.x, "普通修士");
                yield return SceneReady(GameplaySceneNames.World);
                var normal = runtime.CaptureSave();
                Assert.IsTrue(string.IsNullOrEmpty(normal.jindanDemoPresetId));
                string legacy = GameSaveSerializer.Serialize(normal)
                    .Replace(",\"jindanDemoPresetId\":\"\"", "").Replace(",\"jindanDemoContentVersion\":\"\"", "");
                runtime.RestoreSaveJson(legacy, Asset<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset"));
                Assert.AreEqual("练气", runtime.Player.Progression.RealmStage);
                Assert.AreEqual("item_iron_sword", runtime.Player.MainEquipmentItemId);
                Assert.Contains("spell_feixingzhi", runtime.Player.AbilityLoadout.EquippedSpells.ToArray());
                File.AppendAllText(Path.Combine(output, "proof.txt"), size + " PASS: create/load/player+guardian/feixing/deploy/reentry/ordinary schema6\n");
            }
        }

        private IEnumerator CastFlyingStarAtEmptyCell(EncounterCoordinator encounter, CombatantSnapshot actor, string entry)
        {
            int ap = actor.ActionPointsRemainingUnits, mp = actor.CurrentSpirit;
            yield return Click(Find<CombatActionBarView>().ArtButton);
            var input = Find<CombatCommandInput>();
            Assert.AreEqual("spell_feixingzhi", input.Candidate.ProfileId);
            var art = Find<CombatHudPresenter>().Snapshot.ArtActions.Single();
            var movement = Find<CombatMovementView>();
            var surface = Find<CombatBattlefieldSurface>();
            var targeting = Find<CombatTargetInput>();
            foreach (var cell in art.CastableCells)
            {
                if (targeting.FindUnitAtCell(cell) != null) continue;
                Vector3 center = CombatBattlefieldSurface.Horizontal(cell.Q, cell.R);
                if (!surface.TryHeight(center, out center.y)) continue;
                Vector3 point = Camera.main.WorldToScreenPoint(center);
                if (point.z <= 0 || !Camera.main.pixelRect.Contains(point) ||
                    !movement.TryPickCell(point, out var picked) || !picked.Equals(cell) ||
                    targeting.TryPickBodyPoint(point, out _)) continue;
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point);
                Assert.AreEqual(cell, input.Candidate.TargetCell);
                Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
                Assert.IsEmpty(input.Preview.AttackArea.Targets);
                Assert.AreEqual(50000, input.Preview.ActionPointCostUnits);
                Assert.AreEqual(10, input.Preview.SpiritCost);
                Assert.AreEqual(ap, actor.ActionPointsRemainingUnits);
                Assert.AreEqual(mp, actor.CurrentSpirit);
                yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
                var shared = Find<CombatActionPreviewView>();
                AssertPrice(50000, 10);
                Assert.IsTrue(shared.ConfirmButton.interactable);
                yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
                Assert.AreEqual(ap, actor.ActionPointsRemainingUnits, "A second board click must not commit.");
                Assert.AreEqual(mp, actor.CurrentSpirit);
                if (entry == "新建")
                {
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Right));
                    yield return null;
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
                    yield return null;
                }
                else yield return Click(shared.CancelButton);
                Assert.IsTrue(input.IsSelectingAttack, "Cancel the locked tile before leaving the spell.");
                Assert.AreEqual(ap, actor.ActionPointsRemainingUnits);
                Assert.AreEqual(mp, actor.CurrentSpirit);
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point, click: true);
                AssertPrice(50000, 10);
                yield return Capture(Screen.width + "-star-locked");
                yield return Click(shared.ConfirmButton);
                yield return CommandReady(encounter);
                Assert.IsFalse(input.IsSelectingAttack);
                Assert.AreEqual(ap - 50000, actor.ActionPointsRemainingUnits);
                Assert.AreEqual(mp - 10, actor.CurrentSpirit);
                TestContext.WriteLine($"{entry}·飞星指 空格({cell.Q},{cell.R}) AP {ap / 1000}→{actor.ActionPointsRemainingUnits / 1000} MP {mp}→{actor.CurrentSpirit}");
                yield break;
            }
            Assert.Fail("飞星指施放范围内未找到可由鼠标点中的空格。");
        }

        private IEnumerator MoveToMarkedCenter(EncounterCoordinator encounter, CombatantSnapshot actor, CombatUnitPresentationHex center)
        {
            var origin = actor.Position;
            int ap = actor.ActionPointsRemainingUnits, mp = actor.CurrentSpirit;
            yield return Click(Find<CombatActionBarView>().MoveButton);
            Assert.IsNotNull(GameObject.Find("FireCenterLabel"), "Keep the destination visible while choosing the approach.");
            var option = Find<CombatHudPresenter>().Snapshot.Movement.Options.Single(o => o.Destination.Equals(center));
            Assert.IsTrue(TryGroundPoint(option.Destination, out var point), "The marked center must be pickable.");
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point, click: true);
            AssertPrice(option.Cost, 0);
            Assert.AreEqual(origin, actor.Position);
            Assert.AreEqual(ap, actor.ActionPointsRemainingUnits);
            yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
            yield return CommandReady(encounter);
            Assert.AreEqual(new TianZhang.Spatial.HexCoord(center.Q, center.R), actor.Position);
            Assert.AreEqual(ap - option.Cost, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(mp, actor.CurrentSpirit);
        }

        private IEnumerator ConfirmBasicAttack(EncounterCoordinator encounter, CombatantSnapshot actor)
        {
            int ap = actor.ActionPointsRemainingUnits, mp = actor.CurrentSpirit;
            var input = Find<CombatCommandInput>();
            for (int direction = 0; direction < 6; direction++)
            {
                var adjacent = actor.Position.Step(direction);
                var cell = new CombatUnitPresentationHex(adjacent.Q, adjacent.R);
                if (!TryGroundPoint(cell, out var point)) continue;
                yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point);
                if (input.Candidate?.TargetCell?.Equals(cell) != true || input.Preview?.CanConfirm != true) continue;
                int cost = input.Preview.ActionPointCostUnits;
                Assert.Greater(cost, 0);
                yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
                Assert.AreEqual(ap, actor.ActionPointsRemainingUnits);
                AssertPrice(cost, 0);
                yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
                yield return CommandReady(encounter);
                Assert.AreEqual(ap - cost, actor.ActionPointsRemainingUnits);
                Assert.AreEqual(mp, actor.CurrentSpirit);
                yield break;
            }
            Assert.Fail("No legal adjacent basic attack could be selected while the fire domain was active.");
        }

        private static bool TryGroundPoint(CombatUnitPresentationHex cell, out Vector2 point)
        {
            var movement = Find<CombatMovementView>();
            Vector3 center = CombatBattlefieldSurface.Horizontal(cell.Q, cell.R);
            point = default;
            return Find<CombatBattlefieldSurface>().TryHeight(center, out center.y) &&
                GuanzhongMovementPlayModeTests.TryFindCellPoint(movement, Camera.main, center, new Vector2Int(cell.Q, cell.R), out point);
        }

        private IEnumerator EndAtZeroAp(EncounterCoordinator encounter, CombatantSnapshot actor)
        {
            // Prepare the zero-AP boundary without advancing the turn. The player must end it through the HUD.
            typeof(CombatantSnapshot).GetMethod("SpendActionPoints", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(actor, new object[] { actor.ActionPointsRemainingUnits });
            D12UnifiedHudPlayModeTests.Refresh(encounter);
            yield return null;
            var session = encounter.CurrentSession;
            long turn = session.TurnSequence;
            yield return Click(Find<CombatActionBarView>().WaitButton);
            Assert.AreEqual(0, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(turn, session.TurnSequence);
            yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
            for (float end = Time.realtimeSinceStartup + 30; session.TurnSequence == turn && Time.realtimeSinceStartup < end;)
                yield return null;
            Assert.Greater(session.TurnSequence, turn, "The zero-AP player ended their turn.");
            yield return CommandReady(encounter);
        }

        internal static void AssertPrice(int ap, int mp)
        {
            string price = GameObject.Find("CompactQuote").GetComponent<Text>().text;
            StringAssert.Contains(CombatActionPreviewView.FormatActionPoints(ap) + " AP", price);
            StringAssert.Contains(mp + " MP", price);
        }

        private static IEnumerator CommandReady(EncounterCoordinator encounter)
        {
            for (float end = Time.realtimeSinceStartup + 30; !encounter.AcceptsPlayerCommand && Time.realtimeSinceStartup < end;)
                yield return null;
            Assert.IsTrue(encounter.AcceptsPlayerCommand);
            yield return null;
        }

        private IEnumerator Click(Button button)
        {
            yield return null; // Let LateUpdate publish the newly returned command authority to the HUD.
            Canvas.ForceUpdateCanvases(); Assert.IsNotNull(button);
            var encounter = Find<EncounterCoordinator>();
            File.AppendAllText(Path.Combine(output, "click-state.txt"), button.name + " active=" + button.gameObject.activeInHierarchy +
                " enabled=" + button.interactable + " authority=" + (encounter?.AcceptsPlayerCommand == true) + "\n");
            Assert.IsTrue(button.interactable && button.gameObject.activeInHierarchy, button.name);
            var rect = (RectTransform)button.transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Assert.That(point.x, Is.InRange(0f, (float)Screen.width));
            Assert.That(point.y, Is.InRange(0f, (float)Screen.height));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, button.name + " raycast");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private IEnumerator Capture(string name)
        { yield return new WaitForEndOfFrame(); D12UnifiedHudPlayModeTests.CaptureLayout(name); ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); yield return null; }
        private static IEnumerator SceneReady(string name)
        { for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null; yield return null; yield return null; Assert.AreEqual(name, SceneManager.GetActiveScene().name); }
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (mouse != null)
            {
                InputSystem.RemoveDevice(mouse); mouse = null;
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            }
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return null;
        }
    }
}
