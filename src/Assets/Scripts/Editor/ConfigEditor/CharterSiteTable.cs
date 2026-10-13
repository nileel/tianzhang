using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Content;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class CharterSiteTable : IConfigTableProvider, IConfigRowCodec
    {
        public const string TableId = "CharterSites";
        public IEnumerable<ConfigTableDefinition> GetTables()
        {
            var fields = new List<ConfigFieldDefinition>
            {
                Locked("siteId", "基础 · 站点ID", "首版稳定行键不可修改"),
                Locked("displayNameKey", "基础 · 站点名称", "批准文本键：旧水驿"),
                Locked("settlementId", "基础 · 所属据点", "固定为关中城 guanzhong_city"),
                Locked("passageCapabilityId", "通行 · 通行能力", "批准的开阖九章通行能力，不等同管理权限"),
                Text("passageOperatorId", "通行 · 通行操作者"), Text("passageTargetId", "通行 · 通行目标"),
                FixedEnum("passageProtocolState", "通行 · 协议状态", "compatible", "兼容"),
                FixedEnum("passageStructureState", "通行 · 结构状态", "intact", "完好"),
                FixedEnum("passagePowerState", "通行 · 动力状态", "available", "可用"),
                Locked("interactionTimeProfileId", "时序 · 交互时间档案", "固定为旧水驿门禁交互时间档案"),
                FixedEnum("recognitionTiming", "时序 · 识别时序", "instant", "即时"),
                FixedEnum("operationTiming", "时序 · 操作时序", "sustained_guided", "持续引导"),
                FixedEnum("cancellationPolicy", "时序 · 取消策略", "no_commit_on_cancel", "取消不提交"),
                Text("facilityId", "界印 · 设施"), Reference("sealRelicId", "界印 · 界印法宝", c => c.relicIds),
                Text("sealManagerId", "界印 · 界印管理者"), Text("sealBeneficiaryId", "界印 · 受益者"),
                Reference("sealAuthorizationVersionId", "界印 · 组织授权版本", c => c.organizationAuthorizationVersionIds),
                Reference("ruleEntryId", "册界 · 规则条目", c => c.ruleEntryIds),
                Text("ruleEntryOccupancyId", "册界 · 条目占用"), Text("nodeOccupancyId", "册界 · 节点占用"),
                Text("jindanConflictEventId", "金丹 · 冲突事件"), Text("jindanChallengeEventId", "金丹 · 挑战事件"),
                Locked("grantId", "资格 · 跨阶资格ID", "稳定资格ID，须存在于所选冲突档案的跨阶资格目录"),
                Number("grantDefinitionVersion", "资格 · 资格版本", "版本"),
                Reference("grantTargetVariableId", "资格 · 目标变量", c => c.worldVariableIds),
                Text("grantChallengerId", "资格 · 挑战者"),
                CharterEditorFields.Enum("grantQualificationSource", "资格 · 来源类型", "JindanProtection", "金丹护持",
                    "YuanyingOrthodoxy", "元婴正统", "DedicatedGreatFormation", "专用大阵", "NarrativeRelic", "剧情法宝"),
                Text("grantAllowedOperationId", "资格 · 允许操作"), Reference("grantTargetId", "资格 · 目标节点", c => c.nodeIds),
                Text("grantScopeId", "资格 · 作用域"), Locked("grantBeneficiaryId", "资格 · 受益者", "由界印受益者联动"),
                Text("grantRealityAnchorId", "资格 · 现实锚"), Text("grantResourceLedgerRef", "资格 · 资源账本"),
                Text("grantCapacityLedgerRef", "资格 · 容量账本"), Number("grantChallengeRuleTier", "资格 · 挑战规则层级", "等级"),
                Number("grantEffectiveAtTick", "资格 · 生效刻", "游戏刻"), Number("grantExpiresAtTick", "资格 · 失效刻", "游戏刻"),
                Boolean("grantIsRevoked", "资格 · 已撤销"), Text("grantRevocationReason", "资格 · 撤销原因"),
                Text("grantDisplaySource", "资格 · 展示来源")
            };
            AddCandidate(fields, "left", "左候选");
            AddCandidate(fields, "right", "右候选");
            fields.AddRange(new[]
            {
                new ConfigFieldDefinition
                {
                    Column = "charterCandidateId", ChineseName = "后续事件 · 册界侧候选", Control = ConfigControl.Reference, Editable = true,
                    Choices = CandidateChoices, Help = "必须绑定左或右稳定候选；胜方由原领域判定，不由界面指定。"
                },
                Text("yuanyingConflictEventId", "后续事件 · 元婴冲突事件"),
                Reference("yuanyingTargetVariableId", "后续事件 · 元婴目标变量", c => c.worldVariableIds),
                Reference("yuanyingTargetId", "后续事件 · 元婴目标节点", c => c.nodeIds),
                Text("yuanyingScopeId", "后续事件 · 元婴作用域"), Text("yuanyingRealityAnchorId", "后续事件 · 元婴现实锚")
            });
            var version = fields.Single(f => f.Column == "grantDefinitionVersion");
            version.Editable = false; version.ReadOnlyReason = "正整数版本必须等于唯一静态目录版本，不能由编辑器升级";
            var reason = fields.Single(f => f.Column == "grantRevocationReason");
            reason.CanEdit = row => row.At("grantIsRevoked").RawToken == "true";
            reason.ReadOnlyReason = "未撤销时必须显式为 none；撤销后填写非 none 的原因引用";
            reason.Help = "none 表示无撤销原因，不等同空值；切换撤销状态不会自动补写原因。";
            yield return CharterEditorFields.Table(TableId, "册界站点", fields.ToArray(), this, "siteId", "displayNameKey");
        }

        private static ConfigFieldDefinition Text(string column, string label) => CharterEditorFields.Text(column, label);
        private static ConfigFieldDefinition Locked(string column, string label, string reason) => CharterEditorFields.Locked(column, label, reason);
        private static ConfigFieldDefinition Reference(string column, string label,
            Func<CharterRuleReferenceCatalog, IEnumerable<string>> source) => CharterEditorFields.Reference(column, label, source);
        private static ConfigFieldDefinition Number(string column, string label, string unit) => new ConfigFieldDefinition
            { Column = column, ChineseName = label, Unit = unit, Control = ConfigControl.Integer, Editable = true };
        private static ConfigFieldDefinition Boolean(string column, string label) =>
            CharterEditorFields.Enum(column, label, "true", "是", "false", "否");
        private static ConfigFieldDefinition FixedEnum(string column, string label, string id, string chinese)
        {
            var field = CharterEditorFields.Enum(column, label, id, chinese);
            field.Editable = false; field.ReadOnlyReason = "批准的固定契约值：" + chinese;
            return field;
        }

        private static void AddCandidate(ICollection<ConfigFieldDefinition> fields, string side, string label)
        {
            var prefix = side + "Candidate";
            fields.Add(Locked(prefix + "Id", label + " · 候选ID", "稳定候选身份不可修改，左右必须不同"));
            fields.Add(Locked(prefix + "TargetVariableId", label + " · 目标变量", "由资格目标变量联动"));
            fields.Add(Locked(prefix + "TargetId", label + " · 目标节点", "由资格目标节点联动"));
            fields.Add(Boolean(prefix + "HasVariableAuthority", label + " · 具有变量权限"));
            fields.Add(Boolean(prefix + "HasLegalTarget", label + " · 具有合法目标"));
            fields.Add(Number(prefix + "PositionRank", label + " · 位格", "等级"));
            fields.Add(Number(prefix + "RealityAnchorRank", label + " · 现实锚等级", "等级"));
            fields.Add(Number(prefix + "AlreadyPaidCost", label + " · 已付代价", "原始整数"));
            fields.Add(Boolean(prefix + "HasActiveContinuousCarrier", label + " · 具有持续载体"));
            fields.Add(Number(prefix + "ConflictReserve", label + " · 冲突储备", "原始整数"));
            fields.Add(Number(prefix + "PulseCost", label + " · 单脉冲代价", "原始整数"));
            fields.Add(Number(prefix + "SettlementCooldown", label + " · 结算冷却", "原始整数"));
        }

        private static IReadOnlyList<ConfigChoice> CandidateChoices()
        {
            // These identities are stable, site-owned CSV declarations, not global characters.
            var definition = new CharterSiteTable().GetTables().Single();
            var document = CsvConfigDocument.Load(File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath, "DataConfig/CharterSites.csv")));
            return document.GetRows(definition).SelectMany(row => new[]
                { new ConfigChoice(row["leftCandidateId"], "左候选"), new ConfigChoice(row["rightCandidateId"], "右候选") }).ToArray();
        }

        public ConfigValue DecodeRow(ConfigCsvRow row) => ConfigValue.Group(row.Cells.ToDictionary(v => v.Key, v => ConfigValue.Scalar(v.Value)));
        public IReadOnlyDictionary<string, string> EncodeRow(ConfigValue row) => row.Members.ToDictionary(v => v.Key, v => v.Value.RawToken);

        public IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot)
        {
            if (!snapshot.Tables.TryGetValue(TableId, out var table)) return Array.Empty<ConfigEdit>();
            var result = new List<ConfigEdit>();
            foreach (var row in table.Rows)
            {
                var baseline = DecodeRow(table.SourceRows[row.Key]);
                Mirror("sealBeneficiaryId", "grantBeneficiaryId");
                Mirror("grantTargetVariableId", "leftCandidateTargetVariableId");
                Mirror("grantTargetVariableId", "rightCandidateTargetVariableId");
                Mirror("grantTargetId", "leftCandidateTargetId");
                Mirror("grantTargetId", "rightCandidateTargetId");
                void Mirror(string source, string target)
                {
                    // Existing invalid input must remain invalid. Also restore a prior linked
                    // value when the user changes its source back to the saved baseline.
                    if (row.Value.At(source).Equals(baseline.At(source)) && row.Value.At(target).Equals(baseline.At(target))) return;
                    if (!row.Value.At(source).Equals(row.Value.At(target)))
                        result.Add(new ConfigEdit(TableId, row.Key, target, row.Value.At(source)));
                }
            }
            return result;
        }

        public IReadOnlyList<ConfigIssue> ValidateDraft(ConfigDraftSnapshot snapshot)
        {
            if (!snapshot.Tables.TryGetValue(TableId, out var table)) return Array.Empty<ConfigIssue>();
            return CharterEditorFields.Validate(table, () =>
            {
                if (!snapshot.Tables.TryGetValue(CharterRuleTable.TableId, out var rules) || !rules.Definition.IsAdapted || rules.Issues.Count > 0 ||
                    !snapshot.Tables.TryGetValue("Language", out var language))
                    throw new InvalidDataException("CHARTER_EDITOR_DEPENDENCY_MISSING: 校验需要完整册界规则与显示文本草稿快照。");
                var catalog = CharterEditorFields.Catalog();
                var definitions = WorldContentImporter.ParseCharterRuleDefinitions(CharterEditorFields.Lines(rules), CharterRuleTable.TableId, catalog.ReferenceCatalog);
                CharterSiteData[] sites = null;
                try
                {
                    // Read the complete candidate document, including future Language adapter edits.
                    var languageBytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", CharterEditorFields.Lines(language)));
                    var languageTable = new ConfigTableDefinition
                        { TableId = "Language", HeaderMode = ConfigHeaderMode.None, ExactColumns = new[] { "id", "zh_CN" } };
                    var texts = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var entry in CsvConfigDocument.Load(languageBytes).GetRows(languageTable))
                        if (!texts.TryAdd(entry["id"].Trim(), entry["zh_CN"].Trim()))
                            throw new InvalidDataException("CHARTER_EDITOR_LANGUAGE_INVALID: 显示文本键重复。");
                    if (!texts.TryGetValue("charter_site_old_water_station", out var text) || text != "旧水驿")
                        throw new InvalidDataException("CHARTER_EDITOR_SITE_TEXT_INVALID: 站点批准文本必须为旧水驿。");
                    sites = SettlementContentImporter.ParseCharterSites(CharterEditorFields.Lines(table), TableId, texts, catalog.ReferenceCatalog, definitions);
                    if (sites.Any(site => site.jindanGrant.definitionVersion != catalog.DefinitionCatalogVersion))
                        throw new InvalidDataException("CHARTER_SITE_GRANT_INVALID: 资格版本必须等于静态目录版本。");
                }
                finally
                {
                    foreach (var definition in definitions) UnityEngine.Object.DestroyImmediate(definition);
                    if (sites != null) foreach (var site in sites) UnityEngine.Object.DestroyImmediate(site);
                }
            });
        }

        public IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> changedTables) =>
            changedTables.Contains(TableId) ? new[] { SiteImportStep() } : Array.Empty<ConfigImportStep>();

        internal static ConfigImportStep SiteImportStep() => new ConfigImportStep
        {
            Method = "SettlementContentImporter.ImportCharterSites",
            InputPaths = new[] { "Assets/DataConfig/CharterSites.csv", "Assets/DataConfig/Language.csv", CharterEditorFields.CatalogPath }
                .Concat(CharterEditorFields.Catalog().Definitions.Select(v => "Assets/Data/CharterRuleDefinitions/CharterRuleDefinition_" + v.ruleEntryId + ".asset")).ToArray(),
            ProductPaths = new[] { "Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset", "Assets/Data/ContentCatalog/ContentCatalog.asset" }
        };
    }
}
