using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class CharacterTables : IConfigTableProvider
    {
        public IEnumerable<ConfigTableDefinition> GetTables()
        {
            foreach (var id in new[] { "Characters", "Enemies", "Items", "AppearanceProfiles", "Language" })
                yield return CharacterTableCodec.Definition(id);
        }
    }

    internal sealed class CharacterTableCodec : IConfigRowCodec
    {
        private readonly string tableId;
        private CharacterTableCodec(string id) { tableId = id; }
        private const string Stable = "稳定身份，不随中文名称改变";
        private const string Approved = "现有领域解析器锁定的批准投影，只读展示";
        private const string Mirror = "由境界成长联动，不能独立修改";
        private static readonly string[] StatColumns = { "rootBone", "physique", "spirit", "mind", "reaction", "talent" };
        private static readonly string[] StatNames = { "根骨", "体魄", "灵力属性", "心神", "反应", "悟性" };
        private static readonly string[] RateColumns = { "blockRate", "blockReduction", "soulShieldRate", "soulShieldReduction", "dodgeRate", "critRate", "critDamage", "hitRateBonus" };
        private static readonly string[] RateNames = { "格挡率", "格挡减伤", "魂盾率", "魂盾减伤", "闪避率", "暴击率", "暴击伤害加成", "命中加成" };
        private static readonly string[] LockedLanguage = { "settlement_guanzhong_city", "settlement_feature_bounty_board", "item_lingshi_low_description",
            "item_shijia_piece_description", "bounty_guanzhong_shijiahou_title", "bounty_guanzhong_shijiahou_description", "charter_site_old_water_station" };

        internal static ConfigTableDefinition Definition(string id)
        {
            var fields = new List<ConfigFieldDefinition>();
            string key = "name", title;
            if (id == "Characters" || id == "Enemies")
            {
                bool enemy = id == "Enemies";
                title = enemy ? "怪物" : "角色";
                fields.Add(CharacterEditorFields.Lock(CharacterEditorFields.Text("name", title + "ID"), Stable));
                if (enemy)
                {
                    fields.Add(CharacterEditorFields.Lock(CharacterEditorFields.Language("type", "怪物类型"), "正式行批准投影；其他模板尚未消费此描述"));
                    fields.Add(CharacterEditorFields.Lock(CharacterEditorFields.Language("aiType", "AI策略"), "正式行批准投影；其他模板尚未消费此描述"));
                }
                var realm = CharacterEditorFields.Language(enemy ? "realm" : "realmId", "境界", "realm_");
                if (enemy) { realm.Editable = false; realm.ReadOnlyReason = "正式行境界锁定；其他模板此列按冻结合同只读"; }
                else realm.Choices = () => CharacterEditorFields.Rows("AttackProfiles").Where(r => r["unarmedFamilyId"] == "body_human")
                    .Select(r => r["unarmedRealmId"]).Distinct().Select(v => new ConfigChoice(v, CharacterEditorFields.Name(v))).ToArray();
                fields.Add(realm);
                fields.Add(CharacterEditorFields.Number("realmMultiplier", "境界倍率", "倍"));
                for (int i = 0; i < StatColumns.Length; i++) fields.Add(CharacterEditorFields.Number(StatColumns[i], StatNames[i], "属性点", true));
                for (int i = 0; i < RateColumns.Length; i++) fields.Add(CharacterEditorFields.Number(RateColumns[i], RateNames[i], "百分点"));
                if (!enemy) fields.Add(CharacterEditorFields.Reference("gongFaName", "功法", "GongFa", "name", true));
                fields.Add(CharacterEditorFields.IdList("equippedSpells", "装备术法", "Spells"));
                if (!enemy) fields.Add(CharacterEditorFields.IdList("equippedSkills", "装备神通", "Skills"));
                if (enemy)
                {
                    fields.Add(DropField(false));
                    fields.Add(CharacterEditorFields.Lock(CharacterEditorFields.Language("description", "说明文本"), "正式行描述锁定；其他模板尚未消费此描述"));
                    fields.Add(CharacterEditorFields.Lock(CharacterEditorFields.Enum("contentScope", "正式内容范围", "guanzhong", "关中正式内容"), Approved));
                    fields.Last().AllowEmpty = true;
                    fields.Add(DropField(true));
                    fields.Add(CharacterEditorFields.Lock(CharacterEditorFields.Text("unarmedBasicAttackProfileId", "退役直接普攻引用", true), "退役列必须为空，不授予攻击"));
                }
                var ap = CharacterEditorFields.Lock(CharacterEditorFields.Number("actionPointMaximumUnits", "行动力上限", "行动力", true), Mirror);
                ap.Scale = 1000m; fields.Add(ap);
                var movement = CharacterEditorFields.Lock(CharacterEditorFields.Number("movementEfficiencyMilliUnits", "移动效率", "移动效率", true), Mirror);
                movement.Scale = 1000m; fields.Add(movement);
                fields.Add(CharacterEditorFields.Lock(CharacterEditorFields.Enum("unarmedAttackFamilyId", "身体攻击族", "body_human", "人形身体", "body_shijiahou", "石甲兽身体"), "按当前角色身份固定；其他怪物未指定"));
                fields.Last().AllowEmpty = enemy;
                if (enemy) fields.Add(CharacterEditorFields.Lock(CharacterEditorFields.Number("combatStatScale", "敌人战斗属性倍率", "倍"), Approved));
            }
            else if (id == "Items")
            {
                title = "物品"; key = "itemId";
                fields.Add(CharacterEditorFields.Text(key, "物品ID"));
                fields.Add(CharacterEditorFields.Language("displayNameKey", "名称文本"));
                fields.Add(CharacterEditorFields.Language("descriptionKey", "描述文本"));
                fields.Add(CharacterEditorFields.Enum("contentScope", "内容范围", "content_scope_production", "正式生产内容"));
                fields.Add(CharacterEditorFields.Enum("itemCategory", "物品类别", "basic_resource", "基础资源", "monster_material", "怪物材料", "weapon", "武器", "pill", "丹药"));
                fields.Add(CharacterEditorFields.Number("maxStack", "堆叠上限", "件", true));
                fields.Add(CharacterEditorFields.Enum("equipmentSlotId", "装备槽", "main_hand", "主手"));
                fields.Add(CharacterEditorFields.Language("equipmentRealmRequirementId", "装备境界", "realm_"));
                fields.Add(CharacterEditorFields.Reference("equipmentBasicAttackProfileId", "装备普攻", "AttackProfiles", "attackProfileId"));
                fields.Add(CharacterEditorFields.Enum("combatUseEffectId", "战斗使用效果", "restore_current_health", "恢复当前生命"));
                fields.Add(CharacterEditorFields.Language("combatUseRealmId", "使用境界", "realm_"));
                fields.Add(CharacterEditorFields.Enum("combatUseTargetMode", "使用目标", "self", "自身"));
                fields.Add(CharacterEditorFields.Number("combatUseHealthRecovery", "生命恢复量", "点", true));
                fields.Add(CharacterEditorFields.Number("initialGrantQuantity", "初始给予数量", "件", true));
                for (int i = 0; i < fields.Count; i++)
                { CharacterEditorFields.Lock(fields[i], i == 0 ? Stable : Approved); if (i >= 6) fields[i].AllowEmpty = true; }
            }
            else if (id == "AppearanceProfiles")
            {
                title = "外观方案"; key = "appearanceProfileId";
                fields.Add(CharacterEditorFields.Lock(CharacterEditorFields.Enum(key, "外观方案ID", "none", "无外观"), "当前仅批准无外观方案，不创建资源引用"));
            }
            else
            {
                title = "显示文本"; key = "id";
                fields.Add(CharacterEditorFields.Lock(CharacterEditorFields.Text(key, "文本ID"), Stable));
                var text = CharacterEditorFields.Text("zh_CN", "中文文本");
                text.ReservedDelimiters = "";
                text.CanEdit = row => !LockedLanguage.Contains(row.At("id").RawToken);
                text.ReadOnlyReason = "正式目录或册界站点锁定的中文文本";
                fields.Add(text);
            }
            var definition = CharacterEditorFields.Table(id, title, key, fields.ToArray(), new CharacterTableCodec(id));
            if (id == "Language") definition.HeaderMode = ConfigHeaderMode.None;
            return definition;
        }

        private static ConfigFieldDefinition DropField(bool formal)
        {
            var children = formal ? new[] { CharacterEditorFields.Reference("itemId", "物品", "Items", "itemId"),
                CharacterEditorFields.Number("chance", "掉落概率", "百分点"), CharacterEditorFields.Number("quantity", "数量", "件", true) }
                : new[] { CharacterEditorFields.Text("itemId", "旧物品标识"), CharacterEditorFields.Text("legacyValue", "旧候选值") };
            foreach (var child in children) { CharacterEditorFields.Lock(child, formal ? Approved : "未定义为正式概率或数量"); child.ReservedDelimiters = "|:@"; }
            return CharacterEditorFields.Lock(new ConfigFieldDefinition
            {
                Column = formal ? "dropEntries" : "dropTable", ChineseName = formal ? "正式掉落" : "旧掉落候选",
                Control = ConfigControl.List, AllowEmpty = true, EmptyMeaning = "无掉落条目",
                Element = CharacterEditorFields.Lock(new ConfigFieldDefinition { ChineseName = "掉落条目", Control = ConfigControl.Group, Children = children }, Approved),
                Help = formal ? "物品、概率和数量分别展示，当前为固定批准投影" : "旧候选值仅展示原词元，不参与正式掉落"
            }, formal ? Approved : "当前没有正式消费者，不解释或联动旧值");
        }

        public ConfigValue DecodeRow(ConfigCsvRow row)
        {
            var values = row.Cells.ToDictionary(v => v.Key, v => ConfigValue.Scalar(v.Value));
            foreach (var column in new[] { "equippedSpells", "equippedSkills" }.Where(values.ContainsKey))
                values[column] = CharacterEditorFields.DecodeList(row[column], ConfigValue.Scalar);
            foreach (var column in new[] { "dropTable", "dropEntries" }.Where(values.ContainsKey))
            {
                bool formal = column == "dropEntries";
                values[column] = CharacterEditorFields.DecodeList(row[column], token =>
                {
                    var parts = token.Split(formal ? '@' : ':');
                    if (parts.Length != (formal ? 3 : 2)) throw new InvalidDataException("掉落条目不符合本列真实语法：" + column);
                    var members = new Dictionary<string, ConfigValue> { ["itemId"] = ConfigValue.Scalar(parts[0]) };
                    members[formal ? "chance" : "legacyValue"] = ConfigValue.Scalar(parts[1]);
                    if (formal) members["quantity"] = ConfigValue.Scalar(parts[2]);
                    return ConfigValue.Group(members, token);
                });
            }
            return ConfigValue.Group(values);
        }

        public IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row)
        {
            return row.Members.ToDictionary(v => v.Key, v => v.Value.Kind == ConfigNodeKind.Scalar ? v.Value.RawToken :
                string.Join("|", v.Value.Items.Select(item => item.Kind == ConfigNodeKind.Scalar ? item.RawToken :
                    v.Key == "dropEntries" ? item.At("itemId").RawToken + "@" + item.At("chance").RawToken + "@" + item.At("quantity").RawToken :
                    item.At("itemId").RawToken + ":" + item.At("legacyValue").RawToken)));
        }

        public IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            if (!snapshot.Tables.TryGetValue(tableId, out var table)) return Array.Empty<ConfigIssue>();
            var issues = CharacterEditorFields.ValidateFields(table, snapshot);
            foreach (var row in table.Rows)
            {
                if (tableId == "Characters" || tableId == "Enemies")
                {
                    string realm = row.Value.At(tableId == "Characters" ? "realmId" : "realm").RawToken;
                    foreach (var column in new[] { "actionPointMaximumUnits", "movementEfficiencyMilliUnits" })
                    {
                        string source = column == "actionPointMaximumUnits" ? column : "baseMovementEfficiencyMilliUnits";
                        var growth = CharacterEditorFields.Rows("RealmCombatGrowth", snapshot).SingleOrDefault(v => v["realmId"] == realm);
                        if (growth == null || !SameInteger(growth[source], row.Value.At(column).RawToken))
                            issues.Add(new ConfigIssue(tableId, row.Key, column, "境界成长镜像不一致，请由必要关联编辑更新", "realm_growth_checksum"));
                    }
                }
                if (tableId == "AppearanceProfiles" && row.Key != "none")
                    issues.Add(new ConfigIssue(tableId, row.Key, "appearanceProfileId", "当前只允许无外观方案", "appearance_unapproved"));
                if (tableId == "Language" && string.IsNullOrWhiteSpace(row.Value.At("zh_CN").RawToken))
                    issues.Add(new ConfigIssue(tableId, row.Key, "zh_CN", "中文文本不能为空", "language_empty"));
            }
            return issues;
        }

        public IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot)
        {
            if (tableId != "Characters" || !snapshot.Tables.TryGetValue(tableId, out var table)) return Array.Empty<ConfigEdit>();
            var edits = new List<ConfigEdit>();
            foreach (var row in table.Rows.Where(r => snapshot.Edits.Any(e => e.TableId == tableId && e.RowKey == r.Key && e.FieldPath == "realmId")))
            {
                var growth = CharacterEditorFields.Rows("RealmCombatGrowth", snapshot).SingleOrDefault(r => r["realmId"] == row.Value.At("realmId").RawToken);
                if (growth == null) continue;
                foreach (var column in new[] { "actionPointMaximumUnits", "movementEfficiencyMilliUnits" })
                {
                    var token = growth[column == "actionPointMaximumUnits" ? column : "baseMovementEfficiencyMilliUnits"];
                    if (!SameInteger(row.Value.At(column).RawToken, token)) edits.Add(new ConfigEdit(tableId, row.Key, column, ConfigValue.Scalar(token)));
                }
            }
            return edits;
        }

        private static bool SameInteger(string left, string right) => int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) &&
            int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) && a == b;

        public IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> changedTables) =>
            changedTables.Contains(tableId) ? CharacterImportDescriptions.For(tableId) : Array.Empty<ConfigImportStep>();
    }

    // This group's presentation/codec helpers. They read CSV or the actual window's immutable draft;
    // they never own domain rules, save files, import assets, or retain a second configuration store.
    internal static class CharacterEditorFields
    {
        internal static ConfigTableDefinition Table(string id, string name, string key, ConfigFieldDefinition[] fields, IConfigRowCodec codec) =>
            new ConfigTableDefinition { TableId = id, ChineseName = name, CsvPath = "Assets/DataConfig/" + id + ".csv",
                ExactColumns = fields.Select(f => f.Column).ToArray(), Fields = fields, Codec = codec, RowKey = r => r[key],
                RowLabel = r => (id == "Language" ? r["zh_CN"] : Name(r[key])) + "（" + r[key] + "）", ReadOnlyReason = "" };

        internal static ConfigFieldDefinition Text(string column, string name, bool empty = false) => new ConfigFieldDefinition
        { Column = column, ChineseName = name, Control = ConfigControl.Text, Editable = true, AllowEmpty = empty, ReservedDelimiters = "|:@/" };
        internal static ConfigFieldDefinition Number(string column, string name, string unit, bool integer = false) => new ConfigFieldDefinition
        { Column = column, ChineseName = name, Unit = unit, Control = integer ? ConfigControl.Integer : ConfigControl.Decimal, Editable = true };
        internal static ConfigFieldDefinition Lock(ConfigFieldDefinition field, string reason)
        { field.Editable = false; field.ReadOnlyReason = reason; return field; }
        internal static ConfigFieldDefinition Enum(string column, string name, params string[] pairs) => new ConfigFieldDefinition
        { Column = column, ChineseName = name, Control = ConfigControl.Enum, Editable = true,
            Choices = () => Enumerable.Range(0, pairs.Length / 2).Select(i => new ConfigChoice(pairs[2 * i], pairs[2 * i + 1])).ToArray() };
        internal static ConfigFieldDefinition Reference(string column, string name, string table, string key, bool empty = false) => new ConfigFieldDefinition
        { Column = column, ChineseName = name, Control = ConfigControl.Reference, Editable = true, AllowEmpty = empty, ReservedDelimiters = "|:@/",
            Choices = () => Rows(table).Select(r => new ConfigChoice(r[key], Name(r[key]))).ToArray(), Help = "引用源：" + table + "；只保存稳定ID" };
        internal static ConfigFieldDefinition Language(string column, string name, string prefix = "") => new ConfigFieldDefinition
        { Column = column, ChineseName = name, Control = ConfigControl.Reference, Editable = true, ReservedDelimiters = "|:@/",
            Choices = () => Rows("Language").Where(r => r["id"].StartsWith(prefix, StringComparison.Ordinal))
                .Select(r => new ConfigChoice(r["id"], r["zh_CN"])).ToArray(), Help = "引用源：显示文本；中文重名仍保留各自ID" };
        internal static ConfigFieldDefinition IdList(string column, string name, string table)
        {
            var element = Reference("item", "条目引用", table, "name");
            return new ConfigFieldDefinition { Column = column, ChineseName = name, Control = ConfigControl.List, Editable = true,
                AllowEmpty = true, Element = element, NewElement = () => ConfigValue.Scalar(element.Choices().First().Id), EmptyMeaning = "空列表" };
        }
        internal static ConfigValue DecodeList(string raw, Func<string, ConfigValue> decode) =>
            ConfigValue.List(raw.Length == 0 ? Array.Empty<ConfigValue>() : raw.Split('|').Select(decode), raw, raw.Length > 0);

        internal static ConfigDraftSnapshot WindowSnapshot()
        {
            var window = EditorWindow.focusedWindow as ConfigEditorWindow;
            if (window == null) window = Resources.FindObjectsOfTypeAll<ConfigEditorWindow>().FirstOrDefault(w => w.Session != null);
            return window?.Session?.Snapshot();
        }
        internal static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string id, ConfigDraftSnapshot snapshot = null)
        {
            snapshot = snapshot ?? WindowSnapshot();
            if (snapshot != null && snapshot.Tables.TryGetValue(id, out var table) && table.Definition.IsAdapted)
                return table.Rows.Values.Select(table.Definition.Codec.EncodeRow).ToArray();
            var document = CsvConfigDocument.Load(File.ReadAllBytes(Path.Combine(Application.dataPath, "DataConfig/" + id + ".csv")));
            var lines = document.Lines.Where(l => !l.IsTrivia).ToArray();
            var columns = id == "Language" ? new[] { "id", "zh_CN" } : lines[0].Cells.Select(c => c.Value).ToArray();
            var definition = new ConfigTableDefinition { TableId = id, HeaderMode = id == "Language" ? ConfigHeaderMode.None : ConfigHeaderMode.Header, ExactColumns = columns };
            var issues = document.ValidateStructure(definition);
            if (issues.Count > 0) throw new InvalidDataException("引用源结构无效：" + id + "：" + issues[0].ChineseReason);
            return document.GetRows(definition).Select(r => r.Cells).ToArray();
        }
        internal static string Name(string id) => Rows("Language").FirstOrDefault(r => r["id"] == id)?["zh_CN"] ??
            (id == "none" ? "无外观" : "未登记中文名称");

        internal static List<ConfigIssue> ValidateFields(ConfigDraftTableSnapshot table, ConfigDraftSnapshot snapshot)
        {
            var issues = new List<ConfigIssue>(table.Issues);
            foreach (var row in table.Rows)
                foreach (var field in table.Definition.Fields)
                {
                    try
                    {
                        ValidateNode(field, row.Value.At(field.Column), field.Column, table.Definition.TableId, snapshot);
                        if (!field.IsEditable(row.Value) && !row.Value.At(field.Column).Equals(table.Definition.Codec.DecodeRow(table.SourceRows[row.Key]).At(field.Column)) &&
                            field.Column != "actionPointMaximumUnits" && field.Column != "movementEfficiencyMilliUnits")
                            throw new InvalidDataException("只读字段不能改变批准声明");
                    }
                    catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException || e is InvalidDataException)
                    { issues.Add(new ConfigIssue(table.Definition.TableId, row.Key, field.Column, field.ChineseName + "：" + e.Message, "character_editor_field_invalid")); }
                }
            return issues;
        }

        private static void ValidateNode(ConfigFieldDefinition field, ConfigValue value, string path, string tableId, ConfigDraftSnapshot snapshot)
        {
            if (field.Control == ConfigControl.Reference)
            {
                // Use the explicitly supplied validation snapshot, even when another window is open.
                var copy = new ConfigFieldDefinition { Control = ConfigControl.Text, AllowEmpty = field.AllowEmpty, ReservedDelimiters = field.ReservedDelimiters };
                ConfigFieldGUI.ValidateValue(copy, value);
                if (!value.HasValue) return;
                string source = field.Column == "gongFaName" ? "GongFa" : field.Column == "equipmentBasicAttackProfileId" ? "AttackProfiles" :
                    field.Column == "itemId" ? "Items" : path == "equippedSpells" ? "Spells" : path == "equippedSkills" ? "Skills" : "Language";
                string key = source == "Language" ? "id" : source == "Items" ? "itemId" : source == "AttackProfiles" ? "attackProfileId" : "name";
                if (!Rows(source, snapshot).Any(r => r[key] == value.RawToken)) throw new InvalidDataException("未登记的引用ID：" + value.RawToken);
                string prefix = field.Column == "affiliation" ? "faction_" : field.Column == "grade" ? "grade_" :
                    field.Column == "elementMain" || field.Column == "elementSub" ? "element_" :
                    field.Column == "realmId" || field.Column == "realm" || field.Column == "realmKey" || field.Column == "equipmentRealmRequirementId" || field.Column == "combatUseRealmId" ? "realm_" : "";
                if (!value.RawToken.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("引用类型不符合此字段");
                if (tableId == "Characters" && field.Column == "realmId" && !Rows("AttackProfiles", snapshot).Any(r =>
                    r["unarmedFamilyId"] == "body_human" && r["unarmedRealmId"] == value.RawToken)) throw new InvalidDataException("境界没有人形身体攻击映射");
                if ((field.Column == "elementMain" || field.Column == "elementSub") && value.RawToken.Substring(8).Contains("_"))
                    throw new InvalidDataException("属性字段不接受灵根要求或组合");
                return;
            }
            if (field.Control == ConfigControl.List || field.Control == ConfigControl.Group)
            {
                if (value.Kind != (field.Control == ConfigControl.List ? ConfigNodeKind.List : ConfigNodeKind.Group)) throw new InvalidDataException("字段结构不一致");
                if (!value.HasValue && !field.AllowEmpty) throw new InvalidDataException("此列表必须填写");
                if (field.Control == ConfigControl.List) foreach (var item in value.Items) ValidateNode(field.Element, item, path, tableId, snapshot);
                else
                {
                    if (value.Members.Count != field.Children.Count) throw new InvalidDataException("未知或缺失子字段");
                    foreach (var child in field.Children) ValidateNode(child, value.At(child.Column), path + "/" + child.Column, tableId, snapshot);
                }
                return;
            }
            ConfigFieldGUI.ValidateValue(field, value);
            if (field.Control == ConfigControl.Decimal && value.HasValue)
            {
                float invariant = float.Parse(value.RawToken, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (float.IsNaN(invariant) || float.IsInfinity(invariant)) throw new InvalidDataException("原领域浮点必须有限");
                // Legacy character/chapter parsers use current culture. Refuse lossy interpretation.
                if (path != "growth" && !path.StartsWith("growth/", StringComparison.Ordinal))
                {
                    float actual = float.Parse(value.RawToken, CultureInfo.CurrentCulture);
                    if (actual != invariant) throw new InvalidDataException("当前文化下原导入器不能准确回读该数值");
                }
            }
            if (value.HasValue && (field.Column == "maxStack" || field.Column == "combatUseHealthRecovery" || field.Column == "initialGrantQuantity" || field.Column == "quantity") &&
                int.Parse(value.RawToken, CultureInfo.InvariantCulture) <= 0) throw new InvalidDataException("必须为正整数");
            if (field.Column == "chance" && value.HasValue && (decimal.Parse(value.RawToken, CultureInfo.InvariantCulture) < 0 || decimal.Parse(value.RawToken, CultureInfo.InvariantCulture) > 100))
                throw new InvalidDataException("概率必须为0至100百分点");
        }
    }
}
