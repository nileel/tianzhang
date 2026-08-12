using System.Collections.Generic;

namespace TianZhang.World
{
    /// <summary>Immutable charter definition and its legal atomic operations.</summary>
    public sealed class CharterDefinition
    {
        public CharterDefinition(string definitionId, string authorizationId, string conflictKey, IEnumerable<string> atomicOperationIds)
        {
            DefinitionId = definitionId ?? string.Empty; AuthorizationId = authorizationId ?? string.Empty; ConflictKey = conflictKey ?? string.Empty;
            AtomicOperationIds = atomicOperationIds == null ? new string[0] : new List<string>(atomicOperationIds).ToArray();
        }
        public string DefinitionId { get; } public string AuthorizationId { get; } public string ConflictKey { get; } public string[] AtomicOperationIds { get; }
        public bool ContainsAtomicOperation(string operationId) { foreach (string known in AtomicOperationIds) if (known == operationId) return true; return false; }
    }

    public sealed class CharterStateEntry
    {
        public CharterStateEntry(string definitionId, string operationId, string conflictKey)
        {
            DefinitionId = definitionId ?? string.Empty;
            OperationId = operationId ?? string.Empty;
            ConflictKey = conflictKey ?? string.Empty;
        }

        public string DefinitionId { get; }
        public string OperationId { get; }
        public string ConflictKey { get; }
    }

    public sealed class CharterStateSnapshot
    {
        public CharterStateSnapshot(CharterStateEntry[] entries)
            : this(entries, 0, null)
        {
        }

        public CharterStateSnapshot(
            CharterStateEntry[] entries,
            int definitionCatalogVersion,
            CharterRuntimeStateData runtimeState)
        {
            Entries = entries ?? new CharterStateEntry[0];
            DefinitionCatalogVersion = definitionCatalogVersion;
            RuntimeState = runtimeState == null ? null : runtimeState.CreateCopy();
        }

        public CharterStateEntry[] Entries { get; }
        public int DefinitionCatalogVersion { get; }
        public CharterRuntimeStateData RuntimeState { get; }
    }
}
