using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

static class Combat
{
    static readonly Random Rng = new();
    public const double BaseCritMultiplier = 1.5;

    internal readonly record struct Options(bool ExtendCasterRangedEligibility = false);

    internal static bool HasRangedEligibility(string style, bool extendCasterEligibility) =>
        style == "magic" || (extendCasterEligibility && style is "taiyi" or "taiyi_fuxiu" or "taixu" or "taixu_xuangan");

    public static double GetCritMultiplier(double critDamage, double elementCritDamageBonus = 0)
    {
        return BaseCritMultiplier + (critDamage + elementCritDamageBonus) / 100;
    }

    // v4.1: 合并攻击结算（含穿透）
    static int Dmg(int atk, int def, double resist, double defPen = 0, double mult = 1.0)
    {
        int effectiveDef = (int)Math.Round(def * (1 - defPen / 100));
        double df = atk / (double)(atk + effectiveDef);
        return (int)Math.Max(0, Math.Round(atk * df * (1 - resist / 100.0) * mult));
    }

    // 格挡/魂盾/闪避/暴击 统一结算
    static int ApplyDefenses(int rawDmg, Character attacker, Character defender, string atkType, bool ignoreDodge = false, bool ignoreBlock = false, double critRateBonus = 0, double critDamageBonus = 0)
    {
        bool isPhysical = atkType == "物理";
        double blockRate = defender.Secondary.GetValueOrDefault(isPhysical ? "格挡率" : "魂盾率", 0);
        if (!ignoreBlock && Rng.NextDouble() * 100 < blockRate)
        {
            double reduction = defender.Secondary.GetValueOrDefault(isPhysical ? "格挡减伤率" : "魂盾减伤率", 0);
            rawDmg = (int)Math.Round(rawDmg * (1 - reduction / 100));
        }
        if (!ignoreDodge && Rng.NextDouble() * 100 < Math.Max(0, defender.Secondary.GetValueOrDefault("闪避率", 0) - attacker.Secondary.GetValueOrDefault("命中率", 0)))
            rawDmg = 0;
        if (rawDmg > 0 && Rng.NextDouble() * 100 < attacker.Secondary.GetValueOrDefault("暴击率", 0) + critRateBonus)
            rawDmg = (int)Math.Round(rawDmg * GetCritMultiplier(attacker.Secondary.GetValueOrDefault("暴击伤害", 0), critDamageBonus));
        return rawDmg;
    }

    public static (double winsA, double winsB, double avgTurns) Simulate(Character ca, Character cb, int rounds) =>
        Simulate(ca, cb, rounds, new Options());

    public static (double winsA, double winsB, double avgTurns) Simulate(Character ca, Character cb, int rounds, Options options)
    {
        int winsA = 0, winsB = 0;
        int totalTurns = 0;
        for (int r = 0; r < rounds; r++)
        {
            int hpA = ca.Primary["HP"], hpB = cb.Primary["HP"];
            int mpA = ca.Primary["MP"], mpB = cb.Primary["MP"];
            int artCdA = 0, artCdB = 0;
            int divineCdA = 0, divineCdB = 0;
            double rangePenaltyA = 1.0, rangePenaltyB = 1.0; // 远程优势: 对方下轮伤害折扣
            double ctA = 0, ctB = 0;
            double sA = 100.0 / ca.Primary["反应"], sB = 100.0 / cb.Primary["反应"];
            // v4.2: 水系机制 & 眩晕
            int chuanliuA = 0, chuanliuB = 0;       // 川流之势: 下次受击减伤35%
            int shishuiOnB = 0, shishuiOnA = 0;     // 逝水印记层数(每层-5%物防)
            int maxShishui(string realm) => realm switch { "化神" => 99, "元婴" => 5, _ => 3 };
            bool stunnedA = false, stunnedB = false;
            // v5.0: 太一道庭守一 & 符胆机制
            int shouyiA = 0, shouyiB = 0;
            int maxShouyi(string realm) => realm switch { "金丹" => 5, "筑基" => 4, "练气" => 3, _ => 5 };
            if (ca.Style == "taiyi") { shouyiA = 2; }
            if (cb.Style == "taiyi") { shouyiB = 2; }
            int fudanA = 0, fudanB = 0;
            int maxFudan(string realm) => realm switch { "金丹" => 5, "筑基" => 3, _ => 5 };
            if (ca.Style == "taiyi_fuxiu") { fudanA = 2; }
            if (cb.Style == "taiyi_fuxiu") { fudanB = 2; }
            int qiushuiA = 0, qiushuiB = 0;          // 秋水护盾剩余触发次数
            int qiushuiMax(string realm) => realm switch { "元婴" => 3, "金丹" => 2, "筑基" => 1, _ => 0 };
            if (ca.Style == "water_physical") qiushuiA = qiushuiMax(ca.Realm);
            if (cb.Style == "water_physical") qiushuiB = qiushuiMax(cb.Realm);


            int kuxingDefReduceA = 0, kuxingDefReduceB = 0;

            // v5.6: 玉清崖 雷劫印记机制（受击叠层→出手消耗→满层魂防-20%）
            int leijieA = 0, leijieB = 0;
            int maxLeijie(string realm) => realm switch { "筑基" => 3, "金丹" => 5, "元婴" => 5, "化神" => 5, "炼虚" => 5, _ => 3 };
            double leijiePerStack(string realm) => realm switch { "筑基" => 0.15, "金丹" => 0.18, "元婴" => 0.22, "化神" => 0.30, "炼虚" => 0.35, _ => 0.15 };
            // v5.7: 太虚观 玄感机制（debuff清除+神识强度+玄同免疫+HP恢复）
            int xuanganShenshiA = 0, xuanganShenshiB = 0;
            int xuantongA = 0, xuantongB = 0;
            double xuanganClearRate(string realm) => realm switch { "元婴" => 0.80, "金丹" => 0.50, "筑基" => 0.30, "练气" => 0.20, _ => 0.20 };
            int xuanganShenshiVal(string realm) => realm switch { "元婴" => 12, "金丹" => 8, "筑基" => 5, "练气" => 3, _ => 3 };
            int xuanganXuantongDur(string realm) => realm switch { "元婴" => 2, "金丹" => 1, _ => 0 };
            bool xuanganCanHeal(string realm) => realm switch { "元婴" => true, "金丹" => true, "筑基" => true, _ => false };
            if (ca.Style == "taixu_xuangan") xuanganShenshiA = xuanganShenshiVal(ca.Realm);
            if (cb.Style == "taixu_xuangan") xuanganShenshiB = xuanganShenshiVal(cb.Realm);            // v5.8: 苦行剑典 血剑气机制
            double kuxingMult(string realm) => realm switch { "化神" => 3.5, "元婴" => 2.8, "金丹" => 2.2, "筑基" => 1.8, _ => 1.5 };
            double kuxingHpCostRate(string realm) => realm switch { "化神" => 0.25, "元婴" => 0.20, "金丹" => 0.20, "筑基" => 0.15, _ => 0.10 };
            bool kuxingHasRecover(string realm) => realm switch { "化神" => true, "元婴" => true, "金丹" => true, _ => false };
            bool kuxingHasDuanLong(string realm) => realm switch { "化神" => true, "元婴" => true, "金丹" => true, "筑基" => true, _ => false };            int turns = 0;
            while (hpA > 0 && hpB > 0)
            {
                turns++;
                // 秋水回血: water_physical每回合恢复1.5%最大HP
                if (ca.Style == "water_physical") hpA = Math.Min(ca.Primary["HP"], hpA + (int)(ca.Primary["HP"] * 0.015));
                if (cb.Style == "water_physical") hpB = Math.Min(cb.Primary["HP"], hpB + (int)(cb.Primary["HP"] * 0.015));
                if (artCdA > 0) artCdA--;
                if (artCdB > 0) artCdB--;
                if (divineCdA > 0) divineCdA--;
                if (divineCdB > 0) divineCdB--;

                if (ctA <= ctB)
                {
                    // 眩晕: 跳过回合, CT归零, 眩晕标记被消耗

                    // 玄感: 回合开始 debuff清除
                    if (ca.Style == "taixu_xuangan" && stunnedA && Rng.NextDouble() < xuanganClearRate(ca.Realm)) { stunnedA = false; if (xuanganCanHeal(ca.Realm)) hpA = Math.Min(ca.Primary["HP"], hpA + (int)(ca.Primary["HP"] * 0.05)); xuantongA = xuanganXuantongDur(ca.Realm); }                    if (stunnedA) { stunnedA = false; ctA += sA; continue; }
                    // AI决策: 神通 > 术法 > 平A
                    string atkType, skillElement = ""; double mult, defPen; int atk, def; double resist;
                    bool waterSkillA = false;                    bool kuxingUsedA = false; int kuxingHpRecoverA = 0; bool kuxingIgnoreBlockA = false;
                    int artMPCostA = (ca.Style == "taiyi_fuxiu" && fudanA == maxFudan(ca.Realm)) ? 0 : (xuantongA > 0 ? (int)(ca.ArtMPCost * 0.70) : ca.ArtMPCost);
                    if (ca.DivineName != "" && divineCdA == 0)
                    {
                        atkType = ca.DivineType; skillElement = ca.DivineElement; mult = ca.DivineMult; defPen = ca.DivineDefPen;
                        divineCdA = ca.DivineCooldown;
                        waterSkillA = ca.Style == "water_physical";
                        if (waterSkillA) { shishuiOnB = Math.Min(shishuiOnB + 1, maxShishui(ca.Realm)); chuanliuA = 1; }
                    }
                    else if (ca.Style == "yuqing_kuxing" && hpA > 2 && hpB > 0)
                    {
                        kuxingUsedA = true;
                        int hpCost = Math.Min(hpA - 1, (int)(hpA * kuxingHpCostRate(ca.Realm)));
                        hpA -= hpCost;
                        atkType = "物理"; mult = kuxingMult(ca.Realm); defPen = 0;
                        if (kuxingHasDuanLong(ca.Realm) && hpB < cb.Primary["HP"] * 0.50) mult *= 1.3;
                        if (kuxingHasRecover(ca.Realm)) kuxingHpRecoverA = (int)(hpCost * 0.30);
                        kuxingIgnoreBlockA = true;
                        kuxingDefReduceA = (int)(ca.Primary["肉防"] * 0.30);
                    }                    else if (mpA >= artMPCostA && artCdA == 0)
                    {
                        atkType = ca.ArtType; skillElement = ca.ArtElement; mult = ca.ArtMult; defPen = 0;
                        mpA -= artMPCostA; artCdA = ca.ArtCooldown;
                        waterSkillA = ca.Style == "water_physical";


                    }
                    else
                    {
                    bool isPhysicalA = ca.Style == "physical" || ca.Style == "water_physical" || ca.Style == "yuqing_leijie" || ca.Style == "yuqing_kuxing"; atkType = isPhysicalA ? "物理" : "神魂"; mult = 1.0; defPen = 0;
                    }
                    // 守一: 神魂攻击加成 (金丹致虚篇: 每层+5%)
                    if (ca.Style == "taiyi" && atkType == "神魂") mult *= (1 + shouyiA * 0.05);
                    // 符胆: 符箓术法效果加成(消耗全部) (按境界:筑基12%/金丹15%/元婴18%/化神22%)
                    bool fudanMaxA = false;
                    if (ca.Style == "taiyi_fuxiu")
                    {
                        fudanMaxA = fudanA == maxFudan(ca.Realm);
                        double fdpA = ca.Realm switch { "筑基" => 0.12, "金丹" => 0.15, "元婴" => 0.18, "化神" => 0.22, _ => 0.15 };
                        mult *= (1 + fudanA * fdpA);
                        fudanA = ca.Realm == "化神" ? 2 : 0;
                    }
                    // 雷劫印记: 物理出手消耗全部印记, 每层伤害加成
                    if (ca.Style == "yuqing_leijie" && atkType == "物理" && leijieA > 0) { mult *= (1 + leijieA * leijiePerStack(ca.Realm)); leijieA = 0; }                    atk = atkType == "物理" ? ca.Primary["肉攻"] : ca.Primary["神攻"];                    if (ca.Style == "taixu_xuangan") atk += xuanganShenshiA;
                    // 云篆篇: 符胆满层时神识+30%加成
                    if (ca.Style == "taiyi_fuxiu" && fudanMaxA) atk += (int)(ca.Primary["神识"] * 0.30);
                    // 逝水印记: 降低目标物防 (每层-5%)
                    int rawDef = atkType == "物理" ? cb.Primary["肉防"] : cb.Primary["神防"];
                    // 血剑气防御惩罚: B若使用了血剑气则物防降低
                    if (kuxingDefReduceB > 0 && atkType == "物理") { rawDef -= kuxingDefReduceB; kuxingDefReduceB = 0; }
                    def = atkType == "物理" ? (int)(rawDef * (1 - shishuiOnB * 0.05)) : rawDef;
                    // 雷劫印记满层: 魂防-20%
                    if (cb.Style == "yuqing_leijie" && leijieB == maxLeijie(cb.Realm) && atkType == "神魂") def = (int)(rawDef * 0.80);                    resist = atkType == "物理" ? cb.Secondary.GetValueOrDefault("物抗率", 0) : cb.Secondary.GetValueOrDefault("魂抗率", 0);
                    // 守一满层: 神魂防御+15% (金丹致虚篇)
                    if (cb.Style == "taiyi" && shouyiB == maxShouyi(cb.Realm) && atkType == "神魂") resist += 15;
                    // 天书篇: 符胆满层时无视30%魂防
                    if (ca.Style == "taiyi_fuxiu" && fudanMaxA && atkType == "神魂") defPen = 30;
                    var elementMatch = GameData.GetElementMatch(skillElement, ca.GongFaName, cb.GongFaName);
                    int dmg = Dmg(atk, def, resist, defPen, mult * elementMatch.DamageMultiplier);
                    // 远程惩罚: 对方上轮使用远程术法/神通, 本轮需拉近距离
                    dmg = (int)(dmg * rangePenaltyA); rangePenaltyA = 1.0;
                    dmg = ApplyDefenses(dmg, ca, cb, atkType, ignoreDodge: fudanMaxA, ignoreBlock: kuxingIgnoreBlockA, critRateBonus: elementMatch.CritRateBonus, critDamageBonus: elementMatch.CritDamageBonus);
                    // 川流之势: B若持有则减伤35%并消耗
                    if (chuanliuB > 0 && dmg > 0) { dmg = (int)(dmg * 0.65); chuanliuB = 0; }
                    // 守一减伤: 受击消耗1层减伤20%
                    if (cb.Style == "taiyi" && shouyiB > 0 && dmg > 0) { dmg = (int)(dmg * 0.80); shouyiB--; }
                    // 远程优势: 术法/神通出手后对方需要拉近距离
                    bool isRanged = HasRangedEligibility(ca.Style, options.ExtendCasterRangedEligibility);
                    if (isRanged) rangePenaltyB = 0.35;
                    hpB -= dmg;
                    // 川流劲眩晕: 10%概率, 玄同免疫 (v4.2补全)
                    if (waterSkillA && dmg > 0 && Rng.NextDouble() < 0.10) { if (xuantongB == 0) stunnedB = true; }
                    // 雷劫印记: 受伤叠1层
                    if (cb.Style == "yuqing_leijie" && dmg > 0) leijieB = Math.Min(leijieB + 1, maxLeijie(cb.Realm));
                    // 血剑气: HP恢复
                    if (kuxingUsedA) hpA = Math.Min(ca.Primary["HP"], hpA + kuxingHpRecoverA);
                    // 秋水护盾: B濒死触发 (HP<30%且还有触发次数)
                    if (qiushuiB > 0 && hpB > 0 && hpB < cb.Primary["HP"] * 0.30)
                    { hpB += (int)(cb.Primary["HP"] * 0.15); qiushuiB--; }


                    // 守一&符胆: 回合结束印记+1
                    if (ca.Style == "taiyi") shouyiA = Math.Min(shouyiA + 1, maxShouyi(ca.Realm));
                    if (ca.Style == "taiyi_fuxiu") fudanA = Math.Min(fudanA + 1, maxFudan(ca.Realm));
                    if (xuantongA > 0) xuantongA--;                    ctA += sA;
                }
                else
                {

                    // 玄感: 回合开始 debuff清除
                    if (cb.Style == "taixu_xuangan" && stunnedB && Rng.NextDouble() < xuanganClearRate(cb.Realm)) { stunnedB = false; if (xuanganCanHeal(cb.Realm)) hpB = Math.Min(cb.Primary["HP"], hpB + (int)(cb.Primary["HP"] * 0.05)); xuantongB = xuanganXuantongDur(cb.Realm); }                    if (stunnedB) { stunnedB = false; ctB += sB; continue; }
                    string atkType, skillElement = ""; double mult, defPen; int atk, def; double resist;
                    bool waterSkillB = false;                    bool kuxingUsedB = false; int kuxingHpRecoverB = 0; bool kuxingIgnoreBlockB = false;
                    int artMPCostB = (cb.Style == "taiyi_fuxiu" && fudanB == maxFudan(cb.Realm)) ? 0 : (xuantongB > 0 ? (int)(cb.ArtMPCost * 0.70) : cb.ArtMPCost);
                    if (cb.DivineName != "" && divineCdB == 0)
                    {
                        atkType = cb.DivineType; skillElement = cb.DivineElement; mult = cb.DivineMult; defPen = cb.DivineDefPen;
                        divineCdB = cb.DivineCooldown;
                        waterSkillB = cb.Style == "water_physical";
                        if (waterSkillB) { shishuiOnA = Math.Min(shishuiOnA + 1, maxShishui(cb.Realm)); chuanliuB = 1; }
                    }
                    else if (cb.Style == "yuqing_kuxing" && hpB > 2 && hpA > 0)
                    {
                        kuxingUsedB = true;
                        int hpCost = Math.Min(hpB - 1, (int)(hpB * kuxingHpCostRate(cb.Realm)));
                        hpB -= hpCost;
                        atkType = "物理"; mult = kuxingMult(cb.Realm); defPen = 0;
                        if (kuxingHasDuanLong(cb.Realm) && hpA < ca.Primary["HP"] * 0.50) mult *= 1.3;
                        if (kuxingHasRecover(cb.Realm)) kuxingHpRecoverB = (int)(hpCost * 0.30);
                        kuxingIgnoreBlockB = true;
                        kuxingDefReduceB = (int)(cb.Primary["肉防"] * 0.30);
                    }                    else if (mpB >= artMPCostB && artCdB == 0)
                    {
                        atkType = cb.ArtType; skillElement = cb.ArtElement; mult = cb.ArtMult; defPen = 0;
                        mpB -= artMPCostB; artCdB = cb.ArtCooldown;
                        waterSkillB = cb.Style == "water_physical";

                    }
                    else
                    {
                    bool isPhysicalB = cb.Style == "physical" || cb.Style == "water_physical" || cb.Style == "yuqing_leijie" || cb.Style == "yuqing_kuxing"; atkType = isPhysicalB ? "物理" : "神魂"; mult = 1.0; defPen = 0;
                    }
                    if (cb.Style == "taiyi" && atkType == "神魂") mult *= (1 + shouyiB * 0.05);
                    // 符胆: 符箓术法效果加成(消耗全部) (按境界:筑基12%/金丹15%/元婴18%/化神22%)
                    bool fudanMaxB = false;
                    if (cb.Style == "taiyi_fuxiu")
                    {
                        fudanMaxB = fudanB == maxFudan(cb.Realm);
                        double fdpB = cb.Realm switch { "筑基" => 0.12, "金丹" => 0.15, "元婴" => 0.18, "化神" => 0.22, _ => 0.15 };
                        mult *= (1 + fudanB * fdpB);
                        fudanB = cb.Realm == "化神" ? 2 : 0;
                    }
                    // 雷劫印记: 物理出手消耗全部印记（B方向）
                    if (cb.Style == "yuqing_leijie" && atkType == "物理" && leijieB > 0) { mult *= (1 + leijieB * leijiePerStack(cb.Realm)); leijieB = 0; }                    atk = atkType == "物理" ? cb.Primary["肉攻"] : cb.Primary["神攻"];                    if (cb.Style == "taixu_xuangan") atk += xuanganShenshiB;
                    // 云篆篇: 符胆满层时神识+30%加成
                    if (cb.Style == "taiyi_fuxiu" && fudanMaxB) atk += (int)(cb.Primary["神识"] * 0.30);
                    int rawDefB = atkType == "物理" ? ca.Primary["肉防"] : ca.Primary["神防"];
                    // 血剑气防御惩罚: A若使用了血剑气则物防降低
                    if (kuxingDefReduceA > 0 && atkType == "物理") { rawDefB -= kuxingDefReduceA; kuxingDefReduceA = 0; }
                    def = atkType == "物理" ? (int)(rawDefB * (1 - shishuiOnA * 0.05)) : rawDefB;
                    if (ca.Style == "yuqing_leijie" && leijieA == maxLeijie(ca.Realm) && atkType == "神魂") def = (int)(rawDefB * 0.80);
                    resist = atkType == "物理" ? ca.Secondary.GetValueOrDefault("物抗率", 0) : ca.Secondary.GetValueOrDefault("魂抗率", 0);
                    if (ca.Style == "taiyi" && shouyiA == maxShouyi(ca.Realm) && atkType == "神魂") resist += 15;
                    // 天书篇: 符胆满层时无视30%魂防
                    if (cb.Style == "taiyi_fuxiu" && fudanMaxB && atkType == "神魂") defPen = 30;
                    var elementMatch = GameData.GetElementMatch(skillElement, cb.GongFaName, ca.GongFaName);
                    int dmg = Dmg(atk, def, resist, defPen, mult * elementMatch.DamageMultiplier);
                    // 远程惩罚: 对方上轮使用远程术法/神通, 本轮需拉近距离
                    dmg = (int)(dmg * rangePenaltyB); rangePenaltyB = 1.0;
                    dmg = ApplyDefenses(dmg, cb, ca, atkType, ignoreDodge: fudanMaxB, ignoreBlock: kuxingIgnoreBlockB, critRateBonus: elementMatch.CritRateBonus, critDamageBonus: elementMatch.CritDamageBonus);
                    if (chuanliuA > 0 && dmg > 0) { dmg = (int)(dmg * 0.65); chuanliuA = 0; }
                    if (ca.Style == "taiyi" && shouyiA > 0 && dmg > 0) { dmg = (int)(dmg * 0.80); shouyiA--; }
                    // 远程优势: B使用术法/神通后A需要拉近距离
                    bool bRanged = HasRangedEligibility(cb.Style, options.ExtendCasterRangedEligibility);
                    if (bRanged) rangePenaltyA = 0.35;
                    hpA -= dmg;
                    // 血剑气: HP恢复
                    if (kuxingUsedB) hpB = Math.Min(cb.Primary["HP"], hpB + kuxingHpRecoverB);
                    if (qiushuiA > 0 && hpA > 0 && hpA < ca.Primary["HP"] * 0.30)
                    { hpA += (int)(ca.Primary["HP"] * 0.15); qiushuiA--; }

                    if (cb.Style == "taiyi") shouyiB = Math.Min(shouyiB + 1, maxShouyi(cb.Realm));
                    if (cb.Style == "taiyi_fuxiu") fudanB = Math.Min(fudanB + 1, maxFudan(cb.Realm));
                    if (xuantongB > 0) xuantongB--;                    ctB += sB;
                }
            }
            totalTurns += turns;
            if (hpA > 0) winsA++; else winsB++;
        }
        return (winsA * 100.0 / rounds, winsB * 100.0 / rounds, (double)totalTurns / rounds);
    }

    // ═══════════════════════════════════════
    // 2v2 群战模拟 (v6.0)
    // ═══════════════════════════════════════
    struct UnitState
    {
        public Character Char;
        public int HP, MP;
        public double CT;
        public int Shouyi, Fudan, LeiJie, Qiushui, Buqian;
        public bool BuzhenFirstVoid, Stunned;
        public bool IsAlive => HP > 0;
    }

    public static (double winsA, double winsB, double avgTurns) Simulate2v2(
        Character ca1, Character ca2, Character cb1, Character cb2, int rounds)
    {
        int winsA = 0, winsB = 0, totalTurns = 0;
        for (int r = 0; r < rounds; r++)
        {
            var units = new UnitState[4];
            var chars = new[] { ca1, ca2, cb1, cb2 };
            int[] team = { 0, 0, 1, 1 };
            for (int i = 0; i < 4; i++)
            {
                var c = chars[i];
                units[i] = new UnitState { Char = c, HP = c.Primary["HP"], MP = c.Primary["MP"],
                    Shouyi = c.Style == "taiyi" ? 2 : 0, Fudan = c.Style == "taiyi_fuxiu" ? 2 : 0,
                    Qiushui = c.Style == "water_physical" ? (c.Realm switch { "元婴" => 3, "金丹" => 2, "筑基" => 1, _ => 0 }) : 0,
                    Buqian = c.GongFaName == "万物不迁法" ? (c.Realm switch { "元婴" => 5, "化神" => 5, "金丹" => 3, _ => 0 }) : 0,
                    BuzhenFirstVoid = c.GongFaName == "不真自虚法" };
            }
            int turns = 0;
            while (turns < 200)
            {
                bool aAlive = units[0].IsAlive || units[1].IsAlive;
                bool bAlive = units[2].IsAlive || units[3].IsAlive;
                if (!aAlive || !bAlive) break;
                // 2v2 uses an accumulated-action-meter model: higher reaction fills CT faster.
                for (int i = 0; i < 4; i++) if (units[i].IsAlive) { var u = units[i]; u.CT += u.Char.Primary["反应"]; units[i] = u; }
                int actor = -1; double maxCT = -1;
                for (int i = 0; i < 4; i++) if (units[i].IsAlive && !units[i].Stunned && units[i].CT >= 100 && units[i].CT > maxCT) { maxCT = units[i].CT; actor = i; }
                if (actor < 0) continue;
                turns++;
                var au = units[actor]; au.CT -= 100;
                // 目标: 敌方最低HP
                int target = -1; int lowHP = int.MaxValue;
                for (int i = 0; i < 4; i++) if (i != actor && units[i].IsAlive && team[i] != team[actor] && units[i].HP < lowHP) { lowHP = units[i].HP; target = i; }
                if (target < 0) { units[actor] = au; continue; }
                var du = units[target];
                var ca = au.Char; var cb = du.Char;
                bool useMagic = ca.Style is "magic" or "taiyi" or "taiyi_fuxiu" or "taixu" or "taixu_xuangan";
                string atkType = useMagic ? "神魂" : "物理";
                string skillElement = ca.ArtElement;
                int atk = useMagic ? ca.Primary["神攻"] : ca.Primary["肉攻"];
                int def = useMagic ? cb.Primary["神防"] : cb.Primary["肉防"];
                double resist = useMagic ? cb.Secondary.GetValueOrDefault("魂抗率", 0) : cb.Secondary.GetValueOrDefault("物抗率", 0);
                double mult = 1.0;
                // 阵型光环 (绳墨正法录)
                for (int i = 0; i < 4; i++) if (units[i].IsAlive && team[i] == team[actor] && units[i].Char.GongFaName == "绳墨正法录")
                    mult += units[i].Char.Realm switch { "元婴" => 0.20, "金丹" => 0.15, "筑基" => 0.10, _ => 0.10 };
                // 守一满层
                int maxSy = ca.Realm switch { "金丹" => 5, "筑基" => 4, "练气" => 3, _ => 5 };
                if (ca.Style == "taiyi" && au.Shouyi >= maxSy) atk = (int)(atk * 1.08);
                // 雷劫
                if (ca.Style == "yuqing_leijie" && au.LeiJie > 0) { mult *= 1 + au.LeiJie * (ca.Realm switch { "筑基" => 0.15, "金丹" => 0.18, "元婴" => 0.22, "化神" => 0.30, _ => 0.15 }); au.LeiJie = 0; }
                // 万钧 (混元同尘典)
                if (ca.GongFaName == "混元同尘典" && ca.Realm is "元婴" or "化神")
                    for (int i = 0; i < 4; i++) if (i != actor && units[i].IsAlive && team[i] == team[actor] && (double)units[i].HP / units[i].Char.Primary["HP"] > 0.6) mult *= 1.25;
                var elementMatch = GameData.GetElementMatch(skillElement, ca.GongFaName, cb.GongFaName);
                int dmg = Dmg(atk, def, resist, 0, mult * elementMatch.DamageMultiplier);
                dmg = ApplyDefenses(dmg, ca, cb, atkType, critRateBonus: elementMatch.CritRateBonus, critDamageBonus: elementMatch.CritDamageBonus);
                // 守一减伤
                if (cb.Style == "taiyi" && du.Shouyi > 0 && dmg > 0) { dmg = (int)(dmg * 0.80); du.Shouyi--; }
                if (cb.GongFaName == "万物不迁法" && du.Buqian > 0 && dmg > cb.Primary["HP"] * 0.30) { dmg /= 2; du.Buqian--; }
                if (cb.GongFaName == "不真自虚法" && dmg > 0) { double vr = cb.Realm switch { "化神" => 0.40, "元婴" => 0.25, _ => 0 }; if (du.BuzhenFirstVoid) { vr = 1.0; du.BuzhenFirstVoid = false; } if (Rng.NextDouble() < vr) dmg = 0; }
                du.HP -= dmg;
                if (cb.Style == "yuqing_leijie") { int maxLj2 = cb.Realm switch { "筑基" => 3, "金丹" => 5, "元婴" => 5, "化神" => 5, _ => 3 }; du.LeiJie = Math.Min(du.LeiJie + 1, maxLj2); }
                if (ca.Style == "taiyi") au.Shouyi = Math.Min(au.Shouyi + 1, maxSy);
                if (ca.Style == "taiyi_fuxiu") au.Fudan = Math.Min(au.Fudan + 1, ca.Realm switch { "金丹" => 5, "筑基" => 3, _ => 5 });
                if (du.Char.Style == "water_physical" && du.Qiushui > 0 && du.HP > 0 && du.HP < cb.Primary["HP"] * 0.30) { du.HP += (int)(cb.Primary["HP"] * 0.15); du.Qiushui--; }
                // 玄感 debuff清除
                for (int i = 0; i < 4; i++) if (units[i].IsAlive && team[i] == team[actor] && units[i].Char.GongFaName == "南华玄感录") {
                    double cr = units[i].Char.Realm switch { "元婴" => 0.80, "金丹" => 0.50, "筑基" => 0.30, "练气" => 0.20, _ => 0.20 };
                    if (Rng.NextDouble() < cr) for (int j = 0; j < 4; j++) if (j != i && units[j].IsAlive && team[j] == team[actor]) { var uj = units[j]; if (uj.Stunned) { uj.Stunned = false; units[j] = uj; break; } }
                }
                units[actor] = au; units[target] = du;
            }
            bool finalAAlive = units[0].IsAlive || units[1].IsAlive;
            bool finalBAlive = units[2].IsAlive || units[3].IsAlive;
            if (finalAAlive && !finalBAlive) winsA++;
            else if (!finalAAlive && finalBAlive) winsB++;
            else
            {
                double hpRatioA =
                    Math.Max(0, units[0].HP) / (double)units[0].Char.Primary["HP"] +
                    Math.Max(0, units[1].HP) / (double)units[1].Char.Primary["HP"];
                double hpRatioB =
                    Math.Max(0, units[2].HP) / (double)units[2].Char.Primary["HP"] +
                    Math.Max(0, units[3].HP) / (double)units[3].Char.Primary["HP"];
                if (hpRatioA >= hpRatioB) winsA++; else winsB++;
            }
            totalTurns += turns;
        }
        return (winsA * 100.0 / rounds, winsB * 100.0 / rounds, (double)totalTurns / rounds);
    }
    }
