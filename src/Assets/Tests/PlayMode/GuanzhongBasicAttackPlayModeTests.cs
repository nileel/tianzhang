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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongBasicAttackPlayModeTests
    {
        // Explicit qualified-player fixture for damage/UI/save tests. Never called by production creation.
        public static void PrepareEquippedPlayerFixture()
        {
            var runtime = GameBootstrap.RequireRuntime();
            Assert.AreEqual("凡人", runtime.Player.Progression.RealmStage);
            Assert.IsTrue(string.IsNullOrEmpty(runtime.Player.MainEquipmentBasicAttackProfileId));
            Assert.AreEqual("basic_body_human_fanren", runtime.Player.UnarmedBasicAttackProfileId);
            Assert.AreEqual(0, runtime.GetItemQuantity("item_iron_sword"));
            runtime.Player.RestoreProgression(new TianZhang.Character.CharacterProgressionSnapshot("", "练气", 1.5f));
            runtime.BeginEquippedNewGame(runtime.Player, runtime.Cultivation, "guanzhong_hub",
                UnityEditor.AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset"));
            Assert.AreEqual("basic_iron_sword", runtime.Player.MainEquipmentBasicAttackProfileId);
        }

        // Only the temporary combat snapshot is armed to exercise real death and defeat settlement.
        // The production enemy template remains unarmed; this is not production enemy-content evidence.
        public static void ArmEnemyOutcomeFixture(CombatantSnapshot enemy)
        {
            Assert.AreEqual("basic_body_shijiahou_lianqi", enemy.BasicAttackProfileId);
            enemy.BasicAttackProfileId = "basic_iron_sword";
        }

        // Deterministic outcome input for settlement/save coverage, not balance evidence.
        public sealed class OutcomeFixtureRandom : ICombatResolutionRandomSource
        {
            private readonly CombatSession session;
            private readonly bool victory;
            private int roll;
            public OutcomeFixtureRandom(CombatSession session, bool victory) { this.session = session; this.victory = victory; }
            public float NextPercent()
            {
                bool hit = victory ? session.CurrentActorId == "player" : session.CurrentActorId == "enemy";
                return roll++ % 4 == 0 && hit ? 0 : 99;
            }
        }

        private const string BountyId = "bounty_guanzhong_shijiahou";
        private const string SettlementId = "guanzhong_city";
        private const string AdventureId = "guanzhong_wild";
        private string temporarySaveDirectory;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return null;
            if (!string.IsNullOrWhiteSpace(temporarySaveDirectory) && Directory.Exists(temporarySaveDirectory))
                Directory.Delete(temporarySaveDirectory, true);
            temporarySaveDirectory = null;
        }

        [UnityTest]
        public IEnumerator UnarmedProductionPlayerSavesReentersAttacksAndStoneArmorBeastFinishesItsTurn()
        {
            Screen.SetResolution(1920, 1080, false);
            GameBootstrap staleBootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (staleBootstrap != null)
            {
                Object.Destroy(staleBootstrap.gameObject);
                yield return null;
            }

            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForScene(GameplaySceneNames.StartMenu);

            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            temporarySaveDirectory = Path.Combine(
                Application.temporaryCachePath,
                "TianZhang-01G-" + Guid.NewGuid().ToString("N"));
            SetPrivateField(bootstrap, "slotStore", new GameSaveSlotStore(temporarySaveDirectory));
            const string slotId = "formal-01g";
            CharacterCreationController creation = FindInActiveScene<CharacterCreationController>();
            Assert.IsNotNull(creation, "StartMenu must expose the CharacterCreation feature owner.");
            creation.Open();
            creation.Draft.OriginId = "origin_minor_clan";
            creation.Draft.Innate = InnateAttributeSet.Balanced();
            creation.Submit(slotId, "01G 正式薄切片");
            yield return WaitForScene(GameplaySceneNames.World);

            GameRuntime runtime = GameBootstrap.RequireRuntime();
            Assert.IsNotNull(runtime.Player);
            Assert.AreEqual("guanzhong_hub", runtime.Navigation.WorldNodeId);
            WorldMapController world = FindInActiveScene<WorldMapController>();
            Assert.IsNotNull(world, "WorldScene must expose the WorldMap feature owner.");
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return WaitForScene(GameplaySceneNames.Settlement);

            SettlementController settlement = FindInActiveScene<SettlementController>();
            Assert.IsNotNull(settlement, "SettlementScene must expose the Settlement feature owner.");
            Assert.AreEqual(SettlementId, settlement.CurrentSettlementId);
            ContentCatalogData catalog = GetPrivateField<ContentCatalogData>(settlement, "contentCatalog");
            BountyBoardView board = FindInActiveScene<BountyBoardView>();
            Assert.IsNotNull(board, "SettlementScene must expose the bounty board view.");
            board.Show(catalog, SettlementId, runtime.Bounties);
            board.SubmitAccept(BountyId);
            Assert.AreEqual(BountyStatus.Accepted, runtime.Bounties.GetState(BountyId).Status);

            Assert.AreEqual("body_human", runtime.Player.UnarmedAttackFamilyId);
            Assert.AreEqual("basic_body_human_fanren", runtime.Player.UnarmedBasicAttackProfileId);
            string expectedSave = runtime.CaptureSaveJson();
            settlement.SaveAndReturnToMenu();
            yield return WaitForScene(GameplaySceneNames.StartMenu);
            FindInActiveScene<StartMenuController>().LoadPlayer(slotId);
            yield return WaitForScene(GameplaySceneNames.Settlement);
            runtime = GameBootstrap.RequireRuntime();
            Assert.AreEqual(expectedSave, runtime.CaptureSaveJson());
            Assert.AreEqual("basic_body_human_fanren", runtime.Player.UnarmedBasicAttackProfileId);
            settlement = FindInActiveScene<SettlementController>();

            Assert.IsTrue(settlement.EnterAdventure(AdventureId));
            yield return WaitForScene(GameplaySceneNames.Adventure);
            AdventureController adventure = FindInActiveScene<AdventureController>();
            AdventureInputController adventureInput = FindInActiveScene<AdventureInputController>();
            EncounterCoordinator encounter = FindInActiveScene<EncounterCoordinator>();
            Assert.IsNotNull(adventure);
            Assert.IsNotNull(adventureInput);
            Assert.IsNotNull(encounter);
            yield return WaitForAdventureReady(adventure);
            AssertAdventureNodeButtonReadable("shijiahou_encounter");
            adventure.SetEncounterRandomSource(new SequenceRandomSource(0, 0));
            encounter.SetCombatResolutionRandomSource(new BodyAttackProofRandom());
            Assert.IsTrue(adventureInput.SelectNode("shijiahou_encounter"));
            Assert.AreEqual(AdventureSceneState.Combat, adventure.CurrentState);
            var session = GetPrivateField<CombatSession>(encounter, "session");
            var spawned = GetPrivateField<AdventureSpawnSet>(encounter, "spawned");
            yield return WaitForPlayerTurn(encounter, session, spawned.Player.Id);
            Assert.AreEqual("basic_body_human_fanren", spawned.Player.BasicAttackProfileId);
            Assert.AreEqual("basic_body_shijiahou_lianqi", spawned.Enemy.BasicAttackProfileId);
            var recording = new ForwardingPresentationPort(FindInActiveScene<Static3DCombatUnitPresentationProvider>());
            SetPrivateField(encounter, "unitPresentation", recording);
            var service = new CombatCommandService();
            foreach (string profile in new[] { "", "basic_unarmed", "basic_iron_sword", "missing" })
            {
                var quote = service.Preview(session, new CombatCommand(CombatCommandKind.BasicAttack, "player", "enemy", profile));
                Assert.IsFalse(quote.IsLegal);
                Assert.AreEqual(profile == "basic_iron_sword" ? "attack_profile_not_authorized" : "attack_profile_unresolved", quote.RejectionReason);
            }
            int hp = spawned.Enemy.CurrentHealth, ap = spawned.Player.ActionPointsRemainingUnits;
            var punch = service.Preview(session, new CombatCommand(CombatCommandKind.BasicAttack, "player", "enemy", spawned.Player.BasicAttackProfileId));
            Assert.IsTrue(punch.IsLegal, punch.RejectionReason); Assert.AreEqual(20000, punch.ActionPointCostUnits);
            encounter.RequestBasicAttack("player", "enemy");
            Assert.Less(spawned.Enemy.CurrentHealth, hp);
            Assert.AreEqual(ap - punch.ActionPointCostUnits, spawned.Player.ActionPointsRemainingUnits);
            yield return WaitForMovementPresentation(encounter);
            var guard = service.Preview(session, new CombatCommand(CombatCommandKind.Guard, "player"));
            Assert.IsTrue(guard.IsLegal, guard.RejectionReason);
            var move = service.Preview(session, new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(0, 1)));
            Assert.IsTrue(move.IsLegal, move.RejectionReason);
            encounter.RequestMove("player", 0, 1);
            Assert.AreEqual(ap - punch.ActionPointCostUnits - move.ActionPointCostUnits, spawned.Player.ActionPointsRemainingUnits);
            yield return WaitForMovementPresentation(encounter);
            int playerHealth = spawned.Player.CurrentHealth; long priorTurn = session.TurnSequence;
            encounter.RequestEndTurn("player");
            yield return WaitForPlayerTurn(encounter, session, "player");
            Assert.Less(spawned.Player.CurrentHealth, playerHealth);
            Assert.Greater(session.TurnSequence, priorTurn);
            Assert.IsTrue(recording.Events.Any(value => value.ActorCombatantId == "enemy" && value.PresentationEvent == CombatUnitPresentationEvent.Attack));
            Assert.AreEqual("basic_body_shijiahou_lianqi", spawned.Enemy.BasicAttackProfileId);
            Assert.AreEqual(0, runtime.GetItemQuantity("item_iron_sword"));
            Assert.AreEqual(0, runtime.GetItemQuantity("item_iron_blade"));
            Assert.AreEqual("basic_body_human_fanren", runtime.Player.UnarmedBasicAttackProfileId);
            TestContext.WriteLine($"Formal saved body proof: player={spawned.Player.BasicAttackProfileId}, enemy={spawned.Enemy.BasicAttackProfileId}, quoted/paid={punch.ActionPointCostUnits}, playerHit={hp - spawned.Enemy.CurrentHealth}, enemyHit={playerHealth - spawned.Player.CurrentHealth}, enemyAttacks={recording.Events.Count(value => value.ActorCombatantId == "enemy" && value.PresentationEvent == CombatUnitPresentationEvent.Attack)}, turn={priorTurn}->{session.TurnSequence}");
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForScene(GameplaySceneNames.StartMenu);
        }

        // Fixed legal resolution samples make this functional proof repeatable; all character/content inputs are production inputs.
        private sealed class BodyAttackProofRandom : ICombatResolutionRandomSource
        {
            public float NextPercent() => 50f;
        }

        [UnityTest]
        public IEnumerator FormalFeatureSceneChainCreatesFightsClaimsSavesAndLoads()
        {
            Screen.SetResolution(1920, 1080, false);
            GameBootstrap staleBootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (staleBootstrap != null)
            {
                Object.Destroy(staleBootstrap.gameObject);
                yield return null;
            }

            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForScene(GameplaySceneNames.StartMenu);

            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            temporarySaveDirectory = Path.Combine(
                Application.temporaryCachePath,
                "TianZhang-01G-" + Guid.NewGuid().ToString("N"));
            SetPrivateField(bootstrap, "slotStore", new GameSaveSlotStore(temporarySaveDirectory));
            const string slotId = "formal-01g";
            CharacterCreationController creation = FindInActiveScene<CharacterCreationController>();
            Assert.IsNotNull(creation, "StartMenu must expose the CharacterCreation feature owner.");
            creation.Open();
            creation.Draft.OriginId = "origin_minor_clan";
            creation.Draft.Innate = new InnateAttributeSet(15, 3, 6, 3, 3);
            creation.Submit(slotId, "01G 正式薄切片");
            yield return WaitForScene(GameplaySceneNames.World);
            GuanzhongBasicAttackPlayModeTests.PrepareEquippedPlayerFixture();

            GameRuntime runtime = GameBootstrap.RequireRuntime();
            Assert.IsNotNull(runtime.Player);
            Assert.AreEqual("guanzhong_hub", runtime.Navigation.WorldNodeId);
            WorldMapController world = FindInActiveScene<WorldMapController>();
            Assert.IsNotNull(world, "WorldScene must expose the WorldMap feature owner.");
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return WaitForScene(GameplaySceneNames.Settlement);

            SettlementController settlement = FindInActiveScene<SettlementController>();
            Assert.IsNotNull(settlement, "SettlementScene must expose the Settlement feature owner.");
            Assert.AreEqual(SettlementId, settlement.CurrentSettlementId);
            ContentCatalogData catalog = GetPrivateField<ContentCatalogData>(settlement, "contentCatalog");
            BountyBoardView board = FindInActiveScene<BountyBoardView>();
            Assert.IsNotNull(board, "SettlementScene must expose the bounty board view.");
            board.Show(catalog, SettlementId, runtime.Bounties);
            board.SubmitAccept(BountyId);
            Assert.AreEqual(BountyStatus.Accepted, runtime.Bounties.GetState(BountyId).Status);

            Assert.IsTrue(settlement.EnterAdventure(AdventureId));
            yield return WaitForScene(GameplaySceneNames.Adventure);
            AdventureController adventure = FindInActiveScene<AdventureController>();
            AdventureInputController adventureInput = FindInActiveScene<AdventureInputController>();
            EncounterCoordinator encounter = FindInActiveScene<EncounterCoordinator>();
            Assert.IsNotNull(adventure);
            Assert.IsNotNull(adventureInput);
            Assert.IsNotNull(encounter);
            yield return WaitForAdventureReady(adventure);
            AssertAdventureNodeButtonReadable("shijiahou_encounter");
            adventure.SetEncounterRandomSource(new SequenceRandomSource(0, 0));
            Assert.IsTrue(adventureInput.SelectNode("shijiahou_encounter"));
            Assert.AreEqual(AdventureSceneState.Combat, adventure.CurrentState);
            Static3DCombatUnitPresentationProvider provider = FindInActiveScene<Static3DCombatUnitPresentationProvider>();
            Assert.IsNotNull(provider, "AdventureScene must bind the single formal static-3D provider.");
            AssertFormalProviderUnit(provider, "player", Color.cyan);
            AssertFormalProviderUnit(provider, "enemy", Color.red);
            AssertFormalBattlefieldPresentation();
            var outcome = GetPrivateField<AdventureSpawnSet>(encounter, "spawned").Enemy;
            ArmEnemyOutcomeFixture(outcome);
            yield return WaitForPlayerTurn(encounter, GetPrivateField<CombatSession>(encounter, "session"), "player");
            outcome.ReceiveDamage(outcome.CurrentHealth - 1); // Explicit one-hit settlement fixture.
            encounter.SetCombatResolutionRandomSource(new OutcomeFixtureRandom(
                GetPrivateField<CombatSession>(encounter, "session"), true));
            yield return null;

            Assert.IsInstanceOf<ICombatCommandHandler>(encounter);
            ICombatCommandHandler combatCommands = encounter;
            for (float deadline = Time.realtimeSinceStartup + 60f;
                 Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name == GameplaySceneNames.Adventure;)
            {
                combatCommands.RequestBasicAttack("player", "enemy");
                yield return WaitForMovementPresentation(encounter);
                if (SceneManager.GetActiveScene().name != GameplaySceneNames.Adventure) break;
                encounter.RequestEndTurn("player");
                yield return null;
            }
            Assert.AreEqual(
                GameplaySceneNames.Settlement,
                SceneManager.GetActiveScene().name,
                "The formal combat did not resolve and return to its source settlement.");

            runtime = GameBootstrap.RequireRuntime();
            BountyState completed = runtime.Bounties.GetState(BountyId);
            Assert.AreEqual(BountyStatus.ObjectiveCompleted, completed.Status);
            Assert.AreEqual(1, completed.Progress);
            Assert.AreEqual(1, InventoryQuantity(runtime, "item_shijia_piece"));
            Assert.AreEqual(1, InventoryQuantity(runtime, "item_lingshi_low"));
            Assert.AreEqual(SettlementId, runtime.Navigation.SettlementId);
            Assert.IsNull(runtime.Navigation.AdventureId);

            settlement = FindInActiveScene<SettlementController>();
            catalog = GetPrivateField<ContentCatalogData>(settlement, "contentCatalog");
            board = FindInActiveScene<BountyBoardView>();
            board.Show(catalog, SettlementId, runtime.Bounties);
            board.SubmitClaim(BountyId);
            Assert.AreEqual(BountyStatus.Claimed, runtime.Bounties.GetState(BountyId).Status);
            Assert.AreEqual(1, InventoryQuantity(runtime, "item_shijia_piece"));
            Assert.AreEqual(4, InventoryQuantity(runtime, "item_lingshi_low"));
            string expectedSave = runtime.CaptureSaveJson();

            settlement.SaveAndReturnToMenu();
            yield return WaitForScene(GameplaySceneNames.StartMenu);
            StartMenuSceneInstaller startMenu = FindInActiveScene<StartMenuSceneInstaller>();
            Assert.IsNotNull(startMenu);
            Assert.IsTrue(startMenu.ListSlots()[0].CanLoad);
            StartMenuController startMenuController = FindInActiveScene<StartMenuController>();
            startMenuController.LoadPlayer(slotId);
            yield return WaitForScene(GameplaySceneNames.Settlement);

            GameRuntime restored = GameBootstrap.RequireRuntime();
            Assert.AreEqual(expectedSave, restored.CaptureSaveJson());
            Assert.AreEqual(BountyStatus.Claimed, restored.Bounties.GetState(BountyId).Status);
            Assert.AreEqual(1, restored.Bounties.GetState(BountyId).Progress);
            Assert.AreEqual(1, InventoryQuantity(restored, "item_shijia_piece"));
            Assert.AreEqual(4, InventoryQuantity(restored, "item_lingshi_low"));
            Assert.AreEqual("guanzhong_hub", restored.Navigation.WorldNodeId);
            Assert.AreEqual(SettlementId, restored.Navigation.SettlementId);
            Assert.IsNull(restored.Navigation.AdventureId);
        }

        [UnityTest]
        public IEnumerator FormalStartMenuSessionSupportsBothMoveActionOrdersAndProjectsTheCanonicalPath()
        {
            GameBootstrap staleBootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (staleBootstrap != null) { Object.Destroy(staleBootstrap.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForScene(GameplaySceneNames.StartMenu);
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            temporarySaveDirectory = Path.Combine(Application.temporaryCachePath,
                "TianZhang-MoveRule-" + Guid.NewGuid().ToString("N"));
            SetPrivateField(bootstrap, "slotStore", new GameSaveSlotStore(temporarySaveDirectory));
            CharacterCreationController creation = FindInActiveScene<CharacterCreationController>();
            creation.Open();
            creation.Draft.OriginId = "origin_minor_clan";
            creation.Draft.Innate = new InnateAttributeSet(15, 3, 6, 3, 3);
            creation.Submit("formal-move-rule", "移动规则验证");
            yield return WaitForScene(GameplaySceneNames.World);
            GuanzhongBasicAttackPlayModeTests.PrepareEquippedPlayerFixture();
            WorldMapController world = FindInActiveScene<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return WaitForScene(GameplaySceneNames.Settlement);
            SettlementController settlement = FindInActiveScene<SettlementController>();
            BountyBoardView board = FindInActiveScene<BountyBoardView>();
            board.Show(GetPrivateField<ContentCatalogData>(settlement, "contentCatalog"),
                SettlementId, GameBootstrap.RequireRuntime().Bounties);
            board.SubmitAccept(BountyId);
            Assert.IsTrue(settlement.EnterAdventure(AdventureId));
            yield return WaitForScene(GameplaySceneNames.Adventure);
            AdventureController adventure = FindInActiveScene<AdventureController>();
            yield return WaitForAdventureReady(adventure);
            adventure.SetEncounterRandomSource(new SequenceRandomSource(0, 0));
            EncounterCoordinator encounter = FindInActiveScene<EncounterCoordinator>();
            encounter.SetCombatResolutionRandomSource(new SystemCombatResolutionRandomSource(1729));
            Assert.IsTrue(FindInActiveScene<AdventureInputController>().SelectNode("shijiahou_encounter"));
            CombatSession session = GetPrivateField<CombatSession>(encounter, "session");
            AdventureSpawnSet spawned = GetPrivateField<AdventureSpawnSet>(encounter, "spawned");
            CombatantSnapshot player = spawned.Player, enemy = spawned.Enemy;
            Static3DCombatUnitPresentationProvider provider = FindInActiveScene<Static3DCombatUnitPresentationProvider>();
            var recording = new ForwardingPresentationPort(provider);
            SetPrivateField(encounter, "unitPresentation", recording);
            AssertFormalBattlefieldPresentation();
            Assert.AreEqual(100000, player.ActionPointMaximumUnits);

            yield return WaitForPlayerTurn(encounter, session, player.Id);
            int tickBeforeMove = session.TurnScheduler.CurrentTick;
            foreach (HexCoord rejectedDestination in new[]
                     { enemy.Position, new HexCoord(2, 1), new HexCoord(-1, 0), new HexCoord(5, 5) })
            {
                HexCoord positionBefore = player.Position;
                int remainingBefore = player.ActionPointsRemainingUnits, facingBefore = player.Facing;
                Assert.IsFalse(session.SpatialQuery.QueryMovement(positionBefore, rejectedDestination,
                    remainingBefore, player.MovementEfficiencyMilliUnits, new[] { enemy.Position }).IsReachable);
                ((ICombatCommandHandler)encounter).RequestMove(player.Id, rejectedDestination.Q, rejectedDestination.R);
                Assert.That(player.Position, Is.EqualTo(positionBefore));
                Assert.AreEqual(remainingBefore, player.ActionPointsRemainingUnits);
                Assert.AreEqual(facingBefore, player.Facing);
                Assert.AreEqual(tickBeforeMove, session.TurnScheduler.CurrentTick);
                Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
            }
            HexCoord firstDestination = new HexCoord(0, 1);
            CombatMovementQueryResult firstPath = session.SpatialQuery.QueryMovement(
                player.Position, firstDestination, player.ActionPointsRemainingUnits, player.MovementEfficiencyMilliUnits, new[] { enemy.Position });
            Assert.IsTrue(firstPath.IsReachable, firstPath.Reason);
            int movementBefore = player.ActionPointsRemainingUnits;
            ((ICombatCommandHandler)encounter).RequestMove(player.Id, firstDestination.Q, firstDestination.R);
            Assert.That(player.Position, Is.EqualTo(firstDestination));
            Assert.AreEqual(movementBefore - firstPath.ActionPointCostUnits, player.ActionPointsRemainingUnits);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
            Assert.AreEqual(tickBeforeMove, session.TurnScheduler.CurrentTick);
            yield return WaitForMovementPresentation(encounter);
            AssertFormalMovementProjection(recording, provider, player, firstPath);
            ((ICombatCommandHandler)encounter).RequestBasicAttack(player.Id, enemy.Id);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase,
                "A legal movement followed by the equipped sword leaves the quoted remainder.");
            Assert.AreEqual(player.ActionPointMaximumUnits - 40000 - firstPath.ActionPointCostUnits, player.ActionPointsRemainingUnits);
            Assert.IsTrue(provider.TryGetPresentedUnit(player.Id, out GameObject playerRoot));
            Vector3 startedAttackScale = playerRoot.transform.localScale;
            Assert.AreNotEqual(Vector3.one, startedAttackScale, "The actual provider must have started its attack presentation.");
            int eventsBeforeEnd = recording.Events.Count;
            encounter.RequestEndTurn(player.Id);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase, "EndTurn during presentation must not consume the remaining AP.");
            Assert.AreEqual(startedAttackScale, playerRoot.transform.localScale);
            yield return WaitForMovementPresentation(encounter);
            encounter.RequestEndTurn(player.Id);
            Assert.AreEqual(CombatTurnPhase.Finished, session.TurnPhase);
            Assert.IsFalse(recording.Events.Skip(eventsBeforeEnd).Any(value =>
                value.PresentationEvent == CombatUnitPresentationEvent.Idle), "Turn exhaustion must not add an Idle event.");
            Assert.AreEqual(Vector3.one, playerRoot.transform.localScale, "The attack must finish before EndTurn is accepted.");

            yield return WaitForPlayerTurn(encounter, session, player.Id);
            Assert.AreEqual(player.ActionPointMaximumUnits, player.ActionPointsRemainingUnits);
            ((ICombatCommandHandler)encounter).RequestBasicAttack(player.Id, enemy.Id);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
            yield return WaitForMovementPresentation(encounter);
            HexCoord secondDestination = new HexCoord(0, 0);
            CombatMovementQueryResult secondPath = session.SpatialQuery.QueryMovement(
                player.Position, secondDestination, player.ActionPointsRemainingUnits, player.MovementEfficiencyMilliUnits, new[] { enemy.Position });
            Assert.IsTrue(secondPath.IsReachable, secondPath.Reason);
            movementBefore = player.ActionPointsRemainingUnits;
            ((ICombatCommandHandler)encounter).RequestMove(player.Id, secondDestination.Q, secondDestination.R);
            Assert.That(player.Position, Is.EqualTo(secondDestination));
            Assert.AreEqual(movementBefore - secondPath.ActionPointCostUnits, player.ActionPointsRemainingUnits);
            Assert.AreEqual(player.ActionPointMaximumUnits - 40000 - secondPath.ActionPointCostUnits, player.ActionPointsRemainingUnits);
            Assert.AreEqual(CombatTurnPhase.Active, session.TurnPhase);
            yield return WaitForMovementPresentation(encounter);
            AssertFormalMovementProjection(recording, provider, player, secondPath);
            TestContext.WriteLine($"Formal movement: firstCost={firstPath.ActionPointCostUnits}, secondCost={secondPath.ActionPointCostUnits}, " +
                $"maxMovement={player.ActionPointMaximumUnits}, final={player.Position}, facing={player.Facing}");
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForScene(GameplaySceneNames.StartMenu);
        }

        private static IEnumerator WaitForMovementPresentation(EncounterCoordinator encounter)
        {
            for (float deadline = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < deadline && encounter.IsPresenting;) yield return null;
            Assert.IsFalse(encounter.IsPresenting, "Presentation did not release its command lock.");
            yield return null;
        }

        private static IEnumerator WaitForPlayerTurn(EncounterCoordinator encounter, CombatSession session, string playerId)
        {
            for (float deadline = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < deadline;)
            {
                Assert.IsTrue(encounter != null && encounter.IsRunning, "Formal combat ended before the movement scenario completed.");
                if (session.CurrentActorId == playerId && session.TurnPhase == CombatTurnPhase.Active && !encounter.IsPresenting)
                    yield break;
                yield return null;
            }
            Assert.Fail("Formal combat did not yield the player's next turn.");
        }

        private static void AssertFormalMovementProjection(ForwardingPresentationPort recording,
            Static3DCombatUnitPresentationProvider provider, CombatantSnapshot player, CombatMovementQueryResult query)
        {
            CombatUnitPresentationEventProjection movement = recording.Events.Last(value =>
                value.ActorCombatantId == player.Id && value.PresentationEvent == CombatUnitPresentationEvent.Move);
            CollectionAssert.AreEqual(query.Path.Select(value => new CombatUnitPresentationHex(value.Q, value.R)).ToArray(), movement.MovementPath);
            Assert.AreEqual(player.Position.Q, movement.EndPosition.Q);
            Assert.AreEqual(player.Position.R, movement.EndPosition.R);
            Assert.AreEqual(player.Facing, movement.Facing);
            Assert.IsTrue(provider.TryGetPresentedUnit(player.Id, out GameObject root));
            Assert.Less(Vector3.Distance(Static3DCombatUnitPresentationProvider.HexToWorld(movement.EndPosition), root.transform.position), 0.0001f);
            TestContext.WriteLine("Formal provider movement path: " + string.Join(" -> ", movement.MovementPath.Select(value => $"({value.Q},{value.R})")));
        }

        [UnityTest]
        public IEnumerator FormalPlayerExecutionSamplesHitCriticalAndBlockThresholds()
        {
            var hitBoundary = new SequenceCombatRandomSource(50f, 50f, 50f, 50f);
            var missAboveBoundary = new SequenceCombatRandomSource(51f, 0f, 0f, 0f);
            Assert.Greater(ExecutePlayerBasicAttack(hitBoundary), 0);
            Assert.AreEqual(0, ExecutePlayerBasicAttack(missAboveBoundary));
            Assert.AreEqual(4, hitBoundary.Count);
            Assert.AreEqual(4, missAboveBoundary.Count);

            var criticalBelowBoundary = new SequenceCombatRandomSource(0f, 49f, 99f, 99f);
            var criticalAtBoundary = new SequenceCombatRandomSource(0f, 50f, 99f, 99f);
            int criticalDamage = ExecutePlayerBasicAttack(criticalBelowBoundary);
            int normalDamage = ExecutePlayerBasicAttack(criticalAtBoundary);
            Assert.Greater(criticalDamage, normalDamage);

            var blockBelowBoundary = new SequenceCombatRandomSource(0f, 99f, 49f, 99f);
            var blockAtBoundary = new SequenceCombatRandomSource(0f, 99f, 50f, 99f);
            int blockedDamage = ExecutePlayerBasicAttack(blockBelowBoundary);
            int unblockedDamage = ExecutePlayerBasicAttack(blockAtBoundary);
            Assert.Less(blockedDamage, unblockedDamage);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FormalEnemyRunCombatBranchUsesTheSharedSampler()
        {
            CombatSession session = CreateCombatSession(1, 100, out CombatantSnapshot player, out CombatantSnapshot enemy);
            var source = new SequenceCombatRandomSource(50f, 50f, 50f, 50f);
            EncounterCoordinator coordinator = CreateCoordinator(session, player, enemy, source, out GameObject host, out EnemyData enemyData);
            SetPrivateField(coordinator, "enemyPolicy", new LegalActionAI());
            MethodInfo runCombat = typeof(EncounterCoordinator).GetMethod(
                "RunCombat",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(runCombat);
            int healthBefore = player.CurrentHealth;
            Coroutine coroutine = coordinator.StartCoroutine((IEnumerator)runCombat.Invoke(coordinator, null));

            for (int frame = 0; frame < 10 && source.Count == 0; frame++)
                yield return null;

            Assert.AreEqual(4, source.Count, "The formal enemy action did not sample exactly four rolls.");
            Assert.Less(player.CurrentHealth, healthBefore);
            coordinator.StopCoroutine(coroutine);
            Object.Destroy(host);
            Object.Destroy(enemyData);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidEnumerationAndNonAttackCommandsDoNotConsumeSamples()
        {
            CombatSession session = CreateCombatSession(100, 1, out CombatantSnapshot player, out CombatantSnapshot enemy);
            var source = new SequenceCombatRandomSource();
            EncounterCoordinator coordinator = CreateCoordinator(session, player, enemy, source, out GameObject host, out EnemyData enemyData);
            CombatTurnAdvance advance = new CombatCommandService().AdvanceUntilAction(session);
            Assert.AreEqual("player", advance.ActorId);
            SetPrivateField(coordinator, "acceptsPlayerCommand", true);

            Assert.IsNotEmpty(new CombatLegalActionService().GetLegalActions(session, "player"));
            Assert.AreEqual(0, source.Count);
            ICombatCommandHandler commands = coordinator;
            commands.RequestBasicAttack("player", "missing");
            Assert.AreEqual(0, source.Count, "An invalid attack consumed combat samples.");
            commands.RequestGuard("player");
            Assert.AreEqual(0, source.Count, "A non-attack command consumed combat samples.");

            Object.Destroy(host);
            Object.Destroy(enemyData);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnauthorizedBasicAttackDoesNotConsumeCombatSamples()
        {
            CombatantSnapshot player = new CombatantSnapshot("player",
                CombatTeam.Player,
                new HexCoord(0, 0),
                100,
                500,
                500,
                60,
                0,
                0,
                0,
                100000,
                2000,
                1f)
            {
                CriticalRate = 50f,
                DodgeRate = 50f,
                BlockRate = 50f,
                BlockReduction = 50f,
                Facing = 0,
                BasicAttackProfileId = "basic_unarmed",
            };
            CombatantSnapshot enemy = new CombatantSnapshot("enemy",
                CombatTeam.Enemy,
                new HexCoord(1, 0),
                1,
                500,
                500,
                60,
                0,
                0,
                0,
                100000,
                2000,
                1f)
            {
                CriticalRate = 50f,
                DodgeRate = 50f,
                BlockRate = 50f,
                BlockReduction = 50f,
                Facing = 3,
                BasicAttackProfileId = "basic_unarmed",
            };
            CombatSession session = new CombatSession(new[] { player, enemy },
                new[]
                {
                    new CombatAttackProfile(
                        "basic_unarmed", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 50000, 25000, 0, 0,
                        physicalMultiplier: 1f),
                    new CombatAttackProfile(
                        "foreign_basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1, 50000, 25000, 0, 0,
                        physicalMultiplier: 1f),
                },
                new AlwaysInRangeCombatSpatialQuery(),
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) }, ruleSet: SamplerRules());

            var source = new SequenceCombatRandomSource();
            EncounterCoordinator coordinator = CreateCoordinator(session, player, enemy, source, out GameObject host, out EnemyData enemyData);
            CombatTurnAdvance advance = new CombatCommandService().AdvanceUntilAction(session);
            Assert.AreEqual("player", advance.ActorId);
            SetPrivateField(coordinator, "acceptsPlayerCommand", true);
            SetPrivateField(coordinator, "spawned", new AdventureSpawnSet(
                player, enemy, enemyData, "foreign_basic", "basic_unarmed", Array.Empty<string>()));

            var rejected = (CombatActionResult)typeof(EncounterCoordinator).GetMethod("ExecuteCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(coordinator, new object[] { new CombatCommand(CombatCommandKind.BasicAttack, "player", "enemy", "foreign_basic"), null });
            Assert.IsFalse(rejected.Succeeded);
            Assert.AreEqual(0, source.Count, "An unauthorized attack consumed combat samples.");

            Object.Destroy(host);
            Object.Destroy(enemyData);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SeededSamplerReplaysTheSameLegalCommandSequence()
        {
            var first = new SystemCombatResolutionRandomSource(1729);
            var second = new SystemCombatResolutionRandomSource(1729);
            var firstDamage = new int[3];
            var secondDamage = new int[3];
            for (int index = 0; index < firstDamage.Length; index++)
            {
                firstDamage[index] = ExecutePlayerBasicAttack(first);
                secondDamage[index] = ExecutePlayerBasicAttack(second);
            }
            CollectionAssert.AreEqual(firstDamage, secondDamage);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CombatEntryRejectsMissingCommittedProfiles()
        {
            var adapter = new CombatEntryAdapter(new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) }, UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset"));
            bool created = adapter.TryCreateSession(
                null,
                new AttackProfileData[0],
                null,
                out CombatSession session,
                out string reason);
            Assert.IsFalse(created);
            Assert.IsNull(session);
            Assert.AreEqual("adventure_spawn_set_missing", reason);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnitSpawnerRejectsMissingPlayerWithoutFallback()
        {
            var go = new GameObject("AdventureUnitSpawnerTest");
            AdventureUnitSpawner spawner = go.AddComponent<AdventureUnitSpawner>();
            bool spawned = spawner.TrySpawn(
                null,
                ScriptableObject.CreateInstance<ContentCatalogData>(),
                new AdventureNodeData { nodeId = "start", q = 0, r = 0 },
                new AdventureNodeData { nodeId = "encounter", q = 1, r = 0, contentId = "enemy" },
                out AdventureSpawnSet result,
                out string reason);
            Assert.IsFalse(spawned);
            Assert.IsNull(result);
            Assert.AreEqual("adventure_player_missing", reason);
            Object.Destroy(go);
            yield return null;
        }

        private static int ExecutePlayerBasicAttack(ICombatResolutionRandomSource source)
        {
            CombatSession session = CreateCombatSession(100, 1, out CombatantSnapshot player, out CombatantSnapshot enemy);
            EncounterCoordinator coordinator = CreateCoordinator(session, player, enemy, source, out GameObject host, out EnemyData enemyData);
            CombatTurnAdvance advance = new CombatCommandService().AdvanceUntilAction(session);
            Assert.AreEqual("player", advance.ActorId);
            SetPrivateField(coordinator, "acceptsPlayerCommand", true);
            int healthBefore = enemy.CurrentHealth;
            ((ICombatCommandHandler)coordinator).RequestBasicAttack("player", "enemy");
            int damage = healthBefore - enemy.CurrentHealth;
            Object.Destroy(host);
            Object.Destroy(enemyData);
            return damage;
        }

        private static EncounterCoordinator CreateCoordinator(
            CombatSession session,
            CombatantSnapshot player,
            CombatantSnapshot enemy,
            ICombatResolutionRandomSource source,
            out GameObject host,
            out EnemyData enemyData)
        {
            host = new GameObject("CombatResolutionSamplerTest");
            EncounterCoordinator coordinator = host.AddComponent<EncounterCoordinator>();
            enemyData = ScriptableObject.CreateInstance<EnemyData>();
            enemyData.displayNameKey = "enemy_test";
            coordinator.Configure(
                new RecordingCombatPresentationSink(),
                new RecordingCombatUnitPresentationPort(),
                CreatePresentationCatalog(),
                (_, _) => { });
            coordinator.SetCombatResolutionRandomSource(source);
            SetPrivateField(coordinator, "session", session);
            SetPrivateField(coordinator, "spawned", new AdventureSpawnSet(
                player,
                enemy,
                enemyData,
                "basic_unarmed",
                "basic_unarmed",
                Array.Empty<string>()));
            return coordinator;
        }

        private static CombatRuleSet SamplerRules()
        {
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
            return CombatRuleSet.Parse(new[] { CombatRuleSet.RealmColumns }.Concat(data.ExportRealms()).ToArray(),
                new[] { CombatRuleSet.TierColumns }.Concat(data.ExportTiers()).ToArray(),
                new[] { CombatRuleSet.EconomyColumns }.Concat(data.ExportEconomy()).ToArray(), data.sourceDigest);
        }

        private static CombatSession CreateCombatSession(
            int playerSpeed,
            int enemySpeed,
            out CombatantSnapshot player,
            out CombatantSnapshot enemy)
        {
            player = new CombatantSnapshot("player",
                CombatTeam.Player,
                new HexCoord(0, 0),
                playerSpeed,
                500,
                500,
                60,
                0,
                0,
                0,
                100000,
                2000,
                1f)
            {
                CriticalRate = 50f,
                DodgeRate = 50f,
                BlockRate = 50f,
                BlockReduction = 50f,
                Facing = 0,
                BasicAttackProfileId = "basic_unarmed",
            };
            enemy = new CombatantSnapshot("enemy",
                CombatTeam.Enemy,
                new HexCoord(1, 0),
                enemySpeed,
                500,
                500,
                60,
                0,
                0,
                0,
                100000,
                2000,
                1f)
            {
                CriticalRate = 50f,
                DodgeRate = 50f,
                BlockRate = 50f,
                BlockReduction = 50f,
                Facing = 3,
                BasicAttackProfileId = "basic_unarmed",
            };
            return new CombatSession(new[] { player, enemy },
                new[]
                {
                    new CombatAttackProfile(
                        "basic_unarmed",
                        CombatAttackKind.Basic,
                        CombatAttackEffect.Physical,
                        1,
                        1,
                        50000,
                        25000,
                        0,
                        0,
                        physicalMultiplier: 1f),
                },
                new AlwaysInRangeCombatSpatialQuery(),
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) }, ruleSet: SamplerRules());
        }

        private static IEnumerator WaitForScene(string sceneName)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                if (SceneManager.GetActiveScene().name == sceneName)
                {
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("Scene did not load: " + sceneName);
        }

        private static IEnumerator WaitForAdventureReady(AdventureController controller)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                if (controller != null && controller.Session != null &&
                    controller.CurrentState == AdventureSceneState.Exploration)
                    yield break;
                yield return null;
            }
            Assert.Fail("AdventureScene did not reach the exploration state.");
        }

        private static T FindInActiveScene<T>()
            where T : Component
        {
            Scene activeScene = SceneManager.GetActiveScene();
            foreach (T value in Object.FindObjectsByType<T>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                if (value.gameObject.scene == activeScene) return value;
            return null;
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            field.SetValue(target, value);
        }

        private static int InventoryQuantity(GameRuntime runtime, string itemId)
        {
            foreach (InventoryRecord record in runtime.CaptureSave().inventory)
                if (record.itemId == itemId) return record.quantity;
            return 0;
        }

        private static void AssertAdventureNodeButtonReadable(string nodeId)
        {
            GameObject buttonObject = GameObject.Find("AdventureNode_" + nodeId);
            Assert.IsNotNull(buttonObject, "Adventure HUD did not create the expected runtime node button.");
            Image image = buttonObject.GetComponent<Image>();
            Button button = buttonObject.GetComponent<Button>();
            Text label = buttonObject.GetComponentInChildren<Text>(true);
            Assert.IsNotNull(image);
            Assert.IsNotNull(button);
            Assert.IsNotNull(label);
            Type database = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
            Assert.IsNotNull(database);
            MethodInfo loadAll = database.GetMethod("LoadAllAssetsAtPath", new[] { typeof(string) });
            MethodInfo loadAsset = database.GetMethod("LoadAssetAtPath", new[] { typeof(string), typeof(Type) });
            Assert.IsNotNull(loadAll);
            Assert.IsNotNull(loadAsset);
            Sprite[] approvedSprites = ((Object[])loadAll.Invoke(null, new object[]
                { "Assets/Art/UI/Guanzhong/Guanzhong_UI_Atlas.png" })).OfType<Sprite>().ToArray();
            Sprite normal = approvedSprites.Single(sprite => sprite.name == "Guanzhong_Button_Normal");
            Sprite hover = approvedSprites.Single(sprite => sprite.name == "Guanzhong_Button_Hover");
            Sprite selected = approvedSprites.Single(sprite => sprite.name == "Guanzhong_Button_Selected");
            var font = loadAsset.Invoke(null, new object[]
                { "Assets/Art/UI/Guanzhong/Fonts/GuanzhongChinese.otf", typeof(Font) }) as Font;
            Assert.IsNotNull(font);
            Assert.AreEqual(normal, image.sprite);
            Assert.AreEqual(Image.Type.Sliced, image.type);
            Assert.AreEqual(Color.white, image.color, "The approved atlas must retain its untinted colors.");
            Assert.AreEqual(Selectable.Transition.SpriteSwap, button.transition);
            Assert.AreEqual(hover, button.spriteState.highlightedSprite);
            Assert.AreEqual(selected, button.spriteState.pressedSprite);
            Assert.AreEqual(selected, button.spriteState.selectedSprite);
            Assert.AreEqual(normal, button.spriteState.disabledSprite);
            Assert.AreEqual(font, label.font);
            Assert.AreEqual((Color)new Color32(244, 234, 212, 255), label.color);
        }

        private static void AssertFormalProviderUnit(
            Static3DCombatUnitPresentationProvider provider,
            string combatantId,
            Color expectedBaseColor)
        {
            Assert.IsTrue(provider.TryGetPresentedUnit(combatantId, out GameObject unit));
            Assert.Greater(unit.transform.position.y, 0f);
            Assert.Zero(unit.GetComponentsInChildren<SpriteRenderer>(true).Length);
            MeshRenderer[] renderers = unit.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(2, renderers.Length, "The provider must create exactly the approved body and independent base.");
            int baseColorId = Shader.PropertyToID("_BaseColor");
            Transform baseRoot = unit.transform.Find("Static3DCombatBase");
            Assert.IsNotNull(baseRoot);
            MeshRenderer baseRenderer = baseRoot.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(baseRenderer);
            var properties = new MaterialPropertyBlock();
            baseRenderer.GetPropertyBlock(properties, 1);
            Color actual = properties.GetColor(baseColorId);
            Assert.That(actual.r, Is.EqualTo(expectedBaseColor.r).Within(0.001f));
            Assert.That(actual.g, Is.EqualTo(expectedBaseColor.g).Within(0.001f));
            Assert.That(actual.b, Is.EqualTo(expectedBaseColor.b).Within(0.001f));
        }

        private static void AssertFormalBattlefieldPresentation()
        {
            GameObject battlefield = FindSceneObjectIncludingInactive("GuanzhongBattlefield");
            Assert.IsNotNull(battlefield, "AdventureScene is missing the functional Guanzhong battlefield.");
            Assert.IsTrue(battlefield.activeInHierarchy, "The formal Adventure route must show the functional battlefield.");
            Assert.AreEqual(36, battlefield.transform.childCount);
            Assert.AreEqual(4, battlefield.GetComponentsInChildren<Transform>(true).Count(value =>
                value.name.StartsWith("GuanzhongObstacle_", StringComparison.Ordinal)));
            Assert.Zero(battlefield.GetComponentsInChildren<Collider>(true).Length);
            GameObject backdrop = FindSceneObjectIncludingInactive("VisualBackdrop");
            Assert.IsNotNull(backdrop);
            Assert.IsFalse(backdrop.activeInHierarchy,
                "The legacy backdrop must remain hidden after formal runtime initialization.");
            Assert.IsNull(FindSceneObjectIncludingInactive("VisualBaselineBoard"));
            Assert.IsNull(FindSceneObjectIncludingInactive("BattleVisualComparisonPanel"));

            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "AdventureScene is missing its formal camera.");
            Assert.AreEqual(8f, camera.transform.position.y, 0.001f);
            Assert.Less(Quaternion.Angle(camera.transform.rotation, Quaternion.Euler(38f, 0f, 0f)), 0.01f);
            Assert.AreEqual(4f, camera.orthographicSize, 0.001f);
            AdventureCameraController cameraController = camera.GetComponent<AdventureCameraController>();
            Assert.IsNotNull(cameraController);
            Assert.IsTrue(cameraController.enabled);
            Assert.AreSame(camera, GetPrivateField<Camera>(cameraController, "controlledCamera"));
            Assert.AreSame(battlefield.transform, GetPrivateField<Transform>(cameraController, "battlefield"));
            Renderer[] renderers = battlefield.GetComponentsInChildren<Renderer>();
            Assert.IsNotEmpty(renderers);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var plane = new Plane(Vector3.up, new Vector3(0f, 0.34f, 0f));
            Ray centerRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Assert.IsTrue(plane.Raycast(centerRay, out float distance));
            Vector3 focus = centerRay.GetPoint(distance);
            Assert.That(focus.x, Is.InRange(bounds.min.x - 0.001f, bounds.max.x + 0.001f));
            Assert.That(focus.z, Is.InRange(bounds.min.z - 0.001f, bounds.max.z + 0.001f));

            Static3DCombatUnitPresentationProvider provider = FindInActiveScene<Static3DCombatUnitPresentationProvider>();
            AssertProviderGrounding(provider, "player", 0, 0, battlefield.transform);
            AssertProviderGrounding(provider, "enemy", 1, 0, battlefield.transform);
        }

        private static void AssertProviderGrounding(
            Static3DCombatUnitPresentationProvider provider,
            string combatantId,
            int q,
            int r,
            Transform battlefield)
        {
            Assert.IsNotNull(provider);
            Assert.IsTrue(provider.TryGetPresentedUnit(combatantId, out GameObject unit));
            Transform ground = battlefield.Find("GuanzhongHex_" + q + "_" + r);
            Assert.IsNotNull(ground, "The functional battlefield is missing the marker ground cell.");
            Vector3 expectedCenter = new Vector3(q + r * 0.5f, 0f, r * 0.8660254f + 1f);
            Assert.Less(Vector2.Distance(
                new Vector2(unit.transform.position.x, unit.transform.position.z),
                new Vector2(expectedCenter.x, expectedCenter.z)), 0.001f);
            Assert.Less(Vector2.Distance(
                new Vector2(ground.position.x, ground.position.z),
                new Vector2(expectedCenter.x, expectedCenter.z)), 0.001f);
            Assert.AreEqual(0.34f, ground.position.y, 0.001f);
            Assert.AreEqual(Vector3.one, ground.localScale);
            Assert.AreEqual(0.34f, unit.transform.position.y, 0.001f);
        }


        private static float CenterSurfaceY(Transform ground)
        {
            float highest = float.NegativeInfinity;
            Vector3 center = ground.position;
            foreach (MeshFilter filter in ground.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                Assert.IsNotNull(mesh);
                Assert.IsTrue(mesh.isReadable, "Source mesh must remain readable for triangle evidence.");
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = filter.transform.TransformPoint(vertices[triangles[index]]);
                    Vector3 b = filter.transform.TransformPoint(vertices[triangles[index + 1]]);
                    Vector3 c = filter.transform.TransformPoint(vertices[triangles[index + 2]]);
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) < 1e-10f) continue;
                    float u = ((b.z - c.z) * (center.x - c.x) + (c.x - b.x) * (center.z - c.z)) / denominator;
                    float v = ((c.z - a.z) * (center.x - c.x) + (a.x - c.x) * (center.z - c.z)) / denominator;
                    float w = 1f - u - v;
                    if (u >= 0f && v >= 0f && w >= 0f)
                        highest = Mathf.Max(highest, u * a.y + v * b.y + w * c.y);
                }
            }
            Assert.IsFalse(float.IsInfinity(highest), "No source triangle supports the logical cell center.");
            return highest;
        }

        private static IEnumerator CaptureFormalTerrainIfRequested()
        {
            string path = Environment.GetEnvironmentVariable("TZ_GZ_TERRAIN_CAPTURE_PATH");
            if (string.IsNullOrEmpty(path)) yield break;
            // Capture after the spawned scene has gone through its first normal render frame.
            yield return null;
            Camera camera = Camera.main;
            Renderer backdrop = FindSceneObjectIncludingInactive("VisualBackdrop").GetComponent<Renderer>();
            Material backdropMaterial = backdrop.sharedMaterial;
            var backdropProperties = new MaterialPropertyBlock();
            backdrop.GetPropertyBlock(backdropProperties);
            Debug.Log("[GuanzhongRenderProof] backdrop=" + backdropMaterial.name +
                      " active=" + backdrop.gameObject.activeInHierarchy +
                      " color=" + backdropMaterial.GetColor("_BaseColor") +
                      " texture=" + backdropMaterial.GetTexture("_BaseMap") +
                      " propertyBlockEmpty=" + backdropProperties.isEmpty +
                      " position=" + backdrop.transform.position +
                      " bounds=" + backdrop.bounds +
                      " cameraAspect=" + camera.aspect + " pixelRect=" + camera.pixelRect);
            WriteTerrainCapture(camera, path);

            // A temporary source-model reference, never a replacement for the formal provider.
            Transform source = FindSceneObjectIncludingInactive("VisualBaselineBoard")
                .transform.Find("FacingProbe_0/FuYuan_Model");
            Assert.IsNotNull(source, "The preserved FuYuan source model must exist for visual QA.");
            GameObject battlefield = FindSceneObjectIncludingInactive("GuanzhongBattlefield");
            Transform ground = battlefield.transform.Find("GuanzhongHex_0_0");
            GameObject reference = Object.Instantiate(source.gameObject, ground.position, source.rotation);
            reference.name = "FuYuan_TerrainVisualReference";
            reference.SetActive(true);
            Renderer[] markers = FindSceneObjectIncludingInactive("PlayerMarker")
                .GetComponentsInChildren<Renderer>();
            var markerVisibility = new bool[markers.Length];
            for (int index = 0; index < markers.Length; index++)
            {
                markerVisibility[index] = markers[index].enabled;
                markers[index].enabled = false;
            }
            Vector3 cameraPosition = camera.transform.position;
            float cameraSize = camera.orthographicSize;
            string referencePath = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + "-fuyuan.png");
            try
            {
                yield return null;
                Assert.Less(Vector3.Distance(source.lossyScale, reference.transform.lossyScale), .00001f);
                Assert.Less(Quaternion.Angle(source.rotation, reference.transform.rotation), .001f);
                Renderer[] originalRenderers = source.GetComponentsInChildren<Renderer>(true);
                Renderer[] referenceRenderers = reference.GetComponentsInChildren<Renderer>(true);
                Assert.AreEqual(originalRenderers.Length, referenceRenderers.Length);
                for (int index = 0; index < referenceRenderers.Length; index++)
                    CollectionAssert.AreEqual(originalRenderers[index].sharedMaterials,
                        referenceRenderers[index].sharedMaterials);
                Bounds modelBounds = CombinedRendererBounds(reference);
                Bounds terrainBounds = CombinedRendererBounds(battlefield);
                float surfaceY = CenterSurfaceY(ground);
                Debug.Log("[GuanzhongFuYuanProof] source=VisualBaselineBoard/FacingProbe_0/FuYuan_Model" +
                          " model=" + reference.GetComponentInChildren<MeshFilter>().sharedMesh.name +
                          " material=" + referenceRenderers[0].sharedMaterial.name +
                          " root=" + reference.transform.position.ToString("F6") +
                          " scale=" + reference.transform.lossyScale.ToString("F6") +
                          " rotation=" + reference.transform.eulerAngles.ToString("F4") +
                          " modelBoundsMin=" + modelBounds.min.ToString("F6") +
                          " modelBoundsMax=" + modelBounds.max.ToString("F6") +
                          " terrainBoundsMin=" + terrainBounds.min.ToString("F6") +
                          " terrainBoundsMax=" + terrainBounds.max.ToString("F6") +
                          " centerSurfaceY=" + surfaceY.ToString("F6") +
                          " geometryMinGap=" + (modelBounds.min.y - surfaceY).ToString("F6") +
                          " technicalBaseIncluded=False formalProviderChanged=False");
                WriteTerrainCapture(camera, referencePath);

                // Additional inspection framing only; the formal camera is restored before gameplay resumes.
                Bounds inspectionBounds = terrainBounds;
                inspectionBounds.Encapsulate(modelBounds);
                camera.transform.position = inspectionBounds.center - camera.transform.forward * 14f;
                camera.orthographicSize = inspectionBounds.extents.magnitude * 1.1f;
                WriteTerrainCapture(camera, Path.Combine(Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path) + "-fuyuan-detail.png"));
            }
            finally
            {
                camera.transform.position = cameraPosition;
                camera.orthographicSize = cameraSize;
                for (int index = 0; index < markers.Length; index++)
                    markers[index].enabled = markerVisibility[index];
                reference.SetActive(false);
                Object.Destroy(reference);
            }
            yield return null;
            Assert.IsTrue(reference == null, "The visual QA model must be removed before formal combat resumes.");
            AssertFormalBattlefieldPresentation();
        }

        private static void WriteTerrainCapture(Camera camera, string path)
        {
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var request = new RenderPipeline.StandardRequest { destination = target };
            Assert.IsTrue(RenderPipeline.SupportsRenderRequest(camera, request));
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousCameraTarget = camera.targetTexture;
            try
            {
                // Keep the render and measured pixel rectangles on the same 16:9 projection.
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                GameObject battlefield = FindSceneObjectIncludingInactive("GuanzhongBattlefield");
                Rect terrainRect = ScreenRectFromBounds(camera, CombinedRendererBounds(battlefield));
                Assert.GreaterOrEqual(terrainRect.xMin, 0);
                Assert.GreaterOrEqual(terrainRect.yMin, 0);
                Assert.LessOrEqual(terrainRect.xMax, 1920);
                Assert.LessOrEqual(terrainRect.yMax, 1080);
                GameObject reference = GameObject.Find("FuYuan_TerrainVisualReference");
                Debug.Log("[GuanzhongTerrainCapture] path=" + path +
                          " cameraPosition=" + camera.transform.position.ToString("F4") +
                          " cameraEuler=" + camera.transform.eulerAngles.ToString("F4") +
                          " ortho=" + camera.orthographicSize.ToString("F6") +
                          " aspect=" + camera.aspect.ToString("F6") +
                          " terrainScreenRect=" + RectText(terrainRect) +
                          " referenceScreenRect=" + (reference == null ? "none" :
                              RectText(ScreenRectFromBounds(camera, CombinedRendererBounds(reference)))) +
                          " overlayUIIncluded=False");
            }
            finally
            {
                camera.targetTexture = previousCameraTarget;
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static GameObject FindSceneObjectIncludingInactive(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                if (transform.name == name) return transform.gameObject;
            return null;
        }

        private static Bounds CombinedRendererBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, target.name + " must contain a visible renderer.");
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static Rect ScreenRectFromBounds(Camera camera, Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector2 screenMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 screenMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 point = camera.WorldToViewportPoint(new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z));
                Vector2 screenPoint = new Vector2(point.x * 1920f, point.y * 1080f);
                screenMin = Vector2.Min(screenMin, screenPoint);
                screenMax = Vector2.Max(screenMax, screenPoint);
            }
            return Rect.MinMaxRect(screenMin.x, screenMin.y, screenMax.x, screenMax.y);
        }

        private static string RectText(Rect rect) =>
            "(" + rect.xMin.ToString("F2") + "," + rect.yMin.ToString("F2") + ")-(" +
            rect.xMax.ToString("F2") + "," + rect.yMax.ToString("F2") + ")";

        private sealed class SequenceRandomSource : IFormalEncounterRandomSource
        {
            private readonly int[] values;
            private int index;

            public SequenceRandomSource(params int[] values)
            {
                this.values = values ?? Array.Empty<int>();
            }

            public int NextPercent()
            {
                return index < values.Length ? values[index++] : 0;
            }
        }

        private sealed class SequenceCombatRandomSource : ICombatResolutionRandomSource
        {
            private readonly float[] values;
            private int index;

            public SequenceCombatRandomSource(params float[] values)
            {
                this.values = values ?? Array.Empty<float>();
            }

            public int Count => index;

            public float NextPercent()
            {
                if (index >= values.Length)
                    throw new InvalidOperationException("The combat roll sequence was exhausted.");
                return values[index++];
            }
        }

        private sealed class RecordingCombatPresentationSink : ICombatPresentationSink
        {
            public void Present(CombatHudSnapshot snapshot) { }
            public void ClearLog() { }
            public void AppendLog(string message) { }
            public void Hide() { }
        }

        private sealed class RecordingCombatUnitPresentationPort : ICombatUnitPresentationPort
        {
            public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> combatants) { }
            public void Spawn(CombatUnitPresentationDescriptor combatant) { }
            public void Present(CombatUnitPresentationEventProjection presentationEvent) { }
            public void Remove(string combatantId) { }
            public void Clear() { }
        }

        private sealed class ForwardingPresentationPort : ICombatUnitPresentationPort, ICombatPresentationActivity
        {
            private readonly ICombatUnitPresentationPort actual;
            public readonly List<CombatUnitPresentationEventProjection> Events = new List<CombatUnitPresentationEventProjection>();
            public bool IsPresenting => (actual as ICombatPresentationActivity)?.IsPresenting ?? false;
            public ForwardingPresentationPort(ICombatUnitPresentationPort actual) => this.actual = actual;
            public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> combatants) => actual.Prepare(combatants);
            public void Spawn(CombatUnitPresentationDescriptor combatant) => actual.Spawn(combatant);
            public void Present(CombatUnitPresentationEventProjection value) { Events.Add(value); actual.Present(value); }
            public void Remove(string combatantId) => actual.Remove(combatantId);
            public void Clear() => actual.Clear();
        }

        private static CombatUnitPresentationProfileCatalogData CreatePresentationCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<CombatUnitPresentationProfileCatalogData>();
            catalog.SetEntries(new[]
            {
                new CombatUnitPresentationProfileCatalogEntry
                {
                    combatantId = CombatUnitPresentationProfileCatalogData.PlayerCombatantId,
                    presentationProfileId = CombatUnitPresentationProfileCatalogData.PlayerProfileId,
                },
                new CombatUnitPresentationProfileCatalogEntry
                {
                    combatantId = CombatUnitPresentationProfileCatalogData.ShijiahouCombatantId,
                    presentationProfileId = CombatUnitPresentationProfileCatalogData.ShijiahouProfileId,
                },
            });
            return catalog;
        }

        private sealed class AlwaysInRangeCombatSpatialQuery : ICombatSpatialQuery
        {
            public CombatRangeQueryResult QueryRange(
                HexCoord source,
                HexCoord target,
                int minimumRange,
                int maximumRange) => new CombatRangeQueryResult(true, string.Empty);

            public CombatMovementQueryResult QueryMovement(
                HexCoord source,
                HexCoord destination,
                int movementPoints,
                int movementEfficiencyMilliUnits,
                IReadOnlyCollection<HexCoord> occupied) =>
                new CombatMovementQueryResult(false,
                "movement_not_used",
                null,
                null,
                1000,
                (0) * 10000);

            public IReadOnlyDictionary<HexCoord, int> FindReachable(
                HexCoord source,
                int movementPoints,
                int movementEfficiencyMilliUnits,
                IReadOnlyCollection<HexCoord> occupied) => new Dictionary<HexCoord, int>();
        }
    }
}
