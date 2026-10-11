using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

class Character
{
    public string Name, Realm, Style;
    public int SubIndex;
    // v3.5: 道基
    public string DFQuality = "无道基";   // 天品/地品/玄品/黄品/无道基
    public double DFMult = 0.0;            // 道基效果倍率
    public int DFScore = 0;                // 凝聚值 (调试用)
    // TQ-013B: 金丹兼容层。LegacyGCGrade 只用于调试回看，不驱动战斗强度。
    public string FormedState { get; set; } = "未成丹";
    public string DanJiType { get; set; } = "";
    // 兼容诊断快照；常规成丹流程只通过 ApplyGoldenCoreCandidateResult 写入候选状态。
    public string OccupancyState { get; set; } = "未成丹";
    public string DanName { get; set; } = "";
    public string DanNature { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public string TargetSeat { get; set; } = "";
    public string SeatName { get; set; } = "";
    public string DanPivot { get; set; } = "";
    public string NaturalDanJiCandidateState { get; set; } = "非自然候选";
    public string SeatAccessState { get; set; } = "none";
    public string SeatCompetitionState { get; set; } = "未进入";
    public string FinalOccupancyState { get; set; } = "未成丹";
    public int SeatCompetitionScore { get; set; } = 0;
    public int ZifuDivineArtCount { get; set; } = 0;
    public int ZifuPalaceCoverageCount { get; set; } = 0;
    public string ZifuCoreLoopState { get; set; } = "未接入";
    public string ZifuEligibilityNote { get; set; } = "未接入紫府神通/府位闭环，阈值待验证";
    public string LegacyGCGrade { get; set; } = "";
    public double DanJiStabilityMult { get; set; } = 1.0;
    public int GCScore = 0;               // 成丹判定值 (调试用)
    public double DanJiArtAffinityMult { get; set; } = 1.0;
    // v5.1: 功法名称（用于读取实际小境界成长表）
    public string GongFaName = "";
    // Explicit main-hand instance; the effective basic is resolved from this and the body source.
    public GameData.AttackProfile BasicAttackProfile;
    public string UnarmedAttackFamilyId = "";
    public float? ProductionRealmMultiplier;
    public GameData.AttackProfile EffectiveBasicAttackProfile => ResolveBasicAttack(Realm);

    GameData.AttackProfile ResolveBasicAttack(string realm)
    {
        var body = GameData.BodyAttack(UnarmedAttackFamilyId, realm);
        if (BasicAttackProfile == null) return body;
        // Named test fixtures retain their existing scope; production references must be real weapons.
        if (BasicAttackProfile.Cost != null && GameData.ProductionAttacks.ContainsKey(BasicAttackProfile.Cost.DefinitionRef) &&
            BasicAttackProfile.BasicBindingKind != "main_equipment")
            throw new InvalidOperationException("simulation_main_equipment_binding_invalid");
        if (BasicAttackProfile.EffectBudgetMode == "fixed_unarmed")
            throw new InvalidOperationException("simulation_body_is_not_equipment");
        if (BasicAttackProfile.Cost != null && !GameData.ProductionAttacks.ContainsKey(BasicAttackProfile.Cost.DefinitionRef) &&
            !BasicAttackProfile.Cost.Version.StartsWith("fixture", StringComparison.Ordinal))
            throw new InvalidOperationException("simulation_main_equipment_profile_missing");
        return BasicAttackProfile;
    }
    // Long-term scalar inputs belong to the existing stat owner. Existing exact stat clones
    // therefore preserve them without a separate fallback or another source of defaults.
    public bool ActionEconomyInputsPresent => Primary.ContainsKey("AP上限单位") && Primary.ContainsKey("移动效率千分单位");
    public int ActionPointMaximumUnits => Primary["AP上限单位"];
    public int MovementEfficiencyMilliUnits => Primary["移动效率千分单位"];
    public GameData.MovementInput MovementInput => Primary.ContainsKey("行动经济模板输入") ? GameData.ProjectMovement(Realm, SubIndex, GongFaName) : null;
    public GameData.ActionEconomyCost ArtCost => ArtName.Length == 0 ? null : GameData.AbilityCost(GameData.DefinitionForStyle(Style,false),AbilityTierId);
    public GameData.ActionEconomyCost DivineCost => DivineName.Length == 0 ? null : GameData.AbilityCost(GameData.DefinitionForStyle(Style,true),AbilityTierId);
    // The entry explicitly chooses same-tier or locked-Qi comparison; character realm never rewrites an instance tier.
    public string FixedTestAbilityTierId = "";
    public string AbilityTierId => FixedTestAbilityTierId.Length == 0 ?
        throw new InvalidOperationException("simulation_fixed_ability_tier_missing") : FixedTestAbilityTierId;
    // Explicit production loadout; generic Config names never impersonate these profile IDs.
    public IReadOnlyList<string> ProductionArtIds { get; private set; } = Array.Empty<string>();
    public int HealthRecoveryPerOwnTurn { get; set; }
    public int SpiritRecoveryPerOwnTurn { get; set; }

    internal void SetActionEconomyInputs(int maximumUnits, int movementMilliUnits)
    {
        if (maximumUnits <= 0 || movementMilliUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumUnits));
        Primary["AP上限单位"] = maximumUnits;
        Primary["移动效率千分单位"] = movementMilliUnits;
    }

    internal void EquipProductionArts(params string[] profileIds)
    {
        if (profileIds.Distinct(StringComparer.Ordinal).Count() != profileIds.Length ||
            profileIds.Any(id => !GameData.ProductionAttacks.TryGetValue(id, out var profile) || profile.BasicBindingKind != ""))
            throw new ArgumentException("Unknown or duplicate production art.");
        ProductionArtIds = Array.AsReadOnly((string[])profileIds.Clone());
    }
    // v4.1: 术法与神通
    public string ArtName = "";          // 术法名称
    public string ArtType = "";          // "物理" or "神魂"
    public string ArtElement = "";       // 术法五行属性
    public double ArtMult = 1.0;         // 术法倍率
    public int ArtMPCost = 0;            // 术法灵力消耗
    public int ArtCooldown = 3;          // 术法冷却回合数
    public int ArtMinRange = 1;
    public int ArtMaxRange = 1;
    public string DivineName = "";       // 神通名称
    public string DivineType = "";       // "物理" or "神魂"
    public string DivineElement = "";    // 神通五行属性
    public double DivineMult = 1.0;      // 神通倍率
    public double DivineDefPen = 0;      // 神通防御穿透%
    public int DivineCooldown = 5;       // 神通冷却回合数
    public int DivineMinRange = 1;
    public int DivineMaxRange = 1;
    // N-JD-RULE-01A：金丹实位的静态装配与战斗账本绑定。
    public GoldenCoreAssembly GoldenCoreAssembly { get; private set; }
    public string ArtAbilityInstanceId { get; private set; } = "";
    public string DivineAbilityInstanceId { get; private set; } = "";
    public bool IsDead { get; private set; }
    public GoldenCoreCarrierDeathResolution ProtectedGoldenCoreDeath { get; private set; }
    // N-STATE-01: a combatant has exactly one active state owner for temporary statuses, checkpoints and causal debts.
    public CombatStateRuntime CombatState { get; private set; }

    public Dictionary<string, int> Innate = new();
    public Dictionary<string, int> Primary = new();
    public Dictionary<string, double> Secondary = new();
    public static string[] InnateKeys = ["根骨", "魂魄", "神识", "资质", "气运"];
    const double WeightDodgeScale = 30.0;
    const double WeightCritRateScale = 30.0;
    const double WeightCritDamageScale = 80.0;

    public static Character Create(string name, Dictionary<string, int> innate, string style)
    {
        // Same explicit human creation source as CharacterCreationRules, independent of name/style.
        var c = new Character { Name = name, Realm = "凡人", SubIndex = 0, Style = style, UnarmedAttackFamilyId = "body_human" };
        foreach (var k in InnateKeys) c.Innate[k] = innate[k];
        return c;
    }

    internal static Character FromProduction(bool enemy, string id)
    {
        var row = GameData.ProductionCharacterRow(enemy, id);
        int Int(string key) => int.Parse(row[key], System.Globalization.CultureInfo.InvariantCulture);
        float Number(string key) => float.Parse(row[key], System.Globalization.CultureInfo.InvariantCulture);
        string realmId = row[enemy ? "realm" : "realmId"];
        var realm = GameData.Rules.Realms.Single(r => r.RealmId == realmId);
        float multiplier = Number("realmMultiplier");
        int Round(float value) => checked((int)MathF.Round(value, MidpointRounding.ToEven));
        // Existing production CharacterAttributes.Calculate projection, without added bonuses.
        // Synthetic gongfa builds still use FinalizeStats and their actual GongFaTables.
        var c = new Character { Name = id, Realm = GameData.RealmOrder[realm.RealmOrder], Style = "physical",
            UnarmedAttackFamilyId = row["unarmedAttackFamilyId"], ProductionRealmMultiplier = multiplier,
            Innate = new() { ["根骨"] = Int("rootBone"), ["魂魄"] = Int("spirit"), ["神识"] = Int("mind"),
                ["资质"] = Int("talent"), ["气运"] = 0 },
            Primary = new() { ["HP"] = Round(MathF.Pow(Int("rootBone"), .75f) * multiplier * 80f),
                ["MP"] = Round(Int("spirit") * multiplier * 15f), ["肉攻"] = Round(Int("rootBone") * multiplier * 5f),
                ["神攻"] = Round(Int("spirit") * multiplier * 5f), ["肉防"] = Round(Int("physique") * multiplier * 3.5f),
                ["神防"] = Round(Int("mind") * multiplier * 3.5f), ["反应"] = Int("reaction"), ["神识"] = Int("mind") } };
        foreach (var pair in new[] { ("格挡率", "blockRate"), ("格挡减伤率", "blockReduction"), ("魂盾率", "soulShieldRate"),
            ("魂盾减伤率", "soulShieldReduction"), ("闪避率", "dodgeRate"), ("暴击率", "critRate"),
            ("暴击伤害", "critDamage"), ("命中率", "hitRateBonus") }) c.Secondary[pair.Item1] = Number(pair.Item2);
        c.SetActionEconomyInputs(realm.ActionPointMaximumUnits, realm.BaseMovementEfficiencyMilliUnits);
        return c;
    }

    // N-SEAT-01B：角色只消费成丹候选快照；稳定占据只能由位格注册表原子绑定后确认。
    internal void ApplyGoldenCoreCandidateResult(Cultivation.Result result)
    {
        LegacyGCGrade = result.LegacyGCGrade;
        GCScore = result.GCScore;
        FormedState = result.FormedState;
        DanJiType = result.DanJiType;
        OccupancyState = result.OccupancyState;
        DanName = result.DanName;
        DanNature = result.DanNature;
        TargetBranch = result.TargetBranch;
        TargetSeat = result.TargetSeat;
        SeatName = result.SeatName;
        DanPivot = result.DanPivot;
        NaturalDanJiCandidateState = result.NaturalDanJiCandidateState;
        SeatAccessState = result.SeatAccessState;
        SeatCompetitionState = result.SeatCompetitionState;
        FinalOccupancyState = result.FinalOccupancyState;
        SeatCompetitionScore = result.SeatCompetitionScore;
        ZifuDivineArtCount = result.ZifuDivineArtCount;
        ZifuPalaceCoverageCount = result.ZifuPalaceCoverageCount;
        ZifuCoreLoopState = result.ZifuCoreLoopState;
        ZifuEligibilityNote = result.ZifuEligibilityNote;
        DanJiStabilityMult = result.DanJiStabilityMultiplier;
        DanJiArtAffinityMult = result.DanJiArtAffinityMultiplier;
    }

    internal CombatStateRuntime StartCombatState(CombatLocalStateSnapshot initialState)
    {
        var runtime = new CombatStateRuntime(Name, GameData.CombatStateRules);
        runtime.Initialize(initialState);
        CombatState = runtime;
        return runtime;
    }

    public void ApplyGrowth(string realm, string techGrade, Dictionary<string, double> weights)
    {
        int realmIdx = Array.IndexOf(GameData.RealmOrder, realm);
        int breakthroughs = realmIdx;
        if (breakthroughs == 0) return;
        int totalPts = breakthroughs * GameData.InnatePerBreakthrough;
        double tw = weights.Values.Sum();
        int sumAlloc = 0;
        var add = new Dictionary<string, int>();
        foreach (var k in InnateKeys) { int a = (int)Math.Round(totalPts * weights[k] / tw); add[k] = a; sumAlloc += a; }
        int diff = totalPts - sumAlloc;
        if (diff != 0) { string mk = InnateKeys[0]; foreach (var k in InnateKeys) if (weights[k] > weights[mk]) mk = k; add[mk] += diff; }
        foreach (var k in InnateKeys) Innate[k] += add[k];
    }

    static (string[] types, double pctPerChapter) ResolveSecondary(Dictionary<string, double> w)
    {
        var map = new Dictionary<string, string[]> {
            ["根骨"] = new[] { "格挡率" }, ["魂魄"] = new[] { "魂盾率" },
            ["神识"] = new[] { "命中率" }, ["资质"] = new[] { "暴击伤害" }, ["气运"] = new[] { "闪避率" }
        };
        var ordered = w.Where(kv => kv.Value > 0.15).OrderByDescending(kv => kv.Value).Take(2).ToList();
        if (ordered.Count == 0) ordered.Add(new("根骨", 1.0));
        var types = ordered.Select(kv => map[kv.Key][0]).ToArray();
        double pct = types.Length == 1 ? 5.0 : 3.0;
        return (types, pct);
    }

    public void FinalizeStats(string realm, int subIdx, string spiritGrade, Dictionary<string, double> weights)
    {
        ResolveBasicAttack(realm); // Reject bad body/weapon input before publishing growth.
        var movementInput = GameData.ProjectMovement(realm, subIdx, GongFaName);
        Realm = realm; SubIndex = subIdx;
        var rb = GameData.Base[realm];
        var rf = GameData.Factor[realm];
        double sm = GameData.SpiritMod[spiritGrade];
        var (secTypes, secPctPerChapter) = ResolveSecondary(weights);

        // 一级属性 = 境界基础 + sum(小境界成长) + 先天×系数×权重
        Primary["HP"] = (int)Math.Round((rb.HP + SubGrowthSum("HP", realm, subIdx, weights, GongFaName) + Innate["根骨"] * rf.HP * weights["根骨"] * 2.2) * sm);
        Primary["MP"] = (int)Math.Round((rb.MP + SubGrowthSum("MP", realm, subIdx, weights, GongFaName) + Innate["魂魄"] * rf.MP * weights["魂魄"]) * sm * DanJiStabilityMult);
        Primary["肉攻"] = (int)Math.Round((rb.肉攻 + SubGrowthSum("肉攻", realm, subIdx, weights, GongFaName) + Innate["根骨"] * rf.攻 * weights["根骨"]) * sm);
        Primary["神攻"] = (int)Math.Round((rb.神攻 + SubGrowthSum("神攻", realm, subIdx, weights, GongFaName) + Innate["魂魄"] * rf.攻 * weights["魂魄"]) * sm);
        Primary["肉防"] = (int)Math.Round((rb.肉防 + SubGrowthSum("肉防", realm, subIdx, weights, GongFaName) + Innate["根骨"] * rf.防 * weights["根骨"]) * sm);
        Primary["神防"] = (int)Math.Round((rb.神防 + SubGrowthSum("神防", realm, subIdx, weights, GongFaName) + Innate["魂魄"] * rf.防 * weights["魂魄"]) * sm);
        Primary["反应"] = (int)Math.Round((rb.反应 + SubGrowthSum("反应", realm, subIdx, weights, GongFaName) + (Innate["根骨"] * weights["根骨"] + Innate["魂魄"] * weights["魂魄"] + Innate["神识"] * weights["神识"]) * rf.反应 / 3.0) * sm);
        Primary["行动经济模板输入"] = 1;
        SetActionEconomyInputs(movementInput.ActionPointMaximumUnits, movementInput.EffectiveMilliUnits);
        // Legacy display only; combat movement consumes the independent integer input above.
        Primary["移力"] = MovementEfficiencyMilliUnits / 1000;
        Primary["神识"] = (int)Math.Round((rb.神识 + SubGrowthSum("神识", realm, subIdx, weights, GongFaName) + Innate["神识"] * rf.神识 * weights["神识"]) * sm);

        // 二级属性
        int chapters = GameData.TotalSubs(realm, subIdx) / 4 + 1;
        foreach (var t in secTypes) Secondary[t] = chapters * secPctPerChapter;
        Secondary["格挡减伤率"] = Secondary.GetValueOrDefault("格挡率", 0) * 0.8;
        Secondary["魂盾减伤率"] = Secondary.GetValueOrDefault("魂盾率", 0) * 0.8;
        Secondary["闪避率"] = Secondary.GetValueOrDefault("闪避率", 0) + weights["气运"] * WeightDodgeScale;
        Secondary["命中率"] = Secondary.GetValueOrDefault("命中率", 0);
        Secondary["暴击率"] = Secondary.GetValueOrDefault("暴击率", 0) + weights["神识"] * WeightCritRateScale;
        Secondary["暴击伤害"] = Secondary.GetValueOrDefault("暴击伤害", 0) + weights["资质"] * WeightCritDamageScale;
        Secondary["物抗率"] = 0; Secondary["魂抗率"] = 0;
    }

    internal double SubGrowthSum(string attr, string realm, int subIdx, Dictionary<string, double> w, string gongFaName = "")
    {
        // v5.1: 优先使用功法实际成长表
        if (!string.IsNullOrEmpty(gongFaName) && GameData.GongFaTables.TryGetValue(gongFaName, out var table))
        {
            double sum = 0; int totalSubs = GameData.TotalSubs(realm, subIdx);
            // TotalSubs 包含凡人节点；该节点不产生成长，但仍占累计索引。
            int prevSubs = GameData.Sublevels["凡人"];
            foreach (var r in GameData.RealmOrder)
            {
                if (r == "凡人") continue;
                int subsHere = GameData.Sublevels[r];
                int effective = Math.Min(subsHere, Math.Max(0, totalSubs - prevSubs));
                if (effective <= 0) break;
                if (!table.TryGetValue(r, out var grow))
                {
                    // 功法可从更高境界起修；缺少前序境界时不应截断后续已登记的成长。
                    prevSubs += subsHere;
                    continue;
                }
                for (int stage = 0; stage < effective; stage++)
                {
                    var stageGrowth = GameData.GrowthAtSublevel(r, stage, grow);
                    sum += attr switch
                    {
                        "HP" => stageGrowth.HP, "MP" => stageGrowth.MP, "肉攻" => stageGrowth.肉攻, "神攻" => stageGrowth.神攻,
                        "肉防" => stageGrowth.肉防, "神防" => stageGrowth.神防, "反应" => stageGrowth.反应, "神识" => stageGrowth.神识, _ => 0
                    };
                }
                prevSubs += subsHere;
                if (r == realm) break;
            }
            return sum;
        }
        if (!string.IsNullOrEmpty(gongFaName) && !GameData.HasApprovedGrowthFallback(gongFaName))
            throw new InvalidOperationException($"功法「{gongFaName}」缺少成长表；必须补齐成长表或登记显式回退。");
        // 回退：原有权重近似计算
        double wsum = 0; int wtotalSubs = GameData.TotalSubs(realm, subIdx);
        int wprevSubs = GameData.Sublevels["凡人"];
        foreach (var r in GameData.RealmOrder)
        {
            if (r == "凡人") continue;
            int subsHere = GameData.Sublevels[r];
            int effective = Math.Min(subsHere, Math.Max(0, wtotalSubs - wprevSubs));
            if (effective <= 0) break;
            var growth = GameData.SubGrowthBase[r];
            double Scale(string key) => w[key] / 0.6;
            // 权重先确定总预算，再按同一整数分配合同结算阶段成长。
            var weightedGrowth = growth with
            {
                HP = growth.HP * Scale("根骨"), MP = growth.MP * Scale("魂魄"),
                肉攻 = growth.肉攻 * Scale("根骨"), 神攻 = growth.神攻 * Scale("魂魄"),
                肉防 = growth.肉防 * Scale("根骨"), 神防 = growth.神防 * Scale("魂魄"),
                反应 = growth.反应 * Scale("根骨"), 神识 = growth.神识 * Scale("神识")
            };
            for (int stage = 0; stage < effective; stage++)
            {
                var stageGrowth = GameData.GrowthAtSublevel(r, stage, weightedGrowth);
                double val = attr switch
                {
                    "HP" => stageGrowth.HP, "MP" => stageGrowth.MP, "肉攻" => stageGrowth.肉攻, "神攻" => stageGrowth.神攻,
                    "肉防" => stageGrowth.肉防, "神防" => stageGrowth.神防, "反应" => stageGrowth.反应, "神识" => stageGrowth.神识, _ => 0
                };
                wsum += val;
            }
            wprevSubs += subsHere;
            if (r == realm) break;
        }
        return wsum;
    }

    // v4.1: 根据风格和境界分配术法与神通
    public void AssignArts()
    {
        if (Realm == "凡人") { ArtName = ""; DivineName = ""; return; }
        if (FixedTestAbilityTierId.Length == 0) FixedTestAbilityTierId = GameData.TierForRealm(Realm);
        var art = GameData.GenericProfile(GameData.DefinitionForStyle(Style,false),AbilityTierId);
        ArtName = art.Name; ArtType = art.Type; ArtElement = art.Element; ArtMult = art.Mult;
        ArtMPCost = art.SpiritCost; ArtCooldown = art.Cost.ReuseCooldownTicks; ArtMinRange = art.MinRange; ArtMaxRange = art.MaxRange;
        ArtCost.Validate();
        DivineName = "";
        if (Realm == "金丹" && FormedState == "成丹" && !string.IsNullOrEmpty(DanJiType))
        {
            var divine = GameData.GenericProfile(GameData.DefinitionForStyle(Style,true),AbilityTierId);
            DivineName = divine.Name; DivineType = divine.Type; DivineElement = divine.Element; DivineMult = divine.Mult;
            DivineCooldown = divine.Cost.ReuseCooldownTicks; DivineMinRange = divine.MinRange; DivineMaxRange = divine.MaxRange;
            DivineDefPen = Style switch { "physical" or "magic" or "water_physical" => 10, "taiyi_fuxiu" => 20, _ => 15 };
            DivineCost.Validate();
        }
    }

    public void AssignGoldenCoreAssembly(
        GoldenCoreAssembly assembly,
        string artAbilityInstanceId = "",
        string divineAbilityInstanceId = "")
    {
        if (IsDead)
            throw new InvalidOperationException("A protected golden-core death cannot receive a replacement assembly.");
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));
        if (GoldenCoreAssembly != null)
            throw new InvalidOperationException("An existing golden-core assembly may only change through safe primary-carrier reforging.");
        ValidateAssemblyAbility(assembly, artAbilityInstanceId, nameof(artAbilityInstanceId));
        ValidateAssemblyAbility(assembly, divineAbilityInstanceId, nameof(divineAbilityInstanceId));
        GoldenCoreAssembly = assembly;
        ArtAbilityInstanceId = artAbilityInstanceId;
        DivineAbilityInstanceId = divineAbilityInstanceId;
    }

    internal GoldenCoreRuntimeLedger CreateGoldenCoreRuntimeLedger(int initialResource) =>
        GoldenCoreAssembly?.CreateRuntimeLedger(initialResource);

    internal GoldenCoreReforgeResolution ReforgeGoldenCorePrimaryCarrier(
        GoldenCoreRuntimeLedger runtimeLedger,
        GoldenCoreSeatType positionType,
        string replacementAbilityInstanceId,
        IReadOnlyList<string> auxiliaryCarrierAbilityInstanceIds,
        string verifiedCompatibilityProfileId,
        bool isSafeState)
    {
        if (!isSafeState)
            return GoldenCoreReforgeResolution.Rejected("JD_REFORGE_SAFE_STATE_REQUIRED");
        if (IsDead)
            return GoldenCoreReforgeResolution.Rejected("JD_REFORGE_PROTECTED_DEATH");

        var assembly = GoldenCoreAssembly;
        if (assembly == null)
            return GoldenCoreReforgeResolution.Rejected("JD_REFORGE_ASSEMBLY_UNAVAILABLE");
        if (runtimeLedger == null || !ReferenceEquals(runtimeLedger.Assembly, assembly))
            return GoldenCoreReforgeResolution.Rejected("JD_REFORGE_RUNTIME_LEDGER_INVALID");

        try
        {
            var reforgedAssembly = assembly.ReforgePrimaryCarrier(
                positionType,
                replacementAbilityInstanceId,
                auxiliaryCarrierAbilityInstanceIds,
                verifiedCompatibilityProfileId);
            runtimeLedger.RebindAssembly(reforgedAssembly);
            GoldenCoreAssembly = reforgedAssembly;
            return GoldenCoreReforgeResolution.Applied(positionType, replacementAbilityInstanceId);
        }
        catch (GoldenCoreAssemblyException ex)
        {
            return GoldenCoreReforgeResolution.Rejected(ex.Code);
        }
    }

    internal GoldenCoreConflictCandidatePreparation PrepareGoldenCoreConflictCandidate(
        GoldenCoreRuntimeLedger runtimeLedger,
        GoldenCoreConflictCandidateInput input)
    {
        if (GoldenCoreAssembly == null)
            return GoldenCoreConflictCandidatePreparation.Rejected("JD_CONFLICT_ASSEMBLY_UNAVAILABLE");
        return GoldenCoreConflictCandidates.Prepare(GoldenCoreAssembly, runtimeLedger, input);
    }

    internal GoldenCoreCarrierDeathResolution ResolveGoldenCoreCarrierDeath(
        GoldenCoreRuntimeLedger runtimeLedger,
        GoldenCoreCarrierDeathInput input)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.DeathEventId) || string.IsNullOrWhiteSpace(input.CarrierAbilityInstanceId))
            return GoldenCoreCarrierDeathResolution.Rejected("JD_CARRIER_DEATH_INPUT_INVALID");

        if (ProtectedGoldenCoreDeath != null)
        {
            return string.Equals(ProtectedGoldenCoreDeath.DeathEventId, input.DeathEventId, StringComparison.Ordinal)
                ? ProtectedGoldenCoreDeath
                : GoldenCoreCarrierDeathResolution.Rejected("JD_CARRIER_DEATH_ALREADY_SETTLED");
        }

        var assembly = GoldenCoreAssembly;
        if (assembly == null)
            return GoldenCoreCarrierDeathResolution.Rejected("JD_CARRIER_DEATH_ASSEMBLY_UNAVAILABLE");
        if (runtimeLedger == null || !ReferenceEquals(runtimeLedger.Assembly, assembly))
            return GoldenCoreCarrierDeathResolution.Rejected("JD_CARRIER_DEATH_RUNTIME_LEDGER_INVALID");
        if (!assembly.StableSeats.TryGetValue(input.PositionType, out var seat))
            return GoldenCoreCarrierDeathResolution.Rejected("JD_CARRIER_DEATH_REAL_POSITION_INVALID");
        if (!string.Equals(seat.PrimaryCarrierAbilityInstanceId, input.CarrierAbilityInstanceId, StringComparison.Ordinal))
            return GoldenCoreCarrierDeathResolution.Rejected("JD_CARRIER_DEATH_CARRIER_MISMATCH");

        var dispositions = runtimeLedger.CloseForCarrierDeath(input.CarrierAbilityInstanceId);
        var settlement = new GoldenCoreCarrierDeathResolution(
            true,
            "JD_CARRIER_DEATH_SETTLED",
            input.DeathEventId,
            input.PositionType,
            input.CarrierAbilityInstanceId,
            dispositions);

        ProtectedGoldenCoreDeath = settlement;
        IsDead = true;
        GoldenCoreAssembly = null;
        ArtAbilityInstanceId = "";
        DivineAbilityInstanceId = "";
        FormedState = "丹毁死亡";
        OccupancyState = "丹毁死亡";
        FinalOccupancyState = "丹毁死亡";
        SeatCompetitionState = "已关闭";
        return settlement;
    }

    static void ValidateAssemblyAbility(GoldenCoreAssembly assembly, string abilityInstanceId, string parameterName)
    {
        if (!string.IsNullOrWhiteSpace(abilityInstanceId) && !assembly.AbilityLedgers.ContainsKey(abilityInstanceId))
            throw new ArgumentException("Ability instance must belong to the assigned golden-core assembly.", parameterName);
    }

}
