using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Content;
using UnityEngine;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class CharterRuleTable : IConfigTableProvider, IConfigRowCodec
    {
        public const string TableId = "CharterRuleDefinitions";
        public IEnumerable<ConfigTableDefinition> GetTables()
        {
            var fields = new[]
            {
                CharterEditorFields.Locked("ruleEntryId", "基础 · 规则条目ID", "稳定行键不可修改"),
                Reference("displayName", "基础 · 名称文本键", c => c.displayNameKeys),
                Reference("ruleFamily", "基础 · 规则族", c => c.ruleFamilyIds),
                Reference("relationElement", "基础 · 关联五行", c => c.relationElementIds),
                List("compatiblePhenomena", "条件 · 兼容现象", c => c.phenomenonIds),
                Reference("positiveCommit", "提交 · 正向提交", c => c.commits.Select(v => v.commitId)),
                Reference("negativeCommit", "提交 · 负向提交", c => c.commits.Select(v => v.commitId)),
                Reference("requiredAuthority", "条件 · 所需权限", c => c.authorityRequirements.Select(v => v.authorityId)),
                List("requiredNodeTypes", "条件 · 所需节点类型", c => c.nodeTypeIds),
                CharterEditorFields.Enum("scopeType", "范围 · 作用域类型", "SINGLE_NODE", "单节点", "CONNECTED_NODES", "连接节点", "REGIONAL_HUB", "区域枢纽"),
                CharterEditorFields.Enum("scopeTierCap", "范围 · 作用域上限", "NODE", "节点", "AREA", "片区", "REGION", "区域"),
                List("anchorNodeIds", "范围 · 锚定节点", c => c.nodeIds),
                Reference("propagationBoundaryProfileId", "范围 · 传播边界档案", c => c.propagationBoundaries.Select(v => v.propagationBoundaryProfileId)),
                List("currentCoverageSet", "范围 · 当前覆盖", c => c.propagationBoundaries.SelectMany(v => v.allowedCoverageIds).Distinct()),
                List("affectedWorldVariables", "范围 · 受影响世界变量", c => c.worldVariableIds),
                Reference("conflictProfileId", "冲突 · 冲突档案", c => c.conflicts.Select(v => v.conflictProfileId)),
                CharterEditorFields.Enum("failurePolicy", "冲突 · 失败策略", "REJECT", "拒绝", "SUSPEND", "挂起", "SAFE_DOWNGRADE", "安全降级"),
                new ConfigFieldDefinition
                {
                    Column = "worldEventOutputs", ChineseName = "事件 · 世界事件输出", Control = ConfigControl.List, Editable = true,
                    Element = new ConfigFieldDefinition
                    {
                        ChineseName = "事件与环境", Control = ConfigControl.Group, Editable = true,
                        Children = new[] { Reference("eventId", "世界事件", c => c.worldEventIds),
                            Reference("environmentProfileId", "环境档案", c => c.environmentProfileIds) }
                    },
                    NewElement = () => ConfigValue.Group(new Dictionary<string, ConfigValue>
                        { ["eventId"] = ConfigValue.Scalar(CharterEditorFields.Catalog().ReferenceCatalog.worldEventIds.First()),
                            ["environmentProfileId"] = ConfigValue.Scalar(CharterEditorFields.Catalog().ReferenceCatalog.environmentProfileIds.First()) }),
                    Help = "显式添加时先选目录首项，请选择所需事件与环境；重复项不能通过校验。静态配置不代表正式提交。"
                }
            };
            yield return CharterEditorFields.Table(TableId, "册界规则", fields, this, "ruleEntryId", "displayName");
        }

        private static ConfigFieldDefinition Reference(string column, string label,
            Func<CharterRuleReferenceCatalog, IEnumerable<string>> source) => CharterEditorFields.Reference(column, label, source);

        private static ConfigFieldDefinition List(string column, string label, Func<CharterRuleReferenceCatalog, IEnumerable<string>> source) =>
            new ConfigFieldDefinition
            {
                Column = column, ChineseName = label, Control = ConfigControl.List, Editable = true,
                Element = Reference("item", "引用", source), NewElement = () => ConfigValue.Scalar(source(CharterEditorFields.Catalog().ReferenceCatalog).First()),
                Help = "引用必须已在静态目录登记；不能留空或重复。"
            };

        private static readonly string[] ListColumns =
            { "compatiblePhenomena", "requiredNodeTypes", "anchorNodeIds", "currentCoverageSet", "affectedWorldVariables" };

        public ConfigValue DecodeRow(ConfigCsvRow row)
        {
            var values = row.Cells.ToDictionary(v => v.Key, v => ConfigValue.Scalar(v.Value));
            foreach (var column in ListColumns)
                values[column] = ConfigValue.List(row[column].Split('|').Select(ConfigValue.Scalar), row[column]);
            values["worldEventOutputs"] = ConfigValue.List(row["worldEventOutputs"].Split('|').Select(entry =>
            {
                var parts = entry.Split('~');
                if (parts.Length != 2) throw new InvalidDataException("CHARTER_TABLE_INVALID: 世界事件必须完整包含事件与环境两个子字段。");
                return ConfigValue.Group(new Dictionary<string, ConfigValue>
                    { ["eventId"] = ConfigValue.Scalar(parts[0]), ["environmentProfileId"] = ConfigValue.Scalar(parts[1]) }, entry);
            }), row["worldEventOutputs"]);
            return ConfigValue.Group(values);
        }

        public IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row)
        {
            var values = row.Members.ToDictionary(v => v.Key, v => v.Value.RawToken);
            foreach (var column in ListColumns) values[column] = string.Join("|", row.At(column).Items.Select(v => v.RawToken));
            values["worldEventOutputs"] = string.Join("|", row.At("worldEventOutputs").Items.Select(v =>
                v.At("eventId").RawToken + "~" + v.At("environmentProfileId").RawToken));
            return values;
        }

        public IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            if (!snapshot.Tables.TryGetValue(TableId, out var table)) return Array.Empty<ConfigIssue>();
            return CharterEditorFields.Validate(table, () =>
            {
                var catalog = CharterEditorFields.Catalog();
                var definitions = WorldContentImporter.ParseCharterRuleDefinitions(CharterEditorFields.Lines(table), TableId, catalog.ReferenceCatalog);
                foreach (var definition in definitions) UnityEngine.Object.DestroyImmediate(definition);
            });
        }

        public IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot) => Array.Empty<ConfigEdit>();

        public IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> changedTables)
        {
            if (!changedTables.Contains(TableId)) return Array.Empty<ConfigImportStep>();
            return new[]
            {
                new ConfigImportStep
                {
                    Method = "WorldContentImporter.ImportCharterRuleDefinitions",
                    InputPaths = new[] { "Assets/DataConfig/CharterRuleDefinitions.csv", CharterEditorFields.CatalogPath },
                    ProductPaths = CharterEditorFields.Catalog().Definitions.Select(v =>
                        "Assets/Data/CharterRuleDefinitions/CharterRuleDefinition_" + v.ruleEntryId + ".asset").ToArray()
                },
                CharterSiteTable.SiteImportStep(),
                new ConfigImportStep
                {
                    Method = "SettlementContentImporter.ImportContentCatalog",
                    InputPaths = new[] { "Assets/DataConfig/Language.csv", "Assets/DataConfig/Settlements.csv", "Assets/DataConfig/Items.csv",
                        "Assets/DataConfig/Bounties.csv", "Assets/DataConfig/Enemies.csv", "Assets/DataConfig/AttackProfiles.csv",
                        "Assets/DataConfig/RealmCombatGrowth.csv", "Assets/DataConfig/CombatEffectTierRules.csv", "Assets/DataConfig/CombatActionEconomyRules.csv",
                        "Assets/Data/AppearanceProfiles/AppearanceProfile_none.asset", "Assets/Data/AttackProfiles/AttackProfile_basic_iron_sword.asset",
                        "Assets/Data/AttackProfiles/AttackProfile_basic_iron_blade.asset", "Assets/Data/CombatRuleSet.asset", CharterEditorFields.CatalogPath }
                        .Concat(CharterEditorFields.Catalog().Definitions.Select(v => "Assets/Data/CharterRuleDefinitions/CharterRuleDefinition_" + v.ruleEntryId + ".asset")).ToArray(),
                    ProductPaths = new[] { "Assets/Data/ContentCatalog/ContentCatalog.asset", "Assets/Data/Settlements/Settlement_guanzhong_city.asset",
                        "Assets/Data/Enemies/Enemy_enemy_shijiahou.asset", "Assets/Data/Bounties/Bounty_bounty_guanzhong_shijiahou.asset" }
                        .Concat(new[] { "item_lingshi_low", "item_shijia_piece", "item_iron_sword", "item_iron_blade", "item_huoxue_dan" }
                            .Select(id => "Assets/Data/Items/Item_" + id + ".asset"))
                        .Concat(new[] { "enemy_shijiahou", "enemy_fengsun", "enemy_yanweihu", "enemy_xueyingmoxiu", "enemy_huangyesanxiu" }
                            .Select(id => "Assets/Data/Characters/Char_Enemy_" + id + ".asset")).ToArray()
                }
            };
        }
    }

    // Presentation and snapshot plumbing shared only by the two charter adapters.
    // Domain rules remain in the existing parsers; no import or runtime transaction is called.
    internal static class CharterEditorFields
    {
        internal const string CatalogPath = "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset";
        internal static CharterRuleStaticCatalogData Catalog()
        {
            var catalog = WorldContentImporter.LoadCharterRuleStaticCatalog();
            if (!catalog.TryValidateDefinitions(out var reason))
                throw new InvalidDataException("CHARTER_REFERENCE_CATALOG_UNDECLARED: 册界静态目录无效：" + reason);
            return catalog;
        }

        internal static ConfigTableDefinition Table(string id, string name, ConfigFieldDefinition[] fields,
            IConfigRowCodec codec, string key, string label) => new ConfigTableDefinition
        {
            TableId = id, ChineseName = name, CsvPath = "Assets/DataConfig/" + id + ".csv",
            ExactColumns = fields.Select(f => f.Column).ToArray(), Fields = fields,
            RowKey = row => row[key], RowLabel = row => Name(row[label]) + "（" + row[key] + "）",
            Codec = codec, ReadOnlyReason = ""
        };

        internal static ConfigFieldDefinition Text(string column, string label) => new ConfigFieldDefinition
        {
            Column = column, ChineseName = label, Control = ConfigControl.Text, Editable = true, ReservedDelimiters = "|~,",
            Help = "本站点自有引用，仅使用字母、数字、下划线、连字符和点；不产生全局授权。"
        };

        internal static ConfigFieldDefinition Locked(string column, string label, string reason)
        {
            var field = Text(column, label);
            field.Editable = false; field.ReadOnlyReason = reason; field.Help = "";
            return field;
        }

        internal static ConfigFieldDefinition Enum(string column, string label, params string[] pairs) => new ConfigFieldDefinition
        {
            Column = column, ChineseName = label, Control = ConfigControl.Enum, Editable = true,
            Choices = () => Enumerable.Range(0, pairs.Length / 2).Select(i => new ConfigChoice(pairs[i * 2], pairs[i * 2 + 1])).ToArray()
        };

        internal static ConfigFieldDefinition Reference(string column, string label,
            Func<CharterRuleReferenceCatalog, IEnumerable<string>> source) => new ConfigFieldDefinition
        {
            Column = column, ChineseName = label, Control = ConfigControl.Reference, Editable = true, ReservedDelimiters = "|~,",
            Choices = () => source(Catalog().ReferenceCatalog).Select(id => new ConfigChoice(id, Name(id))).ToArray(),
            Help = "引用源：唯一册界静态目录。选择只保留稳定ID，不产生运行时授权。"
        };

        // Labels are presentation only. They never add IDs to the authoritative choice sets.
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["charter_entry_suifu_diji"] = "水府地纪", ["charter_site_old_water_station"] = "旧水驿",
            ["rule_family_five_element_chronicle"] = "五行纪规则族", ["element_water"] = "水",
            ["rain"] = "雨", ["drizzle"] = "细雨", ["commit_suifu_diji_positive_ecology"] = "水府地纪正向生态提交",
            ["commit_suifu_diji_negative_reallocation"] = "水府地纪负向调配提交",
            ["authority_suifu_kaihe_passage_and_seal_management_v1"] = "水府开阖通行与界印管理权限",
            ["node_type_charter"] = "册界节点", ["node_type_waterworks"] = "水工节点", ["node_type_river_wetland"] = "河流湿地节点",
            ["node_old_water_station_charter"] = "旧水驿册界节点", ["node_old_water_station_waterworks"] = "旧水驿水工节点",
            ["node_old_water_station_river_wetland"] = "旧水驿河流湿地节点", ["propagation_suifu_watershed"] = "水府流域传播边界",
            ["coverage_old_water_station_charter"] = "旧水驿册界覆盖", ["coverage_old_water_station_waterworks"] = "旧水驿水工覆盖",
            ["coverage_old_water_station_river_wetland"] = "旧水驿河流湿地覆盖", ["seasonal_precipitation_distribution"] = "季节降水分布",
            ["wetland_waterline_state"] = "湿地水位状态", ["water_element_spirit_flow"] = "水灵流动", ["aquatic_resource_yield"] = "水产资源产出",
            ["conflict_charter_water_basin"] = "流域册界冲突档案", ["event_suifu_water_redistribution"] = "水府水量再分配",
            ["event_suifu_downstream_supply_delay"] = "水府下游供给延迟", ["env_guanzhong_wild"] = "关中野外环境",
            ["relic_world_charter"] = "册界法宝", ["relic_taixuan_realm_seal"] = "太玄界印",
            ["authorization_suifu_water_basin_v1"] = "水府流域授权版本",
            ["authorization_taixuan_seal_old_water_station_management_v1"] = "太玄界印旧水驿管理授权版本"
        };

        internal static string Name(string id)
        {
            // Language is the display authority when a key exists; other labels are Editor presentation.
            var lines = File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig/Language.csv"));
            foreach (var line in lines.Where(v => !string.IsNullOrWhiteSpace(v) && !v.TrimStart().StartsWith("#")))
            {
                var cells = CsvTableReader.ParseRow(line);
                if (cells.Length == 2 && cells[0] == id) return cells[1];
            }
            return Names.TryGetValue(id, out var name) ? name : "已登记引用";
        }

        internal static string[] Lines(ConfigDraftTableSnapshot table)
        {
            var edits = new List<ConfigCellEdit>();
            foreach (var row in table.Rows)
            {
                var source = table.SourceRows[row.Key];
                var baseline = table.Definition.Codec.DecodeRow(source);
                var encoded = table.Definition.Codec.EncodeRow(row.Value);
                for (var i = 0; i < table.Definition.ExactColumns.Count; i++)
                {
                    var column = table.Definition.ExactColumns[i];
                    if (!baseline.At(column).Equals(row.Value.At(column)))
                        edits.Add(new ConfigCellEdit(source.PhysicalLine, i, encoded[column]));
                }
            }
            return CsvConfigDocument.Load(table.Document.EncodeEdits(edits)).Lines.Select(line => line.Text).ToArray();
        }

        internal static IReadOnlyList<ConfigIssue> Validate(ConfigDraftTableSnapshot table, Action validateDomain)
        {
            var issues = new List<ConfigIssue>(table.Issues);
            foreach (var row in table.Rows)
                foreach (var field in table.Definition.Fields)
                {
                    try { ConfigFieldGUI.ValidateValue(field, row.Value.At(field.Column)); }
                    catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException || e is InvalidDataException)
                    { issues.Add(new ConfigIssue(table.Definition.TableId, row.Key, field.Column, field.ChineseName + "：" + e.Message, "CHARTER_EDITOR_FIELD_INVALID")); }
                }
            if (table.Issues.Count > 0) return issues;
            try { validateDomain(); }
            catch (InvalidDataException e)
            {
                var separator = e.Message.IndexOf(':');
                var code = separator > 0 && e.Message.StartsWith("CHARTER_", StringComparison.Ordinal)
                    ? e.Message.Substring(0, separator) : "";
                var column = table.Definition.Fields.FirstOrDefault(f => e.Message.Contains("'" + f.Column + "'"))?.Column ?? "";
                issues.Add(new ConfigIssue(table.Definition.TableId, table.Rows.Count == 1 ? table.Rows.Keys.Single() : "", column,
                    "未通过原册界领域校验：" + e.Message, code));
            }
            return issues;
        }
    }
}
