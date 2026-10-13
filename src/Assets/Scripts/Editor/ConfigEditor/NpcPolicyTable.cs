using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static TianZhang.Editor.ConfigEditor.CultivationFields;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class NpcPolicyTable : CultivationTableCodec, IConfigTableProvider
    {
        public const string Id = "NpcCultivationActionWeightProfiles";
        private static readonly Dictionary<string, string[]> ColumnsByKind = new Dictionary<string, string[]> {
            ["MANIFEST"] = new[] { "sourceContentHash", "authorityKind", "tieBreakPolicy" },
            ["ACTION"] = new[] { "actionStableId", "legalityRuleSetRef", "baseWeight", "subjectiveRiskGateRef", "enabled", "actionTotalCapPolicyRef" },
            ["MODIFIER"] = new[] { "actionStableId", "sourceKind", "selectorRef", "priorityDelta", "applicationOrder", "capPolicyRef", "diminishingPolicyRef", "riskThresholdDelta" },
            ["CAP_POLICY"] = new[] { "scope", "minimum", "maximum", "appliesAfterSourceKind" },
            ["DIMINISHING_POLICY"] = new[] { "scope", "inputBasis", "activationThreshold", "segments", "outputBound" },
            ["RISK_GATE"] = new[] { "knownEvidenceRefs", "riskAssessmentRef", "baseRiskThreshold", "lifespanCapPolicyRef" },
            ["TRIGGER"] = new[] { "triggerStableId" } };
        private static readonly Dictionary<string, string> KindNames = new Dictionary<string, string> { ["MANIFEST"] = "权威清单", ["ACTION"] = "行动", ["MODIFIER"] = "修正",
            ["CAP_POLICY"] = "封顶策略", ["DIMINISHING_POLICY"] = "递减策略", ["RISK_GATE"] = "风险门", ["TRIGGER"] = "重算触发" };
        public NpcPolicyTable()
        {
            var enabled = Text("enabled", "启用"); enabled.Field.Control = ConfigControl.Boolean;
            enabled.Read = raw => ConfigValue.Scalar(raw == "true" ? "1" : raw == "false" ? "0" : raw.Length == 0 ? "" : throw new InvalidDataException("NPC启用字段必须为true或false。"));
            enabled.Write = v => v.RawToken == "1" ? "true" : v.RawToken == "0" ? "false" : v.RawToken;
            var segments = List("segments", "递减分段", '|', Group("segment", "分段", '@',
                Group("bounds", "范围", '-', Number("lower", "起点", false), Number("upper", "终点", false)), Number("multiplier", "倍率", false)), "0-1@1", "");
            Initialize(Id, "NPC修炼策略", "recordId", new[] {
                Text("schemaId", "模式ID", false), Text("schemaVersion", "模式版本", false), Text("profileId", "策略组", false), Text("sourceContentHash", "来源哈希", false),
                Lock(Enum("authorityKind", "权威来源", "CSV_SOURCE_SET", "CSV源集合")), Lock(Enum("recordKind", "记录类型", KindNames.SelectMany(p => new[] { p.Key, p.Value }).ToArray())), Text("recordId", "记录ID", false),
                Lock(Enum("actionStableId", "行动", "FOUNDATION_TRIAL", "道基试炼", "FOUNDATION_NURTURE", "道基温养", "MANSION_EMBRYO_NURTURE", "府胚温养", "MANSION_OPENING_TRIAL", "开府试炼", "JINDAN_PROOF", "结丹证明")),
                External("legalityRuleSetRef", "合法性规则集"), Number("baseWeight", "基础权重", false, "权重点"), ProfileRef("subjectiveRiskGateRef", "主观风险门", "RISK_GATE"), enabled,
                SourceKind("sourceKind", "修正来源"), External("selectorRef", "选择条件"), Number("priorityDelta", "优先权增量", false, "权重点"), Number("applicationOrder", "同来源顺序"),
                ProfileRef("capPolicyRef", "来源封顶", "CAP_POLICY"), ProfileRef("diminishingPolicyRef", "递减策略", "DIMINISHING_POLICY"), ProfileRef("actionTotalCapPolicyRef", "总分封顶", "CAP_POLICY"),
                Enum("scope", "作用域", "SOURCE_GROUP", "来源组", "ACTION_TOTAL", "行动总分", "RISK_THRESHOLD", "风险阈值"), Number("minimum", "下限", false), Number("maximum", "上限", false),
                SourceKind("appliesAfterSourceKind", "应用后来源（未消费）"), Lock(Enum("inputBasis", "输入基准", "ABSOLUTE_DELTA", "绝对增量")), Lock(Number("activationThreshold", "激活阈值（未消费）", false)), segments,
                Number("outputBound", "输出绝对上限", false), Lock(Enum("tieBreakPolicy", "同分规则", "LEXICOGRAPHIC_ASC", "按稳定ID升序")), Text("triggerStableId", "触发稳定ID", false),
                Number("riskThresholdDelta", "寿元风险阈值增量", false), List("knownEvidenceRefs", "已知证据引用", '|', External("evidence", "证据引用"), "evidence", ""),
                External("riskAssessmentRef", "主观风险评估"), Number("baseRiskThreshold", "基础风险阈值", false), ProfileRef("lifespanCapPolicyRef", "寿元风险封顶", "CAP_POLICY") });
            Definition.RowKey = r => string.Join("/", r["profileId"], r["recordKind"], r["recordId"], r["triggerStableId"]);
            Definition.RowLabel = r => (KindNames.TryGetValue(r["recordKind"], out var name) ? name : "未知类型") + "（" + r["recordId"] + "）";
            Definition.GroupingFields = new[] { "profileId", "recordKind" };
            foreach (var field in Definition.Fields.Where(f => f.Editable))
            {
                field.CanEdit = row => ColumnsByKind.TryGetValue(Token(row, "recordKind"), out var columns) && columns.Contains(field.Column) &&
                    !(field.Column == "scope" && Token(row, "recordKind") == "DIMINISHING_POLICY") &&
                    !(field.Column == "riskThresholdDelta" && Token(row, "sourceKind") != "LIFESPAN");
                field.ReadOnlyReason = "本记录类型不使用该列，或当前规则锁定该值；保留空值及原声明";
            }
        }
        private static CultivationShape SourceKind(string id, string name) => Lock(Enum(id, name, "PERSONALITY", "性格", "SECT", "门派", "REALM_GOAL", "境界目标", "LIFESPAN", "寿元", "RESOURCE", "资源", "ENVIRONMENT", "环境"));
        private static CultivationShape ProfileRef(string id, string name, string kind) => Reference(id, name, c =>
            c.Snapshot.Tables[Id].Rows.Values.Where(r => Token(r, "profileId") == Token(c.Row, "profileId") && Token(r, "recordKind") == kind).Select(r => Token(r, "recordId")));
        public IEnumerable<ConfigTableDefinition> GetTables() { yield return Definition; }
        public override IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot)
        {
            if (!snapshot.Tables.TryGetValue(Id, out var table)) return Array.Empty<ConfigEdit>();
            var headers = Definition.ExactColumns.ToArray();
            var rows = table.Rows.Values.Select(EncodeRow).Select(r => headers.Select(c => r[c]).ToArray()).ToArray();
            string hash = CultivationContentImporter.ComputeNpcCultivationSourceHash(headers, rows, Array.IndexOf(headers, "sourceContentHash"));
            return table.Rows.Where(p => Token(p.Value, "recordKind") == "MANIFEST" && Token(p.Value, "sourceContentHash") != hash)
                .Select(p => new ConfigEdit(Id, p.Key, "sourceContentHash", ConfigValue.Scalar(hash))).ToArray();
        }
        protected override void ValidateDomain(ConfigValue row, string[] lines, ConfigDraftSnapshot snapshot) => throw new NotSupportedException();
        public override IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            var issues = new List<ConfigIssue>();
            if (!snapshot.Tables.TryGetValue(Id, out var table)) return issues;
            foreach (var pair in table.Rows)
            {
                ValidateFields(snapshot, pair.Key, pair.Value, issues);
                string kind = Token(pair.Value, "recordKind");
                if (!ColumnsByKind.TryGetValue(kind, out var used)) { issues.Add(Issue(pair.Key, "recordKind", "未知记录类型，禁止写盘。", "NPC_WEIGHT_UNKNOWN_RECORD_KIND")); continue; }
                foreach (var field in Definition.Fields.Skip(3).Where(f => f.Column != "recordKind" && f.Column != "recordId" && !used.Contains(f.Column)))
                    if (EncodeRow(pair.Value)[field.Column].Length != 0) issues.Add(Issue(pair.Key, field.Column, "本记录类型不使用该列，必须保持空串。", "NPC_WEIGHT_UNUSED_COLUMN"));
            }
            try { Destroy(CultivationContentImporter.ParseNpcCultivationActionWeightProfiles(Lines(Definition, table.Rows.Values), Id)); }
            catch (Exception e) when (e is InvalidDataException || e is ArgumentException || e is FormatException || e is OverflowException)
            { issues.Add(Issue("", "", "原领域校验失败：" + e.Message, e.Message.Split(':')[0])); }
            return issues;
        }
        public override IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> tables) => !tables.Contains(Id) ? Array.Empty<ConfigImportStep>() :
            new[] { new ConfigImportStep { Method = "CultivationContentImporter.ImportNpcCultivationActionWeightProfiles", InputPaths = new[] { Definition.CsvPath },
                ProductPaths = new[] { "Assets/Data/NpcCultivationActionWeightProfiles/NpcCultivationActionWeightProfile_npc-cultivation-production-v1.asset" } } };
    }
}
