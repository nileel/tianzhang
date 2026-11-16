using System;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Spatial;

namespace TianZhang.Art.Jindan.Space.Samples
{
    // Resource-sample adapter only. Every transition goes through the production command service.
    public sealed class SpaceSampleRules
    {
        public const string Player = "demo_20261024_space_yang", Enemy = "demo_20261024_seal_yin";
        public static readonly HexCoord ScaleFrom = new(5,14), ScaleTo = new(6,14), GateFrom = new(8,12), GateTo = new(9,12);
        public readonly CombatSession Session;
        public readonly AdventureSpawnSet Spawned;
        public readonly CombatCommandService Commands = new();
        public CombatJindanSeatState ScaleSeat => Session.Jindan.Seats.Single(s => s.EffectId == "SPACE_SCALE");
        public CombatJindanSeatState SealSeat => Session.Jindan.Seats.Single(s => s.EffectId == "SPACE_SEAL");
        public SpaceSampleRules(SpaceAssetSample sample)
        {
            Spawned = JindanRehearsalFactory.Create(sample.catalog, 2, "EARTH_GRAVITY", key => key);
            var entry = new CombatEntryAdapter(new[] {
                new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) }, sample.rules);
            if (!entry.TryCreateSession(Spawned, sample.attacks, sample.environment, out Session, out string reason, sample.catalog))
                throw new InvalidOperationException(reason);
            Session.ConfigureJindan(JindanRehearsalFactory.CreateLoadout(sample.catalog, Spawned, "EARTH_GRAVITY"));
            Commands.AdvanceUntilAction(Session); ToTurn(Player);
        }
        public CombatantSnapshot Actor(string id) { Session.Combatants.TryGet(id, out var actor); return actor; }
        public void ToTurn(string id)
        {
            for (int turn = 0; turn < 12 && Session.CurrentActorId != id; turn++)
            {
                Execute(new CombatCommand(CombatCommandKind.EndTurn, Session.CurrentActorId)); Commands.AdvanceUntilAction(Session);
            }
            if (Session.CurrentActorId != id) throw new InvalidOperationException("space sample turn not reached");
        }
        public void Execute(CombatCommand command)
        {
            var quote = Commands.Preview(Session, command);
            if (!quote.IsLegal) throw new InvalidOperationException(command.Kind+" preview: "+quote.RejectionReason);
            var result=Commands.Execute(Session,command);
            if (!result.Succeeded) throw new InvalidOperationException(command.Kind+": "+result.RejectionReason);
        }
        public void Scale(int metric) => Execute(new CombatCommand(CombatCommandKind.JindanAbility,Player,profileId:"SPACE_SCALE",
            jindan:new CombatJindanRequest(ScaleSeat.Definition.SeatId,new[]{ScaleFrom,ScaleTo},metric)));
        public void CloseScale() => Execute(new CombatCommand(CombatCommandKind.CloseJindan,Player,profileId:"SPACE_SCALE",
            jindan:new CombatJindanRequest(ScaleSeat.Definition.SeatId)));
        public void Seal() => Execute(new CombatCommand(CombatCommandKind.JindanAbility,Enemy,profileId:"SPACE_SEAL",
            jindan:new CombatJindanRequest(SealSeat.Definition.SeatId)));
        public void Strike(HexCoord cell) => Execute(new CombatCommand(CombatCommandKind.Art,Player,profileId:"spell_guanshiji",targetCell:cell));
        public int Metric => Session.SceneObjects.Board.InspectEdge(ScaleFrom,ScaleTo,SpatialQueryKind.Attack).MetricDistanceUnits;
        public CombatCommand Move(string actor,HexCoord cell) => new(CombatCommandKind.Move,actor,destination:cell);
    }
}
