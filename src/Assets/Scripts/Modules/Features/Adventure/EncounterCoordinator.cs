using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
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

    public sealed class EncounterCoordinator : MonoBehaviour, ICombatCommandHandler, ICombatActionPreviewHandler, ICombatExternalResourceQuery
    {
        private readonly CombatCommandService commandService = new CombatCommandService();
        private readonly CombatResultBuilder resultBuilder = new CombatResultBuilder();
        private ICombatResolutionRandomSource combatRandomSource = new SystemCombatResolutionRandomSource();
        private CombatLegalActionService legalActions;
        private CombatSession session;
        private IReadOnlyList<HexCoord> battlefieldCells = Array.Empty<HexCoord>();
        private AdventureSpawnSet spawned;
        private Dictionary<string, ICombatActionPolicy> enemyPolicies;
        private ICombatPresentationSink presentation;
        private ICombatPresentationActivity hudActivity;
        private ICombatUnitPresentationPort unitPresentation;
        private CombatUnitPresentationProfileCatalogData presentationProfileCatalog;
        private Action<CombatSessionOutcome, EnemyData> completed;
        private bool acceptsPlayerCommand;
        private Func<string, string> resolveText = value => value;
        private Func<string, string> displayReason = value => value;
        private Func<string, string> displayProfile = value => value;
        private string playerDisplayName;
        private long commandContextVersion;
        private CombatCommandPreview swapPreview;
        private Coroutine combatRoutine;
        private Coroutine surfaceRoutine;
        private readonly CombatSurfacePlayback surfacePlayback = new CombatSurfacePlayback();
        private bool withdrawalPaused;

        private CharacterRuntimeProfile equipmentOwner;
        private ContentCatalogData equipmentCatalog;
        private Func<string, string, string, bool> ownsEquipment;
        private Func<long> inventoryVersion;
        private long presentedInventoryVersion;
        private Func<string, int> itemQuantity;
        public delegate bool PrepareItemConsumption(string itemId, int quantity, long version, out Action apply, out string reason);
        private PrepareItemConsumption prepareItemConsumption;

        public void ConfigureItems(Func<string, int> quantity, PrepareItemConsumption prepare)
        {
            itemQuantity = quantity ?? throw new ArgumentNullException(nameof(quantity));
            prepareItemConsumption = prepare ?? throw new ArgumentNullException(nameof(prepare));
        }

        public CombatItemResource QueryItem(CombatCommand command)
        {
            if (equipmentOwner == null || equipmentCatalog == null || itemQuantity == null ||
                !equipmentCatalog.TryGetCombatItem(command.ItemId, out ItemData item))
                return new CombatItemResource(0, 0, 0, null, "item_definition_invalid");
            string reason = command.ActorId != equipmentOwner.Identity.CharacterId ||
                equipmentOwner.Progression.RealmStage != "练气" ? "item_qualification_invalid" : null;
            string facts = string.Join("|", item.itemId, item.displayNameKey, item.descriptionKey, item.contentScope,
                item.itemCategory, item.maxStack, item.combatUseEffectId, item.combatUseRealmId,
                item.combatUseTargetMode, item.combatUseHealthRecovery, item.initialGrantQuantity,
                equipmentOwner.Identity.CharacterId, equipmentOwner.Progression.RealmStage);
            return new CombatItemResource(inventoryVersion(), itemQuantity(item.itemId), item.combatUseHealthRecovery, facts, reason);
        }

        public IReadOnlyList<CombatCommand> GetItemCandidates(string actorId) =>
            itemQuantity == null || spawned?.IsJindanRehearsal == true ? Array.Empty<CombatCommand>() : new[]
            {
                new CombatCommand(CombatCommandKind.UseItem, actorId, actorId, contextVersion: commandContextVersion,
                    itemId: "item_huoxue_dan"),
            };

        public void ConfigureEquipment(CharacterRuntimeProfile owner, ContentCatalogData catalog,
            Func<string, string, string, bool> owns, Func<long> version)
        {
            equipmentOwner = owner ?? throw new ArgumentNullException(nameof(owner));
            equipmentCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            ownsEquipment = owns ?? throw new ArgumentNullException(nameof(owns));
            inventoryVersion = version ?? throw new ArgumentNullException(nameof(version));
        }

        public CombatEquipmentResource QueryEquipment(CombatCommand command)
        {
            string reason = "equipment_definition_invalid";
            if (equipmentOwner == null || command.ActorId != equipmentOwner.Identity.CharacterId)
                return new CombatEquipmentResource(0, null, null, reason);
            if (string.IsNullOrEmpty(equipmentOwner.MainEquipmentItemId) ||
                !equipmentOwner.ValidateEquipmentState(equipmentCatalog, ownsEquipment(command.ActorId,
                    equipmentOwner.MainEquipmentItemId, equipmentOwner.MainEquipmentInstanceId)))
                return new CombatEquipmentResource(inventoryVersion(), equipmentOwner.MainEquipmentInstanceId, null, "equipment_definition_invalid");
            equipmentOwner.TryPrepareEquipmentChange(equipmentCatalog, command.EquipmentSlotId, command.ItemId,
                command.InstanceId, ownsEquipment(command.ActorId, command.ItemId, command.InstanceId), out _, out reason);
            equipmentCatalog.TryGetEquipment(command.ItemId, out ItemData item);
            return new CombatEquipmentResource(inventoryVersion(), equipmentOwner.MainEquipmentInstanceId,
                item?.equipmentBasicAttackProfileId, reason);
        }

        public IReadOnlyList<CombatCommand> GetEquipmentCandidates(string actorId)
        {
            var candidates = new List<CombatCommand>();
            if (equipmentOwner == null || actorId != equipmentOwner.Identity.CharacterId) return candidates;
            foreach (string id in new[] { "item_iron_sword", "item_iron_blade", "item_qingfeng_jian" })
            {
                if (id == equipmentOwner.MainEquipmentItemId || !equipmentCatalog.TryGetEquipment(id, out ItemData item)) continue;
                candidates.Add(new CombatCommand(CombatCommandKind.SwapEquipment, actorId,
                    profileId: item.equipmentBasicAttackProfileId, contextVersion: commandContextVersion,
                    equipmentSlotId: "main_hand", itemId: id, instanceId: actorId + ":initial:" + id));
            }
            return candidates;
        }

        public void ConfigureText(Func<string, string> resolve, Func<string, string> reasonDisplay,
            Func<string, string> profileDisplay = null)
        {
            resolveText = resolve ?? throw new ArgumentNullException(nameof(resolve));
            displayReason = reasonDisplay ?? throw new ArgumentNullException(nameof(reasonDisplay));
            displayProfile = profileDisplay ?? resolveText;
        }

        public bool IsRunning => session != null;
        public CombatSession CurrentSession => session;
        public string ControlledActorId => session != null && session.Combatants.TryGet(session.CurrentActorId, out var current) &&
            current.Team == CombatTeam.Player ? current.Id : spawned?.Player.Id ?? "player";
        private bool IsPlayerActor(string id) => session != null && session.Combatants.TryGet(id, out var actor) && actor.Team == CombatTeam.Player;
        private string ActorName(string id) => spawned?.RehearsalNames != null && spawned.RehearsalNames.TryGetValue(id, out var name)
            ? name : IsPlayerActor(id) ? playerDisplayName : resolveText(EnemyDataFor(id)?.displayNameKey ?? id);
        private string PortraitKey(string id) => spawned?.IsJindanRehearsal == true || IsPlayerActor(id) ? "player" : EnemyDataFor(id)?.enemyId;
        public bool IsPresenting => surfacePlayback.Busy || ((unitPresentation as ICombatPresentationActivity)?.IsPresenting ?? false) ||
            (hudActivity?.IsPresenting ?? false);

        /// <summary>The only window in which a direct <c>Request*</c> command is performed instead of
        /// dropped: the player's turn is active and its advance presentation has finished.</summary>
        public bool AcceptsPlayerCommand => acceptsPlayerCommand && !IsPresenting;

        public bool CanWithdraw(out string reason)
        {
            reason = "回合切换中";
            if (session == null) return false;
            if (resultBuilder.Build(session).Outcome != CombatSessionOutcome.Ongoing)
            {
                reason = "胜负已定，等待结算";
                return false;
            }
            if (IsPresenting)
            {
                // A command can finish its rules turn before its movement presentation ends.
                reason = acceptsPlayerCommand ? "行动演出中，请稍后撤离" : "敌方行动中，请等待你的行动";
                return false;
            }
            if (session.TurnPhase != CombatTurnPhase.Active) return false;
            if (!IsPlayerActor(session.CurrentActorId))
            {
                reason = "敌方行动中，请等待你的行动";
                return false;
            }
            if (!acceptsPlayerCommand && !withdrawalPaused) return false;
            reason = null;
            return true;
        }

        public bool PauseForWithdrawal()
        {
            if (withdrawalPaused || !CanWithdraw(out _)) return false;
            withdrawalPaused = true;
            acceptsPlayerCommand = false;
            swapPreview = null;
            Present("确认撤离中", false);
            return true;
        }

        public void ResumeAfterWithdrawal()
        {
            if (!withdrawalPaused) return;
            bool canResume = CanWithdraw(out _);
            withdrawalPaused = false;
            acceptsPlayerCommand = canResume;
            Present("你的行动", canResume);
        }

        public void TerminateWithoutOutcome()
        {
            bool available = CanWithdraw(out string reason);
            if (!withdrawalPaused || !available)
                throw new InvalidOperationException("adventure_withdrawal_unavailable:" + reason);
            acceptsPlayerCommand = false;
            swapPreview = null;
            ++commandContextVersion;
            if (combatRoutine != null) StopCoroutine(combatRoutine);
            if (surfaceRoutine != null) StopCoroutine(surfaceRoutine);
            surfacePlayback.Clear();
            combatRoutine = null;
            session.Jindan?.Clear(session);
            session.Flight?.Clear(session);
            session = null;
            spawned = null;
            enemyPolicies = null;
            withdrawalPaused = false;
            unitPresentation.Clear();
        }

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
            hudActivity = presentationSink as ICombatPresentationActivity;
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
            enemyPolicies = new Dictionary<string, ICombatActionPolicy>(StringComparer.Ordinal);
            for (int index = 0; index < spawned.Enemies.Count; index++)
            {
                if (!EnemyAIProfileResolver.TryResolveCombatActionPolicy(
                        spawned.EnemyDataList[index].aiProfileId, out ICombatActionPolicy policy, out reason))
                {
                    spawned = null;
                    enemyPolicies = null;
                    return false;
                }
                enemyPolicies.Add(spawned.Enemies[index].Id, policy);
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
            battlefieldCells = environmentProfile.battlefieldCells.Select(cell => new HexCoord(cell.q, cell.r)).ToArray();
            session.ExternalResources = this;
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
            presentation.AppendLog("战斗开始：" + playerDisplayName + " 对阵 " +
                string.Join("、", spawned.EnemyDataList.Select(data => resolveText(data.displayNameKey))));
            combatRoutine = StartCoroutine(RunCombat());
            reason = null;
            return true;
        }

        public bool TryBeginRehearsal(AdventureSpawnSet values, CombatSession prepared,
            IReadOnlyList<CombatUnitPresentationDescriptor> descriptors, IReadOnlyList<HexCoord> cells, out string reason)
        {
            reason = "adventure_encounter_already_running";
            if (IsRunning || values == null || !values.IsJindanRehearsal || prepared?.Jindan == null) return false;
            try { unitPresentation.Prepare(descriptors); }
            catch (Exception exception)
            { unitPresentation.Clear(); reason = "adventure_presentation_spawn_failed:" + exception.Message; return false; }
            spawned = values; session = prepared; battlefieldCells = cells;
            // Demo identities never bind world equipment or inventory. Growth refresh is also disabled below.
            session.ExternalResources = null;
            enemyPolicies = values.Enemies.ToDictionary(value => value.Id, value => (ICombatActionPolicy)new LegalActionAI());
            playerDisplayName = ActorName(values.Player.Id);
            presentation.ClearLog(); presentation.AppendLog("金丹演武开始：" + string.Join("、", values.RehearsalNames.Values));
            combatRoutine = StartCoroutine(RunCombat());
            reason = null; return true;
        }

        public CombatCommandPreview PreviewJindan(CombatCommand command) =>
            session == null ? null : commandService.Preview(session, command);

        public CombatActionResult ConfirmJindan(CombatCommand command, CombatCommandPreview quote)
        {
            if (!AcceptsPlayerCommand || command?.ActorId != ControlledActorId || session?.Jindan == null)
                return CombatActionResult.Rejected("combat_quote_stale");
            var result = ExecuteCommand(command, quote);
            string action = command.Kind == CombatCommandKind.SwapJindan ? "切换金丹装配" :
                command.Kind == CombatCommandKind.CloseJindan ? "关闭当前位格效果" :
                command.Kind == CombatCommandKind.Flight ? "御风飞行" : command.Kind==CombatCommandKind.Art?"演武·破垒":
                command.Jindan?.TideAction.HasValue == true ? JindanTidePresentation.Name(command.Jindan.TideAction.Value,
                    command.Jindan.Charged, command.Jindan.CreateTideEye) :
                command.Jindan?.EarthMode.HasValue==true?"厚载·"+command.Jindan.EarthMode:"布设方位定尺";
            presentation.AppendLog(result.Succeeded ? ActorName(command.ActorId) + "：" + action +
                "  AP -" + result.ActionPointSpentUnits / 1000f + "，MP -" + result.SpiritSpent : displayReason(result.RejectionReason));
            Present("你的行动", session.TurnPhase != CombatTurnPhase.Finished);
            return result;
        }

        public CombatActionPreviewSnapshot PreviewAction(CombatActionCandidate candidate)
        {
            RefreshGrowth();
            if (candidate == null) return null;
            CombatCommand command = ToCommand(candidate);
            string reason = !IsCandidateCurrent(candidate) ? "行动状态已变化，请重新选择" : string.Empty;
            CombatCommandPreview preview = string.IsNullOrEmpty(reason) ? commandService.Preview(session, command) : null;
            swapPreview = preview;
            return ProjectQuote(candidate, command, preview, reason);
        }

        private CombatActionPreviewSnapshot ProjectQuote(CombatActionCandidate candidate, CombatCommand command,
            CombatCommandPreview preview, string reason = "")
        {
            if (preview != null && !preview.Validation.Succeeded)
                reason = preview.Validation.RejectionReason == "combat_attack_area_empty"
                    ? "区域内没有存活敌人" : displayReason(preview.Validation.RejectionReason);
            string name = candidate.Kind switch
            {
                CombatActionKind.BasicAttack => "普攻",
                CombatActionKind.Guard => "防御",
                CombatActionKind.Wait => "待机",
                CombatActionKind.EndTurn => "结束行动",
                CombatActionKind.Move => "移动",
                CombatActionKind.SwapSpell => "换入" + displayProfile(command.ProfileId),
                CombatActionKind.SwapEquipment => "装备" + resolveText(command.ItemId),
                CombatActionKind.UseItem => resolveText(command.ItemId),
                _ => displayProfile(command.ProfileId),
            };
            string targetName = string.IsNullOrEmpty(command.TargetId)
                ? candidate.Kind is CombatActionKind.BasicAttack or CombatActionKind.Art ? "待选目标" : "自身"
                : session != null && session.Combatants.TryGet(command.TargetId, out _) ? ActorName(command.TargetId) : "无效目标";
            var resolved = new CombatActionCandidate(candidate.Kind, command.ActorId, command.TargetId,
                command.ProfileId, candidate.ContextVersion, candidate.Destination,
                candidate.Kind == CombatActionKind.Move ? preview?.ActionPointCostUnits ?? 0 : 0,
                candidate.Kind == CombatActionKind.Move ? preview?.MovementPath.Select(ToPresentationHex).ToArray() : null,
                candidate.SlotIndex, candidate.EquipmentSlotId, candidate.ItemId, candidate.InstanceId, command.AttackDirection,
                command.DestinationFacing, preview?.MovementSteps.Select(ToPresentationStep).ToArray(), candidate.TargetCell);
            CombatantSnapshot actor = null, quotedTarget = null;
            session?.Combatants.TryGet(command.ActorId, out actor);
            session?.Combatants.TryGet(command.TargetId, out quotedTarget);
            float targetDistance = -1;
            if (command.TargetCell.HasValue && preview?.AttackArea != null)
            {
                SpatialContactResult contact = preview.AttackArea.CellContacts.FirstOrDefault(value => value.TargetCell == command.TargetCell.Value);
                if (!contact.IsInRange && actor != null && preview.AttackArea.EnvironmentTargetIds.Count > 0)
                {
                    var attack = preview.AttackArea.Profile;
                    contact = session.SceneObjects.Board.QuerySceneObjectContact(actor.Pose, command.TargetCell.Value,
                        preview.AttackArea.EnvironmentTargetIds[0], attack.MinimumRange, attack.MaximumRange, attack.SpatialPolicy);
                }
                if (contact.DistanceUnits >= 0 && contact.UnitsPerRange > 0)
                    targetDistance = contact.DistanceUnits / (float)contact.UnitsPerRange;
                targetName = "地块 (" + command.TargetCell.Value.Q + "," + command.TargetCell.Value.R + ")";
            }
            else if (actor != null && quotedTarget != null)
            {
                SpatialContactResult contact = session.SpatialQuery.QueryRange(actor.Pose, quotedTarget.Pose, 0, 0);
                if (contact.DistanceUnits >= 0 && contact.UnitsPerRange > 0)
                    targetDistance = contact.DistanceUnits / (float)contact.UnitsPerRange;
            }
            CombatAttackProfile profile = null;
            session?.TryGetProfile(command.ProfileId, out profile);
            CombatAttackRuleMetadata metadata = profile?.RuleMetadata;
            bool basic = candidate.Kind == CombatActionKind.BasicAttack;
            bool unarmed = basic && metadata?.EffectBudgetMode == "fixed_unarmed";
            string sourceKind = basic && profile != null ? (unarmed ? "unarmed_body" : "main_equipment") : string.Empty;
            string unarmedRealm = unarmed ? metadata.UnarmedRealmId : string.Empty;
            int capacity = unarmed ? metadata.UnarmedPhysicalAttackCapacity : metadata?.Tier.PhysicalAttackCapacity ?? 0;
            if (unarmed) name = displayProfile(command.ProfileId);
            string attackDetail = metadata == null ? string.Empty :
                resolveText(unarmed ? unarmedRealm : "realm_" + metadata.Tier.AbilityTierId.Substring(5)) +
                (unarmed ? " · 肉攻承载 " + capacity : basic ? "档 · 肉攻承载 " + capacity : "档");
            int remainingCooldown = actor?.GetCooldown(command.ProfileId) ?? 0;
            CombatAttackAreaSnapshot area = preview?.AttackArea == null ? null : new CombatAttackAreaSnapshot(
                ToPresentationHex(preview.AttackArea.Origin), preview.AttackArea.Direction,
                preview.AttackArea.Cells.Select(cell => new CombatAttackAreaCellSnapshot(
                    ToPresentationHex(cell.Coord), cell.IsLegal, cell.IsLegal ? string.Empty : displayReason(cell.Reason))).ToArray(),
                preview.AttackArea.TargetIds.Select(id =>
                {
                    session.Combatants.TryGet(id, out var target);
                    return ToHud(target, ActorName(id));
                }).ToArray(), session.SceneObjects?.All.Where(value => preview.AttackArea.Cells.Any(cell => value.Contains(cell.Coord)))
                    .Select(value => ProjectSceneObject(value, preview.AttackArea.EnvironmentTargetIds.Contains(value.Id)
                        ? Math.Min(value.Durability, profile.EnvironmentDamage) : 0)).ToArray());
            if (area != null) name = displayProfile(command.ProfileId);
            string replaced = candidate.Kind == CombatActionKind.SwapSpell && actor != null &&
                candidate.SlotIndex >= 0 && candidate.SlotIndex < actor.EquippedArtProfileIds.Count
                ? displayProfile(actor.EquippedArtProfileIds[candidate.SlotIndex]) : string.Empty;
            return new CombatActionPreviewSnapshot(resolved, name, playerDisplayName, targetName,
                preview != null && preview.Validation.Succeeded, reason, preview?.SpiritCost ?? 0,
                preview?.RemainsInTurn ?? false, preview?.Turn?.ChargeTime ?? 0,
                preview?.Turn?.NextActionThreshold ?? 0, preview?.Turn?.TicksUntilReady ?? 0,
                preview?.ActionPointCostUnits ?? 0, preview?.CtPenalty ?? 0,
                preview?.ReuseCooldownTicks ?? 0, remainingCooldown, replaced,
                actor == null ? 0 : Math.Max(0, actor.MaxCombatSwaps - actor.CombatSwapsUsed),
                preview?.ItemQuantityCost ?? 0, preview?.ItemAvailableQuantity ?? 0,
                preview?.ItemHealthRecovery ?? 0, preview?.ExpectedHealthRecovery ?? 0, preview?.InventoryVersion ?? 0,
                preview?.CommittedUseCount ?? 0, preview?.RepeatMultiplierPermille ?? 1000,
                preview?.RuleSetVersion ?? string.Empty, preview?.RuleSetSourceDigest ?? string.Empty,
                preview?.AbilityTierId ?? string.Empty, sourceKind, unarmedRealm, capacity, attackDetail,
                candidate.Kind == CombatActionKind.SwapEquipment && equipmentOwner != null
                    ? (string.IsNullOrEmpty(equipmentOwner.MainEquipmentItemId) ? "未装备" : resolveText(equipmentOwner.MainEquipmentItemId)) : string.Empty,
                candidate.Kind == CombatActionKind.SwapEquipment && ownsEquipment != null
                    ? ownsEquipment(command.ActorId, command.ItemId, command.InstanceId) : (bool?)null, area,
                profile?.MinimumRange ?? 0, profile?.MaximumRange ?? 0, targetDistance,
                actor != null && preview != null && actor.ActionPointsRemainingUnits >= preview.ActionPointCostUnits &&
                actor.CurrentSpirit >= preview.SpiritCost);
        }

        public void ConfirmAction(CombatActionCandidate candidate)
        {
            if (IsPresenting) return;
            RefreshGrowth();
            if (!IsCandidateCurrent(candidate))
            {
                presentation?.AppendLog("行动状态已变化，请重新选择");
                Present("行动状态已变化", acceptsPlayerCommand && session != null &&
                    session.TurnPhase != CombatTurnPhase.Finished);
                return;
            }
            if (candidate.Kind == CombatActionKind.Move)
            {
                CombatCommandPreview quote = commandService.Preview(session, ToCommand(candidate));
                if (!quote.IsLegal || candidate.MovementCostUnits != quote.ActionPointCostUnits ||
                    !candidate.MovementPath.SequenceEqual(quote.MovementPath.Select(ToPresentationHex)) ||
                    !SameMovementSteps(candidate.MovementSteps, quote.MovementSteps))
                {
                    presentation?.AppendLog("移动状态已变化，请重新选择");
                    Present("移动状态已变化", true);
                    return;
                }
            }
            if (candidate.Kind is CombatActionKind.SwapSpell or CombatActionKind.SwapEquipment or CombatActionKind.UseItem or
                CombatActionKind.BasicAttack or CombatActionKind.Art or CombatActionKind.Divine)
            {
                CombatCommandPreview quote = swapPreview;
                swapPreview = null;
                if (quote == null)
                {
                    presentation?.AppendLog(displayReason("combat_quote_stale"));
                    return;
                }
                ExecutePlayer(ToCommand(candidate), quote);
                return;
            }
            ExecutePlayer(ToCommand(candidate));
        }

        private bool IsCandidateCurrent(CombatActionCandidate candidate) => candidate != null && session != null &&
            acceptsPlayerCommand && !IsPresenting && candidate.ContextVersion == commandContextVersion &&
            candidate.ActorId == session.CurrentActorId && IsPlayerActor(candidate.ActorId);

        private CombatCommand ToCommand(CombatActionCandidate candidate)
        {
            CombatCommandKind kind = candidate.Kind switch
            {
                CombatActionKind.BasicAttack => CombatCommandKind.BasicAttack,
                CombatActionKind.Art => CombatCommandKind.Art,
                CombatActionKind.Divine => CombatCommandKind.Divine,
                CombatActionKind.Guard => CombatCommandKind.Guard,
                CombatActionKind.Wait => CombatCommandKind.Wait,
                CombatActionKind.EndTurn => CombatCommandKind.EndTurn,
                CombatActionKind.Move => CombatCommandKind.Move,
                CombatActionKind.SwapSpell => CombatCommandKind.SwapSpell,
                CombatActionKind.SwapEquipment => CombatCommandKind.SwapEquipment,
                CombatActionKind.UseItem => CombatCommandKind.UseItem,
                _ => throw new ArgumentOutOfRangeException(nameof(candidate)),
            };
            string profile = candidate.Kind == CombatActionKind.BasicAttack ? CurrentBasicProfile(candidate.ActorId) : candidate.ProfileId;
            string target = candidate.TargetId;
            int? direction = candidate.AttackDirection;
            if (candidate.Kind == CombatActionKind.BasicAttack && session != null &&
                session.TryGetProfile(profile, out var attack) && attack.Area != CombatAttackArea.Single)
            {
                target = null;
                if (!direction.HasValue && session.Combatants.TryGet(candidate.ActorId, out var actor)) direction = actor.Facing;
            }
            var destination = candidate.Destination.HasValue
                ? new TianZhang.Spatial.HexCoord(candidate.Destination.Value.Q, candidate.Destination.Value.R)
                : (TianZhang.Spatial.HexCoord?)null;
            return new CombatCommand(kind, candidate.ActorId, target, profile,
                destination: destination, slotIndex: candidate.SlotIndex, contextVersion: candidate.ContextVersion,
                equipmentSlotId: candidate.EquipmentSlotId, itemId: candidate.ItemId, instanceId: candidate.InstanceId,
                attackDirection: direction, destinationFacing: candidate.DestinationFacing,
                targetCell: candidate.TargetCell.HasValue ? new HexCoord(candidate.TargetCell.Value.Q, candidate.TargetCell.Value.R) : null);
        }

        private string CurrentBasicProfile(string actorId) => session != null && session.Combatants.TryGet(actorId, out var actor)
            ? actor.BasicAttackProfileId : null;

        public void RequestBasicAttack(string actorId, string targetId)
        {
            string profileId = CurrentBasicProfile(actorId);
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
                RefreshGrowth();
                acceptsPlayerCommand = false;
                var flightBefore = SnapshotFlightStates();
                CombatTurnAdvance advance = commandService.AdvanceUntilAction(session, out var advanceSteps);
                ProjectFlightStates(flightBefore);
                surfaceRoutine = StartCoroutine(surfacePlayback.Play(session, unitPresentation, presentation.AppendLog));
                Present("回合推进", false, advanceSteps);
                while (IsPresenting) yield return null;
                if (!advance.HasActor)
                {
                    Complete(resultBuilder.Build(session).Outcome);
                    yield break;
                }

                if (IsPlayerActor(advance.ActorId))
                {
                    acceptsPlayerCommand = true;
                    Present("你的行动", true);
                    bool wasPresenting = IsPresenting;
                    while (IsPresenting || (session.TurnPhase != CombatTurnPhase.Finished &&
                           resultBuilder.Build(session).Outcome == CombatSessionOutcome.Ongoing))
                    {
                        yield return null;
                        while (withdrawalPaused) yield return null;
                        if (RefreshGrowth() || (inventoryVersion?.Invoke() ?? 0) != presentedInventoryVersion)
                            Present("你的行动", session.TurnPhase != CombatTurnPhase.Finished);
                        if (wasPresenting != IsPresenting)
                        {
                            wasPresenting = IsPresenting;
                            Present("你的行动", session.TurnPhase != CombatTurnPhase.Finished &&
                                resultBuilder.Build(session).Outcome == CombatSessionOutcome.Ongoing);
                        }
                    }
                    acceptsPlayerCommand = false;
                }
                else
                {
                    Present(ActorName(advance.ActorId) + " 行动", false);
                    yield return null;
                    while (session.TurnPhase != CombatTurnPhase.Finished &&
                           resultBuilder.Build(session).Outcome == CombatSessionOutcome.Ongoing)
                    {
                        IReadOnlyList<CombatCommand> legal = legalActions.GetLegalActions(session, advance.ActorId);
                        if (!enemyPolicies.TryGetValue(advance.ActorId, out ICombatActionPolicy policy))
                            throw new InvalidOperationException("combat_enemy_ai_profile_missing");
                        CombatCommand command = policy.ChooseAction(session, legal);
                        if (command == null)
                            throw new InvalidOperationException("combat_enemy_turn_has_no_legal_command");
                        CombatActionResult result = ExecuteCommand(command);
                        presentation.AppendLog(BuildActionMessage(ActorName(advance.ActorId), command, result));
                        if (!result.Succeeded)
                            throw new InvalidOperationException("combat_enemy_legal_command_rejected:" + result.RejectionReason);
                        Present(ActorName(advance.ActorId) + " 行动", false);
                        do { yield return null; } while (IsPresenting);
                    }
                }
                Present("战斗中", false);
            }

            Complete(resultBuilder.Build(session).Outcome);
        }

        private void ExecutePlayer(CombatCommand command, CombatCommandPreview quote = null)
        {
            RefreshGrowth();
            if (!AcceptsPlayerCommand || session == null || command == null ||
                command.ActorId != ControlledActorId || !IsPlayerActor(command.ActorId))
            {
                // A direct request outside the accepting window is dropped, never queued. The HUD
                // path (ConfirmAction) already reports stale state; keep one log line so automation
                // and tooling do not lose a command silently.
                if (session != null && command != null) presentation?.AppendLog(displayReason("combat_quote_stale"));
                return;
            }
            CombatActionResult result = ExecuteCommand(command, quote);
            presentation.AppendLog(BuildActionMessage(ActorName(command.ActorId), command, result));
            Present("你的行动", session.TurnPhase != CombatTurnPhase.Finished &&
                resultBuilder.Build(session).Outcome == CombatSessionOutcome.Ongoing);
        }

        private CombatActionResult ExecuteCommand(CombatCommand command, CombatCommandPreview quote = null)
        {
            var flightBefore = command.Kind is CombatCommandKind.EndTurn or CombatCommandKind.JindanAbility
                ? SnapshotFlightStates() : null;
            session.Combatants.TryGet(command.ActorId, out CombatantSnapshot actorBefore);
            CombatUnitPresentationHex actorStart = actorBefore == null
                ? default
                : ToPresentationHex(actorBefore.Position);
            quote ??= commandService.Preview(session, command);
            CombatActionResult validation = commandService.ValidateConfirmation(session, command, quote);
            if (!validation.Succeeded)
                return validation;
            if ((command.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Art or CombatCommandKind.Divine) &&
                session.TryGetProfile(command.ProfileId, out CombatAttackProfile sampledProfile) &&
                sampledProfile.Effect is CombatAttackEffect.Physical or CombatAttackEffect.Soul or CombatAttackEffect.Hybrid)
            {
                CombatResolutionRolls Sample() => new CombatResolutionRolls(combatRandomSource.NextPercent(),
                    combatRandomSource.NextPercent(), combatRandomSource.NextPercent(),
                    combatRandomSource.NextPercent(), combatRandomSource.NextPercent());
                var targetRolls = sampledProfile.Area != CombatAttackArea.Single
                    ? quote.AttackArea.TargetIds.Select(id => new CombatTargetResolutionRolls(id, Sample())).ToArray() : null;
                CombatResolutionRolls rolls = targetRolls == null &&
                    (quote.AttackArea == null || quote.AttackArea.TargetIds.Count > 0) ? Sample() : default;
                command = new CombatCommand(
                    command.Kind,
                    command.ActorId,
                    command.TargetId,
                    command.ProfileId,
                    rolls,
                    command.Destination,
                    command.SlotIndex,
                    command.ContextVersion, attackDirection: command.AttackDirection, targetRolls: targetRolls,
                    destinationFacing: command.DestinationFacing, targetCell: command.TargetCell);
            }
            CombatActionResult result;
            if (command.Kind == CombatCommandKind.UseItem)
            {
                CombatCommandPreview itemQuote = quote ?? commandService.Preview(session, command);
                result = commandService.Prepare(session, command, itemQuote, out Action commitCombat);
                if (!result.Succeeded) return result;
                if (prepareItemConsumption == null) return CombatActionResult.Rejected("item_definition_invalid");
                if (!prepareItemConsumption(command.ItemId, command.ItemQuantityCost, itemQuote.InventoryVersion,
                    out Action commitInventory, out string reason)) return CombatActionResult.Rejected(reason);
                // No yield or publication between applying the two fully prepared candidates.
                commitCombat();
                commitInventory();
            }
            else if (command.Kind == CombatCommandKind.SwapEquipment)
            {
                if (!equipmentOwner.TryPrepareEquipmentChange(equipmentCatalog, command.EquipmentSlotId, command.ItemId,
                    command.InstanceId, ownsEquipment(command.ActorId, command.ItemId, command.InstanceId),
                    out Action commitEquipment, out string reason)) return CombatActionResult.Rejected(reason);
                result = commandService.Prepare(session, command, quote ?? commandService.Preview(session, command), out Action commitCombat);
                if (result.Succeeded)
                {
                    // Both candidates are complete. These synchronous assignments have no remaining business rejection.
                    commitCombat();
                    commitEquipment();
                }
            }
            else result = quote == null ? commandService.Execute(session, command) : commandService.Execute(session, command, quote);
            if (result.Succeeded && command.Kind != CombatCommandKind.EndTurn && command.Jindan?.TideAction == null)
                ProjectPresentation(command, result, actorBefore, actorStart, quote);
            if (result.Succeeded) ProjectFlightStates(flightBefore);
            if (result.Succeeded) surfaceRoutine = StartCoroutine(surfacePlayback.Play(session, unitPresentation, presentation.AppendLog));
            return result;
        }

        private bool RefreshGrowth()
        {
            if (session == null || equipmentOwner == null || spawned?.IsJindanRehearsal == true) return false;
            return session.RefreshActionEconomy("player", equipmentOwner.Attributes.ActionPointMaximumUnits,
                equipmentOwner.Attributes.MovementEfficiencyMilliUnits, equipmentOwner.Progression.RealmStage,
                string.IsNullOrEmpty(equipmentOwner.MainEquipmentBasicAttackProfileId)
                    ? equipmentOwner.UnarmedBasicAttackProfileId : equipmentOwner.MainEquipmentBasicAttackProfileId);
        }

        private void Present(string turnText, bool acceptsCommands, IReadOnlyList<CombatTurnSnapshot> advanceSteps = null)
        {
            if (session == null) return;
            RefreshGrowth();
            session.Combatants.TryGet(ControlledActorId, out CombatantSnapshot player);
            presentedInventoryVersion = inventoryVersion?.Invoke() ?? 0;
            CombatantSnapshot enemy = spawned.Enemies.FirstOrDefault(value => value.IsAlive) ?? spawned.Enemy;
            acceptsCommands = acceptsCommands && !IsPresenting;
            ++commandContextVersion;
            presentation.Present(new CombatHudSnapshot(
                ToHud(player, ActorName(player.Id), PortraitKey(player.Id)),
                ToHud(enemy, enemy == null ? string.Empty : ActorName(enemy.Id),
                    enemy == null ? null : PortraitKey(enemy.Id)),
                turnText,
                acceptsCommands,
                player?.EquippedArtProfileIds,
                spawned.PlayerDivineProfileIds,
                timeline: BuildTimeline(advanceSteps),
                canEndTurn: acceptsCommands && player != null && commandService.Validate(session,
                    new CombatCommand(CombatCommandKind.EndTurn, player.Id)).Succeeded,
                movement: BuildMovement(player, acceptsCommands),
                isPresenting: IsPresenting || (advanceSteps != null && advanceSteps.Count > 1 &&
                    advanceSteps[advanceSteps.Count - 1].CurrentTick > advanceSteps[0].CurrentTick),
                artActions: BuildArtActions(player),
                commandContextVersion: commandContextVersion,
                actions: BuildActions(player, enemy),
                actionPointLimitUnits: player?.ActionPointLimitAtTurnStartUnits ?? 0,
                pendingCtPenalty: session.PendingCtPenalty,
                enemies: spawned.Players.Concat(spawned.Enemies).Where(value => value.Id != player.Id)
                    .Select(value => ToHud(value, ActorName(value.Id), PortraitKey(value.Id))).ToArray(),
                sceneObjects: session.SceneObjects?.All.Select(value => ProjectSceneObject(value)).ToArray()));
        }

        private CombatSceneObjectSnapshot ProjectSceneObject(CombatSceneObject value, int predictedDamage = 0) =>
            new CombatSceneObjectSnapshot(value.Id, value.Definition.ContentType,
                resolveText("scene_object_" + value.Definition.ContentType), value.Definition.MaterialType,
                value.Durability, value.Definition.MaxDurability, value.IsDestroyed ? 2 : value.IsDamaged ? 1 : 0,
                value.Definition.ResidueType, predictedDamage);

        private CombatMovementSnapshot BuildMovement(CombatantSnapshot player, bool acceptsCommands)
        {
            if (player == null) return null;
            var options = new List<CombatMovementOption>();
            if (acceptsCommands && player.ActionPointsRemainingUnits > 0)
            {
                var occupied = session.Combatants.All.Where(unit => unit.IsAlive && unit.Id != player.Id)
                    .SelectMany(unit => unit.OccupiedCells).ToArray();
                foreach (var cell in session.SpatialQuery.FindReachable(player.Pose, player.ActionPointsRemainingUnits, player.MovementEfficiencyMilliUnits, occupied)
                             .OrderBy(pair => pair.Value).ThenBy(pair => pair.Key.Facing).GroupBy(pair => pair.Key.Anchor).Select(group => group.First()))
                {
                    CombatCommandPreview quote = commandService.Preview(session, new CombatCommand(
                        CombatCommandKind.Move, player.Id, destination: cell.Key.Anchor, destinationFacing: cell.Key.Facing));
                    if (quote.IsLegal)
                    {
                        var path = quote.MovementPath.Select(ToPresentationHex).ToArray();
                        var destination = ToPresentationHex(cell.Key.Anchor);
                        var steps = quote.MovementSteps.Select(ToPresentationStep).ToArray();
                        options.Add(new CombatMovementOption(destination, quote.ActionPointCostUnits, path,
                            new CombatActionCandidate(CombatActionKind.Move, player.Id, null, null,
                                commandContextVersion, destination, quote.ActionPointCostUnits, path,
                                destinationFacing: cell.Key.Facing, movementSteps: steps), steps));
                    }
                }
            }
            return new CombatMovementSnapshot(ToPresentationHex(player.Position), player.ActionPointsRemainingUnits,
                session.TurnPhase.ToString(), options);
        }

        private IReadOnlyList<CombatArtActionSnapshot> BuildArtActions(CombatantSnapshot player)
        {
            var actions = new List<CombatArtActionSnapshot>();
            if (player == null) return actions;
            foreach (string id in player.EquippedArtProfileIds)
            {
                session.TryGetProfile(id, out CombatAttackProfile profile);
                var targets = new List<CombatArtTargetSnapshot>();
                var castableCells = new List<CombatUnitPresentationHex>();
                CombatActionPreviewSnapshot entryQuote = null;
                if (profile != null)
                {
                    foreach (HexCoord cell in battlefieldCells)
                        if (session.QueryAttackCell(player.Id, id, cell).Cells[0].IsLegal)
                            castableCells.Add(ToPresentationHex(cell));
                    CombatUnitPresentationHex entryCell = profile.MinimumRange == 0 ? ToPresentationHex(player.Position) :
                        castableCells.FirstOrDefault();
                    var entryCandidate = new CombatActionCandidate(CombatActionKind.Art, player.Id, null, id,
                        commandContextVersion, targetCell: entryCell);
                    CombatCommand entryCommand = ToCommand(entryCandidate);
                    entryQuote = ProjectQuote(entryCandidate, entryCommand, commandService.Preview(session, entryCommand));
                    // Target availability follows the profile's own team rule, never a spell ID.
                    // Keep the player first so self-target labels and default previews stay stable.
                    var ordered = new List<CombatantSnapshot> { player };
                    ordered.AddRange(session.Combatants.All.Where(target => target.Id != player.Id));
                    foreach (CombatantSnapshot target in ordered)
                    {
                        if (!target.IsAlive || !profile.AllowsTarget(player.Team, target.Team)) continue;
                        AddTarget(target, ActorName(target.Id));
                    }
                }
                actions.Add(new CombatArtActionSnapshot(id, displayProfile(id), profile?.CtPenalty ?? 0, targets,
                    profile?.MinimumRange ?? 0, profile?.MaximumRange ?? 0, castableCells, entryQuote));

                void AddTarget(CombatantSnapshot target, string name)
                {
                    var candidate = new CombatActionCandidate(CombatActionKind.Art, player.Id, target.Id, id, commandContextVersion);
                    CombatCommand command = ToCommand(candidate);
                    CombatCommandPreview quote = commandService.Preview(session, command);
                    targets.Add(new CombatArtTargetSnapshot(target.Id, name, quote.SpiritCost,
                        quote.IsLegal, quote.RejectionReason, ProjectQuote(candidate, command, quote)));
                }
            }
            return actions;
        }

        private IReadOnlyList<CombatActionPreviewSnapshot> BuildActions(CombatantSnapshot player, CombatantSnapshot enemy)
        {
            var actions = new List<CombatActionPreviewSnapshot>();
            if (player == null) return actions;
            Add(CombatActionKind.BasicAttack, enemy?.Id);
            if (session.TryGetProfile(player.BasicAttackProfileId, out var basic) && basic.Area != CombatAttackArea.Single)
            {
                for (int direction = 0; direction < 6; direction++)
                    if (direction != player.Facing) Add(CombatActionKind.BasicAttack, direction: direction);
            }
            else
            {
                foreach (HexCoord cell in battlefieldCells)
                    Add(CombatActionKind.BasicAttack, targetCell: ToPresentationHex(cell));
            }
            Add(CombatActionKind.Guard);
            Add(CombatActionKind.Wait);
            Add(CombatActionKind.EndTurn);
            foreach (CombatCommand item in GetItemCandidates(player.Id))
            {
                var candidate = new CombatActionCandidate(CombatActionKind.UseItem, player.Id, player.Id, null,
                    commandContextVersion, itemId: item.ItemId);
                actions.Add(ProjectQuote(candidate, item, commandService.Preview(session, item)));
            }
            foreach (CombatCommand equipment in GetEquipmentCandidates(player.Id))
            {
                var candidate = new CombatActionCandidate(CombatActionKind.SwapEquipment, player.Id, null, equipment.ProfileId,
                    commandContextVersion, equipmentSlotId: equipment.EquipmentSlotId, itemId: equipment.ItemId, instanceId: equipment.InstanceId);
                actions.Add(ProjectQuote(candidate, equipment, commandService.Preview(session, equipment)));
            }
            foreach (string profileId in spawned.PlayerDivineProfileIds)
            {
                if (session.TryGetProfile(profileId, out var divine) && divine.Effect == CombatAttackEffect.RestoreSpirit)
                    Add(CombatActionKind.Divine, player.Id, profileId);
                foreach (CombatantSnapshot target in spawned.Enemies.Where(value => value.IsAlive))
                    Add(CombatActionKind.Divine, target.Id, profileId);
            }
            for (int slot = 0; slot < player.EquippedArtProfileIds.Count; slot++)
                foreach (string profileId in player.AvailableArtProfileIds)
                    Add(CombatActionKind.SwapSpell, profile: profileId, slot: slot);
            return actions;

            void Add(CombatActionKind kind, string target = null, string profile = null, int slot = -1, int? direction = null,
                CombatUnitPresentationHex? targetCell = null)
            {
                var candidate = new CombatActionCandidate(kind, player.Id, target, profile, commandContextVersion,
                    slotIndex: slot, attackDirection: direction, targetCell: targetCell);
                CombatCommand command = ToCommand(candidate);
                actions.Add(ProjectQuote(candidate, command, commandService.Preview(session, command)));
            }
        }

        private CombatTimelineSnapshot BuildTimeline(IReadOnlyList<CombatTurnSnapshot> advanceSteps = null)
        {
            CombatTurnSnapshot turns = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            string current = session.TurnPhase == CombatTurnPhase.Finished ? null : session.CurrentActorId;
            return ProjectTimeline(turns, current, advanceSteps?.Select(step => ProjectTimeline(step, null)).ToArray());
        }

        private CombatTimelineSnapshot ProjectTimeline(CombatTurnSnapshot turns, string current,
            IReadOnlyList<CombatTimelineSnapshot> advanceSteps = null)
        {
            var units = turns.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
            var entries = new List<CombatTimelineEntrySnapshot>();
            var included = new HashSet<string>(StringComparer.Ordinal);
            void Add(string id, int queuePosition)
            {
                if (string.IsNullOrEmpty(id) || !units.TryGetValue(id, out CombatTurnUnitSnapshot unit) || !included.Add(id)) return;
                session.Combatants.TryGet(id, out CombatantSnapshot combatant);
                bool isPlayer = combatant.Team == CombatTeam.Player;
                entries.Add(new CombatTimelineEntrySnapshot(id,
                    ActorName(id),
                    isPlayer ? CombatUnitDisplayFaction.Player : CombatUnitDisplayFaction.Enemy,
                    unit.Speed, unit.ChargeTime, unit.NextActionThreshold, unit.IsReady, queuePosition,
                    string.Equals(current, id, StringComparison.Ordinal), unit.TicksUntilReady,
                    PortraitKey(id)));
            }
            Add(current, -1);
            // A recorded boundary can contain the first ready actor already dequeued but not yet begun.
            foreach (CombatTurnUnitSnapshot unit in turns.Units)
                if (unit.IsReady && !turns.ReadyQueueIds.Contains(unit.Id)) Add(unit.Id, -1);
            for (int index = 0; index < turns.ReadyQueueIds.Count; index++) Add(turns.ReadyQueueIds[index], index);
            foreach (string id in turns.ChargingUnitIds) Add(id, -1);
            return new CombatTimelineSnapshot(turns.CurrentTick, current, entries, advanceSteps);
        }

        private void Complete(CombatSessionOutcome outcome)
        {
            if (session == null) return;
            acceptsPlayerCommand = false;
            presentation.AppendLog(outcome == CombatSessionOutcome.Victory ? "战斗胜利" : "战斗失败");
            Present(resolveText(outcome switch
            {
                CombatSessionOutcome.Victory => "combat_outcome_victory",
                CombatSessionOutcome.Defeat => "combat_outcome_defeat",
                _ => "combat_outcome_ongoing",
            }), false);
            EnemyData defeated = outcome == CombatSessionOutcome.Victory ? spawned.EnemyData : null;
            session.Jindan?.Clear(session);
            session.Flight?.Clear(session);
            foreach (CombatantSnapshot player in spawned.Players) unitPresentation.Remove(player.Id);
            foreach (CombatantSnapshot enemy in spawned.Enemies) unitPresentation.Remove(enemy.Id);
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
                    CombatUnitPresentationProfileCatalogData.PlayerCombatantId, out string playerProfileId))
            {
                reason = "adventure_presentation_profile_unresolved";
                return false;
            }

            var result = new List<CombatUnitPresentationDescriptor>
            {
                new CombatUnitPresentationDescriptor(
                    values.Player.Id,
                    playerProfileId,
                    CombatUnitDisplayFaction.Player,
                    ToPresentationHex(values.Player.Position),
                    values.Player.Facing),
            };
            for (int index = 0; index < values.Enemies.Count; index++)
            {
                CombatantSnapshot enemy = values.Enemies[index];
                if (!presentationProfileCatalog.TryGetPresentationProfileId(
                        values.EnemyDataList[index].enemyId, out string enemyProfileId))
                {
                    reason = "adventure_presentation_profile_unresolved";
                    return false;
                }
                result.Add(new CombatUnitPresentationDescriptor(
                    enemy.Id,
                    enemyProfileId,
                    CombatUnitDisplayFaction.Enemy,
                    ToPresentationHex(enemy.Position),
                    enemy.Facing, enemy.Footprint == SpatialFootprintKind.AdjacentPair));
            }
            descriptors = result;
            reason = null;
            return true;
        }

        private EnemyData EnemyDataFor(string combatantId)
        {
            if (spawned != null)
                for (int index = 0; index < spawned.Enemies.Count; index++)
                    if (string.Equals(spawned.Enemies[index].Id, combatantId, StringComparison.Ordinal))
                        return spawned.EnemyDataList[index];
            return spawned?.EnemyData;
        }

        private void ProjectPresentation(
            CombatCommand command,
            CombatActionResult result,
            CombatantSnapshot actorBefore,
            CombatUnitPresentationHex actorStart, CombatCommandPreview quote)
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
                if (!actorBefore.IsAlive) actionEvent = CombatUnitPresentationEvent.Death;
                // The actor event carries every affected target: heals for restored spirit and the
                // damage group, so presentation can place one impact per affected unit without
                // reading combat state. Individual Hit/Death events still follow separately.
                CombatUnitPresentationTargetResult TargetResult(string id, int damage, bool dead, int recovered = 0)
                {
                    var contact = result.TargetContacts.FirstOrDefault(value => value.TargetId == id);
                    return new CombatUnitPresentationTargetResult(id, damage, dead, recovered,
                        contact.TargetId == null ? null : ToPresentationHex(contact.Contact.SourceCell),
                        contact.TargetId == null ? null : ToPresentationHex(contact.Contact.TargetCell));
                }
                CombatUnitPresentationTargetResult[] affected = result.SpiritRecoveries
                    .Select(value => TargetResult(value.TargetId, 0, false, value.ActualRecovery))
                    .Concat(result.Damage.GroupBy(item => item.TargetId, StringComparer.Ordinal)
                        .Select(group => TargetResult(
                            group.Key, group.Sum(item => item.FinalDamage), false)))
                    .ToArray();
                // Attack and cast turn the actor toward the action's target for presentation only;
                // the authoritative combatant facing stays untouched.
                int facing = actorBefore.Facing;
                if (actorBefore.Footprint == SpatialFootprintKind.Single && command.TargetCell.HasValue &&
                    command.TargetCell.Value != actorBefore.Position)
                    facing = command.AttackDirection ?? actorBefore.Position.DirectionToward(command.TargetCell.Value);
                else if (actorBefore.Footprint == SpatialFootprintKind.Single && command.Kind == CombatCommandKind.BasicAttack &&
                    command.AttackDirection is >= 0 and < 6)
                {
                    facing = command.AttackDirection.Value;
                }
                else if (actorBefore.Footprint == SpatialFootprintKind.Single && command.Kind is CombatCommandKind.Art or CombatCommandKind.Divine &&
                         !string.IsNullOrWhiteSpace(command.TargetId) &&
                         !string.Equals(command.TargetId, actorBefore.Id, StringComparison.Ordinal) &&
                         session.Combatants.TryGet(command.TargetId, out CombatantSnapshot aimed) && aimed != null)
                {
                    facing = FacingToward(actorBefore.Position, aimed.Position);
                }
                unitPresentation.Present(new CombatUnitPresentationEventProjection(
                    actorBefore.Id,
                    actionEvent,
                    actorStart,
                    ToPresentationHex(actorBefore.Position),
                    facing,
                    affected,
                    result.MovementPath.Select(ToPresentationHex).ToArray(),
                    profileId: command.ProfileId, movementSteps: result.MovementSteps.Select(ToPresentationStep).ToArray(),
                    altitudeBand: actorBefore.AltitudeBand,
                    targetCell: command.TargetCell.HasValue ? ToPresentationHex(command.TargetCell.Value) : null,
                    sourceCell: command.TargetCell.HasValue && quote.AttackArea != null
                        ? ToPresentationHex(quote.AttackArea.CellContacts.First(contact => contact.TargetCell == command.TargetCell.Value).SourceCell) : null));

                foreach (var targetDamage in result.Damage.GroupBy(item => item.TargetId, StringComparer.Ordinal))
                {
                    if (!session.Combatants.TryGet(targetDamage.Key, out CombatantSnapshot target))
                        throw new InvalidOperationException("combat_presentation_damage_target_missing");
                    int finalDamage = targetDamage.Sum(item => item.FinalDamage);
                    bool isDead = !target.IsAlive;
                    unitPresentation.Present(new CombatUnitPresentationEventProjection(
                        target.Id, isDead ? CombatUnitPresentationEvent.Death : CombatUnitPresentationEvent.Hit,
                        ToPresentationHex(target.Position), ToPresentationHex(target.Position), target.Facing,
                        new[] { TargetResult(target.Id, finalDamage, isDead) }, altitudeBand: target.AltitudeBand));
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[AdventurePresentation] " + exception.Message);
            }
        }

        private static CombatUnitPresentationHex ToPresentationHex(TianZhang.Spatial.HexCoord value) =>
            new CombatUnitPresentationHex(value.Q, value.R);

        private static CombatUnitPresentationPose ToPresentationPose(SpatialPose pose) =>
            new CombatUnitPresentationPose(ToPresentationHex(pose.Anchor), pose.Facing, pose.Footprint == SpatialFootprintKind.AdjacentPair, pose.AltitudeBand);

        private Dictionary<string, (SpatialPose Pose, int Health)> SnapshotFlightStates() =>
            session.Flight?.States.ToDictionary(value => value.ActorId, value =>
            {
                session.Combatants.TryGet(value.ActorId, out var actor);
                return (actor.Pose, actor.CurrentHealth);
            });

        private void ProjectFlightStates(Dictionary<string, (SpatialPose Pose, int Health)> before)
        {
            if (session?.Flight == null || before == null) return;
            foreach (var flight in session.Flight.States)
            {
                session.Combatants.TryGet(flight.ActorId, out var actor);
                var previous = before[actor.Id];
                if (previous.Health <= 0 || previous.Pose == actor.Pose && previous.Health == actor.CurrentHealth) continue;
                int damage = Math.Max(0, previous.Health - actor.CurrentHealth - surfacePlayback.PendingDamage(session, actor.Id));
                if (previous.Pose.AltitudeBand == actor.AltitudeBand && damage == 0) continue;
                unitPresentation.Present(new CombatUnitPresentationEventProjection(actor.Id,
                    !actor.IsAlive ? CombatUnitPresentationEvent.Death : damage > 0 ? CombatUnitPresentationEvent.Hit : CombatUnitPresentationEvent.Idle,
                    ToPresentationHex(actor.Position), ToPresentationHex(actor.Position), actor.Facing,
                    damage == 0 ? null : new[] { new CombatUnitPresentationTargetResult(actor.Id, damage, !actor.IsAlive) },
                    altitudeBand: actor.AltitudeBand));
            }
        }

        private static CombatUnitPresentationMovementStep ToPresentationStep(SpatialMovementStep step) =>
            new CombatUnitPresentationMovementStep(ToPresentationPose(step.From), ToPresentationPose(step.To), step.TurnFacings, step.IsLightLeap);

        private static bool SameMovementSteps(IReadOnlyList<CombatUnitPresentationMovementStep> shown,
            IReadOnlyList<SpatialMovementStep> quoted)
        {
            if (shown.Count != quoted.Count) return false;
            for (int index = 0; index < shown.Count; index++)
            {
                var left = shown[index]; var right = quoted[index];
                if (!left.From.Position.Equals(ToPresentationHex(right.From.Anchor)) || left.From.Facing != right.From.Facing ||
                    left.From.IsDoubleCell != (right.From.Footprint == SpatialFootprintKind.AdjacentPair) ||
                    left.From.AltitudeBand != right.From.AltitudeBand ||
                    !left.To.Position.Equals(ToPresentationHex(right.To.Anchor)) || left.To.Facing != right.To.Facing ||
                    left.To.IsDoubleCell != (right.To.Footprint == SpatialFootprintKind.AdjacentPair) ||
                    left.To.AltitudeBand != right.To.AltitudeBand ||
                    !left.TurnFacings.SequenceEqual(right.TurnFacings)) return false;
            }
            return true;
        }

        /// <summary>Presentation-only facing: the neighbour step that best approaches the target cell.</summary>
        private static int FacingToward(TianZhang.Spatial.HexCoord from, TianZhang.Spatial.HexCoord to)
        {
            int deltaQ = to.Q - from.Q, deltaR = to.R - from.R;
            int best = 0;
            long bestProjection = long.MinValue;
            for (int direction = 0; direction < 6; direction++)
            {
                TianZhang.Spatial.HexCoord step = TianZhang.Spatial.HexCoord.Directions[direction];
                long projection = (long)deltaQ * step.Q + (long)deltaR * step.R;
                if (projection <= bestProjection) continue;
                bestProjection = projection;
                best = direction;
            }
            return best;
        }

        private static CombatantHudSnapshot ToHud(CombatantSnapshot value, string name, string portraitKey = null)
        {
            return value == null ? null : new CombatantHudSnapshot(
                value.Id,
                name,
                value.CurrentHealth,
                value.MaximumHealth,
                value.CurrentSpirit,
                value.MaximumSpirit,
                portraitKey);
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
                CombatCommandKind.SwapEquipment => "combat_command_swap_equipment",
                CombatCommandKind.UseItem => "combat_command_use_item",
                _ => throw new ArgumentOutOfRangeException(nameof(command.Kind)),
            });
            if (command.Kind == CombatCommandKind.Art) commandName = displayProfile(command.ProfileId);
            if (command.Kind == CombatCommandKind.UseItem) commandName += "：" + resolveText(command.ItemId);
            if (result.SceneObjectChanges.Count > 0)
                commandName += "，" + string.Join(" / ", result.SceneObjectChanges.Select(change =>
                {
                    var item = session.SceneObjects.Find(change.Id);
                    return resolveText("scene_object_" + item.Definition.ContentType) + " 耐久 " + change.Before + "→" + change.After;
                }));
            if (result.SpiritRecoveries.Count > 0)
                return actor + "：" + commandName + "，恢复灵力 " + result.SpiritRecoveries.Sum(item => item.ActualRecovery) +
                    "，消耗灵力 " + result.SpiritSpent;
            if (result.Damage.Count > 0)
                return actor + "：" + commandName + "，伤害 " + result.Damage.Sum(item => item.FinalDamage) +
                    (result.SpiritSpent > 0 ? "，消耗灵力 " + result.SpiritSpent : string.Empty);
            return actor + "：" + commandName;
        }
    }
}
