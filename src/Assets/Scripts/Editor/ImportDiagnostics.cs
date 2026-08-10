using System;

namespace TianZhang.Editor
{
    /// <summary>Stable import diagnostic factory shared by editor-only import boundaries.</summary>
    public static class ImportDiagnostics
    {
        public static InvalidOperationException AtomicCommitRequired(string domain) =>
            new InvalidOperationException("Import domain requires an atomic commit: " + domain);
    }
}
