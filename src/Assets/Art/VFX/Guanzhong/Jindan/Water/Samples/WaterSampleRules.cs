using System;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Spatial;

namespace TianZhang.Art.Jindan.Water.Samples
{
    // Sample-only caller of existing production commands. No duplicated tide or surface rules.
    public sealed class WaterSampleRules
    {
        public const string WaterActor = "demo_20261024_water";
        public readonly HexCoord Source = new HexCoord(5, 5);
        public readonly HexCoord PlantCell = new HexCoord(4, 6);
        public readonly CombatSession Session;
        public readonly AdventureSpawnSet Spawned;
        public readonly CombatCommandService Commands = new CombatCommandService();
        private readonly string seat;

        public WaterSampleRules(WaterAssetSample sample)
        {
            Spawned = JindanRehearsalFactory.Create(sample.catalog, 1, "EARTH_GRAVITY", key => key);
            var entry = new CombatEntryAdapter(new[] {
                new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) }, sample.rules);
            if (!entry.TryCreateSession(Spawned, sample.attacks, sample.environment, out Session, out string reason, sample.catalog))
                throw new InvalidOperationException(reason);
            Session.ConfigureJindan(JindanRehearsalFactory.CreateLoadout(sample.catalog, Spawned, "EARTH_GRAVITY"));
            Session.ConfigureFlight(JindanRehearsalFactory.CreateFlight(sample.catalog, Spawned));
            if (!sample.environment.TryCreateDefinition(out var environment, out reason)) throw new InvalidOperationException(reason);
            Session.ConfigureSurfaces(JindanRehearsalFactory.CreateSurfaces(sample.catalog, environment, 1));
            seat = Session.Jindan.Seats.Single(s => s.Definition.ActorId == WaterActor).Definition.SeatId;
            Commands.AdvanceUntilAction(Session);
            ToWaterTurn();
        }
        public CombatCommand Tide(CombatTideAction action) => new CombatCommand(CombatCommandKind.JindanAbility, WaterActor,
            profileId: "WATER_TIDE", jindan: new CombatJindanRequest(seat, tideAction: action, tideSource: Source));
        public CombatCommand Close() => new CombatCommand(CombatCommandKind.CloseJindan, WaterActor,
            profileId: "WATER_TIDE", jindan: new CombatJindanRequest(seat));
        public void Execute(CombatCommand command)
        {
            var result = Commands.Execute(Session, command);
            if (!result.Succeeded) throw new InvalidOperationException(command.Kind + ": " + result.RejectionReason);
        }
        public void NextTideTurn()
        {
            Execute(new CombatCommand(CombatCommandKind.EndTurn, Session.CurrentActorId));
            Commands.AdvanceUntilAction(Session); ToWaterTurn();
        }
        private void ToWaterTurn()
        {
            for (int turn = 0; turn < 12 && Session.CurrentActorId != WaterActor; turn++)
            {
                if (string.IsNullOrEmpty(Session.CurrentActorId)) throw new InvalidOperationException("sample combat ended");
                Execute(new CombatCommand(CombatCommandKind.EndTurn, Session.CurrentActorId)); Commands.AdvanceUntilAction(Session);
            }
            if (Session.CurrentActorId != WaterActor) throw new InvalidOperationException("water turn not reached");
        }
    }
}
