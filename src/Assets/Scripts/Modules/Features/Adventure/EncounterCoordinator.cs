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
        private Func<string, string> resolveText = value => value;
        private Func<string, string> displayReason = value => value;
        private string playerDisplayName;

        public void ConfigureText(Func<string, string> resolve, Func<string, string> reasonDisplay)
        {
            resolveText = resolve ?? throw new ArgumentNullException(nameof(resolve));
            displayReason = reasonDisplay ?? throw new ArgumentNullException(nameof(reasonDisplay));
        }

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
            playerDisplayName = player.Identity.DisplayName;
            presentation.AppendLog("战斗开始：" + playerDisplayName + " 对阵 " + resolveText(spawned.EnemyData.displayNameKey));
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
        public void RequestEndTurn(string actorId) => ExecutePlayer(new CombatCommand(CombatCommandKind.EndTurn, actorId));
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
                    acceptsPlayerCommand = true;
                    Present("你的行动", true);
                    while (session.TurnPhase != CombatTurnPhase.Finished &&
                           resultBuilder.Build(session).Outcome == CombatSessionOutcome.Ongoing)
                        yield return null;
                    acceptsPlayerCommand = false;
                }
                else
                {
                    Present(resolveText(spawned.EnemyData.displayNameKey) + " 行动", false);
                    yield return null;
                    while (session.TurnPhase != CombatTurnPhase.Finished &&
                           resultBuilder.Build(session).Outcome == CombatSessionOutcome.Ongoing)
                    {
                        IReadOnlyList<CombatCommand> legal = legalActions.GetLegalActions(session, advance.ActorId);
                        CombatCommand command = session.TurnPhase == CombatTurnPhase.ActionThenMove
                            ? legal.FirstOrDefault(action => action.Kind == CombatCommandKind.EndTurn)
                            : enemyPolicy.ChooseAction(legal);
                        if (command == null)
                            throw new InvalidOperationException("combat_enemy_turn_has_no_legal_command");
                        CombatActionResult result = ExecuteCommand(command);
                        presentation.AppendLog(BuildActionMessage(resolveText(spawned.EnemyData.displayNameKey), command, result));
                        if (!result.Succeeded)
                            throw new InvalidOperationException("combat_enemy_legal_command_rejected:" + result.RejectionReason);
                        Present(resolveText(spawned.EnemyData.displayNameKey) + " 行动", false);
                        yield return null;
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
            presentation.AppendLog(BuildActionMessage(playerDisplayName, command, result));
            Present("你的行动", session.TurnPhase != CombatTurnPhase.Finished &&
                resultBuilder.Build(session).Outcome == CombatSessionOutcome.Ongoing);
        }

        private CombatActionResult ExecuteCommand(CombatCommand command)
        {
            session.Combatants.TryGet(command.ActorId, out CombatantSnapshot actorBefore);
            session.Combatants.TryGet(command.TargetId, out CombatantSnapshot targetBefore);
            CombatUnitPresentationHex actorStart = actorBefore == null
                ? default
                : ToPresentationHex(actorBefore.Position);
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
            if (result.Succeeded && command.Kind != CombatCommandKind.EndTurn)
                ProjectPresentation(command, result, actorBefore, actorStart, targetBefore);
            return result;
        }

        private void Present(string turnText, bool acceptsCommands)
        {
            if (session == null) return;
            session.Combatants.TryGet("player", out CombatantSnapshot player);
            session.Combatants.TryGet("enemy", out CombatantSnapshot enemy);
            presentation.Present(new CombatHudSnapshot(
                ToHud(player, playerDisplayName),
                ToHud(enemy, resolveText(spawned.EnemyData.displayNameKey)),
                turnText,
                acceptsCommands,
                player?.EquippedArtProfileIds,
                spawned.PlayerDivineProfileIds,
                timeline: BuildTimeline()));
        }

        private CombatTimelineSnapshot BuildTimeline()
        {
            CombatTurnSnapshot turns = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            string current = session.TurnPhase == CombatTurnPhase.Finished ? null : session.CurrentActorId;
            var units = turns.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
            var entries = new List<CombatTimelineEntrySnapshot>();
            var included = new HashSet<string>(StringComparer.Ordinal);
            void Add(string id, int queuePosition)
            {
                if (string.IsNullOrEmpty(id) || !units.TryGetValue(id, out CombatTurnUnitSnapshot unit) || !included.Add(id)) return;
                session.Combatants.TryGet(id, out CombatantSnapshot combatant);
                bool isPlayer = combatant.Team == CombatTeam.Player;
                entries.Add(new CombatTimelineEntrySnapshot(id,
                    isPlayer ? playerDisplayName : resolveText(spawned.EnemyData.displayNameKey),
                    isPlayer ? CombatUnitDisplayFaction.Player : CombatUnitDisplayFaction.Enemy,
                    unit.Speed, unit.ChargeTime, unit.NextActionThreshold, unit.IsReady, queuePosition,
                    string.Equals(current, id, StringComparison.Ordinal)));
            }
            Add(current, -1);
            for (int index = 0; index < turns.ReadyQueueIds.Count; index++) Add(turns.ReadyQueueIds[index], index);
            foreach (string id in turns.ChargingUnitIds) Add(id, -1);
            return new CombatTimelineSnapshot(turns.CurrentTick, current, entries);
        }

        private void Complete(CombatSessionOutcome outcome)
        {
            acceptsPlayerCommand = false;
            presentation.AppendLog(outcome == CombatSessionOutcome.Victory ? "战斗胜利" : "战斗失败");
            Present(resolveText(outcome switch
            {
                CombatSessionOutcome.Victory => "combat_outcome_victory",
                CombatSessionOutcome.Defeat => "combat_outcome_defeat",
                _ => "combat_outcome_ongoing",
            }), false);
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
                    actorBefore.Facing,
                    Array.Empty<CombatUnitPresentationTargetResult>(),
                    result.MovementPath.Select(ToPresentationHex).ToArray()));

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

        private string BuildActionMessage(string actor, CombatCommand command, CombatActionResult result)
        {
            if (!result.Succeeded) return actor + "：" + displayReason(result.RejectionReason);
            string commandName = resolveText(command.Kind switch
            {
                CombatCommandKind.BasicAttack => "combat_command_basic_attack",
                CombatCommandKind.Art => "combat_command_art",
                CombatCommandKind.Divine => "combat_command_divine",
                CombatCommandKind.Guard => "combat_command_guard",
                CombatCommandKind.Wait => "combat_command_wait",
                CombatCommandKind.EndTurn => "combat_command_end_turn",
                CombatCommandKind.Move => "combat_command_move",
                CombatCommandKind.SwapSpell => "combat_command_swap_spell",
                _ => throw new ArgumentOutOfRangeException(nameof(command.Kind)),
            });
            if (result.Damage.Count > 0)
                return actor + "：" + commandName + "，伤害 " + result.Damage.Sum(item => item.FinalDamage);
            return actor + "：" + commandName;
        }
    }
}
