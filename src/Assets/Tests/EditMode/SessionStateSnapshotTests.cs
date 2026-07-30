using System;
using NUnit.Framework;
using TianZhang.Core;
using TianZhang.Cultivation.JindanProof;
using TianZhang.Entity;
using TianZhang.Game;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class SessionStateSnapshotTests
    {
        [TearDown]
        public void TearDown()
        {
            if (GameSession.Instance != null)
                UnityEngine.Object.DestroyImmediate(GameSession.Instance.gameObject);
        }

        [Test]
        public void GameSessionOwnsSeparateSnapshotsAndKeepsEveryStateStepObservable()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            var steps = new StateStepSnapshot(
                shown: true,
                clicked: false,
                opened: true,
                selected: false,
                applied: true,
                completed: false,
                persisted: true);

            session.QuestStates.Set(new QuestStateSnapshot("quest_first_steps", steps));
            session.InventoryStates.Set(new InventoryStateSnapshot("item_spirit_stone", 3, steps));
            session.NpcStates.Set(new NpcStateSnapshot("npc_guide", "jiangzuo_hub", steps));

            Assert.AreEqual(1, session.QuestStates.Count);
            Assert.AreEqual(1, session.InventoryStates.Count);
            Assert.AreEqual(1, session.NpcStates.Count);

            Assert.IsTrue(session.QuestStates.TryGet("quest_first_steps", out var quest));
            Assert.IsTrue(quest.Steps.Shown);
            Assert.IsFalse(quest.Steps.Clicked);
            Assert.IsTrue(quest.Steps.Opened);
            Assert.IsFalse(quest.Steps.Selected);
            Assert.IsTrue(quest.Steps.Applied);
            Assert.IsFalse(quest.Steps.Completed);
            Assert.IsTrue(quest.Steps.Persisted);

            session.SetAdventureId("state_snapshot_test");
            session.SetWorldNode("guanzhong_hub");

            Assert.IsTrue(session.InventoryStates.TryGet("item_spirit_stone", out var inventory));
            Assert.AreEqual(3, inventory.Quantity);
            Assert.IsTrue(session.NpcStates.TryGet("npc_guide", out var npc));
            Assert.AreEqual("jiangzuo_hub", npc.WorldNodeId);

            session.BeginNewGame(null, "jiangzuo_hub");

            Assert.AreEqual(0, session.QuestStates.Count);
            Assert.AreEqual(0, session.InventoryStates.Count);
            Assert.AreEqual(0, session.NpcStates.Count);
        }

        [Test]
        public void SnapshotOwnersRejectMissingIdentityAndNegativeInventoryQuantity()
        {
            var steps = new StateStepSnapshot(false, false, false, false, false, false, false);

            Assert.Throws<ArgumentException>(() => new QuestStateSnapshot("", steps));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryStateSnapshot("item_test", -1, steps));
            Assert.Throws<ArgumentException>(() => new NpcStateSnapshot("npc_test", "", steps));
        }

        [Test]
        public void NewGameAndClearSessionProduceTheSameInitialSaveState()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();

            session.BeginNewGame(null, "jiangzuo_hub");
            string newGameJson = JsonUtility.ToJson(session.CaptureSaveData());

            session.SetWorldNode("guanzhong_hub");
            session.AdvanceWorldDay();
            session.SetSettlementId("guanzhong_city");
            session.SetAdventureId("guanzhong_wild");
            session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));
            session.QuestStates.Set(new QuestStateSnapshot("quest_changed", Steps(true)));

            session.ClearSession();

            Assert.AreEqual(newGameJson, JsonUtility.ToJson(session.CaptureSaveData()));
        }

        [Test]
        public void CurrentSchemaJsonRoundTripRestoresWorldContextAndSeparateStateSteps()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            var profile = ScriptableObject.CreateInstance<TianZhang.Entity.CharacterData>();
            try
            {
                session.BeginNewGame(profile, "guanzhong_hub");
                session.AdvanceWorldDay();
                session.SetSettlementId("guanzhong_city");
                session.SetAdventureId("guanzhong_wild");
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));
                var steps = new StateStepSnapshot(
                    shown: true,
                    clicked: false,
                    opened: true,
                    selected: false,
                    applied: true,
                    completed: false,
                    persisted: true);
                session.QuestStates.Set(new QuestStateSnapshot("quest_round_trip", steps));
                session.InventoryStates.Set(new InventoryStateSnapshot("item_round_trip", 7, steps));
                session.NpcStates.Set(new NpcStateSnapshot("npc_round_trip", "longxi_hub", steps));

                string json = JsonUtility.ToJson(session.CaptureSaveData());
                var serialized = JsonUtility.FromJson<GameSessionSaveData>(json);

                session.BeginNewGame(profile, "jiangzuo_hub");
                session.RestoreSaveData(serialized);

                Assert.AreSame(profile, session.PlayerProfile);
                Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, serialized.schemaVersion);
                Assert.AreEqual("guanzhong_hub", session.CurrentWorldNodeId);
                Assert.AreEqual(GameSession.InitialWorldYear, session.WorldYear);
                Assert.AreEqual(GameSession.InitialWorldSeasonId, session.WorldSeasonId);
                Assert.AreEqual(2, session.WorldDay);
                Assert.AreEqual(GameSession.InitialWorldTimeOfDayId, session.WorldTimeOfDayId);
                Assert.AreEqual("guanzhong_city", session.CurrentSettlementId);
                Assert.AreEqual("guanzhong_wild", session.CurrentAdventureId);
                Assert.AreEqual("SettlementScene", session.LastReturnTarget.SceneName);
                Assert.AreEqual("guanzhong_city", session.LastReturnTarget.SettlementId);

                Assert.IsTrue(session.QuestStates.TryGet("quest_round_trip", out var quest));
                AssertEveryStepIsSeparate(quest.Steps);
                Assert.IsTrue(session.InventoryStates.TryGet("item_round_trip", out var inventory));
                Assert.AreEqual(7, inventory.Quantity);
                AssertEveryStepIsSeparate(inventory.Steps);
                Assert.IsTrue(session.NpcStates.TryGet("npc_round_trip", out var npc));
                Assert.AreEqual("longxi_hub", npc.WorldNodeId);
                AssertEveryStepIsSeparate(npc.Steps);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void LegacySchemaMigratesMissingStateCollectionsToCurrentEmptyCollections()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            const string legacyJson =
                "{\"schemaVersion\":0,\"currentWorldNodeId\":\"legacy_hub\"," +
                "\"worldYear\":386,\"worldSeasonId\":\"summer\",\"worldDay\":9," +
                "\"worldTimeOfDayId\":\"night\",\"currentSettlementId\":\"legacy_city\"," +
                "\"currentAdventureId\":\"legacy_trial\",\"lastReturnTarget\":{" +
                "\"sceneName\":\"WorldScene\",\"worldNodeId\":\"legacy_hub\"}}";

            session.RestoreSaveData(JsonUtility.FromJson<GameSessionSaveData>(legacyJson));

            Assert.AreEqual("legacy_hub", session.CurrentWorldNodeId);
            Assert.AreEqual(386, session.WorldYear);
            Assert.AreEqual("summer", session.WorldSeasonId);
            Assert.AreEqual(9, session.WorldDay);
            Assert.AreEqual("night", session.WorldTimeOfDayId);
            Assert.AreEqual("legacy_city", session.CurrentSettlementId);
            Assert.AreEqual("legacy_trial", session.CurrentAdventureId);
            Assert.AreEqual("WorldScene", session.LastReturnTarget.SceneName);
            Assert.AreEqual("legacy_hub", session.LastReturnTarget.WorldNodeId);
            Assert.AreEqual(0, session.QuestStates.Count);
            Assert.AreEqual(0, session.InventoryStates.Count);
            Assert.AreEqual(0, session.NpcStates.Count);
            Assert.AreEqual(
                GameSessionSnapshot.CurrentSchemaVersion,
                session.CaptureSaveData().schemaVersion);
        }

        [Test]
        public void FoundationPurpleMansionSaveRoundTripMigratesVersionOneAndRejectsTamperedJindanState()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            FoundationPurpleMansionStateData state = CreateCompleteFoundationPurpleMansionState();
            CharacterData profile = ScriptableObject.CreateInstance<CharacterData>();
            profile.charName = "save_fixture";
            profile.realmMultiplier = 3f;
            profile.foundationPurpleMansionState = state;
            try
            {
                session.BeginNewGame(profile, "jiangzuo_hub");
                Character player = Character.FromData(profile, new HexCoord(0, 0));
                Assert.IsTrue(new JindanProofCoordinator()
                    .TryFormFoundationPurpleMansionLock(player).Succeeded);
                session.CapturePlayerFoundationPurpleMansionState(player);

                string savedJson = JsonUtility.ToJson(session.CaptureSaveData());
                GameSessionSaveData saved = JsonUtility.FromJson<GameSessionSaveData>(savedJson);
                Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, saved.schemaVersion);
                Assert.IsNotNull(saved.playerFoundationPurpleMansionState);

                session.BeginNewGame(profile, "guanzhong_hub");
                session.RestoreSaveData(saved);
                Character restoredPlayer = Character.FromData(profile, new HexCoord(0, 0));
                Assert.IsTrue(session.ApplyPlayerFoundationPurpleMansionState(restoredPlayer));
                Assert.IsTrue(restoredPlayer.FoundationPurpleMansionState.IsJindanFormed);
                Assert.AreEqual("guardian_ming", restoredPlayer.FoundationPurpleMansionState
                    .GetGuardianAbilities()[0].abilityInstanceId);
                Assert.AreEqual("node_ming_1", restoredPlayer.FoundationPurpleMansionState
                    .GetEnhancementNodes()[0].nodeId);
                Assert.AreEqual(savedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                GameSessionSaveData tampered = JsonUtility.FromJson<GameSessionSaveData>(savedJson);
                tampered.playerFoundationPurpleMansionState.foundationState.naturalMansionCapacity = 2;
                tampered.playerFoundationPurpleMansionState.foundationState.releasedNaturalCapacity = 2;
                tampered.playerFoundationPurpleMansionState.foundationState.totalMansionCapacity = 2;
                Assert.Throws<ArgumentException>(() => session.RestoreSaveData(tampered));
                Assert.AreEqual(savedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                saved.schemaVersion = GameSessionSnapshot.StateCollectionsSchemaVersion;
                saved.playerFoundationPurpleMansionState = null;
                session.RestoreSaveData(saved);
                Assert.IsNull(session.PlayerFoundationPurpleMansionSaveData);
                Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion,
                    session.CaptureSaveData().schemaVersion);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(state);
            }
        }

        [Test]
        public void NpcCultivationStateRoundTripsOnlyInCurrentSchemaAndInvalidStateFailsAtomically()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            FoundationPurpleMansionStateData state = CreateCompleteFoundationPurpleMansionState();
            try
            {
                Assert.That(FoundationPurpleMansionRuntimeState.TryCreate(
                    state,
                    out FoundationPurpleMansionRuntimeState runtime,
                    out string failureReason), Is.True, failureReason);
                Assert.That(runtime.TryStartCultivationAction(
                    CultivationActionKind.FoundationNurture,
                    "npc_action_nurture",
                    "foundation_save_fixture",
                    "cycle_nurture",
                    "boundary_started",
                    "progress_nurture",
                    new[] { "numeric_nurture" }).Succeeded, Is.True);
                Assert.That(runtime.TryCommitCultivationActionCycle("world_day_18").Succeeded, Is.True);
                Assert.That(runtime.TryPauseCultivationAction("RESOURCE_INSUFFICIENT").Succeeded, Is.True);
                session.NpcStates.Set(new NpcStateSnapshot(
                    "npc_save_fixture",
                    "jiangzuo_hub",
                    Steps(true),
                    runtime.CaptureSaveData()));

                GameSessionSaveData current = session.CaptureSaveData();
                Assert.That(current.schemaVersion, Is.EqualTo(GameSessionSnapshot.CurrentSchemaVersion));
                Assert.That(current.npcs[0].foundationPurpleMansionState.cultivationActionState.targetRef,
                    Is.EqualTo("foundation_save_fixture"));

                string currentJson = JsonUtility.ToJson(current);
                session.ClearSession();
                session.RestoreSaveData(JsonUtility.FromJson<GameSessionSaveData>(currentJson));
                Assert.That(session.NpcStates.TryGet("npc_save_fixture", out NpcStateSnapshot restored), Is.True);
                Assert.That(restored.FoundationPurpleMansionState.cultivationActionState.actionStateId,
                    Is.EqualTo("npc_action_nurture"));
                Assert.That(restored.FoundationPurpleMansionState.cultivationActionState.committedCycleIds,
                    Is.EquivalentTo(new[] { "world_day_18" }));
                Assert.That(restored.FoundationPurpleMansionState.lastClosedRetreatStopReason,
                    Is.EqualTo("RESOURCE_INSUFFICIENT"));

                foreach (int legacyVersion in new[]
                {
                    GameSessionSnapshot.LegacySchemaVersion,
                    GameSessionSnapshot.StateCollectionsSchemaVersion,
                    GameSessionSnapshot.FoundationPurpleMansionSchemaVersion,
                })
                {
                    GameSessionSaveData legacy = JsonUtility.FromJson<GameSessionSaveData>(currentJson);
                    legacy.schemaVersion = legacyVersion;
                    session.RestoreSaveData(legacy);
                    if (legacyVersion == GameSessionSnapshot.LegacySchemaVersion)
                    {
                        Assert.That(session.NpcStates.Count, Is.EqualTo(0));
                    }
                    else
                    {
                        Assert.That(session.NpcStates.TryGet("npc_save_fixture", out NpcStateSnapshot migrated), Is.True);
                        Assert.That(migrated.FoundationPurpleMansionState, Is.Null);
                    }
                }

                session.RestoreSaveData(JsonUtility.FromJson<GameSessionSaveData>(currentJson));
                GameSessionSaveData invalid = JsonUtility.FromJson<GameSessionSaveData>(currentJson);
                invalid.npcs[0].foundationPurpleMansionState.foundationState.totalMansionCapacity = 0;
                Assert.Throws<ArgumentException>(() => session.RestoreSaveData(invalid));
                Assert.That(JsonUtility.ToJson(session.CaptureSaveData()), Is.EqualTo(currentJson));

                invalid = JsonUtility.FromJson<GameSessionSaveData>(currentJson);
                invalid.npcs[0].foundationPurpleMansionState =
                    new FoundationPurpleMansionSaveData();
                Assert.Throws<ArgumentException>(() => session.RestoreSaveData(invalid));
                Assert.That(JsonUtility.ToJson(session.CaptureSaveData()), Is.EqualTo(currentJson));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(state);
            }
        }

        [Test]
        public void InvalidOrUnsupportedSaveDataFailsBeforeChangingTheSession()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            session.BeginNewGame(null, "jiangzuo_hub");
            session.AdvanceWorldDay();
            session.QuestStates.Set(new QuestStateSnapshot("quest_valid", Steps(true)));
            session.InventoryStates.Set(new InventoryStateSnapshot("item_valid", 2, Steps(false)));
            session.NpcStates.Set(new NpcStateSnapshot("npc_valid", "jiangzuo_hub", Steps(true)));
            string baselineJson = JsonUtility.ToJson(session.CaptureSaveData());

            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.currentWorldNodeId = "");
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.quests[0].questId = "");
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.inventory[0].itemId = "");
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.npcs[0].npcId = "");
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.npcs[0].worldNodeId = "");
            AssertRejectedWithoutMutation<ArgumentOutOfRangeException>(session, baselineJson, data =>
                data.inventory[0].quantity = -1);
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.quests.Add(Clone(data.quests[0])));
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.inventory.Add(Clone(data.inventory[0])));
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.npcs.Add(Clone(data.npcs[0])));
            AssertRejectedWithoutMutation<NotSupportedException>(session, baselineJson, data =>
                data.schemaVersion = -1);
            AssertRejectedWithoutMutation<NotSupportedException>(session, baselineJson, data =>
                data.schemaVersion = GameSessionSnapshot.CurrentSchemaVersion + 1);
        }

        private static FoundationPurpleMansionStateData CreateCompleteFoundationPurpleMansionState()
        {
            var state = ScriptableObject.CreateInstance<FoundationPurpleMansionStateData>();
            state.schemaId = "foundationPurpleMansionState";
            state.schemaVersion = 1;
            state.characterId = "save_fixture";
            state.foundationState = new FoundationStateRecord
            {
                foundationInstanceId = "foundation_save_fixture",
                foundationDefinitionId = "foundation_definition",
                sourceGongFaId = "gongfa_save_fixture",
                phase = FoundationPhase.Phase4,
                continuousProgress = 400f,
                phaseBoundarySetId = "phase_boundaries",
                naturalMansionCapacity = 1,
                releasedNaturalCapacity = 1,
                expansionGrants = Array.Empty<FoundationExpansionGrant>(),
                expandedMansionCapacity = 0,
                totalMansionCapacity = 1,
            };
            state.mansionStates = new[]
            {
                new PurpleMansionStateRecord
                {
                    mansionKind = PurpleMansionKind.Ming,
                    state = PurpleMansionBuildState.Complete,
                    mansionInstanceId = "mansion_ming",
                    mansionBodyEffectBindingId = "MANSION_BODY_MING_YUAN_HUIHU",
                    guardianAbilityInstanceId = "guardian_ming",
                    sourceSpellId = "spell_ming",
                    upgradePlanId = "upgrade_ming",
                    sourceSpellDisposition = "RETAIN",
                },
                NotBuilt(PurpleMansionKind.Hun),
                NotBuilt(PurpleMansionKind.Shi),
                NotBuilt(PurpleMansionKind.Wu),
                NotBuilt(PurpleMansionKind.Yun),
            };
            state.effectBindings = new[]
            {
                new FoundationEffectBinding
                {
                    effectBindingId = "MANSION_BODY_MING_YUAN_HUIHU",
                    carrierKind = FoundationEffectCarrierKind.MansionBody,
                    carrierId = "mansion_ming",
                    order = 1,
                    trigger = "fixture_trigger",
                    conditions = Array.Empty<string>(),
                    target = "fixture_target",
                    atomicEffectType = "fixture_effect",
                    parameters = Array.Empty<string>(),
                },
            };
            state.guardianAbilities = new[]
            {
                new GuardianAbilityRecord
                {
                    abilityInstanceId = "guardian_ming",
                    abilityDefinitionId = "ability_ming",
                    mansionInstanceId = "mansion_ming",
                    sourceSpellId = "spell_ming",
                    upgradePlanId = "upgrade_ming",
                    sourceSpellDisposition = "RETAIN",
                    form = GuardianAbilityForm.Passive,
                    effectBindingIds = Array.Empty<string>(),
                },
            };
            state.enhancementNodes = new[]
            {
                new EnhancementNodeRecord
                {
                    nodeId = "node_ming_1",
                    abilityInstanceId = "guardian_ming",
                    nodeKind = EnhancementNodeKind.Cultivation,
                    requirements = Array.Empty<string>(),
                    effectBindingIds = Array.Empty<string>(),
                },
            };
            state.jindanLock = new JindanLockRecord { status = JindanLockStatus.PreJindan };
            return state;
        }

        private static PurpleMansionStateRecord NotBuilt(PurpleMansionKind kind)
        {
            return new PurpleMansionStateRecord
            {
                mansionKind = kind,
                state = PurpleMansionBuildState.NotBuilt,
            };
        }

        private static StateStepSnapshot Steps(bool firstValue)
        {
            return new StateStepSnapshot(
                firstValue,
                !firstValue,
                firstValue,
                !firstValue,
                firstValue,
                !firstValue,
                firstValue);
        }

        private static void AssertEveryStepIsSeparate(StateStepSnapshot steps)
        {
            Assert.IsTrue(steps.Shown);
            Assert.IsFalse(steps.Clicked);
            Assert.IsTrue(steps.Opened);
            Assert.IsFalse(steps.Selected);
            Assert.IsTrue(steps.Applied);
            Assert.IsFalse(steps.Completed);
            Assert.IsTrue(steps.Persisted);
        }

        private static void AssertRejectedWithoutMutation<TException>(
            GameSession session,
            string baselineJson,
            Action<GameSessionSaveData> mutate)
            where TException : Exception
        {
            GameSessionSaveData invalid =
                JsonUtility.FromJson<GameSessionSaveData>(baselineJson);
            mutate(invalid);

            Assert.Throws<TException>(() => session.RestoreSaveData(invalid));
            Assert.AreEqual(baselineJson, JsonUtility.ToJson(session.CaptureSaveData()));
        }

        private static QuestStateSaveData Clone(QuestStateSaveData source)
        {
            return JsonUtility.FromJson<QuestStateSaveData>(JsonUtility.ToJson(source));
        }

        private static InventoryStateSaveData Clone(InventoryStateSaveData source)
        {
            return JsonUtility.FromJson<InventoryStateSaveData>(JsonUtility.ToJson(source));
        }

        private static NpcStateSaveData Clone(NpcStateSaveData source)
        {
            return JsonUtility.FromJson<NpcStateSaveData>(JsonUtility.ToJson(source));
        }
    }
}
