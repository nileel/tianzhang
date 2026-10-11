using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongStarterArtsPlayModeTests
    {
        private static TianZhang.Content.CombatRuleSetData GrowthRules
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
#else
                throw new System.NotSupportedException("Editor fixture requires the production rule asset.");
#endif
            }
        }

        private const string AttackId = "spell_guanshiji", RecoveryId = "spell_julingshu";
        private readonly List<string> saveDirectories = new List<string>();
        private readonly List<EnvironmentProfileAsset> environmentClones = new List<EnvironmentProfileAsset>();
        private EncounterCoordinator encounter;
        private CombatSession session;
        private AdventureSpawnSet spawned;
        private CharacterStateSnapshot source;
        private CountingCombatRandomSource random;
        private ForwardingSink sink;
        private ForwardingUnitPort unitPort;
        private CombatActionBarView actionBar;
        private string createdOriginId;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            foreach (EnvironmentProfileAsset clone in environmentClones) Object.Destroy(clone);
            environmentClones.Clear();
            foreach (string directory in saveDirectories)
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            saveDirectories.Clear();
        }

        [UnityTest]
        public IEnumerator AllFormalOriginButtonsPreserveInputsAndReplaceRepeatedConfigurationCallbacks()
        {
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForScene(GameplaySceneNames.StartMenu);
            CharacterCreationController creation = FindInActiveScene<CharacterCreationController>();
            CharacterCreationView view = FindInActiveScene<CharacterCreationView>();
            GetPrivateField<Button>(FindInActiveScene<StartMenuView>(), "newPlayerButton").onClick.Invoke();
            yield return null;
            Assert.That(creation.Draft.OriginId, Is.EqualTo("origin_loose"));
            AssertOriginButtonGeometry(view);
            InputField slotInput = GetPrivateField<InputField>(view, "slotIdInput");
            InputField nameInput = GetPrivateField<InputField>(view, "characterNameInput");
            slotInput.text = "origin-input-preservation";
            nameInput.text = "出身选择保留姓名";
            int previousSelections = 0, previousSubmissions = 0, selections = 0, submissions = 0;
            string selectedOrigin = null, submittedSlot = null, submittedName = null;
            view.Configure((slot, name) => previousSubmissions++, id => previousSelections++);
            view.Configure((slot, name) =>
            {
                submissions++;
                submittedSlot = slot;
                submittedName = name;
            }, id =>
            {
                selections++;
                selectedOrigin = id;
                Assert.That(creation.SelectOrigin(id), Is.True);
            });

            Text summary = GetPrivateField<Text>(view, "summaryText");
            Text[] labels = GetPrivateField<Text[]>(view, "originLabels");
            int[] used = { 0, 1, 2, 0 }, refunded = { 0, 0, 0, 2 }, remaining = { 10, 9, 8, 12 };
            for (int index = 0; index < CharacterCreationCatalog.Origins.Length; index++)
            {
                OriginOption origin = CharacterCreationCatalog.Origins[index];
                view.GetOriginButton(origin.Id).onClick.Invoke();
                Assert.That(selectedOrigin, Is.EqualTo(origin.Id));
                Assert.That(creation.Draft.OriginId, Is.EqualTo(origin.Id));
                Assert.That(slotInput.text, Is.EqualTo("origin-input-preservation"));
                Assert.That(nameInput.text, Is.EqualTo("出身选择保留姓名"));
                StringAssert.Contains("出身：" + origin.DisplayName, summary.text);
                StringAssert.Contains("起点：" + (origin.StartNodeId == "guanzhong_hub" ? "关中" : "江左"), summary.text);
                StringAssert.Contains("创建预算：" + used[index] + "/10", summary.text);
                StringAssert.Contains("返还：" + refunded[index], summary.text);
                StringAssert.Contains("剩余：" + remaining[index], summary.text);
                for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
                    Assert.That(labels[labelIndex].text, Is.EqualTo((labelIndex == index ? "已选 · " : string.Empty) +
                        CharacterCreationCatalog.Origins[labelIndex].DisplayName));
                AssertOriginButtonGeometry(view);
            }
            creation.Open();
            Assert.That(slotInput.text, Is.EqualTo("origin-input-preservation"));
            Assert.That(nameInput.text, Is.EqualTo("出身选择保留姓名"));
            string summaryBefore = summary.text;
            view.ShowFailure("保留当前失败提示");
            Assert.That(creation.SelectOrigin("origin_unknown"), Is.False);
            Assert.That(summary.text, Is.EqualTo(summaryBefore));
            Assert.That(GetPrivateField<Text>(view, "failureText").text, Is.EqualTo("保留当前失败提示"));
            Assert.That(slotInput.text, Is.EqualTo("origin-input-preservation"));
            Assert.That(nameInput.text, Is.EqualTo("出身选择保留姓名"));
            GetPrivateField<Button>(view, "createButton").onClick.Invoke();
            Assert.That(previousSelections, Is.Zero);
            Assert.That(previousSubmissions, Is.Zero);
            Assert.That(selections, Is.EqualTo(4));
            Assert.That(submissions, Is.EqualTo(1));
            Assert.That(submittedSlot, Is.EqualTo("origin-input-preservation"));
            Assert.That(submittedName, Is.EqualTo("出身选择保留姓名"));
        }

        [UnityTest]
        public IEnumerator FormalMinorClanSelectionSavesLoadsAndCastsBothRealButtonsWithoutRecoveryRandomness()
        {
            yield return EnterFormalCombat(null);
            Assert.That(session.SpiritRecoveryEnvironmentMultiplier, Is.EqualTo(1f));
            Assert.That(actionBar.ArtButton, Is.Not.Null);
            Assert.That(actionBar.SecondArtButton, Is.Not.Null);
            Assert.That(actionBar.SecondArtTargetButton, Is.Not.Null);
            Assert.That(actionBar.ArtButton.interactable, Is.True);
            Assert.That(actionBar.SecondArtButton.interactable, Is.True);
            StringAssert.Contains("贯石击", actionBar.ArtButton.GetComponentInChildren<UnityEngine.UI.Text>().text);
            StringAssert.Contains("聚灵术", actionBar.SecondArtButton.GetComponentInChildren<UnityEngine.UI.Text>().text);
            AssertArtTargets();

            AssertRejectedWithoutConsumption(() => ((ICombatCommandHandler)encounter).RequestArt(
                spawned.Player.Id, spawned.Player.Id, AttackId));
            AssertRejectedWithoutConsumption(() => ((ICombatCommandHandler)encounter).RequestArt(
                spawned.Player.Id, "missing_target", RecoveryId));
            int mana = spawned.Player.CurrentSpirit, health = spawned.Enemy.CurrentHealth, rolls = random.Count;
            actionBar.ArtButton.onClick.Invoke();
            Object.FindFirstObjectByType<CombatActionPreviewView>().ConfirmButton.onClick.Invoke();
            Assert.That(spawned.Player.CurrentSpirit, Is.EqualTo(mana - 12));
            Assert.That(spawned.Enemy.CurrentHealth, Is.LessThan(health));
            Assert.That(random.Count, Is.EqualTo(rolls + 4));
            Assert.That(session.PendingCtPenalty, Is.Zero);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            StringAssert.Contains("消耗灵力 12", sink.Logs.Last());
            yield return WaitForPlayerTurn(CombatTurnPhase.Active);
            Assert.That(actionBar.WaitButton.interactable, Is.True);
            StringAssert.Contains("结束行动", actionBar.WaitButton.GetComponentInChildren<UnityEngine.UI.Text>().text);
            actionBar.WaitButton.onClick.Invoke();
            Object.FindFirstObjectByType<CombatActionPreviewView>().ConfirmButton.onClick.Invoke();
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));

            foreach (int recovery in new[] { 5, 5, 2 })
            {
                yield return WaitForRecoveryTurn();
                mana = spawned.Player.CurrentSpirit;
                rolls = random.Count;
                int enemyHealth = spawned.Enemy.CurrentHealth;
                AssertArtTargets();
                Assert.That(actionBar.SecondArtButton.interactable, Is.True);
                actionBar.SecondArtButton.onClick.Invoke();
                Object.FindFirstObjectByType<CombatActionPreviewView>().ConfirmButton.onClick.Invoke();
                Assert.That(spawned.Player.CurrentSpirit, Is.EqualTo(mana + recovery));
                Assert.That(spawned.Enemy.CurrentHealth, Is.EqualTo(enemyHealth));
                AssertRecoveryProjection(spawned.Player.Id, recovery, rolls);
                StringAssert.Contains("消耗灵力 0", sink.Logs.Last());
                yield return WaitForPlayerTurn(CombatTurnPhase.Active);
                ((ICombatCommandHandler)encounter).RequestEndTurn(spawned.Player.Id);
            }
            Assert.That(spawned.Player.CurrentSpirit, Is.EqualTo(spawned.Player.MaximumSpirit));

            yield return WaitForRecoveryTurn();
            mana = spawned.Player.CurrentSpirit;
            rolls = random.Count;
            Assert.That(spawned.Enemy.CurrentSpirit, Is.EqualTo(spawned.Enemy.MaximumSpirit));
            actionBar.SecondArtTargetButton.onClick.Invoke();
            StringAssert.Contains(sink.Latest.Enemy.DisplayName,
                actionBar.SecondArtTargetButton.GetComponentInChildren<UnityEngine.UI.Text>().text);
            StringAssert.Contains("20MP", actionBar.SecondArtButton.GetComponentInChildren<UnityEngine.UI.Text>().text);
            actionBar.SecondArtButton.onClick.Invoke();
            Object.FindFirstObjectByType<CombatActionPreviewView>().ConfirmButton.onClick.Invoke();
            Assert.That(spawned.Player.CurrentSpirit, Is.EqualTo(mana - 20));
            AssertRecoveryProjection(spawned.Enemy.Id, 0, rolls);
            StringAssert.Contains("恢复灵力 0", sink.Logs.Last());
            StringAssert.Contains("消耗灵力 20", sink.Logs.Last());
            yield return WaitForPlayerTurn(CombatTurnPhase.Active);
            ((ICombatCommandHandler)encounter).RequestEndTurn(spawned.Player.Id);
        }

        [UnityTest]
        public IEnumerator ExplicitEnvironmentClonesExerciseOrdinaryVeinAndBlessedRecoveryInTheRealScene()
        {
            foreach (SpiritRecoveryEnvironment environment in new[]
                     { SpiritRecoveryEnvironment.Ordinary, SpiritRecoveryEnvironment.SpiritVeinNode, SpiritRecoveryEnvironment.BlessedLand })
            {
                yield return EnterFormalCombat(environment);
                float factor = environment == SpiritRecoveryEnvironment.Ordinary ? 1f : 2f;
                Assert.That(session.SpiritRecoveryEnvironmentMultiplier, Is.EqualTo(factor));
                TestContext.WriteLine("Controlled Adventure scene, explicit cloned environment=" + environment +
                    "; not evidence of a Guanzhong spirit node. Only current MP is arranged below.");
                spawned.Player.SetSpirit(spawned.Player.MaximumSpirit, 19);
                AssertRejectedWithoutConsumption(() => ((ICombatCommandHandler)encounter).RequestArt(
                    spawned.Player.Id, spawned.Enemy.Id, RecoveryId));
                spawned.Player.SetSpirit(spawned.Player.MaximumSpirit, spawned.Player.MaximumSpirit - 1);
                int rolls = random.Count;
                ((ICombatCommandHandler)encounter).RequestArt(spawned.Player.Id, spawned.Player.Id, RecoveryId);
                Assert.That(spawned.Player.CurrentSpirit, Is.EqualTo(spawned.Player.MaximumSpirit));
                AssertRecoveryProjection(spawned.Player.Id, 1, rolls);
                yield return WaitForPlayerTurn(CombatTurnPhase.Active);
                ((ICombatCommandHandler)encounter).RequestEndTurn(spawned.Player.Id);

                yield return WaitForRecoveryTurn();
                spawned.Player.SetSpirit(spawned.Player.MaximumSpirit, 30);
                spawned.Enemy.SetSpirit(spawned.Enemy.MaximumSpirit, spawned.Enemy.MaximumSpirit - 10);
                int targetMana = spawned.Enemy.CurrentSpirit, expected = environment == SpiritRecoveryEnvironment.Ordinary ? 2 : 5;
                rolls = random.Count;
                ((ICombatCommandHandler)encounter).RequestArt(spawned.Player.Id, spawned.Enemy.Id, RecoveryId);
                Assert.That(spawned.Player.CurrentSpirit, Is.EqualTo(10));
                Assert.That(spawned.Enemy.CurrentSpirit, Is.EqualTo(targetMana + expected));
                AssertRecoveryProjection(spawned.Enemy.Id, expected, rolls);
                StringAssert.Contains("消耗灵力 20", sink.Logs.Last());
                yield return WaitForPlayerTurn(CombatTurnPhase.Active);
                ((ICombatCommandHandler)encounter).RequestEndTurn(spawned.Player.Id);
            }
        }

        private IEnumerator EnterFormalCombat(SpiritRecoveryEnvironment? controlledEnvironment)
        {
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForScene(GameplaySceneNames.StartMenu);
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            string directory = Path.Combine(Application.temporaryCachePath, "TianZhang-StarterArts-" + Guid.NewGuid().ToString("N"));
            saveDirectories.Add(directory);
            SetPrivateField(bootstrap, "slotStore", new GameSaveSlotStore(directory));
            CharacterCreationController creation = FindInActiveScene<CharacterCreationController>();
            Assert.That(creation, Is.Not.Null);
            CharacterCreationView creationView = FindInActiveScene<CharacterCreationView>();
            Assert.That(creationView, Is.Not.Null);
            Button newPlayerButton = GetPrivateField<Button>(FindInActiveScene<StartMenuView>(), "newPlayerButton");
            Assert.That(newPlayerButton.gameObject.activeInHierarchy && newPlayerButton.interactable, Is.True);
            newPlayerButton.onClick.Invoke();
            yield return null;
            Assert.That(creation.Draft.OriginId, Is.EqualTo("origin_loose"));
            AssertOriginButtonGeometry(creationView);
            InputField slotInput = GetPrivateField<InputField>(creationView, "slotIdInput");
            InputField nameInput = GetPrivateField<InputField>(creationView, "characterNameInput");
            slotInput.text = "starter-arts";
            nameInput.text = "普通小族术法验证";
            Button minorClanButton = creationView.GetOriginButton("origin_minor_clan");
            Assert.That(minorClanButton, Is.Not.Null);
            Assert.That(minorClanButton.gameObject.activeInHierarchy && minorClanButton.interactable, Is.True);
            minorClanButton.onClick.Invoke();
            Assert.That(creation.Draft.OriginId, Is.EqualTo("origin_minor_clan"));
            Assert.That(slotInput.text, Is.EqualTo("starter-arts"));
            Assert.That(nameInput.text, Is.EqualTo("普通小族术法验证"));
            StringAssert.Contains("小族子弟", GetPrivateField<Text>(creationView, "summaryText").text);
            StringAssert.Contains("起点：关中", GetPrivateField<Text>(creationView, "summaryText").text);
            AssertOriginButtonGeometry(creationView);
            createdOriginId = creation.Draft.OriginId;
            Assert.That(new[] { creation.Draft.Innate.RootBone, creation.Draft.Innate.Soul,
                creation.Draft.Innate.DivineSense, creation.Draft.Innate.Aptitude, creation.Draft.Innate.Fortune },
                Is.EqualTo(new[] { 8, 8, 8, 8, 8 }));
            Button createButton = GetPrivateField<Button>(creationView, "createButton");
            Assert.That(createButton.gameObject.activeInHierarchy && createButton.interactable, Is.True);
            createButton.onClick.Invoke();
            yield return WaitForScene(GameplaySceneNames.World);
            Assert.That(GameBootstrap.RequireRuntime().Navigation.WorldNodeId, Is.EqualTo("guanzhong_hub"));
            source = GameBootstrap.RequireRuntime().Player.Capture();
            string saved = GameBootstrap.RequireRuntime().CaptureSaveJson();
            Assert.That(bootstrap.SaveActiveSlot().Succeeded, Is.True);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForScene(GameplaySceneNames.StartMenu);
            FindInActiveScene<StartMenuController>().LoadPlayer("starter-arts");
            yield return WaitForScene(GameplaySceneNames.World);
            Assert.That(GameBootstrap.RequireRuntime().CaptureSaveJson(), Is.EqualTo(saved));
            Assert.That(GameBootstrap.RequireRuntime().Player.Progression.RealmStage, Is.EqualTo("凡人"));
            // This ability fixture explicitly upgrades through the existing aggregate; creation grants no qualification.
            GameBootstrap.RequireRuntime().Player.RestoreProgression(new CharacterProgressionSnapshot("", "练气", 1.5f));
            source = GameBootstrap.RequireRuntime().Player.Capture();
            Assert.That(new[] { source.Attributes.RootBone, source.Attributes.Spirit, source.Attributes.Mind,
                source.Attributes.Talent, source.Attributes.Fortune }, Is.EqualTo(new[] { 8, 8, 8, 8, 8 }));
            Assert.That(source.AbilityLoadout.KnownSpells, Is.EqualTo(new[] { AttackId, RecoveryId }));
            Assert.That(source.AbilityLoadout.EquippedSpells, Is.EqualTo(new[] { AttackId, RecoveryId }));
            Assert.That(source.VisibleRootId, Is.EqualTo("root_water_middle"));
            Assert.That(source.VisibleRootElement, Is.EqualTo("水"));
            Assert.That(source.VisibleRootGrade, Is.EqualTo("中品"));
            WorldMapController world = FindInActiveScene<WorldMapController>();
            Assert.That(world.SelectNode("guanzhong_hub"), Is.True);
            world.EnterSelectedLocation();
            yield return WaitForScene(GameplaySceneNames.Settlement);
            SettlementController settlement = FindInActiveScene<SettlementController>();
            BountyBoardView board = FindInActiveScene<BountyBoardView>();
            board.Show(GetPrivateField<ContentCatalogData>(settlement, "contentCatalog"), "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            board.SubmitAccept("bounty_guanzhong_shijiahou");
            Assert.That(settlement.EnterAdventure("guanzhong_wild"), Is.True);
            yield return WaitForScene(GameplaySceneNames.Adventure);
            AdventureController adventure = FindInActiveScene<AdventureController>();
            for (int frame = 0; frame < 120 && (adventure.Session == null || adventure.CurrentState != AdventureSceneState.Exploration); frame++)
                yield return null;
            Assert.That(adventure.CurrentState, Is.EqualTo(AdventureSceneState.Exploration));
            AdventureSceneInstaller installer = FindInActiveScene<AdventureSceneInstaller>();
            EnvironmentProfileAsset formalEnvironment = GetPrivateField<EnvironmentProfileAsset>(installer, "environmentProfile");
            Assert.That(formalEnvironment.spiritRecoveryEnvironment, Is.EqualTo(SpiritRecoveryEnvironment.Ordinary));
            if (controlledEnvironment.HasValue)
            {
                EnvironmentProfileAsset clone = Object.Instantiate(formalEnvironment);
                environmentClones.Add(clone);
                clone.spiritRecoveryEnvironment = controlledEnvironment.Value;
                Assert.That(clone.TryCreateDefinition(out _, out string reason), Is.True, reason);
                SetPrivateField(adventure, "environmentProfile", clone);
            }
            encounter = FindInActiveScene<EncounterCoordinator>();
            random = new CountingCombatRandomSource();
            encounter.SetCombatResolutionRandomSource(random);
            sink = new ForwardingSink(GetPrivateField<ICombatPresentationSink>(encounter, "presentation"));
            unitPort = new ForwardingUnitPort(GetPrivateField<ICombatUnitPresentationPort>(encounter, "unitPresentation"));
            SetPrivateField(encounter, "presentation", sink);
            SetPrivateField(encounter, "unitPresentation", unitPort);
            adventure.SetEncounterRandomSource(new GuaranteedEncounterRandomSource());
            Assert.That(FindInActiveScene<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
            session = GetPrivateField<CombatSession>(encounter, "session");
            spawned = GetPrivateField<AdventureSpawnSet>(encounter, "spawned");
            Assert.That(session.AttackProfiles.Select(profile => profile.Id),
                Is.EquivalentTo(new[] { "basic_iron_sword", "basic_iron_blade", AttackId, RecoveryId,
                    "basic_body_human_fanren", "basic_body_human_lianqi", "basic_body_human_zhuji",
                    "basic_body_human_jindan", "basic_body_shijiahou_lianqi" }));
            Assert.That(spawned.Player.RawSpirit, Is.EqualTo(source.Attributes.Spirit));
            Assert.That(spawned.Player.RealmStage, Is.EqualTo(source.Progression.RealmStage));
            Assert.That(spawned.Player.VisibleRootElement, Is.EqualTo(source.VisibleRootElement));
            Assert.That(spawned.Player.VisibleRootGrade, Is.EqualTo(source.VisibleRootGrade));
            Assert.That(spawned.Player.EquippedArtProfileIds, Is.EqualTo(source.AbilityLoadout.EquippedSpells));
            Assert.That(spawned.Player.AvailableArtProfileIds, Is.EqualTo(source.AbilityLoadout.KnownSpells));
            foreach (string id in new[] { AttackId, RecoveryId })
            {
                Assert.That(session.TryGetProfile(id, out CombatAttackProfile profile), Is.True);
                Assert.That(profile.MeetsRequirements(spawned.Player.RealmStage,
                    spawned.Player.VisibleRootElement, spawned.Player.VisibleRootGrade), Is.True);
            }
            actionBar = FindInActiveScene<CombatActionBarView>();
            if (!controlledEnvironment.HasValue) ExportOrdinaryInputs();
            yield return WaitForPlayerTurn();
        }

        private void AssertArtTargets()
        {
            CombatArtActionSnapshot attack = sink.Latest.ArtActions.Single(value => value.ProfileId == AttackId);
            Assert.That(attack.Targets.Single().Id, Is.EqualTo(spawned.Enemy.Id));
            Assert.That(attack.Targets.Single().SpiritCost, Is.EqualTo(12));
            CombatArtActionSnapshot recovery = sink.Latest.ArtActions.Single(value => value.ProfileId == RecoveryId);
            Assert.That(recovery.Targets.Select(value => value.Id), Is.EquivalentTo(new[] { spawned.Player.Id, spawned.Enemy.Id }));
            Assert.That(recovery.Targets.Single(value => value.Id == spawned.Player.Id).SpiritCost, Is.Zero);
            Assert.That(recovery.Targets.Single(value => value.Id == spawned.Enemy.Id).SpiritCost, Is.EqualTo(20));
            Assert.That(recovery.Targets.All(value => value.CanExecute), Is.True);
        }

        private void AssertRecoveryProjection(string targetId, int amount, int previousRolls)
        {
            Assert.That(random.Count, Is.EqualTo(previousRolls), "RestoreSpirit must not sample attack rolls.");
            Assert.That(session.PendingCtPenalty, Is.EqualTo(5));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            CombatUnitPresentationTargetResult target = unitPort.Events.Last(value =>
                value.ActorCombatantId == spawned.Player.Id && value.PresentationEvent == CombatUnitPresentationEvent.Cast).TargetResults.Single();
            Assert.That(target.CombatantId, Is.EqualTo(targetId));
            Assert.That(target.RestoredSpirit, Is.EqualTo(amount));
            Assert.That(target.FinalDamage, Is.Zero);
            StringAssert.Contains("恢复灵力 " + amount, sink.Logs.Last());
        }

        private void AssertRejectedWithoutConsumption(Action request)
        {
            int[] before = { spawned.Player.CurrentHealth, spawned.Player.CurrentSpirit, spawned.Enemy.CurrentHealth,
                spawned.Enemy.CurrentSpirit, spawned.Player.ActionPointsRemainingUnits, session.TurnScheduler.CurrentTick,
                session.PendingCtPenalty, random.Count, unitPort.Events.Count };
            string actor = session.CurrentActorId;
            CombatTurnPhase phase = session.TurnPhase;
            request();
            Assert.That(new[] { spawned.Player.CurrentHealth, spawned.Player.CurrentSpirit, spawned.Enemy.CurrentHealth,
                spawned.Enemy.CurrentSpirit, spawned.Player.ActionPointsRemainingUnits, session.TurnScheduler.CurrentTick,
                session.PendingCtPenalty, random.Count, unitPort.Events.Count }, Is.EqualTo(before));
            Assert.That(session.CurrentActorId, Is.EqualTo(actor));
            Assert.That(session.TurnPhase, Is.EqualTo(phase));
        }

        private IEnumerator WaitForRecoveryTurn()
        {
            yield return WaitForPlayerTurn();
            int startTick = session.TurnScheduler.CurrentTick;
            int cooldown = spawned.Player.GetCooldown(RecoveryId);
            while (spawned.Player.GetCooldown(RecoveryId) > 0)
            {
                // Keep this spell functionality fixture alive while real turns advance its production cooldown.
                // This is explicit fixture HP preparation, not character balance or a production recovery effect.
                spawned.Player.RestoreHealth(spawned.Player.MaximumHealth);
                ((ICombatCommandHandler)encounter).RequestEndTurn(spawned.Player.Id);
                yield return null;
                yield return WaitForPlayerTurn();
                Assert.That(session.TurnScheduler.CurrentTick - startTick, Is.LessThan(200));
            }
            Assert.That(session.TurnScheduler.CurrentTick - startTick, Is.GreaterThanOrEqualTo(cooldown));
        }

        private IEnumerator WaitForPlayerTurn(CombatTurnPhase phase = CombatTurnPhase.Active)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.That(encounter != null && encounter.IsRunning, Is.True, "Formal combat ended before the arts scenario completed.");
                if (session.CurrentActorId == spawned.Player.Id && session.TurnPhase == phase && !encounter.IsPresenting &&
                    sink.Latest != null && sink.Latest.AcceptsCommands) yield break;
                yield return null;
            }
            Assert.Fail("Formal combat did not yield the player's turn.");
        }

        private void ExportOrdinaryInputs()
        {
            var evidence = new OrdinaryInputEvidence
            {
                source = "StartMenu/new-player button/minor-clan origin button/create button/save-load/World/Settlement/Adventure; default balanced attributes and unmodified enemy template",
                originId = createdOriginId,
                innate = new[] { source.Attributes.RootBone, source.Attributes.Spirit, source.Attributes.Mind, source.Attributes.Talent, source.Attributes.Fortune },
                playerCharacter = CharacterRecord.Capture(source),
                enemyCharacter = CharacterRecord.Capture(CharacterRuntimeProfile.FromDefinition(spawned.Enemy.Id, spawned.EnemyData.combatTemplate, GrowthRules).Capture()),
                enemyId = spawned.EnemyData.enemyId,
                player = CombatInputEvidence.Capture(spawned.Player),
                enemy = CombatInputEvidence.Capture(spawned.Enemy),
                environmentMultiplier = session.SpiritRecoveryEnvironmentMultiplier,
            };
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "spells", "ordinary-player-input.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(evidence, true));
            TestContext.WriteLine("Ordinary generated input evidence: " + path);
        }

#if UNITY_EDITOR
        // Batch-only numeric inputs: production owner derives stats; clones never change the asset.
        public static void ExportBalanceCandidates()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string original = File.ReadAllText(Path.Combine(root, "开发管理", "关中首战普通角色输入.json"));
            var template = UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Entity.CharacterData>(
                "Assets/Data/Characters/Char_Enemy_enemy_shijiahou.asset");
            Assert.That(template, Is.Not.Null);
            MethodInfo factory = typeof(AdventureUnitSpawner).GetMethod("CreateEnemy", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(factory, Is.Not.Null);
            string directory = Path.Combine(Application.dataPath, "..", "Logs", "balance");
            Directory.CreateDirectory(directory);
            foreach (int rootBone in Enumerable.Range(8, 11).Append(template.rootBone).Distinct())
            {
                var copy = Object.Instantiate(template);
                try
                {
                    copy.rootBone = rootBone;
                    var enemy = (CombatantSnapshot)factory.Invoke(null, new object[] {
                        copy, new TianZhang.Spatial.HexCoord(1, 0), copy.unarmedBasicAttackProfileId, GrowthRules });
                    var evidence = JsonUtility.FromJson<OrdinaryInputEvidence>(original);
                    evidence.source = "Player: unchanged SPELLS visible ordinary input; enemy: Unity production CreateEnemy on explicit rootBone candidate clone=" + rootBone;
                    evidence.enemyCharacter = CharacterRecord.Capture(CharacterRuntimeProfile.FromDefinition("enemy", copy, GrowthRules).Capture());
                    evidence.enemy = CombatInputEvidence.Capture(enemy);
                    string json = JsonUtility.ToJson(evidence, true);
                    File.WriteAllText(Path.Combine(directory, "rootbone-" + rootBone + ".json"), json);
                    if (rootBone == template.rootBone)
                    {
                        evidence.source = "Player: unchanged SPELLS visible ordinary input; enemy: current imported production template through Unity CreateEnemy; numeric projection, not new UI playthrough";
                        File.WriteAllText(Path.Combine(directory, "production-input.json"), JsonUtility.ToJson(evidence, true));
                    }
                }
                finally { Object.DestroyImmediate(copy); }
            }
            Debug.Log("Guanzhong balance inputs exported through production owner: " + directory);
        }

        [UnityEditor.MenuItem("天章/诊断/导出当前正式两术法角色")]
        public static void ExportCurrentVisibleOrdinaryInputs()
        {
            if (!Application.isPlaying || SceneManager.GetActiveScene().name != GameplaySceneNames.Adventure)
                throw new InvalidOperationException("visible_starter_arts_requires_active_adventure_playmode");
            EncounterCoordinator liveEncounter = FindInActiveScene<EncounterCoordinator>();
            AdventureController liveAdventure = FindInActiveScene<AdventureController>();
            if (liveEncounter == null || !liveEncounter.IsRunning || liveAdventure == null)
                throw new InvalidOperationException("visible_starter_arts_requires_running_encounter");
            AdventureSpawnSet liveSpawned = GetPrivateField<AdventureSpawnSet>(liveEncounter, "spawned");
            CombatSession liveSession = GetPrivateField<CombatSession>(liveEncounter, "session");
            if (GameBootstrap.Runtime == null || GameBootstrap.Runtime.Player == null)
                throw new InvalidOperationException("visible_starter_arts_requires_existing_player_runtime");
            CharacterStateSnapshot liveSource = GameBootstrap.Runtime.Player.Capture();
            int[] liveInnate = { liveSource.Attributes.RootBone, liveSource.Attributes.Spirit,
                liveSource.Attributes.Mind, liveSource.Attributes.Talent, liveSource.Attributes.Fortune };
            string[] expectedArts = { AttackId, RecoveryId };
            if (!liveInnate.All(value => value == 8) ||
                !liveSource.AbilityLoadout.KnownSpells.SequenceEqual(expectedArts) ||
                !liveSource.AbilityLoadout.EquippedSpells.SequenceEqual(expectedArts) ||
                !liveSpawned.Player.AvailableArtProfileIds.SequenceEqual(expectedArts) ||
                !liveSpawned.Player.EquippedArtProfileIds.SequenceEqual(expectedArts) ||
                liveSpawned.Player.RawSpirit != liveSource.Attributes.Spirit ||
                liveSession.SpiritRecoveryEnvironmentMultiplier != 1f)
                throw new InvalidOperationException("visible_starter_arts_ordinary_input_contract_mismatch");
            ICombatResolutionRandomSource liveCombatRandom =
                GetPrivateField<ICombatResolutionRandomSource>(liveEncounter, "combatRandomSource");
            IFormalEncounterRandomSource liveEncounterRandom =
                GetPrivateField<IFormalEncounterRandomSource>(liveAdventure, "randomSource");
            if (liveCombatRandom.GetType() != typeof(SystemCombatResolutionRandomSource) ||
                liveEncounterRandom.GetType() != typeof(SystemFormalEncounterRandomSource))
                throw new InvalidOperationException("visible_starter_arts_controlled_random_source_present");
            var evidence = new OrdinaryInputEvidence
            {
                source = "Current visible PlayMode Adventure encounter; live GameBootstrap player and spawned combatants. " +
                    "originId is unavailable in the runtime snapshot/save and requires the matching visible StartMenu selection evidence. " +
                    "Current combatRandomSource=" + liveCombatRandom.GetType().FullName +
                    "; encounterRandomSource=" + liveEncounterRandom.GetType().FullName +
                    "; current types do not establish prior random-source history or seed configuration.",
                originId = string.Empty,
                innate = liveInnate,
                playerCharacter = CharacterRecord.Capture(liveSource),
                enemyCharacter = CharacterRecord.Capture(CharacterRuntimeProfile.FromDefinition(
                    liveSpawned.Enemy.Id, liveSpawned.EnemyData.combatTemplate, GrowthRules).Capture()),
                enemyId = liveSpawned.EnemyData.enemyId,
                player = CombatInputEvidence.Capture(liveSpawned.Player),
                enemy = CombatInputEvidence.Capture(liveSpawned.Enemy),
                environmentMultiplier = liveSession.SpiritRecoveryEnvironmentMultiplier,
            };
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "spells",
                "visible-ordinary-player-input-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(evidence, true));
            CombatActionBarView liveBar = FindInActiveScene<CombatActionBarView>();
            var state = new VisibleActionEvidence
            {
                frame = Time.frameCount, width = Screen.width, height = Screen.height,
                tick = liveSession.TurnScheduler.CurrentTick, pendingCooldown = liveSession.PendingCtPenalty,
                phase = liveSession.TurnPhase.ToString(), actor = liveSession.CurrentActorId,
                busy = liveEncounter.IsPresenting, player = CombatInputEvidence.Capture(liveSpawned.Player),
                enemy = CombatInputEvidence.Capture(liveSpawned.Enemy),
                controls = GetPrivateField<GameObject>(liveBar, "root").transform.parent
                    .GetComponentsInChildren<RectTransform>(true).Select(rect =>
                    {
                        var corners = new Vector3[4];
                        rect.GetWorldCorners(corners);
                        Text text = rect.GetComponent<Text>();
                        Button button = rect.GetComponent<Button>();
                        return rect.name + "; active=" + rect.gameObject.activeInHierarchy + "; rect=" + rect.rect +
                            "; screen=" + corners[0] + ".." + corners[2] +
                            (button == null ? string.Empty : "; interactable=" + button.interactable) +
                            (text == null ? string.Empty : "; text=" + text.text + "; preferred=" + text.preferredWidth +
                                "x" + text.preferredHeight + "; vertices=" + text.cachedTextGenerator.vertexCount);
                    }).ToArray()
            };
            File.WriteAllText(Path.ChangeExtension(path, ".state.json"), JsonUtility.ToJson(state, true));
            Debug.Log("Current visible ordinary generated input evidence: " + path);
        }

        [Serializable]
        private sealed class VisibleActionEvidence
        {
            public int frame, width, height, tick, pendingCooldown;
            public string phase, actor;
            public bool busy;
            public CombatInputEvidence player, enemy;
            public string[] controls;
        }
#endif

        [Serializable]
        private sealed class OrdinaryInputEvidence
        {
            public string source, originId, enemyId;
            public int[] innate;
            public CharacterRecord playerCharacter, enemyCharacter;
            public CombatInputEvidence player, enemy;
            public float environmentMultiplier;
        }

        [Serializable]
        private sealed class CombatInputEvidence
        {
            public string id, realmStage, visibleRootElement, visibleRootGrade, basicAttackProfileId;
            public int q, r, speed, maximumHealth, currentHealth, maximumSpirit, currentSpirit;
            public int rawSpirit, physicalAttack, soulAttack, physicalDefense, soulDefense, actionPointMaximumUnits, movementEfficiencyMilliUnits;
            public float realmMultiplier;
            public string[] knownArts, equippedArts;
            public static CombatInputEvidence Capture(CombatantSnapshot value) => new CombatInputEvidence
            {
                id = value.Id, q = value.Position.Q, r = value.Position.R, speed = value.Speed,
                maximumHealth = value.MaximumHealth, currentHealth = value.CurrentHealth,
                maximumSpirit = value.MaximumSpirit, currentSpirit = value.CurrentSpirit,
                rawSpirit = value.RawSpirit, physicalAttack = value.PhysicalAttack, soulAttack = value.SoulAttack,
                physicalDefense = value.PhysicalDefense, soulDefense = value.SoulDefense, actionPointMaximumUnits = value.ActionPointMaximumUnits, movementEfficiencyMilliUnits = value.MovementEfficiencyMilliUnits,
                realmMultiplier = value.RealmMultiplier, realmStage = value.RealmStage,
                visibleRootElement = value.VisibleRootElement, visibleRootGrade = value.VisibleRootGrade,
                basicAttackProfileId = value.BasicAttackProfileId,
                knownArts = value.AvailableArtProfileIds.ToArray(), equippedArts = value.EquippedArtProfileIds.ToArray(),
            };
        }

        private static IEnumerator WaitForScene(string name)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                if (SceneManager.GetActiveScene().name == name) { yield return null; yield break; }
                yield return null;
            }
            Assert.Fail("Scene did not load: " + name);
        }

        private static T FindInActiveScene<T>() where T : Component => Object.FindObjectsByType<T>(
            FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(value => value.gameObject.scene == SceneManager.GetActiveScene());

        private static void AssertOriginButtonGeometry(CharacterCreationView view)
        {
            Canvas.ForceUpdateCanvases();
            Text[] labels = GetPrivateField<Text[]>(view, "originLabels");
            Assert.That(labels.Length, Is.EqualTo(4));
            Text summary = GetPrivateField<Text>(view, "summaryText");
            TestContext.WriteLine("Creation panel resolution=" + Screen.width + "x" + Screen.height +
                "; summaryRect=" + DescribeRect(summary.rectTransform) +
                "; summaryPreferred=" + summary.preferredWidth + "x" + summary.preferredHeight);
            foreach (OriginOption origin in CharacterCreationCatalog.Origins)
            {
                Button button = view.GetOriginButton(origin.Id);
                Assert.That(button, Is.Not.Null, origin.Id);
                Text label = button.GetComponentInChildren<Text>(true);
                Assert.That(label, Is.Not.Null, origin.Id);
                TestContext.WriteLine(origin.Id + "; buttonRect=" + DescribeRect((RectTransform)button.transform) +
                    "; labelRect=" + DescribeRect(label.rectTransform) + "; label=" + label.text +
                    "; preferred=" + label.preferredWidth + "x" + label.preferredHeight +
                    "; vertices=" + label.cachedTextGenerator.vertexCount + "; lines=" + label.cachedTextGenerator.lineCount);
            }
            foreach (OriginOption origin in CharacterCreationCatalog.Origins)
            {
                Button button = view.GetOriginButton(origin.Id);
                Text label = button.GetComponentInChildren<Text>(true);
                Assert.That(button.gameObject.activeInHierarchy && button.interactable, Is.True, origin.Id);
                Assert.That(label.gameObject.activeInHierarchy && label.enabled, Is.True, origin.Id);
                StringAssert.Contains(origin.DisplayName, label.text);
                Assert.That(label.rectTransform.rect.width, Is.GreaterThan(0f), origin.Id);
                Assert.That(label.rectTransform.rect.height, Is.GreaterThan(0f), origin.Id);
                Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(label.rectTransform.rect.width + 1f),
                    origin.Id + " label must fit the actual runtime width.");
                Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(label.rectTransform.rect.height + 1f),
                    origin.Id + " label must fit the actual runtime height.");
                Assert.That(label.cachedTextGenerator.vertexCount, Is.GreaterThan(4), origin.Id);
                Assert.That(label.cachedTextGenerator.lineCount, Is.GreaterThan(0), origin.Id);
            }
        }

        private static string DescribeRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return "local=" + rect.rect + "; worldMin=" + corners[0].ToString("F1") +
                "; worldMax=" + corners[2].ToString("F1");
        }

        private static T GetPrivateField<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void SetPrivateField(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);

        private sealed class GuaranteedEncounterRandomSource : IFormalEncounterRandomSource
        {
            public int NextPercent() => 0;
        }

        private sealed class CountingCombatRandomSource : ICombatResolutionRandomSource
        {
            public int Count { get; private set; }
            public float NextPercent() => Count++ % 4 == 0 ? 0f : 99f;
        }

        private sealed class ForwardingSink : ICombatPresentationSink
        {
            private readonly ICombatPresentationSink actual;
            public ForwardingSink(ICombatPresentationSink actual) { this.actual = actual; }
            public CombatHudSnapshot Latest { get; private set; }
            public readonly List<string> Logs = new List<string>();
            public void Present(CombatHudSnapshot snapshot) { Latest = snapshot; actual.Present(snapshot); }
            public void AppendLog(string message) { Logs.Add(message); actual.AppendLog(message); }
            public void ClearLog() { Logs.Clear(); actual.ClearLog(); }
            public void Hide() { actual.Hide(); }
        }

        private sealed class ForwardingUnitPort : ICombatUnitPresentationPort, ICombatPresentationActivity
        {
            private readonly ICombatUnitPresentationPort actual;
            public bool IsPresenting => actual is ICombatPresentationActivity activity && activity.IsPresenting;
            public ForwardingUnitPort(ICombatUnitPresentationPort actual) { this.actual = actual; }
            public readonly List<CombatUnitPresentationEventProjection> Events = new List<CombatUnitPresentationEventProjection>();
            public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> values) => actual.Prepare(values);
            public void Spawn(CombatUnitPresentationDescriptor value) => actual.Spawn(value);
            public void Present(CombatUnitPresentationEventProjection value) { Events.Add(value); actual.Present(value); }
            public void Remove(string id) => actual.Remove(id);
            public void Clear() => actual.Clear();
        }
    }
}
