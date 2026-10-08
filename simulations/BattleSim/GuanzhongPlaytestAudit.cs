using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Spatial;
using UnityHexCoord = TianZhang.Spatial.HexCoord;

namespace BattleSim;

// Numeric audit adapter only. Damage, costs, qualification, cooldowns and CT use production code.
internal static class GuanzhongPlaytestAudit
{
    const string Attack = "spell_guanshiji", Recovery = "spell_julingshu";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    sealed record Inputs(JsonElement Evidence, CombatAttackProfile[] Profiles, string Root, int RootBone, string Hash);
    sealed record RunResult(int Seed, string Strategy, string Outcome, int Actions, int PlayerActions,
        int EnemyActions, int AttackCasts, int RecoveryCasts, int PlayerHealth, int PlayerSpirit,
        int EnemyHealth, int EnemySpirit, int Tick, int RandomDraws);

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length < 3 || args[1] != "--input" || args.Length % 2 != 1)
                throw new ArgumentException("Usage: --guanzhong-playtest --input <json> [--candidate-rootbone <8..18>] [--seed-start <integer>]");
            int? candidate = null;
            int seedStart = 0;
            for (int i = 3; i < args.Length; i += 2)
            {
                int value = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
                if (args[i] == "--candidate-rootbone" && candidate == null && value >= 8 && value <= 18) candidate = value;
                else if (args[i] == "--seed-start" && value >= 0 && value <= int.MaxValue - 200) seedStart = value;
                else throw new ArgumentException("Unknown or invalid audit option: " + args[i]);
            }
            Inputs input = LoadProductionInputs(Path.GetFullPath(args[2]), candidate);
            string[] checks = VerifyRejectedCommands(input);
            var runs = new List<RunResult>();
            foreach (string strategy in new[] { "basic", "attack-until-empty", "attack-with-recovery" })
                for (int seed = seedStart; seed < seedStart + 200; seed++) runs.Add(RunSeed(input, seed, strategy));
            var summary = runs.GroupBy(r => r.Strategy).Select(group => new
            {
                strategy = group.Key, wins = group.Count(r => r.Outcome == "Victory"), samples = group.Count(),
                medianActions = Median(group.Select(r => r.Actions)),
                medianPlayerActions = Median(group.Select(r => r.PlayerActions)),
                medianWinningHealth = Median(group.Where(r => r.Outcome == "Victory").Select(r => r.PlayerHealth)),
                minimumWinningHealth = group.Where(r => r.Outcome == "Victory").Select(r => r.PlayerHealth).DefaultIfEmpty(0).Min(),
                medianRemainingSpirit = Median(group.Select(r => r.PlayerSpirit)),
                recoveryCasts = group.Sum(r => r.RecoveryCasts)
            }).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                mode = candidate.HasValue ? "candidate-clone" : "production", rootBone = input.RootBone,
                inputSha256 = input.Hash, seedStart, seedCount = 200,
                spatialScope = "Stationary adjacent (0,0)/(1,0), original Facing=0/0; no movement or map simulation; player back attack follows production snapshot.",
                profilesSha256 = Hash(File.ReadAllText(Path.Combine(input.Root, "src/Assets/DataConfig/AttackProfiles.csv"))),
                checks, summary, runs
            }, Json));
            return runs.Any(r => r.Outcome == "Ongoing") ? 1 : 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.ToString()); return 1; }
    }

    static Inputs LoadProductionInputs(string path, int? candidate)
    {
        string text = File.ReadAllText(path);
        JsonElement evidence = JsonDocument.Parse(text).RootElement.Clone();
        string root = Path.GetDirectoryName(path);
        while (root != null && !File.Exists(Path.Combine(root, "src/Assets/DataConfig/AttackProfiles.csv"))) root = Path.GetDirectoryName(root);
        Require(root != null, "Input must be inside its source worktree.");
        Require(evidence.GetProperty("innate").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(new[] { 8, 8, 8, 8, 8 }), "Not ordinary balanced input.");
        Require(S(evidence, "enemyId") == "enemy_shijiahou" && F(evidence, "environmentMultiplier") == 1, "Wrong enemy/environment.");
        var enemyRow = ReadRows(Path.Combine(root, "src/Assets/DataConfig/Enemies.csv")).Single(r => r["name"] == "enemy_shijiahou");
        JsonElement character = evidence.GetProperty("enemyCharacter");
        foreach (string field in new[] { "rootBone", "physique", "spirit", "mind", "reaction", "talent", "realmMultiplier", "blockRate", "blockReduction", "soulShieldRate", "soulShieldReduction", "dodgeRate", "critRate", "critDamage", "hitRateBonus" })
        {
            float expected = field == "rootBone" && candidate.HasValue ? candidate.Value : Parse(enemyRow[field]);
            Require(Math.Abs(F(character, field) - expected) < 0.00001f, "Stale enemy input: " + field);
        }
        foreach (string side in new[] { "player", "enemy" })
        {
            JsonElement value = evidence.GetProperty(side), source = evidence.GetProperty(side + "Character");
            Require(I(value, "maximumHealth") == I(source, "maximumHealth") && I(value, "currentHealth") == I(value, "maximumHealth"), "Noninitial HP: " + side);
            Require(I(value, "maximumSpirit") == I(source, "maximumSpirit") && I(value, "currentSpirit") == I(value, "maximumSpirit"), "Noninitial MP: " + side);
            Require(I(value, "rawSpirit") == I(source, "spirit"), "Raw spirit mismatch.");
            Require(I(value, "q") == (side == "player" ? 0 : 1) && I(value, "r") == 0, "Unexpected numeric start.");
        }
        JsonElement player = evidence.GetProperty("player");
        Require(I(player, "maximumHealth") == 571 && I(player, "maximumSpirit") == 180 && I(player, "physicalAttack") == 60 && I(player, "physicalDefense") == 42, "Player delivery changed.");
        var profiles = ReadRows(Path.Combine(root, "src/Assets/DataConfig/AttackProfiles.csv")).Select(row =>
        {
            string id = row["attackProfileId"], realm = row["realmRequirementId"], element = row["elementRequirementId"];
            bool basic = row["profileKind"] == "basic";
            CombatAttackEffect effect = row["effectType"] switch { "physical" => CombatAttackEffect.Physical, "restore_spirit" => CombatAttackEffect.RestoreSpirit, _ => throw new InvalidDataException("Unsupported audit effect.") };
            float Number(string key, float empty = 0) => string.IsNullOrEmpty(row[key]) ? empty : Parse(row[key]);
            return new CombatAttackProfile(id, basic ? CombatAttackKind.Basic : CombatAttackKind.Art, effect,
                (int)Number("minCastRange"), (int)Number("maxCastRange"),
                RequireAttackCostInt(row, "actionPointCostUnits"), RequireAttackCostInt(row, "minimumNetCostUnits"),
                RequireAttackCostInt(row, "ctPenalty"), RequireAttackCostInt(row, "reuseCooldownTicks"),
                Number("physicalDamageMultiplier"), Number("soulDamageMultiplier"),
                (int)Number("healAmount"), (int)Number("resourceCost"), row["damageElementId"],
                Number("defensePenetration"), Number("spiritRecoveryMultiplier"), (int)Number("minimumSpiritRecovery"),
                (int)Number("otherTargetResourceCost"), Number("otherTargetRecoveryMultiplier", 1),
                basic ? null : (stage, rootElement, grade) => AbilityRequirementPolicy.IsSatisfied(stage, rootElement, grade, realm, element));
        }).ToArray();
        Require(profiles.Select(p => p.Id).Order().SequenceEqual(new[] { "basic_unarmed", Attack, Recovery }.Order()), "Unexpected production profiles.");
        return new Inputs(evidence, profiles, root, I(character, "rootBone"), Hash(text));
    }

    static CombatantSnapshot Snapshot(Inputs input, string side)
    {
        JsonElement v = input.Evidence.GetProperty(side), c = input.Evidence.GetProperty(side + "Character");
        string[] List(string field) => v.GetProperty(field).EnumerateArray().Select(x => x.GetString()).ToArray();
        var actor = new CombatantSnapshot(S(v, "id"), side == "player" ? CombatTeam.Player : CombatTeam.Enemy,
            new UnityHexCoord(I(v, "q"), I(v, "r")), I(v, "speed"), I(v, "maximumHealth"), I(v, "currentHealth"),
            I(v, "physicalAttack"), I(v, "soulAttack"), I(v, "physicalDefense"), I(v, "soulDefense"), I(v, "actionPointMaximumUnits"), I(v, "movementEfficiencyMilliUnits"), F(v, "realmMultiplier"), List("equippedArts"), List("knownArts"), basicAttackProfileId: S(v, "basicAttackProfileId"),
            rawSpirit: I(v, "rawSpirit"), realmStage: S(v, "realmStage"), visibleRootElement: S(v, "visibleRootElement"), visibleRootGrade: S(v, "visibleRootGrade"))
        {
            BlockRate = F(c, "blockRate"), BlockReduction = F(c, "blockReduction"), SoulShieldRate = F(c, "soulShieldRate"),
            SoulShieldReduction = F(c, "soulShieldReduction"), DodgeRate = F(c, "dodgeRate"), CriticalRate = F(c, "critRate"),
            CriticalDamage = F(c, "critDamage"), HitRateBonus = F(c, "hitRateBonus"), GongFaId = S(c, "gongFaId"),
            GongFaElement = CombatElementFacts.ResolveGongFaElement(S(c, "gongFaId"))
        };
        actor.SetSpirit(I(v, "maximumSpirit"), I(v, "currentSpirit"));
        return actor;
    }

    static CombatSession Session(Inputs input) => new(new[] { Snapshot(input, "player"), Snapshot(input, "enemy") }, input.Profiles, new AuditSpace(), new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) });

    static RunResult RunSeed(Inputs input, int seed, string strategy)
    {
        CombatSession session = Session(input);
        session.Combatants.TryGet("player", out var player); session.Combatants.TryGet("enemy", out var enemy);
        var service = new CombatCommandService(); var random = new Random(seed);
        int actions = 0, playerActions = 0, enemyActions = 0, attacks = 0, recoveries = 0, draws = 0;
        while (!session.IsFinished && actions < 300)
        {
            var advance = service.AdvanceUntilAction(session);
            Require(advance.HasActor, "No action available.");
            bool isPlayer = advance.ActorId == player.Id;
            var actor = isPlayer ? player : enemy; var target = isPlayer ? enemy : player;
            var command = new CombatCommand(CombatCommandKind.BasicAttack, actor.Id, target.Id, actor.BasicAttackProfileId);
            if (isPlayer && strategy != "basic")
            {
                var art = new CombatCommand(CombatCommandKind.Art, player.Id, enemy.Id, Attack);
                var legality = service.Validate(session, art);
                if (legality.Succeeded) command = art;
                else if (strategy == "attack-with-recovery" && legality.RejectionReason == "spirit_insufficient")
                {
                    var recovery = new CombatCommand(CombatCommandKind.Art, player.Id, player.Id, Recovery);
                    if (service.Validate(session, recovery).Succeeded) command = recovery;
                }
            }
            CombatActionResult result = Execute(session, command, service, random, ref draws);
            Require(result.Succeeded, result.RejectionReason);
            actions++; if (isPlayer) playerActions++; else enemyActions++;
            if (command.ProfileId == Attack) attacks++;
            if (command.ProfileId == Recovery) recoveries++;
            if (session.HasCommittedBenefit)
                Require(service.Execute(session, new CombatCommand(CombatCommandKind.EndTurn, actor.Id)).Succeeded, "EndTurn failed.");
        }
        return new RunResult(seed, strategy, new CombatResultBuilder().Build(session).Outcome.ToString(), actions,
            playerActions, enemyActions, attacks, recoveries, player.CurrentHealth, player.CurrentSpirit,
            enemy.CurrentHealth, enemy.CurrentSpirit, session.TurnScheduler.CurrentTick, draws);
    }

    static CombatActionResult Execute(CombatSession session, CombatCommand command, CombatCommandService service, Random random, ref int draws)
    {
        var validation = service.Validate(session, command);
        if (!validation.Succeeded) return validation;
        if (session.TryGetProfile(command.ProfileId, out var profile) && profile.Effect != CombatAttackEffect.RestoreSpirit)
        {
            var rolls = new CombatResolutionRolls((float)(random.NextDouble() * 100), (float)(random.NextDouble() * 100),
                (float)(random.NextDouble() * 100), (float)(random.NextDouble() * 100));
            draws += 4;
            command = new CombatCommand(command.Kind, command.ActorId, command.TargetId, command.ProfileId, rolls);
        }
        return service.Execute(session, command);
    }

    static string[] VerifyRejectedCommands(Inputs input)
    {
        var checks = new List<string>(); var service = new CombatCommandService();
        CombatSession Ready() { var s = Session(input); Require(service.AdvanceUntilAction(s).ActorId == "player", "Player first turn changed."); return s; }
        foreach (string scenario in new[] { "attack-no-mp", "other-recovery-no-mp", "range", "dead-actor", "dead-target" })
        {
            var s = Ready(); s.Combatants.TryGet("player", out var p); s.Combatants.TryGet("enemy", out var e);
            string profile = scenario == "other-recovery-no-mp" ? Recovery : Attack;
            if (scenario.EndsWith("no-mp")) p.SetSpirit(p.MaximumSpirit, 0);
            if (scenario == "range") e.SetPosition(new UnityHexCoord(3, 0));
            if (scenario == "dead-actor") p.ReceiveDamage(p.CurrentHealth);
            if (scenario == "dead-target") e.ReceiveDamage(e.CurrentHealth);
            string before = State(s); int draws = 0;
            var result = Execute(s, new CombatCommand(CombatCommandKind.Art, p.Id, e.Id, profile), service, new Random(1), ref draws);
            string reason = scenario switch {
                "attack-no-mp" or "other-recovery-no-mp" => "spirit_insufficient",
                "range" => "above_max_range", "dead-actor" => "combat_session_actor_invalid",
                "dead-target" => "combat_session_finished", _ => throw new InvalidOperationException() };
            Require(!result.Succeeded && result.RejectionReason == reason && State(s) == before && draws == 0, "Rejected command mismatch: " + scenario);
            checks.Add(scenario + ":" + result.RejectionReason + ":unchanged");
        }
        foreach (string scenario in new[] { "self-empty", "self-full", "self-cap", "other" })
        {
            var s = Ready(); s.Combatants.TryGet("player", out var p); s.Combatants.TryGet("enemy", out var e);
            var profile = input.Profiles.Single(x => x.Id == Recovery);
            bool self = scenario != "other";
            p.SetSpirit(p.MaximumSpirit, !self ? profile.OtherTargetSpiritCost : scenario == "self-empty" ? 0 : scenario == "self-cap" ? p.MaximumSpirit - 1 : p.MaximumSpirit);
            if (!self) e.SetSpirit(e.MaximumSpirit, 0);
            var target = self ? p : e; int before = target.CurrentSpirit, actorBefore = p.CurrentSpirit, draws = 0;
            int request = (int)Math.Floor(Math.Max(profile.MinimumSpiritRecovery, p.RawSpirit * (double)profile.SpiritRecoveryMultiplier) * (self ? 1 : profile.OtherTargetSpiritRecoveryMultiplier));
            var result = Execute(s, new CombatCommand(CombatCommandKind.Art, p.Id, target.Id, Recovery), service, new Random(1), ref draws);
            int expected = Math.Min(request, target.MaximumSpirit - before);
            Require(result.Succeeded && draws == 0 && result.SpiritRecoveries.Single().ActualRecovery == expected &&
                result.SpiritSpent == profile.GetSpiritCost(self) && target.CurrentSpirit == before + expected &&
                p.CurrentSpirit == actorBefore - profile.GetSpiritCost(self) + (self ? expected : 0) &&
                s.PendingCtPenalty == profile.CtPenalty &&
                p.GetCooldown(Recovery) == profile.ReuseCooldownTicks, "Recovery mismatch: " + scenario);
            checks.Add(scenario + ":actual=" + expected + ":cost=" + result.SpiritSpent +
                ":ct=" + profile.CtPenalty + ":reuse=" + profile.ReuseCooldownTicks + ":rng=0");
        }
        {
            var s = Ready(); s.Combatants.TryGet("player", out var p); s.Combatants.TryGet("enemy", out var e);
            var attack = input.Profiles.Single(x => x.Id == Attack);
            p.SetSpirit(p.MaximumSpirit, attack.SpiritCost);
            int draws = 0;
            Require(Execute(s, new CombatCommand(CombatCommandKind.Art, p.Id, e.Id, Attack), service, new Random(1), ref draws).Succeeded &&
                p.CurrentSpirit == 0 && draws == 4 && s.PendingCtPenalty == attack.CtPenalty &&
                p.GetCooldown(Attack) == attack.ReuseCooldownTicks,
                "Legal attack did not exhaust exact resource.");
            // Isolated resource control: enemy guards so the next player command can prove exhaustion.
            Require(service.Execute(s, new CombatCommand(CombatCommandKind.EndTurn, p.Id)).Succeeded, "Controlled turn end failed.");
            while (service.AdvanceUntilAction(s).ActorId != p.Id)
            {
                Require(service.Execute(s, new CombatCommand(CombatCommandKind.Guard, e.Id)).Succeeded, "Controlled guard failed.");
                Require(service.Execute(s, new CombatCommand(CombatCommandKind.EndTurn, e.Id)).Succeeded, "Controlled guard end failed.");
            }
            string before = State(s);
            var failed = Execute(s, new CombatCommand(CombatCommandKind.Art, p.Id, e.Id, Attack), service, new Random(2), ref draws);
            Require(failed.RejectionReason == "spirit_insufficient" && State(s) == before && draws == 4, "Exhaustion rejection changed state.");
            Require(Execute(s, new CombatCommand(CombatCommandKind.Art, p.Id, p.Id, Recovery), service, new Random(3), ref draws).Succeeded && p.CurrentSpirit > 0 && draws == 4,
                "Recovery after real exhaustion failed.");
            checks.Add("controlled-exhaustion:paid-last-attack/rejected-next/recovered-self:rng=4");
        }
        return checks.ToArray();
    }

    static string State(CombatSession session) => JsonSerializer.Serialize(new
    {
        session.CurrentActorId, session.TurnPhase, session.PendingCtPenalty,
        turn = session.TurnScheduler.GetSnapshot(session.Combatants.All),
        actors = session.Combatants.All.Select(p => new { p.Id, p.CurrentHealth, p.CurrentSpirit, p.Position, p.ActionPointsRemainingUnits, p.Facing, p.IsGuarding, p.Cooldowns })
    });
    static IEnumerable<Dictionary<string, string>> ReadRows(string path)
    {
        string[] lines = CsvTableReader.ReadRequired(path), headers = CsvTableReader.FindHeader(lines);
        foreach (string line in lines.Skip(CsvTableReader.FindHeaderIndex(lines) + 1).Where(x => !string.IsNullOrWhiteSpace(x) && !x.TrimStart().StartsWith("#")))
        {
            string[] values = CsvTableReader.ParseRow(line);
            Require(values.Length == headers.Length, "CSV width mismatch: " + path);
            yield return headers.Select((h, i) => (h, values[i].Trim())).ToDictionary(x => x.h, x => x.Item2);
        }
    }
    static string S(JsonElement value, string field) => value.GetProperty(field).GetString() ?? "";
    static int I(JsonElement value, string field) => value.GetProperty(field).GetInt32();
    static float F(JsonElement value, string field) => value.GetProperty(field).GetSingle();
    static float Parse(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    /// <summary>
    /// 统一行动力四列必须显式存在且为合法整数：空值不当 0，也不回填已删除的旧 CSV 列。
    /// 直接构造 CombatAttackProfile 会再按合同复核范围（正费、下限、CT 上界与非负重用刻）。
    /// </summary>
    static int RequireAttackCostInt(IReadOnlyDictionary<string, string> row, string field)
    {
        Require(row.TryGetValue(field, out string raw), "Missing production column: " + field);
        Require(int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed),
            "Non-integer production value: " + field);
        return parsed;
    }
    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")))).ToLowerInvariant();
    static double Median(IEnumerable<int> values) { var a = values.Order().ToArray(); return a.Length == 0 ? 0 : (a[(a.Length - 1) / 2] + a[a.Length / 2]) / 2d; }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }

    sealed class AuditSpace : ICombatSpatialQuery
    {
        readonly SpatialQueryBoard board;
        public AuditSpace()
        {
            var cells = new Dictionary<UnityHexCoord, SpatialCellRules>(); var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            for (int q = 0; q <= 3; q++) cells.Add(new UnityHexCoord(q, 0), new SpatialCellRules(0, false, false, false, 0));
            for (int q = 0; q < 3; q++)
            {
                edges.Add(new SpatialDirectedEdge(new UnityHexCoord(q, 0), new UnityHexCoord(q + 1, 0)), new SpatialEdgeRules(1, true, true));
                edges.Add(new SpatialDirectedEdge(new UnityHexCoord(q + 1, 0), new UnityHexCoord(q, 0)), new SpatialEdgeRules(1, true, true));
            }
            board = new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(1, 3));
        }
        public CombatRangeQueryResult QueryRange(UnityHexCoord source, UnityHexCoord target, int minimumRange, int maximumRange)
        { var r = board.QueryRangeEntry(source, target, minimumRange, maximumRange, TianZhang.Spatial.SpatialQueryKind.Attack, true); return new CombatRangeQueryResult(r.IsInRange, r.Reason); }
        public CombatMovementQueryResult QueryMovement(UnityHexCoord source, UnityHexCoord destination, int actionPointBudgetUnits, int movementEfficiencyMilliUnits, IReadOnlyCollection<UnityHexCoord> occupied) => throw new NotSupportedException("No map movement in numeric audit.");
        public IReadOnlyDictionary<UnityHexCoord, int> FindReachable(UnityHexCoord source, int actionPointBudgetUnits, int movementEfficiencyMilliUnits, IReadOnlyCollection<UnityHexCoord> occupied) => throw new NotSupportedException("No map movement in numeric audit.");
    }
}
