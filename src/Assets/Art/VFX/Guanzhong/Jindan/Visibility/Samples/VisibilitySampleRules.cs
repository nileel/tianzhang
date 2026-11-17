using System;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Spatial;

namespace TianZhang.Art.Jindan.Visibility.Samples
{
    public sealed class VisibilitySampleRules
    {
        public const string Yang = "demo_20261024_space_yang", Yin = "demo_20261024_seal_yin";
        public readonly CombatSession Session;
        public readonly AdventureSpawnSet Spawned;
        public readonly CombatCommandService Commands = new();
        public CombatJindanSeatState Seat(string effect) => Session.Jindan.Seats.Single(s => s.EffectId == effect);
        public CombatantSnapshot Actor(string id) { Session.Combatants.TryGet(id, out var actor); return actor; }

        public VisibilitySampleRules(VisibilityAssetSample sample)
        {
            Spawned = JindanRehearsalFactory.Create(sample.catalog, 2, "EARTH_GRAVITY", key => key);
            var entry = new CombatEntryAdapter(new[] {
                new CombatOrdinaryActionDefinition(CombatCommandKind.Guard,50000,50000,0,0),
                new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell,50000,50000,0,60) }, sample.rules);
            if (!entry.TryCreateSession(Spawned, sample.attacks, sample.environment, out Session, out string reason, sample.catalog))
                throw new InvalidOperationException(reason);
            Session.ConfigureJindan(JindanRehearsalFactory.CreateLoadout(sample.catalog, Spawned, "EARTH_GRAVITY"));
            Commands.AdvanceUntilAction(Session);
        }
        public void ToTurn(string id)
        {
            for (int i = 0; i < 12 && Session.CurrentActorId != id; i++)
            {
                Execute(new CombatCommand(CombatCommandKind.EndTurn, Session.CurrentActorId));
                Commands.AdvanceUntilAction(Session);
            }
            if (Session.CurrentActorId != id) throw new InvalidOperationException("visibility_sample_turn_not_reached");
        }
        public void Execute(CombatCommand command)
        {
            var quote = Commands.Preview(Session, command);
            if (!quote.IsLegal) throw new InvalidOperationException(command.Kind + " preview:" + quote.RejectionReason);
            var result = Commands.Execute(Session, command);
            if (!result.Succeeded) throw new InvalidOperationException(command.Kind + ":" + result.RejectionReason);
        }
        public void Deploy(string effect)
        {
            var seat = Seat(effect); ToTurn(seat.Definition.ActorId);
            Execute(new CombatCommand(CombatCommandKind.JindanAbility, seat.Definition.ActorId, profileId:effect,
                jindan:new CombatJindanRequest(seat.Definition.SeatId)));
        }
        public void Close(string effect)
        {
            var seat = Seat(effect); ToTurn(seat.Definition.ActorId);
            Execute(new CombatCommand(CombatCommandKind.CloseJindan, seat.Definition.ActorId, profileId:effect,
                jindan:new CombatJindanRequest(seat.Definition.SeatId)));
        }
        public void Resolve(string result)
        {
            var visibility = Session.Visibility;
            long id = visibility.Current.EventId;
            if (result == "neutral") { visibility.Skip(id, false); return; }
            visibility.Input(id, 0, true, false, 0);
            if (result == "yin") { visibility.Input(id, 100, false, true, 0); return; }
            visibility.Input(id, 250, false, false, 1);
            visibility.Input(id, 1000, false, false, 0);
            visibility.Input(id, 1250, false, false, 2);
        }
        public bool Visible => Session.Visibility.IsVisible(Yang, Yin);
        public CombatCommand Move(HexCoord cell) => new(CombatCommandKind.Move, Yang, destination:cell);
    }
}
