using System;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Spatial;

namespace TianZhang.Art.Jindan.Fire.Samples
{
    // A declared resource fixture on the production board, driven only through production commands after entry.
    public sealed class FireSampleRules
    {
        public const string FireActor = "demo_20261024_fire", EarthActor = "demo_20261024_earth", WaterActor = "demo_20261024_water";
        public readonly HexCoord[] FuelCells = { new HexCoord(7, 13), new HexCoord(8, 12), new HexCoord(8, 13) };
        public readonly HexCoord WaterSource = new HexCoord(9, 12);
        public readonly CombatSession Session;
        public readonly AdventureSpawnSet Spawned;
        public readonly CombatCommandService Commands = new CombatCommandService();
        public CombatJindanSeatState Seat => Session.Jindan.Seats.Single(s => s.Definition.ActorId == FireActor);
        public CombatFireDomainState Fire => Seat.FireDomain;

        public FireSampleRules(FireAssetSample sample)
        {
            Spawned = JindanRehearsalFactory.Create(sample.catalog, 1, "EARTH_GRAVITY", key => key);
            // Initial placement belongs to this sample fixture, not a runtime move or modified official preset.
            foreach (var actor in Spawned.Players.Concat(Spawned.Enemies))
                actor.SetPosition(actor.Id == EarthActor ? new HexCoord(7, 11) : actor.Id == WaterActor ? new HexCoord(8, 11) : new HexCoord(7, 14));
            var entry = new CombatEntryAdapter(new[] {
                new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) }, sample.rules);
            if (!entry.TryCreateSession(Spawned, sample.attacks, sample.environment, out Session, out string reason, sample.catalog))
                throw new InvalidOperationException(reason);
            Session.ConfigureJindan(JindanRehearsalFactory.CreateLoadout(sample.catalog, Spawned, "EARTH_GRAVITY"));
            Session.ConfigureFlight(JindanRehearsalFactory.CreateFlight(sample.catalog, Spawned));
            if (!sample.environment.TryCreateDefinition(out var environment, out reason)) throw new InvalidOperationException(reason);
            // Three dry fuel patches expose the capacity boundary without changing any fire rule or source budget.
            var original = JindanRehearsalFactory.CreateSurfaces(sample.catalog, environment, 1);
            var definitions = original.Cells.Select(cell => new CombatSurfaceDefinition(cell.Cell,
                FuelCells.Contains(cell.Cell) ? CombatSurfaceKind.None : cell.Kind == CombatSurfaceKind.Burning ? CombatSurfaceKind.None : cell.Kind,
                FuelCells.Contains(cell.Cell) ? 4 : 0));
            Session.ConfigureSurfaces(new CombatSurfaces(definitions, wetMovementBurden: sample.catalog.surfaceRules.wetMovementBurden,
                deepMovementBurden: sample.catalog.surfaceRules.deepMovementBurden));
            Commands.AdvanceUntilAction(Session); ToActor(FireActor);
        }
        public CombatCommand Deploy() => new CombatCommand(CombatCommandKind.JindanAbility, FireActor,
            profileId: CombatFireDomainRules.Effect, jindan: new CombatJindanRequest(Seat.Definition.SeatId));
        public CombatCommand Strike() => new CombatCommand(CombatCommandKind.Art, EarthActor,
            profileId: "demo_20261026_polei", targetCell: Fire.Support.Cell);
        public CombatCommand Tide() => new CombatCommand(CombatCommandKind.JindanAbility, WaterActor,
            profileId: "WATER_TIDE", jindan: new CombatJindanRequest(Session.Jindan.Seats.Single(s => s.Definition.ActorId == WaterActor).Definition.SeatId,
                tideAction: CombatTideAction.Rise, tideSource: WaterSource, createTideEye: true));
        public void Execute(CombatCommand command)
        {
            var quote = Commands.Preview(Session, command);
            if (!quote.IsLegal) throw new InvalidOperationException(command.Kind + " preview: " + quote.RejectionReason);
            var result = Commands.Execute(Session, command);
            if (!result.Succeeded) throw new InvalidOperationException(command.Kind + ": " + result.RejectionReason);
        }
        public void EndCurrent()
        {
            Execute(new CombatCommand(CombatCommandKind.EndTurn, Session.CurrentActorId));
            Commands.AdvanceUntilAction(Session);
        }
        public void NextFireTurn() { EndCurrent(); ToActor(FireActor); }
        public void ToActor(string id)
        {
            for (int i = 0; i < 16 && Session.CurrentActorId != id; i++) EndCurrent();
            if (Session.CurrentActorId != id) throw new InvalidOperationException("sample_actor_unreachable:" + id);
        }
    }
}
