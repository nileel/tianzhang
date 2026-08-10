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
    }
}
