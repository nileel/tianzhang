using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TianZhang.Combat;
using NativeHex = TianZhang.Spatial.HexCoord;

namespace BattleSim;

static class StageCombatReport
{
    public static IReadOnlyList<Character>[] SelectPools(IEnumerable<IReadOnlyList<Character>> sourcePools, string realm, int? subIndex = null)
    {
        if (sourcePools == null) throw new ArgumentNullException(nameof(sourcePools));
        if (string.IsNullOrWhiteSpace(realm)) throw new ArgumentException("Realm is required.", nameof(realm));

        return sourcePools
            .Select(pool => (IReadOnlyList<Character>)pool
                .Where(c => c.Realm == realm && (!subIndex.HasValue || c.SubIndex == subIndex.Value))
                .ToList())
            .ToArray();
    }

    public static void PrintSampleCounts(string title, string[] tags, IReadOnlyList<Character>[] pools, int totalSamples)
    {
        Console.WriteLine($"【{title}】");
        for (int i = 0; i < tags.Length; i++)
            Console.WriteLine($"  {tags[i],-8}: {pools[i].Count}/{totalSamples}");
    }
}

// Bounded experiments only. Parameters here are never loaded by either production entry.
static class ActionEconomyBalanceReport
{
    const int Seed = 20261009;
    // Numeric fixtures have no obstacles or movement; the production spatial owner is tested above.
    sealed class AdjacentSpace : ICombatSpatialQuery
    {
        public CombatRangeQueryResult QueryRange(NativeHex a, NativeHex b, int min, int max)
        {
            int d = (Math.Abs(a.Q - b.Q) + Math.Abs(a.R - b.R) + Math.Abs(a.Q + a.R - b.Q - b.R)) / 2;
            return new(d >= min && d <= max, "fixture_out_of_range");
        }
        public CombatMovementQueryResult QueryMovement(NativeHex a, NativeHex b, int ap, int v, IReadOnlyCollection<NativeHex> occupied)
            => throw new NotSupportedException("No movement in abstract cost rows.");
        public IReadOnlyDictionary<NativeHex, int> FindReachable(NativeHex a, int ap, int v, IReadOnlyCollection<NativeHex> occupied)
            => throw new NotSupportedException("No movement in abstract cost rows.");
    }
    static void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("ap_balance: " + message);
    }

    internal static Character Build(Program.BuildDef build, string realm)
    {
        var c = Character.Create(build.Name, build.Innate, build.Style);
        var weights = build.Weights ?? GameData.WeightsFromGongFa(build.GongFaName);
        c.GongFaName = build.GongFaName;
        c.ApplyGrowth(realm, "上品", weights);
        c.FinalizeStats(realm, realm == "练气" ? 8 : 2, "中品", weights);
        if (realm == "金丹") { c.FormedState = "成丹"; c.DanJiType = "balance-fixture"; }
        c.AssignArts();
        return c;
    }

    internal static int Run(IReadOnlyList<Program.BuildDef> builds)
    {
        Emit(new { report = "N-COMBAT-AP-BALANCE-01", version = 1, seed = Seed,
            combatStreamSeed = 20260712, samplesPerBuildRow = 8, samplesPerNativeRow = 100,
            inputs = new[] { "docs/角色养成/术法神通数值模板.txt", "docs/角色养成/功法数值模板.txt",
                "src/Assets/DataConfig/AttackProfiles.csv", "开发管理/统一行动力道具合同.txt", "开发管理/统一行动力换装合同.txt" }
                .ToDictionary(p => p, GameData.InputHash) });
        BuildMatrix(builds);
        ProductionMatrix();
        BudgetMatrix();
        ItemEquipmentMatrix(builds);
        Emit(new { result = "AP_BALANCE_PASS", kernelChanged = false });
        return 0;
    }

    static void BuildMatrix(IReadOnlyList<Program.BuildDef> builds)
    {
        var board = new HexBattlefield();
        foreach (string realm in new[] { "练气", "筑基", "金丹" })
        for (int i = 0; i < builds.Count; i++)
        foreach (bool group in new[] { false, true })
        foreach (string variant in new[] { "baseline", "ap125", "without-movement-bonus" })
        {
            Combat.ResetDeterministicRandom();
            double wins = 0, ticks = 0, turns = 0, actions = 0, movement = 0, ap = 0, mp = 0, penalty = 0, hp = 0;
            int skips = 0, timeouts = 0;
            for (int sample = 0; sample < 8; sample++)
            {
                var a = Build(builds[i], realm);
                var b = Build(builds[(i + 1) % builds.Count], realm);
                if (variant == "ap125") a.SetActionEconomyInputs(125000, a.MovementEfficiencyMilliUnits);
                if (variant == "without-movement-bonus") a.SetActionEconomyInputs(100000, a.MovementInput.BaseMilliUnits);
                bool swap = sample % 2 != 0;
                Combat.GroupRoundResult r;
                if (!group)
                    r = Combat.SimulateDetailed(board, swap ? b : a, swap ? a : b);
                else
                {
                    var aa = Build(builds[(i + 2) % builds.Count], realm);
                    var bb = Build(builds[(i + 3) % builds.Count], realm);
                    r = Combat.Simulate2v2Detailed(board, swap ? b : a, swap ? bb : aa, swap ? a : b, swap ? aa : bb);
                }
                int teamA = swap ? 1 : 0;
                int actorA = swap ? group ? 2 : 1 : 0;
                wins += r.WinnerTeam < 0 ? .5 : r.WinnerTeam == teamA ? 1 : 0;
                ticks += r.GlobalTick; turns += r.Turns;
                hp += swap ? r.HpRatioB : r.HpRatioA;
                var own = r.Economy.Where(e => e.ActorIndex == actorA).ToArray();
                actions += own.Count(e => e.Event == "action_committed");
                movement += own.Count(e => e.Event == "move_committed");
                ap += own.Sum(e => e.CostUnits);
                mp += own.Where(e => e.Event == "action_committed").Sum(e => Math.Max(0, e.SpiritBefore - e.SpiritAfter));
                penalty += own.Where(e => e.Event is "own_turn_ended" or "combat_terminated").Sum(e => e.PendingCtPenalty);
                skips += own.Count(e => e.Event == "own_turn_started_control_skip");
                if (r.Turns == Combat.DuelTurnLimit && r.HpRatioA > 0 && r.HpRatioB > 0) timeouts++;
                foreach (var e in r.Economy.Where(e => e.Event is "action_committed" or "move_committed"))
                    Require(e.CostUnits > 0 && e.ActionPointsBeforeUnits - e.ActionPointsRemainingUnits == e.CostUnits,
                        "generic paid action must strictly decrease AP");
            }
            var input = Build(builds[i], realm);
            var aggregate = Combat.Simulate(input, Build(builds[(i + 1) % builds.Count], realm), 1);
            Require(double.IsFinite(aggregate.avgTurns), "public Simulate observation");
            Emit(new { matrix = "builds", realm, build = builds[i].Name, opponent = builds[(i + 1) % builds.Count].Name,
                mode = group ? "2v2" : "1v1", variant, input.Primary,
                art = input.ArtCost, divine = input.DivineCost, movementInput = input.MovementInput,
                winPercent = wins * 12.5, meanTicks = ticks / 8, meanOwnTurnsAllActors = turns / 8,
                meanActorActions = actions / 8, meanActorMoves = movement / 8, meanActorApUnits = ap / 8,
                meanActorSpiritSpent = mp / 8, meanActorCtPenalty = penalty / 8, meanTeamHpRatio = hp / 8, skips, timeouts });
        }
    }

    sealed record NativeCase(string Name, int GuanshiCost = 50000, int GuanshiCt = 10, int GuanshiCd = 10,
        int RecoveryCost = 25000, int RecoveryCd = 5, int Maximum = 100000, int BasicCost = 50000, int BasicMinimum = 25000,
        float BasicMultiplier = 1, int Heal = 0, int HealCost = 30000, int Pills = 0, int SwapCost = 0,
        CombatActionEconomyModifier Modifier = null, int InitialSpirit = 20);

    sealed record NativeResult(int Winner, int Ticks, int Turns, int Actions, int Ap, int Refund, int SpiritSpent,
        int Heals, int Restored, int Overflow, int PillsLeft, int Swaps, int Ct, int Hp, int EnemyHp, int Mp, bool Timeout,
        int Basics, int Guanshi, int Recoveries);

    // Explicit old-rule sensitivity fixture; not production weapon authorization or AP2 balance proof.
    static CombatAttackProfile[] Profiles(NativeCase c)
    {
        var attack = GameData.ProductionAttacks["spell_guanshiji"];
        var recovery = GameData.ProductionAttacks["spell_julingshu"];
        return new[]
        {
            new CombatAttackProfile("fixture-legacy-basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1,
                c.BasicCost, c.BasicMinimum, 0, 0, physicalMultiplier: c.BasicMultiplier),
            new CombatAttackProfile("spell_guanshiji", CombatAttackKind.Art, CombatAttackEffect.Physical, 1, 1,
                c.GuanshiCost, attack.Cost.MinimumNetCostUnits, c.GuanshiCt, c.GuanshiCd,
                physicalMultiplier: (float)attack.Mult, spiritCost: attack.SpiritCost),
            new CombatAttackProfile("spell_julingshu", CombatAttackKind.Art, CombatAttackEffect.RestoreSpirit, 0, 2,
                c.RecoveryCost, recovery.Cost.MinimumNetCostUnits, recovery.Cost.CtPenalty, c.RecoveryCd,
                spiritRecoveryMultiplier: (float)recovery.SpiritRecoveryMultiplier, minimumSpiritRecovery: recovery.MinimumSpiritRecovery,
                otherTargetSpiritCost: recovery.OtherTargetSpiritCost, otherTargetSpiritRecoveryMultiplier: (float)recovery.OtherTargetRecoveryMultiplier),
            // The unchanged opposing basic action isolates one actor's parameter sensitivity.
            new CombatAttackProfile("fixture-control-basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1,
                50000, 50000, 0, 0, physicalMultiplier: 1)
        };
    }

    static CombatantSnapshot Snapshot(string id, CombatTeam team, Character source, NativeCase c, int speed, bool endowed)
    {
        return new CombatantSnapshot(id, team, new NativeHex(id.EndsWith("1") ? team == CombatTeam.Player ? 1 : 0 : team == CombatTeam.Player ? 0 : 1, id.EndsWith("1") ? -1 : 0),
            speed, source.Primary["HP"], source.Primary["HP"], source.Primary["肉攻"], source.Primary["神攻"],
            source.Primary["肉防"], source.Primary["神防"], endowed ? c.Maximum : 100000, 0,
            equippedArtProfileIds: new[] { "spell_guanshiji", "spell_julingshu" },
            availableArtProfileIds: new[] { "spell_guanshiji", "spell_julingshu" },
            basicAttackProfileId: endowed ? "fixture-legacy-basic" : "fixture-control-basic", rawSpirit: source.Innate["魂魄"], realmStage: "练气",
            actionEconomyModifiers: endowed && c.Modifier != null ? new[] { c.Modifier } : Array.Empty<CombatActionEconomyModifier>())
        { HitRateBonus = (float)source.Secondary.GetValueOrDefault("命中率", 0) };
    }

    static NativeResult NativeBattle(Character source, NativeCase c, bool group, int seed, string policy = "production", int speed = 50)
    {
        var random = new Random(seed);
        var actors = new[] { Snapshot(seed % 2 == 0 ? "a0" : "z0", CombatTeam.Player, source, c, speed, true),
            Snapshot("e0", CombatTeam.Enemy, source, c, 50, false) }.ToList();
        if (group) { actors.Add(Snapshot("p1", CombatTeam.Player, source, c, 50, false)); actors.Add(Snapshot("e1", CombatTeam.Enemy, source, c, 50, false)); }
        foreach (var actor in actors) actor.SetSpirit(source.Primary["MP"], source.Primary["MP"]);
        actors[0].SetSpirit(source.Primary["MP"], Math.Min(source.Primary["MP"], c.InitialSpirit));
        foreach (var actor in actors)
            actor.Facing = actor.Position.DirectionTo(actors.First(a => a.Team != actor.Team).Position);
        var profiles = Profiles(c);
        var session = new CombatSession(actors, profiles, new AdjacentSpace(), new[] {
            new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
            new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) });
        var service = new CombatCommandService();
        int actions = 0, turns = 0, paid = 0, refunds = 0, spent = 0, heals = 0, restored = 0, overflow = 0, swaps = 0, ct = 0;
        int pills = c.Pills;
        int basics = 0, guanshi = 0, recoveries = 0;
        while (!session.IsFinished && turns < 100)
        {
            var advance = service.AdvanceUntilAction(session);
            if (!advance.HasActor) break;
            turns++;
            var actor = actors.Single(a => a.Id == advance.ActorId);
            bool player = actor == actors[0];
            int committed = 0;
            while (actor.OwnTurnActive && !session.IsFinished)
            {
                var target = actors.Where(a => a.Team != actor.Team && a.IsAlive).OrderBy(a => a.CurrentHealth).ThenBy(a => a.Id, StringComparer.Ordinal).First();
                string profile = actor.BasicAttackProfileId;
                string targetId = target.Id;
                CombatCommandKind kind = CombatCommandKind.BasicAttack;
                bool Legal(string id, string tid) => service.Preview(session, new CombatCommand(CombatCommandKind.Art, actor.Id, tid, id)).IsLegal;
                // Abstract contract costs use existing scalar primitives; no fake UseItem command.
                if (player && c.SwapCost > 0 && swaps == 0 && actor.ActionPointsRemainingUnits >= c.SwapCost)
                {
                    actor.SpendActionPoints(c.SwapCost); session.RecordAction(0); swaps++; actions++; paid += c.SwapCost;
                    committed++; continue;
                }
                if (player && pills > 0 && actor.MaximumHealth - actor.CurrentHealth >= c.Heal && actor.ActionPointsRemainingUnits >= c.HealCost)
                {
                    int before = actor.CurrentHealth;
                    actor.SpendActionPoints(c.HealCost); actor.RestoreHealth(c.Heal); session.RecordAction(0);
                    pills--; heals++; restored += actor.CurrentHealth - before; overflow += c.Heal - (actor.CurrentHealth - before);
                    actions++; paid += c.HealCost; committed++;
                    Require(committed <= c.Maximum / Math.Min(c.HealCost, 25000), "finite abstract actions");
                    continue;
                }
                if (player && policy == "production" && Legal("spell_guanshiji", target.Id))
                { profile = "spell_guanshiji"; kind = CombatCommandKind.Art; }
                else if (player && policy == "production" && actor.CurrentSpirit < 12 && Legal("spell_julingshu", actor.Id))
                { profile = "spell_julingshu"; targetId = actor.Id; kind = CombatCommandKind.Art; }
                var command = new CombatCommand(kind, actor.Id, targetId, profile);
                var quote = service.Preview(session, command);
                if (!quote.IsLegal) break;
                if (profiles.Single(value => value.Id == profile).Effect is CombatAttackEffect.Physical or CombatAttackEffect.Soul or CombatAttackEffect.Hybrid)
                    command = new CombatCommand(kind, actor.Id, targetId, profile,
                        new CombatResolutionRolls((float)random.NextDouble() * 100, (float)random.NextDouble() * 100,
                            (float)random.NextDouble() * 100, (float)random.NextDouble() * 100, (float)random.NextDouble() * 100));
                int beforeMp = actor.CurrentSpirit;
                int tick = session.TurnScheduler.CurrentTick;
                var result = service.Execute(session, command, quote);
                Require(result.Succeeded, result.RejectionReason);
                Require(session.TurnScheduler.CurrentTick == tick, "subcommand advanced the integer clock");
                int refund = result.ActionPointRefundUnits;
                Require(quote.ActionPointCostUnits - refund >= quote.MinimumNetCostUnits, "nonpositive net paid action");
                Require(++committed <= c.Maximum / profiles.Min(p => p.MinimumNetCostUnits), "unbounded own-turn actions");
                if (player)
                {
                    actions++; paid += quote.ActionPointCostUnits; refunds += refund;
                    spent += Math.Max(0, beforeMp - actor.CurrentSpirit); ct += quote.CtPenalty;
                    if (profile == "fixture-legacy-basic") basics++;
                    if (profile == "spell_guanshiji") guanshi++;
                    if (profile == "spell_julingshu") recoveries++;
                }
            }
            if (actor.OwnTurnActive && !session.IsFinished)
                Require(service.Execute(session, new CombatCommand(CombatCommandKind.EndTurn, actor.Id)).Succeeded, "end turn");
        }
        var p = actors[0];
        int ph = actors.Where(a => a.Team == CombatTeam.Player).Sum(a => a.CurrentHealth);
        int eh = actors.Where(a => a.Team == CombatTeam.Enemy).Sum(a => a.CurrentHealth);
        return new(ph == eh ? -1 : ph > eh ? 0 : 1, session.TurnScheduler.CurrentTick, turns, actions, paid, refunds,
            spent, heals, restored, overflow, pills, swaps, ct, ph, eh, p.CurrentSpirit, !session.IsFinished, basics, guanshi, recoveries);
    }

    static void NativeRow(string matrix, Character source, NativeCase c, bool group, string policy = "production", int speed = 50)
    {
        var results = Enumerable.Range(Seed, 100).Select(seed => NativeBattle(source, c, group, seed, policy, speed)).ToArray();
        Emit(new { matrix, build = source.Name, source.Primary, mode = group ? "2v2" : "1v1", parameters = c, speed, policy,
            winPercent = results.Average(r => r.Winner < 0 ? .5 : r.Winner == 0 ? 1 : 0) * 100,
            meanTicks = results.Average(r => r.Ticks), meanTurns = results.Average(r => r.Turns),
            meanActions = results.Average(r => r.Actions), meanApUnits = results.Average(r => r.Ap),
            meanRefundUnits = results.Average(r => r.Refund), meanSpiritSpent = results.Average(r => r.SpiritSpent),
            meanHeals = results.Average(r => r.Heals), meanRestored = results.Average(r => r.Restored), meanOverflow = results.Average(r => r.Overflow),
            meanPillsLeft = results.Average(r => r.PillsLeft), meanSwaps = results.Average(r => r.Swaps), meanCt = results.Average(r => r.Ct),
            meanHp = results.Average(r => r.Hp), meanEnemyHp = results.Average(r => r.EnemyHp), meanMp = results.Average(r => r.Mp),
            meanBasics = results.Average(r => r.Basics), meanGuanshi = results.Average(r => r.Guanshi), meanRecoveries = results.Average(r => r.Recoveries),
            timeouts = results.Count(r => r.Timeout) });
    }

    static Character Neutral(int hp = 571)
    {
        var c = new Character { Name = "production-neutral", Realm = "练气", Style = "physical",
            Innate = new() { ["根骨"] = 8, ["魂魄"] = 8, ["神识"] = 8, ["资质"] = 8, ["气运"] = 8 },
            Primary = new() { ["HP"] = hp, ["MP"] = 180, ["肉攻"] = 60, ["神攻"] = 60, ["肉防"] = 42, ["神防"] = 42, ["反应"] = 50, ["神识"] = 25 } };
        c.SetActionEconomyInputs(100000, 2000);
        return c;
    }

    static void ProductionMatrix()
    {
        foreach (bool group in new[] { false, true })
        foreach (var c in new[] { new NativeCase("baseline"), new("guanshi-ap40", GuanshiCost: 40000),
            new("guanshi-ap60", GuanshiCost: 60000), new("guanshi-ct0", GuanshiCt: 0), new("guanshi-ct20", GuanshiCt: 20),
            new("guanshi-cd0", GuanshiCd: 0), new("guanshi-cd20", GuanshiCd: 20),
            new("recovery-ap20", RecoveryCost: 20000), new("recovery-ap35", RecoveryCost: 35000), new("recovery-cd0", RecoveryCd: 0) })
            NativeRow("production", Neutral(), c, group);
    }

    static void BudgetMatrix()
    {
        foreach (bool group in new[] { false, true })
        {
            foreach (int maximum in new[] { 100000, 125000, 150000 })
                NativeRow("budget", Neutral(), new("ap" + maximum, Maximum: maximum), group, "basic");
            foreach (int speed in new[] { 25, 50, 100 })
                NativeRow("speed", Neutral(), new("speed" + speed), group, "basic", speed);
            foreach (var modifier in new[] {
                new CombatActionEconomyModifier("fixed10", "fixture", CombatCommandKind.BasicAttack, "fixture-legacy-basic", 10000, 1000, 25000, 0, 0, 0, 0, 0),
                new("retained80", "fixture", CombatCommandKind.BasicAttack, "fixture-legacy-basic", 0, 800, 25000, 0, 0, 0, 0, 0),
                new("floor25", "fixture", CombatCommandKind.BasicAttack, "fixture-legacy-basic", 0, 0, 25000, 0, 0, 0, 0, 0),
                new("refund10x2", "fixture", CombatCommandKind.BasicAttack, "fixture-legacy-basic", 0, 1000, 25000, 0, 0, 10000, 2, 20000),
                new("refund-stress", "fixture", CombatCommandKind.BasicAttack, "fixture-legacy-basic", 0, 1000, 25000, 0, 0, int.MaxValue, int.MaxValue, 1000000) })
                NativeRow("modifier", Neutral(), new(modifier.SourceInstanceId, Modifier: modifier), group, "basic");
        }
        var board = new HexBattlefield();
        foreach (int v in new[] { 2000, 4000, 5000, 12000, 16000, 48000, 64000, 240000, 1440000 })
        {
            var path = board.QueryMovementPath(new(0, 0), new(1, 0), 100000, v);
            Require(path.ActionPointCostUnits > 0, "positive movement floor");
            Emit(new { matrix = "movement", vMilli = v, edgeApUnits = path.ActionPointCostUnits,
                edgesAfterBasic = 50000 / path.ActionPointCostUnits, edgesWithAp100 = 100000 / path.ActionPointCostUnits,
                edgesWithAp125 = 125000 / path.ActionPointCostUnits, reactionDependent = false,
                scope = v > 64000 ? "NPC arithmetic only" : "ordinary path" });
        }
    }

    static void ItemEquipmentMatrix(IReadOnlyList<Program.BuildDef> builds)
    {
        var ordered = builds.Select(b => Build(b, "练气")).OrderBy(c => c.Primary["HP"]).ToArray();
        foreach (var source in new[] { ordered.First(), ordered[ordered.Length / 2], ordered.Last() })
        foreach (bool group in new[] { false, true })
        foreach (var c in new[] { new NativeCase("no-pill"), new("pill80-ap30-n3", Heal: 80, Pills: 3),
            new("pill40", Heal: 40, Pills: 3), new("pill120", Heal: 120, Pills: 3),
            new("pill-ap20", Heal: 80, HealCost: 20000, Pills: 3), new("pill-ap40", Heal: 80, HealCost: 40000, Pills: 3),
            new("pill-n1", Heal: 80, Pills: 1), new("pill-n5", Heal: 80, Pills: 5) })
            NativeRow("item-abstract", source, c, group, "basic");
        foreach (bool group in new[] { false, true })
        foreach (var c in new[] { new NativeCase("unarmed"), new("sword", BasicCost: 40000, BasicMinimum: 40000),
            new("blade", BasicMinimum: 50000, BasicMultiplier: 1.25f), new("swap-to-sword", BasicCost: 40000, BasicMinimum: 40000, SwapCost: 25000),
            new("swap-to-blade", BasicMinimum: 50000, BasicMultiplier: 1.25f, SwapCost: 25000),
            new("sword-ap80", Maximum: 80000, BasicCost: 40000, BasicMinimum: 40000),
            new("blade-ap80", Maximum: 80000, BasicMinimum: 50000, BasicMultiplier: 1.25f),
            new("sword-with-pills", BasicCost: 40000, BasicMinimum: 40000, Heal: 80, Pills: 3) })
            NativeRow("equipment-abstract", Neutral(), c, group, "basic");
        // Exact finite inventory / missing-health boundary, independent of policy in the encounter rows.
        foreach (int hp in new[] { 200, 180, 50, 0 })
        foreach (bool stun in new[] { false, true })
        {
            var c = new NativeCase("boundary", Heal: 80, Pills: 3);
            var actor = Snapshot("p0", CombatTeam.Player, Neutral(200), c, 100, true);
            var target = Snapshot("e0", CombatTeam.Enemy, Neutral(200), c, 1, false);
            actor.ReceiveDamage(200 - hp);
            if (stun) actor.ApplyStatus(CombatStatusKind.Stun, 1);
            var s = new CombatSession(new[] { actor, target }, Profiles(c), new AdjacentSpace(), new[] {
                new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
                new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) });
            s.BeginTurn(actor); // Inspect this first start, not a later turn returned after a control skip.
            int pills = 3, count = 0, restored = 0;
            while (pills > 0 && actor.OwnTurnActive && actor.IsAlive)
            {
                if (actor.HasStatus(CombatStatusKind.Stun) || actor.ActionPointsRemainingUnits < c.HealCost) break;
                int before = actor.CurrentHealth;
                actor.SpendActionPoints(c.HealCost); actor.RestoreHealth(c.Heal); s.RecordAction(0);
                pills--; count++; restored += actor.CurrentHealth - before;
            }
            Require(hp == 0 || stun ? count == 0 : count == 3, "dead/control/finite pill boundary");
            Emit(new { matrix = "item-boundary", initialHp = hp, stun, count, restored,
                overflow = 80 * count - restored, pillsLeft = pills, apLeft = actor.ActionPointsRemainingUnits });
        }
    }
}
