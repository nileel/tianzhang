using UnityEditor;
using UnityEngine;

namespace TianZhang.Editor
{
    /// <summary>Coordinates the deterministic full import order; domain importers own all pipelines.</summary>
    public static class ContentImportCoordinator
    {
        [MenuItem("天章/导入全部配置")]
        public static void ImportAll()
        {
            CultivationContentImporter.ImportNpcCultivationActionWeightProfiles();
            CultivationContentImporter.ImportFoundationPurpleMansionStates();
            CultivationContentImporter.ImportJindanStaticStates();
            WorldContentImporter.ImportCharterRuleDefinitions();
            SettlementContentImporter.ImportCharterSites();
            CultivationContentImporter.ImportGongFa();
            CombatContentImporter.ImportSpells();
            CombatContentImporter.ImportSkills();
            CharacterContentImporter.ImportCharacterDefinitions();
            SettlementContentImporter.ImportContentCatalog();
            SettlementContentImporter.ImportCharacterCreationPointBuy();
            WorldContentImporter.ImportEnvironmentProfiles();
            AdventureContentImporter.Import();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ContentImportCoordinator] 全部配置导入完成");
        }
    }
}
