using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TianZhang.Features.Adventure
{
    [Serializable]
    public sealed class FireDomainCell
    {
        public Vector2Int coordinate;
        public int fuel = 12, normal, ember;
        public bool water, isolated, reached, tailPaid;
        public bool Registered => normal > 0 || ember > 0;
    }

    [Serializable]
    public sealed class FireDomainUnit
    {
        public string id, faction;
        public Vector2Int coordinate;
        public int hp = 30;
        public int soulDamage; // These fixture carriers have no soul channel.
    }

    [Serializable]
    public sealed class FireDomainConfig
    {
        public string fixtureId = "JD_COMBO_FIRE_DOMAIN_01_SCENE_FIXTURE";
        public int version = 1;
        public string localRecipe = "FIXTURE_ONLY_FIRE_DOMAIN", sourceSeat = "燃心灯/SOURCE";
        public string domainSeat = "不熄炉/DOMAIN", sourceEffect = "FIRE_MANIFEST", domainEffect = "FIRE_ESCALATE";
        public string medium = "CONTIGUOUS_FUEL", carrier = "BURNING_SURFACE", prototype = "FINITE_FIRE";
        public string permission = "INITIAL_FIRE_AND_FIXED_SPREAD", breakPolicy = "END";
        public int radius = 2, distance = 2, maxReached = 7, nodesPerCycle = 2;
        public int actions = 6, spirit = 60, propagation = 8, surfaceCapacity = 7, environmentCapacity = 1;
        public int surfaceReserved, environmentReserved;
        public bool domainSeatReserved, sourceSeatReserved, naturalSupport = true;
        public int manifestActions = 1, manifestSpirit = 6, supportActions = 1, supportSpirit = 2;
        public int openActions = 1, openSpirit = 4, maintenance = 2, expansion = 2, tail = 1;
        public int fuelPerIgnition = 4, normalCycles = 4, emberCycles = 2;
        public int enterDamage = 4, stayDamage = 2, emberDamage = 1, centerHp = 10;
        public Vector2Int landing = Vector2Int.zero, center = new Vector2Int(0, 1);
        public Vector2Int[] initial = { new Vector2Int(-1, 0), Vector2Int.zero, new Vector2Int(1, 0) };
        public FireDomainCell[] cells = DefaultCells();
        public FireDomainUnit[] units =
        {
            new FireDomainUnit { id = "Friend", faction = "友", coordinate = new Vector2Int(0, 1) },
            new FireDomainUnit { id = "Enemy", faction = "敌", coordinate = new Vector2Int(1, 1) },
            new FireDomainUnit { id = "Neutral", faction = "中立", coordinate = new Vector2Int(-2, 2) }
        };

        private static FireDomainCell[] DefaultCells()
        {
            var cells = new List<FireDomainCell>();
            for (int r = -2; r <= 2; r++)
                for (int q = -2; q <= 2; q++)
                    if (JindanFireDomainScenarioState.Distance(new Vector2Int(q, r)) <= 2)
                        cells.Add(new FireDomainCell { coordinate = new Vector2Int(q, r) });
            return cells.ToArray();
        }
    }

    [Serializable]
    public sealed class FireDomainSnapshot
    {
        public int revision, cycle, actions, spirit, propagation, centerHp;
        public int surfaceCapacity, environmentCapacity, surfaceReserved, environmentReserved;
        public int manifestPaid, supportPaid, openPaid, maintenancePaid, expansionPaid, tailPaid, fuelPaid;
        public string sourceRef = "", comboInstanceId = "", sourceMapping = "", domainMapping = "";
        public bool ended, mainClosed;
        public FireDomainCell[] cells;
        public FireDomainUnit[] units;
        public int SurfaceUsed => cells.Count(c => c.Registered);
        public int EnvironmentUsed => string.IsNullOrEmpty(comboInstanceId) ? 0 : 1;
        public int Reached => cells.Count(c => c.reached);
        public int ActiveComboCount => EnvironmentUsed;
        public int SurfaceAvailable => surfaceCapacity - surfaceReserved - SurfaceUsed;
        public int EnvironmentAvailable => environmentCapacity - environmentReserved - EnvironmentUsed;
    }

    /// <summary>Immutable preview of one operation, valid only for its owner and revision.</summary>
    public sealed class FireDomainPlan
    {
        internal readonly JindanFireDomainScenarioState Owner;
        internal readonly Vector2Int[] Targets;
        public readonly int Revision, Actions, Spirit, Maintenance, Expansion, Tail;
        public readonly string Operation, Failure;
        public IReadOnlyList<Vector2Int> Nodes => Array.AsReadOnly(Targets);
        internal FireDomainPlan(JindanFireDomainScenarioState owner, string op, int revision,
            string failure, Vector2Int[] nodes, int actions = 0, int spirit = 0,
            int maintenance = 0, int expansion = 0, int tail = 0)
        {
            Owner = owner; Operation = op; Revision = revision; Failure = failure;
            Targets = nodes; Actions = actions; Spirit = spirit;
            Maintenance = maintenance; Expansion = expansion; Tail = tail;
        }
    }

    /// <summary>Only rule owner of the isolated fixture. No production state or services.</summary>
    public sealed class JindanFireDomainScenarioState
    {
        private readonly FireDomainConfig config;
        private readonly FireDomainSnapshot state;
        private static readonly Vector2Int[] Neighbors =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1),
            new Vector2Int(0, -1), new Vector2Int(1, -1), new Vector2Int(-1, 1)
        };
        public string LastFailure { get; private set; } = "";
        public JindanFireDomainScenarioState(FireDomainConfig input)
        {
            config = Clone(input);
            state = new FireDomainSnapshot
            {
                actions = config.actions, spirit = config.spirit,
                centerHp = config.centerHp, cells = Clone(config).cells, units = Clone(config).units,
                surfaceCapacity = config.surfaceCapacity, environmentCapacity = config.environmentCapacity,
                surfaceReserved = config.surfaceReserved, environmentReserved = config.environmentReserved
            };
        }
        public static T Clone<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        public FireDomainSnapshot Capture() => Clone(state);
        public static int Distance(Vector2Int cell) => Math.Max(Math.Max(Math.Abs(cell.x), Math.Abs(cell.y)), Math.Abs(cell.x + cell.y));
        private FireDomainCell Cell(Vector2Int p) => state.cells.FirstOrDefault(c => c.coordinate == p);
        private bool Legal(FireDomainCell c) => c != null && !c.water && !c.isolated && c.fuel > 0 && Distance(c.coordinate - config.landing) <= config.radius;
        private bool CanIgnite(FireDomainCell c) => Legal(c) && !c.Registered && c.fuel >= config.fuelPerIgnition;
        private bool Reject(string reason) { LastFailure = reason; return false; }
        private bool Changed() { state.revision++; LastFailure = ""; return true; }

        private bool ValidProfile()
        {
            return config.fixtureId == "JD_COMBO_FIRE_DOMAIN_01_SCENE_FIXTURE" && config.version == 1
                && config.localRecipe == "FIXTURE_ONLY_FIRE_DOMAIN" && config.sourceSeat == "燃心灯/SOURCE"
                && config.domainSeat == "不熄炉/DOMAIN" && config.sourceEffect == "FIRE_MANIFEST"
                && config.domainEffect == "FIRE_ESCALATE" && config.medium == "CONTIGUOUS_FUEL"
                && config.carrier == "BURNING_SURFACE" && config.prototype == "FINITE_FIRE"
                && config.permission == "INITIAL_FIRE_AND_FIXED_SPREAD" && config.breakPolicy == "END"
                && config.normalCycles == config.fuelPerIgnition && config.normalCycles > 0
                && config.emberCycles > 0 && config.expansion > 0 && config.tail > 0
                && config.maintenance >= 0 && config.manifestSpirit >= 0 && config.openSpirit >= 0;
        }

        public FireDomainPlan Preview(string operation)
        {
            string failure = "";
            int actions = 0, spirit = 0, maintenance = 0, expansion = 0, tail = 0;
            var nodes = new List<Vector2Int>();
            if (state.ended) failure = "battle_ended";
            else if (!ValidProfile()) failure = "invalid_profile";
            else if (operation == "manifest")
            {
                actions = config.manifestActions + (config.naturalSupport ? 0 : config.supportActions);
                spirit = config.manifestSpirit + (config.naturalSupport ? 0 : config.supportSpirit);
                var connected = new HashSet<Vector2Int>();
                if (config.initial.Length > 0) connected.Add(config.initial[0]);
                for (int i = 0; i < config.initial.Length; i++)
                    foreach (var p in config.initial)
                        if (connected.Any(c => Distance(c - p) == 1)) connected.Add(p);
                if (state.mainClosed || state.sourceMapping != "" || config.sourceSeatReserved) failure = "source_occupied";
                else if (config.initial.Length != 3 || connected.Count != 3 || !config.initial.Contains(config.landing)
                    || Distance(config.landing) > config.distance) failure = "invalid_initial_geometry";
                else if (config.initial.Any(p => !CanIgnite(Cell(p)))) failure = "initial_carrier_or_fuel";
                else if (state.SurfaceUsed + config.surfaceReserved + 3 > config.surfaceCapacity) failure = "surface_capacity";
                else nodes.AddRange(config.initial);
            }
            else if (operation == "open")
            {
                var fires = state.cells.Where(c => c.normal > 0 && Legal(c)).ToArray();
                actions = config.openActions; tail = fires.Length * config.tail; spirit = config.openSpirit + tail;
                if (state.mainClosed || state.centerHp <= 0 || Cell(config.center) == null) failure = "invalid_center";
                else if (state.sourceRef == "" || state.sourceMapping != state.sourceRef || fires.Length == 0) failure = "missing_source";
                else if (state.domainMapping != "" || config.domainSeatReserved) failure = "domain_occupied";
                else if (config.environmentReserved + 1 > config.environmentCapacity) failure = "environment_capacity";
                else nodes.AddRange(fires.Select(c => c.coordinate));
            }
            else if (operation == "cycle")
            {
                var sources = state.cells.Where(c => c.normal > 0 && Legal(c)).ToArray();
                if (state.EnvironmentUsed == 1 && state.centerHp > 0 && sources.Length > 0)
                {
                    if (state.spirit >= config.maintenance)
                    {
                        maintenance = config.maintenance; spirit = maintenance;
                        var candidates = sources.SelectMany(c => Neighbors.Select(n => c.coordinate + n))
                            .Distinct().Select(Cell).Where(CanIgnite).OrderBy(c => c.reached ? 0 : 1)
                            .ThenBy(c => c.coordinate.y).ThenBy(c => c.coordinate.x);
                        int newReached = state.Reached;
                        foreach (var c in candidates)
                        {
                            if (!c.reached && newReached >= config.maxReached) continue;
                            if (nodes.Count >= config.nodesPerCycle || nodes.Count >= state.propagation
                                || state.SurfaceUsed + config.surfaceReserved + nodes.Count >= config.surfaceCapacity
                                || spirit + config.expansion + config.tail > state.spirit) break;
                            nodes.Add(c.coordinate); if (!c.reached) newReached++;
                            expansion += config.expansion; tail += config.tail; spirit += config.expansion + config.tail;
                        }
                    }
                }
            }
            else failure = "unknown_operation";
            if (failure == "" && state.actions < actions) failure = "actions_insufficient";
            if (failure == "" && state.spirit < spirit) failure = "spirit_insufficient";
            return new FireDomainPlan(this, operation, state.revision, failure, nodes.ToArray(), actions, spirit, maintenance, expansion, tail);
        }

        public bool Commit(FireDomainPlan plan)
        {
            if (plan == null || plan.Owner != this || plan.Revision != state.revision) return Reject("stale_preview");
            if (plan.Failure != "") return Reject(plan.Failure);
            // All mutation is below the validated plan. Countermeasures and movement invalidate its revision.
            var oldFires = state.cells.Where(c => c.Registered).ToArray();
            state.actions -= plan.Actions; state.spirit -= plan.Spirit;
            if (plan.Operation == "manifest")
            {
                state.sourceRef = "fixture_source_1"; state.sourceMapping = state.sourceRef;
                state.propagation = config.propagation;
                state.manifestPaid += config.manifestSpirit;
                state.supportPaid += config.naturalSupport ? 0 : config.supportSpirit;
                foreach (var p in plan.Targets) Ignite(Cell(p), false);
            }
            else if (plan.Operation == "open")
            {
                state.comboInstanceId = "fixture_combo_1";
                state.sourceMapping = state.domainMapping = state.comboInstanceId;
                state.openPaid += config.openSpirit; state.tailPaid += plan.Tail;
                foreach (var p in plan.Targets) { Cell(p).tailPaid = true; Cell(p).reached = true; }
            }
            else
            {
                state.cycle++; state.maintenancePaid += plan.Maintenance;
                state.expansionPaid += plan.Expansion; state.tailPaid += plan.Tail;
                state.propagation -= plan.Targets.Length;
                foreach (var p in plan.Targets) Ignite(Cell(p), true);
                foreach (var unit in state.units)
                    unit.hp = Math.Max(0, unit.hp - Damage(Cell(unit.coordinate), false));
                foreach (var c in oldFires)
                {
                    if (c.normal > 0)
                    {
                        c.normal--;
                        if (c.normal == 0 && c.tailPaid && Legal(c)) c.ember = config.emberCycles;
                        if (c.normal == 0) c.tailPaid = false;
                    }
                    else if (c.ember > 0) c.ember--;
                }
            }
            return Changed();
        }

        private void Ignite(FireDomainCell c, bool reached)
        {
            c.fuel -= config.fuelPerIgnition; c.normal = config.normalCycles; c.ember = 0;
            c.tailPaid = reached; c.reached |= reached; state.fuelPaid += config.fuelPerIgnition;
        }
        private int Damage(FireDomainCell c, bool enter)
            => c == null ? 0 : c.normal > 0 ? (enter ? config.enterDamage : config.stayDamage) : c.ember > 0 ? config.emberDamage : 0;

        public bool Counter(Vector2Int coordinate, string kind)
        {
            if (state.ended) return Reject("battle_ended");
            var c = Cell(coordinate);
            if (c == null) return Reject("unknown_cell");
            if (kind != "extinguish" && kind != "water" && kind != "isolate" && kind != "fuel") return Reject("unknown_counter");
            c.normal = c.ember = 0; c.tailPaid = false;
            if (kind == "water") c.water = true;
            if (kind == "isolate") c.isolated = true;
            if (kind == "fuel") c.fuel = 0;
            return Changed();
        }

        public bool Move(string id, Vector2Int target)
        {
            if (state.ended) return Reject("battle_ended");
            var unit = state.units.FirstOrDefault(u => u.id == id);
            if (unit == null || Cell(target) == null || Distance(target - unit.coordinate) != 1) return Reject("invalid_move");
            if (state.units.Any(u => u != unit && u.coordinate == target)) return Reject("unit_occupied");
            unit.coordinate = target; unit.hp = Math.Max(0, unit.hp - Damage(Cell(target), true));
            return Changed();
        }

        public bool CloseMain(string reason)
        {
            if (state.ended) return Reject("battle_ended");
            state.mainClosed = true; state.comboInstanceId = state.sourceMapping = state.domainMapping = "";
            return Changed();
        }
        public bool AttackCenter(int damage)
        {
            if (state.ended || damage <= 0) return Reject("invalid_attack");
            state.centerHp = Math.Max(0, state.centerHp - damage);
            if (state.centerHp == 0) return CloseMain("center_destroyed");
            return Changed();
        }
        public bool TryOccupy(string seat)
        {
            if (state.ended) return Reject("battle_ended");
            if (seat != "source" && seat != "domain") return Reject("unknown_seat");
            if (state.mainClosed || (seat == "source" ? state.sourceMapping != "" || config.sourceSeatReserved
                : state.domainMapping != "" || config.domainSeatReserved)) return Reject("seat_occupied");
            if (seat == "source") state.sourceMapping = "external_fixture_instance";
            else state.domainMapping = "external_fixture_instance";
            return Changed();
        }
        public void EndBattle()
        {
            if (state.ended) return;
            foreach (var c in state.cells) { c.normal = c.ember = 0; c.tailPaid = c.reached = false; }
            state.sourceRef = state.comboInstanceId = state.sourceMapping = state.domainMapping = "";
            state.propagation = 0; state.ended = state.mainClosed = true; Changed();
        }
    }
}
