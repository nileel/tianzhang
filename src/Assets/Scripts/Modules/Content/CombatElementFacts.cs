using System;
using System.Collections.Generic;

namespace TianZhang.Content
{
    /// <summary>Immutable element facts shared by content import and runtime composition.</summary>
    public static class CombatElementFacts
    {
        private static readonly Dictionary<string, string> GongFaElements = new()
        {
            ["抱元守一经"] = "水", ["gongfa_baoyuanshouyi"] = "水",
            ["云篆度人经"] = "风", ["gongfa_yunzhuandurenjing"] = "风",
            ["秋水游心经"] = "水", ["gongfa_qiushuiyouxin"] = "水",

            ["九霄雷劫录"] = "雷", ["gongfa_jiuxiaoleijie"] = "雷",
            ["苦行剑典"] = "金", ["gongfa_kuxingjiandian"] = "金",
            ["疾雷破山经"] = "雷", ["gongfa_jileiposhan"] = "雷",
            ["雷池淬体功"] = "雷", ["gongfa_leicnicuiti"] = "雷",

            ["含弘光大典"] = "土", ["gongfa_hanhongguangda"] = "土",
            ["白屋青云录"] = "土", ["gongfa_baiwuqingyun"] = "土",
            ["混元同尘典"] = "土", ["gongfa_hunyuantongchen"] = "土",
            ["绳墨正法录"] = "土", ["gongfa_shengmozhengfa"] = "土",

            ["万物不迁法"] = "暗", ["gongfa_wanwubuqian"] = "暗",
            ["不真自虚法"] = "暗", ["gongfa_buzhenzixu"] = "暗",
            ["南华玄感录"] = "暗", ["gongfa_nanhuaxuangan"] = "暗",
            ["心无性有法"] = "暗", ["gongfa_xinwuxingyou"] = "暗",

            ["南华大梦书"] = "水", ["gongfa_nanhuadamengshu"] = "水",
            ["南华阐衍典"] = "风", ["gongfa_nanhuachanyandian"] = "风",
            ["大洞炼真经"] = "土", ["gongfa_dadonglianzhenjing"] = "土",
            ["太易山藏经"] = "土", ["gongfa_taiyishancangjing"] = "土",
            ["太易玄义笺"] = "土", ["gongfa_taiyixuanyijian"] = "土",
            ["玄牝道藏"] = "混沌", ["gongfa_xuanpindaocang"] = "混沌",
            ["空无般若经"] = "水", ["gongfa_kongwuborejing"] = "水",
            ["见素抱朴经"] = "木", ["gongfa_jiansubaopujing"] = "木",
            ["通神三玄礼录"] = "金", ["gongfa_tongshensanxuanlilu"] = "金",
        };

        public static string ResolveGongFaElement(string gongFaName)
        {
            if (string.IsNullOrEmpty(gongFaName))
                return string.Empty;
            return GongFaElements.TryGetValue(gongFaName, out string element) ? element : string.Empty;
        }

        public static string ResolveElement(string elementRequirement)
        {
            if (string.IsNullOrWhiteSpace(elementRequirement))
                return string.Empty;

            string value = elementRequirement.Trim().ToLowerInvariant();
            if (value == "-" || value.Contains("any")) return string.Empty;
            if (value.Contains("chaos")) return "混沌";
            if (value.Contains("thunder")) return "雷";
            if (value.Contains("wind")) return "风";
            if (value.Contains("ice")) return "冰";
            if (value.Contains("dark")) return "暗";
            if (value.Contains("star")) return "星";
            if (value.Contains("poison") || value.Contains("toxin")) return "毒";
            if (value.Contains("water")) return "水";
            if (value.Contains("fire")) return "火";
            if (value.Contains("earth")) return "土";
            if (value.Contains("metal")) return "金";
            if (value.Contains("wood")) return "木";

            return NormalizeElement(elementRequirement);
        }

        internal static string NormalizeElement(string element)
        {
            if (string.IsNullOrWhiteSpace(element)) return string.Empty;
            return element.Trim() switch
            {
                "金" or "木" or "水" or "火" or "土" or "风" or "雷" or "冰" or "暗" or "星" or "毒" or "混沌" => element.Trim(),
                "element_metal" or "element_metal_root" => "金",
                "element_wood" or "element_wood_root" => "木",
                "element_water" or "element_water_root" => "水",
                "element_fire" or "element_fire_root" => "火",
                "element_earth" or "element_earth_root" => "土",
                "element_wind" or "element_wind_root" => "风",
                "element_thunder" or "element_thunder_root" => "雷",
                "element_ice" or "element_ice_root" => "冰",
                "element_dark" or "element_dark_root" => "暗",
                "element_star" or "element_star_root" => "星",
                "element_poison" or "element_poison_root" => "毒",
                "element_chaos" or "element_chaos_root" => "混沌",
                _ => string.Empty,
            };
        }
    }

    public static class CultivationRealmFacts
    {
        private static readonly string[] Ids = { "realm_fanren", "realm_lianqi", "realm_zhuji", "realm_jindan", "realm_yuanying", "realm_huashen" };
        private static readonly string[] Names = { "凡人", "练气", "筑基", "金丹", "元婴", "化神" };

        public static int RankOfId(string id) => Array.IndexOf(Ids, id);

        public static int RankOfStage(string stage)
        {
            switch (stage)
            {
                case "练气一品" or "练气二品" or "练气三品" or "练气四品" or "练气五品" or
                     "练气六品" or "练气七品" or "练气八品" or "练气九品": return 1;
                case "筑基开府" or "开府" or "筑基圆满": return 2;
                case "金丹初结" or "金丹温养" or "金丹圆满": return 3;
                default:
                    int rank = Array.IndexOf(Names, stage);
                    return rank >= 0 ? rank : RankOfId(stage);
            }
        }

        public static int RequireStageRank(string stage)
        {
            int rank = RankOfStage(stage);
            return rank >= 0 ? rank : throw new ArgumentException("character_realm_unknown:" + stage);
        }

        public static string ResolveId(string stage) => Ids[RequireStageRank(stage)];
        public static string ResolveName(string stage) => Names[RequireStageRank(stage)];
    }

    /// <summary>Pure content gate over explicit realm identity and valid visible spirit-root facts.</summary>
    public static class AbilityRequirementPolicy
    {
        public static bool IsSatisfied(
            string realmStage,
            string visibleRootElement,
            string visibleRootGrade,
            string realmRequirement,
            string elementRequirement)
        {
            int minimum = CultivationRealmFacts.RankOfId(realmRequirement);
            if (minimum < 0 || CultivationRealmFacts.RankOfStage(realmStage) < minimum ||
                visibleRootGrade is not ("凡品" or "下品" or "中品" or "上品" or "极品") ||
                string.IsNullOrWhiteSpace(visibleRootElement))
                return false;
            // Rehearsal identities preserve every visible root, separated by '/'. All must be valid;
            // an elemental entry requirement can be met by any one of those existing roots.
            string[] characterElements = Array.ConvertAll(visibleRootElement.Split('/'), CombatElementFacts.NormalizeElement);
            if (Array.Exists(characterElements, string.IsNullOrEmpty)) return false;
            if (string.Equals(elementRequirement?.Trim(), "element_none", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.IsNullOrWhiteSpace(elementRequirement) || string.IsNullOrWhiteSpace(visibleRootElement))
                return false;

            const string prefix = "element_";
            string normalized = elementRequirement.Trim().ToLowerInvariant();
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            foreach (string alternative in normalized.Substring(prefix.Length)
                         .Replace("_root", string.Empty)
                         .Split(new[] { "_or_" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Array.IndexOf(characterElements, CombatElementFacts.NormalizeElement(prefix + alternative)) >= 0)
                    return true;
            }
            return false;
        }
    }
}
