using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.Spatial;
using TianZhang.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongCombatEquipmentPlayModeTests
    {
        private const string Attack = "spell_guanshiji", Recovery = "spell_julingshu";
        private string saveDirectory;
        private Mouse mouse;
        private InputSettings.BackgroundBehavior background;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        private EncounterCoordinator encounter;
        private CombatSession session;
        private AdventureSpawnSet spawned;
        private CombatHudPresenter hud;
        private CombatActionBarView bar;
        private CombatCommandInput input;
        private CombatActionPreviewView preview;

        [UnitySetUp]
        public IEnumerator EnterFormalAdventure()
        {
            background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            mouse = InputSystem.AddDevice<Mouse>();
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-Equipment-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saveDirectory));
            CharacterCreationController creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Draft.Innate = new InnateAttributeSet(15, 3, 6, 3, 3);
            creation.Submit("formal-swap", "战斗换装验证");
            yield return SceneReady(GameplaySceneNames.World);
            var runtime = GameBootstrap.RequireRuntime();
            Assert.AreEqual("练气", runtime.Player.Progression.RealmStage);
            Assert.AreEqual("item_iron_sword", runtime.Player.MainEquipmentItemId);
            AssertInventory(runtime);
            Assert.AreEqual(3, runtime.GetItemQuantity("item_huoxue_dan"));
            var firstSave = new GameSaveSlotStore(saveDirectory).Read("formal-swap");
            Assert.IsTrue(firstSave.Succeeded);
            Assert.AreEqual("item_iron_sword", firstSave.Envelope.player.mainEquipmentItemId);
            Assert.AreEqual("player:initial:item_iron_sword", firstSave.Envelope.player.mainEquipmentInstanceId);
            foreach (string id in new[] { "item_iron_sword", "item_iron_blade" })
                Assert.AreEqual(1, firstSave.Envelope.inventory.Single(entry => entry.itemId == id).quantity);
            Assert.AreEqual(3, firstSave.Envelope.inventory.Single(entry => entry.itemId == "item_huoxue_dan").quantity);
            WorldMapController world = Find<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            SettlementController settlement = Find<SettlementController>();
            Find<BountyBoardView>().Show(Get<ContentCatalogData>(settlement, "contentCatalog"),
                "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            Find<BountyBoardView>().SubmitAccept("bounty_guanzhong_shijiahou");
        }

        private IEnumerator BeginEncounter(bool reserveRecovery, bool reserveAttack = false)
        {
            // Existing character-domain API and real production spells; no invented content or session replacement.
            if (reserveRecovery)
                Assert.IsTrue(GameBootstrap.RequireRuntime().Player.AbilityLoadout.UnequipSpell(Recovery));
            if (reserveAttack)
                Assert.IsTrue(GameBootstrap.RequireRuntime().Player.AbilityLoadout.UnequipSpell(Attack));
            // Adventure takes its immutable character input on entry, after this legitimate loadout preparation.
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            AdventureController adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            adventure.SetEncounterRandomSource(new EncounterRandom());
            encounter = Find<EncounterCoordinator>();
            hud = Find<CombatHudPresenter>();
            bar = Find<CombatActionBarView>();
            input = Find<CombatCommandInput>();
            preview = Find<CombatActionPreviewView>();
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            session = Get<CombatSession>(encounter, "session");
            spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            yield return Ready();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            InputSystem.settings.backgroundBehavior = background;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
#endif
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            if (Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest] public IEnumerator SwordRoundTripWithNormalCombat() { yield return RoundTrip(false); }
        [UnityTest] public IEnumerator BladeRoundTripWithPassiveCombat() { yield return RoundTrip(true); }

        [UnityTest] public IEnumerator AoeSwordSelectsPreviewsAndConfirmsFormalTwoEnemyQuote() => FormalAreaAttack(false);
        [UnityTest] public IEnumerator AoeBladeSelectsPreviewsAndConfirmsFormalTwoEnemyQuote() => FormalAreaAttack(true);

        private IEnumerator FormalAreaAttack(bool blade)
        {
            yield return BeginEncounter(false);
            Assert.AreEqual(2, spawned.Enemies.Count);
            Assert.That(Get<ICombatResolutionRandomSource>(encounter, "combatRandomSource"), Is.TypeOf<SystemCombatResolutionRandomSource>());
            if (blade)
            {
                bar.SwapEquipmentButton.onClick.Invoke();
                input.ConfirmCandidate(); yield return Ready();
            }
            string profileId = blade ? "basic_iron_blade" : "basic_iron_sword";
            Assert.AreEqual(profileId, spawned.Player.BasicAttackProfileId);
            session.TryGetProfile(profileId, out var profile);
            // Approach the interior before contact instead of spending all AP chasing one enemy along the edge.
            var approach = hud.Snapshot.Movement.Options.Single(move => move.Destination.Q == 1 && move.Destination.R == 1);
            Assert.IsTrue(encounter.PreviewAction(approach.Candidate).CanConfirm);
            encounter.ConfirmAction(approach.Candidate); yield return Ready();
            encounter.RequestEndTurn("player"); yield return Ready();
            int selectedDirection = -1;
            for (int step = 0; step < 12 && selectedDirection < 0; step++)
            {
                yield return Ready();
                Debug.Log("[AOE route] " + profileId + "; step=" + step + "; " + AreaState());
                bar.BasicAttackButton.onClick.Invoke();
                Assert.AreEqual(hud.Snapshot.FindPreferredAction(CombatActionKind.BasicAttack).CanConfirm, input.IsSelectingAttack);
                Assert.IsFalse(preview.ConfirmButton.interactable);
                for (int direction = 0; input.IsSelectingAttack && direction < 6; direction++)
                {
                    string before = AreaState();
                    yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session, direction);
                    if (input.Preview.AttackArea == null) { Assert.IsFalse(input.Preview.CanConfirm); continue; }
                    AssertAreaMatchesAuthority(direction);
                    Debug.Log("[AOE direction] d=" + direction + "; targets=" + input.Preview.AttackArea.Targets.Count +
                        "; confirm=" + input.Preview.CanConfirm + "; reason=" + input.Preview.Reason);
                    Assert.AreEqual(before, AreaState(), "Selecting/previews cannot apply an attack.");
                    if (input.Preview.CanConfirm && input.Preview.AttackArea.Targets.Count == 2)
                    { selectedDirection = direction; break; }
                }
                if (selectedDirection >= 0) break;
                preview.CancelButton.onClick.Invoke();
                var option = hud.Snapshot.Movement.Options.Select(move => new
                {
                    Move = move,
                    Targets = Enumerable.Range(0, 6).Max(direction => CombatSession.QueryAttackArea(
                        new CombatantSnapshot("player", CombatTeam.Player, new HexCoord(move.Destination.Q, move.Destination.R),
                            1, 1, 1, 0, 0, 0, 0, 100000, 1000), profile, direction,
                        session.Combatants.All, session.SpatialQuery).TargetIds.Count),
                    Distance = spawned.Enemies.Where(enemy => enemy.IsAlive).Sum(enemy =>
                        new HexCoord(move.Destination.Q, move.Destination.R).Distance(enemy.Position)),
                }).Where(value => value.Targets == 2 && value.Move.Cost + profile.ActionPointCostUnits <= spawned.Player.ActionPointsRemainingUnits)
                    .OrderBy(value => value.Move.Cost).ThenBy(value => value.Distance).FirstOrDefault();
                if (option != null)
                {
                    Debug.Log("[AOE move] destination=" + option.Move.Destination.Q + "," + option.Move.Destination.R +
                        "; cost=" + option.Move.Cost + "; predictedTargets=" + option.Targets);
                    long turnBeforeMove = session.TurnSequence;
                    Assert.IsTrue(encounter.PreviewAction(option.Move.Candidate).CanConfirm);
                    encounter.ConfirmAction(option.Move.Candidate); yield return Ready();
                    if (session.TurnSequence != turnBeforeMove ||
                        option.Targets == 2 && spawned.Player.ActionPointsRemainingUnits >= profile.ActionPointCostUnits) continue;
                }
                encounter.RequestEndTurn("player");
            }
            Assert.GreaterOrEqual(selectedDirection, 0, "Real movement/AI must form two-target coverage; do not replace enemies or freeze AI.");
            foreach (int width in new[] { 1920, 1366 })
            {
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.AreEqual(width, Screen.width); Assert.AreEqual(width == 1920 ? 1080 : 768, Screen.height);
                yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session, selectedDirection);
                yield return CaptureArea((blade ? "blade" : "sword") + "-two-targets-" + width);
                for (int direction = 0; direction < 6; direction++)
                {
                    yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session, direction);
                    if (input.Preview.AttackArea == null) { Assert.IsFalse(input.Preview.CanConfirm); continue; }
                    AssertAreaMatchesAuthority(direction);
                    if (input.Preview.AttackArea.Targets.Count != 0) continue;
                    string before = AreaState();
                    yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
                    Assert.AreEqual(before, AreaState());
                    Assert.IsFalse(preview.ConfirmButton.interactable);
                    yield return CaptureArea((blade ? "blade" : "sword") + "-empty-" + width);
                    break;
                }
                yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session, selectedDirection);
                string cancelled = AreaState(); preview.CancelButton.onClick.Invoke();
                Assert.AreEqual(cancelled, AreaState());
                Assert.IsNull(input.Candidate);
                Assert.IsFalse(Find<CombatMovementView>().Highlights.Values.Any(ring => ring.gameObject.activeSelf));
                bar.BasicAttackButton.onClick.Invoke();
                Assert.IsTrue(input.IsSelectingAttack);
                Assert.IsFalse(preview.ConfirmButton.interactable);
                yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session, selectedDirection);
            }
            var quote = input.Preview;
            var old = input.Candidate;
            var health = spawned.Enemies.ToDictionary(enemy => enemy.Id, enemy => enemy.CurrentHealth);
            int ap = spawned.Player.ActionPointsRemainingUnits;
            int uses = BasicUseCount();
            int facing = spawned.Player.Facing;
            yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
            input.ConfirmCandidate();
            Assert.AreEqual(ap - quote.ActionPointCostUnits, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(uses + 1, BasicUseCount());
            Assert.AreEqual(facing, spawned.Player.Facing);
            Assert.IsNull(input.Candidate);
            string after = AreaState(); encounter.ConfirmAction(old); Assert.AreEqual(after, AreaState());
            Debug.Log("[AOE applied] " + profileId + "; origin=" + quote.AttackArea.Origin.Q + "," + quote.AttackArea.Origin.R +
                "; direction=" + selectedDirection + "; AP=" + ap + "->" + spawned.Player.ActionPointsRemainingUnits +
                "; uses=" + uses + "->" + (uses + 1) + "; HP=" + string.Join(";", spawned.Enemies.Select(enemy =>
                    enemy.Id + ":" + health[enemy.Id] + "->" + enemy.CurrentHealth)));
            yield return Ready();
        }

        private void AssertAreaMatchesAuthority(int direction)
        {
            var domain = session.QueryAttackArea("player", spawned.Player.BasicAttackProfileId, direction);
            var area = input.Preview.AttackArea;
            Assert.AreEqual(domain.Direction, area.Direction);
            Assert.AreEqual(domain.Origin.Q, area.Origin.Q); Assert.AreEqual(domain.Origin.R, area.Origin.R);
            Assert.AreEqual(2, area.Cells.Count);
            for (int index = 0; index < 2; index++)
            {
                Assert.AreEqual(domain.Cells[index].Coord.Q, area.Cells[index].Position.Q);
                Assert.AreEqual(domain.Cells[index].Coord.R, area.Cells[index].Position.R);
                Assert.AreEqual(domain.Cells[index].IsLegal, area.Cells[index].IsLegal);
            }
            CollectionAssert.AreEqual(domain.TargetIds, area.Targets.Select(target => target.Id));
            Assert.IsTrue(string.IsNullOrEmpty(input.Candidate.TargetId));
        }

        private string AreaState() => session.StateVersion + ":" + session.TurnSequence + ":" +
            string.Join(";", session.Combatants.All.Select(unit => unit.Id + ":" + unit.Position + ":" + unit.Facing +
                ":" + unit.CurrentHealth + ":" + unit.CurrentSpirit + ":" + unit.ActionPointsRemainingUnits)) + ":uses=" + BasicUseCount();

        private int BasicUseCount() => new CombatCommandService().Preview(session,
            new CombatCommand(CombatCommandKind.BasicAttack, "player", profileId: spawned.Player.BasicAttackProfileId,
                attackDirection: spawned.Player.Facing)).CommittedUseCount;

        private IEnumerator CaptureArea(string label)
        {
#if UNITY_EDITOR
            for (float deadline = Time.realtimeSinceStartup + 120f; UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.IsFalse(UnityEditor.ShaderUtil.anythingCompiling, "Shader compilation must finish before visual evidence.");
#endif
            for (int frame = 0; frame < 8; frame++) yield return null;
            yield return null; Canvas.ForceUpdateCanvases();
            AssertPreviewBounds(preview, label);
            var text = Get<Text>(preview, "summary");
            var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
            Assert.GreaterOrEqual(corners[0].x, 0); Assert.GreaterOrEqual(corners[0].y, 0);
            Assert.LessOrEqual(corners[2].x, Screen.width); Assert.LessOrEqual(corners[2].y, Screen.height);
            var move = Find<CombatMovementView>();
            foreach (var cell in input.Preview.AttackArea.Cells)
            {
                var key = new Vector2Int(cell.Position.Q, cell.Position.R);
                if (!move.Highlights.TryGetValue(key, out var ring)) continue;
                Assert.IsTrue(ring.gameObject.activeInHierarchy);
                Vector3 center = Enumerable.Range(0, 6).Select(ring.GetPosition).Aggregate(Vector3.zero, (sum, point) => sum + point) / 6f;
                Debug.Log("[AOE owner] AdventureRoot/" + ring.name + "; writer=CombatMovementView.PresentAttackArea; " +
                    "cell=" + key + "; center=" + center + "; camera=" + Camera.main.transform.position +
                    "; screen=" + Camera.main.WorldToScreenPoint(center) + "; legal=" + cell.IsLegal);
            }
            Assert.IsFalse(preview.ConfirmButton.gameObject.activeSelf);
            AssertVisibleText(preview.CancelButton.GetComponentInChildren<Text>(), label + "/cancel");
            string directory = Path.Combine(Application.dataPath, "../Logs/aoe-input"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, label + ".png");
            File.WriteAllText(Path.ChangeExtension(path, ".txt"), text.text + "\nstate=" + AreaState() +
                "\nheight=" + text.preferredHeight + "; viewport=" + text.GetComponentInParent<ScrollRect>().viewport.rect.height +
                "; corners=" + string.Join("/", corners.Select(corner => corner.ToString("F2"))));
            ScreenCapture.CaptureScreenshot(path);
            for (int frame = 0; frame < 8; frame++) yield return null;
            var png = new Texture2D(2, 2);
            Assert.IsTrue(png.LoadImage(File.ReadAllBytes(path)));
            Assert.AreEqual(Screen.width, png.width); Assert.AreEqual(Screen.height, png.height);
            Object.Destroy(png);
        }

        [UnityTest] public IEnumerator IsolatedMissingWeaponRejectsWithoutChangingEquipmentOrResources()
        {
            var runtime = GameBootstrap.RequireRuntime();
            // Isolated missing-stock boundary; successful player chains never replace inventory.
            var inventory = Get<InventoryStore>(runtime, "inventory");
            inventory.Restore(new InventoryStoreSnapshot(inventory.Capture().Entries
                .Where(entry => entry.ItemId != "item_iron_blade").ToArray()));
            yield return BeginEncounter(false);
            string saved = runtime.CaptureSaveJson();
            int ap = spawned.Player.ActionPointsRemainingUnits;
            long version = runtime.InventoryVersion;
            foreach (int width in new[] { 1920, 1366 })
            {
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Assert.IsFalse(bar.SwapEquipmentButton.interactable);
                bar.SwapEquipmentButton.onClick.Invoke();
                Assert.IsNull(input.Candidate);
                var rejected = encounter.PreviewAction(hud.Snapshot.FindAction(CombatActionKind.SwapEquipment).Candidate);
                Assert.IsFalse(rejected.CanConfirm);
                Assert.IsFalse(rejected.EquipmentOwned);
                StringAssert.Contains("未拥有该武器", rejected.Reason);
                encounter.ConfirmAction(rejected.Candidate);
                AssertVisibleText(bar.SwapEquipmentButton.GetComponentInChildren<Text>(), "missing-equipment-button");
                string directory = Path.Combine(Application.dataPath, "../Logs/equipment"); Directory.CreateDirectory(directory);
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "missing-" + width + ".png"));
                for (int frame = 0; frame < 3; frame++) yield return null;
                preview.ConfirmButton.onClick.Invoke();
                Assert.AreEqual(saved, runtime.CaptureSaveJson());
                Assert.AreEqual(ap, spawned.Player.ActionPointsRemainingUnits);
                Assert.AreEqual(version, runtime.InventoryVersion);
                preview.CancelButton.onClick.Invoke();
            }
        }

        private IEnumerator RoundTrip(bool defeat)
        {
            var runtime = GameBootstrap.RequireRuntime();
            Assert.AreEqual("item_iron_sword", runtime.Player.MainEquipmentItemId);
            AssertInventory(runtime);
            yield return BeginEncounter(false);
            long inventoryVersion = runtime.InventoryVersion, turn = session.TurnSequence;
            int mp = spawned.Player.CurrentSpirit, tick = session.TurnScheduler.CurrentTick;
            var owner = runtime.Player;
            Assert.IsNotNull(bar.SwapEquipmentButton);
            bar.SwapEquipmentButton.onClick.Invoke();
            Assert.AreEqual(CombatActionKind.SwapEquipment, input.Candidate.Kind);
            Assert.AreEqual(25000, input.Preview.ActionPointCostUnits);
            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            var cancelled = input.Candidate;
            preview.CancelButton.onClick.Invoke();
            Assert.AreEqual(100000, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual("item_iron_sword", owner.MainEquipmentItemId);
            foreach (int width in new[] { 1920, 1366 })
            {
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                bar.SwapEquipmentButton.onClick.Invoke();
                Canvas.ForceUpdateCanvases();
                Assert.IsTrue(input.Preview.EquipmentOwned);
                Assert.AreEqual("铁剑", input.Preview.CurrentEquipmentName);
                StringAssert.Contains("铁剑", Get<Text>(preview, "summary").text);
                StringAssert.Contains("铁刀", Get<Text>(preview, "summary").text);
                AssertVisibleText(bar.SwapEquipmentButton.GetComponentInChildren<Text>(), "equipment-button");
                AssertPreviewBounds(preview, "equipment-preview");
                string directory = Path.Combine(Application.dataPath, "../Logs/equipment"); Directory.CreateDirectory(directory);
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "preview-" + width + (defeat ? "-blade" : "-sword") + ".png"));
                for (int frame = 0; frame < 3; frame++) yield return null;
                preview.CancelButton.onClick.Invoke();
            }
            bar.SwapEquipmentButton.onClick.Invoke();
            var old = input.Candidate;
            input.ConfirmCandidate(); yield return Ready();
            Assert.AreEqual(75000, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual("basic_iron_blade", spawned.Player.BasicAttackProfileId);
            Assert.AreEqual("item_iron_blade", owner.MainEquipmentItemId);
            Assert.AreSame(owner, runtime.Player); Assert.AreEqual(turn, session.TurnSequence);
            Assert.AreEqual(tick, session.TurnScheduler.CurrentTick); Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
            Assert.AreEqual(inventoryVersion, runtime.InventoryVersion);
            encounter.ConfirmAction(old);
            Assert.AreEqual(75000, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(50000, hud.Snapshot.FindAction(CombatActionKind.BasicAttack).ActionPointCostUnits);
            if (!defeat)
            {
                bar.SwapEquipmentButton.onClick.Invoke(); input.ConfirmCandidate(); yield return Ready();
                Assert.AreEqual(50000, spawned.Player.ActionPointsRemainingUnits);
                Assert.AreEqual("basic_iron_sword", spawned.Player.BasicAttackProfileId);
                Assert.AreEqual(40000, hud.Snapshot.FindAction(CombatActionKind.BasicAttack).ActionPointCostUnits);
            }
            string expected = defeat ? "item_iron_blade" : "item_iron_sword";
            yield return FinishNaturalCombat(encounter, session, spawned, defeat);
            Assert.AreEqual(expected, runtime.Player.MainEquipmentItemId); AssertInventory(runtime);
            string json = runtime.CaptureSaveJson();
            Find<SettlementController>().SaveAndReturnToMenu(); yield return SceneReady(GameplaySceneNames.StartMenu);
            Find<StartMenuController>().LoadPlayer("formal-swap"); yield return SceneReady(GameplaySceneNames.Settlement);
            runtime = GameBootstrap.RequireRuntime(); Assert.AreEqual(json, runtime.CaptureSaveJson()); AssertInventory(runtime);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            var next = Get<AdventureSpawnSet>(Find<EncounterCoordinator>(), "spawned");
            Assert.AreEqual(expected.Replace("item_", "basic_"), next.Player.BasicAttackProfileId);
            AssertInventory(runtime);
        }
        private static void AssertInventory(GameRuntime runtime)
        {
            foreach (string id in new[] { "item_iron_sword", "item_iron_blade" })
            {
                Assert.AreEqual(1, runtime.GetItemQuantity(id));
                Assert.IsTrue(runtime.OwnsEquipmentInstance("player", id, "player:initial:" + id));
            }
        }

        public static IEnumerator FinishNaturalCombat(EncounterCoordinator encounter, CombatSession session,
            AdventureSpawnSet units, bool passive)
        {
            var bar = Find<CombatActionBarView>();
            var preview = Find<CombatActionPreviewView>();
            var hud = Find<CombatHudPresenter>();
            var input = Find<CombatCommandInput>();
            for (float deadline = Time.realtimeSinceStartup + 180f;
                Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name == GameplaySceneNames.Adventure;)
            {
                if (session.CurrentActorId == "player" && session.TurnPhase == CombatTurnPhase.Active && !encounter.IsPresenting)
                {
                    bool canAttack = false;
                    if (!passive && bar.BasicAttackButton.interactable)
                    {
                        bar.BasicAttackButton.onClick.Invoke();
                        yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session);
                        for (int direction = 0; !input.Preview.CanConfirm && direction < 6; direction++)
                            yield return GuanzhongBasicAttackPlayModeTests.PointBasicAttack(session, direction);
                        canAttack = input.Preview.CanConfirm;
                    }
                    var enemy = units.Enemies.FirstOrDefault(unit => unit.IsAlive);
                    if (canAttack)
                    {
                        yield return GuanzhongBasicAttackPlayModeTests.ClickPointedAttack();
                    }
                    else if (!passive && enemy != null && units.Player.Position.Distance(enemy.Position) > 1)
                    {
                        input.CancelCandidate();
                        var move = hud.Snapshot.Movement?.Options.OrderBy(option =>
                            Math.Max(Math.Abs(option.Destination.Q - enemy.Position.Q),
                                Math.Max(Math.Abs(option.Destination.R - enemy.Position.R),
                                    Math.Abs(option.Destination.Q + option.Destination.R - enemy.Position.Q - enemy.Position.R))))
                            .ThenBy(option => option.Cost).FirstOrDefault();
                        if (move != null)
                        {
                            Assert.IsTrue(encounter.PreviewAction(move.Candidate).CanConfirm);
                            encounter.ConfirmAction(move.Candidate);
                        }
                        else encounter.RequestEndTurn("player");
                    }
                    else encounter.RequestEndTurn("player");
                }
                yield return null;
            }
            Assert.AreEqual(GameplaySceneNames.Settlement, SceneManager.GetActiveScene().name,
                "Normal combat must end without modified health, rolls, profiles or outcome. HP=" +
                units.Player.CurrentHealth + "/" + units.Enemy.CurrentHealth);
            Assert.IsTrue(units.Player.CurrentHealth == 0 || units.Enemy.CurrentHealth == 0);
            Debug.Log("[EquipmentItem] natural outcome=" + (units.Player.CurrentHealth == 0 ? "defeat" : "victory"));
        }

        public static void AssertVisibleText(Text text, string evidence)
        {
            Canvas.ForceUpdateCanvases();
            var rect = text.rectTransform;
            Debug.Log("[EquipmentItemUI] " + evidence + "; screen=" + Screen.width + "x" + Screen.height +
                "; object=" + rect.name + "; parent=" + rect.parent.name + "; rect=" + rect.rect.size +
                "; preferred=" + text.preferredWidth + "/" + text.preferredHeight + "; text=" + text.text);
            Assert.LessOrEqual(text.preferredWidth, rect.rect.width + .1f, evidence);
            Assert.LessOrEqual(text.preferredHeight, rect.rect.height + .1f, evidence);
        }

        public static void AssertPreviewBounds(CombatActionPreviewView view, string evidence)
        {
            Canvas.ForceUpdateCanvases();
            var text = Get<Text>(view, "summary");
            var scroll = text.GetComponentInParent<ScrollRect>();
            Assert.IsNotNull(scroll);
            var viewport = scroll.viewport;
            var corners = new Vector3[4]; viewport.GetWorldCorners(corners);
            var textCorners = new Vector3[4]; text.rectTransform.GetWorldCorners(textCorners);
            var visibleButton = view.ConfirmButton.gameObject.activeSelf ? view.ConfirmButton : view.CancelButton;
            Assert.IsTrue(visibleButton.gameObject.activeInHierarchy);
            var buttons = new Vector3[4]; visibleButton.GetComponent<RectTransform>().GetWorldCorners(buttons);
            Debug.Log("[EquipmentItemUI] " + evidence + "; screen=" + Screen.width + "x" + Screen.height +
                "; preferredHeight=" + text.preferredHeight + "; viewport=" + viewport.rect.size +
                "; viewportMin=" + corners[0] + "; viewportMax=" + corners[2] +
                "; textMin=" + textCorners[0] + "; textMax=" + textCorners[2] + "; buttonMax=" + buttons[2]);
            Assert.LessOrEqual(text.preferredHeight, viewport.rect.height + .1f);
            Assert.GreaterOrEqual(textCorners[0].y, corners[0].y - 1f);
            Assert.LessOrEqual(textCorners[2].y, corners[2].y + 1f);
            Assert.LessOrEqual(buttons[2].y, corners[0].y);
        }

        private IEnumerator Ready()
        {
            for (float deadline = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < deadline &&
                (session.CurrentActorId != "player" || session.TurnPhase == CombatTurnPhase.Finished || encounter.IsPresenting);)
                yield return null;
            yield return null;
            Assert.That(session.CurrentActorId, Is.EqualTo("player"), "Formal state=" + session.StateVersion + "; units=" +
                string.Join(";", session.Combatants.All.Select(unit => unit.Id + ":" + unit.Position + ":HP" + unit.CurrentHealth +
                    ":AP" + unit.ActionPointsRemainingUnits)));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(encounter.IsPresenting, Is.False);
        }

        public static void SetGameSize(int width, int height)
        {
#if UNITY_EDITOR
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "Swap validation" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            var game = UnityEditor.EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, count - 1);
#else
            Screen.SetResolution(width, height, false);
#endif
        }

        private static IEnumerator SceneReady(string scene)
        {
            for (float deadline = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name != scene;)
                yield return null;
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);
            yield return null;
        }
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }

    }
}
