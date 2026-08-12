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

        private static string NormalizeElement(string element)
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

    /// <summary>Pure content gate over the two runtime primitives needed by ability requirements.</summary>
    public static class AbilityRequirementPolicy
    {
        private static readonly Dictionary<string, float> RealmThresholds = new()
        {
            ["realm_fanren"] = 1f, ["realm_lianqi"] = 1.5f, ["realm_zhuji"] = 3f,
            ["realm_jindan"] = 6f, ["realm_yuanying"] = 12f, ["realm_huashen"] = 24f,
        };

        public static bool IsSatisfied(
            float realmMultiplier,
            string visibleRootElement,
            string realmRequirement,
            string elementRequirement)
        {
            if (string.IsNullOrWhiteSpace(realmRequirement) ||
                !RealmThresholds.TryGetValue(realmRequirement.Trim(), out float minimum) ||
                realmMultiplier < minimum)
                return false;
            if (string.Equals(elementRequirement?.Trim(), "element_none", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.IsNullOrWhiteSpace(elementRequirement) || string.IsNullOrWhiteSpace(visibleRootElement))
                return false;

            const string prefix = "element_";
            string normalized = elementRequirement.Trim().ToLowerInvariant();
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string characterElement = CombatElementFacts.ResolveElement(visibleRootElement);
            foreach (string alternative in normalized.Substring(prefix.Length)
                         .Replace("_root", string.Empty)
                         .Split(new[] { "_or_" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (CombatElementFacts.ResolveElement(prefix + alternative) == characterElement)
                    return true;
            }
            return false;
        }
    }
}
