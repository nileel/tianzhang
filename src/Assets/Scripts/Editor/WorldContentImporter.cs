using UnityEditor;

namespace TianZhang.Editor
{
    /// <summary>World definition import boundary, including the environment asset adapter projection.</summary>
    public static class WorldContentImporter
    {
        [MenuItem("天章/内容/导入世界定义")]
        public static void Import()
        {
            ContentImportCoordinator.ImportEnvironmentProfiles();
            ContentImportCoordinator.ImportCharterRuleDefinitions();
        }
    }
}
