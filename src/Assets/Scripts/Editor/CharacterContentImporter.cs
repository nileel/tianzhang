using UnityEditor;

namespace TianZhang.Editor
{
    /// <summary>Character definition import boundary: one CSV domain is validated before committing its assets.</summary>
    public static class CharacterContentImporter
    {
        [MenuItem("天章/内容/导入角色定义")]
        public static void Import() => ContentImportCoordinator.ImportCharacterDefinitions();
    }
}
