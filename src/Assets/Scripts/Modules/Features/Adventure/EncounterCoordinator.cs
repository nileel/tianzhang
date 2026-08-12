using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.UnityContent;
using UnityEngine;

namespace TianZhang.Features.Adventure
{
    public sealed class EncounterCoordinator : MonoBehaviour, ICombatCommandHandler
    {
        private readonly CombatCommandService commandService = new CombatCommandService();
        private readonly CombatResultBuilder resultBuilder = new CombatResultBuilder();
        private CombatLegalActionService legalActions;
        private CombatSession session;
        private AdventureSpawnSet spawned;
        private ICombatActionPolicy enemyPolicy;
        private ICombatPresentationSink presentation;
        private Action<CombatSessionOutcome, EnemyData> completed;
        private bool acceptsPlayerCommand;
        private bool playerActed;

        public bool IsRunning => session != null;

        public void Configure(
            ICombatPresentationSink presentationSink,
            Action<CombatSessionOutcome, EnemyData> onCompleted)
        {
            presentation = presentationSink ?? throw new ArgumentNullException(nameof(presentationSink));
            completed = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            legalActions = new CombatLegalActionService(commandService);
        }

        public bool TryBegin(
            CharacterStateSnapshot player,
            ContentCatalogData catalog,
            AdventureNodeData startNode,
            AdventureNodeData encounterNode,
            GameObject unitMarkerPrefab,
            AttackProfileData[] attackProfiles,
            EnvironmentProfileAsset environmentProfile,
            AdventureUnitSpawner unitSpawner,
            CombatEntryAdapter combatEntry,
            out string reason)
        {
            if (IsRunning)
            {
                reason = "adventure_encounter_already_running";
                return false;
            }
            if (!unitSpawner.TrySpawn(
                    player, catalog, startNode, encounterNode, unitMarkerPrefab, out spawned, out reason))
                return false;
            if (!EnemyAIProfileResolver.TryResolveCombatActionPolicy(
                    spawned.EnemyData.aiProfileId, out enemyPolicy, out reason))
            {
                DestroyMarkers();
                spawned = null;
                return false;
            }
            if (!combatEntry.TryCreateSession(spawned, attackProfiles, environmentProfile, out session, out reason))
            {
                DestroyMarkers();
                spawned = null;
                return false;
            }

            presentation.ClearLog();
            presentation.AppendLog("战斗开始：" + spawned.Player.Id + " VS " + spawned.EnemyData.displayNameKey);
            StartCoroutine(RunCombat());
            reason = null;
            return true;
        }

        public void RequestBasicAttack(string actorId, string targetId)
        {
            string profileId = string.Equals(actorId, "player", StringComparison.Ordinal)
                ? spawned?.PlayerBasicProfileId
                : spawned?.EnemyBasicProfileId;
            ExecutePlayer(new CombatCommand(CombatCommandKind.BasicAttack, actorId, targetId, profileId));
        }

        public void RequestArt(string actorId, string targetId, string profileId) =>
            ExecutePlayer(new CombatCommand(CombatCommandKind.Art, actorId, targetId, profileId));
        public void RequestDivine(string actorId, string targetId, string profileId) =>
            ExecutePlayer(new CombatCommand(CombatCommandKind.Divine, actorId, targetId, profileId));
        public void RequestGuard(string actorId) => ExecutePlayer(new CombatCommand(CombatCommandKind.Guard, actorId));
        public void RequestWait(string actorId) => ExecutePlayer(new CombatCommand(CombatCommandKind.Wait, actorId));
        public void RequestMove(string actorId, int destinationQ, int destinationR) =>
            ExecutePlayer(new CombatCommand(
                CombatCommandKind.Move,
                actorId,
                destination: new TianZhang.Spatial.HexCoord(destinationQ, destinationR)));
        public void RequestSwapSpell(string actorId, int slotIndex, string profileId) =>
            ExecutePlayer(new CombatCommand(
                CombatCommandKind.SwapSpell,
                actorId,
                profileId: profileId,
                slotIndex: slotIndex));

        private IEnumerator RunCombat()
        {
            while (resultBuilder.Build(session).Outcome == CombatSessionOutcome.Ongoing)
            {
                CombatTurnAdvance advance = commandService.AdvanceUntilAction(session);
                if (!advance.HasActor)
                {
                    Complete(CombatSessionOutcome.Defeat);
                    yield break;
                }

                if (string.Equals(advance.ActorId, "player", StringComparison.Ordinal))
                {
                    playerActed = false;
                    acceptsPlayerCommand = true;
                    Present("你的行动", true);
                    while (!playerActed && resultBuilder.Build(session).Outcome == CombatSessionOutcome.Ongoing)
                        yield return null;
                    acceptsPlayerCommand = false;
                }
                else
                {
                    Present(spawned.EnemyData.displayNameKey + " 行动", false);
                    yield return null;
                    IReadOnlyList<CombatCommand> legal = legalActions.GetLegalActions(session, "enemy");
                    CombatCommand command = enemyPolicy.ChooseAction(legal);
                    if (command != null)
                    {
                        CombatActionResult result = commandService.Execute(session, command);
                        presentation.AppendLog(BuildActionMessage(spawned.EnemyData.displayNameKey, command, result));
                    }
                }
                Present("战斗中", false);
            }

            Complete(resultBuilder.Build(session).Outcome);
        }

        private void ExecutePlayer(CombatCommand command)
        {
            if (!acceptsPlayerCommand || session == null || command == null ||
                !string.Equals(command.ActorId, "player", StringComparison.Ordinal)) return;
            CombatActionResult result = commandService.Execute(session, command);
            presentation.AppendLog(BuildActionMessage("玩家", command, result));
            if (result.Succeeded) playerActed = true;
            Present("你的行动", !playerActed);
        }

        private void Present(string turnText, bool acceptsCommands)
        {
            if (session == null) return;
            session.Combatants.TryGet("player", out CombatantSnapshot player);
            session.Combatants.TryGet("enemy", out CombatantSnapshot enemy);
            presentation.Present(new CombatHudSnapshot(
                ToHud(player, "玩家"),
                ToHud(enemy, spawned.EnemyData.displayNameKey),
                turnText,
                acceptsCommands,
                player?.EquippedArtProfileIds,
                spawned.PlayerDivineProfileIds));
        }

        private void Complete(CombatSessionOutcome outcome)
        {
            acceptsPlayerCommand = false;
            presentation.AppendLog(outcome == CombatSessionOutcome.Victory ? "战斗胜利" : "战斗失败");
            Present(outcome.ToString(), false);
            EnemyData defeated = outcome == CombatSessionOutcome.Victory ? spawned.EnemyData : null;
            DestroyMarkers();
            session = null;
            AdventureSpawnSet prior = spawned;
            spawned = null;
            completed(outcome, defeated ?? prior.EnemyData);
        }

        private void DestroyMarkers()
        {
            if (spawned?.PlayerMarker != null) Destroy(spawned.PlayerMarker);
            if (spawned?.EnemyMarker != null) Destroy(spawned.EnemyMarker);
        }

        private static CombatantHudSnapshot ToHud(CombatantSnapshot value, string name)
        {
            return value == null ? null : new CombatantHudSnapshot(
                value.Id,
                name,
                value.CurrentHealth,
                value.MaximumHealth,
                value.CurrentSpirit,
                value.MaximumSpirit);
        }

        private static string BuildActionMessage(string actor, CombatCommand command, CombatActionResult result)
        {
            if (!result.Succeeded) return actor + "：" + result.RejectionReason;
            if (result.Damage.Count > 0)
                return actor + "：" + command.Kind + "，伤害 " + result.Damage.Sum(item => item.FinalDamage);
            return actor + "：" + command.Kind;
        }
    }
}
