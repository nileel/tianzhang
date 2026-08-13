using System.IO;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Editor
{
    /// <summary>Single editor asset commit point. Call only after a domain projection validates.</summary>
    public static class AssetCommitter
    {
        public static void Commit(UnityEngine.Object asset, string path)
        {
            if (asset == null || string.IsNullOrWhiteSpace(path))
                throw ImportDiagnostics.AtomicCommitRequired(path ?? "unknown");
            AssetDatabase.CreateAsset(asset, path);
            EditorUtility.SetDirty(asset);
        }

        public static string SanitizeName(string name) =>
            name.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");

        public static void EnsureDirectory(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }
    }
}
