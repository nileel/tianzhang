using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed partial class D12ChallengeRoutesPlayModeTests
    {
        private static readonly int[] Widths = { 1366, 1920 };
        private const string Player = "player", Earth = "demo_20261024_earth", Water = "demo_20261024_water";
        private const string Yang = "demo_20261024_space_yang", Yin = "demo_20261024_seal_yin", Fire = "demo_20261024_fire";
        private const string Star = "spell_feixingzhi";

        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousInput;
        private Mouse mouse;
        private JindanDemoView view;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private CombatBattlefieldSurface surface;
        private string output, savedPlayer, savedCultivation, playerName;
        private Coroutine recording;
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();
        private CombatantSnapshot Actor(string id) => session.Combatants.All.Single(a => a.Id == id);
        private CombatantSnapshot Current => Actor(session.CurrentActorId);
        private CombatJindanSeatState Seat(string effect) => session.Jindan.Seats.Single(s => s.EffectId == effect);
        private bool ResultOpen => view?.GetButton("choose_challenge")?.gameObject.activeInHierarchy == true;
        private static CombatUnitPresentationHex Hex(HexCoord c) => new CombatUnitPresentationHex(c.Q, c.R);
        private string Panel => GameObject.Find("PreparationDetails")?.GetComponent<Text>().text ?? string.Empty;

        private IEnumerator StartChallenge(int width, string preset, string stage, bool acceptBounty = false)
        {
            output = Path.GetFullPath(Path.Combine("Logs/d12-challenges", TestContext.CurrentContext.Test.MethodName + "-" + preset + "-" + width));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "actions.txt"), "");
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>();
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width, width == 1366 ? 768 : 1080);
            var stale = Find<GameBootstrap>();
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu); yield return null;
            var bootstrap = GameBootstrap.RequireInstance();
            var folder = Path.Combine(Application.temporaryCachePath, "D12Route-" + Guid.NewGuid().ToString("N"));
            typeof(GameBootstrap).GetField("slotStore", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(bootstrap, new GameSaveSlotStore(folder));
            bool ordinary = preset == "ordinary" || preset == "legacy";
            playerName = preset == "legacy" ? "岑二" : "行舟";
            if (preset == "legacy")
            {
                Directory.CreateDirectory(folder);
                File.Copy(Path.Combine(Application.dataPath, "../../artifacts/guanzhong-playtest-20261119/jiangzuo-qa-save.json"),
                    Path.Combine(folder, "route.json"));
                Assert.IsTrue(Find<StartMenuSceneInstaller>().LoadPlayer("route").Succeeded);
            }
            else
            {
                yield return Named("NewPlayerButton");
                if (!ordinary) yield return Named("JindanStartButton");
                string origin = preset switch
                {
                    Earth => "loose", Water => "sect_servant", Yang => "minor_clan", Yin => "debt_exile",
                    "ordinary" => "minor_clan", _ => null
                };
                if (origin != null) yield return Named("Origin_origin_" + origin);
                GameObject.Find("SlotIdInput").GetComponent<InputField>().text = "route";
                GameObject.Find("CharacterNameInput").GetComponent<InputField>().text = playerName;
                yield return Named("CreatePlayerButton");
            }
            yield return Scene(GameplaySceneNames.World);
            var runtime = GameBootstrap.RequireRuntime();
            if (ordinary) Assert.That(runtime.JindanDemoPresetId, Is.Null.Or.Empty);
            else Assert.AreEqual(preset, runtime.JindanDemoPresetId);
            Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation();
            yield return Scene(GameplaySceneNames.Settlement);
            if (acceptBounty)
            {
                // Model an existing accepted order in a trial save, before entering the terrace.
                var catalog = UnityEngine.Resources.FindObjectsOfTypeAll<TianZhang.Content.ContentCatalogData>()
                    .Single(c => c.TryGetBounty("bounty_guanzhong_shijiahou", out _));
                Assert.IsTrue(runtime.Bounties.Accept(catalog, "bounty_guanzhong_shijiahou", "guanzhong_city").Succeeded);
            }
            Assert.IsTrue(bootstrap.SaveActiveSlot().Succeeded);
            savedPlayer = JsonUtility.ToJson(runtime.CaptureSave().player);
            savedCultivation = JsonUtility.ToJson(runtime.CaptureSave().cultivation);
            yield return Named("SettlementTerraceButton");
            yield return Scene(GameplaySceneNames.Adventure);
            view = Find<JindanDemoView>();
            Assert.IsTrue(view.GetButton("enter").gameObject.activeInHierarchy, "本人金丹开局直接选择遭遇");
            foreach (string key in new[] { "basic", "stage1", "stage2", "stage3" })
                Assert.IsTrue(view.GetButton(key).gameObject.activeInHierarchy && view.GetButton(key).IsInteractable(), key);
            yield return Press(stage);
            string title = stage switch { "stage1" => "登高", "stage2" => "水火", "stage3" => "关隘", _ => "基础演练" };
            for (int frame = 0; frame < 30 && !Panel.Contains(title); frame++) yield return null;
            StringAssert.Contains(title, Panel);
            StringAssert.Contains("出战角色  " + playerName + " · " + runtime.Player.Progression.RealmStage, Panel);
            StringAssert.Contains("遭遇  ", Panel);
            StringAssert.Contains("对手  ", Panel);
            StringAssert.Contains("敌方全灭", Panel);
            StringAssert.Contains("我方全灭", Panel);
            foreach (string key in new[] { "earth_toggle", "preset_next", "fire_side" })
                Assert.IsFalse(view.GetButton(key).gameObject.activeInHierarchy, key);
            Record("准备 " + Panel);
            if (Environment.GetEnvironmentVariable("TZ_D12_RECORD") == "1")
                recording = bootstrap.StartCoroutine(RecordFrames());
            yield return Capture("00-preparation");
            yield return Press("enter");
            yield return Entered();
        }

        private IEnumerator Entered()
        {
            encounter = Find<EncounterCoordinator>();
            session = encounter.CurrentSession;
            Assert.NotNull(session);
            Assert.AreEqual(CombatSessionOutcome.Ongoing, Find<AdventureController>().LastEncounterOutcome);
            Assert.AreEqual(playerName, GameBootstrap.RequireRuntime().Player.Identity.DisplayName);
            Assert.AreEqual(Player, Actor(Player).Id);
            surface = Find<CombatBattlefieldSurface>();
            float limit = Time.realtimeSinceStartup + 35;
            while ((ResultOpen || !encounter.AcceptsPlayerCommand) && Time.realtimeSinceStartup < limit) yield return null;
            Assert.IsTrue(!ResultOpen && encounter.AcceptsPlayerCommand, "新局首个可控行动");
            yield return null;
            StringAssert.Contains(encounter.ControlledActorId == Player ? "本人行动" : "同伴行动",
                GameObject.Find("CombatPlayerText").GetComponent<Text>().text);
            AssertVisibleRoleLabels();
            Record("入场 " + string.Join(", ", session.Combatants.All.Select(a => $"{a.Id}/{a.Team} {a.Position}")));
        }

        private void AssertVisibleRoleLabels()
        {
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            var expected = new List<string>();
            foreach (var actor in session.Combatants.All)
            {
                if (!actor.IsAlive || session.Visibility?.IsVisible(encounter.ControlledActorId, actor.Id) == false ||
                    !provider.TryGetHeadAnchor(actor.Id, out var anchor)) continue;
                var position = Camera.main.WorldToViewportPoint(anchor);
                if (position.z <= 0 || position.x < 0 || position.x > 1 || position.y < 0 || position.y > 1) continue;
                expected.Add(actor.Id == Player ? "你" : actor.Team == CombatTeam.Player ? "友" : "敌");
            }
            var visible = GameObject.Find("CombatUnitHealthBars").GetComponentsInChildren<Text>();
            CollectionAssert.AreEquivalent(expected, visible.Select(label => label.text));
            Assert.IsTrue(visible.All(label => !label.raycastTarget));
        }

        private static IEnumerator Scene(string name)
        {
            for (int i = 0; i < 400 && SceneManager.GetActiveScene().name != name; i++) yield return null;
            yield return null; yield return null;
            Assert.AreEqual(name, SceneManager.GetActiveScene().name);
        }

        private IEnumerator Ready()
        {
            float end = Time.realtimeSinceStartup + 50;
            while (!ResultOpen && !encounter.AcceptsPlayerCommand && session.Visibility?.IsPaused != true &&
                Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(ResultOpen || encounter.AcceptsPlayerCommand || session.Visibility?.IsPaused == true,
                "等待正式行动、结局或观察窗口超时");
            yield return null;
        }

        private IEnumerator Press(string key) => Click(view.GetButton(key), key);
        private static IEnumerator Named(string name) => Click(GameObject.Find(name)?.GetComponent<Button>(), name);
        private static IEnumerator Click(Button button, string label = null)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.NotNull(button, label);
            Assert.IsTrue(button.gameObject.activeInHierarchy && button.IsInteractable(), label ?? button.name);
            var rect = (RectTransform)button.transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.AreSame(button, hits.FirstOrDefault().gameObject?.GetComponentInParent<Button>(), label ?? button.name);
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point, true);
            yield return null; // Allow the presenter to project the action into the next rendered HUD.
        }

        private IEnumerator Pick(HexCoord cell)
        {
            var center = surface.Center(Hex(cell));
            var movement = Find<CombatMovementView>();
            if (!GuanzhongMovementPlayModeTests.TryFindCellPoint(movement, Camera.main, center,
                new Vector2Int(cell.Q, cell.R), out var point) || movement.UiOwnsPointer(point))
            {
                // Reframe an off-screen or HUD-covered tile at the same production zoom, as in the D9 movement proof.
                Camera.main.transform.position = center - Camera.main.transform.forward * 20;
                yield return null;
                Assert.IsTrue(GuanzhongMovementPlayModeTests.TryFindCellPoint(movement, Camera.main, center,
                    new Vector2Int(cell.Q, cell.R), out point), cell.ToString());
                Assert.IsFalse(movement.UiOwnsPointer(point), "棋盘落点被界面遮挡 " + cell);
            }
            yield return GuanzhongBasicAttackPlayModeTests.PointAttackAt(point, true);
        }

        private IEnumerator Pay(CombatCommandPreview quote, string label)
        {
            Assert.NotNull(quote, label);
            Assert.IsTrue(quote.IsLegal, label + ": " + quote.RejectionReason);
            var actor = Current;
            int ap = actor.ActionPointsRemainingUnits, mp = actor.CurrentSpirit;
            long turn = session.TurnSequence;
            var confirm = view.GetButton("confirm");
            for (int i = 0; i < 30 && (!confirm.gameObject.activeInHierarchy || !confirm.IsInteractable()); i++) yield return null;
            Assert.IsTrue(confirm.gameObject.activeInHierarchy && confirm.IsInteractable(),
                label + " 统一报价未显示，当前页 " + view.SelectedKey + "，状态 " + view.PersistentStatus);
            D12PlayerStartPlayModeTests.AssertPrice(quote.ActionPointCostUnits, quote.SpiritCost);
            yield return Press("confirm");
            if (session.TurnSequence == turn && !ResultOpen)
            {
                Assert.AreEqual(ap - quote.ActionPointCostUnits, actor.ActionPointsRemainingUnits, label + " AP");
                Assert.AreEqual(mp - quote.SpiritCost, actor.CurrentSpirit, label + " MP");
            }
            Record($"{actor.Id} {label} 报价 {quote.ActionPointCostUnits / 1000f} AP/{quote.SpiritCost} MP, " +
                $"AP {ap / 1000f}→{actor.ActionPointsRemainingUnits / 1000f}, MP {mp}→{actor.CurrentSpirit}, 格 {actor.Position}");
            yield return Ready();
        }

        private IEnumerator Move(HexCoord cell)
        {
            if (Current.Position == cell) yield break;
            var actor = Current;
            var quote = Movement(cell);
            Assert.IsTrue(quote.IsLegal, $"移动 {actor.Id} {actor.Position}→{cell}: {quote.RejectionReason}");
            yield return Click(Find<CombatActionBarView>().MoveButton);
            yield return Pick(cell);
            yield return Pay(quote, "移动 " + cell);
            Assert.AreEqual(cell, actor.Position);
        }
        private CombatCommandPreview Movement(HexCoord cell) => encounter.PreviewJindan(
            new CombatCommand(CombatCommandKind.Move, session.CurrentActorId, destination: cell));

        private IEnumerator End()
        {
            Assert.IsFalse(ResultOpen, Panel);
            long turn = session.TurnSequence;
            RecordTurn("结束前");
            yield return Click(Find<CombatActionBarView>().WaitButton);
            yield return Press("confirm");
            yield return Ready();
            RecordTurn("结束后");
            Assert.IsTrue(ResultOpen || session.TurnSequence > turn || session.Visibility?.IsPaused == true);
        }

        private IEnumerator UntilActor(string id)
        {
            for (int i = 0; i < 12 && !ResultOpen && session.CurrentActorId != id; i++) yield return End();
            Assert.IsFalse(ResultOpen, Panel);
            Assert.AreEqual(id, session.CurrentActorId);
        }

        private IEnumerator Use(string tab, string option, CombatCommand command, params HexCoord[] cells)
        {
            var quote = encounter.PreviewJindan(command);
            Assert.IsTrue(quote.IsLegal, option + ": " + quote.RejectionReason);
            yield return Press(tab); yield return Press(option);
            foreach (var cell in cells) yield return Pick(cell);
            yield return Pay(quote, command.ProfileId + "/" + option);
        }

        private CombatCommand Jindan(string effect, CombatJindanRequest request = null, bool close = false) =>
            new CombatCommand(close ? CombatCommandKind.CloseJindan : CombatCommandKind.JindanAbility,
                session.CurrentActorId, profileId: effect, jindan: request ?? new CombatJindanRequest(Seat(effect).Definition.SeatId));
        private IEnumerator Attack(string profile, HexCoord target)
        {
            var bar = Find<CombatActionBarView>();
            int index = Find<CombatHudPresenter>().Snapshot.ArtActions.ToList().FindIndex(a => a.ProfileId == profile);
            var button = bar.BasicAttackButton;
            if (profile != Current.BasicAttackProfileId)
            {
                Assert.That(index, Is.InRange(0, 4), "本人装配 " + profile);
                button = new[] { bar.ArtButton, bar.SecondArtButton, bar.ThirdArtButton, bar.FourthArtButton, bar.FifthArtButton }[index];
            }
            yield return Click(button);
            yield return Pick(target);
            var input = Find<CombatCommandInput>();
            Assert.IsTrue(input.IsTargetLocked && input.Preview?.CanConfirm == true, input.Preview?.Reason + " " + target);
            Assert.AreEqual(profile, input.Candidate.ProfileId);
            var preview = input.Preview;
            long turn = session.TurnSequence;
            var actor = Current;
            int ap = actor.ActionPointsRemainingUnits, mp = actor.CurrentSpirit;
            D12PlayerStartPlayModeTests.AssertPrice(preview.ActionPointCostUnits, preview.SpiritCost);
            yield return Press("confirm");
            Record($"{actor.Id} {profile} {target}, AP {ap / 1000f}→{actor.ActionPointsRemainingUnits / 1000f}, MP {mp}→{actor.CurrentSpirit}");
            if (!ResultOpen && session.TurnSequence == turn)
            {
                int expectedAp = CombatResultBuilder.GetOutcome(session) == CombatSessionOutcome.Ongoing
                    ? ap - preview.ActionPointCostUnits : 0;
                Assert.AreEqual(expectedAp, actor.ActionPointsRemainingUnits);
                Assert.AreEqual(mp - preview.SpiritCost, actor.CurrentSpirit);
            }
            yield return Ready();
        }

        private void AssertBattleContinues()
        {
            Assert.That(session.Combatants.HasLivingMembers(CombatTeam.Enemy), Is.True);
            Assert.That(session.Combatants.HasLivingMembers(CombatTeam.Player), Is.True);
            Assert.IsFalse(ResultOpen, Panel);
            Assert.AreSame(session, encounter.CurrentSession);
            Assert.IsTrue(encounter.AcceptsPlayerCommand, "应能继续操作我方角色");
            StringAssert.Contains("全歼敌方", Find<CombatBountyTrackerView>().Summary);
        }

        private IEnumerator Victory()
        {
            for (int i = 0; i < 120 && !ResultOpen; i++) yield return null;
            Assert.IsTrue(ResultOpen, "最后一个敌人已倒下，应显示胜利");
            Assert.IsFalse(session.Combatants.HasLivingMembers(CombatTeam.Enemy));
            Assert.AreEqual(CombatSessionOutcome.Victory, Find<AdventureController>().LastEncounterOutcome);
            StringAssert.Contains("战斗胜利", Panel);
            Assert.AreEqual("关中阶地 · 战斗结算", Heading());
            Assert.IsTrue(view.GetButton("retry").IsInteractable());
            Assert.IsTrue(view.GetButton("choose_challenge").IsInteractable());
            Record("正式结局 " + Panel);
            yield return Capture("99-victory");
            yield return Press("choose_challenge");
            for (int i = 0; i < 30 && !view.GetButton("enter").gameObject.activeInHierarchy; i++) yield return null;
            Assert.IsTrue(view.GetButton("enter").gameObject.activeInHierarchy, "结局返回演武准备");
            Assert.AreEqual(savedPlayer, JsonUtility.ToJson(GameBootstrap.RequireRuntime().CaptureSave().player));
            Assert.AreEqual(savedCultivation, JsonUtility.ToJson(GameBootstrap.RequireRuntime().CaptureSave().cultivation));
        }

        private void RecordTurn(string label)
        {
            var units = string.Join(" / ", session.Combatants.All.Select(a =>
                $"{a.Id}{a.Position} 高{a.AltitudeBand} HP{a.CurrentHealth} AP{a.ActionPointsRemainingUnits / 1000f}"));
            var states = string.Join(" / ", session.Jindan?.Seats.Select(s =>
                $"{s.EffectId} 维护{s.MaintenancePayments}" +
                (s.FireDomain == null ? "" : $" 心{s.FireDomain.Support.Durability} 主{s.FireDomain.Main != null} 余{s.FireDomain.Tails.Count}") +
                (s.ActiveSeal == null ? "" : $" 门{s.ActiveSeal.Support.Durability}") +
                (s.ActiveVisibility == null ? "" : " 坛在")) ?? Enumerable.Empty<string>());
            Record($"{label} tick{session.TurnScheduler.CurrentTick} {units} | {states}");
        }

        private void Record(string text) => File.AppendAllText(Path.Combine(output, "actions.txt"), text + "\n");
        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG()); }
            finally { Object.Destroy(image); }
        }

        private IEnumerator RecordFrames()
        {
            // The render tap uses the project evidence format without changing game or input time.
            string folder = Path.Combine(output, "frames");
            Directory.CreateDirectory(folder);
            string manifest = Path.Combine(folder, "captures.csv");
            File.WriteAllText(manifest, "file,realtime,frame\n");
            var endOfFrame = new WaitForEndOfFrame();
            double next = 0;
            for (int index = 0; ; )
            {
                yield return endOfFrame;
                double now = Time.realtimeSinceStartupAsDouble;
                if (now < next) continue;
                next = now + 1d / 12;
                string name = "frame-" + (++index).ToString("D5") + ".jpg";
                var image = ScreenCapture.CaptureScreenshotAsTexture();
                try { File.WriteAllBytes(Path.Combine(folder, name), image.EncodeToJPG(82)); }
                finally { Object.Destroy(image); }
                File.AppendAllText(manifest, name + "," + now.ToString("F6", CultureInfo.InvariantCulture) +
                    "," + Time.frameCount + "\n");
            }
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (recording != null && Find<GameBootstrap>() != null)
                Find<GameBootstrap>().StopCoroutine(recording);
            recording = null;
            if (mouse != null)
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.settings.backgroundBehavior = previousBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousInput;
            }
            var bootstrap = Find<GameBootstrap>();
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return null;
        }
    }
}
