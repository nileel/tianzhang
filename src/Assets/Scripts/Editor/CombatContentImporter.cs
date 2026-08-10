using UnityEditor;

namespace TianZhang.Editor
{
    /// <summary>Combat definition import boundary for attack profiles, spells and divine skills.</summary>
    public static class CombatContentImporter
    {
        [MenuItem("天章/内容/导入战斗定义")]
        public static void Import()
        {
            ContentImportCoordinator.ImportAttackProfiles();
            ContentImportCoordinator.ImportSpells();
            ContentImportCoordinator.ImportSkills();
        }
    }
}
