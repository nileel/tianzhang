using System.Linq;
using UnityEditor;

namespace TianZhang.Editor
{
    public static class BuildSettingsRegistrar
    {
        [MenuItem("天章/场景/登记四个正式场景")]
        public static void Register()
        {
            string[] paths =
            {
                SceneBuildSupport.StartMenuScenePath,
                SceneBuildSupport.WorldScenePath,
                SceneBuildSupport.SettlementScenePath,
                SceneBuildSupport.AdventureScenePath,
            };
            EditorBuildSettings.scenes = paths.Select(path => new EditorBuildSettingsScene(path, true)).ToArray();
        }
    }
}
