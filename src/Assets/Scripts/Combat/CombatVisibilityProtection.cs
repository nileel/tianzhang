using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    /// <summary>Prepared with the root attack; guardian first-hit receipts publish only with its combat commit.</summary>
    internal sealed class CombatVisibilityProtection
    {
        private readonly CombatSession session;
        private readonly Dictionary<CombatVisibilityLedger, Window> windows = new Dictionary<CombatVisibilityLedger, Window>();
        private sealed class Window
        {
            internal int Index, Used;
            internal HashSet<string> Members;
        }
        internal CombatVisibilityProtection(CombatSession session) => this.session = session;
        internal int Reduce(CombatantSnapshot source, CombatantSnapshot target, int damage, float existingPercent)
        {
            if (source.Id == target.Id || session.Jindan == null) return damage;
            int remaining = damage;
            float availablePercent = Math.Max(0f, 30f - existingPercent);
            foreach (var seat in session.Jindan.Seats.Where(value => value.ActiveVisibility != null))
            {
                session.Combatants.TryGet(seat.Definition.ActorId, out var owner);
                if (owner.Team != target.Team) continue;
                var definition = seat.ActiveVisibility.Definition;
                Window window = null;
                if (seat.EffectId == CombatVisibility.Yang)
                {
                    var ledger = seat.VisibilityLedger;
                    int index = session.TurnScheduler.CurrentTick / definition.ProtectionWindowTicks;
                    if (!windows.TryGetValue(ledger, out window))
                    {
                        window = new Window { Index = index, Used = ledger.ProtectionWindow == index ? ledger.ProtectionUsed : 0,
                            Members = ledger.ProtectionWindow == index ? new HashSet<string>(ledger.ProtectedMembers) : new HashSet<string>() };
                        windows.Add(ledger, window);
                    }
                    if (!window.Members.Add(target.Id)) continue;
                }
                else
                {
                    bool distant = source.OccupiedCells.All(from => target.OccupiedCells.All(to =>
                        from.TopologicalDistanceTo(to) > 1 && !session.SceneObjects.Board.QueryMetricDistance(from, to, SpatialQueryKind.Attack, 1).IsReachable));
                    if (!distant) continue;
                }
                float percent = Math.Min(definition.ReductionPercent, availablePercent);
                int reduction = Math.Min((int)Math.Floor(remaining * (double)percent / 100), definition.ReductionCap);
                if (window != null) reduction = Math.Min(reduction, Math.Max(0, definition.WindowBudget - window.Used));
                remaining -= reduction; availablePercent -= percent;
                if (window != null) window.Used += reduction;
            }
            return remaining;
        }
        internal void Commit()
        {
            foreach (var pair in windows)
            {
                pair.Key.ProtectionWindow = pair.Value.Index; pair.Key.ProtectionUsed = pair.Value.Used;
                pair.Key.ProtectedMembers.Clear(); pair.Key.ProtectedMembers.UnionWith(pair.Value.Members);
            }
        }
        internal static float ExistingPercent(CombatantSnapshot target) =>
            (target.Statuses.FirstOrDefault(value => value.Kind == CombatStatusKind.WaterReduction)?.DeltaNumerator ?? 0) / 10000f +
            (target.GongFaId == "抱元守一经" && target.ShouyiStacks > 0 ? 20f : 0f);
    }
}
