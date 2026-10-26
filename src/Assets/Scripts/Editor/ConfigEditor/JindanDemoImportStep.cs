using System.Linq;

namespace TianZhang.Editor.ConfigEditor
{
    internal static class JindanDemoImportStep
    {
        internal static ConfigImportStep Create() => new ConfigImportStep
        {
            Method = "JindanDemoContentImporter.Import",
            InputPaths = JindanDemoContentImporter.DefinitionPaths.Concat(new[]
            {
                "Assets/DataConfig/FoundationPurpleMansionStates.csv", "Assets/DataConfig/JindanStaticStates.csv",
                "Assets/DataConfig/Language.csv", "Assets/DataConfig/GongFa.csv", "Assets/DataConfig/Characters.csv",
            }).ToArray(),
            ProductPaths = JindanDemoContentImporter.IdentityProductPaths(true)
                .Concat(JindanDemoContentImporter.IdentityProductPaths(false))
                .Concat(new[] { JindanDemoContentImporter.CatalogPath }).ToArray(),
        };
    }
}
