using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.World;
using TianZhang.Combat.Turns;

namespace BattleSim;

static class Combat
{
    const int DeterministicRandomSeed = 20260712;
    const string SettlementEvidenceUnavailableReason = "settlement_evidence_unavailable";
    internal const int DuelTurnLimit = 100;
    static readonly HexBattlefield DefaultDuelBattlefield = new();
    static Random Rng = new(DeterministicRandomSeed);
    public const double BaseCritMultiplier = 1.5;
    internal static readonly HexCoord InitialPositionA = new(0, 0);
    internal static readonly HexCoord InitialPositionB = new(6, 0);
    internal static readonly IReadOnlyList<HexCoord> InitialGroupPositions = Array.AsReadOnly(new[]
    {
        new HexCoord(0, 0),
        new HexCoord(0, 1),
        new HexCoord(6, 0),
        new HexCoord(6, -1),
    });
    internal const int DivineAction = 0;
    internal const int KuxingAction = 1;
    internal const int ArtAction = 2;
    internal const int BasicAction = 3;
    internal readonly record struct ActionPositionResult(HexCoord Position, bool CanAttack);
    internal readonly record struct GroupActionPlan(
        ActionPositionResult Position,
        GameData.AreaTargetingResult AreaTargeting);
    internal enum GroupActionSettlementEvidenceStatus
    {
        Resolved,
        Unavailable,
    }
    internal readonly record struct GroupActionSettlementEvidence(
        GroupActionSettlementEvidenceStatus Status,
        IReadOnlyList<int> ResolvedKillTargetIndexes,
        string UnavailableReason)
    {
        internal static GroupActionSettlementEvidence Resolved(IEnumerable<int> resolvedKillTargetIndexes)
        {
            if (resolvedKillTargetIndexes == null)
                throw new ArgumentNullException(nameof(resolvedKillTargetIndexes));
            return new GroupActionSettlementEvidence(
                GroupActionSettlementEvidenceStatus.Resolved,
                NormalizeTargetIndexes(resolvedKillTargetIndexes),
                "");
        }

        internal static GroupActionSettlementEvidence Unavailable() => new(
            GroupActionSettlementEvidenceStatus.Unavailable,
            Array.Empty<int>(),
            SettlementEvidenceUnavailableReason);
    }
    internal readonly record struct GroupActionSettlementCandidate(
        int Turn,
        int ActorIndex,
        int PrimaryTargetIndex,
        IReadOnlyList<int> LegalHitTargetIndexes,
        int InputOrder,
        GroupActionSettlementEvidence SettlementEvidence);
    internal readonly record struct GroupTargetCandidate(
        int Index,
        int Team,
        int HP,
        bool IsAlive,
        bool IsLegal,
        GroupActionSettlementCandidate? SettlementCandidate = null);
    internal readonly record struct GroupTargetPriority(
        int ConfirmedKillTargetCount,
        int LegalHitTargetCount,
        int PrimaryTargetHP,
        int CandidateInputOrder,
        GroupActionSettlementEvidenceStatus SettlementEvidenceStatus,
        string SettlementEvidenceReason);
    internal readonly record struct GroupTargetSelection(
        int TargetIndex,
        string Reason,
        GroupTargetPriority? Priority = null);
    internal sealed record GroupActionObservation(
        int Turn,
        int ActorIndex,
        HexCoord StartPosition,
        HexCoord EndPosition,
        IReadOnlyList<HexCoord> ReachablePositions,
        int TargetIndex,
        string TargetSelectionReason,
        GroupTargetPriority? TargetSelectionPriority,
        string InactionReason,
        HexCoord? AreaCenter,
        IReadOnlyList<int> AreaHitTargetIndexes,
        string AreaRejectionReason,
        IReadOnlyList<HexCoord> PositionsAfterAction);
    internal sealed record GroupRoundResult(
        int WinnerTeam,
        int Turns,
        IReadOnlyList<HexCoord> InitialPositions,
        IReadOnlyList<GroupActionObservation> Actions,
        double HpRatioA,
        double HpRatioB,
        int GlobalTick,
        IReadOnlyList<ActionEconomyObservation> Economy);

    internal sealed record ActionEconomyObservation(
        int Turn, int ActorIndex, int GlobalTick, string Event, string DefinitionRef, string AbilityInstanceId,
        int ActionPointsBeforeUnits, int ActionPointsRemainingUnits, int CostUnits, int ReductionUnits, int RefundUnits,
        int SpiritBefore, int SpiritAfter, int HealthBefore, int HealthAfter, int PendingCtPenalty,
        float ChargeTime, float NextThreshold, IReadOnlyDictionary<string, int> Cooldowns,
        int OwnTurns, int CommittedActions, int Shouyi, int Fudan, int Leijie, bool Stunned, string InputVersion,
        IReadOnlyList<int> HealthByActor, IReadOnlyList<int> SpiritByActor);

    internal static void ResetDeterministicRandom() => Rng = new Random(DeterministicRandomSeed);

    static bool CanAttack(HexBattlefield battlefield, HexCoord attacker, HexCoord defender, int minRange, int maxRange)
    {
        return battlefield
            .QueryRange(
                attacker,
                defender,
                minRange,
                maxRange,
                SpatialQueryKind.Attack,
                requireLineOfSight: true)
            .IsInRange;
    }

    internal static ActionPositionResult ResolveActionPosition(
        HexBattlefield battlefield,
        HexCoord attacker,
        HexCoord defender,
        int movementBudget,
        int minRange,
        int maxRange,
        bool preventsVoluntaryMovement,
        IReadOnlyCollection<HexCoord> occupied = null)
    {
        int availableMovement = preventsVoluntaryMovement ? 0 : movementBudget;
        var position = battlefield.FindAttackPosition(
            attacker, defender, availableMovement, minRange, maxRange, occupied);
        return new ActionPositionResult(position, CanAttack(battlefield, position, defender, minRange, maxRange));
    }

    internal static GroupTargetSelection SelectGroupTarget(
        int actorTeam,
        IReadOnlyList<GroupTargetCandidate> candidates)
    {
        var legalCandidates = candidates
            .Where(candidate => candidate.Team != actorTeam && candidate.IsAlive && candidate.IsLegal)
            .ToArray();
        if (legalCandidates.Length == 0)
            return new GroupTargetSelection(-1, "no_legal_target");

        if (legalCandidates[0].SettlementCandidate is not GroupActionSettlementCandidate actionInstance)
            throw new InvalidOperationException("A legal group target candidate requires a settlement candidate.");
        if (legalCandidates.Any(candidate => candidate.SettlementCandidate is not GroupActionSettlementCandidate settlementCandidate ||
                                            settlementCandidate.Turn != actionInstance.Turn ||
                                            settlementCandidate.ActorIndex != actionInstance.ActorIndex))
            throw new InvalidOperationException("Group target selection only compares candidates from one action instance.");

        var selected = legalCandidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Priority = CreateGroupTargetPriority(candidate),
            })
            .OrderByDescending(candidate => candidate.Priority.ConfirmedKillTargetCount)
            .ThenByDescending(candidate => candidate.Priority.LegalHitTargetCount)
            .ThenBy(candidate => candidate.Priority.PrimaryTargetHP)
            .ThenBy(candidate => candidate.Priority.CandidateInputOrder)
            .FirstOrDefault();
        return selected != null
            ? new GroupTargetSelection(
                selected.Candidate.Index,
                "selected_kill_count_then_legal_hit_count_then_lowest_hp_then_input_order",
                selected.Priority)
            : throw new InvalidOperationException("A non-empty legal candidate set must select one candidate.");
    }

    static GroupTargetPriority CreateGroupTargetPriority(GroupTargetCandidate candidate)
    {
        if (candidate.SettlementCandidate is not GroupActionSettlementCandidate settlementCandidate)
            throw new InvalidOperationException("A legal group target candidate requires a settlement candidate.");
        if (settlementCandidate.PrimaryTargetIndex != candidate.Index)
            throw new InvalidOperationException("The settlement candidate primary target must match its group target candidate.");
        if (settlementCandidate.InputOrder < 0)
            throw new InvalidOperationException("The settlement candidate input order must be non-negative.");

        var evidence = settlementCandidate.SettlementEvidence;
        int confirmedKillTargetCount = evidence.Status switch
        {
            GroupActionSettlementEvidenceStatus.Resolved when string.IsNullOrEmpty(evidence.UnavailableReason)
                => evidence.ResolvedKillTargetIndexes.Count,
            GroupActionSettlementEvidenceStatus.Unavailable when
                evidence.ResolvedKillTargetIndexes.Count == 0 &&
                evidence.UnavailableReason == SettlementEvidenceUnavailableReason
                => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(evidence.Status)),
        };
        string evidenceReason = evidence.Status == GroupActionSettlementEvidenceStatus.Unavailable
            ? evidence.UnavailableReason
            : "";
        return new GroupTargetPriority(
            confirmedKillTargetCount,
            settlementCandidate.LegalHitTargetIndexes.Count,
            candidate.HP,
            settlementCandidate.InputOrder,
            evidence.Status,
            evidenceReason);
    }

    static IReadOnlyList<int> NormalizeTargetIndexes(IEnumerable<int> targetIndexes) =>
        targetIndexes.Distinct().OrderBy(index => index).ToArray();

    internal static int SelectAction(
        HexBattlefield battlefield, HexCoord attacker, HexCoord defender,
        bool divineReady, int divineMinRange, int divineMaxRange,
        bool kuxingReady,
        bool artReady, int artMinRange, int artMaxRange,
        GameData.AttackProfile basicAttack,
        out int minRange, out int maxRange)
    {
        var actions = new[]
        {
            (Kind: DivineAction, Ready: divineReady, MinRange: divineMinRange, MaxRange: divineMaxRange),
            (Kind: KuxingAction, Ready: kuxingReady, MinRange: 1, MaxRange: 1),
            (Kind: ArtAction, Ready: artReady, MinRange: artMinRange, MaxRange: artMaxRange),
            (Kind: BasicAction, Ready: basicAttack != null, MinRange: basicAttack?.MinRange ?? 0, MaxRange: basicAttack?.MaxRange ?? 0),
        };
        var selected = actions.FirstOrDefault(action => action.Ready && CanAttack(battlefield, attacker, defender, action.MinRange, action.MaxRange));
        if (!selected.Ready)
            selected = actions.FirstOrDefault(action => action.Ready);
        if (!selected.Ready) { minRange = maxRange = 0; return -1; }
        minRange = selected.MinRange;
        maxRange = selected.MaxRange;
        return selected.Kind;
    }

    public static double GetCritMultiplier(double critDamage, double elementCritDamageBonus = 0)
    {
        return BaseCritMultiplier + (critDamage + elementCritDamageBonus) / 100;
    }

    // 命中率派生：Clamp(100 + 命中率 - 闪避率, 5, 100)。
    internal static double EffectiveHitRate(double hitRateBonus, double dodgeRate) =>
        Math.Clamp(100 + hitRateBonus - dodgeRate, 5.0, 100.0);

    // 格挡/魂盾减伤倍率：1 - 减伤率/100。
    internal static double ShieldReductionMultiplier(double shieldReduction) =>
        1 - shieldReduction / 100;

    // v4.1: 合并攻击结算（含穿透）
    internal static int Dmg(int atk, int def, double resist, double defPen = 0, double mult = 1.0)
    {
        int effectiveDef = (int)Math.Round(def * (1 - defPen / 100));
        double df = atk / (double)(atk + effectiveDef);
        return (int)Math.Max(0, Math.Round(atk * df * (1 - resist / 100.0) * mult));
    }

    // N-COMBAT-PARITY-01：四命名概率样本（[0,100) 命中/暴击/格挡/魂盾）。
    internal readonly record struct CombatSettlementSamples(
        double HitPercent,
        double CriticalPercent,
        double BlockPercent,
        double SoulShieldPercent)
    {
        internal static CombatSettlementSamples FromRandom(Random rng) => new(
            rng.NextDouble() * 100,
            rng.NextDouble() * 100,
            rng.NextDouble() * 100,
            rng.NextDouble() * 100);
    }

    internal readonly record struct DefenseSettlementResult(
        bool Hit,
        bool Critical,
        bool Blocked,
        bool SoulShielded,
        int Damage);

    // 格挡/魂盾/闪避/暴击 统一结算（真实 duel/group 链从种子 Rng 取样）。
    static int ApplyDefenses(int rawDmg, Character attacker, Character defender, string atkType, bool ignoreDodge = false, bool ignoreBlock = false, double critRateBonus = 0, double critDamageBonus = 0) =>
        ResolveDefenseSettlement(
            rawDmg, attacker, defender, atkType,
            CombatSettlementSamples.FromRandom(Rng),
            ignoreDodge, ignoreBlock, critRateBonus, critDamageBonus).Damage;

    // 命中 -> 暴击 -> 格挡/魂盾 的确定性结算；duel/group 真实链与 combat-parity-v1 共用同一实现。
    internal static DefenseSettlementResult ResolveDefenseSettlement(
        int rawDmg,
        Character attacker,
        Character defender,
        string atkType,
        CombatSettlementSamples samples,
        bool ignoreDodge = false,
        bool ignoreBlock = false,
        double critRateBonus = 0,
        double critDamageBonus = 0)
    {
        bool isPhysical = atkType == "物理";

        // 命中：hitPercent <= Clamp(100 + 命中率 - 闪避率, 5, 100)。
        double hitRateBonus = attacker.Secondary.GetValueOrDefault("命中率", 0);
        double dodgeRate = defender.Secondary.GetValueOrDefault("闪避率", 0);
        bool hit = ignoreDodge || samples.HitPercent <= EffectiveHitRate(hitRateBonus, dodgeRate);
        if (!hit)
            return new DefenseSettlementResult(false, false, false, false, 0);

        int damage = rawDmg;

        // 暴击：criticalPercent < 暴击率（+ 元素加成）。
        double critRate = attacker.Secondary.GetValueOrDefault("暴击率", 0) + critRateBonus;
        bool critical = samples.CriticalPercent < critRate;
        if (critical)
            damage = (int)Math.Round(damage * GetCritMultiplier(attacker.Secondary.GetValueOrDefault("暴击伤害", 0), critDamageBonus));

        // 格挡（物理）/ 魂盾（神魂）：对应样本 < 对应率。
        double shieldRate = defender.Secondary.GetValueOrDefault(isPhysical ? "格挡率" : "魂盾率", 0);
        double shieldReduction = defender.Secondary.GetValueOrDefault(isPhysical ? "格挡减伤率" : "魂盾减伤率", 0);
        bool shielded = !ignoreBlock && (isPhysical ? samples.BlockPercent : samples.SoulShieldPercent) < shieldRate;
        if (shielded)
            damage = (int)Math.Round(damage * ShieldReductionMultiplier(shieldReduction));

        return new DefenseSettlementResult(hit, critical, isPhysical && shielded, !isPhysical && shielded, damage);
    }

    public static RuleConflictDecision ResolveGoldenCoreConflict(
        Character left,
        GoldenCoreRuntimeLedger leftRuntimeLedger,
        GoldenCoreConflictCandidateInput leftInput,
        Character right,
        GoldenCoreRuntimeLedger rightRuntimeLedger,
        GoldenCoreConflictCandidateInput rightInput,
        GoldenCoreConflictInputMode inputMode)
    {
        if (inputMode != GoldenCoreConflictInputMode.Qte && inputMode != GoldenCoreConflictInputMode.Skip)
            return RuleConflictDecision.Rejected("JD_CONFLICT_INPUT_MODE_INVALID", 0, null);

        GoldenCoreConflictCandidatePreparation leftPreparation = left.PrepareGoldenCoreConflictCandidate(leftRuntimeLedger, leftInput);
        GoldenCoreConflictCandidatePreparation rightPreparation = right.PrepareGoldenCoreConflictCandidate(rightRuntimeLedger, rightInput);
        int rejectedCandidateCount = (leftPreparation.IsEligible ? 0 : 1) + (rightPreparation.IsEligible ? 0 : 1);
        if (rejectedCandidateCount > 0)
        {
            string rejectionReason = !leftPreparation.IsEligible
                ? leftPreparation.RejectionCode
                : rightPreparation.RejectionCode;
            return RuleConflictDecision.Rejected(rejectionReason, rejectedCandidateCount, null);
        }

        RuleConflictInstance conflict = CreateGoldenCoreConflict(leftPreparation.Candidate, rightPreparation.Candidate);
        RuleConflictDecision decision = conflict.Decide(null);
        if (decision.RequiresLedgerSettlement)
        {
            ApplyGoldenCoreSettlement(leftPreparation.Candidate.RuntimeState, decision.LeftReserveSpent, decision.LeftSettlementCooldown);
            ApplyGoldenCoreSettlement(rightPreparation.Candidate.RuntimeState, decision.RightReserveSpent, decision.RightSettlementCooldown);
        }

        return decision;
    }

    static RuleConflictInstance CreateGoldenCoreConflict(GoldenCoreConflictCandidate left, GoldenCoreConflictCandidate right)
    {
        GoldenCoreConflictCandidateInput leftInput = left.Input;
        GoldenCoreConflictCandidateInput rightInput = right.Input;
        return new RuleConflictInstance(
            RuleConflictInstance.ContractVersionV1,
            "battle-sim:" + leftInput.CandidateId + ":" + rightInput.CandidateId,
            RuleConflictKind.JindanSameVariable,
            "battle-sim-jindan-conflict",
            leftInput.VariableId,
            "jindan-same-variable",
            leftInput.TargetId,
            "battle-sim",
            "battle-sim",
            "battle-sim",
            "battle-sim",
            "battle-sim",
            0,
            ToRuleConflictCandidate(left),
            ToRuleConflictCandidate(right),
            null);
    }

    static RuleConflictCandidate ToRuleConflictCandidate(GoldenCoreConflictCandidate candidate)
    {
        GoldenCoreConflictCandidateInput input = candidate.Input;
        return new RuleConflictCandidate(
            input.CandidateId,
            input.VariableId,
            input.TargetId,
            input.HasVariableAuthority,
            input.HasLegalTarget,
            PositionRank(input.PositionType),
            input.RealityAnchorRank,
            input.AlreadyPaidCost,
            input.HasActiveContinuousCarrier,
            candidate.RuntimeState.ConflictReserve,
            input.PulseCost,
            input.SettlementCooldown);
    }

    static void ApplyGoldenCoreSettlement(GoldenCoreAbilityRuntimeState runtimeState, int reserveSpent, int cooldown)
    {
        if (reserveSpent > 0 && !runtimeState.TrySpendConflictReserve(reserveSpent))
            throw new InvalidOperationException("conflict reserve changed during deterministic settlement.");
        runtimeState.StartCooldown(cooldown);
    }

    static int PositionRank(GoldenCoreSeatType positionType)
    {
        return positionType == GoldenCoreSeatType.Source
            ? 3
            : positionType == GoldenCoreSeatType.Transformation
                ? 2
                : positionType == GoldenCoreSeatType.Domain
                    ? 1
                    : 0;
    }

    public static GoldenCoreCarrierDeathResolution ResolveGoldenCoreCarrierDeath(
        Character character,
        GoldenCoreRuntimeLedger runtimeLedger,
        GoldenCoreCarrierDeathInput input) =>
        character == null
            ? GoldenCoreCarrierDeathResolution.Rejected("JD_CARRIER_DEATH_CHARACTER_UNAVAILABLE")
            : character.ResolveGoldenCoreCarrierDeath(runtimeLedger, input);

    static CombatLocalStateSnapshot CaptureCombatLocalState(
        HexCoord position,
        int hitPoints,
        int mana,
        int artCooldown,
        int divineCooldown) => new(
            $"{position.Q},{position.R}",
            "ground",
            "ground",
            hitPoints > 0 ? "alive" : "dead",
            Math.Max(0, hitPoints),
            Math.Max(0, mana),
            "none",
            "none",
            new Dictionary<string, int> { ["art"] = Math.Max(0, artCooldown), ["divine"] = Math.Max(0, divineCooldown) },
            new Dictionary<string, int>(),
            new Dictionary<string, int>());

    public static (double winsA, double winsB, double avgTurns) Simulate(Character ca, Character cb, int rounds)
    {
        if (rounds <= 0) throw new ArgumentOutOfRangeException(nameof(rounds));
        if (ca.IsDead || cb.IsDead)
            return ca.IsDead && cb.IsDead ? (50, 50, 0) : ca.IsDead ? (0, 100, 0) : (100, 0, 0);
        double winsA = 0, winsB = 0;
        int turns = 0;
        var battlefield = DefaultDuelBattlefield;
        for (int i = 0; i < rounds; i++)
        {
            var result = RunBattle(battlefield, new[] { ca, cb }, new[] { 0, 1 },
                new[] { InitialPositionA, InitialPositionB }, DuelTurnLimit, observe: false);
            if (result.WinnerTeam < 0) { winsA += .5; winsB += .5; }
            else if (result.WinnerTeam == 0) winsA++; else winsB++;
            turns += result.Turns;
        }
        return (winsA * 100 / rounds, winsB * 100 / rounds, (double)turns / rounds);
    }

    internal static GroupRoundResult SimulateDetailed(HexBattlefield battlefield, Character ca, Character cb,
        IReadOnlyList<HexCoord> positions = null, int turnLimit = DuelTurnLimit) =>
        RunBattle(battlefield, new[] { ca, cb }, new[] { 0, 1 },
            positions ?? new[] { InitialPositionA, InitialPositionB }, turnLimit, observe: true);

    public static (double winsA, double winsB, double avgTurns) Simulate2v2(
        HexBattlefield battlefield, Character ca1, Character ca2, Character cb1, Character cb2, int rounds)
    {
        if (rounds <= 0) throw new ArgumentOutOfRangeException(nameof(rounds));
        double winsA = 0, winsB = 0;
        int turns = 0;
        for (int i = 0; i < rounds; i++)
        {
            var result = RunBattle(battlefield, new[] { ca1, ca2, cb1, cb2 }, new[] { 0, 0, 1, 1 },
                InitialGroupPositions, 200, observe: false);
            if (result.WinnerTeam < 0) { winsA += .5; winsB += .5; }
            else if (result.WinnerTeam == 0) winsA++; else winsB++;
            turns += result.Turns;
        }
        return (winsA * 100 / rounds, winsB * 100 / rounds, (double)turns / rounds);
    }

    internal static GroupRoundResult Simulate2v2Detailed(
        HexBattlefield battlefield, Character ca1, Character ca2, Character cb1, Character cb2,
        IReadOnlyList<HexCoord> positions = null, int turnLimit = 200) =>
        RunBattle(battlefield, new[] { ca1, ca2, cb1, cb2 }, new[] { 0, 0, 1, 1 },
            positions ?? InitialGroupPositions, turnLimit, observe: true);

    sealed class UnitState
    {
        internal Character Char;
        internal string Id;
        internal int HP, MP, AP, Limit, PendingPenalty, OwnTurns, Actions;
        internal HexCoord Position;
        internal int Shouyi, Fudan, LeiJie, QiushuiCharges, Shield, Shishui, Chuanliu, KuxingDefReduction, Buqian;
        internal int Xuantong, XuantongAppliedOwnTurn;
        internal bool Stunned, Moved;
        internal GoldenCoreRuntimeLedger GoldenCore;
        internal CombatStateRuntime State;
        internal List<Ability> Abilities;
        // Non-shared abilities use actor+definition; declared assembly instances use their existing ledger only.
        internal readonly Dictionary<string, int> Cooldowns = new(StringComparer.Ordinal);
        internal bool IsAlive => HP > 0 && !Char.IsDead;
        internal int Cooldown(Ability ability) => SharedLedger(ability)?.Cooldown ?? Cooldowns.GetValueOrDefault(ability.Key);
        internal GoldenCoreAbilityRuntimeState SharedLedger(Ability ability) =>
            ability.SharedInstanceId.Length == 0 ? null : GoldenCore.Get(ability.SharedInstanceId);
        internal int Resource(Ability ability) => SharedLedger(ability)?.Resource ?? MP;
        internal void StartCooldown(Ability ability)
        {
            if (SharedLedger(ability) is { } ledger) ledger.StartCooldown(ability.Cost.ReuseCooldownTicks);
            else Cooldowns[ability.Key] = ability.Cost.ReuseCooldownTicks;
        }
        internal IReadOnlyDictionary<string, int> SnapshotCooldowns()
        {
            var result = new Dictionary<string, int>(Cooldowns, StringComparer.Ordinal);
            foreach (var ability in Abilities) result[ability.Key] = Cooldown(ability);
            return result;
        }
    }

    sealed record Ability(int Kind, GameData.AttackProfile Profile, GameData.ActionEconomyCost Cost,
        int SpiritCost, double DefPen, string Key, string SharedInstanceId);

    static bool HasGongFa(UnitState unit, string name, string minimumRealm) =>
        unit.Char.GongFaName == name && Array.IndexOf(GameData.RealmOrder, unit.Char.Realm) >= Array.IndexOf(GameData.RealmOrder, minimumRealm);
    static int ShouyiMaximum(UnitState unit) => unit.Char.Realm == "练气" ? 3 : 5;
    static int FudanMaximum(UnitState unit) => unit.Char.Realm == "金丹" ? 5 : 3;
    static int LeijieMaximum(UnitState unit) => unit.Char.Realm == "金丹" ? 5 : 3;

    static List<Ability> CreateAbilities(UnitState unit)
    {
        var c = unit.Char;
        var result = new List<Ability>();
        void Add(int kind, GameData.AttackProfile profile, GameData.ActionEconomyCost cost, int spirit, double pen, string shared = "")
        {
            if (cost == null) throw new InvalidOperationException("action_economy_ability_mapping_missing: " + profile.Name);
            cost.Validate();
            if (shared.Length > 0 && unit.GoldenCore == null) throw new InvalidOperationException("ability_instance_ledger_missing");
            result.Add(new(kind, profile, cost, spirit, pen, shared.Length > 0 ? shared : unit.Id + ":" + cost.DefinitionRef, shared));
        }
        if (!string.IsNullOrEmpty(c.DivineName))
            Add(DivineAction, new(c.DivineName, c.DivineType, c.DivineMult, c.DivineElement, c.DivineMinRange, c.DivineMaxRange),
                c.DivineCost, 0, c.DivineDefPen, c.DivineAbilityInstanceId);
        if (HasGongFa(unit, "苦行剑典", "练气") && c.Style == "yuqing_kuxing")
            Add(KuxingAction, new("血剑气", "物理", c.Realm switch { "金丹" => 2.2, "筑基" => 1.8, _ => 1.5 }, "", 1, 1),
                GameData.AbilityCost("KuxingAction"), 0, 0);
        if (!string.IsNullOrEmpty(c.ArtName))
            Add(ArtAction, new(c.ArtName, c.ArtType, c.ArtMult, c.ArtElement, c.ArtMinRange, c.ArtMaxRange),
                c.ArtCost, c.ArtMPCost, 0, c.ArtAbilityInstanceId);
        foreach (string id in c.ProductionArtIds)
        {
            var profile = GameData.ProductionAttacks[id];
            if (c.Realm == "凡人") throw new InvalidOperationException("production_art_qualification_invalid");
            Add(ArtAction, profile, profile.Cost, profile.SpiritCost, 0);
        }
        if (c.BasicAttackProfile != null)
            Add(BasicAction, c.BasicAttackProfile, c.BasicAttackProfile.Cost, c.BasicAttackProfile.SpiritCost, 0);
        return result;
    }

    static int SpiritCost(UnitState unit, Ability ability, bool otherTarget = false)
    {
        if (ability.Profile.Type == "恢复灵力") return otherTarget ? ability.Profile.OtherTargetSpiritCost : ability.SpiritCost;
        if (ability.Cost.DefinitionRef == "TaiyiFuxiuArt" && HasGongFa(unit, "云篆度人经", "金丹") && unit.Fudan == FudanMaximum(unit)) return 0;
        return ability.SpiritCost;
    }

    static GroupRoundResult RunBattle(HexBattlefield battlefield, Character[] characters, int[] team,
        IReadOnlyList<HexCoord> positions, int turnLimit, bool observe)
    {
        if (battlefield == null) throw new ArgumentNullException(nameof(battlefield));
        if (positions.Count != characters.Length || positions.Distinct().Count() != positions.Count || turnLimit <= 0)
            throw new ArgumentException("Battle deployment and turn limit must be explicit and valid.");
        var scheduler = new CTBEngine();
        var units = new UnitState[characters.Length];
        var actions = new List<GroupActionObservation>();
        var economy = new List<ActionEconomyObservation>();
        for (int i = 0; i < characters.Length; i++)
        {
            var c = characters[i];
            var u = units[i] = new UnitState { Char = c, Id = "actor-" + i, HP = c.IsDead ? 0 : c.Primary["HP"], MP = c.Primary["MP"], Position = positions[i] };
            if (!u.IsAlive) { u.Abilities = new(); continue; }
            if (!c.ActionEconomyInputsPresent || c.ActionPointMaximumUnits <= 0 || c.MovementEfficiencyMilliUnits < 0 ||
                c.Primary["反应"] <= 0 || c.HealthRecoveryPerOwnTurn < 0 || c.SpiritRecoveryPerOwnTurn < 0 || !GameData.RealmOrder.Contains(c.Realm))
                throw new InvalidOperationException("action_economy_character_input_invalid");
            scheduler.Register(u.Id, c.Primary["反应"]);
            u.GoldenCore = c.CreateGoldenCoreRuntimeLedger(u.MP);
            u.Abilities = CreateAbilities(u);
            u.State = c.StartCombatState(CaptureCombatLocalState(u.Position, u.HP, u.MP, 0, 0));
            u.Shouyi = HasGongFa(u, "抱元守一经", "金丹") ? 2 : 0;
            u.Fudan = HasGongFa(u, "云篆度人经", "金丹") ? 2 : 0;
            u.QiushuiCharges = HasGongFa(u, "秋水游心经", "筑基") ? c.Realm == "金丹" ? 2 : 1 : 0;
            u.Buqian = HasGongFa(u, "万物不迁法", "金丹") ? 3 : 0;
        }
        int turns = 0;
        bool BothTeamsAlive() => units.Where((u, i) => team[i] == 0).Any(u => u.IsAlive) && units.Where((u, i) => team[i] == 1).Any(u => u.IsAlive);
        void Observe(int actor, string evt, Ability ability = null, int apBefore = 0, int mpBefore = 0, int hpBefore = 0, int cost = 0)
        {
            if (!observe) return;
            var u = units[actor];
            var axis = scheduler.GetSnapshot().Units.Single(value => value.Id == u.Id);
            economy.Add(new(turns, actor, scheduler.CurrentTick, evt, ability?.Cost.DefinitionRef ?? "", ability?.Key ?? "",
                apBefore, u.AP, cost, 0, 0, mpBefore, ability == null ? u.MP : u.Resource(ability), hpBefore, u.HP, u.PendingPenalty,
                axis.ChargeTime, axis.NextActionThreshold, u.SnapshotCooldowns(), u.OwnTurns, u.Actions, u.Shouyi, u.Fudan, u.LeiJie, u.Stunned, ability?.Cost.Version ?? "",
                units.Select(unit => unit.HP).ToArray(), units.Select(unit => unit.MP).ToArray()));
        }
        while (BothTeamsAlive() && turns < turnLimit)
        {
            var advance = scheduler.AdvanceUntilAction(units.ToDictionary(u => u.Id, u => u.IsAlive, StringComparer.Ordinal));
            if (string.IsNullOrEmpty(advance.UnitId)) throw new InvalidOperationException("ct_scheduler_has_no_actor");
            // One decrement per elapsed global tick, including every unique shared ledger once.
            foreach (var u in units.Where(u => u.IsAlive))
            {
                foreach (string key in u.Cooldowns.Keys.ToArray()) u.Cooldowns[key] = Math.Max(0, u.Cooldowns[key] - advance.TicksElapsed);
                for (int tick = 0; tick < advance.TicksElapsed; tick++) u.GoldenCore?.TickCooldowns();
            }
            int actor = Array.FindIndex(units, u => u.Id == advance.UnitId);
            var au = units[actor];
            turns++; au.OwnTurns++; au.Actions = 0; au.Moved = false; au.PendingPenalty = 0;
            int hpBeforeStart = au.HP, mpBeforeStart = au.MP;
            if (HasGongFa(au, "南华玄感录", "练气") && au.Stunned &&
                Rng.NextDouble() < (au.Char.Realm switch { "金丹" => .50, "筑基" => .30, _ => .20 }))
            {
                au.Stunned = false;
                if (au.Char.Realm != "练气") au.HP = Math.Min(au.Char.Primary["HP"], au.HP + (int)(au.Char.Primary["HP"] * .05));
                if (au.Char.Realm == "金丹") { au.Xuantong = 1; au.XuantongAppliedOwnTurn = au.OwnTurns; }
            }
            au.HP = Math.Min(au.Char.Primary["HP"], checked(au.HP + au.Char.HealthRecoveryPerOwnTurn));
            au.MP = Math.Min(au.Char.Primary["MP"], checked(au.MP + au.Char.SpiritRecoveryPerOwnTurn));
            au.Limit = au.Char.ActionPointMaximumUnits;
            bool skipped = au.Stunned;
            au.AP = skipped ? 0 : au.Limit;
            Observe(actor, skipped ? "own_turn_started_control_skip" : "own_turn_started", mpBefore: mpBeforeStart, hpBefore: hpBeforeStart);
            if (skipped) au.Stunned = false;
            while (!skipped && au.AP > 0 && au.IsAlive && BothTeamsAlive())
            {
                var occupied = units.Where((u, i) => i != actor && u.IsAlive).Select(u => u.Position).ToArray();
                Ability selectedAbility = null;
                GroupActionPlan selectedPlan = default;
                GroupTargetSelection selection = default;
                // Each affordable ability competes over actual legal positions; failed candidates do not consume rolls.
                foreach (var ability in au.Abilities)
                {
                    if (ability.Cost.ActionPointCostUnits > au.AP || au.Cooldown(ability) != 0 || au.Resource(ability) < SpiritCost(au, ability) ||
                        ability.Kind == KuxingAction && au.HP <= 2) continue;
                    if (ability.Profile.Type == "恢复灵力")
                    {
                        foreach (int ally in Enumerable.Range(0, units.Length).Where(i => team[i] == team[actor] && units[i].IsAlive &&
                            units[i].MP < units[i].Char.Primary["MP"] && au.Resource(ability) >= SpiritCost(au, ability, i != actor))
                            .OrderBy(i => i == actor ? 0 : 1).ThenBy(i => units[i].MP).ThenBy(i => i))
                        {
                            var plan = ally == actor ? new GroupActionPlan(new(au.Position, true), null) :
                                ResolvePaidPlan(battlefield, units, team, actor, ally, ability, au.AP - ability.Cost.ActionPointCostUnits, occupied);
                            if (!plan.Position.CanAttack) continue;
                            selectedAbility = ability;
                            selection = new(ally, ally == actor ? "selected_self_recovery" : "selected_ally_recovery");
                            selectedPlan = plan;
                            break;
                        }
                        if (selectedAbility != null) break;
                        continue;
                    }
                    var plans = new Dictionary<int, GroupActionPlan>();
                    var candidates = new GroupTargetCandidate[units.Length];
                    for (int target = 0; target < units.Length; target++)
                    {
                        bool legal = false;
                        GroupActionSettlementCandidate? settlement = null;
                        if (team[target] != team[actor] && units[target].IsAlive)
                        {
                            var plan = ResolvePaidPlan(battlefield, units, team, actor, target, ability,
                                au.AP - ability.Cost.ActionPointCostUnits, occupied);
                            plans.Add(target, plan);
                            legal = plan.Position.CanAttack;
                            if (legal) settlement = new(turns, actor, target, plan.AreaTargeting?.HitTargetIndexes ?? new[] { target },
                                target, GroupActionSettlementEvidence.Unavailable());
                        }
                        candidates[target] = new(target, team[target], units[target].HP, units[target].IsAlive, legal, settlement);
                    }
                    var candidateSelection = SelectGroupTarget(team[actor], candidates);
                    if (candidateSelection.TargetIndex < 0) continue;
                    selectedAbility = ability; selection = candidateSelection; selectedPlan = plans[selection.TargetIndex];
                    break;
                }
                var start = au.Position;
                var reachable = observe ? battlefield.FindReachableWithinActionPoints(start, au.AP, au.Char.MovementEfficiencyMilliUnits, occupied)
                    .OrderBy(pair => pair.Value).ThenBy(pair => pair.Key.Q).ThenBy(pair => pair.Key.R).Select(pair => pair.Key).ToArray() : Array.Empty<HexCoord>();
                if (selectedAbility == null)
                {
                    int focus = Enumerable.Range(0, units.Length).Where(i => team[i] != team[actor] && units[i].IsAlive)
                        .OrderBy(i => units[i].HP).ThenBy(i => i).First();
                    var affordable = au.Abilities.Where(a => a.Cost.ActionPointCostUnits <= au.AP && a.Profile.Type != "恢复灵力").LastOrDefault();
                    if (affordable != null)
                    {
                        var move = ResolvePaidPlan(battlefield, units, team, actor, focus, affordable, au.AP, occupied);
                        int before = au.AP;
                        CommitMovement(battlefield, au, move.Position.Position, occupied);
                        if (before != au.AP) Observe(actor, "move_committed", apBefore: before, mpBefore: au.MP, hpBefore: au.HP, cost: before - au.AP);
                    }
                    if (observe) actions.Add(new(turns, actor, start, au.Position, reachable, -1, "no_legal_target", null,
                        "no_legal_target_after_move", null, Array.Empty<int>(), "", SnapshotOccupiedPositions(units)));
                    break;
                }
                var a = selectedAbility;
                if (selectedPlan.Position.Position != start)
                {
                    int before = au.AP;
                    CommitMovement(battlefield, au, selectedPlan.Position.Position, occupied);
                    Observe(actor, "move_committed", apBefore: before, mpBefore: au.MP, hpBefore: au.HP, cost: before - au.AP);
                }
                int targetIndex = selection.TargetIndex;
                int apBefore = au.AP, mpBefore = au.Resource(a), hpBefore = au.HP;
                int spiritCost = SpiritCost(au, a, targetIndex != actor);
                int nextPenalty = checked(au.PendingPenalty + a.Cost.CtPenalty);
                if (nextPenalty > int.MaxValue - 100 || au.AP < a.Cost.ActionPointCostUnits || au.Resource(a) < spiritCost || au.Cooldown(a) > 0)
                    throw new InvalidOperationException("selected_action_not_committable");
                au.State.SynchronizeLocalState(CaptureLocalState(au));
                au.State.RecordOwnActionCheckpoint($"{au.Id}-turn-{au.OwnTurns}-action-{au.Actions + 1}", turns);
                au.AP -= a.Cost.ActionPointCostUnits;
                if (au.SharedLedger(a) is { } shared)
                {
                    if (!shared.TrySpendResource(spiritCost)) throw new InvalidOperationException("prepared_shared_resource_commit_failed");
                }
                else au.MP -= spiritCost;
                au.PendingPenalty = nextPenalty; au.Actions++; au.StartCooldown(a);
                if (a.Profile.Type == "恢复灵力")
                {
                    var target = units[targetIndex];
                    int restored = (int)decimal.Floor(Math.Max(a.Profile.MinimumSpiritRecovery,
                        au.Char.Innate["魂魄"] * a.Profile.SpiritRecoveryMultiplier) * (targetIndex == actor ? 1 : a.Profile.OtherTargetRecoveryMultiplier));
                    target.MP = Math.Min(target.Char.Primary["MP"], checked(target.MP + restored));
                }
                else
                {
                    int[] targets = (selectedPlan.AreaTargeting?.HitTargetIndexes ?? new[] { targetIndex }).Distinct().OrderBy(i => i).ToArray();
                    SettleAttack(units, team, actor, targets, a);
                }
                au.State.SynchronizeLocalState(CaptureLocalState(au)); au.State.CompleteOwnActiveAction();
                Observe(actor, "action_committed", a, apBefore, mpBefore, hpBefore, a.Cost.ActionPointCostUnits);
                if (observe) actions.Add(new(turns, actor, start, au.Position, reachable, targetIndex, selection.Reason, selection.Priority, "",
                    selectedPlan.AreaTargeting?.Center, selectedPlan.AreaTargeting?.HitTargetIndexes ?? Array.Empty<int>(),
                    selectedPlan.AreaTargeting?.RejectionReason ?? "", SnapshotOccupiedPositions(units)));
            }
            // Exactly one end event, even for control skips and an AP balance exhausted by a successful command.
            if (au.IsAlive && BothTeamsAlive())
            {
                if (HasGongFa(au, "抱元守一经", "练气") && !au.Moved) au.Shouyi = Math.Min(au.Shouyi + 1, ShouyiMaximum(au));
                if (HasGongFa(au, "云篆度人经", "筑基")) au.Fudan = Math.Min(au.Fudan + 1, FudanMaximum(au));
                if (HasGongFa(au, "秋水游心经", "练气"))
                {
                    decimal rate = au.Char.Realm switch { "金丹" => .06m, "筑基" => .04m, _ => .02m };
                    au.HP = Math.Min(au.Char.Primary["HP"], au.HP + (int)decimal.Floor(au.Char.Primary["HP"] * rate));
                }
                if (au.Xuantong > 0 && au.XuantongAppliedOwnTurn < au.OwnTurns) au.Xuantong--;
            }
            int abandoned = au.AP;
            au.AP = 0;
            bool wait = !skipped && au.Actions == 0 && !au.Moved && BothTeamsAlive();
            if (wait) scheduler.WaitAction(au.Id); else scheduler.ConsumeAction(au.Id, au.PendingPenalty);
            Observe(actor, !BothTeamsAlive() ? "combat_terminated" : wait ? "wait_ended" : "own_turn_ended", apBefore: abandoned, mpBefore: au.MP, hpBefore: au.HP);
        }
        foreach (var unit in units) unit.AP = 0;
        double Ratio(int t) => units.Where((u, i) => team[i] == t).Sum(u => u.Char.Primary["HP"] > 0 ? Math.Max(0, u.HP) / (double)u.Char.Primary["HP"] : 0);
        double ratioA = Ratio(0), ratioB = Ratio(1);
        bool aAlive = units.Where((u, i) => team[i] == 0).Any(u => u.IsAlive), bAlive = units.Where((u, i) => team[i] == 1).Any(u => u.IsAlive);
        int winner = aAlive && !bAlive ? 0 : bAlive && !aAlive ? 1 : ratioA > ratioB ? 0 : ratioB > ratioA ? 1 : -1;
        return new(winner, turns, positions.ToArray(), actions.AsReadOnly(), ratioA, ratioB, scheduler.CurrentTick, economy.AsReadOnly());
    }

    static CombatLocalStateSnapshot CaptureLocalState(UnitState unit) =>
        CaptureCombatLocalState(unit.Position, unit.HP, unit.MP, 0, 0) with { Cooldowns = unit.SnapshotCooldowns() };

    static GroupActionPlan ResolvePaidPlan(HexBattlefield battlefield, UnitState[] units, int[] team,
        int actor, int target, Ability ability, int budget, IReadOnlyCollection<HexCoord> occupied)
    {
        var unit = units[actor];
        if (ability.Profile.AreaTargeting == null && CanAttack(battlefield, unit.Position, units[target].Position,
            ability.Profile.MinRange, ability.Profile.MaxRange)) return new(new(unit.Position, true), null);
        var reachable = battlefield.FindReachableWithinActionPoints(unit.Position, budget, unit.Char.MovementEfficiencyMilliUnits, occupied)
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        reachable.TryAdd(unit.Position, 0);
        var candidates = reachable.Select(entry =>
        {
            var area = ability.Profile.AreaTargeting == null ? null : battlefield.ResolveAreaTargeting(ability.Profile.AreaTargeting,
                entry.Key, team[actor], actor, units[target].Position, 0,
                units.Select((u, i) => new GameData.AreaTargetCandidate(i, team[i], i == actor ? entry.Key : u.Position, u.IsAlive)).ToArray());
            // Range quotes retain the raw Attack distance even outside the requested range.
            // Reuse that same quote for ranking instead of traversing the board a second time.
            var range = battlefield.QueryRange(entry.Key, units[target].Position,
                ability.Profile.MinRange, ability.Profile.MaxRange, SpatialQueryKind.Attack, requireLineOfSight: true);
            bool legal = area == null ? range.IsInRange : area.HitTargetIndexes.Contains(target);
            int d = range.DistanceUnits >= 0 ? range.DistanceUnits : int.MaxValue / 4;
            int gap = Math.Max(ability.Profile.MinRange * battlefield.MetricUnitsPerRange - d,
                Math.Max(0, d - ability.Profile.MaxRange * battlefield.MetricUnitsPerRange));
            return new { Position = entry.Key, Cost = entry.Value, Legal = legal, Gap = gap, Area = area };
        }).OrderBy(entry => entry.Legal ? 0 : 1).ThenBy(entry => entry.Legal ? entry.Cost : entry.Gap)
            .ThenBy(entry => entry.Cost).ThenBy(entry => entry.Position.Q).ThenBy(entry => entry.Position.R).First();
        return new(new(candidates.Position, candidates.Legal), candidates.Area);
    }

    static void CommitMovement(HexBattlefield battlefield, UnitState unit, HexCoord destination,
        IReadOnlyCollection<HexCoord> occupied)
    {
        if (destination == unit.Position) return;
        var path = battlefield.QueryMovementPath(unit.Position, destination, unit.AP, unit.Char.MovementEfficiencyMilliUnits, occupied);
        if (path.Reason != TianZhang.Spatial.SpatialQueryReasons.Ok || path.ActionPointCostUnits <= 0 || path.ActionPointCostUnits > unit.AP)
            throw new InvalidOperationException("selected_movement_not_committable");
        unit.AP -= path.ActionPointCostUnits; unit.Position = destination; unit.Moved = true; unit.Actions++;
    }

    static void SettleAttack(UnitState[] units, int[] team, int actor, int[] targetIndexes, Ability ability)
    {
        var au = units[actor]; var ca = au.Char;
        string type = ability.Profile.Type;
        double mult = ability.Profile.Mult;
        bool water = ca.Style == "water_physical" && ability.Kind is ArtAction or DivineAction &&
            ability.Cost.DefinitionRef is "WaterArt" or "WaterDivine";
        int hpRecovery = 0;
        if (ability.Kind == KuxingAction)
        {
            decimal costRate = ca.Realm switch { "金丹" => .20m, "筑基" => .15m, _ => .10m };
            int hpCost = Math.Min(au.HP - 1, (int)decimal.Floor(au.HP * costRate));
            au.HP -= hpCost;
            if (ca.Realm == "金丹") hpRecovery = (int)decimal.Floor(hpCost * .30m);
            au.KuxingDefReduction = (int)(ca.Primary["肉防"] * .30);
        }
        if (HasGongFa(au, "抱元守一经", "练气") && type == "神魂")
            mult *= 1 + au.Shouyi * (ca.Realm == "金丹" ? .05 : .04);
        bool fuxiu = HasGongFa(au, "云篆度人经", "筑基") && ability.Cost.DefinitionRef == "TaiyiFuxiuArt";
        if (fuxiu) { mult *= 1 + au.Fudan * (ca.Realm == "金丹" ? .15 : .12); au.Fudan = 0; }
        if (HasGongFa(au, "九霄雷劫录", "筑基") && type == "物理" && au.LeiJie > 0)
        { mult *= 1 + au.LeiJie * (ca.Realm == "金丹" ? .18 : .15); au.LeiJie = 0; }
        // Existing team-only aura consumption stays in the actual multi-member battle context.
        if (team.Count(t => t == team[actor]) > 1)
            foreach (int i in Enumerable.Range(0, units.Length).Where(i => team[i] == team[actor] && units[i].IsAlive && HasGongFa(units[i], "绳墨正法录", "筑基")))
                mult += units[i].Char.Realm == "金丹" ? .15 : .10;
        foreach (int target in targetIndexes)
        {
            var du = units[target]; var cb = du.Char;
            if (!du.IsAlive) continue;
            int atk = type == "物理" ? ca.Primary["肉攻"] : ca.Primary["神攻"];
            // Retain the existing generic simulation's strength projection; this is not a new production effect.
            if (HasGongFa(au, "南华玄感录", "练气") && ca.Style == "taixu_xuangan")
                atk += ca.Realm switch { "金丹" => 8, "筑基" => 5, _ => 3 };
            int rawDef = type == "物理" ? cb.Primary["肉防"] : cb.Primary["神防"];
            if (type == "神魂" && HasGongFa(du, "抱元守一经", "金丹") && du.Shouyi == ShouyiMaximum(du)) rawDef = (int)(rawDef * 1.15);
            if (type == "物理" && du.KuxingDefReduction > 0) { rawDef = Math.Max(0, rawDef - du.KuxingDefReduction); du.KuxingDefReduction = 0; }
            int def = type == "物理" ? (int)(rawDef * (1 - du.Shishui * .05)) : rawDef;
            if (type == "神魂" && HasGongFa(du, "九霄雷劫录", "筑基") && du.LeiJie == LeijieMaximum(du)) def = (int)(def * .80);
            double resist = cb.Secondary.GetValueOrDefault(type == "物理" ? "物抗率" : "魂抗率", 0);
            double targetMult = mult;
            if (ability.Kind == KuxingAction && ca.Realm != "练气" && du.HP < cb.Primary["HP"] * .50) targetMult *= 1.3;
            var element = GameData.GetElementMatch(ability.Profile.Element, ca.GongFaName, cb.GongFaName);
            int dmg = ApplyDefenses(Dmg(atk, def, resist, ability.DefPen, targetMult * element.DamageMultiplier), ca, cb, type,
                ignoreBlock: ability.Kind == KuxingAction, critRateBonus: element.CritRateBonus, critDamageBonus: element.CritDamageBonus);
            if (du.Chuanliu > 0 && dmg > 0) { dmg = (int)(dmg * .65); du.Chuanliu = 0; }
            if (HasGongFa(du, "抱元守一经", "筑基") && du.Shouyi > 0 && dmg > 0) { dmg = (int)(dmg * .80); du.Shouyi--; }
            if (du.Buqian > 0 && dmg > cb.Primary["HP"] * .30) { dmg /= 2; du.Buqian--; }
            int absorbed = Math.Min(du.Shield, dmg); du.Shield -= absorbed; dmg -= absorbed;
            du.HP = Math.Max(0, du.HP - dmg);
            if (dmg > 0 && HasGongFa(du, "九霄雷劫录", "筑基")) du.LeiJie = Math.Min(du.LeiJie + 1, LeijieMaximum(du));
            if (water && dmg > 0 && du.Xuantong == 0 && !du.Stunned)
            {
                double chance = Math.Clamp(.10 + (ca.Primary["神识"] - cb.Primary["神识"]) / 50.0, .05, .90);
                if (Rng.NextDouble() < chance) du.Stunned = true;
            }
            if (ability.Cost.DefinitionRef == "WaterDivine") { du.Shishui = Math.Min(3, du.Shishui + 1); au.Chuanliu = 1; }
            if (du.QiushuiCharges > 0 && du.HP > 0 && du.HP < cb.Primary["HP"] * .30 && dmg > 0)
            { du.Shield = (int)decimal.Floor(du.HP * .30m); du.QiushuiCharges--; }
        }
        if (hpRecovery > 0 && au.IsAlive) au.HP = Math.Min(ca.Primary["HP"], au.HP + hpRecovery);
    }

    static IReadOnlyList<HexCoord> SnapshotOccupiedPositions(IEnumerable<UnitState> units) =>
        units.Where(unit => unit.IsAlive).Select(unit => unit.Position).ToArray();
}
