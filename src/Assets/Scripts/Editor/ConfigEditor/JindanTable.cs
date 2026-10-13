using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Entity;
using static TianZhang.Editor.ConfigEditor.CultivationFields;

namespace TianZhang.Editor.ConfigEditor
{
    public sealed class JindanTable : CultivationTableCodec, IConfigTableProvider
    {
        public const string Id = "JindanStaticStates";
        private readonly Func<ConfigValue, JindanStaticReferenceCatalog> fixtureCatalog;
        public JindanTable() : this(null) { }
        // An explicit, isolated test caller supplies its authority. The production provider has none.
        public JindanTable(Func<ConfigValue, JindanStaticReferenceCatalog> isolatedFixtureCatalog)
        {
            fixtureCatalog = isolatedFixtureCatalog; AllowFixtures = fixtureCatalog != null;
            var core = List("jindanCoreBinding", "唯一金丹核心", '|', Group("core", "核心", '~', Text("jindanCoreBindingId", "核心绑定ID"),
                Text("jindanInstanceId", "金丹实例ID"), External("boundDanshuCoreId", "绑定丹枢核心"), External("formationTransactionId", "结丹事务引用"), Number("formationVersion", "形成版本")),
                "core~jindan~danshu~transaction~1");
            var danxiang = List("danxiang", "唯一丹相", '|', Group("danxiangRecord", "丹相", '~', Text("danxiangInstanceId", "丹相实例"),
                Text("jindanInstanceId", "金丹实例"), External("danxiangNameKey", "丹相名称键"), External("danxingDefinitionId", "丹性定义（可无）"),
                External("danxiangPresentationProfileId", "表现配置")), "danxiang~jindan~name~none~profile");
            var positions = List("stablePositionBindings", "稳定位（1至3项）", '|', Group("position", "稳定位", '~',
                External("positionId", "位ID"), Number("expectedPositionVersion", "期望版本"), External("roadId", "道途"),
                Enum("positionType", "位类型", "SOURCE", "源", "TRANSFORMATION", "化", "DOMAIN", "域"), External("proofProfileId", "证明配置"),
                External("equippedBaseEffectId", "基础效果"), External("compatibilityProfileId", "相容配置"), Carrier("primaryCarrierAbilityInstanceId", "主载体神通"),
                List("auxiliaryCarrierAbilityInstanceIds", "辅助载体（有序）", '+', Carrier("carrier", "辅助神通"), "")), "position~0~road~SOURCE~proof~effect~compat~~none");
            var ledgers = List("abilityLedgerBindings", "神通台账归属", '|', Group("ledger", "台账", '~', Carrier("abilityInstanceId", "神通实例"),
                External("resourceDebitLedgerRef", "资源扣费账本"), External("cooldownLedgerRef", "冷却账本"), External("chargeLedgerRef", "充能账本"),
                External("costLedgerRef", "代价账本"), External("conflictReserveLedgerRef", "冲突储备账本"), External("conflictCostProfileId", "冲突费用配置")), "~none~none~none~none~none~none");
            Initialize(Id, "金丹静态状态", "characterId", new[] { Text("schemaId", "模式ID", false), Text("schemaVersion", "模式版本", false), Text("characterId", "角色ID", false),
                Text("foundationPurpleMansionStateRef", "同角色已成丹道基实例", false), Lock(List("mansionInputs", "冻结五府输入", '|', FoundationTable.Mansion(true), "MING~NOT_BUILT"), "必须精确匹配已形成的道基，不能编辑冻结输入"),
                core, danxiang, positions, ledgers, Text("fixtureId", "测试ID", false), Lock(Enum("expect", "测试预期", "ACCEPT", "接受", "REJECT", "拒绝")), Text("fixtureOnlyNumericProfile", "测试数值引用", false) });
            // Frozen inputs are displayed structurally; they use their own root rather than Foundation's editable lists.
        }
        private static CultivationShape Carrier(string id, string name) => Reference(id, name, c =>
            c.Row.At("mansionInputs").Items.Where(m => m.At("state").RawToken == "COMPLETE").Select(m => m.At("guardianAbilityInstanceId").RawToken), true);
        public IEnumerable<ConfigTableDefinition> GetTables() { yield return Definition; }
        protected override void ValidateDomain(ConfigValue row, string[] lines, ConfigDraftSnapshot snapshot)
        {
            var foundation = Array.Empty<FoundationPurpleMansionStateData>();
            try
            {
                JindanStaticReferenceCatalog catalog;
                if (AllowFixtures && !IsNone(Token(row, "fixtureId"))) catalog = fixtureCatalog(row);
                else
                {
                    if (snapshot.Tables.TryGetValue(FoundationTable.Id, out var table))
                        foundation = CultivationContentImporter.ParseFoundationPurpleMansionStates(Lines(table.Definition, table.Rows.Values), FoundationTable.Id, false);
                    catalog = new JindanStaticReferenceCatalog { foundationPurpleMansionStates = foundation };
                }
                Destroy(CultivationContentImporter.ParseJindanStaticStates(lines, Id, catalog, AllowFixtures));
            }
            finally { Destroy(foundation); }
        }
        public override IReadOnlyList<ConfigImportStep> GetImportSteps(IReadOnlyCollection<string> tables) => !tables.Contains(Id) ? Array.Empty<ConfigImportStep>() :
            new[] { new ConfigImportStep { Method = "CultivationContentImporter.ImportJindanStaticStates", InputPaths = new[] { "Assets/DataConfig/FoundationPurpleMansionStates.csv", Definition.CsvPath } } };
    }
}
