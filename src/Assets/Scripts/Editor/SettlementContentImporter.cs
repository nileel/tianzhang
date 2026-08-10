using UnityEditor;

namespace TianZhang.Editor
{
    /// <summary>Settlement definition import boundary.</summary>
    public static class SettlementContentImporter
    {
        [MenuItem("天章/内容/导入据点定义")]
        public static void Import()
        {
            ContentImportCoordinator.ImportContentCatalog();
            ContentImportCoordinator.ImportCharterSites();
            ContentImportCoordinator.ImportCharacterCreationPointBuy();
        }
    }
}
