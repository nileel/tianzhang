using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TianZhang.Combat;
using UnityEngine;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class CombatRuleTables : IConfigTableProvider
    {
        public IEnumerable<ConfigTableDefinition> GetTables()
        {
            yield return Definition("RealmCombatGrowth", "境界战斗成长", CombatRuleSet.RealmColumns,
                "结构版本|规则版本|境界|境界顺序|使用范围|行动力上限|基础移动效率|旧成长参照效率|各成长节点权重分子|移动节点权重分母", "realmId");
            yield return Definition("CombatEffectTierRules", "效果层级规则", CombatRuleSet.TierColumns,
                "结构版本|规则版本|术能层级|对应境界|参考行动力|参考灵力预算|物攻容量|魂攻容量|物防容量|魂防容量|恢复魂攻容量|原始灵力恢复容量|反应容量|参考行动间隔", "abilityTierId");
            yield return Definition("CombatActionEconomyRules", "行动消耗规则", CombatRuleSet.EconomyColumns,
                "结构版本|规则版本|记录类型|行动定义引用|层级引用|首次重复系数|第二次重复系数|第三次重复系数|后续重复上限系数|低两境系数|低一境系数|同境系数|高一境系数|高两境系数|相对状态上限|概率点上限|强控上限|爆发冷却倍数|控制冷却倍数|恢复冷却倍数|移动预算分子|移动预算分母|水系基础概率|水系最低概率|水系心神差除数|每层水系防御比例|水系防御层数上限|水系减伤换算分子|水系减伤换算分母|行动力系数分子|行动力系数分母|灵力系数分子|灵力系数分母|行动力费用覆写|净费下限覆写|灵力费用覆写|行动条惩罚|冷却类别|重用冷却覆写|伤害换算分子|伤害换算分母|伤害权重分子|伤害权重分母", "definitionRef");
        }

        private static ConfigTableDefinition Definition(string id, string name, string[] columns, string labels, string key)
        {
            var names = labels.Split('|');
            var fields = columns.Select((c, i) => CombatEditorFields.Number(c, names[i], "整数")).ToArray();
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (i < (id == "CombatActionEconomyRules" ? 5 : id == "RealmCombatGrowth" ? 5 : 4))
                {
                    f = CombatEditorFields.Text(f.Column, f.ChineseName, f.Column == "abilityTierId" && id == "CombatActionEconomyRules");
                    if (f.Column == "realmId") f = CombatEditorFields.Reference(f.Column, f.ChineseName, "Language", "id", "realm_");
                    if (f.Column == "abilityTierId") f = CombatEditorFields.Reference(f.Column, f.ChineseName, "CombatEffectTierRules", "abilityTierId", "", f.AllowEmpty);
                    if (f.Column == "usageScope") f = CombatEditorFields.Scope(f.Column, f.ChineseName);
                    if (f.Column == "recordKind") f = CombatEditorFields.Enum(f.Column, f.ChineseName, "global", "全局规则", "test_action", "参考行动规则");
                    if (f.Column == "definitionRef") f = CombatEditorFields.Enum(f.Column, f.ChineseName,
                        new[] { "action_economy" }.Concat(CombatRuleSet.DefinitionIds).SelectMany((v, n) => new[] { v,
                            new[] { "全局行动消耗", "物理术法", "法术术法", "水系术法", "太一术法", "太一符修术法", "太虚术法", "玉清术法", "玉清雷劫术法", "物理神通", "法术神通", "水系神通", "太一神通", "太一符修神通", "太虚神通", "玉清神通", "玉清雷劫神通", "苦行行动" }[n] }).ToArray());
                    CombatEditorFields.Lock(f, "稳定身份或版本，按现有规则固定");
                    if (f.Column == "ruleSetVersion") f.ReservedDelimiters = "";
                }
                if (f.Column.EndsWith("Units", StringComparison.Ordinal)) { f.Scale = 1000m; f.Unit = "行动力"; }
                if (f.Column.EndsWith("MilliUnits", StringComparison.Ordinal)) { f.Scale = 1000m; f.Unit = "移动效率"; }
                if (f.Column.EndsWith("Permille", StringComparison.Ordinal)) f.Unit = "千分比";
                if (f.Column.EndsWith("Ticks", StringComparison.Ordinal)) f.Unit = "游戏刻";
                if (f.Column.EndsWith("Capacity", StringComparison.Ordinal) || f.Column == "waterMindDifferenceDivisor") f.Unit = "属性点";
                if (f.Column == "spiritBudgetReference" || f.Column == "spiritCostOverride") f.Unit = "灵力点";
                if (f.Column.EndsWith("Multiplier", StringComparison.Ordinal)) f.Unit = "倍";
                if (f.Column.EndsWith("Numerator", StringComparison.Ordinal) || f.Column.EndsWith("Denominator", StringComparison.Ordinal))
                { f.Unit = "无量纲分数"; f.Help = "分子与分母分别编辑；未改声明保持原样，不自动约分。"; }
                if (f.Column == "legacyBaseMovementEfficiencyMilliUnits") CombatEditorFields.Lock(f, "原始成长参照，不参与编辑联动");
                if (f.Column == "movementNodeWeightNumerators")
                {
                    f = CombatEditorFields.List(f.Column, f.ChineseName, CombatEditorFields.Number("weight", "节点权重分子", "正整数"), "1");
                    f.AllowEmpty = true; f.CanEdit = row => row.At("realmId").RawToken != "realm_fanren";
                    f.EmptyMeaning = "凡人没有成长节点"; f.ReadOnlyReason = "凡人权重列表固定为空";
                    f.Help = "顺序不变；练气9项，筑基/金丹3项，元婴/化神4项；总和必须等于分母。";
                }
                if (f.Column == "baseMovementEfficiencyMilliUnits")
                    f.Help = "凡人移动效率的唯一编辑入口。保存预览将同步创角default所有行及角色/怪物镜像；旧成长参照不变。普通地表报价由现有移动规则计算。";
                if (id == "CombatActionEconomyRules" && i >= 5)
                {
                    bool global = i < 29;
                    f.AllowEmpty = true; f.CanEdit = row => row.At("recordKind").RawToken == (global ? "global" : "test_action");
                    f.ReadOnlyReason = "当前记录类型不适用，保持空值";
                    f.EmptyMeaning = f.Column.Contains("Override") ? "未指定：按规则计算；零是显式覆写" : "不适用；适用分支必须填写";
                    if (f.Column == "cooldownClass")
                    {
                        f.Control = ConfigControl.Enum; f.Choices = CombatEditorFields.CooldownChoices;
                    }
                }
                fields[i] = f;
            }
            var table = CombatEditorFields.Table(id, name, columns, fields, new CombatRuleCodec(id), key);
            if (id == "CombatActionEconomyRules")
            {
                table.RowKey = row => row["recordKind"] + "/" + row["definitionRef"] + "/" + row["abilityTierId"];
                table.RowLabel = row => fields[3].Choices().Single(c => c.Id == row["definitionRef"]).ChineseName + "（" + row["abilityTierId"] + "）";
                table.GroupingFields = new[] { "recordKind", "abilityTierId" };
            }
            return table;
        }
    }

    internal sealed class CombatRuleCodec : CombatScalarCodec
    {
        internal CombatRuleCodec(string id) : base(id) { }
        public override ConfigValue DecodeRow(ConfigCsvRow row)
        {
            var decoded = base.DecodeRow(row);
            return Id != "RealmCombatGrowth" ? decoded : decoded.With("movementNodeWeightNumerators",
                ConfigValue.List(row["movementNodeWeightNumerators"] == "" ? Array.Empty<ConfigValue>() : row["movementNodeWeightNumerators"].Split('|').Select(ConfigValue.Scalar),
                    row["movementNodeWeightNumerators"], row["movementNodeWeightNumerators"] != ""));
        }
        public override IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row)
        {
            var values = base.EncodeRow(row).ToDictionary(v => v.Key, v => v.Value);
            if (Id == "RealmCombatGrowth") values["movementNodeWeightNumerators"] = string.Join("|", row.At("movementNodeWeightNumerators").Items.Select(v => v.RawToken));
            return values;
        }
        public override IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            var issues = base.ValidateDraft(snapshot).ToList();
            if (issues.Count == 0) CombatEditorFields.Check(issues, Id, "", "", "战斗规则校验失败", () => CombatEditorFields.Rules(snapshot));
            return issues;
        }
        public override IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot)
        {
            if (Id != "RealmCombatGrowth" || !snapshot.Tables.TryGetValue(Id, out var growth)) return Array.Empty<ConfigEdit>();
            var edits = new List<ConfigEdit>();
            foreach (var id in new[] { "Characters", "Enemies", "CharacterCreationPointBuy" })
            {
                if (!snapshot.Tables.TryGetValue(id, out var target) || !target.Definition.IsAdapted) continue;
                foreach (var row in target.Rows)
                {
                    if (id == "CharacterCreationPointBuy" && row.Value.At("configId").RawToken != "default") continue;
                    string realm = id == "CharacterCreationPointBuy" ? "realm_fanren" : row.Value.At(id == "Characters" ? "realmId" : "realm").RawToken;
                    var sourceRow = growth.Rows.SingleOrDefault(r => r.Value.At("realmId").RawToken == realm);
                    var source = sourceRow.Value;
                    if (source == null) continue;
                    foreach (var c in new[] { "actionPointMaximumUnits", "movementEfficiencyMilliUnits" })
                    {
                        string sourceColumn = c == "actionPointMaximumUnits" ? c : "baseMovementEfficiencyMilliUnits";
                        if (!snapshot.Edits.Any(e => e.TableId == Id && e.RowKey == sourceRow.Key && e.FieldPath == sourceColumn) &&
                            !snapshot.Edits.Any(e => e.TableId == id && e.RowKey == row.Key && e.FieldPath == (id == "Characters" ? "realmId" : "realm"))) continue;
                        var value = source.At(sourceColumn);
                        if (!int.TryParse(value.RawToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int expected) ||
                            !int.TryParse(row.Value.At(c).RawToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int actual) || actual != expected)
                            edits.Add(new ConfigEdit(id, row.Key, c, value));
                    }
                }
            }
            return edits;
        }
    }

    internal class CombatScalarCodec : IConfigRowCodec
    {
        protected readonly string Id;
        internal CombatScalarCodec(string id) { Id = id; }
        public virtual ConfigValue DecodeRow(ConfigCsvRow row) => ConfigValue.Group(row.Cells.ToDictionary(v => v.Key, v => ConfigValue.Scalar(v.Value)));
        public virtual IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row) => row.Members.ToDictionary(v => v.Key, v => v.Value.RawToken);
        public virtual IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot) => CombatEditorFields.ValidateFields(Id, snapshot);
        public virtual IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot) => Array.Empty<ConfigEdit>();
        public IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> changedTables) =>
            changedTables.Contains(Id) ? CombatEditorFields.ImportSteps(Id) : Array.Empty<ConfigImportStep>();
    }

    // Presentation and deterministic projections only. Rules stay in the existing domain parsers.
    internal static class CombatEditorFields
    {
        internal static ConfigTableDefinition Table(string id, string name, string[] columns, ConfigFieldDefinition[] fields, IConfigRowCodec codec, string key) =>
            new ConfigTableDefinition { TableId = id, ChineseName = name, CsvPath = "Assets/DataConfig/" + id + ".csv", ExactColumns = columns,
                Fields = fields, Codec = codec, RowKey = r => r[key], RowLabel = r => Name(r[key]) + "（" + r[key] + "）", ReadOnlyReason = "" };
        internal static ConfigFieldDefinition Text(string c, string n, bool empty = false) =>
            new ConfigFieldDefinition { Column = c, ChineseName = n, Control = ConfigControl.Text, Editable = true, AllowEmpty = empty, ReservedDelimiters = "|:@/" };
        internal static ConfigFieldDefinition Number(string c, string n, string unit, bool integer = true, bool empty = false) =>
            new ConfigFieldDefinition { Column = c, ChineseName = n, Unit = unit, Control = integer ? ConfigControl.Integer : ConfigControl.Decimal, Editable = true, AllowEmpty = empty };
        internal static ConfigFieldDefinition Lock(ConfigFieldDefinition f, string reason) { f.Editable = false; f.ReadOnlyReason = reason; return f; }
        internal static ConfigFieldDefinition Enum(string c, string n, params string[] pairs) => new ConfigFieldDefinition
        { Column = c, ChineseName = n, Control = ConfigControl.Enum, Editable = true,
            Choices = () => Enumerable.Range(0, pairs.Length / 2).Select(i => new ConfigChoice(pairs[i * 2], pairs[i * 2 + 1])).ToArray() };
        internal static ConfigFieldDefinition Scope(string c, string n) => Enum(c, n, "player", "玩家内容", "reserved", "预留内容");
        internal static IReadOnlyList<ConfigChoice> CooldownChoices() => Enum("", "", "basic_repeat", "普通重复", "burst", "爆发", "control", "控制", "recovery", "恢复").Choices();
        internal static ConfigFieldDefinition Reference(string c, string n, string source, string key, string prefix = "", bool empty = false) =>
            new ConfigFieldDefinition { Column = c, ChineseName = n, Control = ConfigControl.Reference, Editable = true, AllowEmpty = empty,
                Help = "引用源：" + source + "；中文重名仍保留稳定ID", ReservedDelimiters = "|:@/",
                Choices = () => Rows(source, CharacterEditorFields.WindowSnapshot()).Where(r => r[key].StartsWith(prefix, StringComparison.Ordinal))
                    .Select(r => new ConfigChoice(r[key], source == "Language" ? r["zh_CN"] : Name(r[key]))).ToArray() };
        internal static ConfigFieldDefinition List(string c, string n, ConfigFieldDefinition element, string initial) => new ConfigFieldDefinition
        { Column = c, ChineseName = n, Control = ConfigControl.List, Editable = true, Element = element, NewElement = () => ConfigValue.Scalar(initial) };
        internal static string Name(string id) => id.StartsWith("tier_", StringComparison.Ordinal) ?
            CharacterEditorFields.Name("realm_" + id.Substring(5)) + "效果层级" : CharacterEditorFields.Name(id);
        internal static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string id, ConfigDraftSnapshot snapshot)
        {
            if (snapshot != null && snapshot.Tables.TryGetValue(id, out var table))
            {
                if (table.Issues.Count > 0) throw new InvalidDataException("关联表结构无效：" + id);
                if (table.Definition.IsAdapted) return table.Rows.Values.Select(table.Definition.Codec.EncodeRow).ToArray();
                return DocumentRows(id, table.Document);
            }
            return DocumentRows(id, CsvConfigDocument.Load(File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig/" + id + ".csv"))));
        }
        private static IReadOnlyList<IReadOnlyDictionary<string, string>> DocumentRows(string id, CsvConfigDocument document)
        {
            var definition = new ConfigTableDefinition { TableId = id, HeaderMode = id == "Language" ? ConfigHeaderMode.None : ConfigHeaderMode.Header,
                ExactColumns = id == "Language" ? new[] { "id", "zh_CN" } : document.Lines.First(l => !l.IsTrivia).Cells.Select(c => c.Value).ToArray() };
            if (document.ValidateStructure(definition).Count != 0) throw new InvalidDataException("关联表结构无效：" + id);
            return document.GetRows(definition).Select(r => r.Cells).ToArray();
        }
        internal static string[][] Tokens(string id, ConfigDraftSnapshot snapshot)
        {
            var columns = id == "RealmCombatGrowth" ? CombatRuleSet.RealmColumns : id == "CombatEffectTierRules" ? CombatRuleSet.TierColumns :
                id == "CombatActionEconomyRules" ? CombatRuleSet.EconomyColumns : CombatRuleSet.AttackColumns;
            return new[] { columns }.Concat(Rows(id, snapshot).Select(r => columns.Select(c => r[c]).ToArray())).ToArray();
        }
        internal static CombatRuleSet Rules(ConfigDraftSnapshot snapshot)
        {
            var ids = new[] { "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules" };
            var bytes = ids.ToDictionary(id => id + ".csv", id => System.Text.Encoding.UTF8.GetBytes(string.Join("\n", Tokens(id, snapshot).Select(CsvLine))));
            return CombatRuleSet.Parse(Tokens(ids[0], snapshot), Tokens(ids[1], snapshot), Tokens(ids[2], snapshot), CombatRuleSet.ComputeSourceDigest(bytes));
        }
        internal static string CsvLine(IEnumerable<string> tokens) => string.Join(",", tokens.Select(v => v.Contains(',') ? "\"" + v + "\"" : v));
        internal static void Check(List<ConfigIssue> issues, string id, string key, string field, string label, Action action)
        {
            try { action(); }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException || e is InvalidDataException)
            { issues.Add(new ConfigIssue(id, key, field, label + "：" + e.Message, e is InvalidDataException ? e.Message.Split(':')[0] : "combat_editor_field_invalid")); }
        }
        internal static IReadOnlyList<ConfigIssue> ValidateFields(string id, ConfigDraftSnapshot snapshot)
        {
            var issues = new List<ConfigIssue>();
            if (!snapshot.Tables.TryGetValue(id, out var table)) return issues;
            issues.AddRange(table.Issues);
            foreach (var row in table.Rows)
                foreach (var field in table.Definition.Fields)
                    Check(issues, id, row.Key, field.Column, field.ChineseName, () =>
                    {
                        ValidateNode(field, row.Value.At(field.Column), snapshot);
                        if (!field.IsEditable(row.Value) && !row.Value.At(field.Column).Equals(table.Definition.Codec.DecodeRow(table.SourceRows[row.Key]).At(field.Column)) &&
                            field.Column != "actionPointMaximumUnits" && field.Column != "movementEfficiencyMilliUnits" &&
                            !(id == "CharacterCreationPointBuy" && new[] { "purchasePointLimit", "minValue", "baseValue", "maxValue" }.Contains(field.Column)))
                            throw new InvalidDataException("combat_editor_read_only_changed");
                    });
            return issues;
        }
        private static void ValidateNode(ConfigFieldDefinition f, ConfigValue value, ConfigDraftSnapshot snapshot)
        {
            if (f.Control == ConfigControl.Group || f.Control == ConfigControl.List)
            {
                if (value.Kind != (f.Control == ConfigControl.List ? ConfigNodeKind.List : ConfigNodeKind.Group)) throw new InvalidDataException("combat_editor_node_kind");
                if (!value.HasValue && !f.AllowEmpty) throw new InvalidDataException("combat_editor_required");
                if (f.Control == ConfigControl.List) foreach (var item in value.Items) ValidateNode(f.Element, item, snapshot);
                else
                {
                    if (value.Members.Count != f.Children.Count) throw new InvalidDataException("combat_editor_group_fields");
                    foreach (var child in f.Children) ValidateNode(child, value.At(child.Column), snapshot);
                }
                return;
            }
            if (f.Control == ConfigControl.Reference)
            {
                ConfigFieldGUI.ValidateValue(Text(f.Column, f.ChineseName, f.AllowEmpty), value);
                if (!value.HasValue) return;
                bool tier = f.Column == "abilityTierId";
                string prefix = f.Column == "sourceAffiliation" ? "faction_" : f.Column == "realmId" || f.Column == "realmReq" || f.Column == "realmRequirementId" ? "realm_" : "";
                if (!value.RawToken.StartsWith(prefix, StringComparison.Ordinal) || !Rows(tier ? "CombatEffectTierRules" : "Language", snapshot)
                    .Any(r => r[tier ? "abilityTierId" : "id"] == value.RawToken)) throw new InvalidDataException("combat_editor_reference_unknown");
                return;
            }
            ConfigFieldGUI.ValidateValue(f, value);
            if (f.Control == ConfigControl.Decimal && value.HasValue)
            {
                float parsed = float.Parse(value.RawToken, CultureInfo.InvariantCulture);
                if (float.IsNaN(parsed) || float.IsInfinity(parsed)) throw new InvalidDataException("combat_editor_float_not_finite");
            }
        }
        internal static IReadOnlyList<ConfigImportStep> ImportSteps(string id)
        {
            ConfigImportStep Step(string method, string[] inputs, string[] products) => new ConfigImportStep
            { Method = method, InputPaths = inputs.Select(v => "Assets/DataConfig/" + v + ".csv").ToArray(), ProductPaths = products };
            string[] Products(string table, string directory, string prefix, string key) => Rows(table, null)
                .Select(r => "Assets/Data/" + directory + "/" + prefix + AssetCommitter.SanitizeName(r[key]) + ".asset").ToArray();
            var point = Step("SettlementContentImporter.ImportCharacterCreationPointBuy", new[] { "CharacterCreationPointBuy", "RealmCombatGrowth" },
                new[] { "Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset" });
            point.InputPaths = point.InputPaths.Concat(new[] { "Assets/Data/CombatRuleSet.asset" }).ToArray();
            if (id == "CharacterCreationPointBuy") return new[] { point };
            if (id == "Spells" || id == "Skills") return new[] { Step("CombatContentImporter.Import" + id, new[] { id, "Language" }, Products(id, id, id == "Spells" ? "Spell_" : "Skill_", "name")) };
            var attack = Step("CombatContentImporter.ImportAttackProfiles", new[] { "RealmCombatGrowth", "CombatEffectTierRules", "CombatActionEconomyRules", "AttackProfiles", "Language" },
                new[] { "Assets/Data/CombatRuleSet.asset", "Assets/Data/ContentCatalog/ContentCatalog.asset" }.Concat(Products("AttackProfiles", "AttackProfiles", "AttackProfile_", "attackProfileId")).ToArray());
            attack.InputPaths = attack.InputPaths.Concat(new[] { "Assets/Data/CombatRuleSet.asset", "Assets/Data/ContentCatalog/ContentCatalog.asset" }).ToArray();
            return new[] { attack }
                .Concat(CharacterImportDescriptions.For("Characters")).Concat(CharacterImportDescriptions.For("Enemies"))
                .Concat(new[] { point }).GroupBy(s => s.Method).Select(g => g.First()).ToArray();
        }
    }
}
