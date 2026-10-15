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
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongCombatSwapSpellPlayModeTests
    {
        private const string Attack = "spell_guanshiji", Recovery = "spell_julingshu";
        private string saveDirectory;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private AdventureSpawnSet spawned;
        private CombatHudPresenter hud;
        private CombatActionBarView bar;
        private CombatCommandInput input;
        private CombatActionPreviewView preview;
        private CombatRandom random;

        [UnitySetUp]
        public IEnumerator EnterFormalAdventure()
        {
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-Swap-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saveDirectory));
            CharacterCreationController creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Submit("formal-swap", "战斗换术验证");
            yield return SceneReady(GameplaySceneNames.World);
            WorldMapController world = Find<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            SettlementController settlement = Find<SettlementController>();
            Find<BountyBoardView>().Show(Get<ContentCatalogData>(settlement, "contentCatalog"),
                "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            Find<BountyBoardView>().SubmitAccept("bounty_guanzhong_shijiahou");
        }

        private IEnumerator BeginEncounter(bool reserveRecovery, bool reserveAttack = false, bool waitForContact = true)
        {
            Assert.AreEqual("练气", GameBootstrap.RequireRuntime().Player.Progression.RealmStage);
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
            random = new CombatRandom();
            encounter.SetCombatResolutionRandomSource(random);
            hud = Find<CombatHudPresenter>();
            bar = Find<CombatActionBarView>();
            input = Find<CombatCommandInput>();
            preview = Find<CombatActionPreviewView>();
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            session = Get<CombatSession>(encounter, "session");
            spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            yield return Ready();
            if (waitForContact) yield return GuanzhongBasicAttackPlayModeTests.WaitForMeleeContact(encounter, session);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            if (Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest]
        public IEnumerator DefaultLoadoutSelectsBothSlotsAndRejectsNoChangeWithoutPayment()
        {
            yield return BeginEncounter(false);
            Assert.That(spawned.Player.EquippedArtProfileIds, Is.EqualTo(new[] { Attack, Recovery }));
            Assert.That(hud.Snapshot.Actions.Count(action => action.Candidate.Kind == CombatActionKind.SwapSpell), Is.EqualTo(4));
            string before = State();
            foreach (int width in new[] { 1920, 1366 })
            {
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                bar.SwapSpellButton.onClick.Invoke();
                Assert.That(input.Candidate.SlotIndex, Is.Zero);
                Assert.That(input.Preview.CanConfirm, Is.False);
                StringAssert.Contains("已装配", input.Preview.Reason);
                preview.SwapSlotButton.onClick.Invoke();
                Assert.That(input.Candidate.SlotIndex, Is.EqualTo(1));
                StringAssert.Contains("其他栏位", input.Preview.Reason);
                preview.SwapCandidateButton.onClick.Invoke();
                Assert.That(input.Candidate.ProfileId, Is.EqualTo(Recovery));
                Assert.That(preview.ConfirmButton.interactable, Is.False);
                input.ConfirmCandidate();
                yield return RecordLayout("default-" + width);
                preview.CancelButton.onClick.Invoke();
                Assert.That(input.Candidate, Is.Null);
                Assert.That(preview.IsVisible, Is.False);
                Assert.That(State(), Is.EqualTo(before));
            }
            foreach (var candidate in new[] { Candidate("unknown_spell", 0), Candidate(Recovery, -1), Candidate(Recovery, 99) })
            {
                Assert.That(encounter.PreviewAction(candidate).CanConfirm, Is.False);
                encounter.ConfirmAction(candidate);
                Assert.That(State(), Is.EqualTo(before));
            }
        }

        [UnityTest]
        public IEnumerator FormalSwapChargesOncePreservesCooldownExitsGuardAndExhaustsTwoUses()
        {
            yield return BeginEncounter(true, waitForContact: false);
            RecordArtState("initial-before-contact");
            yield return GuanzhongBasicAttackPlayModeTests.WaitForMeleeContact(encounter, session);
            RecordArtState("contact-before-swap");
            Assert.That(GameBootstrap.RequireRuntime().Player.AbilityLoadout.KnownSpells, Does.Contain(Recovery));
            bar.GuardButton.onClick.Invoke(); input.ConfirmCandidate();
            yield return Ready();
            Assert.That(spawned.Player.IsGuarding, Is.True);
            Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(50000));
            spawned.Player.SetCooldown(Recovery, 90);
            spawned.Player.SetCooldown(Attack, 80);
            typeof(EncounterCoordinator).GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(encounter, new object[] { "受控冷却输入", true });
            int tick = session.TurnScheduler.CurrentTick;
            long turn = session.TurnSequence;
            bar.SwapSpellButton.onClick.Invoke();
            Assert.That(input.Candidate.ProfileId, Is.EqualTo(Recovery));
            Assert.That(input.Preview.ActionPointCostUnits, Is.EqualTo(50000));
            Assert.That(input.Preview.SpiritCost, Is.Zero);
            Assert.That(input.Preview.CtPenalty, Is.Zero);
            Assert.That(input.Preview.ReuseCooldownTicks, Is.EqualTo(90));
            Assert.That(input.Preview.CombatSwapsRemaining, Is.EqualTo(2));
            string before = State();
            preview.CancelButton.onClick.Invoke();
            Assert.That(State(), Is.EqualTo(before));
            bar.SwapSpellButton.onClick.Invoke();
            CombatActionCandidate first = input.Candidate;
            int mp = spawned.Player.CurrentSpirit, rolls = random.Count;
            RecordArtState("before-swap-out");
            yield return RecordLayout("legal");
            input.ConfirmCandidate();
            RecordArtState("after-swap-out");
            Assert.That(spawned.Player.EquippedArtProfileIds, Is.EqualTo(new[] { Recovery }));
            Assert.That(spawned.Player.CombatSwapsUsed, Is.EqualTo(1));
            Assert.That(spawned.Player.IsGuarding, Is.False);
            Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.Zero);
            Assert.That(spawned.Player.CurrentSpirit, Is.EqualTo(mp));
            Assert.That(spawned.Player.GetCooldown(Recovery), Is.EqualTo(90));
            Assert.That(spawned.Player.GetCooldown(Attack), Is.EqualTo(80));
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(session.TurnSequence, Is.EqualTo(turn));
            Assert.That(random.Count, Is.EqualTo(rolls));
            string committed = State();
            input.ConfirmCandidate(); encounter.ConfirmAction(first);
            Assert.That(State(), Is.EqualTo(committed));
            yield return Ready();
            int recoveryBefore = spawned.Player.GetCooldown(Recovery);
            int attackBefore = spawned.Player.GetCooldown(Attack);
            bar.SwapSpellButton.onClick.Invoke();
            Assert.That(input.Candidate.ProfileId, Is.EqualTo(Attack));
            Assert.That(input.Preview.CombatSwapsRemaining, Is.EqualTo(1));
            Assert.That(input.Preview.ReuseCooldownTicks, Is.EqualTo(Math.Max(60, attackBefore)));
            RecordArtState("before-swap-back");
            input.ConfirmCandidate();
            RecordArtState("after-swap-back");
            Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(50000));
            Assert.That(spawned.Player.CombatSwapsUsed, Is.EqualTo(2));
            Assert.That(spawned.Player.GetCooldown(Recovery), Is.EqualTo(recoveryBefore));
            Assert.That(spawned.Player.GetCooldown(Attack), Is.EqualTo(Math.Max(60, attackBefore)));
            Assert.That(spawned.Player.EquippedArtProfileIds, Is.EqualTo(new[] { Attack }));
            bar.SwapSpellButton.onClick.Invoke();
            Assert.That(input.Preview.CanConfirm, Is.False);
            StringAssert.Contains("次数已用尽", input.Preview.Reason);
            before = State();
            input.ConfirmCandidate(); preview.CancelButton.onClick.Invoke();
            Assert.That(State(), Is.EqualTo(before));
            bar.ArtButton.onClick.Invoke();
            Assert.That(input.Preview.CanConfirm, Is.False);
            preview.CancelButton.onClick.Invoke();
            Assert.That(bar.ArtButton.interactable, Is.True, "Cooldown rejection must remain previewable after swapping too.");
            Assert.That(encounter.PreviewAction(new CombatActionCandidate(CombatActionKind.Art, "player", "enemy", Attack,
                hud.Snapshot.CommandContextVersion)).CanConfirm, Is.False);
            int startTick = session.TurnScheduler.CurrentTick, initialCd = spawned.Player.GetCooldown(Attack);
            while (spawned.Player.GetCooldown(Attack) > 0)
            {
                // Keep this cooldown functionality fixture alive; HP restoration is not an AP or balance assertion.
                spawned.Player.RestoreHealth(spawned.Player.MaximumHealth);
                bar.WaitButton.onClick.Invoke(); input.ConfirmCandidate();
                yield return null;
                yield return Ready();
                Assert.That(session.TurnScheduler.CurrentTick - startTick, Is.LessThan(200));
            }
            Assert.That(session.TurnScheduler.CurrentTick - startTick, Is.GreaterThanOrEqualTo(initialCd));
            Assert.That(bar.ArtButton.interactable, Is.True, hud.Snapshot.ArtActions[0].Targets[0].RejectionReason);
            RecordArtState("after-real-cooldown");
            bar.ArtButton.onClick.Invoke();
            Assert.That(input.Preview.CanConfirm, Is.True);
            int ap = spawned.Player.ActionPointsRemainingUnits;
            input.ConfirmCandidate();
            Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(ap - 50000));
            Assert.That(spawned.Player.GetCooldown(Attack), Is.Zero, "The final production Guanshiji has CD 0.");
            Assert.That(spawned.Player.CombatSwapsUsed, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator StaleQuoteAndInsufficientActionPointsDoNotChargeOrConsumeSwap()
        {
            yield return BeginEncounter(false, true);
            bar.SwapSpellButton.onClick.Invoke();
            Assert.That(input.Preview.CanConfirm, Is.True);
            spawned.Player.SetCooldown(Attack, 100);
            string before = State();
            input.ConfirmCandidate();
            Assert.That(State(), Is.EqualTo(before), "Changed authority facts invalidate the original quote.");
            Assert.That(spawned.Player.CombatSwapsUsed, Is.Zero);
            bar.ArtButton.onClick.Invoke();
            Assert.That(input.Preview.CanConfirm, Is.True, input.Preview.Reason);
            input.ConfirmCandidate();
            yield return Ready();
            bar.GuardButton.onClick.Invoke(); input.ConfirmCandidate();
            yield return Ready();
            Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(25000));
            bar.SwapSpellButton.onClick.Invoke();
            Assert.That(input.Preview.CanConfirm, Is.False);
            StringAssert.Contains("行动力", input.Preview.Reason);
            before = State();
            input.ConfirmCandidate();
            preview.CancelButton.onClick.Invoke();
            Assert.That(State(), Is.EqualTo(before));
            Assert.That(spawned.Player.CombatSwapsUsed, Is.Zero);
        }

        private void RecordArtState(string label)
        {
            string record = label + "; context=" + hud.Snapshot.CommandContextVersion + "; state=" + State() +
                "; selected=" + Get<string>(input, "firstArtTargetId") + "; button=" + bar.ArtButton.interactable +
                "; targets=" + string.Join("/", hud.Snapshot.ArtActions.SelectMany(art => art.Targets.Select(target =>
                    art.ProfileId + ":" + target.Id + "=" + target.CanExecute + ":" + target.RejectionReason +
                    ":AP=" + target.Quote.ActionPointCostUnits + ":MP=" + target.Quote.SpiritCost +
                    ":CD=" + target.Quote.RemainingCooldownTicks + ":reuse=" + target.Quote.ReuseCooldownTicks +
                    ":context=" + target.Quote.Candidate.ContextVersion)));
            TestContext.WriteLine(record);
            string directory = Path.Combine(Application.dataPath, "../Logs/swap");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "starter-" + label + ".txt"), record);
        }

        private IEnumerator Ready()
        {
            for (float deadline = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < deadline &&
                (session.CurrentActorId != "player" || session.TurnPhase == CombatTurnPhase.Finished || encounter.IsPresenting);)
                yield return null;
            yield return null;
            Assert.That(session.CurrentActorId, Is.EqualTo("player"));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(encounter.IsPresenting, Is.False);
        }

        private CombatActionCandidate Candidate(string id, int slot) => new CombatActionCandidate(
            CombatActionKind.SwapSpell, "player", null, id, hud.Snapshot.CommandContextVersion, slotIndex: slot);

        private string State() => session.TurnSequence + ":" + session.StateVersion + ":" + session.CurrentActorId + ":" +
            session.TurnPhase + ":" + session.PendingCtPenalty + ":" + session.TurnScheduler.CurrentTick + ":" + random.Count + ":" +
            string.Join(";", session.Combatants.All.Select(actor => actor.Id + ":" + actor.CurrentHealth + ":" + actor.CurrentSpirit +
                ":" + actor.ActionPointsRemainingUnits + ":" + actor.Position + ":" + actor.IsGuarding + ":" + actor.CombatSwapsUsed +
                ":" + string.Join(",", actor.EquippedArtProfileIds) + ":" +
                string.Join(",", actor.Cooldowns.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value))));

        private IEnumerator RecordLayout(string label)
        {
            Canvas.ForceUpdateCanvases();
            foreach (Button button in new[] { bar.SwapSpellButton, preview.SwapSlotButton, preview.SwapCandidateButton,
                preview.ConfirmButton, preview.CancelButton })
            {
                Assert.That(button.gameObject.activeInHierarchy, Is.True);
                Text text = button.GetComponentInChildren<Text>();
                Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + .1f), text.text);
                Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(text.rectTransform.rect.width + .1f), text.text);
                var corners = new Vector3[4]; button.GetComponent<RectTransform>().GetWorldCorners(corners);
                Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(0));
                Assert.That(corners[2].x, Is.LessThanOrEqualTo(Screen.width));
                Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(0));
                Assert.That(corners[2].y, Is.LessThanOrEqualTo(Screen.height));
            }
            string directory = Path.Combine(Application.dataPath, "../Logs/swap");
            Directory.CreateDirectory(directory);
            Text summary = Get<Text>(preview, "summary");
            var summaryCorners = new Vector3[4]; summary.rectTransform.GetWorldCorners(summaryCorners);
            RectTransform viewport = summary.GetComponentInParent<ScrollRect>().viewport;
            var viewportCorners = new Vector3[4]; viewport.GetWorldCorners(viewportCorners);
            Assert.That(summary.preferredHeight, Is.LessThanOrEqualTo(viewport.rect.height + .1f),
                "The swap cost and rejection reason must be visible together without scrolling.");
            File.WriteAllText(Path.Combine(directory, label + ".txt"), "scene=" + SceneManager.GetActiveScene().name +
                "; resolution=" + Screen.width + "x" + Screen.height + "; state=" + State() +
                "\nsummary=" + summary.rectTransform.rect + "; preferredHeight=" + summary.preferredHeight +
                "; viewport=" + viewport.rect + "; canvas=" + summary.canvas.name + "/" + summary.canvas.renderMode +
                "; scale=" + summary.canvas.scaleFactor +
                "; summaryCorners=" + string.Join("/", summaryCorners.Select(corner => corner.ToString("F2"))) +
                "; viewportCorners=" + string.Join("/", viewportCorners.Select(corner => corner.ToString("F2"))) + "\n" + summary.text);
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, label + ".png"));
            for (int frame = 0; frame < 4; frame++) yield return null;
        }

        private static void SetGameSize(int width, int height)
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
        private sealed class CombatRandom : ICombatResolutionRandomSource
        {
            public int Count { get; private set; }
            public float NextPercent() => Count++ % 5 == 0 ? 0f : 99f;
        }
    }
}
