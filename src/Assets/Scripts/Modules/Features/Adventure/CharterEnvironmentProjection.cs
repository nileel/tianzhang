using System.Collections.Generic;
using TianZhang.Content;
using TianZhang.World;

namespace TianZhang.Features.Adventure
{
    /// <summary>
    /// Stable rejection reasons of the single charter environment projection.
    /// </summary>
    public static class CharterEnvironmentProjectionReasons
    {
        public const string Ok = "";
        /// <summary>会话尚未接入册界长期状态（<see cref="CharterRuntimeStateData"/> 为 null）。</summary>
        public const string NoLongTermState = "charter_environment_projection_no_long_term_state";
        /// <summary>当前地区没有任何已生效条目（currentRegionRuleEntryIds 为空）。</summary>
        public const string NoCurrentRegionEntry = "charter_environment_projection_no_current_region_entry";
        /// <summary>当前地区条目集合出现重复稳定 ID。</summary>
        public const string DuplicateCurrentRegionEntry = "charter_environment_projection_duplicate_current_region_entry";
        /// <summary>当前地区条目在唯一静态目录中不存在对应定义。</summary>
        public const string UnknownRuleEntry = "charter_environment_projection_unknown_rule_entry";
        /// <summary>唯一静态目录缺失或校验失败；原因附目录自身稳定代码。</summary>
        public const string CatalogUnavailable = "charter_environment_projection_catalog_unavailable";
        /// <summary>条目定义声明的环境输出解析出多个互异环境档案 ID，无法唯一匹配既有 asset。</summary>
        public const string DuplicateEnvironmentId = "charter_environment_projection_duplicate_environment_id";
        /// <summary>解析出的环境档案 ID 与 Adventure 已序列化 asset 的 profileId 不一致（或 asset 未绑定）。</summary>
        public const string AssetProfileMismatch = "charter_environment_projection_asset_profile_mismatch";
    }

    /// <summary>One read-only projection outcome; it never mutates the session or any asset.</summary>
    public sealed class CharterEnvironmentProjectionResult
    {
        public bool Succeeded;
        public string Reason;
        public string[] RuleEntryIds;
        public string[] EventIds;
        public string EnvironmentProfileId;
    }

    /// <summary>
    /// 无状态册界环境投影：只把当前地区已生效条目单向解析为定义声明的环境档案引用，并与 Adventure
    /// 已序列化 asset 的 profileId 精确匹配。只读取 <see cref="CharterRuntimeStateData"/>、
    /// <see cref="ContentCatalogData"/> 的唯一静态目录与序列化档案 ID；不保存状态、不执行规则、
    /// 不反写条目、覆盖、供给、提交或地区状态。
    /// </summary>
    public static class CharterEnvironmentProjection
    {
        /// <summary>
        /// 从当前地区已生效条目解析唯一定义、事件输出和 environmentProfileId，并与序列化 asset 的
        /// profileId 精确匹配。任一层失败都返回稳定原因且不提供 fallback；定义级有效性（事件输出
        /// 缺失、环境 ID 越界等）由唯一静态目录校验失败关闭，本投影原样传播目录自身稳定代码。
        /// </summary>
        public static bool TryResolve(
            CharterRuntimeStateData state,
            ContentCatalogData contentCatalog,
            string serializedEnvironmentProfileId,
            out CharterEnvironmentProjectionResult result)
        {
            if (state == null)
            {
                result = Failed(CharterEnvironmentProjectionReasons.NoLongTermState);
                return false;
            }

            string[] currentRegionEntries = state.currentRegionRuleEntryIds;
            if (currentRegionEntries == null || currentRegionEntries.Length == 0)
            {
                result = Failed(CharterEnvironmentProjectionReasons.NoCurrentRegionEntry);
                return false;
            }

            if (!HasUniqueIds(currentRegionEntries))
            {
                result = Failed(CharterEnvironmentProjectionReasons.DuplicateCurrentRegionEntry);
                return false;
            }

            if (contentCatalog == null)
            {
                result = Failed(CharterEnvironmentProjectionReasons.CatalogUnavailable + ":charter_static_catalog_unavailable");
                return false;
            }
            if (!contentCatalog.TryGetCharterRuleStaticCatalog(
                    out CharterRuleStaticCatalogData staticCatalog,
                    out string catalogReason))
            {
                result = Failed(CharterEnvironmentProjectionReasons.CatalogUnavailable + ":" + catalogReason);
                return false;
            }

            var ruleEntryIds = new List<string>();
            var eventIds = new List<string>();
            var environmentProfileIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (string entryId in currentRegionEntries)
            {
                CharterRuleDefinitionData definition = FindDefinition(staticCatalog.Definitions, entryId);
                if (definition == null)
                {
                    result = Failed(CharterEnvironmentProjectionReasons.UnknownRuleEntry);
                    return false;
                }

                ruleEntryIds.Add(entryId);
                foreach (CharterWorldEventOutputData output in definition.worldEventOutputs)
                {
                    if (!eventIds.Contains(output.eventId))
                        eventIds.Add(output.eventId);
                    environmentProfileIds.Add(output.environmentProfileId);
                }
            }

            if (environmentProfileIds.Count != 1)
            {
                result = Failed(CharterEnvironmentProjectionReasons.DuplicateEnvironmentId);
                return false;
            }

            string environmentProfileId = null;
            foreach (string profileId in environmentProfileIds)
                environmentProfileId = profileId;

            if (!string.Equals(serializedEnvironmentProfileId, environmentProfileId, System.StringComparison.Ordinal))
            {
                result = Failed(CharterEnvironmentProjectionReasons.AssetProfileMismatch);
                return false;
            }

            result = new CharterEnvironmentProjectionResult
            {
                Succeeded = true,
                Reason = CharterEnvironmentProjectionReasons.Ok,
                RuleEntryIds = ruleEntryIds.ToArray(),
                EventIds = eventIds.ToArray(),
                EnvironmentProfileId = environmentProfileId,
            };
            return true;
        }

        private static CharterRuleDefinitionData FindDefinition(
            CharterRuleDefinitionData[] definitions,
            string ruleEntryId)
        {
            if (definitions == null)
                return null;
            foreach (CharterRuleDefinitionData definition in definitions)
            {
                if (definition != null &&
                    string.Equals(definition.ruleEntryId, ruleEntryId, System.StringComparison.Ordinal))
                {
                    return definition;
                }
            }
            return null;
        }

        private static bool HasUniqueIds(string[] values)
        {
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                    return false;
            }
            return true;
        }

        private static CharterEnvironmentProjectionResult Failed(string reason)
        {
            return new CharterEnvironmentProjectionResult { Succeeded = false, Reason = reason };
        }
    }
}
