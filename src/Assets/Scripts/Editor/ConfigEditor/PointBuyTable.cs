using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class PointBuyTable : IConfigTableProvider
    {
        [MenuItem("天章/配置编辑器跳转/凡人行动力与移动效率")]
        public static void OpenFanrenSource()
        {
            var window = ConfigEditorWindow.Open();
            window.SelectTable("RealmCombatGrowth");
            window.SelectRow("realm_fanren");
        }
        public IEnumerable<ConfigTableDefinition> GetTables()
        {
            var codec = new PointBuyCodec();
            var columns = "configId,purchasePointLimit,minValue,baseValue,maxValue,fromValue,toValue,costPerLevel,actionPointMaximumUnits,movementEfficiencyMilliUnits".Split(',');
            var labels = "配置组|可购总点数|属性最小值|属性基础值|属性最大值|费用段起值|费用段止值|每级费用|行动力上限|移动效率".Split('|');
            var fields = columns.Select((c, i) => CombatEditorFields.Number(c, labels[i], "点")).ToArray();
            fields[0] = CombatEditorFields.Lock(CombatEditorFields.Text("configId", labels[0]), "配置组是稳定身份");
            for (int i = 1; i <= 4; i++)
            {
                fields[i].CanEdit = codec.IsGroupHead;
                fields[i].ReadOnlyReason = "组级声明只在该组第一条费用段编辑，并联动同组全部行";
                fields[i].Help = "组级声明；保存预览列出同组同步计划，费用段本身独立编辑。";
            }
            for (int i = 8; i <= 9; i++)
            {
                fields[i].Scale = 1000m; fields[i].Unit = i == 8 ? "行动力" : "移动效率";
                CombatEditorFields.Lock(fields[i], "由境界成长联动；请在境界战斗成长的凡人条目编辑");
                fields[i].Help = "跳转：天章 → 配置编辑器跳转 → 凡人行动力与移动效率；default所有费用段都校验同一源值。";
            }
            var table = CombatEditorFields.Table("CharacterCreationPointBuy", "创角点购", columns, fields, codec, "configId");
            table.RowKey = r => r["configId"] + "/" + r.PhysicalLine.ToString(CultureInfo.InvariantCulture);
            table.RowLabel = r => "费用段 " + r["fromValue"] + "～" + r["toValue"] + "（原始行 " + (r.PhysicalLine + 1) + "）";
            table.GroupingFields = new[] { "configId" };
            yield return table;
        }
    }

    internal sealed class PointBuyCodec : CombatScalarCodec
    {
        private static readonly string[] GroupColumns = { "purchasePointLimit", "minValue", "baseValue", "maxValue" };
        private readonly Dictionary<string, int> firstLines = new Dictionary<string, int>(StringComparer.Ordinal);
        internal PointBuyCodec() : base("CharacterCreationPointBuy") { }
        internal bool IsGroupHead(ConfigValue row) => firstLines.TryGetValue(row.At("configId").RawToken, out int line) &&
            row.RawToken == line.ToString(CultureInfo.InvariantCulture);
        public override ConfigValue DecodeRow(ConfigCsvRow row)
        {
            string id = row["configId"];
            if (!firstLines.TryGetValue(id, out int line) || row.PhysicalLine < line) firstLines[id] = row.PhysicalLine;
            return ConfigValue.Group(row.Cells.ToDictionary(v => v.Key, v => ConfigValue.Scalar(v.Value)), row.PhysicalLine.ToString(CultureInfo.InvariantCulture));
        }
        public override IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot)
        {
            if (!snapshot.Tables.TryGetValue(Id, out var table)) return Array.Empty<ConfigEdit>();
            var edits = new List<ConfigEdit>();
            foreach (var group in table.Rows.GroupBy(r => r.Value.At("configId").RawToken))
            {
                var head = group.Single(r => IsGroupHead(r.Value));
                foreach (var row in group.Where(r => r.Key != head.Key))
                    foreach (var c in GroupColumns)
                        if (snapshot.Edits.Any(e => e.TableId == Id && e.RowKey == head.Key && e.FieldPath == c) &&
                            !row.Value.At(c).Equals(head.Value.At(c))) edits.Add(new ConfigEdit(Id, row.Key, c, head.Value.At(c)));
            }
            return edits;
        }
        public override IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            var issues = base.ValidateDraft(snapshot).ToList();
            if (issues.Count != 0 || !snapshot.Tables.TryGetValue(Id, out var table)) return issues;
            if (!table.Rows.Values.Any(r => r.At("configId").RawToken == "default"))
                issues.Add(new ConfigIssue(Id, "", "configId", "缺少现有default配置组", "point_buy_default_missing"));
            foreach (var group in table.Rows.GroupBy(r => r.Value.At("configId").RawToken))
            {
                var first = group.Single(r => IsGroupHead(r.Value));
                foreach (var row in group)
                {
                    foreach (string c in GroupColumns)
                        if (row.Value.At(c).RawToken != first.Value.At(c).RawToken)
                            issues.Add(new ConfigIssue(Id, row.Key, c, "组级声明需要先应用已展示的同组联动计划", "point_buy_group_mismatch"));
                    if (group.Key != "default") continue;
                    CombatEditorFields.Check(issues, Id, row.Key, "movementEfficiencyMilliUnits", "凡人镜像校验失败", () =>
                    {
                        var source = CombatEditorFields.Rows("RealmCombatGrowth", snapshot).Single(r => r["realmId"] == "realm_fanren");
                        foreach (string c in new[] { "actionPointMaximumUnits", "movementEfficiencyMilliUnits" })
                        {
                            int actual = int.Parse(row.Value.At(c).RawToken, CultureInfo.InvariantCulture);
                            int expected = int.Parse(source[c == "actionPointMaximumUnits" ? c : "baseMovementEfficiencyMilliUnits"], CultureInfo.InvariantCulture);
                            if (actual <= 0 || actual != expected) throw new InvalidDataException("point_buy_realm_growth_checksum_mismatch");
                        }
                    });
                }
            }
            return issues;
        }
    }
}
