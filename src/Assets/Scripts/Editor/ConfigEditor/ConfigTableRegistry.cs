using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class ConfigTableRegistry
    {
        private static readonly string[] ProductionIds =
        {
            "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules", "AttackProfiles",
            "Spells", "Skills", "CharacterCreationPointBuy", "Characters", "Enemies", "Items", "GongFa",
            "AppearanceProfiles", "Language", "Settlements", "Bounties", "Adventures", "EnvironmentProfiles",
            "FoundationPurpleMansionStates", "JindanStaticStates", "NpcCultivationActionWeightProfiles",
            "CharterRuleDefinitions", "CharterSites"
        };
        private static readonly string[] ProductionNames =
        {
            "境界战斗成长", "效果层级规则", "行动消耗规则", "攻击档案", "术法", "神通", "创角点购",
            "角色", "怪物", "物品", "功法", "外观方案", "显示文本", "据点", "悬赏", "冒险", "战场环境",
            "道基与紫府状态", "金丹静态状态", "NPC修炼行动权重", "册界规则", "册界站点"
        };

        public IReadOnlyList<ConfigTableDefinition> Tables { get; }
        public IConfigApplyService ApplyService { get; }

        public ConfigTableRegistry(IEnumerable<ConfigTableDefinition> definitions, IConfigApplyService applyService = null)
        {
            var registered = new Dictionary<string, ConfigTableDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.TableId) || string.IsNullOrEmpty(definition.ChineseName) ||
                    string.IsNullOrEmpty(definition.CsvPath)) throw new InvalidDataException("表登记缺少身份、中文名或路径。");
                if (!registered.TryAdd(definition.TableId, definition)) throw new InvalidDataException($"重复表ID：{definition.TableId}");
            }
            Tables = Array.AsReadOnly(registered.Values.ToArray());
            ApplyService = applyService;
        }

        public static ConfigTableRegistry Discover()
        {
            var definitions = new List<ConfigTableDefinition>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IConfigTableProvider>().Where(IsConcrete).OrderBy(type => type.FullName, StringComparer.Ordinal))
                definitions.AddRange(((IConfigTableProvider)Activator.CreateInstance(type)).GetTables());
            // Reject duplicates before replacing the display-only catalogue entries.
            var supplied = new ConfigTableRegistry(definitions);
            foreach (var table in supplied.Tables)
                if (!ProductionIds.Contains(table.TableId, StringComparer.Ordinal) ||
                    table.CsvPath != $"Assets/DataConfig/{table.TableId}.csv")
                    throw new InvalidDataException($"登记不属于批准的生产表：{table.TableId}");
            var services = TypeCache.GetTypesDerivedFrom<IConfigApplyService>().Where(IsConcrete).ToArray();
            if (services.Length > 1) throw new InvalidDataException("保存与应用服务只能有一个实现。");
            var service = services.Length == 0 ? null : (IConfigApplyService)Activator.CreateInstance(services[0]);
            var tables = ProductionIds.Select((id, index) => supplied.Tables.FirstOrDefault(table => table.TableId == id) ??
                new ConfigTableDefinition
                {
                    TableId = id, ChineseName = ProductionNames[index], CsvPath = $"Assets/DataConfig/{id}.csv",
                    HeaderMode = id == "Language" ? ConfigHeaderMode.None :
                        id == "Adventures" ? ConfigHeaderMode.FirstPhysicalLine : ConfigHeaderMode.Header
                });
            return new ConfigTableRegistry(tables, service);
        }

        public ConfigDraftSession OpenSession()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var documents = Tables.ToDictionary(table => table.TableId,
                table => File.ReadAllBytes(Path.Combine(projectRoot, table.CsvPath)), StringComparer.Ordinal);
            return new ConfigDraftSession(Tables, documents);
        }

        private static bool IsConcrete(Type type) => !type.IsAbstract && !type.IsInterface && !type.ContainsGenericParameters;
    }
}
