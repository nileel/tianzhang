using System;
using System.IO;
using System.Linq;

namespace TianZhang.Entity
{
    public static class JindanDemoCatalogValidator
    {
        public static bool Validate(JindanDemoCatalogData catalog, bool requireBindings, out string reason)
        {
            try
            {
                Check(catalog != null && catalog.contentVersion == "demo_20261024_v1", "VERSION");
                Unique(catalog.presets, p => p.characterId, 5, "PRESET");
                Unique(catalog.seats, s => s.seatInstanceId, 7, "SEAT");
                Unique(catalog.abilities, a => a.definitionId, 15, "ABILITY");
                Unique(catalog.contracts, c => c.contractId, 8, "CONTRACT");
                Unique(catalog.commands, c => c.commandId, -1, "COMMAND");
                Unique(catalog.segments, s => s.segmentId, 3, "SEGMENT");
                Check(catalog.seats.Select(s => s.positionId).Distinct().Count() == 7, "DUPLICATE_POSITION");
                Check(catalog.seats.Select(s => s.guardianAbilityInstanceId).Distinct().Count() == 7, "DUPLICATE_CARRIER");
                foreach (var ability in catalog.abilities) ValidateAbility(catalog, ability);
                foreach (var command in catalog.commands) ValidateCommand(catalog, command);
                foreach (string kind in new[] { "FLIGHT_TAKEOFF", "FLIGHT_ASCEND", "FLIGHT_DESCEND", "FLIGHT_LAND", "FLIGHT_RECOVER" })
                    Check(catalog.commands.Count(c => c.effectId == "FLIGHT" && c.kind == kind && c.enabled) == 1,
                        "FLIGHT_COMMAND_SET");
                foreach (var contract in catalog.contracts)
                    Check(contract.compatibilityProfileId == JindanDemoCompatibilityCompiler.Compile(catalog, contract), "STALE_COMPILATION");
                foreach (var preset in catalog.presets) ValidatePreset(catalog, preset, requireBindings);
                foreach (var segment in catalog.segments)
                {
                    Check(segment.actors != null && segment.actors.Length >= 2 &&
                          segment.actors.Select(a => a.characterId).Distinct().Count() == segment.actors.Length &&
                          segment.actors.Select(a => a.q + ":" + a.r).Distinct().Count() == segment.actors.Length, "SEGMENT_ACTORS");
                    foreach (var actor in segment.actors)
                        Check(catalog.GetPreset(actor.characterId) != null && actor.team is 0 or 1 &&
                              actor.facing >= 0 && actor.facing < 6, "SEGMENT_ACTOR_SOURCE");
                }
                ValidateTuning(catalog.loadoutTuning);
                Check(catalog.demolition != null && catalog.GetPreset(catalog.demolition.characterId) != null &&
                    catalog.demolition.sourceProfileId == "spell_guanshiji" && catalog.demolition.profileId == "demo_20261026_polei" &&
                    catalog.demolition.environmentDamage == 375 && catalog.demolition.environmentTargetTypes.SequenceEqual(new[]{"high_wall","stone_support"}) &&
                    !string.IsNullOrWhiteSpace(catalog.demolition.sourceDocument), "DEMOLITION_CONFIGURATION");
                reason = null;
                return true;
            }
            catch (Exception error) when (error is InvalidDataException || error is ArgumentException ||
                                           error is InvalidOperationException || error is NullReferenceException)
            {
                reason = error is InvalidDataException ? error.Message : "JD_DEMO_INCOMPLETE_CONTENT";
                return false;
            }
        }

        public static void ValidateFoundation(JindanDemoCatalogData catalog, FoundationPurpleMansionStateData state)
        {
            var preset = catalog.GetPreset(state.characterId);
            Check(preset != null, "UNREGISTERED_FOUNDATION");
            var foundation = state.foundationState;
            Check(foundation != null && foundation.foundationInstanceId == preset.characterId + "_foundation" &&
                  foundation.foundationDefinitionId == preset.foundationDefinitionId &&
                  foundation.sourceGongFaId == preset.sourceGongFaId &&
                  foundation.phaseBoundarySetId == preset.phaseBoundarySetId &&
                  foundation.carryingCapacityProfileId == preset.carryingCapacityProfileId &&
                  foundation.stageId == FoundationStage.Complete && foundation.stageCode == 30 &&
                  foundation.naturalMansionCapacity == preset.naturalMansionCapacity &&
                  foundation.currentMansionCarryingCapacity == preset.naturalMansionCapacity &&
                  foundation.completionMansionCapacity == preset.naturalMansionCapacity &&
                  foundation.expandedMansionCapacity == 0 && foundation.expansionGrants.Length == 0 &&
                  state.jindanLock?.status == JindanLockStatus.Formed, "FOUNDATION_SOURCE");
            Check(state.mansionStates.Length == 5 && state.guardianAbilities.Length == preset.naturalMansionCapacity, "FIVE_MANSIONS");
            foreach (var seatId in preset.seatInstanceIds)
            {
                var seat = catalog.GetSeat(seatId);
                var guardian = state.guardianAbilities.SingleOrDefault(g => g.abilityInstanceId == seat.guardianAbilityInstanceId);
                var definition = catalog.GetAbility(seat.guardianDefinitionId);
                Check(guardian != null && guardian.abilityDefinitionId == definition.definitionId &&
                      guardian.sourceSpellId == definition.sourceSpellId && guardian.upgradePlanId == definition.upgradePlanId &&
                      guardian.sourceSpellDisposition == "INTERNALIZE", "GUARDIAN_SOURCE");
                var mansion = state.mansionStates.SingleOrDefault(m => m.guardianAbilityInstanceId == guardian.abilityInstanceId);
                Check(mansion != null && mansion.state == PurpleMansionBuildState.Complete &&
                      definition.compatibleMansions.Contains(mansion.mansionKind.ToString().ToUpperInvariant()) &&
                      mansion.mansionBodyEffectBindingId == mansion.mansionInstanceId + "_body", "MANSION_SOURCE");
            }
        }

        private static void ValidatePreset(JindanDemoCatalogData catalog, JindanDemoPresetDefinition preset, bool bindings)
        {
            Check(preset.realmId == "realm_jindan" && preset.actionPointMaximumUnits > 0 &&
                  preset.realmMultiplier > 0 && preset.basicAttackProfileId == "basic_body_human_jindan" &&
                  preset.basicAttackSourceModeMask == 3 && preset.basicAttackTargetModeMask == 3 &&
                  preset.movementEfficiencyMilliUnits > 0 && preset.manaMaximum > 0 && preset.normalVisionRange >= 4 &&
                  preset.rootElements.Length > 0 && preset.rootElements.Distinct().Count() == preset.rootElements.Length &&
                  preset.rootGrade is 2 or 3 && preset.naturalMansionCapacity == preset.rootGrade - 1, "PRESET_RESOURCES");
            Check(preset.seatInstanceIds.Length == preset.naturalMansionCapacity &&
                  preset.seatInstanceIds.Distinct().Count() == preset.seatInstanceIds.Length, "PRESET_SEATS");
            var owned = preset.seatInstanceIds.Select(catalog.GetSeat).ToArray();
            Check(owned.All(s => s != null && s.characterId == preset.characterId) &&
                  owned.Select(s => s.positionType).Distinct().Count() == owned.Length, "PRESET_OWNERSHIP");
            foreach (var seat in owned)
            {
                Check(seat.candidateEffectIds.Length > 0 && seat.candidateEffectIds.Contains(seat.defaultEffectId) &&
                      seat.candidateEffectIds.Distinct().Count() == seat.candidateEffectIds.Length, "LOADOUT_CANDIDATES");
                foreach (string effect in seat.candidateEffectIds)
                {
                    Check(catalog.contracts.Count(c => c.positionId == seat.positionId && c.effectId == effect) == 1, "MISSING_CONTRACT");
                    foreach (string kind in new[] { "DEPLOY", "TERMINATE" })
                        Check(catalog.commands.Count(c => c.effectId == effect && c.kind == kind &&
                              c.abilityDefinitionId == seat.guardianDefinitionId) == 1, "MISSING_LOADOUT_COMMAND");
                }
            }
            if (!string.IsNullOrEmpty(preset.flightAbilityInstanceId))
                Check(preset.flightAbilityInstanceId == preset.characterId + "_flight" &&
                      catalog.GetAbility("demo_20261024_yufeng_flight")?.kind == "FLIGHT", "FLIGHT_SOURCE");
            if (!bindings) return;
            Check(preset.sourceCharacterDefinition != null && preset.sourceGongFa != null &&
                  preset.foundationState != null && preset.jindanState != null, "MISSING_ASSET_BINDING");
            ValidateFoundation(catalog, preset.foundationState);
            var root = preset.jindanState;
            Check(root.characterId == preset.characterId && root.foundationPurpleMansionStateRef == preset.characterId + "_foundation" &&
                  root.jindanCoreBinding.boundDanshuCoreId == preset.characterId + "_core" &&
                  root.danxiang.danxiangInstanceId == preset.characterId + "_danxiang" &&
                  root.stablePositionBindings.Length == owned.Length && root.abilityLedgerBindings.Length == owned.Length, "STATIC_IDENTITY");
            foreach (var seat in owned)
            {
                var binding = root.stablePositionBindings.SingleOrDefault(b => b.positionId == seat.positionId);
                var contract = catalog.contracts.Single(c => c.positionId == seat.positionId && c.effectId == seat.defaultEffectId);
                Check(binding != null && binding.primaryCarrierAbilityInstanceId == seat.guardianAbilityInstanceId &&
                      binding.equippedBaseEffectId == seat.defaultEffectId && binding.compatibilityProfileId == contract.compatibilityProfileId &&
                      binding.auxiliaryCarrierAbilityInstanceIds.Length == 0, "STATIC_LOADOUT");
            }
        }

        private static void ValidateAbility(JindanDemoCatalogData catalog, JindanDemoAbilityDefinition ability)
        {
            Check(!string.IsNullOrWhiteSpace(ability.sourceDocument) && ability.definitionVersion == "1.0" &&
                  !string.IsNullOrWhiteSpace(ability.displayNameKey), "ABILITY_DEFINITION");
            Check(ability.kind is "SOURCE_SPELL" or "GUARDIAN" or "FLIGHT", "ABILITY_KIND");
            if (ability.kind != "GUARDIAN") return;
            var source = catalog.GetAbility(ability.sourceSpellId);
            Check(source?.kind == "SOURCE_SPELL" && ability.upgradePlanId == source.definitionId + "_upgrade_v1" &&
                  ability.sourceDisposition == "INTERNALIZE" && ability.compatibleMansions.Length > 0, "ABILITY_UPGRADE");
        }

        private static void ValidateCommand(JindanDemoCatalogData catalog, JindanDemoCommandDefinition command)
        {
            Check(catalog.GetAbility(command.abilityDefinitionId) != null && !string.IsNullOrWhiteSpace(command.sourceDocument) &&
                  command.actionPointCostUnits > 0 && command.manaCost >= 0 && command.ctPenalty >= 0 &&
                  command.cooldownTicks >= 0 && command.upkeepMana >= 0 && command.remoteOriginManaCost >= 0 &&
                  command.durationCount >= 0 && command.capacity >= 0 && command.maximumInstances >= 0, "COMMAND_VALUES");
            Check(command.sourceModeMask > 0 && command.sourceModeMask <= 3 && command.targetModeMask >= 0 &&
                  command.targetModeMask <= 3 && command.range >= 0 && command.verticalReachRank >= 0, "COMMAND_SPACE");
            if (command.enabled)
            {
                bool flight = command.effectId == "FLIGHT" && command.abilityDefinitionId == "demo_20261024_yufeng_flight";
                bool earth = command.effectId is "EARTH_MANIFEST" or "EARTH_GRAVITY" && command.abilityDefinitionId == "demo_20261024_guardian_houzai";
                Check((flight && command.kind is ("FLIGHT_TAKEOFF" or "FLIGHT_ASCEND" or "FLIGHT_DESCEND" or "FLIGHT_LAND" or "FLIGHT_RECOVER") ||
                      earth && command.kind is ("DEPLOY" or "TERMINATE") ||
                      command.effectId == "SPACE_SCALE" && command.abilityDefinitionId == "demo_20261024_guardian_fangwei_dingchi" &&
                      command.kind is ("DEPLOY" or "TERMINATE")) && string.IsNullOrEmpty(command.unavailableReasonKey), "UNIMPLEMENTED_COMMAND_ENABLED");
                if (flight)
                    Check(command.upkeepClock == "OWN_TURN_START" && command.durationClock == "OWN_TURN_END" &&
                        command.durationCount == 3 && command.upkeepMana == 100 && command.ctPenalty == 0 &&
                        command.maximumInstances == 1 && command.capacity == 1, "FLIGHT_CONFIGURATION");
                else if (earth && command.kind == "DEPLOY")
                    Check(command.actionPointCostUnits == 250000 && command.manaCost == 400 && command.upkeepMana == 0 &&
                        command.range == 4 && command.verticalReachRank == 3 && command.maximumInstances == 1 &&
                        command.durationCount == (command.effectId == "EARTH_MANIFEST" ? 3 : 2) &&
                        command.capacity == (command.effectId == "EARTH_MANIFEST" ? 2 : 1) &&
                        command.cooldownTicks == (command.effectId == "EARTH_MANIFEST" ? 1 : 3) &&
                        command.durationClock == (command.effectId == "EARTH_MANIFEST" ? "OWN_TURN_END" : "TARGET_OWN_TURN_END"), "EARTH_CONFIGURATION");
                else if (!earth && command.kind == "DEPLOY")
                    Check(command.metricDistanceModes.SequenceEqual(new[] { 1, 4 }) && command.capacity == 2 &&
                          command.upkeepClock == "GLOBAL_TICK" && command.durationClock == "UNTIL_INVALID", "SCALE_CONFIGURATION");
            }
            else Check(!string.IsNullOrWhiteSpace(command.unavailableReasonKey), "MISSING_UNAVAILABLE_REASON");
        }

        private static void ValidateTuning(JindanDemoLoadoutTuning tuning)
        {
            Check(tuning != null && tuning.combatSwitchActionMode == "UNUSED_FULL_OWN_TURN_AP" &&
                  tuning.maxSwitchesPerOwnAction == 1 && tuning.safeStateSwitchMode == "FREE" &&
                  tuning.switchActionPointCostMode == "SNAPSHOT_MAX_AP" && tuning.switchManaCost == 0 &&
                  tuning.switchCtPenalty == 0 && tuning.switchCooldownTicks == 0 &&
                  tuning.activationGate == "NEXT_OWN_TURN_START" && !string.IsNullOrWhiteSpace(tuning.switchCostProfileId), "LOADOUT_TUNING");
        }

        private static void Unique<T>(T[] items, Func<T, string> key, int count, string kind) where T : class
        {
            Check(items != null && items.Length > 0 && (count < 0 || items.Length == count) && items.All(x => x != null), kind + "_COUNT");
            Check(items.All(x => !string.IsNullOrWhiteSpace(key(x))) && items.Select(key).Distinct(StringComparer.Ordinal).Count() == items.Length,
                kind + "_DUPLICATE");
        }

        private static void Check(bool condition, string reason)
        {
            if (!condition) throw new InvalidDataException("JD_DEMO_" + reason);
        }
    }
}
