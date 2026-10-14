using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TianZhang.Features.Adventure;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class WorldTables : IConfigTableProvider
    {
        public IEnumerable<ConfigTableDefinition> GetTables()
        {
            var features = WorldFields.List("features", "据点功能", WorldFields.Group("feature", "功能",
                WorldFields.Text("featureId", "功能ID"), WorldFields.Reference("displayNameKey", "名称文本", "Language", "id"),
                WorldFields.Enum("availability", "启用状态", "enabled", "启用", "disabled", "禁用"),
                WorldFields.Reference("disabledReasonKey", "禁用理由文本", "Language", "id", true)),
                () => WorldFields.Record("featureId", "bounty_board", "displayNameKey", "settlement_feature_bounty_board", "availability", "enabled", "disabledReasonKey", ""));
            var settlements = new[]
            {
                WorldFields.Text("settlementId", "据点ID"), WorldFields.Reference("displayNameKey", "名称文本", "Language", "id"),
                WorldFields.Enum("contentScope", "内容范围", "content_scope_production", "正式内容"),
                WorldFields.Enum("settlementType", "据点类型", "settlement_type_city", "城市"),
                WorldFields.Enum("regionId", "地域", "guanzhong", "关中"),
                WorldFields.Enum("ownerFactionId", "所属势力", "faction_neutral", "中立势力"),
                WorldFields.Enum("visualThemeId", "视觉主题", "visual_theme_loess_city", "黄土城市"), features,
                WorldFields.List("adventureEntranceIds", "冒险入口", WorldFields.Enum("entrance", "入口", "guanzhong_wild", "关中野外"), () => ConfigValue.Scalar("guanzhong_wild"))
            };
            foreach (var field in settlements) WorldFields.Lock(field, "现有批准的关中城固定投影；首版不开放修改");
            yield return WorldFields.Table("Settlements", "据点", "settlementId", settlements, new WorldRowCodec("Settlements"));

            var rewards = WorldFields.List("rewardEntries", "奖励列表", WorldFields.Group("reward", "奖励",
                WorldFields.Reference("itemId", "物品", "Items", "itemId"), WorldFields.Number("quantity", "数量", "个")),
                () => WorldFields.Record("itemId", "item_lingshi_low", "quantity", "3"));
            var bounties = new[]
            {
                WorldFields.Text("bountyId", "悬赏ID"), WorldFields.Reference("titleKey", "标题文本", "Language", "id"),
                WorldFields.Reference("descriptionKey", "描述文本", "Language", "id"),
                WorldFields.Enum("contentScope", "内容范围", "content_scope_production", "正式内容"),
                WorldFields.Reference("issuerSettlementId", "发布据点", "Settlements", "settlementId"),
                WorldFields.Enum("objectiveType", "目标类型", "defeat_enemy", "击杀敌人"),
                WorldFields.Reference("targetEnemyId", "目标敌人", "Enemies", "name"),
                WorldFields.Number("requiredCount", "击杀数", "个"),
                WorldFields.Enum("allowedAdventureId", "允许地图", "guanzhong_wild", "关中野外"), rewards,
                WorldFields.Enum("repeatPolicy", "重复策略", "one_time", "一次")
            };
            foreach (var field in bounties) WorldFields.Lock(field, "现有批准的石甲兽悬赏固定投影；首版不开放修改");
            yield return WorldFields.Table("Bounties", "悬赏", "bountyId", bounties, new WorldRowCodec("Bounties"));

            var adventureCodec = new WorldRowCodec("Adventures");
            var content = WorldFields.Reference("contentId", "节点内容", "Enemies", "name", true);
            content.CanEdit = row => row.At("nodeTypeId").RawToken == EncounterNodeHandler.StableNodeTypeId;
            content.ReadOnlyReason = "起点和返回节点内容必须为空；切换类型后不会自动清空原值";
            var additionalEnemies = WorldFields.Lock(WorldFields.Text("additionalEnemies", "额外敌人实例及落点", true),
                "批准的遭遇实例固定配置");
            // This field carries the complete declaration; the Adventure parser owns |, @ and : syntax.
            additionalEnemies.ReservedDelimiters = "~;=+>";
            var adventures = new[]
            {
                WorldFields.Lock(WorldFields.Text("adventureId", "地图ID"), "稳定地图分组不可修改"),
                WorldFields.Reference("displayNameKey", "地图名称文本（同图联动）", "Language", "id", true),
                WorldFields.Text("contentScope", "内容范围（同图联动）", true),
                WorldFields.Lock(WorldFields.Text("nodeId", "节点ID"), "稳定节点ID不可修改"),
                WorldFields.Enum("nodeTypeId", "节点类型", AdventureNodeDispatcher.StartNodeHandler.StableNodeTypeId, "起点",
                    EncounterNodeHandler.StableNodeTypeId, "遭遇", ReturnNodeHandler.StableNodeTypeId, "返回"),
                WorldFields.Number("q", "轴坐标q", "格"), WorldFields.Number("r", "轴坐标r", "格"), content,
                additionalEnemies
            };
            foreach (var field in adventures.Where(f => f.Column == "displayNameKey" || f.Column == "contentScope"))
            {
                field.CanEdit = adventureCodec.IsGroupHead;
                field.ReadOnlyReason = "组级字段只在同地图第一条节点编辑，并联动该地图全部已有节点";
            }
            var adventure = WorldFields.Table("Adventures", "冒险", "nodeId", adventures, adventureCodec);
            adventure.HeaderMode = ConfigHeaderMode.FirstPhysicalLine;
            adventure.RowKey = row => row["adventureId"] + "/" + row["nodeId"];
            adventure.RowLabel = row => WorldFields.Name(row["displayNameKey"]) + " · " + row["nodeId"];
            adventure.GroupingFields = new[] { "adventureId", "displayNameKey", "contentScope" };
            yield return adventure;
        }
    }

    internal sealed class WorldRowCodec : IConfigRowCodec
    {
        private readonly string tableId;
        private readonly Dictionary<string, int> firstLines = new Dictionary<string, int>(StringComparer.Ordinal);
        internal WorldRowCodec(string id) { tableId = id; }
        internal bool IsGroupHead(ConfigValue row) => firstLines.TryGetValue(row.At("adventureId").RawToken, out var line) &&
            row.RawToken == line.ToString(CultureInfo.InvariantCulture);
        public ConfigValue DecodeRow(ConfigCsvRow row)
        {
            var members = row.Cells.ToDictionary(p => p.Key, p => ConfigValue.Scalar(p.Value));
            if (tableId == "Settlements")
            {
                members["features"] = WorldFields.DecodeList(row["features"], '|', token =>
                    WorldFields.DecodeRecord(token, '~', "featureId", "displayNameKey", "availability", "disabledReasonKey"));
                members["adventureEntranceIds"] = WorldFields.DecodeList(row["adventureEntranceIds"], '|', ConfigValue.Scalar);
            }
            if (tableId == "Bounties") members["rewardEntries"] = WorldFields.DecodeList(row["rewardEntries"], '|', token =>
                WorldFields.DecodeRecord(token, '@', "itemId", "quantity"));
            if (tableId == "Adventures")
            {
                if (!firstLines.TryGetValue(row["adventureId"], out var line) || row.PhysicalLine < line) firstLines[row["adventureId"]] = row.PhysicalLine;
            }
            return ConfigValue.Group(members, row.PhysicalLine.ToString(CultureInfo.InvariantCulture));
        }

        public IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row) => row.Members.ToDictionary(p => p.Key, p =>
            p.Key == "features" ? string.Join("|", p.Value.Items.Select(v => string.Join("~", new[] { "featureId", "displayNameKey", "availability", "disabledReasonKey" }.Select(k => v.At(k).RawToken)))) :
            p.Key == "adventureEntranceIds" ? string.Join("|", p.Value.Items.Select(v => v.RawToken)) :
            p.Key == "rewardEntries" ? string.Join("|", p.Value.Items.Select(v => v.At("itemId").RawToken + "@" + v.At("quantity").RawToken)) : p.Value.RawToken);

        public IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            if (!snapshot.Tables.TryGetValue(tableId, out var table)) return Array.Empty<ConfigIssue>();
            var issues = WorldFields.Validate(table, snapshot);
            if (issues.Count > 0) return issues;
            var coordinates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in table.Rows)
            {
                var row = entry.Value;
                void Issue(string path, string reason, string code) => issues.Add(new ConfigIssue(tableId, entry.Key, path, reason, code));
                if (tableId == "Settlements")
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < row.At("features").Items.Count; i++)
                    {
                        var feature = row.At("features").Items[i];
                        if (!seen.Add(feature.At("featureId").RawToken)) Issue("features/" + i + "/featureId", "据点功能ID重复", "settlement_feature_duplicate");
                        if (feature.At("availability").RawToken == "enabled" && feature.At("disabledReasonKey").HasValue ||
                            feature.At("availability").RawToken == "disabled" && !feature.At("disabledReasonKey").HasValue)
                            Issue("features/" + i + "/disabledReasonKey", "启用时理由必须为空，禁用时必须提供有效理由文本", "settlement_feature_reason");
                    }
                    if (row.At("adventureEntranceIds").Items.Select(v => v.RawToken).Distinct().Count() != row.At("adventureEntranceIds").Items.Count)
                        Issue("adventureEntranceIds", "冒险入口不能重复", "settlement_entrance_duplicate");
                }
                if (tableId == "Bounties")
                {
                    if (int.Parse(row.At("requiredCount").RawToken, CultureInfo.InvariantCulture) <= 0) Issue("requiredCount", "击杀数必须为正整数", "bounty_count");
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    var items = WorldFields.Rows("Items", snapshot);
                    for (var i = 0; i < row.At("rewardEntries").Items.Count; i++)
                    {
                        var reward = row.At("rewardEntries").Items[i];
                        var id = reward.At("itemId").RawToken;
                        var quantity = int.Parse(reward.At("quantity").RawToken, CultureInfo.InvariantCulture);
                        var item = items.SingleOrDefault(v => v["itemId"] == id);
                        if (!seen.Add(id)) Issue("rewardEntries/" + i + "/itemId", "奖励物品不能重复", "bounty_reward_duplicate");
                        if (item == null || quantity <= 0 || quantity > int.Parse(item["maxStack"], CultureInfo.InvariantCulture))
                            Issue("rewardEntries/" + i + "/quantity", "奖励数量必须为正整数且不超过物品堆叠上限", "bounty_reward_quantity");
                    }
                }
                if (tableId != "Adventures") continue;
                var map = row.At("adventureId").RawToken;
                var coordinate = map + "/" + int.Parse(row.At("q").RawToken, CultureInfo.InvariantCulture) + ":" + int.Parse(row.At("r").RawToken, CultureInfo.InvariantCulture);
                if (!coordinates.Add(coordinate)) Issue("q", "同一地图的节点坐标不能重复", "adventure_duplicate_coordinate");
                var type = row.At("nodeTypeId").RawToken;
                if (type != EncounterNodeHandler.StableNodeTypeId && row.At("contentId").HasValue)
                    Issue("contentId", "起点和返回节点的内容必须为空", "adventure_content_must_be_empty");
                if (type == EncounterNodeHandler.StableNodeTypeId && !row.At("contentId").HasValue)
                    Issue("contentId", "遭遇节点必须引用正式敌人", "adventure_encounter_enemy_unresolved");
                foreach (var column in new[] { "displayNameKey", "contentScope" })
                    if (table.Rows.Values.Any(v => v.At("adventureId").RawToken == map && !v.At(column).Equals(row.At(column))))
                        Issue(column, "同一地图的名称文本和内容范围必须一致；应用组级关联编辑后再校验", "adventure_group_mismatch");
            }
            // Reuse the authoritative structural parser only after local checks, which also
            // cover handler/content requirements absent from that parser. No asset writes.
            if (tableId == "Adventures" && table.Rows.Count > 0 && issues.Count == 0)
                foreach (var map in AdventureContentImporter.Parse(WorldFields.Lines(table), tableId)) UnityEngine.Object.DestroyImmediate(map);
            return issues;
        }

        public IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot)
        {
            if (tableId != "Adventures" || !snapshot.Tables.TryGetValue(tableId, out var table)) return Array.Empty<ConfigEdit>();
            var result = new List<ConfigEdit>();
            foreach (var column in new[] { "displayNameKey", "contentScope" })
                foreach (var group in table.Rows.GroupBy(r => r.Value.At("adventureId").RawToken))
                {
                    if (!snapshot.Edits.Any(e => e.TableId == tableId && e.FieldPath == column && group.Any(r => r.Key == e.RowKey))) continue;
                    var value = group.Single(r => IsGroupHead(r.Value)).Value.At(column);
                    foreach (var row in group.Where(r => !r.Value.At(column).Equals(value))) result.Add(new ConfigEdit(tableId, row.Key, column, value));
                }
            return result;
        }

        public IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> changedTables)
        {
            if (!changedTables.Contains(tableId)) return Array.Empty<ConfigImportStep>();
            if (tableId != "Adventures") return CharacterImportDescriptions.For("Items");
            return new[] { new ConfigImportStep { Method = "AdventureContentImporter.Import",
                InputPaths = new[] { "Assets/DataConfig/Adventures.csv", "Assets/DataConfig/Language.csv", "Assets/DataConfig/Enemies.csv", "Assets/Data/ContentCatalog/ContentCatalog.asset" },
                ProductPaths = new[] { "Assets/Data/Adventures/AdventureMap_guanzhong_wild.asset", "Assets/Data/ContentCatalog/ContentCatalog.asset" } } };
        }
    }

    internal static class WorldFields
    {
        internal static ConfigTableDefinition Table(string id, string label, string key, ConfigFieldDefinition[] fields, IConfigRowCodec codec) =>
            new ConfigTableDefinition { TableId = id, ChineseName = label, CsvPath = "Assets/DataConfig/" + id + ".csv", ExactColumns = fields.Select(f => f.Column).ToArray(),
                Fields = fields, Codec = codec, RowKey = row => row[key], RowLabel = row => Name(row[key]) + "（" + row[key] + "）", ReadOnlyReason = "" };
        internal static ConfigFieldDefinition Text(string id, string label, bool empty = false) => new ConfigFieldDefinition
            { Column = id, ChineseName = label, Control = ConfigControl.Text, Editable = true, AllowEmpty = empty, ReservedDelimiters = "|~@:;=+>" };
        internal static ConfigFieldDefinition Number(string id, string label, string unit = "") => new ConfigFieldDefinition
            { Column = id, ChineseName = label, Control = ConfigControl.Integer, Editable = true, Unit = unit };
        internal static ConfigFieldDefinition Enum(string id, string label, params string[] choices) => new ConfigFieldDefinition
            { Column = id, ChineseName = label, Control = ConfigControl.Enum, Editable = true,
                Choices = () => Enumerable.Range(0, choices.Length / 2).Select(i => new ConfigChoice(choices[2 * i], choices[2 * i + 1])).ToArray() };
        internal static ConfigFieldDefinition Reference(string id, string label, string source, string key, bool empty = false)
        {
            var field = Text(id, label, empty); field.Control = ConfigControl.Reference; field.Help = "引用源：" + source + "；保留稳定ID";
            field.Choices = () => Choices(source, key, null);
            return field;
        }
        internal static ConfigFieldDefinition Group(string id, string label, params ConfigFieldDefinition[] children) => new ConfigFieldDefinition
            { Column = id, ChineseName = label, Control = ConfigControl.Group, Editable = true, Children = children };
        internal static ConfigFieldDefinition List(string id, string label, ConfigFieldDefinition element, Func<ConfigValue> create, bool empty = false) => new ConfigFieldDefinition
            { Column = id, ChineseName = label, Control = ConfigControl.List, Editable = true, Element = element, NewElement = create, AllowEmpty = empty, EmptyMeaning = "空列表" };
        internal static ConfigFieldDefinition Lock(ConfigFieldDefinition field, string reason)
        {
            field.Editable = false; field.ReadOnlyReason = reason;
            foreach (var child in field.Children) Lock(child, reason);
            if (field.Element != null) Lock(field.Element, reason);
            return field;
        }
        internal static ConfigValue Record(params string[] pairs) => ConfigValue.Group(Enumerable.Range(0, pairs.Length / 2)
            .ToDictionary(i => pairs[2 * i], i => ConfigValue.Scalar(pairs[2 * i + 1])));
        internal static ConfigValue DecodeRecord(string token, char delimiter, params string[] keys)
        {
            var parts = token.Split(delimiter);
            if (parts.Length != keys.Length) throw new InvalidDataException("复合字段子项数量不符：" + token);
            return ConfigValue.Group(keys.Select((key, i) => new { key, i }).ToDictionary(p => p.key, p => ConfigValue.Scalar(parts[p.i])), token);
        }
        internal static ConfigValue DecodeList(string token, char delimiter, Func<string, ConfigValue> decode) =>
            ConfigValue.List(token.Length == 0 ? Array.Empty<ConfigValue>() : token.Split(delimiter).Select(decode), token, token.Length > 0);
        internal static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string id, ConfigDraftSnapshot snapshot = null) => CharacterEditorFields.Rows(id, snapshot);
        internal static string Name(string id) => CharacterEditorFields.Name(id);
        private static IReadOnlyList<ConfigChoice> Choices(string source, string key, ConfigDraftSnapshot snapshot) => Rows(source, snapshot)
            .Where(r => source != "Enemies" || r["contentScope"] == "guanzhong")
            .Select(r => new ConfigChoice(r[key], source == "Language" ? r["zh_CN"] : Name(r.ContainsKey("displayNameKey") ? r["displayNameKey"] : r[key]))).ToArray();
        internal static Dictionary<string, string> Cells(ConfigDraftTableSnapshot table, string key)
        {
            var source = table.SourceRows[key];
            var baseline = table.Definition.Codec.DecodeRow(source);
            var encoded = table.Definition.Codec.EncodeRow(table.Rows[key]);
            return table.Definition.ExactColumns.ToDictionary(c => c, c => baseline.At(c).Equals(table.Rows[key].At(c)) ? source[c] : encoded[c]);
        }
        internal static string[] Lines(ConfigDraftTableSnapshot table) => CharterEditorFields.Lines(table);
        internal static List<ConfigIssue> Validate(ConfigDraftTableSnapshot table, ConfigDraftSnapshot snapshot)
        {
            var issues = new List<ConfigIssue>(table.Issues);
            foreach (var row in table.Rows)
                foreach (var field in table.Definition.Fields) Visit(field, row.Value.At(field.Column), field.Column, row.Key);
            return issues;
            void Visit(ConfigFieldDefinition field, ConfigValue value, string path, string key)
            {
                try
                {
                    if (field.Control == ConfigControl.List || field.Control == ConfigControl.Group)
                    {
                        if (value.Kind != (field.Control == ConfigControl.List ? ConfigNodeKind.List : ConfigNodeKind.Group) || !value.HasValue && !field.AllowEmpty)
                            throw new ArgumentException("此结构必填，不能以空列表替代");
                        if (field.Control == ConfigControl.List)
                        {
                            if (value.Items.Count == 0 && !field.AllowEmpty) throw new ArgumentException("列表至少需要一项");
                            for (var i = 0; i < value.Items.Count; i++) Visit(field.Element, value.Items[i], path + "/" + i, key);
                        }
                        else
                        {
                            if (value.Members.Count != field.Children.Count || field.Children.Any(c => !value.Members.ContainsKey(c.Column))) throw new ArgumentException("子字段缺失或未知");
                            foreach (var child in field.Children) Visit(child, value.At(child.Column), path + "/" + child.Column, key);
                        }
                    }
                    else if (field.Control == ConfigControl.Reference)
                    {
                        var source = field.Column == "itemId" ? "Items" : field.Column == "issuerSettlementId" ? "Settlements" :
                            field.Column == "targetEnemyId" || field.Column == "contentId" ? "Enemies" : "Language";
                        var sourceKey = source == "Items" ? "itemId" : source == "Settlements" ? "settlementId" : source == "Enemies" ? "name" : "id";
                        ConfigFieldGUI.ValidateValue(new ConfigFieldDefinition { Control = ConfigControl.Text, AllowEmpty = field.AllowEmpty, ReservedDelimiters = field.ReservedDelimiters }, value);
                        if (value.HasValue && !Choices(source, sourceKey, snapshot).Any(c => c.Id == value.RawToken)) throw new ArgumentException("引用不在当前草稿权威候选中");
                    }
                    else ConfigFieldGUI.ValidateValue(field, value);
                }
                catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException || e is InvalidDataException)
                { issues.Add(new ConfigIssue(table.Definition.TableId, key, path, field.ChineseName + "：" + e.Message, "world_editor_field_invalid")); }
            }
        }
    }
}
