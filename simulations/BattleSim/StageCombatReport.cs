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
        Emit(new { report = "LEGACY_NATIVE_SENSITIVITY_ONLY", version = 1, seed = Seed,
            combatStreamSeed = 20260712, samplesPerBuildRow = 8, samplesPerNativeRow = 100,
            inputs = new[] { "docs/角色养成/术法神通数值模板.txt", "docs/角色养成/功法数值模板.txt",
                "src/Assets/DataConfig/AttackProfiles.csv", "开发管理/统一行动力道具合同.txt", "开发管理/统一行动力换装合同.txt" }
                .ToDictionary(p => p, GameData.InputHash) });
        BuildMatrix(builds);
        ProductionMatrix();
        BudgetMatrix();
        ItemEquipmentMatrix(builds);
        Emit(new { result = "LEGACY_FIXTURE_ONLY_NOT_AP2_BALANCE", kernelChanged = true });
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

// AP2 entry uses the same four-table projection and the actual shared command transaction.
static class Ap2CombatReport
{
    internal sealed class Space : ICombatSpatialQuery
    {
        public CombatRangeQueryResult QueryRange(NativeHex a, NativeHex b, int min, int max)
        {
            int d = (Math.Abs(a.Q-b.Q)+Math.Abs(a.R-b.R)+Math.Abs(a.Q+a.R-b.Q-b.R))/2;
            return new(d >= min && d <= max,"target_out_of_range");
        }
        public CombatMovementQueryResult QueryMovement(NativeHex a, NativeHex b, int ap, int v, IReadOnlyCollection<NativeHex> occupied)
            => throw new NotSupportedException("AP2 adjacent fixture has no movement command.");
        public IReadOnlyDictionary<NativeHex,int> FindReachable(NativeHex a,int ap,int v,IReadOnlyCollection<NativeHex> occupied)
            => throw new NotSupportedException("AP2 adjacent fixture has no movement command.");
    }
    internal static CombatOrdinaryActionDefinition[] Ordinary() => new[] {
        new CombatOrdinaryActionDefinition(CombatCommandKind.Guard,50000,50000,0,0),
        new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell,50000,50000,0,60) };
    internal static CombatAttackProfile[] Profiles(Character c)
    {
        var profiles = new List<CombatAttackProfile>();
        if (c.ArtName != "") profiles.Add(GameData.SharedProfile(GameData.GenericProfile(GameData.DefinitionForStyle(c.Style,false),c.AbilityTierId)));
        if (c.DivineName != "") profiles.Add(GameData.SharedProfile(GameData.GenericProfile(GameData.DefinitionForStyle(c.Style,true),c.AbilityTierId),c.DivineDefPen));
        if (c.GongFaName == "苦行剑典" && c.Style == "yuqing_kuxing" && c.Realm != "凡人")
            profiles.Add(GameData.SharedProfile(GameData.GenericProfile("KuxingAction",c.AbilityTierId)));
        profiles.AddRange(c.ProductionArtIds.Select(id => GameData.SharedProfile(GameData.ProductionAttacks[id])));
        profiles.Add(GameData.SharedProfile(c.EffectiveBasicAttackProfile));
        return profiles.ToArray();
    }
    internal static CombatantSnapshot Snapshot(string id, CombatTeam team, Character c, NativeHex position)
    {
        var profiles = Profiles(c);
        var arts = profiles.Where(p => p.Kind == CombatAttackKind.Art).Select(p => p.Id).ToArray();
        var divine = profiles.Where(p => p.Kind == CombatAttackKind.Divine).Select(p => p.Id).ToArray();
        var instances = profiles.ToDictionary(p => p.Id,p =>
            p.Kind == CombatAttackKind.Divine && c.DivineAbilityInstanceId != "" ? c.DivineAbilityInstanceId :
            p.RuleMetadata.PrototypeDefinitionRef == GameData.DefinitionForStyle(c.Style,false) && c.ArtAbilityInstanceId != "" ? c.ArtAbilityInstanceId :
            id + ":" + (p.RuleMetadata.PrototypeDefinitionRef == "" ? p.Id : p.RuleMetadata.PrototypeDefinitionRef));
        var actor = new CombatantSnapshot(id,team,position,c.Primary["反应"],c.Primary["HP"],c.IsDead ? 0 : c.Primary["HP"],
            c.Primary["肉攻"],c.Primary["神攻"],c.Primary["肉防"],c.Primary["神防"],c.ActionPointMaximumUnits,c.MovementEfficiencyMilliUnits,
            realmMultiplier:c.ProductionRealmMultiplier ?? (c.Realm switch { "金丹" => 6f,"筑基" => 3f,_ => 1f }),
            equippedArtProfileIds:arts,availableArtProfileIds:arts,basicAttackProfileId:profiles.SingleOrDefault(p => p.Kind == CombatAttackKind.Basic)?.Id,
            equippedDivineProfileIds:divine,availableDivineProfileIds:divine,rawSpirit:c.Innate["魂魄"],realmStage:c.Realm,
            abilityInstanceIds:instances,rawMind:c.Primary["神识"])
        {
            GongFaId = c.GongFaName,GongFaElement = GameData.NormalizeElement(GameData.GongFaElements.GetValueOrDefault(c.GongFaName,"")),
            HealthRecoveryPerOwnTurn = c.HealthRecoveryPerOwnTurn,SpiritRecoveryPerOwnTurn = c.SpiritRecoveryPerOwnTurn,
            HitRateBonus = (float)c.Secondary.GetValueOrDefault("命中率",0),DodgeRate = (float)c.Secondary.GetValueOrDefault("闪避率",0),
            CriticalRate = (float)c.Secondary.GetValueOrDefault("暴击率",0),CriticalDamage = (float)c.Secondary.GetValueOrDefault("暴击伤害",0),
            BlockRate = (float)c.Secondary.GetValueOrDefault("格挡率",0),BlockReduction = (float)c.Secondary.GetValueOrDefault("格挡减伤率",0),
            SoulShieldRate = (float)c.Secondary.GetValueOrDefault("魂盾率",0),SoulShieldReduction = (float)c.Secondary.GetValueOrDefault("魂盾减伤率",0),
        };
        actor.SetSpirit(c.Primary["MP"],c.Primary["MP"]);
        actor.ShouyiStacks = c.GongFaName == "抱元守一经" && c.Realm == "金丹" ? 2 : 0;
        actor.FudanStacks = c.GongFaName == "云篆度人经" && c.Realm == "金丹" ? 2 : 0;
        return actor;
    }
    internal static CombatSession Session(IReadOnlyList<Character> characters, bool group)
    {
        var actors = characters.Select((c,i) => Snapshot("actor-"+i,
            i < (group ? 2 : 1) ? CombatTeam.Player : CombatTeam.Enemy,c,
            group ? new[] { new NativeHex(0,0), new NativeHex(1,0), new NativeHex(0,1), new NativeHex(1,-1) }[i] :
                new NativeHex(i == 0 ? 0 : 1,0))).ToArray();
        foreach (var actor in actors) actor.Facing = actor.Position.DirectionTo(actors.First(a => a.Team != actor.Team).Position);
        return new CombatSession(actors,characters.SelectMany(Profiles)
            .Concat(GameData.ProductionAttacks.Values.Where(p => p.BasicBindingKind != "").Select(p => GameData.SharedProfile(p)))
            .GroupBy(p => p.Id,StringComparer.Ordinal).Select(g => g.First()),
            new Space(),Ordinary(),ruleSet:GameData.Rules);
    }
    internal static void RefreshGrowth(CombatSession session, string actorId, Character c) =>
        session.RefreshActionEconomy(actorId,c.ActionPointMaximumUnits,c.MovementEfficiencyMilliUnits,c.Realm,c.EffectiveBasicAttackProfile.Cost.DefinitionRef);

    internal static object[] FullTurnSummary(Combat.GroupRoundResult battle, IReadOnlyList<Character> characters, bool group)
    {
        var previous = characters.Select(c => c.Primary["HP"]).ToArray();
        var damage = new Dictionary<int,int>();
        int teamSize = group ? 2 : 1;
        foreach (var e in battle.Economy)
        {
            if (e.Event == "action_committed")
                damage[e.Turn] = damage.GetValueOrDefault(e.Turn) + Enumerable.Range(0,characters.Count)
                    .Where(i => i/teamSize != e.ActorIndex/teamSize).Sum(i => Math.Max(0,previous[i]-e.HealthByActor[i]));
            previous = e.HealthByActor.ToArray();
        }
        return battle.Economy.GroupBy(e => (e.Turn,e.ActorIndex)).Select(g => (object)new {
            g.Key.Turn,g.Key.ActorIndex,actions=g.Count(e => e.Event == "action_committed"),
            moves=g.Count(e => e.Event == "move_committed"),apUnits=g.Sum(e => e.CostUnits),
            spiritUnits=g.Where(e => e.Event == "action_committed").Sum(e => Math.Max(0,e.SpiritBefore-e.SpiritAfter)),
            netSpirit=g.Last().SpiritAfter-g.First().SpiritBefore,
            damage=damage.GetValueOrDefault(g.Key.Turn) }).ToArray();
    }
    internal static object Battle(IReadOnlyList<Character> characters,bool group,int seed,int turnLimit = 100)
    {
        var session = Session(characters,group); var service = new CombatCommandService(); var random = new Random(seed);
        var trace = new List<object>(); var ownTurns = new List<object>(); int turns = 0, controls = 0;
        var basicCounts = new int[characters.Count]; var spiritSpent = new int[characters.Count];
        var output = new int[characters.Count]; var activeCounts = new int[characters.Count];
        while (!session.IsFinished && turns < turnLimit)
        {
            var beforeSpirit = session.Combatants.All.ToDictionary(a => a.Id,a => a.CurrentSpirit);
            var next = service.AdvanceUntilAction(session); if (!next.HasActor) break;
            turns++; var actor = session.Combatants.All.Single(a => a.Id == next.ActorId);
            activeCounts[int.Parse(actor.Id.Split('-')[1])]++;
            int count=0,paid=0,damage=0,spirit=0;
            while (actor.OwnTurnActive && !session.IsFinished)
            {
                var enemy = session.Combatants.All.Where(a => a.Team != actor.Team && a.IsAlive).OrderBy(a => a.CurrentHealth).ThenBy(a => a.Id).First();
                CombatCommand selected = null; CombatCommandPreview quote = null;
                foreach (var profile in session.AttackProfiles.OrderBy(p => p.Kind == CombatAttackKind.Divine ? 0 : p.RuleMetadata.PrototypeDefinitionRef == "KuxingAction" ? 1 : p.Kind == CombatAttackKind.Art ? 2 : 3))
                {
                    if (profile.Effect == CombatAttackEffect.RestoreSpirit && actor.CurrentSpirit == actor.MaximumSpirit) continue;
                    var command = new CombatCommand(profile.Kind == CombatAttackKind.Basic ? CombatCommandKind.BasicAttack : profile.Kind == CombatAttackKind.Art ? CombatCommandKind.Art : CombatCommandKind.Divine,
                        actor.Id,profile.Effect == CombatAttackEffect.RestoreSpirit ? actor.Id : enemy.Id,profile.Id);
                    var candidate = service.Preview(session,command); if (!candidate.IsLegal) continue;
                    selected = command; quote = candidate; break;
                }
                if (selected == null) break;
                var rolls = new CombatResolutionRolls((float)random.NextDouble()*100,(float)random.NextDouble()*100,
                    (float)random.NextDouble()*100,(float)random.NextDouble()*100,(float)random.NextDouble()*100);
                var resolved = new CombatCommand(selected.Kind,selected.ActorId,selected.TargetId,selected.ProfileId,rolls);
                // The preview confirmation is bound to identity, not to the private resolution samples.
                quote = service.Preview(session,resolved);
                int ap = actor.ActionPointsRemainingUnits,tick = session.TurnScheduler.CurrentTick;
                bool controlled = enemy.HasStatus(CombatStatusKind.Stun);
                var result = service.Execute(session,resolved,quote);
                if (!result.Succeeded || quote.ActionPointCostUnits <= 0 || result.ActionPointSpentUnits != quote.ActionPointCostUnits ||
                    (!session.IsFinished && ap-actor.ActionPointsRemainingUnits != quote.ActionPointCostUnits) || tick != session.TurnScheduler.CurrentTick)
                    throw new InvalidOperationException("ap2_shared_commit:"+result.RejectionReason);
                trace.Add(new { turns,actor.Id,tick,selected.ProfileId,quote.ActionPointCostUnits,quote.CommittedUseCount,quote.RepeatMultiplierPermille,
                    quote.AbilityTierId,actor.CurrentHealth,actor.CurrentSpirit,actor.ShouyiStacks,actor.FudanStacks,result.Damage });
                count++; paid+=result.ActionPointSpentUnits; damage+=result.Damage.Sum(d => d.FinalDamage); spirit+=result.SpiritSpent;
                int index = int.Parse(actor.Id.Split('-')[1]);
                if (selected.Kind == CombatCommandKind.BasicAttack) basicCounts[index]++;
                spiritSpent[index] += result.SpiritSpent; output[index] += result.Damage.Sum(d => d.FinalDamage);
                if (!controlled && enemy.HasStatus(CombatStatusKind.Stun)) controls++;
            }
            ownTurns.Add(new { turns,actor.Id,actions=count,apUnits=paid,spiritUnits=spirit,
                netSpirit=actor.CurrentSpirit-beforeSpirit[actor.Id],damage });
            if (actor.OwnTurnActive && !session.IsFinished) service.Execute(session,new CombatCommand(CombatCommandKind.EndTurn,actor.Id));
        }
        var all = session.Combatants.All.ToArray(); int teamSize = group ? 2 : 1;
        bool playerAlive = all.Any(a => a.Team == CombatTeam.Player && a.IsAlive);
        bool enemyAlive = all.Any(a => a.Team == CombatTeam.Enemy && a.IsAlive);
        return new { turns,ticks=session.TurnScheduler.CurrentTick,trace,ownTurns,
            winner = playerAlive && !enemyAlive ? 0 : enemyAlive && !playerAlive ? 1 : -1,
            timeout = playerAlive && enemyAlive, controls,
            actors = all.Select((a,i) => new { a.Id, team = i/teamSize, a.OwnTurnsStarted,
                activeTurns = activeCounts[i],
                basics = basicCounts[i], spiritSpent = spiritSpent[i], damage = output[i],
                netSpirit = a.CurrentSpirit-characters[i].Primary["MP"], a.ShouyiStacks,a.FudanStacks }),
            remaining=all.Select(a => new { a.Id,a.CurrentHealth,a.CurrentSpirit }) };
    }
    internal static int Run(IReadOnlyList<Program.BuildDef> builds,string tierMode)
    {
        if (tierMode is not ("same-tier" or "locked-qi")) throw new ArgumentException("AP2 mode must be same-tier or locked-qi");
        Console.WriteLine(JsonSerializer.Serialize(new { report="N-COMBAT-AP2-SIM-01",GameData.Rules.RuleSetVersion,GameData.Rules.SourceDigest,
            testTriples=GameData.Rules.Economy.Count(r => r.RecordKind=="test_action"),productionIds=GameData.ProductionAttacks.Keys,tierMode }));
        var board = new HexBattlefield();
        foreach (string realm in new[] { "练气","筑基","金丹" })
        for (int i=0;i<builds.Count;i++) foreach (bool group in new[] { false,true }) foreach (bool mirror in new[] { false,true })
        {
            Character Build(int n)
            {
                var c=ActionEconomyBalanceReport.Build(builds[n%builds.Count],realm);
                c.FixedTestAbilityTierId=tierMode=="locked-qi" ? "tier_lianqi" : GameData.TierForRealm(realm); c.AssignArts(); return c;
            }
            var chars=group ? new[] { Build(i),Build(i+2),Build(i+1),Build(i+3) } : new[] { Build(i),Build(i+1) };
            if (mirror) chars=group ? new[] { chars[2],chars[3],chars[0],chars[1] } : new[] { chars[1],chars[0] };
            Combat.ResetDeterministicRandom();
            var full=group ? Combat.Simulate2v2Detailed(board,chars[0],chars[1],chars[2],chars[3]) : Combat.SimulateDetailed(board,chars[0],chars[1]);
            Console.WriteLine(JsonSerializer.Serialize(new { realm,build=builds[i].Name,group,mirror,tierMode,
                input=chars.Select(c => new { c.Name,c.Realm,c.GongFaName,c.UnarmedAttackFamilyId,c.Primary,
                    basic=c.EffectiveBasicAttackProfile,movement=c.MovementInput }),
                full=new { full.WinnerTeam,full.Turns,full.GlobalTick,commands=full.Economy.Count(e=>e.Event=="action_committed"),paid=full.Economy.Sum(e=>e.CostUnits),
                    ownTurns=FullTurnSummary(full,chars,group) },
                shared=Battle(chars,group,20261010+i) }));
        }
        return 0;
    }
}

// Numeric experiments on the real AP2 entries. No alternate settlement or production parameters.
static class Ap2BalanceReport
{
    const int Seed = 20261011;
    static void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
    static readonly HexCoord[] DuelPositions = { new(0,0), new(1,0) };
    static readonly HexCoord[] GroupPositions = { new(0,0), new(1,0), new(0,1), new(1,-1) };

    static object FullMetrics(Combat.GroupRoundResult battle, IReadOnlyList<Character> chars, bool group)
    {
        int size = group ? 2 : 1;
        var damage = new int[chars.Count]; var previous = chars.Select(c => c.Primary["HP"]).ToArray();
        foreach (var e in battle.Economy)
        {
            if (e.Event is "action_committed" or "move_committed")
                if (e.CostUnits <= 0 || e.ActionPointsBeforeUnits-e.ActionPointsRemainingUnits != e.CostUnits)
                    throw new InvalidOperationException("ap2_balance_nonpositive_or_mismatched_payment");
            if (e.Event == "action_committed") damage[e.ActorIndex] += Enumerable.Range(0,chars.Count)
                .Where(i => i/size != e.ActorIndex/size).Sum(i => Math.Max(0,previous[i]-e.HealthByActor[i]));
            previous = e.HealthByActor.ToArray();
        }
        bool timeout = battle.HpRatioA > 0 && battle.HpRatioB > 0;
        return new { winner = timeout ? -1 : battle.WinnerTeam, timeout, turns = battle.Turns, ticks = battle.GlobalTick,
            actors = Enumerable.Range(0,chars.Count).Select(i => {
                var events = battle.Economy.Where(e => e.ActorIndex == i).ToArray();
                var committed = events.Where(e => e.Event == "action_committed").ToArray();
                return new { actor = i, team = i/size,
                    ownTurns = events.Count(e => e.Event is "own_turn_started" or "own_turn_started_control_skip"),
                    controlSkips = events.Count(e => e.Event == "own_turn_started_control_skip"),
                    actions = committed.Length, basics = committed.Count(e => e.DefinitionRef.StartsWith("basic_",StringComparison.Ordinal)),
                    recoveries = committed.Count(e => e.DefinitionRef == "spell_julingshu"),
                    apUnits = events.Sum(e => e.CostUnits), spiritSpent = committed.Sum(e => Math.Max(0,e.SpiritBefore-e.SpiritAfter)),
                    netSpirit = (battle.Economy.LastOrDefault()?.SpiritByActor[i] ?? chars[i].Primary["MP"])-chars[i].Primary["MP"],
                    damage = damage[i], maxShouyi = events.Select(e => e.Shouyi).DefaultIfEmpty().Max(),
                    maxFudan = events.Select(e => e.Fudan).DefaultIfEmpty().Max(),
                    maxLeijie = events.Select(e => e.Leijie).DefaultIfEmpty().Max() };
            }), ownTurns = Ap2CombatReport.FullTurnSummary(battle,chars,group) };
    }

    static void Row(string matrix, string label, string realm, IReadOnlyList<Character> original, bool group, bool mirror, int sample, int limit = 200)
    {
        var chars = original.ToArray();
        if (mirror) chars = group ? new[] { chars[2],chars[3],chars[0],chars[1] } : new[] { chars[1],chars[0] };
        var board = new HexBattlefield();
        var full = group ? Combat.Simulate2v2Detailed(board,chars[0],chars[1],chars[2],chars[3],GroupPositions,limit) :
            Combat.SimulateDetailed(board,chars[0],chars[1],DuelPositions,limit);
        var shared = JsonSerializer.SerializeToElement(Ap2CombatReport.Battle(chars,group,Seed+sample,limit));
        Emit(new { matrix,label,realm,group,mirror,sample,
            input = chars.Select(c => new { c.Name,c.GongFaName,c.Realm,c.Primary,c.ArtName,c.DivineName,
                c.ProductionArtIds,fixedAbilityTierId = c.FixedTestAbilityTierId,c.HealthRecoveryPerOwnTurn,c.SpiritRecoveryPerOwnTurn,
                basic = c.EffectiveBasicAttackProfile,movement = c.MovementInput }),
            full = FullMetrics(full,chars,group),
            shared = new { winner = shared.GetProperty("winner"), timeout = shared.GetProperty("timeout"),
                turns = shared.GetProperty("turns"),ticks = shared.GetProperty("ticks"),controls = shared.GetProperty("controls"),
                actors = shared.GetProperty("actors"),ownTurns = shared.GetProperty("ownTurns"),
                paid = shared.GetProperty("trace").EnumerateArray().Sum(t => t.GetProperty("ActionPointCostUnits").GetInt32()),
                recoveries = shared.GetProperty("trace").EnumerateArray().Count(t => t.GetProperty("ProfileId").GetString() == "spell_julingshu") } });
    }

    static Character Body(string realm, bool trained = false, string weapon = "")
    {
        var c = Character.Create(trained ? "body-trained" : "body-ordinary",Character.InnateKeys.ToDictionary(k => k,_ => 8),"physical");
        c.GongFaName = trained ? "含弘光大典" : "";
        var weights = trained ? GameData.WeightsFromGongFa(c.GongFaName) : Character.InnateKeys.ToDictionary(k => k,_ => .2);
        c.ApplyGrowth(realm,"上品",weights);
        c.FinalizeStats(realm,realm == "凡人" ? 0 : realm == "练气" ? 8 : 2,"中品",weights);
        c.FixedTestAbilityTierId = realm == "凡人" ? "tier_lianqi" : GameData.TierForRealm(realm);
        if (weapon != "") c.BasicAttackProfile = GameData.ProductionAttacks[weapon];
        // Isolate basic attacks without granting new abilities. Actual growth/defenses/resources stay intact.
        return c;
    }

    internal static int Run(IReadOnlyList<Program.BuildDef> builds, bool sensitivity = false)
    {
        Combat.ResetDeterministicRandom();
        int samples = sensitivity ? 2 : 8;
        Emit(new { report = "N-COMBAT-AP2-BALANCE-01",Seed,fullStreamSeed = 20260712,samples,sensitivity,
            GameData.Rules.RuleSetVersion,GameData.Rules.SourceDigest,
            inputs = new[] { "AttackProfiles","RealmCombatGrowth","CombatEffectTierRules","CombatActionEconomyRules","Characters","Enemies" }
                .ToDictionary(n => n, n => GameData.InputHash("src/Assets/DataConfig/"+n+".csv")),
            realms = GameData.Rules.Realms,tiers = GameData.Rules.Tiers,global = GameData.Rules.Global,
            limitations = "fixed adjacent positions; full existing area/policy vs shared single-target/policy; timeout is draw, no HP-ratio win; no Unity click/animation proof" });
        foreach (string realm in new[] { "练气","筑基","金丹" })
        foreach (string mode in sensitivity ? new[] { "same-tier","locked-qi" } : new[] { "same-tier","locked-qi","art-led" })
        for (int i=0;i<builds.Count;i++) foreach (bool group in new[] { false,true })
        {
            if (sensitivity && !new[] { 0,4,8,10,17,18 }.Contains(i)) continue;
            Character Build(int n)
            {
                var c = ActionEconomyBalanceReport.Build(builds[n%builds.Count],realm);
                c.FixedTestAbilityTierId = mode == "locked-qi" ? "tier_lianqi" : GameData.TierForRealm(realm); c.AssignArts();
                if (mode == "art-led") c.DivineName = "";
                return c;
            }
            var chars = group ? new[] { Build(i),Build(i+2),Build(i+1),Build(i+3) } : new[] { Build(i),Build(i+1) };
            for (int sample=0;sample<samples;sample++)
                Row("builds",builds[i].Name+":"+mode,realm,chars,group,sample%2!=0,sample);
        }
        foreach (string realm in GameData.RealmOrder)
        foreach (bool trained in new[] { false,true }) foreach (string weapon in new[] { "","basic_iron_sword","basic_iron_blade" })
        foreach (bool group in new[] { false,true }) for (int sample=0;sample<samples;sample++)
        {
            var chars = group ? new[] { Body(realm,trained,weapon),Body(realm),Body(realm),Body(realm,true) } :
                new[] { Body(realm,trained,weapon),Body(realm) };
            Row("body",(trained ? "trained" : "ordinary")+":"+weapon,realm,chars,group,sample%2!=0,sample);
        }
        foreach (string realm in new[] { "筑基","金丹" }) foreach (string weapon in new[] { "basic_iron_sword","basic_iron_blade" })
        foreach (bool group in new[] { false,true }) for (int sample=0;sample<samples;sample++)
        {
            Character Reference()
            {
                var c = Body(realm,true); var p = GameData.ProductionAttacks[weapon];
                string id = "basic_fixture_tier_reference_"+weapon+"_"+GameData.TierForRealm(realm);
                int cost = CombatRuleSet.CeilRatio(GameData.Rules.GetTier(GameData.TierForRealm(realm)).ActionPointReferenceUnits,
                    p.Cost.ActionPointCostUnits,GameData.Rules.GetTier("tier_lianqi").ActionPointReferenceUnits);
                c.BasicAttackProfile = p with { Name = id, AbilityTierId = GameData.TierForRealm(realm),
                    Cost = p.Cost with { DefinitionRef = id, Version = "fixture:AP2-tier-reference",ActionPointCostUnits = cost,MinimumNetCostUnits = cost } };
                return c;
            }
            Row("reference-weapon","EXPERIMENT_ONLY:"+weapon,realm,group ? new[] { Reference(),Body(realm),Body(realm,true),Body(realm) } :
                new[] { Reference(),Body(realm,true) },group,sample%2!=0,sample);
        }
        foreach (string realm in new[] { "练气","筑基","金丹" }) foreach (bool group in new[] { false,true })
        for (int sample=0;sample<samples;sample++)
        {
            Character Recovery()
            {
                var c = Body(realm); c.Primary["HP"] *= 20;
                c.EquipProductionArts("spell_guanshiji","spell_julingshu"); return c;
            }
            Character Control()
            {
                var c = ActionEconomyBalanceReport.Build(builds.Single(b => b.GongFaName == "秋水游心经"),realm);
                c.Primary["HP"] *= 20; return c;
            }
            Row("long-war","production-recovery-vs-water",realm,group ? new[] { Recovery(),Recovery(),Control(),Control() } :
                new[] { Recovery(),Control() },group,sample%2!=0,sample,400);
        }
        foreach (bool group in new[] { false,true }) for (int sample=0;sample<samples;sample++)
            Row("stone","formal-input-vs-ordinary","练气",group ? new[] { Body("练气"),Body("练气",true),
                Character.FromProduction(true,"enemy_shijiahou"),Character.FromProduction(true,"enemy_shijiahou") } :
                new[] { Body("练气"),Character.FromProduction(true,"enemy_shijiahou") },group,sample%2!=0,sample);
        foreach (string highRealm in new[] { "筑基","金丹" }) for (int sample=0;sample<samples;sample++)
            Row("cross-realm","water-status","练气→"+highRealm,new[] {
                ActionEconomyBalanceReport.Build(builds[8],"练气"),ActionEconomyBalanceReport.Build(builds[8],highRealm) },
                false,sample%2!=0,sample);
        Emit(new { result = "AP2_BALANCE_MATRIX_COMPLETE",GameData.Rules.SourceDigest });
        return 0;
    }
}
