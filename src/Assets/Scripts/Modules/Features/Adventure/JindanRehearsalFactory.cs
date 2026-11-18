using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Gameplay.Contracts;
using TianZhang.Spatial;

namespace TianZhang.Features.Adventure
{
    /// <summary>Projects validated demo identities into the existing combat session, never into GameRuntime.</summary>
    public static class JindanRehearsalFactory
    {
        public static AdventureSpawnSet Create(JindanDemoCatalogData catalog, int segmentIndex,
            string earthEffect, Func<string, string> text, bool controlFire = false) =>
            CreateActors(catalog, segmentIndex, earthEffect, text, controlFire, null);

        public static AdventureSpawnSet CreateForPlayer(JindanDemoCatalogData catalog, string earthEffect,
            Func<string, string> text, CharacterStateSnapshot player) =>
            CreateActors(catalog, 1, earthEffect, text, true, player ?? throw new ArgumentNullException(nameof(player)));

        private static AdventureSpawnSet CreateActors(JindanDemoCatalogData catalog, int segmentIndex,
            string earthEffect, Func<string, string> text, bool controlFire, CharacterStateSnapshot player)
        {
            if (catalog == null || !catalog.Validate(out string reason))
                throw new ArgumentException("jindan_catalog_invalid");
            if (segmentIndex < 0 || segmentIndex >= catalog.segments.Length) throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            if (player != null && (player.Identity.CharacterId != "player" || segmentIndex != 1 || !controlFire))
                throw new ArgumentException("jindan_player_challenge_invalid");
            var segment = catalog.segments[segmentIndex];
            if (controlFire && segmentIndex != 1) throw new ArgumentException("fire_control_requires_second_segment");
            var players = new List<CombatantSnapshot>();
            var enemies = new List<CombatantSnapshot>();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var placement in segment.actors)
            {
                var preset = catalog.GetPreset(placement.characterId);
                var source = preset.sourceCharacterDefinition;
                // A declared existing base template supplies unbalanced acceptance stats. No imagined growth history.
                var attributes = new CharacterAttributes(source.rootBone, source.physique, source.spirit, source.mind,
                    source.reaction, source.talent, source.fortune, preset.actionPointMaximumUnits,
                    preset.movementEfficiencyMilliUnits, true);
                var stats = attributes.Derive(preset.realmMultiplier, CharacterAttributeBonuses.Empty);
                var bindings = preset.seatInstanceIds.Select(catalog.GetSeat).SelectMany(seat =>
                    seat.candidateEffectIds.Select(effect => new KeyValuePair<string, string>(effect, seat.guardianAbilityInstanceId)))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(preset.flightAbilityInstanceId))
                    bindings.Add(CombatFlight.ProfileId, preset.flightAbilityInstanceId);
                var arts = preset.equippedArtProfileIds.Concat(preset.characterId == catalog.demolition.characterId
                    ? new[] { catalog.demolition.profileId } : Array.Empty<string>()).ToArray();
                int team = controlFire ? 1 - placement.team : placement.team;
                var actor = new CombatantSnapshot(preset.characterId, team == 0 ? CombatTeam.Player : CombatTeam.Enemy,
                    new HexCoord(placement.q, placement.r), attributes.Reaction, stats.MaxHealth, stats.MaxHealth,
                    stats.PhysicalAttack, stats.MagicAttack, stats.PhysicalDefense, stats.MagicDefense,
                    preset.actionPointMaximumUnits, preset.movementEfficiencyMilliUnits, preset.realmMultiplier,
                    basicAttackProfileId: preset.basicAttackProfileId, rawSpirit: attributes.Spirit, rawMind: attributes.Mind,
                    realmStage: "金丹", visibleRootElement: string.Join("/", preset.rootElements.Select(CombatElementFacts.ResolveElement)),
                    visibleRootGrade: preset.rootGrade == 2 ? "中品" : "上品", abilityInstanceIds: bindings,
                    facing: placement.facing,
                    equippedArtProfileIds: arts, availableArtProfileIds: arts)
                { GongFaId = preset.sourceGongFaId };
                actor.SetSpirit(preset.manaMaximum, preset.manaMaximum);
                if (player != null && preset.characterId == JindanDemoCatalogData.PlayerFirePresetId)
                {
                    var playerBindings = bindings.ToDictionary(pair => pair.Key,
                        pair => JindanDemoCatalogData.PlayerInstanceId(preset.characterId, pair.Value));
                    actor = AdventureUnitSpawner.CreatePlayer(player, new HexCoord(placement.q, placement.r),
                        string.IsNullOrEmpty(player.MainEquipmentBasicAttackProfileId)
                            ? player.UnarmedBasicAttackProfileId : player.MainEquipmentBasicAttackProfileId,
                        playerBindings, placement.facing);
                }
                (actor.Team == CombatTeam.Player ? players : enemies).Add(actor);
                names.Add(actor.Id, actor.Id == "player" ? player.Identity.DisplayName : text(preset.displayNameKey));
            }
            return new AdventureSpawnSet(players[0], enemies[0], null, players[0].BasicAttackProfileId,
                enemies[0].BasicAttackProfileId, player?.AbilityLoadout.EquippedSkills ?? Array.Empty<string>(), enemies.Skip(1).ToArray(),
                additionalPlayers: players.Skip(1).ToArray(), rehearsalNames: names);
        }

        public static CombatJindanLoadout CreateLoadout(JindanDemoCatalogData catalog, AdventureSpawnSet spawned, string earthEffect)
        {
            var actors = new HashSet<string>(spawned.Players.Concat(spawned.Enemies).Select(value => value.Id));
            bool playerTrial = actors.Contains("player");
            if (playerTrial) actors.Add(JindanDemoCatalogData.PlayerFirePresetId);
            string Owner(string id) => playerTrial && id == JindanDemoCatalogData.PlayerFirePresetId ? "player" : id;
            string Instance(string owner, string id) => Owner(owner) == "player"
                ? JindanDemoCatalogData.PlayerInstanceId(owner, id) : id;
            var seats = catalog.seats.Where(value => actors.Contains(value.characterId)).Select(value =>
                new CombatJindanSeatDefinition(Owner(value.characterId), Instance(value.characterId, value.seatInstanceId), value.positionId, value.positionType.ToString(),
                    Instance(value.characterId, value.guardianAbilityInstanceId), value.candidateEffectIds,
                    value.candidateEffectIds.Contains(earthEffect) ? earthEffect : value.defaultEffectId)).ToArray();
            var abilities = new List<CombatJindanAbilityDefinition>();
            foreach (string effect in seats.SelectMany(value => value.Candidates).Distinct())
            {
                var deploy = catalog.commands.First(value => value.effectId == effect && value.kind == "DEPLOY");
                var close = catalog.commands.FirstOrDefault(value => value.effectId == effect && value.kind == "TERMINATE");
                if (close == null) throw new ArgumentException("jindan_close_definition_missing:" + effect);
                var seat = catalog.seats.First(value => value.candidateEffectIds.Contains(effect));
                abilities.Add(new CombatJindanAbilityDefinition(effect, deploy.enabled, deploy.actionPointCostUnits,
                    deploy.manaCost, deploy.cooldownTicks, close.actionPointCostUnits, deploy.upkeepMana,
                    deploy.range, deploy.verticalReachRank, deploy.capacity, deploy.supportDurability,
                    deploy.remoteOriginManaCost, catalog.GetPreset(seat.characterId).normalVisionRange,
                    deploy.metricDistanceModes, deploy.unavailableReasonKey, deploy.durationCount,
                    effect == "WATER_TIDE" ? CreateTide(catalog.surfaceRules) : null,
                    effect == "SPACE_SEAL" ? new CombatSealDefinition(new HexCoord(catalog.sealRules.fromQ, catalog.sealRules.fromR),
                        new HexCoord(catalog.sealRules.toQ, catalog.sealRules.toR), catalog.sealRules.supportId,
                        catalog.sealRules.localRadius, catalog.sealRules.layerDurability, catalog.sealRules.brokenLayerTicks,
                        deploy.durationCount) : null,
                    effect == CombatFireDomainRules.Effect ? new CombatFireDomainDefinition(
                        new HexCoord(catalog.fireDomainRules.centerQ, catalog.fireDomainRules.centerR), catalog.fireDomainRules.supportId,
                        catalog.fireDomainRules.fuelBudget, catalog.fireDomainRules.tailFuel, catalog.fireDomainRules.tailCycles,
                        catalog.fireDomainRules.attackCapacity, catalog.fireDomainRules.damageMultiplier) : null,
                    CombatVisibility.IsEffect(effect) ? CreateVisibility(catalog.visibilityRules, effect, deploy.durationCount) : null));
            }
            return new CombatJindanLoadout(seats, abilities, catalog.loadoutTuning.maxSwitchesPerOwnAction);
        }

        private static CombatTideDefinition CreateTide(JindanDemoSurfaceRules rules) => new CombatTideDefinition(
            rules.tideRadius, rules.waterlineOffset, rules.forceRank, rules.distanceBudget,
            rules.chargeExtraActionPoints, rules.chargeExtraMana, rules.chargeTicks,
            rules.eyeExtraActionPoints, rules.eyeExtraMana, rules.attackCapacity,
            rules.waterMultiplier, rules.chargeMultiplier, rules.erosionDamage);

        private static CombatVisibilityDefinition CreateVisibility(JindanDemoVisibilityRules rule, string effect, int duration)
        {
            bool yang = effect == CombatVisibility.Yang;
            return new CombatVisibilityDefinition(new HexCoord(yang ? rule.yangQ : rule.yinQ, yang ? rule.yangR : rule.yinR),
                yang ? rule.yangSupportId : rule.yinSupportId, rule.supportRank, rule.carrying, duration,
                new CombatConflictSettings(rule.rulesId, rule.conflictReserve, rule.pulseMana, rule.pulseMilliseconds,
                    rule.windowOpenMilliseconds, rule.windowCloseMilliseconds, rule.gaugeStep, rule.gaugeWin,
                    rule.holdKey, rule.firstKey, rule.secondKey), rule.yangWindowTicks, rule.reductionPercent,
                rule.reductionCap, rule.yangWindowBudget);
        }

        public static CombatSurfaces CreateSurfaces(JindanDemoCatalogData catalog,
            EnvironmentProfileDefinition environment, int segmentIndex, bool playerFire = false)
        {
            if (catalog == null || !catalog.Validate(out _)) throw new ArgumentException("jindan_catalog_invalid");
            if (environment == null || environment.profileId != catalog.surfaceRules.environmentProfileId)
                throw new ArgumentException("jindan_surface_environment_mismatch");
            if (segmentIndex < 0 || segmentIndex >= catalog.segments.Length)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            string segment = catalog.segments[segmentIndex].segmentId;
            var definitions = environment.battlefieldCells.ToDictionary(value => new HexCoord(value.q, value.r), value =>
                new CombatSurfaceDefinition(new HexCoord(value.q, value.r), value.surfaceKind switch
                {
                    "mud_bank" => CombatSurfaceKind.Wet,
                    "shallow_water" => CombatSurfaceKind.ShallowWater,
                    "deep_water" => CombatSurfaceKind.DeepWater,
                    _ => CombatSurfaceKind.None,
                }));
            foreach (var cell in catalog.surfaceCells.Where(value => value.segmentId == segment))
            {
                var position = new HexCoord(cell.q, cell.r);
                if (!definitions.ContainsKey(position)) throw new ArgumentException("jindan_surface_cell_missing");
                if (!string.IsNullOrEmpty(cell.fuelObjectId) &&
                    !(environment.sceneObjects?.instances?.Any(value => value.instanceId == cell.fuelObjectId &&
                        value.cell == cell.q + ":" + cell.r && value.contentType == "low_plant") ?? false))
                    throw new ArgumentException("jindan_surface_fuel_object_missing");
                definitions[position] = new CombatSurfaceDefinition(position,
                    (CombatSurfaceKind)Enum.Parse(typeof(CombatSurfaceKind), cell.kind), cell.fuel, cell.sourceRef, cell.fuelObjectId);
            }
            var fires = catalog.surfaceFires.Where(value => value.segmentId == segment).Select(value =>
                new CombatFireDefinition(value.sourceRef, playerFire && value.actorId == JindanDemoCatalogData.PlayerFirePresetId ? "player" : value.actorId, value.periodTicks, value.durationTicks,
                    value.capacity, value.fuelBudget, value.attackCapacity, value.damageMultiplier));
            return new CombatSurfaces(definitions.Values, fires,
                catalog.surfaceRules.wetMovementBurden, catalog.surfaceRules.deepMovementBurden);
        }

        public static CombatFlight CreateFlight(JindanDemoCatalogData catalog, AdventureSpawnSet spawned)
        {
            var actors = spawned.Players.Concat(spawned.Enemies).Select(value => value.Id).ToHashSet();
            var sources = catalog.presets.Where(value => actors.Contains(value.characterId) &&
                !string.IsNullOrEmpty(value.flightAbilityInstanceId)).Select(value =>
                    new KeyValuePair<string, string>(value.characterId, value.flightAbilityInstanceId));
            var commands = catalog.commands.Where(value => value.effectId == "FLIGHT").ToArray();
            if (commands.Length != 5 || commands.Any(value => !value.enabled)) throw new ArgumentException("flight_commands_missing");
            var takeoff = commands.Single(value => value.kind == "FLIGHT_TAKEOFF");
            return new CombatFlight(sources, commands.Select(value => new CombatFlightCost(value.kind switch
            {
                "FLIGHT_TAKEOFF" => CombatFlightAction.Takeoff, "FLIGHT_ASCEND" => CombatFlightAction.Ascend,
                "FLIGHT_DESCEND" => CombatFlightAction.Descend, "FLIGHT_LAND" => CombatFlightAction.Land,
                "FLIGHT_RECOVER" => CombatFlightAction.Recover, _ => throw new ArgumentException("flight_command_unknown"),
            }, value.actionPointCostUnits, value.manaCost, value.cooldownTicks)), takeoff.upkeepMana, takeoff.durationCount);
        }

        public static IReadOnlyList<CombatAttackProfile> ProjectAttacks(JindanDemoCatalogData catalog,
            IReadOnlyList<CombatAttackProfile> originals)
        {
            if (!catalog.Validate(out string reason)) throw new ArgumentException(reason);
            var result = originals.ToList();
            result.Add(originals.Single(value=>value.Id==catalog.demolition.sourceProfileId).WithEnvironmentVariant(
                catalog.demolition.profileId,catalog.demolition.environmentTargetTypes,catalog.demolition.environmentDamage));
            foreach (var preset in catalog.presets.GroupBy(value => value.basicAttackProfileId).Select(group => group.First()))
            {
                var source = originals.Single(value => value.Id == preset.basicAttackProfileId);
                result[result.IndexOf(source)] = source.WithSpatialPolicy(
                    new SpatialAttackPolicy((SpatialMovementMode)preset.basicAttackSourceModeMask,
                        (SpatialMovementMode)preset.basicAttackTargetModeMask, source.SpatialPolicy.VerticalReachRank, source.SpatialPolicy.IsMelee));
            }
            return result.AsReadOnly();
        }

        public static IReadOnlyList<CombatUnitPresentationDescriptor> Descriptors(AdventureSpawnSet spawned, string humanProfile)
            => spawned.Players.Concat(spawned.Enemies).Select(actor => new CombatUnitPresentationDescriptor(actor.Id, humanProfile,
                actor.Team == CombatTeam.Player ? CombatUnitDisplayFaction.Player : CombatUnitDisplayFaction.Enemy,
                new CombatUnitPresentationHex(actor.Position.Q, actor.Position.R), actor.Facing)).ToArray();
    }
}
