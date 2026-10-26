using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Entity;
using TianZhang.Gameplay.Contracts;
using TianZhang.Spatial;

namespace TianZhang.Features.Adventure
{
    /// <summary>Projects validated demo identities into the existing combat session, never into GameRuntime.</summary>
    public static class JindanRehearsalFactory
    {
        public static AdventureSpawnSet Create(JindanDemoCatalogData catalog, int segmentIndex,
            string earthEffect, Func<string, string> text)
        {
            if (catalog == null || !catalog.Validate(out string reason))
                throw new ArgumentException("jindan_catalog_invalid");
            if (segmentIndex < 0 || segmentIndex >= catalog.segments.Length) throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            var segment = catalog.segments[segmentIndex];
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
                var actor = new CombatantSnapshot(preset.characterId, placement.team == 0 ? CombatTeam.Player : CombatTeam.Enemy,
                    new HexCoord(placement.q, placement.r), attributes.Reaction, stats.MaxHealth, stats.MaxHealth,
                    stats.PhysicalAttack, stats.MagicAttack, stats.PhysicalDefense, stats.MagicDefense,
                    preset.actionPointMaximumUnits, preset.movementEfficiencyMilliUnits, preset.realmMultiplier,
                    basicAttackProfileId: preset.basicAttackProfileId, rawSpirit: attributes.Spirit, rawMind: attributes.Mind,
                    realmStage: "金丹", visibleRootElement: string.Join("/", preset.rootElements),
                    visibleRootGrade: preset.rootGrade == 2 ? "中品" : "上品", abilityInstanceIds: bindings,
                    facing: placement.facing)
                { GongFaId = preset.sourceGongFaId };
                actor.SetSpirit(preset.manaMaximum, preset.manaMaximum);
                (actor.Team == CombatTeam.Player ? players : enemies).Add(actor);
                names.Add(actor.Id, text(preset.displayNameKey));
            }
            return new AdventureSpawnSet(players[0], enemies[0], null, players[0].BasicAttackProfileId,
                enemies[0].BasicAttackProfileId, Array.Empty<string>(), enemies.Skip(1).ToArray(),
                additionalPlayers: players.Skip(1).ToArray(), rehearsalNames: names);
        }

        public static CombatJindanLoadout CreateLoadout(JindanDemoCatalogData catalog, AdventureSpawnSet spawned, string earthEffect)
        {
            var actors = new HashSet<string>(spawned.Players.Concat(spawned.Enemies).Select(value => value.Id));
            var seats = catalog.seats.Where(value => actors.Contains(value.characterId)).Select(value =>
                new CombatJindanSeatDefinition(value.characterId, value.seatInstanceId, value.positionId, value.positionType.ToString(),
                    value.guardianAbilityInstanceId, value.candidateEffectIds,
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
                    deploy.metricDistanceModes, deploy.unavailableReasonKey));
            }
            return new CombatJindanLoadout(seats, abilities, catalog.loadoutTuning.maxSwitchesPerOwnAction);
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
