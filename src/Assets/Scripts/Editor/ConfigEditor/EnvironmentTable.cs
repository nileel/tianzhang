using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class EnvironmentTable : IConfigTableProvider, IConfigRowCodec
    {
        public const string TableId = "EnvironmentProfiles";
        private static readonly string[] Channels = { "airflow", "visibility", "temperature", "precipitation", "suspendedHazard", "cloudDischarge" };
        private static readonly string[] ChannelNames = { "气流", "能见度", "温度", "降水", "悬浮危险", "云层放电" };

        public IEnumerable<ConfigTableDefinition> GetTables()
        {
            var channels = WorldFields.Group("phenomenonChannels", "现象通道", Channels.Select((id, i) =>
                WorldFields.List(id, ChannelNames[i], WorldFields.Text("type", "现象类型引用"), () => ConfigValue.Scalar("new_phenomenon"))).ToArray());
            var pairs = WorldFields.List("phenomenonPairs", "现象组合", WorldFields.Group("pair", "组合规则",
                ChannelField(), WorldFields.Text("first", "第一现象引用"), WorldFields.Text("second", "第二现象引用"), WorldFields.Text("result", "结果现象引用")),
                () => WorldFields.Record("channel", "airflow", "first", "wind", "second", "gust", "result", "gust"));
            pairs.Help = "三项现象均引用所选通道的类型；前两项为无序组合，结果含义保持不变。";
            var surface = WorldFields.List("surfacePrototypeRefs", "地表原型引用", WorldFields.Text("surface", "地表原型ID"), () => ConfigValue.Scalar("surface_new"));
            surface.Help = "原领域仅校验引用形状与唯一性，没有地表原型目录；不宣称已解析实际资产。";
            var clearance = WorldFields.Text("edgeClearanceLimits", "有向边净空上限");
            clearance.ReservedDelimiters = "~;=+";
            clearance.Help = "无约束写none；v1每条使用fromQ:fromR>toQ:toR@正整数，以|分隔，反向边独立声明。v2必须写none，净空由有向边覆盖列承载。";

            var cells = WorldFields.List("battlefieldCells", "战场格子", WorldFields.Group("cell", "格子",
                    WorldFields.Number("q", "轴坐标q", "格"), WorldFields.Number("r", "轴坐标r", "格"),
                    new ConfigFieldDefinition { Column = "blocksGroundMove", ChineseName = "阻挡地面移动", Control = ConfigControl.Boolean, Editable = true },
                    new ConfigFieldDefinition { Column = "blocksLineOfSight", ChineseName = "阻挡视线", Control = ConfigControl.Boolean, Editable = true }),
                () => WorldFields.Record("q", "0", "r", "0", "blocksGroundMove", "0", "blocksLineOfSight", "0"));
            cells.AllowEmpty = true;
            cells.EmptyMeaning = "v2 固定地图格由地图脚本生成，编辑器只读";
            cells.Help = "v1 记录为 q:r@阻挡地面@阻挡视线；v2 的七段格记录由固定地图脚本生成，本编辑器只读但保存时仍按原值提交给导入器。";
            cells.CanEdit = row => row.At("mapSchemaVersion").RawToken != "2";

            var mapSchemaVersion = WorldFields.Text("mapSchemaVersion", "地图架构版本", empty: true);
            mapSchemaVersion.Help = "留空为旧版平地契约；固定三档地图写2。版本决定格与有向边编码，不在编辑器中推断。";
            WorldFields.Lock(mapSchemaVersion, "地图架构版本由固定地图定义决定");

            var mapVersion = WorldFields.Text("mapVersion", "地图版本", empty: true);
            mapVersion.Help = "v2 地图的稳定版本ID；输入变化使依赖地图的缓存路径失效。";
            WorldFields.Lock(mapVersion, "地图版本由固定地图定义决定");

            var fields = new[]
            {
                WorldFields.Lock(WorldFields.Text("profileId", "环境档案ID"), "稳定档案ID不可修改"),
                WorldFields.Group("queryLimits", "查询限制", WorldFields.Number("unitsPerRange", "每射程度量单位", "度量单位"), WorldFields.Number("maxQueryRange", "最大查询射程", "射程")),
                cells, surface, channels, pairs,
                WorldFields.Lock(WorldFields.List("elementRelationRefs", "五行关系", WorldFields.Enum("element", "五行",
                    "element_wood", "木", "element_fire", "火", "element_earth", "土", "element_metal", "金", "element_water", "水"), () => ConfigValue.Scalar("element_wood")), "固定的五行关系集合，各出现一次，不由本工具改变规则"),
                WorldFields.Enum("spiritRecoveryEnvironment", "回灵环境", "ordinary", "普通", "spirit_vein_node", "灵脉节点", "blessed_land", "福地"),
                clearance, mapSchemaVersion, mapVersion,
                Opaque("directedEdgeOverrides", "有向边覆盖", "v2 有向边的类型、代价、净空与具名连接；由固定地图脚本生成，导入器逐条校验。"),
                Opaque("physicalSurfaceProfiles", "物理支承档案", "v2 六角几何、坡肩、真实台阶与支承profile的JSON；导入器解析并核对引用，D3读取同一档案。"),
                Opaque("mapMarkers", "地图标记", "v2 出生点、目标、支点与掩体标记JSON；只有所属遭遇启用后才成为对象，标记不创造占位或阻挡。"),
                Opaque("sceneObjects", "场景物", "场景物类型和实例JSON；导入器按同一地格与有向边校验。"),
                Opaque("flightSpace", "空中许可", "已有柱与有向邻边的空中许可、最高有效高度及额外负担JSON；空配置不开放飞行。")
            };
            fields[2].Help = "编辑后按q、r排序；重复坐标拒绝。v2 固定地图格由脚本生成，编辑器只读。";
            yield return WorldFields.Table(TableId, "战场环境", "profileId", fields, this);
        }

        private static ConfigFieldDefinition Opaque(string id, string label, string help)
        {
            return new ConfigFieldDefinition
            {
                Column = id,
                ChineseName = label,
                Control = ConfigControl.Text,
                Editable = false,
                AllowEmpty = true,
                ReservedDelimiters = "",
                Help = help,
                ReadOnlyReason = "由固定地图脚本生成；导入器校验，编辑器只读",
            };
        }

        private static ConfigFieldDefinition ChannelField() => WorldFields.Enum("channel", "通道",
            Channels.SelectMany((id, i) => new[] { id, ChannelNames[i] }).ToArray());

        public ConfigValue DecodeRow(ConfigCsvRow row)
        {
            var values = row.Cells.ToDictionary(p => p.Key, p => ConfigValue.Scalar(p.Value));
            var limits = row["queryLimits"].Split(';');
            if (limits.Length != 2 || !limits[0].StartsWith("unitsPerRange=", StringComparison.Ordinal) || !limits[1].StartsWith("maxQueryRange=", StringComparison.Ordinal))
                throw new InvalidDataException("查询限制必须有两个命名数值，且按每射程度量单位、最大查询射程顺序声明。");
            values["queryLimits"] = WorldFields.Record("unitsPerRange", limits[0].Substring(14), "maxQueryRange", limits[1].Substring(14));
            string version = (row["mapSchemaVersion"] ?? "").Trim();
            if (version == "2")
            {
                // Fixed-maps are generated by the D1 map scripts; the editor keeps the raw cell
                // and edge envelopes untouched and locks them instead of inventing a second shape.
                values["battlefieldCells"] = ConfigValue.List(Array.Empty<ConfigValue>(), row["battlefieldCells"], hasValue: true);
            }
            else
            {
                values["battlefieldCells"] = WorldFields.DecodeList(row["battlefieldCells"], '|', token =>
                {
                    var parts = token.Split('@');
                    if (parts.Length != 3) throw new InvalidDataException("战场格子必须包含坐标与两个阻挡标志。");
                    var coordinate = parts[0].Split(':');
                    if (coordinate.Length != 2) throw new InvalidDataException("战场格子必须包含q和r坐标。");
                    return WorldFields.Record("q", coordinate[0], "r", coordinate[1], "blocksGroundMove", parts[1], "blocksLineOfSight", parts[2]);
                });
            }
            values["surfacePrototypeRefs"] = WorldFields.DecodeList(row["surfacePrototypeRefs"], '|', ConfigValue.Scalar);
            values["elementRelationRefs"] = WorldFields.DecodeList(row["elementRelationRefs"], '|', ConfigValue.Scalar);
            var channels = new Dictionary<string, ConfigValue>(StringComparer.Ordinal);
            foreach (var token in row["phenomenonChannels"].Split(';'))
            {
                var parts = token.Split('=');
                if (parts.Length != 2 || !Channels.Contains(parts[0]) || channels.ContainsKey(parts[0]))
                    throw new InvalidDataException("现象通道格式错误、未知或重复。");
                channels.Add(parts[0], WorldFields.DecodeList(parts[1], '+', ConfigValue.Scalar));
            }
            if (channels.Count != Channels.Length) throw new InvalidDataException("六个现象通道必须各声明一次。");
            values["phenomenonChannels"] = ConfigValue.Group(channels, row["phenomenonChannels"]);
            values["phenomenonPairs"] = WorldFields.DecodeList(row["phenomenonPairs"], '|', token =>
            {
                var parts = token.Split(new[] { ':', '+', '>' });
                if (parts.Length != 4) throw new InvalidDataException("现象组合必须包含通道、第一现象、第二现象与结果。");
                return WorldFields.Record("channel", parts[0], "first", parts[1], "second", parts[2], "result", parts[3]);
            });
            values["mapSchemaVersion"] = ConfigValue.Scalar(version);
            values["mapVersion"] = ConfigValue.Scalar((row["mapVersion"] ?? "").Trim());
            values["directedEdgeOverrides"] = ConfigValue.Scalar("");
            values["physicalSurfaceProfiles"] = ConfigValue.Scalar("");
            values["mapMarkers"] = ConfigValue.Scalar("");
            values["sceneObjects"] = ConfigValue.Scalar("");
            values["flightSpace"] = ConfigValue.Scalar("");
            return ConfigValue.Group(values);
        }

        public IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row)
        {
            var values = row.Members.ToDictionary(p => p.Key, p => p.Value.RawToken);
            var limits = row.At("queryLimits");
            values["queryLimits"] = "unitsPerRange=" + limits.At("unitsPerRange").RawToken + ";maxQueryRange=" + limits.At("maxQueryRange").RawToken;
            string version = row.At("mapSchemaVersion").RawToken;
            values["battlefieldCells"] = version == "2"
                ? ""
                : string.Join("|", row.At("battlefieldCells").Items
                    .OrderBy(v => int.Parse(v.At("q").RawToken, CultureInfo.InvariantCulture)).ThenBy(v => int.Parse(v.At("r").RawToken, CultureInfo.InvariantCulture))
                    .Select(v => v.At("q").RawToken + ":" + v.At("r").RawToken + "@" + v.At("blocksGroundMove").RawToken + "@" + v.At("blocksLineOfSight").RawToken));
            values["surfacePrototypeRefs"] = string.Join("|", row.At("surfacePrototypeRefs").Items.Select(v => v.RawToken));
            values["elementRelationRefs"] = string.Join("|", row.At("elementRelationRefs").Items.Select(v => v.RawToken));
            values["phenomenonChannels"] = string.Join(";", Channels.Select(c => c + "=" + string.Join("+", row.At("phenomenonChannels").At(c).Items.Select(v => v.RawToken).OrderBy(v => v, StringComparer.Ordinal))));
            values["phenomenonPairs"] = string.Join("|", row.At("phenomenonPairs").Items.Select(v =>
            {
                var first = v.At("first").RawToken;
                var second = v.At("second").RawToken;
                if (string.CompareOrdinal(first, second) > 0) { var temporary = first; first = second; second = temporary; }
                return v.At("channel").RawToken + ":" + first + "+" + second + ">" + v.At("result").RawToken;
            }).OrderBy(v => v, StringComparer.Ordinal));
            // Fixed-map JSON and cell/edge envelopes are never rewritten here: the draft keeps
            // the original source cell for unchanged columns and the importer validates it.
            values["directedEdgeOverrides"] = "";
            values["physicalSurfaceProfiles"] = "";
            values["mapMarkers"] = "";
            values["sceneObjects"] = "";
            values["flightSpace"] = "";
            return values;
        }

        public IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            if (!snapshot.Tables.TryGetValue(TableId, out var table)) return Array.Empty<ConfigIssue>();
            var issues = WorldFields.Validate(table, snapshot);
            if (issues.Count > 0) return issues;
            foreach (var row in table.Rows)
            {
                var before = issues.Count;
                void Issue(string path, string reason, string code) => issues.Add(new ConfigIssue(TableId, row.Key, path, reason, code));
                foreach (var name in new[] { "unitsPerRange", "maxQueryRange" })
                    if (int.Parse(row.Value.At("queryLimits/" + name).RawToken, CultureInfo.InvariantCulture) <= 0)
                        Issue("queryLimits/" + name, "查询限制必须为正整数，空值和零均非法", "environment_query_limit");
                string version = row.Value.At("mapSchemaVersion").RawToken;
                if (version.Length > 0 && version != "2")
                    Issue("mapSchemaVersion", "地图架构版本只接受留空或2", "environment_map_schema_version");
                if (version != "2" && row.Value.At("battlefieldCells").Items.Count == 0)
                    Issue("battlefieldCells", "旧版平地地图至少需要一个战场格子", "environment_required_cell");
                var cells = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < row.Value.At("battlefieldCells").Items.Count; i++)
                {
                    var cell = row.Value.At("battlefieldCells").Items[i];
                    var key = int.Parse(cell.At("q").RawToken, CultureInfo.InvariantCulture) + ":" + int.Parse(cell.At("r").RawToken, CultureInfo.InvariantCulture);
                    if (!cells.Add(key)) Issue("battlefieldCells/" + i + "/q", "战场格子坐标重复；排序不会合并重复项", "environment_duplicate_cell");
                }
                if (issues.Count != before) continue;
                // Preserve unchanged source tokens so invalid input is not silently sorted,
                // deduplicated or otherwise repaired merely by opening the editor.
                var values = WorldFields.Cells(table, row.Key);
                try
                {
                    string line = string.Join(",", table.Definition.ExactColumns.Select(column => CsvCell(values[column])));
                    WorldContentImporter.ParseEnvironmentProfiles(new[] { string.Join(",", table.Definition.ExactColumns), line }, TableId);
                }
                catch (InvalidDataException e)
                {
                    var path = e.Message.Contains("flightSpace") ? "flightSpace" :
                        e.Message.Contains("sceneObjects") ? "sceneObjects" :
                        e.Message.Contains("physicalSurfaceProfiles") ? "physicalSurfaceProfiles" :
                        e.Message.Contains("mapMarkers") ? "mapMarkers" :
                        e.Message.Contains("directedEdge") ? "directedEdgeOverrides" :
                        e.Message.Contains("mapVersion") ? "mapVersion" :
                        e.Message.Contains("mapSchemaVersion") ? "mapSchemaVersion" :
                        e.Message.Contains("edgeClearanceLimits") ? "edgeClearanceLimits" :
                        e.Message.Contains("battlefield") || e.Message.Contains("hex coordinate") || e.Message.Contains("binary flag") ? "battlefieldCells" :
                        e.Message.Contains("pair") ? "phenomenonPairs" : e.Message.Contains("channel") || e.Message.Contains("phenomenonChannels") ? "phenomenonChannels" :
                        e.Message.Contains("surfacePrototypeRefs") ? "surfacePrototypeRefs" : e.Message.Contains("element") ? "elementRelationRefs" :
                        e.Message.Contains("spiritRecoveryEnvironment") ? "spiritRecoveryEnvironment" : e.Message.Contains("profileId") ? "profileId" : "queryLimits";
                    var field = table.Definition.Fields.Single(f => f.Column == path);
                    Issue(path, field.ChineseName + "未通过原环境领域校验：" + e.Message, "environment_domain_invalid");
                }
            }
            return issues;
        }

        private static string CsvCell(string value)
        {
            value ??= "";
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot) => Array.Empty<ConfigEdit>();
        public IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> changedTables) => changedTables.Contains(TableId)
            ? new[] { new ConfigImportStep { Method = "WorldContentImporter.ImportEnvironmentProfiles",
                InputPaths = new[] { "Assets/DataConfig/EnvironmentProfiles.csv", "Assets/DataConfig/Adventures.csv" },
                ProductPaths = new[]
                {
                    "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset",
                    "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset",
                } } } : Array.Empty<ConfigImportStep>();
    }
}
