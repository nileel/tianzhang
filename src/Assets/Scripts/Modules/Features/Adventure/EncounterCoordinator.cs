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
    public interface ICombatResolutionRandomSource
    {
        float NextPercent();
    }

    public sealed class SystemCombatResolutionRandomSource : ICombatResolutionRandomSource
    {
        private readonly System.Random random;

        public SystemCombatResolutionRandomSource()
        {
            random = new System.Random();
        }

        public SystemCombatResolutionRandomSource(int seed)
        {
            random = new System.Random(seed);
        }

        public float NextPercent() => (float)(random.NextDouble() * 100d);
    }

    public sealed class EncounterCoordinator : MonoBehaviour, ICombatCommandHandler
    {
        private readonly CombatCommandService commandService = new CombatCommandService();
        private readonly CombatResultBuilder resultBuilder = new CombatResultBuilder();
        private ICombatResolutionRandomSource combatRandomSource = new SystemCombatResolutionRandomSource();
        private CombatLegalActionService legalActions;
        private CombatSession session;
        private AdventureSpawnSet spawned;
        private ICombatActionPolicy enemyPolicy;
        private ICombatPresentationSink presentation;
        private ICombatUnitPresentationPort unitPresentation;
        private CombatUnitPresentationProfileCatalogData presentationProfileCatalog;
        private Action<CombatSessionOutcome, EnemyData> completed;
        private bool acceptsPlayerCommand;
        private bool playerActed;

        public bool IsRunning => session != null;

        public void SetCombatResolutionRandomSource(ICombatResolutionRandomSource source)
        {
            combatRandomSource = source ?? throw new ArgumentNullException(nameof(source));
        }

        public void Configure(
            ICombatPresentationSink presentationSink,
            ICombatUnitPresentationPort unitPresentationPort,
            CombatUnitPresentationProfileCatalogData profileCatalog,
            Action<CombatSessionOutcome, EnemyData> onCompleted)
        {
            presentation = presentationSink ?? throw new ArgumentNullException(nameof(presentationSink));
            unitPresentation = unitPresentationPort ?? throw new ArgumentNullException(nameof(unitPresentationPort));
            presentationProfileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
            completed = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            legalActions = new CombatLegalActionService(commandService);
        }

        public bool TryBegin(
            CharacterStateSnapshot player,
            ContentCatalogData catalog,
            AdventureNodeData startNode,
            AdventureNodeData encounterNode,
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
                    player, catalog, startNode, encounterNode, out spawned, out reason))
                return false;
            if (!EnemyAIProfileResolver.TryResolveCombatActionPolicy(
                    spawned.EnemyData.aiProfileId, out enemyPolicy, out reason))
            {
                spawned = null;
                return false;
            }
            if (!TryCreatePresentationDescriptors(spawned, out IReadOnlyList<CombatUnitPresentationDescriptor> descriptors, out reason))
            {
                spawned = null;
                return false;
            }
            if (!combatEntry.TryCreateSession(spawned, attackProfiles, environmentProfile, out session, out reason))
            {
                spawned = null;
                return false;
            }
            try
            {
                unitPresentation.Prepare(descriptors);
            }
            catch (Exception exception)
            {
                unitPresentation.Clear();
                session = null;
                spawned = null;
                reason = "adventure_presentation_spawn_failed:" + exception.Message;
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
                        CombatActionResult result = ExecuteCommand(command);
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
            CombatActionResult result = ExecuteCommand(command);
            presentation.AppendLog(BuildActionMessage("玩家", command, result));
            if (result.Succeeded) playerActed = true;
            Present("你的行动", !playerActed);
        }

        private CombatActionResult ExecuteCommand(CombatCommand command)
        {
            session.Combatants.TryGet(command.ActorId, out CombatantSnapshot actorBefore);
            session.Combatants.TryGet(command.TargetId, out CombatantSnapshot targetBefore);
            CombatUnitPresentationHex actorStart = actorBefore == null
                ? default
                : ToPresentationHex(actorBefore.Position);
            int actorFacing = actorBefore?.Facing ?? 0;
            CombatActionResult validation = commandService.Validate(session, command);
            if (!validation.Succeeded)
                return validation;
            if (command.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Art or CombatCommandKind.Divine)
            {
                var rolls = new CombatResolutionRolls(
                    combatRandomSource.NextPercent(),
                    combatRandomSource.NextPercent(),
                    combatRandomSource.NextPercent(),
                    combatRandomSource.NextPercent());
                command = new CombatCommand(
                    command.Kind,
                    command.ActorId,
                    command.TargetId,
                    command.ProfileId,
                    rolls,
                    command.Destination,
                    command.SlotIndex);
            }
            CombatActionResult result = commandService.Execute(session, command);
            if (result.Succeeded)
                ProjectPresentation(command, result, actorBefore, actorStart, actorFacing, targetBefore);
            return result;
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
            unitPresentation.Remove(spawned.Player.Id);
            unitPresentation.Remove(spawned.Enemy.Id);
            unitPresentation.Clear();
            session = null;
            AdventureSpawnSet prior = spawned;
            spawned = null;
            completed(outcome, defeated ?? prior.EnemyData);
        }

        private bool TryCreatePresentationDescriptors(
            AdventureSpawnSet values,
            out IReadOnlyList<CombatUnitPresentationDescriptor> descriptors,
            out string reason)
        {
            descriptors = null;
            if (presentationProfileCatalog == null || !presentationProfileCatalog.TryValidate(out _))
            {
                reason = "adventure_presentation_catalog_invalid";
                return false;
            }
            if (!presentationProfileCatalog.TryGetPresentationProfileId(
                    CombatUnitPresentationProfileCatalogData.PlayerCombatantId, out string playerProfileId) ||
                !presentationProfileCatalog.TryGetPresentationProfileId(
                    values.EnemyData.enemyId, out string enemyProfileId))
            {
                reason = "adventure_presentation_profile_unresolved";
                return false;
            }

            int playerFacing = values.Player.Position.DirectionTo(values.Enemy.Position);
            int enemyFacing = values.Enemy.Position.DirectionTo(values.Player.Position);
            descriptors = new[]
            {
                new CombatUnitPresentationDescriptor(
                    values.Player.Id,
                    playerProfileId,
                    CombatUnitDisplayFaction.Player,
                    ToPresentationHex(values.Player.Position),
                    playerFacing < 0 ? 0 : playerFacing),
                new CombatUnitPresentationDescriptor(
                    values.Enemy.Id,
                    enemyProfileId,
                    CombatUnitDisplayFaction.Enemy,
                    ToPresentationHex(values.Enemy.Position),
                    enemyFacing < 0 ? 0 : enemyFacing),
            };
            reason = null;
            return true;
        }

        private void ProjectPresentation(
            CombatCommand command,
            CombatActionResult result,
            CombatantSnapshot actorBefore,
            CombatUnitPresentationHex actorStart,
            int actorFacing,
            CombatantSnapshot targetBefore)
        {
            if (actorBefore == null) return;
            try
            {
                CombatUnitPresentationEvent actionEvent = command.Kind switch
                {
                    CombatCommandKind.Move => CombatUnitPresentationEvent.Move,
                    CombatCommandKind.BasicAttack => CombatUnitPresentationEvent.Attack,
                    CombatCommandKind.Art or CombatCommandKind.Divine => CombatUnitPresentationEvent.Cast,
                    _ => CombatUnitPresentationEvent.Idle,
                };
                unitPresentation.Present(new CombatUnitPresentationEventProjection(
                    actorBefore.Id,
                    actionEvent,
                    actorStart,
                    ToPresentationHex(actorBefore.Position),
                    actorFacing,
                    Array.Empty<CombatUnitPresentationTargetResult>()));

                if (targetBefore == null || result.Damage.Count == 0) return;
                int finalDamage = result.Damage.Sum(item => item.FinalDamage);
                bool isDead = targetBefore.CurrentHealth <= 0;
                unitPresentation.Present(new CombatUnitPresentationEventProjection(
                    targetBefore.Id,
                    isDead ? CombatUnitPresentationEvent.Death : CombatUnitPresentationEvent.Hit,
                    ToPresentationHex(targetBefore.Position),
                    ToPresentationHex(targetBefore.Position),
                    targetBefore.Facing,
                    new[] { new CombatUnitPresentationTargetResult(targetBefore.Id, finalDamage, isDead) }));
            }
            catch (Exception exception)
            {
                Debug.LogError("[AdventurePresentation] " + exception.Message);
            }
        }

        private static CombatUnitPresentationHex ToPresentationHex(TianZhang.Spatial.HexCoord value) =>
            new CombatUnitPresentationHex(value.Q, value.R);

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
