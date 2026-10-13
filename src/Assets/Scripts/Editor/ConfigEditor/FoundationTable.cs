using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TianZhang.Entity;
using static TianZhang.Editor.ConfigEditor.CultivationFields;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class FoundationTable : CultivationTableCodec, IConfigTableProvider
    {
        public const string Id = "FoundationPurpleMansionStates";
        public FoundationTable() : this(false) { }
        public FoundationTable(bool allowFixtures)
        {
            AllowFixtures = allowFixtures;
            var expansion = List("expansionGrants", "拓容授予", '|', Group("grant", "授予", '~',
                Text("grantId", "授予ID"), External("sourceItemId", "来源物品"),
                Local("capacityEffectBindingId", "容量效果", "effectBindings", "effectBindingId")), "new_grant~new_item~none");
            var effects = List("effectBindings", "效果绑定", '|', Group("effect", "效果", '~',
                Text("effectBindingId", "效果ID"), Enum("carrierKind", "载体类型", "FOUNDATION", "道基", "MANSION_BODY", "府体",
                    "GUARDIAN_ABILITY", "镇府神通", "ENHANCEMENT_NODE", "强化节点", "EXPANSION_GRANT", "拓容授予", "CULTIVATION_ACTION", "修炼行动"),
                Reference("carrierId", "载体", CarrierIds), Number("order", "执行序号"), External("trigger", "触发"),
                Conditions("conditions", "条件"), External("target", "目标"), External("atomicEffectType", "原子效果类型"),
                List("parameters", "参数", '+', Group("parameter", "参数项", ':', Text("key", "参数键"), External("value", "引用值")), "key:value")),
                "new_effect~FOUNDATION~~1~trigger~none~target~atomic~none");
            var guardians = List("guardianAbilities", "镇府神通", '|', Group("guardian", "神通", '~',
                Text("abilityInstanceId", "神通实例"), External("abilityDefinitionId", "神通定义"), Local("mansionInstanceId", "府实例", "mansionStates", "mansionInstanceId"),
                External("sourceSpellId", "来源术法"), External("upgradePlanId", "升格方案"), Disposition(),
                Enum("form", "形式", "ACTIVE", "主动", "PASSIVE", "被动", "TRIGGERED", "触发"),
                LocalList("effectBindingIds", "效果引用", "effectBindings", "effectBindingId")), "new_guardian~definition~~spell~upgrade~RETAIN~PASSIVE~none");
            var nodes = List("enhancementNodes", "强化节点", '|', Group("node", "节点", '~', Text("nodeId", "节点ID"),
                Local("abilityInstanceId", "神通实例", "guardianAbilities", "abilityInstanceId"),
                Enum("nodeKind", "节点类型", "BEHAVIOR", "行为", "CULTIVATION", "修炼", "RESOURCE", "资源", "INTER_MANSION", "跨府", "SPECIAL", "特殊"),
                Conditions("requirements", "要求"), LocalList("effectBindingIds", "效果引用", "effectBindings", "effectBindingId")), "new_node~~BEHAVIOR~condition~none");
            var action = List("cultivationActionState", "当前修炼行动（至多一项）", '|', Group("action", "行动", '~',
                Text("actionStateId", "行动ID"), Enum("actionKind", "行动类型", "FOUNDATION_TRIAL", "道基试炼", "FOUNDATION_NURTURE", "道基温养",
                    "MANSION_EMBRYO_NURTURE", "府胚温养", "MANSION_OPENING_TRIAL", "开府试炼"),
                Enum("status", "行动状态", "READY", "就绪", "ACTIVE", "进行中", "PAUSED", "暂停", "COMPLETED", "完成", "FAILED", "失败", "TERMINATED", "终止"),
                External("targetRef", "目标"), External("fixedCycleDefinitionId", "周期定义"), External("lastStableBoundaryId", "稳定边界"),
                List("committedCycleIds", "已提交周期", '+', External("cycle", "周期引用"), "cycle"), External("progressChannelId", "进度通道"),
                List("numericProfileRefs", "数值配置引用", '+', External("profile", "数值配置"), "profile")),
                "new_action~FOUNDATION_TRIAL~READY~target~cycle~boundary~none~channel~profile");
            var retreat = List("closedRetreatPlan", "闭关计划（至多一项）", '|', Group("retreat", "闭关", '~',
                Local("actionStateId", "行动", "cultivationActionState", "actionStateId"), External("targetRef", "目标"),
                List("stopConditions", "停止条件", '+', Enum("condition", "条件", "WAITING_RESPONSE", "等待响应", "INSUFFICIENT_NEXT_CYCLE_RESOURCES", "下周期资源不足",
                    "TARGET_COMPLETED", "目标完成", "ACTION_FAILED", "行动失败", "INJURY_UNRESOLVED", "伤势未解", "ACTION_INVALIDATED", "行动失效",
                    "PLAYER_GUARD", "玩家保护", "CHAPTER_OR_UNLOCK_BOUNDARY", "篇章或解锁边界", "MANUAL_PAUSE", "手动暂停"), "MANUAL_PAUSE")), "~target~MANUAL_PAUSE");
            Initialize(Id, "道基与紫府状态", "characterId", new[] {
                Text("schemaId", "模式ID", false), Text("schemaVersion", "模式版本", false), Text("characterId", "角色ID", false), Text("foundationInstanceId", "道基实例ID", false),
                External("foundationDefinitionId", "道基定义"), External("sourceGongFaId", "来源功法"), Stage(), Lock(Number("stageCode", "阶段码")),
                Number("continuousProgress", "连续进度", false, "进度量"), External("phaseBoundarySetId", "阶段边界配置"), Number("naturalMansionCapacity", "天生府容量", true, "府"),
                expansion, Lock(Number("expandedMansionCapacity", "拓容府数", true, "府")), External("carryingCapacityProfileId", "承载配置"),
                Lock(Number("currentMansionCarryingCapacity", "当前承载府数", true, "府"), "由承载权威决定，不可手填"), Number("completionMansionCapacity", "圆满确认府数", true, "府"),
                List("mansionStates", "五府状态", '|', Mansion(), "MING~NOT_BUILT"), effects, guardians, nodes, action, retreat, FormationLock(),
                Text("fixtureId", "样例ID", false), Lock(Enum("expect", "测试预期", "ACCEPT", "接受", "REJECT", "拒绝")),
                Lock(List("fixtureOnlyNumericProfile", "测试数值权威替身（至多一项）", '|', Group("profile", "测试配置", '~',
                    External("phaseBoundarySetId", "阶段边界配置"), External("carryingCapacityProfileId", "承载配置"), Number("capacity", "当前承载府数")), "phase~carrying~0")) });
            var completion = Definition.Fields.Single(f => f.Column == "completionMansionCapacity");
            completion.CanEdit = row => Token(row, "stageId") == "COMPLETE";
            completion.ReadOnlyReason = "非圆满阶段必须保持0";
        }
        public IEnumerable<ConfigTableDefinition> GetTables() { yield return Definition; }
        internal static CultivationShape Stage(string id = "stageId") => Enum(id, "筑基阶段", "FOUNDATION", "筑基", "MANSION", "开府", "COMPLETE", "圆满");
        internal static CultivationShape Kind() => Enum("kind", "府种", "MING", "命府", "HUN", "魂府", "SHI", "识府", "WU", "物府", "YUN", "运府");
        internal static CultivationShape Disposition() => Enum("sourceSpellDisposition", "原术处置", "RETAIN", "保留", "REPLACE", "替换", "INTERNALIZE", "内化");
        internal static CultivationShape Local(string id, string name, string list, string key) => Reference(id, name, c => RowIds(c, list, key), true);
        internal static CultivationShape LocalList(string id, string name, string list, string key) => List(id, name, '+', Local("reference", "引用", list, key), "");
        internal static CultivationShape Mansion(bool frozen = false)
        {
            var state = frozen ? Enum("state", "府状态", "NOT_BUILT", "未建", "COMPLETE", "建成") :
                Enum("state", "府状态", "NOT_BUILT", "未建", "EMBRYO", "府胚", "COMPLETE", "建成");
            var fields = new[] { Kind(), state, Text("mansionInstanceId", "府实例"), Local("mansionBodyEffectBindingId", "府体效果", "effectBindings", "effectBindingId"),
                Local("guardianAbilityInstanceId", "镇府神通", "guardianAbilities", "abilityInstanceId"), External("sourceSpellId", "来源术法"), External("upgradePlanId", "升格方案"), Disposition(),
                Text("embryoId", "府胚ID"), Number("continuousProgress", "连续进度", false), External("progressChannelId", "进度通道"),
                Local("relatedActionStateId", "关联行动", "cultivationActionState", "actionStateId") };
            if (frozen)
                for (var i = 0; i < fields.Length; i++)
                    if (fields[i].Field.Control == ConfigControl.Reference) fields[i] = External(fields[i].Field.Column, fields[i].Field.ChineseName);
            foreach (var f in fields.Skip(2))
            {
                f.Field.CanEditDraft = c => MansionLayout(new[] { "", c.Parent.At("state").RawToken }).Contains(f.Field.Column) || c.Parent.At(f.Field.Column).HasValue;
                f.Field.ReadOnlyReason = "此府状态不使用该字段；切换前请清空旧分支字段";
            }
            return Record("mansion", "府", '~', fields, MansionLayout);
        }
        private static string[] MansionLayout(string[] tokens)
        {
            if (tokens.Length < 2) throw new InvalidDataException("府状态缺失。");
            switch (tokens[1])
            {
                case "NOT_BUILT": return new[] { "kind", "state" };
                case "EMBRYO": return new[] { "kind", "state", "embryoId", "sourceSpellId", "upgradePlanId", "continuousProgress", "progressChannelId", "relatedActionStateId" };
                case "COMPLETE": return new[] { "kind", "state", "mansionInstanceId", "mansionBodyEffectBindingId", "guardianAbilityInstanceId", "sourceSpellId", "upgradePlanId", "sourceSpellDisposition" };
                default: throw new InvalidDataException("未知府状态，拒绝猜测格式。");
            }
        }
        private static CultivationShape Conditions(string id, string name)
        {
            var shape = Group("condition", "条件", ':', Enum("type", "条件类型", "reference", "外部条件", "completeMansion", "指定府建成"),
                External("reference", "条件引用"), Kind());
            shape.Read = raw => ConfigValue.Group(new Dictionary<string, ConfigValue> {
                ["type"] = ConfigValue.Scalar(raw.StartsWith("completeMansion:", StringComparison.Ordinal) ? "completeMansion" : "reference"),
                ["reference"] = ConfigValue.Scalar(raw.StartsWith("completeMansion:", StringComparison.Ordinal) ? "" : raw),
                ["kind"] = ConfigValue.Scalar(raw.StartsWith("completeMansion:", StringComparison.Ordinal) ? raw.Substring(16) : "") }, raw);
            shape.Write = v =>
            {
                bool mansion = v.At("type").RawToken == "completeMansion";
                if (v.At(mansion ? "reference" : "kind").HasValue) throw new InvalidDataException("切换条件类型前请清空旧分支值，不能丢弃未理解内容。");
                return mansion ? "completeMansion:" + v.At("kind").RawToken : v.At("reference").RawToken;
            };
            return List(id, name, '+', shape, "condition");
        }
        private static CultivationShape FormationLock()
        {
            var snap = Record("mansion", "府冻结快照", ':', new[] { Kind(), Enum("state", "状态", "NOT_BUILT", "未建", "COMPLETE", "建成"),
                External("bodyEffectBindingId", "府体效果"), External("guardianAbilityInstanceId", "镇府神通") },
                t => t.Length >= 2 && t[1] == "NOT_BUILT" ? new[] { "kind", "state" } : new[] { "kind", "state", "bodyEffectBindingId", "guardianAbilityInstanceId" });
            var children = new[] { Enum("status", "结丹状态", "PRE_JINDAN", "未结丹", "FORMED", "已成丹"), Text("foundationInstanceId", "道基实例"), Stage(),
                Number("stageCode", "冻结阶段码"), Number("naturalMansionCapacity", "天生府容量"), Number("completionMansionCapacity", "圆满府容量"),
                List("expansionGrantIds", "拓容授予快照", '+', External("grant", "授予"), "grant"), List("mansionSnapshots", "五府冻结快照", '+', snap, "MING:NOT_BUILT") };
            return Lock(Record("jindanLock", "结丹锁及冻结快照", '~', children, t => t[0] == "PRE_JINDAN" ? new[] { "status" } : children.Select(c => c.Field.Column).ToArray()), "结丹锁与形成快照属于冻结事实，不由配置编辑器推进状态");
        }
        private static IEnumerable<string> CarrierIds(ConfigFieldContext c)
        {
            switch (c.Parent.At("carrierKind").RawToken)
            {
                case "FOUNDATION": return new[] { Token(c.Row, "foundationInstanceId") };
                case "MANSION_BODY": return RowIds(c, "mansionStates", "mansionInstanceId");
                case "GUARDIAN_ABILITY": return RowIds(c, "guardianAbilities", "abilityInstanceId");
                case "ENHANCEMENT_NODE": return RowIds(c, "enhancementNodes", "nodeId");
                case "EXPANSION_GRANT": return RowIds(c, "expansionGrants", "grantId");
                case "CULTIVATION_ACTION": return RowIds(c, "cultivationActionState", "actionStateId");
                default: return Array.Empty<string>();
            }
        }
        protected override void ValidateDomain(ConfigValue row, string[] lines, ConfigDraftSnapshot snapshot) =>
            Destroy(CultivationContentImporter.ParseFoundationPurpleMansionStates(lines, Id, AllowFixtures));
        public override IReadOnlyList<ConfigEdit> GetLinkedEdits(ConfigDraftSnapshot snapshot)
        {
            var edits = new List<ConfigEdit>();
            if (!snapshot.Tables.TryGetValue(Id, out var table)) return edits;
            foreach (var pair in table.Rows)
            {
                if (pair.Value.At("jindanLock/status").RawToken == "FORMED") continue;
                string code = Token(pair.Value, "stageId") == "FOUNDATION" ? "10" : Token(pair.Value, "stageId") == "MANSION" ? "20" : Token(pair.Value, "stageId") == "COMPLETE" ? "30" : "";
                if (code.Length > 0 && Token(pair.Value, "stageCode") != code) edits.Add(new ConfigEdit(Id, pair.Key, "stageCode", ConfigValue.Scalar(code)));
                string count = pair.Value.At("expansionGrants").Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (Token(pair.Value, "expandedMansionCapacity") != count) edits.Add(new ConfigEdit(Id, pair.Key, "expandedMansionCapacity", ConfigValue.Scalar(count)));
            }
            return edits;
        }
        public override IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> tables) => !tables.Contains(Id) ? Array.Empty<ConfigImportStep>() :
            new[] { new ConfigImportStep { Method = "CultivationContentImporter.ImportFoundationPurpleMansionStates", InputPaths = new[] { Definition.CsvPath } },
                new ConfigImportStep { Method = "CultivationContentImporter.ImportJindanStaticStates", InputPaths = new[] { Definition.CsvPath, "Assets/DataConfig/JindanStaticStates.csv" } } };
    }
}
