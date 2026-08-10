using UnityEditor;

namespace TianZhang.Editor
{
    /// <summary>Cultivation definition import boundary.</summary>
    public static class CultivationContentImporter
    {
        [MenuItem("天章/内容/导入修炼定义")]
        public static void Import()
        {
            ContentImportCoordinator.ImportGongFa();
            ContentImportCoordinator.ImportFoundationPurpleMansionStates();
            ContentImportCoordinator.ImportJindanStaticStates();
            ContentImportCoordinator.ImportNpcCultivationActionWeightProfiles();
        }
    }
}
